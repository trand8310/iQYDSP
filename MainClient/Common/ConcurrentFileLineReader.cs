using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace MainClient.Common;


public sealed class MultiFileLineReader : IDisposable
{
    private readonly string[] _filePaths;
    private readonly Options _options;
    private readonly Channel<FileLine> _channel;
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    private readonly ConcurrentDictionary<string, FileCheckpoint> _checkpoints =
        new ConcurrentDictionary<string, FileCheckpoint>(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _checkpointSaveLock = new SemaphoreSlim(1, 1);

    private readonly System.Threading.Timer? _checkpointTimer;
    private readonly Task _producerTask;

    private int _stopped;
    private int _disposed;

    public event Action<string>? Log;
    public event Action<FileLine>? LineRead;
    public event Action<string>? FileCompleted;
    public event Action<Exception>? Error;
    public event Action? Stopped;
    public event Action? Completed;

    public sealed class Options
    {
        /// <summary>
        /// 统一队列容量。
        /// 生产速度大于消费速度时，超过容量会等待，防止内存爆。
        /// </summary>
        public int QueueCapacity { get; set; } = 30000;

        /// <summary>
        /// 文件读取缓冲区。
        /// </summary>
        public int FileBufferSize { get; set; } = 1024 * 1024;

        /// <summary>
        /// 文本编码。
        /// </summary>
        public Encoding Encoding { get; set; } = Encoding.UTF8;

        /// <summary>
        /// 是否跳过空行。
        /// </summary>
        public bool SkipEmptyLines { get; set; } = true;

        /// <summary>
        /// 是否启用断点续读。
        /// </summary>
        public bool EnableCheckpoint { get; set; } = true;

        /// <summary>
        /// checkpoint 文件路径。
        /// </summary>
        public string CheckpointFilePath { get; set; } = "multi-file-reader-checkpoint.json";

        /// <summary>
        /// checkpoint 定时保存间隔。
        /// </summary>
        public TimeSpan CheckpointSaveInterval { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// offset 如果落在一行中间，启动时自动跳过残缺行。
        /// </summary>
        public bool AutoFixBrokenOffset { get; set; } = true;

        /// <summary>
        /// Stop / Dispose 时是否强制保存一次 checkpoint。
        /// </summary>
        public bool FlushCheckpointOnStop { get; set; } = true;

        /// <summary>
        /// 单行最大字节数，防止异常文件没有换行导致内存无限涨。
        /// </summary>
        public int MaxLineBytes { get; set; } = 1024 * 1024;
    }

    public sealed class FileLine
    {
        public FileLine(
            string filePath,
            long lineNumber,
            long offset,
            long nextOffset,
            string content)
        {
            FilePath = filePath;
            FileName = Path.GetFileName(filePath);
            LineNumber = lineNumber;
            Offset = offset;
            NextOffset = nextOffset;
            Content = content;
        }

        public string FilePath { get; }

        public string FileName { get; }

        public long LineNumber { get; }

        /// <summary>
        /// 当前行开始 byte offset。
        /// </summary>
        public long Offset { get; }

        /// <summary>
        /// 下一行开始 byte offset。
        /// </summary>
        public long NextOffset { get; }

        public string Content { get; }
    }

    private sealed class FileCheckpoint
    {
        public string FilePath { get; set; } = "";

        public long Offset { get; set; }

        public long LineNumber { get; set; }

        public long FileLength { get; set; }

        public DateTime LastUpdateTime { get; set; }
    }

    private sealed class RawLine
    {
        public long Offset { get; set; }

        public long NextOffset { get; set; }

        public string Content { get; set; } = "";
    }

    public MultiFileLineReader(IEnumerable<string> filePaths, Options? options = null)
    {
        if (filePaths == null)
            throw new ArgumentNullException(nameof(filePaths));

        _filePaths = filePaths
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (_filePaths.Length == 0)
            throw new ArgumentException("文件列表不能为空。", nameof(filePaths));

        _options = options ?? new Options();

        if (_options.QueueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(_options.QueueCapacity));

        if (_options.FileBufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(_options.FileBufferSize));

        if (_options.MaxLineBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(_options.MaxLineBytes));

        var channelOptions = new BoundedChannelOptions(_options.QueueCapacity)
        {
            SingleWriter = false,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait
        };

        _channel = Channel.CreateBounded<FileLine>(channelOptions);

        if (_options.EnableCheckpoint)
        {
            LoadCheckpoint();

            _checkpointTimer = new System.Threading.Timer(
                async _ => await SaveCheckpointSafeAsync().ConfigureAwait(false),
                null,
                _options.CheckpointSaveInterval,
                _options.CheckpointSaveInterval);
        }

        // new 对象后自动启动读取
        _producerTask = Task.Run(ProducerMainAsync);
    }

    public int FileCount => _filePaths.Length;

    public bool IsStopped => _stopped == 1;

    /// <summary>
    /// 从统一队列读取一行。
    /// 多个消费者可以同时调用。
    /// 没有数据并且所有文件读完时，返回 null。
    /// </summary>
    public async ValueTask<FileLine?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_channel.Reader.TryRead(out var item))
            {
                // 只更新内存，不每行写磁盘。
                // 后台 Timer 定时落盘。
                UpdateCheckpointInMemory(item);

                LineRead?.Invoke(item);

                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// 尝试立即读取一行，不等待。
    /// </summary>
    public bool TryReadLine(out FileLine? line)
    {
        ThrowIfDisposed();

        if (_channel.Reader.TryRead(out var item))
        {
            UpdateCheckpointInMemory(item);

            LineRead?.Invoke(item);

            line = item;
            return true;
        }

        line = null;
        return false;
    }

    /// <summary>
    /// 停止读取。
    /// 已经进入队列的数据仍可继续 ReadLineAsync 读出。
    /// </summary>
    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
            return;

        try
        {
            _cts.Cancel();
        }
        catch
        {
        }

        try
        {
            await _producerTask.ConfigureAwait(false);
        }
        catch
        {
        }

        if (_options.FlushCheckpointOnStop)
        {
            await FlushCheckpointAsync().ConfigureAwait(false);
        }

        Stopped?.Invoke();
        Log?.Invoke("MultiFileLineReader stopped.");
    }

    /// <summary>
    /// 等待所有文件读取完成。
    /// </summary>
    public async Task WaitForCompletionAsync()
    {
        await _producerTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 手动保存 checkpoint。
    /// 程序退出前可以调用一次。
    /// </summary>
    public async Task FlushCheckpointAsync()
    {
        if (!_options.EnableCheckpoint)
            return;

        await SaveCheckpointSafeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 清空指定文件的断点。
    /// 注意：如果当前对象已经开始读取，建议下次创建对象前使用。
    /// </summary>
    public void ResetCheckpoint(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        _checkpoints.TryRemove(filePath, out _);
    }

    /// <summary>
    /// 清空所有断点。
    /// 注意：如果当前对象已经开始读取，建议下次创建对象前使用。
    /// </summary>
    public void ResetAllCheckpoints()
    {
        _checkpoints.Clear();
    }

    private async Task ProducerMainAsync()
    {
        try
        {
            var tasks = new List<Task>();

            foreach (var filePath in _filePaths)
            {
                var path = filePath;

                tasks.Add(Task.Run(async () =>
                {
                    await ReadFileToChannelAsync(path, _cts.Token).ConfigureAwait(false);
                }));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            _channel.Writer.TryComplete();

            Completed?.Invoke();
            Log?.Invoke("All files completed.");
        }
        catch (OperationCanceledException)
        {
            _channel.Writer.TryComplete();

            Log?.Invoke("Reading cancelled.");
        }
        catch (Exception ex)
        {
            _channel.Writer.TryComplete(ex);

            Error?.Invoke(ex);
        }
    }

    private async Task ReadFileToChannelAsync(string filePath, CancellationToken token)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Log?.Invoke($"File not found: {filePath}");
                return;
            }

            long startOffset = 0;
            long lineNumber = 0;

            if (_options.EnableCheckpoint &&
                _checkpoints.TryGetValue(filePath, out var checkpoint))
            {
                startOffset = Math.Max(0, checkpoint.Offset);
                lineNumber = Math.Max(0, checkpoint.LineNumber);
            }

            using var fs = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: _options.FileBufferSize,
                options: FileOptions.SequentialScan | FileOptions.Asynchronous);

            if (startOffset > fs.Length)
            {
                // 文件被清空或者重写过
                startOffset = 0;
                lineNumber = 0;
            }

            if (startOffset > 0)
            {
                fs.Seek(startOffset, SeekOrigin.Begin);

                if (_options.AutoFixBrokenOffset)
                {
                    FixOffsetIfInMiddleOfLine(fs);
                }
            }

            await foreach (var rawLine in ReadRawLinesAsync(fs, _options.Encoding, token))
            {
                token.ThrowIfCancellationRequested();

                lineNumber++;

                if (_options.SkipEmptyLines && string.IsNullOrWhiteSpace(rawLine.Content))
                    continue;

                var item = new FileLine(
                    filePath,
                    lineNumber,
                    rawLine.Offset,
                    rawLine.NextOffset,
                    rawLine.Content);

                await _channel.Writer.WriteAsync(item, token).ConfigureAwait(false);
            }

            FileCompleted?.Invoke(filePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex);
        }
    }

    private async IAsyncEnumerable<RawLine> ReadRawLinesAsync(
        FileStream fs,
        Encoding encoding,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        var buffer = new byte[_options.FileBufferSize];
        var lineBuffer = new MemoryStream(1024);

        long currentLineStartOffset = fs.Position;
        long currentByteOffset = fs.Position;

        while (true)
        {
            token.ThrowIfCancellationRequested();

            long bufferStartOffset = fs.Position;

            int read = await fs.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);

            if (read <= 0)
                break;

            for (int i = 0; i < read; i++)
            {
                byte b = buffer[i];
                long byteOffset = bufferStartOffset + i;
                currentByteOffset = byteOffset + 1;

                if (b == (byte)'\n')
                {
                    var line = DecodeLine(lineBuffer, encoding);

                    yield return new RawLine
                    {
                        Offset = currentLineStartOffset,
                        NextOffset = currentByteOffset,
                        Content = line
                    };

                    lineBuffer.SetLength(0);
                    currentLineStartOffset = currentByteOffset;
                    continue;
                }

                lineBuffer.WriteByte(b);

                if (lineBuffer.Length > _options.MaxLineBytes)
                {
                    throw new InvalidOperationException(
                        $"单行超过最大限制 MaxLineBytes={_options.MaxLineBytes}。Offset={currentLineStartOffset}");
                }
            }
        }

        if (lineBuffer.Length > 0)
        {
            var line = DecodeLine(lineBuffer, encoding);

            yield return new RawLine
            {
                Offset = currentLineStartOffset,
                NextOffset = currentByteOffset,
                Content = line
            };
        }
    }

    private static string DecodeLine(MemoryStream ms, Encoding encoding)
    {
        var bytes = ms.ToArray();

        if (bytes.Length > 0 && bytes[bytes.Length - 1] == (byte)'\r')
        {
            Array.Resize(ref bytes, bytes.Length - 1);
        }

        return encoding.GetString(bytes);
    }

    private static void FixOffsetIfInMiddleOfLine(FileStream fs)
    {
        if (fs.Position <= 0)
            return;

        long current = fs.Position;

        fs.Seek(current - 1, SeekOrigin.Begin);

        int prev = fs.ReadByte();

        if (prev == '\n')
        {
            fs.Seek(current, SeekOrigin.Begin);
            return;
        }

        fs.Seek(current, SeekOrigin.Begin);

        while (fs.Position < fs.Length)
        {
            int b = fs.ReadByte();

            if (b == '\n')
                break;
        }
    }

    private void UpdateCheckpointInMemory(FileLine line)
    {
        if (!_options.EnableCheckpoint)
            return;

        long fileLength = 0;

        try
        {
            var fileInfo = new FileInfo(line.FilePath);
            if (fileInfo.Exists)
                fileLength = fileInfo.Length;
        }
        catch
        {
        }

        var item = new FileCheckpoint
        {
            FilePath = line.FilePath,
            Offset = line.NextOffset,
            LineNumber = line.LineNumber,
            FileLength = fileLength,
            LastUpdateTime = DateTime.Now
        };

        _checkpoints.AddOrUpdate(
            line.FilePath,
            item,
            (_, old) =>
            {
                // 防止并发情况下 offset 回退
                if (item.Offset >= old.Offset)
                    return item;

                return old;
            });
    }

    private void LoadCheckpoint()
    {
        var path = _options.CheckpointFilePath;

        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!File.Exists(path))
            return;

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var list = JsonSerializer.Deserialize<List<FileCheckpoint>>(json);

            if (list == null)
                return;

            foreach (var item in list)
            {
                if (string.IsNullOrWhiteSpace(item.FilePath))
                    continue;

                _checkpoints[item.FilePath] = item;
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex);
        }
    }

    private async Task SaveCheckpointSafeAsync()
    {
        if (!_options.EnableCheckpoint)
            return;

        if (_disposed == 1)
            return;

        if (string.IsNullOrWhiteSpace(_options.CheckpointFilePath))
            return;

        await _checkpointSaveLock.WaitAsync().ConfigureAwait(false);

        try
        {
            var dir = Path.GetDirectoryName(_options.CheckpointFilePath);

            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var list = _checkpoints.Values
                .OrderBy(x => x.FilePath)
                .ToList();

            var json = JsonSerializer.Serialize(
                list,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            var tempPath = _options.CheckpointFilePath + ".tmp";

            await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8)
                .ConfigureAwait(false);

            if (File.Exists(_options.CheckpointFilePath))
            {
                File.Delete(_options.CheckpointFilePath);
            }

            File.Move(tempPath, _options.CheckpointFilePath);
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex);
        }
        finally
        {
            _checkpointSaveLock.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed == 1)
            throw new ObjectDisposedException(nameof(MultiFileLineReader));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        try
        {
            _checkpointTimer?.Dispose();
        }
        catch
        {
        }

        try
        {
            _cts.Cancel();
        }
        catch
        {
        }

        if (_options.FlushCheckpointOnStop)
        {
            try
            {
                FlushCheckpointAsync().GetAwaiter().GetResult();
            }
            catch
            {
            }
        }

        _cts.Dispose();
        _checkpointSaveLock.Dispose();
    }
}