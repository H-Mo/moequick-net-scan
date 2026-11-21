using MQNetScan.Engine.Contracts;

namespace MQNetScan.Engine.Contracts
{
    /// <summary>
    /// 引擎向 UI 投递结果的接口。
    /// 实现方只负责入队，不得在引擎线程里碰任何控件（architecture §5.2）。
    /// </summary>
    internal interface IResultSink
    {
        /// <summary>投递一台存活主机。</summary>
        void Post(HostRecord record);

        /// <summary>投递一条告警。</summary>
        void PostAlert(AlertItem alert);
    }

    /// <summary>
    /// UI 读取引擎进度的接口（只读快照）。
    /// </summary>
    internal interface IProgressSource
    {
        /// <summary>取当前进度快照。</summary>
        ScanProgress Snapshot();
    }

    /// <summary>
    /// 扫描控制接口（UI → 引擎的唯一控制面）。
    /// </summary>
    internal interface IScanController
    {
        /// <summary>开始扫描。</summary>
        void Start();

        /// <summary>暂停。</summary>
        void Pause();

        /// <summary>继续。</summary>
        void Resume();

        /// <summary>终止。</summary>
        void Stop();

        /// <summary>当前是否处于活动状态（Running 或 Paused）。</summary>
        bool IsActive { get; }
    }
}
