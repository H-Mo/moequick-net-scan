using System;
using System.Reflection;
using System.Windows.Forms;
using MQNetScan.Application;
using MQNetScan.Presentation.Pages;

namespace MQNetScan.Tests
{
    internal static class SettingsUpdateTests
    {
        public static void Run()
        {
            TestAboutAreaDefaults();
            TestUpdateResultStates();
            TestStartupCheckWiring();
        }

        private static void TestAboutAreaDefaults()
        {
            Console.WriteLine();
            Console.WriteLine("[UPDATE-UI-1] 关于区域初始状态");
            TestRunner.BeginCase("当前版本、检测更新、官网和 GitHub 按钮存在");
            using (SettingsPage page = new SettingsPage())
            {
                TestRunner.AssertTrue(page.CurrentVersionText.IndexOf("当前版本：v") >= 0,
                    "显示当前版本（实际: " + page.CurrentVersionText + "）");
                TestRunner.AssertEqual(false, page.DownloadButtonEnabled, "无新版本时下载按钮禁用");
                TestRunner.AssertEqual("https://div.moe/products/moequick-net-scan.html",
                    page.WebsiteAddress, "官网地址正确");
                TestRunner.AssertEqual("https://github.com/H-Mo/moequick-net-scan",
                    page.GithubAddress, "GitHub 地址正确");
                TestRunner.AssertTrue(GetControl(page, "grpAbout") != null, "关于分组存在");
                TestRunner.AssertTrue(GetControl(page, "btnCheckUpdates") != null, "检测更新按钮存在");
                TestRunner.AssertTrue(GetControl(page, "btnOpenWebsite") != null, "官网按钮存在");
                TestRunner.AssertTrue(GetControl(page, "btnOpenGithub") != null, "GitHub 按钮存在");
            }
        }

        private static void TestUpdateResultStates()
        {
            Console.WriteLine();
            Console.WriteLine("[UPDATE-UI-2] 更新结果显示状态");
            TestRunner.BeginCase("失败展示无更新，成功只在关于区提示");
            using (SettingsPage page = new SettingsPage())
            {
                page.ApplyUpdateCheckResult(UpdateCheckResult.NoUpdate());
                TestRunner.AssertTrue(page.UpdateStatusText.Contains("当前没有可用更新"),
                    "无更新/请求失败文案一致");
                TestRunner.AssertEqual(false, page.DownloadButtonEnabled, "无更新下载按钮禁用");

                page.ApplyUpdateCheckResult(new UpdateCheckResult(true, "1.2.0",
                    "https://github.com/H-Mo/moequick-net-scan/releases/download/v1.2.0/MQNetScan-v1.2.0.zip",
                    "2026-12-01", new string[] { "更新内容一", "更新内容二" }));
                TestRunner.AssertTrue(page.UpdateStatusText.Contains("v1.2.0"), "显示新版本号");
                TestRunner.AssertTrue(page.UpdateStatusText.Contains("更新内容一"), "显示更新说明");
                TestRunner.AssertEqual(true, page.DownloadButtonEnabled, "发现新版本启用下载按钮");
            }
        }

        private static void TestStartupCheckWiring()
        {
            Console.WriteLine();
            Console.WriteLine("[UPDATE-UI-3] 启动时静默检查接线");
            TestRunner.BeginCase("主窗体在 Shown 后启动一次更新检查");
            MethodInfo method = typeof(MQNetScan.Presentation.MainForm).GetMethod(
                "OnMainFormShown", BindingFlags.Instance | BindingFlags.NonPublic);
            TestRunner.AssertTrue(method != null, "存在启动后检查处理器");
            FieldInfo flag = typeof(MQNetScan.Presentation.MainForm).GetField(
                "_updateCheckStarted", BindingFlags.Instance | BindingFlags.NonPublic);
            TestRunner.AssertTrue(flag != null, "有重复调用保护");
        }

        private static Control GetControl(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(target) as Control;
        }
    }
}
