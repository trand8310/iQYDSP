using CefClient.Common;
using CefClient.Handler;
using CefSharp;
using CefSharp.WinForms;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Policy;



namespace CefClient
{
    public partial class WebViewForm : Form
    {
        //access token:bdc0f9bee8514f36bfa61c0c3a7b2b18
        const string encryption_token = "b97500816d0742119af34b7fade07e40";
        const string integrity_token = "4a403884d3b744ab95d771ef29b3ef21";


        //const string encToken = "1234567890abcdefghijklmnopqrstuv";
        //const string signToken = "abcdefghijklmnopqrstuv1234567890";
        //const string iv = "1a2b3c4d5e6f7g8h";


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


        static ConcurrentDictionary<string, string> urls_macro_dict =
           new ConcurrentDictionary<string, string>();


        static string url_macro_process(JToken vast, JToken ad, string url, int os, JToken dev, string realip, string type, bool click = false)
        {
            if (string.IsNullOrEmpty(url))
            {

                return url;
            }
            var request_id = vast["id"]?.Value<string>();


            try
            {
                if (url.Contains("${AUCTION_PRICE}"))
                {
                    var price = ad.SelectToken("price")?.Value<int>() ?? -1;
                    if (price != -1)
                    {
                        bool ok = PriceEncryptor.EncryptPrice(
                           price.ToString(),
                           request_id!,
                           encryption_token,
                           integrity_token,
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
                        // 2. 获取
                        string impArea = urls_macro_dict.GetOrAdd("__IMP_AREA_X1Y1X2Y2__", k =>
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

                            var result = $"{materialX1}_{materialY1}_{materialX2}_{materialY2}";
                            return result;
                        });
                        url = url.Replace("__IMP_AREA_X1Y1X2Y2__", impArea);
                    }




                    // 2. 按钮区域：按钮在素材区域中间
                    if (url.Contains("__BUTTON_AREA_X1Y1X2Y2__"))
                    {
                        string buttonArea = urls_macro_dict.GetOrAdd("__BUTTON_AREA_X1Y1X2Y2__", k =>
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

                            var result = $"{buttonX1}_{buttonY1}_{buttonX2}_{buttonY2}";
                            return result;
                        });
                        url = url.Replace("__BUTTON_AREA_X1Y1X2Y2__", buttonArea);
                    }


                    if (click)
                    {
                        if (url.Contains("__CLICK_POS_XY__"))
                        {
                            string clickArea = urls_macro_dict.GetOrAdd("__CLICK_POS_XY__", k =>
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


                                var result = $"{CommonHelper.RandomRange(materialX1, materialX2)}_{CommonHelper.RandomRange(materialY1, materialY2)}";
                                return result;
                            });

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

        private ChromiumWebBrowser CreateChromiumWebBrowser(DeviceViewportResult devProfile, int os = 1, string? cacheId = null, string? address = null, bool isProxyMode = false, string? proxy_server = null)
        {

            var cachePath = System.IO.Path.Combine(CefCachePaths.RootCachePath, cacheId ?? "s00");
            LogWriteLine($"cachePath:{cachePath}");


            var browserSettings = new BrowserSettings()
            {

            };

            var requestContextSettings = new RequestContextSettings
            {
                //CachePath = cachePath,
                AcceptLanguageList = "zh-CN,zh;q=0.9",
            };

            var requestContext = new RequestContext(requestContextSettings);
            var browser = new ChromiumWebBrowser(address ?? "about:blank", requestContext)
            {
                BrowserSettings = browserSettings,
            };

            //browser.DownloadHandler = new DisableDownloadHandler();
            var downloadHandler = new TimedDownloadHandler(
                downloadDir: Path.Combine(cachePath, "_timed_downloads"),
                maxDownloadSeconds: 5,
                limitKbPerSecond: 100,
                fileExpireHours: 24,
                cleanupIntervalMinutes: 30
            );

            downloadHandler.FakeCompleted += (item, path) =>
            {
                Console.WriteLine("下载被业务层模拟完成:");
                Console.WriteLine(item.Url);
                Console.WriteLine(path);
            };

            browser.DownloadHandler = downloadHandler;

            browser.RequestHandler = new ExternalProtocolRequestHandler(message => LogWriteLine($"{message}"));
            //browser.IsBrowserInitializedChanged += (s, e) =>
            //{
            //    if (!browser.IsBrowserInitialized)
            //        return;
            //    Cef.UIThreadTaskFactory.StartNew(() =>
            //    {
            //        try
            //        {
            //            #region 代理设置
            //            LogWriteLine($"代理设置_1:{isProxyMode},{proxy_server}");
            //            if (isProxyMode && !string.IsNullOrWhiteSpace(proxy_server))
            //            {
            //                var context = browser.GetBrowser().GetHost().RequestContext;

            //                var proxy = new Dictionary<string, object>
            //                {
            //                    ["mode"] = "fixed_servers",
            //                    ["server"] = proxy_server,
            //                    ["bypass_list"] = ""
            //                };
            //                bool success = context.SetPreference("proxy", proxy, out string error);

            //                LogWriteLine($"代理设置_2:{proxy_server}, success={success}, error={error}");
            //                if (!success)
            //                {
            //                    return;
            //                }
            //            }
            //            #endregion
            //        }
            //        catch (Exception ex)
            //        {
            //            LogWriteLine($"代理设置异常:{ex}");
            //        }
            //    });
            //};



            //browser.LifeSpanHandler = new CfxLifeSpanHandler();
            //browser.JsDialogHandler = new CfxJsDialogHandler();

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


        public WebViewForm(JObject args, EventHandler<string> logEventHandler)
        {
            InitializeComponent();
            try
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



                var dev = _args.SelectToken("dev")?.Value<JObject>();
                var os = _args.SelectToken("os")?.Value<int>() ?? 1;
                var ua = _args.SelectToken("dev.ua")?.Value<string>();
                var model = _args.SelectToken("dev.model")?.Value<string>();
                int sw = _args.SelectToken("dev.sw")?.Value<int>() ?? 1920;
                int sh = _args.SelectToken("dev.sh")?.Value<int>() ?? 1080;
                var devProfile = DeviceViewportMatcher.Match(sw, sh, (os == 2 ? DeviceSystemType.IOS : DeviceSystemType.Android), model);
                var cacheIndex = _args.SelectToken("cacheIndex")?.Value<string>() ?? "s00";
                var browser = CreateChromiumWebBrowser(devProfile, os, cacheIndex, "about:blank", isProxyMode, proxy_server);
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
                    await browser.WaitForInitialLoadAsync();
                    await Cef.UIThreadTaskFactory.StartNew(() =>
                    {
                        try
                        {
                            #region 代理设置
                            //LogWriteLine($"代理设置_1:{isProxyMode},{proxy_server}");
                            if (isProxyMode && !string.IsNullOrWhiteSpace(proxy_server))
                            {
                                var context = browser.GetBrowser().GetHost().RequestContext;
                                var proxy = new Dictionary<string, object>
                                {
                                    ["mode"] = "fixed_servers",
                                    ["server"] = proxy_server,
                                };
                                bool success = context.SetPreference("proxy", proxy, out string error);

                                LogWriteLine($"代理设置_2:{proxy_server}, success={success}, error={error}");
                                if (!success)
                                {
                                    return;
                                }
                            }
                            #endregion
                        }
                        catch (Exception ex)
                        {
                            LogWriteLine($"代理设置异常:{ex}");
                        }
                    });
                    await Task.Delay(1000);
                    #region
                    //var loaded = await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, "https://m.baidu.com", TimeSpan.FromSeconds(30));
                    //if (loaded == CefNavigationResult.Success)
                    //{

                    //}
                    //await Task.Delay(TimeSpan.FromSeconds(180));
                    #endregion

                    try
                    {

                        //browser.LoadUrl("http://211.154.24.179:9000/api/dash/ipinfo.php");
                        //await Task.Delay(TimeSpan.FromSeconds(180));

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
                                    });

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
                            //double probability = 1.0;// 0.15;


                            //
                            string bid_type = "opening";
                            var opening = bid!.SelectToken("opening");
                            if (opening != null)
                            {
                                bid_type = "opening";
                                var bid_action = bid!.SelectToken("action")?.Value<string>() ?? "";
                                //开屏
                                var winNoticeUrl = bid!.SelectToken("winNoticeUrl");
                                if (winNoticeUrl != null)
                                {
                                    foreach (var tracker in winNoticeUrl)
                                    {
                                        try
                                        {
                                            var url = tracker.Value<string>();
                                            if (string.IsNullOrWhiteSpace(url))
                                                continue;
                                            url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                            await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                            LogWriteLine($"{bid_type}::winNoticeUrl[{task["id"]}]:{url}");
                                        }
                                        catch (Exception)
                                        {


                                        }

                                    }

                                }

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

                                            if (bid_action.Equals("DOWNLOAD_APP", StringComparison.CurrentCultureIgnoreCase))
                                            {
                                                #region downloadtrackers
                                                if (CommonHelper.IsEventOccurring(1.0))
                                                {
                                                    //点击落地页
                                                    var curl = bid!.SelectToken("link.curl")?.Value<string>();
                                                    if (!string.IsNullOrWhiteSpace(curl))
                                                    {
                                                        var landing_url = url_macro_process(vast, bid, curl, os, dev, realip, bid_type, true);
                                                        browser.Load(landing_url);
                                                        LogWriteLine($"{bid_type}::landing[{task["id"]}]:{landing_url}");
                                                        var downloadtrackers = bid.SelectToken("link.downloadtrackers");
                                                        if (downloadtrackers != null)
                                                        {
                                                            var startdownload = downloadtrackers.SelectToken("startdownload");
                                                            if (CommonHelper.IsEventOccurring(0.55) && startdownload != null)
                                                            {
                                                                await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                                                foreach (var tracker in startdownload)
                                                                {
                                                                    var url = tracker.Value<string>();
                                                                    if (string.IsNullOrWhiteSpace(url))
                                                                        continue;
                                                                    url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                                                    await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                                                    LogWriteLine($"{bid_type}::downloadtrackers::startdownload[{task["id"]}]:{url}");
                                                                }
                                                                var finishdownload = downloadtrackers.SelectToken("finishdownload");
                                                                if (CommonHelper.IsEventOccurring(0.75) && finishdownload != null)
                                                                {
                                                                    await Task.Delay(CommonHelper.RandomRange(1000, 3000));
                                                                    foreach (var tracker in finishdownload)
                                                                    {
                                                                        var url = tracker.Value<string>();
                                                                        if (string.IsNullOrWhiteSpace(url))
                                                                            continue;
                                                                        url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                                                        await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                                                        LogWriteLine($"{bid_type}::downloadtrackers::finishdownload[{task["id"]}]:{url}");
                                                                    }

                                                                    var startinstall = downloadtrackers.SelectToken("startinstall");
                                                                    if (startinstall != null)
                                                                    {
                                                                        foreach (var tracker in startinstall)
                                                                        {
                                                                            try
                                                                            {
                                                                                var url = tracker.Value<string>();
                                                                                if (string.IsNullOrWhiteSpace(url))
                                                                                    continue;
                                                                                url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                                                                await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                                                                LogWriteLine($"{bid_type}::downloadtrackers::startinstall[{task["id"]}]:{url}");
                                                                            }
                                                                            catch (Exception)
                                                                            {


                                                                            }

                                                                        }
                                                                    }

                                                                    var finishinstall = downloadtrackers.SelectToken("finishinstall");
                                                                    if (CommonHelper.IsEventOccurring(0.75) && finishinstall != null)
                                                                    {
                                                                        foreach (var tracker in finishinstall)
                                                                        {
                                                                            try
                                                                            {
                                                                                var url = tracker.Value<string>();
                                                                                if (string.IsNullOrWhiteSpace(url))
                                                                                    continue;
                                                                                url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                                                                await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                                                                LogWriteLine($"{bid_type}::downloadtrackers::finishinstall[{task["id"]}]:{url}");
                                                                            }
                                                                            catch (Exception)
                                                                            {


                                                                            }

                                                                        }
                                                                    }


                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                                #endregion
                                            }
                                            else if (bid_action.Equals("OPEN_APP_DEEPLINK", StringComparison.CurrentCultureIgnoreCase))
                                            {
                                                //detailPageUrl
                                                var landing_url = bid!.SelectToken("detailPageUrl")?.Value<string>();
                                                if (!string.IsNullOrWhiteSpace(landing_url))
                                                {
                                                    await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, landing_url, TimeSpan.FromSeconds(15));
                                                    LogWriteLine($"{bid_type}::landing[{task["id"]}]:{landing_url}");
                                                }
                                            }

                                        }


                                    }
                                }

                            }
                            else if (bid!.SelectToken("admnative") != null)
                            {
                                var winNoticeUrl = bid!.SelectToken("winNoticeUrl");
                                if (winNoticeUrl != null)
                                {
                                    foreach (var tracker in winNoticeUrl)
                                    {
                                        try
                                        {
                                            var url = tracker.Value<string>();
                                            if (string.IsNullOrWhiteSpace(url))
                                                continue;
                                            url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                            await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url);
                                            LogWriteLine($"{bid_type}::winNoticeUrl[{task["id"]}]:{url}");
                                        }
                                        catch (Exception)
                                        {


                                        }

                                    }

                                }


                                var admnative = bid!.SelectToken("admnative");
                                var imgs = admnative!.SelectToken("imgs");
                                var video = admnative!.SelectToken("video");
                                var bid_action = bid!.SelectToken("action")?.Value<string>() ?? "";

                                if (imgs != null && imgs.Count() > 0)
                                {
                                    bid_type = "imgs";
                                    //信息流图片
                                    var imptrackers = admnative!.SelectToken("link.imptrackers");
                                    if (imptrackers != null)
                                    {
                                        foreach (var tracker in imptrackers)
                                        {
                                            try
                                            {
                                                var url = tracker.Value<string>();
                                                url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                                await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url);
                                                LogWriteLine($"{bid_type}::imptrackers[{task["id"]}]:{url}");
                                            }
                                            catch (Exception)
                                            {

                                            }
                                        }
                                        DspChanged();
                                        if (clickJump)
                                        {
                                            var clicktrackers = admnative!.SelectToken("link.clicktrackers");
                                            if (clicktrackers != null)
                                            {
                                                foreach (var tracker in clicktrackers)
                                                {
                                                    try
                                                    {
                                                        var url = tracker.Value<string>();
                                                        url = url_macro_process(vast, bid, url, os, dev, realip, bid_type, true);
                                                        await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url);
                                                        LogWriteLine($"{bid_type}::clicktrackers[{task["id"]}]:{url}");
                                                    }
                                                    catch (Exception)
                                                    {

                                                    }
                                                }
                                                DspClickChanged();

                                                //点击落地页
                                                var curl = admnative!.SelectToken("link.curl")?.Value<string>();
                                                if (!string.IsNullOrWhiteSpace(curl))
                                                {
                                                    var landing_url = url_macro_process(vast, bid, curl, os, dev, realip, bid_type, true);
                                                    browser.Load(landing_url);
                                                    LogWriteLine($"{bid_type}::landing[{task["id"]}]:{landing_url}");
                                                    var downloadtrackers = admnative.SelectToken("link.downloadtrackers");
                                                    if (downloadtrackers != null)
                                                    {
                                                        var startdownload = downloadtrackers.SelectToken("startdownload");
                                                        if (CommonHelper.IsEventOccurring(0.55) && startdownload != null)
                                                        {
                                                            await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                                            foreach (var tracker in startdownload)
                                                            {
                                                                var url = tracker.Value<string>();
                                                                if (string.IsNullOrWhiteSpace(url))
                                                                    continue;
                                                                url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                                                await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                                                LogWriteLine($"{bid_type}::downloadtrackers::startdownload[{task["id"]}]:{url}");
                                                            }
                                                            var finishdownload = downloadtrackers.SelectToken("finishdownload");
                                                            if (CommonHelper.IsEventOccurring(0.75) && finishdownload != null)
                                                            {
                                                                await Task.Delay(CommonHelper.RandomRange(1000, 3000));
                                                                foreach (var tracker in finishdownload)
                                                                {
                                                                    var url = tracker.Value<string>();
                                                                    if (string.IsNullOrWhiteSpace(url))
                                                                        continue;
                                                                    url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                                                    await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                                                    LogWriteLine($"{bid_type}::downloadtrackers::startdownload[{task["id"]}]:{url}");
                                                                }

                                                                var startinstall = downloadtrackers.SelectToken("startinstall");
                                                                if (startinstall != null)
                                                                {
                                                                    foreach (var tracker in startinstall)
                                                                    {
                                                                        try
                                                                        {
                                                                            var url = tracker.Value<string>();
                                                                            if (string.IsNullOrWhiteSpace(url))
                                                                                continue;
                                                                            url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                                                            await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                                                            LogWriteLine($"{bid_type}::downloadtrackers::startinstall[{task["id"]}]:{url}");
                                                                        }
                                                                        catch (Exception)
                                                                        {


                                                                        }

                                                                    }
                                                                }

                                                                var finishinstall = downloadtrackers.SelectToken("finishinstall");
                                                                if (CommonHelper.IsEventOccurring(0.75) && finishinstall != null)
                                                                {
                                                                    foreach (var tracker in finishinstall)
                                                                    {
                                                                        try
                                                                        {
                                                                            var url = tracker.Value<string>();
                                                                            if (string.IsNullOrWhiteSpace(url))
                                                                                continue;
                                                                            url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                                                            await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                                                            LogWriteLine($"{bid_type}::downloadtrackers::finishinstall[{task["id"]}]:{url}");
                                                                        }
                                                                        catch (Exception)
                                                                        {


                                                                        }

                                                                    }
                                                                }


                                                            }
                                                        }
                                                    }
                                                }

                                                var conversionTrackers = admnative.SelectToken("link.conversionTrackers");
                                                if (conversionTrackers != null)
                                                {
                                                    foreach (var tracker in conversionTrackers)
                                                    {
                                                        try
                                                        {
                                                            var url = tracker.Value<string>();
                                                            url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                                            //CUPID_CCN
                                                            if(bid_action.Equals("OPEN_APP_DEEPLINK") || bid_action.Contains("DEEPLINK"))
                                                            {
                                                                url = url.Replace("CUPID_CCN", "20001");
                                                                url = url.Replace("__TARGET_APP_INSTALL__", "0");
                                                            }
                                                            await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url);
                                                            LogWriteLine($"{bid_type}::conversionTrackers[{task["id"]}]:{url}");
                                                        }
                                                        catch (Exception)
                                                        {

                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                else if (video != null && video.Count() > 0)
                                {
                                    //信息流视频
                                    bid_type = "video";
                                    var imptrackers = admnative!.SelectToken("link.imptrackers");
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


                                                //点击落地页
                                                var curl = admnative!.SelectToken("link.curl")?.Value<string>();
                                                if (!string.IsNullOrWhiteSpace(curl))
                                                {
                                                    var landing_url = url_macro_process(vast, bid, curl, os, dev, realip, bid_type, true);
                                                    browser.Load(landing_url);
                                                    LogWriteLine($"{bid_type}::landing[{task["id"]}]:{landing_url}");
                                                    var downloadtrackers = admnative.SelectToken("link.downloadtrackers");
                                                    if (downloadtrackers != null)
                                                    {
                                                        var startdownload = downloadtrackers.SelectToken("startdownload");
                                                        if (CommonHelper.IsEventOccurring(0.55) && startdownload != null)
                                                        {
                                                            await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                                            foreach (var tracker in startdownload)
                                                            {
                                                                var url = tracker.Value<string>();
                                                                if (string.IsNullOrWhiteSpace(url))
                                                                    continue;
                                                                url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                                                await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                                                LogWriteLine($"{bid_type}::downloadtrackers::startdownload[{task["id"]}]:{url}");
                                                            }
                                                            var finishdownload = downloadtrackers.SelectToken("finishdownload");
                                                            if (CommonHelper.IsEventOccurring(0.75) && finishdownload != null)
                                                            {
                                                                await Task.Delay(CommonHelper.RandomRange(1000, 3000));
                                                                foreach (var tracker in finishdownload)
                                                                {
                                                                    var url = tracker.Value<string>();
                                                                    if (string.IsNullOrWhiteSpace(url))
                                                                        continue;
                                                                    url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                                                    await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                                                    LogWriteLine($"{bid_type}::downloadtrackers::startdownload[{task["id"]}]:{url}");
                                                                }

                                                                var startinstall = downloadtrackers.SelectToken("startinstall");
                                                                if (startinstall != null)
                                                                {
                                                                    foreach (var tracker in startinstall)
                                                                    {
                                                                        try
                                                                        {
                                                                            var url = tracker.Value<string>();
                                                                            if (string.IsNullOrWhiteSpace(url))
                                                                                continue;
                                                                            url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                                                            await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                                                            LogWriteLine($"{bid_type}::downloadtrackers::startinstall[{task["id"]}]:{url}");
                                                                        }
                                                                        catch (Exception)
                                                                        {


                                                                        }

                                                                    }
                                                                }

                                                                var finishinstall = downloadtrackers.SelectToken("finishinstall");
                                                                if (CommonHelper.IsEventOccurring(0.75) && finishinstall != null)
                                                                {
                                                                    foreach (var tracker in finishinstall)
                                                                    {
                                                                        try
                                                                        {
                                                                            var url = tracker.Value<string>();
                                                                            if (string.IsNullOrWhiteSpace(url))
                                                                                continue;
                                                                            url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                                                            await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url, TimeSpan.FromSeconds(15));
                                                                            LogWriteLine($"{bid_type}::downloadtrackers::finishinstall[{task["id"]}]:{url}");
                                                                        }
                                                                        catch (Exception)
                                                                        {


                                                                        }

                                                                    }
                                                                }


                                                            }
                                                        }
                                                    }
                                                }

                                                var conversionTrackers = admnative.SelectToken("link.conversionTrackers");
                                                if (conversionTrackers != null)
                                                {
                                                    foreach (var tracker in conversionTrackers)
                                                    {
                                                        try
                                                        {
                                                            var url = tracker.Value<string>();
                                                            url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                                            //CUPID_CCN
                                                            if (bid_action.Equals("OPEN_APP_DEEPLINK") || bid_action.Contains("DEEPLINK"))
                                                            {
                                                                url = url.Replace("CUPID_CCN", "20001");
                                                                url = url.Replace("__TARGET_APP_INSTALL__", "0");
                                                            }
                                                            await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url);
                                                            LogWriteLine($"{bid_type}::conversionTrackers[{task["id"]}]:{url}");
                                                        }
                                                        catch (Exception)
                                                        {

                                                        }
                                                    }
                                                }
                                            }


                                        }


                                        var firstQuartileTrackers = admnative!.SelectToken("link.firstQuartileTrackers");
                                        if (firstQuartileTrackers != null)
                                        {
                                            foreach (var tracker in firstQuartileTrackers)
                                            {
                                                try
                                                {
                                                    var url = tracker.Value<string>();
                                                    if (string.IsNullOrWhiteSpace(url))
                                                        continue;
                                                    url = url_macro_process(vast, bid, url, os, dev, realip, bid_type);
                                                    await CefLoadHelper.LoadUrlWithTimeoutAsync(browser, url);
                                                    LogWriteLine($"{bid_type}::firstQuartileTrackers[{task["id"]}]:{url}");
                                                }
                                                catch (Exception)
                                                {


                                                }

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
            }
            catch (Exception ex)
            {
                LogWriteLine(ex.Message);
            }
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