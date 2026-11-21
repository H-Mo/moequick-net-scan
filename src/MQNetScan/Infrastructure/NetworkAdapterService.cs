using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MQNetScan.Engine;

namespace MQNetScan.Infrastructure
{
    /// <summary>
    /// 一块可用于扫描的网卡信息。
    /// </summary>
    internal sealed class AdapterInfo
    {
        private readonly string _id;
        private readonly string _name;
        private readonly string _description;
        private readonly uint _ipAddress;
        private readonly uint _subnetMask;
        private readonly int _prefixLength;
        private readonly bool _hasGateway;
        private readonly string _gatewayText;
        private readonly bool _isVirtual;
        private readonly string _gatewayMac;

        public AdapterInfo(string id, string name, string description,
            uint ipAddress, uint subnetMask, int prefixLength,
            bool hasGateway, string gatewayText, bool isVirtual)
            : this(id, name, description, ipAddress, subnetMask, prefixLength,
                   hasGateway, gatewayText, isVirtual, "")
        {
        }

        public AdapterInfo(string id, string name, string description,
            uint ipAddress, uint subnetMask, int prefixLength,
            bool hasGateway, string gatewayText, bool isVirtual, string gatewayMac)
        {
            _id = id;
            _name = name;
            _description = description;
            _ipAddress = ipAddress;
            _subnetMask = subnetMask;
            _prefixLength = prefixLength;
            _hasGateway = hasGateway;
            _gatewayText = gatewayText;
            _isVirtual = isVirtual;
            _gatewayMac = gatewayMac == null ? "" : gatewayMac;
        }

        /// <summary>网卡唯一标识（NetworkInterface.Id）。</summary>
        public string Id { get { return _id; } }

        /// <summary>连接名称（如"本地连接"）。</summary>
        public string Name { get { return _name; } }

        /// <summary>网卡描述（如"Realtek PCIe GbE Family Controller"）。</summary>
        public string Description { get { return _description; } }

        /// <summary>本机在该网卡上的 IPv4 地址。</summary>
        public uint IpAddress { get { return _ipAddress; } }

        /// <summary>子网掩码。</summary>
        public uint SubnetMask { get { return _subnetMask; } }

        /// <summary>前缀长度。</summary>
        public int PrefixLength { get { return _prefixLength; } }

        /// <summary>该网卡是否配置了默认网关。</summary>
        public bool HasGateway { get { return _hasGateway; } }

        /// <summary>默认网关文本（无网关时为空串）。</summary>
        public string GatewayText { get { return _gatewayText; } }

        /// <summary>
        /// 该网卡对应的网段。
        /// </summary>
        public NetSegment GetSegment()
        {
            return NetSegment.FromAddressAndPrefix(_ipAddress, _prefixLength);
        }

        /// <summary>该网卡是否被判定为虚拟网卡（仅在兜底模式下会出现）。</summary>
        public bool IsVirtual { get { return _isVirtual; } }

        /// <summary>
        /// 默认网关的 MAC 地址（形如 52:54:00:63:FB:B8）；未知时为空串。
        ///
        /// 用途（S13 修复 F1）：识别"网关代理回应"造成的跨网段假阳性。
        /// 当目标不在本机子网时，某些网关会代答 ARP，返回**网关自己的 MAC**，
        /// 导致不存在的远端地址被误判为存活。有了网关 MAC 就能把这类结果剔除。
        /// </summary>
        public string GatewayMac { get { return _gatewayMac; } }

        /// <summary>
        /// 下拉框显示文本。
        /// </summary>
        public string DisplayText
        {
            get
            {
                string text = _description + "  [" + Ipv4Util.Format(_ipAddress)
                    + "/" + _prefixLength + "]";

                if (_hasGateway)
                {
                    text += "  (默认网关 " + _gatewayText + ")";
                }

                if (_isVirtual)
                {
                    text += "  ⚠ 虚拟网卡";
                }

                return text;
            }
        }

        public override string ToString()
        {
            return DisplayText;
        }
    }

    /// <summary>
    /// 网卡枚举服务。
    /// 设计要点（architecture §4.2）：
    ///   · 使用 NetworkInterface，不用 WMI（旧电脑上的 WMI 可能不可用，PRD NFR-C4）；
    ///   · 过滤虚拟网卡（VMware/VirtualBox/Hyper-V/VPN/Loopback/Tunnel/Bluetooth）；
    ///   · 跳过 APIPA（169.254.x.x）与环回地址；
    ///   · 默认选中"有默认网关"的网卡。
    /// </summary>
    internal static class NetworkAdapterService
    {
        /// <summary>
        /// 虚拟/非物理网卡的识别关键字（小写匹配描述与名称）。
        /// 采用"宁可多排除"策略，避免把 VPN 虚拟网卡当成扫描源。
        /// </summary>
        private static readonly string[] VirtualKeywords = new string[]
        {
            // VMware
            "vmware",
            // VirtualBox
            "virtualbox",
            "vbox",
            // Hyper-V
            "hyper-v",
            "vethernet",
            // QEMU / KVM 半虚拟化网卡（VirtIO 系列）
            "virtio",
            "red hat virtio",
            // Xen
            "xen",
            // VPN / 隧道
            "tap-",
            "tap adapter",
            "tun",
            "openvpn",
            "wireguard",
            "npcap",
            "winpcap",
            // 系统虚拟接口
            "loopback",
            "pseudo",
            "tunnel",
            "teredo",
            "6to4",
            "isatap",
            // 蓝牙 / 虚拟适配器
            "bluetooth",
            "蓝牙",
            "虚拟",
            "wsl",
            "docker",
            "vpn",
            // 其它常见虚拟设备
            "wan miniport",
            "wi-fi direct",
            "microsoft wi-fi direct",
            "km-test",
            "qemu",
            "parallels",
            "virtual"
        };

        /// <summary>
        /// 枚举所有可用于扫描的网卡上的 IPv4 地址。
        /// 结果按"有网关优先"排序。
        ///
        /// 两阶段策略：
        ///   1. 优先只返回物理网卡（排除虚拟网卡）；
        ///   2. 若一块物理网卡都没有（例如程序运行在虚拟机内），
        ///      退回返回虚拟网卡并标记 IsVirtual，避免"什么都扫不了"。
        /// </summary>
        public static List<AdapterInfo> GetActiveAdapters()
        {
            List<AdapterInfo> physical = new List<AdapterInfo>();
            List<AdapterInfo> virtualOnes = new List<AdapterInfo>();

            try
            {
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();

                for (int i = 0; i < interfaces.Length; i++)
                {
                    NetworkInterface ni = interfaces[i];

                    try
                    {
                        if (!IsTypeCandidate(ni)) { continue; }

                        bool isVirtual = HasVirtualKeyword(ni);
                        AdapterInfo info = TryBuildAdapterInfo(ni, isVirtual);

                        if (info == null) { continue; }

                        if (isVirtual) { virtualOnes.Add(info); }
                        else { physical.Add(info); }
                    }
                    catch (Exception)
                    {
                        // 单块网卡读取失败不应影响其它网卡
                    }
                }
            }
            catch (Exception)
            {
                return new List<AdapterInfo>();
            }

            List<AdapterInfo> result;

            if (physical.Count > 0)
            {
                // 正常情况：只用物理网卡
                result = physical;
            }
            else
            {
                // 兜底：本机只有虚拟网卡（典型场景是程序跑在虚拟机里）
                result = virtualOnes;
            }

            result.Sort(CompareAdapters);

            return result;
        }

        /// <summary>
        /// 是否处于"兜底模式"（本机无物理网卡，只能列出虚拟网卡）。
        /// </summary>
        public static bool IsFallbackMode(List<AdapterInfo> adapters)
        {
            if (adapters == null || adapters.Count == 0) { return false; }

            for (int i = 0; i < adapters.Count; i++)
            {
                if (!adapters[i].IsVirtual) { return false; }
            }

            return true;
        }

        private static int CompareAdapters(AdapterInfo a, AdapterInfo b)
        {
            if (a.HasGateway != b.HasGateway)
            {
                return a.HasGateway ? -1 : 1;
            }

            return string.Compare(a.Description, b.Description, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 判断网卡类型是否为可用的物理以太网/无线（只看类型与状态，不看虚拟关键字）。
        /// </summary>
        private static bool IsTypeCandidate(NetworkInterface ni)
        {
            if (ni.OperationalStatus != OperationalStatus.Up) { return false; }

            NetworkInterfaceType t = ni.NetworkInterfaceType;

            return t == NetworkInterfaceType.Ethernet
                || t == NetworkInterfaceType.GigabitEthernet
                || t == NetworkInterfaceType.FastEthernetT
                || t == NetworkInterfaceType.FastEthernetFx
                || t == NetworkInterfaceType.Wireless80211;
        }

        /// <summary>
        /// 描述或名称中是否含虚拟网卡关键字。
        /// </summary>
        private static bool HasVirtualKeyword(NetworkInterface ni)
        {
            string haystack = ((ni.Description == null ? "" : ni.Description)
                + " " + (ni.Name == null ? "" : ni.Name)).ToLowerInvariant();

            for (int i = 0; i < VirtualKeywords.Length; i++)
            {
                if (haystack.IndexOf(VirtualKeywords[i], StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 从网卡提取第一个合法的 IPv4 单播地址，构造 AdapterInfo。
        /// 无合法地址时返回 null。
        /// </summary>
        private static AdapterInfo TryBuildAdapterInfo(NetworkInterface ni, bool isVirtual)
        {
            IPInterfaceProperties props = ni.GetIPProperties();
            if (props == null) { return null; }

            string gatewayText = "";
            bool hasGateway = false;

            try
            {
                foreach (GatewayIPAddressInformation gw in props.GatewayAddresses)
                {
                    if (gw.Address == null) { continue; }
                    if (gw.Address.AddressFamily != AddressFamily.InterNetwork) { continue; }

                    uint gwValue = IpToUInt(gw.Address);
                    if (Ipv4Util.IsAny(gwValue)) { continue; }

                    gatewayText = gw.Address.ToString();
                    hasGateway = true;
                    break;
                }
            }
            catch (Exception)
            {
                // 网关信息读取失败视为无网关，不影响本机地址解析
            }

            foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
            {
                if (ua.Address == null) { continue; }
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) { continue; }

                uint ip = IpToUInt(ua.Address);

                if (Ipv4Util.IsLoopback(ip)) { continue; }
                if (Ipv4Util.IsAny(ip)) { continue; }
                if (Ipv4Util.IsApipa(ip)) { continue; }

                uint mask = 0;

                try
                {
                    if (ua.IPv4Mask != null)
                    {
                        mask = IpToUInt(ua.IPv4Mask);
                    }
                }
                catch (Exception)
                {
                    mask = 0;
                }

                int prefix = mask == 0 ? 32 : Ipv4Util.MaskToPrefix(mask);

                // 非连续掩码或无掩码：跳过（无法可靠确定网段）
                if (prefix < 0) { continue; }
                if (prefix < NetSegment.MinPrefixLength || prefix > NetSegment.MaxPrefixLength)
                {
                    continue;
                }

                return new AdapterInfo(
                    ni.Id,
                    ni.Name,
                    ni.Description,
                    ip,
                    mask,
                    prefix,
                    hasGateway,
                    gatewayText,
                    isVirtual,
                    ResolveGatewayMac(gatewayText));
            }

            return null;
        }

        /// <summary>
        /// 查询默认网关的 MAC（用于识别网关代理回应造成的假阳性）。
        /// 查询失败时返回空串，不影响正常扫描（只是少一层过滤）。
        /// </summary>
        private static string ResolveGatewayMac(string gatewayText)
        {
            if (string.IsNullOrEmpty(gatewayText)) { return ""; }

            uint gateway;

            if (!Ipv4Util.TryParse(gatewayText, out gateway)) { return ""; }

            try
            {
                return ArpTableReader.FindMac(gateway);
            }
            catch (Exception)
            {
                // 查询失败：返回空，跳过假阳性过滤
                return "";
            }
        }

        /// <summary>
        /// IPAddress → uint（主机字节序）。
        /// </summary>
        public static uint IpToUInt(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();

            // GetAddressBytes 对大端顺序返回，按网络序拼装
            return ((uint)bytes[0] << 24)
                 | ((uint)bytes[1] << 16)
                 | ((uint)bytes[2] << 8)
                 | bytes[3];
        }

        /// <summary>
        /// uint → IPAddress。
        /// </summary>
        public static IPAddress UIntToIp(uint address)
        {
            return new IPAddress(new byte[]
            {
                (byte)((address >> 24) & 0xFF),
                (byte)((address >> 16) & 0xFF),
                (byte)((address >> 8) & 0xFF),
                (byte)(address & 0xFF)
            });
        }
    }
}
