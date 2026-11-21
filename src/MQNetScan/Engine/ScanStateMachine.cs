using System;
using MQNetScan.Engine.Contracts;

namespace MQNetScan.Engine
{
    /// <summary>
    /// 扫描状态机。
    /// 状态迁移的**唯一入口**（architecture §3.5）；UI 与工作线程均不得直接改状态。
    ///
    /// 合法迁移：
    ///   Idle      → Running            （开始扫描）
    ///   Running   → Paused             （暂停）
    ///   Paused    → Running            （继续）
    ///   Running   → Stopped            （终止）
    ///   Paused    → Stopped            （终止）
    ///   Running   → Completed          （全部地址处理完）
    ///   Stopped/Completed → Idle       （新建会话，非本实例行为）
    ///
    /// 非法迁移（抛 InvalidOperationException）：
    ///   Completed → Paused
    ///   Stopped   → Running
    ///   Idle      → Paused
    ///   等一切未列出的组合。
    /// </summary>
    internal sealed class ScanStateMachine
    {
        private readonly object _lock = new object();
        private volatile ScanState _state = ScanState.Idle;

        /// <summary>当前状态。</summary>
        public ScanState State
        {
            get { return _state; }
        }

        /// <summary>是否处于活动状态（Running 或 Paused）。</summary>
        public bool IsActive
        {
            get
            {
                ScanState s = _state;
                return s == ScanState.Running || s == ScanState.Paused;
            }
        }

        /// <summary>是否应继续处理任务（仅 Running 为 true）。</summary>
        public bool ShouldWork
        {
            get { return _state == ScanState.Running; }
        }

        /// <summary>是否应退出工作循环（Stopped 或 Completed）。</summary>
        public bool ShouldExit
        {
            get
            {
                ScanState s = _state;
                return s == ScanState.Stopped || s == ScanState.Completed;
            }
        }

        /// <summary>
        /// 状态迁移。非法迁移抛异常，便于尽早暴露逻辑错误。
        /// </summary>
        public void Transition(ScanState target)
        {
            lock (_lock)
            {
                ScanState from = _state;

                if (from == target) { return; }   // 幂等

                if (!IsLegalTransition(from, target))
                {
                    throw new InvalidOperationException(
                        "非法的状态迁移：" + from + " → " + target);
                }

                _state = target;
            }
        }

        /// <summary>
        /// 尝试迁移，非法时返回 false 而不抛异常（供 UI 容错路径使用）。
        /// </summary>
        public bool TryTransition(ScanState target)
        {
            try
            {
                Transition(target);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>
        /// 判断迁移是否合法（纯函数，便于单测）。
        /// </summary>
        public static bool IsLegalTransition(ScanState from, ScanState to)
        {
            switch (from)
            {
                case ScanState.Idle:
                    return to == ScanState.Running;

                case ScanState.Running:
                    return to == ScanState.Paused
                        || to == ScanState.Stopped
                        || to == ScanState.Completed;

                case ScanState.Paused:
                    return to == ScanState.Running
                        || to == ScanState.Stopped;

                case ScanState.Stopped:
                    return false;   // 终止为终态

                case ScanState.Completed:
                    return false;   // 完成也为终态（新扫描应新建会话）

                default:
                    return false;
            }
        }
    }
}
