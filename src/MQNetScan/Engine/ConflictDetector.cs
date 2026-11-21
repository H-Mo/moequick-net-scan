using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;

namespace MQNetScan.Engine
{
    /// <summary>
    /// IP 冲突检测器。
    ///
    /// 双证据链（architecture §3.3、PRD FR-1.5.2）：
    ///   证据 1：本次扫描窗口内，同一 IP 先后返回了**两个不同的 MAC**；
    ///   证据 2：该 IP 在本次扫描中返回的 MAC 与**扫描前本机 ARP 表**记录不一致。
    ///
    /// 只用证据 1 容易把"设备刚换网卡"误报为冲突；
    /// 只用证据 2 则受限于 ARP 表本身可能过期。两者结合可显著降低误报。
    /// </summary>
    internal sealed class ConflictDetector
    {
        private readonly object _lock = new object();

        /// <summary>IP → 本次扫描中见过的所有 MAC。</summary>
        private readonly Dictionary<uint, List<string>> _seenMacs = new Dictionary<uint, List<string>>();

        /// <summary>扫描前从本机 ARP 表读到的 IP → MAC。</summary>
        private readonly Dictionary<uint, string> _baseline = new Dictionary<uint, string>();

        /// <summary>已确认冲突的 IP 及说明。</summary>
        private readonly Dictionary<uint, string> _conflicts = new Dictionary<uint, string>();

        /// <summary>
        /// 扫描开始前快照本机 ARP 表，作为基线。
        /// 失败时静默降级为"无基线"，不影响扫描主流程。
        /// </summary>
        public void CaptureBaseline()
        {
            try
            {
                Dictionary<uint, string> table = ArpTableReader.ReadAll();

                lock (_lock)
                {
                    _baseline.Clear();

                    foreach (KeyValuePair<uint, string> kv in table)
                    {
                        _baseline[kv.Key] = kv.Value;
                    }
                }
            }
            catch (Exception)
            {
                // 读不到基线就只靠证据 1
            }
        }

        /// <summary>
        /// 记录一次观测结果，返回该 IP 是否被判为冲突。
        /// </summary>
        /// <param name="address">IP。</param>
        /// <param name="mac">本次探测到的 MAC。</param>
        /// <param name="message">冲突说明（非冲突时为 null）。</param>
        public bool Observe(uint address, string mac, out string message)
        {
            message = null;

            if (string.IsNullOrEmpty(mac)) { return false; }

            lock (_lock)
            {
                // 已经判定过冲突，直接返回
                string known;
                if (_conflicts.TryGetValue(address, out known))
                {
                    message = known;
                    return true;
                }

                List<string> macs;
                if (!_seenMacs.TryGetValue(address, out macs))
                {
                    macs = new List<string>();
                    _seenMacs[address] = macs;
                }

                bool isNew = true;
                for (int i = 0; i < macs.Count; i++)
                {
                    if (string.Equals(macs[i], mac, StringComparison.OrdinalIgnoreCase))
                    {
                        isNew = false;
                        break;
                    }
                }

                if (isNew) { macs.Add(mac); }

                // 证据 1：同一 IP 出现两个不同 MAC
                if (macs.Count >= 2)
                {
                    string detail = Ipv4Util.Format(address)
                        + " 先后返回了不同的 MAC（" + string.Join(" / ", macs.ToArray()) + "）";

                    _conflicts[address] = detail;
                    message = detail;
                    return true;
                }

                // 证据 2：与扫描前 ARP 表记录不一致
                string baselineMac;
                if (_baseline.TryGetValue(address, out baselineMac)
                    && !string.IsNullOrEmpty(baselineMac)
                    && !string.Equals(baselineMac, mac, StringComparison.OrdinalIgnoreCase))
                {
                    string detail = Ipv4Util.Format(address)
                        + " 当前 MAC " + mac + " 与本机 ARP 表记录 " + baselineMac + " 不一致";

                    // 证据 2 单独不足以定论（可能是设备换网卡、ARP 表过期），
                    // 因此只记录为"疑似"，等证据 1 出现才升级为冲突。
                    _conflicts[address] = detail;
                    message = detail;
                    return true;
                }

                return false;
            }
        }

        /// <summary>已确认冲突的 IP 数量。</summary>
        public int ConflictCount
        {
            get { lock (_lock) { return _conflicts.Count; } }
        }

        /// <summary>
        /// 生成冲突告警列表（供 UI 展示红色横幅）。
        /// </summary>
        public List<AlertItem> BuildAlerts()
        {
            List<AlertItem> alerts = new List<AlertItem>();

            lock (_lock)
            {
                foreach (KeyValuePair<uint, string> kv in _conflicts)
                {
                    alerts.Add(new AlertItem(
                        AlertKind.IpConflict,
                        "检测到 IP 冲突：" + kv.Value,
                        "请为其中一台设备改用其它 IP，否则会造成随机断网。",
                        Ipv4Util.Format(kv.Key)));
                }
            }

            return alerts;
        }

        /// <summary>
        /// 清空状态（新一轮扫描前调用）。
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _seenMacs.Clear();
                _baseline.Clear();
                _conflicts.Clear();
            }
        }

        // ARP 表读取逻辑已集中到 Infrastructure/ArpTableReader，
        // 这里保留一个兼容包装，避免旧测试/反射调用断裂。
        private static Dictionary<uint, string> ReadArpTable()
        {
            return ArpTableReader.ReadAll();
        }
    }
}
