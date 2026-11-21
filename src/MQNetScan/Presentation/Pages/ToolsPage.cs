using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MQNetScan.Application;
using MQNetScan.Engine;
using MQNetScan.Infrastructure;

namespace MQNetScan.Presentation.Pages
{
    /// <summary>
    /// Tab 2：便捷功能。
    ///
    /// 功能（PRD FR-3）：
    ///   · 一键清理 ARP 缓存（普通权限只给指引，不弹 UAC）；
    ///   · 刷新 DNS 缓存；
    ///   · 打开网络连接 / 服务管理器；
    ///   · 单点握手测通（区分拒绝/超时/不可达/DNS 失败）；
    ///   · 三项连通性自检（网关 / 公共 DNS / SaaS 域名）。
    /// </summary>
    internal partial class ToolsPage : UserControl
    {
        private AppSettings _settings;
        private AdapterInfo _adapter;

        // 自检列
        private const int ColCheckName = 0;
        private const int ColCheckTarget = 1;
        private const int ColCheckResult = 2;
        private const int ColCheckElapsed = 3;
        private const int ColCheckDetail = 4;

        /// <summary>需要向主窗体转发的提示（状态栏）。</summary>
        public event EventHandler<MessageEventArgs> Notify;

        public ToolsPage()
        {
            InitializeComponent();

            ConfigureCheckList();

            lblCacheHint.Text = ProcessLauncher.IsAdministrator()
                ? "当前以管理员身份运行，可直接清理缓存。"
                : "当前为普通权限：清理 ARP 只给操作指引，不会弹出授权窗口。";
        }

        private void ConfigureCheckList()
        {
            listChecks.Columns.Clear();

            AddColumn("检查项", 100);
            AddColumn("目标", 190);
            AddColumn("结果", 60);
            AddColumn("耗时", 80);
            AddColumn("说明", 260);
        }

        private void AddColumn(string text, int width)
        {
            ColumnHeader h = new ColumnHeader();
            h.Text = text;
            h.Width = width;
            listChecks.Columns.Add(h);
        }

        /// <summary>
        /// 由主窗体注入设置与当前网卡。
        /// </summary>
        public void Initialize(AppSettings settings, AdapterInfo adapter)
        {
            _settings = settings;
            _adapter = adapter;

            UpdateCheckTargetsHint();
        }

        private void UpdateCheckTargetsHint()
        {
            string dns = _settings == null ? IniSchema.DefaultDnsProbeIp : _settings.DnsProbeIp;
            string domain = _settings == null ? IniSchema.DefaultSaasDomain : _settings.SaasDomain;

            // 明示当前自检目标（PRD 风险 R4：域名需实施确认）
            lblCheckTargets.Text = "自检目标：默认网关 / " + dns + " / " + domain;
        }

        // -------------------------------------------------------------------
        // 缓存清理
        // -------------------------------------------------------------------
        private void OnClearArpClick(object sender, EventArgs e)
        {
            try
            {
                ToolResult r = ToolsService.ClearArpCache();

                if (r.Success)
                {
                    RaiseNotify(r.Message, false);
                    return;
                }

                if (r.NeedsElevation)
                {
                    // 关键：普通权限下**只弹说明框，不触发 UAC**（PRD FR-3.1.1）
                    ShowElevationGuide(r);
                    return;
                }

                MessageBox.Show(this, r.Message, "清理 ARP 缓存",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);

                RaiseNotify("清理 ARP 缓存失败。", true);
            }
            catch (Exception ex)
            {
                RaiseNotify("清理 ARP 缓存异常：" + ex.Message, true);
            }
        }

        /// <summary>
        /// 权限不足时的操作指引框：展示可复制命令 + "以管理员身份重新启动"入口。
        /// </summary>
        private void ShowElevationGuide(ToolResult r)
        {
            using (ElevationGuideDialog dlg = new ElevationGuideDialog(r.Message, r.CommandToCopy))
            {
                dlg.ShowDialog(this);
            }

            RaiseNotify("清理 ARP 需要管理员权限，已提供操作指引。", true);
        }

        private void OnFlushDnsClick(object sender, EventArgs e)
        {
            try
            {
                ToolResult r = ToolsService.FlushDns();

                if (r.Success)
                {
                    RaiseNotify(r.Message, false);
                    return;
                }

                if (r.NeedsElevation)
                {
                    ShowElevationGuide(r);
                    return;
                }

                MessageBox.Show(this, r.Message, "刷新 DNS 缓存",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);

                RaiseNotify("刷新 DNS 缓存失败。", true);
            }
            catch (Exception ex)
            {
                RaiseNotify("刷新 DNS 缓存异常：" + ex.Message, true);
            }
        }

        // -------------------------------------------------------------------
        // 系统面板
        // -------------------------------------------------------------------
        private void OnOpenNcpaClick(object sender, EventArgs e)
        {
            ToolResult r = ToolsService.OpenNetworkConnections();
            RaiseNotify(r.Message, !r.Success);
        }

        private void OnOpenServicesClick(object sender, EventArgs e)
        {
            ToolResult r = ToolsService.OpenServices();
            RaiseNotify(r.Message, !r.Success);
        }

        // -------------------------------------------------------------------
        // 单点测通
        // -------------------------------------------------------------------
        private void OnTestPortClick(object sender, EventArgs e)
        {
            string host = txtHost.Text.Trim();
            string portText = txtPort.Text.Trim();

            if (host.Length == 0)
            {
                SetPortResult("请输入 IP 地址或域名。", true);
                return;
            }

            int port;
            if (!int.TryParse(portText, out port) || port < 1 || port > 65535)
            {
                SetPortResult("端口必须是 1~65535 之间的整数。", true);
                return;
            }

            int timeout = _settings == null ? IniSchema.DefaultPortTimeoutMs : _settings.PortTimeoutMs;

            // 超时上限 5 秒（PRD FR-3.2.5）
            if (timeout > 5000) { timeout = 5000; }

            SetPortResult("正在测通 " + host + ":" + port + " …", false);
            btnTestPort.Enabled = false;

            // 后台执行，避免 UI 卡死（黑洞地址要等满超时）
            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                SinglePortTester.Result result = null;

                try
                {
                    result = ToolsService.TestPort(host, port, timeout);
                }
                catch (Exception ex)
                {
                    result = null;

                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            SetPortResult("测通失败：" + ex.Message, true);
                            btnTestPort.Enabled = true;
                        });
                    }
                    catch (Exception) { }

                    return;
                }

                if (IsDisposed) { return; }

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        ApplyPortResult(result);
                    });
                }
                catch (Exception) { }
            });

            t.IsBackground = true;
            t.Start();
        }

        private void ApplyPortResult(SinglePortTester.Result result)
        {
            btnTestPort.Enabled = true;

            if (result == null)
            {
                SetPortResult("测通未返回结果，请重试。", true);
                return;
            }

            SetPortResult(result.Describe(), !result.IsOpen);
        }

        /// <summary>
        /// 外部带入测通参数（从 Tab 1 右键"单点测通"跳转）。
        /// </summary>
        public void SetTestTarget(string host, int port)
        {
            txtHost.Text = host;
            txtPort.Text = port.ToString();

            RaiseNotify("已带入目标 " + host + ":" + port + "，点「测通」开始检测。", false);
        }

        private void SetPortResult(string text, bool isError)
        {
            lblPortResult.Text = text;
            lblPortResult.ForeColor = isError ? Color.Firebrick : Color.DarkGreen;
        }

        // -------------------------------------------------------------------
        // 连通性自检
        // -------------------------------------------------------------------
        private void OnRunCheckClick(object sender, EventArgs e)
        {
            RunSelfCheck();
        }

        /// <summary>
        /// 执行三项自检（供测试直接调用）。
        /// </summary>
        public void RunSelfCheck()
        {
            btnRunCheck.Enabled = false;
            listChecks.Items.Clear();

            string dns = _settings == null ? IniSchema.DefaultDnsProbeIp : _settings.DnsProbeIp;
            string domain = _settings == null ? IniSchema.DefaultSaasDomain : _settings.SaasDomain;

            RaiseNotify("正在执行连通性自检…", false);

            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                List<CheckResult> results = null;

                try
                {
                    results = ConnectivityChecker.RunAll(_adapter, dns, domain);
                }
                catch (Exception)
                {
                    results = new List<CheckResult>();
                }

                if (IsDisposed) { return; }

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        ApplyCheckResults(results);
                    });
                }
                catch (Exception) { }
            });

            t.IsBackground = true;
            t.Start();
        }

        private void ApplyCheckResults(List<CheckResult> results)
        {
            btnRunCheck.Enabled = true;

            listChecks.BeginUpdate();

            try
            {
                listChecks.Items.Clear();

                int passed = 0;

                for (int i = 0; i < results.Count; i++)
                {
                    CheckResult r = results[i];

                    if (r.Passed) { passed++; }

                    ListViewItem item = new ListViewItem(r.Name);
                    item.SubItems.Add(r.Target);
                    item.SubItems.Add(r.ConclusionText);
                    item.SubItems.Add(r.ElapsedMs + " ms");
                    item.SubItems.Add(r.Detail);

                    item.ForeColor = r.Passed ? Color.DarkGreen : Color.Firebrick;

                    listChecks.Items.Add(item);
                }

                string summary = "自检完成：" + passed + "/" + results.Count + " 项通过。";
                RaiseNotify(summary, passed < results.Count);
            }
            finally
            {
                listChecks.EndUpdate();
            }
        }

        /// <summary>供测试读取当前自检行数。</summary>
        public int CheckRowCount { get { return listChecks.Items.Count; } }

        private void RaiseNotify(string message, bool isError)
        {
            if (Notify != null)
            {
                Notify(this, new MessageEventArgs(message, isError));
            }
        }
    }

    /// <summary>
    /// 提权操作指引对话框。
    ///
    /// 这是"不自动弹 UAC"策略的落地（PRD FR-3.1.1 / NFR-P2）：
    ///   · 展示需要管理员执行的命令；
    ///   · 提供一键复制；
    ///   · 提供"以管理员身份重新启动本程序"入口（由用户主动点击才触发 UAC）。
    /// </summary>
    internal sealed class ElevationGuideDialog : Form
    {
        private readonly string _command;

        public ElevationGuideDialog(string message, string command)
        {
            _command = command == null ? "" : command;

            this.Text = "需要管理员权限";
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ClientSize = new Size(560, 320);

            Label lblMessage = new Label();
            lblMessage.Text = message;
            lblMessage.Location = new Point(16, 16);
            lblMessage.Size = new Size(528, 130);
            lblMessage.ForeColor = Color.FromArgb(60, 60, 60);

            Label lblCmdTitle = new Label();
            lblCmdTitle.Text = "需要以管理员身份执行的命令：";
            lblCmdTitle.Location = new Point(16, 152);
            lblCmdTitle.Size = new Size(300, 16);

            TextBox txtCmd = new TextBox();
            txtCmd.Text = _command;
            txtCmd.Location = new Point(16, 174);
            txtCmd.Size = new Size(400, 22);
            txtCmd.ReadOnly = true;
            txtCmd.BackColor = Color.FromArgb(245, 245, 245);

            Button btnCopy = new Button();
            btnCopy.Text = "复制命令";
            btnCopy.Location = new Point(424, 172);
            btnCopy.Size = new Size(120, 26);
            btnCopy.Click += delegate { CopyCommand(); };

            Button btnRestart = new Button();
            btnRestart.Text = "以管理员身份重新启动本程序";
            btnRestart.Location = new Point(16, 214);
            btnRestart.Size = new Size(240, 30);
            btnRestart.Click += delegate { RestartAsAdmin(); };

            Button btnClose = new Button();
            btnClose.Text = "关闭";
            btnClose.Location = new Point(424, 262);
            btnClose.Size = new Size(120, 30);
            btnClose.DialogResult = DialogResult.Cancel;

            this.Controls.Add(lblMessage);
            this.Controls.Add(lblCmdTitle);
            this.Controls.Add(txtCmd);
            this.Controls.Add(btnCopy);
            this.Controls.Add(btnRestart);
            this.Controls.Add(btnClose);

            this.CancelButton = btnClose;
        }

        private void CopyCommand()
        {
            if (string.IsNullOrEmpty(_command)) { return; }

            try
            {
                Clipboard.SetText(_command);
                MessageBox.Show(this, "命令已复制到剪贴板。", "已复制",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception)
            {
                MessageBox.Show(this, "复制失败，请手动选中命令后按 Ctrl+C。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RestartAsAdmin()
        {
            // 由用户主动点击才触发 UAC，这是"按需提权"的唯一入口
            try
            {
                System.Diagnostics.ProcessStartInfo psi =
                    new System.Diagnostics.ProcessStartInfo();

                psi.FileName = System.Windows.Forms.Application.ExecutablePath;
                psi.UseShellExecute = true;
                psi.Verb = "runas";      // 触发 UAC

                System.Diagnostics.Process.Start(psi);

                System.Windows.Forms.Application.Exit();
            }
            catch (Exception ex)
            {
                // 用户取消 UAC 或提权失败
                MessageBox.Show(this,
                    "未能以管理员身份启动：" + ex.Message
                    + "\r\n\r\n您可以手动右键程序选择「以管理员身份运行」。",
                    "启动失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
