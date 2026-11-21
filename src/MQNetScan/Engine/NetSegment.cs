using System;
using System.Globalization;

namespace MQNetScan.Engine
{
    /// <summary>
    /// 目标范围的来源模式。
    /// </summary>
    internal enum SegmentMode
    {
        /// <summary>CIDR 网段，如 192.168.1.0/24，扫描除网络号与广播外的全部地址。</summary>
        Cidr = 0,

        /// <summary>用户指定的精确起止区间，只扫描该区间内的地址，不做对齐扩大。</summary>
        ExplicitRange = 1
    }

    /// <summary>
    /// 网段解析结果。
    /// </summary>
    internal enum SegmentParseError
    {
        None = 0,

        /// <summary>输入为空。</summary>
        Empty,

        /// <summary>格式无法识别（既不是 CIDR，也不是起止 IP 区间）。</summary>
        BadFormat,

        /// <summary>IP 地址本身非法。</summary>
        BadIpAddress,

        /// <summary>掩码长度不在允许范围（8~30）。</summary>
        BadPrefixLength,

        /// <summary>起止 IP 顺序颠倒。</summary>
        RangeReversed,

        /// <summary>起止 IP 不在同一网段。</summary>
        RangeCrossSubnet,

        /// <summary>目标地址数超出可处理上限。</summary>
        TooManyHosts
    }

    /// <summary>
    /// 一个可扫描的目标范围。
    /// 两种模式：
    ///   · CIDR：由 地址/前缀 得出，扫描 firstHost~lastHost（排除网络号与广播）；
    ///   · 精确区间：由用户给定的起止 IP 得出，**严格只扫该区间**，不做对齐扩大。
    /// 纯数据结构，不持有任何网络句柄。
    /// </summary>
    internal sealed class NetSegment
    {
        private readonly uint _firstHost;
        private readonly uint _lastHost;
        private readonly SegmentMode _mode;

        // CIDR 模式下的网络号/广播地址；精确区间模式下这两者无意义，
        // 但为保持属性可用，统一用 firstHost/lastHost 兜底。
        private readonly uint _networkAddress;
        private readonly uint _broadcastAddress;
        private readonly int _prefixLength;

        private NetSegment(uint firstHost, uint lastHost, SegmentMode mode,
            uint networkAddress, uint broadcastAddress, int prefixLength)
        {
            _firstHost = firstHost;
            _lastHost = lastHost;
            _mode = mode;
            _networkAddress = networkAddress;
            _broadcastAddress = broadcastAddress;
            _prefixLength = prefixLength;
        }

        /// <summary>范围来源模式。</summary>
        public SegmentMode Mode { get { return _mode; } }

        /// <summary>网络号（仅 CIDR 模式有意义）。</summary>
        public uint NetworkAddress { get { return _networkAddress; } }

        /// <summary>广播地址（仅 CIDR 模式有意义）。</summary>
        public uint BroadcastAddress { get { return _broadcastAddress; } }

        /// <summary>前缀长度（仅 CIDR 模式有意义；精确区间模式下为推断值）。</summary>
        public int PrefixLength { get { return _prefixLength; } }

        /// <summary>掩码。</summary>
        public uint Mask { get { return Ipv4Util.PrefixToMask(_prefixLength); } }

        /// <summary>第一个目标地址。</summary>
        public uint FirstHost { get { return _firstHost; } }

        /// <summary>最后一个目标地址。</summary>
        public uint LastHost { get { return _lastHost; } }

        /// <summary>目标地址数量。</summary>
        public int HostCount
        {
            get { return (int)(_lastHost - _firstHost) + 1; }
        }

        /// <summary>
        /// 展示用文本。
        /// CIDR 模式为 192.168.1.0/24；精确区间为 192.168.1.10 ~ 192.168.1.50。
        /// </summary>
        public string DisplayText
        {
            get
            {
                if (_mode == SegmentMode.Cidr)
                {
                    return Cidr;
                }

                return Ipv4Util.Format(_firstHost) + " ~ " + Ipv4Util.Format(_lastHost);
            }
        }

        /// <summary>
        /// CIDR 文本形式（如 192.168.1.0/24）。
        /// 精确区间模式下返回覆盖区间的最小 CIDR，仅用于展示参考。
        /// </summary>
        public string Cidr
        {
            get
            {
                return Ipv4Util.Format(_networkAddress) + "/"
                    + _prefixLength.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// 枚举全部目标地址。调用方须自行保证 HostCount 在可接受规模内。
        /// </summary>
        public uint[] EnumerateHosts()
        {
            int count = HostCount;
            uint[] result = new uint[count];

            uint start = _firstHost;
            for (int i = 0; i < count; i++)
            {
                result[i] = start + (uint)i;
            }

            return result;
        }

        /// <summary>
        /// 判断地址是否落在目标范围内。
        /// </summary>
        public bool Contains(uint address)
        {
            return address >= _firstHost && address <= _lastHost;
        }

        public override string ToString()
        {
            return DisplayText;
        }

        // -------------------------------------------------------------------
        // 解析
        // -------------------------------------------------------------------

        /// <summary>允许的最小前缀长度（PRD FR-1.1.4：掩码长度需在 8~30 之间）。</summary>
        public const int MinPrefixLength = 8;

        /// <summary>允许的最大前缀长度。</summary>
        public const int MaxPrefixLength = 30;

        /// <summary>
        /// 超过 254 个地址需要用户确认（PRD FR-1.1.5）。
        /// </summary>
        public const int ConfirmHostThreshold = 254;

        /// <summary>
        /// 一次扫描能处理的地址上限。
        /// 超过后仍统计但不再追加显示（PRD NFR-S2）。
        /// </summary>
        public const int MaxHostsSoftLimit = 5000;

        /// <summary>
        /// 硬性拒绝阈值：/16 有 65534 个地址，是本工具支持的最大规模。
        /// 超过则拒绝（/8 的 16777214 个地址显然不可处理）。
        /// </summary>
        public const int MaxHostsHardLimit = 65534;

        /// <summary>
        /// 从 CIDR 文本解析网段（如 "192.168.1.0/24"）。
        /// </summary>
        public static bool TryParseCidr(string text, out NetSegment segment, out SegmentParseError error, out string message)
        {
            segment = null;
            error = SegmentParseError.None;
            message = null;

            if (text == null || text.Trim().Length == 0)
            {
                error = SegmentParseError.Empty;
                message = "请输入网段，例如 192.168.1.0/24。";
                return false;
            }

            string s = text.Trim();
            int slash = s.IndexOf('/');

            if (slash <= 0 || slash == s.Length - 1)
            {
                error = SegmentParseError.BadFormat;
                message = "网段格式应为 地址/掩码长度，例如 192.168.1.0/24。";
                return false;
            }

            string ipPart = s.Substring(0, slash).Trim();
            string prefixPart = s.Substring(slash + 1).Trim();

            uint address;
            if (!Ipv4Util.TryParse(ipPart, out address))
            {
                error = SegmentParseError.BadIpAddress;
                message = "IP 地址“" + ipPart + "”不合法，每段应为 0~255。";
                return false;
            }

            int prefix;
            if (!int.TryParse(prefixPart, NumberStyles.None, CultureInfo.InvariantCulture, out prefix))
            {
                error = SegmentParseError.BadFormat;
                message = "掩码长度“" + prefixPart + "”不是数字。";
                return false;
            }

            if (prefix < MinPrefixLength || prefix > MaxPrefixLength)
            {
                error = SegmentParseError.BadPrefixLength;
                message = "掩码长度需在 " + MinPrefixLength + "~" + MaxPrefixLength
                    + " 之间（当前 " + prefix + "）。";
                return false;
            }

            segment = FromAddressAndPrefix(address, prefix);

            if (segment.HostCount > MaxHostsHardLimit)
            {
                error = SegmentParseError.TooManyHosts;
                message = "目标地址数 " + segment.HostCount + " 过多，单次扫描上限为 "
                    + MaxHostsHardLimit + " 个，请缩小范围。";
                segment = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 由"地址 + 前缀长度"构造 CIDR 网段。
        /// 传入的地址可以是网段内任意地址（如本机 IP），会自动归到网络号。
        /// </summary>
        public static NetSegment FromAddressAndPrefix(uint address, int prefixLength)
        {
            uint mask = Ipv4Util.PrefixToMask(prefixLength);
            uint network = address & mask;
            uint broadcast = network | (~mask);

            // 排除网络号与广播地址
            return new NetSegment(network + 1u, broadcast - 1u, SegmentMode.Cidr,
                network, broadcast, prefixLength);
        }

        /// <summary>
        /// 由"精确起止地址"构造目标范围。
        /// **不做对齐扩大**：用户给 10~50 就只扫这 41 个地址。
        /// 这是与 CIDR 模式的关键区别，避免工具替用户擅自扩大扫描范围。
        /// </summary>
        public static NetSegment FromExplicitRange(uint start, uint end)
        {
            // 覆盖区间的最小 CIDR，仅用于展示与掩码相关属性
            int prefix = 32;
            while (prefix > 0)
            {
                uint m = Ipv4Util.PrefixToMask(prefix);
                if ((start & m) == (end & m)) { break; }
                prefix--;
            }

            uint mask = Ipv4Util.PrefixToMask(prefix);
            uint network = start & mask;
            uint broadcast = network | (~mask);

            return new NetSegment(start, end, SegmentMode.ExplicitRange,
                network, broadcast, prefix);
        }

        /// <summary>
        /// 从"起止 IP"构造精确区间。
        /// </summary>
        public static bool TryParseRange(string startText, string endText,
            out NetSegment segment, out SegmentParseError error, out string message)
        {
            segment = null;
            error = SegmentParseError.None;
            message = null;

            uint start;
            if (!Ipv4Util.TryParse(startText, out start))
            {
                error = SegmentParseError.BadIpAddress;
                message = "起始 IP“" + (startText == null ? "" : startText) + "”不合法。";
                return false;
            }

            uint end;
            if (!Ipv4Util.TryParse(endText, out end))
            {
                error = SegmentParseError.BadIpAddress;
                message = "结束 IP“" + (endText == null ? "" : endText) + "”不合法。";
                return false;
            }

            if (end < start)
            {
                error = SegmentParseError.RangeReversed;
                message = "结束 IP 不能小于起始 IP。";
                return false;
            }

            segment = FromExplicitRange(start, end);

            if (segment.HostCount > MaxHostsHardLimit)
            {
                error = SegmentParseError.TooManyHosts;
                message = "该区间包含 " + segment.HostCount + " 个地址，超过单次扫描上限 "
                    + MaxHostsHardLimit + " 个。";
                segment = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 估算扫描耗时提示。
        ///
        /// 【已弃用，保留仅为兼容旧调用点】
        /// 用户已决定：**不做耗时预估，只显示进度条**。
        /// 原因：SendARP 对无响应地址需等 3~4 秒（ARP 重传超时），
        /// 实际耗时强依赖于现场有多少设备在线，无法可靠预估，
        /// 给出错误预估值反而会误导运维判断。
        /// 新代码请勿再调用此方法。
        /// </summary>
        [Obsolete("用户已决定不做耗时预估，改为只显示进度条。新代码请勿调用。")]
        public string EstimateDurationText()
        {
            return "";
        }
    }
}
