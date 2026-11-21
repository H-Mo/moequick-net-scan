using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MQNetScan.Application;
using MQNetScan.Engine;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;
using MQNetScan.Presentation.Common;
using MQNetScan.Presentation.Pages;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S9 端到端：界面扫描 → 导出报告 → 校验报告与界面列表一致。
    ///
    /// 这是 PRD FR-1.7 的关键约束：
    /// "报告内容必须与界面列表完全一致（行数、IP、角色逐条比对）"。
    /// </summary>
    internal static class ReportEndToEndTests
    {
        public static void Run()
        {
            TestReportMatchesList();
        }

        private static void TestReportMatchesList()
        {
            Console.WriteLine();
            Console.WriteLine("[S9-E2E] 报告内容与界面列表一致（PRD FR-1.7）");
            TestRunner.BeginCase("扫描 → 导出 → 逐条比对");

            // 用临时目录作为"桌面"不可控，这里直接验证 ReportBuilder
            // 基于界面列表构造的输入数据，避免真实写桌面的副作用。
            ScanPage page = new ScanPage();

            try
            {
                page.Initialize("", new AppSettings());

                ListView lv = (ListView)GetField(page, "listHosts");

                page.ProberFactory = delegate { return new FakeHostProber(2, 5, 0.9); };

                TextBox txtCidr = (TextBox)GetField(page, "txtCidr");
                txtCidr.Text = "10.7.7.0/28";   // 14 个地址
                page.Revalidate();

                Button btnStart = (Button)GetField(page, "btnStartScan");
                InvokePrivate(page, "OnStartScanClick", btnStart, EventArgs.Empty);

                Label lblStatus = (Label)GetField(page, "lblScanStatus");

                for (int i = 0; i < 300; i++)
                {
                    InvokePrivate(page, "OnRefreshTick", null, EventArgs.Empty);
                    Thread.Sleep(15);

                    if (lblStatus.Text.IndexOf("扫描完成") >= 0) { break; }
                }

                TestRunner.AssertTrue(lblStatus.Text.IndexOf("扫描完成") >= 0,
                    "扫描完成");
                TestRunner.AssertTrue(lv.Items.Count > 0,
                    "界面列表有 " + lv.Items.Count + " 行");

                // 通过反射调用 BuildReportInput，再用 ReportBuilder 生成
                object input = InvokePrivateWithReturn(page, "BuildReportInput");

                TestRunner.AssertTrue(input != null, "可构造报告输入");

                ReportInput ri = input as ReportInput;
                if (ri == null)
                {
                    TestRunner.AssertTrue(false, "报告输入类型正确");
                    return;
                }

                Console.WriteLine("       报告目标数: " + ri.TargetCount);
                Console.WriteLine("       报告存活数: " + ri.AliveCount);
                Console.WriteLine("       报告明细行数: " + (ri.Hosts == null ? 0 : ri.Hosts.Count));

                // 1) 行数一致
                TestRunner.AssertEqual(lv.Items.Count, ri.Hosts.Count,
                    "报告明细行数 = 界面列表行数");

                // 2) 逐条比对 IP 与角色
                bool allMatch = true;

                for (int i = 0; i < lv.Items.Count && i < ri.Hosts.Count; i++)
                {
                    string listIp = lv.Items[i].SubItems[ListViewHelper.ColIp].Text;
                    string listRole = lv.Items[i].SubItems[ListViewHelper.ColRole].Text;
                    string listPorts = lv.Items[i].SubItems[ListViewHelper.ColPorts].Text;

                    HostRecord h = ri.Hosts[i];

                    if (h.IpText != listIp) { allMatch = false; }
                    if (h.Role != listRole) { allMatch = false; }
                    if (h.OpenPortsText != listPorts) { allMatch = false; }
                }

                TestRunner.AssertTrue(allMatch,
                    "报告中每行的 IP / 角色 / 端口都与界面列表一致");

                // 3) 生成全文，校验关键内容确实出现
                string text = ReportBuilder.Build(ri);

                bool allInReport = true;
                for (int i = 0; i < lv.Items.Count; i++)
                {
                    string ip = lv.Items[i].SubItems[ListViewHelper.ColIp].Text;
                    if (text.IndexOf(ip) < 0) { allInReport = false; }
                }

                TestRunner.AssertTrue(allInReport, "界面列表中的每个 IP 都出现在报告中");

                // 4) 报告目标数不应小于列表行数
                TestRunner.AssertTrue(ri.TargetCount >= lv.Items.Count,
                    "报告目标数不小于列表行数");

                // 5) 写盘并读回，确认内容一致（UTF-8 BOM）
                string dir = Path.Combine(Path.GetTempPath(), "MQE2E_" + Guid.NewGuid().ToString("N"));

                try
                {
                    Directory.CreateDirectory(dir);

                    string path = Path.Combine(dir, "r.txt");

                    ReportSaveResult save = ReportSaveService.TryWrite(path, text);

                    TestRunner.AssertTrue(save.Success, "报告写盘成功");

                    if (save.Success)
                    {
                        string readBack = File.ReadAllText(path, new UTF8Encoding(true));

                        TestRunner.AssertEqual(text, readBack,
                            "读回内容与生成内容完全一致（无编码损失）");

                        byte[] bytes = File.ReadAllBytes(path);
                        TestRunner.AssertEqual((byte)0xEF, bytes[0], "BOM 首字节正确");
                    }
                }
                finally
                {
                    try { if (Directory.Exists(dir)) { Directory.Delete(dir, true); } }
                    catch (Exception) { }
                }

                // 打印报告片段供人工检查
                Console.WriteLine();
                Console.WriteLine("       报告片段:");
                string[] lines = text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);

                int printed = 0;
                for (int i = 0; i < lines.Length && printed < 12; i++)
                {
                    if (lines[i].Trim().Length == 0) { continue; }
                    Console.WriteLine("         " + lines[i]);
                    printed++;
                }
            }
            finally
            {
                page.Dispose();
            }
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
            InvokePrivateWithReturn(target, method, args);
        }

        private static object InvokePrivateWithReturn(object target, string method, params object[] args)
        {
            Type t = target.GetType();
            MethodInfo m = t.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            if (m == null) { throw new InvalidOperationException("找不到方法: " + method); }
            return m.Invoke(target, args);
        }
    }
}
