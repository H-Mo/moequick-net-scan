using System;
using System.Globalization;
using System.Net;
using System.Threading;
using MQNetScan.Engine;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;

namespace MQNetScan.Engine
{
    /// <summary>
    /// 基于 SendARP 的存活探测器。
    ///
    /// 设计约束（architecture §3.2 / §4.1）：
    ///   · **字节序**：destIp 必须按小端（主机字节序）构造，否则全部返回 67；
    ///   · **限流**：SendARP 是同步阻塞调用，全局并发硬上限 16，
    ///     即使用户把并发设为 128 也不会超过 16 路同时调用；
    ///   · **重试**：按参数重试，每次间隔 50ms；
    ///   · **无效 MAC 过滤**：全 FF（广播）或全 00 不算存活；
    ///   · 不抛异常：任何失败都折算为"无响应"。
    /// </summary>
    internal sealed class ArpProber : IHostProber
    {
        /// <summary>
        /// SendARP 同时调用数上限（architecture §3.2，v1.1 实测修正为 32）。
        ///
        /// 【为什么是 32 而不是 16 或更高】
        /// SendARP 对**无响应地址**需等待 ARP 重传超时，实测约 3~4 秒；
        /// 对**有响应地址**仅 0~35ms。因此扫描耗时几乎完全由无响应地址决定。
        ///
        /// 实测（32 个空地址）：
        ///   16 路 → 7393ms（平均 231ms/地址）
        ///   32 路 → 4180ms（平均 131ms/地址）
        ///   64 路 → 4186ms（与 32 路持平，说明系统侧已达饱和）
        ///
        /// 结论：32 是性价比拐点。再往上不再提速，只会徒增线程与句柄占用，
        /// 对配置较低的旧电脑不利。据此把原定的 16 上调为 32。
        /// </summary>
        public const int MaxConcurrentArpCalls = 32;

        /// <summary>重试间隔（毫秒）。</summary>
        public const int RetryIntervalMs = 50;

        private readonly Semaphore _semaphore;
        private readonly object _statLock = new object();

        private int _activeCalls;
        private int _maxObservedActiveCalls;
        private int _totalCalls;

        public ArpProber()
        {
            _semaphore = new Semaphore(MaxConcurrentArpCalls, MaxConcurrentArpCalls);
        }

        /// <summary>当前正在执行的 SendARP 调用数。</summary>
        public int ActiveCalls
        {
            get { lock (_statLock) { return _activeCalls; } }
        }

        /// <summary>观测到的最大同时调用数（用于验证限流是否生效）。</summary>
        public int MaxObservedActiveCalls
        {
            get { lock (_statLock) { return _maxObservedActiveCalls; } }
        }

        /// <summary>累计调用次数（含重试）。</summary>
        public int TotalCalls
        {
            get { lock (_statLock) { return _totalCalls; } }
        }

        /// <summary>
        /// 探测单个地址。
        /// 返回 null 表示无响应（不入列表）。
        /// </summary>
        public HostRecord Probe(uint address, ScanParameters parameters, Func<bool> shouldContinue)
        {
            int retry = parameters == null ? 1 : parameters.ArpRetry;
            if (retry < 1) { retry = 1; }

            string mac = null;

            for (int attempt = 0; attempt < retry; attempt++)
            {
                if (shouldContinue != null && !shouldContinue()) { return null; }

                mac = TrySendArp(address);

                if (mac != null) { break; }

                // 最后一次失败后不再等待
                if (attempt < retry - 1)
                {
                    // 分段休眠，便于及时响应终止
                    SleepInterruptible(RetryIntervalMs, shouldContinue);
                }
            }

            if (mac == null) { return null; }

            // 本阶段先只做存活判定，端口扫描在 S7 接入
            HostRecord record = new HostRecord(address, mac, new int[0]);
            return record;
        }

        /// <summary>
        /// 调用一次 SendARP。成功返回格式化的 MAC，失败返回 null。
        /// </summary>
        private string TrySendArp(uint address)
        {
            bool acquired = false;

            try
            {
                _semaphore.WaitOne();
                acquired = true;

                EnterCall();

                byte[] macBytes = new byte[6];
                uint macLength = 6;

                // 【关键】Ipv4Util 产出的 address 是"网络序整数值"（0xC0A87A01），
                // 而 SendARP 要求把网络序字节按**小端**解释（0x017AA8C0）。
                // 两者互为字节翻转，因此必须在这里显式转换。
                //
                // 若不转换，SendARP 对所有地址（**包括网关**）一律返回 67，
                // 表现为"扫不到任何设备，但 arp -a 明明有条目"。
                // 已实测验证，详见 architecture.md §4.1.1。
                uint destIp = ByteSwap32(address);

                int rc = NativeMethods.SendARP(destIp, 0, macBytes, ref macLength);

                if (rc != NativeMethods.NO_ERROR) { return null; }
                if (macLength != 6) { return null; }

                if (!IsValidMac(macBytes)) { return null; }

                return FormatMac(macBytes);
            }
            catch (Exception)
            {
                // 任何异常（DLL 缺失、权限、参数）都折算为无响应
                return null;
            }
            finally
            {
                if (acquired)
                {
                    ExitCall();
                    _semaphore.Release();
                }
            }
        }

        private void EnterCall()
        {
            lock (_statLock)
            {
                _activeCalls++;
                _totalCalls++;

                if (_activeCalls > _maxObservedActiveCalls)
                {
                    _maxObservedActiveCalls = _activeCalls;
                }
            }
        }

        private void ExitCall()
        {
            lock (_statLock)
            {
                _activeCalls--;
                if (_activeCalls < 0) { _activeCalls = 0; }
            }
        }

        /// <summary>
        /// 32 位字节翻转（网络序整数 ↔ SendARP 要求的小端整数）。
        ///
        /// 例：192.168.122.1
        ///   Ipv4Util 产出 0xC0A87A01（网络序）
        ///   翻转后       0x017AA8C0（SendARP 需要）
        /// </summary>
        public static uint ByteSwap32(uint value)
        {
            return ((value & 0x000000FFu) << 24)
                 | ((value & 0x0000FF00u) << 8)
                 | ((value & 0x00FF0000u) >> 8)
                 | ((value & 0xFF000000u) >> 24);
        }

        /// <summary>
        /// MAC 有效性判定：全 0xFF（广播）与全 0x00 均为无效回应。
        /// </summary>
        public static bool IsValidMac(byte[] mac)
        {
            if (mac == null || mac.Length != 6) { return false; }

            bool allZero = true;
            bool allOnes = true;

            for (int i = 0; i < 6; i++)
            {
                if (mac[i] != 0x00) { allZero = false; }
                if (mac[i] != 0xFF) { allOnes = false; }
            }

            return !allZero && !allOnes;
        }

        /// <summary>
        /// 格式化为大写冒号分段，如 52:54:00:63:FB:B8。
        /// </summary>
        public static string FormatMac(byte[] mac)
        {
            if (mac == null || mac.Length != 6) { return ""; }

            return mac[0].ToString("X2", CultureInfo.InvariantCulture) + ":"
                + mac[1].ToString("X2", CultureInfo.InvariantCulture) + ":"
                + mac[2].ToString("X2", CultureInfo.InvariantCulture) + ":"
                + mac[3].ToString("X2", CultureInfo.InvariantCulture) + ":"
                + mac[4].ToString("X2", CultureInfo.InvariantCulture) + ":"
                + mac[5].ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 可中断休眠。
        /// </summary>
        private static void SleepInterruptible(int totalMs, Func<bool> shouldContinue)
        {
            int slept = 0;

            while (slept < totalMs)
            {
                if (shouldContinue != null && !shouldContinue()) { return; }

                int step = 10;
                if (slept + step > totalMs) { step = totalMs - slept; }

                Thread.Sleep(step);
                slept += step;
            }
        }
    }
}
