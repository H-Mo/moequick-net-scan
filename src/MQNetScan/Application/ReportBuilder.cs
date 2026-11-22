using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MQNetScan.Engine;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;

namespace MQNetScan.Application
{
    /// <summary>
    /// 报告生成所需的数据输入。
    /// </summary>
    internal sealed class ReportInput
    {
        /// <summary>扫描目标范围描述（如 192.168.1.0/24 或 起止区间）。</summary>
        public string TargetText;

        /// <summary>源网卡描述。</summary>
        public string AdapterText;

        /// <summary>探测方式（ARP / ICMP Ping）。</summary>
        public string ProbeModeText;

        /// <summary>本机地址。</summary>
        public string LocalIpText;

        /// <summary>并发数。</summary>
        public int Concurrency;

        /// <summary>单端口超时（毫秒）。</summary>
        public int PortTimeoutMs;

        /// <summary>特征端口数量。</summary>
        public int PortCount;

        /// <summary>目标地址总数。</summary>
        public int TargetCount;

        /// <summary>存活主机数。</summary>
        public int AliveCount;

        /// <summary>已用时长文本（如 00:01:42）。</summary>
        public string ElapsedText;

        /// <summary>设备明细（按界面显示顺序）。</summary>
        public IList<HostRecord> Hosts;

        /// <summary>异常告警。</summary>
        public IList<AlertItem> Alerts;

        /// <summary>扫描是否被截断（超过 5000 行）。</summary>
        public bool Truncated;

        /// <summary>扫描最终状态描述（完成 / 已终止）。</summary>
        public string FinalStateText;

        /// <summary>生成时间。</summary>
        public DateTime GeneratedAt;
    }

    /// <summary>
    /// 诊断报告生成器。
    ///
    /// 输出四段固定结构（PRD FR-1.7.4）：
    ///   一、设备明细
    ///   二、异常告警
    ///   三、结论与建议
    ///   （若用户做过深度测速，追加"深度测速记录"小节）
    ///
    /// 设计约束：
    ///   · 纯字符串拼接，不写文件（由调用方负责落盘）；
    ///   · 报告内容必须与界面列表**完全一致**（数据源就是界面已接受的记录）。
    /// </summary>
    internal static class ReportBuilder
    {
        /// <summary>分隔线宽度。</summary>
        private const int LineWidth = 72;

        /// <summary>
        /// 生成报告全文。
        /// </summary>
        public static string Build(ReportInput input)
        {
            if (input == null) { return ""; }

            StringBuilder sb = new StringBuilder();

            AppendHeader(sb, input);
            AppendHosts(sb, input);
            AppendAlerts(sb, input);
            AppendConclusion(sb, input);
            AppendPing3Section(sb, input);
            AppendFooter(sb);

            return sb.ToString();
        }

        // -------------------------------------------------------------------
        // 表头
        // -------------------------------------------------------------------
        private static void AppendHeader(StringBuilder sb, ReportInput input)
        {
            AppendLine(sb, '=', "MoeQuick NetScan v1.0 - 萌快网维  局域网诊断报告", true);
            sb.AppendLine();

            AppendField(sb, "生成时间", input.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            AppendField(sb, "扫描范围", Safe(input.TargetText));
            AppendField(sb, "探测方式", Safe(input.ProbeModeText));
            AppendField(sb, "源网卡", Safe(input.AdapterText));
            AppendField(sb, "本机地址", Safe(input.LocalIpText));
            AppendField(sb, "扫描参数", "并发 " + input.Concurrency
                + " | 单端口超时 " + input.PortTimeoutMs + " ms"
                + " | 特征端口 " + input.PortCount + " 个");
            AppendField(sb, "目标地址数", input.TargetCount.ToString(CultureInfo.InvariantCulture));
            AppendField(sb, "存活主机数", input.AliveCount.ToString(CultureInfo.InvariantCulture));
            AppendField(sb, "本次耗时", Safe(input.ElapsedText));

            if (!string.IsNullOrEmpty(input.FinalStateText))
            {
                AppendField(sb, "扫描状态", input.FinalStateText);
            }

            if (input.Truncated)
            {
                AppendField(sb, "注意", "结果行数超过上限，明细仅列出前 5000 条");
            }

            sb.AppendLine();
        }

        // -------------------------------------------------------------------
        // 一、设备明细
        // -------------------------------------------------------------------
        private static void AppendHosts(StringBuilder sb, ReportInput input)
        {
            AppendLine(sb, '-', "一、设备明细", true);
            sb.AppendLine();

            if (input.Hosts == null || input.Hosts.Count == 0)
            {
                sb.AppendLine("本次扫描未发现存活设备。");
                sb.AppendLine();
                return;
            }

            // 列宽：编号(4) IP(16) MAC(18) 厂商前缀(11) 开放端口(20) 角色
            sb.AppendLine("编号  IP 地址          MAC 地址            厂商前缀    开放端口              角色        可信度  识别依据");
            sb.AppendLine("----  ---------------  ------------------  ----------  --------------------  ----------  ------  ------------------------------");

            for (int i = 0; i < input.Hosts.Count; i++)
            {
                HostRecord h = input.Hosts[i];

                string index = (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(4);
                string ip = PadRight(Safe(h.IpText), 15);
                string mac = PadRight(Truncate(Safe(h.MacAddress), 18), 18);
                string oui = PadRight(Truncate(Safe(h.OuiPrefix), 10), 10);
                string ports = PadRight(Truncate(Safe(h.OpenPortsText), 20), 20);
                string role = Safe(h.Role);
                string confidence = ConfidenceText(h.Confidence);
                string evidence = Truncate(Safe(h.Evidence), 30);

                sb.Append(index).Append("  ")
                  .Append(ip).Append("  ")
                  .Append(mac).Append("  ")
                  .Append(oui).Append("  ")
                  .Append(ports).Append("  ")
                  .Append(PadRight(role, 10)).Append("  ")
                  .Append(PadRight(confidence, 6)).Append("  ")
                  .Append(evidence).AppendLine();
            }

            sb.AppendLine();
        }

        // -------------------------------------------------------------------
        // 二、异常告警
        // -------------------------------------------------------------------
        private static void AppendAlerts(StringBuilder sb, ReportInput input)
        {
            AppendLine(sb, '-', "二、异常告警", true);
            sb.AppendLine();

            bool hasGatewayAlert = false;
            bool hasConflictAlert = false;

            if (input.Alerts != null)
            {
                for (int i = 0; i < input.Alerts.Count; i++)
                {
                    AlertItem a = input.Alerts[i];

                    if (a.Kind == AlertKind.GatewayUnreachable) { hasGatewayAlert = true; }
                    if (a.Kind == AlertKind.IpConflict) { hasConflictAlert = true; }

                    string tag = a.IsCritical ? "[警告]" : "[提示]";

                    sb.Append(tag).Append(' ').AppendLine(Safe(a.Message));
                }
            }

            // 即使没有告警，也要给出"正常"结论，避免报告显得空泛
            if (!hasGatewayAlert)
            {
                sb.AppendLine("[正常] 默认网关可达");
            }

            if (!hasConflictAlert)
            {
                sb.AppendLine("[正常] 未检测到 IP 冲突");
            }

            // 未知设备较多时给出提示（PRD 风险 R2）
            int unknownCount = CountUnknown(input.Hosts);

            if (unknownCount > 0)
            {
                sb.AppendLine("[提示] 有 " + unknownCount
                    + " 台设备未能识别角色，可在「设置」页补充对应端口规则后重新扫描");
            }

            sb.AppendLine();
        }

        private static int CountUnknown(IList<HostRecord> hosts)
        {
            if (hosts == null) { return 0; }

            int n = 0;

            for (int i = 0; i < hosts.Count; i++)
            {
                if (hosts[i].Role == RolePriority.RoleUnknown) { n++; }
            }

            return n;
        }

        // -------------------------------------------------------------------
        // 三、结论与建议
        // -------------------------------------------------------------------
        private static void AppendConclusion(StringBuilder sb, ReportInput input)
        {
            AppendLine(sb, '-', "三、结论与建议", true);
            sb.AppendLine();

            if (input.Hosts == null || input.Hosts.Count == 0)
            {
                sb.AppendLine("1. 本次扫描未发现任何存活设备。");
                sb.AppendLine("2. 建议：确认网线已连接、本机与目标在同一网段；");
                sb.AppendLine("   若目标设备启用了 ARP 防护，本工具可能无法发现。");
                sb.AppendLine();
                return;
            }

            // 1. 设备构成统计
            // 注意：C# 3.0 不支持 out 变量内联声明，必须先声明后传入。
            List<string> order;
            Dictionary<string, int> counts = CountByRole(input.Hosts, out order);

            StringBuilder composition = new StringBuilder();

            for (int i = 0; i < order.Count; i++)
            {
                if (i > 0) { composition.Append("、"); }
                composition.Append(order[i]).Append(' ')
                    .Append(counts[order[i]].ToString(CultureInfo.InvariantCulture)).Append(" 台");
            }

            sb.AppendLine("1. 本次共发现 " + input.Hosts.Count + " 台设备：" + composition + "。");

            // 2. 基于告警的针对性建议
            int lineNo = 2;

            bool hasConflict = false;
            bool hasGatewayDown = false;

            if (input.Alerts != null)
            {
                for (int i = 0; i < input.Alerts.Count; i++)
                {
                    if (input.Alerts[i].Kind == AlertKind.IpConflict) { hasConflict = true; }
                    if (input.Alerts[i].Kind == AlertKind.GatewayUnreachable) { hasGatewayDown = true; }
                }
            }

            if (hasGatewayDown)
            {
                sb.AppendLine(lineNo + ". 默认网关不可达：请优先检查交换机、路由器与上联线路，"
                    + "设备层面的排查可能无意义。");
                lineNo++;
            }

            if (hasConflict)
            {
                sb.AppendLine(lineNo + ". 检测到 IP 冲突：请为冲突设备改用其它 IP，"
                    + "否则会造成随机断网。");
                lineNo++;
            }

            // 3. 未知设备建议
            int unknown = CountUnknown(input.Hosts);

            if (unknown > 0)
            {
                sb.AppendLine(lineNo + ". 有 " + unknown + " 台设备角色未知："
                    + "可在「设置」页补充对应端口规则后重新扫描。");
                lineNo++;
            }

            // 4. 无开放端口的主机（可能被防火墙拦截）
            int noPorts = CountNoPorts(input.Hosts);

            if (noPorts > 0)
            {
                sb.AppendLine(lineNo + ". 有 " + noPorts + " 台设备未探测到开放端口："
                    + "可能启用了防火墙或 ARP 防护，建议用其它方式进一步确认。");
                lineNo++;
            }

            // 5. 终止扫描的说明
            if (!string.IsNullOrEmpty(input.FinalStateText)
                && input.FinalStateText.IndexOf("终止") >= 0)
            {
                sb.AppendLine(lineNo + ". 本次扫描被中途终止，结果可能不完整，"
                    + "建议重新完整扫描后再做判断。");
                lineNo++;
            }

            sb.AppendLine();
        }

        private static Dictionary<string, int> CountByRole(IList<HostRecord> hosts, out List<string> order)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            order = new List<string>();

            // 按固定优先级顺序输出，保证报告稳定可读
            string[] builtInOrder = {
                RolePriority.RoleLocalServer,
                RolePriority.RoleCashierPc,
                RolePriority.RolePrinter,
                RolePriority.RoleScale,
                RolePriority.RoleAndroidPos,
                RolePriority.RoleNetworkDevice
            };

            for (int i = 0; i < hosts.Count; i++)
            {
                string role = Safe(hosts[i].Role);

                if (role.Length == 0) { role = RolePriority.RoleUnknown; }

                int cur;
                if (counts.TryGetValue(role, out cur)) { counts[role] = cur + 1; }
                else { counts[role] = 1; }
            }

            // 先按内置优先级
            for (int i = 0; i < builtInOrder.Length; i++)
            {
                if (counts.ContainsKey(builtInOrder[i])) { order.Add(builtInOrder[i]); }
            }

            // 自定义角色（按名称排序）
            List<string> customs = new List<string>();

            foreach (string k in counts.Keys)
            {
                bool isBuiltIn = false;

                for (int i = 0; i < builtInOrder.Length; i++)
                {
                    if (builtInOrder[i] == k) { isBuiltIn = true; }
                }

                if (k == RolePriority.RoleUnknown) { continue; }
                if (!isBuiltIn) { customs.Add(k); }
            }

            customs.Sort(StringComparer.CurrentCulture);

            for (int i = 0; i < customs.Count; i++) { order.Add(customs[i]); }

            // 未知放最后
            if (counts.ContainsKey(RolePriority.RoleUnknown))
            {
                order.Add(RolePriority.RoleUnknown);
            }

            return counts;
        }

        private static int CountNoPorts(IList<HostRecord> hosts)
        {
            if (hosts == null) { return 0; }

            int n = 0;

            for (int i = 0; i < hosts.Count; i++)
            {
                if (hosts[i].OpenPorts == null || hosts[i].OpenPorts.Length == 0) { n++; }
            }

            return n;
        }

        // -------------------------------------------------------------------
        // 深度测速小节（仅在用户测速过时出现，PRD §6.7）
        // -------------------------------------------------------------------
        private static void AppendPing3Section(StringBuilder sb, ReportInput input)
        {
            if (input.Hosts == null || input.Hosts.Count == 0) { return; }

            List<HostRecord> tested = new List<HostRecord>();

            for (int i = 0; i < input.Hosts.Count; i++)
            {
                if (input.Hosts[i].HasPing3) { tested.Add(input.Hosts[i]); }
            }

            // 没测速过就整节省略，避免出现大量空行（PRD §6.7 归一化说明）
            if (tested.Count == 0) { return; }

            AppendLine(sb, '-', "四、深度测速记录（仅列出已测速主机）", true);
            sb.AppendLine();

            sb.AppendLine("IP 地址           端口     测速结果");
            sb.AppendLine("----------------  ------  ----------------------------------");

            for (int i = 0; i < tested.Count; i++)
            {
                HostRecord h = tested[i];

                sb.Append(PadRight(Safe(h.IpText), 17)).Append(" ")
                  .Append(PadRight(h.Ping3 == null ? "-" : h.Ping3.Port.ToString(CultureInfo.InvariantCulture), 7))
                  .Append(" ")
                  .AppendLine(Safe(h.Ping3Text));
            }

            sb.AppendLine();
        }

        private static void AppendFooter(StringBuilder sb)
        {
            AppendLine(sb, '=', "报告结束 - 由 MoeQuick NetScan 自动生成", true);
        }

        // -------------------------------------------------------------------
        // 工具
        // -------------------------------------------------------------------
        private static void AppendLine(StringBuilder sb, char ch, string title, bool withTitle)
        {
            if (withTitle)
            {
                sb.Append(ch, LineWidth).AppendLine();
                sb.Append(ch).Append(' ').Append(title).AppendLine();
                sb.Append(ch, LineWidth).AppendLine();
            }
            else
            {
                sb.Append(ch, LineWidth).AppendLine();
            }
        }

        private static void AppendField(StringBuilder sb, string name, string value)
        {
            // 中文标签按显示宽度对齐（中文占 2 列）
            sb.Append(PadRight(name, 10)).Append(": ").AppendLine(Safe(value));
        }

        /// <summary>
        /// 按显示宽度右填充（中文算 2 列）。
        /// </summary>
        private static string PadRight(string text, int displayWidth)
        {
            string s = Safe(text);
            int width = DisplayWidth(s);

            if (width >= displayWidth) { return s; }

            return s + new string(' ', displayWidth - width);
        }

        /// <summary>
        /// 按显示宽度截断（避免中文被截半）。
        /// </summary>
        private static string Truncate(string text, int maxDisplayWidth)
        {
            string s = Safe(text);

            if (DisplayWidth(s) <= maxDisplayWidth) { return s; }

            StringBuilder sb = new StringBuilder();
            int width = 0;

            for (int i = 0; i < s.Length; i++)
            {
                int w = IsWideChar(s[i]) ? 2 : 1;

                if (width + w > maxDisplayWidth - 1) { break; }

                sb.Append(s[i]);
                width += w;
            }

            return sb.ToString();
        }

        private static int DisplayWidth(string s)
        {
            if (string.IsNullOrEmpty(s)) { return 0; }

            int width = 0;

            for (int i = 0; i < s.Length; i++)
            {
                width += IsWideChar(s[i]) ? 2 : 1;
            }

            return width;
        }

        /// <summary>
        /// 粗略判断是否为全角字符（CJK 区段）。
        /// </summary>
        private static bool IsWideChar(char c)
        {
            return (c >= 0x1100 && c <= 0x115F)     // 韩文字母
                || (c >= 0x2E80 && c <= 0xA4CF)     // CJK 部首 + 日文 + 中文
                || (c >= 0xAC00 && c <= 0xD7A3)     // 韩文音节
                || (c >= 0xF900 && c <= 0xFAFF)     // CJK 兼容
                || (c >= 0xFE30 && c <= 0xFE6F)     // CJK 兼容形式
                || (c >= 0xFF00 && c <= 0xFF60)     // 全角形式
                || (c >= 0xFFE0 && c <= 0xFFE6);
        }

        private static string ConfidenceText(DeviceConfidence confidence)
        {
            if (confidence == DeviceConfidence.High) { return "高"; }
            if (confidence == DeviceConfidence.Medium) { return "中"; }
            if (confidence == DeviceConfidence.Low) { return "低"; }
            return "未知";
        }

        private static string Safe(string s)
        {
            return s == null ? "" : s;
        }

        /// <summary>
        /// 生成带时间戳的报告文件名。
        /// </summary>
        public static string BuildFileName(string timestampFormat, DateTime time)
        {
            string fmt = string.IsNullOrEmpty(timestampFormat)
                ? IniSchema.DefaultTimestampFormat
                : timestampFormat;

            string stamp;

            try
            {
                stamp = time.ToString(fmt, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                // 格式串非法时退回默认格式（不抛异常）
                stamp = time.ToString(IniSchema.DefaultTimestampFormat, CultureInfo.InvariantCulture);
            }

            return "MQNetScan_报告_" + stamp + ".txt";
        }
    }
}
