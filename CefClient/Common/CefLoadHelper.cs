using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CefClient.Common
{
    using CefSharp;
    using CefSharp.WinForms;
    using System;
    using System.Threading.Tasks;

    public enum CefNavigationResult
    {
        Success,
        Timeout,
        Failed,
        BrowserDisposed,
        InvalidUrl
    }
    public static class CefLoadHelper
    {
        public static async Task<bool> LoadUrlWithTimeoutAsync(
        ChromiumWebBrowser browser,
        string url,
        int timeoutMilliseconds = 15000)
        {
            if (browser == null || browser.IsDisposed)
                return false;

            if (string.IsNullOrWhiteSpace(url))
                return false;

            var tcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            EventHandler<LoadingStateChangedEventArgs>? loadingHandler = null;

            loadingHandler = (sender, e) =>
            {
                // IsLoading == false 表示整体加载状态结束
                if (!e.IsLoading)
                {
                    tcs.TrySetResult(true);
                }
            };

            browser.LoadingStateChanged += loadingHandler;

            try
            {
                browser.Load(url);
                using var cts = new CancellationTokenSource(timeoutMilliseconds);
                await using (cts.Token.Register(() =>
                {
                    tcs.TrySetResult(false);
                }))
                {
                    bool loaded = await tcs.Task;
                    if (!loaded)
                    {
                        try
                        {
                            if (!browser.IsDisposed)
                            {
                                browser.Stop();
                            }
                        }
                        catch
                        {
                        }
                        return false;
                    }
                    return true;
                }
            }
            finally
            {
                browser.LoadingStateChanged -= loadingHandler;
            }
        }


        public static async Task<CefNavigationResult> LoadUrlWithTimeoutAsync(
            ChromiumWebBrowser browser,
            string url,
            TimeSpan timeout)
        {
            if (browser == null || browser.IsDisposed)
                return CefNavigationResult.BrowserDisposed;

            if (string.IsNullOrWhiteSpace(url))
                return CefNavigationResult.InvalidUrl;

            if (timeout <= TimeSpan.Zero)
                timeout = TimeSpan.FromSeconds(15);

            try
            {
                var waitTask = browser.WaitForNavigationAsync();

                browser.Load(url);

                var timeoutTask = Task.Delay(timeout);
                var finishedTask = await Task.WhenAny(waitTask, timeoutTask);

                if (finishedTask == timeoutTask)
                {
                    try
                    {
                        if (!browser.IsDisposed)
                        {
                            browser.Stop();
                            browser.Load("about:blank");
                        }
                    }
                    catch
                    {
                    }

                    return CefNavigationResult.Timeout;
                }

                await waitTask;
                return CefNavigationResult.Success;
            }
            catch
            {
                try
                {
                    if (!browser.IsDisposed)
                        browser.Stop();
                }
                catch
                {
                }

                return CefNavigationResult.Failed;
            }
        }
    }
}
