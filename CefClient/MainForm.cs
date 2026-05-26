using CefClient.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Windows.Forms;
using static System.Net.WebRequestMethods;


namespace CefClient
{
    public partial class MainForm : Form
    {

        private int hMainWnd = 0;
        private bool isHiddenMode = true;
        private string clientId = string.Empty;
        private int taskCount = 0;


        #region  LogWrite

        public void LogWriteLine()
        {
            LogWrite(Environment.NewLine);
        }
        public void LogWriteLine(string msg)
        {
            LogWrite(msg + Environment.NewLine);
        }
        public void LogWriteLine(string msg, params object[] parameters)
        {
            LogWrite(msg + Environment.NewLine, parameters);
        }

        public void LogWrite(string msg, params object[] parameters)
        {
            LogWrite(string.Format(msg, parameters));
        }

        public void LogWrite(string msg)
        {
            if (InvokeRequired)
            {
                Invoke((MethodInvoker)(() => { LogWrite(msg); }));
                return;
            }
            LogTextBox.AppendText($"{System.DateTime.Now.ToString("[HH:mm:ss]")} {msg}");
            LogTextBox.ScrollToCaret();
        }

        private void SendTaskMsgHandler(string message)
        {
            byte[] sarr = System.Text.Encoding.Default.GetBytes(message);
            Win32.COPYDATASTRUCT cds;
            cds.dwData = (IntPtr)100;
            cds.lpData = message;
            cds.cbData = sarr.Length + 1;
            Win32.User.SendMessage(this.hMainWnd, Win32.User.WM_COPYDATA, 0, ref cds);
        }
        private void OnTaskLogHandler(string message)
        {
            Task.Run(() =>
            {
#if DEBUG
                LogWriteLine(message);
#endif
                LogWriteLine(message);
                var data = JsonConvert.SerializeObject(JObject.FromObject(new
                {
                    ClientId = clientId,
                    Msg = "OnTaskLogHandler",
                    Data = new { Message = message },
                }));
                SendTaskMsgHandler(data);
            });

        }
        private void OnTaskDspHandler(int taskid, int type = 1, int count = 1)
        {
            var data = JsonConvert.SerializeObject(JObject.FromObject(new
            {
                ClientId = clientId,
                Msg = "OnTaskDspHandler",
                Data = new { TaskId = taskid, Type = type, Count = count },
            }));
            SendTaskMsgHandler(data);
        }
        private void OnTaskCountHandler(int count)
        {
            var data = JsonConvert.SerializeObject(JObject.FromObject(new
            {
                ClientId = clientId,
                Msg = "OnTaskCountHandler",
                Data = count,
            }));
            SendTaskMsgHandler(data);
        }


        #endregion

        private void ResolveMessage(string value)
        {
            Task.Run(() =>
            {
                var message = (JObject)JsonConvert.DeserializeObject(value);
                var msgName = message["Msg"].Value<string>();
                if (msgName.Equals("LOAD"))
                {
                    var data = message["Data"].ToString();
                    //LogWriteLine(data);
                    var args = (JObject)JsonConvert.DeserializeObject(data);
                    var taskId = args.SelectToken("task.id").Value<int>();
                    OnTaskCountHandler(Interlocked.Increment(ref taskCount));
                    this.BeginInvoke((MethodInvoker)(() =>
                    {
                        var form = new WebViewForm(args, (s, e) =>
                        {
                            OnTaskLogHandler(e);
                        })
                        {
                            Size = new Size(960, 1000),
                        };
                        form.OnDspEventHandler += (s, e) =>
                        {
                            OnTaskDspHandler(taskId, 1, e);
                        };
                        form.OnDspClickEventHandler += (s, e) =>
                        {
                            OnTaskDspHandler(taskId, 2, e);
                        };
                        form.FormClosed += (s, arg) =>
                        {
                            OnTaskCountHandler(Interlocked.Decrement(ref taskCount));
                        };
                        form.Show();
                    }));
                }
                else if (msgName.Equals("STOP"))
                {
                    LogWriteLine("5秒后退出该进程");
                    this.InvokeOnUiThreadIfRequired(() => { System.Environment.Exit(0); });
                }
                else if (msgName.Equals("SHOW"))
                {
                    this.isHiddenMode = false;
                }
                else if (msgName.Equals("HIDE"))
                {
                    this.isHiddenMode = true;
                }
            });

        }

        protected override void DefWndProc(ref System.Windows.Forms.Message m)
        {
            switch (m.Msg)
            {
                case Win32.User.WM_COPYDATA:
                    Win32.COPYDATASTRUCT data = new Win32.COPYDATASTRUCT();
                    Type myType = data.GetType();
                    data = (Win32.COPYDATASTRUCT)m.GetLParam(myType);
                    var value = data.lpData;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        ResolveMessage(value);
                    }
                    break;
                default:
                    base.DefWndProc(ref m);
                    break;
            }
        }

        public MainForm()
        {
            InitializeComponent();
            var commandLineArgs = System.Environment.GetCommandLineArgs();
            foreach (var c in commandLineArgs)
            {
                if (c.StartsWith("mainWnd="))
                {
                    this.hMainWnd = Convert.ToInt32(c.Split('=')[1]);
                }
                else if (c.StartsWith("isHiddenMode="))
                {
                    this.isHiddenMode = Convert.ToBoolean(c.Split('=')[1]);
                    if(isHiddenMode)
                    {
                        this.WindowState = FormWindowState.Minimized;
                        this.ShowInTaskbar = false;
                        SetVisibleCore(false);
                    }

                }
                else if (c.StartsWith("clientId="))
                {
                    this.clientId = c.Split('=')[1];
                }
            }
            SendRegMessage();
            LogWriteLine($"ProcessId={Process.GetCurrentProcess().Id},Handle={this.Handle},RootCachePath={CefCachePaths.RootCachePath},isHiddenMode={this.isHiddenMode}");
        }

        protected override void SetVisibleCore(bool value)
        {
#if DEBUG
            value = true;
#else
            //value = value;
#endif
            base.SetVisibleCore(value);
        }


        private void SendRegMessage()
        {
            var currentProcess = Process.GetCurrentProcess();
            var message = JsonConvert.SerializeObject(JObject.FromObject(new
            {
                Msg = "REG",
                WindowHandle = (int)this.Handle,
                ClientId = this.clientId,
                ProcessId = currentProcess.Id,
                ProcessPath = currentProcess.MainModule.FileName,
            }));
            SendTaskMsgHandler(message);
        }
        private void MainForm_Load(object sender, EventArgs e)
        {

        }

        private async Task<string> GetIp(string url)
        {
            HttpClient httpClient = new HttpClient();
            HttpResponseMessage response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        private async Task<string> GetDev(string type = "android")
        {
            var client = new HttpClient();
            try
            {
                HttpResponseMessage response = await client.GetAsync($"http://117.21.200.18:9000/api/getdev.php?type={type}&count=1&t={System.DateTime.Now.Ticks}");
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    return responseBody;
                }
                else
                {
                    return null;
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine(ex.Message);
                return null;
            }
        }
        private async Task<string> GetTask(string taskName)
        {
            var client = new HttpClient();
            try
            {
                //http://117.21.200.148/
                //http://117.21.200.19/client-v5.php?type=1
                HttpResponseMessage response = await client.GetAsync($"http://117.21.200.19/client-v5.php?type=1&action=getTask&task={taskName}&test=0&_t={System.DateTime.Now.Ticks}");
                response.EnsureSuccessStatusCode();
                response.EnsureSuccessStatusCode();

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine(ex.Message);
            }
            return null;
        }



        private int i = 0;
        private void buttonStart_Click(object sender, EventArgs e)
        {




 



            Task.Run(async () =>
            {

                //const string encToken = "1234567890abcdefghijklmnopqrstuv";
                //const string signToken = "abcdefghijklmnopqrstuv1234567890";
                //const string iv = "982a78c4d1be43f1a8763bdc39d69204";

                //var testCaseList = new List<(string Price, string ExpectedCiphertext)>
                //{
                //    ("145",  "MWEyYjNjNGQ1ZTZmN2c4aFOlli81T4k"),
                //    ("7800", "MWEyYjNjNGQ1ZTZmN2c4aFWpk6gAWuDZ"),
                //    ("92",   "MWEyYjNjNGQ1ZTZmN2c4aFujKohFEQ"),
                //    ("7",    "MWEyYjNjNGQ1ZTZmN2c4aFW8Tc8V"),
                //    ("103",  "MWEyYjNjNGQ1ZTZmN2c4aFOhkCSwFiA")
                //};

                //foreach (var testCase in testCaseList)
                //{
                //    bool ok = PriceEncryptor.EncryptPrice(
                //        testCase.Price,
                //        iv,
                //        encToken,
                //        signToken,
                //        out string ciphertext
                //    );

                //    if (!ok)
                //    {
                //        Console.WriteLine($"加密失败，price={testCase.Price}");
                //        return 1;
                //    }

                //    if (ciphertext != testCase.ExpectedCiphertext)
                //    {
                //        Console.WriteLine("测试失败");
                //        Console.WriteLine($"Price:    {testCase.Price}");
                //        Console.WriteLine($"Expected: {testCase.ExpectedCiphertext}");
                //        Console.WriteLine($"Actual:   {ciphertext}");
                //        return 1;
                //    }

                //    Console.WriteLine($"测试通过 Price={testCase.Price}, Ciphertext={ciphertext}");
                //}






                var vast = JProperty.Parse(Properties.Resources.android_opening2);
                var task = ((JObject)JsonConvert.DeserializeObject(await GetTask("iqitest")))["task"][0];
                var dev = ((JObject)JsonConvert.DeserializeObject(await GetDev("android")))["data"][0];
                var referer = task["referer"].ToString();
                var isProxyMode = false;
                string proxy_server = string.Empty;
                if (isProxyMode)
                {
                    proxy_server = await GetIp("http://api.test.myipproxy.com:8422/api/getIp?type=1&num=1&orderId=O21082011400595127523&time=1629430893&sign=deb17c877bdbf17a9e461bfcaab4c141&unbindTime=60&dataType=1&noDuplicate=1&pid=&cid=");
                }
                var args = new JObject();
                args["task"] = task;
                args["dev"] = dev;
                args["vast"] = vast;
                args["disableLoadImage"] = false;
                args["disableUserCache"] = false;
                args["isProxyMode"] = isProxyMode;
                args["isHiddenMode"] = false;
                args["proxy_server"] = proxy_server?.Trim();
                args["realip"] = "172.16.12.247";
                args["clickJump"] = true;
                args["cacheIndex"] = "1";
                args["url"] = null;
                args["referer"] = referer;
                args["os"] = 1;
                args["isShowLog"] = true;
                args["showDevTools"] = false;
                args["useCacheImg"] = false;
                args["useCacheVideo"] = false;
                args["useCacheCss"] = false;
                args["useCacheJS"] = false;
                args["clearDataForOrigin"] = "local_storage";// "cache_storage,cookies,local_storage";
                this.BeginInvoke(() =>
                {
                    var form = new WebViewForm(args, (s, e) =>
                    {
                        LogWriteLine(e);
                    });

                    form.FormClosed += (s, arg) =>
                    {
                        LogWriteLine("FormClosed");
                        //OnTaskLogHandler("FormClosed");
                    };
                    form.Show();
                });
            });

        }
    }

}
