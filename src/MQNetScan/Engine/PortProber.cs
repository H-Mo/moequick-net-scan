using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;

namespace MQNetScan.Engine
{
    /// <summary>
    /// 单个端口的探测结果。
    /// </summary>
    internal enum PortStatus
    {
        /// <summary>未探测。</summary>
        Unknown = 0,

        /// <summary>开放（握手成功）。</summary>
        Open = 1,

        /// <summary>被拒绝（主机在线但端口未监听）。</summary>
        Refused = 2,

        /// <summary>超时（无任何回应，通常是防火墙丢弃）。</summary>
        Timeout = 3,

        /// <summary>主机或网络不可达。</summary>
        Unreachable = 4
    }

    /// <summary>
    /// 端口探测结果集合（供测通器与扫描共用）。
    /// </summary>
    internal sealed class PortProbeResult
    {
        private readonly int _port;
        private readonly PortStatus _status;
        private readonly int _elapsedMs;

        public PortProbeResult(int port, PortStatus status, int elapsedMs)
        {
            _port = port;
            _status = status;
            _elapsedMs = elapsedMs;
        }

        public int Port { get { return _port; } }
        public PortStatus Status { get { return _status; } }
        public int ElapsedMs { get { return _elapsedMs; } }
        public bool IsOpen { get { return _status == PortStatus.Open; } }

        /// <summary>失败原因的中文说明（成功时为空串）。</summary>
        public string DescribeFailure()
        {
            switch (_status)
            {
                case PortStatus.Open: return "";
                case PortStatus.Refused: return "连接被拒绝（端口未监听）";
                case PortStatus.Timeout: return "连接超时（可能被防火墙丢弃）";
                case PortStatus.Unreachable: return "主机或网络不可达";
                default: return "未知状态";
            }
        }
    }

    /// <summary>
    /// 端口探测器。
    ///
    /// 设计要点（architecture §3.4）：
    ///   · **非阻塞 connect + Socket.Select 轮询**，绝不为每个端口起线程；
    ///   · 一次 Select 批量收割该 IP 的全部端口结果；
    ///   · 单 IP 总预算 = 单端口超时 × 2，防止黑洞地址拖住工作线程；
    ///   · 每个 socket 在 finally 中关闭，所有权限定在创建它的线程内。
    ///
    /// 禁止事项（architecture §3.4）：
    ///   · 不得使用 Socket.ConnectAsync（4.5+ 语义）；
    ///   · 不得使用 TcpClient.BeginConnect + 回调（难以统一暂停/终止且易产生未捕获异常）。
    /// </summary>
    internal static class PortProber
    {
        /// <summary>
        /// 对单个 IP 的全部端口发起探测。
        /// </summary>
        /// <param name="ipAddress">目标 IP（uint，主机字节序）。</param>
        /// <param name="ports">待探测端口（已去重升序）。</param>
        /// <param name="timeoutMs">单端口超时（毫秒）。</param>
        /// <param name="shouldContinue">返回 false 时尽快放弃。</param>
        /// <returns>全部端口的探测结果。</returns>
        public static List<PortProbeResult> ProbePorts(
            uint ipAddress, int[] ports, int timeoutMs, Func<bool> shouldContinue)
        {
            List<PortProbeResult> results = new List<PortProbeResult>();

            if (ports == null || ports.Length == 0) { return results; }

            if (timeoutMs < 1) { timeoutMs = 1; }

            IPAddress target = NetworkAdapterService.UIntToIp(ipAddress);

            // 单 IP 总预算：单端口超时 × 2（architecture §3.3）
            int totalBudgetMs = timeoutMs * 2;

            Stopwatch budget = Stopwatch.StartNew();

            // 每个端口一个 socket，一次性全部发起非阻塞 connect
            Socket[] sockets = new Socket[ports.Length];
            int[] remaining = new int[ports.Length];      // 剩余预算
            PortStatus[] statuses = new PortStatus[ports.Length];
            int[] elapsed = new int[ports.Length];
            bool[] finished = new bool[ports.Length];

            for (int i = 0; i < ports.Length; i++)
            {
                statuses[i] = PortStatus.Unknown;
                elapsed[i] = 0;
                remaining[i] = timeoutMs;
            }

            try
            {
                // ---- 阶段 1：发起全部非阻塞连接 ----
                for (int i = 0; i < ports.Length; i++)
                {
                    if (shouldContinue != null && !shouldContinue())
                    {
                        // 提前放弃：未发起的记为 Unknown
                        for (int k = i; k < ports.Length; k++) { finished[k] = true; }
                        break;
                    }

                    sockets[i] = TryStartConnect(target, ports[i], statuses, elapsed, i);
                }

                // ---- 阶段 2：Select 轮询收割 ----
                while (true)
                {
                    if (shouldContinue != null && !shouldContinue()) { break; }

                    // 收集仍在进行中的 socket
                    List<Socket> pending = new List<Socket>();
                    List<int> pendingIndex = new List<int>();

                    for (int i = 0; i < ports.Length; i++)
                    {
                        if (finished[i]) { continue; }
                        if (sockets[i] == null) { finished[i] = true; continue; }

                        pending.Add(sockets[i]);
                        pendingIndex.Add(i);
                    }

                    if (pending.Count == 0) { break; }

                    // 总预算耗尽：剩余一律记为超时
                    long budgetLeft = totalBudgetMs - budget.ElapsedMilliseconds;

                    if (budgetLeft <= 0)
                    {
                        for (int k = 0; k < pendingIndex.Count; k++)
                        {
                            int idx = pendingIndex[k];
                            if (!finished[idx])
                            {
                                statuses[idx] = PortStatus.Timeout;
                                elapsed[idx] = (int)budget.ElapsedMilliseconds;
                                finished[idx] = true;
                            }
                        }
                        break;
                    }

                    // Select 的等待时间取"总预算剩余"的较小值
                    int waitMicro = (int)Math.Min(budgetLeft, 50) * 1000;

                    List<Socket> checkRead = null;
                    List<Socket> checkWrite = new List<Socket>(pending);
                    List<Socket> checkError = new List<Socket>(pending);

                    try
                    {
                        Socket.Select(checkRead, checkWrite, checkError, waitMicro);
                    }
                    catch (SocketException)
                    {
                        // Select 本身出错：全部记为不可达
                        for (int k = 0; k < pendingIndex.Count; k++)
                        {
                            int idx = pendingIndex[k];
                            if (!finished[idx])
                            {
                                statuses[idx] = PortStatus.Unreachable;
                                finished[idx] = true;
                            }
                        }
                        break;
                    }

                    // 可写或出错 → 可判定
                    for (int k = 0; k < pendingIndex.Count; k++)
                    {
                        int idx = pendingIndex[k];
                        if (finished[idx]) { continue; }

                        Socket s = sockets[idx];

                        bool writable = ContainsSocket(checkWrite, s);
                        bool errored = ContainsSocket(checkError, s);

                        if (!writable && !errored) { continue; }

                        statuses[idx] = ClassifyCompletion(s, errored);
                        elapsed[idx] = (int)budget.ElapsedMilliseconds;
                        finished[idx] = true;
                    }

                    // 检查哪些 socket 因超时而未完成
                    for (int k = 0; k < pendingIndex.Count; k++)
                    {
                        int idx = pendingIndex[k];
                        if (finished[idx]) { continue; }

                        if (budget.ElapsedMilliseconds >= totalBudgetMs)
                        {
                            statuses[idx] = PortStatus.Timeout;
                            elapsed[idx] = (int)budget.ElapsedMilliseconds;
                            finished[idx] = true;
                        }
                    }
                }
            }
            finally
            {
                // 无论成功失败，全部关闭（所有权限定在本线程）
                for (int i = 0; i < sockets.Length; i++)
                {
                    if (sockets[i] != null)
                    {
                        try { sockets[i].Close(); }
                        catch (Exception) { }
                        sockets[i] = null;
                    }
                }
            }

            // ---- 组装结果 ----
            for (int i = 0; i < ports.Length; i++)
            {
                PortStatus st = statuses[i];

                if (st == PortStatus.Unknown)
                {
                    // 未能判定（被提前打断）：不当作开放
                    st = PortStatus.Timeout;
                }

                results.Add(new PortProbeResult(ports[i], st, elapsed[i]));
            }

            return results;
        }

        /// <summary>
        /// 发起一次非阻塞 connect。返回 socket（失败时返回 null 并写入状态）。
        /// </summary>
        private static Socket TryStartConnect(
            IPAddress target, int port, PortStatus[] statuses, int[] elapsed, int index)
        {
            Socket sock = null;

            try
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                sock.Blocking = false;

                try
                {
                    sock.Connect(target, port);

                    // 小概率：立即连上（本机回环或极快网络）
                    statuses[index] = PortStatus.Open;
                    elapsed[index] = 0;
                    return null;
                }
                catch (SocketException ex)
                {
                    // WSAEWOULDBLOCK(10035) 表示连接进行中，属正常
                    if (ex.SocketErrorCode == SocketError.WouldBlock
                        || ex.ErrorCode == 10035)
                    {
                        return sock;   // 交给 Select 处理
                    }

                    statuses[index] = MapSocketError(ex.SocketErrorCode);
                    elapsed[index] = 0;

                    sock.Close();
                    return null;
                }
            }
            catch (Exception)
            {
                if (sock != null)
                {
                    try { sock.Close(); } catch (Exception) { }
                }

                statuses[index] = PortStatus.Unreachable;
                elapsed[index] = 0;
                return null;
            }
        }

        /// <summary>
        /// 判定已完成的 socket 是开放还是被拒。
        /// </summary>
        private static PortStatus ClassifyCompletion(Socket sock, bool errored)
        {
            try
            {
                // 有错误码 → 被拒/不可达
                int socketError = (int)sock.GetSocketOption(
                    SocketOptionLevel.Socket, SocketOptionName.Error);

                if (socketError != 0)
                {
                    return MapSocketError((SocketError)socketError);
                }

                if (sock.Connected) { return PortStatus.Open; }

                if (errored) { return PortStatus.Refused; }

                return PortStatus.Open;
            }
            catch (Exception)
            {
                return errored ? PortStatus.Refused : PortStatus.Open;
            }
        }

        private static bool ContainsSocket(List<Socket> list, Socket s)
        {
            if (list == null) { return false; }

            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], s)) { return true; }
            }

            return false;
        }

        /// <summary>
        /// WinSock 错误码 → 统一分类。
        /// </summary>
        public static PortStatus MapSocketError(SocketError error)
        {
            switch (error)
            {
                case SocketError.ConnectionRefused:
                    return PortStatus.Refused;

                case SocketError.TimedOut:
                    return PortStatus.Timeout;

                case SocketError.HostUnreachable:
                case SocketError.NetworkUnreachable:
                case SocketError.HostDown:
                case SocketError.NetworkDown:
                    return PortStatus.Unreachable;

                case SocketError.ConnectionReset:
                    return PortStatus.Refused;

                default:
                    return PortStatus.Timeout;
            }
        }

        /// <summary>
        /// 单点握手测通（供 Tab 2 的测通器使用）。
        /// 同步阻塞，调用方须在后台线程执行。
        /// </summary>
        public static PortProbeResult ProbeSingle(uint ipAddress, int port, int timeoutMs)
        {
            List<PortProbeResult> list = ProbePorts(
                ipAddress, new int[] { port }, timeoutMs, null);

            if (list.Count == 0)
            {
                return new PortProbeResult(port, PortStatus.Unknown, 0);
            }

            return list[0];
        }
    }
}
