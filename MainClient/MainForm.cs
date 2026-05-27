using MainClient.Common;
using MainClient.Logging;
using MainClient.LogViewer;
using MainClient.Models;
using MainClient.UiTask;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog.Events;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Win32;
using System.Windows.Forms;

namespace MainClient
{
    public partial class MainForm : Form
    {

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger _logger;
        private readonly AppSettings _appSettings;
        private readonly IpHelper _ipHelper;
        private readonly ProxyTester _ipTester;
        private readonly AdxHelper _adxHelper;
        private readonly DevHelper _devHelper;
        private readonly AdeHelper _adeHelper;
        private int mainWnd = 0;
        private CancellationTokenSource cts;
        private SynchronizationContext sync;
        /// <summary>
        /// 标记应用程序是否重启
        /// </summary>
        private bool isRestart = false;
        private bool isRunning = false;
        private Stopwatch sw = new Stopwatch();
        private static DateTime appStartTime = System.DateTime.Now;

        #region 任务计数属性
        /// <summary>
        /// 任务数量:
        /// </summary>
        private int GetTaskCount = 0;
        /// <summary>
        /// 任务总量
        /// </summary>
        private int TotalGetTaskCount = 0;
        /// <summary>
        /// 请求数量
        /// </summary>
        private int RequestCount = 0;
        /// <summary>
        /// 请求总量
        /// </summary>
        private int TotalRequestCount = 0;
        /// <summary>
        /// 提交数量
        /// </summary>
        private int SuccessCount = 0;
        /// <summary>
        /// 提交总量
        /// </summary>
        private int TotalSuccessCount = 0;
        /// <summary>
        /// 曝光次数
        /// </summary>
        private int DspCount = 0;
        /// <summary>
        /// 曝光总量
        /// </summary>
        private int TotalDspCount = 0;

        /// <summary>
        /// 点击次数
        /// </summary>
        private int DspClickCount = 0;
        /// <summary>
        /// 点击总量
        /// </summary>
        private int TotalDspClickCount = 0;
        #endregion

        #region LogWrite

        private readonly ConcurrentQueue<UiLogItem> _uiLogBuffer = new();
        private readonly System.Windows.Forms.Timer _uiTimer = new();
        private CancellationTokenSource _uiLogCts = new();
        private int _flushing = 0;
        private const int MaxFlushCount = 500;
        // 新控件
        private LogViewerUltra logViewer;
        private void StartLogConsumer()
        {
            // 初始化新控件
            logViewer = new LogViewerUltra()
            {
                Dock = DockStyle.Fill
            };
            tabPage2.Controls.Add(logViewer);
            // 后台读取日志
            Task.Run(async () =>
            {
                var reader = UiLogChannel.Channel.Reader;

                try
                {
                    await foreach (var item in reader.ReadAllAsync(_uiLogCts.Token))
                    {
                        if (_uiLogCts.IsCancellationRequested)
                            break;

                        _uiLogBuffer.Enqueue(item);
                    }
                }
                catch (OperationCanceledException) { }

            }, _uiLogCts.Token);

            // UI Timer
            _uiTimer.Interval = 200;
            _uiTimer.Tick += (_, __) =>
            {
                if (Interlocked.Exchange(ref _flushing, 1) == 1)
                    return;

                try
                {
                    FlushLogsToUi();
                }
                finally
                {
                    Interlocked.Exchange(ref _flushing, 0);
                }
            };
            _uiTimer.Start();

            this.FormClosing += (s, e) =>
            {
                try
                {
                    _uiTimer.Stop();
                    _uiLogCts.Cancel();
                    UiLogChannel.Channel.Writer.TryComplete();
                }
                catch { }
            };
        }
        private void FlushLogsToUi()
        {
            if (IsDisposed || Disposing)
                return;

            if (!IsHandleCreated || logViewer.IsDisposed)
                return;

            if (_uiLogBuffer.IsEmpty)
                return;

            int count = 0;

            while (_uiLogBuffer.TryDequeue(out var item))
            {
                logViewer.WriteLog(item.Message, ConvertLevel(item.Level));

                if (++count >= MaxFlushCount)
                    break;
            }
        }
        // 日志级别映射
        private LogLevel ConvertLevel(LogEventLevel level) => level switch
        {
            LogEventLevel.Verbose => LogLevel.Trace,
            LogEventLevel.Debug => LogLevel.Debug,
            LogEventLevel.Information => LogLevel.Information,
            LogEventLevel.Warning => LogLevel.Warning,
            LogEventLevel.Error => LogLevel.Error,
            _ => LogLevel.Information
        };

        public void LogWriteLine(string message)
        {
            _logger.LogInformation(message);
        }

        #endregion

        #region 消息解析
        private void ResolveMessage(string value)
        {
            var message = (JObject)JsonConvert.DeserializeObject(value);
            var msgName = message["Msg"].ToString();
            if (msgName.Equals("REG"))
            {
                var clientId = message["ClientId"].ToString();
                var windowHandle = Convert.ToInt32(message["WindowHandle"].ToString());
                if (this.processOfList.TryGetValue(clientId, out var client))
                {
                    this.processOfList.AddOrUpdate(clientId, client, (key, oldValue) =>
                    {
                        oldValue.ClientWindowHandle = windowHandle;
                        return oldValue;
                    });
                }
            }
            else if (msgName.Equals("OnTaskCountHandler"))
            {
                var clientId = message["ClientId"].ToString();
                if (this.processOfList.TryGetValue(clientId, out var client))
                {
                    this.processOfList.AddOrUpdate(clientId, client, (key, oldValue) =>
                    {
                        oldValue.TaskCount = message["Data"].Value<int>();
                        return oldValue;
                    });
                }
            }
            else if (msgName.Equals("OnTaskDspHandler"))
            {
                var taskId = message.SelectToken("Data.TaskId").Value<int>();
                if (message.SelectToken("Data.Type").Value<int>() == 2)
                {
                    _adxHelper.UpdateTaskDspClick(taskId, 1);
                    Interlocked.Increment(ref this.TotalDspClickCount);
                    Interlocked.Increment(ref this.DspClickCount);
                }
                else
                {
                    _adxHelper.UpdateTaskDsp(taskId, 1);
                    Interlocked.Increment(ref this.DspCount);
                    Interlocked.Increment(ref this.TotalDspCount);
                }
            }
            else if (msgName.Equals("OnTaskLogHandler"))
            {
                LogWriteLine(message.SelectToken("Data.Message").Value<string>());
            }
        }

        private static void SendCefLoadMessage(ConsumerModel consumer, JObject args)
        {
            var message = JsonConvert.SerializeObject(JObject.FromObject(new
            {
                Msg = "LOAD",
                Data = args,
            }));
            byte[] buffer = System.Text.Encoding.Default.GetBytes(message);
            COPYDATASTRUCT cds;
            cds.dwData = (IntPtr)100;
            cds.lpData = message;
            cds.cbData = buffer.Length + 1;
            NativeMethod.SendMessage(consumer.ClientWindowHandle, NativeMethod.WM_COPYDATA, 0, ref cds);
        }


        protected override void DefWndProc(ref System.Windows.Forms.Message m)
        {
            switch (m.Msg)
            {
                case NativeMethod.WM_COPYDATA:
                    COPYDATASTRUCT data = new COPYDATASTRUCT();
                    Type myType = data.GetType();
                    data = (COPYDATASTRUCT)m.GetLParam(myType);
                    if (!string.IsNullOrWhiteSpace(data.lpData))
                    {
                        Task.Run(() => ResolveMessage(data.lpData));
                    }
                    break;
                default:
                    base.DefWndProc(ref m);
                    break;
            }
        }
        #endregion


        #region 任务调度
        private PipelineRunner<JToken>? _pipeline;
        private UiTaskRunner? _uiRunner;
        private AppAutoRestart? _appAutoRestart;
        private readonly AdTrafficAggregator _aggregator;
        #endregion


        public MainForm(
            AdTrafficAggregator aggregator,
            AdeHelper adeHelper,
            DevHelper devHelper,
            AdxHelper adxHelper,
            UrlHelper urlHelper,
            IpHelper ipHelper,
            ProxyTester ipTester,
            AppSettings appSettings,
            IHttpClientFactory httpClientFactory,
            ILogger<MainForm> logger)
        {
            InitializeComponent();
            _aggregator = aggregator;
            _adeHelper = adeHelper;
            _adxHelper = adxHelper;
            _devHelper = devHelper;
            _ipHelper = ipHelper;
            _ipTester = ipTester;
            _appSettings = appSettings;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            this.Text += $"{AppConsts.AppVersion}";
            LoadAppSetting();
            #region 数据初始化
            foreach (var item in new ManagementObjectSearcher("Select * from Win32_ComputerSystem").Get())
            {
                toolStripStatusLabel1.Text = $"CPU:{item["NumberOfLogicalProcessors"]}";
            }
            #endregion

            this.FormClosing += (s, e) =>
            {
                ShutdownAllConsumers();
            };
        }



        private void ShutdownAllConsumers()
        {
            foreach (var item in this.processOfList ?? new ConcurrentDictionary<string, ConsumerModel>())
            {
                TryKillConsumerProcess(item.Value?.ProcessId ?? 0);
            }
            CommonHelper.ClearProcesses(new string[] { "CefClient", "CefSharp.BrowserSubprocess", "WerFault" });
        }

        private void TryKillConsumerProcess(int pid)
        {
            if (pid <= 0)
                return;

            try
            {
                var target = Process.GetProcessById(pid);
                if (target.HasExited)
                    return;

                target.Kill(entireProcessTree: true);
                target.WaitForExit(3000);
            }
            catch (Exception)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = $"/PID {pid} /T /F",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                catch (Exception)
                {
                }
            }
        }

        private void TriggerStartTask()
        {
            this.InvokeOnUiThreadIfRequired(() =>
            {
                if (btnStartStop.Enabled)
                {
                    btnStartStop.PerformClick();
                }
            });
        }



        private void MainForm_Load(object sender, EventArgs e)
        {
            StartLogConsumer();
            _logger.LogInformation("应用已启动");
            Task.Run(() =>
            {
                var isRestart = System.Environment.GetCommandLineArgs().Any(p => p.StartsWith("restart"));
                if (isRestart)
                {
                    TriggerStartTask();
                }

                this.InvokeOnUiThreadIfRequired(() =>
                {
                    #region 控件初始化
                    var controls = new List<Control>() { groupBox2, groupBox6 };
                    foreach (var control in controls)
                    {
                        foreach (var c in control.Controls)
                        {
                            if (c is NumericUpDown)
                            {
                                (c as NumericUpDown).ValueChanged += (s, e) =>
                                {
                                    UpdateAppSetting();
                                };
                            }
                            else if (c is TextBox)
                            {
                                (c as TextBox).TextChanged += (s, e) =>
                                {
                                    UpdateAppSetting();
                                };
                            }
                            else if (c is CheckBox)
                            {
                                (c as CheckBox).Click += (s, e) =>
                                {
                                    UpdateAppSetting();
                                };
                            }
                            else if (c is RadioButton)
                            {
                                (c as RadioButton).Click += (s, e) =>
                                {
                                    UpdateAppSetting();
                                };
                            }
                            else if (c is ComboBox)
                            {
                                (c as ComboBox).SelectedIndexChanged += (s, e) =>
                                {
                                    UpdateAppSetting();
                                };
                            }
                        }
                    }
                    #endregion

                });
            });

        }

        private void AddTaskInfo(JToken tasks)
        {
            this.Invoke(new MethodInvoker(() =>
            {
                this.taskInfoListView.BeginUpdate();
                this.taskInfoListView.Items.Clear();
                try
                {
                    foreach (var task in tasks)
                    {
                        ListViewItem lvi = new ListViewItem();
                        lvi.Tag = task["id"].ToString();
                        lvi.Text = $"{task["type"].ToString()}-{task["title"].ToString()}";
                        lvi.SubItems.Add("");
                        lvi.SubItems.Add("");
                        lvi.SubItems.Add("");
                        lvi.SubItems.Add("");
                        lvi.SubItems.Add("");
                        this.taskInfoListView.Items.Add(lvi);
                    }
                }
                finally
                {
                    this.taskInfoListView.EndUpdate();
                }

            }));
        }

        #region 应用设置

        private void ApplyOneTimeLocalPatch()
        {

            string patchDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "patches");
            if (!Directory.Exists(patchDir))
                Directory.CreateDirectory(patchDir);
            string patchFile = Path.Combine(patchDir, "patch_page_loading202605062117.done");
            if (File.Exists(patchFile))
                return;


            //_appSettings.Rfq1688 = false;
            //_appSettings.Rfq1688Rate = 0;

            //_appSettings.p4psearch = false;
            //_appSettings.p4psearchRate = 0;

            //_appSettings.DevApiUrl = "http://211.154.24.179:9000/api/fingerprint.php";
            //_appSettings.NoTrigger1688Shop = true;
            //_appSettings.Protocol = "http";

            //var chromePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "File", "chrome-win", "130.0.6723.139");
            //if(Directory.Exists(chromePath))
            //{
            //    try
            //    {
            //        System.IO.Directory.Delete(chromePath, true);
            //    }
            //    catch (Exception)
            //    {

            //    }

            //}

            UserConfigService.Save("AppSettings", _appSettings);
            // 创建标记文件
            File.WriteAllText(
                patchFile,
                $"done at {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                Encoding.UTF8
            );
        }
        private void LoadAppSetting()
        {

            ApplyOneTimeLocalPatch();

            textBox_ProxyIpUrl.Text = _appSettings.ProxyIpUrl;
            textBox_TaskApiUrl.Text = _appSettings.TaskApiUrl;
            textBox_DevApiUrl.Text = _appSettings.DevApiUrl;
            textBox_TaskName.Text = _appSettings.TaskName;

            numericUpDown_TaskPullIntervalMs.Value = _appSettings.TaskPullIntervalMs;
            numericUpDown_MaxConcurrencyCount.Value = _appSettings.MaxConcurrencyCount;
            numericUpDown_PageLoadTimeout.Value = _appSettings.PageLoadTimeout;

            numericUpDown_Multiple.Value = _appSettings.Multiple;
            numericUpDown_MainProcessResetIntervalMinutes.Value = _appSettings.MainProcessResetIntervalMinutes;
            numericUpDown_ChildProcessResetIntervalMinutes.Value = _appSettings.ChildProcessResetIntervalMinutes;
            numericUpDown_UvIntervalMs.Value = _appSettings.UvIntervalMs;
            checkBox_IsHiddenMode.Checked = _appSettings.IsHiddenMode;
            checkBox_IsProxyMode.Checked = _appSettings.IsProxyMode;
            checkBox_IsRealIp.Checked = _appSettings.IsRealIp;
            checkBox_GetIpInfo.Checked = _appSettings.GetIpInfo;
            numericUpDown_IpValidityDuration.Value = _appSettings.IpValidityDuration;


            var usingDevIndex = _appSettings.UsingDevIndex;
            if (usingDevIndex == 2)
                radioButton_UseLocalDev.Checked = true;
            else
                radioButton_UseCloudDev.Checked = true;
            checkBox_IsDetailLog.Checked = _appSettings.IsDetailLog;



        }
        private static object lock_config = new object();
        private void UpdateAppSetting()
        {
            lock (lock_config)
            {
                _appSettings.ProxyIpUrl = textBox_ProxyIpUrl.Text;
                _appSettings.TaskApiUrl = textBox_TaskApiUrl.Text;
                _appSettings.DevApiUrl = textBox_DevApiUrl.Text;
                _appSettings.TaskName = textBox_TaskName.Text;
                _appSettings.TaskPullIntervalMs = (int)numericUpDown_TaskPullIntervalMs.Value;
                _appSettings.MaxConcurrencyCount = (int)numericUpDown_MaxConcurrencyCount.Value;
                _appSettings.PageLoadTimeout = (int)numericUpDown_PageLoadTimeout.Value;
                _appSettings.Multiple = (int)numericUpDown_Multiple.Value;
                _appSettings.MainProcessResetIntervalMinutes = (int)numericUpDown_MainProcessResetIntervalMinutes.Value;
                _appSettings.ChildProcessResetIntervalMinutes = (int)numericUpDown_ChildProcessResetIntervalMinutes.Value;
                _appSettings.UvIntervalMs = (int)numericUpDown_UvIntervalMs.Value;
                _appSettings.IsHiddenMode = checkBox_IsHiddenMode.Checked;
                _appSettings.IsProxyMode = checkBox_IsProxyMode.Checked;
                _appSettings.IsRealIp = checkBox_IsRealIp.Checked;
                _appSettings.GetIpInfo = checkBox_GetIpInfo.Checked;
                _appSettings.IpValidityDuration = (int)numericUpDown_IpValidityDuration.Value;



                if (radioButton_UseLocalDev.Checked)
                    _appSettings.UsingDevIndex = 2;
                else
                    _appSettings.UsingDevIndex = 1;
                _appSettings.IsDetailLog = checkBox_IsDetailLog.Checked;

                UserConfigService.Save("AppSettings", _appSettings);
            }

        }
        #endregion


        private ConcurrentDictionary<string, ConsumerModel> processOfList;

        /// <summary>
        /// 更新任务状态信息
        /// </summary>
        private void UpdateStatInfo()
        {

            this.BeginInvoke(new Action(() =>
            {
                label5.Text = $"请求数量:{this.RequestCount}";
                if (this.RequestCount > 0)
                    label6.Text = $"提交数量:{this.SuccessCount},{(this.SuccessCount / (double)this.RequestCount * 100):N1}%";

                label7.Text = $"曝光数量:{this.DspCount}";
                label8.Text = $"点击数量:{this.DspClickCount}";
                toolStripStatusLabel2.Text = $"进程：{this.processOfList.Count()}";
                toolStripStatusLabel3.Text = $"请求总量：{this.TotalRequestCount}";
                toolStripStatusLabel4.Text = $"提交总量：{this.TotalSuccessCount}";
                toolStripStatusLabel5.Text = $"曝光总量：{this.TotalDspCount}";
                toolStripStatusLabel6.Text = $"点击总量：{this.TotalDspClickCount}";
                if (sw.IsRunning)
                {
                    label9.Text = $"运行时间:{sw.Elapsed.Minutes}分{sw.Elapsed.Seconds}秒";
                }
            }));
        }

        /// <summary>
        /// 保存运行状态
        /// </summary>
        /// <returns></returns>
        private void LoadAppState()
        {
            var runDatPath = @"Logs/run_" + System.DateTime.Today.ToString("yyyyMMdd") + "_" + _appSettings.TaskName + ".dat";
            if (System.IO.File.Exists(runDatPath))
            {
                var content = System.IO.File.ReadAllLines(runDatPath).LastOrDefault();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    var jo = (JObject)JsonConvert.DeserializeObject(content);
                    if (jo["Task"].ToString().Equals(_appSettings.TaskName))
                    {
                        this.TotalDspCount = Convert.ToInt32(jo["TotalDspCount"].ToString());
                        if (jo.ContainsKey("TotalDspClickCount"))
                        {
                            this.TotalDspClickCount = Convert.ToInt32(jo["TotalDspClickCount"].ToString());
                        }
                        if (jo.ContainsKey("TotalRequestCount"))
                        {
                            this.TotalRequestCount = Convert.ToInt32(jo["TotalRequestCount"].ToString());
                        }
                        if (jo.ContainsKey("TotalSuccessCount"))
                        {
                            this.TotalSuccessCount = Convert.ToInt32(jo["TotalSuccessCount"].ToString());
                        }


                    }
                }
            }
        }
        /// <summary>
        /// 保存运行状态
        /// </summary>
        /// <returns></returns>
        private async Task SaveAppState()
        {
            var rundatFile = @"./Logs/run_" + System.DateTime.Today.ToString("yyyyMMdd") + "_" + _appSettings.TaskName + ".dat";
            var runData = JObject.FromObject(new
            {
                Task = _appSettings.TaskName,
                GetTaskCount,
                TotalGetTaskCount,
                RequestCount,
                TotalRequestCount,
                SuccessCount,
                TotalSuccessCount,
                DspCount,
                TotalDspCount,
                DspClickCount,
                TotalDspClickCount,
                LastDateTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
            if (!System.IO.File.Exists(rundatFile))
            {
                runData = JObject.FromObject(new
                {
                    Task = _appSettings.TaskName,
                    GetTaskCount,
                    TotalGetTaskCount = GetTaskCount,
                    RequestCount,
                    TotalRequestCount = RequestCount,
                    SuccessCount,
                    TotalSuccessCount = SuccessCount,
                    DspCount,
                    TotalDspCount = DspCount,
                    DspClickCount,
                    TotalDspClickCount = DspClickCount,
                    LastDateTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
                await System.IO.File.WriteAllTextAsync(rundatFile, $"{JsonConvert.SerializeObject(runData, Newtonsoft.Json.Formatting.None)}{System.Environment.NewLine}");
            }
            else
                await System.IO.File.AppendAllTextAsync(rundatFile, $"{JsonConvert.SerializeObject(runData, Newtonsoft.Json.Formatting.None)}{System.Environment.NewLine}");
        }





        private void InitPipelineRunner()
        {
            int capacity = Math.Max(1, _appSettings.Multiple * _appSettings.MaxConcurrencyCount);
            int consumerCount = Math.Max(1, _appSettings.MaxConcurrencyCount);
            _pipeline = new PipelineRunner<JToken>(
                capacity,
                consumerCount,
                ProducerAsync,
                ConsumerAsync
            );
            _pipeline.ProgressChanged += _ =>
            {
                if (IsDisposed || Disposing)
                    return;
            };
            _pipeline.Started += () =>
            {
                this.InvokeOnUiThreadIfRequired(() =>
                {
                    lblStatus.Text = "任务状态：Running";
                });
            };
            _pipeline.Completed += () =>
            {
                this.InvokeOnUiThreadIfRequired(() =>
                {
                    lblStatus.Text = "任务状态：Completed";
                });
            };
            _pipeline.Canceled += () =>
            {
                this.InvokeOnUiThreadIfRequired(() =>
                {
                    lblStatus.Text = "任务状态：Canceled";
                });
            };
            _pipeline.Faulted += ex => _logger.LogError(ex, "Pipeline faulted");
        }

        private async Task StartRunnerAsync()
        {
            //string version = comboBox_KernelVersion.Text;
            //var chromeDir = Path.Combine(
            //    AppDomain.CurrentDomain.BaseDirectory,
            //    "File", "chrome-win", version, version);

            //if (!Directory.Exists(chromeDir))
            //{
            //    await DownloadBrowserAsync(version);
            //    if (!Directory.Exists(chromeDir))
            //    {
            //        _logger.LogWarning("Chrome kernel missing after download: {ChromeDir}", chromeDir);
            //        MessageBox.Show("浏览器内核缺失，请检查下载配置后重试。");
            //        return;
            //    }
            //}

            await _aggregator.StartAsync();


            InitPipelineRunner();

            var runner = new UiTaskRunner(token => _pipeline!.RunAsync(token));

            ConfigureRunner(runner);

            _uiRunner = runner;
            _uiRunner.Start();



            _appAutoRestart?.Dispose();
            _appAutoRestart = null;
            var restartInterval = TimeSpan.FromMinutes(_appSettings.MainProcessResetIntervalMinutes) + TimeSpan.FromSeconds(Random.Shared.Next(-180, 180));
            _appAutoRestart = new AppAutoRestart(
                restartInterval,
                () =>
                {
                    return _uiRunner != null && _uiRunner.State == RunnerState.Running;
                });

            _appAutoRestart.Start();
        }
        private async Task StopRunnerAsync()
        {
            try
            {
                _appAutoRestart?.Stop();

                if (_uiRunner != null)
                {
                    await _uiRunner.StopAsync();
                }
                await _aggregator.StopAsync();
            }
            finally
            {
                _appAutoRestart = null;
            }
        }
        private void ConfigureRunner(UiTaskRunner runner)
        {
            runner.StateChanged += state =>
            {
                this.InvokeOnUiThreadIfRequired(() =>
                {
                    lblStatus.Text = $"任务状态:{state}";
                    btnStartStop.Text = state == RunnerState.Running ? "停止" : "开始";
                });
            };

            runner.Faulted += ex =>
            {
                _logger.LogError(ex, "UiTaskRunner faulted");
            };

            runner.LogEmitted += log =>
            {
                if (_appSettings.IsDetailLog)
                {
                    if (log.Exception == null)
                        _logger.LogInformation("[{Source}] {Message}", log.Source, log.Message);
                    else
                        _logger.LogWarning(log.Exception, "[{Source}] {Message}", log.Source, log.Message);
                }
            };

            // 1秒一次：UI统计刷新
            runner.SetPeriodicAction(
                interval: TimeSpan.FromSeconds(1),
                onTick: async token =>
                {
                    var elapsed = runner.RunElapsed;
                    var totalStats = _aggregator.GetHostTaskStats();
                    this.InvokeOnUiThreadIfRequired(() =>
                    {
                        //label_request.Text = $"提交数量:{totalStats.Request}";
                        //label_start.Text = $"执行数量:{totalStats.Start}";
                        //label_dsp.Text = $"曝光数量:{totalStats.DSP}";

                        //label5.Text = $"提交数量:{totalStats.Request}";
                        //label6.Text = $"执行数量:{totalStats.Start}";
                        //label7.Text = $"曝光数量:{totalStats.DSP}";
                        //label8.Text = $"点击数量:{totalStats.Clickthrough}";
                        //label9.Text = $"成功数量:{totalStats.Success}";
                        //toolStripStatusLabel4.Text = $"执行总量：{QTPTotalStartCount + totalStats.Start}";
                        //toolStripStatusLabel5.Text = $"曝光总量：{QTPTotalDspCount + totalStats.DSP}";
                        //toolStripStatusLabel6.Text = $"点击总量：{QTPTotalClickthroughCount + totalStats.Clickthrough}";
                        label7.Text = $"运行时长:{elapsed:hh\\:mm\\:ss}";
                    });

                    await Task.CompletedTask;
                },
                name: "RefreshStatsUi",
                skipIfRunning: true,
                timeout: TimeSpan.FromSeconds(2),
                circuitBreakThreshold: 10,
                circuitBreakCooldown: TimeSpan.FromSeconds(30)
            );
        }



        private async void btnStartStop_Click(object sender, EventArgs e)
        {
            if (!btnStartStop.Enabled)
                return;

            btnStartStop.Enabled = false;

            try
            {
                if (_uiRunner != null && _uiRunner.State is RunnerState.Running or RunnerState.Stopping)
                {
                    await StopRunnerAsync();
                }
                else
                {
                    await StartRunnerAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "btnStartStop_Click failed");
                MessageBox.Show($"启动/停止任务失败: {ex.Message}");
            }
            finally
            {
                this.InvokeOnUiThreadIfRequired(() =>
                {
                    btnStartStop.Enabled = true;
                });

            }
        }
        private void buttonClear_Click(object sender, EventArgs e)
        {

            buttonClear.Enabled = false;
            btnStartStop.Enabled = false;
            Task.Factory.StartNew(() =>
            {
                CommonHelper.ClearProcesses(new string[] { "CefClient", "CefSharp.BrowserSubprocess", "WerFault" });
                //GC.Collect();
                //GC.WaitForPendingFinalizers();
                //foreach (Process process in Process.GetProcesses())
                //{
                //    try
                //    {
                //        NativeMethod.EmptyWorkingSet(process.Handle);
                //    }
                //    catch (Exception)
                //    {
                //    }
                //}
                ////try
                ////{
                ////   // Directory.Delete(System.IO.Path.Combine(System.AppDomain.CurrentDomain.SetupInformation.ApplicationBase, "chrome", "User Data"), recursive: true);
                ////}
                ////catch (Exception ex)
                ////{
                ////    Debug.Write(ex.Message);
                ////}
            }).ContinueWith(t =>
            {
                this.BeginInvoke(new MethodInvoker(() =>
                {
                    btnStartStop.Enabled = true;
                    buttonClear.Enabled = true;
                }));

            });
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.IO.DirectoryInfo dir = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            foreach (System.IO.FileInfo file in dir.GetFiles())
                file.Delete();
            Process.Start(new ProcessStartInfo { FileName = Environment.GetFolderPath(Environment.SpecialFolder.Startup), UseShellExecute = true });
            CommonHelper.CreateShortcut("曝光服务");
        }


        /// <summary>
        /// 获取任务
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task ProducerAsync(ChannelWriter<JToken> writer, CancellationToken token)
        {
            Exception? completionError = null;

            try
            {
                var host = await IpHelper.GetLocalHostAsync();

                while (!token.IsCancellationRequested)
                {
                    var url =
                        $"{_appSettings.TaskApiUrl}?type=1&action=getTask" +
                        $"&name={System.Web.HttpUtility.UrlEncode(_appSettings.TaskName)}" +
                        $"&host={System.Web.HttpUtility.UrlEncode(host)}" +
                        $"&ver={System.Web.HttpUtility.UrlEncode(AppConsts.AppVersion)}" +
                        $"&test=0&_t={DateTime.Now.Ticks}";

                    var res = await _adeHelper.GetTaskAsync(url, token);

                    if (string.IsNullOrWhiteSpace(res))
                    {
                        LogWriteLine("读取任务异常");
                        await Task.Delay(_appSettings.TaskPullIntervalMs, token);
                        continue;
                    }

                    JArray? data;

                    try
                    {
                        var json = JToken.Parse(res);
                        data = json["data"] as JArray;
                    }
                    catch (JsonReaderException)
                    {
                        _logger.LogError("ProducerAsync json parse failed: {Response}", res);
                        await Task.Delay(_appSettings.TaskPullIntervalMs, token);
                        continue;
                    }
                    catch (JsonException)
                    {
                        _logger.LogError("ProducerAsync json parse failed: {Response}", res);
                        await Task.Delay(_appSettings.TaskPullIntervalMs, token);
                        continue;
                    }

                    if (data == null || data.Count == 0)
                    {
                        LogWriteLine("暂无任务");
                        await Task.Delay(_appSettings.TaskPullIntervalMs, token);
                        continue;
                    }

                    int multiple = Math.Max(1, _appSettings.Multiple);
                    int totalEnqueued = 0;

                    for (int i = 0; i < multiple; i++)
                    {
                        foreach (var item in data)
                        {
                            if (!await writer.WaitToWriteAsync(token))
                                return;

                            var clonedItem = item?.DeepClone() ?? new JObject();

                            await writer.WriteAsync(clonedItem, token);
                            totalEnqueued++;
                        }
                    }

                    LogWriteLine($"新增{totalEnqueued}条任务");

                    await Task.Delay(_appSettings.TaskPullIntervalMs, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                completionError = ex;
                throw;
            }
            finally
            {
                writer.TryComplete(completionError);
            }
        }


        /// <summary>
        /// 消费任务
        /// </summary>
        /// <param name="consumerId"></param>
        /// <param name="task"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task ConsumerAsync(int consumerId, JToken task, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                var parseResult = ParseTask(task);
                if (!parseResult.Success)
                {
                    _logger.LogWarning("ConsumerAsync skip malformed task: {Task}", task?.ToString());
                    return;
                }


                var ctx = parseResult.Context!;

                var initdev = await GetDeviceForTaskAsync(ctx.OS, ctx.TaskId, 0, token);
                if (initdev == null)
                {
                    _logger.LogWarning("ConsumerAsync get device failed after retries. taskId={TaskId}, uv={Uv}", ctx.TaskId, 1);
                    return;
                }

                await PrepareProxyContextAsync(ctx, task, token);

                var ipTtlSeconds = _appSettings.IpValidityDuration;
                if (ipTtlSeconds <= 0)
                {
                    _logger.LogWarning("ConsumerAsync invalid IpTtl={IpTtl}, taskId={TaskId}", ipTtlSeconds, ctx.TaskId);
                    return;
                }

                bool stopRemainingUv = await ExecuteTaskByCefClientAsync(
                    ctx,
                    task,
                    consumerId,
                    token);

                if (stopRemainingUv)
                {
                    _logger.LogInformation("ConsumerAsync stop remaining uv. taskId={TaskId}", ctx.TaskId);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (IOException ex) when (ex.Message.Contains("Pipe is broken", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("pipe has been ended", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug(ex, "Pipe closed during shutdown. consumerId={consumerId}", consumerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConsumerAsync failed:{Message}", ex.Message);
            }
        }


        private async Task<bool> ExecuteTaskByCefClientAsync(
           ConsumerTaskContext ctx,
           JToken task,
           int consumerId,
           CancellationToken token)
        {

            

            return await Task.FromResult(true);
        }





        /// <summary>
        /// 解析任务
        /// </summary>
        /// <param name="task"></param>
        /// <returns></returns>
        private ParseTaskResult ParseTask(JToken task)
        {
            if (task is not JToken taskObj)
                return new ParseTaskResult { Success = false };

            var taskIdToken = taskObj["id"]?.Value<int>() ?? 0;
            var url = taskObj["url"]?.Value<string>();
            var referer = taskObj["referer"]?.Value<string>();

            var totalUvToken = taskObj["uv"]?.Value<int>() ?? 0;
            var totalPvToken = taskObj["pv"]?.Value<int>() ?? 0;

            if (taskIdToken == 0 || totalUvToken == 0 || totalPvToken == 0 || string.IsNullOrWhiteSpace(url))
                return new ParseTaskResult { Success = false };

            var devClientId = taskObj["client"]?.Value<string>()?
                .Split(new[] { "|" }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "0";

            var ctx = new ConsumerTaskContext
            {
                TaskId = taskIdToken,
                TotalUV = Math.Max(1, totalUvToken),
                TotalPV = Math.Max(1, totalPvToken),
                DevClientId = devClientId,
                OS = _adeHelper.GetOS(devClientId),
                TaskTitle = taskObj["title"]?.Value<string>() ?? string.Empty,
                StartTime = DateTime.Now
            };

            return new ParseTaskResult
            {
                Success = true,
                Context = ctx
            };
        }

        /// <summary>
        /// 获取设备
        /// </summary>
        /// <param name="os"></param>
        /// <param name="taskId"></param>
        /// <param name="uvIndex"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task<JToken?> GetDeviceForTaskAsync(OSType os, int taskId, int uvIndex, CancellationToken token)
        {
            for (int retry = 0; retry < 5; retry++)
            {
                token.ThrowIfCancellationRequested();

                var dev = await _adeHelper.GetDeviceAsync(os, 200);
                if (dev != null)
                    return dev;
            }
            _logger.LogWarning(
                "ConsumerAsync get device failed after retries. taskId={TaskId}, uv={Uv}",
                taskId, uvIndex + 1);
            return null;
        }

        /// <summary>
        /// 标准化设备信息
        /// </summary>
        /// <param name="dev"></param>
        /// <param name="os"></param>
        private void NormalizeDevice(JToken dev, OSType os)
        {
            var ua = dev["ua"]?.Value<string>() ?? string.Empty;
            if (os == OSType.ANDROID)
            {


            }
            else if (os == OSType.IOS)
            {

            }
            else if (os == OSType.PC)
            {
            }
        }





        #region 代理 / IP 信息
        /// <summary>
        /// 准备代理 / IP 信息
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="task"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task PrepareProxyContextAsync(ConsumerTaskContext ctx, JToken task, CancellationToken token)
        {
            ctx.ProxyServer = null;
            ctx.RealIp = string.Empty;
            ctx.IpInfo = null;

            if (_appSettings.IsProxyMode)
            {
                if (!string.IsNullOrWhiteSpace(_appSettings.ProxyIpUrl))
                {
                    await PrepareRemoteProxyAsync(ctx, task, token);
                }
                else
                {
                    await PrepareLocalProxyAsync(ctx, token);
                }
            }
            else
            {
                await PrepareDirectNetworkIpInfoAsync(ctx, token);
            }
        }
        /// <summary>
        /// 远程代理模式
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="task"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private async Task PrepareRemoteProxyAsync(ConsumerTaskContext ctx, JToken task, CancellationToken token)
        {
            const int maxRetry = 10;

            for (int retry = 1; retry <= maxRetry; retry++)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    _aggregator.EnqueueFetchedIp(ctx.TaskId, 1);

                    var ipEntity = await _ipHelper.GetProxyIpAsync((JObject)task);
                    if (ipEntity == null)
                    {
                        LogWriteLine("获取IP错误");
                        await Task.Delay(Random.Shared.Next(100, 200), token);
                        continue;
                    }

                    FillProxyServerFromEntity(ctx, ipEntity);

                    if (string.IsNullOrWhiteSpace(ctx.ProxyServer) || !IsValidProxyServer(ctx.ProxyServer))
                    {
                        LogWriteLine($"IP异常,{ctx.ProxyServer}");
                        await Task.Delay(Random.Shared.Next(100, 200), token);
                        continue;
                    }

                    if (_appSettings.GetIpInfo || _appSettings.IsRealIp)
                    {
                        var ok = await TryFillIpInfoAsync(ctx, token);
                        if (!ok)
                        {
                            LogWriteLine($"无法获取IP信息,{ctx.ProxyServer}");
                            await Task.Delay(Random.Shared.Next(100, 200), token);
                            continue;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(ctx.RealIp))
                    {
                        _aggregator.EnqueueConsumedIp(ctx.TaskId, ctx.RealIp, 1);
                    }

                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogWriteLine($"IP异常,{ex.Message}");

                    if (ex.Message.Contains("没有满足您选择的条件IP"))
                        await Task.Delay(Random.Shared.Next(2000, 3000), token);

                    await Task.Delay(Random.Shared.Next(300, 500), token);
                }
            }

            throw new InvalidOperationException($"获取代理 IP 失败，taskId={ctx.TaskId}");
        }
        /// <summary>
        /// 本地代理模式
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private async Task PrepareLocalProxyAsync(ConsumerTaskContext ctx, CancellationToken token)
        {
            ctx.ProxyServer = "127.0.0.1:7890";

            var result = await _ipTester.TestAsync(ctx.ProxyServer);
            if (!result.IsValid)
            {
                LogWriteLine($"无法获取IP信息,{ctx.ProxyServer}");
                throw new InvalidOperationException($"无法获取IP信息,{ctx.ProxyServer}");
            }

            ApplyIpTestResult(ctx, result);
        }
        /// <summary>
        /// 非代理模式
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private async Task PrepareDirectNetworkIpInfoAsync(ConsumerTaskContext ctx, CancellationToken token)
        {
            if (!_appSettings.GetIpInfo && !_appSettings.IsRealIp)
                return;

            var result = await _ipTester.TestAsync(ctx.ProxyServer);
            if (!result.IsValid)
            {
                LogWriteLine($"无法获取IP信息,{ctx.ProxyServer}");
                throw new InvalidOperationException($"无法获取IP信息,{ctx.ProxyServer}");
            }

            ApplyIpTestResult(ctx, result);
        }
        #endregion

        #region 辅助方法：填代理 / 验证代理 / 填 IP 结果
        /// <summary>
        /// 辅助方法：填代理 / 验证代理 / 填 IP 结果
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="ipEntity"></param>
        private void FillProxyServerFromEntity(ConsumerTaskContext ctx, dynamic ipEntity)
        {
            if (ipEntity.format == IPFormat.JSON)
            {
                ctx.ProxyServer = $"{ipEntity.json["ip"]}:{ipEntity.json["port"]}";

                if (_appSettings.IsRealIp)
                {
                    ctx.RealIp =
                        ipEntity.json["rip"]?.GetValue<string>() ??
                        ipEntity.json["real_ip"]?.GetValue<string>() ??
                        ipEntity.json["realIp"]?.GetValue<string>() ??
                        string.Empty;
                }
            }
            else
            {
                ctx.ProxyServer = ipEntity.value;
                if (_appSettings.IsRealIp)
                    ctx.RealIp = ctx.ProxyServer ?? string.Empty;
            }
        }

        /// <summary>
        /// 验证代理1
        /// </summary>
        /// <param name="proxyServer"></param>
        /// <returns></returns>
        private bool IsValidProxyServer(string proxyServer)
        {
            const string pattern = @"(?:(?:[0,1]?\d?\d|2[0-4]\d|25[0-5])\.){3}(?:[0,1]?\d?\d|2[0-4]\d|25[0-5]):\d{1,5}";
            return Regex.IsMatch(proxyServer, pattern);
        }

        /// <summary>
        /// 验证代理2
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task<bool> TryFillIpInfoAsync(ConsumerTaskContext ctx, CancellationToken token)
        {
            var result = await _ipTester.TestAsync(ctx.ProxyServer);
            if (!result.IsValid)
                return false;

            ApplyIpTestResult(ctx, result);
            return true;
        }
        /// <summary>
        /// 验证代理3
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="result"></param>

        private void ApplyIpTestResult(ConsumerTaskContext ctx, dynamic result)
        {
            if (result.SuccessUrl.Equals("http://ip-api.com/json") ||
                result.SuccessUrl.Equals("http://211.154.24.179:9000/api/dash/ipinfo.php") ||
                result.SuccessUrl.Equals("http://117.21.200.221/api/dash/ipinfo.php") ||
                result.SuccessUrl.Equals("http://117.21.200.18:9000/api/dash/ipinfo.php"))
            {
                ctx.IpInfo = JObject.Parse(result.Data);
                ctx.RealIp = ctx.IpInfo["query"]?.Value<string>() ?? string.Empty;
            }
            else
            {
                var json = JObject.Parse(result.Data);
                if (json?.ContainsKey("query"))
                    ctx.RealIp = json["query"]?.Value<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(ctx.RealIp) && json?.ContainsKey("ip") == true)
                    ctx.RealIp = json["ip"]?.Value<string>() ?? string.Empty;

                ctx.IpInfo = new JObject
                {
                    ["query"] = ctx.RealIp
                };
            }
        }
        #endregion






        //public async Task ProduceWithWhileAndTryWrite(ChannelWriter<JObject> writer, CancellationToken token)
        //{
        //    try
        //    {
        //        while (this.isRunning && !this.isRestart && !token.IsCancellationRequested)
        //        {
        //            try
        //            {
        //                var content = await this._adxHelper.GetTaskAsync($"{_appSettings.TaskApiUrl}?type=1&action=getTask&task={_appSettings.TaskName}&test=0&_t={System.DateTime.Now.Ticks}");
        //                if (!string.IsNullOrWhiteSpace(content))
        //                {
        //                    if (content.Equals("empty"))
        //                    {
        //                        sync.Post((p) =>
        //                        {
        //                            this.taskInfoListView.Items.Clear();
        //                        }, null);
        //                        LogWriteLine($"共取到[0]条任务");
        //                    }
        //                    else
        //                    {
        //                        var tasks = (JObject)JsonConvert.DeserializeObject(content);
        //                        int taskCount = tasks["task"].Count();
        //                        if (taskCount > 0)
        //                        {
        //                            AddTaskInfo(tasks["task"]);
        //                            LogWriteLine($"新增加{tasks["task"].Count()}条任务");
        //                            for (int i = 0; i < _appSettings.Multiple; i++)
        //                            {
        //                                if (!this.isRunning || this.isRestart || token.IsCancellationRequested)
        //                                {
        //                                    break;
        //                                }

        //                                foreach (JObject task in tasks["task"])
        //                                {
        //                                    if (await writer.WaitToWriteAsync(token))
        //                                    {
        //                                        writer.TryWrite(task);
        //                                    }
        //                                }
        //                            }
        //                        }
        //                    }
        //                }
        //            }
        //            catch (Exception)
        //            {

        //            }
        //            await Task.Delay(_appSettings.TaskPullIntervalMs, token);
        //        }
        //    }
        //    catch
        //    {
        //        throw;
        //    }
        //    finally
        //    {
        //        writer.Complete();
        //    }
        //}

        public async Task ConsumeWithNestedWhileAsync(ChannelReader<JObject> reader, int processIndex, CancellationToken token)
        {
            bool isFirstTime = true;
            bool isCopyFile = true;
            bool isForcedCopy = false;
            Process process = null;
            ConsumerModel consumer = null;
            int timeout = _appSettings.ChildProcessResetIntervalMinutes * 60 + CommonHelper.RandomRange(-5, 5);


            while (await reader.WaitToReadAsync(token))
            {
                while (reader.TryRead(out var task))
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        #region isFirst
                        if (!isFirstTime && (process == null || process.HasExited || consumer == null))
                        {
                            isFirstTime = true;
                            consumer = null;
                        }

                        if (isFirstTime)
                        {
                            var initResult = await TryInitializeConsumerProcessAsync(processIndex, token, isCopyFile, isForcedCopy);
                            isFirstTime = false;
                            process = initResult.Process;
                            consumer = initResult.Consumer;
                            isCopyFile = initResult.IsCopyFile;
                            isForcedCopy = initResult.IsForcedCopy;

                            if (!initResult.IsSuccess)
                            {
                                isFirstTime = true;
                                continue;
                            }
                        }
                        #endregion

                        var taskId = task["id"].Value<int>();
                        var exposure = _adxHelper.GetOrAddTaskStatus(taskId);

                        var taskTitle = task["title"].ToString();
                        var logTitle = $"{taskTitle}【{taskId}_{processIndex}】";
                        var totalUV = task["uv"].Value<int>();
                        var totalPV = task["pv"].Value<int>();
                        var dev_client_id = task["client"].ToString().Split(new String[] { "|" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                        var adParam = (JObject)JsonConvert.DeserializeObject(System.Web.HttpUtility.UrlDecode(task["jstext"].ToString()));
                        int clickRate = 0;
                        if (task.ContainsKey("click_rate"))
                        {
                            clickRate = Convert.ToInt32(task["click_rate"].ToString());
                        }



                        string proxy_server = string.Empty;
                        string realIp = string.Empty;
                        string ip = string.Empty;
                        int redo_getip_count = 0;//IP重试次数
                        int redo_max_getip_count = 5;//IP重试最大次数
                        int successUV = 0;//UV成功次数

                    redo_getip:
                        if (redo_getip_count++ > redo_max_getip_count || successUV > 0)
                        {
                            continue;
                        }
                        if (this._appSettings.IsProxyMode)
                        {
                            try
                            {
                                var ipEntity = await _ipHelper.GetProxyIpAsync(task);
                                if (ipEntity == null)
                                {
                                    LogWriteLine($"获取IP错误");
                                    await Task.Delay(new Random().Next(100, 200));
                                    goto redo_getip;
                                }
                                if (ipEntity.format == IPFormat.JSON)
                                {
                                    proxy_server = $"{ipEntity.json["ip"]}:{ipEntity.json["port"]}";
                                    if (this._appSettings.RealIp)
                                        realIp = ipEntity.json["realIp"].ToString();
                                }
                                else
                                {
                                    proxy_server = ipEntity.value;
                                    if (this._appSettings.RealIp)
                                        realIp = proxy_server;
                                }
                                string pattern = @"(?:(?:[0,1]?\d?\d|2[0-4]\d|25[0-5])\.){3}(?:[0,1]?\d?\d|2[0-4]\d|25[0-5]):\d{0,5}";
                                if (!Regex.IsMatch(proxy_server, pattern))
                                {
                                    LogWriteLine($"IP异常,{proxy_server}");
                                    await Task.Delay(new Random().Next(100, 200));
                                    goto redo_getip;
                                }
                            }
                            catch (Exception ex)
                            {
                                LogWriteLine($"IP异常,{ex.Message}");
                                if (ex.Message.Contains("没有满足您选择的条件IP"))
                                {
                                    await Task.Delay(new Random().Next(2000, 3000));
                                }
                                await Task.Delay(new Random().Next(300, 500));
                                goto redo_getip;
                            }

                        }

                        JObject ipinfo;

                        if (_appSettings.IsProxyMode)
                        {
                            var iptester = await _ipTester.TestAsync(proxy_server);
                            if (!iptester.IsValid)
                            {
                                LogWriteLine($"IP异常,{proxy_server}");
                                await Task.Delay(new Random().Next(300, 500));
                                goto redo_getip;
                            }
                            ipinfo = JObject.Parse(iptester.Data!);
                        }
                        else
                        {
                            var iptester = await _ipTester.TestAsync();
                            if (!iptester.IsValid)
                            {
                                await Task.Delay(new Random().Next(300, 500));
                                goto redo_getip;
                            }
                            ipinfo = JObject.Parse(iptester.Data!);
                            if (_appSettings.IsRealIp && string.IsNullOrWhiteSpace(realIp))
                                realIp = ipinfo["query"].Value<string>();
                        }




                        OSType os = dev_client_id.Equals("4") ? OSType.IOS : OSType.ANDROID;

                        if (dev_client_id.Equals("7"))
                            os = OSType.PC;
                        else if (dev_client_id.Equals("10"))
                            os = OSType.OTT;


                        var ipTtlSeconds = Math.Max(1, _appSettings.IpValidityDuration);
                        var uvIntervalMs = Math.Max(0, _appSettings.UvIntervalMs);
                        var ipDeadline = DateTime.UtcNow.AddSeconds(ipTtlSeconds);

                        var hasCheckedFirstAdxInCurrentTask = false;

                        async Task<bool> ExecuteUvAsync(int uv)
                        {
                            if (process == null || process.HasExited || token.IsCancellationRequested)
                            {
                                return false;
                            }

                            var delayMs = uv > 0 ? uvIntervalMs : 0;
                            if (delayMs > 0)
                            {
                                if (DateTime.UtcNow.AddMilliseconds(delayMs) > ipDeadline)
                                {
                                    LogWriteLine($"跳过UV[{taskId}_{processIndex}_{uv}]，预计执行时间超出IP有效期{ipTtlSeconds}s");
                                    return false;
                                }
                                await Task.Delay(delayMs, token);
                            }

                            if (DateTime.UtcNow > ipDeadline || process == null || process.HasExited || token.IsCancellationRequested)
                            {
                                return false;
                            }

                            Interlocked.Increment(ref this.RequestCount);
                            Interlocked.Increment(ref this.TotalRequestCount);
                            exposure.AddAllCount(1);
                            JObject dev = (JObject)(await _devHelper.GetDevByOS(os, 200));
                            JObject? adx = null;
                            try
                            {
                                adx = await _adxHelper.GetAdRequest(task, adParam, dev, os, realIp, proxy_server, ipinfo, _appSettings.IsProxyMode);
                            }
                            catch (InvalidOperationException ex)
                            {
                                LogWriteLine($"请求广告[{task["id"]}_{Thread.CurrentThread.ManagedThreadId}_{processIndex}]:{uv},{ex.Message},{proxy_server},{uv}/{totalUV}");
                                return false;
                            }

                            if (adx == null || adx.SelectToken("bid") == null || adx.SelectToken("bid").Count() == 0)
                            {
                                LogWriteLine($"请求广告[{task["id"]}_{Thread.CurrentThread.ManagedThreadId}_{processIndex}]:{uv},没有填充,{proxy_server},{uv}/{totalUV}");
                                return false;
                            }
                            exposure.AddAdxCount(1);

                            var cacheIndex = $"s{processIndex}_{uv}";
                            var url = task["url"].Value<string>();
                            var referer = string.Empty;
                            var clickJump = false;
                            double ctr = 0;
                            ctr = clickRate > 0 && exposure.adxCount > 0 ? ((exposure.pendingClick + 1) / (double)exposure.adxCount) * 100 : 0;
                            if (!hasCheckedFirstAdxInCurrentTask && clickRate > 0)
                            {
                                hasCheckedFirstAdxInCurrentTask = true;
                                if (clickRate == 100 || exposure.pendingClick == 0 || exposure.adxCount == 0 || (ctr < clickRate))
                                {
                                    clickJump = true;
                                    exposure.AddPendingClick(1);
                                }
                            }



                            var args = new JObject();
                            args["task"] = task;
                            args["dev"] = dev;
                            args["isShowLog"] = _appSettings.IsDetailLog;
                            args["isHiddenMode"] = _appSettings.IsHiddenMode;
                            args["isProxyMode"] = _appSettings.IsProxyMode;
                            args["proxy_server"] = proxy_server;
                            args["ipinfo"] = ipinfo;
                            args["realip"] = realIp;
                            args["access_token"] = adParam["access_token"];
                            args["vast"] = adx;
                            args["cacheIndex"] = cacheIndex;
                            args["url"] = url;
                            args["referer"] = referer;
                            args["clickJump"] = clickJump;
                            args["os"] = (int)os;
                            args["clearDataForOrigin"] = "local_storage";//cache_storage,cookies,
                            args["pageLoadingTimeout"] = _appSettings.PageLoadTimeout;

                            SendCefLoadMessage(consumer, args);
                            Interlocked.Increment(ref successUV);
                            ctr = clickRate > 0 && exposure.adxCount > 0 && exposure.pendingClick > 0 ? (exposure.pendingClick / (double)exposure.adxCount) * 100 : 0;
                            LogWriteLine($"提交任务:{task["title"]}[{task["id"]}_{processIndex}_{cacheIndex}],activity={consumer.TaskCount},os={os},{proxy_server},click={clickJump},点击比率:{ctr:N2}%,{uv}/{totalUV}");
                            _adxHelper.UpdateTaskAll(taskId, 1);
                            Interlocked.Increment(ref this.SuccessCount);
                            Interlocked.Increment(ref this.TotalSuccessCount);
                            if (consumer.TaskCount > totalUV)
                                await Task.Delay(TimeSpan.FromSeconds(new Random().Next(3, 5)), token);
                            return clickJump;
                        }

                        for (var uv = 0; uv < totalUV; uv++)
                        {
                            var triggeredClick = await ExecuteUvAsync(uv);
                            if (triggeredClick)
                            {
                                break;
                            }
                        }

                        #region 清理代码
                        if (process != null && !process.HasExited && timeout > 0 && ((TimeSpan)(System.DateTime.Now - process.StartTime)).TotalSeconds > timeout)
                        {
                            isFirstTime = true;
                            isCopyFile = false;
                            if (!process.HasExited)
                            {
                                LogWriteLine($"清理进程:开始{process.MainModule.FileName}");
                                TryKillConsumerProcess(process.Id);
                            }
                        }
                        #endregion


                    }
                    catch (Exception EX)
                    {
                        Debug.WriteLine(EX.Message);
                    }
                }
            }

            #region 清理代码

            if (process != null && !process.HasExited)
            {
                if (!process.HasExited)
                {
                    TryKillConsumerProcess(process.Id);
                }
            }
            #endregion

        }

        private async Task<(bool IsSuccess, Process Process, ConsumerModel Consumer, bool IsCopyFile, bool IsForcedCopy)> TryInitializeConsumerProcessAsync(int processIndex, CancellationToken token, bool isCopyFile, bool isForcedCopy)
        {
            var clientId = Guid.NewGuid().ToString("N");
            var appBase = System.AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
            var sourRoot = System.IO.Path.Combine(appBase, "CefClient");
            var destRoot = System.IO.Path.Combine(appBase, "chrome", $"CefClient{processIndex}");
            var destFileName = System.IO.Path.Combine(destRoot, "CefClient.exe");

            EnsureConsumerClientFiles(sourRoot, destRoot, ref isCopyFile, ref isForcedCopy);

            var process = StartConsumerProcess(destFileName, clientId, processIndex);
            if (process == null)
            {
                return (false, null, null, isCopyFile, true);
            }

            var consumer = new ConsumerModel() { ProcessId = process.Id, ClientWindowHandle = 0, ProcessPath = destFileName, time = System.DateTime.Now };
            this.processOfList.TryAdd(clientId, consumer);
            SpinWait.SpinUntil(() => token.IsCancellationRequested || consumer.ClientWindowHandle != 0, 30 * 1000);
            try
            {
                LogWriteLine($"创建进程:完成{consumer.ProcessPath}");
            }
            catch (Exception ex)
            {
                LogWriteLine(ex.Message);
                return (false, process, consumer, true, true);
            }

            await Task.Delay(new Random().Next(500, 1000), token);
            return (true, process, consumer, isCopyFile, isForcedCopy);
        }

        private void EnsureConsumerClientFiles(string sourRoot, string destRoot, ref bool isCopyFile, ref bool isForcedCopy)
        {
            if (!Directory.Exists(destRoot))
            {
                Directory.CreateDirectory(destRoot);
                isCopyFile = true;
            }

            var destFileName = System.IO.Path.Combine(destRoot, "CefClient.exe");
            if (!File.Exists(destFileName) || isForcedCopy)
            {
                isForcedCopy = false;
                CommonHelper.CopyFilesRecursively(new DirectoryInfo(sourRoot), new DirectoryInfo(destRoot));
                return;
            }

            if (!isCopyFile)
            {
                return;
            }

            try
            {
                System.IO.File.Copy(System.IO.Path.Combine(sourRoot, "CefClient.exe"), System.IO.Path.Combine(destRoot, "CefClient.exe"), true);
                System.IO.File.Copy(System.IO.Path.Combine(sourRoot, "CefClient.dll"), System.IO.Path.Combine(destRoot, "CefClient.dll"), true);
                System.IO.File.Copy(System.IO.Path.Combine(sourRoot, "CefClient.runtimeconfig.json"), System.IO.Path.Combine(destRoot, "CefClient.runtimeconfig.json"), true);
                System.IO.File.Copy(System.IO.Path.Combine(sourRoot, "CefClient.deps.json"), System.IO.Path.Combine(destRoot, "CefClient.deps.json"), true);
                isCopyFile = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private Process StartConsumerProcess(string destFileName, string clientId, int processIndex)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = destFileName,
                    Arguments = $"mainWnd={this.mainWnd} isHiddenMode={_appSettings.IsHiddenMode} clientId={clientId} --consumer-id={processIndex}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var process = System.Diagnostics.Process.Start(psi);
                process.EnableRaisingEvents = true;
                process.Exited += (a, b) =>
                {
                    if (this.processOfList.TryRemove(clientId, out var value))
                    {
                        LogWriteLine($"退出进程:{clientId},{value.ProcessPath}");
                    }


                };
                return process;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return null;
            }
        }


        private void button_Running_Click(object sender, EventArgs e)
        {

        }

        private void button2_Click(object sender, EventArgs e)
        {

        }
    }

}
