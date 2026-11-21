using System;
using System.Collections.Generic;
using System.Text;

namespace MQNetScan.Infrastructure
{
    /// <summary>
    /// 角色定义：**一个角色持有一组特征端口**。
    ///
    /// 数据模型说明（本项为 S13 重构，取代原先"一条端口 → 一个角色"的 PortRule）：
    ///
    ///   旧模型（问题）：按端口配置角色
    ///     [PortRule.1] Port=9100 Role=打印机
    ///     [PortRule.2] Port=515  Role=打印机
    ///     → 同一角色的端口被拆成多行，用户想给"打印机"加一个端口
    ///       必须新增一行并再次选择角色，容易写错、也难以整体启停。
    ///
    ///   新模型（本类）：按角色配置端口
    ///     [Role.1] Name=打印机 Ports=9100,515,631
    ///     → 角色与端口是"一对多"的自然关系，一个角色可整体启用/禁用，
    ///       端口列表可一次编辑完毕。
    ///
    /// 判定语义不变：一台主机命中多个角色时，仍按内置固定优先级取唯一角色。
    /// </summary>
    internal sealed class RoleDefinition
    {
        private readonly string _name;
        private readonly List<int> _ports;
        private string _comment;
        private bool _enabled;

        public RoleDefinition(string name)
            : this(name, new int[0], "", true)
        {
        }

        public RoleDefinition(string name, int[] ports)
            : this(name, ports, "", true)
        {
        }

        public RoleDefinition(string name, int[] ports, string comment)
            : this(name, ports, comment, true)
        {
        }

        public RoleDefinition(string name, int[] ports, string comment, bool enabled)
        {
            _name = name == null ? "" : name.Trim();
            _ports = new List<int>();
            _comment = comment == null ? "" : comment;
            _enabled = enabled;

            if (ports != null)
            {
                for (int i = 0; i < ports.Length; i++)
                {
                    AddPort(_ports, ports[i]);
                }
            }
        }

        /// <summary>角色显示名（内置角色或用户自定义）。</summary>
        public string Name { get { return _name; } }

        /// <summary>该角色的特征端口（升序、去重）。</summary>
        public int[] Ports { get { return _ports.ToArray(); } }

        /// <summary>端口数量。</summary>
        public int PortCount { get { return _ports.Count; } }

        /// <summary>备注说明。</summary>
        public string Comment
        {
            get { return _comment; }
            set { _comment = value == null ? "" : value; }
        }

        /// <summary>是否启用。禁用后该角色不参与扫描与判定。</summary>
        public bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; }
        }

        /// <summary>角色名是否有效（非空且非"未知"）。</summary>
        public bool IsNameValid
        {
            get { return _name.Length > 0; }
        }

        /// <summary>
        /// 该角色是否可用于扫描：名称有效、已启用、且至少有一个合法端口。
        /// </summary>
        public bool IsUsable
        {
            get { return _enabled && IsNameValid && _ports.Count > 0; }
        }

        /// <summary>端口列表的文本形式（逗号分隔，用于界面与 ini）。</summary>
        public string PortsText
        {
            get
            {
                if (_ports.Count == 0) { return ""; }

                StringBuilder sb = new StringBuilder();

                for (int i = 0; i < _ports.Count; i++)
                {
                    if (i > 0) { sb.Append(','); }
                    sb.Append(_ports[i]);
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// 解析端口文本（如 "9100,515,631"）。忽略非法项与重复项。
        /// </summary>
        /// <param name="text">逗号/分号/空格分隔的端口串。</param>
        /// <param name="ignored">被忽略的原始片段。</param>
        public static int[] ParsePorts(string text, out string ignored)
        {
            ignored = "";

            if (string.IsNullOrEmpty(text)) { return new int[0]; }

            string[] parts = text.Split(new char[] { ',', '，', ';', '；', ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);

            List<int> result = new List<int>();
            StringBuilder bad = new StringBuilder();

            for (int i = 0; i < parts.Length; i++)
            {
                int port;

                if (int.TryParse(parts[i].Trim(), out port) && port >= 1 && port <= 65535)
                {
                    AddPort(result, port);
                }
                else
                {
                    if (bad.Length > 0) { bad.Append('、'); }
                    bad.Append(parts[i].Trim());
                }
            }

            ignored = bad.ToString();

            return result.ToArray();
        }

        /// <summary>添加端口（忽略非法值与重复值）。返回是否实际添加。</summary>
        public bool AddPortValue(int port)
        {
            if (port < 1 || port > 65535) { return false; }

            return AddPort(_ports, port);
        }

        /// <summary>移除端口。返回是否实际移除。</summary>
        public bool RemovePort(int port)
        {
            return _ports.Remove(port);
        }

        /// <summary>是否包含某端口。</summary>
        public bool HasPort(int port)
        {
            return _ports.Contains(port);
        }

        /// <summary>批量设置端口。</summary>
        public void SetPorts(int[] ports)
        {
            _ports.Clear();

            if (ports == null) { return; }

            for (int i = 0; i < ports.Length; i++)
            {
                AddPort(_ports, ports[i]);
            }
        }

        /// <summary>
        /// 内部统一的添加逻辑：过滤非法值、保持升序、去重。
        /// </summary>
        private static bool AddPort(List<int> target, int port)
        {
            if (port < 1 || port > 65535) { return false; }
            if (target.Contains(port)) { return false; }

            target.Add(port);
            target.Sort();

            return true;
        }

        /// <summary>
        /// 取全部可用角色的端口集合（去重升序），供扫描使用。
        /// </summary>
        public static int[] CollectPorts(IList<RoleDefinition> roles)
        {
            List<int> ports = new List<int>();

            if (roles == null) { return new int[0]; }

            for (int i = 0; i < roles.Count; i++)
            {
                RoleDefinition r = roles[i];

                if (r == null || !r.IsUsable) { continue; }

                int[] mine = r.Ports;

                for (int k = 0; k < mine.Length; k++)
                {
                    if (!ports.Contains(mine[k])) { ports.Add(mine[k]); }
                }
            }

            ports.Sort();

            return ports.ToArray();
        }

        public override string ToString()
        {
            return _name + " [" + PortsText + "]";
        }
    }
}
