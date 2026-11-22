using System;
using System.Collections.Generic;
using MQNetScan.Engine.Contracts;

namespace MQNetScan.Engine.Discovery
{
    /// <summary>
    /// 局域网协议发现扩展接口。
    ///
    /// 当前版本先定义稳定边界：SSDP/mDNS 探测必须独立于主 ARP/Ping 扫描，
    /// 不能阻塞主扫描，也不能把弱厂商线索当成确定类别。后续实现可注入
    /// SSDP、mDNS 或厂商协议发现器，统一返回 Evidence。
    /// </summary>
    internal interface IProtocolDiscovery
    {
        IList<DiscoveryEvidence> Discover(uint address, int timeoutMs, Func<bool> shouldContinue);
    }

    /// <summary>协议发现的一条可解释证据。</summary>
    internal sealed class DiscoveryEvidence
    {
        public string Source { get; private set; }
        public string Protocol { get; private set; }
        public string Detail { get; private set; }
        public string SuggestedRole { get; private set; }
        public DeviceConfidence Confidence { get; private set; }

        public DiscoveryEvidence(string source, string protocol, string detail,
            string suggestedRole, DeviceConfidence confidence)
        {
            Source = source == null ? "" : source;
            Protocol = protocol == null ? "" : protocol;
            Detail = detail == null ? "" : detail;
            SuggestedRole = suggestedRole == null ? "" : suggestedRole;
            Confidence = confidence;
        }
    }

    /// <summary>
    /// 统一合并端口、网关、SSDP/mDNS 等证据的接口。
    /// 当前端口识别仍由 CompositeProber 完成；此接口为后续协议探测保留扩展点。
    /// </summary>
    internal interface IDeviceEvidenceMerger
    {
        void Apply(HostRecord record, IList<DiscoveryEvidence> evidence);
    }
}
