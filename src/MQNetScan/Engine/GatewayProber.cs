using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;

namespace MQNetScan.Engine
{
    /// <summary>
    /// 网关探测结果。
    /// </summary>
    internal sealed class GatewayProbeResult
    {
        private readonly bool _reachable;
        private readonly string _gatewayText;
        private readonly string _method;
        private readonly long _elapsedMs;

        public GatewayProbeResult(bool reachable, string gatewayText, string method, long elapsedMs)
        {
            _reachable = reachable;
            _gatewayText = gatewayText == null ? "" : gatewayText;
            _method = method == null ? "" : method;
            _elapsedMs = elapsedMs;
        }

        /// <summary>网关是否可达。</summary>
        public bool Reachable { get { return _reachable; } }

        /// <summary>网关地址文本。</summary>
        public string GatewayText { get { return _gatewayText; } }

        /// <summary>探测手段（ARP / TCP / ICMP / 无）。</summary>
        public string Method { get { return _method; } }

        /// <summary>耗时（毫秒）。</summary>
        public long ElapsedMs { get { return _elapsedMs; } }

        /// <summary>生成对应的告警项（可达时返回 null）。</summary>
        public AlertItem ToAlert()
        {
            if (_reachable) { return null; }

            string message;

            if (string.IsNullOrEmpty(_gatewayText))
            {
                message = "未配置默认网关，无法判断外网连通性。";
            }
            else
            {
                message = "默认网关 " + _gatewayText + " 不可达。";
            }

            return new AlertItem(
                AlertKind.GatewayUnreachable,
                message,
                "请优先检查交换机、路由器与上联线路；此时设备层面的排查意义不大。",
                _gatewayText);
        }
    }

    /// <summary>
    /// 网关探测。
    ///
    /// 设计要点（PRD FR-1.5.1）：
    ///   1. 先用 SendARP 判断网关是否在同一广播域内存活（最快，且不受禁 Ping 影响）；
    ///   2. ARP 通 → 再尝试 TCP 80/443（确认三层转发正常）；
    ///   3. ARP 不通 → 用 ICMP 兜底（极少数网关会回应 ping 但不回 ARP 的异常情况）；
    ///   4. 全失败 → 判定不可达，触发红色横幅。
    ///
    /// 注意：本类同步阻塞，调用方须在后台线程执行。
    /// </summary>
    internal static class GatewayProber
    {
        /// <summary>TCP 探测端口（PRD FR-1.5.1：80/443 任一）。</summary>
        private static readonly int[] TcpProbePorts = new int[] { 80, 443 };

        /// <summary>
        /// 探测默认网关。无法确定网关时返回"未配置"结果。
        /// </summary>
        public static GatewayProbeResult Probe(string gatewayText, uint localAddress, int tcpTimeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();

            if (string.IsNullOrEmpty(gatewayText))
            {
                sw.Stop();

                // 无网关：这本身是"不可达"的一种情形，需要提示
                return new GatewayProbeResult(false, "", "无", sw.ElapsedMilliseconds);
            }

            uint gw;
            if (!Ipv4Util.TryParse(gatewayText, out gw))
            {
                sw.Stop();
                return new GatewayProbeResult(false, gatewayText, "无", sw.ElapsedMilliseconds);
            }

            // ---- 1. ARP 存活判定 ----
            bool arpAlive = false;

            try
            {
                ArpProber arp = new ArpProber();
                ScanParameters prm = new ScanParameters(new uint[] { gw }, 1, tcpTimeoutMs, 1, new int[0]);
                HostRecord rec = arp.Probe(gw, prm, null);
                arpAlive = rec != null;
            }
            catch (Exception)
            {
                arpAlive = false;
            }

            if (!arpAlive)
            {
                // ---- 3. ICMP 兜底 ----
                // 注意：用 Ping 作为兜底是刻意的——
                // 有些环境（如跨 VLAN 的网关）ARP 不可达但 ICMP 可达。
                bool icmpAlive = TryIcmp(gw, 1000);

                sw.Stop();

                if (icmpAlive)
                {
                    return new GatewayProbeResult(true, gatewayText, "ICMP", sw.ElapsedMilliseconds);
                }

                return new GatewayProbeResult(false, gatewayText, "ARP+ICMP", sw.ElapsedMilliseconds);
            }

            // ---- 2. ARP 已通 → 判定可达 ----
            //
            // 重要设计决定：**ARP 通即视为网关可达，不再等待 TCP 探测**。
            //
            // 原因：绝大多数路由器/网关本身并不监听 80/443（或被防火墙丢弃），
            // 若在此处逐个探测 TCP 端口，每个都要等满超时，
            // 实测会让"网关正常"的场景也白等 3 秒以上（2 端口 × 800ms × 重试）。
            //
            // TCP 探测保留为**快速加分项**：只在超时很短时尝试一次，
            // 通就记录更精确的方式，不通也不影响"可达"的结论。
            sw.Stop();

            string method = "ARP";

            try
            {
                // 用很短的超时（最多 300ms）尝试一次，避免拖慢主流程
                int quickTimeout = tcpTimeoutMs < 300 ? tcpTimeoutMs : 300;

                for (int i = 0; i < TcpProbePorts.Length; i++)
                {
                    PortProbeResult pr = PortProber.ProbeSingle(gw, TcpProbePorts[i], quickTimeout);

                    if (pr.IsOpen)
                    {
                        method = "ARP+TCP" + TcpProbePorts[i];
                        break;
                    }

                    if (sw.ElapsedMilliseconds > 600) { break; }   // 总耗时不超 600ms
                }
            }
            catch (Exception)
            {
                // TCP 探测失败不影响"可达"结论
            }

            sw.Stop();

            return new GatewayProbeResult(true, gatewayText, method, sw.ElapsedMilliseconds);
        }

        /// <summary>
        /// ICMP 探测（兜底用）。
        /// </summary>
        private static bool TryIcmp(uint address, int timeoutMs)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = ping.Send(NetworkAdapterService.UIntToIp(address), timeoutMs);

                    return reply != null && reply.Status == IPStatus.Success;
                }
            }
            catch (Exception)
            {
                // 无权限或 ICMP 被禁用
                return false;
            }
        }

        /// <summary>
        /// 从网卡信息中取出默认网关文本。
        /// </summary>
        public static string GetGatewayText(AdapterInfo adapter)
        {
            if (adapter == null) { return ""; }

            return adapter.HasGateway ? adapter.GatewayText : "";
        }

        /// <summary>
        /// 探测指定网卡的网关。
        /// </summary>
        public static GatewayProbeResult ProbeAdapter(AdapterInfo adapter, int tcpTimeoutMs)
        {
            if (adapter == null)
            {
                return new GatewayProbeResult(false, "", "无", 0);
            }

            return Probe(adapter.GatewayText, adapter.IpAddress, tcpTimeoutMs);
        }
    }
}
