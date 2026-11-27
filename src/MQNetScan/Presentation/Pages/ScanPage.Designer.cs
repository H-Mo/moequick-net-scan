namespace MQNetScan.Presentation.Pages
{
    partial class ScanPage
    {
        private System.ComponentModel.IContainer components = null;

        // 注意：Dispose(bool) 的实现在 ScanPage.cs 中（需先停止扫描线程）。
        // 此处不再重复定义，否则编译报"已定义"。

        private System.Windows.Forms.GroupBox grpTarget;
        private System.Windows.Forms.Label lblAdapter;
        private System.Windows.Forms.ComboBox cboAdapter;
        private System.Windows.Forms.Button btnRefreshAdapter;
        private System.Windows.Forms.RadioButton rbCidr;
        private System.Windows.Forms.RadioButton rbRange;
        private System.Windows.Forms.Label lblProbeMode;
        private System.Windows.Forms.Panel pnlProbeMode;
        private System.Windows.Forms.RadioButton rbProbeArp;
        private System.Windows.Forms.RadioButton rbProbePing;
        private System.Windows.Forms.Label lblProbeHint;
        private System.Windows.Forms.TextBox txtCidr;
        private System.Windows.Forms.TextBox txtRangeStart;
        private System.Windows.Forms.Label lblRangeSep;
        private System.Windows.Forms.TextBox txtRangeEnd;
        private System.Windows.Forms.Label lblPreview;
        private System.Windows.Forms.Label lblValidation;
        private System.Windows.Forms.Label lblAdapterWarning;

        private System.Windows.Forms.Button btnStartScan;
        private System.Windows.Forms.Button btnPause;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label lblScanStatus;
        private System.Windows.Forms.ListView listHosts;
        private System.Windows.Forms.ColumnHeader colDummy;
        private System.Windows.Forms.ContextMenuStrip menuHosts;
        private System.Windows.Forms.ToolStripMenuItem miCopyIp;
        private System.Windows.Forms.ToolStripMenuItem miCopyMac;
        private System.Windows.Forms.ToolStripMenuItem miCopyRow;
        private System.Windows.Forms.ToolStripSeparator miSep1;
        private System.Windows.Forms.ToolStripMenuItem miDeepTest;
        private System.Windows.Forms.ToolStripMenuItem miOpenWeb;
        private System.Windows.Forms.ToolStripMenuItem miOpenRdp;
        private System.Windows.Forms.ToolStripSeparator miSep2;
        private System.Windows.Forms.ToolStripMenuItem miSingleTest;
        private System.Windows.Forms.Button btnExport;
        private System.Windows.Forms.Button btnOpenReportFolder;

        private void InitializeComponent()
        {
            this.grpTarget = new System.Windows.Forms.GroupBox();
            this.lblAdapter = new System.Windows.Forms.Label();
            this.cboAdapter = new System.Windows.Forms.ComboBox();
            this.btnRefreshAdapter = new System.Windows.Forms.Button();
            this.rbCidr = new System.Windows.Forms.RadioButton();
            this.rbRange = new System.Windows.Forms.RadioButton();
            this.lblProbeMode = new System.Windows.Forms.Label();
            this.pnlProbeMode = new System.Windows.Forms.Panel();
            this.rbProbeArp = new System.Windows.Forms.RadioButton();
            this.rbProbePing = new System.Windows.Forms.RadioButton();
            this.lblProbeHint = new System.Windows.Forms.Label();
            this.txtCidr = new System.Windows.Forms.TextBox();
            this.txtRangeStart = new System.Windows.Forms.TextBox();
            this.lblRangeSep = new System.Windows.Forms.Label();
            this.txtRangeEnd = new System.Windows.Forms.TextBox();
            this.lblPreview = new System.Windows.Forms.Label();
            this.lblValidation = new System.Windows.Forms.Label();
            this.lblAdapterWarning = new System.Windows.Forms.Label();
            this.btnStartScan = new System.Windows.Forms.Button();
            this.btnPause = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.lblScanStatus = new System.Windows.Forms.Label();
            this.listHosts = new System.Windows.Forms.ListView();
            this.colDummy = new System.Windows.Forms.ColumnHeader();
            this.menuHosts = new System.Windows.Forms.ContextMenuStrip();
            this.miCopyIp = new System.Windows.Forms.ToolStripMenuItem();
            this.miCopyMac = new System.Windows.Forms.ToolStripMenuItem();
            this.miCopyRow = new System.Windows.Forms.ToolStripMenuItem();
            this.miSep1 = new System.Windows.Forms.ToolStripSeparator();
            this.miDeepTest = new System.Windows.Forms.ToolStripMenuItem();
            this.miOpenWeb = new System.Windows.Forms.ToolStripMenuItem();
            this.miOpenRdp = new System.Windows.Forms.ToolStripMenuItem();
            this.miSep2 = new System.Windows.Forms.ToolStripSeparator();
            this.miSingleTest = new System.Windows.Forms.ToolStripMenuItem();
            this.btnExport = new System.Windows.Forms.Button();
            this.btnOpenReportFolder = new System.Windows.Forms.Button();

            this.grpTarget.SuspendLayout();
            this.SuspendLayout();

            // grpTarget
            this.grpTarget.Controls.Add(this.lblValidation);
            this.grpTarget.Controls.Add(this.pnlProbeMode);
            this.grpTarget.Controls.Add(this.lblProbeMode);
            this.grpTarget.Controls.Add(this.lblPreview);
            this.grpTarget.Controls.Add(this.txtRangeEnd);
            this.grpTarget.Controls.Add(this.lblRangeSep);
            this.grpTarget.Controls.Add(this.txtRangeStart);
            this.grpTarget.Controls.Add(this.txtCidr);
            this.grpTarget.Controls.Add(this.rbRange);
            this.grpTarget.Controls.Add(this.rbCidr);
            this.grpTarget.Controls.Add(this.btnRefreshAdapter);
            this.grpTarget.Controls.Add(this.cboAdapter);
            this.grpTarget.Controls.Add(this.lblAdapter);
            this.grpTarget.Location = new System.Drawing.Point(14, 10);
            this.grpTarget.Name = "grpTarget";
            this.grpTarget.Size = new System.Drawing.Size(840, 146);
            this.grpTarget.TabIndex = 0;
            this.grpTarget.TabStop = false;
            this.grpTarget.Text = "扫描目标";

            // lblAdapter
            this.lblAdapter.AutoSize = true;
            this.lblAdapter.Location = new System.Drawing.Point(16, 26);
            this.lblAdapter.Name = "lblAdapter";
            this.lblAdapter.Size = new System.Drawing.Size(53, 12);
            this.lblAdapter.TabIndex = 0;
            this.lblAdapter.Text = "网卡：";

            // cboAdapter
            this.cboAdapter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboAdapter.Location = new System.Drawing.Point(70, 23);
            this.cboAdapter.Name = "cboAdapter";
            this.cboAdapter.Size = new System.Drawing.Size(660, 20);
            this.cboAdapter.TabIndex = 1;
            this.cboAdapter.SelectedIndexChanged += new System.EventHandler(this.OnAdapterChanged);

            // btnRefreshAdapter
            this.btnRefreshAdapter.Location = new System.Drawing.Point(740, 21);
            this.btnRefreshAdapter.Name = "btnRefreshAdapter";
            this.btnRefreshAdapter.Size = new System.Drawing.Size(80, 24);
            this.btnRefreshAdapter.TabIndex = 2;
            this.btnRefreshAdapter.Text = "刷新";
            this.btnRefreshAdapter.UseVisualStyleBackColor = true;
            this.btnRefreshAdapter.Click += new System.EventHandler(this.OnRefreshAdapterClick);

            // rbCidr
            this.rbCidr.AutoSize = true;
            this.rbCidr.Checked = true;
            this.rbCidr.Location = new System.Drawing.Point(18, 60);
            this.rbCidr.Name = "rbCidr";
            this.rbCidr.Size = new System.Drawing.Size(71, 16);
            this.rbCidr.TabIndex = 3;
            this.rbCidr.TabStop = true;
            this.rbCidr.Text = "网段：";
            this.rbCidr.UseVisualStyleBackColor = true;
            this.rbCidr.CheckedChanged += new System.EventHandler(this.OnModeChanged);

            // txtCidr
            this.txtCidr.Location = new System.Drawing.Point(95, 57);
            this.txtCidr.Name = "txtCidr";
            this.txtCidr.Size = new System.Drawing.Size(200, 21);
            this.txtCidr.TabIndex = 4;
            this.txtCidr.TextChanged += new System.EventHandler(this.OnTargetTextChanged);

            // rbRange
            this.rbRange.AutoSize = true;
            this.rbRange.Location = new System.Drawing.Point(320, 60);
            this.rbRange.Name = "rbRange";
            this.rbRange.Size = new System.Drawing.Size(95, 16);
            this.rbRange.TabIndex = 5;
            this.rbRange.Text = "起止 IP：";
            this.rbRange.UseVisualStyleBackColor = true;
            this.rbRange.CheckedChanged += new System.EventHandler(this.OnModeChanged);

            // txtRangeStart
            this.txtRangeStart.Enabled = false;
            this.txtRangeStart.Location = new System.Drawing.Point(415, 57);
            this.txtRangeStart.Name = "txtRangeStart";
            this.txtRangeStart.Size = new System.Drawing.Size(140, 21);
            this.txtRangeStart.TabIndex = 6;
            this.txtRangeStart.TextChanged += new System.EventHandler(this.OnTargetTextChanged);

            // lblRangeSep
            this.lblRangeSep.AutoSize = true;
            this.lblRangeSep.Enabled = false;
            this.lblRangeSep.Location = new System.Drawing.Point(561, 60);
            this.lblRangeSep.Name = "lblRangeSep";
            this.lblRangeSep.Size = new System.Drawing.Size(17, 12);
            this.lblRangeSep.TabIndex = 7;
            this.lblRangeSep.Text = "~";

            // txtRangeEnd
            this.txtRangeEnd.Enabled = false;
            this.txtRangeEnd.Location = new System.Drawing.Point(584, 57);
            this.txtRangeEnd.Name = "txtRangeEnd";
            this.txtRangeEnd.Size = new System.Drawing.Size(140, 21);
            this.txtRangeEnd.TabIndex = 8;
            this.txtRangeEnd.TextChanged += new System.EventHandler(this.OnTargetTextChanged);

            // lblProbeMode
            this.lblProbeMode.AutoSize = true;
            this.lblProbeMode.Location = new System.Drawing.Point(18, 84);
            this.lblProbeMode.Name = "lblProbeMode";
            this.lblProbeMode.Size = new System.Drawing.Size(65, 12);
            this.lblProbeMode.TabIndex = 11;
            this.lblProbeMode.Text = "探测方式：";

            // pnlProbeMode
            this.pnlProbeMode.Location = new System.Drawing.Point(95, 78);
            this.pnlProbeMode.Name = "pnlProbeMode";
            this.pnlProbeMode.Size = new System.Drawing.Size(115, 24);
            this.pnlProbeMode.TabIndex = 12;
            this.pnlProbeMode.Controls.Add(this.rbProbePing);
            this.pnlProbeMode.Controls.Add(this.rbProbeArp);

            // rbProbeArp
            this.rbProbeArp.AutoSize = true;
            this.rbProbeArp.Checked = true;
            this.rbProbeArp.Location = new System.Drawing.Point(0, 4);
            this.rbProbeArp.Name = "rbProbeArp";
            this.rbProbeArp.Size = new System.Drawing.Size(47, 16);
            this.rbProbeArp.TabIndex = 0;
            this.rbProbeArp.TabStop = true;
            this.rbProbeArp.Text = "ARP";
            this.rbProbeArp.UseVisualStyleBackColor = true;
            this.rbProbeArp.CheckedChanged += new System.EventHandler(this.OnProbeModeChanged);

            // rbProbePing
            this.rbProbePing.AutoSize = true;
            this.rbProbePing.Location = new System.Drawing.Point(58, 4);
            this.rbProbePing.Name = "rbProbePing";
            this.rbProbePing.Size = new System.Drawing.Size(47, 16);
            this.rbProbePing.TabIndex = 1;
            this.rbProbePing.Text = "Ping";
            this.rbProbePing.UseVisualStyleBackColor = true;
            this.rbProbePing.CheckedChanged += new System.EventHandler(this.OnProbeModeChanged);

            // lblProbeHint
            this.lblProbeHint.AutoSize = true;
            this.lblProbeHint.ForeColor = System.Drawing.Color.FromArgb(100, 100, 100);
            this.lblProbeHint.Location = new System.Drawing.Point(220, 84);
            this.lblProbeHint.Name = "lblProbeHint";
            this.lblProbeHint.Size = new System.Drawing.Size(0, 12);
            this.lblProbeHint.TabIndex = 14;

            // lblPreview
            this.lblPreview.AutoSize = true;
            this.lblPreview.Location = new System.Drawing.Point(18, 106);
            this.lblPreview.Name = "lblPreview";
            this.lblPreview.Size = new System.Drawing.Size(0, 12);
            this.lblPreview.TabIndex = 9;

            // lblValidation
            this.lblValidation.AutoSize = true;
            this.lblValidation.Location = new System.Drawing.Point(18, 130);
            this.lblValidation.MaximumSize = new System.Drawing.Size(800, 0);
            this.lblValidation.Name = "lblValidation";
            this.lblValidation.Size = new System.Drawing.Size(0, 12);
            this.lblValidation.TabIndex = 10;

            // lblAdapterWarning（放在分组框外，独立一行，避免压住边框）
            this.lblAdapterWarning.AutoSize = true;
            this.lblAdapterWarning.ForeColor = System.Drawing.Color.FromArgb(180, 100, 0);
            this.lblAdapterWarning.Location = new System.Drawing.Point(18, 166);
            this.lblAdapterWarning.MaximumSize = new System.Drawing.Size(840, 0);
            this.lblAdapterWarning.Name = "lblAdapterWarning";
            this.lblAdapterWarning.Size = new System.Drawing.Size(0, 12);
            this.lblAdapterWarning.TabIndex = 1;

            // btnStartScan
            this.btnStartScan.Location = new System.Drawing.Point(14, 194);
            this.btnStartScan.Name = "btnStartScan";
            this.btnStartScan.Size = new System.Drawing.Size(104, 30);
            this.btnStartScan.TabIndex = 2;
            this.btnStartScan.Text = "开始扫描";
            this.btnStartScan.UseVisualStyleBackColor = true;
            this.btnStartScan.Click += new System.EventHandler(this.OnStartScanClick);

            // btnPause
            this.btnPause.Enabled = false;
            this.btnPause.Location = new System.Drawing.Point(126, 194);
            this.btnPause.Name = "btnPause";
            this.btnPause.Size = new System.Drawing.Size(88, 30);
            this.btnPause.TabIndex = 3;
            this.btnPause.Text = "暂停";
            this.btnPause.UseVisualStyleBackColor = true;
            this.btnPause.Click += new System.EventHandler(this.OnPauseClick);

            // btnStop
            this.btnStop.Enabled = false;
            this.btnStop.Location = new System.Drawing.Point(222, 194);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(88, 30);
            this.btnStop.TabIndex = 4;
            this.btnStop.Text = "终止";
            this.btnStop.UseVisualStyleBackColor = true;
            this.btnStop.Click += new System.EventHandler(this.OnStopClick);

            // progressBar
            this.progressBar.Location = new System.Drawing.Point(326, 198);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(528, 22);
            this.progressBar.TabIndex = 5;

            // lblScanStatus
            this.lblScanStatus.AutoSize = true;
            this.lblScanStatus.ForeColor = System.Drawing.Color.FromArgb(90, 90, 90);
            this.lblScanStatus.Location = new System.Drawing.Point(14, 234);
            this.lblScanStatus.MaximumSize = new System.Drawing.Size(840, 0);
            this.lblScanStatus.Name = "lblScanStatus";
            this.lblScanStatus.Size = new System.Drawing.Size(0, 12);
            this.lblScanStatus.TabIndex = 6;

            // listHosts
            //
            // 注意：此处**刻意不绑定 DoubleClick / MouseDoubleClick**（PRD FR-1.6.1）。
            // 这是架构级保证，而不是运行时判断——没有事件绑定，双击就不可能触发任何动作。
            this.listHosts.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            this.listHosts.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
                this.colDummy});
            this.listHosts.FullRowSelect = true;
            this.listHosts.GridLines = true;
            this.listHosts.HideSelection = false;
            this.listHosts.Location = new System.Drawing.Point(14, 290);
            this.listHosts.Name = "listHosts";
            this.listHosts.Size = new System.Drawing.Size(840, 238);
            this.listHosts.TabIndex = 7;
            this.listHosts.UseCompatibleStateImageBehavior = false;
            this.listHosts.View = System.Windows.Forms.View.Details;

            // colDummy（真正的列在 ListViewHelper.ConfigureColumns 中动态建立）
            this.colDummy.Text = "";
            this.colDummy.Width = 0;

            // menuHosts（右键菜单，6 项操作 + 分隔线）
            this.menuHosts.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.miCopyIp,
                this.miCopyMac,
                this.miCopyRow,
                this.miSep1,
                this.miDeepTest,
                this.miOpenWeb,
                this.miOpenRdp,
                this.miSep2,
                this.miSingleTest});
            this.menuHosts.Name = "menuHosts";
            this.menuHosts.Size = new System.Drawing.Size(200, 170);

            // miCopyIp
            this.miCopyIp.Name = "miCopyIp";
            this.miCopyIp.Text = "复制 IP 地址";
            this.miCopyIp.Click += new System.EventHandler(this.OnCopyIpClick);

            // miCopyMac
            this.miCopyMac.Name = "miCopyMac";
            this.miCopyMac.Text = "复制 MAC 地址";
            this.miCopyMac.Click += new System.EventHandler(this.OnCopyMacClick);

            // miCopyRow
            this.miCopyRow.Name = "miCopyRow";
            this.miCopyRow.Text = "复制整行（IP + 名称 + MAC + 端口 + 角色）";
            this.miCopyRow.Click += new System.EventHandler(this.OnCopyRowClick);

            // miSep1
            this.miSep1.Name = "miSep1";

            // miDeepTest
            this.miDeepTest.Name = "miDeepTest";
            this.miDeepTest.Text = "深度测速（3 次握手）";
            this.miDeepTest.Click += new System.EventHandler(this.OnDeepTestClick);

            // miOpenWeb
            this.miOpenWeb.Name = "miOpenWeb";
            this.miOpenWeb.Text = "浏览器打开 Web 后台";
            this.miOpenWeb.Click += new System.EventHandler(this.OnOpenWebClick);

            // miOpenRdp
            this.miOpenRdp.Name = "miOpenRdp";
            this.miOpenRdp.Text = "打开远程桌面（mstsc）";
            this.miOpenRdp.Click += new System.EventHandler(this.OnOpenRdpClick);

            // miSep2
            this.miSep2.Name = "miSep2";

            // miSingleTest
            this.miSingleTest.Name = "miSingleTest";
            this.miSingleTest.Text = "单点测通（跳转便捷功能）";
            this.miSingleTest.Click += new System.EventHandler(this.OnSingleTestClick);

            // btnExport
            this.btnExport.Location = new System.Drawing.Point(14, 254);
            this.btnExport.Name = "btnExport";
            this.btnExport.Size = new System.Drawing.Size(110, 24);
            this.btnExport.TabIndex = 8;
            this.btnExport.Text = "导出诊断报告";
            this.btnExport.UseVisualStyleBackColor = true;
            this.btnExport.Click += new System.EventHandler(this.OnExportReportClick);

            // btnOpenReportFolder
            this.btnOpenReportFolder.Enabled = false;
            this.btnOpenReportFolder.Location = new System.Drawing.Point(132, 254);
            this.btnOpenReportFolder.Name = "btnOpenReportFolder";
            this.btnOpenReportFolder.Size = new System.Drawing.Size(130, 24);
            this.btnOpenReportFolder.TabIndex = 9;
            this.btnOpenReportFolder.Text = "打开所在文件夹";
            this.btnOpenReportFolder.UseVisualStyleBackColor = true;
            this.btnOpenReportFolder.Click += new System.EventHandler(this.OnOpenReportFolderClick);

            // ScanPage
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.btnOpenReportFolder);
            this.Controls.Add(this.btnExport);
            this.Controls.Add(this.listHosts);
            this.Controls.Add(this.lblScanStatus);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.btnStop);
            this.Controls.Add(this.btnPause);
            this.Controls.Add(this.btnStartScan);
            this.Controls.Add(this.lblAdapterWarning);
            this.Controls.Add(this.grpTarget);
            this.Name = "ScanPage";
            this.Size = new System.Drawing.Size(886, 542);

            this.grpTarget.ResumeLayout(false);
            this.grpTarget.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
