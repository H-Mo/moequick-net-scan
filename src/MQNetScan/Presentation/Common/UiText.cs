namespace MQNetScan.Presentation.Common
{
    /// <summary>
    /// 界面文案单一来源。
    /// 所有用户可见字符串集中于此，便于统一修订与检查（project-structure.md §9）。
    /// </summary>
    internal static class UiText
    {
        /// <summary>
        /// 主窗体标题。必须与 AssemblyInfo 的 AssemblyTitle 保持一致（边界检查 B-9）。
        /// </summary>
        public const string AppTitle = "MoeQuick NetScan v1.4.0 - 萌快网维";

        public const string TabScan = "局域网探测";
        public const string TabLocalInfo = "本机信息";
        public const string TabTools = "便捷功能";
        public const string TabSettings = "设置";

        public const string StatusReady = "就绪";

        // S0 阶段各 Tab 的占位说明，后续阶段逐个替换为真实功能
        public const string PlaceholderScan =
            "局域网探测（S0 骨架占位）\r\n\r\n" +
            "后续阶段将在此实现：\r\n" +
            "  · 网卡枚举与网段选择（S2 / S3）\r\n" +
            "  · ARP 存活探测（S5）\r\n" +
            "  · 端口扫描与角色判定（S7）\r\n" +
            "  · 结果列表与右键菜单（S6 / S8）\r\n" +
            "  · 诊断报告导出（S9）";

        public const string PlaceholderTools =
            "便捷功能（S0 骨架占位）\r\n\r\n" +
            "后续阶段将在此实现（S11）：\r\n" +
            "  · 一键清理 ARP 缓存（按需提权）\r\n" +
            "  · 刷新 DNS 缓存\r\n" +
            "  · 打开网络连接 / 服务管理器\r\n" +
            "  · 单点握手测通器\r\n" +
            "  · 网关 / 外网 / SaaS 域名连通性自检";

        public const string PlaceholderSettings =
            "设置（S0 骨架占位）\r\n\r\n" +
            "后续阶段将在此实现（S1 / S12）：\r\n" +
            "  · 最大并发线程数与单端口超时（S1）\r\n" +
            "  · 端口特征规则表增删改（S12）\r\n" +
            "  · 配置持久化到 MQNetScan.ini（S1）";
    }
}
