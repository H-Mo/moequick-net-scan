using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MQNetScan.Engine;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;
using MQNetScan.Presentation.Pages;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S4 UI 集成测试：真实驱动 ScanPage 的扫描生命周期。
    /// 通过反射调用私有事件处理器，绕过不可靠的鼠标模拟。
    /// </summary>
    internal static class ScanPageScanTests
    {
        public static void Run()
        {
            TestStartScanUpdatesUi();
            TestPauseAndResumeViaUi();
            TestStopViaUi();
        }

        // -------------------------------------------------------------------
        // 点击开始扫描 → 进度推进 → 自动完成
        // -------------------------------------------------------------------
        private static void TestStartScanUpdatesUi()
        {
            Console.WriteLine();
            Console.WriteLine("[S4-UI-1] 界面驱动扫描全流程（PRD FR-1.4.2）");
            TestRunner.BeginCase("开始扫描 → 进度推进 → 状态栏更新 → 完成");

            ScanPage page = CreatePage();
            try
            {
                Label lblStatus = (Label)GetField(page, "lblScanStatus");
                ProgressBar bar = (ProgressBar)GetField(page, "progressBar");
                Button btnStart = (Button)GetField(page, "btnStartScan");

                if (GetAdapterCount(page) == 0)
                {
                    TestRunner.Skip("当前环境无可枚举网卡，跳过 UI 扫描用例");
                    return;
                }

                // 用小网段加速测试
                TextBox txtCidr = (TextBox)GetField(page, "txtCidr");
                txtCidr.Text = "192.168.1.0/28";   // 14 个地址
                page.Revalidate();

                TestRunner.AssertTrue(btnStart.Enabled, "扫描前「开始扫描」可用");

                // 反射调用开始按钮处理器
                InvokePrivate(page, "OnStartScanClick", btnStart, EventArgs.Empty);

                TestRunner.AssertTrue(!btnStart.Enabled, "点击后「开始扫描」被禁用（进入扫描态）");

                // 驱动 UI 刷新循环（模拟 Timer.Tick）
                bool sawProgress = false;
                bool completed = false;
                int lastPercent = -1;

                for (int i = 0; i < 200; i++)
                {
                    InvokePrivate(page, "OnRefreshTick", null, EventArgs.Empty);
                    Thread.Sleep(20);

                    int p = bar.Value;

                    if (p > 0) { sawProgress = true; }
                    if (p < lastPercent)
                    {
                        TestRunner.AssertTrue(false, "进度出现回退（" + lastPercent + " → " + p + "）");
                        break;
                    }
                    lastPercent = p;

                    if (lblStatus.Text.IndexOf("扫描完成") >= 0)
                    {
                        completed = true;
                        break;
                    }

                    if (lblStatus.Text.IndexOf("已终止") >= 0) { break; }
                }

                TestRunner.AssertTrue(sawProgress, "进度条有推进（最终 " + bar.Value + "%）");
                TestRunner.AssertTrue(completed, "扫描完成（状态栏：" + lblStatus.Text + "）");
                TestRunner.AssertTrue(btnStart.Enabled, "完成后「开始扫描」恢复可用");
                TestRunner.AssertTrue(bar.Value == 100, "完成后进度为 100%（实际 " + bar.Value + "）");
            }
            finally
            {
                page.Dispose();
            }
        }

        // -------------------------------------------------------------------
        // 暂停与继续
        // -------------------------------------------------------------------
        private static void TestPauseAndResumeViaUi()
        {
            Console.WriteLine();
            Console.WriteLine("[S4-UI-2] 界面暂停与继续（PRD FR-1.4.3）");
            TestRunner.BeginCase("暂停后按钮文字变「继续」、进度停止推进");

            ScanPage page = CreatePage();
            try
            {
                Label lblStatus = (Label)GetField(page, "lblScanStatus");
                ProgressBar bar = (ProgressBar)GetField(page, "progressBar");
                Button btnStart = (Button)GetField(page, "btnStartScan");
                Button btnPause = (Button)GetField(page, "btnPause");
                TextBox txtCidr = (TextBox)GetField(page, "txtCidr");

                if (GetAdapterCount(page) == 0)
                {
                    TestRunner.Skip("当前环境无可枚举网卡，跳过");
                    return;
                }

                // 注入慢速探测器：每地址 60~100ms，确保暂停请求一定在扫描进行中到达。
                // 若不注入，默认假探测器（10~40ms）可能在本轮刷新前就跑完，
                // 造成"暂停无效"的假象——那是测试环境问题，不是产品缺陷。
                page.ProberFactory = delegate { return new FakeHostProber(60, 100, 0.5); };

                txtCidr.Text = "192.168.1.0/24";   // 254 个地址，32 线程约需 1~2 秒
                page.Revalidate();

                InvokePrivate(page, "OnStartScanClick", btnStart, EventArgs.Empty);

                // 先让扫描真正跑起来（等待进度出现非零值）
                int waited = 0;
                while (bar.Value == 0 && waited < 100)
                {
                    InvokePrivate(page, "OnRefreshTick", null, EventArgs.Empty);
                    Thread.Sleep(40);
                    waited++;
                }

                TestRunner.AssertTrue(bar.Value > 0, "扫描已开始推进（" + bar.Value + "%）");

                // 再跑一会，让进度推进到有意义的位置
                for (int i = 0; i < 4; i++)
                {
                    InvokePrivate(page, "OnRefreshTick", null, EventArgs.Empty);
                    Thread.Sleep(40);
                }

                int percentBeforePause = bar.Value;
                Console.WriteLine("       暂停前进度: " + percentBeforePause + "%");

                TestRunner.AssertTrue(percentBeforePause < 95,
                    "暂停前进度尚未接近完成（实际 " + percentBeforePause + "%）");

                // 暂停
                InvokePrivate(page, "OnPauseClick", btnPause, EventArgs.Empty);

                TestRunner.AssertEqual("继续", btnPause.Text, "暂停后按钮文字变为「继续」");
                TestRunner.AssertTrue(lblStatus.Text.IndexOf("已暂停") >= 0,
                    "状态栏显示已暂停（" + lblStatus.Text + "）");

                // 暂停后立即取一次进度作为基准（在途任务此时仍在收尾，
                // 因此不能拿"暂停前最后一次采样"当基准，否则会把收尾的部分误判为"暂停无效"）
                InvokePrivate(page, "OnRefreshTick", null, EventArgs.Empty);
                int percentAtPauseSettled = bar.Value;

                // 等足够长时间让所有在途探测彻底结束
                Thread.Sleep(600);

                for (int i = 0; i < 10; i++)
                {
                    InvokePrivate(page, "OnRefreshTick", null, EventArgs.Empty);
                    Thread.Sleep(40);
                }

                int percentAfterPause = bar.Value;

                Console.WriteLine("       暂停收尾后: " + percentAtPauseSettled + "% → 静置后: " + percentAfterPause + "%");

                TestRunner.AssertTrue(percentAfterPause < 100,
                    "暂停期间未跑完（实际 " + percentAfterPause + "%）");

                // 关键：暂停的语义是"不再取新任务，但在途探测允许自然结束"（PRD FR-1.4.3）。
                // 因此暂停后进度仍会前进，上限约为"工作线程数 × 1 个地址"。
                // 默认并发 32、目标 254，32/254 ≈ 12.6%；这里给到 20% 的余量。
                // 若超过这个量级，才说明暂停真的失效了。
                int concurrency = new AppSettings().MaxConcurrency;   // 默认 32
                int totalHosts = 254;
                int allowedDrift = (int)Math.Ceiling(concurrency * 100.0 / totalHosts) + 8;

                TestRunner.AssertTrue(
                    percentAfterPause - percentAtPauseSettled <= allowedDrift,
                    "暂停后增量仅来自在途任务收尾（实际增长 "
                    + (percentAfterPause - percentAtPauseSettled) + "%，上限 " + allowedDrift + "%）");

                // 继续
                InvokePrivate(page, "OnPauseClick", btnPause, EventArgs.Empty);
                TestRunner.AssertEqual("暂停", btnPause.Text, "继续后按钮文字恢复「暂停」");
                TestRunner.AssertTrue(lblStatus.Text.IndexOf("已暂停") < 0,
                    "状态栏不再显示已暂停（" + lblStatus.Text + "）");

                // 跑完
                bool finished = false;
                for (int i = 0; i < 400; i++)
                {
                    InvokePrivate(page, "OnRefreshTick", null, EventArgs.Empty);
                    Thread.Sleep(20);

                    if (lblStatus.Text.IndexOf("扫描完成") >= 0)
                    {
                        finished = true;
                        break;
                    }
                }

                TestRunner.AssertTrue(finished, "继续后能跑完（" + lblStatus.Text + "）");
                TestRunner.AssertEqual(100, bar.Value, "最终进度为 100%");
            }
            finally
            {
                page.Dispose();
            }
        }

        // -------------------------------------------------------------------
        // 终止
        // -------------------------------------------------------------------
        private static void TestStopViaUi()
        {
            Console.WriteLine();
            Console.WriteLine("[S4-UI-3] 界面终止（PRD FR-1.4.4）");
            TestRunner.BeginCase("终止后状态为「已终止（部分结果）」且按钮恢复");

            ScanPage page = CreatePage();
            try
            {
                Label lblStatus = (Label)GetField(page, "lblScanStatus");
                Button btnStart = (Button)GetField(page, "btnStartScan");
                Button btnStop = (Button)GetField(page, "btnStop");
                TextBox txtCidr = (TextBox)GetField(page, "txtCidr");

                if (GetAdapterCount(page) == 0)
                {
                    TestRunner.Skip("当前环境无可枚举网卡，跳过");
                    return;
                }

                txtCidr.Text = "192.168.1.0/24";
                page.Revalidate();

                // 同样注入慢速探测器，确保终止请求在扫描进行中到达
                page.ProberFactory = delegate { return new FakeHostProber(60, 100, 0.5); };

                InvokePrivate(page, "OnStartScanClick", btnStart, EventArgs.Empty);

                for (int i = 0; i < 5; i++)
                {
                    InvokePrivate(page, "OnRefreshTick", null, EventArgs.Empty);
                    Thread.Sleep(40);
                }

                InvokePrivate(page, "OnStopClick", btnStop, EventArgs.Empty);

                TestRunner.AssertTrue(lblStatus.Text.IndexOf("已终止") >= 0,
                    "状态栏显示已终止（" + lblStatus.Text + "）");
                TestRunner.AssertTrue(btnStart.Enabled, "终止后「开始扫描」恢复可用");
                TestRunner.AssertTrue(!btnStop.Enabled, "终止后「终止」禁用");
            }
            finally
            {
                page.Dispose();
            }
        }

        // -------------------------------------------------------------------
        private static ScanPage CreatePage()
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
