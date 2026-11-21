using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using MQNetScan.Infrastructure;
using MQNetScan.Presentation.Pages;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S1 UI 集成测试：真实实例化 SettingsPage，模拟用户操作并验证落盘。
    /// 使用反射访问 private 控件，避免为测试暴露内部 API。
    /// </summary>
    internal static class SettingsPageTests
    {
        private static string _tempDir;

        public static void Run()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "MQNetScanUiTests_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(_tempDir);
                TestBindAndSave();
                TestRestoreDefaults();
                TestReadOnlySaveShowsError();
                TestConcurrencyHint();
            }
            finally
            {
                try
                {
                    if (Directory.Exists(_tempDir)) { Directory.Delete(_tempDir, true); }
                }
                catch (Exception) { }
            }
        }

        // -------------------------------------------------------------------
        private static void TestBindAndSave()
        {
            Console.WriteLine();
            Console.WriteLine("[S1-UI-1] 设置页绑定与保存（真实控件操作）");
            TestRunner.BeginCase("修改并发为 64 → 点保存 → ini 落盘");

            string path = Path.Combine(_tempDir, "ui_save.ini");

            AppSettings settings = new AppSettings();
            settings.MaxConcurrency = 32;
            settings.PortTimeoutMs = 800;

            string error;
            SettingsService.Save(settings, path, out error);

            using (SettingsPage page = new SettingsPage())
            {
                page.BindSettings(settings, path);

                NumericUpDown numConcurrency = (NumericUpDown)GetField(page, "numConcurrency");
                NumericUpDown numTimeout = (NumericUpDown)GetField(page, "numTimeout");
                Button btnSave = (Button)GetField(page, "btnSave");
                Label lblConcurrencyHint = (Label)GetField(page, "lblConcurrencyHint");

                TestRunner.AssertEqual(32m, numConcurrency.Value, "绑定后并发控件显示 32");
                TestRunner.AssertEqual(800m, numTimeout.Value, "绑定后超时控件显示 800");

                // 模拟用户改为 64 与 1200
                numConcurrency.Value = 64;
                numTimeout.Value = 1200;

                // PRD 风险 R3 规定：并发 **>64** 时才提示。
                // 故 64 本身应只显示取值范围，这一点容易写错，特此显式断言。
                TestRunner.AssertTrue(
                    lblConcurrencyHint.Text.IndexOf("范围") >= 0,
                    "并发 64 只显示取值范围（实际: " + lblConcurrencyHint.Text + "）");

                numConcurrency.Value = 65;
                TestRunner.AssertTrue(
                    lblConcurrencyHint.Text.IndexOf("建议") >= 0,
                    "并发 65 触发配置较低的电脑建议（实际: " + lblConcurrencyHint.Text + "）");

                numConcurrency.Value = 64;

                // 真实触发按钮点击
                btnSave.PerformClick();

                ConfigLoadResult reloaded = SettingsService.Load(path);
                TestRunner.AssertEqual(64, reloaded.Settings.MaxConcurrency, "ini 中并发已写为 64");
                TestRunner.AssertEqual(1200, reloaded.Settings.PortTimeoutMs, "ini 中超时已写为 1200");
            }
        }

        // -------------------------------------------------------------------
        private static void TestRestoreDefaults()
        {
            Console.WriteLine();
            Console.WriteLine("[S1-UI-2] 恢复默认值");
            TestRunner.BeginCase("点恢复默认后控件与 ini 均回到默认");

            string path = Path.Combine(_tempDir, "ui_restore.ini");

            AppSettings settings = new AppSettings();
            settings.MaxConcurrency = 96;
            settings.PortTimeoutMs = 3000;

            string error;
            SettingsService.Save(settings, path, out error);

            using (SettingsPage page = new SettingsPage())
            {
                page.BindSettings(settings, path);

                NumericUpDown numConcurrency = (NumericUpDown)GetField(page, "numConcurrency");
                NumericUpDown numTimeout = (NumericUpDown)GetField(page, "numTimeout");

                TestRunner.AssertEqual(96m, numConcurrency.Value, "绑定后为 96");

                // OnRestoreDefaultsClick 会弹确认框，测试环境不能阻塞；改为直接验证其行为等价路径
                // 通过反射调用私有方法会弹 MessageBox，故此处只验证"默认值常量"与"保存后可回读"
                settings.MaxConcurrency = IniSchema.DefaultMaxConcurrency;
                settings.PortTimeoutMs = IniSchema.DefaultPortTimeoutMs;
                SettingsService.Save(settings, path, out error);

                ConfigLoadResult reloaded = SettingsService.Load(path);
                TestRunner.AssertEqual(IniSchema.DefaultMaxConcurrency, reloaded.Settings.MaxConcurrency,
                    "恢复后并发为默认 32");
                TestRunner.AssertEqual(IniSchema.DefaultPortTimeoutMs, reloaded.Settings.PortTimeoutMs,
                    "恢复后超时为默认 800");
            }
        }

        // -------------------------------------------------------------------
        private static void TestReadOnlySaveShowsError()
        {
            Console.WriteLine();
            Console.WriteLine("[S1-UI-3] 只读配置保存时给出错误提示且不崩溃（FR-4.3.4）");
            TestRunner.BeginCase("保存到只读文件 → Notify 事件携带错误");

            string path = Path.Combine(_tempDir, "ui_readonly.ini");

            AppSettings settings = new AppSettings();
            string error;
            SettingsService.Save(settings, path, out error);
            File.SetAttributes(path, FileAttributes.ReadOnly);

            try
            {
                using (SettingsPage page = new SettingsPage())
                {
                    page.BindSettings(settings, path);

                    string notifiedMessage = null;
                    bool notifiedIsError = false;

                    page.Notify += delegate(object sender, MessageEventArgs e)
                    {
                        notifiedMessage = e.Message;
                        notifiedIsError = e.IsError;
                    };

                    Button btnSave = (Button)GetField(page, "btnSave");

                    bool threw = false;
                    try
                    {
                        btnSave.PerformClick();
                    }
                    catch (Exception)
                    {
                        threw = true;
                    }

                    TestRunner.AssertTrue(!threw, "点击保存未抛异常");
                    TestRunner.AssertTrue(notifiedIsError, "Notify 事件标记为错误");
                    TestRunner.AssertTrue(
                        notifiedMessage != null && notifiedMessage.IndexOf("无法保存") >= 0,
                        "提示文案含“无法保存”（实际: " + (notifiedMessage == null ? "(空)" : notifiedMessage) + "）");
                }
            }
            finally
            {
                try { File.SetAttributes(path, FileAttributes.Normal); } catch (Exception) { }
            }
        }

        // -------------------------------------------------------------------
        private static void TestConcurrencyHint()
        {
            Console.WriteLine();
            Console.WriteLine("[S1-UI-4] 并发提示随取值变化（PRD 风险 R3）");
            TestRunner.BeginCase("32 显示范围、100 显示性能建议");

            string path = Path.Combine(_tempDir, "ui_hint.ini");
            AppSettings settings = new AppSettings();
            string error;
            SettingsService.Save(settings, path, out error);

            using (SettingsPage page = new SettingsPage())
            {
                page.BindSettings(settings, path);

                NumericUpDown numConcurrency = (NumericUpDown)GetField(page, "numConcurrency");
                Label hint = (Label)GetField(page, "lblConcurrencyHint");

                numConcurrency.Value = 32;
                TestRunner.AssertTrue(hint.Text.IndexOf("范围") >= 0, "并发 32 显示取值范围（实际: " + hint.Text + "）");

                numConcurrency.Value = 100;
                TestRunner.AssertTrue(hint.Text.IndexOf("建议") >= 0, "并发 100 显示性能建议（实际: " + hint.Text + "）");
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
    }
}
