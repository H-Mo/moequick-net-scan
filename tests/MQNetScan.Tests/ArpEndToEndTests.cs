using System;
using System.Collections.Generic;
using System.Diagnostics;
using MQNetScan.Engine;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S5 真实 ARP 端到端验证。
    ///
    /// 默认**不**在常规自测中运行：真实 ARP 对无响应地址要等 3~4 秒，
    /// 会显著拉长测试时间。需要时用参数显式触发：
    ///     MQNetScan.Tests.exe --arp-e2e
    /// </summary>
    internal static class ArpEndToEndTests
    {
        public static void Run(int maxAddresses)
        {
            Console.WriteLine();
            Console.WriteLine("[S5-E2E] 真实 ARP 端到端扫描");
            TestRunner.BeginCase("Session + ArpProber 协作，结果与 arp -a 对照");

            List<AdapterInfo> adapters = NetworkAdapterService.GetActiveAdapters();

            if (adapters.Count == 0)
            {
                TestRunner.Skip("无可枚举网卡");
                return;
            }

            AdapterInfo a = adapters[0];
            NetSegment seg = a.GetSegment();

            Console.WriteLine("       网卡: " + a.Description);
            Console.WriteLine("       本机: " + Ipv4Util.Format(a.IpAddress));
            Console.WriteLine("       网段: " + seg.Cidr + "（" + seg.HostCount + " 地址）");

            // 必须让扫描范围**包含本机地址**，否则"发现本机"的断言必然失败
            // （本机可能不在网段前 N 个地址里，例如本机是 .39 而只扫了 .1~.32）。
            uint[] all = seg.EnumerateHosts();
            uint[] targets = BuildTargetsIncluding(all, a.IpAddress, maxAddresses);

            Console.WriteLine("       实际扫描 " + targets.Length + " 个地址"
                + "（包含本机 " + Ipv4Util.Format(a.IpAddress) + "）");

            ScanParameters prm = new ScanParameters(targets, 32, 800, 1, new int[] { 9100 });
            ArpProber prober = new ArpProber();
            ScanSession session = new ScanSession(prm, null, prober);

            Stopwatch sw = Stopwatch.StartNew();
            session.Start();
            bool completed = session.WaitForCompletion(180000);
            sw.Stop();

            ScanProgress snap = session.Snapshot();

            Console.WriteLine("       耗时: " + sw.ElapsedMilliseconds + " ms");
            Console.WriteLine("       状态: " + snap.State + "，已探测 " + snap.Done + "/" + snap.Total);
            Console.WriteLine("       存活: " + snap.AliveCount + " 台");
            Console.WriteLine("       SendARP 累计 " + prober.TotalCalls
                + " 次，最大同时 " + prober.MaxObservedActiveCalls);

            TestRunner.AssertTrue(completed, "扫描在超时内完成");
            TestRunner.AssertEqual(ScanState.Completed, snap.State, "最终状态为 Completed");
            TestRunner.AssertEqual(targets.Length, snap.Done,
                "全部地址都被探测（" + targets.Length + "）");

            List<HostRecord> results = session.Results.TakeResults(0);
            int count = results == null ? 0 : results.Count;

            TestRunner.AssertEqual(snap.AliveCount, count, "结果条数与存活计数一致");
            TestRunner.AssertTrue(prober.MaxObservedActiveCalls <= ArpProber.MaxConcurrentArpCalls,
                "并发不超过上限（最大 " + prober.MaxObservedActiveCalls + "）");

            Console.WriteLine();
            Console.WriteLine("       === 发现设备 ===");

            bool foundSelf = false;

            if (results != null)
            {
                for (int i = 0; i < results.Count; i++)
                {
                    HostRecord r = results[i];
                    Console.WriteLine("         " + r.IpText + "  " + r.MacAddress
                        + "  OUI=" + r.OuiPrefix);

                    if (r.IpAddress == a.IpAddress) { foundSelf = true; }

                    TestRunner.AssertTrue(r.MacAddress.Length == 17,
                        r.IpText + " 的 MAC 格式正确");
                }
            }

            TestRunner.AssertTrue(foundSelf,
                "发现本机地址 " + Ipv4Util.Format(a.IpAddress));
        }

        /// <summary>
        /// 构造目标列表：取网段前 maxCount 个地址，但**确保包含本机地址与网关**。
        /// 若本机不在前 N 个里，就用本机附近的地址替换末尾若干个。
        /// </summary>
        private static uint[] BuildTargetsIncluding(uint[] allHosts, uint selfIp, int maxCount)
        {
            List<uint> picked = new List<uint>();
            int n = Math.Min(maxCount, allHosts.Length);

            for (int i = 0; i < n; i++)
            {
                picked.Add(allHosts[i]);
            }

            if (!picked.Contains(selfIp))
            {
                // 用本机替换最后一个，保证本机一定在范围内
                if (picked.Count == 0) { picked.Add(selfIp); }
                else { picked[picked.Count - 1] = selfIp; }
            }

            return picked.ToArray();
        }
    }
}
