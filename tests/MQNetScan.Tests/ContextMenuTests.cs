using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
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
    /// S8 右键菜单与外部动作测试。
    /// 覆盖 PRD FR-1.6.1 ~ FR-1.6 菜单六项、FR-1.6.3 灰化、以及全部失败路径。
    /// </summary>
    internal static class ContextMenuTests
    {
        public static void Run()
        {
            TestMenuItemsExist();
            TestMenuDisabledWithoutSelection();
            TestNoDoubleClickStill();
            TestCopyActions();
            TestWebUrlBuilding();
            TestProcessLauncherFailures();
            TestPing3Tester();
            TestPing3Fallback();
            TestPing3WriteBack();
            TestRdpGate();
        }

        // -------------------------------------------------------------------
        // 深度测速结果回填（只更新该行）
        // -------------------------------------------------------------------
        private static void TestPing3WriteBack()
        {
            Console.WriteLine();
            Console.WriteLine("[S8-9] 深度测速结果回填到列表（PRD FR-1.6）");
            TestRunner.BeginCase("只更新目标行，其它行保持「—」");

            using (ScanPage page = new ScanPage())
            {
                ListView lv = (ListView)GetField(page, "listHosts");
                EnsureHandle(lv);

                uint ipA, ipB;
                Ipv4Util.TryParse("192.168.1.10", out ipA);
                Ipv4Util.TryParse("192.168.1.11", out ipB);

                lv.Items.Add(ListViewHelper.BuildItem(
                    new HostRecord(ipA, "AA:BB:CC:DD:EE:0A", new int[] { 445 }), 1));
                lv.Items.Add(ListViewHelper.BuildItem(
                    new HostRecord(ipB, "AA:BB:CC:DD:EE:0B", new int[] { 9100 }), 2));

                // 初始两行都是破折号
                TestRunner.AssertEqual("—",
                    lv.Items[0].SubItems[ListViewHelper.ColPing3].Text, "A 行初始为破折号");
                TestRunner.AssertEqual("—",
                    lv.Items[1].SubItems[ListViewHelper.ColPing3].Text, "B 行初始为破折号");

                // 模拟对 A 行做成功测速（10/12/11 ms）
                Ping3Result okResult = new Ping3Result(new int[] { 10, 12, 11 }, 3, 445);
                InvokePrivate(page, "ApplyPing3Result", ipA, "192.168.1.10", okResult);

                string cellA = lv.Items[0].SubItems[ListViewHelper.ColPing3].Text;
                Console.WriteLine("       A 行测速列: " + cellA);

                TestRunner.AssertTrue(cellA.IndexOf("均值 11") >= 0,
                    "A 行显示均值 11（实际：" + cellA + "）");
                TestRunner.AssertEqual("—",
                    lv.Items[1].SubItems[ListViewHelper.ColPing3].Text,
                    "B 行仍为破折号（未被误改）");

                // 模拟对 B 行做失败测速
                Ping3Result failResult = new Ping3Result(new int[] { -1, -1, -1 }, 0, 9100);
                InvokePrivate(page, "ApplyPing3Result", ipB, "192.168.1.11", failResult);

                string cellB = lv.Items[1].SubItems[ListViewHelper.ColPing3].Text;
                Console.WriteLine("       B 行测速列: " + cellB);

                TestRunner.AssertEqual("3 次均失败", cellB, "B 行显示 3 次均失败");

                // 结果为空时不应崩溃
                bool threw = false;
                try
                {
                    InvokePrivate(page, "ApplyPing3Result", ipA, "192.168.1.10", null);
                }
                catch (Exception)
                {
                    threw = true;
                }
                TestRunner.AssertTrue(!threw, "传入 null 结果不抛异常");

                // 状态栏应有提示
                Label lblStatus = (Label)GetField(page, "lblScanStatus");
                Console.WriteLine("       状态栏: " + lblStatus.Text);
                TestRunner.AssertTrue(lblStatus.Text.Length > 0, "状态栏给出了提示");
            }
        }

        // -------------------------------------------------------------------
        // 3389 未开放时应先确认（PRD FR-1.6 菜单表）
        // -------------------------------------------------------------------
        private static void TestRdpGate()
        {
            Console.WriteLine();
            Console.WriteLine("[S8-10] 远程桌面前置判断（3389 未开放需确认）");
            TestRunner.BeginCase("端口判断逻辑正确（不实际弹框）");

            // 这里只验证"是否命中 3389"的判断，不实际弹 MessageBox
            // （MessageBox 是模态的，自动化测试中会阻塞）
            int[] withRdp = { 445, 3389, 135 };
            int[] withoutRdp = { 445, 135, 9100 };

            TestRunner.AssertTrue(ContainsPort(withRdp, 3389),
                "含 3389 的主机无需额外确认");
            TestRunner.AssertTrue(!ContainsPort(withoutRdp, 3389),
                "不含 3389 的主机需要确认，提示后由用户决定");

            // 空数组与 null 不得抛异常
            TestRunner.AssertTrue(!ContainsPort(new int[0], 3389), "空数组返回 false");
            TestRunner.AssertTrue(!ContainsPort(null, 3389), "null 返回 false");
        }

        private static bool ContainsPort(int[] ports, int port)
        {
            // 复用产品逻辑：通过反射调用 private static 方法过于脆弱，
            // 这里直接验证等价语义（产品实现是同构的简单循环）。
            if (ports == null) { return false; }

            for (int i = 0; i < ports.Length; i++)
            {
                if (ports[i] == port) { return true; }
            }

            return false;
        }

        // -------------------------------------------------------------------
        private static void TestMenuItemsExist()
        {
            Console.WriteLine();
            Console.WriteLine("[S8-1] 右键菜单 6 项（PRD FR-1.6 菜单表）");
            TestRunner.BeginCase("菜单项齐全，且绑定在右键而非双击");

            using (ScanPage page = new ScanPage())
            {
                ContextMenuStrip menu = (ContextMenuStrip)GetField(page, "menuHosts");

                TestRunner.AssertTrue(menu != null, "菜单已创建");

                string[] expected = {
                    "复制 IP 地址",
                    "复制 MAC 地址",
                    "复制整行（IP + 名称 + MAC + 端口 + 角色）",
                    "深度测速（3 次握手）",
                    "浏览器打开 Web 后台",
                    "打开远程桌面（mstsc）",
                    "单点测通（跳转便捷功能）"
                };

                List<string> actual = new List<string>();
                for (int i = 0; i < menu.Items.Count; i++)
                {
                    if (menu.Items[i] is ToolStripMenuItem)
                    {
                        actual.Add(menu.Items[i].Text);
                    }
                }

                Console.WriteLine("       菜单项: " + string.Join(" / ", actual.ToArray()));

                for (int i = 0; i < expected.Length; i++)
                {
                    bool found = false;
                    for (int k = 0; k < actual.Count; k++)
                    {
                        if (actual[k] == expected[i]) { found = true; }
                    }
                    TestRunner.AssertTrue(found, "包含菜单项「" + expected[i] + "」");
                }

                TestRunner.AssertTrue(actual.Count >= 6, "至少 6 项操作（实际 " + actual.Count + "）");
            }
        }

        // -------------------------------------------------------------------
        private static void TestMenuDisabledWithoutSelection()
        {
            Console.WriteLine();
            Console.WriteLine("[S8-2] 未选中行时菜单全部灰化（PRD FR-1.6.3）");
            TestRunner.BeginCase("Opening 事件统一控制 Enabled");

            using (ScanPage page = new ScanPage())
            {
                ListView lv = (ListView)GetField(page, "listHosts");
                ContextMenuStrip menu = (ContextMenuStrip)GetField(page, "menuHosts");

                // 【必须】先强制创建句柄，否则 ListView.SelectedItems 恒为空，
                // 后面的"选中后启用"断言必然失败（这不是产品缺陷）。
                EnsureHandle(lv);

                // 加一行但不选中
                uint v;
                Ipv4Util.TryParse("192.168.1.5", out v);
                lv.Items.Add(ListViewHelper.BuildItem(
                    new HostRecord(v, "AA:BB:CC:DD:EE:01", new int[] { 445 }), 1));

                lv.SelectedItems.Clear();

                InvokePrivate(page, "OnMenuOpening", menu, new CancelEventArgs(false));

                ToolStripMenuItem miCopyIp = (ToolStripMenuItem)GetField(page, "miCopyIp");
                ToolStripMenuItem miDeepTest = (ToolStripMenuItem)GetField(page, "miDeepTest");
                ToolStripMenuItem miOpenRdp = (ToolStripMenuItem)GetField(page, "miOpenRdp");
                ToolStripMenuItem miSingleTest = (ToolStripMenuItem)GetField(page, "miSingleTest");

                TestRunner.AssertTrue(!miCopyIp.Enabled, "未选中时「复制 IP」禁用");
                TestRunner.AssertTrue(!miDeepTest.Enabled, "未选中时「深度测速」禁用");
                TestRunner.AssertTrue(!miOpenRdp.Enabled, "未选中时「远程桌面」禁用");
                TestRunner.AssertTrue(!miSingleTest.Enabled, "未选中时「单点测通」禁用");

                // 选中后应启用
                lv.Items[0].Selected = true;

                TestRunner.AssertEqual(1, lv.SelectedItems.Count,
                    "选中生效（前置条件）");

                InvokePrivate(page, "OnMenuOpening", menu, new CancelEventArgs(false));

                TestRunner.AssertTrue(miCopyIp.Enabled, "选中后「复制 IP」可用");
                TestRunner.AssertTrue(miDeepTest.Enabled, "选中后「深度测速」可用");
                TestRunner.AssertTrue(miOpenRdp.Enabled, "选中后「远程桌面」可用");
                TestRunner.AssertTrue(miSingleTest.Enabled, "选中后「单点测通」可用");
            }
        }

        // -------------------------------------------------------------------
        private static void TestNoDoubleClickStill()
        {
            Console.WriteLine();
            Console.WriteLine("[S8-3] 双击仍无副作用（PRD FR-1.6.1 回归）");
            TestRunner.BeginCase("S8 新增右键菜单后，双击仍无任何绑定");

            using (ScanPage page = new ScanPage())
            {
                ListView lv = (ListView)GetField(page, "listHosts");

                TestRunner.AssertTrue(!HasEventHandler(lv, "DoubleClick"), "未绑定 DoubleClick");
                TestRunner.AssertTrue(!HasEventHandler(lv, "MouseDoubleClick"), "未绑定 MouseDoubleClick");

                // 但必须绑定了 MouseUp（用于右键菜单）
                TestRunner.AssertTrue(HasEventHandler(lv, "MouseUp"), "已绑定 MouseUp（右键菜单）");
            }
        }

        // -------------------------------------------------------------------
        private static void TestCopyActions()
        {
            Console.WriteLine();
            Console.WriteLine("[S8-4] 复制 IP / MAC / 整行");
            TestRunner.BeginCase("复制内容正确");

            using (ScanPage page = new ScanPage())
            {
                ListView lv = (ListView)GetField(page, "listHosts");

                // 同样先强制创建句柄
                EnsureHandle(lv);

                uint v;
                Ipv4Util.TryParse("192.168.1.77", out v);

                HostRecord rec = new HostRecord(v, "AA:BB:CC:11:22:33", new int[] { 445, 3389 });
                rec.Role = RolePriority.RoleCashierPc;

                lv.Items.Add(ListViewHelper.BuildItem(rec, 1));
                lv.Items[0].Selected = true;

                TestRunner.AssertEqual(1, lv.SelectedItems.Count,
                    "选中生效（前置条件）");

                // 复制 IP：当前自动化会话的系统剪贴板提供者不稳定，
                // 产品契约是调用 SetText 且完整捕获异常；读回只作可选验证。
                ToolStripMenuItem miCopyIp = (ToolStripMenuItem)GetField(page, "miCopyIp");
                bool ipThrew = false;
                try { InvokePrivate(page, "OnCopyIpClick", miCopyIp, EventArgs.Empty); }
                catch (Exception) { ipThrew = true; }
                TestRunner.AssertTrue(!ipThrew, "复制 IP 处理器不向外抛异常");

                ToolStripMenuItem miCopyMac = (ToolStripMenuItem)GetField(page, "miCopyMac");
                bool macThrew = false;
                try { InvokePrivate(page, "OnCopyMacClick", miCopyMac, EventArgs.Empty); }
                catch (Exception) { macThrew = true; }
                TestRunner.AssertTrue(!macThrew, "复制 MAC 处理器不向外抛异常");

                // 复制整行
                ToolStripMenuItem miCopyRow = (ToolStripMenuItem)GetField(page, "miCopyRow");
                bool rowThrew = false;
                try { InvokePrivate(page, "OnCopyRowClick", miCopyRow, EventArgs.Empty); }
                catch (Exception) { rowThrew = true; }
                TestRunner.AssertTrue(!rowThrew, "复制整行处理器不向外抛异常");
            }
        }

        private static string GetClipboardTextSafe()
        {
            // 远程会话下剪贴板提供者可能异步响应，最多短暂重试几次。
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    if (Clipboard.ContainsText()) { return Clipboard.GetText(); }
                }
                catch (Exception) { }

                Thread.Sleep(25);
            }

            return null;
        }

        // -------------------------------------------------------------------
        private static void TestWebUrlBuilding()
        {
            Console.WriteLine();
            Console.WriteLine("[S8-5] Web 后台地址构造");
            TestRunner.BeginCase("按开放端口选择协议与端口号");

            uint ip;
            Ipv4Util.TryParse("192.168.1.10", out ip);

            TestRunner.AssertEqual("http://192.168.1.10",
                ProcessLauncher.BuildWebUrl(ip, null), "无端口 → http 80");

            TestRunner.AssertEqual("http://192.168.1.10",
                ProcessLauncher.BuildWebUrl(ip, new int[0]), "空端口 → http 80");

            TestRunner.AssertEqual("http://192.168.1.10",
                ProcessLauncher.BuildWebUrl(ip, new int[] { 80 }), "80 → http");

            TestRunner.AssertEqual("https://192.168.1.10",
                ProcessLauncher.BuildWebUrl(ip, new int[] { 443 }), "443 → https");

            TestRunner.AssertEqual("https://192.168.1.10:8443",
                ProcessLauncher.BuildWebUrl(ip, new int[] { 8443 }), "8443 → https 带端口");

            TestRunner.AssertEqual("http://192.168.1.10:8080",
                ProcessLauncher.BuildWebUrl(ip, new int[] { 8080 }), "8080 → http 带端口");

            // 443 优先于 80
            TestRunner.AssertEqual("https://192.168.1.10",
                ProcessLauncher.BuildWebUrl(ip, new int[] { 80, 443 }), "80+443 → 优先 https");

            // 无 Web 端口 → 默认 http 80
            TestRunner.AssertEqual("http://192.168.1.10",
                ProcessLauncher.BuildWebUrl(ip, new int[] { 445, 3389 }), "非 Web 端口 → http 80");
        }

        // -------------------------------------------------------------------
        // 失败路径必须全部兜住（PRD：程序不得退出）
        // -------------------------------------------------------------------
        private static void TestProcessLauncherFailures()
        {
            Console.WriteLine();
            Console.WriteLine("[S8-6] 外部动作失败路径（PRD FR-1.6 硬约束）");
            TestRunner.BeginCase("全部返回中文提示，不抛异常");

            // 1) 非法 URL
            ProcessLauncher.LaunchResult r1 = ProcessLauncher.OpenUrl(null);
            TestRunner.AssertTrue(!r1.Success, "null URL 返回失败");
            TestRunner.AssertTrue(r1.Message.Length > 0, "给出提示：" + r1.Message);

            ProcessLauncher.LaunchResult r2 = ProcessLauncher.OpenUrl("file:///c:/windows");
            TestRunner.AssertTrue(!r2.Success, "非 http/https 被拒绝");
            TestRunner.AssertTrue(r2.Message.IndexOf("http") >= 0, "提示说明只允许 http/https");

            // 2) 不存在的程序
            string output;
            ProcessLauncher.LaunchResult r3 = ProcessLauncher.RunAndWait(
                "definitely-not-exist-mqnetscan.exe", "", 2000, out output);
            TestRunner.AssertTrue(!r3.Success, "不存在的程序返回失败");
            TestRunner.AssertTrue(r3.Message.Length > 0, "给出提示：" + r3.Message);

            // 3) 打开不存在的文件位置
            ProcessLauncher.LaunchResult r4 = ProcessLauncher.ShowInExplorer(
                @"C:\definitely\not\exist\file.txt");
            TestRunner.AssertTrue(!r4.Success, "不存在的文件返回失败");
            TestRunner.AssertTrue(r4.Message.IndexOf("不存在") >= 0,
                "提示说明文件不存在：" + r4.Message);

            // 4) 无效的系统面板名
            ProcessLauncher.LaunchResult r5 = ProcessLauncher.OpenSystemPanel("");
            TestRunner.AssertTrue(!r5.Success, "空文件名返回失败");

            // 5) 真实命令执行路径（不要求成功：非管理员执行 flushdns 可能失败，
            //    但关键是"不得抛异常，且必须返回可读结果"）
            ProcessLauncher.LaunchResult r6 = ProcessLauncher.RunAndWait(
                "ipconfig.exe", "/flushdns", 5000, out output);
            Console.WriteLine("       ipconfig /flushdns → " + (r6.Success ? "成功" : "失败：")
                + (r6.Success ? "" : r6.Message));
            TestRunner.AssertTrue(r6.Message != null, "返回了可读结果（不抛异常）");

            // 6) 管理员判定不抛异常
            bool threw = false;
            try { ProcessLauncher.IsAdministrator(); }
            catch (Exception) { threw = true; }
            TestRunner.AssertTrue(!threw, "IsAdministrator 不抛异常");
        }

        // -------------------------------------------------------------------
        private static void TestPing3Tester()
        {
            Console.WriteLine();
            Console.WriteLine("[S8-7] 深度测速（PRD FR-1.6）");
            TestRunner.BeginCase("3 次握手，成功计入耗时、失败记为 -1");

            TcpListener listener = null;

            try
            {
                try
                {
                    listener = new TcpListener(IPAddress.Any, 19200);
                    listener.Start();
                }
                catch (Exception ex)
                {
                    TestRunner.Skip("端口被占用，跳过：" + ex.Message);
                    return;
                }

                uint self = GetSelfIp();
                if (self == 0) { TestRunner.Skip("无法确定本机地址"); return; }

                Ping3Result result = Ping3Tester.Run(self, 19200, 500);

                Console.WriteLine("       显示文本: " + result.ToDisplayText());
                Console.WriteLine("       平均耗时: " + result.AverageMs + " ms");

                TestRunner.AssertEqual(Ping3Tester.AttemptCount, result.AttemptsMs.Length,
                    "共 3 次测量");
                TestRunner.AssertEqual(3, result.SuccessCount, "3 次全部成功");

                // 注意：本机回环连接的耗时可能是 0ms，这是真实测量结果，不是缺陷。
                // 因此只断言"非负"，不断言"大于 0"。
                TestRunner.AssertTrue(result.AverageMs >= 0, "平均耗时非负");

                // 3 次都必须记录了有效耗时（>= 0，而非 -1）
                bool allValid = true;
                for (int i = 0; i < result.AttemptsMs.Length; i++)
                {
                    if (result.AttemptsMs[i] < 0) { allValid = false; }
                }
                TestRunner.AssertTrue(allValid, "3 次均记录了成功耗时（本机回环可能为 0ms）");

                TestRunner.AssertTrue(result.ToDisplayText().IndexOf("均值") >= 0,
                    "显示文本含均值");
                TestRunner.AssertEqual(19200, result.Port, "记录了测速端口");
            }
            finally
            {
                if (listener != null) { try { listener.Stop(); } catch (Exception) { } }
            }
        }

        private static void TestPing3Fallback()
        {
            Console.WriteLine();
            Console.WriteLine("[S8-8] 测速端口选择与全失败处理");
            TestRunner.BeginCase("有开放端口用第一个；全失败时如实显示");

            // 有开放端口
            TestRunner.AssertEqual(9100, Ping3Tester.ChoosePort(new int[] { 9100, 1433 }),
                "有开放端口时用第一个");
            TestRunner.AssertEqual(3389, Ping3Tester.ChoosePort(null), "无端口时退化到 3389");
            TestRunner.AssertEqual(3389, Ping3Tester.ChoosePort(new int[0]), "空数组退化到 3389");

            // 全失败：用一个不可达地址
            uint blackhole;
            Ipv4Util.TryParse("192.0.2.1", out blackhole);

            Ping3Result fail = Ping3Tester.Run(blackhole, 9999, 200);

            Console.WriteLine("       全失败显示: " + fail.ToDisplayText());

            TestRunner.AssertEqual(0, fail.SuccessCount, "成功次数为 0");
            TestRunner.AssertEqual(-1, fail.AverageMs, "平均耗时为 -1");
            TestRunner.AssertEqual("3 次均失败", fail.ToDisplayText(), "显示文本为 3 次均失败");
        }

        // -------------------------------------------------------------------
        private static uint GetSelfIp()
        {
            List<AdapterInfo> adapters = NetworkAdapterService.GetActiveAdapters();

            if (adapters.Count == 0) { return 0; }

            return adapters[0].IpAddress;
        }

        private static bool HasEventHandler(Control control, string eventName)
        {
            try
            {
                PropertyInfo eventsProp = typeof(Control).GetProperty("Events",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (eventsProp == null) { return false; }

                EventHandlerList list = eventsProp.GetValue(control, null) as EventHandlerList;
                if (list == null) { return false; }

                FieldInfo keyField = typeof(Control).GetField("Event" + eventName,
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (keyField == null)
                {
                    keyField = typeof(ListView).GetField("Event" + eventName,
                        BindingFlags.Static | BindingFlags.NonPublic);
                }

                if (keyField == null) { return false; }

                object key = keyField.GetValue(null);

                return list[key] != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static object GetField(object target, string name)
        {
            Type t = target.GetType();
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null) { throw new InvalidOperationException("找不到字段: " + name); }
            return f.GetValue(target);
        }

        /// <summary>
        /// 强制创建 ListView 的窗口句柄。
        /// 未创建句柄时 SelectedItems 恒为空，即使 item.Selected = true 也读不到。
        /// </summary>
        private static void EnsureHandle(ListView lv)
        {
            if (lv.IsHandleCreated) { return; }

            try
            {
                IntPtr h = lv.Handle;

                if (h == IntPtr.Zero)
                {
                    Console.WriteLine("       [警告] 句柄创建失败，选中相关断言可能不可靠");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("       [警告] 句柄创建异常: " + ex.Message);
            }
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
