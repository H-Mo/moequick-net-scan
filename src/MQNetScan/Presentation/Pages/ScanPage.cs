using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using MQNetScan.Application;
using MQNetScan.Engine;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;
using MQNetScan.Presentation.Common;

namespace MQNetScan.Presentation.Pages
{
    /// <summary>
    /// Tab 1：局域网探测。
    /// S4 阶段接入真实调度引擎（仍用假探测器）；S5 起替换为真实 ARP 探测。
    /// </summary>
    internal partial class ScanPage : UserControl
    {
        /// <summary>UI 刷新周期（architecture §3.6：200ms 批量提交）。</summary>
        private const int UiRefreshIntervalMs = 200;

        /// <summary>单次刷新最多插入的行数，避免单次 Tick 过长导致掉帧。</summary>
        private const int MaxRowsPerTick = 200;

        private List<AdapterInfo> _adapters = new List<AdapterInfo>();
        private NetSegment _currentSegment;
        private string _lastNetSegment = "";
        private bool _suppressEvents;

        /// <summary>
        /// 网卡层的持续警示（如兜底模式、无网卡）。
        /// 与输入校验分开存放，避免"校验通过就把网卡问题一起擦掉"。
        /// </summary>
        private string _adapterWarning = "";

        private bool _scanning;

        private ScanSession _session;
        private AppSettings _settings;
        private string _settingsPath = "";
        private Timer _refreshTimer;
        private UiDispatcher _dispatcher;
        private bool _sortColumnSet;
        private int _sortColumn = -1;
        private bool _sortAscending = true;

        /// <summary>
        /// 探测器工厂。默认返回假探测器（S4）；
        /// S5 接入真实 ARP 探测后改为返回 WalkProber。
        /// 测试可替换此委托以控制探测速度，从而稳定验证暂停/终止语义。
        /// </summary>
        internal Func<IHostProber> ProberFactory;

        /// <summary>本次扫描中已被 UI 接收并显示的行数（用于上限保护）。</summary>
        private int _displayedRows;

        /// <summary>本次扫描的告警（供报告"异常告警"段使用）。</summary>
        private List<AlertItem> _alerts = new List<AlertItem>();

        /// <summary>最近一次导出的报告路径（供"打开所在文件夹"使用）。</summary>
        private string _lastReportPath = "";

        /// <summary>本次扫描的目标范围描述（供报告表头使用）。</summary>
        private string _currentTargetText = "";

        /// <summary>本次扫描的最终状态描述（供报告使用）。</summary>
        private string _finalStateText = "";
        private ProbeMode _currentProbeMode = ProbeMode.Arp;

        /// <summary>本次扫描的 IP 冲突检测器（供收尾时汇总告警）。</summary>
        private ConflictDetector _conflictDetector;
        private DeviceNameLookupQueue _deviceNameQueue;
        private readonly object _hostRecordLock = new object();
        private readonly Dictionary<uint, HostRecord> _hostRecords = new Dictionary<uint, HostRecord>();

        /// <summary>用户确认开始扫描时触发。参数为本次扫描的目标范围。</summary>
        public event EventHandler<ScanStartEventArgs> ScanStartRequested;

        /// <summary>用户请求单点测通（跳转 Tab 2）。</summary>
        public event EventHandler<SingleTestEventArgs> SingleTestRequested;

        /// <summary>产生告警时触发（供主窗体更新红色横幅）。</summary>
        public event EventHandler<AlertsChangedEventArgs> AlertsChanged;

        /// <summary>需要向主窗体转发的提示（状态栏）。</summary>
        public event EventHandler<MessageEventArgs> Notify;

        public ScanPage()
        {
            InitializeComponent();

            // 列表初始化与批量刷新器
            _dispatcher = new UiDispatcher(listHosts);

            HookListViewEvents();
            _currentProbeMode = ProbeMode.Arp;
            UpdateProbeModeHint();
            ApplyTargetModeControls();

            SetScanStatus("");
            SetProgress(0);
        }

        /// <summary>
        /// 绑定列表交互事件。
        ///
        /// 【刻意不绑定 DoubleClick / MouseDoubleClick】（PRD FR-1.6.1）：
        /// 没有绑定就不可能有双击副作用，这是架构级保证而非运行时判断。
        /// </summary>
        private void HookListViewEvents()
        {
            listHosts.ColumnClick += OnListViewColumnClick;
            listHosts.KeyDown += OnListViewKeyDown;

            // 右键菜单：只在右键时弹出（不响应左键、不响应双击）
            listHosts.MouseUp += OnListViewMouseUp;

            // Opening 事件里统一控制菜单项可用性（PRD FR-1.6.3）
            menuHosts.Opening += OnMenuOpening;
        }

        private void OnListViewMouseUp(object sender, MouseEventArgs e)
        {
            // 只有在列表项上右键才弹菜单；空白处右键也弹，但所有项灰化
            if (e.Button != MouseButtons.Right) { return; }

            ListViewHitTestInfo hit = listHosts.HitTest(e.Location);

            if (hit.Item == null)
            {
                // 空白处右键：清除选中，让菜单项全部灰化
                listHosts.SelectedItems.Clear();
            }
            else if (!hit.Item.Selected)
            {
                // 右键点在未选中的行上：先选中它
                listHosts.SelectedItems.Clear();
                hit.Item.Selected = true;
            }

            menuHosts.Show(listHosts, e.Location);
        }

        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            bool hasSelection = listHosts.SelectedItems.Count > 0;

            // 未选中任何行时禁用全部操作项（PRD FR-1.6.3）
            miCopyIp.Enabled = hasSelection;
            miCopyMac.Enabled = hasSelection;
            miCopyRow.Enabled = hasSelection;
            miDeepTest.Enabled = hasSelection;
            miOpenWeb.Enabled = hasSelection;
            miOpenRdp.Enabled = hasSelection;
            miSingleTest.Enabled = hasSelection;
        }

        // -------------------------------------------------------------------
        // 右键菜单动作
        // -------------------------------------------------------------------
        /// <summary>
        /// 取当前选中行对应的主机信息。
        /// </summary>
        private bool TryGetSelectedHost(out uint ipAddress, out string ipText,
            out string macText, out int[] openPorts, out string roleText)
        {
            ipAddress = 0;
            ipText = "";
            macText = "";
            openPorts = new int[0];
            roleText = "";

            if (listHosts.SelectedItems.Count == 0) { return false; }

            ListViewItem item = listHosts.SelectedItems[0];

            ipText = item.SubItems[ListViewHelper.ColIp].Text;
            macText = item.SubItems[ListViewHelper.ColMac].Text;
            roleText = item.SubItems[ListViewHelper.ColRole].Text;

            if (!Ipv4Util.TryParse(ipText, out ipAddress)) { return false; }

            openPorts = ParsePorts(item.SubItems[ListViewHelper.ColPorts].Text);

            return true;
        }

        private static int[] ParsePorts(string text)
        {
            if (string.IsNullOrEmpty(text)) { return new int[0]; }

            string[] parts = text.Split(',');
            List<int> result = new List<int>();

            for (int i = 0; i < parts.Length; i++)
            {
                int v;

                if (int.TryParse(parts[i].Trim(), out v) && v > 0 && v <= 65535)
                {
                    result.Add(v);
                }
            }

            return result.ToArray();
        }

        private void OnCopyIpClick(object sender, EventArgs e)
        {
            uint ip;
            string ipText, mac, role;
            int[] ports;

            if (!TryGetSelectedHost(out ip, out ipText, out mac, out ports, out role)) { return; }

            CopyToClipboard(ipText, "已复制 IP 地址：" + ipText);
        }

        private void OnCopyMacClick(object sender, EventArgs e)
        {
            uint ip;
            string ipText, mac, role;
            int[] ports;

            if (!TryGetSelectedHost(out ip, out ipText, out mac, out ports, out role)) { return; }

            CopyToClipboard(mac, "已复制 MAC 地址：" + mac);
        }

        private void OnCopyRowClick(object sender, EventArgs e)
        {
            if (listHosts.SelectedItems.Count == 0) { return; }

            int count = ListViewHelper.CopySelectedToClipboard(listHosts);

            if (count > 0)
            {
                SetScanStatus("已复制 " + count + " 行到剪贴板。");
            }
            else
            {
                SetScanStatus("复制失败：剪贴板可能被其它程序占用。");
            }
        }

        private void CopyToClipboard(string text, string successMessage)
        {
            if (string.IsNullOrEmpty(text))
            {
                SetScanStatus("没有可复制的内容。");
                return;
            }

            try
            {
                Clipboard.SetText(text);
                SetScanStatus(successMessage);
            }
            catch (Exception)
            {
                SetScanStatus("复制失败：剪贴板可能被其它程序占用。");
            }
        }

        private void OnDeepTestClick(object sender, EventArgs e)
        {
            uint ip;
            string ipText, mac, role;
            int[] ports;

            if (!TryGetSelectedHost(out ip, out ipText, out mac, out ports, out role)) { return; }

            // 深度测速涉及多次网络往返，必须在后台线程执行，否则界面会卡死
            SetScanStatus("正在对 " + ipText + " 进行 3 次握手测速…");

            int timeout = _settings == null ? IniSchema.DefaultPortTimeoutMs : _settings.PortTimeoutMs;

            // 用一个轻量后台线程（低频操作，不必接入扫描引擎）
            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                Ping3Result result = null;

                try
                {
                    result = Ping3Tester.RunWithFallback(ip, ports, timeout);
                }
                catch (Exception)
                {
                    result = null;
                }

                // 回填到 UI 线程
                if (IsDisposed) { return; }

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        ApplyPing3Result(ip, ipText, result);
                    });
                }
                catch (Exception)
                {
                    // 窗体已关闭时忽略
                }
            });

            t.IsBackground = true;
            t.Start();
        }

        /// <summary>
        /// 把深度测速结果回填到列表（只更新该行，不重建整表）。
        /// </summary>
        private void ApplyPing3Result(uint ipAddress, string ipText, Ping3Result result)
        {
            if (result == null)
            {
                // 探测未返回结果（线程异常或被测速目标已从列表移除）
                SetScanStatus("测速未返回结果：" + ipText + "，请稍后重试。");
                return;
            }

            string text = result.ToDisplayText();

            bool updated = ListViewHelper.UpdateCell(
                listHosts, ipAddress, ListViewHelper.ColPing3, text);

            if (!updated)
            {
                SetScanStatus("测速完成，但未能在列表中找到 " + ipText + "。");
                return;
            }

            if (result.SuccessCount <= 0)
            {
                SetScanStatus("3 次连接均失败：" + ipText + ":" + result.Port
                    + " 端口不可达。");
            }
            else
            {
                SetScanStatus("测速完成：" + ipText + ":" + result.Port
                    + "　" + text);
            }
        }

        private void OnOpenWebClick(object sender, EventArgs e)
        {
            uint ip;
            string ipText, mac, role;
            int[] ports;

            if (!TryGetSelectedHost(out ip, out ipText, out mac, out ports, out role)) { return; }

            string url = ProcessLauncher.BuildWebUrl(ip, ports);

            ProcessLauncher.LaunchResult r = ProcessLauncher.OpenUrl(url);

            SetScanStatus(r.Message);
        }

        private void OnOpenRdpClick(object sender, EventArgs e)
        {
            uint ip;
            string ipText, mac, role;
            int[] ports;

            if (!TryGetSelectedHost(out ip, out ipText, out mac, out ports, out role)) { return; }

            // 3389 未开放时先提示，由用户决定是否仍要尝试（PRD FR-1.6 右键菜单）
            bool hasRdp = ContainsPort(ports, 3389);

            if (!hasRdp)
            {
                DialogResult r = MessageBox.Show(
                    this,
                    "该主机 3389 端口未开放，远程桌面很可能连接失败。\r\n\r\n"
                    + "仍要尝试启动 mstsc 连接到 " + ipText + " 吗？",
                    "远程桌面",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);

                if (r != DialogResult.Yes)
                {
                    SetScanStatus("已取消远程桌面连接。");
                    return;
                }
            }

            ProcessLauncher.LaunchResult result = ProcessLauncher.OpenRemoteDesktop(ip);

            SetScanStatus(result.Message);
        }

        private static bool ContainsPort(int[] ports, int port)
        {
            if (ports == null) { return false; }

            for (int i = 0; i < ports.Length; i++)
            {
                if (ports[i] == port) { return true; }
            }

            return false;
        }

        private void OnSingleTestClick(object sender, EventArgs e)
        {
            uint ip;
            string ipText, mac, role;
            int[] ports;

            if (!TryGetSelectedHost(out ip, out ipText, out mac, out ports, out role)) { return; }

            int port = ports.Length > 0 ? ports[0] : 80;

            // 通过事件通知主窗体切到 Tab 2 并带入参数（S11 实现具体跳转）
            if (SingleTestRequested != null)
            {
                SingleTestRequested(this, new SingleTestEventArgs(ipText, port));
            }
            else
            {
                SetScanStatus("单点测通：目标 " + ipText + ":" + port
                    + "（便捷功能页将在后续阶段接入）");
            }
        }

        private void OnListViewColumnClick(object sender, ColumnClickEventArgs e)
        {
            // 同一列点击切换升降序，换列则默认升序
            if (_sortColumnSet && _sortColumn == e.Column)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumn = e.Column;
                _sortAscending = true;
                _sortColumnSet = true;
            }

            try
            {
                _dispatcher.Sort(e.Column, _sortAscending);
            }
            catch (Exception ex)
            {
                RaiseNotify("排序失败：" + ex.Message, true);
            }
        }

        private void OnListViewKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+C 复制选中行（PRD FR-1.6.4）
            if (e.Control && e.KeyCode == Keys.C)
            {
                int count = ListViewHelper.CopySelectedToClipboard(listHosts);

                if (count > 0)
                {
                    SetScanStatus("已复制 " + count + " 行到剪贴板。");
                }
                else
                {
                    SetScanStatus("没有选中任何行。");
                }

                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        /// <summary>
        /// 由主窗体在启动时调用。
        /// </summary>
        public void Initialize(string lastNetSegment, AppSettings settings)
        {
            Initialize(lastNetSegment, settings, SettingsService.ResolveConfigPath());
        }

        public void Initialize(string lastNetSegment, AppSettings settings, string configPath)
        {
            _lastNetSegment = lastNetSegment == null ? "" : lastNetSegment;
            _settings = settings;
            _settingsPath = configPath == null ? "" : configPath;
            _currentProbeMode = settings == null || settings.LastProbeMode == (int)ProbeMode.Arp
                ? ProbeMode.Arp : ProbeMode.Ping;
            _suppressEvents = true;
            try
            {
                rbProbeArp.Checked = _currentProbeMode == ProbeMode.Arp;
                rbProbePing.Checked = _currentProbeMode == ProbeMode.Ping;
            }
            finally
            {
                _suppressEvents = false;
            }
            UpdateProbeModeHint();
            ReloadAdapters();
        }

        // -------------------------------------------------------------------
        // 网卡
        // -------------------------------------------------------------------
        private void ReloadAdapters()
        {
            _adapters = NetworkAdapterService.GetActiveAdapters();

            _suppressEvents = true;
            try
            {
                cboAdapter.Items.Clear();

                for (int i = 0; i < _adapters.Count; i++)
                {
                    cboAdapter.Items.Add(_adapters[i].DisplayText);
                }

                if (_adapters.Count > 0)
                {
                    cboAdapter.SelectedIndex = 0;
                }
            }
            finally
            {
                _suppressEvents = false;
            }

            if (_adapters.Count == 0)
            {
                _adapterWarning = "未检测到可用的网卡。请检查网线连接或无线是否已连接网络。";
                SetAdapterWarning(_adapterWarning);
                SetPreview("");
                _currentSegment = null;
                txtCidr.Text = "";
                return;
            }

            if (NetworkAdapterService.IsFallbackMode(_adapters))
            {
                _adapterWarning =
                    "未检测到物理网卡，当前显示的是虚拟网卡。扫描结果仅供调试参考，"
                    + "不代表真实局域网环境。";
            }
            else
            {
                _adapterWarning = "";
            }

            SetAdapterWarning(_adapterWarning);

            ApplyAdapterToInputs();
        }

        private void ApplyAdapterToInputs()
        {
            AdapterInfo adapter = GetSelectedAdapter();
            if (adapter == null) { return; }

            string cidrText = adapter.GetSegment().Cidr;

            if (!string.IsNullOrEmpty(_lastNetSegment))
            {
                NetSegment last;
                SegmentParseError e;
                string m;

                if (NetSegment.TryParseCidr(_lastNetSegment, out last, out e, out m))
                {
                    if (last.Mode == SegmentMode.Cidr
                        && last.NetworkAddress == adapter.GetSegment().NetworkAddress
                        && last.PrefixLength == adapter.GetSegment().PrefixLength)
                    {
                        cidrText = _lastNetSegment;
                    }
                }
            }

            _suppressEvents = true;
            try
            {
                txtCidr.Text = cidrText;
                txtRangeStart.Text = Ipv4Util.Format(adapter.GetSegment().FirstHost);
                txtRangeEnd.Text = Ipv4Util.Format(adapter.GetSegment().LastHost);
            }
            finally
            {
                _suppressEvents = false;
            }

            Revalidate();
        }

        private AdapterInfo GetSelectedAdapter()
        {
            int index = cboAdapter.SelectedIndex;

            if (index < 0 || index >= _adapters.Count) { return null; }

            return _adapters[index];
        }

        // -------------------------------------------------------------------
        // 事件
        // -------------------------------------------------------------------
        private void OnRefreshAdapterClick(object sender, EventArgs e)
        {
            ReloadAdapters();
        }

        private void OnAdapterChanged(object sender, EventArgs e)
        {
            if (_suppressEvents) { return; }
            ApplyAdapterToInputs();
        }

        private void OnModeChanged(object sender, EventArgs e)
        {
            if (_suppressEvents) { return; }
            ApplyTargetModeControls();
            Revalidate();
        }

        private void ApplyTargetModeControls()
        {
            bool cidrMode = rbCidr.Checked;
            txtCidr.Enabled = cidrMode;
            txtRangeStart.Enabled = !cidrMode;
            lblRangeSep.Enabled = !cidrMode;
            txtRangeEnd.Enabled = !cidrMode;
        }

        private void OnTargetTextChanged(object sender, EventArgs e)
        {
            if (_suppressEvents) { return; }
            Revalidate();
        }

        // -------------------------------------------------------------------
        // 校验与预览
        // -------------------------------------------------------------------
        /// <summary>当前解析出的目标范围；无效时为 null。</summary>
        public NetSegment CurrentSegment { get { return _currentSegment; } }

        /// <summary>当前选中的网卡（供 Tab 2 自检使用）。</summary>
        public AdapterInfo CurrentAdapter { get { return GetSelectedAdapter(); } }

        public int CurrentProbeModeValue { get { return (int)_currentProbeMode; } }

        /// <summary>
        /// 重新解析输入并刷新预览与校验提示。
        /// </summary>
        public bool Revalidate()
        {
            _currentSegment = null;

            if (_adapters.Count == 0)
            {
                return false;
            }

            bool ok;

            if (rbCidr.Checked)
            {
                SegmentParseError err;
                string msg;

                ok = NetSegment.TryParseCidr(txtCidr.Text, out _currentSegment, out err, out msg);

                if (!ok)
                {
                    SetValidation(msg, true);
                    SetPreview("");
                    return false;
                }
            }
            else
            {
                SegmentParseError err;
                string msg;

                ok = NetSegment.TryParseRange(txtRangeStart.Text, txtRangeEnd.Text,
                    out _currentSegment, out err, out msg);

                if (!ok)
                {
                    SetValidation(msg, true);
                    SetPreview("");
                    return false;
                }
            }

            SetValidation("", false);
            BuildPreview();

            return true;
        }

        private void BuildPreview()
        {
            if (_currentSegment == null) { return; }

            AdapterInfo adapter = GetSelectedAdapter();
            NetSegment seg = _currentSegment;

            int hosts = seg.HostCount;

            string text = "目标地址数：" + hosts
                + "    范围：" + Ipv4Util.Format(seg.FirstHost)
                + " ~ " + Ipv4Util.Format(seg.LastHost)
                + "    模式：" + (seg.Mode == SegmentMode.Cidr ? "网段" : "精确区间");

            if (adapter != null)
            {
                NetSegment local = adapter.GetSegment();

                bool crossesSubnet =
                    seg.FirstHost < local.FirstHost
                    || seg.LastHost > local.LastHost;

                if (crossesSubnet)
                {
                    text += "\r\n⚠ 目标范围超出本机所在网段（" + local.Cidr
                        + "），跨网段设备可能无法被 ARP 发现，建议逐段扫描。";
                }
            }

            if (hosts > NetSegment.ConfirmHostThreshold)
            {
                text += "\r\n⚠ 超过 " + NetSegment.ConfirmHostThreshold
                    + " 个地址，开始扫描前将要求确认。";
            }

            if (hosts > NetSegment.MaxHostsSoftLimit)
            {
                text += "\r\n⚠ 超过 " + NetSegment.MaxHostsSoftLimit
                    + " 个地址，结果列表将只保留前 " + NetSegment.MaxHostsSoftLimit + " 行。";
            }

            SetPreview(text);
        }

        private void SetPreview(string text)
        {
            lblPreview.Text = text;
            lblPreview.ForeColor = text.IndexOf('⚠') >= 0
                ? System.Drawing.Color.FromArgb(180, 100, 0)
                : System.Drawing.SystemColors.ControlText;
        }

        private void SetValidation(string message, bool isError)
        {
            if (string.IsNullOrEmpty(message))
            {
                lblValidation.Text = "";
                return;
            }

            lblValidation.Text = (isError ? "✗ " : "") + message;
            lblValidation.ForeColor = isError
                ? System.Drawing.Color.Firebrick
                : System.Drawing.SystemColors.ControlText;
        }

        private void SetAdapterWarning(string message)
        {
            lblAdapterWarning.Text = string.IsNullOrEmpty(message) ? "" : "⚠ " + message;
        }

        // -------------------------------------------------------------------
        // 探测方式选择
        // -------------------------------------------------------------------
        private void OnProbeModeChanged(object sender, EventArgs e)
        {
            if (_suppressEvents || _scanning) { return; }
            if (rbProbeArp == null || rbProbePing == null) { return; }

            _currentProbeMode = rbProbePing.Checked ? ProbeMode.Ping : ProbeMode.Arp;
            UpdateProbeModeHint();

            if (_settings != null)
            {
                _settings.LastProbeMode = (int)_currentProbeMode;
                string error;
                string configPath = _settingsPath;
                if (string.IsNullOrEmpty(configPath)) { configPath = SettingsService.ResolveConfigPath(); }
                if (!SettingsService.Save(_settings, configPath, out error))
                {
                    SetScanStatus("无法保存探测方式：" + error);
                }
            }

            RaiseNotify(_currentProbeMode == ProbeMode.Arp
                ? "已选择 ARP 探测：适合同一广播域，可获取 MAC 地址。"
                : "已选择 Ping 探测：适用于路由可达的网段；ICMP 被过滤时可能漏报。", false);
        }

        private void UpdateProbeModeHint()
        {
            if (lblProbeHint == null) { return; }
            lblProbeHint.Text = _currentProbeMode == ProbeMode.Arp
                ? "同一广播域，能获取 MAC；跨网段通常不可用。"
                : "可经路由探测；目标或网络屏蔽 ICMP 时可能漏报，且无 MAC。";
        }

        // -------------------------------------------------------------------
        // 扫描控制
        // -------------------------------------------------------------------
        private void OnStartScanClick(object sender, EventArgs e)
        {
            if (_scanning) { return; }

            if (!Revalidate() || _currentSegment == null)
            {
                return;
            }

            NetSegment seg = _currentSegment;

            if (seg.HostCount > NetSegment.ConfirmHostThreshold)
            {
                if (!ConfirmLargeScan(seg)) { return; }
            }

            StartScan(seg);
        }

        /// <summary>
        /// 启动一次扫描会话并开始 UI 刷新。
        /// </summary>
        private void StartScan(NetSegment seg)
        {
            _displayedRows = 0;

            // 清空上一轮结果与告警
            if (_dispatcher != null) { _dispatcher.Clear(); }

            _alerts = new List<AlertItem>();
            _finalStateText = "";
            _currentTargetText = seg.DisplayText;

            _sortColumnSet = false;
            _sortColumn = -1;
            _sortAscending = true;

            SetProgress(0);
            SetScanStatus("正在准备…");

            int concurrency = _settings == null ? IniSchema.DefaultMaxConcurrency : _settings.MaxConcurrency;
            int timeout = _settings == null ? IniSchema.DefaultPortTimeoutMs : _settings.PortTimeoutMs;
            int retry = _settings == null ? IniSchema.DefaultSendArpRetry : _settings.SendArpRetry;
            int[] ports = _settings == null
                ? new int[0]
                : _settings.GetEnabledPorts();
            int pingTimeout = _settings == null ? IniSchema.DefaultPingTimeoutMs : _settings.PingTimeoutMs;
            int pingRetry = _settings == null ? IniSchema.DefaultPingRetry : _settings.PingRetry;

            ScanParameters prm = new ScanParameters(
                seg.EnumerateHosts(), concurrency, timeout,
                _currentProbeMode == ProbeMode.Arp ? retry : pingRetry,
                ports, _currentProbeMode, pingTimeout, pingRetry);

            // S10：接入 IP 冲突检测（先快照扫描前 ARP 表作为基线）
            ConflictDetector conflictDetector = null;
            if (_currentProbeMode == ProbeMode.Arp)
            {
                conflictDetector = new ConflictDetector();
                conflictDetector.CaptureBaseline();
            }
            _conflictDetector = conflictDetector;

            AdapterInfo scanAdapter = GetSelectedAdapter();
            if (_currentProbeMode == ProbeMode.Arp)
            {
                // 网关 ARP 探测在后台执行；Ping 模式不做 MAC 证据链告警。
                StartGatewayProbeAsync(scanAdapter, timeout);
            }

            // 构造探测器：
            //   · localAddress → 本机地址不入结果列表（PRD FR-1.1.7）
            //   · gatewayMac  → 识别"网关代理回应"造成的跨网段假阳性（S13 F1）
            uint localAddress = scanAdapter == null ? 0 : scanAdapter.IpAddress;
            string gatewayMac = scanAdapter == null || _currentProbeMode != ProbeMode.Arp
                ? "" : scanAdapter.GatewayMac;

            IHostProber prober = ProberFactory != null
                ? ProberFactory()
                : new CompositeProber(new ArpProber(),
                    _settings == null ? null : _settings.Roles,
                    conflictDetector,
                    localAddress,
                    gatewayMac,
                    _currentProbeMode,
                    null);

            _session = new ScanSession(prm, null, prober);

            SetScanning(true);

            try
            {
                _session.Start();
            }
            catch (Exception ex)
            {
                SetScanning(false);
                SetScanStatus("扫描启动失败：" + ex.Message);
                return;
            }

            if (_dispatcher != null) { _dispatcher.Start(); }

            StartRefreshTimer();

            if (ScanStartRequested != null)
            {
                ScanStartRequested(this, new ScanStartEventArgs(seg));
            }

            RaiseNotify("开始扫描：" + seg.DisplayText + "（" + seg.HostCount + " 个地址）", false);
        }

        private void QueueDeviceNameLookup(HostRecord record)
        {
            if (_deviceNameQueue == null) { _deviceNameQueue = new DeviceNameLookupQueue(); }
            _deviceNameQueue.Enqueue(record, OnDeviceNameResolved);
        }

        private void OnDeviceNameResolved(HostRecord record)
        {
            if (record == null || IsDisposed || !IsHandleCreated) { return; }
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (IsDisposed || listHosts.IsDisposed) { return; }
                    lock (_hostRecordLock) { _hostRecords[record.IpAddress] = record; }
                    string ip = record.IpText;
                    for (int i = 0; i < listHosts.Items.Count; i++)
                    {
                        ListViewItem item = listHosts.Items[i];
                        if (item.SubItems[ListViewHelper.ColIp].Text == ip)
                        {
                            item.SubItems[ListViewHelper.ColDeviceName].Text = record.DeviceName;
                            break;
                        }
                    }
                }));
            }
            catch (InvalidOperationException) { }
        }

        private void StartRefreshTimer()
        {
            if (_refreshTimer == null)
            {
                _refreshTimer = new Timer();
                _refreshTimer.Interval = UiRefreshIntervalMs;
                _refreshTimer.Tick += OnRefreshTick;
            }

            _refreshTimer.Start();
        }

        /// <summary>
        /// 每 200ms 从引擎批量取结果并刷新界面。
        /// 这里刻意只做"取数据 + 更新控件"，不做任何网络或计算工作。
        /// </summary>
        private void OnRefreshTick(object sender, EventArgs e)
        {
            if (_session == null) { return; }

            ScanProgress snap = _session.Snapshot();

            // 结果批量取出并投递给刷新器（由 UiDispatcher 自己按 200ms 批量插入）
            List<HostRecord> batch = _session.Results.TakeResults(MaxRowsPerTick);

            if (batch != null && batch.Count > 0)
            {
                for (int i = 0; i < batch.Count; i++)
                {
                    HostRecord record = batch[i];
                    lock (_hostRecordLock) { _hostRecords[record.IpAddress] = record; }
                    _dispatcher.Post(record);
                    QueueDeviceNameLookup(record);
                }

                _displayedRows += batch.Count;
            }

            SetProgress(snap.Percent);

            string status = "已探测 " + snap.Done + "/" + snap.Total
                + " ｜存活 " + snap.AliveCount + " 台"
                + " ｜ 已用 " + snap.ElapsedText;

            switch (snap.State)
            {
                case ScanState.Paused:
                    status = "已暂停　" + status;
                    break;
                case ScanState.Stopped:
                    status = "已终止（部分结果）　" + status;
                    break;
                case ScanState.Completed:
                    status = "扫描完成　" + status;
                    break;
            }

            SetScanStatus(status);

            if (snap.State == ScanState.Completed || snap.State == ScanState.Stopped)
            {
                FinishScan(snap.State);
            }
        }

        private void FinishScan(ScanState finalState)
        {
            StopRefreshTimer();

            if (_dispatcher != null)
            {
                // 把队列里还没提交的结果一次性刷到界面，
                // 否则扫描结束后会"少几台设备"（看起来像漏扫）。
                int remaining = _session == null ? 0 : _session.Results.ResultCount;

                if (remaining > 0 && _session != null)
                {
                    List<HostRecord> rest = _session.Results.TakeResults(0);

                    if (rest != null)
                    {
                        for (int i = 0; i < rest.Count; i++) { _dispatcher.Post(rest[i]); }
                    }
                }

                _dispatcher.FlushNow();
                _dispatcher.Stop();
            }

            SetScanning(false);

            if (finalState == ScanState.Completed)
            {
                SetProgress(100);
            }

            ScanProgress snap = _session == null ? null : _session.Snapshot();

            if (snap != null)
            {
                int shown = _dispatcher == null ? 0 : _dispatcher.DisplayedRows;
                int dropped = _dispatcher == null ? 0 : _dispatcher.DroppedRows;

                StringBuilder sb = new StringBuilder();

                if (finalState == ScanState.Completed)
                {
                    sb.Append("扫描完成：共探测 " + snap.Done + " 个地址，发现 "
                        + snap.AliveCount + " 台设备");
                }
                else
                {
                    sb.Append("已终止：已探测 " + snap.Done + " 个地址，发现 "
                        + snap.AliveCount + " 台设备（部分结果）");
                }

                sb.Append("，列表显示 " + shown + " 行");

                if (dropped > 0)
                {
                    sb.Append("（另有 " + dropped + " 行超出上限未显示）");
                }

                sb.Append("。");

                string msg = sb.ToString();

                SetScanStatus(msg);
                RaiseNotify(msg, false);

                // 记录最终状态，供报告"扫描状态"字段使用
                _finalStateText = finalState == ScanState.Completed
                    ? "扫描完成"
                    : "已终止（部分结果）";
            }

            _session = null;

            // S10：扫描结束后汇总 IP 冲突告警（冲突可能在扫描过程中才被发现）
            CollectConflictAlerts();
        }

        private void StopRefreshTimer()
        {
            if (_refreshTimer != null) { _refreshTimer.Stop(); }
        }

        private void OnPauseClick(object sender, EventArgs e)
        {
            if (_session == null) { return; }

            // 会话已结束（Completed/Stopped）时，暂停按钮应当已经随 FinishScan 禁用。
            // 这里再兜一层：若因时序原因仍被点到，直接刷新界面把状态纠正过来，
            // 不要把按钮文字改成「继续」——那会让用户以为还能恢复。
            if (!_session.IsActive)
            {
                OnRefreshTick(null, EventArgs.Empty);
                return;
            }

            try
            {
                if (_session.State == ScanState.Paused)
                {
                    _session.Resume();
                    btnPause.Text = "暂停";
                    RaiseNotify("已继续扫描。", false);
                }
                else
                {
                    _session.Pause();
                    btnPause.Text = "继续";
                    RaiseNotify("已暂停扫描。", false);
                }
            }
            catch (Exception ex)
            {
                RaiseNotify("暂停/继续失败：" + ex.Message, true);
            }

            // 立即刷新一次，让状态栏与进度马上反映新状态，
            // 避免"按钮已变、文案还是旧的"的自相矛盾反馈。
            OnRefreshTick(null, EventArgs.Empty);
        }

        private void OnStopClick(object sender, EventArgs e)
        {
            if (_session == null) { return; }

            try
            {
                _session.Stop();
            }
            catch (Exception ex)
            {
                RaiseNotify("终止失败：" + ex.Message, true);
            }

            // 立即刷新一次，让"已终止"状态马上可见
            OnRefreshTick(null, EventArgs.Empty);
        }

        private bool ConfirmLargeScan(NetSegment seg)
        {
            AdapterInfo adapter = GetSelectedAdapter();
            NetSegment local = adapter == null ? null : adapter.GetSegment();

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            sb.AppendLine("即将扫描 " + seg.HostCount + " 个地址：");
            sb.AppendLine();
            sb.AppendLine("  范围：" + Ipv4Util.Format(seg.FirstHost)
                + " ~ " + Ipv4Util.Format(seg.LastHost));
            sb.AppendLine("  模式：" + (seg.Mode == SegmentMode.Cidr ? "网段（" + seg.Cidr + "）" : "精确区间"));
            // 不提供耗时预估：SendARP 对无响应地址需等 3~4 秒，
            // 实际耗时取决于现场在线设备数量，无法可靠预估，
            // 给出错误数字反而误导运维（用户已确认此决定）。

            if (local != null && (seg.FirstHost < local.FirstHost || seg.LastHost > local.LastHost))
            {
                sb.AppendLine();
                sb.AppendLine("⚠ 目标范围超出本机所在网段（" + local.Cidr + "）。");
                sb.AppendLine("   跨网段设备可能无法被 ARP 发现，建议逐段扫描。");
            }

            if (seg.HostCount > NetSegment.MaxHostsSoftLimit)
            {
                sb.AppendLine();
                sb.AppendLine("⚠ 地址数超过 " + NetSegment.MaxHostsSoftLimit
                    + "，结果列表将只保留前 " + NetSegment.MaxHostsSoftLimit + " 行。");
            }

            sb.AppendLine();
            sb.Append("是否继续？");

            DialogResult r = MessageBox.Show(
                this,
                sb.ToString(),
                "确认扫描范围",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (r != DialogResult.OK)
            {
                SetScanStatus("已取消本次扫描。");
            }

            return r == DialogResult.OK;
        }

        /// <summary>
        /// 切换扫描状态下的按钮可用性（PRD FR-1.4.1）。
        /// </summary>
        public void SetScanning(bool scanning)
        {
            _scanning = scanning;

            btnStartScan.Enabled = !scanning;
            btnPause.Enabled = scanning;
            btnStop.Enabled = scanning;

            if (!scanning) { btnPause.Text = "暂停"; }

            grpTarget.Enabled = !scanning;
        }

        public void SetScanStatus(string text)
        {
            lblScanStatus.Text = text;
        }

        // -------------------------------------------------------------------
        // 告警：网关探测与 IP 冲突
        // -------------------------------------------------------------------
        /// <summary>
        /// 在后台探测网关，完成后通过 AlertsChanged 通知主窗体。
        /// ARP 对不可达网关要等 3~4 秒，必须在后台执行。
        /// </summary>
        private void StartGatewayProbeAsync(AdapterInfo adapter, int timeoutMs)
        {
            if (adapter == null) { return; }

            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                GatewayProbeResult result = null;

                try
                {
                    result = GatewayProber.ProbeAdapter(adapter, timeoutMs);
                }
                catch (Exception)
                {
                    result = null;
                }

                if (IsDisposed) { return; }

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        ApplyGatewayResult(adapter, result);
                    });
                }
                catch (Exception)
                {
                    // 窗体已关闭
                }
            });

            t.IsBackground = true;
            t.Start();
        }

        private void ApplyGatewayResult(AdapterInfo adapter, GatewayProbeResult result)
        {
            if (result == null) { return; }

            if (!result.Reachable)
            {
                AlertItem alert = result.ToAlert();

                if (alert != null)
                {
                    _alerts.Add(alert);
                    RaiseAlertsChanged();
                }

                SetScanStatus("默认网关不可达，设备排查可能无意义。");
            }
            else
            {
                SetScanStatus("默认网关可达（" + result.Method + "，"
                    + result.ElapsedMs + " ms）。");
            }
        }

        /// <summary>
        /// 汇总本轮扫描的告警（网关 + IP 冲突）并通知界面。
        /// </summary>
        private void CollectConflictAlerts()
        {
            if (_conflictDetector == null) { return; }
            if (_conflictDetector.ConflictCount == 0) { return; }

            List<AlertItem> conflicts = _conflictDetector.BuildAlerts();

            for (int i = 0; i < conflicts.Count; i++)
            {
                _alerts.Add(conflicts[i]);
            }

            RaiseAlertsChanged();
        }

        private void RaiseAlertsChanged()
        {
            if (AlertsChanged != null)
            {
                AlertsChanged(this, new AlertsChangedEventArgs(_alerts));
            }
        }

        /// <summary>
        /// 外部（主窗体）主动设置告警，用于测试与状态恢复。
        /// </summary>
        public void SetAlerts(IList<AlertItem> alerts)
        {
            _alerts = alerts == null ? new List<AlertItem>() : new List<AlertItem>(alerts);
            RaiseAlertsChanged();
        }

        /// <summary>
        /// 当前告警列表（只读副本，供测试校验）。
        /// </summary>
        public IList<AlertItem> CurrentAlerts
        {
            get { return new List<AlertItem>(_alerts); }
        }

        // -------------------------------------------------------------------
        // 报告导出（PRD FR-1.7）
        // -------------------------------------------------------------------
        private void OnExportReportClick(object sender, EventArgs e)
        {
            ExportReport();
        }

        private void OnOpenReportFolderClick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_lastReportPath))
            {
                SetScanStatus("尚未导出报告。");
                return;
            }

            ProcessLauncher.LaunchResult r = ProcessLauncher.ShowInExplorer(_lastReportPath);

            SetScanStatus(r.Message);
        }

        /// <summary>
        /// 生成并导出诊断报告。返回是否成功（供测试调用）。
        /// </summary>
        public bool ExportReport()
        {
            try
            {
                ReportInput input = BuildReportInput();

                string content = ReportBuilder.Build(input);

                bool preferDesktop = _settings == null || _settings.SaveToDesktop;
                string format = _settings == null
                    ? IniSchema.DefaultTimestampFormat
                    : _settings.TimestampFormat;

                string fileName = ReportBuilder.BuildFileName(format, input.GeneratedAt);

                ReportSaveResult result = ReportSaveService.Save(content, fileName, preferDesktop);

                if (result.Success)
                {
                    _lastReportPath = result.FilePath;

                    if (btnOpenReportFolder != null) { btnOpenReportFolder.Enabled = true; }

                    SetScanStatus(result.Message);

                    MessageBox.Show(
                        this,
                        "诊断报告已导出。\r\n\r\n" + result.FilePath
                        + "\r\n\r\n可使用「打开所在文件夹」按钮快速定位。",
                        "导出成功",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    return true;
                }

                SetScanStatus(result.Message);

                DialogResult dr = MessageBox.Show(
                    this,
                    result.Message + "\r\n\r\n是否改用「另存为」选择保存位置？",
                    "导出失败",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (dr == DialogResult.Yes)
                {
                    return SaveReportAs(content, fileName);
                }

                return false;
            }
            catch (Exception ex)
            {
                SetScanStatus("导出报告失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 另存为。
        /// </summary>
        private bool SaveReportAs(string content, string suggestedName)
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "保存诊断报告";
                dlg.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                dlg.FileName = suggestedName;
                dlg.OverwritePrompt = true;

                string desktop = ReportSaveService.GetDesktopPath();

                if (!string.IsNullOrEmpty(desktop)) { dlg.InitialDirectory = desktop; }

                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    SetScanStatus("已取消保存。");
                    return false;
                }

                ReportSaveResult r = ReportSaveService.TryWrite(dlg.FileName, content);

                if (r.Success)
                {
                    _lastReportPath = r.FilePath;

                    if (btnOpenReportFolder != null) { btnOpenReportFolder.Enabled = true; }

                    SetScanStatus(r.Message);
                    return true;
                }

                SetScanStatus(r.Message);
                return false;
            }
        }

        /// <summary>
        /// 组装报告输入。
        /// 数据源是**界面已显示的记录**，保证"报告内容 = 用户看到的列表"。
        /// </summary>
        private ReportInput BuildReportInput()
        {
            ReportInput input = new ReportInput();

            input.GeneratedAt = DateTime.Now;
            input.TargetText = _currentTargetText;
            input.ProbeModeText = _currentProbeMode == ProbeMode.Arp
                ? "ARP（MAC 地址可用）" : "ICMP Ping（不提供 MAC 地址）";
            input.FinalStateText = _finalStateText;

            AdapterInfo adapter = GetSelectedAdapter();

            if (adapter != null)
            {
                input.AdapterText = adapter.Description
                    + " / " + Ipv4Util.Format(adapter.IpAddress) + "/" + adapter.PrefixLength;

                input.LocalIpText = Ipv4Util.Format(adapter.IpAddress);
            }
            else
            {
                input.AdapterText = "（未识别）";
                input.LocalIpText = "（未识别）";
            }

            input.Concurrency = _settings == null ? 0 : _settings.MaxConcurrency;
            input.PortTimeoutMs = _settings == null ? 0 : _settings.PortTimeoutMs;
            input.PortCount = _settings == null ? 0 : _settings.GetEnabledPorts().Length;

            if (_session == null)
            {
                input.ElapsedText = TimeSpan.Zero.ToString(@"hh\:mm\:ss");
                input.TargetCount = 0;
                input.AliveCount = 0;
            }
            else
            {
                ScanProgress snap = _session.Snapshot();
                input.ElapsedText = snap.ElapsedText;
                input.TargetCount = snap.Total;
                input.AliveCount = snap.AliveCount;
            }

            // 从界面列表读取（唯一事实来源）
            List<HostRecord> hosts = new List<HostRecord>();

            for (int i = 0; i < listHosts.Items.Count; i++)
            {
                ListViewItem item = listHosts.Items[i];

                uint ip;
                if (!Ipv4Util.TryParse(item.SubItems[ListViewHelper.ColIp].Text, out ip)) { continue; }

                int[] ports = ParsePorts(item.SubItems[ListViewHelper.ColPorts].Text);

                HostRecord rec = new HostRecord(ip, item.SubItems[ListViewHelper.ColMac].Text, ports);
                rec.DeviceName = item.SubItems[ListViewHelper.ColDeviceName].Text == "—"
                    ? "" : item.SubItems[ListViewHelper.ColDeviceName].Text;
                rec.Role = item.SubItems[ListViewHelper.ColRole].Text;
                rec.Confidence = ReadConfidence(item.SubItems[ListViewHelper.ColConfidence].Text);
                rec.Evidence = item.SubItems[ListViewHelper.ColEvidence].Text;

                hosts.Add(rec);
            }

            input.Hosts = hosts;

            if (input.AliveCount == 0) { input.AliveCount = hosts.Count; }
            if (input.TargetCount == 0) { input.TargetCount = hosts.Count; }

            input.Alerts = _alerts;
            input.Truncated = _dispatcher != null && _dispatcher.IsTruncated;

            return input;
        }

        private static DeviceConfidence ReadConfidence(string value)
        {
            if (value == "高") { return DeviceConfidence.High; }
            if (value == "中") { return DeviceConfidence.Medium; }
            if (value == "低") { return DeviceConfidence.Low; }
            return DeviceConfidence.Unknown;
        }

        public void SetProgress(int percent)
        {
            if (percent < 0) { percent = 0; }
            if (percent > 100) { percent = 100; }

            // ProgressBar 在 Value 超出范围时会抛异常，这里已钳制
            progressBar.Value = percent;
        }

        private void RaiseNotify(string message, bool isError)
        {
            if (Notify != null)
            {
                Notify(this, new MessageEventArgs(message, isError));
            }
        }

        /// <summary>
        /// 释放资源时确保扫描线程被终止（PRD UI-8）。
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopRefreshTimer();

                if (_refreshTimer != null)
                {
                    _refreshTimer.Dispose();
                    _refreshTimer = null;
                }

                if (_session != null)
                {
                    try { _session.Stop(); }
                    catch (Exception) { }

                    _session = null;
                }

                if (_dispatcher != null)
                {
                    _dispatcher.Dispose();
                    _dispatcher = null;
                }

                if (_deviceNameQueue != null)
                {
                    _deviceNameQueue.Dispose();
                    _deviceNameQueue = null;
                }

                if (components != null)
                {
                    components.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 开始扫描请求的参数。
    /// </summary>
    internal sealed class ScanStartEventArgs : EventArgs
    {
        private readonly NetSegment _segment;

        public ScanStartEventArgs(NetSegment segment)
        {
            _segment = segment;
        }

        public NetSegment Segment { get { return _segment; } }
    }

    /// <summary>
    /// 单点测通请求参数（从 Tab 1 右键菜单跳转到 Tab 2）。
    /// </summary>
    internal sealed class SingleTestEventArgs : EventArgs
    {
        private readonly string _ipText;
        private readonly int _port;

        public SingleTestEventArgs(string ipText, int port)
        {
            _ipText = ipText;
            _port = port;
        }

        public string IpText { get { return _ipText; } }
        public int Port { get { return _port; } }
    }

    /// <summary>
    /// 告警变化事件参数。
    /// </summary>
    internal sealed class AlertsChangedEventArgs : EventArgs
    {
        private readonly IList<AlertItem> _alerts;

        public AlertsChangedEventArgs(IList<AlertItem> alerts)
        {
            _alerts = alerts;
        }

        public IList<AlertItem> Alerts { get { return _alerts; } }
    }
}
