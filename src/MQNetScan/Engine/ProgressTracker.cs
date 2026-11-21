using System;
using System.Diagnostics;
using System.Threading;
using MQNetScan.Engine.Contracts;

namespace MQNetScan.Engine
{
    /// <summary>
    /// 进度追踪器。
    ///
    /// 设计要点：
    ///   · 计数用 Interlocked，避免每次加锁；
    ///   · 耗时统计需扣除暂停时间，否则"暂停 10 分钟"会让耗时数字失去意义；
    ///   · Snapshot() 返回不可变快照，UI 侧无需同步。
    /// </summary>
    internal sealed class ProgressTracker
    {
        private readonly object _lock = new object();
        private readonly int _total;

        private int _done;
        private int _aliveCount;

        private readonly Stopwatch _activeWatch = new Stopwatch();
        private long _accumulatedMs;      // 之前各活跃区间的累计
        private bool _timingActive;

        private bool _truncated;

        public ProgressTracker(int total)
        {
            _total = total < 0 ? 0 : total;
        }

        /// <summary>目标总数。</summary>
        public int Total
        {
            get { return _total; }
        }

        /// <summary>已完成数量。</summary>
        public int Done
        {
            get { return Interlocked.CompareExchange(ref _done, 0, 0); }
        }

        /// <summary>存活主机数量。</summary>
        public int AliveCount
        {
            get { return Interlocked.CompareExchange(ref _aliveCount, 0, 0); }
        }

        /// <summary>结果是否被截断。</summary>
        public bool Truncated
        {
            get { lock (_lock) { return _truncated; } }
        }

        /// <summary>标记一个地址处理完毕。</summary>
        public void IncrementDone()
        {
            Interlocked.Increment(ref _done);
        }

        /// <summary>标记发现一台存活主机。</summary>
        public void IncrementAlive()
        {
            Interlocked.Increment(ref _aliveCount);
        }

        /// <summary>标记结果被截断。</summary>
        public void MarkTruncated()
        {
            lock (_lock)
            {
                _truncated = true;
            }
        }

        // -------------------------------------------------------------------
        // 计时：暂停时停止累计，继续时恢复
        // -------------------------------------------------------------------
        /// <summary>开始/恢复计时。</summary>
        public void StartTiming()
        {
            lock (_lock)
            {
                if (_timingActive) { return; }

                _timingActive = true;
                _activeWatch.Start();
            }
        }

        /// <summary>暂停计时。</summary>
        public void PauseTiming()
        {
            lock (_lock)
            {
                if (!_timingActive) { return; }

                _activeWatch.Stop();
                _accumulatedMs += _activeWatch.ElapsedMilliseconds;
                _activeWatch.Reset();
                _timingActive = false;
            }
        }

        /// <summary>停止计时（终止/完成）。</summary>
        public void StopTiming()
        {
            PauseTiming();
        }

        /// <summary>当前有效耗时（毫秒，已扣除暂停区间）。</summary>
        public long ElapsedMs
        {
            get
            {
                lock (_lock)
                {
                    long ms = _accumulatedMs;

                    if (_timingActive)
                    {
                        ms += _activeWatch.ElapsedMilliseconds;
                    }

                    return ms < 0 ? 0 : ms;
                }
            }
        }

        /// <summary>
        /// 生成进度快照。
        /// </summary>
        public ScanProgress Snapshot(ScanState state)
        {
            int done = Interlocked.CompareExchange(ref _done, 0, 0);
            int alive = Interlocked.CompareExchange(ref _aliveCount, 0, 0);

            bool truncated;
            long elapsed;

            lock (_lock)
            {
                truncated = _truncated;

                elapsed = _accumulatedMs;
                if (_timingActive)
                {
                    elapsed += _activeWatch.ElapsedMilliseconds;
                }
            }

            return new ScanProgress(state, _total, done, alive, elapsed, truncated);
        }
    }
}
