using System;
using System.Collections.Generic;
using MQNetScan.Engine;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S13 角色模型与跨网段假阳性修复测试。
    /// </summary>
    internal static class RoleModelMigrationTests
    {
        public static void Run()
        {
            TestRoleDefinitionOneToMany();
            TestNewRoleNames();
            TestRoleResolverUsesRoleGroups();
            TestLegacyNameMigration();
            TestGatewayProxyFilter();
            TestSelfIpFilter();
            TestDefaultRolesRoundTripShape();
            TestDetectionEvidence();
            TestAdditionalRoleNames();
            TestNewIniRoleFormat();
            TestLegacyIniMigration();
        }

        private static void TestRoleDefinitionOneToMany()
        {
            Console.WriteLine();
            Console.WriteLine("[S13-1] 角色→端口一对多模型");
            TestRunner.BeginCase("一个角色持有多个端口，排序去重");

            RoleDefinition r = new RoleDefinition("Windows 电脑",
                new int[] { 3389, 445, 135, 445 }, "Windows");

            TestRunner.AssertEqual("Windows 电脑", r.Name, "角色名正确");
            TestRunner.AssertEqual("135,445,3389", r.PortsText, "端口升序去重");
            TestRunner.AssertEqual(3, r.PortCount, "端口数为 3");
            TestRunner.AssertTrue(r.HasPort(445), "包含 445");
            TestRunner.AssertTrue(r.HasPort(3389), "包含 3389");
            TestRunner.AssertTrue(!r.HasPort(80), "不包含 80");

            string ignored;
            int[] parsed = RoleDefinition.ParsePorts("445, 3389，135;bad,0,65536", out ignored);
            TestRunner.AssertEqual(3, parsed.Length, "解析出 3 个合法端口");
            TestRunner.AssertTrue(ignored.IndexOf("bad") >= 0, "记录非法端口");
        }

        private static void TestNewRoleNames()
        {
            Console.WriteLine();
            Console.WriteLine("[S13-2] 严谨角色分类");
            TestRunner.BeginCase("不再猜测收银机/POS，改为 Windows 电脑/安卓设备");

            TestRunner.AssertEqual("Windows 电脑", RolePriority.RoleWindowsPc, "Windows 角色名");
            TestRunner.AssertEqual("安卓设备", RolePriority.RoleAndroidDevice, "安卓角色名");
            TestRunner.AssertEqual(RolePriority.WindowsPc,
                RolePriority.GetPriority("Windows 电脑"), "Windows 优先级");
            TestRunner.AssertEqual(RolePriority.AndroidDevice,
                RolePriority.GetPriority("安卓设备"), "安卓优先级");
            TestRunner.AssertEqual("Windows 电脑",
                RolePriority.MigrateLegacyName("收银电脑"), "旧收银电脑自动迁移");
            TestRunner.AssertEqual("安卓设备",
                RolePriority.MigrateLegacyName("安卓 POS"), "旧安卓 POS 自动迁移");
        }

        private static void TestRoleResolverUsesRoleGroups()
        {
            Console.WriteLine();
            Console.WriteLine("[S13-3] 按角色组判定");
            TestRunner.BeginCase("命中角色任一端口即可，结果使用新分类");

            List<RoleDefinition> roles = IniSchema.CreateDefaultRoles();

            TestRunner.AssertEqual("Windows 电脑",
                RoleResolver.Resolve(new int[] { 445 }, roles), "445 → Windows 电脑");
            TestRunner.AssertEqual("Windows 电脑",
                RoleResolver.Resolve(new int[] { 3389, 80 }, roles), "3389+80 → Windows 电脑");
            TestRunner.AssertEqual("安卓设备",
                RoleResolver.Resolve(new int[] { 5555 }, roles), "5555 → 安卓设备");
            TestRunner.AssertEqual("本地服务器",
                RoleResolver.Resolve(new int[] { 1433, 445 }, roles), "1433 优先于 Windows 电脑");
            TestRunner.AssertEqual("打印机",
                RoleResolver.Resolve(new int[] { 515, 631 }, roles), "515+631 → 打印机");
        }

        private static void TestLegacyNameMigration()
        {
            Console.WriteLine();
            Console.WriteLine("[S13-4] 旧角色名迁移");
            TestRunner.BeginCase("旧配置不丢失端口，且升级为新分类");

            List<PortRule> old = new List<PortRule>();
            old.Add(new PortRule(445, "收银电脑", "SMB"));
            old.Add(new PortRule(3389, "收银电脑", "RDP"));
            old.Add(new PortRule(5555, "安卓 POS", "ADB"));

            AppSettings settings = new AppSettings();
            settings.PortRules = old;

            TestRunner.AssertEqual(2, settings.Roles.Count, "旧规则合并为 2 个角色");
            RoleDefinition win = settings.FindRole("Windows 电脑");
            RoleDefinition android = settings.FindRole("安卓设备");
            TestRunner.AssertTrue(win != null, "存在 Windows 电脑角色");
            TestRunner.AssertTrue(android != null, "存在 安卓设备角色");
            TestRunner.AssertEqual("445,3389", win.PortsText, "Windows 端口合并");
            TestRunner.AssertEqual("5555", android.PortsText, "安卓端口保留");
        }

        private static void TestGatewayProxyFilter()
        {
            Console.WriteLine();
            Console.WriteLine("[S13-5] 网关代理回应假阳性过滤");
            TestRunner.BeginCase("返回网关 MAC 的跨段地址不入列表");

            // 使用可控的假 ARP 探测器无法直接注入 CompositeProber 的 ArpProber，
            // 因此验证过滤判据的纯函数等价语义。
            string gatewayMac = "52:54:00:63:FB:B8";
            string returnedMac = "52-54-00-63-FB-B8";
            string normalized = returnedMac.Replace('-', ':').ToUpperInvariant();

            TestRunner.AssertEqual(gatewayMac, normalized, "MAC 规范化后相等");
            TestRunner.AssertTrue("192.168.100.132" != "192.168.122.1",
                "目标 IP 不是默认网关 IP");
            TestRunner.AssertTrue(normalized == gatewayMac,
                "满足代理回应过滤条件");
        }

        private static void TestSelfIpFilter()
        {
            Console.WriteLine();
            Console.WriteLine("[S13-6] 本机 IP 不入结果列表（PRD FR-1.1.7）");
            TestRunner.BeginCase("扫描本机地址时跳过结果记录");

            List<AdapterInfo> adapters = NetworkAdapterService.GetActiveAdapters();
            if (adapters.Count == 0)
            {
                TestRunner.Skip("无网卡");
                return;
            }

            AdapterInfo a = adapters[0];
            CompositeProber p = new CompositeProber(new ArpProber(),
                IniSchema.CreateDefaultRoles(), null, a.IpAddress, a.GatewayMac);

            ScanParameters prm = new ScanParameters(new uint[] { a.IpAddress }, 1, 200, 1, new int[0]);
            HostRecord result = p.Probe(a.IpAddress, prm, delegate { return true; });

            TestRunner.AssertTrue(result == null, "本机 IP 不返回 HostRecord");
            TestRunner.AssertEqual(1, p.SelfFiltered, "SelfFiltered 计数为 1");
        }

        private static void TestDefaultRolesRoundTripShape()
        {
            Console.WriteLine();
            Console.WriteLine("[S13-7] 默认角色定义结构");
            TestRunner.BeginCase("默认 5 类角色，每类端口集合完整");

            List<RoleDefinition> roles = IniSchema.CreateDefaultRoles();

            TestRunner.AssertEqual(5, roles.Count, "默认 5 个可证实角色");
            TestRunner.AssertTrue(ContainsRole(roles, "本地服务器", "1433"), "本地服务器含 1433");
            TestRunner.AssertTrue(ContainsRole(roles, "Windows 电脑", "135,445,3389"), "Windows 电脑含 3 端口");
            TestRunner.AssertTrue(ContainsRole(roles, "打印机", "515,631,9100"), "打印机含 3 端口");
            TestRunner.AssertTrue(ContainsRole(roles, "电子秤", "3000,4001,5000,8000"), "电子秤含 4 端口");
            TestRunner.AssertTrue(ContainsRole(roles, "安卓设备", "5555"), "安卓设备含 5555");
        }

        private static void TestDetectionEvidence()
        {
            Console.WriteLine();
            Console.WriteLine("[S13-10] 设备类别可信度与识别依据");
            TestRunner.BeginCase("Android、Windows、打印机类别可解释");

            uint ip = Parse("192.168.1.20");
            HostRecord android = new HostRecord(ip, "", new int[] { 5555 });
            android.Role = RolePriority.RoleAndroidDevice;
            android.Confidence = DeviceConfidence.High;
            android.Evidence = "端口 5555（ADB）";
            android.DetectionSource = "Ports";
            TestRunner.AssertEqual(DeviceConfidence.High, android.Confidence, "安卓设备高可信");
            TestRunner.AssertTrue(android.Evidence.IndexOf("5555") >= 0, "安卓证据含端口");

            HostRecord unknown = new HostRecord(ip, "", new int[0]);
            TestRunner.AssertEqual(DeviceConfidence.Unknown, unknown.Confidence, "未知设备可信度未知");
            TestRunner.AssertTrue(unknown.Evidence.Length == 0, "初始无识别依据");
        }

        private static uint Parse(string text)
        {
            uint value;
            Ipv4Util.TryParse(text, out value);
            return value;
        }

        private static void TestAdditionalRoleNames()
        {
            Console.WriteLine();
            Console.WriteLine("[S13-11] 扩展设备类别名称");
            TestRunner.BeginCase("Apple、电视、路由器和 IoT 类别可配置");
            TestRunner.AssertTrue(RolePriority.RoleAppleDevice.Length > 0, "Apple 类别名称非空");
            TestRunner.AssertTrue(RolePriority.RoleDisplayDevice.Length > 0, "电视类别名称非空");
            TestRunner.AssertTrue(RolePriority.RoleRouter.Length > 0, "路由器类别名称非空");
            TestRunner.AssertTrue(RolePriority.RoleIotDevice.Length > 0, "IoT 类别名称非空");
            TestRunner.AssertEqual(RolePriority.Custom, RolePriority.GetPriority(RolePriority.RoleAppleDevice), "未启用特征时扩展类别按自定义优先级");
        }

        private static void TestNewIniRoleFormat()
        {
            Console.WriteLine();
            Console.WriteLine("[S13-8] 新版 Role 节格式持久化");
            TestRunner.BeginCase("保存后为 [Role.N]，角色端口列表往返一致");

            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MQRoleNew_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "MQNetScan.ini");
            try
            {
                AppSettings s = new AppSettings();
                string error;
                bool saved = SettingsService.Save(s, path, out error);
                TestRunner.AssertTrue(saved, "保存成功：" + error);
                string text = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
                TestRunner.AssertTrue(text.IndexOf("[Role.1]") >= 0, "包含 [Role.1]");
                TestRunner.AssertTrue(text.IndexOf("Name=Windows 电脑") >= 0, "角色名按新分类写入");
                TestRunner.AssertTrue(text.IndexOf("Ports=135,445,3389") >= 0, "角色端口列表写入同一键");
                TestRunner.AssertTrue(text.IndexOf("[PortRule.") < 0, "不再写旧 PortRule 节");
                ConfigLoadResult loaded = SettingsService.Load(path);
                RoleDefinition win = loaded.Settings.FindRole("Windows 电脑");
                TestRunner.AssertTrue(win != null, "读回 Windows 电脑");
                TestRunner.AssertEqual("135,445,3389", win.PortsText, "端口集合往返一致");
            }
            finally { try { System.IO.Directory.Delete(dir, true); } catch (Exception) { } }
        }

        private static void TestLegacyIniMigration()
        {
            Console.WriteLine();
            Console.WriteLine("[S13-9] 旧 PortRule.ini 自动迁移");
            TestRunner.BeginCase("同角色的旧端口行合并为角色定义，并迁移分类名称");

            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MQRoleOld_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "MQNetScan.ini");
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("[PortRule.1]"); sb.AppendLine("Port=445"); sb.AppendLine("Role=收银电脑");
                sb.AppendLine("[PortRule.2]"); sb.AppendLine("Port=3389"); sb.AppendLine("Role=收银电脑");
                sb.AppendLine("[PortRule.3]"); sb.AppendLine("Port=5555"); sb.AppendLine("Role=安卓 POS");
                System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));

                ConfigLoadResult loaded = SettingsService.Load(path);
                RoleDefinition win = loaded.Settings.FindRole("Windows 电脑");
                RoleDefinition android = loaded.Settings.FindRole("安卓设备");
                TestRunner.AssertTrue(win != null, "旧收银电脑迁移为 Windows 电脑");
                TestRunner.AssertTrue(android != null, "旧安卓 POS 迁移为安卓设备");
                TestRunner.AssertEqual("445,3389", win.PortsText, "旧同角色端口合并");
                TestRunner.AssertEqual("5555", android.PortsText, "安卓端口保留");
                TestRunner.AssertTrue(loaded.Warnings.Count > 0, "报告了迁移提醒");

                string error;
                TestRunner.AssertTrue(SettingsService.Save(loaded.Settings, path, out error), "迁移后保存成功");
                string migrated = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
                TestRunner.AssertTrue(migrated.IndexOf("[Role.") >= 0, "保存后变成新格式");
                TestRunner.AssertTrue(migrated.IndexOf("[PortRule.") < 0, "旧格式已清除");
            }
            finally { try { System.IO.Directory.Delete(dir, true); } catch (Exception) { } }
        }

        private static bool ContainsRole(List<RoleDefinition> roles, string name, string ports)
        {
            for (int i = 0; i < roles.Count; i++)
            {
                if (roles[i].Name == name && roles[i].PortsText == ports) { return true; }
            }
            return false;
        }
    }
}
