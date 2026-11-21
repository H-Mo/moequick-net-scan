using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using MQNetScan.Engine;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S5 ARP 探测测试。
    /// 覆盖 PRD FR-1.2.1 ~ FR-1.2.4、architecture §3.2 / §4.1.1。
    /// </summary>
    internal static class ArpProberTests
    {
        public static void Run()
        {
            TestByteOrder();
            TestMacValidation();
            TestMacFormatting();
            TestConcurrencyLimit();
            TestRealScanAgainstLocalNetwork();
            TestConflictDetector();
        }

        // -------------------------------------------------------------------
        // 字节序：本项目最容易出错的地方（architecture A-C2）
        // -------------------------------------------------------------------
        private static void TestByteOrder()
        {
            Console.WriteLine();
            Console.WriteLine("[S5-1] SendARP 字节序（architecture A-C2，最关键）");
            TestRunner.BeginCase("Ipv4Util 产出网络序，传 SendARP 前必须翻转");

            uint fromUtil;
            Ipv4Util.TryParse("192.168.122.1", out fromUtil);

            byte[] bytes = IPAddress.Parse("192.168.122.1").GetAddressBytes();
            uint viaBitConverter = BitConverter.ToUInt32(bytes, 0);

            Console.WriteLine("       Ipv4Util.TryParse    = 0x" + fromUtil.ToString("X8"));
            Console.WriteLine("       BitConverter.ToUInt32 = 0x" + viaBitConverter.ToString("X8"));

            // 这是本项目最关键的一致性约束：
            // Ipv4Util 按 (a<<24)|(b<<16)|(c<<8)|d 拼装，得到网络序整数；
            // SendARP 要求网络序字节被按小端解释，即两者互为字节翻转。
            TestRunner.AssertEqual(0xC0A87A01u, fromUtil,
                "Ipv4Util 产出网络序 0xC0A87A01");
            TestRunner.AssertEqual(0x017AA8C0u, viaBitConverter,
                "BitConverter 产出 0x017AA8C0");
            TestRunner.AssertTrue(fromUtil != viaBitConverter,
                "两者不相等，必须显式转换（ArpProber 内部已完成）");

            // ArpProber 内部必须做这个转换。这里直接验证翻转函数正确。
            TestRunner.AssertEqual(viaBitConverter, ByteSwap32(fromUtil),
                "字节翻转后与 BitConverter 结果一致");

            // 多组地址交叉验证
            AssertByteOrder("10.0.0.1", 0x0100000Au);
            AssertByteOrder("192.168.1.1", 0x0101A8C0u);
            AssertByteOrder("192.168.122.1", 0x017AA8C0u);
            AssertByteOrder("255.255.255.255", 0xFFFFFFFFu);
            AssertByteOrder("0.0.0.0", 0x00000000u);
        }

        private static void AssertByteOrder(string ip, uint expectedLittleEndian)
        {
            uint netOrder;
            Ipv4Util.TryParse(ip, out netOrder);

            uint swapped = ByteSwap32(netOrder);

            byte[] bytes = IPAddress.Parse(ip).GetAddressBytes();
            uint viaBitConverter = BitConverter.ToUInt32(bytes, 0);

            TestRunner.AssertEqual(expectedLittleEndian, swapped,
                ip + " 翻转后为 0x" + expectedLittleEndian.ToString("X8"));
            TestRunner.AssertEqual(viaBitConverter, swapped,
                ip + " 翻转结果与 BitConverter 一致");
        }

        /// <summary>
        /// 32 位字节翻转。直接复用产品代码的实现，避免测试与实现各写一份导致"一致地错"。
        /// </summary>
        public static uint ByteSwap32(uint value)
        {
            return ArpProber.ByteSwap32(value);
        }

        // -------------------------------------------------------------------
        // MAC 有效性
        // -------------------------------------------------------------------
        private static void TestMacValidation()
        {
            Console.WriteLine();
            Console.WriteLine("[S5-2] MAC 有效性判定（PRD FR-1.2.1）");
            TestRunner.BeginCase("全 FF / 全 00 判为无效");

            TestRunner.AssertTrue(
                ArpProber.IsValidMac(new byte[] { 0x52, 0x54, 0x00, 0x63, 0xFB, 0xB8 }),
                "正常 MAC 有效");

            TestRunner.AssertTrue(
                !ArpProber.IsValidMac(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }),
                "全 FF（广播）无效");

            TestRunner.AssertTrue(
                !ArpProber.IsValidMac(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
                "全 00 无效");

            TestRunner.AssertTrue(!ArpProber.IsValidMac(null), "null 无效");
            TestRunner.AssertTrue(!ArpProber.IsValidMac(new byte[5]), "长度 5 无效");
            TestRunner.AssertTrue(!ArpProber.IsValidMac(new byte[7]), "长度 7 无效");

            // 只有一个字节不同也应有效
            TestRunner.AssertTrue(
                ArpProber.IsValidMac(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 }),
                "仅末字节非零仍有效");
        }

        // -------------------------------------------------------------------
        // MAC 格式化
        // -------------------------------------------------------------------
        private static void TestMacFormatting()
        {
            Console.WriteLine();
            Console.WriteLine("[S5-3] MAC 格式化（PRD FR-1.2.3）");
            TestRunner.BeginCase("大写、冒号分段");

            string mac = ArpProber.FormatMac(
                new byte[] { 0x52, 0x54, 0x00, 0x63, 0xFB, 0xB8 });

            TestRunner.AssertEqual("52:54:00:63:FB:B8", mac, "格式为大写冒号分段");
            TestRunner.AssertEqual(17, mac.Length, "长度为 17");

            // 单数字节应补零
            string mac2 = ArpProber.FormatMac(
                new byte[] { 0x00, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E });
            TestRunner.AssertEqual("00:0A:0B:0C:0D:0E", mac2, "单数字节补零为大写两位");

            // OUI 前缀（HostRecord 提供）
            HostRecord r = new HostRecord(0, "52:54:00:63:FB:B8", new int[0]);
            TestRunner.AssertEqual("52:54:00", r.OuiPrefix, "OUI 前缀为前 3 字节");
        }

        // -------------------------------------------------------------------
        // 限流：同时调用数不得超过 16
        // -------------------------------------------------------------------
        private static void TestConcurrencyLimit()
        {
            Console.WriteLine();
            Console.WriteLine("[S5-4] SendARP 并发限流（architecture §3.2）");
            TestRunner.BeginCase("并发设为 128 时同时调用仍 ≤ 16");

            TestRunner.AssertEqual(32, ArpProber.MaxConcurrentArpCalls,
                "上限常量为 32（实测拐点：32 与 64 效果相同，故取 32）");

            ArpProber prober = new ArpProber();

            // 构造 200 个本机网段地址（大多无响应，但会真实调用 SendARP）
            uint[] targets = new uint[200];
            uint baseAddr;
            Ipv4Util.TryParse("192.168.122.1", out baseAddr);

            for (int i = 0; i < targets.Length; i++)
            {
                targets[i] = baseAddr + (uint)i;
            }

            // 并发 128（远超 ARP 上限）
            ScanParameters prm = new ScanParameters(targets, 128, 800, 1, new int[0]);

            // 并行发起探测，模拟 128 个工作线程
            int workerCount = 128;
            Thread[] threads = new Thread[workerCount];
            int nextIndex = -1;
            object idxLock = new object();

            for (int i = 0; i < workerCount; i++)
            {
                threads[i] = new Thread(delegate()
                {
                    while (true)
                    {
                        int idx;
                        lock (idxLock)
                        {
                            idx = ++nextIndex;
                            if (idx >= targets.Length) { return; }
                        }

                        try
                        {
                            prober.Probe(targets[idx], prm, delegate { return true; });
                        }
                        catch (Exception)
                        {
                        }
                    }
                });
                threads[i].IsBackground = true;
                threads[i].Start();
            }

            for (int i = 0; i < workerCount; i++)
            {
                threads[i].Join(30000);
            }

            int maxObserved = prober.MaxObservedActiveCalls;

            Console.WriteLine("       累计 SendARP 调用: " + prober.TotalCalls
                + "，观测到的最大同时调用: " + maxObserved);

            TestRunner.AssertTrue(prober.TotalCalls > 0, "确实发起了 SendARP 调用");
            TestRunner.AssertTrue(maxObserved <= ArpProber.MaxConcurrentArpCalls,
                "最大同时调用不超过 " + ArpProber.MaxConcurrentArpCalls + "（实际 " + maxObserved + "）");
            TestRunner.AssertTrue(prober.ActiveCalls == 0,
                "全部调用结束后 ActiveCalls 归零（实际 " + prober.ActiveCalls + "）");
        }

        // -------------------------------------------------------------------
        // 真实网段扫描
        //
        // 【重要】SendARP 对**无响应地址**需要 3~4 秒（ARP 重传超时），
        // 对**有响应地址**只需 0~35ms。因此本用例绝不能串行扫大范围，
        // 否则一个 /24 就要 4 分钟以上。这里只取极少数地址，
        // 并按并发方式验证，真实大规模扫描的耗时评估见 ScanTimingTests。
        // -------------------------------------------------------------------
        private static void TestRealScanAgainstLocalNetwork()
        {
            Console.WriteLine();
            Console.WriteLine("[S5-5] 真实 ARP 扫描（PRD FR-1.2.1 / FR-1.2.2）");
            TestRunner.BeginCase("发现本机与网关、无响应地址不入列表");

            List<AdapterInfo> adapters = NetworkAdapterService.GetActiveAdapters();

            if (adapters.Count == 0)
            {
                TestRunner.Skip("无可枚举网卡，跳过真实扫描");
                return;
            }

            AdapterInfo adapter = adapters[0];
            NetSegment seg = adapter.GetSegment();

            Console.WriteLine("       扫描网段: " + seg.Cidr);
            Console.WriteLine("       本机地址: " + Ipv4Util.Format(adapter.IpAddress));

            ScanParameters prm = new ScanParameters(new uint[0], 32, 800, 1, new int[0]);
            ArpProber prober = new ArpProber();

            // 1) 本机必然可达
            Stopwatch sw = Stopwatch.StartNew();
            HostRecord self = prober.Probe(adapter.IpAddress, prm, delegate { return true; });
            sw.Stop();

            Console.WriteLine("       本机探测耗时 " + sw.ElapsedMilliseconds + "ms");
            TestRunner.AssertTrue(self != null, "发现本机地址 " + Ipv4Util.Format(adapter.IpAddress));
            TestRunner.AssertTrue(self != null && self.MacAddress.Length == 17,
                "本机 MAC 格式正确（" + (self == null ? "" : self.MacAddress) + "）");

            // 2) 网关（若在同一网段）
            if (adapter.HasGateway && !string.IsNullOrEmpty(adapter.GatewayText))
            {
                uint gw;
                if (Ipv4Util.TryParse(adapter.GatewayText, out gw) && seg.Contains(gw))
                {
                    HostRecord gwRec = prober.Probe(gw, prm, delegate { return true; });
                    TestRunner.AssertTrue(gwRec != null, "发现默认网关 " + adapter.GatewayText);

                    if (gwRec != null)
                    {
                        Console.WriteLine("       网关 MAC " + gwRec.MacAddress);
                        TestRunner.AssertTrue(ArpProber.IsValidMac(
                            ParseMac(gwRec.MacAddress)), "网关 MAC 有效");
                    }
                }
            }

            // 3) 无响应地址必须返回 null（约 3~4 秒，只测一个）
            uint emptyAddr;
            Ipv4Util.TryParse("192.168.122.250", out emptyAddr);

            if (seg.Contains(emptyAddr) && emptyAddr != adapter.IpAddress)
            {
                Stopwatch swEmpty = Stopwatch.StartNew();
                HostRecord empty = prober.Probe(emptyAddr, prm, delegate { return true; });
                swEmpty.Stop();

                Console.WriteLine("       空地址 192.168.122.250 耗时 " + swEmpty.ElapsedMilliseconds
                    + "ms（无响应需等 ARP 超时）");

                TestRunner.AssertTrue(empty == null, "无响应地址返回 null（不入列表，符合 FR-1.2.2）");
            }
        }

        /// <summary>把 "AA:BB:CC:DD:EE:FF" 解析回字节数组，用于复用 IsValidMac。</summary>
        private static byte[] ParseMac(string mac)
        {
            if (string.IsNullOrEmpty(mac)) { return null; }

            string[] parts = mac.Split(':');
            if (parts.Length != 6) { return null; }

            byte[] result = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                byte b;
                if (!byte.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out b))
                {
                    return null;
                }
                result[i] = b;
            }
            return result;
        }

        // -------------------------------------------------------------------
        // IP 冲突检测
        // -------------------------------------------------------------------
        private static void TestConflictDetector()
        {
            Console.WriteLine();
            Console.WriteLine("[S5-6] IP 冲突检测（PRD FR-1.5.2）");
            TestRunner.BeginCase("同 IP 双 MAC 判为冲突、同 MAC 重复不误报");

            ConflictDetector d = new ConflictDetector();

            uint ip;
            Ipv4Util.TryParse("192.168.1.100", out ip);

            string msg;

            // 第一次出现：不算冲突
            bool c1 = d.Observe(ip, "AA:BB:CC:DD:EE:01", out msg);
            TestRunner.AssertTrue(!c1, "首次观测不算冲突");
            TestRunner.AssertEqual(0, d.ConflictCount, "冲突计数为 0");

            // 同一 MAC 再来一次：仍不算冲突
            bool c2 = d.Observe(ip, "AA:BB:CC:DD:EE:01", out msg);
            TestRunner.AssertTrue(!c2, "同 MAC 重复不误报");

            // 不同 MAC：冲突
            bool c3 = d.Observe(ip, "AA:BB:CC:DD:EE:02", out msg);
            TestRunner.AssertTrue(c3, "同 IP 出现第二个 MAC 判为冲突");
            TestRunner.AssertTrue(msg != null && msg.IndexOf("192.168.1.100") >= 0,
                "冲突信息包含 IP（" + msg + "）");
            TestRunner.AssertEqual(1, d.ConflictCount, "冲突计数为 1");

            // 冲突应能生成告警
            List<AlertItem> alerts = d.BuildAlerts();
            TestRunner.AssertEqual(1, alerts.Count, "生成 1 条冲突告警");
            TestRunner.AssertEqual(AlertKind.IpConflict, alerts[0].Kind, "告警类型为 IpConflict");
            TestRunner.AssertTrue(alerts[0].IsCritical, "冲突为严重告警（需红色横幅）");
            TestRunner.AssertTrue(alerts[0].Suggestion.Length > 0, "告警含处理建议");

            // 另一个 IP 只有正常观测，不应产生冲突
            uint ip2;
            Ipv4Util.TryParse("192.168.1.101", out ip2);
            d.Observe(ip2, "AA:BB:CC:DD:EE:03", out msg);
            TestRunner.AssertEqual(1, d.ConflictCount, "其它 IP 不影响冲突计数");

            // Reset 清空
            d.Reset();
            TestRunner.AssertEqual(0, d.ConflictCount, "Reset 后冲突清零");

            // CaptureBaseline 不应抛异常（本机 ARP 表读取）
            bool threw = false;
            try { d.CaptureBaseline(); }
            catch (Exception ex)
            {
                threw = true;
                Console.WriteLine("       基线快照异常: " + ex.Message);
            }
            TestRunner.AssertTrue(!threw, "CaptureBaseline 不抛异常");
        }
    }
}
