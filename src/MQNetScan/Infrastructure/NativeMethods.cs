using System;
using System.Runtime.InteropServices;

namespace MQNetScan.Infrastructure
{
    /// <summary>
    /// 全部 P/Invoke 声明的集中处。
    ///
    /// 边界规则（project-structure.md §10 B-1）：
    /// **全项目只有本文件允许出现 [DllImport]**。
    /// 其它任何文件出现 DllImport 即为违规，build.ps1 会检查。
    /// </summary>
    internal static class NativeMethods
    {
        // -------------------------------------------------------------------
        // SendARP
        //
        // 【字节序警告 —— 必须用小端】
        // destIp 必须按**主机字节序（小端）**构造，即：
        //     uint destIp = BitConverter.ToUInt32(IPAddress.Parse(ip).GetAddressBytes(), 0);
        // 若误用大端（手工移位拼装），SendARP 对**所有**地址（含网关）均返回 67，
        // 表现为"扫不到任何设备但 arp -a 明明有条目"。
        // 已实测验证，详见 architecture.md §4.1.1。
        // -------------------------------------------------------------------
        /// <summary>
        /// 发送 ARP 请求，获取目标 IPv4 对应的 MAC 地址。
        /// </summary>
        /// <param name="destIp">目标 IPv4（**小端** uint，见上方警告）。</param>
        /// <param name="srcIp">源 IPv4；传 0 表示由系统选择。</param>
        /// <param name="macAddr">6 字节缓冲，接收 MAC。</param>
        /// <param name="phyAddrLen">传入时必须为 6；返回时为实际长度。</param>
        /// <returns>0 表示成功；其它为 Win32 错误码（67 = ERROR_BAD_NET_NAME）。</returns>
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern int SendARP(
            uint destIp,
            uint srcIp,
            byte[] macAddr,
            ref uint phyAddrLen);

        // -------------------------------------------------------------------
        // GetIpNetTable
        //
        // 用于枚举本机 ARP 表，为 IP 冲突判定提供交叉比对依据（architecture §4.1）。
        // 该 API 需要两段式调用：先传 null 获取所需长度，再分配缓冲读取。
        // -------------------------------------------------------------------
        /// <summary>
        /// 获取本机 ARP 表。
        /// </summary>
        /// <param name="ipNetTable">接收缓冲；传 null 时返回所需大小。</param>
        /// <param name="size">缓冲字节数。</param>
        /// <param name="order">是否排序（TRUE / FALSE）。</param>
        /// <returns>0 成功；122（ERROR_INSUFFICIENT_BUFFER）表示缓冲不足，需按 size 重试。</returns>
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern int GetIpNetTable(
            IntPtr ipNetTable,
            ref int size,
            bool order);

        // -------------------------------------------------------------------
        /// <summary>系统启动后经过的毫秒数，64 位计数避免长时间运行时溢出。</summary>
        [DllImport("kernel32.dll", ExactSpelling = true)]
        public static extern ulong GetTickCount64();

        // -------------------------------------------------------------------
        // 常用错误码
        // -------------------------------------------------------------------
        /// <summary>成功。</summary>
        public const int NO_ERROR = 0;

        /// <summary>系统找不到指定的网络名（SendARP 最常见的"无响应"返回）。</summary>
        public const int ERROR_BAD_NET_NAME = 67;

        /// <summary>一般性失败。</summary>
        public const int ERROR_GEN_FAILURE = 31;

        /// <summary>不支持该请求（跨网段时常见）。</summary>
        public const int ERROR_NOT_SUPPORTED = 50;

        /// <summary>缓冲不足（GetIpNetTable 两段式调用时的正常返回）。</summary>
        public const int ERROR_INSUFFICIENT_BUFFER = 122;

        /// <summary>网络不可达。</summary>
        public const int ERROR_NETWORK_UNREACHABLE = 1231;

        /// <summary>主机不可达。</summary>
        public const int ERROR_HOST_UNREACHABLE = 1232;

        // -------------------------------------------------------------------
        // 返回码分类
        // -------------------------------------------------------------------
        /// <summary>
        /// SendARP 返回码是否表示"未获取到 MAC"（而非真正的异常）。
        /// 这类情况应静默计入"无响应"，不进入结果列表（PRD FR-1.2.2）。
        /// </summary>
        public static bool IsArpNoResponse(int errorCode)
        {
            return errorCode == ERROR_BAD_NET_NAME
                || errorCode == ERROR_GEN_FAILURE
                || errorCode == ERROR_NOT_SUPPORTED
                || errorCode == ERROR_NETWORK_UNREACHABLE
                || errorCode == ERROR_HOST_UNREACHABLE;
        }

        /// <summary>
        /// 是否为"跨网段/不支持"类错误（用于给出"ARP 可能无效"的提示）。
        /// </summary>
        public static bool IsArpUnsupported(int errorCode)
        {
            return errorCode == ERROR_NOT_SUPPORTED
                || errorCode == ERROR_NETWORK_UNREACHABLE
                || errorCode == ERROR_HOST_UNREACHABLE;
        }
    }
}
