using System;
using System.Collections.Generic;
using System.Threading;
using MQNetScan.Engine.Contracts;

namespace MQNetScan.Engine
{
    /// <summary>
    /// 结果队列：引擎线程入队，UI 线程批量取走。
    ///
    /// 设计要点（architecture §3.6）：
    ///   · 用简单加锁队列，不用跨线程控件调用；
    ///   · UI 通过 Timer 每 200ms 批量取走，消息数量与扫描速度彻底解耦。
    /// </summary>
    internal sealed class ResultQueue
    {
        private readonly object _lock = new object();
        private readonly Queue<HostRecord> _results = new Queue<HostRecord>();
        private readonly Queue<AlertItem> _alerts = new Queue<AlertItem>();

        /// <summary>待提交结果数量。</summary>
        public int ResultCount
        {
            get { lock (_lock) { return _results.Count; } }
        }

        /// <summary>待提交告警数量。</summary>
        public int AlertCount
        {
            get { lock (_lock) { return _alerts.Count; } }
        }

        /// <summary>入队一台主机。</summary>
        public void Enqueue(HostRecord record)
        {
            if (record == null) { return; }

            lock (_lock)
            {
                _results.Enqueue(record);
                Monitor.Pulse(_lock);
            }
        }

        /// <summary>入队一条告警。</summary>
        public void EnqueueAlert(AlertItem alert)
        {
            if (alert == null) { return; }

            lock (_lock)
            {
                _alerts.Enqueue(alert);
                Monitor.Pulse(_lock);
            }
        }

        /// <summary>
        /// 批量取走结果，最多 maxCount 条。
        /// maxCount &lt;= 0 表示不限制。
        /// </summary>
        public List<HostRecord> TakeResults(int maxCount)
        {
            lock (_lock)
            {
                if (_results.Count == 0) { return null; }

                int n = (maxCount <= 0 || maxCount > _results.Count) ? _results.Count : maxCount;
                List<HostRecord> list = new List<HostRecord>(n);

                for (int i = 0; i < n; i++)
                {
                    list.Add(_results.Dequeue());
                }

                return list;
            }
        }

        /// <summary>
        /// 批量取走全部告警。
        /// </summary>
        public List<AlertItem> TakeAlerts()
        {
            lock (_lock)
            {
                if (_alerts.Count == 0) { return null; }

                List<AlertItem> list = new List<AlertItem>(_alerts.Count);
                while (_alerts.Count > 0)
                {
                    list.Add(_alerts.Dequeue());
                }

                return list;
            }
        }

        /// <summary>清空全部待提交内容（会话终止时用于丢弃残留）。</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _results.Clear();
                _alerts.Clear();
            }
        }
    }
}
