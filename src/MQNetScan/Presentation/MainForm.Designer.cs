namespace MQNetScan.Presentation
{
    partial class MainForm
    {
        /// <summary>
        /// 设计器所需变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.TabControl tabMain;
        private System.Windows.Forms.TabPage tabScan;
        private System.Windows.Forms.TabPage tabLocalInfo;
        private System.Windows.Forms.TabPage tabTools;
        private System.Windows.Forms.TabPage tabSettings;
        private MQNetScan.Presentation.Common.AlertBanner alertBanner;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;
        private System.Windows.Forms.ToolStripStatusLabel lblSpring;
        private System.Windows.Forms.ToolStripStatusLabel lblNetInfo;

        private MQNetScan.Presentation.Pages.ScanPage scanPage;
        private MQNetScan.Presentation.Pages.LocalMachineInfoPage localInfoPage;
        private MQNetScan.Presentation.Pages.ToolsPage toolsPage;
        private MQNetScan.Presentation.Pages.SettingsPage settingsPage;

        /// <summary>
        /// 清理正在使用的资源。
        /// </summary>
        /// <param name="disposing">是否释放托管资源。</param>
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
            this.tabMain = new System.Windows.Forms.TabControl();
            this.tabScan = new System.Windows.Forms.TabPage();
            this.tabLocalInfo = new System.Windows.Forms.TabPage();
            this.tabTools = new System.Windows.Forms.TabPage();
            this.tabSettings = new System.Windows.Forms.TabPage();
            this.alertBanner = new MQNetScan.Presentation.Common.AlertBanner();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.lblStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblSpring = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblNetInfo = new System.Windows.Forms.ToolStripStatusLabel();
            this.scanPage = new MQNetScan.Presentation.Pages.ScanPage();
            this.toolsPage = new MQNetScan.Presentation.Pages.ToolsPage();
            this.settingsPage = new MQNetScan.Presentation.Pages.SettingsPage();
            this.localInfoPage = new MQNetScan.Presentation.Pages.LocalMachineInfoPage();
            this.tabMain.SuspendLayout();
            this.tabScan.SuspendLayout();
            this.tabTools.SuspendLayout();
            this.tabSettings.SuspendLayout();
            this.statusStrip.SuspendLayout();
            this.SuspendLayout();

            // tabMain
            this.tabMain.Controls.Add(this.tabScan);
            this.tabMain.Controls.Add(this.tabLocalInfo);
            this.tabMain.Controls.Add(this.tabTools);
            this.tabMain.Controls.Add(this.tabSettings);
            this.tabMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabMain.Location = new System.Drawing.Point(0, 0);
            this.tabMain.Name = "tabMain";
            this.tabMain.SelectedIndex = 0;
            this.tabMain.SelectedIndexChanged += new System.EventHandler(this.OnMainTabChanged);
            this.tabMain.Size = new System.Drawing.Size(900, 574);
            this.tabMain.TabIndex = 0;

            // tabScan
            this.tabScan.Controls.Add(this.scanPage);
            this.tabScan.Location = new System.Drawing.Point(4, 22);
            this.tabScan.Name = "tabScan";
            this.tabScan.Padding = new System.Windows.Forms.Padding(3);
            this.tabScan.Size = new System.Drawing.Size(892, 548);
            this.tabScan.TabIndex = 0;
            this.tabScan.Text = MQNetScan.Presentation.Common.UiText.TabScan;
            this.tabScan.UseVisualStyleBackColor = true;

            // scanPage
            this.scanPage.Dock = System.Windows.Forms.DockStyle.Fill;
            this.scanPage.Location = new System.Drawing.Point(3, 3);
            this.scanPage.Name = "scanPage";
            this.scanPage.Size = new System.Drawing.Size(886, 542);
            this.scanPage.TabIndex = 0;

            // tabLocalInfo
            this.tabLocalInfo.Controls.Add(this.localInfoPage);
            this.tabLocalInfo.Location = new System.Drawing.Point(4, 22);
            this.tabLocalInfo.Name = "tabLocalInfo";
            this.tabLocalInfo.Padding = new System.Windows.Forms.Padding(3);
            this.tabLocalInfo.Size = new System.Drawing.Size(892, 548);
            this.tabLocalInfo.TabIndex = 1;
            this.tabLocalInfo.Text = MQNetScan.Presentation.Common.UiText.TabLocalInfo;
            this.tabLocalInfo.UseVisualStyleBackColor = true;

            // localInfoPage
            this.localInfoPage.Dock = System.Windows.Forms.DockStyle.Fill;
            this.localInfoPage.Location = new System.Drawing.Point(3, 3);
            this.localInfoPage.Name = "localInfoPage";
            this.localInfoPage.Size = new System.Drawing.Size(886, 542);
            this.localInfoPage.TabIndex = 0;

            // tabTools
            this.tabTools.Controls.Add(this.toolsPage);
            this.tabTools.Location = new System.Drawing.Point(4, 22);
            this.tabTools.Name = "tabTools";
            this.tabTools.Padding = new System.Windows.Forms.Padding(3);
            this.tabTools.Size = new System.Drawing.Size(892, 548);
            this.tabTools.TabIndex = 2;
            this.tabTools.Text = MQNetScan.Presentation.Common.UiText.TabTools;
            this.tabTools.UseVisualStyleBackColor = true;

            // toolsPage
            this.toolsPage.Dock = System.Windows.Forms.DockStyle.Fill;
            this.toolsPage.Location = new System.Drawing.Point(3, 3);
            this.toolsPage.Name = "toolsPage";
            this.toolsPage.Size = new System.Drawing.Size(886, 542);
            this.toolsPage.TabIndex = 0;

            // tabSettings
            this.tabSettings.Controls.Add(this.settingsPage);
            this.tabSettings.Location = new System.Drawing.Point(4, 22);
            this.tabSettings.Name = "tabSettings";
            this.tabSettings.Padding = new System.Windows.Forms.Padding(3);
            this.tabSettings.Size = new System.Drawing.Size(892, 548);
            this.tabSettings.TabIndex = 3;
            this.tabSettings.Text = MQNetScan.Presentation.Common.UiText.TabSettings;
            this.tabSettings.UseVisualStyleBackColor = true;

            // settingsPage
            this.settingsPage.Dock = System.Windows.Forms.DockStyle.Fill;
            this.settingsPage.Location = new System.Drawing.Point(3, 3);
            this.settingsPage.Name = "settingsPage";
            this.settingsPage.Size = new System.Drawing.Size(886, 542);
            this.settingsPage.TabIndex = 0;

            // statusStrip
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.lblStatus,
                this.lblSpring,
                this.lblNetInfo});
            this.statusStrip.Location = new System.Drawing.Point(0, 574);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(900, 22);
            this.statusStrip.TabIndex = 2;

            // alertBanner
            //
            // 注意：默认 Visible=false 且 Height=0，正常状态下**完全不占位**（PRD FR-1.5.4）。
            // Dock=Top 且先于 tabMain 加入控件，保证它显示在 Tab 之上。
            this.alertBanner.Dock = System.Windows.Forms.DockStyle.Top;
            this.alertBanner.Height = 0;
            this.alertBanner.Name = "alertBanner";
            this.alertBanner.Visible = false;

            // lblStatus
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Text = MQNetScan.Presentation.Common.UiText.StatusReady;

            // lblSpring
            this.lblSpring.Name = "lblSpring";
            this.lblSpring.Spring = true;
            this.lblSpring.Text = "";

            // lblNetInfo
            this.lblNetInfo.Name = "lblNetInfo";
            this.lblNetInfo.Text = "并发: 32 | 超时: 800ms";

            // MainForm
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(900, 596);
            this.Controls.Add(this.tabMain);
            this.Controls.Add(this.alertBanner);
            this.Controls.Add(this.statusStrip);
            this.MinimumSize = new System.Drawing.Size(900, 600);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = MQNetScan.Presentation.Common.UiText.AppTitle;

            this.tabMain.ResumeLayout(false);
            this.tabScan.ResumeLayout(false);
            this.tabTools.ResumeLayout(false);
            this.tabSettings.ResumeLayout(false);
            this.statusStrip.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
