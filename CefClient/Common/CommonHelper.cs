using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CefClient.Common
{
    public class CommonHelper
    {
        public static long UnixTimeNow()
        {
            return new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds();
        }
        public static long UnixTimeNowSecond()
        {
            return new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();
        }
        public static long UnixTimeNow(DateTime dt)
        {
            return new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        }
        public static long UnixTimeNowSecond(DateTime dt)
        {
            return new DateTimeOffset(dt).ToUnixTimeSeconds();
        }
        public static string MD5Hash(string input)
        {
            using (var md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                StringBuilder sb = new StringBuilder(hashBytes.Length * 2);
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// 比如，要获取-1000~+1000范围的随机数，总的数量为2001个，这样就可以通过代码
        /// Random(Guid.NewGuid().GetHashCode()).Next()%2001 使得到的结果限制在0-2000范围，再减去1000, 结果就是-1000~+1000之间了。
        /// </summary>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public static int RandomRange(int min, int max)
        {
            return Random.Shared.Next(min, max);
        }

        /// <summary>
        /// 返回[min, max)之间的随机整数
        /// </summary>
        public static int NextInt(int min, int max)
        {
            return Random.Shared.Next(min, max);
        }
        public static Int64 NextInt64(Int64 min, Int64 max)
        {
            return Random.Shared.NextInt64(min, max);
        }


        public static double NextDouble()
        {
            return Random.Shared.NextDouble();
        }

        public static double NextDouble(double min, double max)
        {
            return min + Random.Shared.NextDouble() * (max - min);
        }

        /// <summary>
        /// 随机生成一个满足百分比的数字
        /// </summary>
        /// <param name="probability"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static bool IsEventOccurring(double probability)
        {
            if (probability < 0 || probability > 1)
                throw new ArgumentOutOfRangeException(nameof(probability), "Probability must be between 0 and 1");
            double randomValue = Random.Shared.NextDouble();
            return randomValue < probability;
        }

        public static HttpClient CreateSocks5HttpClient(string proxyAddress)
        {
            var handler = new SocketsHttpHandler
            {
                Proxy = new WebProxy($"{proxyAddress}"),
                UseProxy = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            };
            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
        }

        public static HttpClient CreateProxyHttpClient(string proxyAddress)
        {
            var handler = new HttpClientHandler
            {
                UseCookies = false,
                Proxy = new WebProxy(proxyAddress),
                UseProxy = true,
            };
            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
        }
    }
}
