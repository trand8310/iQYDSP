using MainClient.Common;
using Newtonsoft.Json;



namespace MainClient
{
    public static class UserConfigService
    {
        public static readonly string FilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.user.json");


        public static void Init(AppSettings appSettings)
        {
            if (!File.Exists(UserConfigService.FilePath))
            {
                appSettings.TaskPullIntervalMs = 1000;
                appSettings.UvIntervalMs = 1000;
                appSettings.MaxConcurrencyCount = 1;
                appSettings.MainProcessResetIntervalMinutes = 60;
                appSettings.ChildProcessResetIntervalMinutes = 15;
                appSettings.PageLoadTimeout = 30;
                appSettings.Multiple = 1;
                appSettings.IpValidityDuration = 60;
                appSettings.DevApiUrl = "http://117.21.200.18:9000/api/getdev.php";
                appSettings.TaskApiUrl = "http://117.21.200.19/client-v5.php";
                appSettings.IsHiddenMode = true;
                appSettings.IsProxyMode = true;
            }
        }

        public static void Save<T>(string sectionName, T value)
        {
            Dictionary<string, object> root;
            if (File.Exists(FilePath))
            {
                try
                {
                    root = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(FilePath)) ?? new();
                }
                catch
                {
                    root = new();
                }
            }
            else
            {
                root = new();
            }
            root[sectionName] = value;
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(root, Formatting.Indented));
        }
    }

}
