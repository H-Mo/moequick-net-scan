using System;
using System.Reflection;
using System.Windows.Forms;
using MQNetScan.Infrastructure;
using MQNetScan.Presentation.Pages;

namespace MQNetScan.Tests
{
    internal static class SettingsLayoutTests
    {
        public static void Run()
        {
            TestCompactScanParamsAndRulesArea();
            TestAliyunSelfCheckDefault();
        }

        private static void TestCompactScanParamsAndRulesArea()
        {
            Console.WriteLine();
            Console.WriteLine("[LAYOUT-SETTINGS] 设置页空间利用");
            TestRunner.BeginCase("参数两排展示并扩大端口规则区域");
            using (SettingsPage page = new SettingsPage())
            {
                Control parameters = Get(page, "grpScanParams");
                Control concurrency = Get(page, "numConcurrency");
                Control timeout = Get(page, "numTimeout");
                Control pingTimeout = Get(page, "numPingTimeout");
                Control pingRetry = Get(page, "numPingRetry");
                Control rules = Get(page, "grpRules");
                Control rulesList = Get(page, "listRules");
                Control factory = Get(page, "grpFactory");

                TestRunner.AssertTrue(parameters.Height <= 125, "扫描参数区收紧（实际 " + parameters.Height + "px）");
                TestRunner.AssertTrue(Math.Abs(concurrency.Top - timeout.Top) < 8,
                    "并发和端口超时处于同一排");
                TestRunner.AssertTrue(Math.Abs(pingTimeout.Top - pingRetry.Top) < 8,
                    "Ping 参数处于同一排");
                TestRunner.AssertTrue(pingTimeout.Top > concurrency.Top, "Ping 参数在第二排");
                TestRunner.AssertTrue(rulesList.Height >= 195, "端口规则列表增高（实际 " + rulesList.Height + "px）");
                TestRunner.AssertTrue(!rules.Bounds.IntersectsWith(factory.Bounds), "规则区不遮挡危险操作区");
                TestRunner.AssertTrue(factory.Top >= rules.Bottom, "危险操作区位于规则区下方");
            }
        }

        private static void TestAliyunSelfCheckDefault()
        {
            Console.WriteLine();
            Console.WriteLine("[SELF-CHECK-DEFAULT] 阿里云目标");
            TestRunner.BeginCase("默认自测域名为阿里云且旧默认值自动迁移");
            TestRunner.AssertEqual("www.aliyun.com", IniSchema.DefaultSaasDomain, "默认自测目标为阿里云");
        }

        private static Control Get(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) { throw new InvalidOperationException("找不到控件：" + fieldName); }
            return field.GetValue(target) as Control;
        }
    }
}
