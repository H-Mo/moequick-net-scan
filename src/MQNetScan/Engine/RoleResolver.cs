using System;
using System.Collections.Generic;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;

namespace MQNetScan.Engine
{
    /// <summary>
    /// 角色判定器。
    ///
    /// 规则（PRD §7.2）：
    ///   优先级内置固定，**不可由用户修改**：
    ///   本地服务器 &gt; Windows 电脑 &gt; 打印机 &gt; 电子秤 &gt; 安卓设备 &gt; 网络设备 &gt; 未知
    ///
    ///   · 一台主机命中多个角色时，按上述优先级取**唯一**角色展示；
    ///   · 无任何角色端口开放 → "未知"；
    ///   · 用户自定义角色排在内置角色之后（优先级 Custom）。
    ///
    /// S13 变更：规则模型由"端口 → 角色"改为"角色 → 端口集合"，
    /// 判定语义保持不变（仍是"命中即候选，按优先级取最优"）。
    ///
    /// 本类是纯函数式判定，无状态，可完全单元测试。
    /// </summary>
    internal static class RoleResolver
    {
        /// <summary>
        /// 根据开放端口与角色定义判定角色。
        /// </summary>
        /// <param name="openPorts">开放端口（可含重复）。</param>
        /// <param name="roles">角色定义表。</param>
        /// <returns>唯一角色名；无匹配时返回 "未知"。</returns>
        public static string Resolve(int[] openPorts, IList<RoleDefinition> roles)
        {
            if (openPorts == null || openPorts.Length == 0)
            {
                return RolePriority.RoleUnknown;
            }

            if (roles == null || roles.Count == 0)
            {
                // 没有任何角色定义：只能靠 80/443 判网络设备，否则未知
                return HasNetworkPort(openPorts)
                    ? RolePriority.RoleNetworkDevice
                    : RolePriority.RoleUnknown;
            }

            string bestRole = null;
            int bestPriority = int.MaxValue;

            for (int i = 0; i < roles.Count; i++)
            {
                RoleDefinition role = roles[i];

                if (role == null || !role.IsUsable) { continue; }

                // 该角色的任一端口开放即视为命中
                int[] mine = role.Ports;
                bool hit = false;

                for (int k = 0; k < mine.Length; k++)
                {
                    if (ContainsPort(openPorts, mine[k])) { hit = true; break; }
                }

                if (!hit) { continue; }

                int priority = RolePriority.GetPriority(role.Name);

                // 同优先级时保留先出现者，保证结果稳定
                if (priority < bestPriority)
                {
                    bestPriority = priority;
                    bestRole = role.Name;
                }
            }

            if (bestRole != null) { return bestRole; }

            // 未命中任何角色，但有 80/443 → 网络设备
            if (HasNetworkPort(openPorts)) { return RolePriority.RoleNetworkDevice; }

            return RolePriority.RoleUnknown;
        }

        /// <summary>兼容旧版测试/调用方：把端口规则展开为角色定义后判定。</summary>
        public static string Resolve(int[] openPorts, IList<PortRule> rules)
        {
            List<RoleDefinition> roles = new List<RoleDefinition>();
            if (rules != null)
            {
                Dictionary<string, RoleDefinition> map = new Dictionary<string, RoleDefinition>();
                for (int i = 0; i < rules.Count; i++)
                {
                    PortRule r = rules[i];
                    if (r == null) { continue; }
                    string name = RolePriority.MigrateLegacyName(r.Role);
                    RoleDefinition def;
                    if (!map.TryGetValue(name, out def))
                    {
                        def = new RoleDefinition(name, new int[0], r.Comment, r.Enabled);
                        map[name] = def;
                        roles.Add(def);
                    }
                    def.AddPortValue(r.Port);
                    if (!r.Enabled) { def.Enabled = false; }
                }
            }
            return Resolve(openPorts, roles);
        }

        /// <summary>兼容旧版优先级重载。</summary>
        public static string Resolve(int[] openPorts, IList<PortRule> rules, out int priority)
        {
            string role = Resolve(openPorts, rules);
            priority = role == RolePriority.RoleUnknown ? RolePriority.Unknown : RolePriority.GetPriority(role);
            if (priority == RolePriority.Unknown && role != RolePriority.RoleUnknown) { priority = RolePriority.Custom; }
            return role;
        }
        private static bool HasNetworkPort(int[] openPorts)
        {
            for (int i = 0; i < openPorts.Length; i++)
            {
                if (openPorts[i] == 80 || openPorts[i] == 443) { return true; }
            }

            return false;
        }

        private static bool ContainsPort(int[] ports, int port)
        {
            for (int i = 0; i < ports.Length; i++)
            {
                if (ports[i] == port) { return true; }
            }

            return false;
        }

        /// <summary>
        /// 判断某端口是否属于"网络设备"专用端口。
        /// </summary>
        public static bool IsNetworkDevicePort(int port)
        {
            return port == 80 || port == 443;
        }

        /// <summary>
        /// 把端口数组格式化为升序去重的结果。
        /// </summary>
        public static int[] NormalizePorts(List<int> ports)
        {
            if (ports == null || ports.Count == 0) { return new int[0]; }

            List<int> sorted = new List<int>(ports);
            sorted.Sort();

            List<int> result = new List<int>(sorted.Count);

            for (int i = 0; i < sorted.Count; i++)
            {
                if (i == 0 || sorted[i] != sorted[i - 1])
                {
                    result.Add(sorted[i]);
                }
            }

            return result.ToArray();
        }
    }
}
