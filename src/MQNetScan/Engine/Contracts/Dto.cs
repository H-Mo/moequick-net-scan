using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MQNetScan.Engine.Contracts
{
    /// <summary>
    /// 一台存活主机的完整记录（结果表一行）。
    /// 设计要点：字段保持精简，OpenPorts 用 int[] 而非 List&lt;int&gt;，减少装箱与扩容开销（architecture §8）。
    /// </summary>
    internal sealed class HostRecord
    {
        private static readonly int[] EmptyPorts = new int[0];

        private readonly uint _ipAddress;
        private readonly string _macAddress;
        private string _deviceName;
        private readonly int[] _openPorts;
        private string _role;
        private DeviceConfidence _confidence;
        private string _evidence;
        private string _detectionSource;
        private Ping3Result _ping3;
        private bool _hasPing3;
        private bool _conflictFlag;
        private int _index;

        public HostRecord(uint ipAddress, string macAddress, int[] openPorts)
        {
            _ipAddress = ipAddress;
            _macAddress = macAddress == null ? "" : macAddress;
            _deviceName = "";
            _openPorts = openPorts == null ? EmptyPorts : openPorts;
            _role = RolePriority.RoleUnknown;
            _confidence = DeviceConfidence.Unknown;
            _evidence = "";
            _detectionSource = "";
        }

        /// <summary>行号（从 1 开始，由 UI 赋值）。</summary>
        public int Index
        {
            get { return _index; }
            set { _index = value; }
        }

        /// <summary>IPv4 地址（uint 形式）。</summary>
        public uint IpAddress { get { return _ipAddress; } }

        /// <summary>IPv4 文本形式。</summary>
        public string IpText
        {
            get { return Ipv4Util.Format(_ipAddress); }
        }

        /// <summary>MAC 地址（大写冒号分段）。</summary>
        public string MacAddress { get { return _macAddress; } }

        /// <summary>尽力解析得到的设备名称，未知时为空。</summary>
        public string DeviceName
        {
            get { return _deviceName; }
            set { _deviceName = NormalizeDeviceName(value); }
        }

        private static string NormalizeDeviceName(string value)
        {
            if (string.IsNullOrEmpty(value)) { return ""; }
            string name = value.Trim();
            if (name.Length > 253) { name = name.Substring(0, 253); }
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsControl(name[i])) { return ""; }
            }
            return name;
        }

        /// <summary>OUI 前缀（MAC 前 3 字节），用作厂商线索。</summary>
        public string OuiPrefix
        {
            get
            {
                if (_macAddress.Length < 8) { return _macAddress; }
                return _macAddress.Substring(0, 8);
            }
        }

        /// <summary>开放端口（升序）。</summary>
        public int[] OpenPorts { get { return _openPorts; } }

        /// <summary>开放端口的逗号分隔文本；无开放端口时为空串。</summary>
        public string OpenPortsText
        {
            get
            {
                if (_openPorts.Length == 0) { return ""; }

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < _openPorts.Length; i++)
                {
                    if (i > 0) { sb.Append(", "); }
                    sb.Append(_openPorts[i].ToString(CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
        }

        /// <summary>角色（由 RoleResolver 判定）。</summary>
        public string Role
        {
            get { return _role; }
            set { _role = value == null ? RolePriority.RoleUnknown : value; }
        }

        /// <summary>识别可信度。</summary>
        public DeviceConfidence Confidence
        {
            get { return _confidence; }
            set { _confidence = value; }
        }

        /// <summary>识别依据，例如“端口 5555（ADB）”或“默认网关”。</summary>
        public string Evidence
        {
            get { return _evidence; }
            set { _evidence = value == null ? "" : value; }
        }

        /// <summary>识别来源，例如 Ports、Gateway、SSDP、mDNS、OUI。</summary>
        public string DetectionSource
        {
            get { return _detectionSource; }
            set { _detectionSource = value == null ? "" : value; }
        }

        /// <summary>是否已做过深度测速。</summary>
        public bool HasPing3 { get { return _hasPing3; } }

        /// <summary>深度测速结果（未测速时 HasPing3 为 false）。</summary>
        public Ping3Result Ping3
        {
            get { return _ping3; }
            set { _ping3 = value; _hasPing3 = true; }
        }

        /// <summary>是否被标记为疑似 IP 冲突。</summary>
        public bool ConflictFlag
        {
            get { return _conflictFlag; }
            set { _conflictFlag = value; }
        }

        /// <summary>深度测速列的显示文本：未测速时为 "—"。</summary>
        public string Ping3Text
        {
            get
            {
                if (!_hasPing3) { return "—"; }
                if (_ping3 == null) { return "—"; }
                return _ping3.ToDisplayText();
            }
        }

        public override string ToString()
        {
            return IpText + " [" + _macAddress + "] " + _role;
        }
    }

    /// <summary>
    /// 深度测速（3 次握手）结果。
    /// </summary>
    internal sealed class Ping3Result
    {
        private readonly int[] _attemptsMs;
        private readonly int _successCount;
        private readonly int _port;

        public Ping3Result(int[] attemptsMs, int successCount, int port)
        {
            _attemptsMs = attemptsMs == null ? new int[0] : attemptsMs;
            _successCount = successCount;
            _port = port;
        }

        /// <summary>3 次握手各自的耗时（失败的记为 -1）。</summary>
        public int[] AttemptsMs { get { return _attemptsMs; } }

        /// <summary>成功的次数。</summary>
        public int SuccessCount { get { return _successCount; } }

        /// <summary>测速使用的端口。</summary>
        public int Port { get { return _port; } }

        /// <summary>成功次数为 0 时返回 -1。</summary>
        public int AverageMs
        {
            get
            {
                if (_successCount <= 0) { return -1; }

                int sum = 0;
                int n = 0;

                for (int i = 0; i < _attemptsMs.Length; i++)
                {
                    if (_attemptsMs[i] >= 0) { sum += _attemptsMs[i]; n++; }
                }

                if (n == 0) { return -1; }
                return sum / n;
            }
        }

        /// <summary>
        /// 结果表格中的显示文本，如 "12/15/11 ms，均值 12"。
        /// </summary>
        public string ToDisplayText()
        {
            if (_successCount <= 0) { return "3 次均失败"; }

            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < _attemptsMs.Length; i++)
            {
                if (i > 0) { sb.Append("/"); }
                sb.Append(_attemptsMs[i] < 0 ? "X" : _attemptsMs[i].ToString(CultureInfo.InvariantCulture));
            }

            sb.Append(" ms，均值 ");
            sb.Append(AverageMs.ToString(CultureInfo.InvariantCulture));

            return sb.ToString();
        }
    }

    /// <summary>
    /// 扫描参数快照。
    /// 会话创建后不可变（architecture §3.5 / §4 决定 A），保证同一份报告内口径一致。
    /// </summary>
    internal sealed class ScanParameters
    {
        private readonly int _concurrency;
        private readonly int _portTimeoutMs;
        private readonly int _arpRetry;
        private readonly int _pingTimeoutMs;
        private readonly int _pingRetry;
        private readonly int[] _enabledPorts;
        private readonly ProbeMode _probeMode;
        private readonly uint[] _targets;

        public ScanParameters(uint[] targets, int concurrency, int portTimeoutMs, int arpRetry, int[] enabledPorts)
            : this(targets, concurrency, portTimeoutMs, arpRetry, enabledPorts, ProbeMode.Arp, 800, 1)
        {
        }

        public ScanParameters(uint[] targets, int concurrency, int portTimeoutMs, int arpRetry,
            int[] enabledPorts, ProbeMode probeMode, int pingTimeoutMs)
            : this(targets, concurrency, portTimeoutMs, arpRetry, enabledPorts, probeMode, pingTimeoutMs, 1)
        {
        }

        public ScanParameters(uint[] targets, int concurrency, int portTimeoutMs, int arpRetry,
            int[] enabledPorts, ProbeMode probeMode, int pingTimeoutMs, int pingRetry)
        {
            _targets = targets == null ? new uint[0] : targets;
            _concurrency = concurrency;
            _portTimeoutMs = portTimeoutMs;
            _arpRetry = arpRetry;
            _enabledPorts = enabledPorts == null ? new int[0] : enabledPorts;
            _probeMode = probeMode;
            _pingTimeoutMs = pingTimeoutMs < 1 ? 800 : pingTimeoutMs;
            _pingRetry = pingRetry < 1 ? 1 : (pingRetry > 3 ? 3 : pingRetry);
        }

        /// <summary>目标地址列表（不可变）。</summary>
        public uint[] Targets { get { return _targets; } }

        /// <summary>工作线程数。</summary>
        public int Concurrency { get { return _concurrency; } }

        /// <summary>单端口超时（毫秒）。</summary>
        public int PortTimeoutMs { get { return _portTimeoutMs; } }

        /// <summary>ARP 重试次数。</summary>
        public int ArpRetry { get { return _arpRetry; } }

        /// <summary>本次存活探测方式。</summary>
        public ProbeMode ProbeMode { get { return _probeMode; } }

        /// <summary>ICMP Ping 单次超时（毫秒）。</summary>
        public int PingTimeoutMs { get { return _pingTimeoutMs; } }

        /// <summary>ICMP Ping 尝试次数（1~3）。</summary>
        public int PingRetry { get { return _pingRetry; } }

        /// <summary>启用的特征端口（已去重升序）。</summary>
        public int[] EnabledPorts { get { return _enabledPorts; } }

        /// <summary>目标地址总数。</summary>
        public int TargetCount { get { return _targets.Length; } }
    }

    /// <summary>
    /// 进度快照（只读）。
    /// 由 UI 线程周期读取，用于刷新进度条与状态栏。
    /// </summary>
    internal sealed class ScanProgress
    {
        private readonly ScanState _state;
        private readonly int _total;
        private readonly int _done;
        private readonly int _aliveCount;
        private readonly long _elapsedMs;
        private readonly bool _truncated;

        public ScanProgress(ScanState state, int total, int done, int aliveCount, long elapsedMs, bool truncated)
        {
            _state = state;
            _total = total;
            _done = done;
            _aliveCount = aliveCount;
            _elapsedMs = elapsedMs;
            _truncated = truncated;
        }

        public ScanState State { get { return _state; } }
        public int Total { get { return _total; } }
        public int Done { get { return _done; } }
        public int AliveCount { get { return _aliveCount; } }

        /// <summary>已用时长（毫秒，已扣除暂停时间）。</summary>
        public long ElapsedMs { get { return _elapsedMs; } }

        /// <summary>结果是否因超过行数上限而被截断。</summary>
        public bool Truncated { get { return _truncated; } }

        /// <summary>完成百分比（0~100）。</summary>
        public int Percent
        {
            get
            {
                if (_total <= 0) { return 0; }
                if (_done >= _total) { return 100; }

                int p = (int)((long)_done * 100L / _total);
                if (p < 0) { p = 0; }
                if (p > 100) { p = 100; }
                return p;
            }
        }

        /// <summary>已用时长的可读文本，如 "00:01:42"。</summary>
        public string ElapsedText
        {
            get
            {
                TimeSpan t = TimeSpan.FromMilliseconds(_elapsedMs < 0 ? 0 : _elapsedMs);
                return t.Hours.ToString("00", CultureInfo.InvariantCulture) + ":"
                    + t.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":"
                    + t.Seconds.ToString("00", CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// 一条告警（红色横幅或一般提示）。
    /// </summary>
    internal sealed class AlertItem
    {
        private readonly AlertKind _kind;
        private readonly string _message;
        private readonly string _suggestion;
        private readonly DateTime _timestamp;
        private readonly string _ipText;

        public AlertItem(AlertKind kind, string message, string suggestion)
            : this(kind, message, suggestion, null)
        {
        }

        public AlertItem(AlertKind kind, string message, string suggestion, string ipText)
        {
            _kind = kind;
            _message = message == null ? "" : message;
            _suggestion = suggestion == null ? "" : suggestion;
            _timestamp = DateTime.Now;
            _ipText = ipText;
        }

        public AlertKind Kind { get { return _kind; } }
        public string Message { get { return _message; } }
        public string Suggestion { get { return _suggestion; } }
        public DateTime Timestamp { get { return _timestamp; } }
        public string IpText { get { return _ipText; } }

        /// <summary>是否为需要红色横幅的严重告警。</summary>
        public bool IsCritical
        {
            get { return _kind == AlertKind.GatewayUnreachable || _kind == AlertKind.IpConflict; }
        }

        /// <summary>横幅完整文本：结论 + 一句话建议。</summary>
        public string ToBannerText()
        {
            if (string.IsNullOrEmpty(_suggestion)) { return _message; }
            return _message + "　" + _suggestion;
        }
    }
}
