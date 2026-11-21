namespace MQNetScan.Infrastructure
{
    /// <summary>
    /// 端口特征规则条目（PRD FR-4.2）。
    /// 描述"某端口开放时，可将主机判定为某角色"。
    /// </summary>
    internal sealed class PortRule
    {
        private readonly int _port;
        private readonly string _role;
        private readonly string _comment;
        private bool _enabled;

        public PortRule(int port, string role, string comment)
            : this(port, role, comment, true)
        {
        }

        public PortRule(int port, string role, string comment, bool enabled)
        {
            _port = port;
            _role = role == null ? "" : role;
            _comment = comment == null ? "" : comment;
            _enabled = enabled;
        }

        /// <summary>端口号（1~65535）。</summary>
        public int Port { get { return _port; } }

        /// <summary>角色显示名（内置角色或用户自定义）。</summary>
        public string Role { get { return _role; } }

        /// <summary>备注说明。</summary>
        public string Comment { get { return _comment; } }

        /// <summary>是否启用。禁用后不参与扫描。</summary>
        public bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; }
        }

        /// <summary>端口号是否在合法范围内。</summary>
        public bool IsPortValid
        {
            get { return _port >= 1 && _port <= 65535; }
        }

        /// <summary>该条目是否可用于扫描（端口合法且已启用）。</summary>
        public bool IsUsable
        {
            get { return IsPortValid && _enabled && !string.IsNullOrEmpty(_role.Trim()); }
        }

        public override string ToString()
        {
            return _port.ToString() + " -> " + _role;
        }
    }
}
