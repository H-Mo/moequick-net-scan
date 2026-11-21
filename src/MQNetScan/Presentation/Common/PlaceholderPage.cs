using System.Drawing;
using System.Windows.Forms;

namespace MQNetScan.Presentation.Common
{
    /// <summary>
    /// 占位页基类：S0 骨架阶段用于展示"该 Tab 后续要做什么"。
    /// 后续阶段各 Tab 实现真实功能后，逐步替换为具体控件。
    /// </summary>
    internal class PlaceholderPage : UserControl
    {
        private Label _label;

        public PlaceholderPage()
        {
            InitializePlaceholder();
        }

        public PlaceholderPage(string title, string body)
        {
            InitializePlaceholder();
            SetContent(title, body);
        }

        private void InitializePlaceholder()
        {
            this._label = new Label();
            this.SuspendLayout();

            this._label.Dock = DockStyle.Fill;
            this._label.Font = new Font("Microsoft Sans Serif", 10F, FontStyle.Regular);
            this._label.ForeColor = Color.FromArgb(90, 90, 90);
            this._label.Name = "lblPlaceholder";
            this._label.Padding = new Padding(24);
            this._label.TextAlign = ContentAlignment.MiddleLeft;
            this._label.Size = new Size(800, 500);

            this.Controls.Add(this._label);
            this.Name = "PlaceholderPage";
            this.Size = new Size(800, 500);
            this.ResumeLayout(false);
        }

        protected void SetContent(string title, string body)
        {
            this._label.Text = title + "\r\n\r\n" + body;
        }
    }
}
