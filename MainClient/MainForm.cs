using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using MainClient.Common;
using MainClient.Models;
using Microsoft.Extensions.Logging;
using System.Win32;
using System.Management;
using System.Threading.Channels;
using System.Text.RegularExpressions;
using System.Diagnostics.Eventing.Reader;

namespace MainClient
{
    public partial class MainForm : Form
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger _logger;
        private readonly IWritableOptions<AppSettings> _appSettings;
        private readonly DevHelper _devHelper = null;
        private readonly AdxHelper _adxHelper = null;
        private readonly UrlHelper _urlHelper = null;
        private readonly IpHelper _ipHelper = null;
        private readonly ProxyTester _ipTester;
        private int mainWnd = 0;
        private CancellationTokenSource cts = null;
        private SynchronizationContext sync;
        /// <summary>
        /// 标记应用程序是否重启
        /// </summary>
        private bool isRestart = false;
        private bool isRunning = false;
        private Stopwatch sw = new Stopwatch();
        private int NumberOfLogicalProcessors = Environment.ProcessorCount;
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

        #region  LogWrite

        private ConcurrentQueue<string> logBuffer = new ConcurrentQueue<string>();
        private bool isProcessingLogs = false;
        private const int MaxBatchSize = 50;
        private void ProcessLogs()
        {
            isProcessingLogs = true;
            Task.Run(async () =>
            {
                var logsToProcess = new StringBuilder();
                while (isProcessingLogs)
                {
                    bool logsProcessed = false;
                    int logCount = 0;
                    while (logCount < MaxBatchSize && logBuffer.TryDequeue(out string logMessage))
                    {
                        logsToProcess.Append(logMessage);
                        logsProcessed = true;
                    }
                    if (logsProcessed)
                    {
                        WriteToLogs(logsToProcess.ToString());
                        logsToProcess.Clear();
                    }
                    await Task.Delay(1000);
                }

            });
        }
        private void WriteToLogs(string logMessage)
        {
            if (LogTextBox.InvokeRequired)
            {
                LogTextBox.Invoke((MethodInvoker)(() => { WriteToLogs(logMessage); }));
                return;
            }
            LogTextBox.AppendText(logMessage);
            LogTextBox.ScrollToCaret();
        }
        public void LogWriteLine(string logMessage)
        {
            logBuffer.Enqueue(($"{System.DateTime.Now.ToString("[HH:mm:ss]")} {logMessage}{System.Environment.NewLine}"));
        }

        public void LogDetailInfo(string message)
        {
            this.InvokeOnUiThreadIfRequired(() =>
            {
                LogDetailTextBox.AppendText($"{System.DateTime.Now.ToString("[HH:mm:ss]")} {message}{Environment.NewLine}");
                LogDetailTextBox.ScrollToCaret();
            });


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
                LogDetailInfo(message.SelectToken("Data.Message").Value<string>());
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



        public MainForm(
            DevHelper devHelper,
            AdxHelper adxHelper,
            UrlHelper urlHelper,
            IpHelper ipHelper,
            ProxyTester ipTester,
            IWritableOptions<AppSettings> appSettings,
            IHttpClientFactory httpClientFactory,
            ILogger<MainForm> logger)
        {
            InitializeComponent();
            this.FormClosing += MainForm_FormClosing;
            this._devHelper = devHelper;
            this._adxHelper = adxHelper;
            this._urlHelper = urlHelper;
            this._ipHelper = ipHelper;
            this._ipTester = ipTester;
            this._appSettings = appSettings;
            this._logger = logger;
            this._httpClientFactory = httpClientFactory;
            this.Text += $"{AppConsts.AppVertion}";
            this.sync = SynchronizationContext.Current;
            LoadAppSetting();
            #region 控件初始化
            var controls = new List<Control>() { groupBox2, groupBox5, groupBox6 };
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
                }
            }
            #endregion

            #region 数据初始化
            this.textBox_SmsName.Text = CommonHelper.GetHostName();
            this._appSettings.Update(opt => opt.SmsName = CommonHelper.GetHostName());
            foreach (var item in new ManagementObjectSearcher("Select * from Win32_ComputerSystem").Get())
            {
                toolStripStatusLabel1.Text = $"CPU:{item["NumberOfLogicalProcessors"]}";
                this.NumberOfLogicalProcessors = Int32.Parse(item["NumberOfLogicalProcessors"].ToString());
            }
            #endregion
        }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            try
            {
                isRunning = false;
                isProcessingLogs = false;
                cts?.Cancel();
            }
            catch (Exception)
            {
            }

            ShutdownAllConsumers();
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

        private void MainForm_Load(object sender, EventArgs e)
        {
            var commandLineArgs = System.Environment.GetCommandLineArgs();
            var isRestart = System.Environment.GetCommandLineArgs().Any(p => p.StartsWith("restart"));
            if (isRestart)
            {
                LoadAppState();
                sync.Post((p) =>
                {
                    buttonStart.PerformClick();
                }, null);
            }


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
        private void LoadAppSetting()
        {

            textBox_ProxyIpUrl.Text = _appSettings.Value.ProxyIpUrl;
            textBox_TaskApiUrl.Text = _appSettings.Value.TaskApiUrl;
            textBox_DevApiUrl.Text = _appSettings.Value.DevApiUrl;
            numericUpDown_FetchTaskInterval.Value = _appSettings.Value.FetchTaskInterval;
            numericUpDown_UVInterval.Value = _appSettings.Value.UVInterval;
            numericUpDown_MaximumConcurrency.Value = _appSettings.Value.MaximumConcurrency;
            numericUpDown_PageLoadingTimeout.Value = _appSettings.Value.PageLoadingTimeout;
            textBox_TaskName.Text = _appSettings.Value.TaskName;
            numericUpDown_Multiple.Value = _appSettings.Value.Multiple;
            numericUpDown_MainResetTimeout.Value = _appSettings.Value.MainResetTimeout;
            numericUpDown_SubResetTimeout.Value = _appSettings.Value.SubResetTimeout;
            checkBox_IsHiddenMode.Checked = _appSettings.Value.IsHiddenMode;
            checkBox_IsProxyMode.Checked = _appSettings.Value.IsProxyMode;
            checkBox_IsRealIp.Checked = _appSettings.Value.IsRealIp;
            checkBox_IsCheckIp.Checked = _appSettings.Value.IsCheckIp;
            checkBox_DisableUserCache.Checked = _appSettings.Value.DisableUserCache;
            checkBox_DisableLoadImage.Checked = _appSettings.Value.DisableLoadImage;
            checkBox_UseCacheImg.Checked = _appSettings.Value.UseCacheImg;
            checkBox_UseCacheVideo.Checked = _appSettings.Value.UseCacheVideo;
            checkBox_UseCacheCss.Checked = _appSettings.Value.UseCacheCss;
            checkBox_UseCacheJS.Checked = _appSettings.Value.UseCacheJS;
            checkBox_SendSms.Checked = _appSettings.Value.SendSms;
            textBox_SmsName.Text = _appSettings.Value.SmsName;
            textBox_SmsPhone.Text = _appSettings.Value.SmsPhone;
            numericUpDown_SendSmsTimeout.Value = _appSettings.Value.SendSmsTimeout;
            var usingDevIndex = _appSettings.Value.UsingDevIndex;
            if (usingDevIndex == 2)
                radioButton_UsingRealDev.Checked = true;
            else if (usingDevIndex == 3)
                radioButton_UseLocalDev.Checked = true;
            else
                radioButton_UseSystemDev.Checked = true;
            checkBox_IsDetailLog.Checked = _appSettings.Value.IsDetailLog;
            numericUpDown_IpTtl.Value = _appSettings.Value.IpTtl;
        }
        private void UpdateAppSetting()
        {
            _appSettings.Update(opt =>
            {
                opt.ProxyIpUrl = textBox_ProxyIpUrl.Text;
                opt.TaskApiUrl = textBox_TaskApiUrl.Text;
                opt.DevApiUrl = textBox_DevApiUrl.Text;
                opt.FetchTaskInterval = (int)numericUpDown_FetchTaskInterval.Value;
                opt.UVInterval = (int)numericUpDown_UVInterval.Value;
                opt.MaximumConcurrency = (int)numericUpDown_MaximumConcurrency.Value;
                opt.PageLoadingTimeout = (int)numericUpDown_PageLoadingTimeout.Value;
                opt.TaskName = textBox_TaskName.Text;
                opt.Multiple = (int)numericUpDown_Multiple.Value;
                opt.MainResetTimeout = (int)numericUpDown_MainResetTimeout.Value;
                opt.SubResetTimeout = (int)numericUpDown_SubResetTimeout.Value;
                opt.IsHiddenMode = checkBox_IsHiddenMode.Checked;
                opt.IsProxyMode = checkBox_IsProxyMode.Checked;
                opt.IsRealIp = checkBox_IsRealIp.Checked;
                opt.IsCheckIp = checkBox_IsCheckIp.Checked;
                opt.DisableUserCache = checkBox_DisableUserCache.Checked;
                opt.DisableLoadImage = checkBox_DisableLoadImage.Checked;
                opt.UseCacheImg = checkBox_UseCacheImg.Checked;
                opt.UseCacheVideo = checkBox_UseCacheVideo.Checked;
                opt.UseCacheCss = checkBox_UseCacheCss.Checked;
                opt.UseCacheJS = checkBox_UseCacheJS.Checked;
                opt.SendSms = checkBox_SendSms.Checked;
                opt.SmsName = textBox_SmsName.Text;
                opt.SmsPhone = textBox_SmsPhone.Text;
                opt.SendSmsTimeout = (int)numericUpDown_SendSmsTimeout.Value;
                if (radioButton_UsingRealDev.Checked)
                    opt.UsingDevIndex = 2;
                else if (radioButton_UseLocalDev.Checked)
                    opt.UsingDevIndex = 3;
                else
                    opt.UsingDevIndex = 1;
                opt.IpTtl = (int)numericUpDown_IpTtl.Value;
                opt.IsDetailLog = checkBox_IsDetailLog.Checked;
            });
        }
        #endregion

        private ConcurrentDictionary<string, ConsumerModel> processOfList;

        private Process? CreateNewProcess(string filePath, int handle, string clientId, int taskIndex)
        {
            try
            {
                ProcessStartInfo processInfo = new ProcessStartInfo();
                processInfo.FileName = filePath;
                processInfo.Arguments = $"mainWnd={handle} isHiddenMode={_appSettings.Value.IsHiddenMode} clientId={clientId}";
                processInfo.UseShellExecute = false;
                processInfo.CreateNoWindow = true;
                Process process = new Process();
                process.EnableRaisingEvents = true;
                process.StartInfo = processInfo;
                process.Exited += (a, b) =>
                {
                    LogDetailInfo($"退出进程:{clientId},{filePath}");
                    this.processOfList.TryRemove(clientId, out var value);
                };
                process.Start();
                return process;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            return null;
        }


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
            var runDatPath = @"Logs/run_" + System.DateTime.Today.ToString("yyyyMMdd") + "_" + _appSettings.Value.TaskName + ".dat";
            if (System.IO.File.Exists(runDatPath))
            {
                var content = System.IO.File.ReadAllLines(runDatPath).LastOrDefault();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    var jo = (JObject)JsonConvert.DeserializeObject(content);
                    if (jo["Task"].ToString().Equals(_appSettings.Value.TaskName))
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
            var rundatFile = @"./Logs/run_" + System.DateTime.Today.ToString("yyyyMMdd") + "_" + _appSettings.Value.TaskName + ".dat";
            var runData = JObject.FromObject(new
            {
                Task = _appSettings.Value.TaskName,
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
                    Task = _appSettings.Value.TaskName,
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

        private void buttonStart_Click(object sender, EventArgs e)
        {
            if (buttonStart.Text.Equals("停止"))
            {
                isRestart = false;
                isRunning = false;
                isProcessingLogs = false;
                this.cts.Cancel();
                return;
            }
            if (!File.Exists(System.IO.Path.Combine(System.AppDomain.CurrentDomain.SetupInformation.ApplicationBase, "CefClient", "CefClient.exe")))
            {
                MessageBox.Show("CefClient.exe不存在!");
                return;
            }
            ProcessLogs();
            isRestart = false;
            isRunning = true;
            CommonHelper.ClearProcesses(new string[] { "CefClient", "CefSharp.BrowserSubprocess", "WerFault" });
            CommonHelper.ClearAllErrorMsgDialog();
            this.GetTaskCount = 0;
            this.RequestCount = 0;
            this.SuccessCount = 0;
            this.DspCount = 0;
            this.DspClickCount = 0;
            this.mainWnd = (int)this.Handle;
            this.processOfList = new ConcurrentDictionary<string, ConsumerModel>();
            this.processOfList.Clear();
            buttonStart.Text = "停止";
            buttonStart.ForeColor = Color.Blue;
            buttonClear.Enabled = false;




            sw.Reset();
            sw.Start();
            this.cts = new CancellationTokenSource();
            this.cts.Token.Register(() =>
            {
                this.BeginInvoke(new MethodInvoker(() =>
                {
                    buttonStart.Enabled = false;
                    buttonStart.Text = this.isRestart ? "重启中..." : "停止中...";
                    buttonStart.ForeColor = Color.Black;
                }));
            });

            Task.Factory.StartNew(async () =>
            {
                var channel = Channel.CreateBounded<JObject>(
                    new BoundedChannelOptions(_appSettings.Value.MaximumConcurrency * _appSettings.Value.Multiple)
                    {
                        SingleWriter = true,
                        SingleReader = false,
                        FullMode = BoundedChannelFullMode.Wait
                    });

                try
                {
                    _ = Task.Run(async () =>
                    {
                        while (!this.cts.IsCancellationRequested)
                        {

                            UpdateStatInfo();
                            await Task.Delay(1000);
                        }
                    });

                    _ = Task.Run(async () =>
                    {
                        while (!this.cts.IsCancellationRequested)
                        {
                            await Task.Delay(5 * 1000);
                            await _adxHelper.UpdateTaskStat();

                        }
                    });

                    _ = Task.Run(() =>
                    {
                        int timeout = _appSettings.Value.MainResetTimeout * 60 + CommonHelper.RandomRange(-5, 5);
                        while (!this.isRestart && this.isRunning && !this.cts.IsCancellationRequested)
                        {
                            try
                            {
                                var process = Process.GetCurrentProcess();
                                var totalSeconds = (int)(((TimeSpan)(System.DateTime.Now - process.StartTime)).TotalSeconds);
                                if (_appSettings.Value.MainResetTimeout > 0 && totalSeconds > timeout)
                                {
                                    LogWriteLine("重启任务");
                                    this.isRestart = true;
                                    this.isRunning = false;
                                    this.isProcessingLogs = false;
                                    this.cts.Cancel();
                                    break;
                                }
                                CommonHelper.ClearAllErrorMsgDialog();
                            }
                            catch (Exception)
                            {

                            }
                            SpinWait.SpinUntil(() => this.cts.IsCancellationRequested || this.isRestart || !this.isRunning, 5 * 1000);
                        }
                    });

                    var producer = ProduceWithWhileAndTryWrite(channel.Writer, this.cts.Token);

                    var consumer = Parallel.ForEachAsync(Enumerable.Range(1, _appSettings.Value.MaximumConcurrency),
                        new ParallelOptions()
                        {
                            MaxDegreeOfParallelism = _appSettings.Value.MaximumConcurrency,
                            CancellationToken = this.cts.Token
                        },
                        async (index, ct) =>
                        {
                            await ConsumeWithNestedWhileAsync(channel.Reader, index, ct);
                        });
                    await Task.WhenAll(consumer, producer);
                }
                catch (TaskCanceledException)
                {
                    LogWriteLine("TaskCanceledException");
                }
                catch (Exception ex)
                {
                    LogWriteLine(ex.Message);
                }
                await _adxHelper.UpdateTaskStat();
                //保存任务状态
                await SaveAppState();
                if (isRestart)
                {
                    sync.Post((p) =>
                    {
                        CommonHelper.ProcessRestart();
                    }, null);
                }
                else if (!this.isRunning)
                {
                    this.sync.Post(p =>
                    {
                        this.buttonClear.Enabled = true;
                        this.buttonStart.Enabled = true;
                        this.buttonStart.Text = "开始";
                        this.buttonStart.ForeColor = Color.Black;
                    }, null);
                }
            }, TaskCreationOptions.LongRunning);
        }
        private void buttonClear_Click(object sender, EventArgs e)
        {

            buttonClear.Enabled = false;
            buttonStart.Enabled = false;
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
                    buttonStart.Enabled = true;
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




        public async Task ProduceWithWhileAndTryWrite(ChannelWriter<JObject> writer, CancellationToken token)
        {
            try
            {
                while (this.isRunning && !this.isRestart && !token.IsCancellationRequested)
                {
                    try
                    {
                        var content = await this._adxHelper.GetTaskAsync($"{_appSettings.Value.TaskApiUrl}?type=1&action=getTask&task={_appSettings.Value.TaskName}&test=0&_t={System.DateTime.Now.Ticks}");
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            if (content.Equals("empty"))
                            {
                                sync.Post((p) =>
                                {
                                    this.taskInfoListView.Items.Clear();
                                }, null);
                                LogWriteLine($"共取到[0]条任务");
                            }
                            else
                            {
                                var tasks = (JObject)JsonConvert.DeserializeObject(content);
                                int taskCount = tasks["task"].Count();
                                if (taskCount > 0)
                                {
                                    AddTaskInfo(tasks["task"]);
                                    LogWriteLine($"新增加{tasks["task"].Count()}条任务");
                                    for (int i = 0; i < _appSettings.Value.Multiple; i++)
                                    {
                                        if (!this.isRunning || this.isRestart || token.IsCancellationRequested)
                                        {
                                            break;
                                        }

                                        foreach (JObject task in tasks["task"])
                                        {
                                            if (await writer.WaitToWriteAsync(token))
                                            {
                                                writer.TryWrite(task);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {

                    }
                    await Task.Delay(_appSettings.Value.FetchTaskInterval, token);
                }
            }
            catch
            {
                throw;
            }
            finally
            {
                writer.Complete();
            }
        }

        public async Task ConsumeWithNestedWhileAsync(ChannelReader<JObject> reader, int processIndex, CancellationToken token)
        {
            bool isFirstTime = true;
            bool isCopyFile = true;
            bool isForcedCopy = false;
            Process process = null;
            ConsumerModel consumer = null;
            int timeout = _appSettings.Value.SubResetTimeout * 60 + CommonHelper.RandomRange(-5, 5);


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
                        if (this._appSettings.Value.IsProxyMode)
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
                                    if (this._appSettings.Value.RealIp)
                                        realIp = ipEntity.json["realIp"].ToString();
                                }
                                else
                                {
                                    proxy_server = ipEntity.value;
                                    if (this._appSettings.Value.RealIp)
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

                        if (_appSettings.Value.IsProxyMode)
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
                            if (_appSettings.Value.IsRealIp && string.IsNullOrWhiteSpace(realIp))
                                realIp = ipinfo["query"].Value<string>();
                        }




                        OSType os = dev_client_id.Equals("4") ? OSType.IOS : OSType.ANDROID;

                        if (dev_client_id.Equals("7"))
                            os = OSType.PC;
                        else if (dev_client_id.Equals("10"))
                            os = OSType.OTT;


                        var ipTtlSeconds = Math.Max(1, _appSettings.Value.IpTtl);
                        var uvIntervalMs = Math.Max(0, _appSettings.Value.UVInterval);
                        var ipDeadline = DateTime.UtcNow.AddSeconds(ipTtlSeconds);

                        var hasClickedInCurrentTask = false;

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
                            exposure.AddAck(1);
                            Interlocked.Increment(ref this.RequestCount);
                            Interlocked.Increment(ref this.TotalRequestCount);
                            JObject dev = (JObject)(await _devHelper.GetDevByOS(os, 200));
                            JObject? adx = null;
                            try
                            {
                                adx = await _adxHelper.GetAdRequest(task, adParam, dev, os, realIp, proxy_server, ipinfo, _appSettings.Value.IsProxyMode);
                            }
                            catch (InvalidOperationException ex)
                            {
                                LogWriteLine($"请求广告[{task["id"]}_{Thread.CurrentThread.ManagedThreadId}_{processIndex}]:{uv},{ex.Message},{proxy_server}");
                                return false;
                            }

                            if (adx == null || adx.SelectToken("bid") == null || adx.SelectToken("bid").Count() == 0)
                            {
                                LogWriteLine($"请求广告[{task["id"]}_{Thread.CurrentThread.ManagedThreadId}_{processIndex}]:{uv},没有填充,{proxy_server}");
                                return false;
                            }


                            var cacheIndex = $"s{processIndex}_{uv}";
                            var url = task["url"].Value<string>();
                            var referer = string.Empty;
                            var clickJump = false;
                            if (clickRate > 0 && !hasClickedInCurrentTask)
                            {
                                if (clickRate == 100 || exposure.pendingClick == 0 || exposure.ack == 0 || (exposure.pendingClick / (double)exposure.ack) * 100 < clickRate)
                                {
                                    clickJump = true;
                                    hasClickedInCurrentTask = true;
                                    exposure.AddPendingClick(1);
                                }
                            }


                            var args = new JObject();
                            args["task"] = task;
                            args["dev"] = dev;
                            args["isShowLog"] = _appSettings.Value.IsDetailLog;
                            args["isHiddenMode"] = _appSettings.Value.IsHiddenMode;
                            args["isProxyMode"] = _appSettings.Value.IsProxyMode;
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
                            args["pageLoadingTimeout"] = _appSettings.Value.PageLoadingTimeout;

                            SendCefLoadMessage(consumer, args);
                            Interlocked.Increment(ref successUV);
                            LogWriteLine($"提交任务:{task["title"]}[{task["id"]}_{processIndex}_{cacheIndex}],activity={consumer.TaskCount},os={os},{proxy_server},click={clickJump},{uv}/{totalUV}");
                            _adxHelper.UpdateTaskAck(taskId, 1);
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
                LogDetailInfo($"创建进程:完成{process.MainModule.FileName}");
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
                    Arguments = $"mainWnd={this.mainWnd} isHiddenMode={_appSettings.Value.IsHiddenMode} clientId={clientId} --consumer-id={processIndex}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var process = System.Diagnostics.Process.Start(psi);
                process.EnableRaisingEvents = true;
                process.Exited += (a, b) =>
                {
                    LogDetailInfo($"退出进程:{clientId},{destFileName}");
                    this.processOfList.TryRemove(clientId, out var value);
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
    }

}
