using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MQNetScan.Engine;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;
using MQNetScan.Presentation.Common;
using MQNetScan.Presentation.Pages;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S6 界面端到端：验证真实扫描结果真正进入了 ListView。
    ///
    /// 用注入的快速探测器（FakeHostProber）驱动，
    /// 因为真实 ARP 对无响应地址要等 3~4 秒，不适合放在常规测试里。
    /// 真实 ARP 的列表落地由 --arp-e2e 参数下的用例覆盖。
    /// </summary>
    internal static class ListViewEndToEndTests
    {
        public static void Run()
        {
            TestResultsLandInListView();
            TestProgressAndListConsistency();
        }

        private static void TestResultsLandInListView()
        {
            Console.WriteLine();
            Console.WriteLine("[S6-E2E] 扫描结果进入 ListView");
            TestRunner.BeginCase("真实控件 + 注入探测器，验证行内容");

            ScanPage page = new ScanPage();

            try
            {
                page.Initialize("", new AppSettings());

                ListView lv = (ListView)GetField(page, "listHosts");

                // 用较快但足够慢的探测器，保证能观察增量插入
                page.ProberFactory = delegate { return new FakeHostProber(3, 8, 0.8); };

                // 设一个能确认全部存活的场景：用 /28（14 个地址）
                TextBox txtCidr = (TextBox)GetField(page, "txtCidr");
                txtCidr.Text = "10.9.9.0/28";
                page.Revalidate();

                Button btnStart = (Button)GetField(page, "btnStartScan");
                InvokePrivate(page, "OnStartScanClick", btnStart, EventArgs.Empty);

                Label lblStatus = (Label)GetField(page, "lblScanStatus");

                bool done = false;

                for (int i = 0; i < 300; i++)
                {
                    InvokePrivate(page, "OnRefreshTick", null, EventArgs.Empty);
                    Thread.Sleep(15);

                    if (lblStatus.Text.IndexOf("扫描完成") >= 0)
                    {
                        done = true;
                        break;
                    }
                }

                TestRunner.AssertTrue(done, "扫描完成（" + lblStatus.Text + "）");
                TestRunner.AssertTrue(lv.Items.Count > 0,
                    "ListView 中有 " + lv.Items.Count + " 行结果");

                // 逐行校验格式
                bool allIpValid = true;
                bool allMacValid = true;
                bool allRoleFilled = true;
                bool indexSequential = true;

                for (int i = 0; i < lv.Items.Count; i++)
                {
                    ListViewItem it = lv.Items[i];

                    uint ip;
                    if (!Ipv4Util.TryParse(it.SubItems[ListViewHelper.ColIp].Text, out ip))
                    {
                        allIpValid = false;
                    }

                    string mac = it.SubItems[ListViewHelper.ColMac].Text;
                    if (mac.Length != 17 || mac.IndexOf(':') < 0) { allMacValid = false; }

                    if (string.IsNullOrEmpty(it.SubItems[ListViewHelper.ColRole].Text))
                    {
                        allRoleFilled = false;
                    }

                    int idx;
                    if (!int.TryParse(it.SubItems[ListViewHelper.ColIndex].Text, out idx) || idx != i + 1)
                    {
                        indexSequential = false;
                    }
                }

                TestRunner.AssertTrue(allIpValid, "所有行 IP 可解析");
                TestRunner.AssertTrue(allMacValid, "所有行 MAC 格式正确（17 字符含冒号）");
                TestRunner.AssertTrue(allRoleFilled, "所有行角色非空");
                TestRunner.AssertTrue(indexSequential, "行号连续（1..N）");

                // 打印前几行供人工比对
                Console.WriteLine("       前几行:");
                for (int i = 0; i < lv.Items.Count && i < 5; i++)
                {
                    ListViewItem it = lv.Items[i];
                    Console.WriteLine("         " + it.SubItems[ListViewHelper.ColIndex].Text
                        + " | " + it.SubItems[ListViewHelper.ColIp].Text
                        + " | " + it.SubItems[ListViewHelper.ColMac].Text
                        + " | " + it.SubItems[ListViewHelper.ColPorts].Text
                        + " | " + it.SubItems[ListViewHelper.ColRole].Text);
                }

                // 全选后复制，验证格式
                for (int i = 0; i < lv.Items.Count; i++) { lv.Items[i].Selected = true; }

                string copied = ListViewHelper.BuildSelectedText(lv);

                TestRunner.AssertTrue(copied != null, "全选后可复制");
                TestRunner.AssertTrue(copied.Split('\n').Length >= lv.Items.Count,
                    "复制文本行数不少于列表行数");
            }
            finally
            {
                page.Dispose();
            }
        }

        private static void TestProgressAndListConsistency()
        {
            Console.WriteLine();
            Console.WriteLine("[S6-E2E] 进度与列表一致性");
            TestRunner.BeginCase("存活台数与列表行数一致");

            ScanPage page = new ScanPage();

            try
            {
                page.Initialize("", new AppSettings());

                ListView lv = (ListView)GetField(page, "listHosts");
                page.ProberFactory = delegate { return new FakeHostProber(2, 6, 0.6); };

                TextBox txtCidr = (TextBox)GetField(page, "txtCidr");
                txtCidr.Text = "10.8.8.0/27";   // 30 个地址
                page.Revalidate();

                Button btnStart = (Button)GetField(page, "btnStartScan");
                InvokePrivate(page, "OnStartScanClick", btnStart, EventArgs.Empty);

                Label lblStatus = (Label)GetField(page, "lblScanStatus");

                for (int i = 0; i < 400; i++)
                {
                    InvokePrivate(page, "OnRefreshTick", null, EventArgs.Empty);
                    Thread.Sleep(15);
                    if (lblStatus.Text.IndexOf("扫描完成") >= 0) { break; }
                }

                // 从状态栏解析"发现 N 台"
                string status = lblStatus.Text;
                Console.WriteLine("       状态: " + status);

                TestRunner.AssertTrue(status.IndexOf("扫描完成") >= 0, "扫描完成");

                // 状态栏应包含列表行数说明
                TestRunner.AssertTrue(status.IndexOf("列表显示") >= 0,
                    "状态栏说明了列表显示行数");

                // 列表行数必须与"列表显示 N 行"一致
                int shownInText = ExtractNumberAfter(status, "列表显示");

                TestRunner.AssertEqual(lv.Items.Count, shownInText,
                    "状态栏显示的列表行数与实际行数一致（" + lv.Items.Count + "）");

                // 列表显示行数不应超过"发现 N 台"
                int aliveInText = ExtractNumberAfter(status, "发现");

                TestRunner.AssertTrue(lv.Items.Count <= aliveInText,
                    "列表行数不超过发现的设备数（" + lv.Items.Count + " <= " + aliveInText + "）");
            }
            finally
            {
                page.Dispose();
            }
        }

        private static int ExtractNumberAfter(string text, string marker)
        {
            int idx = text.IndexOf(marker);
            if (idx < 0) { return -1; }

            int start = idx + marker.Length;

            while (start < text.Length && !char.IsDigit(text[start])) { start++; }

            int end = start;
            while (end < text.Length && char.IsDigit(text[end])) { end++; }

            if (end <= start) { return -1; }

            int v;
            int.TryParse(text.Substring(start, end - start), out v);
            return v;
        }

        private static object GetField(object target, string name)
        {
            Type t = target.GetType();
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null) { throw new InvalidOperationException("找不到字段: " + name); }
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
