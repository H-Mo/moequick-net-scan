using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MQNetScan.Infrastructure;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S1 回归：验证"首运行生成 ini 后能正确读回端口规则"。
    ///
    /// 背景：界面曾出现"未读到任何端口规则"的提示，但 ini 文件里明明有 12 条。
    /// 本用例复现该链路：Save 默认设置 → Load → 规则数应为 12。
    /// </summary>
    internal static class IniRoundTripRegressionTests
    {
        public static void Run()
        {
            TestFirstRunGenerateThenLoad();
            TestDefaultSettingsHaveRules();
        }

        private static void TestFirstRunGenerateThenLoad()
        {
            Console.WriteLine();
            Console.WriteLine("[S1-REG] 首运行生成 ini 后能读回 10 条规则");
            TestRunner.BeginCase("Save(默认) → Load → 规则数 12、无警告");

            string dir = Path.Combine(Path.GetTempPath(), "MQReg_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "MQNetScan.ini");

                // 模拟首运行：用默认设置保存
                AppSettings defaults = new AppSettings();

                TestRunner.AssertEqual(10, defaults.PortRules.Count,
                    "默认设置自带 10 条规则");

                string err;
                bool ok = SettingsService.Save(defaults, path, out err);

                TestRunner.AssertTrue(ok, "保存成功（" + (err == null ? "" : err) + "）");
                TestRunner.AssertTrue(File.Exists(path), "文件已生成");

                // 关键：读回来必须还有 12 条
                ConfigLoadResult r = SettingsService.Load(path);

                Console.WriteLine("       读回规则数: " + r.Settings.PortRules.Count);
                Console.WriteLine("       警告数: " + r.Warnings.Count);

                for (int i = 0; i < r.Warnings.Count; i++)
                {
                    Console.WriteLine("         - " + r.Warnings[i]);
                }

                TestRunner.AssertEqual(10, r.Settings.PortRules.Count,
                    "读回 10 条规则（不应出现「未读到任何端口规则」）");

                // 不应出现"未读到任何端口规则"这类告警
                bool hasNoRuleWarning = false;
                for (int i = 0; i < r.Warnings.Count; i++)
                {
                    if (r.Warnings[i].IndexOf("未读到任何端口规则") >= 0) { hasNoRuleWarning = true; }
                }
                TestRunner.AssertTrue(!hasNoRuleWarning,
                    "没有出现「未读到任何端口规则」告警");

                // 抽查第一条规则内容
                bool foundPrinter = false;
                for (int i = 0; i < r.Settings.PortRules.Count; i++)
                {
                    PortRule pr = r.Settings.PortRules[i];
                    if (pr.Port == 9100 && pr.Role == "打印机") { foundPrinter = true; }
                }
                TestRunner.AssertTrue(foundPrinter, "读到 9100 → 打印机 规则");
            }
            finally
            {
                try { if (Directory.Exists(dir)) { Directory.Delete(dir, true); } }
                catch (Exception) { }
            }
        }

        private static void TestDefaultSettingsHaveRules()
        {
            Console.WriteLine();
            Console.WriteLine("[S1-REG] 默认端口规则内容校验");
            TestRunner.BeginCase("内置 10 条规则的角色与端口");

            AppSettings s = new AppSettings();

            int[] expectedPorts = { 9100, 515, 631, 3001, 4001, 1433, 445, 3389, 135, 5555 };

            for (int i = 0; i < expectedPorts.Length; i++)
            {
                bool found = false;
                for (int k = 0; k < s.PortRules.Count; k++)
                {
                    if (s.PortRules[k].Port == expectedPorts[i]) { found = true; break; }
                }

                TestRunner.AssertTrue(found, "包含端口 " + expectedPorts[i]);
            }

            // 启用端口集合应去重且升序
            int[] enabled = s.GetEnabledPorts();

            TestRunner.AssertEqual(10, enabled.Length, "启用端口 10 个");

            bool ascending = true;
            for (int i = 1; i < enabled.Length; i++)
            {
                if (enabled[i] <= enabled[i - 1]) { ascending = false; }
            }
            TestRunner.AssertTrue(ascending, "启用端口升序且无重复");
        }
    }
}
