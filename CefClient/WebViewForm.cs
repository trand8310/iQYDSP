using CefClient.Common;
using CefClient.Handler;
using CefSharp;
using CefSharp.Handler;
using CefSharp.WinForms;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CefClient
{
    public partial class WebViewForm : Form
    {
        const string encToken = "1234567890abcdefghijklmnopqrstuv";
        const string signToken = "abcdefghijklmnopqrstuv1234567890";
        const string iv = "1a2b3c4d5e6f7g8h";


        private string caption = "浏览器";
        private readonly JObject _args;
        private bool isHiddenMode = true;
        private bool isShowLog = false;


        #region  LogWrite

        public event EventHandler<string> OnLogEventHandler;
        public event EventHandler<int> OnDspEventHandler;
        public event EventHandler<int> OnDspClickEventHandler;

        private void DspChanged(int count = 1)
        {
            OnDspEventHandler?.Invoke(this, count);
        }
        private void DspClickChanged(int count = 1)
        {
            OnDspClickEventHandler?.Invoke(this, count);
        }
        private void LogWriteLine(string message)
        {
            if (this.isShowLog)
            {
                OnLogEventHandler?.Invoke(this, message);
            }
        }

        #endregion


        private Task UiInvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            if (IsDisposed || Disposing)
            {
                tcs.TrySetException(new ObjectDisposedException(nameof(MainForm)));
                return tcs.Task;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return tcs.Task;
            }

            void Execute()
            {
                try
                {
                    if (IsDisposed || Disposing)
                    {
                        tcs.TrySetException(new ObjectDisposedException(nameof(MainForm)));
                        return;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                        return;
                    }

                    action();
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke((Action)Execute);
                }
                else
                {
                    Execute();
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }
        private Task<T> UiInvokeAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (IsDisposed || Disposing)
            {
                tcs.TrySetException(new ObjectDisposedException(nameof(MainForm)));
                return tcs.Task;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return tcs.Task;
            }

            void Execute()
            {
                try
                {
                    if (IsDisposed || Disposing)
                    {
                        tcs.TrySetException(new ObjectDisposedException(nameof(MainForm)));
                        return;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                        return;
                    }

                    var result = func();
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke((Action)Execute);
                }
                else
                {
                    Execute();
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }
        private async Task<bool> TryUiInvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            try
            {
                await UiInvokeAsync(action, cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }


        private Task<LoadUrlAsyncResponse> LoadPageAsync(IWebBrowser browser, string address = null, int timeout = 10)
        {
            return browser.LoadUrlAsync(address).TimeoutAfter(TimeSpan.FromSeconds(timeout));
        }






        static string url_macro_process(JToken vast, JToken ad, string url, int os, JToken dev, string realip, string type, bool click = false)
        {
            if (string.IsNullOrEmpty(url))
            {

                return url;
            }


            try
            {
                if (url.Contains("${AUCTION_PRICE}"))
                {
                    var price = ad.SelectToken("price")?.Value<int>() ?? -1;
                    if (price != -1)
                    {
                        bool ok = PriceEncryptor.EncryptPrice(
                           price.ToString(),
                           iv,
                           encToken,
                           signToken,
                           out string ciphertext
                       );
                        if (ok)
                        {
                            url = url.Replace("${AUCTION_PRICE}", ciphertext);
                        }
                    }

                }
                if (url.Contains("__REQUESTID__"))
                {
                    url = url.Replace("__REQUESTID__", vast["id"].Value<string>());
                }

                if (url.Contains("__IP__"))
                {
                    url = url.Replace("__IP__", realip);
                }

                if (url.Contains("__OS__"))
                {
                    url = url.Replace("__OS__", (os == 2 ? "1" : "0"));
                }

                if (os == 1)
                {
                    if (url.Contains("__IMEI__"))
                    {
                        var imei = dev["imei"]?.Value<string>();
                        if (!string.IsNullOrWhiteSpace(imei))
                        {
                            url = url.Replace("__IMEI__", CommonHelper.MD5Hash(imei));
                        }
                    }
                    if (url.Contains("__ANDROIDID__"))
                    {
                        var androidid = dev["androidid"]?.Value<string>();
                        if (!string.IsNullOrWhiteSpace(androidid))
                        {
                            url = url.Replace("__ANDROIDID__", CommonHelper.MD5Hash(androidid));
                        }
                    }

                    if (url.Contains("__MAC__") || url.Contains("__MAC1__"))
                    {
                        var mac = dev["mac"]?.Value<string>();
                        if (!string.IsNullOrWhiteSpace(mac))
                        {
                            url = url.Replace("__MAC__", CommonHelper.MD5Hash(mac.Replace(":", "").ToUpper()));//字符串，去除分隔符“:”，转大写，然后 MD5 加密
                            url = url.Replace("__MAC1__", CommonHelper.MD5Hash(mac.ToUpper()));//字符串，保留分隔符“:”，转大写，然后 MD5 加密
                        }
                    }
                }
                else if (os == 2)
                {
                    if (url.Contains("__IDFA__"))
                    {
                        var idfa = dev["idfa"]?.Value<string>();
                        if (!string.IsNullOrWhiteSpace(idfa))
                        {
                            url = url.Replace("__IDFA__", idfa);
                        }
                    }
                }


                //bid
                if (url.Contains("__TS__"))
                {
                    url = url.Replace("__TS__", CommonHelper.UnixTimeNow().ToString());
                }

                var sw = dev["sw"]?.Value<int>();
                var sh = dev["sh"]?.Value<int>();
                if (sw.HasValue && sh.HasValue)
                {
                    int screenWidth = sw.Value;
                    int screenHeight = sh.Value;

                    int materialX1 = 0;
                    int materialY1 = 0;
                    int materialX2 = 0;
                    int materialY2 = 0;

                    // 1. 素材区域
                    if (url.Contains("__IMP_AREA_X1Y1X2Y2__"))
                    {
                        // 素材宽度：大概等于屏幕宽度
                        materialX1 = CommonHelper.RandomRange(0, 2);
                        materialX2 = screenWidth;

                        // 素材高度：屏幕高度的 5% ~ 100%
                        int materialHeight = (int)Math.Ceiling(
                            CommonHelper.RandomRange(5, 100) * 0.01 * screenHeight
                        );

                        // 防止素材太小，按钮放不下
                        materialHeight = Math.Max(materialHeight, 80);

                        // 素材顶部位置
                        // 如果你希望素材从页面顶部附近开始，可以用 47~145
                        materialY1 = CommonHelper.RandomRange(47, 145);

                        // 防止超过屏幕底部
                        if (materialY1 + materialHeight > screenHeight)
                        {
                            materialY1 = Math.Max(0, screenHeight - materialHeight);
                        }

                        materialY2 = materialY1 + materialHeight;

                        var impArea = $"{materialX1}_{materialY1}_{materialX2}_{materialY2}";
                        url = url.Replace("__IMP_AREA_X1Y1X2Y2__", impArea);
                    }

                    // 2. 按钮区域：按钮在素材区域中间
                    if (url.Contains("__BUTTON_AREA_X1Y1X2Y2__"))
                    {
                        // 如果没有素材占位符，也给一个默认素材区域
                        if (materialX2 <= materialX1 || materialY2 <= materialY1)
                        {
                            materialX1 = 0;
                            materialX2 = screenWidth;

                            int materialHeight = (int)Math.Ceiling(
                                CommonHelper.RandomRange(5, 100) * 0.01 * screenHeight
                            );

                            materialHeight = Math.Max(materialHeight, 80);

                            materialY1 = CommonHelper.RandomRange(47, 145);

                            if (materialY1 + materialHeight > screenHeight)
                            {
                                materialY1 = Math.Max(0, screenHeight - materialHeight);
                            }

                            materialY2 = materialY1 + materialHeight;
                        }

                        int materialWidthFinal = materialX2 - materialX1;
                        int materialHeightFinal = materialY2 - materialY1;

                        // 按钮宽度：素材宽度的 20% ~ 45%
                        int buttonWidth = (int)Math.Round(
                            CommonHelper.RandomRange(20, 45) * 0.01 * materialWidthFinal
                        );

                        // 按钮高度：42 ~ 58
                        int buttonHeight = CommonHelper.RandomRange(42, 58);

                        // 防止按钮高度超过素材高度
                        if (buttonHeight > materialHeightFinal)
                        {
                            buttonHeight = Math.Max(20, materialHeightFinal - 4);
                        }

                        int buttonX1 = materialX1 + (int)Math.Round((materialWidthFinal - buttonWidth) / 2.0);
                        int buttonY1 = materialY1 + (int)Math.Round((materialHeightFinal - buttonHeight) / 2.0);

                        int buttonX2 = buttonX1 + buttonWidth;
                        int buttonY2 = buttonY1 + buttonHeight;

                        var buttonArea = $"{buttonX1}_{buttonY1}_{buttonX2}_{buttonY2}";
                        url = url.Replace("__BUTTON_AREA_X1Y1X2Y2__", buttonArea);
                    }


                    if (click)
                    {
                        if (url.Contains("__CLICK_POS_XY__"))
                        {
                            // 素材宽度：大概等于屏幕宽度
                            materialX1 = CommonHelper.RandomRange(materialX1, materialX2);
                            materialX2 = screenWidth;

                            // 素材高度：屏幕高度的 5% ~ 100%
                            int materialHeight = (int)Math.Ceiling(
                                CommonHelper.RandomRange(5, 100) * 0.01 * screenHeight
                            );

                            // 防止素材太小，按钮放不下
                            materialHeight = Math.Max(materialHeight, 80);

                            // 素材顶部位置
                            // 如果你希望素材从页面顶部附近开始，可以用 47~145
                            materialY1 = CommonHelper.RandomRange(47, 145);

                            // 防止超过屏幕底部
                            if (materialY1 + materialHeight > screenHeight)
                            {
                                materialY1 = Math.Max(0, screenHeight - materialHeight);
                            }

                            materialY2 = materialY1 + materialHeight;
                            var clickArea = $"{CommonHelper.RandomRange(materialX1, materialX2)}_{CommonHelper.RandomRange(materialY1, materialY2)}";
                            url = url.Replace("__CLICK_POS_XY__", clickArea);
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }


            return url;
        }

        private ChromiumWebBrowser CreateChromiumWebBrowser(DeviceViewportResult devProfile, int os = 1, string? cacheId = null, string? address = null)
        {
            var browserSettings = new BrowserSettings()
            {

            };

            var requestContextSettings = new RequestContextSettings
            {
                //CachePath = System.IO.Path.Combine(CefCachePaths.RootCachePath, cacheId ?? "s00"),
            };

            var requestContext = new RequestContext(requestContextSettings);
            var browser = new ChromiumWebBrowser(address ?? "about:blank", requestContext)
            {
                BrowserSettings = browserSettings,
            };
            browser.LifeSpanHandler = new CfxLifeSpanHandler();
            browser.JsDialogHandler = new CfxJsDialogHandler();

            if (!this.isHiddenMode)
            {
                browser.FrameLoadEnd += (sender, args) =>
                {
                    if (args.Frame.IsMain)
                    {
                        if (_args.SelectToken("showDevTools")?.Value<bool>() ?? false)
                        {
                            (sender as ChromiumWebBrowser).GetBrowserHost().ShowDevTools();
                        }
                    }
                };

                browser.TitleChanged += (s, args) =>
                {
                    this.InvokeOnUiThreadIfRequired(() =>
                    {
                        var title = args.Title;
                        if (string.IsNullOrWhiteSpace(title))
                            title = args.Browser.MainFrame.Url;
                        title = $"{this.caption}{title}";
                        this.Text = title;

                    });
                };

                browser.AddressChanged += (s, args) =>
                {
                    this.InvokeOnUiThreadIfRequired(() =>
                    {
                        this.textBox_Address.Text = args.Address;

                    });
                };
            }
            return browser;
        }



        private async Task ProcessDownloadTrackersAsync(
        ChromiumWebBrowser browser,
        JToken vast,
        JToken bid,
        int os,
        JToken dev,
        string realip,
        string bid_type,
        JToken task,
        double probability,
        TimeSpan timeout)
        {
            var downloadtrackers = bid.SelectToken("admnative.link.downloadtrackers");
            if (downloadtrackers == null)
                return;

            // 第一级：是否触发下载流程
            if (!CommonHelper.IsEventOccurring(probability))
                return;

            bool startOk = await FireTrackersAsync(
                browser,
                downloadtrackers,
                "startdownload",
                $"{bid_type}::startdownload",
                vast,
                bid,
                os,
                dev,
                realip,
                bid_type,
                task,
                timeout
            );

            // 如果没有 startdownload 节点，或者没有成功执行，就不继续
            if (!startOk)
                return;

            // 第二级：是否触发下载完成
            if (!CommonHelper.IsEventOccurring(probability))
                return;

            bool finishDownloadOk = await FireTrackersAsync(
                browser,
                downloadtrackers,
                "finishdownload",
                $"{bid_type}::finishdownload",
                vast,
                bid,
                os,
                dev,
                realip,
                bid_type,
                task,
                timeout
            );

            if (!finishDownloadOk)
                return;

            // 第三级：是否触发安装完成
            if (!CommonHelper.IsEventOccurring(probability))
                return;

            await FireTrackersAsync(
                browser,
                downloadtrackers,
                "finishinstall",
                $"{bid_type}::finishinstall",
                vast,
                bid,
                os,
                dev,
                realip,
                bid_type,
                task,
                timeout
            );


            // 第四级：是否触发安装完成
            if (!CommonHelper.IsEventOccurring(probability))
                return;


            var conversionTrackers = bid.SelectToken("admnative.link.conversionTrackers");
            if (conversionTrackers == null)
                return;
            var link = bid.SelectToken("admnative.link");

            await FireTrackersAsync(
                browser,
                link,
                "conversionTrackers",
                $"{bid_type}::conversionTrackers",
                vast,
                bid,
                os,
                dev,
                realip,
                bid_type,
                task,
                timeout
            );

        }

        private async Task<bool> FireTrackersAsync(
            ChromiumWebBrowser browser,
            JToken trackerRoot,
            string tokenName,
            string logPrefix,
            JToken vast,
            JToken bid,
            int os,
            JToken dev,
            string realip,
            string bid_type,
            JToken task,
            TimeSpan timeout)
        {
            var trackers = trackerRoot.SelectToken(tokenName);
            if (trackers == null)
                return false;

            bool hasExecuted = false;

            foreach (var tracker in trackers)
            {
                try
                {
                    var url = tracker.Value<string>();

                    if (string.IsNullOrWhiteSpace(url))
                        continue;

                    url = url_macro_process(
                        vast,
                        bid,
                        url,
                        os,
                        dev,
                        realip,
                        bid_type,
                        true
                    );

                    if (string.IsNullOrWhiteSpace(url))
                        continue;

                    await CefLoadHelper.LoadUrlWithTimeoutAsync(
                        browser,
                        url,
                        timeout
                    );

                    LogWriteLine($"{logPrefix}:[{task["id"]}]:{url}");

                    hasExecuted = true;
                }
                catch (Exception ex)
                {
                    LogWriteLine($"{logPrefix}:error:[{task["id"]}]:{ex.Message}");
                }
            }

            return hasExecuted;
        }


        public WebViewForm(JObject args, EventHandler<string> logEventHandler)
        {
            this.OnLogEventHandler += logEventHandler;
            this._args = args;
            this.caption = "local:";
            var isProxyMode = _args.SelectToken("isProxyMode")?.Value<bool>() ?? false;
            var realip = _args.SelectToken("realip")?.Value<string>();
            var proxy_server = _args.SelectToken("proxy_server")?.Value<string>();
            if (isProxyMode && !string.IsNullOrWhiteSpace(proxy_server))
            {
                this.caption = $"proxy[{proxy_server}]:";
            }
            this.isHiddenMode = _args.SelectToken("isHiddenMode")?.Value<bool>() ?? false;
            this.isShowLog = _args.SelectToken("isShowLog")?.Value<bool>() ?? false;

            InitializeComponent();

            var dev = _args.SelectToken("dev")?.Value<JObject>();
            var os = _args.SelectToken("os")?.Value<int>() ?? 1;
            var ua = _args.SelectToken("dev.ua")?.Value<string>();
            var model = _args.SelectToken("dev.model")?.Value<string>();
            int sw = _args.SelectToken("dev.sw")?.Value<int>() ?? 1920;
            int sh = _args.SelectToken("dev.sh")?.Value<int>() ?? 1080;
            var devProfile = DeviceViewportMatcher.Match(sw, sh, (os == 2 ? DeviceSystemType.IOS : DeviceSystemType.Android), model);
            var cacheIndex = _args.SelectToken("cacheIndex")?.Value<string>() ?? "s00";
            var browser = CreateChromiumWebBrowser(devProfile, os, cacheIndex, "about:blank");


            browser.Location = new Point(0, this.textBox_Address.Height + 1);
            browser.Dock = DockStyle.None;
            if (os == 1 || os == 2)
            {
                browser.Size = new System.Drawing.Size(devProfile.ViewportWidth, devProfile.ViewportHeight);
            }
            else
            {
                browser.Size = new System.Drawing.Size(sw, sh);
            }
            this.Controls.Add(browser);
            this.Width = devProfile.ViewportWidth + 200;
            this.Height = devProfile.ViewportHeight + 100;






            Task.Run(async () =>
            {

                browser.RequestHandler = new ExternalProtocolRequestHandler(message => LogWriteLine($"{message}"));
                await browser.WaitForInitialLoadAsync();
                //var loaded = await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, "https://m.baidu.com", TimeSpan.FromSeconds(30));
                //if (loaded == CefNavigationResult.Success)
                //{

                //}
                //await Task.Delay(TimeSpan.FromSeconds(180));


                #region 代理设置
                //user:password@ip:port
                if (isProxyMode)
                {

                    if (!string.IsNullOrWhiteSpace(proxy_server))
                    {
                        var context = browser.GetBrowser().GetHost().RequestContext;
                        var v = new Dictionary<string, object>();
                        v["mode"] = "fixed_servers";
                        v["server"] = proxy_server;
                        bool success = context.SetPreference("proxy", v, out string error);
                    }
                }
                #endregion

                //var requestHandler = new CfxRequestHandler(this._args, (s, e) =>
                //{
                //    //LogWriteLine(e);
                //});
                //browser.RequestHandler = requestHandler;
                try
                {

                    var task = _args.SelectToken("task")?.Value<JObject>()!;
                    var vast = _args.SelectToken("vast")?.Value<JObject>();
                    var bid = vast?.SelectToken("bid").FirstOrDefault();
                    int pv = task.SelectToken("pv")?.Value<int>() ?? 1;
                    pv = pv == 0 ? 1 : pv;

                    #region sleep
                    int sleep = 0;
                    if (task.ContainsKey("sleep") && !string.IsNullOrWhiteSpace(task["sleep"].ToString()))
                    {
                        var text = task["sleep"].ToString();
                        try
                        {
                            if (text.Contains("-"))
                            {
                                var values = text.Split('-');
                                sleep = new Random().Next(Convert.ToInt32(values[0]), Convert.ToInt32(values[1]));
                            }
                            else
                            {
                                sleep = Convert.ToInt32(text);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex.Message);
                        }
                    }
                    else
                    {
                        sleep = new Random().Next(4, 8);
                    }
                    #endregion

                    var pageLoadingTimeout = _args["pageLoadingTimeout"]?.Value<int>() ?? 10;
                    pageLoadingTimeout = pageLoadingTimeout == 0 ? 10 : pageLoadingTimeout;
                    var clickJump = _args.SelectToken("clickJump")?.Value<bool>() ?? false;

                    using (var devToolsClient = browser.GetDevToolsClient())
                    {
                        //var clearDataForOrigin = _args.SelectToken("clearDataForOrigin")?.Value<string>() ?? "cache_storage,cookies,local_storage";//"appcache,cache_storage,cookies,local_storage"
                        //await devToolsClient.Storage.ClearDataForOriginAsync("*", clearDataForOrigin);
                        if (os == 1 || os == 2)
                        {
                            await devToolsClient.Emulation.SetDeviceMetricsOverrideAsync(
                                width: devProfile.ViewportWidth,
                                height: devProfile.ViewportHeight,
                                deviceScaleFactor: devProfile.DeviceScaleFactor,
                                mobile: true,
                                scale: 1.0,
                                //screenWidth: devProfile.ViewportWidth,
                                //screenHeight: devProfile.ViewportHeight,
                                positionX: 0, positionY: 0,
                                dontSetVisibleSize: false,
                                screenOrientation: new CefSharp.DevTools.Emulation.ScreenOrientation()
                                {
                                    Type = CefSharp.DevTools.Emulation.ScreenOrientationType.PortraitPrimary,
                                    Angle = 0
                                }
                                );
                            await devToolsClient.Emulation.SetTouchEmulationEnabledAsync(true, 5);
                            if (os == 1)
                            {
                                await devToolsClient.Emulation.SetUserAgentOverrideAsync(userAgent: ua, platform: "Android");
                            }
                            else
                            {
                                await devToolsClient.Emulation.SetUserAgentOverrideAsync(userAgent: ua, platform: "iPhone");
                            }
                            await devToolsClient.Emulation.SetScrollbarsHiddenAsync(true);
                            // await devToolsClient.Emulation.SetAutoDarkModeOverrideAsync(true);
                        }
                        else
                        {
                            await devToolsClient.Emulation.SetDeviceMetricsOverrideAsync(
                                width: devProfile.ViewportWidth,
                                height: devProfile.ViewportHeight,
                                deviceScaleFactor: devProfile.DeviceScaleFactor,
                                mobile: false,
                                scale: 1.0,
                                screenWidth: devProfile.ViewportWidth,
                                screenHeight: devProfile.ViewportHeight);
                        }

                        //await devToolsClient.SetEmitTouchEventsForMouse();
                        double probability = 1.0;// 0.15;


                        string bid_type = "opening";
                        var opening = bid!.SelectToken("opening");

                        if (opening != null)
                        {
                            bid_type = "opening";
                            //开屏
                            var imptrackers = bid!.SelectToken("link.imptrackers");
                            if (imptrackers != null)
                            {
                                foreach (var tracker in imptrackers)
                                {
                                    try
                                    {
                                        var url = tracker.Value<string>();
                                        url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                        await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                        LogWriteLine($"{bid_type}::imptrackers[{task["id"]}]:{url}");
                                    }
                                    catch (Exception)
                                    {

                                    }
                                    DspChanged();
                                }
                                if (clickJump)
                                {
                                    var clicktrackers = bid!.SelectToken("link.clicktrackers");
                                    if (clicktrackers != null)
                                    {
                                        foreach (var tracker in clicktrackers)
                                        {
                                            try
                                            {
                                                var url = tracker.Value<string>();
                                                url = url_macro_process(vast, bid, url, os, dev, realip, bid_type, true);
                                                await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                                LogWriteLine($"{bid_type}::clicktrackers[{task["id"]}]:{url}");
                                            }
                                            catch (Exception)
                                            {

                                            }
                                        }
                                        DspClickChanged();
                                        //点击落地页
                                        var curl = bid!.SelectToken("link.curl");


                                        //curl

                                    }
                                }
                            }

                        }
                        else
                        {
                            var imgs = bid!.SelectToken("admnative.imgs");
                            var video = bid!.SelectToken("admnative.video");
                            if (imgs != null && video == null)
                            {
                                bid_type = "imgs";
                                //信息流图片
                                var imptrackers = bid!.SelectToken("admnative.link.imptrackers");
                                if (imptrackers != null)
                                {
                                    foreach (var tracker in imptrackers)
                                    {
                                        try
                                        {
                                            var url = tracker.Value<string>();
                                            url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                            await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                            LogWriteLine($"{bid_type}::imptrackers[{task["id"]}]:{url}");
                                        }
                                        catch (Exception)
                                        {

                                        }
                                    }
                                    DspChanged();
                                    if (clickJump)
                                    {
                                        var clicktrackers = bid!.SelectToken("admnative.link.clicktrackers");
                                        if (clicktrackers != null)
                                        {
                                            foreach (var tracker in clicktrackers)
                                            {
                                                try
                                                {
                                                    var url = tracker.Value<string>();
                                                    url = url_macro_process(vast, bid, url, os, dev, realip, bid_type, true);
                                                    await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                                    LogWriteLine($"{bid_type}::clicktrackers[{task["id"]}]:{url}");
                                                }
                                                catch (Exception)
                                                {

                                                }
                                            }
                                            DspClickChanged();
                                            #region downloadtrackers
                                            await ProcessDownloadTrackersAsync(
                                                browser,
                                                vast!,
                                                bid!,
                                                os,
                                                dev!,
                                                realip,
                                                bid_type,
                                                task,
                                                probability,
                                                TimeSpan.FromSeconds(15)
                                            );
                                            #endregion
                                        }
                                    }
                                }
                            }
                            else if (video != null && imgs == null)
                            {
                                //信息流视频
                                bid_type = "video";
                                var imptrackers = bid!.SelectToken("admnative.link.imptrackers");
                                if (imptrackers != null)
                                {
                                    foreach (var tracker in imptrackers)
                                    {
                                        try
                                        {
                                            var url = tracker.Value<string>();
                                            url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                            await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                            LogWriteLine($"{bid_type}::imptrackers:[{task["id"]}]:{url}");
                                        }
                                        catch (Exception)
                                        {

                                        }
                                        DspChanged();
                                    }
                                    if (clickJump)
                                    {
                                        var clicktrackers = bid!.SelectToken("admnative.link.clicktrackers");
                                        if (clicktrackers != null)
                                        {
                                            #region clicktrackers
                                            foreach (var tracker in clicktrackers)
                                            {
                                                try
                                                {
                                                    var url = tracker.Value<string>();
                                                    url = url_macro_process(vast, bid, url, os, dev, realip, bid_type, true);
                                                    await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                                    LogWriteLine($"{bid_type}::clicktrackers:[{task["id"]}]:{url}");
                                                }
                                                catch (Exception)
                                                {

                                                }
                                            }

                                            DspClickChanged();
                                            #endregion


                                            #region downloadtrackers
                                            await ProcessDownloadTrackersAsync(
                                                browser,
                                                vast!,
                                                bid!,
                                                os,
                                                dev!,
                                                realip,
                                                bid_type,
                                                task,
                                                probability,
                                                TimeSpan.FromSeconds(15)
                                            );
                                            #endregion
                                        }


                                    }




                                }
                            }
                        }
                    }
                    LogWriteLine($"vast[{task["id"]}]:操作完成");
                    await TaskDelay(sleep, "关闭浏览器");
                    LogWriteLine($"vast[{task["id"]}]:任务结束");
                }
                catch (Exception ex)
                {
                    LogWriteLine($"任务异常:{ex.Message}");
                }
                finally
                {
                    TaskEnd();
                }
            });


            if (isHiddenMode)
            {
                this.ShowInTaskbar = false;
                this.WindowState = FormWindowState.Minimized;
            }




        }

        private async Task TaskDelay(int interval, string text = "结束")
        {
            try
            {
                while (interval-- > 1 && this.IsHandleCreated)
                {
                    this.InvokeOnUiThreadIfRequired(() => { this.Text = $"{caption}{interval}秒后{text}."; });
                    await Task.Delay(1000);
                }
            }
            catch (Exception)
            {

          
            }
        }
        private void TaskEnd()
        {
            this.InvokeOnUiThreadIfRequired(() => { this.Close(); });
        }
        private void WebViewForm_Load(object sender, EventArgs e)
        {
            //this.Visible = !this.isHiddenMode;



        }

        private void button1_Click(object sender, EventArgs e)
        {

            //Task.Run(async () =>
            //{
            //    using (DevToolsClient DTC = this.chromiumWebBrowser.GetDevToolsClient())
            //    {
            //        await DTC.Storage.ClearDataForOriginAsync("*", "all");
            //        //await DTC.Network.ClearBrowserCacheAsync();
            //        //await DTC.Network.ClearBrowserCookiesAsync();
            //    }
            //    this.chromiumWebBrowser.Reload();
            //});

        }
    }
}