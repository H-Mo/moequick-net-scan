using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using MQNetScan.Presentation.Pages;

namespace MQNetScan.Tests
{
    internal static class ScanLayoutTests
    {
        public static void Run()
        {
            Console.WriteLine();
            Console.WriteLine("[LAYOUT-1] 扫描页控件布局不遮挡");
            TestRunner.BeginCase("进度条、报告按钮、状态和结果列表区域不重叠");

            using (ScanPage page = new ScanPage())
            {
                Control progress = Get<Control>(page, "progressBar");
                Control export = Get<Control>(page, "btnExport");
                Control folder = Get<Control>(page, "btnOpenReportFolder");
                Control status = Get<Control>(page, "lblScanStatus");
                Control list = Get<Control>(page, "listHosts");

                TestRunner.AssertTrue(!progress.Bounds.IntersectsWith(export.Bounds),
                    "进度条不遮挡导出按钮");
                TestRunner.AssertTrue(!progress.Bounds.IntersectsWith(folder.Bounds),
                    "进度条不遮挡文件夹按钮");
                TestRunner.AssertTrue(!status.Bounds.IntersectsWith(export.Bounds),
                    "状态文本不遮挡导出按钮");
                TestRunner.AssertTrue(!status.Bounds.IntersectsWith(folder.Bounds),
                    "状态文本不遮挡文件夹按钮");
                TestRunner.AssertTrue(!list.Bounds.IntersectsWith(export.Bounds),
                    "结果列表不遮挡导出按钮");
                TestRunner.AssertTrue(!list.Bounds.IntersectsWith(folder.Bounds),
                    "结果列表不遮挡文件夹按钮");
                Control target = Get<Control>(page, "grpTarget");
                TestRunner.AssertTrue(target.Height <= 150, "扫描目标区域已收紧（实际 " + target.Height + "px）");
                TestRunner.AssertTrue(list.Height >= 230, "扫描列表高度增加（实际 " + list.Height + "px）");
                TestRunner.AssertTrue(list.Bottom <= page.ClientSize.Height,
                    "结果列表位于页面可见区域");
            }
        }

        private static T Get<T>(object target, string name) where T : class
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) { throw new InvalidOperationException("找不到字段: " + name); }
            return field.GetValue(target) as T;
        }
    }
}
