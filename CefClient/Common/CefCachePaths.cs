
namespace CefClient.Common
{
    internal static class CefCachePaths
    {
        public static string RootCachePath { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "User Data");

        public static string GetConsumerRootCachePath(string consumerId)
        {
            //return Path.Combine(RootCachePath, "Consumers", consumerId);
            return System.IO.Path.Combine(new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory).Parent!.FullName, "chrome", "User Data", consumerId);
        }
        public static string GetBrowserCachePath(string browserId)
        {
            return Path.Combine(RootCachePath, browserId);
        }
    }
}
