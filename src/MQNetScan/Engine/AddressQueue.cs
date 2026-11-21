using System;
using System.Collections.Generic;
using System.Threading;

namespace MQNetScan.Engine
{
    /// <summary>
    /// 待探测地址队列（自建阻塞队列）。
    ///
    /// 设计要点（architecture §3.1）：
    ///   · 使用 Monitor.Wait/Pulse 而非线程池，保证并发度完全可控；
    ///   · 暂停/终止时调用 WakeAll，唤醒所有等待线程让它们自行检查状态；
    ///   · 取任务与归还任务均为线程安全。
    ///
    /// 不使用 BlockingCollection（其语义不易表达"暂停"），也不使用 Task。
    /// </summary>
    internal sealed class AddressQueue
    {
        private readonly object _lock = new object();
        private readonly Queue<uint> _items;
        private readonly int _totalCount;
        private int _takenCount;
        private bool _closed;

        public AddressQueue(IEnumerable<uint> addresses)
        {
            _items = new Queue<uint>();

            if (addresses != null)
            {
                foreach (uint a in addresses)
                {
                    _items.Enqueue(a);
                }
            }

            _totalCount = _items.Count;
        }

        /// <summary>初始地址总数。</summary>
        public int TotalCount
        {
            get { return _totalCount; }
        }

        /// <summary>剩余待取数量。</summary>
        public int RemainingCount
        {
            get
            {
                lock (_lock)
                {
                    return _items.Count;
                }
            }
        }

        /// <summary>已被取走的数量。</summary>
        public int TakenCount
        {
            get
            {
                lock (_lock)
                {
                    return _takenCount;
                }
            }
        }

        /// <summary>队列是否已关闭。</summary>
        public bool IsClosed
        {
            get
            {
                lock (_lock)
                {
                    return _closed;
                }
            }
        }

        /// <summary>
        /// 取一个地址。
        /// 队列为空且未关闭时阻塞等待；关闭后立即返回 false。
        /// </summary>
        /// <param name="shouldContinue">
        /// 每次被唤醒时调用，返回 false 表示调用方希望退出（已终止）。
        /// 这样状态机判断与队列等待能统一在一个循环里。
        /// </param>
        /// <param name="address">取到的地址。</param>
        /// <returns>true 表示取到地址；false 表示应退出。</returns>
        public bool TryTake(Func<bool> shouldContinue, out uint address)
        {
            address = 0;

            lock (_lock)
            {
                while (true)
                {
                    if (_closed) { return false; }

                    // 调用方主动要求退出（已终止）
                    if (shouldContinue != null && !shouldContinue()) { return false; }

                    if (_items.Count > 0)
                    {
                        address = _items.Dequeue();
                        _takenCount++;
                        return true;
                    }

                    // 队列空：等待生产者或 WakeAll
                    Monitor.Wait(_lock);
                }
            }
        }

        /// <summary>
        /// 带空队列回调的取任务。
        ///
        /// 解决的关键问题：若队列已被其它线程取空，阻塞等待的线程永远不会自行醒来，
        /// 导致"最后一个地址处理完后状态停在 Running 而不是 Completed"。
        ///
        /// 【重要】onEmpty 必须在**释放本队列锁之后**调用。
        /// 否则会与 ScanSession.TryComplete 形成 ABBA 死锁：
        ///   线程 A：持有 _lock → 调用 onEmpty → 想拿 _completeLock
        ///   线程 B：持有 _completeLock → 读 RemainingCount → 想拿 _lock
        /// </summary>
        /// <param name="shouldContinue">返回 false 表示应退出。</param>
        /// <param name="onEmpty">队列为空时调用（**不持锁**）；返回 true 表示应退出。</param>
        /// <param name="waitMs">单次等待时长（毫秒）。</param>
        /// <param name="address">取到的地址。</param>
        public bool TryTakeWithIdle(Func<bool> shouldContinue, Func<bool> onEmpty, int waitMs, out uint address)
        {
            address = 0;

            while (true)
            {
                bool shouldCallEmpty = false;

                lock (_lock)
                {
                    if (_closed) { return false; }

                    if (shouldContinue != null && !shouldContinue()) { return false; }

                    if (_items.Count > 0)
                    {
                        address = _items.Dequeue();
                        _takenCount++;
                        return true;
                    }

                    // 队列为空：标记需要在锁外做完成判定
                    shouldCallEmpty = true;
                }

                // —— 此处已释放 _lock ——
                if (shouldCallEmpty && onEmpty != null)
                {
                    if (onEmpty()) { return false; }
                }

                // 短暂等待后重新检查（避免忙等，又能及时响应状态变化）
                lock (_lock)
                {
                    if (_closed) { return false; }

                    if (_items.Count == 0)
                    {
                        if (waitMs > 0) { Monitor.Wait(_lock, waitMs); }
                        else { Monitor.Wait(_lock); }
                    }
                }
            }
        }

        /// <summary>
        /// 把已取出的地址放回队首。
        /// 用于"取到任务后才发现已暂停"的场景，避免地址丢失导致扫描不完整。
        /// </summary>
        public void PushFront(uint address)
        {
            lock (_lock)
            {
                // Queue 不支持头插，重建一次（规模最多几千，成本可接受）
                uint[] rest = _items.ToArray();
                _items.Clear();
                _items.Enqueue(address);

                for (int i = 0; i < rest.Length; i++)
                {
                    _items.Enqueue(rest[i]);
                }

                Monitor.Pulse(_lock);
            }
        }

        /// <summary>
        /// 唤醒所有等待中的线程，让它们重新检查状态。
        /// 暂停、继续、终止都必须调用，否则线程会一直卡在 Wait 上。
        /// </summary>
        public void WakeAll()
        {
            lock (_lock)
            {
                Monitor.PulseAll(_lock);
            }
        }

        /// <summary>
        /// 关闭队列：所有等待线程立即返回 false。
        /// </summary>
        public void Close()
        {
            lock (_lock)
            {
                _closed = true;
                Monitor.PulseAll(_lock);
            }
        }
    }
}
