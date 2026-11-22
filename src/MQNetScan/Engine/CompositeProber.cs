using System;
using System.Collections.Generic;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;

namespace MQNetScan.Engine
{
    /// <summary>
    /// 组合探测器：ARP 存活判定 + 特征端口扫描 + 角色判定。
    ///
    /// 单地址处理链（architecture §3.3）：
    ///   1. SendARP 判存活（无 MAC 响应则直接结束，不入列表）；
    ///   2. 对开放端口做非阻塞 connect 扫描；
    ///   3. 按规则表判定角色；
    ///   4. 组装 HostRecord。
    ///
    /// 失败降级：
    ///   · ARP 失败 → 返回 null（该地址不入列表）；
    ///   · 端口扫描失败 → 端口列为空，角色为"未知"，但主机仍入列表
    ///     （已确认存活，不能因为端口扫不到就丢弃）。
    /// </summary>
    internal sealed class CompositeProber : IHostProber
    {
        private readonly IHostProber _livenessProber;
        private readonly ArpProber _arpProber;
        private readonly bool _hasMacEvidence;
        private readonly IList<RoleDefinition> _roles;
        private readonly ConflictDetector _conflictDetector;

        private int _portScanFailures;
        private int _conflictCount;
        private string _lastConflictMessage = "";

        /// <summary>被"网关代理回应"过滤器拦下的次数（跨网段假阳性）。</summary>
        private int _gatewayProxyFiltered;
        private int _selfFiltered;

        public CompositeProber(ArpProber arpProber, IList<PortRule> rules)
            : this(arpProber, ConvertLegacyRules(rules), null, 0, "")
        {
        }

        private static IList<RoleDefinition> ConvertLegacyRules(IList<PortRule> rules)
        {
            List<RoleDefinition> roles = new List<RoleDefinition>();
            Dictionary<string, RoleDefinition> map = new Dictionary<string, RoleDefinition>();
            if (rules == null) { return roles; }
            for (int i = 0; i < rules.Count; i++)
            {
                PortRule old = rules[i];
                if (old == null) { continue; }
                string name = RolePriority.MigrateLegacyName(old.Role);
                RoleDefinition role;
                if (!map.TryGetValue(name, out role))
                {
                    role = new RoleDefinition(name, new int[0], old.Comment, old.Enabled);
                    map[name] = role;
                    roles.Add(role);
                }
                role.AddPortValue(old.Port);
            }
            return roles;
        }

        /// <param name="conflictDetector">
        /// IP 冲突检测器；传 null 表示不检测冲突。
        /// 传入时必须**先调用 CaptureBaseline()** 记录扫描前 ARP 表。
        /// </param>
        public CompositeProber(ArpProber arpProber, IList<RoleDefinition> roles,
            ConflictDetector conflictDetector)
            : this(arpProber, roles, conflictDetector, 0, "")
        {
        }

        /// <param name="localAddress">
        /// 本机 IP（网络序）。非 0 时本机地址不入结果列表（PRD FR-1.1.7）。
        /// </param>
        /// <param name="gatewayMac">
        /// 默认网关的 MAC（形如 52:54:00:63:FB:B8）。非空时用于识别
        /// "网关代理回应"造成的跨网段假阳性（S13 修复 F1）。
        /// </param>
        public CompositeProber(ArpProber arpProber, IList<RoleDefinition> roles,
            ConflictDetector conflictDetector, uint localAddress, string gatewayMac)
            : this(arpProber, roles, conflictDetector, localAddress, gatewayMac, ProbeMode.Arp, null)
        {
        }

        public CompositeProber(ArpProber arpProber, IList<RoleDefinition> roles,
            ConflictDetector conflictDetector, uint localAddress, string gatewayMac,
            ProbeMode probeMode, IHostProber pingProber)
        {
            _arpProber = arpProber == null ? new ArpProber() : arpProber;
            _roles = roles;
            _conflictDetector = conflictDetector;
            _localAddress = localAddress;
            _gatewayMac = NormalizeMac(gatewayMac);
            _hasMacEvidence = probeMode == ProbeMode.Arp;
            _livenessProber = pingProber != null
                ? pingProber
                : (_hasMacEvidence ? (IHostProber)_arpProber : (IHostProber)new PingProber());
        }

        private readonly uint _localAddress;
        private readonly string _gatewayMac;

        /// <summary>端口扫描失败的次数（用于诊断）。</summary>
        public int PortScanFailures { get { return _portScanFailures; } }

        /// <summary>检测到的 IP 冲突数量。</summary>
        public int ConflictCount { get { return _conflictCount; } }

        /// <summary>被过滤的「网关代理回应」数量（跨网段假阳性）。</summary>
        public int GatewayProxyFiltered { get { return _gatewayProxyFiltered; } }

        /// <summary>被过滤的本机地址数量（PRD FR-1.1.7）。</summary>
        public int SelfFiltered { get { return _selfFiltered; } }

        /// <summary>底层 ARP 探测器（便于读取限流统计）。</summary>
        public ArpProber ArpProber { get { return _arpProber; } }

        public HostRecord Probe(uint address, ScanParameters parameters, Func<bool> shouldContinue)
        {
            // ---- 0. 本机地址不入结果列表（PRD FR-1.1.7）----
            //
            // 注意：本机仍参与网关探测与冲突检测，只是不作为"发现设备"展示。
            if (_localAddress != 0 && address == _localAddress)
            {
                _selfFiltered++;
                return null;
            }

            // ---- 1. 按用户选择的方式判定存活 ----
            HostRecord alive = _livenessProber.Probe(address, parameters, shouldContinue);

            if (alive == null) { return null; }   // 无响应：不入列表（PRD FR-1.2.2）

            // ---- 1.5 网关代理回应过滤（S13 修复 F1）----
            //
            // 背景（实测发现）：当目标 IP 不在本机子网时，Windows 会把 ARP 请求
            // 交给默认网关；某些网关（如 libvirt NAT、部分企业网关）会**代理回应**，
            // 于是工具收到"网关自己的 MAC"，把根本不存在的远端地址误判为存活。
            //
            // 识别依据：返回的 MAC 等于默认网关 MAC，但该 IP 又不是网关本身。
            // 真实设备不可能恰好拥有网关的 MAC，因此这个判据是安全的。
            if (_hasMacEvidence
                && _gatewayMac.Length > 0
                && NormalizeMac(alive.MacAddress) == _gatewayMac
                && _gatewayMac != "00:00:00:00:00:00")
            {
                _gatewayProxyFiltered++;
                return null;
            }

            // ---- 2. IP 冲突观测（PRD FR-1.5.2）----
            bool conflicted = false;

            if (_hasMacEvidence && _conflictDetector != null)
            {
                string conflictMessage;

                if (_conflictDetector.Observe(address, alive.MacAddress, out conflictMessage))
                {
                    conflicted = true;

                    // 只在首次发现时计数，避免重复累加
                    if (_conflictCount == 0
                        || conflictMessage != _lastConflictMessage)
                    {
                        _conflictCount++;
                        _lastConflictMessage = conflictMessage;
                    }
                }
            }

            // ---- 2. 端口扫描 ----
            int[] enabledPorts = parameters == null ? new int[0] : parameters.EnabledPorts;
            int timeout = parameters == null ? 800 : parameters.PortTimeoutMs;

            int[] openPorts;

            try
            {
                openPorts = ScanPorts(address, enabledPorts, timeout, shouldContinue);
            }
            catch (Exception)
            {
                // 端口扫描失败不能丢弃已确认存活的主机
                _portScanFailures++;
                openPorts = new int[0];
            }

            // ---- 3. 角色判定 ----
            string role = RoleResolver.Resolve(openPorts, _roles);

            // ---- 4. 组装记录 ----
            HostRecord record = new HostRecord(address, alive.MacAddress, openPorts);
            record.Role = role;
            record.ConflictFlag = conflicted;

            return record;
        }

        /// <summary>
        /// 把 MAC 统一成大写冒号分隔形式，便于比较。
        /// </summary>
        private static string NormalizeMac(string mac)
        {
            if (string.IsNullOrEmpty(mac)) { return ""; }

            string s = mac.Trim().ToUpperInvariant().Replace('-', ':');

            // 过滤掉明显无效的 MAC
            if (s == "00:00:00:00:00:00" || s == "FF:FF:FF:FF:FF:FF") { return ""; }

            return s;
        }

        /// <summary>
        /// 扫描端口并返回开放端口列表（升序去重）。
        /// </summary>
        private static int[] ScanPorts(
            uint address, int[] ports, int timeoutMs, Func<bool> shouldContinue)
        {
            if (ports == null || ports.Length == 0) { return new int[0]; }

            List<PortProbeResult> results = PortProber.ProbePorts(
                address, ports, timeoutMs, shouldContinue);

            List<int> open = new List<int>();

            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].IsOpen) { open.Add(results[i].Port); }
            }

            return RoleResolver.NormalizePorts(open);
        }
    }

    /// <summary>
    /// 单点测通器（供 Tab 2 使用）。
    /// 支持 IP 或主机名，区分拒绝/超时/不可达/DNS 失败。
    /// </summary>
    internal sealed class SinglePortTester
    {
        /// <summary>测通结果。</summary>
        internal sealed class Result
        {
            public bool Resolved;
            public uint IpAddress;
            public string IpText;
            public int Port;
            public PortStatus Status;
            public int ElapsedMs;
            public string FailureReason;
            public string DnsError;

            public bool IsOpen { get { return Status == PortStatus.Open; } }

            public string Describe()
            {
                if (!Resolved)
                {
                    return "DNS 解析失败" + (string.IsNullOrEmpty(DnsError) ? "" : "：" + DnsError);
                }

                if (IsOpen)
                {
                    return "通　" + IpText + ":" + Port + "　耗时 " + ElapsedMs + " ms";
                }

                return "不通　" + IpText + ":" + Port + "　" + FailureReason
                    + "（耗时 " + ElapsedMs + " ms）";
            }
        }

        /// <summary>
        /// 执行一次握手测通。同步阻塞，调用方须在后台线程执行。
        /// </summary>
        public static Result Test(string hostOrIp, int port, int timeoutMs)
        {
            Result r = new Result();
            r.Port = port;

            if (timeoutMs < 1) { timeoutMs = 1; }

            // ---- 解析目标 ----
            uint ipValue;

            if (Ipv4Util.TryParse(hostOrIp, out ipValue))
            {
                r.Resolved = true;
                r.IpAddress = ipValue;
                r.IpText = Ipv4Util.Format(ipValue);
            }
            else
            {
                try
                {
                    System.Net.IPAddress[] addresses =
                        System.Net.Dns.GetHostAddresses(hostOrIp);

                    bool found = false;

                    for (int i = 0; i < addresses.Length; i++)
                    {
                        if (addresses[i].AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            continue;
                        }

                        r.IpAddress = NetworkAdapterService.IpToUInt(addresses[i]);
                        r.IpText = Ipv4Util.Format(r.IpAddress);
                        found = true;
                        break;
                    }

                    r.Resolved = found;

                    if (!found) { r.DnsError = "未返回 IPv4 地址"; }
                }
                catch (Exception ex)
                {
                    r.Resolved = false;
                    r.DnsError = ex.Message;
                }
            }

            if (!r.Resolved)
            {
                r.Status = PortStatus.Unknown;
                r.FailureReason = "DNS 解析失败";
                return r;
            }

            // ---- 握手 ----
            PortProbeResult pr = PortProber.ProbeSingle(r.IpAddress, port, timeoutMs);

            r.Status = pr.Status;
            r.ElapsedMs = pr.ElapsedMs;
            r.FailureReason = pr.DescribeFailure();

            return r;
        }
    }
}
