using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MQNetScan.Engine;
using MQNetScan.Infrastructure;

namespace MQNetScan.Application
{
    /// <summary>
    /// 单项连通性自检结果。
    /// </summary>
    internal sealed class CheckResult
    {
        private readonly string _name;
        private readonly string _target;
        private readonly bool _passed;
        private readonly long _elapsedMs;
        private readonly string _detail;

        public CheckResult(string name, string target, bool passed, long elapsedMs, string detail)
        {
            _name = name == null ? "" : name;
            _target = target == null ? "" : target;
            _passed = passed;
            _elapsedMs = elapsedMs;
            _detail = detail == null ? "" : detail;
        }

        /// <summary>检查项名称（如"默认网关"）。</summary>
        public string Name { get { return _name; } }

        /// <summary>检查目标（IP 或域名）。</summary>
        public string Target { get { return _target; } }

        /// <summary>是否通过。</summary>
        public bool Passed { get { return _passed; } }

        /// <summary>耗时（毫秒）。</summary>
        public long ElapsedMs { get { return _elapsedMs; } }

        /// <summary>详细说明（失败原因等）。</summary>
        public string Detail { get { return _detail; } }

        /// <summary>结论文本：通 / 不通。</summary>
        public string ConclusionText { get { return _passed ? "通" : "不通"; } }

        /// <summary>
        /// 表格行显示文本。
        /// </summary>
        public string ToDisplayText()
        {
            string status = _passed ? "通　" : "不通";

            return _name + "：" + status
                + "　" + _target
                + "　" + _elapsedMs + " ms"
                + (string.IsNullOrEmpty(_detail) ? "" : "　" + _detail);
        }
    }

    /// <summary>
    /// 三项连通性自检（PRD FR-3.3）。
    ///
    /// 检查项：
    ///   1. 默认网关；
    ///   2. 公共 DNS（默认 223.5.5.5，可配置）；
    ///   3. 思迅 SaaS 域名（可配置）。
    ///
    /// 设计约束：
    ///   · 三项**相互独立**，单项失败不影响其它项；
    ///   · 用 Dns.GetHostAddresses + Socket，**禁止 HttpClient**（PRD FR-3.3.4）；
    ///   · 每项都有超时，不阻塞界面。
    /// </summary>
    internal static class ConnectivityChecker
    {
        /// <summary>单项超时（毫秒）。</summary>
        public const int ItemTimeoutMs = 3000;

        /// <summary>
        /// 检查默认网关。
        /// </summary>
        public static CheckResult CheckGateway(AdapterInfo adapter)
        {
            Stopwatch sw = Stopwatch.StartNew();

            if (adapter == null)
            {
                sw.Stop();
                return new CheckResult("默认网关", "（未识别网卡）", false, sw.ElapsedMilliseconds,
                    "未找到可用网卡");
            }

            string gwText = adapter.GatewayText;

            if (string.IsNullOrEmpty(gwText))
            {
                sw.Stop();
                return new CheckResult("默认网关", "（未配置）", false, sw.ElapsedMilliseconds,
                    "本机未配置默认网关");
            }

            GatewayProbeResult r = GatewayProber.Probe(gwText, adapter.IpAddress, ItemTimeoutMs);

            sw.Stop();

            return new CheckResult("默认网关", gwText, r.Reachable,
                sw.ElapsedMilliseconds,
                r.Reachable ? ("方式 " + r.Method) : "ARP 与 ICMP 均无响应");
        }

        /// <summary>
        /// 检查公共 DNS 服务器（用 TCP 53 与 ICMP 双路）。
        /// </summary>
        public static CheckResult CheckDnsServer(string dnsIpText)
        {
            Stopwatch sw = Stopwatch.StartNew();

            string target = string.IsNullOrEmpty(dnsIpText) ? IniSchema.DefaultDnsProbeIp : dnsIpText.Trim();

            uint ip;
            if (!Ipv4Util.TryParse(target, out ip))
            {
                sw.Stop();
                return new CheckResult("公共 DNS", target, false, sw.ElapsedMilliseconds,
                    "配置的地址不是合法 IPv4");
            }

            // 用 ICMP 最快（大多数公共 DNS 会回应 ping）
            bool passed = false;
            string detail = "";

            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = ping.Send(NetworkAdapterService.UIntToIp(ip), ItemTimeoutMs);

                    if (reply != null && reply.Status == IPStatus.Success)
                    {
                        passed = true;
                        detail = "ICMP 往返";
                    }
                    else
                    {
                        detail = reply == null ? "无回应" : reply.Status.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                detail = "ICMP 不可用（" + ex.Message + "）";
            }

            // ICMP 不通时用 TCP 53 兜底（有些网络屏蔽 ICMP）
            if (!passed)
            {
                try
                {
                    PortProbeResult pr = PortProber.ProbeSingle(ip, 53, ItemTimeoutMs);

                    if (pr.IsOpen)
                    {
                        passed = true;
                        detail = "TCP 53 可达";
                    }
                }
                catch (Exception)
                {
                    // 忽略
                }
            }

            sw.Stop();

            if (!passed && string.IsNullOrEmpty(detail)) { detail = "ICMP 与 TCP 53 均不可达"; }

            return new CheckResult("公共 DNS", target, passed, sw.ElapsedMilliseconds, detail);
        }

        /// <summary>
        /// 检查 SaaS 域名（DNS 解析 + TCP 探测）。
        /// </summary>
        public static CheckResult CheckSaasDomain(string domain)
        {
            Stopwatch sw = Stopwatch.StartNew();

            string target = string.IsNullOrEmpty(domain) ? IniSchema.DefaultSaasDomain : domain.Trim();

            // ---- 1. DNS 解析 ----
            IPAddress resolved = null;
            string dnsError = null;

            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(target);

                for (int i = 0; i < addresses.Length; i++)
                {
                    if (addresses[i].AddressFamily == AddressFamily.InterNetwork)
                    {
                        resolved = addresses[i];
                        break;
                    }
                }

                if (resolved == null && addresses.Length > 0)
                {
                    resolved = addresses[0];
                }
            }
            catch (Exception ex)
            {
                dnsError = ex.Message;
            }

            if (resolved == null)
            {
                sw.Stop();

                return new CheckResult("SaaS 域名", target, false, sw.ElapsedMilliseconds,
                    "DNS 解析失败" + (string.IsNullOrEmpty(dnsError) ? "" : "：" + Trim(dnsError, 60)));
            }

            // ---- 2. TCP 探测（443 或 80）----
            bool tcpOk = false;
            int usedPort = 0;

            int[] ports = new int[] { 443, 80 };

            for (int i = 0; i < ports.Length; i++)
            {
                try
                {
                    PortProbeResult pr = PortProber.ProbeSingle(
                        NetworkAdapterService.IpToUInt(resolved), ports[i], ItemTimeoutMs);

                    if (pr.IsOpen)
                    {
                        tcpOk = true;
                        usedPort = ports[i];
                        break;
                    }
                }
                catch (Exception)
                {
                    // 忽略，尝试下一个端口
                }
            }

            sw.Stop();

            if (tcpOk)
            {
                return new CheckResult("SaaS 域名", target, true, sw.ElapsedMilliseconds,
                    "解析到 " + resolved + "，TCP " + usedPort + " 可达");
            }

            // 解析成功但 TCP 不通：仍算"部分成功"，如实说明
            return new CheckResult("SaaS 域名", target, false, sw.ElapsedMilliseconds,
                "解析到 " + resolved + "，但 443/80 均不可达");
        }

        /// <summary>
        /// 执行全部三项自检。
        /// 注意：本方法同步阻塞，调用方须在后台线程执行。
        /// </summary>
        public static List<CheckResult> RunAll(AdapterInfo adapter, string dnsIp, string saasDomain)
        {
            List<CheckResult> results = new List<CheckResult>();

            // 三项相互独立：单项异常不影响其它项
            try { results.Add(CheckGateway(adapter)); }
            catch (Exception ex)
            {
                results.Add(new CheckResult("默认网关", "-", false, 0, "检查异常：" + Trim(ex.Message, 40)));
            }

            try { results.Add(CheckDnsServer(dnsIp)); }
            catch (Exception ex)
            {
                results.Add(new CheckResult("公共 DNS", dnsIp, false, 0, "检查异常：" + Trim(ex.Message, 40)));
            }

            try { results.Add(CheckSaasDomain(saasDomain)); }
            catch (Exception ex)
            {
                results.Add(new CheckResult("SaaS 域名", saasDomain, false, 0, "检查异常：" + Trim(ex.Message, 40)));
            }

            return results;
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) { return ""; }
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
