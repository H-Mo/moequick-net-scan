using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MQNetScan.Engine.Contracts;

namespace MQNetScan.Infrastructure
{
    /// <summary>
    /// 配置读取结果：携带数据与"本次读取发现的问题"。
    /// </summary>
    internal sealed class ConfigLoadResult
    {
        private readonly AppSettings _settings;
        private readonly List<string> _warnings;
        private readonly bool _fileExisted;
        private readonly string _filePath;

        public ConfigLoadResult(AppSettings settings, List<string> warnings, bool fileExisted, string filePath)
        {
            _settings = settings;
            _warnings = warnings;
            _fileExisted = fileExisted;
            _filePath = filePath;
        }

        public AppSettings Settings { get { return _settings; } }

        /// <summary>读取期间发现的问题（供 UI 一次性提示）。</summary>
        public IList<string> Warnings { get { return _warnings; } }

        /// <summary>配置文件在加载前是否已存在。</summary>
        public bool FileExisted { get { return _fileExisted; } }

        public string FilePath { get { return _filePath; } }
    }

    /// <summary>
    /// 应用设置的内存模型。
    /// </summary>
    internal sealed class AppSettings
    {
        public int MaxConcurrency;
        public int PortTimeoutMs;
        public bool EnableRedBanner;
        public string LastNetSegment;
        public int ConfirmAboveHosts;
        public int SendArpRetry;
        public bool SaveToDesktop;
        public string TimestampFormat;
        public string SaasDomain;
        public string DnsProbeIp;
        public string GatewayProbePorts;
        public int LastProbeMode;
        public int PingTimeoutMs;
        public int PingRetry;
        public List<RoleDefinition> Roles;

        /// <summary>兼容旧代码的展开视图；新代码应使用 Roles。</summary>
        public List<PortRule> PortRules
        {
            get
            {
                List<PortRule> result = new List<PortRule>();
                if (Roles == null) { return result; }
                for (int i = 0; i < Roles.Count; i++)
                {
                    RoleDefinition role = Roles[i];
                    if (role == null) { continue; }
                    int[] ports = role.Ports;
                    for (int k = 0; k < ports.Length; k++)
                    {
                        result.Add(new PortRule(ports[k], role.Name, role.Comment, role.Enabled));
                    }
                }
                return result;
            }
            set
            {
                Roles = new List<RoleDefinition>();
                if (value == null) { return; }
                Dictionary<string, RoleDefinition> byName = new Dictionary<string, RoleDefinition>();
                for (int i = 0; i < value.Count; i++)
                {
                    PortRule old = value[i];
                    if (old == null) { continue; }
                    string name = RolePriority.MigrateLegacyName(old.Role);
                    RoleDefinition role;
                    if (!byName.TryGetValue(name, out role))
                    {
                        role = new RoleDefinition(name, new int[0], old.Comment, old.Enabled);
                        byName[name] = role;
                        Roles.Add(role);
                    }
                    role.AddPortValue(old.Port);
                    if (!old.Enabled) { role.Enabled = false; }
                }
            }
        }

        public AppSettings()
        {
            MaxConcurrency = IniSchema.DefaultMaxConcurrency;
            PortTimeoutMs = IniSchema.DefaultPortTimeoutMs;
            EnableRedBanner = IniSchema.DefaultEnableRedBanner;
            LastNetSegment = IniSchema.DefaultLastNetSegment;
            ConfirmAboveHosts = IniSchema.DefaultConfirmAboveHosts;
            SendArpRetry = IniSchema.DefaultSendArpRetry;
            SaveToDesktop = IniSchema.DefaultSaveToDesktop;
            TimestampFormat = IniSchema.DefaultTimestampFormat;
            SaasDomain = IniSchema.DefaultSaasDomain;
            DnsProbeIp = IniSchema.DefaultDnsProbeIp;
            GatewayProbePorts = IniSchema.DefaultGatewayProbePorts;
            LastProbeMode = (int)ProbeMode.Arp;
            PingTimeoutMs = IniSchema.DefaultPingTimeoutMs;
            PingRetry = IniSchema.DefaultPingRetry;
            Roles = IniSchema.CreateDefaultRoles();
        }

        /// <summary>取所有可用角色的端口号（去重升序），供扫描使用。</summary>
        public int[] GetEnabledPorts()
        {
            return RoleDefinition.CollectPorts(Roles);
        }

        /// <summary>按角色名查找定义；找不到返回 null。</summary>
        public RoleDefinition FindRole(string name)
        {
            if (Roles == null || string.IsNullOrEmpty(name)) { return null; }

            string target = name.Trim();

            for (int i = 0; i < Roles.Count; i++)
            {
                if (Roles[i] != null && Roles[i].Name == target) { return Roles[i]; }
            }

            return null;
        }
    }

    /// <summary>
    /// 设置服务：负责加载、校验、保存配置。
    /// 上层（UI）只与此类交互，不直接碰 IniConfigStore。
    /// </summary>
    internal static class SettingsService
    {
        /// <summary>
        /// 计算 exe 同级目录下的 ini 路径。
        /// </summary>
        public static string ResolveConfigPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "MQNetScan.ini");
        }

        /// <summary>
        /// 加载配置。
        /// 文件不存在时：返回全默认值，并在 out 参数中标记需要写入默认文件。
        /// 任何异常都不得向上抛出（PRD FR-4.3.3）。
        /// </summary>
        public static ConfigLoadResult Load(string filePath)
        {
            List<string> warnings = new List<string>();
            bool existed = false;
            AppSettings settings = new AppSettings();

            try
            {
                existed = File.Exists(filePath);

                IniConfigStore store = new IniConfigStore(filePath);

                settings.MaxConcurrency = store.GetInt(
                    IniSchema.SectionGeneral, IniSchema.KeyMaxConcurrency,
                    IniSchema.DefaultMaxConcurrency, IniSchema.ConcurrencyMin, IniSchema.ConcurrencyMax);

                settings.PortTimeoutMs = store.GetInt(
                    IniSchema.SectionGeneral, IniSchema.KeyPortTimeoutMs,
                    IniSchema.DefaultPortTimeoutMs, IniSchema.TimeoutMinMs, IniSchema.TimeoutMaxMs);

                settings.EnableRedBanner = store.GetBool(
                    IniSchema.SectionGeneral, IniSchema.KeyEnableRedBanner,
                    IniSchema.DefaultEnableRedBanner);

                settings.LastNetSegment = store.GetString(
                    IniSchema.SectionScan, IniSchema.KeyLastNetSegment,
                    IniSchema.DefaultLastNetSegment);

                settings.ConfirmAboveHosts = store.GetInt(
                    IniSchema.SectionScan, IniSchema.KeyConfirmAboveHosts,
                    IniSchema.DefaultConfirmAboveHosts, 1, 65535);

                settings.LastProbeMode = store.GetInt(IniSchema.SectionScan,
                    IniSchema.KeyLastProbeMode, (int)ProbeMode.Arp, (int)ProbeMode.Arp, (int)ProbeMode.Ping);
                settings.PingTimeoutMs = store.GetInt(IniSchema.SectionScan,
                    IniSchema.KeyPingTimeoutMs, IniSchema.DefaultPingTimeoutMs,
                    IniSchema.PingTimeoutMinMs, IniSchema.PingTimeoutMaxMs);
                settings.PingRetry = store.GetInt(IniSchema.SectionScan,
                    IniSchema.KeyPingRetry, IniSchema.DefaultPingRetry,
                    IniSchema.PingRetryMin, IniSchema.PingRetryMax);

                settings.SendArpRetry = store.GetInt(
                    IniSchema.SectionProbe, IniSchema.KeySendArpRetry,
                    IniSchema.DefaultSendArpRetry, IniSchema.SendArpRetryMin, IniSchema.SendArpRetryMax);

                settings.SaveToDesktop = store.GetBool(
                    IniSchema.SectionReport, IniSchema.KeySaveToDesktop,
                    IniSchema.DefaultSaveToDesktop);

                settings.TimestampFormat = store.GetString(
                    IniSchema.SectionReport, IniSchema.KeyTimestampFormat,
                    IniSchema.DefaultTimestampFormat);

                settings.SaasDomain = store.GetString(
                    IniSchema.SectionSelfCheck, IniSchema.KeySaasDomain,
                    IniSchema.DefaultSaasDomain);

                settings.DnsProbeIp = store.GetString(
                    IniSchema.SectionSelfCheck, IniSchema.KeyDnsProbeIp,
                    IniSchema.DefaultDnsProbeIp);

                settings.GatewayProbePorts = store.GetString(
                    IniSchema.SectionSelfCheck, IniSchema.KeyGatewayProbePorts,
                    IniSchema.DefaultGatewayProbePorts);

                // 角色定义：即使解析出问题也要给出可用集合
                settings.Roles = LoadRoles(store, warnings);

                warnings.AddRange(store.InvalidKeys);

                if (settings.TimestampFormat.Trim().Length == 0)
                {
                    settings.TimestampFormat = IniSchema.DefaultTimestampFormat;
                    warnings.Add("Report.TimestampFormat 为空，已使用默认值");
                }
            }
            catch (Exception ex)
            {
                // 兜底：加载整体失败时用全默认值，保证程序能启动
                settings = new AppSettings();
                warnings.Add("读取配置时发生异常，已全部使用默认值：" + ex.Message);
            }

            return new ConfigLoadResult(settings, warnings, existed, filePath);
        }

        /// <summary>
        /// 读取角色定义。
        ///
        /// 兼容策略（S13）：
        ///   1. 优先读新版 [Role.N]（Name + Ports）；
        ///   2. 若无 [Role.N] 但存在旧版 [PortRule.N]，**自动迁移**：
        ///      把"多条端口→同一角色"合并成"一个角色持有多端口"，
        ///      并把旧角色名升级为新分类（收银电脑→Windows 电脑、安卓 POS→安卓设备）；
        ///   3. 都没有则回退内置默认。
        /// </summary>
        private static List<RoleDefinition> LoadRoles(IniConfigStore store, List<string> warnings)
        {
            List<RoleDefinition> roles = ReadRoleSections(store, warnings);

            if (roles.Count > 0) { return roles; }

            // 尝试旧格式迁移
            List<RoleDefinition> migrated = MigrateLegacyPortRules(store, warnings);

            if (migrated.Count > 0)
            {
                warnings.Add("已把旧版「按端口配置角色」的规则自动迁移为「按角色配置端口」，"
                    + "保存设置后将写入新格式。");
                return migrated;
            }

            warnings.Add("未读到任何角色定义，已载入内置默认角色");
            return IniSchema.CreateDefaultRoles();
        }

        /// <summary>
        /// 读取新版 [Role.N] 节。
        /// </summary>
        private static List<RoleDefinition> ReadRoleSections(IniConfigStore store, List<string> warnings)
        {
            List<RoleDefinition> roles = new List<RoleDefinition>();

            int misses = 0;
            int index = 1;

            while (misses < 20)
            {
                string section = IniSchema.RoleSectionPrefix + index;
                string rawName = store.GetRaw(section, IniSchema.KeyRoleName);

                if (rawName == null)
                {
                    misses++;
                    index++;
                    continue;
                }

                misses = 0;

                string name = RolePriority.MigrateLegacyName(rawName.Trim());

                if (name.Length == 0)
                {
                    warnings.Add(section + ".Name 为空，该角色已忽略");
                    index++;
                    continue;
                }

                string rawPorts = store.GetString(section, IniSchema.KeyRolePorts, "");
                string ignored;
                int[] ports = RoleDefinition.ParsePorts(rawPorts, out ignored);

                if (ignored.Length > 0)
                {
                    warnings.Add(section + ".Ports 中的「" + ignored + "」不是合法端口，已忽略");
                }

                if (ports.Length == 0)
                {
                    warnings.Add(section + "（" + name + "）没有任何合法端口，该角色已忽略");
                    index++;
                    continue;
                }

                string comment = store.GetString(section, IniSchema.KeyComment, "");
                bool enabled = store.GetBool(section, IniSchema.KeyEnabled, true);

                roles.Add(new RoleDefinition(name, ports, comment, enabled));
                index++;
            }

            return roles;
        }

        /// <summary>
        /// 把旧版 [PortRule.N]（一条端口 → 一个角色）迁移成本模型。
        /// 同一角色的多条记录会被合并到一个 RoleDefinition 的端口列表中。
        /// </summary>
        private static List<RoleDefinition> MigrateLegacyPortRules(
            IniConfigStore store, List<string> warnings)
        {
            List<RoleDefinition> roles = new List<RoleDefinition>();

            // 记录每个角色在 roles 中的位置，便于合并
            Dictionary<string, int> indexByName = new Dictionary<string, int>();

            int misses = 0;
            int index = 1;

            while (misses < 20)
            {
                string section = IniSchema.LegacyPortRuleSectionPrefix + index;
                string rawPort = store.GetRaw(section, "Port");

                if (rawPort == null)
                {
                    misses++;
                    index++;
                    continue;
                }

                misses = 0;

                int port;
                if (!int.TryParse(rawPort.Trim(), out port) || port < 1 || port > 65535)
                {
                    warnings.Add("旧规则 " + section + ".Port 非法（" + rawPort + "），已忽略");
                    index++;
                    continue;
                }

                string rawRole = store.GetString(section, "Role", "");
                string roleName = RolePriority.MigrateLegacyName(rawRole.Trim());

                if (roleName.Length == 0)
                {
                    warnings.Add("旧规则 " + section + ".Role 为空，已忽略");
                    index++;
                    continue;
                }

                bool enabled = store.GetBool(section, "Enabled", true);

                int existing;

                if (indexByName.TryGetValue(roleName, out existing))
                {
                    // 合并到已有角色
                    roles[existing].AddPortValue(port);
                }
                else
                {
                    RoleDefinition def = new RoleDefinition(roleName, new int[] { port }, "", enabled);
                    indexByName[roleName] = roles.Count;
                    roles.Add(def);
                }

                index++;
            }

            return roles;
        }

        /// <summary>
        /// 保存配置。返回是否成功；失败原因通过 out 参数给出（不抛异常）。
        /// </summary>
        public static bool Save(AppSettings settings, string filePath, out string errorMessage)
        {
            errorMessage = null;

            if (settings == null)
            {
                errorMessage = "内部错误：设置为空。";
                return false;
            }

            try
            {
                IniConfigStore store = new IniConfigStore(filePath);

                store.SetInt(IniSchema.SectionGeneral, IniSchema.KeyMaxConcurrency, settings.MaxConcurrency);
                store.SetInt(IniSchema.SectionGeneral, IniSchema.KeyPortTimeoutMs, settings.PortTimeoutMs);
                store.SetBool(IniSchema.SectionGeneral, IniSchema.KeyEnableRedBanner, settings.EnableRedBanner);

                store.Set(IniSchema.SectionScan, IniSchema.KeyLastNetSegment, settings.LastNetSegment);
                store.SetInt(IniSchema.SectionScan, IniSchema.KeyConfirmAboveHosts, settings.ConfirmAboveHosts);
                store.SetInt(IniSchema.SectionScan, IniSchema.KeyLastProbeMode, settings.LastProbeMode);
                store.SetInt(IniSchema.SectionScan, IniSchema.KeyPingTimeoutMs, settings.PingTimeoutMs);
                store.SetInt(IniSchema.SectionScan, IniSchema.KeyPingRetry, settings.PingRetry);

                store.SetInt(IniSchema.SectionProbe, IniSchema.KeySendArpRetry, settings.SendArpRetry);

                store.SetBool(IniSchema.SectionReport, IniSchema.KeySaveToDesktop, settings.SaveToDesktop);
                store.Set(IniSchema.SectionReport, IniSchema.KeyTimestampFormat, settings.TimestampFormat);

                store.Set(IniSchema.SectionSelfCheck, IniSchema.KeySaasDomain, settings.SaasDomain);
                store.Set(IniSchema.SectionSelfCheck, IniSchema.KeyDnsProbeIp, settings.DnsProbeIp);
                store.Set(IniSchema.SectionSelfCheck, IniSchema.KeyGatewayProbePorts, settings.GatewayProbePorts);

                // 角色定义整体重写，避免删除条目后留下孤儿节
                // 同时清掉旧格式节，实现平滑升级
                store.RemoveSectionsByPrefix(IniSchema.RoleSectionPrefix);
                store.RemoveSectionsByPrefix(IniSchema.LegacyPortRuleSectionPrefix);

                if (settings.Roles != null)
                {
                    for (int i = 0; i < settings.Roles.Count; i++)
                    {
                        RoleDefinition r = settings.Roles[i];

                        if (r == null) { continue; }

                        string section = IniSchema.RoleSectionPrefix + (i + 1);

                        store.Set(section, IniSchema.KeyRoleName, r.Name);
                        store.Set(section, IniSchema.KeyRolePorts, r.PortsText);
                        store.Set(section, IniSchema.KeyComment, r.Comment);
                        store.SetBool(section, IniSchema.KeyEnabled, r.Enabled);
                    }
                }

                return store.Save(out errorMessage);
            }
            catch (Exception ex)
            {
                errorMessage = "配置无法保存：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 校验并纠正并发数。返回纠正后的值；若发生纠正，warning 非空。
        /// </summary>
        public static int NormalizeConcurrency(int value, out string warning)
        {
            warning = null;

            if (value < IniSchema.ConcurrencyMin)
            {
                warning = "并发数不得小于 " + IniSchema.ConcurrencyMin + "，已自动纠正。";
                return IniSchema.ConcurrencyMin;
            }

            if (value > IniSchema.ConcurrencyMax)
            {
                warning = "并发数不得大于 " + IniSchema.ConcurrencyMax + "，已自动纠正。";
                return IniSchema.ConcurrencyMax;
            }

            if (value > IniSchema.ConcurrencyWarnThreshold)
            {
                warning = "配置较低的电脑建议并发不超过 " + IniSchema.DefaultMaxConcurrency + "。";
            }

            return value;
        }

        /// <summary>
        /// 校验并纠正超时时间。
        /// </summary>
        public static int NormalizeTimeout(int value, out string warning)
        {
            warning = null;

            if (value < IniSchema.TimeoutMinMs)
            {
                warning = "超时时间不得小于 " + IniSchema.TimeoutMinMs + " ms，已自动纠正。";
                return IniSchema.TimeoutMinMs;
            }

            if (value > IniSchema.TimeoutMaxMs)
            {
                warning = "超时时间不得大于 " + IniSchema.TimeoutMaxMs + " ms，已自动纠正。";
                return IniSchema.TimeoutMaxMs;
            }

            return value;
        }

        /// <summary>兼容旧版 PortRule 校验。</summary>
        public static string ValidatePortRule(PortRule candidate, IList<PortRule> existing, int excludeIndex)
        {
            if (candidate == null) { return "规则为空。"; }
            if (!candidate.IsPortValid) { return "端口号必须在 1~65535 之间。"; }
            if (candidate.Role == null || candidate.Role.Trim().Length == 0) { return "角色名称不能为空。"; }
            if (existing != null)
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    if (i != excludeIndex && existing[i] != null && existing[i].Port == candidate.Port)
                    {
                        return "端口 " + candidate.Port + " 已存在（当前角色：" + existing[i].Role + "），不能重复。";
                    }
                }
            }
            return null;
        }

        /// <summary>校验新模型的角色定义。</summary>
        public static string ValidateRole(RoleDefinition candidate,
            IList<RoleDefinition> existing, int excludeIndex)
        {
            if (candidate == null) { return "角色为空。"; }
            if (candidate.Name.Length == 0) { return "角色名称不能为空。"; }
            if (candidate.Name == RolePriority.RoleUnknown) { return "未知是系统保留名称。"; }
            if (candidate.PortCount == 0) { return "该角色至少需要配置一个端口。"; }

            if (existing != null)
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    if (i == excludeIndex || existing[i] == null) { continue; }
                    if (existing[i].Name == candidate.Name) { return "角色名称重复。"; }

                    int[] ports = candidate.Ports;
                    for (int k = 0; k < ports.Length; k++)
                    {
                        if (existing[i].HasPort(ports[k]))
                        {
                            return "端口 " + ports[k] + " 已属于角色「" + existing[i].Name + "」。";
                        }
                    }
                }
            }
            return null;
        }
    }
}
