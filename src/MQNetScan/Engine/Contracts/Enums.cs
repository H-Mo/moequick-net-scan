namespace MQNetScan.Engine.Contracts
{
    /// <summary>
    /// 扫描状态。
    /// 状态迁移唯一入口为 ScanStateMachine（architecture §3.5）。
    /// </summary>
    internal enum ScanState
    {
        /// <summary>空闲：未开始扫描。</summary>
        Idle = 0,

        /// <summary>运行中。</summary>
        Running = 1,

        /// <summary>已暂停：不再取新任务，在途探测自然结束。</summary>
        Paused = 2,

        /// <summary>已终止：线程退出，结果保留。</summary>
        Stopped = 3,

        /// <summary>已完成：全部地址处理完毕。</summary>
        Completed = 4
    }

    /// <summary>设备识别可信度。</summary>
    internal enum DeviceConfidence
    {
        Unknown = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    /// <summary>设备存活探测方式。</summary>
    internal enum ProbeMode
    {
        Arp = 0,
        Ping = 1
    }

    /// <summary>
    /// 告警种类。
    /// </summary>
    internal enum AlertKind
    {
        /// <summary>默认网关不可达（红色横幅）。</summary>
        GatewayUnreachable = 0,

        /// <summary>检测到 IP 冲突（红色横幅）。</summary>
        IpConflict = 1,

        /// <summary>一般提示。</summary>
        Info = 2,

        /// <summary>配置问题。</summary>
        Config = 3
    }

    /// <summary>
    /// 角色判定的固定优先级。
    /// 数值越小优先级越高（PRD §7.2）。
    /// 注意：此顺序为内置固定，不可由用户修改（PRD FR-2.3）。
    ///
    /// 分类学原则（S13 重构，重要）：
    ///   **只按"能被端口客观证实"的类别归类，不做业务猜测。**
    ///
    ///   例如一台开了 445/3389 的机器，无法从网络侧区分它是收银机还是办公电脑，
    ///   因此归类为「Windows 电脑」而非「收银电脑」；同理 5555 只能证明是
    ///   安卓设备（ADB），不能证明是 POS 机。这样的归类才是严谨、可复现的。
    /// </summary>
    internal static class RolePriority
    {
        public const int LocalServer = 10;      // 本地服务器（1433）
        public const int WindowsPc = 20;        // Windows 电脑（445/3389/135）
        public const int Printer = 30;          // 打印机（9100/515/631）
        public const int Scale = 40;            // 电子秤（3001/4001）
        public const int AndroidDevice = 50;    // 安卓设备（5555）
        public const int NetworkDevice = 80;    // 网络设备（仅 80/443）
        public const int Custom = 90;           // 用户自定义角色（排在内置之后）
        public const int Unknown = 100;         // 未知

        /// <summary>角色显示名。</summary>
        public const string RoleLocalServer = "本地服务器";
        public const string RoleWindowsPc = "Windows 电脑";
        public const string RoleCashierPc = RoleWindowsPc; // 兼容旧代码
        public const string RolePrinter = "打印机";
        public const string RoleScale = "电子秤";
        public const string RoleAndroidDevice = "安卓设备";
        public const string RoleAppleDevice = "iPhone / iPad";
        public const string RoleDisplayDevice = "电视 / 显示设备";
        public const string RoleRouter = "路由器 / 网络设备";
        public const string RoleIotDevice = "其它 IoT 设备";
        public const string RoleAndroidPos = RoleAndroidDevice; // 兼容旧代码
        public const string RoleNetworkDevice = "网络设备";
        public const string RoleUnknown = "未知";

        /// <summary>
        /// 为内置角色名取优先级；自定义角色返回 Custom。
        /// </summary>
        public static int GetPriority(string roleName)
        {
            if (roleName == null) { return Unknown; }

            string r = roleName.Trim();

            if (r == RoleLocalServer) { return LocalServer; }
            if (r == RoleWindowsPc) { return WindowsPc; }
            if (r == RolePrinter) { return Printer; }
            if (r == RoleScale) { return Scale; }
            if (r == RoleAndroidDevice) { return AndroidDevice; }
            if (r == RoleNetworkDevice) { return NetworkDevice; }
            if (r == RoleUnknown) { return Unknown; }

            return Custom;
        }

        /// <summary>
        /// 把旧版角色名迁移为新的严谨分类（S13 兼容）。
        /// 用于读取旧 ini 时自动升级，避免用户配置失效。
        /// </summary>
        public static string MigrateLegacyName(string roleName)
        {
            if (string.IsNullOrEmpty(roleName)) { return ""; }

            string r = roleName.Trim();

            // 旧名 → 新名
            if (r == "收银电脑") { return RoleWindowsPc; }
            if (r == "安卓 POS" || r == "安卓POS") { return RoleAndroidDevice; }

            return r;
        }

        /// <summary>全部内置角色名（按优先级顺序）。</summary>
        public static string[] GetBuiltInRoles()
        {
            return new string[] {
                RoleLocalServer,
                RoleWindowsPc,
                RolePrinter,
                RoleScale,
                RoleAndroidDevice,
                RoleNetworkDevice
            };
        }
    }
}
