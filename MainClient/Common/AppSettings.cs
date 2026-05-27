
namespace MainClient.Common
{
    public class AppSettings
    {
        /// <summary>
        /// 任务名称
        /// </summary>
        public string TaskName { get; set; }
        public string ProxyIpUrl { get; set; }
        public string TaskApiUrl { get; set; }
        public string UpdateApiUrl { get; set; }
        public string DevApiUrl { get; set; }


        /// <summary>
        /// 拉取任务间隔
        /// </summary>
        public int TaskPullIntervalMs { get; set; }
        /// <summary>
        /// 单UV执行间隔
        /// </summary>
        public int UvIntervalMs { get; set; }
        /// <summary>
        /// 并发数量
        /// </summary>
        public int MaxConcurrencyCount { get; set; }
        /// <summary>
        /// 页面加载超时(秒)
        /// </summary>
        public int PageLoadTimeout { get; set; }
        /// <summary>
        /// 隐藏模式
        /// </summary>
        public bool IsHiddenMode { get; set; }
        /// <summary>
        /// 代理模式
        /// </summary>
        public bool IsProxyMode { get; set; }
        /// <summary>
        /// 真实IP
        /// </summary>
        public bool IsRealIp { get; set; }

        /// <summary>
        /// IP有效时长
        /// </summary>
        public int IpValidityDuration { get; set; } = 60;

        /// <summary>
        /// 获取IP信息
        /// </summary>
        public bool GetIpInfo { get; set; }
        /// <summary>
        /// 主进程重置间隔秒数
        /// </summary>
        public int MainProcessResetIntervalMinutes { get; set; }

        /// <summary>
        /// 子进程重置间隔秒数
        /// </summary>
        public int ChildProcessResetIntervalMinutes { get; set; }
        /// <summary>
        /// 倍率
        /// </summary>
        public int Multiple { get; set; }
        public bool RealIp { get; set; }
        public int UsingDevIndex { get; set; }
        public bool IsDetailLog { get; set; }


    }
}
