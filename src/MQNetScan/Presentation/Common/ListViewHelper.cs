using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using MQNetScan.Engine;
using MQNetScan.Engine.Contracts;

namespace MQNetScan.Presentation.Common
{
    /// <summary>
    /// ListView 辅助工具：
    ///   · 列头排序（IP 按 4 段数值比较，不是字符串比较）；
    ///   · Ctrl+C 复制选中行（制表符分隔，便于贴入 Excel）；
    ///   · 角色配色。
    ///
    /// 设计约束（architecture §5.1）：
    ///   · 使用 ListView Details 视图，**不使用 VirtualMode**（用户已确认）；
    ///   · 严禁双击触发外部动作——本类不绑定任何 DoubleClick 事件。
    /// </summary>
    internal static class ListViewHelper
    {
        // -------------------------------------------------------------------
        // 列定义（固定顺序，PRD §6.6）
        // -------------------------------------------------------------------
        public const int ColIndex = 0;
        public const int ColIp = 1;
        public const int ColMac = 2;
        public const int ColOui = 3;
        public const int ColPorts = 4;
        public const int ColRole = 5;
        public const int ColConfidence = 6;
        public const int ColEvidence = 7;
        public const int ColPing3 = 8;

        public const int ColumnCount = 9;

        /// <summary>
        /// 初始化列头与基本样式。
        /// </summary>
        public static void ConfigureColumns(ListView listView)
        {
            listView.View = View.Details;
            listView.FullRowSelect = true;
            listView.GridLines = true;
            listView.HideSelection = false;
            listView.MultiSelect = true;
            listView.LabelEdit = false;
            listView.HeaderStyle = ColumnHeaderStyle.Clickable;

            listView.Columns.Clear();

            AddColumn(listView, "#", 44, HorizontalAlignment.Right);
            AddColumn(listView, "IP 地址", 130, HorizontalAlignment.Left);
            AddColumn(listView, "MAC 地址", 150, HorizontalAlignment.Left);
            AddColumn(listView, "厂商前缀", 90, HorizontalAlignment.Left);
            AddColumn(listView, "开放端口", 180, HorizontalAlignment.Left);
            AddColumn(listView, "角色", 110, HorizontalAlignment.Left);
            AddColumn(listView, "可信度", 70, HorizontalAlignment.Left);
            AddColumn(listView, "识别依据", 190, HorizontalAlignment.Left);
            AddColumn(listView, "深度测速", 150, HorizontalAlignment.Left);
        }

        private static void AddColumn(ListView lv, string text, int width, HorizontalAlignment align)
        {
            ColumnHeader h = new ColumnHeader();
            h.Text = text;
            h.Width = width;
            h.TextAlign = align;
            lv.Columns.Add(h);
        }

        // -------------------------------------------------------------------
        // 行构造
        // -------------------------------------------------------------------
        /// <summary>
        /// 由 HostRecord 构造一行。
        /// </summary>
        public static ListViewItem BuildItem(HostRecord record, int index)
        {
            ListViewItem item = new ListViewItem(index.ToString(CultureInfo.InvariantCulture));

            item.SubItems.Add(record.IpText);
            item.SubItems.Add(string.IsNullOrEmpty(record.MacAddress) ? "—" : record.MacAddress);
            item.SubItems.Add(string.IsNullOrEmpty(record.MacAddress) ? "—" : record.OuiPrefix);
            item.SubItems.Add(record.OpenPortsText);
            item.SubItems.Add(record.Role);
            item.SubItems.Add(GetConfidenceText(record.Confidence));
            item.SubItems.Add(record.Evidence);
            item.SubItems.Add(record.Ping3Text);

            // 把 IP 的数值形式存进 Tag，供排序使用（避免每次排序重新解析）
            item.Tag = record.IpAddress;

            ApplyRoleColor(item, record.Role);

            return item;
        }

        /// <summary>
        /// 按角色设置行底色（低饱和度，保证 Win7 基本主题下可读）。
        /// 颜色仅作辅助信息，文字必须能独立表达含义（PRD §7.3）。
        /// </summary>
        private static string GetConfidenceText(DeviceConfidence confidence)
        {
            if (confidence == DeviceConfidence.High) { return "高"; }
            if (confidence == DeviceConfidence.Medium) { return "中"; }
            if (confidence == DeviceConfidence.Low) { return "低"; }
            return "未知";
        }

        public static void ApplyRoleColor(ListViewItem item, string role)
        {
            switch (role)
            {
                case RolePriority.RoleLocalServer:
                    item.BackColor = System.Drawing.Color.FromArgb(232, 240, 254);
                    break;
                case RolePriority.RoleCashierPc:
                    item.BackColor = System.Drawing.Color.FromArgb(232, 248, 236);
                    break;
                case RolePriority.RolePrinter:
                    item.BackColor = System.Drawing.Color.FromArgb(255, 246, 229);
                    break;
                case RolePriority.RoleScale:
                    item.BackColor = System.Drawing.Color.FromArgb(245, 240, 255);
                    break;
                case RolePriority.RoleAndroidPos:
                    item.BackColor = System.Drawing.Color.FromArgb(234, 246, 250);
                    break;
                case RolePriority.RoleNetworkDevice:
                    item.BackColor = System.Drawing.Color.FromArgb(240, 240, 240);
                    break;
                default:
                    item.BackColor = System.Drawing.Color.White;
                    break;
            }
        }

        // -------------------------------------------------------------------
        // 排序
        // -------------------------------------------------------------------
        /// <summary>
        /// 按指定列排序。
        /// IP 列按 4 段数值比较（192.168.1.9 在 192.168.1.10 之前），而非字符串比较。
        /// </summary>
        /// <param name="listView">目标列表。</param>
        /// <param name="column">列索引。</param>
        /// <param name="ascending">是否升序。</param>
        public static void Sort(ListView listView, int column, bool ascending)
        {
            if (listView == null) { return; }
            if (listView.Items.Count < 2) { return; }

            listView.BeginUpdate();

            try
            {
                ListViewItem[] items = new ListViewItem[listView.Items.Count];
                listView.Items.CopyTo(items, 0);

                Array.Sort(items, new RowComparer(column, ascending));

                listView.Items.Clear();
                listView.Items.AddRange(items);

                // 排序后行号需要重排，否则"#"列会与新顺序不一致
                RenumberRows(listView);
            }
            finally
            {
                listView.EndUpdate();
            }
        }

        /// <summary>
        /// 重排行号列。
        /// </summary>
        public static void RenumberRows(ListView listView)
        {
            for (int i = 0; i < listView.Items.Count; i++)
            {
                listView.Items[i].SubItems[ColIndex].Text =
                    (i + 1).ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// 行比较器。
        /// </summary>
        private sealed class RowComparer : IComparer
        {
            private readonly int _column;
            private readonly bool _ascending;

            public RowComparer(int column, bool ascending)
            {
                _column = column;
                _ascending = ascending;
            }

            public int Compare(object x, object y)
            {
                ListViewItem a = x as ListViewItem;
                ListViewItem b = y as ListViewItem;

                if (a == null && b == null) { return 0; }
                if (a == null) { return _ascending ? -1 : 1; }
                if (b == null) { return _ascending ? 1 : -1; }

                int result;

                switch (_column)
                {
                    case ColIp:
                        result = CompareIp(a, b);
                        break;

                    case ColIndex:
                        result = CompareInt(GetSubText(a, ColIndex), GetSubText(b, ColIndex));
                        break;

                    case ColPorts:
                        result = ComparePorts(GetSubText(a, ColPorts), GetSubText(b, ColPorts));
                        break;

                    default:
                        result = string.Compare(GetSubText(a, _column), GetSubText(b, _column),
                            StringComparison.CurrentCulture);
                        break;
                }

                return _ascending ? result : -result;
            }

            private static string GetSubText(ListViewItem item, int column)
            {
                if (column < 0 || column >= item.SubItems.Count) { return ""; }
                return item.SubItems[column].Text;
            }

            /// <summary>
            /// IP 数值比较。优先用 Tag 里缓存的 uint。
            /// </summary>
            private static int CompareIp(ListViewItem a, ListViewItem b)
            {
                uint ia;
                uint ib;

                bool hasA = TryGetIpValue(a, out ia);
                bool hasB = TryGetIpValue(b, out ib);

                if (hasA && hasB) { return ia.CompareTo(ib); }

                // 退化到字符串比较
                return string.Compare(GetSubText(a, ColIp), GetSubText(b, ColIp),
                    StringComparison.Ordinal);
            }

            private static bool TryGetIpValue(ListViewItem item, out uint value)
            {
                value = 0;

                if (item.Tag is uint)
                {
                    value = (uint)item.Tag;
                    return true;
                }

                return Ipv4Util.TryParse(GetSubText(item, ColIp), out value);
            }

            private static int CompareInt(string a, string b)
            {
                int ia;
                int ib;

                bool okA = int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out ia);
                bool okB = int.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out ib);

                if (okA && okB) { return ia.CompareTo(ib); }

                return string.Compare(a, b, StringComparison.CurrentCulture);
            }

            /// <summary>
            /// 端口列比较：取第一个端口号比较，无端口视为最小。
            /// </summary>
            private static int ComparePorts(string a, string b)
            {
                int pa = FirstPort(a);
                int pb = FirstPort(b);

                return pa.CompareTo(pb);
            }

            private static int FirstPort(string text)
            {
                if (string.IsNullOrEmpty(text)) { return -1; }

                int idx = text.IndexOf(',');
                string first = idx < 0 ? text : text.Substring(0, idx);

                int v;
                if (int.TryParse(first.Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out v))
                {
                    return v;
                }

                return -1;
            }
        }

        // -------------------------------------------------------------------
        // 复制
        // -------------------------------------------------------------------
        /// <summary>
        /// 把选中行复制为制表符分隔文本（可直接贴入 Excel）。
        /// 未选中任何行时返回 null。
        /// </summary>
        public static string BuildSelectedText(ListView listView)
        {
            if (listView == null) { return null; }

            // 【重要】ListView 在窗口句柄尚未创建时，SelectedItems 恒为空，
            // 即使把 ListViewItem.Selected 设为 true 也读不到。
            // 访问一次 Handle 强制创建，确保复制逻辑在非交互调用下也可靠。
            EnsureHandleCreated(listView);

            if (listView.SelectedItems.Count == 0) { return null; }

            StringBuilder sb = new StringBuilder();

            foreach (ListViewItem item in listView.SelectedItems)
            {
                for (int c = 0; c < item.SubItems.Count; c++)
                {
                    if (c > 0) { sb.Append('\t'); }
                    sb.Append(item.SubItems[c].Text);
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// 确保控件句柄已创建（SelectedItems 依赖它）。
        /// </summary>
        private static void EnsureHandleCreated(ListView listView)
        {
            if (listView.IsHandleCreated) { return; }

            try
            {
                // 访问 Handle 会触发 OnHandleCreated
                IntPtr handle = listView.Handle;

                if (handle == IntPtr.Zero) { return; }
            }
            catch (Exception)
            {
                // 未挂到窗体上时可能失败，忽略
            }
        }

        /// <summary>
        /// 复制选中行到剪贴板。返回复制的行数。
        /// </summary>
        public static int CopySelectedToClipboard(ListView listView)
        {
            string text = BuildSelectedText(listView);

            if (string.IsNullOrEmpty(text)) { return 0; }

            try
            {
                Clipboard.SetText(text);
                return listView.SelectedItems.Count;
            }
            catch (Exception)
            {
                // 剪贴板被其它进程占用时可能失败，不应抛给调用方
                return 0;
            }
        }

        // -------------------------------------------------------------------
        // 查找
        // -------------------------------------------------------------------
        /// <summary>
        /// 按 IP 查找行索引；找不到返回 -1。
        /// </summary>
        public static int FindRowByIp(ListView listView, uint ipAddress)
        {
            for (int i = 0; i < listView.Items.Count; i++)
            {
                object tag = listView.Items[i].Tag;

                if (tag is uint && (uint)tag == ipAddress) { return i; }

                uint parsed;
                if (tag == null && Ipv4Util.TryParse(listView.Items[i].SubItems[ColIp].Text, out parsed)
                    && parsed == ipAddress)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 只刷新指定行的某一列，避免整表重建（用于深度测速回填）。
        /// </summary>
        public static bool UpdateCell(ListView listView, uint ipAddress, int column, string text)
        {
            int index = FindRowByIp(listView, ipAddress);

            if (index < 0) { return false; }
            if (column < 0 || column >= listView.Items[index].SubItems.Count) { return false; }

            listView.Items[index].SubItems[column].Text = text;
            return true;
        }
    }
}
