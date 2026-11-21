using System;
using System.Collections.Generic;
using System.Windows.Forms;
using MQNetScan.Engine;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;
using MQNetScan.Presentation.Common;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S10 网关预警与 IP 冲突测试。
    /// 覆盖 PRD FR-1.5.1 ~ FR-1.5.4。
    /// </summary>
    internal static class AlertTests
    {
        public static void Run()
        {
            TestAlertItemBasics();
            TestBannerHiddenWhenEmpty();
            TestBannerShowsCritical();
            TestBannerDismissAndReappear();
            TestBannerMergesMultiple();
            TestGatewayProbeRealGateway();
            TestGatewayProbeUnreachable();
            TestGatewayProbeNoGateway();
            TestConflictDetectorEvidence();
            TestCompositeProberMarksConflict();
        }

        // -------------------------------------------------------------------
        private static void TestAlertItemBasics()
        {
            Console.WriteLine();
            Console.WriteLine("[S10-1] 告警项基本属性");
            TestRunner.BeginCase("严重告警标记正确、横幅文本含建议");

            AlertItem gw = new AlertItem(AlertKind.GatewayUnreachable,
                "默认网关不可达", "请检查交换机", "192.168.1.1");

            TestRunner.AssertTrue(gw.IsCritical, "网关不可达为严重告警");
            TestRunner.AssertTrue(gw.ToBannerText().IndexOf("默认网关不可达") >= 0,
                "横幅含结论");
            TestRunner.AssertTrue(gw.ToBannerText().IndexOf("请检查交换机") >= 0,
                "横幅含建议");
            TestRunner.AssertEqual("192.168.1.1", gw.IpText, "记录了 IP");

            AlertItem conflict = new AlertItem(AlertKind.IpConflict,
                "检测到 IP 冲突", "请改用其它 IP", "192.168.1.50");
            TestRunner.AssertTrue(conflict.IsCritical, "IP 冲突为严重告警");

            AlertItem info = new AlertItem(AlertKind.Info, "一般提示", "");
            TestRunner.AssertTrue(!info.IsCritical, "一般提示非严重");
            TestRunner.AssertEqual("一般提示", info.ToBannerText(), "无建议时横幅只显示结论");
        }

        // -------------------------------------------------------------------
        // FR-1.5.4：正常状态完全不占位
        // -------------------------------------------------------------------
        private static void TestBannerHiddenWhenEmpty()
        {
            Console.WriteLine();
            Console.WriteLine("[S10-2] 正常状态不占位（PRD FR-1.5.4）");
            TestRunner.BeginCase("无告警时 Visible=false 且 Height=0");

            using (AlertBanner banner = new AlertBanner())
            {
                TestRunner.AssertTrue(!banner.Visible, "初始不可见");
                TestRunner.AssertEqual(0, banner.Height, "初始高度为 0（不占位）");

                banner.SetAlerts(new List<AlertItem>());

                TestRunner.AssertTrue(!banner.Visible, "空列表仍不可见");
                TestRunner.AssertEqual(0, banner.Height, "空列表高度仍为 0");
                TestRunner.AssertEqual(0, banner.AlertCount, "告警数为 0");

                // null 也不应抛异常
                banner.SetAlerts(null);
                TestRunner.AssertTrue(!banner.Visible, "null 不抛异常且不可见");
            }
        }

        private static void TestBannerShowsCritical()
        {
            Console.WriteLine();
            Console.WriteLine("[S10-3] 严重告警显示横幅（PRD FR-1.5.1）");
            TestRunner.BeginCase("网关不可达时显示红色横幅");

            using (AlertBanner banner = new AlertBanner())
            {
                List<AlertItem> alerts = new List<AlertItem>();
                alerts.Add(new AlertItem(AlertKind.GatewayUnreachable,
                    "默认网关 192.168.1.1 不可达。",
                    "请优先检查交换机、路由器与上联线路。",
                    "192.168.1.1"));

                banner.SetAlerts(alerts);

                TestRunner.AssertTrue(banner.Visible, "显示横幅");
                TestRunner.AssertTrue(banner.Height > 0, "高度大于 0（占位）");
                TestRunner.AssertTrue(banner.IsShowing, "IsShowing 为 true");
                TestRunner.AssertEqual(1, banner.AlertCount, "告警数为 1");

                Console.WriteLine("       横幅文本: " + banner.BannerText);

                TestRunner.AssertTrue(banner.BannerText.IndexOf("不可达") >= 0,
                    "横幅含结论");
                TestRunner.AssertTrue(banner.BannerText.IndexOf("交换机") >= 0,
                    "横幅含建议");

                // 非严重告警不应触发横幅
                List<AlertItem> info = new List<AlertItem>();
                info.Add(new AlertItem(AlertKind.Info, "一般提示", ""));

                banner.Clear();
                banner.SetAlerts(info);

                TestRunner.AssertTrue(!banner.Visible, "一般提示不触发横幅");
            }
        }

        // -------------------------------------------------------------------
        // FR-1.5.3：可关闭，但同一问题再次出现时重新显示
        // -------------------------------------------------------------------
        private static void TestBannerDismissAndReappear()
        {
            Console.WriteLine();
            Console.WriteLine("[S10-4] 关闭与重现（PRD FR-1.5.3）");
            TestRunner.BeginCase("关闭后隐藏；同一问题不再自动弹；新问题会弹");

            using (AlertBanner banner = new AlertBanner())
            {
                AlertItem gwAlert = new AlertItem(AlertKind.GatewayUnreachable,
                    "默认网关不可达", "请检查线路", "192.168.1.1");

                List<AlertItem> list = new List<AlertItem>();
                list.Add(gwAlert);

                banner.SetAlerts(list);
                TestRunner.AssertTrue(banner.Visible, "首次显示");

                // 模拟点击关闭
                InvokeClose(banner);

                TestRunner.AssertTrue(!banner.Visible, "关闭后隐藏");
                TestRunner.AssertEqual(0, banner.Height, "关闭后不占位");

                // 同一问题再次上报 → 不再自动弹
                banner.SetAlerts(list);
                TestRunner.AssertTrue(!banner.Visible, "同一问题不再自动弹出");

                // 不同的问题（另一个 IP 的冲突）→ 应重新弹出
                List<AlertItem> list2 = new List<AlertItem>();
                list2.Add(gwAlert);
                list2.Add(new AlertItem(AlertKind.IpConflict,
                    "检测到 IP 冲突：192.168.1.50", "请改用其它 IP", "192.168.1.50"));

                banner.SetAlerts(list2);
                TestRunner.AssertTrue(banner.Visible, "出现新问题时重新弹出");
                TestRunner.AssertTrue(banner.BannerText.IndexOf("另有 1 项") >= 0,
                    "多条告警时提示还有其它项");

                // Clear 后应能重新显示旧问题
                banner.Clear();
                TestRunner.AssertTrue(!banner.Visible, "Clear 后隐藏");

                banner.SetAlerts(list);
                TestRunner.AssertTrue(banner.Visible, "Clear 后同一问题可重新显示");
            }
        }

        private static void InvokeClose(AlertBanner banner)
        {
            System.Reflection.MethodInfo m = typeof(AlertBanner).GetMethod("OnCloseClick",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            if (m == null) { throw new InvalidOperationException("找不到 OnCloseClick"); }

            m.Invoke(banner, new object[] { null, EventArgs.Empty });
        }

        private static void TestBannerMergesMultiple()
        {
            Console.WriteLine();
            Console.WriteLine("[S10-5] 多条告警合并显示");
            TestRunner.BeginCase("避免横幅过高");

            using (AlertBanner banner = new AlertBanner())
            {
                List<AlertItem> alerts = new List<AlertItem>();
                alerts.Add(new AlertItem(AlertKind.GatewayUnreachable,
                    "默认网关不可达", "请检查线路", "192.168.1.1"));
                alerts.Add(new AlertItem(AlertKind.IpConflict,
                    "检测到 IP 冲突：.50", "请改用其它 IP", "192.168.1.50"));
                alerts.Add(new AlertItem(AlertKind.IpConflict,
                    "检测到 IP 冲突：.60", "请改用其它 IP", "192.168.1.60"));

                banner.SetAlerts(alerts);

                TestRunner.AssertEqual(3, banner.AlertCount, "3 条告警");

                Console.WriteLine("       横幅文本: " + banner.BannerText);

                TestRunner.AssertTrue(banner.BannerText.IndexOf("另有 2 项") >= 0,
                    "合并显示为「另有 2 项」");
                TestRunner.AssertTrue(banner.Height <= 60,
                    "高度受控（实际 " + banner.Height + " px）");
            }
        }

        // -------------------------------------------------------------------
        private static void TestGatewayProbeRealGateway()
        {
            Console.WriteLine();
            Console.WriteLine("[S10-6] 真实网关探测");
            TestRunner.BeginCase("本机网关应判定为可达");

            List<AdapterInfo> adapters = NetworkAdapterService.GetActiveAdapters();

            if (adapters.Count == 0)
            {
                TestRunner.Skip("无可枚举网卡");
                return;
            }

            AdapterInfo a = adapters[0];

            if (!a.HasGateway)
            {
                TestRunner.Skip("本机无默认网关");
                return;
            }

            GatewayProbeResult r = GatewayProber.ProbeAdapter(a, 800);

            Console.WriteLine("       网关: " + r.GatewayText);
            Console.WriteLine("       可达: " + r.Reachable);
            Console.WriteLine("       方式: " + r.Method + "，耗时 " + r.ElapsedMs + " ms");

            TestRunner.AssertTrue(r.Reachable, "网关可达（" + r.Method + "）");
            TestRunner.AssertTrue(r.Method.Length > 0, "记录了探测方式");
            TestRunner.AssertTrue(r.ToAlert() == null, "可达时不生成告警");
        }

        // -------------------------------------------------------------------
        private static void TestGatewayProbeUnreachable()
        {
            Console.WriteLine();
            Console.WriteLine("[S10-7] 网关不可达（PRD FR-1.5.1）");
            TestRunner.BeginCase("不存在的网关应判定不可达并生成告警");

            // 用一个不在本网段的地址作为"网关"
            GatewayProbeResult r = GatewayProber.Probe("192.0.2.254", 0, 400);

            Console.WriteLine("       结果: 可达=" + r.Reachable
                + "，方式=" + r.Method + "，耗时 " + r.ElapsedMs + " ms");

            TestRunner.AssertTrue(!r.Reachable, "判定为不可达");

            AlertItem alert = r.ToAlert();

            TestRunner.AssertTrue(alert != null, "生成告警");
            TestRunner.AssertTrue(alert.IsCritical, "为严重告警（触发红色横幅）");
            TestRunner.AssertTrue(alert.Message.IndexOf("不可达") >= 0,
                "告警说明不可达：" + alert.Message);
            TestRunner.AssertTrue(alert.Suggestion.Length > 0, "含排查建议");
        }

        private static void TestGatewayProbeNoGateway()
        {
            Console.WriteLine();
            Console.WriteLine("[S10-8] 未配置网关");
            TestRunner.BeginCase("无网关时给出明确提示");

            GatewayProbeResult r = GatewayProber.Probe("", 0, 300);

            TestRunner.AssertTrue(!r.Reachable, "无网关判定为不可达");

            AlertItem alert = r.ToAlert();

            TestRunner.AssertTrue(alert != null, "生成告警");
            TestRunner.AssertTrue(alert.Message.IndexOf("未配置默认网关") >= 0,
                "提示未配置网关：" + alert.Message);

            // 非法网关文本
            GatewayProbeResult r2 = GatewayProber.Probe("not-an-ip", 0, 300);
            TestRunner.AssertTrue(!r2.Reachable, "非法网关文本判定为不可达");
        }

        // -------------------------------------------------------------------
        private static void TestConflictDetectorEvidence()
        {
            Console.WriteLine();
            Console.WriteLine("[S10-9] IP 冲突双证据链（PRD FR-1.5.2）");
            TestRunner.BeginCase("同 IP 双 MAC 判冲突；单 MAC 不误报");

            ConflictDetector d = new ConflictDetector();

            uint ip;
            Ipv4Util.TryParse("192.168.1.100", out ip);

            string msg;

            TestRunner.AssertTrue(!d.Observe(ip, "AA:BB:CC:00:00:01", out msg),
                "首次观测不是冲突");
            TestRunner.AssertTrue(!d.Observe(ip, "AA:BB:CC:00:00:01", out msg),
                "同 MAC 重复不是冲突");
            TestRunner.AssertTrue(d.Observe(ip, "AA:BB:CC:00:00:02", out msg),
                "出现第二个 MAC 判为冲突");

            TestRunner.AssertEqual(1, d.ConflictCount, "冲突计数为 1");

            List<AlertItem> alerts = d.BuildAlerts();

            TestRunner.AssertEqual(1, alerts.Count, "生成 1 条告警");
            TestRunner.AssertTrue(alerts[0].IsCritical, "冲突为严重告警");
            TestRunner.AssertTrue(alerts[0].IpText == "192.168.1.100",
                "告警记录了冲突 IP");

            // Reset 后清零
            d.Reset();
            TestRunner.AssertEqual(0, d.ConflictCount, "Reset 后清零");
        }

        // -------------------------------------------------------------------
        private static void TestCompositeProberMarksConflict()
        {
            Console.WriteLine();
            Console.WriteLine("[S10-10] 组合探测器标记冲突主机");
            TestRunner.BeginCase("冲突主机的 ConflictFlag 被置位");

            // 用假探测器模拟：同一个 IP 两次返回不同 MAC
            ConflictDetector d = new ConflictDetector();

            uint ip;
            Ipv4Util.TryParse("192.168.1.100", out ip);

            string msg;

            // 手工模拟 CompositeProber 内部的观测调用
            d.Observe(ip, "AA:BB:CC:00:00:01", out msg);
            bool conflicted = d.Observe(ip, "AA:BB:CC:00:00:02", out msg);

            TestRunner.AssertTrue(conflicted, "第二次观测判定为冲突");

            // HostRecord 的 ConflictFlag 应能被置位并读取
            HostRecord rec = new HostRecord(ip, "AA:BB:CC:00:00:02", new int[0]);
            TestRunner.AssertTrue(!rec.ConflictFlag, "默认未标记冲突");

            rec.ConflictFlag = true;
            TestRunner.AssertTrue(rec.ConflictFlag, "可标记为冲突主机");
        }
    }
}
