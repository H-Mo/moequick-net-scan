using System;
using System.Collections.Generic;
using System.Text;
using MQNetScan.Engine;
using MQNetScan.Infrastructure;

namespace MQNetScan.Application
{
    /// <summary>
    /// 系统操作执行结果。
    /// </summary>
    internal sealed class ToolResult
    {
        private readonly bool _success;
        private readonly string _message;
        private readonly string _commandToCopy;
        private readonly bool _needsElevation;

        public ToolResult(bool success, string message)
            : this(success, message, null, false)
        {
        }

        public ToolResult(bool success, string message, string commandToCopy, bool needsElevation)
        {
            _success = success;
            _message = message == null ? "" : message;
            _commandToCopy = commandToCopy;
            _needsElevation = needsElevation;
        }

        public bool Success { get { return _success; } }
        public string Message { get { return _message; } }

        /// <summary>非空时表示需要用户以管理员身份执行的命令（供"一键复制"）。</summary>
        public string CommandToCopy { get { return _commandToCopy; } }

        /// <summary>是否因权限不足而未能执行（需要引导用户提权）。</summary>
        public bool NeedsElevation { get { return _needsElevation; } }
    }

    /// <summary>
    /// Tab 2 便捷功能的业务逻辑。
    ///
    /// 设计约束（PRD FR-3.1.1）：
    ///   · **普通权限下清理 ARP 不自动弹 UAC**，只返回操作指引；
    ///   · 所有系统命令的失败都折算为中文 ToolResult，不抛异常；
    ///   · 命令输出使用 OEM 代码页解码，避免中文乱码（见 ProcessLauncher）。
    /// </summary>
    internal static class ToolsService
    {
        /// <summary>清理 ARP 缓存时可复制给用户的命令。</summary>
        public const string ArpClearCommand = "netsh interface ip delete arpcache";

        /// <summary>清理 ARP 的备用命令（旧系统或 netsh 缺失时）。</summary>
        public const string ArpClearFallbackCommand = "arp -d *";

        /// <summary>刷新 DNS 命令。</summary>
        public const string FlushDnsCommand = "ipconfig /flushdns";

        /// <summary>
        /// 清理 ARP 缓存。
        ///
        /// 权限分支（PRD FR-3.1.1）：
        ///   · 管理员 → 直接执行 netsh，失败回退 arp -d *；
        ///   · 普通权限 → **不执行、不弹 UAC**，只返回指引（含可复制命令）。
        /// </summary>
        public static ToolResult ClearArpCache()
        {
            bool isAdmin = ProcessLauncher.IsAdministrator();

            if (!isAdmin)
            {
                // 关键：这里绝不能触发 UAC 弹窗
                return new ToolResult(
                    false,
                    "清理 ARP 缓存需要管理员权限。\r\n\r\n"
                    + "当前以普通权限运行，未执行任何操作（也不会弹出授权窗口）。\r\n\r\n"
                    + "请任选一种方式：\r\n"
                    + "  1. 右键本程序 → 「以管理员身份运行」，再点此按钮；\r\n"
                    + "  2. 以管理员身份打开命令提示符，执行下方命令。",
                    ArpClearCommand,
                    true);
            }

            return ExecuteArpClear();
        }

        /// <summary>
        /// 实际执行清 ARP（仅管理员路径调用）。
        /// </summary>
        internal static ToolResult ExecuteArpClear()
        {
            string output;

            // 首选 netsh
            ProcessLauncher.LaunchResult r1 = ProcessLauncher.RunAndWait(
                "netsh.exe", "interface ip delete arpcache", 8000, out output);

            if (r1.Success)
            {
                string text = string.IsNullOrEmpty(output) ? "ARP 缓存已清理。" : output.Trim();

                // netsh 成功时通常输出 "Ok."，转成中文更友好
                if (text == "Ok." || text == "Ok") { text = "ARP 缓存已清理。"; }

                return new ToolResult(true, text);
            }

            // 回退 arp -d *
            ProcessLauncher.LaunchResult r2 = ProcessLauncher.RunAndWait(
                "arp.exe", "-d *", 8000, out output);

            if (r2.Success)
            {
                return new ToolResult(true, "ARP 缓存已清理（通过 arp -d）。");
            }

            // 两条命令都失败
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("清理 ARP 缓存失败。");
            sb.AppendLine();
            sb.AppendLine("netsh 结果：" + r1.Message);

            if (!string.IsNullOrEmpty(r2.Message))
            {
                sb.AppendLine("arp 结果：" + r2.Message);
            }

            sb.AppendLine();
            sb.Append("可尝试以管理员身份手动执行：").Append(ArpClearCommand);

            return new ToolResult(false, sb.ToString(), ArpClearCommand, true);
        }

        /// <summary>
        /// 刷新 DNS 缓存。
        /// 注意：非管理员执行会返回"请求的操作需要提升"，需如实转达。
        /// </summary>
        public static ToolResult FlushDns()
        {
            string output;

            ProcessLauncher.LaunchResult r = ProcessLauncher.RunAndWait(
                "ipconfig.exe", "/flushdns", 8000, out output);

            if (r.Success)
            {
                return new ToolResult(true, "DNS 缓存已刷新。");
            }

            // 非管理员常见：请求的操作需要提升
            bool needsElevation = r.Message.IndexOf("提升") >= 0
                || r.Message.IndexOf("管理员") >= 0
                || r.Message.IndexOf("elevation") >= 0
                || r.Message.IndexOf("Access is denied") >= 0;

            if (needsElevation)
            {
                return new ToolResult(
                    false,
                    "刷新 DNS 缓存需要管理员权限。\r\n\r\n"
                    + "请以管理员身份重新运行本程序，或在管理员命令提示符中执行下方命令。",
                    FlushDnsCommand,
                    true);
            }

            return new ToolResult(false, "刷新 DNS 缓存失败：" + r.Message, FlushDnsCommand, false);
        }

        /// <summary>
        /// 打开「网络连接」（ncpa.cpl）。
        /// </summary>
        public static ToolResult OpenNetworkConnections()
        {
            ProcessLauncher.LaunchResult r = ProcessLauncher.OpenSystemPanel("ncpa.cpl");

            return new ToolResult(r.Success, r.Message);
        }

        /// <summary>
        /// 打开「服务」（services.msc）。
        /// </summary>
        public static ToolResult OpenServices()
        {
            ProcessLauncher.LaunchResult r = ProcessLauncher.OpenSystemPanel("services.msc");

            if (!r.Success)
            {
                return new ToolResult(false,
                    "打开服务管理器失败，可能需要管理员权限。\r\n\r\n" + r.Message);
            }

            return new ToolResult(true, "已打开服务管理器。");
        }

        /// <summary>
        /// 单点握手测通（同步阻塞，调用方须在后台线程执行）。
        /// </summary>
        public static SinglePortTester.Result TestPort(string hostOrIp, int port, int timeoutMs)
        {
            return SinglePortTester.Test(hostOrIp, port, timeoutMs);
        }
    }
}
