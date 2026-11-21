using System;
using System.Globalization;

namespace MQNetScan.Engine
{
    /// <summary>
    /// IPv4 地址与整数之间的转换、掩码运算工具。
    /// 纯静态逻辑，无网络依赖，可完全单元测试。
    /// 内部统一用 uint 表示地址（主机字节序），避免 int 的符号问题。
    /// </summary>
    internal static class Ipv4Util
    {
        /// <summary>
        /// 解析点分十进制 IPv4。
        /// 严格模式：必须是 4 段、每段 0~255、不允许前导零以外的异常输入。
        /// </summary>
        public static bool TryParse(string text, out uint address)
        {
            address = 0;

            if (text == null) { return false; }

            string s = text.Trim();
            if (s.Length == 0) { return false; }

            string[] parts = s.Split('.');
            if (parts.Length != 4) { return false; }

            uint result = 0;

            for (int i = 0; i < 4; i++)
            {
                string p = parts[i];

                if (p.Length == 0 || p.Length > 3) { return false; }

                // 只允许数字字符
                for (int k = 0; k < p.Length; k++)
                {
                    if (p[k] < '0' || p[k] > '9') { return false; }
                }

                int octet;
                if (!int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out octet))
                {
                    return false;
                }

                if (octet < 0 || octet > 255) { return false; }

                result = (result << 8) | (uint)(octet & 0xFF);
            }

            address = result;
            return true;
        }

        /// <summary>
        /// 将 uint 格式化为点分十进制。
        /// </summary>
        public static string Format(uint address)
        {
            return ((address >> 24) & 0xFF).ToString(CultureInfo.InvariantCulture)
                + "." + ((address >> 16) & 0xFF).ToString(CultureInfo.InvariantCulture)
                + "." + ((address >> 8) & 0xFF).ToString(CultureInfo.InvariantCulture)
                + "." + (address & 0xFF).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 由前缀长度生成掩码（例如 24 → 255.255.255.0 对应的 uint）。
        /// 前缀 0 返回 0；前缀 32 返回 0xFFFFFFFF。
        /// </summary>
        public static uint PrefixToMask(int prefixLength)
        {
            if (prefixLength <= 0) { return 0; }
            if (prefixLength >= 32) { return 0xFFFFFFFF; }

            // 左移时用 uint 运算，避免 int 溢出的符号问题
            return 0xFFFFFFFFu << (32 - prefixLength);
        }

        /// <summary>
        /// 由掩码反推前缀长度（要求掩码是连续的 1）。
        /// 返回 -1 表示掩码不连续（非法掩码）。
        /// </summary>
        public static int MaskToPrefix(uint mask)
        {
            int prefix = 0;
            bool seenZero = false;

            for (int bit = 31; bit >= 0; bit--)
            {
                bool isOne = ((mask >> bit) & 1) == 1;

                if (isOne)
                {
                    if (seenZero) { return -1; }   // 1 出现在 0 之后 → 不连续
                    prefix++;
                }
                else
                {
                    seenZero = true;
                }
            }

            return prefix;
        }

        /// <summary>
        /// 比较两个地址是否在同一子网。
        /// </summary>
        public static bool InSameSubnet(uint a, uint b, uint mask)
        {
            return (a & mask) == (b & mask);
        }

        /// <summary>
        /// 是否为 APIPA 地址（169.254.0.0/16）——这类地址表示 DHCP 失败，不应作为扫描源。
        /// </summary>
        public static bool IsApipa(uint address)
        {
            return (address & 0xFFFF0000u) == 0xA9FE0000u;
        }

        /// <summary>
        /// 是否为环回地址（127.0.0.0/8）。
        /// </summary>
        public static bool IsLoopback(uint address)
        {
            return (address & 0xFF000000u) == 0x7F000000u;
        }

        /// <summary>
        /// 是否为零地址（0.0.0.0）。
        /// </summary>
        public static bool IsAny(uint address)
        {
            return address == 0;
        }
    }
}
