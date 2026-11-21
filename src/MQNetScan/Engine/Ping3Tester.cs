using System;
using System.Collections.Generic;
using System.Diagnostics;
using MQNetScan.Engine.Contracts;

namespace MQNetScan.Engine
{
    /// <summary>
    /// 深度测速：对指定 IP:端口连续建立 3 次 TCP 连接，统计耗时。
    ///
    /// 用途（PRD FR-1.6 右键菜单）：
    ///   判断一个端口的"响应质量"，而不仅仅是"通不通"。
    ///   3 次中若有失败，如实记录为 -1，不伪造成功。
    ///
    /// 注意：本类同步阻塞，调用方必须在后台线程执行。
    /// </summary>
    internal static class Ping3Tester
    {
        /// <summary>默认测量次数（PRD 规定 3 次）。</summary>
        public const int AttemptCount = 3;

        /// <summary>
        /// 选择测速端口。
        ///
        /// 规则：
        ///   1. 优先用该主机第一个开放端口；
        ///   2. 无开放端口时，按 3389 → 445 → 80 的顺序挑第一个可连的；
        ///   3. 全都不通则用 3389（结果会如实显示 3 次失败）。
        /// </summary>
        public static int ChoosePort(int[] openPorts)
        {
            if (openPorts != null && openPorts.Length > 0)
            {
                return openPorts[0];
            }

            // 无开放端口：退化为常见端口探测
            return 3389;
        }

        /// <summary>
        /// 执行 3 次握手测速。
        /// </summary>
        /// <param name="ipAddress">目标 IP（uint）。</param>
        /// <param name="port">目标端口。</param>
        /// <param name="timeoutMs">单次超时（毫秒）。</param>
        /// <returns>测速结果（成功次数为 0 时 AverageMs 为 -1）。</returns>
        public static Ping3Result Run(uint ipAddress, int port, int timeoutMs)
        {
            if (timeoutMs < 1) { timeoutMs = 1; }

            int[] attempts = new int[AttemptCount];
            int success = 0;

            for (int i = 0; i < AttemptCount; i++)
            {
                Stopwatch sw = Stopwatch.StartNew();

                bool ok = false;

                try
                {
                    PortProbeResult r = PortProber.ProbeSingle(ipAddress, port, timeoutMs);
                    ok = r.IsOpen;
                }
                catch (Exception)
                {
                    ok = false;
                }

                sw.Stop();

                if (ok)
                {
                    attempts[i] = (int)sw.ElapsedMilliseconds;
                    success++;
                }
                else
                {
                    // 失败记为 -1，显示时会变成 "X"
                    attempts[i] = -1;
                }

                // 两次测量之间略作间隔，避免触发目标的连接限制
                if (i < AttemptCount - 1)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }

            return new Ping3Result(attempts, success, port);
        }

        /// <summary>
        /// 为某台主机挑选端口并执行测速。
        /// 若首个开放端口不可用，按 3389 → 445 → 80 依次尝试。
        /// </summary>
        public static Ping3Result RunWithFallback(uint ipAddress, int[] openPorts, int timeoutMs)
        {
            if (openPorts != null && openPorts.Length > 0)
            {
                return Run(ipAddress, openPorts[0], timeoutMs);
            }

            int[] candidates = new int[] { 3389, 445, 80 };

            for (int i = 0; i < candidates.Length; i++)
            {
                Ping3Result r = Run(ipAddress, candidates[i], timeoutMs);

                if (r.SuccessCount > 0) { return r; }
            }

            // 全部失败：返回最后一次结果（端口 80），如实反映"3 次均失败"
            return Run(ipAddress, 80, timeoutMs);
        }
    }
}
