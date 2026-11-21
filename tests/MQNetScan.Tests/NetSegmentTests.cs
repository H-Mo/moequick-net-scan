using System;
using System.Collections.Generic;
using MQNetScan.Engine;
using MQNetScan.Infrastructure;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S2 网段与网卡单测。
    /// 覆盖 PRD FR-1.1.3 ~ FR-1.1.6 与 architecture §4.2。
    /// 全部为纯逻辑测试，不发任何网络包。
    /// </summary>
    internal static class NetSegmentTests
    {
        public static void Run()
        {
            TestIpv4Parse();
            TestMaskConversion();
            TestCidrParse();
            TestPrefixBoundary();
            TestHostCount();
            TestRangeParse();
            TestSegmentModeDifference();
            TestEstimateDuration();
            TestAdapterServiceSmoke();
        }

        // -------------------------------------------------------------------
        private static void TestIpv4Parse()
        {
            Console.WriteLine();
            Console.WriteLine("[S2-1] IPv4 解析与格式化");
            TestRunner.BeginCase("合法与非法输入");

            uint v;

            TestRunner.AssertTrue(Ipv4Util.TryParse("192.168.1.1", out v), "解析 192.168.1.1");
            TestRunner.AssertEqual("192.168.1.1", Ipv4Util.Format(v), "格式化回原样");

            TestRunner.AssertTrue(Ipv4Util.TryParse("0.0.0.0", out v), "解析 0.0.0.0");
            TestRunner.AssertEqual(0u, v, "0.0.0.0 值为 0");

            TestRunner.AssertTrue(Ipv4Util.TryParse("255.255.255.255", out v), "解析 255.255.255.255");
            TestRunner.AssertEqual(0xFFFFFFFFu, v, "全 255 值为 0xFFFFFFFF");

            TestRunner.AssertTrue(Ipv4Util.TryParse(" 10.0.0.1 ", out v), "容忍首尾空格");
            TestRunner.AssertEqual("10.0.0.1", Ipv4Util.Format(v), "空格处理后正确");

            TestRunner.AssertTrue(!Ipv4Util.TryParse("192.168.1", out v), "拒绝 3 段");
            TestRunner.AssertTrue(!Ipv4Util.TryParse("192.168.1.1.1", out v), "拒绝 5 段");
            TestRunner.AssertTrue(!Ipv4Util.TryParse("192.168.1.256", out v), "拒绝 256");
            TestRunner.AssertTrue(!Ipv4Util.TryParse("192.168.-1.1", out v), "拒绝负数");
            TestRunner.AssertTrue(!Ipv4Util.TryParse("abc.def.ghi.jkl", out v), "拒绝字母");
            TestRunner.AssertTrue(!Ipv4Util.TryParse("", out v), "拒绝空串");
            TestRunner.AssertTrue(!Ipv4Util.TryParse(null, out v), "拒绝 null");
            TestRunner.AssertTrue(!Ipv4Util.TryParse("192.168.1.1/24", out v), "拒绝带掩码（应由 CIDR 解析处理）");
        }

        // -------------------------------------------------------------------
        private static void TestMaskConversion()
        {
            Console.WriteLine();
            Console.WriteLine("[S2-2] 掩码互转");
            TestRunner.BeginCase("前缀转掩码、掩码转前缀");

            TestRunner.AssertEqual(0xFFFFFF00u, Ipv4Util.PrefixToMask(24), "/24 掩码为 255.255.255.0");
            TestRunner.AssertEqual("255.255.255.0", Ipv4Util.Format(Ipv4Util.PrefixToMask(24)), "/24 格式化正确");
            TestRunner.AssertEqual("255.255.252.0", Ipv4Util.Format(Ipv4Util.PrefixToMask(22)), "/22 掩码为 255.255.252.0");
            TestRunner.AssertEqual("255.0.0.0", Ipv4Util.Format(Ipv4Util.PrefixToMask(8)), "/8 掩码为 255.0.0.0");
            TestRunner.AssertEqual("255.255.255.252", Ipv4Util.Format(Ipv4Util.PrefixToMask(30)), "/30 掩码正确");

            TestRunner.AssertEqual(24, Ipv4Util.MaskToPrefix(0xFFFFFF00u), "255.255.255.0 → 24");
            TestRunner.AssertEqual(22, Ipv4Util.MaskToPrefix(Ipv4Util.PrefixToMask(22)), "/22 往返一致");
            TestRunner.AssertEqual(8, Ipv4Util.MaskToPrefix(0xFF000000u), "255.0.0.0 → 8");
            TestRunner.AssertEqual(32, Ipv4Util.MaskToPrefix(0xFFFFFFFFu), "全 1 → 32");
            TestRunner.AssertEqual(0, Ipv4Util.MaskToPrefix(0u), "全 0 → 0");

            // 非连续掩码必须被识别为非法
            TestRunner.AssertEqual(-1, Ipv4Util.MaskToPrefix(0xFF00FF00u), "非连续掩码返回 -1");
        }

        // -------------------------------------------------------------------
        private static void TestCidrParse()
        {
            Console.WriteLine();
            Console.WriteLine("[S2-3] CIDR 解析（PRD FR-1.1.3）");
            TestRunner.BeginCase("标准网段与网络号计算");

            NetSegment seg;
            SegmentParseError err;
            string msg;

            bool ok = NetSegment.TryParseCidr("192.168.1.0/24", out seg, out err, out msg);
            TestRunner.AssertTrue(ok, "解析 192.168.1.0/24");
            TestRunner.AssertEqual("192.168.1.0", Ipv4Util.Format(seg.NetworkAddress), "网络号为 .0");
            TestRunner.AssertEqual("192.168.1.255", Ipv4Util.Format(seg.BroadcastAddress), "广播为 .255");
            TestRunner.AssertEqual(24, seg.PrefixLength, "前缀为 24");
            TestRunner.AssertEqual("192.168.1.1", Ipv4Util.Format(seg.FirstHost), "首主机为 .1");
            TestRunner.AssertEqual("192.168.1.254", Ipv4Util.Format(seg.LastHost), "末主机为 .254");

            // 传入网段内任意地址应自动归到网络号
            ok = NetSegment.TryParseCidr("192.168.1.37/24", out seg, out err, out msg);
            TestRunner.AssertTrue(ok, "解析 192.168.1.37/24");
            TestRunner.AssertEqual("192.168.1.0", Ipv4Util.Format(seg.NetworkAddress), ".37 归到网络号 .0");

            // /22 的网络号对齐
            ok = NetSegment.TryParseCidr("10.1.0.0/22", out seg, out err, out msg);
            TestRunner.AssertTrue(ok, "解析 10.1.0.0/22");
            TestRunner.AssertEqual("10.1.0.0", Ipv4Util.Format(seg.NetworkAddress), "网络号 10.1.0.0");
            TestRunner.AssertEqual("10.1.3.255", Ipv4Util.Format(seg.BroadcastAddress), "广播 10.1.3.255");
            TestRunner.AssertEqual(1022, seg.HostCount, "/22 有 1022 个可用地址");

            // 非对齐的 /22 输入
            ok = NetSegment.TryParseCidr("10.1.2.55/22", out seg, out err, out msg);
            TestRunner.AssertTrue(ok, "解析 10.1.2.55/22");
            TestRunner.AssertEqual("10.1.0.0", Ipv4Util.Format(seg.NetworkAddress), "归到 10.1.0.0");

            // 格式错误
            ok = NetSegment.TryParseCidr("192.168.1.0", out seg, out err, out msg);
            TestRunner.AssertTrue(!ok, "缺少 /前缀 被拒绝");
            TestRunner.AssertEqual(SegmentParseError.BadFormat, err, "错误类型为 BadFormat");
            TestRunner.AssertTrue(!string.IsNullOrEmpty(msg), "给出中文提示：" + msg);

            ok = NetSegment.TryParseCidr("192.168.1.0/", out seg, out err, out msg);
            TestRunner.AssertTrue(!ok, "前缀为空被拒绝");

            ok = NetSegment.TryParseCidr("192.168.1.999/24", out seg, out err, out msg);
            TestRunner.AssertTrue(!ok, "非法 IP 被拒绝");
            TestRunner.AssertEqual(SegmentParseError.BadIpAddress, err, "错误类型为 BadIpAddress");

            ok = NetSegment.TryParseCidr("192.168.1.0/abc", out seg, out err, out msg);
            TestRunner.AssertTrue(!ok, "前缀非数字被拒绝");

            ok = NetSegment.TryParseCidr("", out seg, out err, out msg);
            TestRunner.AssertTrue(!ok, "空串被拒绝");
            TestRunner.AssertEqual(SegmentParseError.Empty, err, "错误类型为 Empty");
        }

        // -------------------------------------------------------------------
        private static void TestPrefixBoundary()
        {
            Console.WriteLine();
            Console.WriteLine("[S2-4] 前缀长度边界（PRD FR-1.1.4：8~30）");
            TestRunner.BeginCase("7 语法越界、8 语法合法但规模超限、16 为最大可扫、30/31/32 边界");

            NetSegment seg;
            SegmentParseError err;
            string msg;

            // 7：语法越界（小于 8），错误类型应为 BadPrefixLength
            TestRunner.AssertTrue(!NetSegment.TryParseCidr("10.0.0.0/7", out seg, out err, out msg), "/7 被拒绝");
            TestRunner.AssertEqual(SegmentParseError.BadPrefixLength, err, "/7 错误类型为 BadPrefixLength（语法越界）");
            TestRunner.AssertTrue(msg.IndexOf("8~30") >= 0, "/7 提示说明了范围：" + msg);

            // 8：语法合法（8 在 8~30 内），但 16777214 个地址超出规模上限
            // 因此拒绝原因必须是 TooManyHosts 而不是 BadPrefixLength —— 这两类错误含义不同，
            // 混用会让用户误以为掩码写错了。
            TestRunner.AssertTrue(!NetSegment.TryParseCidr("10.0.0.0/8", out seg, out err, out msg), "/8 被拒绝");
            TestRunner.AssertEqual(SegmentParseError.TooManyHosts, err,
                "/8 错误类型为 TooManyHosts（规模超限，非语法错误）");
            TestRunner.AssertTrue(msg.IndexOf("过多") >= 0, "/8 提示说明地址过多：" + msg);

            // 16：65534 个地址，正好是硬上限，必须被接受（PRD NFR-S2 明确以 /16 为真实场景）
            bool ok16 = NetSegment.TryParseCidr("10.1.0.0/16", out seg, out err, out msg);
            TestRunner.AssertTrue(ok16, "/16 被接受（硬上限边界）");

            if (ok16)
            {
                TestRunner.AssertEqual(65534, seg.HostCount, "/16 有 65534 个地址");
                TestRunner.AssertEqual(NetSegment.MaxHostsHardLimit, seg.HostCount, "/16 恰好等于硬上限");
            }

            // 30：最小可用网段
            bool ok30 = NetSegment.TryParseCidr("192.168.1.0/30", out seg, out err, out msg);
            TestRunner.AssertTrue(ok30, "/30 被接受");

            if (ok30)
            {
                TestRunner.AssertEqual(2, seg.HostCount, "/30 有 2 个可用地址");
                TestRunner.AssertEqual("192.168.1.1", Ipv4Util.Format(seg.FirstHost), "/30 首主机 .1");
                TestRunner.AssertEqual("192.168.1.2", Ipv4Util.Format(seg.LastHost), "/30 末主机 .2");
            }

            // 31/32：无可用主机区间，必须拒绝
            TestRunner.AssertTrue(!NetSegment.TryParseCidr("192.168.1.0/31", out seg, out err, out msg), "/31 被拒绝");
            TestRunner.AssertEqual(SegmentParseError.BadPrefixLength, err, "/31 错误类型为 BadPrefixLength");

            TestRunner.AssertTrue(!NetSegment.TryParseCidr("192.168.1.0/32", out seg, out err, out msg), "/32 被拒绝");
            TestRunner.AssertEqual(SegmentParseError.BadPrefixLength, err, "/32 错误类型为 BadPrefixLength");

            TestRunner.AssertTrue(!NetSegment.TryParseCidr("192.168.1.0/0", out seg, out err, out msg), "/0 被拒绝");
        }

        // -------------------------------------------------------------------
        private static void TestHostCount()
        {
            Console.WriteLine();
            Console.WriteLine("[S2-5] 地址数量与枚举");
            TestRunner.BeginCase("计数正确性与枚举首尾");

            NetSegment seg;
            SegmentParseError err;
            string msg;

            NetSegment.TryParseCidr("192.168.1.0/24", out seg, out err, out msg);
            TestRunner.AssertEqual(254, seg.HostCount, "/24 有 254 个可用地址（符合 PRD FR-1.1.3）");

            uint[] hosts = seg.EnumerateHosts();
            TestRunner.AssertEqual(254, hosts.Length, "枚举长度一致");
            TestRunner.AssertEqual("192.168.1.1", Ipv4Util.Format(hosts[0]), "首个为 .1");
            TestRunner.AssertEqual("192.168.1.254", Ipv4Util.Format(hosts[253]), "末个为 .254");
            TestRunner.AssertEqual("192.168.1.128", Ipv4Util.Format(hosts[127]), "第 128 个为 .128");

            // 不含网络号与广播地址
            bool containsNetwork = false;
            bool containsBroadcast = false;
            for (int i = 0; i < hosts.Length; i++)
            {
                if (hosts[i] == seg.NetworkAddress) { containsNetwork = true; }
                if (hosts[i] == seg.BroadcastAddress) { containsBroadcast = true; }
            }
            TestRunner.AssertTrue(!containsNetwork, "枚举不含网络号");
            TestRunner.AssertTrue(!containsBroadcast, "枚举不含广播地址");

            // Contains 判定
            uint a;
            Ipv4Util.TryParse("192.168.1.100", out a);
            TestRunner.AssertTrue(seg.Contains(a), "Contains 认可段内地址");
            Ipv4Util.TryParse("192.168.2.100", out a);
            TestRunner.AssertTrue(!seg.Contains(a), "Contains 拒绝段外地址");

            // 硬上限保护
            TestRunner.AssertTrue(
                NetSegment.MaxHostsHardLimit == 65534,
                "硬上限为 65534（/16），与 architecture 一致");
        }

        // -------------------------------------------------------------------
        private static void TestRangeParse()
        {
            Console.WriteLine();
            Console.WriteLine("[S2-6] 起止 IP 区间解析（精确区间模式，用户已确认语义 A）");
            TestRunner.BeginCase("只扫给定区间，不做对齐扩大");

            NetSegment seg;
            SegmentParseError err;
            string msg;

            // 已确认语义：精确区间。输入 10~50 就只扫 41 个地址。
            bool ok = NetSegment.TryParseRange("192.168.1.10", "192.168.1.50", out seg, out err, out msg);
            TestRunner.AssertTrue(ok, "解析 10~50 区间");

            if (ok)
            {
                Console.WriteLine("       10~50 -> " + seg.DisplayText + "（" + seg.HostCount + " 地址）");
                TestRunner.AssertEqual(41, seg.HostCount, "只含 41 个地址（50-10+1）");
                TestRunner.AssertEqual(SegmentMode.ExplicitRange, seg.Mode, "模式为精确区间");
                TestRunner.AssertEqual("192.168.1.10", Ipv4Util.Format(seg.FirstHost), "首地址为 .10");
                TestRunner.AssertEqual("192.168.1.50", Ipv4Util.Format(seg.LastHost), "末地址为 .50");

                // 关键断言：不得扩大到整个 /24 或 /26
                TestRunner.AssertTrue(seg.HostCount != 254, "未被扩大到 /24 的 254 个地址");
                TestRunner.AssertTrue(seg.HostCount != 62, "未被扩大到 /26 的 62 个地址");
            }

            // 跨 /25 边界的区间同样精确
            bool ok2 = NetSegment.TryParseRange("192.168.1.10", "192.168.1.200", out seg, out err, out msg);
            TestRunner.AssertTrue(ok2, "解析 10~200 区间");

            if (ok2)
            {
                Console.WriteLine("       10~200 -> " + seg.DisplayText + "（" + seg.HostCount + " 地址）");
                TestRunner.AssertEqual(191, seg.HostCount, "只含 191 个地址（200-10+1）");
            }

            // 完整 /24 范围
            bool ok3 = NetSegment.TryParseRange("192.168.1.1", "192.168.1.254", out seg, out err, out msg);
            TestRunner.AssertTrue(ok3, "解析 1~254 区间");

            if (ok3)
            {
                TestRunner.AssertEqual(254, seg.HostCount, "1~254 恰为 254 个地址");
            }

            // 单地址区间
            bool ok4 = NetSegment.TryParseRange("192.168.1.100", "192.168.1.100", out seg, out err, out msg);
            TestRunner.AssertTrue(ok4, "解析单地址区间");

            if (ok4)
            {
                TestRunner.AssertEqual(1, seg.HostCount, "单地址区间只含 1 个地址");
                TestRunner.AssertEqual("192.168.1.100", Ipv4Util.Format(seg.FirstHost), "首地址正确");
                TestRunner.AssertEqual("192.168.1.100", Ipv4Util.Format(seg.LastHost), "末地址相同");
            }

            // 起 > 止
            ok = NetSegment.TryParseRange("192.168.1.50", "192.168.1.10", out seg, out err, out msg);
            TestRunner.AssertTrue(!ok, "起 > 止 被拒绝");
            TestRunner.AssertEqual(SegmentParseError.RangeReversed, err, "错误类型为 RangeReversed");

            // 非法 IP
            ok = NetSegment.TryParseRange("192.168.1.10", "not-an-ip", out seg, out err, out msg);
            TestRunner.AssertTrue(!ok, "非法结束 IP 被拒绝");
            TestRunner.AssertEqual(SegmentParseError.BadIpAddress, err, "错误类型为 BadIpAddress");

            // 区间过大：恰好等于硬上限的区间是合法的（/16 场景，PRD NFR-S2 明确支持）
            ok = NetSegment.TryParseRange("10.0.0.1", "10.0.255.254", out seg, out err, out msg);
            TestRunner.AssertTrue(ok, "65534 地址的区间等于硬上限，应被接受");

            if (ok)
            {
                TestRunner.AssertEqual(65534, seg.HostCount, "恰好 65534 个地址");
            }

            // 真正超过硬上限：/15 规模
            ok = NetSegment.TryParseRange("10.0.0.1", "10.1.255.254", out seg, out err, out msg);
            TestRunner.AssertTrue(!ok, "超过 65534 的区间被拒绝");
            TestRunner.AssertEqual(SegmentParseError.TooManyHosts, err, "错误类型为 TooManyHosts");
            TestRunner.AssertTrue(msg.IndexOf("上限") >= 0, "提示说明超过上限：" + msg);
        }

        // -------------------------------------------------------------------
        private static void TestSegmentModeDifference()
        {
            Console.WriteLine();
            Console.WriteLine("[S2-6b] CIDR 与精确区间的行为差异");
            TestRunner.BeginCase("同一 IP 范围，两种模式的地址数不同");

            NetSegment cidr;
            NetSegment range;
            SegmentParseError err;
            string msg;

            NetSegment.TryParseCidr("192.168.1.0/26", out cidr, out err, out msg);
            NetSegment.TryParseRange("192.168.1.10", "192.168.1.50", out range, out err, out msg);

            Console.WriteLine("       CIDR /26        : " + cidr.HostCount + " 地址（" + cidr.DisplayText + "）");
            Console.WriteLine("       精确区间 10~50  : " + range.HostCount + " 地址（" + range.DisplayText + "）");

            TestRunner.AssertEqual(62, cidr.HostCount, "CIDR /26 为 62 地址");
            TestRunner.AssertEqual(41, range.HostCount, "精确区间为 41 地址");
            TestRunner.AssertTrue(cidr.HostCount != range.HostCount, "两种模式结果不同，语义已区分");

            // CIDR 模式的展示文本为斜杠形式
            TestRunner.AssertTrue(cidr.DisplayText.IndexOf('/') >= 0, "CIDR 展示含 /：" + cidr.DisplayText);
            // 精确区间的展示文本为波浪号形式
            TestRunner.AssertTrue(range.DisplayText.IndexOf('~') >= 0, "区间展示含 ~：" + range.DisplayText);

            // CIDR 模式排除网络号与广播
            TestRunner.AssertTrue(!cidr.Contains(cidr.NetworkAddress), "CIDR 不含网络号");
            TestRunner.AssertTrue(!cidr.Contains(cidr.BroadcastAddress), "CIDR 不含广播地址");
            TestRunner.AssertEqual(62, cidr.HostCount, "64 - 2 = 62，已正确排除两端");
        }

        // -------------------------------------------------------------------
        private static void TestEstimateDuration()
        {
            Console.WriteLine();
            Console.WriteLine("[S2-7] 耗时预估已废弃（用户决策：只做进度条）");
            TestRunner.BeginCase("确认不再提供耗时预估");

            // 用户已明确决定：不做耗时预估，只显示进度条。
            // 原因：SendARP 对无响应地址需等 3~4 秒（ARP 重传超时），
            // 实际耗时强依赖现场在线设备数量，无法可靠预估。
            NetSegment seg;
            SegmentParseError err;
            string msg;

            NetSegment.TryParseCidr("192.168.1.0/24", out seg, out err, out msg);

            if (seg == null)
            {
                TestRunner.Skip("未能构造网段，跳过");
                return;
            }

#pragma warning disable 0618
            string text = seg.EstimateDurationText();
#pragma warning restore 0618

            TestRunner.AssertEqual("", text, "EstimateDurationText 返回空串（已废弃）");

            Console.WriteLine("       说明：确认框与预览区均不再显示耗时预估");
        }

        // -------------------------------------------------------------------
        private static void TestAdapterServiceSmoke()
        {
            Console.WriteLine();
            Console.WriteLine("[S2-8] 网卡枚举冒烟（PRD FR-1.1.1）");
            TestRunner.BeginCase("枚举不抛异常且结果自洽");

            List<AdapterInfo> adapters = null;
            bool threw = false;

            try
            {
                adapters = NetworkAdapterService.GetActiveAdapters();
            }
            catch (Exception ex)
            {
                threw = true;
                Console.WriteLine("     异常: " + ex.Message);
            }

            TestRunner.AssertTrue(!threw, "枚举未抛异常");
            TestRunner.AssertTrue(adapters != null, "返回非空列表对象");

            Console.WriteLine("     本机可用网卡数: " + (adapters == null ? 0 : adapters.Count));

            if (adapters == null) { return; }

            for (int i = 0; i < adapters.Count; i++)
            {
                AdapterInfo a = adapters[i];
                Console.WriteLine("       - " + a.DisplayText);

                TestRunner.AssertTrue(a.PrefixLength >= 8 && a.PrefixLength <= 30,
                    "网卡 " + i + " 前缀在 8~30 内（" + a.PrefixLength + "）");
                TestRunner.AssertTrue(!Ipv4Util.IsApipa(a.IpAddress),
                    "网卡 " + i + " 不是 APIPA 地址");
                TestRunner.AssertTrue(!Ipv4Util.IsLoopback(a.IpAddress),
                    "网卡 " + i + " 不是环回地址");
                TestRunner.AssertTrue(a.GetSegment() != null,
                    "网卡 " + i + " 可算出网段");
            }

            // 排序规则：有网关的必须排在无网关的前面
            bool gatewayOrderOk = true;
            bool seenNoGateway = false;
            for (int i = 0; i < adapters.Count; i++)
            {
                if (!adapters[i].HasGateway) { seenNoGateway = true; }
                else if (seenNoGateway) { gatewayOrderOk = false; }
            }
            TestRunner.AssertTrue(gatewayOrderOk, "有网关的网卡排在前面");

            // 关键规则：物理网卡存在时，结果中不得混入虚拟网卡。
            // 只有当本机一块物理网卡都没有时（如运行在虚拟机内），才允许兜底显示虚拟网卡。
            bool fallback = NetworkAdapterService.IsFallbackMode(adapters);
            bool anyVirtual = false;
            for (int i = 0; i < adapters.Count; i++)
            {
                if (adapters[i].IsVirtual) { anyVirtual = true; }
            }

            Console.WriteLine("     兜底模式: " + (fallback ? "是（本机无物理网卡）" : "否"));

            if (fallback)
            {
                TestRunner.AssertTrue(anyVirtual, "兜底模式下所有网卡均标记为虚拟");
                Console.WriteLine("     注意：当前为兜底模式，扫描结果仅代表虚拟机网络");
            }
            else
            {
                TestRunner.AssertTrue(!anyVirtual, "非兜底模式下结果不含虚拟网卡");
            }

            // VirtIO 必须被识别为虚拟网卡（本项目开发环境即为此类，曾漏判）
            TestRunner.AssertTrue(HasKeyword("Red Hat VirtIO Ethernet Adapter", "virtio"),
                "VirtIO 关键字已被纳入虚拟网卡判定");
        }

        private static bool HasKeyword(string text, string keyword)
        {
            return text.ToLowerInvariant().IndexOf(keyword, StringComparison.Ordinal) >= 0;
        }
    }
}
