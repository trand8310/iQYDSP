using CefClient.Common;
using CefSharp;
using CefSharp.WinForms;
using System.Diagnostics;
using System.Text;

namespace CefClient
{
    public class Program
    {

        [STAThread]
        public static int Main(string[] args)
        {
            var consumerId = args
            .FirstOrDefault(x => x.StartsWith("--consumer-id=", StringComparison.OrdinalIgnoreCase))
            ?.Substring("--consumer-id=".Length);

            if (!string.IsNullOrWhiteSpace(consumerId))
            {
                CefCachePaths.RootCachePath = CefCachePaths.GetConsumerRootCachePath(consumerId);
            }

            ApplicationConfiguration.Initialize();
            //Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) =>
            {
                Debug.WriteLine("ThreadException");
                // TODO: 这里接你的日志
            };
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Debug.WriteLine("UnhandledException");
                // TODO: 这里接你的日志
            };
            AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
            {
                // 如无必要，不建议这里做重日志
            };
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                e.SetObserved();
            };

            CefSharpSettings.SubprocessExitIfParentProcessClosed = true;
            Cef.EnableWaitForBrowsersToClose();
            var settings = new CefSettings
            {
                RootCachePath = CefCachePaths.RootCachePath,
                PersistSessionCookies = false,
                //WindowlessRenderingEnabled = false,
                IgnoreCertificateErrors = true,
                LogSeverity = LogSeverity.Disable,
                UserAgent = "Mozilla/5.0 (Linux; Android 13; SM-G981B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/147.0.0.0 Mobile Safari/537.36",
            };
            settings.CefCommandLineArgs.Add("enable-media-stream");
            settings.CefCommandLineArgs.Add("use-fake-ui-for-media-stream");
            settings.CefCommandLineArgs.Add("enable-usermedia-screen-capturing");
            //settings.CefCommandLineArgs.Add("plugin-policy", "allow");
            Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null);
            Application.ApplicationExit += (sender, e) =>
            {
                if (Cef.IsInitialized ?? false)
                {
                    Cef.WaitForBrowsersToClose();
                    Cef.Shutdown();
                }
            };
            try
            {
                Application.Run(new MainForm());
            }
            finally
            {
                try
                {
                    if (Cef.IsInitialized == true)
                    {
                        Cef.WaitForBrowsersToClose();
                        Cef.Shutdown();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Cef shutdown error: {ex}");
                }
            }
            return 0;
        }
    }
}
