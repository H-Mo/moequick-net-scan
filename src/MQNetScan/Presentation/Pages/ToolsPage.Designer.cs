namespace MQNetScan.Presentation.Pages
{
    partial class ToolsPage
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.GroupBox grpCache;
        private System.Windows.Forms.Button btnClearArp;
        private System.Windows.Forms.Button btnFlushDns;
        private System.Windows.Forms.Label lblCacheHint;

        private System.Windows.Forms.GroupBox grpPanels;
        private System.Windows.Forms.Button btnOpenNcpa;
        private System.Windows.Forms.Button btnOpenServices;

        private System.Windows.Forms.GroupBox grpPortTest;
        private System.Windows.Forms.Label lblHost;
        private System.Windows.Forms.TextBox txtHost;
        private System.Windows.Forms.Label lblPort;
        private System.Windows.Forms.TextBox txtPort;
        private System.Windows.Forms.Button btnTestPort;
        private System.Windows.Forms.Label lblPortResult;

        private System.Windows.Forms.GroupBox grpSelfCheck;
        private System.Windows.Forms.ListView listChecks;
        private System.Windows.Forms.Button btnRunCheck;
        private System.Windows.Forms.Label lblCheckTargets;

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
            this.grpCache = new System.Windows.Forms.GroupBox();
            this.lblCacheHint = new System.Windows.Forms.Label();
            this.btnFlushDns = new System.Windows.Forms.Button();
            this.btnClearArp = new System.Windows.Forms.Button();

            this.grpPanels = new System.Windows.Forms.GroupBox();
            this.btnOpenServices = new System.Windows.Forms.Button();
            this.btnOpenNcpa = new System.Windows.Forms.Button();

            this.grpPortTest = new System.Windows.Forms.GroupBox();
            this.lblPortResult = new System.Windows.Forms.Label();
            this.btnTestPort = new System.Windows.Forms.Button();
            this.txtPort = new System.Windows.Forms.TextBox();
            this.lblPort = new System.Windows.Forms.Label();
            this.txtHost = new System.Windows.Forms.TextBox();
            this.lblHost = new System.Windows.Forms.Label();

            this.grpSelfCheck = new System.Windows.Forms.GroupBox();
            this.lblCheckTargets = new System.Windows.Forms.Label();
            this.btnRunCheck = new System.Windows.Forms.Button();
            this.listChecks = new System.Windows.Forms.ListView();

            this.grpCache.SuspendLayout();
            this.grpPanels.SuspendLayout();
            this.grpPortTest.SuspendLayout();
            this.grpSelfCheck.SuspendLayout();
            this.SuspendLayout();

            // grpCache
            this.grpCache.Controls.Add(this.lblCacheHint);
            this.grpCache.Controls.Add(this.btnFlushDns);
            this.grpCache.Controls.Add(this.btnClearArp);
            this.grpCache.Location = new System.Drawing.Point(14, 10);
            this.grpCache.Name = "grpCache";
            this.grpCache.Size = new System.Drawing.Size(420, 92);
            this.grpCache.TabIndex = 0;
            this.grpCache.TabStop = false;
            this.grpCache.Text = "系统缓存";

            // btnClearArp
            this.btnClearArp.Location = new System.Drawing.Point(16, 24);
            this.btnClearArp.Name = "btnClearArp";
            this.btnClearArp.Size = new System.Drawing.Size(130, 30);
            this.btnClearArp.TabIndex = 0;
            this.btnClearArp.Text = "一键清理 ARP 缓存";
            this.btnClearArp.UseVisualStyleBackColor = true;
            this.btnClearArp.Click += new System.EventHandler(this.OnClearArpClick);

            // btnFlushDns
            this.btnFlushDns.Location = new System.Drawing.Point(156, 24);
            this.btnFlushDns.Name = "btnFlushDns";
            this.btnFlushDns.Size = new System.Drawing.Size(120, 30);
            this.btnFlushDns.TabIndex = 1;
            this.btnFlushDns.Text = "刷新 DNS 缓存";
            this.btnFlushDns.UseVisualStyleBackColor = true;
            this.btnFlushDns.Click += new System.EventHandler(this.OnFlushDnsClick);

            // lblCacheHint
            this.lblCacheHint.AutoSize = true;
            this.lblCacheHint.ForeColor = System.Drawing.Color.FromArgb(120, 120, 120);
            this.lblCacheHint.Location = new System.Drawing.Point(16, 62);
            this.lblCacheHint.Name = "lblCacheHint";
            this.lblCacheHint.Size = new System.Drawing.Size(0, 12);
            this.lblCacheHint.TabIndex = 2;

            // grpPanels
            this.grpPanels.Controls.Add(this.btnOpenServices);
            this.grpPanels.Controls.Add(this.btnOpenNcpa);
            this.grpPanels.Location = new System.Drawing.Point(448, 10);
            this.grpPanels.Name = "grpPanels";
            this.grpPanels.Size = new System.Drawing.Size(300, 92);
            this.grpPanels.TabIndex = 1;
            this.grpPanels.TabStop = false;
            this.grpPanels.Text = "系统面板";

            // btnOpenNcpa
            this.btnOpenNcpa.Location = new System.Drawing.Point(16, 24);
            this.btnOpenNcpa.Name = "btnOpenNcpa";
            this.btnOpenNcpa.Size = new System.Drawing.Size(125, 30);
            this.btnOpenNcpa.TabIndex = 0;
            this.btnOpenNcpa.Text = "网络连接";
            this.btnOpenNcpa.UseVisualStyleBackColor = true;
            this.btnOpenNcpa.Click += new System.EventHandler(this.OnOpenNcpaClick);

            // btnOpenServices
            this.btnOpenServices.Location = new System.Drawing.Point(151, 24);
            this.btnOpenServices.Name = "btnOpenServices";
            this.btnOpenServices.Size = new System.Drawing.Size(125, 30);
            this.btnOpenServices.TabIndex = 1;
            this.btnOpenServices.Text = "服务管理器";
            this.btnOpenServices.UseVisualStyleBackColor = true;
            this.btnOpenServices.Click += new System.EventHandler(this.OnOpenServicesClick);

            // grpPortTest
            this.grpPortTest.Controls.Add(this.lblPortResult);
            this.grpPortTest.Controls.Add(this.btnTestPort);
            this.grpPortTest.Controls.Add(this.txtPort);
            this.grpPortTest.Controls.Add(this.lblPort);
            this.grpPortTest.Controls.Add(this.txtHost);
            this.grpPortTest.Controls.Add(this.lblHost);
            this.grpPortTest.Location = new System.Drawing.Point(14, 114);
            this.grpPortTest.Name = "grpPortTest";
            this.grpPortTest.Size = new System.Drawing.Size(734, 92);
            this.grpPortTest.TabIndex = 2;
            this.grpPortTest.TabStop = false;
            this.grpPortTest.Text = "单点握手测通";

            // lblHost
            this.lblHost.AutoSize = true;
            this.lblHost.Location = new System.Drawing.Point(16, 30);
            this.lblHost.Name = "lblHost";
            this.lblHost.Size = new System.Drawing.Size(65, 12);
            this.lblHost.TabIndex = 0;
            this.lblHost.Text = "IP / 域名：";

            // txtHost
            this.txtHost.Location = new System.Drawing.Point(90, 27);
            this.txtHost.Name = "txtHost";
            this.txtHost.Size = new System.Drawing.Size(220, 21);
            this.txtHost.TabIndex = 1;

            // lblPort
            this.lblPort.AutoSize = true;
            this.lblPort.Location = new System.Drawing.Point(326, 30);
            this.lblPort.Name = "lblPort";
            this.lblPort.Size = new System.Drawing.Size(41, 12);
            this.lblPort.TabIndex = 2;
            this.lblPort.Text = "端口：";

            // txtPort
            this.txtPort.Location = new System.Drawing.Point(373, 27);
            this.txtPort.Name = "txtPort";
            this.txtPort.Size = new System.Drawing.Size(80, 21);
            this.txtPort.TabIndex = 3;
            this.txtPort.Text = "80";

            // btnTestPort
            this.btnTestPort.Location = new System.Drawing.Point(470, 25);
            this.btnTestPort.Name = "btnTestPort";
            this.btnTestPort.Size = new System.Drawing.Size(90, 26);
            this.btnTestPort.TabIndex = 4;
            this.btnTestPort.Text = "测通";
            this.btnTestPort.UseVisualStyleBackColor = true;
            this.btnTestPort.Click += new System.EventHandler(this.OnTestPortClick);

            // lblPortResult
            this.lblPortResult.AutoSize = true;
            this.lblPortResult.Location = new System.Drawing.Point(16, 62);
            this.lblPortResult.MaximumSize = new System.Drawing.Size(700, 0);
            this.lblPortResult.Name = "lblPortResult";
            this.lblPortResult.Size = new System.Drawing.Size(0, 12);
            this.lblPortResult.TabIndex = 5;

            // grpSelfCheck
            this.grpSelfCheck.Controls.Add(this.listChecks);
            this.grpSelfCheck.Controls.Add(this.lblCheckTargets);
            this.grpSelfCheck.Controls.Add(this.btnRunCheck);
            this.grpSelfCheck.Location = new System.Drawing.Point(14, 218);
            this.grpSelfCheck.Name = "grpSelfCheck";
            this.grpSelfCheck.Size = new System.Drawing.Size(734, 240);
            this.grpSelfCheck.TabIndex = 3;
            this.grpSelfCheck.TabStop = false;
            this.grpSelfCheck.Text = "连通性自检";

            // btnRunCheck
            this.btnRunCheck.Location = new System.Drawing.Point(16, 22);
            this.btnRunCheck.Name = "btnRunCheck";
            this.btnRunCheck.Size = new System.Drawing.Size(110, 26);
            this.btnRunCheck.TabIndex = 0;
            this.btnRunCheck.Text = "重新自检";
            this.btnRunCheck.UseVisualStyleBackColor = true;
            this.btnRunCheck.Click += new System.EventHandler(this.OnRunCheckClick);

            // lblCheckTargets
            this.lblCheckTargets.AutoSize = true;
            this.lblCheckTargets.ForeColor = System.Drawing.Color.FromArgb(120, 120, 120);
            this.lblCheckTargets.Location = new System.Drawing.Point(140, 29);
            this.lblCheckTargets.Name = "lblCheckTargets";
            this.lblCheckTargets.Size = new System.Drawing.Size(0, 12);
            this.lblCheckTargets.TabIndex = 1;

            // listChecks
            this.listChecks.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            this.listChecks.FullRowSelect = true;
            this.listChecks.GridLines = true;
            this.listChecks.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
            this.listChecks.Location = new System.Drawing.Point(16, 56);
            this.listChecks.MultiSelect = false;
            this.listChecks.Name = "listChecks";
            this.listChecks.Size = new System.Drawing.Size(700, 170);
            this.listChecks.TabIndex = 2;
            this.listChecks.UseCompatibleStateImageBehavior = false;
            this.listChecks.View = System.Windows.Forms.View.Details;

            // ToolsPage
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.grpSelfCheck);
            this.Controls.Add(this.grpPortTest);
            this.Controls.Add(this.grpPanels);
            this.Controls.Add(this.grpCache);
            this.Name = "ToolsPage";
            this.Size = new System.Drawing.Size(886, 542);

            this.grpCache.ResumeLayout(false);
            this.grpCache.PerformLayout();
            this.grpPanels.ResumeLayout(false);
            this.grpPortTest.ResumeLayout(false);
            this.grpPortTest.PerformLayout();
            this.grpSelfCheck.ResumeLayout(false);
            this.grpSelfCheck.PerformLayout();
            this.ResumeLayout(false);
        }
    }
}
