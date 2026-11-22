using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;
using MQNetScan.Presentation.Common;

namespace MQNetScan.Presentation.Pages
{
    /// <summary>
    /// Tab 3：设置。
    /// S1 阶段实现：并发数与超时时间的读取/校验/保存。
    /// 端口特征规则表维护在 S12 阶段补全。
    /// </summary>
    internal partial class SettingsPage : UserControl
    {
        private AppSettings _settings;
        private string _configPath = "";
        private bool _suppressEvents;

        /// <summary>保存成功或设置变更后触发，供主窗体刷新状态栏。</summary>
        public event EventHandler SettingsSaved;

        /// <summary>需要向用户展示的提示（由主窗体转发到状态栏）。</summary>
        public event EventHandler<MessageEventArgs> Notify;

        /// <summary>当前规则列表（供测试校验）。</summary>
        public IList<RoleDefinition> CurrentRoles
        {
            get { return _settings == null ? null : _settings.Roles; }
        }

        // 兼容旧测试/调用方：展开为端口规则视图。
        public IList<PortRule> CurrentRules
        {
            get { return _settings == null ? null : _settings.PortRules; }
        }

        public SettingsPage()
        {
            InitializeComponent();
            ConfigureRuleList();
            lblPhaseNote.Text = "";
        }

        /// <summary>
        /// 初始化规则列表列。
        /// </summary>
        private void ConfigureRuleList()
        {
            listRules.Columns.Clear();

            AddRuleColumn("角色", 130);
            AddRuleColumn("端口列表", 190);
            AddRuleColumn("备注", 200);
            AddRuleColumn("状态", 110);
        }

        private void AddRuleColumn(string text, int width)
        {
            ColumnHeader h = new ColumnHeader();
            h.Text = text;
            h.Width = width;
            listRules.Columns.Add(h);
        }

        /// <summary>
        /// 由主窗体注入已加载的设置。
        /// </summary>
        public void BindSettings(AppSettings settings, string configPath)
        {
            _settings = settings;
            _configPath = configPath;

            _suppressEvents = true;
            try
            {
                numConcurrency.Value = Clamp(settings.MaxConcurrency, 16, 128);
                numTimeout.Value = Clamp(settings.PortTimeoutMs, 200, 5000);
                numPingTimeout.Value = Clamp(settings.PingTimeoutMs, IniSchema.PingTimeoutMinMs, IniSchema.PingTimeoutMaxMs);
                numPingRetry.Value = Clamp(settings.PingRetry, IniSchema.PingRetryMin, IniSchema.PingRetryMax);
            }
            finally
            {
                _suppressEvents = false;
            }

            lblConfigPathValue.Text = configPath;
            UpdateConcurrencyHint();
            UpdateTimeoutHint();

            // 规则表
            RefreshRuleList();
        }

        /// <summary>
        /// 从设置重绘规则列表。
        /// </summary>
        private void RefreshRuleList()
        {
            listRules.BeginUpdate();

            try
            {
                listRules.Items.Clear();

                if (_settings == null || _settings.Roles == null) { return; }

                for (int i = 0; i < _settings.Roles.Count; i++)
                {
                    RoleDefinition r = _settings.Roles[i];
                    ListViewItem item = new ListViewItem(r.Name);
                    item.SubItems.Add(r.PortsText);
                    item.SubItems.Add(r.Comment);
                    item.SubItems.Add(r.Enabled ? "启用" : "已禁用");

                    if (!r.Enabled) { item.ForeColor = Color.FromArgb(140, 140, 140); }
                    else if (!r.IsUsable) { item.ForeColor = Color.Firebrick; item.SubItems[3].Text = "无有效端口"; }
                    listRules.Items.Add(item);
                }
            }
            finally
            {
                listRules.EndUpdate();
            }

            UpdateRulesHint();
        }

        /// <summary>
        /// 规则表下方的提示文字。
        /// </summary>
        private void UpdateRulesHint()
        {
            int count = _settings == null || _settings.Roles == null ? 0 : _settings.Roles.Count;
            int enabled = 0;
            if (_settings != null && _settings.Roles != null)
            {
                for (int i = 0; i < _settings.Roles.Count; i++)
                {
                    if (_settings.Roles[i].IsUsable) { enabled++; }
                }
            }

            if (count == 0)
            {
                lblRulesHint.Text = "⚠ 当前没有任何端口规则，扫描将无法判定设备角色。";
                lblRulesHint.ForeColor = Color.Firebrick;
            }
            else if (enabled == 0)
            {
                lblRulesHint.Text = "⚠ 所有规则均被禁用，扫描将无法判定设备角色。";
                lblRulesHint.ForeColor = Color.Firebrick;
            }
            else
            {
                lblRulesHint.Text = "共 " + count + " 条规则，其中 " + enabled
                    + " 条启用。规则保存在 MQNetScan.ini，重启程序后仍然生效。";
                lblRulesHint.ForeColor = Color.FromArgb(90, 90, 90);
            }
        }

        // -------------------------------------------------------------------
        // 规则增删改
        // -------------------------------------------------------------------
        private void OnAddRuleClick(object sender, EventArgs e)
        {
            using (RoleDefinitionDialog dlg = new RoleDefinitionDialog(_settings, -1))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                _settings.Roles.Add(dlg.Result);
                RefreshRuleList();
                SaveRules(false);
            }
        }

        private void OnEditRuleClick(object sender, EventArgs e)
        {
            int index = GetSelectedRuleIndex();

            if (index < 0)
            {
                MessageBox.Show(this, "请先在列表中选中一条规则。", "编辑规则",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (RoleDefinitionDialog dlg = new RoleDefinitionDialog(_settings, index))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                _settings.Roles[index] = dlg.Result;
                RefreshRuleList();
                SaveRules(false);
            }
        }

        private void OnRuleDoubleClick(object sender, EventArgs e)
        {
            // 双击 = 编辑（此处是规则编辑入口，不属于设备列表的"禁止双击"约束）
            OnEditRuleClick(sender, e);
        }

        private void OnDeleteRuleClick(object sender, EventArgs e)
        {
            int index = GetSelectedRuleIndex();

            if (index < 0)
            {
                MessageBox.Show(this, "请先在列表中选中一条规则。", "删除规则",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            RoleDefinition role = _settings.Roles[index];

            DialogResult r = MessageBox.Show(this,
                "确定删除角色「" + role.Name + "」（端口：" + role.PortsText + "）吗？",
                "删除规则",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (r != DialogResult.Yes) { return; }

            _settings.Roles.RemoveAt(index);

            RefreshRuleList();
            SaveRules(false);
        }

        private void OnRestoreRulesClick(object sender, EventArgs e)
        {
            DialogResult r = MessageBox.Show(this,
                "将把角色定义恢复为内置默认的 5 类，自定义角色会丢失。\r\n\r\n是否继续？",
                "恢复内置规则",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (r != DialogResult.Yes) { return; }

            _settings.Roles = IniSchema.CreateDefaultRoles();

            RefreshRuleList();
            SaveRules(true);
        }

        private int GetSelectedRuleIndex()
        {
            if (listRules.SelectedIndices.Count == 0) { return -1; }
            return listRules.SelectedIndices[0];
        }

        /// <summary>
        /// 保存规则。silent=false 时弹成功提示。
        /// </summary>
        private void SaveRules(bool showSuccess)
        {
            string error;
            bool ok = SettingsService.Save(_settings, _configPath, out error);

            if (!ok)
            {
                MessageBox.Show(this, error, "保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RaiseNotify(error, true);
                return;
            }

            if (showSuccess)
            {
                RaiseNotify("端口规则已保存。", false);
            }
            else
            {
                RaiseNotify("端口规则已更新并保存。", false);
            }

            if (SettingsSaved != null)
            {
                SettingsSaved(this, EventArgs.Empty);
            }
        }

        // -------------------------------------------------------------------
        // 恢复出厂设置
        // -------------------------------------------------------------------
        private void OnFactoryResetClick(object sender, EventArgs e)
        {
            DialogResult r = MessageBox.Show(this,
                "恢复出厂设置将把以下内容全部重置为默认值：\r\n\r\n"
                + "  · 并发数（32）与单端口超时（800ms）\r\n"
                + "  · 角色定义（内置 5 类，自定义角色会丢失）\r\n"
                + "  · 其它配置项（自检目标、导出策略等）\r\n\r\n"
                + "此操作不可撤销。是否继续？",
                "恢复出厂设置",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (r != DialogResult.Yes) { return; }

            AppSettings fresh = new AppSettings();

            _settings.MaxConcurrency = fresh.MaxConcurrency;
            _settings.PortTimeoutMs = fresh.PortTimeoutMs;
            _settings.EnableRedBanner = fresh.EnableRedBanner;
            _settings.SendArpRetry = fresh.SendArpRetry;
            _settings.SaveToDesktop = fresh.SaveToDesktop;
            _settings.TimestampFormat = fresh.TimestampFormat;
            _settings.SaasDomain = fresh.SaasDomain;
            _settings.DnsProbeIp = fresh.DnsProbeIp;
            _settings.GatewayProbePorts = fresh.GatewayProbePorts;
            _settings.LastProbeMode = fresh.LastProbeMode;
            _settings.PingTimeoutMs = fresh.PingTimeoutMs;
            _settings.PingRetry = fresh.PingRetry;
            _settings.Roles = fresh.Roles;

            // 回显控件
            _suppressEvents = true;
            try
            {
                numConcurrency.Value = fresh.MaxConcurrency;
                numTimeout.Value = fresh.PortTimeoutMs;
                numPingTimeout.Value = fresh.PingTimeoutMs;
                numPingRetry.Value = fresh.PingRetry;
            }
            finally
            {
                _suppressEvents = false;
            }

            RefreshRuleList();
            UpdateConcurrencyHint();
            UpdateTimeoutHint();

            SaveRules(true);
            RaiseNotify("已恢复出厂设置。", false);
        }

        private static decimal Clamp(int value, int min, int max)
        {
            if (value < min) { return min; }
            if (value > max) { return max; }
            return value;
        }

        // -------------------------------------------------------------------
        // 事件
        // -------------------------------------------------------------------
        private void OnConcurrencyChanged(object sender, EventArgs e)
        {
            if (_suppressEvents) { return; }
            UpdateConcurrencyHint();
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            if (_settings == null) { return; }

            int concurrency;
            string warn1;
            concurrency = SettingsService.NormalizeConcurrency((int)numConcurrency.Value, out warn1);

            int timeout;
            string warn2;
            timeout = SettingsService.NormalizeTimeout((int)numTimeout.Value, out warn2);

            // 纠正后的值回显到控件
            _suppressEvents = true;
            try
            {
                numConcurrency.Value = concurrency;
                numTimeout.Value = timeout;
            }
            finally
            {
                _suppressEvents = false;
            }

            _settings.MaxConcurrency = concurrency;
            _settings.PortTimeoutMs = timeout;
            _settings.PingTimeoutMs = (int)numPingTimeout.Value;
            _settings.PingRetry = (int)numPingRetry.Value;

            string error;
            bool ok = SettingsService.Save(_settings, _configPath, out error);

            if (!ok)
            {
                // 只读目录等场景：提示但不能崩（PRD FR-4.3.4）
                RaiseNotify(error, true);
                return;
            }

            if (!string.IsNullOrEmpty(warn1)) { RaiseNotify(warn1, false); }
            if (!string.IsNullOrEmpty(warn2)) { RaiseNotify(warn2, false); }

            RaiseNotify("设置已保存。", false);

            if (SettingsSaved != null)
            {
                SettingsSaved(this, EventArgs.Empty);
            }
        }

        private void OnRestoreDefaultsClick(object sender, EventArgs e)
        {
            DialogResult r = MessageBox.Show(
                "将把并发数与超时时间恢复为默认值（并发 " + IniSchema.DefaultMaxConcurrency
                + "，超时 " + IniSchema.DefaultPortTimeoutMs + " ms）。\r\n\r\n是否继续？",
                "恢复默认值",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);

            if (r != DialogResult.OK) { return; }

            _suppressEvents = true;
            try
            {
                numConcurrency.Value = IniSchema.DefaultMaxConcurrency;
                numTimeout.Value = IniSchema.DefaultPortTimeoutMs;
            }
            finally
            {
                _suppressEvents = false;
            }

            UpdateConcurrencyHint();
            UpdateTimeoutHint();

            // 恢复默认后立即保存，避免用户以为已生效但实际未落盘
            OnSaveClick(sender, e);
        }

        // -------------------------------------------------------------------
        // 提示文字
        // -------------------------------------------------------------------
        private void UpdateConcurrencyHint()
        {
            int v = (int)numConcurrency.Value;

            if (v > IniSchema.ConcurrencyWarnThreshold)
            {
                lblConcurrencyHint.Text = "配置较低的电脑建议不超过 " + IniSchema.DefaultMaxConcurrency;
                lblConcurrencyHint.ForeColor = System.Drawing.Color.FromArgb(200, 120, 0);
            }
            else
            {
                lblConcurrencyHint.Text = "(范围 " + IniSchema.ConcurrencyMin + "~" + IniSchema.ConcurrencyMax + ")";
                lblConcurrencyHint.ForeColor = System.Drawing.Color.FromArgb(120, 120, 120);
            }
        }

        private void UpdateTimeoutHint()
        {
            lblTimeoutHint.Text = "(范围 " + IniSchema.TimeoutMinMs + "~" + IniSchema.TimeoutMaxMs + " ms)";
        }

        private void RaiseNotify(string message, bool isError)
        {
            if (Notify != null)
            {
                Notify(this, new MessageEventArgs(message, isError));
            }
        }
    }

    /// <summary>
    /// 携带提示文本的事件参数。
    /// </summary>
    internal sealed class MessageEventArgs : EventArgs
    {
        private readonly string _message;
        private readonly bool _isError;

        public MessageEventArgs(string message, bool isError)
        {
            _message = message;
            _isError = isError;
        }

        public string Message { get { return _message; } }
        public bool IsError { get { return _isError; } }
    }

    /// <summary>
    /// 角色定义编辑对话框：一个角色一次编辑一组端口。
    /// </summary>
    internal sealed class RoleDefinitionDialog : Form
    {
        private readonly AppSettings _settings;
        private readonly int _index;
        private readonly TextBox _name;
        private readonly TextBox _ports;
        private readonly TextBox _comment;
        private readonly CheckBox _enabled;
        private readonly Label _error;
        public RoleDefinition Result { get; private set; }

        public RoleDefinitionDialog(AppSettings settings, int index)
        {
            _settings = settings; _index = index;
            Text = index < 0 ? "新增角色定义" : "编辑角色定义";
            FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ClientSize = new Size(500, 260);
            Label n = new Label(); n.Text = "角色名称："; n.Location = new Point(16, 20); n.Size = new Size(75, 20);
            _name = new TextBox(); _name.Location = new Point(100, 17); _name.Size = new Size(360, 22);
            Label p = new Label(); p.Text = "端口列表："; p.Location = new Point(16, 55); p.Size = new Size(75, 20);
            _ports = new TextBox(); _ports.Location = new Point(100, 52); _ports.Size = new Size(360, 22);
            Label ph = new Label(); ph.Text = "逗号分隔，如 445,3389,135"; ph.ForeColor = Color.Gray; ph.Location = new Point(100, 78); ph.Size = new Size(300, 18);
            Label c = new Label(); c.Text = "备注："; c.Location = new Point(16, 108); c.Size = new Size(75, 20);
            _comment = new TextBox(); _comment.Location = new Point(100, 105); _comment.Size = new Size(360, 22);
            _enabled = new CheckBox(); _enabled.Text = "启用该角色"; _enabled.Location = new Point(100, 138); _enabled.Size = new Size(150, 20); _enabled.Checked = true;
            _error = new Label(); _error.ForeColor = Color.Firebrick; _error.Location = new Point(16, 168); _error.Size = new Size(440, 30);
            Button ok = new Button(); ok.Text = "确定"; ok.Location = new Point(260, 215); ok.Size = new Size(90, 28); ok.Click += OnOk;
            Button cancel = new Button(); cancel.Text = "取消"; cancel.Location = new Point(370, 215); cancel.Size = new Size(90, 28); cancel.DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { n, _name, p, _ports, ph, c, _comment, _enabled, _error, ok, cancel });
            AcceptButton = ok; CancelButton = cancel;
            if (index >= 0 && settings != null && settings.Roles != null && index < settings.Roles.Count)
            {
                RoleDefinition r = settings.Roles[index]; _name.Text = r.Name; _ports.Text = r.PortsText; _comment.Text = r.Comment; _enabled.Checked = r.Enabled;
            }
        }

        private void OnOk(object sender, EventArgs e)
        {
            string ignored; int[] ports = RoleDefinition.ParsePorts(_ports.Text.Trim(), out ignored);
            string name = _name.Text.Trim();
            if (name.Length == 0) { _error.Text = "角色名称不能为空。"; return; }
            if (ports.Length == 0) { _error.Text = "至少配置一个合法端口。"; return; }
            if (ignored.Length > 0) { _error.Text = "非法端口：" + ignored; return; }
            RoleDefinition candidate = new RoleDefinition(name, ports, _comment.Text.Trim(), _enabled.Checked);
            string error = SettingsService.ValidateRole(candidate, _settings.Roles, _index);
            if (error != null) { _error.Text = error; return; }
            Result = candidate; DialogResult = DialogResult.OK; Close();
        }
    }

    /// <summary>
    /// 旧版端口规则编辑对话框（保留用于旧测试兼容）。
    ///
    /// 校验规则（PRD FR-4.2.2）：
    ///   · 端口 1~65535；
    ///   · 同端口不可重复；
    ///   · 角色不可为空；
    ///   · 内置角色从下拉选择，也允许自定义文本（排在内置角色之后）。
    /// </summary>
    internal sealed class PortRuleDialog : Form
    {
        private readonly AppSettings _settings;
        private readonly int _editIndex;   // -1 表示新增

        private TextBox _txtPort;
        private ComboBox _cboRole;
        private TextBox _txtComment;
        private CheckBox _chkEnabled;
        private Button _btnOk;
        private Button _btnCancel;
        private Label _lblError;

        /// <summary>校验通过后的规则结果。</summary>
        public PortRule Result { get; private set; }

        public PortRuleDialog(AppSettings settings, int editIndex)
        {
            _settings = settings;
            _editIndex = editIndex;

            InitializeUi();

            if (editIndex >= 0 && settings != null
                && settings.PortRules != null
                && editIndex < settings.PortRules.Count)
            {
                PortRule existing = settings.PortRules[editIndex];
                _txtPort.Text = existing.Port.ToString();
                _cboRole.Text = existing.Role;
                _txtComment.Text = existing.Comment;
                _chkEnabled.Checked = existing.Enabled;

                this.Text = "编辑端口规则";
            }
            else
            {
                _chkEnabled.Checked = true;
                this.Text = "新增端口规则";
            }
        }

        private void InitializeUi()
        {
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ClientSize = new Size(420, 240);

            Label lblPort = new Label();
            lblPort.Text = "端口号：";
            lblPort.Location = new Point(16, 22);
            lblPort.Size = new Size(70, 16);

            _txtPort = new TextBox();
            _txtPort.Location = new Point(95, 19);
            _txtPort.Size = new Size(120, 22);

            Label lblPortHint = new Label();
            lblPortHint.Text = "(1~65535，不可与现有规则重复)";
            lblPortHint.ForeColor = Color.FromArgb(120, 120, 120);
            lblPortHint.Location = new Point(225, 22);
            lblPortHint.Size = new Size(190, 16);

            Label lblRole = new Label();
            lblRole.Text = "角色：";
            lblRole.Location = new Point(16, 52);
            lblRole.Size = new Size(70, 16);

            _cboRole = new ComboBox();
            _cboRole.Location = new Point(95, 49);
            _cboRole.Size = new Size(300, 24);
            _cboRole.DropDownStyle = ComboBoxStyle.DropDown;   // 允许自定义文本

            _cboRole.Items.Add(RolePriority.RoleLocalServer);
            _cboRole.Items.Add(RolePriority.RoleCashierPc);
            _cboRole.Items.Add(RolePriority.RolePrinter);
            _cboRole.Items.Add(RolePriority.RoleScale);
            _cboRole.Items.Add(RolePriority.RoleAndroidPos);

            Label lblComment = new Label();
            lblComment.Text = "备注：";
            lblComment.Location = new Point(16, 82);
            lblComment.Size = new Size(70, 16);

            _txtComment = new TextBox();
            _txtComment.Location = new Point(95, 79);
            _txtComment.Size = new Size(300, 22);

            _chkEnabled = new CheckBox();
            _chkEnabled.Text = "启用该规则";
            _chkEnabled.Location = new Point(95, 110);
            _chkEnabled.Size = new Size(140, 20);

            _lblError = new Label();
            _lblError.ForeColor = Color.Firebrick;
            _lblError.Location = new Point(16, 140);
            _lblError.Size = new Size(380, 30);

            _btnOk = new Button();
            _btnOk.Text = "确定";
            _btnOk.Location = new Point(190, 185);
            _btnOk.Size = new Size(90, 28);
            _btnOk.Click += OnOkClick;

            _btnCancel = new Button();
            _btnCancel.Text = "取消";
            _btnCancel.Location = new Point(295, 185);
            _btnCancel.Size = new Size(90, 28);
            _btnCancel.DialogResult = DialogResult.Cancel;

            this.Controls.Add(lblPort);
            this.Controls.Add(_txtPort);
            this.Controls.Add(lblPortHint);
            this.Controls.Add(lblRole);
            this.Controls.Add(_cboRole);
            this.Controls.Add(lblComment);
            this.Controls.Add(_txtComment);
            this.Controls.Add(_chkEnabled);
            this.Controls.Add(_lblError);
            this.Controls.Add(_btnOk);
            this.Controls.Add(_btnCancel);

            this.CancelButton = _btnCancel;
            this.AcceptButton = _btnOk;
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            int port;
            string portText = _txtPort.Text.Trim();

            if (!int.TryParse(portText, out port))
            {
                ShowError("端口号必须是数字。");
                return;
            }

            if (port < 1 || port > 65535)
            {
                ShowError("端口号必须在 1~65535 之间。");
                return;
            }

            string role = _cboRole.Text.Trim();

            if (role.Length == 0)
            {
                ShowError("角色名称不能为空。");
                return;
            }

            // 重复校验（编辑自身时排除自己）
            if (_settings != null && _settings.PortRules != null)
            {
                for (int i = 0; i < _settings.PortRules.Count; i++)
                {
                    if (i == _editIndex) { continue; }

                    if (_settings.PortRules[i].Port == port)
                    {
                        ShowError("端口 " + port + " 已存在（当前角色："
                            + _settings.PortRules[i].Role + "），不能重复。");
                        return;
                    }
                }
            }

            Result = new PortRule(port, role, _txtComment.Text.Trim(), _chkEnabled.Checked);

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void ShowError(string text)
        {
            _lblError.Text = "✗ " + text;
        }
    }
}
