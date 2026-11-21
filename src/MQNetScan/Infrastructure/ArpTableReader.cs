using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MQNetScan.Engine;

namespace MQNetScan.Infrastructure
{
    /// <summary>
    /// 本机 ARP 表读取器（GetIpNetTable 的封装）。
    ///
    /// 从 ConflictDetector 中抽出的公共部件（S13），因为现在有两处需要：
    ///   1. ConflictDetector：扫描前快照基线，用于检测 IP 冲突；
    ///   2. NetworkAdapterService：查默认网关的 MAC，用于识别
    ///      "网关代理回应"造成的跨网段假阳性。
    ///
    /// 边界规则：**GetIpNetTable 的 P/Invoke 声明只在 NativeMethods**，
    /// 本文件只做结果解析，不再声明 DllImport。
    /// </summary>
    internal static class ArpTableReader
    {
        /// <summary>MIB_IPNETROW 大小：dwIndex,dwPhysAddrLen,dwPhysAddr[8],dwAddr,dwType。</summary>
        private const int MibIpNetRowSize = 24;

        /// <summary>缓冲上限，防止异常大的表导致内存问题。</summary>
        private const int MaxTableBytes = 4 * 1024 * 1024;

        /// <summary>
        /// 读取整张 ARP 表，返回 IP（网络序）→ MAC 文本。
        /// 失败返回空字典（调用方应降级处理，不抛异常）。
        /// </summary>
        public static Dictionary<uint, string> ReadAll()
        {
            Dictionary<uint, string> result = new Dictionary<uint, string>();

            int size = 0;

            // 两段式调用：先取所需大小
            int rc = NativeMethods.GetIpNetTable(IntPtr.Zero, ref size, false);

            if (size <= 0 || size > MaxTableBytes) { return result; }

            IntPtr buffer = IntPtr.Zero;

            try
            {
                buffer = Marshal.AllocHGlobal(size);

                rc = NativeMethods.GetIpNetTable(buffer, ref size, false);

                if (rc != NativeMethods.NO_ERROR) { return result; }

                int numEntries = Marshal.ReadInt32(buffer, 0);

                if (numEntries <= 0) { return result; }

                // 表头 4 字节，之后每行 24 字节。
                // 注意：MIB_IPNETROW 的字段全为 DWORD，32/64 位下布局一致。
                const int headerSize = 4;
                int maxRows = (size - headerSize) / MibIpNetRowSize;

                if (numEntries > maxRows) { numEntries = maxRows; }

                for (int i = 0; i < numEntries; i++)
                {
                    int rowBase = headerSize + i * MibIpNetRowSize;

                    try
                    {
                        int physAddrLen = Marshal.ReadInt32(rowBase, 4);

                        if (physAddrLen != 6) { continue; }

                        byte[] mac = new byte[6];

                        for (int k = 0; k < 6; k++)
                        {
                            mac[k] = Marshal.ReadByte(rowBase, 8 + k);
                        }

                        // dwAddr 在偏移 16
                        uint addr = (uint)Marshal.ReadInt32(rowBase, 16);

                        if (!ArpProber.IsValidMac(mac)) { continue; }

                        result[addr] = ArpProber.FormatMac(mac);
                    }
                    catch (Exception)
                    {
                        // 单行解析失败不影响其它行
                    }
                }
            }
            catch (Exception)
            {
                return new Dictionary<uint, string>();
            }
            finally
            {
                if (buffer != IntPtr.Zero) { Marshal.FreeHGlobal(buffer); }
            }

            return result;
        }

        /// <summary>
        /// 查找指定 IP 的 MAC 文本；找不到返回空串。
        /// </summary>
        public static string FindMac(uint address)
        {
            Dictionary<uint, string> table = ReadAll();

            string mac;

            if (table.TryGetValue(address, out mac))
            {
                return mac == null ? "" : mac;
            }

            return "";
        }
    }
}
