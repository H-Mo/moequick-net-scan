using System.Collections.Generic;

namespace MQNetScan.Infrastructure
{
    /// <summary>
    /// 配置文件键名与默认值的单一事实来源。
    /// 其它任何文件不得硬编码 ini 的节名或键名（边界检查 B-8）。
    /// </summary>
    internal static class IniSchema
    {
        // -------------------------------------------------------------------
        // 节名
        // -------------------------------------------------------------------
        public const string SectionGeneral = "General";
        public const string SectionScan = "Scan";
        public const string SectionProbe = "Probe";
        public const string SectionReport = "Report";
        public const string SectionSelfCheck = "SelfCheck";

        /// <summary>
        /// 角色定义节的名称前缀，实际形如 Role.1、Role.2。
        /// 每个节描述"一个角色 + 它的一组特征端口"。
        /// </summary>
        public const string RoleSectionPrefix = "Role.";

        /// <summary>
        /// 旧版（按端口配角色）的节前缀，仅用于读取旧配置时自动迁移。
        /// </summary>
        public const string LegacyPortRuleSectionPrefix = "PortRule.";

        // -------------------------------------------------------------------
        // [General]
        // -------------------------------------------------------------------
        public const string KeyMaxConcurrency = "MaxConcurrency";
        public const string KeyPortTimeoutMs = "PortTimeoutMs";
        public const string KeyEnableRedBanner = "EnableRedBanner";

        // -------------------------------------------------------------------
        // [Scan]
        // -------------------------------------------------------------------
        public const string KeyLastNetSegment = "LastNetSegment";
        public const string KeyConfirmAboveHosts = "ConfirmAboveHosts";
        public const string KeyLastProbeMode = "LastProbeMode";
        public const string KeyPingTimeoutMs = "PingTimeoutMs";
        public const string KeyPingRetry = "PingRetry";

        // -------------------------------------------------------------------
        // [Probe]
        // -------------------------------------------------------------------
        public const string KeySendArpRetry = "SendArpRetry";

        // -------------------------------------------------------------------
        // [Report]
        // -------------------------------------------------------------------
        public const string KeySaveToDesktop = "SaveToDesktop";
        public const string KeyTimestampFormat = "TimestampFormat";

        // -------------------------------------------------------------------
        // [SelfCheck]
        // -------------------------------------------------------------------
        public const string KeySaasDomain = "SaasDomain";
        public const string KeyDnsProbeIp = "DnsProbeIp";
        public const string KeyGatewayProbePorts = "GatewayProbePorts";

        // -------------------------------------------------------------------
        // [Role.N]
        // -------------------------------------------------------------------
        public const string KeyRoleName = "Name";
        public const string KeyRolePorts = "Ports";
        public const string KeyComment = "Comment";
        public const string KeyEnabled = "Enabled";

        // -------------------------------------------------------------------
        // 取值范围（PRD FR-4.1.1 / FR-4.1.2 / FR-1.3.2）
        // -------------------------------------------------------------------
        public const int ConcurrencyMin = 16;
        public const int ConcurrencyMax = 128;
        public const int TimeoutMinMs = 200;
        public const int TimeoutMaxMs = 5000;
        public const int SendArpRetryMin = 1;
        public const int SendArpRetryMax = 3;
        public const int PingTimeoutMinMs = 200;
        public const int PingTimeoutMaxMs = 5000;
        public const int PingRetryMin = 1;
        public const int PingRetryMax = 3;

        /// <summary>并发数超过此值时提示“配置较低的电脑建议不超过 32”（PRD 风险 R3）。</summary>
        public const int ConcurrencyWarnThreshold = 64;

        // -------------------------------------------------------------------
        // 默认值（PRD FR-4.1.1、architecture §3.2）
        // -------------------------------------------------------------------
        public const int DefaultMaxConcurrency = 32;
        public const int DefaultPortTimeoutMs = 800;
        public const bool DefaultEnableRedBanner = true;
        public const string DefaultLastNetSegment = "";
        public const int DefaultConfirmAboveHosts = 254;
        public const int DefaultSendArpRetry = 1;
        public const int DefaultPingTimeoutMs = 800;
        public const int DefaultPingRetry = 1;
        public const bool DefaultSaveToDesktop = true;
        public const string DefaultTimestampFormat = "yyyyMMdd_HHmmss";

        /// <summary>
        /// 思迅 SaaS 域名默认值。
        /// 注意：该值需实施确认（PRD 风险 R4）；此处为占位值，界面须明示当前值。
        /// </summary>
        public const string DefaultSaasDomain = "www.sissyun.com";

        public const string DefaultDnsProbeIp = "223.5.5.5";
        public const string DefaultGatewayProbePorts = "80,443";

        /// <summary>
        /// 内置角色定义（PRD §7.1，S13 起为"角色 → 端口"模型）。
        /// 分类学原则：只按能被端口客观证实的类别归类。
        /// </summary>
        public static List<RoleDefinition> CreateDefaultRoles()
        {
            List<RoleDefinition> roles = new List<RoleDefinition>();
            roles.Add(new RoleDefinition("本地服务器", new int[] { 1433 }, "SQL Server 本地数据库"));
            roles.Add(new RoleDefinition("Windows 电脑", new int[] { 445, 3389, 135 }, "SMB / 远程桌面 / RPC"));
            roles.Add(new RoleDefinition("打印机", new int[] { 9100, 515, 631 }, "RAW 直打 / LPR / IPP"));
            roles.Add(new RoleDefinition("电子秤", new int[] { 3001, 4001, 5001 }, "计价秤 / 条码秤通讯"));
            roles.Add(new RoleDefinition("安卓设备", new int[] { 5555 }, "ADB 调试端口"));
            return roles;
        }

        /// <summary>兼容旧调用方：返回展开后的端口规则视图。</summary>
        public static List<PortRule> CreateDefaultPortRules()
        {
            List<PortRule> result = new List<PortRule>();
            List<RoleDefinition> roles = CreateDefaultRoles();
            for (int i = 0; i < roles.Count; i++)
            {
                int[] ports = roles[i].Ports;
                for (int k = 0; k < ports.Length; k++)
                {
                    result.Add(new PortRule(ports[k], roles[i].Name, roles[i].Comment, roles[i].Enabled));
                }
            }
            return result;
        }
    }
}
