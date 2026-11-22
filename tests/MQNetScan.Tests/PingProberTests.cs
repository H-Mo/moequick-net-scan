using System;
using System.Net.NetworkInformation;
using System.Reflection;
using MQNetScan.Engine;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;

namespace MQNetScan.Tests
{
    internal static class PingProberTests
    {
        public static void Run()
        {
            TestPingReplyCreatesRecordWithoutMac();
            TestPingFailureReturnsNull();
            TestCompositeUsesPingAndStillScansPorts();
            TestPingModeDisablesArpOnlyEvidence();
            TestLegacyScanParametersDefaultToArp();
            TestPingRetryRecovers();
        }

        private static void TestPingReplyCreatesRecordWithoutMac()
        {
            Console.WriteLine();
            Console.WriteLine("[PING-1] ICMP 成功生成无 MAC 的主机记录");
            TestRunner.BeginCase("Ping 成功的主机保留，MAC 为空");
            PingProber p = new PingProber(delegate { return new FakePingSender(IPStatus.Success); });
            ScanParameters prm = CreateParameters(ProbeMode.Ping, 500, 1);
            HostRecord result = p.Probe(Parse("192.0.2.10"), prm, delegate { return true; });
            TestRunner.AssertTrue(result != null, "Ping 成功并保留主机");
            if (result != null)
            {
                TestRunner.AssertEqual("", result.MacAddress, "Ping 不提供 MAC");
                TestRunner.AssertEqual("192.0.2.10", result.IpText, "IP 正确");
            }
        }

        private static void TestPingFailureReturnsNull()
        {
            Console.WriteLine();
            Console.WriteLine("[PING-2] ICMP 失败不生成主机记录");
            TestRunner.BeginCase("超时或不可达返回 null");
            ScanParameters prm = CreateParameters(ProbeMode.Ping, 200, 1);
            PingProber timeout = new PingProber(delegate { return new FakePingSender(IPStatus.TimedOut); });
            TestRunner.AssertTrue(timeout.Probe(Parse("192.0.2.1"), prm, delegate { return true; }) == null,
                "超时返回 null");
            PingProber unreachable = new PingProber(delegate { return new FakePingSender(IPStatus.DestinationHostUnreachable); });
            TestRunner.AssertTrue(unreachable.Probe(Parse("192.0.2.2"), prm, delegate { return true; }) == null,
                "不可达返回 null");
        }

        private static void TestCompositeUsesPingAndStillScansPorts()
        {
            Console.WriteLine();
            Console.WriteLine("[PING-3] Ping 存活后继续端口扫描与角色判定");
            TestRunner.BeginCase("Ping 成功设备无 MAC，但继续探测开放端口");
            System.Net.Sockets.TcpListener listener = null;
            try
            {
                listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 19444);
                listener.Start();
            }
            catch (Exception ex)
            {
                TestRunner.Skip("端口 19444 不可用，跳过：" + ex.Message);
                return;
            }
            try
            {
                uint self = Parse("127.0.0.2");
                uint target = Parse("127.0.0.1");
                CompositeProber composite = new CompositeProber(
                    new ArpProber(), IniSchema.CreateDefaultRoles(), new ConflictDetector(), self, "",
                    ProbeMode.Ping, new PingProber(delegate { return new FakePingSender(IPStatus.Success); }));
                ScanParameters prm = new ScanParameters(new uint[] { target }, 1, 500, 1,
                    new int[] { 19444 }, ProbeMode.Ping, 500, 1);
                HostRecord record = composite.Probe(target, prm, delegate { return true; });
                TestRunner.AssertTrue(record != null, "Ping 成功后保留主机");
                if (record != null)
                {
                    TestRunner.AssertEqual("", record.MacAddress, "Ping 模式 MAC 为空");
                    TestRunner.AssertTrue(record.OpenPorts.Length == 1 && record.OpenPorts[0] == 19444,
                        "Ping 成功后仍继续端口扫描");
                }
            }
            finally { if (listener != null) { try { listener.Stop(); } catch (Exception) { } } }
        }

        private static void TestPingModeDisablesArpOnlyEvidence()
        {
            Console.WriteLine();
            Console.WriteLine("[PING-4] Ping 模式不调用 ARP 冲突证据");
            TestRunner.BeginCase("Ping 模式无 MAC 证据");
            FieldInfo f = typeof(CompositeProber).GetField("_hasMacEvidence", BindingFlags.Instance | BindingFlags.NonPublic);
            CompositeProber p = new CompositeProber(new ArpProber(), IniSchema.CreateDefaultRoles(),
                new ConflictDetector(), 0, "", ProbeMode.Ping,
                new PingProber(delegate { return new FakePingSender(IPStatus.Success); }));
            TestRunner.AssertEqual(false, (bool)f.GetValue(p), "Ping 模式不启用 ARP-only 检测");
        }

        private static void TestLegacyScanParametersDefaultToArp()
        {
            Console.WriteLine();
            Console.WriteLine("[PING-5] 旧 ScanParameters 构造默认 ARP");
            TestRunner.BeginCase("原有调用方行为不变");
            ScanParameters p = new ScanParameters(new uint[0], 16, 800, 1, new int[0]);
            TestRunner.AssertEqual(ProbeMode.Arp, p.ProbeMode, "默认 ARP");
            TestRunner.AssertEqual(800, p.PingTimeoutMs, "Ping 超时默认 800ms");
            TestRunner.AssertEqual(1, p.PingRetry, "Ping 尝试次数默认 1");
        }

        private static void TestPingRetryRecovers()
        {
            Console.WriteLine();
            Console.WriteLine("[PING-6] Ping 重试策略");
            TestRunner.BeginCase("首次超时、第二次成功时保留设备");
            int calls = 0;
            PingProber p = new PingProber(delegate
            {
                return new FakePingSender(delegate
                {
                    calls++;
                    return calls == 1 ? IPStatus.TimedOut : IPStatus.Success;
                });
            });
            ScanParameters prm = CreateParameters(ProbeMode.Ping, 200, 2);
            HostRecord record = p.Probe(Parse("192.0.2.20"), prm, delegate { return true; });
            TestRunner.AssertTrue(record != null, "第二次成功后保留设备");
            TestRunner.AssertEqual(2, calls, "恰好尝试两次");
            TestRunner.AssertEqual(2, prm.PingRetry, "参数保存两次尝试");
        }

        private static ScanParameters CreateParameters(ProbeMode mode, int timeout, int retry)
        {
            return new ScanParameters(new uint[0], 16, 800, 1, new int[0], mode, timeout, retry);
        }

        private static uint Parse(string ip)
        {
            uint value;
            Ipv4Util.TryParse(ip, out value);
            return value;
        }

        private sealed class FakePingSender : IIcmpPingSender
        {
            private readonly Func<IPStatus> _factory;
            public FakePingSender(IPStatus status) : this(delegate { return status; }) { }
            public FakePingSender(Func<IPStatus> factory) { _factory = factory; }
            public IPStatus Send(System.Net.IPAddress address, int timeoutMs) { return _factory(); }
        }
    }
}
