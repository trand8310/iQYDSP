namespace CefClient.Handler
{
    using CefSharp;
    using CefSharp.Handler;
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// 支持：
    /// 1. 下载指定时长后自动终止
    /// 2. 粗略限流下载
    /// 3. 自动清理超过指定时间的旧下载文件
    /// 4. 业务层模拟完成事件
    /// </summary>
    public sealed class TimedDownloadHandler : DownloadHandler, IDisposable
    {
        /// <summary>
        /// 下载多少秒后终止。
        /// 小于等于 0 表示不按时间终止。
        /// </summary>
        private readonly int _maxDownloadSeconds;

        /// <summary>
        /// 限速，单位 KB/s。
        /// 小于等于 0 表示不限速。
        /// </summary>
        private readonly int _limitKbPerSecond;

        /// <summary>
        /// 下载保存目录。
        /// </summary>
        private readonly string _downloadDir;

        /// <summary>
        /// 文件过期时间。
        /// 默认 24 小时。
        /// </summary>
        private readonly TimeSpan _fileExpireTime;

        /// <summary>
        /// 清理定时器。
        /// </summary>
        private readonly Timer _cleanupTimer;

        /// <summary>
        /// 活跃下载任务。
        /// key = DownloadItem.Id
        /// </summary>
        private readonly ConcurrentDictionary<int, DownloadState> _states =
            new ConcurrentDictionary<int, DownloadState>();

        private bool _disposed;

        /// <summary>
        /// 业务层模拟完成事件。
        /// 注意：这不代表 Chromium 认为下载完成，只是你自己的业务完成。
        /// </summary>
        public event Action<DownloadItem, string> FakeCompleted;

        public TimedDownloadHandler(
            string downloadDir,
            int maxDownloadSeconds = 10,
            int limitKbPerSecond = 0,
            int fileExpireHours = 24,
            int cleanupIntervalMinutes = 30)
        {
            if (string.IsNullOrWhiteSpace(downloadDir))
            {
                throw new ArgumentNullException(nameof(downloadDir));
            }

            _downloadDir = downloadDir;
            _maxDownloadSeconds = maxDownloadSeconds;
            _limitKbPerSecond = limitKbPerSecond;
            _fileExpireTime = TimeSpan.FromHours(fileExpireHours <= 0 ? 24 : fileExpireHours);

            if (!Directory.Exists(_downloadDir))
            {
                Directory.CreateDirectory(_downloadDir);
            }

            // 启动时先清理一次旧文件
            CleanupExpiredFiles();

            // 定时清理旧文件
            var interval = TimeSpan.FromMinutes(cleanupIntervalMinutes <= 0 ? 30 : cleanupIntervalMinutes);

            _cleanupTimer = new Timer(
                _ => CleanupExpiredFiles(),
                null,
                interval,
                interval);
        }

        /// <summary>
        /// 返回 true 才会继续触发 OnBeforeDownload。
        /// 如果返回 false，则直接禁止下载。
        /// </summary>
        protected override bool CanDownload(
            IWebBrowser chromiumWebBrowser,
            IBrowser browser,
            string url,
            string requestMethod)
        {
            return true;
        }

        /// <summary>
        /// 下载开始前调用。
        /// 这里决定保存路径，并调用 callback.Continue。
        /// </summary>
        protected override bool OnBeforeDownload(
            IWebBrowser chromiumWebBrowser,
            IBrowser browser,
            DownloadItem downloadItem,
            IBeforeDownloadCallback callback)
        {
            if (_disposed)
            {
                return true;
            }

            if (callback == null || callback.IsDisposed)
            {
                return true;
            }

            using (callback)
            {
                string fileName = GetSafeFileName(downloadItem);
                string savePath = GetUniqueFilePath(Path.Combine(_downloadDir, fileName));

                var state = new DownloadState
                {
                    DownloadId = downloadItem.Id,
                    Url = downloadItem.Url,
                    SavePath = savePath,
                    StartTime = DateTime.UtcNow,
                    LastCheckTime = DateTime.UtcNow,
                    LastReceivedBytes = 0,
                    IsCancelling = false,
                    IsThrottling = false
                };

                _states[downloadItem.Id] = state;

                // 开始真实下载
                callback.Continue(savePath, showDialog: false);
            }

            // true 表示这个下载事件已经处理
            return true;
        }

        /// <summary>
        /// 下载状态更新。
        /// 可以在这里取消、暂停、恢复。
        /// </summary>
        protected override void OnDownloadUpdated(
            IWebBrowser chromiumWebBrowser,
            IBrowser browser,
            DownloadItem downloadItem,
            IDownloadItemCallback callback)
        {
            if (_disposed)
            {
                return;
            }

            if (callback == null || callback.IsDisposed)
            {
                return;
            }

            DownloadState state;
            if (!_states.TryGetValue(downloadItem.Id, out state))
            {
                return;
            }

            try
            {
                if (downloadItem.IsComplete)
                {
                    _states.TryRemove(downloadItem.Id, out _);
                    return;
                }

                if (downloadItem.IsCancelled)
                {
                    _states.TryRemove(downloadItem.Id, out _);

                    // 取消后的残留文件删除掉
                    TryDeleteFile(state.SavePath);
                    return;
                }

                // 按下载时长自动取消
                if (_maxDownloadSeconds > 0)
                {
                    var elapsed = DateTime.UtcNow - state.StartTime;

                    if (elapsed.TotalSeconds >= _maxDownloadSeconds)
                    {
                        CancelDownload(callback, downloadItem, state);
                        return;
                    }
                }

                // 粗略限速
                if (_limitKbPerSecond > 0)
                {
                    TryThrottle(callback, downloadItem, state);
                }
            }
            catch
            {
                // CefSharp 回调里不要抛异常
            }
        }

        /// <summary>
        /// 取消下载。
        /// </summary>
        private void CancelDownload(
            IDownloadItemCallback callback,
            DownloadItem downloadItem,
            DownloadState state)
        {
            if (state.IsCancelling)
            {
                return;
            }

            state.IsCancelling = true;

            try
            {
                if (!callback.IsDisposed)
                {
                    callback.Cancel();
                }
            }
            catch
            {
            }

            // 业务层模拟完成
            // 注意：这个不会改变 Chromium 内部的 IsComplete 状态
            try
            {
                FakeCompleted?.Invoke(downloadItem, state.SavePath);
            }
            catch
            {
            }

            TryDeleteFile(state.SavePath);

            _states.TryRemove(downloadItem.Id, out _);
        }

        /// <summary>
        /// 粗略限速。
        /// CefSharp 没有真正意义上的精准限速接口，
        /// 这里只能通过 Pause/Resume 做近似控制。
        /// </summary>
        private void TryThrottle(
            IDownloadItemCallback callback,
            DownloadItem downloadItem,
            DownloadState state)
        {
            if (state.IsThrottling)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var timeSpan = now - state.LastCheckTime;

            if (timeSpan.TotalMilliseconds < 500)
            {
                return;
            }

            long receivedBytes = downloadItem.ReceivedBytes;
            long diffBytes = receivedBytes - state.LastReceivedBytes;

            state.LastCheckTime = now;
            state.LastReceivedBytes = receivedBytes;

            if (diffBytes <= 0)
            {
                return;
            }

            double currentKbPerSecond = diffBytes / 1024.0 / timeSpan.TotalSeconds;

            if (currentKbPerSecond <= _limitKbPerSecond)
            {
                return;
            }

            state.IsThrottling = true;

            try
            {
                if (!callback.IsDisposed)
                {
                    callback.Pause();
                }
            }
            catch
            {
                state.IsThrottling = false;
                return;
            }

            int delayMs = CalculateDelayMs(currentKbPerSecond, _limitKbPerSecond);

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs).ConfigureAwait(false);

                    if (!callback.IsDisposed &&
                        !downloadItem.IsCancelled &&
                        !downloadItem.IsComplete &&
                        !state.IsCancelling)
                    {
                        callback.Resume();
                    }
                }
                catch
                {
                }
                finally
                {
                    state.IsThrottling = false;
                }
            });
        }

        /// <summary>
        /// 自动清理超过指定时间的下载文件。
        /// </summary>
        private void CleanupExpiredFiles()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                if (!Directory.Exists(_downloadDir))
                {
                    return;
                }

                var now = DateTime.UtcNow;

                foreach (var file in Directory.EnumerateFiles(_downloadDir, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var info = new FileInfo(file);

                        // 正在下载中的文件不能删
                        if (IsActiveDownloadFile(info.FullName))
                        {
                            continue;
                        }

                        // 用最后写入时间判断是否过期
                        var lastWriteTimeUtc = info.LastWriteTimeUtc;

                        if (now - lastWriteTimeUtc >= _fileExpireTime)
                        {
                            info.Delete();
                        }
                    }
                    catch
                    {
                        // 单个文件删除失败，不影响其他文件
                    }
                }
            }
            catch
            {
                // 定时器里不能抛异常
            }
        }

        /// <summary>
        /// 判断文件是否正在下载。
        /// </summary>
        private bool IsActiveDownloadFile(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return false;
            }

            foreach (var item in _states.Values)
            {
                if (string.Equals(item.SavePath, fullName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 根据当前速度和目标速度计算暂停时间。
        /// </summary>
        private static int CalculateDelayMs(double currentKbPerSecond, int limitKbPerSecond)
        {
            if (limitKbPerSecond <= 0)
            {
                return 0;
            }

            double ratio = currentKbPerSecond / limitKbPerSecond;

            if (ratio <= 1)
            {
                return 0;
            }

            int delay = (int)Math.Min(3000, Math.Max(200, (ratio - 1) * 500));

            return delay;
        }

        /// <summary>
        /// 获取安全文件名。
        /// </summary>
        private static string GetSafeFileName(DownloadItem item)
        {
            string fileName = item.SuggestedFileName;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "download_" + item.Id + ".tmp";
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }

            return fileName;
        }

        /// <summary>
        /// 避免文件名冲突。
        /// </summary>
        private static string GetUniqueFilePath(string path)
        {
            if (!File.Exists(path))
            {
                return path;
            }

            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);

            for (int i = 1; i <= 9999; i++)
            {
                string newPath = Path.Combine(dir, name + "_" + i + ext);

                if (!File.Exists(newPath))
                {
                    return newPath;
                }
            }

            return Path.Combine(dir, name + "_" + Guid.NewGuid().ToString("N") + ext);
        }

        /// <summary>
        /// 删除文件。
        /// </summary>
        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _cleanupTimer?.Dispose();
            }
            catch
            {
            }

            _states.Clear();
        }

        private sealed class DownloadState
        {
            public int DownloadId { get; set; }

            public string Url { get; set; }

            public string SavePath { get; set; }

            public DateTime StartTime { get; set; }

            public DateTime LastCheckTime { get; set; }

            public long LastReceivedBytes { get; set; }

            public bool IsCancelling { get; set; }

            public bool IsThrottling { get; set; }
        }
    }
}