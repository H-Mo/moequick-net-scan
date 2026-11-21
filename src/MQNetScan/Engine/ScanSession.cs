using System;
using System.Collections.Generic;
using System.Threading;
using MQNetScan.Engine.Contracts;

namespace MQNetScan.Engine
{
    /// <summary>
    /// 探测器接口。
    /// S4 阶段用假实现（FakeProber）驱动调度逻辑；S5/S7 替换为真实 ARP + 端口探测。
    /// </summary>
    internal interface IHostProber
    {
        /// <summary>
        /// 探测单个地址。
        /// 返回 null 表示该地址无响应（不入列表，符合 PRD FR-1.2.2）。
        /// </summary>
        /// <param name="address">目标地址。</param>
        /// <param name="parameters">本次扫描的参数快照。</param>
        /// <param name="shouldContinue">返回 false 表示应尽快放弃当前探测。</param>
        HostRecord Probe(uint address, ScanParameters parameters, Func<bool> shouldContinue);
    }

    /// <summary>
    /// 一次扫描的会话实例。
    ///
    /// 核心机制（architecture §3.1）：
    ///   · 固定数量工作线程 + 自建阻塞队列，不使用线程池；
    ///   · 暂停/继续/终止全部通过 ScanStateMachine + AddressQueue.WakeAll 实现；
    ///   · 工作线程外层 try/catch 全包裹，单个地址探测失败不影响整轮扫描；
    ///   · 所有 socket/资源的所有权限定在创建它的线程内。
    ///
    /// 生命周期：每次"开始扫描"创建一个新实例，终止或完成后丢弃。
    /// </summary>
    internal sealed class ScanSession : IScanController, IProgressSource
    {
        /// <summary>终止时等待线程退出的上限（architecture §3.7）。</summary>
        public const int StopJoinTimeoutMs = 2000;

        private readonly ScanParameters _parameters;
        private readonly IResultSink _sink;
        private readonly IHostProber _prober;
        private readonly ScanStateMachine _stateMachine = new ScanStateMachine();
        private readonly AddressQueue _queue;
        private readonly ProgressTracker _progress;
        private readonly ResultQueue _resultQueue;

        private Thread[] _workers;
        private int _workerCount;
        private volatile bool _started;

        /// <summary>
        /// 仍在运行的工作线程数。
        /// 注意：.NET 4.0 没有 Volatile.Read/Write（4.5 才引入），
        /// 因此使用 volatile 字段 + Interlocked 保证可见性与原子性。
        /// </summary>
        private volatile int _activeWorkers;

        public ScanSession(ScanParameters parameters, IResultSink sink, IHostProber prober)
        {
            if (parameters == null) { throw new ArgumentNullException("parameters"); }
            if (prober == null) { throw new ArgumentNullException("prober"); }

            _parameters = parameters;
            _sink = sink;
            _prober = prober;

            _queue = new AddressQueue(parameters.Targets);
            _progress = new ProgressTracker(parameters.TargetCount);
            _resultQueue = new ResultQueue();
        }

        // -------------------------------------------------------------------
        // 供 UI 读取
        // -------------------------------------------------------------------
        /// <summary>参数快照。</summary>
        public ScanParameters Parameters { get { return _parameters; } }

        /// <summary>当前状态。</summary>
        public ScanState State { get { return _stateMachine.State; } }

        /// <summary>是否活动。</summary>
        public bool IsActive { get { return _stateMachine.IsActive; } }

        /// <summary>实际启动的工作线程数。</summary>
        public int WorkerCount { get { return _workerCount; } }

        /// <summary>仍在运行的工作线程数。</summary>
        public int ActiveWorkers { get { return _activeWorkers; } }

        /// <summary>结果队列（UI 侧通过 TakeResults 拉取）。</summary>
        public ResultQueue Results { get { return _resultQueue; } }

        // -------------------------------------------------------------------
        // IScanController
        // -------------------------------------------------------------------
        /// <summary>
        /// 开始扫描：启动 N 个工作线程并进入 Running。
        /// </summary>
        public void Start()
        {
            if (_started) { throw new InvalidOperationException("会话已启动，不能重复启动"); }

            _stateMachine.Transition(ScanState.Running);
            _progress.StartTiming();
            _started = true;

            // 线程数取参数值；此处不再二次限制（参数已由 SettingsService 校验）
            _workerCount = _parameters.Concurrency;

            if (_workerCount < 1) { _workerCount = 1; }
            if (_workerCount > _parameters.TargetCount && _parameters.TargetCount > 0)
            {
                // 目标比线程还少时，不必起多余线程
                _workerCount = _parameters.TargetCount;
            }

            _activeWorkers = _workerCount;

            _workers = new Thread[_workerCount];

            for (int i = 0; i < _workerCount; i++)
            {
                // IsBackground = true：关闭程序时不阻塞进程退出
                Thread t = new Thread(WorkerLoop);
                t.IsBackground = true;
                t.Name = "MQNetScan-Worker-" + i;
                _workers[i] = t;
                t.Start();
            }
        }

        /// <summary>
        /// 暂停：停止取新任务，唤醒等待线程让它们进入等待。
        /// 在途探测会自然结束（受单地址预算限制）。
        ///
        /// 容错：若已处于 Stopped/Completed 终态，暂停无意义，直接忽略。
        /// （UI 定时器与用户操作存在时序竞争，暂停请求可能晚于完成事件到达。）
        /// </summary>
        public void Pause()
        {
            if (_stateMachine.State == ScanState.Stopped || _stateMachine.State == ScanState.Completed)
            {
                return;
            }

            _stateMachine.Transition(ScanState.Paused);
            _progress.PauseTiming();
            _queue.WakeAll();
        }

        /// <summary>
        /// 继续：恢复取任务。
        ///
        /// 容错：终态下继续无意义，直接忽略。
        /// </summary>
        public void Resume()
        {
            if (_stateMachine.State == ScanState.Stopped || _stateMachine.State == ScanState.Completed)
            {
                return;
            }

            _stateMachine.Transition(ScanState.Running);
            _progress.StartTiming();
            _queue.WakeAll();
        }

        /// <summary>
        /// 终止：唤醒全部线程，等待退出（上限 2 秒），保留已产生的结果。
        /// </summary>
        public void Stop()
        {
            // 已完成或已终止则无需再处理
            if (_stateMachine.State == ScanState.Stopped || _stateMachine.State == ScanState.Completed)
            {
                return;
            }

            _stateMachine.Transition(ScanState.Stopped);
            _progress.StopTiming();

            // 先关闭队列，让所有 TryTake 立即返回 false
            _queue.Close();
            _queue.WakeAll();

            JoinWorkers(StopJoinTimeoutMs);
        }

        /// <summary>
        /// 等待工作线程退出。超时则放弃（线程为后台线程，不阻塞进程退出）。
        /// </summary>
        private void JoinWorkers(int timeoutMs)
        {
            Thread[] workers = _workers;
            if (workers == null) { return; }

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            for (int i = 0; i < workers.Length; i++)
            {
                Thread t = workers[i];
                if (t == null) { continue; }
                if (!t.IsAlive) { continue; }

                int remain = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remain < 1) { remain = 1; }

                try
                {
                    t.Join(remain);
                }
                catch (Exception)
                {
                    // Join 失败不应抛给调用方
                }
            }
        }

        /// <summary>
        /// 等待扫描自然完成（供测试与"完成"判定使用）。
        /// </summary>
        public bool WaitForCompletion(int timeoutMs)
        {
            Thread[] workers = _workers;

            if (workers == null)
            {
                return _stateMachine.State == ScanState.Completed;
            }

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            for (int i = 0; i < workers.Length; i++)
            {
                Thread t = workers[i];
                if (t == null) { continue; }

                int remain = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remain < 1) { return false; }

                try
                {
                    if (!t.Join(remain)) { return false; }
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return true;
        }

        // -------------------------------------------------------------------
        // 工作线程主循环
        // -------------------------------------------------------------------
        private void WorkerLoop()
        {
            try
            {
                while (true)
                {
                    // 1) 已终止/已完成 → 退出
                    if (_stateMachine.ShouldExit) { return; }

                    // 2) 暂停中 → 等待唤醒（WakeAll 会在暂停/继续/终止时被调用）
                    if (_stateMachine.State == ScanState.Paused)
                    {
                        WaitWhilePaused();
                        continue;
                    }

                    // 3) 取任务
                    // 使用带空队列回调的取任务：
                    // 队列被取空时先做完成判定，避免本线程永久阻塞在 Wait 上。
                    uint address;

                    bool got = _queue.TryTakeWithIdle(
                        delegate
                        {
                            // 只有 Running 才继续取任务；Paused 时返回 false 让它跳出等待
                            return _stateMachine.ShouldWork;
                        },
                        delegate
                        {
                            // 队列为空时的完成判定：若全部处理完则返回 true 让本线程退出
                            TryComplete();
                            return _stateMachine.ShouldExit;
                        },
                        50,
                        out address);

                    if (!got)
                    {
                        // 队列空且应退出，或状态不再是 Running
                        if (_stateMachine.ShouldExit) { return; }

                        // 暂停导致取不到：回到循环顶部等待
                        continue;
                    }

                    // 4) 取到任务后再确认一次状态，避免暂停瞬间丢任务
                    if (!_stateMachine.ShouldWork)
                    {
                        _queue.PushFront(address);   // 放回队首，保证不漏扫

                        if (_stateMachine.ShouldExit) { return; }

                        WaitWhilePaused();
                        continue;
                    }

                    // 5) 探测
                    HostRecord record = null;

                    try
                    {
                        record = _prober.Probe(address, _parameters, delegate
                        {
                            return _stateMachine.ShouldWork;
                        });
                    }
                    catch (Exception)
                    {
                        // 单个地址失败不能拖垮整轮扫描
                        record = null;
                    }
                    finally
                    {
                        _progress.IncrementDone();
                    }

                    // 6) 结果入队
                    if (record != null)
                    {
                        _progress.IncrementAlive();

                        try
                        {
                            if (_sink != null) { _sink.Post(record); }
                            else { _resultQueue.Enqueue(record); }
                        }
                        catch (Exception)
                        {
                            // 投递失败不影响后续探测
                        }
                    }

                    // 7) 队列空 → 标记完成
                    if (_queue.RemainingCount == 0)
                    {
                        TryComplete();
                    }
                }
            }
            catch (Exception)
            {
                // 工作线程最外层兜底：任何异常都不得导致线程静默死亡
            }
            finally
            {
                Interlocked.Decrement(ref _activeWorkers);
            }
        }

        /// <summary>
        /// 在暂停状态下等待，直到状态变为 Running 或退出。
        /// 用短周期轮询等待，避免依赖 Pulse 的时序（老机器上更稳妥）。
        /// </summary>
        private void WaitWhilePaused()
        {
            while (_stateMachine.State == ScanState.Paused)
            {
                _queue.WakeAll();              // 让其它等待线程也能及时响应状态变化
                Thread.Sleep(20);
            }
        }

        /// <summary>
        /// 尝试把状态置为 Completed。
        /// 用锁保证只有一个线程能完成迁移。
        /// </summary>
        private readonly object _completeLock = new object();

        private void TryComplete()
        {
            lock (_completeLock)
            {
                if (_stateMachine.State != ScanState.Running) { return; }

                // 注意：以下两次读取都会获取队列内部锁。
                // 调用方必须**不持有队列锁**，否则形成 ABBA 死锁。
                int remaining = _queue.RemainingCount;
                if (remaining > 0) { return; }

                // 队列空但仍有地址"在途"（已被取走、尚未处理完）→ 未完成。
                // 判据：已取走数量已全部计入 Done 才算真正结束。
                // 否则最后一个地址仍在别的线程手里，此时置 Completed 会导致结果丢失。
                int taken = _queue.TakenCount;
                int done = _progress.Done;
                if (taken > done) { return; }

                _stateMachine.TryTransition(ScanState.Completed);
                _progress.StopTiming();
                _queue.WakeAll();
            }
        }

        // -------------------------------------------------------------------
        // IProgressSource
        // -------------------------------------------------------------------
        /// <summary>取进度快照。</summary>
        public ScanProgress Snapshot()
        {
            return _progress.Snapshot(_stateMachine.State);
        }
    }

    /// <summary>
    /// S4 阶段的假探测器。
    /// 不进行任何网络操作，仅按随机间隔返回伪造的主机记录，
    /// 用于在无网络依赖的前提下验证并发调度与暂停/终止语义。
    /// S5 阶段将被真实的 ARP 探测器替换。
    /// </summary>
    internal sealed class FakeHostProber : IHostProber
    {
        private readonly int _minDelayMs;
        private readonly int _maxDelayMs;
        private readonly double _aliveRatio;
        private readonly Random _random = new Random();
        private readonly object _lock = new object();

        public FakeHostProber()
            : this(20, 80, 0.5)
        {
        }

        /// <param name="minDelayMs">最小模拟耗时。</param>
        /// <param name="maxDelayMs">最大模拟耗时。</param>
        /// <param name="aliveRatio">判定为存活的比例（0~1）。</param>
        public FakeHostProber(int minDelayMs, int maxDelayMs, double aliveRatio)
        {
            _minDelayMs = minDelayMs < 0 ? 0 : minDelayMs;
            _maxDelayMs = maxDelayMs < _minDelayMs ? _minDelayMs : maxDelayMs;
            _aliveRatio = aliveRatio;
        }

        /// <summary>每 10 个地址返回一个 MAC，其余视为无响应。</summary>
        public HostRecord Probe(uint address, ScanParameters parameters, Func<bool> shouldContinue)
        {
            int delay;

            lock (_lock)
            {
                delay = _random.Next(_minDelayMs, _maxDelayMs + 1);
            }

            // 分段睡眠，便于在终止时快速响应
            int slept = 0;
            while (slept < delay)
            {
                if (shouldContinue != null && !shouldContinue()) { return null; }

                int step = 10;
                if (slept + step > delay) { step = delay - slept; }

                Thread.Sleep(step);
                slept += step;
            }

            lock (_lock)
            {
                if (_random.NextDouble() > _aliveRatio) { return null; }
            }

            // 伪造 MAC：用地址的低 3 字节拼一个看起来合理的 OUI
            byte[] mac = new byte[6];
            mac[0] = 0x52;
            mac[1] = 0x54;
            mac[2] = 0x00;
            mac[3] = (byte)((address >> 16) & 0xFF);
            mac[4] = (byte)((address >> 8) & 0xFF);
            mac[5] = (byte)(address & 0xFF);

            string macText = string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
                mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);

            // 伪造开放端口：按地址哈希取一部分
            int[] ports = BuildFakePorts(address, parameters);

            HostRecord record = new HostRecord(address, macText, ports);
            return record;
        }

        private static int[] BuildFakePorts(uint address, ScanParameters parameters)
        {
            if (parameters == null || parameters.EnabledPorts.Length == 0)
            {
                return new int[0];
            }

            List<int> open = new List<int>();
            uint hash = (address * 2654435761u) >> 8;

            for (int i = 0; i < parameters.EnabledPorts.Length; i++)
            {
                if (((hash >> (i % 16)) & 1) == 1)
                {
                    open.Add(parameters.EnabledPorts[i]);
                }
            }

            return open.ToArray();
        }
    }
}
