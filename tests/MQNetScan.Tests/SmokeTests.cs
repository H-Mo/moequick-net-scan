using System;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S0 冒烟测试：验证骨架的关键不变量。
    /// 真实业务单测（字节序、网段、角色判定等）在后续阶段加入。
    /// </summary>
    internal static class SmokeTests
    {
        public static void Run()
        {
            Console.WriteLine();
            Console.WriteLine("[1] 版本与标题一致性（边界检查 B-9）");
            TestRunner.BeginCase("AssemblyTitle 与 UiText.AppTitle 必须一致");

            System.Reflection.Assembly asm = typeof(MQNetScan.Presentation.Common.UiText).Assembly;

            object[] titles = asm.GetCustomAttributes(
                typeof(System.Reflection.AssemblyTitleAttribute), false);

            string assemblyTitle = titles.Length > 0
                ? ((System.Reflection.AssemblyTitleAttribute)titles[0]).Title
                : "";

            TestRunner.AssertEqual(
                MQNetScan.Presentation.Common.UiText.AppTitle,
                assemblyTitle,
                "AssemblyTitle == UiText.AppTitle");

            TestRunner.AssertTrue(
                MQNetScan.Presentation.Common.UiText.AppTitle
                    == "MoeQuick NetScan v1.3.0 - 萌快网维",
                "标题严格为指定字符串");

            Console.WriteLine();
            Console.WriteLine("[2] 目标框架校验");

            Version runtime = Environment.Version;
            TestRunner.AssertTrue(
                runtime.Major == 4,
                "运行时主版本为 4（实际 " + runtime.ToString() + "）");

            Version mscorlib = typeof(object).Assembly.GetName().Version;
            TestRunner.AssertTrue(
                mscorlib.Major == 4 && mscorlib.Minor == 0,
                "mscorlib 为 4.0.x（实际 " + mscorlib.ToString() + "）");

            Console.WriteLine();
            Console.WriteLine("[3] UiText 文案非空");
            TestRunner.AssertTrue(
                !string.IsNullOrEmpty(MQNetScan.Presentation.Common.UiText.TabScan),
                "Tab 名称非空");
            TestRunner.AssertTrue(
                !string.IsNullOrEmpty(MQNetScan.Presentation.Common.UiText.StatusReady),
                "状态栏就绪文案非空");
        }
    }
}
