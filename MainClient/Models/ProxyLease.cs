using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MainClient.Models
{
    public sealed class ProxyLease
    {
        public string ProxyServer { get; set; } = string.Empty;
        public string RealIp { get; set; } = string.Empty;
        public JObject IpInfo { get; set; } = new JObject();

        public DateTime CreateTime { get; set; } = DateTime.Now;
        public DateTime ExpireTime { get; set; } = DateTime.Now;

        public int UseCount { get; set; }
        public int MaxUseCount { get; set; } = 1;

        public bool IsExpired(TimeSpan safeMargin)
        {
            return DateTime.Now >= ExpireTime - safeMargin;
        }

        public bool NeedRefresh(TimeSpan safeMargin)
        {
            if (string.IsNullOrWhiteSpace(ProxyServer))
                return true;

            if (IsExpired(safeMargin))
                return true;

            if (UseCount >= MaxUseCount)
                return true;

            return false;
        }
    }
}
