using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MQNetScan.Engine.Contracts;

namespace MQNetScan.Presentation.Common
{
    /// <summary>
    /// 顶部红色预警横幅。
    ///
    /// 设计要点（PRD FR-1.5.3 / FR-1.5.4）：
    ///   · 正常状态时**完全不占位**（Visible=false，不显示空白条）；
    ///   · 内容为「结论 + 一句话建议」；
    ///   · 可关闭（X），但同一问题再次出现时会重新显示；
    ///   · 多条告警时合并显示，避免堆叠占用过多屏幕。
    /// </summary>
    internal sealed class AlertBanner : Panel
    {
        private readonly Label _label;
        private readonly Button _closeButton;

        /// <summary>被用户关闭过的告警 key，用于"同一问题再次出现时重新显示"。</summary>
        private readonly HashSet<string> _dismissed = new HashSet<string>();

        private List<AlertItem> _current = new List<AlertItem>();

        public AlertBanner()
        {
            this.SuspendLayout();

            this.Visible = false;
            this.Dock = DockStyle.Top;
            this.Height = 0;          // 正常时不占位
            this.BackColor = Color.FromArgb(253, 231, 233);
            this.Padding = new Padding(0);

            _label = new Label();
            _label.AutoSize = false;
            _label.Dock = DockStyle.Fill;
            _label.ForeColor = Color.FromArgb(160, 30, 30);
            _label.Font = new Font("Microsoft Sans Serif", 9F, FontStyle.Bold);
            _label.TextAlign = ContentAlignment.MiddleLeft;
            _label.Padding = new Padding(10, 0, 30, 0);
            _label.BackColor = Color.Transparent;

            _closeButton = new Button();
            _closeButton.Text = "✕";
            _closeButton.Dock = DockStyle.Right;
            _closeButton.Width = 28;
            _closeButton.FlatStyle = FlatStyle.Flat;
            _closeButton.FlatAppearance.BorderSize = 0;
            _closeButton.BackColor = Color.Transparent;
            _closeButton.ForeColor = Color.FromArgb(160, 30, 30);
            _closeButton.Cursor = Cursors.Hand;
            _closeButton.TabStop = false;
            _closeButton.Click += OnCloseClick;

            this.Controls.Add(_label);
            this.Controls.Add(_closeButton);

            this.ResumeLayout(false);
        }

        /// <summary>当前显示的告警数量。</summary>
        public int AlertCount { get { return _current.Count; } }

        /// <summary>横幅是否处于显示状态。</summary>
        public bool IsShowing { get { return this.Visible; } }

        /// <summary>当前横幅文本（供测试校验）。</summary>
        public string BannerText { get { return _label.Text; } }

        /// <summary>
        /// 设置告警列表。空列表则隐藏横幅且不占位。
        /// </summary>
        public void SetAlerts(IList<AlertItem> alerts)
        {
            List<AlertItem> critical = new List<AlertItem>();

            if (alerts != null)
            {
                for (int i = 0; i < alerts.Count; i++)
                {
                    if (alerts[i] != null && alerts[i].IsCritical)
                    {
                        critical.Add(alerts[i]);
                    }
                }
            }

            _current = critical;

            if (critical.Count == 0)
            {
                HideBanner();
                return;
            }

            // 若全部告警都已被用户关闭过，则保持隐藏；
            // 只要有新的（未关闭过的）告警出现，就重新显示（PRD FR-1.5.3）。
            bool hasNew = false;

            for (int i = 0; i < critical.Count; i++)
            {
                if (!_dismissed.Contains(KeyOf(critical[i]))) { hasNew = true; }
            }

            if (!hasNew)
            {
                HideBanner();
                return;
            }

            ShowBanner(BuildText(critical));
        }

        /// <summary>
        /// 追加一条告警（不清除已有）。
        /// </summary>
        public void AddAlert(AlertItem alert)
        {
            if (alert == null || !alert.IsCritical) { return; }

            List<AlertItem> merged = new List<AlertItem>(_current);
            merged.Add(alert);

            SetAlerts(merged);
        }

        /// <summary>
        /// 清空并隐藏横幅。
        /// </summary>
        public void Clear()
        {
            _current = new List<AlertItem>();
            _dismissed.Clear();
            HideBanner();
        }

        private static string BuildText(IList<AlertItem> alerts)
        {
            if (alerts == null || alerts.Count == 0) { return ""; }

            if (alerts.Count == 1) { return alerts[0].ToBannerText(); }

            // 多条：第一条完整显示，其余以数量概括，避免横幅过高
            return alerts[0].ToBannerText()
                + "　（另有 " + (alerts.Count - 1) + " 项异常，详见导出报告）";
        }

        private static string KeyOf(AlertItem alert)
        {
            if (alert == null) { return ""; }

            // 用类型 + IP 作为唯一标识：同一网关不可达算同一问题，
            // 但不同 IP 的冲突算不同问题。
            return alert.Kind.ToString() + "|" + (alert.IpText == null ? "" : alert.IpText);
        }

        private void OnCloseClick(object sender, EventArgs e)
        {
            // 记录为"已关闭"，同一问题不再自动弹出；出现新问题时仍会弹出
            for (int i = 0; i < _current.Count; i++)
            {
                _dismissed.Add(KeyOf(_current[i]));
            }

            HideBanner();
        }

        private void ShowBanner(string text)
        {
            _label.Text = text;

            // 单行横幅高度；过长文本自动换行时适当增高
            this.Height = 34;
            this.Visible = true;
        }

        private void HideBanner()
        {
            this.Visible = false;
            this.Height = 0;      // 关键：不占位（PRD FR-1.5.4）
            _label.Text = "";
        }
    }
}
