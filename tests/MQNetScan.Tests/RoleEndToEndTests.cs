using System;
using System.Collections.Generic;
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
    /// S7 界面端到端：用**真实** CompositeProber（ARP + 端口扫描 + 角色判定）
    /// 驱动 ScanPage，验证列表中的"开放端口"与"角色"两列确有正确数据。
    ///
    /// 这是对 [S6-E2E] 的补强：[S6-E2E] 用的是 FakeHostProber，角色列恒为"未知"，
    /// 并未真正验证角色判定链路。
    /// </summary>
    internal static class RoleEndToEndTests
    {
        public static void Run()
        {
            TestRealProberFillsPortsAndRole();
        }

        private static void TestRealProberFillsPortsAndRole()
        {
            Console.WriteLine();
            Console.WriteLine("[S7-E2E] 真实探测器驱动界面，角色列有数据");
            TestRunner.BeginCase("本机开自定义端口，应判定为对应角色");

            TcpListener l1 = null;
            TcpListener l2 = null;

            // 用两个不容易被系统占用的高位端口，避免与真实服务冲突
            // （1433/445/3389 等在开发机上常被 SQL Server、SMB 等占用，
            //   导致监听失败、测试被迫跳过，那是环境问题而非产品缺陷。）
            const int portA = 19100;   // 将映射为"打印机"
            const int portB = 19133;   // 将映射为"本地服务器"

            try
            {
                try
                {
                    l1 = new TcpListener(IPAddress.Any, portA);
                    l1.Start();

                    l2 = new TcpListener(IPAddress.Any, portB);
                    l2.Start();
                }
                catch (Exception ex)
                {
                    TestRunner.Skip("端口 " + portA + "/" + portB + " 被占用，跳过：" + ex.Message);
                    return;
                }

                uint self = GetSelfIp();
                if (self == 0) { TestRunner.Skip("无法确定本机地址"); return; }

                Console.WriteLine("       本机 " + Ipv4Util.Format(self)
                    + " 已监听 " + portA + " 与 " + portB);

                // 构造自定义规则：
                //   portA(19100) → 打印机
                //   portB(19133) → 本地服务器（优先级更高）
                // 预期：两者都开放时，角色取"本地服务器"。
                List<PortRule> rules = new List<PortRule>();
                rules.Add(new PortRule(portA, RolePriority.RolePrinter, "测试用打印机端口"));
                rules.Add(new PortRule(portB, RolePriority.RoleLocalServer, "测试用服务器端口"));

                int[] enabledPorts = { portA, portB };

                ScanParameters prm = new ScanParameters(
                    new uint[] { self }, 1, 500, 1, enabledPorts);

                CompositeProber prober = new CompositeProber(new ArpProber(), rules);

                HostRecord rec = prober.Probe(self, prm, delegate { return true; });

                TestRunner.AssertTrue(rec != null, "真实探测器发现了本机");

                if (rec == null) { return; }

                Console.WriteLine("       MAC: " + rec.MacAddress);
                Console.WriteLine("       开放端口: " + rec.OpenPortsText);
                Console.WriteLine("       角色: " + rec.Role);

                TestRunner.AssertTrue(rec.OpenPortsText.IndexOf(portA.ToString()) >= 0,
                    "开放端口包含 " + portA);
                TestRunner.AssertTrue(rec.OpenPortsText.IndexOf(portB.ToString()) >= 0,
                    "开放端口包含 " + portB);
                TestRunner.AssertEqual(RolePriority.RoleLocalServer, rec.Role,
                    "两个端口都开放时取更高优先级的「本地服务器」");

                // ---- 再走一遍界面链路 ----
                using (ScanPage page = new ScanPage())
                {
                    AppSettings settings = new AppSettings();
                    settings.PortRules = rules;

                    page.Initialize("", settings);

                    ListView lv = (ListView)GetField(page, "listHosts");

                    // 注入真实 CompositeProber（不使用假探测器）
                    page.ProberFactory = delegate
                    {
                        return new CompositeProber(new ArpProber(), rules);
                    };

                    // 精确定向到本机这一个地址
                    RadioButton rbRange = (RadioButton)GetField(page, "rbRange");
                    RadioButton rbCidr = (RadioButton)GetField(page, "rbCidr");
                    TextBox txtStart = (TextBox)GetField(page, "txtRangeStart");
                    TextBox txtEnd = (TextBox)GetField(page, "txtRangeEnd");

                    rbCidr.Checked = false;
                    rbRange.Checked = true;
                    InvokePrivate(page, "OnModeChanged", rbRange, EventArgs.Empty);

                    string ipText = Ipv4Util.Format(self);
                    txtStart.Text = ipText;
                    txtEnd.Text = ipText;

                    page.Revalidate();

                    if (page.CurrentSegment == null)
                    {
                        TestRunner.Skip("无法构造单地址范围");
                        return;
                    }

                    Button btnStart = (Button)GetField(page, "btnStartScan");
                    InvokePrivate(page, "OnStartScanClick", btnStart, EventArgs.Empty);

                    Label lblStatus = (Label)GetField(page, "lblScanStatus");

                    for (int i = 0; i < 200; i++)
                    {
                        InvokePrivate(page, "OnRefreshTick", null, EventArgs.Empty);
                        Thread.Sleep(30);

                        if (lblStatus.Text.IndexOf("扫描完成") >= 0) { break; }
                    }

                    Console.WriteLine("       界面状态: " + lblStatus.Text);

                    TestRunner.AssertEqual(1, lv.Items.Count,
                        "列表中有 1 行（本机）");

                    if (lv.Items.Count >= 1)
                    {
                        string portsCell = lv.Items[0].SubItems[ListViewHelper.ColPorts].Text;
                        string roleCell = lv.Items[0].SubItems[ListViewHelper.ColRole].Text;

                        Console.WriteLine("       列表端口列: " + portsCell);
                        Console.WriteLine("       列表角色列: " + roleCell);

                        TestRunner.AssertTrue(portsCell.Length > 0,
                            "列表端口列非空");
                        TestRunner.AssertTrue(roleCell != RolePriority.RoleUnknown,
                            "列表角色列不是「未知」（实际：" + roleCell + "）");
                        TestRunner.AssertEqual(RolePriority.RoleLocalServer, roleCell,
                            "列表角色为本地服务器");
                    }
                }
            }
            finally
            {
                if (l1 != null) { try { l1.Stop(); } catch (Exception) { } }
                if (l2 != null) { try { l2.Stop(); } catch (Exception) { } }
            }
        }

        private static uint GetSelfIp()
        {
            List<AdapterInfo> adapters = NetworkAdapterService.GetActiveAdapters();

            if (adapters.Count == 0) { return 0; }

            return adapters[0].IpAddress;
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
            Type t = target.GetType();
            MethodInfo m = t.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            if (m == null) { throw new InvalidOperationException("找不到方法: " + method); }
            m.Invoke(target, args);
        }
    }
}
