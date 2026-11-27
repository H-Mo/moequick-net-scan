namespace MQNetScan.Presentation.Pages
{
    partial class SettingsPage
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.GroupBox grpScanParams;
        private System.Windows.Forms.Label lblConcurrency;
        private System.Windows.Forms.NumericUpDown numConcurrency;
        private System.Windows.Forms.Label lblConcurrencyHint;
        private System.Windows.Forms.Label lblTimeout;
        private System.Windows.Forms.NumericUpDown numTimeout;
        private System.Windows.Forms.Label lblTimeoutHint;
        private System.Windows.Forms.Label lblPingTimeout;
        private System.Windows.Forms.NumericUpDown numPingTimeout;
        private System.Windows.Forms.Label lblPingRetry;
        private System.Windows.Forms.NumericUpDown numPingRetry;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnRestoreDefaults;
        private System.Windows.Forms.Label lblConfigPath;
        private System.Windows.Forms.Label lblConfigPathValue;
        private System.Windows.Forms.Label lblPhaseNote;

        private System.Windows.Forms.GroupBox grpRules;
        private System.Windows.Forms.ListView listRules;
        private System.Windows.Forms.Button btnAddRule;
        private System.Windows.Forms.Button btnEditRule;
        private System.Windows.Forms.Button btnDeleteRule;
        private System.Windows.Forms.Button btnRestoreRules;
        private System.Windows.Forms.Label lblRulesHint;

        private System.Windows.Forms.GroupBox grpFactory;
        private System.Windows.Forms.Button btnFactoryReset;
        private System.Windows.Forms.GroupBox grpAbout;
        private System.Windows.Forms.Label lblCurrentVersion;
        private System.Windows.Forms.Label lblUpdateStatus;
        private System.Windows.Forms.Button btnCheckUpdates;
        private System.Windows.Forms.Button btnDownloadUpdate;
        private System.Windows.Forms.Button btnOpenWebsite;
        private System.Windows.Forms.Button btnOpenGithub;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.grpScanParams = new System.Windows.Forms.GroupBox();
            this.lblConcurrency = new System.Windows.Forms.Label();
            this.numConcurrency = new System.Windows.Forms.NumericUpDown();
            this.lblConcurrencyHint = new System.Windows.Forms.Label();
            this.lblTimeout = new System.Windows.Forms.Label();
            this.numTimeout = new System.Windows.Forms.NumericUpDown();
            this.lblTimeoutHint = new System.Windows.Forms.Label();
            this.lblPingTimeout = new System.Windows.Forms.Label();
            this.numPingTimeout = new System.Windows.Forms.NumericUpDown();
            this.lblPingRetry = new System.Windows.Forms.Label();
            this.numPingRetry = new System.Windows.Forms.NumericUpDown();
            this.btnSave = new System.Windows.Forms.Button();
            this.btnRestoreDefaults = new System.Windows.Forms.Button();
            this.lblConfigPath = new System.Windows.Forms.Label();
            this.lblConfigPathValue = new System.Windows.Forms.Label();
            this.lblPhaseNote = new System.Windows.Forms.Label();

            this.grpRules = new System.Windows.Forms.GroupBox();
            this.listRules = new System.Windows.Forms.ListView();
            this.btnAddRule = new System.Windows.Forms.Button();
            this.btnEditRule = new System.Windows.Forms.Button();
            this.btnDeleteRule = new System.Windows.Forms.Button();
            this.btnRestoreRules = new System.Windows.Forms.Button();
            this.lblRulesHint = new System.Windows.Forms.Label();

            this.grpFactory = new System.Windows.Forms.GroupBox();
            this.btnFactoryReset = new System.Windows.Forms.Button();
            this.grpAbout = new System.Windows.Forms.GroupBox();
            this.lblCurrentVersion = new System.Windows.Forms.Label();
            this.lblUpdateStatus = new System.Windows.Forms.Label();
            this.btnCheckUpdates = new System.Windows.Forms.Button();
            this.btnDownloadUpdate = new System.Windows.Forms.Button();
            this.btnOpenWebsite = new System.Windows.Forms.Button();
            this.btnOpenGithub = new System.Windows.Forms.Button();

            this.grpScanParams.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numConcurrency)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTimeout)).BeginInit();
            this.grpRules.SuspendLayout();
            this.SuspendLayout();

            // grpScanParams
            this.grpScanParams.Controls.Add(this.numPingRetry);
            this.grpScanParams.Controls.Add(this.lblPingRetry);
            this.grpScanParams.Controls.Add(this.numPingTimeout);
            this.grpScanParams.Controls.Add(this.lblPingTimeout);
            this.grpScanParams.Controls.Add(this.lblTimeoutHint);
            this.grpScanParams.Controls.Add(this.numTimeout);
            this.grpScanParams.Controls.Add(this.lblTimeout);
            this.grpScanParams.Controls.Add(this.lblConcurrencyHint);
            this.grpScanParams.Controls.Add(this.numConcurrency);
            this.grpScanParams.Controls.Add(this.lblConcurrency);
            this.grpScanParams.Location = new System.Drawing.Point(18, 18);
            this.grpScanParams.Name = "grpScanParams";
            this.grpScanParams.Size = new System.Drawing.Size(848, 120);
            this.grpScanParams.TabIndex = 0;
            this.grpScanParams.TabStop = false;
            this.grpScanParams.Text = "扫描参数";

            // lblConcurrency
            this.lblConcurrency.AutoSize = true;
            this.lblConcurrency.Location = new System.Drawing.Point(20, 34);
            this.lblConcurrency.Name = "lblConcurrency";
            this.lblConcurrency.Size = new System.Drawing.Size(101, 12);
            this.lblConcurrency.TabIndex = 0;
            this.lblConcurrency.Text = "最大并发线程数：";

            // numConcurrency
            this.numConcurrency.Location = new System.Drawing.Point(130, 31);
            this.numConcurrency.Maximum = new decimal(new int[] { 128, 0, 0, 0 });
            this.numConcurrency.Minimum = new decimal(new int[] { 16, 0, 0, 0 });
            this.numConcurrency.Name = "numConcurrency";
            this.numConcurrency.Size = new System.Drawing.Size(80, 21);
            this.numConcurrency.TabIndex = 1;
            this.numConcurrency.Value = new decimal(new int[] { 32, 0, 0, 0 });
            this.numConcurrency.ValueChanged += new System.EventHandler(this.OnConcurrencyChanged);

            // lblConcurrencyHint
            this.lblConcurrencyHint.AutoSize = true;
            this.lblConcurrencyHint.ForeColor = System.Drawing.Color.FromArgb(120, 120, 120);
            this.lblConcurrencyHint.Location = new System.Drawing.Point(222, 34);
            this.lblConcurrencyHint.Name = "lblConcurrencyHint";
            this.lblConcurrencyHint.Size = new System.Drawing.Size(0, 12);
            this.lblConcurrencyHint.TabIndex = 2;

            // lblTimeout
            this.lblTimeout.AutoSize = true;
            this.lblTimeout.Location = new System.Drawing.Point(430, 34);
            this.lblTimeout.Name = "lblTimeout";
            this.lblTimeout.Size = new System.Drawing.Size(101, 12);
            this.lblTimeout.TabIndex = 3;
            this.lblTimeout.Text = "单端口超时时间：";

            // numTimeout
            this.numTimeout.Increment = new decimal(new int[] { 50, 0, 0, 0 });
            this.numTimeout.Location = new System.Drawing.Point(540, 31);
            this.numTimeout.Maximum = new decimal(new int[] { 5000, 0, 0, 0 });
            this.numTimeout.Minimum = new decimal(new int[] { 200, 0, 0, 0 });
            this.numTimeout.Name = "numTimeout";
            this.numTimeout.Size = new System.Drawing.Size(80, 21);
            this.numTimeout.TabIndex = 4;
            this.numTimeout.Value = new decimal(new int[] { 800, 0, 0, 0 });

            // lblTimeoutHint
            this.lblTimeoutHint.AutoSize = true;
            this.lblTimeoutHint.ForeColor = System.Drawing.Color.FromArgb(120, 120, 120);
            this.lblTimeoutHint.Location = new System.Drawing.Point(632, 34);
            this.lblTimeoutHint.Name = "lblTimeoutHint";
            this.lblTimeoutHint.Size = new System.Drawing.Size(0, 12);
            this.lblTimeoutHint.TabIndex = 5;

            // lblPingTimeout
            this.lblPingTimeout.AutoSize = true;
            this.lblPingTimeout.Location = new System.Drawing.Point(20, 78);
            this.lblPingTimeout.Name = "lblPingTimeout";
            this.lblPingTimeout.Size = new System.Drawing.Size(101, 12);
            this.lblPingTimeout.TabIndex = 6;
            this.lblPingTimeout.Text = "Ping 超时：";

            // numPingTimeout
            this.numPingTimeout.Increment = new decimal(new int[] { 100, 0, 0, 0 });
            this.numPingTimeout.Location = new System.Drawing.Point(130, 75);
            this.numPingTimeout.Maximum = new decimal(new int[] { 5000, 0, 0, 0 });
            this.numPingTimeout.Minimum = new decimal(new int[] { 200, 0, 0, 0 });
            this.numPingTimeout.Name = "numPingTimeout";
            this.numPingTimeout.Size = new System.Drawing.Size(80, 21);
            this.numPingTimeout.TabIndex = 7;
            this.numPingTimeout.Value = new decimal(new int[] { 800, 0, 0, 0 });

            // lblPingRetry
            this.lblPingRetry.AutoSize = true;
            this.lblPingRetry.Location = new System.Drawing.Point(430, 78);
            this.lblPingRetry.Name = "lblPingRetry";
            this.lblPingRetry.Size = new System.Drawing.Size(101, 12);
            this.lblPingRetry.TabIndex = 8;
            this.lblPingRetry.Text = "Ping 尝试次数：";

            // numPingRetry
            this.numPingRetry.Location = new System.Drawing.Point(540, 75);
            this.numPingRetry.Maximum = new decimal(new int[] { 3, 0, 0, 0 });
            this.numPingRetry.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numPingRetry.Name = "numPingRetry";
            this.numPingRetry.Size = new System.Drawing.Size(80, 21);
            this.numPingRetry.TabIndex = 9;
            this.numPingRetry.Value = new decimal(new int[] { 1, 0, 0, 0 });

            // btnSave
            this.btnSave.Location = new System.Drawing.Point(18, 160);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(100, 28);
            this.btnSave.TabIndex = 1;
            this.btnSave.Text = "保存设置";
            this.btnSave.UseVisualStyleBackColor = true;
            this.btnSave.Click += new System.EventHandler(this.OnSaveClick);

            // btnRestoreDefaults
            this.btnRestoreDefaults.Location = new System.Drawing.Point(130, 160);
            this.btnRestoreDefaults.Name = "btnRestoreDefaults";
            this.btnRestoreDefaults.Size = new System.Drawing.Size(100, 28);
            this.btnRestoreDefaults.TabIndex = 2;
            this.btnRestoreDefaults.Text = "恢复默认值";
            this.btnRestoreDefaults.UseVisualStyleBackColor = true;
            this.btnRestoreDefaults.Click += new System.EventHandler(this.OnRestoreDefaultsClick);

            // lblConfigPath
            this.lblConfigPath.AutoSize = true;
            this.lblConfigPath.Location = new System.Drawing.Point(18, 200);
            this.lblConfigPath.Name = "lblConfigPath";
            this.lblConfigPath.Size = new System.Drawing.Size(77, 12);
            this.lblConfigPath.TabIndex = 3;
            this.lblConfigPath.Text = "配置文件：";

            // lblConfigPathValue
            this.lblConfigPathValue.AutoSize = true;
            this.lblConfigPathValue.ForeColor = System.Drawing.Color.FromArgb(90, 90, 90);
            this.lblConfigPathValue.Location = new System.Drawing.Point(101, 200);
            this.lblConfigPathValue.Name = "lblConfigPathValue";
            this.lblConfigPathValue.Size = new System.Drawing.Size(0, 12);
            this.lblConfigPathValue.TabIndex = 4;

            // lblPhaseNote
            this.lblPhaseNote.AutoSize = true;
            this.lblPhaseNote.ForeColor = System.Drawing.Color.FromArgb(120, 120, 120);
            this.lblPhaseNote.Location = new System.Drawing.Point(18, 224);
            this.lblPhaseNote.MaximumSize = new System.Drawing.Size(700, 0);
            this.lblPhaseNote.Name = "lblPhaseNote";
            this.lblPhaseNote.Size = new System.Drawing.Size(0, 12);
            this.lblPhaseNote.TabIndex = 5;

            // grpRules
            this.grpRules.Controls.Add(this.lblRulesHint);
            this.grpRules.Controls.Add(this.btnRestoreRules);
            this.grpRules.Controls.Add(this.btnDeleteRule);
            this.grpRules.Controls.Add(this.btnEditRule);
            this.grpRules.Controls.Add(this.btnAddRule);
            this.grpRules.Controls.Add(this.listRules);
            this.grpRules.Location = new System.Drawing.Point(18, 248);
            this.grpRules.Name = "grpRules";
            this.grpRules.Size = new System.Drawing.Size(848, 276);
            this.grpRules.TabIndex = 6;
            this.grpRules.TabStop = false;
            this.grpRules.Text = "端口特征规则表（决定设备角色判定）";

            // listRules
            this.listRules.FullRowSelect = true;
            this.listRules.GridLines = true;
            this.listRules.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
            this.listRules.HideSelection = false;
            this.listRules.Location = new System.Drawing.Point(12, 22);
            this.listRules.MultiSelect = false;
            this.listRules.Name = "listRules";
            this.listRules.Size = new System.Drawing.Size(560, 202);
            this.listRules.TabIndex = 0;
            this.listRules.UseCompatibleStateImageBehavior = false;
            this.listRules.View = System.Windows.Forms.View.Details;
            this.listRules.DoubleClick += new System.EventHandler(this.OnRuleDoubleClick);

            // btnAddRule
            this.btnAddRule.Location = new System.Drawing.Point(584, 22);
            this.btnAddRule.Name = "btnAddRule";
            this.btnAddRule.Size = new System.Drawing.Size(110, 26);
            this.btnAddRule.TabIndex = 1;
            this.btnAddRule.Text = "新增规则";
            this.btnAddRule.UseVisualStyleBackColor = true;
            this.btnAddRule.Click += new System.EventHandler(this.OnAddRuleClick);

            // btnEditRule
            this.btnEditRule.Location = new System.Drawing.Point(704, 22);
            this.btnEditRule.Name = "btnEditRule";
            this.btnEditRule.Size = new System.Drawing.Size(110, 26);
            this.btnEditRule.TabIndex = 2;
            this.btnEditRule.Text = "编辑规则";
            this.btnEditRule.UseVisualStyleBackColor = true;
            this.btnEditRule.Click += new System.EventHandler(this.OnEditRuleClick);

            // btnDeleteRule
            this.btnDeleteRule.Location = new System.Drawing.Point(584, 54);
            this.btnDeleteRule.Name = "btnDeleteRule";
            this.btnDeleteRule.Size = new System.Drawing.Size(110, 26);
            this.btnDeleteRule.TabIndex = 3;
            this.btnDeleteRule.Text = "删除规则";
            this.btnDeleteRule.UseVisualStyleBackColor = true;
            this.btnDeleteRule.Click += new System.EventHandler(this.OnDeleteRuleClick);

            // btnRestoreRules
            this.btnRestoreRules.Location = new System.Drawing.Point(704, 54);
            this.btnRestoreRules.Name = "btnRestoreRules";
            this.btnRestoreRules.Size = new System.Drawing.Size(110, 26);
            this.btnRestoreRules.TabIndex = 4;
            this.btnRestoreRules.Text = "恢复内置规则";
            this.btnRestoreRules.UseVisualStyleBackColor = true;
            this.btnRestoreRules.Click += new System.EventHandler(this.OnRestoreRulesClick);

            // lblRulesHint
            this.lblRulesHint.AutoSize = true;
            this.lblRulesHint.ForeColor = System.Drawing.Color.FromArgb(120, 120, 120);
            this.lblRulesHint.Location = new System.Drawing.Point(12, 152);
            this.lblRulesHint.MaximumSize = new System.Drawing.Size(820, 0);
            this.lblRulesHint.Name = "lblRulesHint";
            this.lblRulesHint.Size = new System.Drawing.Size(0, 12);
            this.lblRulesHint.TabIndex = 5;

            // grpFactory
            this.grpFactory.Controls.Add(this.btnFactoryReset);
            this.grpFactory.Location = new System.Drawing.Point(18, 534);
            this.grpFactory.Name = "grpFactory";
            this.grpFactory.Size = new System.Drawing.Size(848, 50);
            this.grpFactory.TabIndex = 7;
            this.grpFactory.TabStop = false;
            this.grpFactory.Text = "危险操作";

            // grpAbout
            this.grpAbout.Controls.Add(this.btnOpenGithub);
            this.grpAbout.Controls.Add(this.btnOpenWebsite);
            this.grpAbout.Controls.Add(this.btnDownloadUpdate);
            this.grpAbout.Controls.Add(this.btnCheckUpdates);
            this.grpAbout.Controls.Add(this.lblUpdateStatus);
            this.grpAbout.Controls.Add(this.lblCurrentVersion);
            this.grpAbout.Location = new System.Drawing.Point(18, 596);
            this.grpAbout.Name = "grpAbout";
            this.grpAbout.Size = new System.Drawing.Size(848, 148);
            this.grpAbout.TabIndex = 8;
            this.grpAbout.TabStop = false;
            this.grpAbout.Text = "关于";

            // lblCurrentVersion
            this.lblCurrentVersion.AutoSize = true;
            this.lblCurrentVersion.Location = new System.Drawing.Point(12, 24);
            this.lblCurrentVersion.Name = "lblCurrentVersion";
            this.lblCurrentVersion.Size = new System.Drawing.Size(0, 12);
            this.lblCurrentVersion.TabIndex = 0;

            // lblUpdateStatus
            this.lblUpdateStatus.AutoSize = true;
            this.lblUpdateStatus.ForeColor = System.Drawing.Color.FromArgb(90, 90, 90);
            this.lblUpdateStatus.Location = new System.Drawing.Point(12, 48);
            this.lblUpdateStatus.MaximumSize = new System.Drawing.Size(800, 40);
            this.lblUpdateStatus.Name = "lblUpdateStatus";
            this.lblUpdateStatus.Size = new System.Drawing.Size(0, 12);
            this.lblUpdateStatus.TabIndex = 1;

            // btnCheckUpdates
            this.btnCheckUpdates.Location = new System.Drawing.Point(12, 78);
            this.btnCheckUpdates.Name = "btnCheckUpdates";
            this.btnCheckUpdates.Size = new System.Drawing.Size(110, 26);
            this.btnCheckUpdates.TabIndex = 2;
            this.btnCheckUpdates.Text = "检测更新";
            this.btnCheckUpdates.UseVisualStyleBackColor = true;
            this.btnCheckUpdates.Click += new System.EventHandler(this.OnCheckUpdatesClick);

            // btnDownloadUpdate
            this.btnDownloadUpdate.Enabled = false;
            this.btnDownloadUpdate.Location = new System.Drawing.Point(130, 78);
            this.btnDownloadUpdate.Name = "btnDownloadUpdate";
            this.btnDownloadUpdate.Size = new System.Drawing.Size(110, 26);
            this.btnDownloadUpdate.TabIndex = 3;
            this.btnDownloadUpdate.Text = "前往下载";
            this.btnDownloadUpdate.UseVisualStyleBackColor = true;
            this.btnDownloadUpdate.Click += new System.EventHandler(this.OnDownloadUpdateClick);

            // btnOpenWebsite
            this.btnOpenWebsite.Location = new System.Drawing.Point(584, 78);
            this.btnOpenWebsite.Name = "btnOpenWebsite";
            this.btnOpenWebsite.Size = new System.Drawing.Size(110, 26);
            this.btnOpenWebsite.TabIndex = 4;
            this.btnOpenWebsite.Text = "官方网站";
            this.btnOpenWebsite.UseVisualStyleBackColor = true;
            this.btnOpenWebsite.Click += new System.EventHandler(this.OnOpenWebsiteClick);

            // btnOpenGithub
            this.btnOpenGithub.Location = new System.Drawing.Point(702, 78);
            this.btnOpenGithub.Name = "btnOpenGithub";
            this.btnOpenGithub.Size = new System.Drawing.Size(110, 26);
            this.btnOpenGithub.TabIndex = 5;
            this.btnOpenGithub.Text = "GitHub 仓库";
            this.btnOpenGithub.UseVisualStyleBackColor = true;
            this.btnOpenGithub.Click += new System.EventHandler(this.OnOpenGithubClick);

            // btnFactoryReset
            this.btnFactoryReset.Location = new System.Drawing.Point(12, 16);
            this.btnFactoryReset.Name = "btnFactoryReset";
            this.btnFactoryReset.Size = new System.Drawing.Size(180, 26);
            this.btnFactoryReset.TabIndex = 0;
            this.btnFactoryReset.Text = "恢复出厂设置（全部重置）";
            this.btnFactoryReset.UseVisualStyleBackColor = true;
            this.btnFactoryReset.Click += new System.EventHandler(this.OnFactoryResetClick);

            // SettingsPage
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoScroll = true;
            this.Controls.Add(this.grpAbout);
            this.Controls.Add(this.grpFactory);
            this.Controls.Add(this.grpRules);
            this.Controls.Add(this.lblPhaseNote);
            this.Controls.Add(this.lblConfigPathValue);
            this.Controls.Add(this.lblConfigPath);
            this.Controls.Add(this.btnRestoreDefaults);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.grpScanParams);
            this.Name = "SettingsPage";
            this.Size = new System.Drawing.Size(886, 542);

            this.grpScanParams.ResumeLayout(false);
            this.grpScanParams.PerformLayout();
            this.grpRules.ResumeLayout(false);
            this.grpRules.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numConcurrency)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTimeout)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
