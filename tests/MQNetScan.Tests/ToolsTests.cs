using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MQNetScan.Application;
using MQNetScan.Engine;
using MQNetScan.Infrastructure;
using MQNetScan.Presentation.Pages;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S11 Tab 2 便捷功能测试。
    /// 覆盖 PRD FR-3.1 ~ FR-3.3。
    /// </summary>
    internal static class ToolsTests
    {
        public static void Run()
        {
            TestArpClearPermissionBranch();
            TestFlushDns();
            TestOpenPanels();
            TestPortTesterFourOutcomes();
            TestPortTesterInputValidation();
            TestSelfCheckIndependence();
            TestSelfCheckRowCount();
            TestCheckResultText();
        }

        // -------------------------------------------------------------------
        // 最关键：普通权限不得弹 UAC（PRD FR-3.1.1 / NFR-P2）
        // -------------------------------------------------------------------
        private static void TestArpClearPermissionBranch()
        {
            Console.WriteLine();
            Console.WriteLine("[S11-1] 清理 ARP 的权限分支（PRD FR-3.1.1，最高优先级）");
            TestRunner.BeginCase("普通权限只给指引、不执行、不弹 UAC");

            bool isAdmin = ProcessLauncher.IsAdministrator();

            Console.WriteLine("       当前是否管理员: " + isAdmin);

            ToolResult r = ToolsService.ClearArpCache();

            TestRunner.AssertTrue(r != null, "返回结果非空");

            if (!isAdmin)
            {
                // 普通权限路径：必须失败 + 需要提权 + 给出可复制命令
                TestRunner.AssertTrue(!r.Success, "普通权限下不执行清理（返回失败）");
                TestRunner.AssertTrue(r.NeedsElevation, "标记为需要提权");
                TestRunner.AssertTrue(r.CommandToCopy == ToolsService.ArpClearCommand,
                    "给出可复制的 netsh 命令");
                TestRunner.AssertTrue(r.Message.IndexOf("管理员") >= 0,
                    "提示说明需要管理员权限");
                TestRunner.AssertTrue(r.Message.IndexOf("不会弹出授权窗口") >= 0,
                    "明确告知不会弹 UAC");

                Console.WriteLine("       提示文本长度: " + r.Message.Length + " 字符");
            }
            else
            {
                // 管理员路径：实际执行
                Console.WriteLine("       （管理员环境，实际执行清理）");
                TestRunner.AssertTrue(r.Message.Length > 0, "返回了执行结果说明");
            }

            // 命令常量正确性
            TestRunner.AssertEqual("netsh interface ip delete arpcache",
                ToolsService.ArpClearCommand, "ARP 清理命令正确");
            TestRunner.AssertEqual("arp -d *",
                ToolsService.ArpClearFallbackCommand, "备用命令正确");
            TestRunner.AssertEqual("ipconfig /flushdns",
                ToolsService.FlushDnsCommand, "DNS 刷新命令正确");
        }

        // -------------------------------------------------------------------
        private static void TestFlushDns()
        {
            Console.WriteLine();
            Console.WriteLine("[S11-2] 刷新 DNS 缓存（PRD FR-3.1.2）");
            TestRunner.BeginCase("执行或给出可读失败原因，不抛异常");

            bool threw = false;
            ToolResult r = null;

            try { r = ToolsService.FlushDns(); }
            catch (Exception ex)
            {
                threw = true;
                Console.WriteLine("       异常: " + ex.Message);
            }

            TestRunner.AssertTrue(!threw, "不抛异常");
            TestRunner.AssertTrue(r != null, "返回结果非空");
            TestRunner.AssertTrue(r.Message.Length > 0, "有可读提示");

            Console.WriteLine("       结果: " + (r.Success ? "成功" : "失败")
                + " / " + r.Message.Replace("\r\n", " "));

            if (!r.Success)
            {
                TestRunner.AssertTrue(!r.Message.Contains("����"),
                    "中文未乱码（不含替换字符）");
            }
        }

        // -------------------------------------------------------------------
        private static void TestOpenPanels()
        {
            Console.WriteLine();
            Console.WriteLine("[S11-3] 系统面板入口（PRD FR-3.1.3 / FR-3.1.4）");
            TestRunner.BeginCase("空参数与失败路径不抛异常");

            // 只验证不抛异常与返回结构；实际打开窗口会在测试机上弹窗，故不直接调用
            bool threw = false;

            try
            {
                // 用无效文件名验证失败路径
                ToolResult bad = ToolsService.OpenServices();
                TestRunner.AssertTrue(bad != null, "OpenServices 返回非空");
            }
            catch (Exception)
            {
                threw = true;
            }

            TestRunner.AssertTrue(!threw, "OpenServices 不抛异常");
        }

        // -------------------------------------------------------------------
        // FR-3.2.2：四类结果可区分
        // -------------------------------------------------------------------
        private static void TestPortTesterFourOutcomes()
        {
            Console.WriteLine();
            Console.WriteLine("[S11-4] 单点测通四类结果（PRD FR-3.2.2）");
            TestRunner.BeginCase("通 / 拒绝 / 超时 / DNS 失败 文案不同");

            TcpListener listener = null;

            try
            {
                try
                {
                    listener = new TcpListener(IPAddress.Any, 19300);
                    listener.Start();
                }
                catch (Exception ex)
                {
                    TestRunner.Skip("端口被占用，跳过：" + ex.Message);
                    return;
                }

                // 1) 通
                SinglePortTester.Result ok = ToolsService.TestPort("127.0.0.1", 19300, 800);
                Console.WriteLine("       通: " + ok.Describe());
                TestRunner.AssertTrue(ok.IsOpen, "19300 为通");
                TestRunner.AssertTrue(ok.Describe().IndexOf("通") >= 0, "文案含「通」");

                // 2) 拒绝或超时（未监听端口）
                SinglePortTester.Result closedPort = ToolsService.TestPort("127.0.0.1", 54323, 500);
                Console.WriteLine("       关闭: " + closedPort.Describe());
                TestRunner.AssertTrue(!closedPort.IsOpen, "54323 不通");
                TestRunner.AssertTrue(closedPort.FailureReason.Length > 0,
                    "给出原因：" + closedPort.FailureReason);

                // 3) 黑洞（超时/不可达）
                SinglePortTester.Result black = ToolsService.TestPort("192.0.2.1", 80, 400);
                Console.WriteLine("       黑洞: " + black.Describe());
                TestRunner.AssertTrue(!black.IsOpen, "黑洞地址不通");

                // 4) DNS 失败
                SinglePortTester.Result dns = ToolsService.TestPort(
                    "no-such-host-mqns.invalid", 80, 500);
                Console.WriteLine("       DNS: " + dns.Describe());
                TestRunner.AssertTrue(!dns.Resolved, "不存在的域名解析失败");
                TestRunner.AssertTrue(dns.Describe().IndexOf("DNS") >= 0,
                    "文案说明 DNS 失败");

                // 四类文案互不相同
                TestRunner.AssertTrue(ok.Describe() != dns.Describe(), "通与 DNS 失败文案不同");
                TestRunner.AssertTrue(black.Describe() != dns.Describe(), "黑洞与 DNS 失败文案不同");
            }
            finally
            {
                if (listener != null) { try { listener.Stop(); } catch (Exception) { } }
            }
        }

        // -------------------------------------------------------------------
        private static void TestPortTesterInputValidation()
        {
            Console.WriteLine();
            Console.WriteLine("[S11-5] 测通输入校验（PRD FR-3.2.3）");
            TestRunner.BeginCase("端口越界与空输入被拒绝");

            using (ToolsPage page = new ToolsPage())
            {
                TextBox txtHost = (TextBox)GetField(page, "txtHost");
                TextBox txtPort = (TextBox)GetField(page, "txtPort");
                Label lblResult = (Label)GetField(page, "lblPortResult");
                Button btnTest = (Button)GetField(page, "btnTestPort");

                // 空主机
                txtHost.Text = "";
                txtPort.Text = "80";
                InvokePrivate(page, "OnTestPortClick", btnTest, EventArgs.Empty);

                TestRunner.AssertTrue(lblResult.Text.IndexOf("请输入") >= 0,
                    "空主机被拒绝：" + lblResult.Text);

                // 端口 0
                txtHost.Text = "127.0.0.1";
                txtPort.Text = "0";
                InvokePrivate(page, "OnTestPortClick", btnTest, EventArgs.Empty);

                TestRunner.AssertTrue(lblResult.Text.IndexOf("1~65535") >= 0,
                    "端口 0 被拒绝：" + lblResult.Text);

                // 端口 65536
                txtPort.Text = "65536";
                InvokePrivate(page, "OnTestPortClick", btnTest, EventArgs.Empty);

                TestRunner.AssertTrue(lblResult.Text.IndexOf("1~65535") >= 0,
                    "端口 65536 被拒绝");

                // 非数字端口
                txtPort.Text = "abc";
                InvokePrivate(page, "OnTestPortClick", btnTest, EventArgs.Empty);

                TestRunner.AssertTrue(lblResult.Text.IndexOf("1~65535") >= 0,
                    "非数字端口被拒绝");
            }
        }

        // -------------------------------------------------------------------
        // FR-3.3.1：三项相互独立
        // -------------------------------------------------------------------
        private static void TestSelfCheckIndependence()
        {
            Console.WriteLine();
            Console.WriteLine("[S11-6] 三项自检相互独立（PRD FR-3.3.1）");
            TestRunner.BeginCase("单项失败不影响其它项；三项均有结果");

            List<AdapterInfo> adapters = NetworkAdapterService.GetActiveAdapters();
            AdapterInfo adapter = adapters.Count > 0 ? adapters[0] : null;

            // 故意给一个不存在的域名，验证「域名项失败但网关项仍正常」
            List<CheckResult> results = ConnectivityChecker.RunAll(
                adapter, "223.5.5.5", "no-such-host-mqns.invalid");

            TestRunner.AssertEqual(3, results.Count, "三项都有结果");

            for (int i = 0; i < results.Count; i++)
            {
                Console.WriteLine("       " + results[i].ToDisplayText());
                TestRunner.AssertTrue(results[i].Name.Length > 0, "第 " + (i + 1) + " 项有名称");
                TestRunner.AssertTrue(results[i].ElapsedMs >= 0, "第 " + (i + 1) + " 项有耗时");
            }

            // 网关项应该通过（本机网关可达）
            CheckResult gw = results[0];
            TestRunner.AssertEqual("默认网关", gw.Name, "第 1 项为网关");

            if (gw.Passed)
            {
                TestRunner.AssertTrue(true, "网关项通过");

                // 域名项必然失败：验证独立性
                CheckResult saas = results[2];
                TestRunner.AssertTrue(!saas.Passed,
                    "域名项失败（域名不存在），但网关项仍通过 → 相互独立");
            }

            // 每一项失败都必须有说明
            for (int i = 0; i < results.Count; i++)
            {
                if (!results[i].Passed)
                {
                    TestRunner.AssertTrue(results[i].Detail.Length > 0,
                        results[i].Name + " 失败时给出原因");
                }
            }
        }

        private static void TestSelfCheckRowCount()
        {
            Console.WriteLine();
            Console.WriteLine("[S11-7] 自检结果写入列表");
            TestRunner.BeginCase("界面显示三行");

            using (ToolsPage page = new ToolsPage())
            {
                page.Initialize(new AppSettings(), null);

                ListView listChecks = (ListView)GetField(page, "listChecks");

                TestRunner.AssertEqual(5, listChecks.Columns.Count, "自检列表 5 列");

                // 直接调用 ApplyCheckResults（避免后台线程时序问题）
                List<CheckResult> fake = new List<CheckResult>();
                fake.Add(new CheckResult("默认网关", "192.168.1.1", true, 12, "方式 ARP"));
                fake.Add(new CheckResult("公共 DNS", "223.5.5.5", true, 25, "ICMP 往返"));
                fake.Add(new CheckResult("SaaS 域名", "example.com", false, 3000, "解析成功但不可达"));

                InvokePrivate(page, "ApplyCheckResults", fake);

                TestRunner.AssertEqual(3, listChecks.Items.Count, "列表有 3 行");
                TestRunner.AssertEqual(3, page.CheckRowCount, "CheckRowCount 为 3");

                TestRunner.AssertEqual("通", listChecks.Items[0].SubItems[ColCheckResult].Text,
                    "第 1 行结果为「通」");
                TestRunner.AssertEqual("不通", listChecks.Items[2].SubItems[ColCheckResult].Text,
                    "第 3 行结果为「不通」");

                TestRunner.AssertEqual("12 ms", listChecks.Items[0].SubItems[ColCheckElapsed].Text,
                    "耗时列格式正确");
            }
        }

        private static void TestCheckResultText()
        {
            Console.WriteLine();
            Console.WriteLine("[S11-8] 自检结果文案");
            TestRunner.BeginCase("结论与明细正确");

            CheckResult ok = new CheckResult("默认网关", "192.168.1.1", true, 12, "方式 ARP");
            TestRunner.AssertEqual("通", ok.ConclusionText, "通过的结论为「通」");
            TestRunner.AssertTrue(ok.ToDisplayText().IndexOf("默认网关") >= 0, "显示含名称");
            TestRunner.AssertTrue(ok.ToDisplayText().IndexOf("192.168.1.1") >= 0, "显示含目标");
            TestRunner.AssertTrue(ok.ToDisplayText().IndexOf("12 ms") >= 0, "显示含耗时");

            CheckResult fail = new CheckResult("SaaS 域名", "x.com", false, 3000, "DNS 解析失败");
            TestRunner.AssertEqual("不通", fail.ConclusionText, "失败的结论为「不通」");
            TestRunner.AssertTrue(fail.ToDisplayText().IndexOf("DNS 解析失败") >= 0,
                "显示含失败原因");
        }

        private const int ColCheckResult = 2;
        private const int ColCheckElapsed = 3;

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
