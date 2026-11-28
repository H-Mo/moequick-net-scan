using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MQNetScan.Application;
using MQNetScan.Presentation.Pages;

namespace MQNetScan.Tests
{
    internal static class LocalMachineInfoTests
    {
        public static void Run()
        {
            TestCacheLifetimePolicy();
            TestCollectorReturnsOrReportsFailure();
            TestLazyLoadAndManualRefresh();
            TestExpiredCacheReloadsOnReselect();
            TestRefreshQueuedDuringLoad();
        }

        private static void TestCacheLifetimePolicy()
        {
            Console.WriteLine();
            Console.WriteLine("[LOCAL-INFO-1] 分类缓存有效期");
            TestRunner.BeginCase("网络短缓存，硬件长缓存");
            TestRunner.AssertTrue(LocalMachineInfoPage.CacheLifetime(1) < LocalMachineInfoPage.CacheLifetime(0),
                "网络缓存短于系统缓存");
            TestRunner.AssertTrue(LocalMachineInfoPage.CacheLifetime(2) > LocalMachineInfoPage.CacheLifetime(1),
                "WMI 硬件缓存长于网络缓存");
            TestRunner.AssertEqual(TimeSpan.FromMinutes(30), LocalMachineInfoPage.CacheLifetime(2),
                "硬件信息缓存 30 分钟");
        }

        private static void TestCollectorReturnsOrReportsFailure()
        {
            Console.WriteLine();
            Console.WriteLine("[LOCAL-INFO-COLLECTOR] 系统信息采集异常隔离");
            TestRunner.BeginCase("四个类别均返回结果对象，不向调用方抛出采集异常");
            for (int i = 0; i < 4; i++)
            {
                LocalInfoSnapshot snapshot = LocalMachineInfoService.Read((LocalInfoCategory)i);
                TestRunner.AssertTrue(snapshot != null, "类别 " + i + " 返回快照");
                if (snapshot != null)
                {
                    TestRunner.AssertEqual((LocalInfoCategory)i, snapshot.Category, "类别标记一致");
                    TestRunner.AssertTrue(snapshot.Rows != null, "行集合非空对象");
                    TestRunner.AssertTrue(snapshot.Error != null, "错误信息字段有效");
                }
            }
        }

        private static void TestLazyLoadAndManualRefresh()
        {
            Console.WriteLine();
            Console.WriteLine("[LOCAL-INFO-2] 懒加载与手动刷新");
            TestRunner.BeginCase("首次激活只读系统，缓存命中不重复读取，刷新强制读取");
            int[] reads = new int[4];
            using (Form host = new Form())
            using (LocalMachineInfoPage page = new LocalMachineInfoPage(delegate(LocalInfoCategory category)
            {
                Interlocked.Increment(ref reads[(int)category]);
                Thread.Sleep(30);
                return CreateSnapshot(category, "read-" + reads[(int)category]);
            }))
            {
                host.Controls.Add(page);
                host.Show();
                TestRunner.AssertEqual(0, Sum(reads), "构造页面时不读取任何类别");
                page.ActivatePage();
                WaitForIdle(page, 0);
                TestRunner.AssertEqual(1, reads[(int)LocalInfoCategory.System], "首次激活加载系统类别");
                TestRunner.AssertEqual(0, reads[(int)LocalInfoCategory.Network], "未进入网络不启动读取");
                TestRunner.AssertEqual(0, reads[(int)LocalInfoCategory.Hardware], "未进入硬件不启动 WMI");
                TestRunner.AssertEqual(0, reads[(int)LocalInfoCategory.Runtime], "未进入运行环境不读取注册表");

                SelectCategory(page, 1);
                WaitForIdle(page, 1);
                TestRunner.AssertEqual(1, reads[(int)LocalInfoCategory.Network], "首次选网络时读取一次");
                SelectCategory(page, 0);
                SelectCategory(page, 1);
                Pump(30);
                TestRunner.AssertEqual(1, reads[(int)LocalInfoCategory.Network], "缓存有效时不重复读取");

                InvokePrivate(page, "OnRefreshClick", null, EventArgs.Empty);
                WaitForIdle(page, 1);
                TestRunner.AssertEqual(2, reads[(int)LocalInfoCategory.Network], "刷新按钮绕过缓存重新读取");
            }
        }

        private static void TestExpiredCacheReloadsOnReselect()
        {
            Console.WriteLine();
            Console.WriteLine("[LOCAL-INFO-3] 缓存过期重载");
            TestRunner.BeginCase("网络缓存过期后重新选择会重新获取");
            int reads = 0;
            using (Form host = new Form())
            using (LocalMachineInfoPage page = new LocalMachineInfoPage(delegate(LocalInfoCategory category)
            {
                Interlocked.Increment(ref reads);
                return CreateSnapshot(category, "expired");
            }))
            {
                host.Controls.Add(page);
                host.Show();
                page.ActivatePage();
                WaitForIdle(page, 0);
                SelectCategory(page, 1);
                WaitForIdle(page, 1);
                SetSnapshotTimeToOld(page, 1);
                SelectCategory(page, 0);
                SelectCategory(page, 1);
                WaitForIdle(page, 1);
                TestRunner.AssertEqual(3, reads, "网络类别再次读取");
            }
        }

        private static void SetSnapshotTimeToOld(LocalMachineInfoPage page, int index)
        {
            FieldInfo statesField = typeof(LocalMachineInfoPage).GetField("_states", BindingFlags.Instance | BindingFlags.NonPublic);
            Array states = (Array)statesField.GetValue(page);
            object state = states.GetValue(index);
            FieldInfo snapshotField = state.GetType().GetField("Snapshot", BindingFlags.Instance | BindingFlags.Public);
            LocalInfoSnapshot snapshot = (LocalInfoSnapshot)snapshotField.GetValue(state);
            typeof(LocalInfoSnapshot).GetProperty("CollectedAt").GetSetMethod(true)
                .Invoke(snapshot, new object[] { DateTime.Now - TimeSpan.FromMinutes(1) });
        }

        private static void TestRefreshQueuedDuringLoad()
        {
            Console.WriteLine();
            Console.WriteLine("[LOCAL-INFO-3] 同类别并发读取合并");
            TestRunner.BeginCase("读取过程中连续请求刷新只排队一次");
            int reads = 0;
            using (Form host = new Form())
            using (LocalMachineInfoPage page = new LocalMachineInfoPage(delegate(LocalInfoCategory category)
            {
                Interlocked.Increment(ref reads);
                Thread.Sleep(90);
                return CreateSnapshot(category, "queued");
            }))
            {
                host.Controls.Add(page);
                host.Show();
                page.ActivatePage();
                InvokePrivate(page, "OnRefreshClick", null, EventArgs.Empty);
                InvokePrivate(page, "OnRefreshClick", null, EventArgs.Empty);
                WaitForIdle(page, 0);
                TestRunner.AssertEqual(2, reads, "首次读取加一次合并刷新");
            }
        }

        private static LocalInfoSnapshot CreateSnapshot(LocalInfoCategory category, string value)
        {
            List<LocalInfoRow> rows = new List<LocalInfoRow>();
            rows.Add(new LocalInfoRow("测试", value));
            return new LocalInfoSnapshot(category, rows, "");
        }

        private static int Sum(int[] values)
        {
            int sum = 0;
            for (int i = 0; i < values.Length; i++) { sum += values[i]; }
            return sum;
        }

        private static void SelectCategory(LocalMachineInfoPage page, int index)
        {
            ListBox list = Get<ListBox>(page, "listCategories");
            list.SelectedIndex = index;
            Pump(10);
        }

        private static void WaitForIdle(LocalMachineInfoPage page, int index)
        {
            DateTime deadline = DateTime.Now.AddSeconds(3);
            while (DateTime.Now < deadline)
            {
                Pump(15);
                Label state = Get<Label>(page, "lblLoadState");
                if (!state.Text.StartsWith("正在读取", StringComparison.Ordinal)) { return; }
            }
            TestRunner.AssertTrue(false, "类别 " + index + " 加载应在超时前结束");
        }

        private static void Pump(int milliseconds)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(milliseconds);
            System.Windows.Forms.Application.DoEvents();
        }

        private static T Get<T>(object target, string name) where T : class
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) { throw new InvalidOperationException("找不到字段：" + name); }
            return field.GetValue(target) as T;
        }

        private static void InvokePrivate(object target, string method, params object[] args)
        {
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            if (info == null) { throw new InvalidOperationException("找不到方法：" + method); }
            info.Invoke(target, args);
        }
    }
}
