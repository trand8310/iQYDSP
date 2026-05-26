using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MainClient
{
    using System;
    using System.Runtime.InteropServices;

    public static class BootTimeHelper
    {
        private const int CTL_KERN = 1;
        private const int KERN_BOOTTIME = 21;

        [StructLayout(LayoutKind.Sequential)]
        private struct TimeVal
        {
            public long tv_sec;
            public int tv_usec;
        }

        [DllImport("libc")]
        private static extern int sysctl(
            int[] name,
            uint namelen,
            IntPtr oldp,
            ref UIntPtr oldlenp,
            IntPtr newp,
            UIntPtr newlen
        );

        /// <summary>
        /// 获取系统启动时间 Unix 秒级时间戳
        /// </summary>
        public static long GetBootTimeSeconds()
        {
            int[] mib = { CTL_KERN, KERN_BOOTTIME };

            TimeVal boottime = new TimeVal();
            int size = Marshal.SizeOf<TimeVal>();

            IntPtr ptr = Marshal.AllocHGlobal(size);

            try
            {
                UIntPtr len = (UIntPtr)size;

                int ret = sysctl(
                    mib,
                    2,
                    ptr,
                    ref len,
                    IntPtr.Zero,
                    UIntPtr.Zero
                );

                if (ret != 0)
                {
                    return 0;
                }

                boottime = Marshal.PtrToStructure<TimeVal>(ptr);

                return boottime.tv_sec;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>
        /// 获取系统启动时间 Unix 秒级时间戳字符串
        /// </summary>
        public static string GetBootTimeSecondsString()
        {
            return GetBootTimeSeconds().ToString();
        }

        /// <summary>
        /// 获取系统启动时间 Unix 毫秒级时间戳
        /// </summary>
        public static long GetBootTimeMilliseconds()
        {
            long sec = GetBootTimeSeconds();

            if (sec <= 0)
            {
                return 0;
            }

            return sec * 1000L;
        }

        /// <summary>
        /// 获取系统已运行秒数
        /// </summary>
        public static long GetUptimeSeconds()
        {
            long bootSec = GetBootTimeSeconds();

            if (bootSec <= 0)
            {
                return 0;
            }

            long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            return nowSec - bootSec;
        }
    }
}
