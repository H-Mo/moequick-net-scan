using System;
using System.Collections.Generic;
using System.Windows.Forms;
using MQNetScan.Engine.Contracts;

namespace MQNetScan.Presentation.Common
{
    /// <summary>
    /// UI 刷新器：把引擎产生的结果批量投递到 ListView。
    ///
    /// 为什么需要这一层（architecture §3.6）：
    ///   若引擎每发现一台设备就通过 BeginInvoke 更新控件，
    ///   高并发下会向 UI 消息队列灌入大量消息，导致消息泵拥堵、界面掉帧甚至假死。
    ///   改用 Timer 定时批量取走，可把"消息数量"与"扫描速度"彻底解耦：
    ///   无论引擎多快，UI 每 200ms 只处理 1 次。
    ///
    /// 使用约定：
    ///   · 只由 UI 线程创建与调用；
    ///   · 不绑定任何 DoubleClick 事件（PRD FR-1.6.1 硬约束）。
    /// </summary>
    internal sealed class UiDispatcher : IDisposable
    {
        /// <summary>刷新周期（毫秒）。</summary>
        public const int RefreshIntervalMs = 200;

        /// <summary>单次 Tick 最多插入的行数，避免单次过长导致掉帧。</summary>
        public const int MaxRowsPerTick = 200;

        /// <summary>结果行数上限，超过后只统计不显示（PRD NFR-S2）。</summary>
        public const int MaxDisplayRows = 5000;

        private readonly ListView _listView;
        private readonly Timer _timer;
        private readonly List<HostRecord> _pending = new List<HostRecord>();

        private int _displayedRows;
        private int _droppedRows;
        private bool _disposed;

        /// <summary>每完成一次批量刷新后触发（用于更新状态栏）。</summary>
        public event EventHandler Flushed;

        public UiDispatcher(ListView listView)
        {
            if (listView == null) { throw new ArgumentNullException("listView"); }

            _listView = listView;

            ListViewHelper.ConfigureColumns(_listView);

            _timer = new Timer();
            _timer.Interval = RefreshIntervalMs;
            _timer.Tick += OnTick;
        }

        /// <summary>已显示的行数。</summary>
        public int DisplayedRows { get { return _displayedRows; } }

        /// <summary>因超出上限而未显示的行数。</summary>
        public int DroppedRows { get { return _droppedRows; } }

        /// <summary>是否发生了截断。</summary>
        public bool IsTruncated { get { return _droppedRows > 0; } }

        /// <summary>开始（或恢复）定时刷新。</summary>
        public void Start()
        {
            if (_disposed) { return; }
            _timer.Start();
        }

        /// <summary>暂停定时刷新（已入队的结果保留，继续后会补上）。</summary>
        public void Stop()
        {
            if (_disposed) { return; }
            _timer.Stop();
        }

        /// <summary>
        /// 投递一条结果。只入队，不触发重绘。
        /// </summary>
        public void Post(HostRecord record)
        {
            if (record == null) { return; }
            _pending.Add(record);
        }

        /// <summary>
        /// 立即执行一次刷新（不等待 Timer）。
        /// 用于扫描结束后把剩余结果尽快提交。
        /// </summary>
        public int FlushNow()
        {
            return FlushOnce();
        }

        private void OnTick(object sender, EventArgs e)
        {
            FlushOnce();
        }

        /// <summary>
        /// 执行一次批量刷新。返回本次插入的行数。
        /// </summary>
        private int FlushOnce()
        {
            if (_disposed) { return 0; }

            int inserted = 0;

            if (_pending.Count > 0)
            {
                _listView.BeginUpdate();

                try
                {
                    int take = _pending.Count;

                    if (take > MaxRowsPerTick) { take = MaxRowsPerTick; }

                    for (int i = 0; i < take; i++)
                    {
                        HostRecord r = _pending[i];

                        // 行数上限保护：超出后仍继续消费队列，但不加入列表
                        if (_displayedRows >= MaxDisplayRows)
                        {
                            _droppedRows++;
                            continue;
                        }

                        _displayedRows++;

                        ListViewItem item = ListViewHelper.BuildItem(r, _displayedRows);
                        _listView.Items.Add(item);
                    }

                    _pending.RemoveRange(0, take);
                    inserted = take;
                }
                finally
                {
                    _listView.EndUpdate();
                }
            }

            if (Flushed != null && (inserted > 0 || _pending.Count == 0))
            {
                Flushed(this, EventArgs.Empty);
            }

            return inserted;
        }

        /// <summary>
        /// 清空列表与待提交队列（新一轮扫描前调用）。
        /// </summary>
        public void Clear()
        {
            _listView.BeginUpdate();

            try
            {
                _listView.Items.Clear();
            }
            finally
            {
                _listView.EndUpdate();
            }

            _pending.Clear();
            _displayedRows = 0;
            _droppedRows = 0;
        }

        /// <summary>
        /// 对当前列表排序。
        /// </summary>
        public void Sort(int column, bool ascending)
        {
            ListViewHelper.Sort(_listView, column, ascending);
        }

        public void Dispose()
        {
            if (_disposed) { return; }

            _disposed = true;

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTick;
                _timer.Dispose();
            }

            _pending.Clear();
        }
    }
}
