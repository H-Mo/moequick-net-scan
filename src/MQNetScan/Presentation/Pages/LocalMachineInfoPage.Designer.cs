namespace MQNetScan.Presentation.Pages
{
    partial class LocalMachineInfoPage
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.ListBox listCategories;
        private System.Windows.Forms.Label lblLoadState;
        private System.Windows.Forms.Label lblUpdated;
        private System.Windows.Forms.Button btnRefresh;
        private System.Windows.Forms.ListView listInfo;
        private System.Windows.Forms.ColumnHeader colName;
        private System.Windows.Forms.ColumnHeader colValue;
        private System.Windows.Forms.ContextMenuStrip menuInfo;
        private System.Windows.Forms.ToolStripMenuItem miCopyInfo;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.MarkDisposed();
                if (components != null) { components.Dispose(); }
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.listCategories = new System.Windows.Forms.ListBox();
            this.lblLoadState = new System.Windows.Forms.Label();
            this.lblUpdated = new System.Windows.Forms.Label();
            this.btnRefresh = new System.Windows.Forms.Button();
            this.listInfo = new System.Windows.Forms.ListView();
            this.colName = new System.Windows.Forms.ColumnHeader();
            this.colValue = new System.Windows.Forms.ColumnHeader();
            this.menuInfo = new System.Windows.Forms.ContextMenuStrip();
            this.miCopyInfo = new System.Windows.Forms.ToolStripMenuItem();
            this.SuspendLayout();

            // listCategories
            this.listCategories.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) | System.Windows.Forms.AnchorStyles.Left)));
            this.listCategories.FormattingEnabled = true;
            this.listCategories.ItemHeight = 18;
            this.listCategories.Location = new System.Drawing.Point(16, 16);
            this.listCategories.Name = "listCategories";
            this.listCategories.Size = new System.Drawing.Size(132, 508);
            this.listCategories.TabIndex = 0;

            // lblLoadState
            this.lblLoadState.AutoSize = false;
            this.lblLoadState.ForeColor = System.Drawing.Color.FromArgb(90, 90, 90);
            this.lblLoadState.Location = new System.Drawing.Point(164, 17);
            this.lblLoadState.Size = new System.Drawing.Size(360, 26);
            this.lblLoadState.Name = "lblLoadState";
            this.lblLoadState.Size = new System.Drawing.Size(0, 12);
            this.lblLoadState.TabIndex = 1;

            // lblUpdated
            this.lblUpdated.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblUpdated.AutoSize = false;
            this.lblUpdated.ForeColor = System.Drawing.Color.FromArgb(120, 120, 120);
            this.lblUpdated.Location = new System.Drawing.Point(620, 25);
            this.lblUpdated.MaximumSize = new System.Drawing.Size(138, 18);
            this.lblUpdated.Name = "lblUpdated";
            this.lblUpdated.Size = new System.Drawing.Size(138, 18);
            this.lblUpdated.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblUpdated.TabIndex = 2;

            // btnRefresh
            this.btnRefresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnRefresh.Location = new System.Drawing.Point(766, 17);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(100, 28);
            this.btnRefresh.TabIndex = 3;
            this.btnRefresh.Text = "刷新当前页";
            this.btnRefresh.UseVisualStyleBackColor = true;
            this.btnRefresh.Click += new System.EventHandler(this.OnRefreshClick);

            // listInfo
            this.listInfo.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.listInfo.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { this.colName, this.colValue });
            this.listInfo.FullRowSelect = true;
            this.listInfo.GridLines = true;
            this.listInfo.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
            this.listInfo.HideSelection = false;
            this.listInfo.Location = new System.Drawing.Point(164, 56);
            this.listInfo.MultiSelect = false;
            this.listInfo.Name = "listInfo";
            this.listInfo.Size = new System.Drawing.Size(702, 468);
            this.listInfo.TabIndex = 4;
            this.listInfo.ContextMenuStrip = this.menuInfo;
            this.listInfo.MouseDown += new System.Windows.Forms.MouseEventHandler(this.OnInfoListMouseDown);
            this.listInfo.UseCompatibleStateImageBehavior = false;
            this.listInfo.View = System.Windows.Forms.View.Details;

            // menuInfo
            this.menuInfo.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { this.miCopyInfo });
            this.menuInfo.Name = "menuInfo";
            this.menuInfo.Size = new System.Drawing.Size(101, 26);
            this.menuInfo.Opening += new System.ComponentModel.CancelEventHandler(this.OnInfoMenuOpening);

            // miCopyInfo
            this.miCopyInfo.Name = "miCopyInfo";
            this.miCopyInfo.Size = new System.Drawing.Size(100, 22);
            this.miCopyInfo.Text = "复制";
            this.miCopyInfo.Click += new System.EventHandler(this.OnCopyInfoClick);

            // colName
            this.colName.Text = "项目";
            this.colName.Width = 190;
            // colValue
            this.colValue.Text = "信息";
            this.colValue.Width = 490;

            // LocalMachineInfoPage
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.listInfo);
            this.Controls.Add(this.btnRefresh);
            this.Controls.Add(this.lblUpdated);
            this.Controls.Add(this.lblLoadState);
            this.Controls.Add(this.listCategories);
            this.Name = "LocalMachineInfoPage";
            this.Size = new System.Drawing.Size(886, 542);
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
