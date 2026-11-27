using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MQNetScan.Engine;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S7 端口探测与角色判定测试。
    /// 覆盖 PRD FR-1.3.1 ~ FR-1.3.4、FR-2.1 ~ FR-2.4 与 architecture §3.4。
    /// </summary>
    internal static class PortProberTests
    {
        public static void Run()
        {
            TestRolePriority();
            TestRoleResolverBasic();
            TestRoleResolverWithRealPorts();
            TestPortProbeAgainstRealListener();
            TestBlackholeDoesNotBlock();
            TestPortNormalization();
            TestSinglePortTester();
        }

        // -------------------------------------------------------------------
        // 角色优先级（PRD §7.2，内置固定）
        // -------------------------------------------------------------------
        private static void TestRolePriority()
        {
            Console.WriteLine();
            Console.WriteLine("[S7-1] 角色优先级（PRD FR-2.3，内置固定）");
            TestRunner.BeginCase("本地服务器 > Windows 电脑 > 打印机 > 电子秤 > 安卓POS > 网络设备 > 未知");

            int pServer = RolePriority.GetPriority(RolePriority.RoleLocalServer);
            int pCashier = RolePriority.GetPriority(RolePriority.RoleCashierPc);
            int pPrinter = RolePriority.GetPriority(RolePriority.RolePrinter);
            int pScale = RolePriority.GetPriority(RolePriority.RoleScale);
            int pPos = RolePriority.GetPriority(RolePriority.RoleAndroidPos);
            int pNet = RolePriority.GetPriority(RolePriority.RoleNetworkDevice);
            int pUnknown = RolePriority.GetPriority(RolePriority.RoleUnknown);

            TestRunner.AssertTrue(pServer < pCashier, "本地服务器 优先于 Windows 电脑");
            TestRunner.AssertTrue(pCashier < pPrinter, "Windows 电脑 优先于 打印机");
            TestRunner.AssertTrue(pPrinter < pScale, "打印机 优先于 电子秤");
            TestRunner.AssertTrue(pScale < pPos, "电子秤 优先于 安卓设备");
            TestRunner.AssertTrue(pPos < pNet, "安卓设备 优先于 网络设备");
            TestRunner.AssertTrue(pNet < pUnknown, "网络设备 优先于 未知");

            // 自定义角色排在内置之后
            int pCustom = RolePriority.GetPriority("自助点餐机");
            TestRunner.AssertEqual(RolePriority.Custom, pCustom, "自定义角色优先级为 Custom");
            TestRunner.AssertTrue(pCustom > pNet, "自定义角色 排在 网络设备 之后");
            TestRunner.AssertTrue(pCustom < pUnknown, "自定义角色 排在 未知 之前");
        }

        // -------------------------------------------------------------------
        // 角色判定基本用例
        // -------------------------------------------------------------------
        private static void TestRoleResolverBasic()
        {
            Console.WriteLine();
            Console.WriteLine("[S7-2] 角色判定（PRD FR-2.1 / FR-2.2）");
            TestRunner.BeginCase("多角色命中取最高优先级、无命中为未知");

            List<PortRule> rules = IniSchema.CreateDefaultPortRules();

            // 1433 单独 → 本地服务器
            TestRunner.AssertEqual("本地服务器",
                RoleResolver.Resolve(new int[] { 1433 }, rules), "仅 1433 → 本地服务器");

            // 1433 + 445 同时开 → 本地服务器（优先级更高）
            TestRunner.AssertEqual("本地服务器",
                RoleResolver.Resolve(new int[] { 1433, 445 }, rules),
                "1433+445 → 本地服务器（不是Windows 电脑）");

            // 445 + 3389 + 135 → Windows 电脑
            TestRunner.AssertEqual("Windows 电脑",
                RoleResolver.Resolve(new int[] { 445, 3389, 135 }, rules),
                "445+3389+135 → Windows 电脑");

            // 445 + 9100 → Windows 电脑（优先于打印机）
            TestRunner.AssertEqual("Windows 电脑",
                RoleResolver.Resolve(new int[] { 445, 9100 }, rules),
                "445+9100 → Windows 电脑（优先于打印机）");

            // 9100 + 515 + 631 → 打印机
            TestRunner.AssertEqual("打印机",
                RoleResolver.Resolve(new int[] { 9100, 515, 631 }, rules), "打印机三端口 → 打印机");

            // 3001 + 4001 → 电子秤
            TestRunner.AssertEqual("电子秤",
                RoleResolver.Resolve(new int[] { 3001, 4001 }, rules), "电子秤端口 → 电子秤");

            // 5555 → 安卓设备
            TestRunner.AssertEqual("安卓设备",
                RoleResolver.Resolve(new int[] { 5555 }, rules), "5555 → 安卓设备");

            // 9100 + 5555 → 打印机（优先于安卓设备）
            TestRunner.AssertEqual("打印机",
                RoleResolver.Resolve(new int[] { 9100, 5555 }, rules),
                "9100+5555 → 打印机（优先于安卓设备）");

            // 仅 80 → 网络设备
            TestRunner.AssertEqual("网络设备",
                RoleResolver.Resolve(new int[] { 80 }, rules), "仅 80 → 网络设备");

            // 80 + 443 → 网络设备
            TestRunner.AssertEqual("网络设备",
                RoleResolver.Resolve(new int[] { 80, 443 }, rules), "80+443 → 网络设备");

            // 无端口 → 未知
            TestRunner.AssertEqual("未知",
                RoleResolver.Resolve(new int[0], rules), "无开放端口 → 未知");

            // null → 未知
            TestRunner.AssertEqual("未知",
                RoleResolver.Resolve(null, rules), "null 端口 → 未知");

            // 未命中的端口 → 未知
            TestRunner.AssertEqual("未知",
                RoleResolver.Resolve(new int[] { 12345, 54321 }, rules), "无关端口 → 未知");

            // 自定义角色
            List<PortRule> custom = new List<PortRule>();
            custom.Add(new PortRule(8081, "自助点餐机", "自定义"));

            TestRunner.AssertEqual("自助点餐机",
                RoleResolver.Resolve(new int[] { 8081 }, custom), "自定义角色可正常判定");

            // 自定义 + 内置同时命中 → 内置优先
            custom.Add(new PortRule(1433, "本地服务器", "SQL"));
            TestRunner.AssertEqual("本地服务器",
                RoleResolver.Resolve(new int[] { 8081, 1433 }, custom),
                "自定义与内置同时命中 → 内置优先");
        }

        // -------------------------------------------------------------------
        // 用真实监听端口验证
        // -------------------------------------------------------------------
        private static void TestRoleResolverWithRealPorts()
        {
            Console.WriteLine();
            Console.WriteLine("[S7-3] 用真实监听端口验证端口扫描（PRD FR-1.3.3）");
            TestRunner.BeginCase("netstat 可见的监听端口应被探测为开放");

            // 在本机起两个监听器：一个打印机端口、一个电子秤端口
            TcpListener l1 = null;
            TcpListener l2 = null;

            try
            {
                // 端口可能已被其它程序（如假设备模拟器）占用，
                // 此时本用例无法构造受控环境，应跳过而不是崩溃。
                try
                {
                    l1 = new TcpListener(IPAddress.Any, 9100);
                    l1.Start();

                    l2 = new TcpListener(IPAddress.Any, 4001);
                    l2.Start();
                }
                catch (Exception ex)
                {
                    TestRunner.Skip("无法监听 9100/4001（可能被占用），跳过：" + ex.Message);
                    return;
                }

                Console.WriteLine("       已监听 9100（打印机）与 4001（电子秤）");

                uint self = GetSelfIp();

                if (self == 0)
                {
                    TestRunner.Skip("无法确定本机 IPv4 地址");
                    return;
                }

                // 探测这三个端口：9100 与 4001 应开放，9999 应不开放
                int[] ports = { 9100, 4001, 9999 };

                Stopwatch sw = Stopwatch.StartNew();
                List<PortProbeResult> results = PortProber.ProbePorts(self, ports, 800, null);
                sw.Stop();

                Console.WriteLine("       探测耗时 " + sw.ElapsedMilliseconds + " ms");

                PortStatus s9100 = FindStatus(results, 9100);
                PortStatus s4001 = FindStatus(results, 4001);
                PortStatus s9999 = FindStatus(results, 9999);

                Console.WriteLine("       9100 → " + s9100);
                Console.WriteLine("       4001 → " + s4001);
                Console.WriteLine("       9999 → " + s9999);

                TestRunner.AssertEqual(PortStatus.Open, s9100, "9100 探测为开放");
                TestRunner.AssertEqual(PortStatus.Open, s4001, "4001 探测为开放");
                TestRunner.AssertTrue(s9999 != PortStatus.Open, "9999 探测为非开放");

                // 角色：9100 + 4001 同时开 → 打印机（优先级高于电子秤）
                List<int> open = new List<int>();
                for (int i = 0; i < results.Count; i++)
                {
                    if (results[i].IsOpen) { open.Add(results[i].Port); }
                }

                int[] openArr = RoleResolver.NormalizePorts(open);
                Console.WriteLine("       开放端口: " + string.Join(", ", Array.ConvertAll(openArr, delegate(int v) { return v.ToString(); })));

                string role = RoleResolver.Resolve(openArr, IniSchema.CreateDefaultPortRules());
                TestRunner.AssertEqual("打印机", role, "9100+4001 → 打印机（优先于电子秤）");
            }
            finally
            {
                if (l1 != null) { try { l1.Stop(); } catch (Exception) { } }
                if (l2 != null) { try { l2.Stop(); } catch (Exception) { } }
            }
        }

        private static PortStatus FindStatus(List<PortProbeResult> results, int port)
        {
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Port == port) { return results[i].Status; }
            }

            return PortStatus.Unknown;
        }

        // -------------------------------------------------------------------
        // 真实监听器上验证开放/关闭区分
        // -------------------------------------------------------------------
        private static void TestPortProbeAgainstRealListener()
        {
            Console.WriteLine();
            Console.WriteLine("[S7-4] 开放与关闭端口的区分");
            TestRunner.BeginCase("区分 开放 / 拒绝 / 超时");

            TcpListener listener = null;

            try
            {
                try
                {
                    listener = new TcpListener(IPAddress.Any, 1433);
                    listener.Start();
                }
                catch (Exception ex)
                {
                    TestRunner.Skip("无法监听 1433（可能被占用），跳过：" + ex.Message);
                    return;
                }

                uint self = GetSelfIp();
                if (self == 0) { TestRunner.Skip("无法确定本机地址"); return; }

                // 1433 开放
                PortProbeResult open = PortProber.ProbeSingle(self, 1433, 500);
                TestRunner.AssertEqual(PortStatus.Open, open.Status, "1433 为开放");
                TestRunner.AssertTrue(open.IsOpen, "IsOpen 为 true");
                TestRunner.AssertEqual("", open.DescribeFailure(), "开放时无失败原因");

                // 一个很不可能被占用的端口 → 拒绝
                PortProbeResult closedPort = PortProber.ProbeSingle(self, 54321, 500);
                TestRunner.AssertTrue(closedPort.Status != PortStatus.Open,
                    "54321 非开放（实际 " + closedPort.Status + "）");
                TestRunner.AssertTrue(closedPort.DescribeFailure().Length > 0,
                    "非开放时给出失败原因：" + closedPort.DescribeFailure());
            }
            finally
            {
                if (listener != null) { try { listener.Stop(); } catch (Exception) { } }
            }
        }

        // -------------------------------------------------------------------
        // 黑洞地址不拖住线程
        // -------------------------------------------------------------------
        private static void TestBlackholeDoesNotBlock()
        {
            Console.WriteLine();
            Console.WriteLine("[S7-5] 黑洞地址不阻塞（PRD FR-1.3.4）");
            TestRunner.BeginCase("单 IP 总预算 = 超时×2，不会无限等待");

            int timeoutMs = 300;
            int[] ports = { 9100, 1433, 445, 3389, 135, 5555 };

            // 192.0.2.0/24 是 TEST-NET-1，保证不可达
            uint blackhole;
            Ipv4Util.TryParse("192.0.2.1", out blackhole);

            Stopwatch sw = Stopwatch.StartNew();
            List<PortProbeResult> results = PortProber.ProbePorts(blackhole, ports, timeoutMs, null);
            sw.Stop();

            long elapsed = sw.ElapsedMilliseconds;
            int budget = timeoutMs * 2;

            Console.WriteLine("       " + ports.Length + " 个端口耗时 " + elapsed
                + " ms（预算上限 " + budget + " ms）");

            // 允许一定余量（Select 粒度约 15.6ms，加上系统调度）
            TestRunner.AssertTrue(elapsed <= budget + 400,
                "在总预算内结束（" + elapsed + " <= " + (budget + 400) + "）");
            TestRunner.AssertEqual(ports.Length, results.Count, "每个端口都有结果");

            // 黑洞地址不应有任何开放端口
            bool anyOpen = false;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].IsOpen) { anyOpen = true; }
            }
            TestRunner.AssertTrue(!anyOpen, "黑洞地址无开放端口");
        }

        // -------------------------------------------------------------------
        private static void TestPortNormalization()
        {
            Console.WriteLine();
            Console.WriteLine("[S7-6] 端口规范化");
            TestRunner.BeginCase("去重 + 升序");

            List<int> raw = new List<int> { 9100, 1433, 9100, 445, 1433, 135 };
            int[] normalized = RoleResolver.NormalizePorts(raw);

            TestRunner.AssertEqual(4, normalized.Length, "去重后 4 个");
            TestRunner.AssertEqual(135, normalized[0], "第 1 个为 135");
            TestRunner.AssertEqual(445, normalized[1], "第 2 个为 445");
            TestRunner.AssertEqual(1433, normalized[2], "第 3 个为 1433");
            TestRunner.AssertEqual(9100, normalized[3], "第 4 个为 9100");

            // 空输入
            TestRunner.AssertEqual(0, RoleResolver.NormalizePorts(null).Length, "null → 空数组");
            TestRunner.AssertEqual(0, RoleResolver.NormalizePorts(new List<int>()).Length, "空列表 → 空数组");
        }

        // -------------------------------------------------------------------
        private static void TestSinglePortTester()
        {
            Console.WriteLine();
            Console.WriteLine("[S7-7] 单点测通器区分四类结果（PRD FR-3.2.2）");
            TestRunner.BeginCase("通 / 拒绝 / 超时 / DNS 失败");

            TcpListener listener = null;

            try
            {
                try
                {
                    listener = new TcpListener(IPAddress.Any, 5555);
                    listener.Start();
                }
                catch (Exception ex)
                {
                    TestRunner.Skip("无法监听 5555（可能被占用），跳过：" + ex.Message);
                    return;
                }

                // 1) 通
                SinglePortTester.Result ok = SinglePortTester.Test("127.0.0.1", 5555, 800);
                TestRunner.AssertTrue(ok.Resolved, "127.0.0.1 解析成功");
                TestRunner.AssertTrue(ok.IsOpen, "5555 为通（" + ok.Describe() + "）");

                // 2) 拒绝/超时（未监听端口）
                SinglePortTester.Result closed = SinglePortTester.Test("127.0.0.1", 54322, 500);
                TestRunner.AssertTrue(closed.Resolved, "解析成功");
                TestRunner.AssertTrue(!closed.IsOpen, "54322 不通");
                TestRunner.AssertTrue(closed.FailureReason.Length > 0,
                    "给出原因：" + closed.FailureReason);

                // 3) 黑洞地址
                SinglePortTester.Result black = SinglePortTester.Test("192.0.2.1", 80, 400);
                TestRunner.AssertTrue(!black.IsOpen, "黑洞地址不通");
                TestRunner.AssertTrue(black.FailureReason.IndexOf("超时") >= 0
                    || black.FailureReason.IndexOf("不可达") >= 0,
                    "原因为超时或不可达：" + black.FailureReason);

                // 4) DNS 失败
                SinglePortTester.Result dns = SinglePortTester.Test(
                    "this-domain-should-not-exist-mqnetscan.invalid", 80, 500);
                TestRunner.AssertTrue(!dns.Resolved, "不存在的域名解析失败");
                TestRunner.AssertTrue(dns.Describe().IndexOf("DNS") >= 0,
                    "说明 DNS 失败：" + dns.Describe());
            }
            finally
            {
                if (listener != null) { try { listener.Stop(); } catch (Exception) { } }
            }
        }

        // -------------------------------------------------------------------
        private static uint GetSelfIp()
        {
            List<AdapterInfo> adapters = NetworkAdapterService.GetActiveAdapters();

            if (adapters.Count == 0) { return 0; }

            return adapters[0].IpAddress;
        }
    }
}
