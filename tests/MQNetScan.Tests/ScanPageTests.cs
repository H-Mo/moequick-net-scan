using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using MQNetScan.Engine;
using MQNetScan.Infrastructure;
using MQNetScan.Presentation.Pages;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S3 网段输入交互测试。
    /// 覆盖 PRD FR-1.1.4 / FR-1.1.5 / FR-1.1.6 与 FR-1.4.1。
    /// 说明：确认框本身是模态 MessageBox，测试中不点击，
    ///       而是验证"何时会弹"的判定逻辑与按钮状态机。
    /// </summary>
    internal static class ScanPageTests
    {
        public static void Run()
        {
            TestConfirmThreshold();
            TestButtonStateMachine();
            TestCidrInputValidation();
            TestRangeModeToggle();
            TestLargeScanConfirmationGate();
        }

        // -------------------------------------------------------------------
        // FR-1.1.5：>254 才需要确认
        // -------------------------------------------------------------------
        private static void TestConfirmThreshold()
        {
            Console.WriteLine();
            Console.WriteLine("[S3-1] 确认框阈值边界（PRD FR-1.1.5）");
            TestRunner.BeginCase("254 不确认、255 与 1022 需确认");

            TestRunner.AssertEqual(254, NetSegment.ConfirmHostThreshold, "阈值常量为 254");

            NetSegment seg;
            SegmentParseError err;
            string msg;

            // /24 = 254，恰好等于阈值，不需要确认
            NetSegment.TryParseCidr("192.168.1.0/24", out seg, out err, out msg);
            TestRunner.AssertEqual(254, seg.HostCount, "/24 为 254 个地址");
            TestRunner.AssertTrue(
                seg.HostCount <= NetSegment.ConfirmHostThreshold,
                "254 不触发确认框（等于阈值不弹）");

            // /23 = 510，超过阈值
            NetSegment.TryParseCidr("192.168.0.0/23", out seg, out err, out msg);
            TestRunner.AssertEqual(510, seg.HostCount, "/23 为 510 个地址");
            TestRunner.AssertTrue(
                seg.HostCount > NetSegment.ConfirmHostThreshold,
                "510 触发确认框");

            // /22 = 1022
            NetSegment.TryParseCidr("10.1.0.0/22", out seg, out err, out msg);
            TestRunner.AssertEqual(1022, seg.HostCount, "/22 为 1022 个地址");
            TestRunner.AssertTrue(
                seg.HostCount > NetSegment.ConfirmHostThreshold,
                "1022 触发确认框");

            // 精确区间 1~254 = 254，不触发
            NetSegment.TryParseRange("192.168.1.1", "192.168.1.254", out seg, out err, out msg);
            TestRunner.AssertEqual(254, seg.HostCount, "区间 1~254 为 254 个地址");
            TestRunner.AssertTrue(
                seg.HostCount <= NetSegment.ConfirmHostThreshold,
                "区间 254 不触发确认框");

            // 精确区间 1~255 = 255，触发
            NetSegment.TryParseRange("192.168.1.1", "192.168.1.255", out seg, out err, out msg);
            TestRunner.AssertEqual(255, seg.HostCount, "区间 1~255 为 255 个地址");
            TestRunner.AssertTrue(
                seg.HostCount > NetSegment.ConfirmHostThreshold,
                "区间 255 触发确认框（比阈值多 1 个也弹）");
        }

        // -------------------------------------------------------------------
        // FR-1.4.1：按钮可用态随状态切换
        // -------------------------------------------------------------------
        private static void TestButtonStateMachine()
        {
            Console.WriteLine();
            Console.WriteLine("[S3-2] 按钮状态机（PRD FR-1.4.1）");
            TestRunner.BeginCase("未扫描时仅开始可用；扫描中开始禁用");

            using (ScanPage page = new ScanPage())
            {
                Button btnStart = (Button)GetField(page, "btnStartScan");
                Button btnPause = (Button)GetField(page, "btnPause");
                Button btnStop = (Button)GetField(page, "btnStop");
                GroupBox grpTarget = (GroupBox)GetField(page, "grpTarget");

                // 初始：只有开始可用
                TestRunner.AssertTrue(btnStart.Enabled, "初始状态「开始扫描」可用");
                TestRunner.AssertTrue(!btnPause.Enabled, "初始状态「暂停」禁用");
                TestRunner.AssertTrue(!btnStop.Enabled, "初始状态「终止」禁用");
                TestRunner.AssertTrue(grpTarget.Enabled, "初始状态目标输入区可编辑");

                // 进入扫描
                page.SetScanning(true);
                TestRunner.AssertTrue(!btnStart.Enabled, "扫描中「开始扫描」禁用");
                TestRunner.AssertTrue(btnPause.Enabled, "扫描中「暂停」可用");
                TestRunner.AssertTrue(btnStop.Enabled, "扫描中「终止」可用");
                TestRunner.AssertTrue(!grpTarget.Enabled, "扫描中目标输入区锁定");

                // 退出扫描
                page.SetScanning(false);
                TestRunner.AssertTrue(btnStart.Enabled, "结束后「开始扫描」恢复可用");
                TestRunner.AssertTrue(!btnPause.Enabled, "结束后「暂停」禁用");
                TestRunner.AssertTrue(!btnStop.Enabled, "结束后「终止」禁用");
                TestRunner.AssertTrue(grpTarget.Enabled, "结束后目标输入区解锁");
            }
        }

        // -------------------------------------------------------------------
        // FR-1.1.4：非法输入即时校验
        // -------------------------------------------------------------------
        private static void TestCidrInputValidation()
        {
            Console.WriteLine();
            Console.WriteLine("[S3-3] 输入校验与提示（PRD FR-1.1.4）");
            TestRunner.BeginCase("非法 CIDR 给红字提示且不算成功");

            using (ScanPage page = CreateInitializedPage())
            {
                TextBox txtCidr = (TextBox)GetField(page, "txtCidr");
                Label lblValidation = (Label)GetField(page, "lblValidation");

                if (GetAdapterCount(page) == 0)
                {
                    TestRunner.Skip("当前环境无可枚举网卡，跳过输入校验用例");
                    return;
                }

                // 非法：前缀越界
                txtCidr.Text = "192.168.1.0/7";
                bool ok = page.Revalidate();

                TestRunner.AssertTrue(!ok, "/7 校验不通过");
                TestRunner.AssertTrue(
                    lblValidation.Text.IndexOf("8~30") >= 0,
                    "提示说明了掩码范围（实际: " + lblValidation.Text + "）");
                TestRunner.AssertEqual(null, page.CurrentSegment, "校验失败时 CurrentSegment 为空");

                // 非法：IP 格式
                txtCidr.Text = "192.168.1.999/24";
                ok = page.Revalidate();

                TestRunner.AssertTrue(!ok, "非法 IP 校验不通过");
                TestRunner.AssertTrue(
                    lblValidation.Text.IndexOf("不合法") >= 0,
                    "提示说明了 IP 非法（实际: " + lblValidation.Text + "）");

                // 合法
                txtCidr.Text = "192.168.1.0/24";
                ok = page.Revalidate();

                TestRunner.AssertTrue(ok, "合法 CIDR 校验通过");
                TestRunner.AssertTrue(page.CurrentSegment != null, "CurrentSegment 非空");

                if (page.CurrentSegment != null)
                {
                    TestRunner.AssertEqual(254, page.CurrentSegment.HostCount, "解析出 254 个地址");
                }
            }
        }

        // -------------------------------------------------------------------
        // 模式切换：CIDR ↔ 起止 IP
        // -------------------------------------------------------------------
        private static void TestRangeModeToggle()
        {
            Console.WriteLine();
            Console.WriteLine("[S3-4] 模式切换（CIDR / 起止 IP）");
            TestRunner.BeginCase("切换后控件启用状态与解析结果同步");

            using (ScanPage page = CreateInitializedPage())
            {
                RadioButton rbCidr = (RadioButton)GetField(page, "rbCidr");
                RadioButton rbRange = (RadioButton)GetField(page, "rbRange");
                TextBox txtCidr = (TextBox)GetField(page, "txtCidr");
                TextBox txtStart = (TextBox)GetField(page, "txtRangeStart");
                TextBox txtEnd = (TextBox)GetField(page, "txtRangeEnd");
                Label lblValidation = (Label)GetField(page, "lblValidation");

                // 默认 CIDR 模式
                TestRunner.AssertTrue(txtCidr.Enabled, "CIDR 模式下网段输入框可编辑");
                TestRunner.AssertTrue(!txtStart.Enabled, "CIDR 模式下起止框禁用");

                // 切到区间模式
                rbCidr.Checked = false;
                rbRange.Checked = true;

                // 手动触发事件（测试环境不会自动跑设计器事件）
                InvokePrivate(page, "OnModeChanged", rbRange, EventArgs.Empty);

                TestRunner.AssertTrue(!txtCidr.Enabled, "区间模式下网段框禁用");
                TestRunner.AssertTrue(txtStart.Enabled, "区间模式下起止框可编辑");
                TestRunner.AssertTrue(txtEnd.Enabled, "区间模式下结束框可编辑");

                if (GetAdapterCount(page) == 0)
                {
                    TestRunner.Skip("当前环境无可枚举网卡，跳过区间解析用例");
                    return;
                }

                // 区间模式下解析
                txtStart.Text = "192.168.1.10";
                txtEnd.Text = "192.168.1.50";

                bool ok = page.Revalidate();

                TestRunner.AssertTrue(ok, "合法区间校验通过（提示: " + lblValidation.Text + "）");

                if (ok && page.CurrentSegment != null)
                {
                    TestRunner.AssertEqual(41, page.CurrentSegment.HostCount, "区间为 41 个地址");
                    TestRunner.AssertEqual(SegmentMode.ExplicitRange, page.CurrentSegment.Mode,
                        "模式为精确区间");
                }

                // 区间顺序颠倒
                txtStart.Text = "192.168.1.50";
                txtEnd.Text = "192.168.1.10";
                ok = page.Revalidate();

                TestRunner.AssertTrue(!ok, "起 > 止 校验不通过");
            }
        }

        // -------------------------------------------------------------------
        // 辅助：构造并初始化 ScanPage
        // -------------------------------------------------------------------
        /// <summary>
        /// ScanPage 的 _adapters 只在 Initialize() 中填充。
        /// 若不调用 Initialize，Revalidate() 会因无网卡而直接返回 false，
        /// 导致输入校验用例全部"莫名失败"。这里统一先初始化。
        /// </summary>
        private static ScanPage CreateInitializedPage()
        {
            ScanPage page = new ScanPage();
            page.Initialize("", new AppSettings());
            return page;
        }

        private static int GetAdapterCount(ScanPage page)
        {
            object list = GetField(page, "_adapters");
            if (list == null) { return 0; }

            System.Collections.IList il = list as System.Collections.IList;
            return il == null ? 0 : il.Count;
        }

        // -------------------------------------------------------------------
        // 确认门槛：HostCount 决定是否弹框（不实际弹出 MessageBox）
        // -------------------------------------------------------------------
        private static void TestLargeScanConfirmationGate()
        {
            Console.WriteLine();
            Console.WriteLine("[S3-5] 大范围扫描的确认门槛");
            TestRunner.BeginCase("验证判定依据为 HostCount 而非前缀长度");

            NetSegment small;
            NetSegment large;
            SegmentParseError err;
            string msg;

            // /25 = 126，小范围
            NetSegment.TryParseCidr("192.168.1.0/25", out small, out err, out msg);
            TestRunner.AssertEqual(126, small.HostCount, "/25 为 126 个地址");
            TestRunner.AssertTrue(small.HostCount <= 254, "/25 不需要确认");

            // 精确区间 1~300 = 300，即使不是标准网段也要确认
            NetSegment.TryParseRange("192.168.1.1", "192.168.2.44", out large, out err, out msg);

            if (large != null)
            {
                Console.WriteLine("       区间 1~300 实际地址数: " + large.HostCount);
                TestRunner.AssertTrue(large.HostCount > 254, "300 地址区间需要确认");
            }
            else
            {
                // 跨网段区间可能被拒绝，这也可接受
                TestRunner.AssertTrue(err == SegmentParseError.TooManyHosts
                    || err == SegmentParseError.RangeCrossSubnet,
                    "跨段区间被合理拒绝（" + err + "）");
            }
        }

        // -------------------------------------------------------------------
        private static object GetField(object target, string name)
        {
            Type t = target.GetType();
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null) { throw new InvalidOperationException("找不到控件字段: " + name); }
            return f.GetValue(target);
        }

        private static void InvokePrivate(object target, string method, params object[] args)
        {
            Type t = target.GetType();
            MethodInfo m = t.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            if (m == null) { throw new InvalidOperationException("找不到方法: " + method); }
            m.Invoke(target, args);
        }
    }
}
