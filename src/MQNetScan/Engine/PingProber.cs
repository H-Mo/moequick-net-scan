using System;
using System.Net.NetworkInformation;
using System.Threading;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;

namespace MQNetScan.Engine
{
    internal interface IIcmpPingSender
    {
        IPStatus Send(System.Net.IPAddress address, int timeoutMs);
    }

    internal sealed class SystemIcmpPingSender : IIcmpPingSender, IDisposable
    {
        private readonly Ping _ping = new Ping();
        public IPStatus Send(System.Net.IPAddress address, int timeoutMs)
        {
            PingReply reply = _ping.Send(address, timeoutMs);
            return reply == null ? IPStatus.Unknown : reply.Status;
        }
        public void Dispose() { _ping.Dispose(); }
    }

    /// <summary>
    /// 基于 ICMP Echo 的存活探测器.
    ///
    /// Ping 成功只证明目标回应了 ICMP，不提供 MAC 地址；成功主机以空 MAC
    /// 返回，交由 CompositeProber 继续进行端口扫描与角色辅助判定。
    /// </summary>
    internal sealed class PingProber : IHostProber
    {
        public const int MaxConcurrentPings = 64;

        private readonly Semaphore _semaphore = new Semaphore(MaxConcurrentPings, MaxConcurrentPings);
        private readonly Func<IIcmpPingSender> _senderFactory;

        public PingProber() : this(null) { }

        internal PingProber(Func<IIcmpPingSender> senderFactory)
        {
            _senderFactory = senderFactory;
        }

        public HostRecord Probe(uint address, ScanParameters parameters, Func<bool> shouldContinue)
        {
            int timeout = parameters == null ? 800 : parameters.PingTimeoutMs;
            if (timeout < 1) { timeout = 800; }
            if (timeout > 5000) { timeout = 5000; }

            int retry = parameters == null ? 1 : parameters.PingRetry;
            if (retry < 1) { retry = 1; }
            if (retry > 3) { retry = 3; }

            bool acquired = false;
            try
            {
                while (shouldContinue == null || shouldContinue())
                {
                    if (_semaphore.WaitOne(50, false))
                    {
                        acquired = true;
                        break;
                    }
                }

                if (!acquired || (shouldContinue != null && !shouldContinue())) { return null; }

                for (int attempt = 0; attempt < retry; attempt++)
                {
                    if (shouldContinue != null && !shouldContinue()) { return null; }

                    try
                    {
                        IIcmpPingSender sender = _senderFactory == null
                            ? (IIcmpPingSender)new SystemIcmpPingSender()
                            : _senderFactory();
                        IPStatus status;
                        try
                        {
                            status = sender.Send(NetworkAdapterService.UIntToIp(address), timeout);
                        }
                        finally
                        {
                            IDisposable disposable = sender as IDisposable;
                            if (disposable != null) { disposable.Dispose(); }
                        }

                        if (shouldContinue != null && !shouldContinue()) { return null; }
                        if (status == IPStatus.Success)
                        {
                            return new HostRecord(address, "", new int[0]);
                        }
                    }
                    catch (Exception)
                    {
                        // 单次 Ping 异常按失败处理，可由配置的下一次尝试恢复。
                    }

                    if (attempt + 1 < retry && !WaitInterruptible(80, shouldContinue)) { return null; }
                }

                return null;
            }
            finally
            {
                if (acquired) { _semaphore.Release(); }
            }
        }

        private static bool WaitInterruptible(int milliseconds, Func<bool> shouldContinue)
        {
            int remaining = milliseconds;
            while (remaining > 0)
            {
                if (shouldContinue != null && !shouldContinue()) { return false; }
                int slice = remaining > 20 ? 20 : remaining;
                Thread.Sleep(slice);
                remaining -= slice;
            }
            return shouldContinue == null || shouldContinue();
        }
    }
}
