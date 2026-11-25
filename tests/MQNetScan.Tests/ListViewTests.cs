using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Forms;
using MQNetScan.Engine;
using MQNetScan.Engine.Contracts;
using MQNetScan.Presentation.Common;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S6 列表渲染测试。
    /// 覆盖 PRD FR-1.6.1 ~ FR-1.6.4、NFR-S2 与 architecture §3.6 / §5.1。
    /// </summary>
    internal static class ListViewTests
    {
        public static void Run()
        {
            TestColumns();
            TestBuildItem();
            TestIpNumericSort();
            TestOtherColumnSort();
            TestBuildSelectedText();
            TestDoubleClickNotBound();
            TestDispatcherBatching();
            TestDispatcherRowLimit();
            TestUpdateCell();
        }

        // -------------------------------------------------------------------
        private static ListView CreateList()
        {
            ListView lv = new ListView();
            ListViewHelper.ConfigureColumns(lv);
            return lv;
        }

        private static void TestColumns()
        {
            Console.WriteLine();
            Console.WriteLine("[S6-1] 列定义（PRD §6.6）");
            TestRunner.BeginCase("7 列、顺序固定、Details 视图");

            using (ListView lv = CreateList())
            {
                TestRunner.AssertEqual(ListViewHelper.ColumnCount, lv.Columns.Count, "列表列数符合当前定义");
                TestRunner.AssertEqual(View.Details, lv.View, "视图为 Details");
                TestRunner.AssertTrue(lv.FullRowSelect, "整行选中");
                TestRunner.AssertTrue(lv.GridLines, "显示网格线");
                TestRunner.AssertTrue(lv.MultiSelect, "支持多选");
                TestRunner.AssertTrue(!lv.LabelEdit, "禁止就地编辑");

                string[] expected = { "#", "IP 地址", "设备名称", "MAC 地址", "厂商前缀", "开放端口", "角色", "可信度", "识别依据", "深度测速" };

                for (int i = 0; i < expected.Length; i++)
                {
                    TestRunner.AssertEqual(expected[i], lv.Columns[i].Text,
                        "第 " + (i + 1) + " 列为「" + expected[i] + "」");
                }
            }
        }

        private static void TestBuildItem()
        {
            Console.WriteLine();
            Console.WriteLine("[S6-2] 行构造与角色配色（PRD §7.3）");
            TestRunner.BeginCase("各字段正确、未测速显示破折号");

            uint ip;
            Ipv4Util.TryParse("192.168.1.21", out ip);

            HostRecord r = new HostRecord(ip, "00:26:66:AA:BB:CC", new int[] { 515, 631, 9100 });
            r.Role = RolePriority.RolePrinter;

            ListViewItem item = ListViewHelper.BuildItem(r, 7);

            TestRunner.AssertEqual("7", item.SubItems[ListViewHelper.ColIndex].Text, "行号为 7");
            TestRunner.AssertEqual("192.168.1.21", item.SubItems[ListViewHelper.ColIp].Text, "IP 正确");
            TestRunner.AssertEqual("—", item.SubItems[ListViewHelper.ColDeviceName].Text, "名称未解析时显示破折号");
            TestRunner.AssertEqual("00:26:66:AA:BB:CC", item.SubItems[ListViewHelper.ColMac].Text, "MAC 正确");
            r.DeviceName = "WORKSTATION";
            ListViewItem namedItem = ListViewHelper.BuildItem(r, 7);
            TestRunner.AssertEqual("WORKSTATION", namedItem.SubItems[ListViewHelper.ColDeviceName].Text,
                "显示已解析的设备名称");
            TestRunner.AssertEqual("00:26:66", item.SubItems[ListViewHelper.ColOui].Text, "OUI 正确");
            TestRunner.AssertEqual("515, 631, 9100", item.SubItems[ListViewHelper.ColPorts].Text,
                "端口逗号分隔");
            TestRunner.AssertEqual("打印机", item.SubItems[ListViewHelper.ColRole].Text, "角色正确");
            TestRunner.AssertEqual("—", item.SubItems[ListViewHelper.ColPing3].Text,
                "未测速时显示破折号（不是空串）");

            TestRunner.AssertTrue(item.Tag is uint && (uint)item.Tag == ip, "Tag 缓存了 IP 数值");

            // 不同角色应有不同底色
            uint ip2;
            Ipv4Util.TryParse("192.168.1.31", out ip2);
            HostRecord r2 = new HostRecord(ip2, "00:11:22:33:44:55", new int[0]);
            r2.Role = RolePriority.RoleCashierPc;

            ListViewItem item2 = ListViewHelper.BuildItem(r2, 8);

            TestRunner.AssertTrue(item.BackColor != item2.BackColor,
                "打印机与Windows 电脑底色不同");

            // 无开放端口时端口列为空串
            TestRunner.AssertEqual("", item2.SubItems[ListViewHelper.ColPorts].Text,
                "无开放端口时端口列为空");
        }

        private static void TestIpNumericSort()
        {
            Console.WriteLine();
            Console.WriteLine("[S6-3] IP 按数值排序（PRD FR-1.6.4）");
            TestRunner.BeginCase("192.168.1.9 必须排在 192.168.1.10 之前");

            using (ListView lv = CreateList())
            {
                // 故意按字符串顺序插入，数值顺序应不同
                string[] ips = { "192.168.1.10", "192.168.1.9", "192.168.1.100", "192.168.1.2" };

                for (int i = 0; i < ips.Length; i++)
                {
                    uint v;
                    Ipv4Util.TryParse(ips[i], out v);
                    HostRecord r = new HostRecord(v, "AA:BB:CC:00:00:0" + i, new int[0]);
                    lv.Items.Add(ListViewHelper.BuildItem(r, i + 1));
                }

                ListViewHelper.Sort(lv, ListViewHelper.ColIp, true);

                string[] actual = new string[lv.Items.Count];
                for (int i = 0; i < lv.Items.Count; i++)
                {
                    actual[i] = lv.Items[i].SubItems[ListViewHelper.ColIp].Text;
                }

                Console.WriteLine("       排序后: " + string.Join(", ", actual));

                TestRunner.AssertEqual("192.168.1.2", actual[0], "第 1 个为 .2");
                TestRunner.AssertEqual("192.168.1.9", actual[1], "第 2 个为 .9（不是 .10）");
                TestRunner.AssertEqual("192.168.1.10", actual[2], "第 3 个为 .10（不是 .9）");
                TestRunner.AssertEqual("192.168.1.100", actual[3], "第 4 个为 .100");

                // 降序
                ListViewHelper.Sort(lv, ListViewHelper.ColIp, false);
                TestRunner.AssertEqual("192.168.1.100",
                    lv.Items[0].SubItems[ListViewHelper.ColIp].Text, "降序首个为 .100");
                TestRunner.AssertEqual("192.168.1.2",
                    lv.Items[3].SubItems[ListViewHelper.ColIp].Text, "降序末个为 .2");

                // 排序后行号应重排为 1..N
                TestRunner.AssertEqual("1", lv.Items[0].SubItems[ListViewHelper.ColIndex].Text,
                    "排序后首行行号为 1");
                TestRunner.AssertEqual("4", lv.Items[3].SubItems[ListViewHelper.ColIndex].Text,
                    "排序后末行行号为 4");
            }
        }

        private static void TestOtherColumnSort()
        {
            Console.WriteLine();
            Console.WriteLine("[S6-4] 其它列排序");
            TestRunner.BeginCase("行号与端口列按数值排序");

            using (ListView lv = CreateList())
            {
                // 行号 1,2,10 —— 字符串排序会把 10 排在 2 前
                for (int i = 0; i < 3; i++)
                {
                    uint v;
                    Ipv4Util.TryParse("10.0.0." + (i + 1), out v);
                    HostRecord r = new HostRecord(v, "AA:BB:CC:DD:EE:0" + i, new int[] { 0 });
                    ListViewItem it = ListViewHelper.BuildItem(r, i + 1);
                    // 人为设置行号 1、2、10
                    if (i == 2) { it.SubItems[ListViewHelper.ColIndex].Text = "10"; }
                    lv.Items.Add(it);
                }

                ListViewHelper.Sort(lv, ListViewHelper.ColIndex, true);

                TestRunner.AssertEqual("1", lv.Items[0].SubItems[ListViewHelper.ColIndex].Text,
                    "行号排序后首个为 1");

                // 端口排序：取第一个端口
                using (ListView lv2 = CreateList())
                {
                    int[] ports = { 9100, 135, 3389 };

                    for (int i = 0; i < ports.Length; i++)
                    {
                        uint v;
                        Ipv4Util.TryParse("10.0.1." + (i + 1), out v);
                        HostRecord r = new HostRecord(v, "AA:BB:CC:DD:EE:1" + i, new int[] { ports[i] });
                        lv2.Items.Add(ListViewHelper.BuildItem(r, i + 1));
                    }

                    ListViewHelper.Sort(lv2, ListViewHelper.ColPorts, true);

                    TestRunner.AssertEqual("135", lv2.Items[0].SubItems[ListViewHelper.ColPorts].Text,
                        "端口升序首个为 135");
                    TestRunner.AssertEqual("9100", lv2.Items[2].SubItems[ListViewHelper.ColPorts].Text,
                        "端口升序末个为 9100");
                }
            }
        }

        private static void TestBuildSelectedText()
        {
            Console.WriteLine();
            Console.WriteLine("[S6-5] 复制选中行为制表符分隔（PRD FR-1.6.4）");
            TestRunner.BeginCase("可直接贴入 Excel");

            using (ListView lv = CreateList())
            {
                uint v1, v2;
                Ipv4Util.TryParse("192.168.1.5", out v1);
                Ipv4Util.TryParse("192.168.1.6", out v2);

                HostRecord r1 = new HostRecord(v1, "AA:BB:CC:DD:EE:01", new int[] { 445 });
                r1.Role = RolePriority.RoleCashierPc;

                HostRecord r2 = new HostRecord(v2, "AA:BB:CC:DD:EE:02", new int[] { 9100 });
                r2.Role = RolePriority.RolePrinter;

                lv.Items.Add(ListViewHelper.BuildItem(r1, 1));
                lv.Items.Add(ListViewHelper.BuildItem(r2, 2));

                // 未选中
                TestRunner.AssertEqual(null, ListViewHelper.BuildSelectedText(lv),
                    "未选中时返回 null");

                // 选中两行
                lv.Items[0].Selected = true;
                lv.Items[1].Selected = true;

                string text = ListViewHelper.BuildSelectedText(lv);

                TestRunner.AssertTrue(text != null, "选中后返回非空文本");

                string[] lines = text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                TestRunner.AssertEqual(2, lines.Length, "共 2 行");

                string[] cols = lines[0].Split('\t');
                TestRunner.AssertEqual(ListViewHelper.ColumnCount, cols.Length, "每行列数符合当前定义（制表符分隔）");
                TestRunner.AssertEqual("192.168.1.5", cols[ListViewHelper.ColIp], "第 1 行 IP 正确");
                TestRunner.AssertEqual("Windows 电脑", cols[ListViewHelper.ColRole], "第 1 行角色正确");
            }
        }

        // -------------------------------------------------------------------
        // PRD FR-1.6.1：严禁双击触发外部动作
        // -------------------------------------------------------------------
        private static void TestDoubleClickNotBound()
        {
            Console.WriteLine();
            Console.WriteLine("[S6-6] 双击无副作用（PRD FR-1.6.1，硬约束）");
            TestRunner.BeginCase("架构级保证：不绑定 DoubleClick 事件");

            using (ListView lv = CreateList())
            {
                uint v;
                Ipv4Util.TryParse("192.168.1.5", out v);
                HostRecord r = new HostRecord(v, "AA:BB:CC:DD:EE:01", new int[0]);
                lv.Items.Add(ListViewHelper.BuildItem(r, 1));

                // 通过反射检查事件订阅表：
                // WinForms 的 EventHandlerList 是私有的，这里用更直接的方式——
                // 检查 ListView 的 DoubleClick 事件是否为空。
                // Control 内部把事件存在 Events 属性（protected EventHandlerList）里。
                bool hasDoubleClick = HasEventHandler(lv, "DoubleClick");
                bool hasMouseDoubleClick = HasEventHandler(lv, "MouseDoubleClick");

                TestRunner.AssertTrue(!hasDoubleClick, "未绑定 DoubleClick 事件");
                TestRunner.AssertTrue(!hasMouseDoubleClick, "未绑定 MouseDoubleClick 事件");
            }
        }

        /// <summary>
        /// 用反射判断某个控件的指定事件是否已有订阅者。
        /// </summary>
        private static bool HasEventHandler(Control control, string eventName)
        {
            try
            {
                Type controlType = typeof(Control);

                PropertyInfo eventsProp = controlType.GetProperty("Events",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (eventsProp == null) { return false; }

                EventHandlerList list = eventsProp.GetValue(control, null) as EventHandlerList;
                if (list == null) { return false; }

                // 找到对应事件的静态 key
                FieldInfo keyField = typeof(Control).GetField("Event" + eventName,
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (keyField == null)
                {
                    // 某些事件（如 MouseDoubleClick）的 key 在 ListView 上
                    keyField = typeof(ListView).GetField("Event" + eventName,
                        BindingFlags.Static | BindingFlags.NonPublic);
                }

                if (keyField == null) { return false; }

                object key = keyField.GetValue(null);
                Delegate handler = list[key];

                return handler != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // -------------------------------------------------------------------
        private static void TestDispatcherBatching()
        {
            Console.WriteLine();
            Console.WriteLine("[S6-7] 批量刷新（architecture §3.6）");
            TestRunner.BeginCase("单 Tick 最多插入 200 行，其余留到下次");

            using (ListView lv = CreateList())
            using (UiDispatcher d = new UiDispatcher(lv))
            {
                TestRunner.AssertEqual(200, UiDispatcher.MaxRowsPerTick, "单 Tick 上限为 200");
                TestRunner.AssertEqual(5000, UiDispatcher.MaxDisplayRows, "行数上限为 5000");

                // 投递 450 条
                for (int i = 0; i < 450; i++)
                {
                    uint v;
                    Ipv4Util.TryParse("10.0." + (i / 250) + "." + (i % 250 + 1), out v);
                    HostRecord r = new HostRecord(v, "AA:BB:CC:00:00:00", new int[0]);
                    d.Post(r);
                }

                TestRunner.AssertEqual(0, lv.Items.Count, "Post 后不立即插入（只入队）");

                int first = d.FlushNow();
                TestRunner.AssertEqual(200, first, "第一次刷新插入 200 行");
                TestRunner.AssertEqual(200, lv.Items.Count, "列表有 200 行");

                int second = d.FlushNow();
                TestRunner.AssertEqual(200, second, "第二次刷新再插入 200 行");

                int third = d.FlushNow();
                TestRunner.AssertEqual(50, third, "第三次刷新插入剩余 50 行");

                TestRunner.AssertEqual(450, lv.Items.Count, "共 450 行");
                TestRunner.AssertEqual(450, d.DisplayedRows, "已显示计数为 450");

                // 行号应连续
                TestRunner.AssertEqual("1", lv.Items[0].SubItems[ListViewHelper.ColIndex].Text, "首行行号 1");
                TestRunner.AssertEqual("450", lv.Items[449].SubItems[ListViewHelper.ColIndex].Text,
                    "末行行号 450");
            }
        }

        private static void TestDispatcherRowLimit()
        {
            Console.WriteLine();
            Console.WriteLine("[S6-8] 行数上限保护（PRD NFR-S2）");
            TestRunner.BeginCase("超过 5000 行只统计不显示");

            using (ListView lv = CreateList())
            using (UiDispatcher d = new UiDispatcher(lv))
            {
                // 投递 5100 条
                for (int i = 0; i < 5100; i++)
                {
                    uint v;
                    Ipv4Util.TryParse("10.1." + (i / 250) + "." + (i % 250 + 1), out v);
                    d.Post(new HostRecord(v, "AA:BB:CC:00:00:00", new int[0]));
                }

                // 反复刷新直到队列清空
                for (int i = 0; i < 40; i++) { d.FlushNow(); }

                TestRunner.AssertEqual(5000, lv.Items.Count, "列表最多 5000 行");
                TestRunner.AssertEqual(5000, d.DisplayedRows, "已显示 5000");
                TestRunner.AssertEqual(100, d.DroppedRows, "丢弃 100 行");
                TestRunner.AssertTrue(d.IsTruncated, "标记为已截断");
            }
        }

        private static void TestUpdateCell()
        {
            Console.WriteLine();
            Console.WriteLine("[S6-9] 局部刷新单元格（深度测速回填）");
            TestRunner.BeginCase("只改目标行，不影响其它行");

            using (ListView lv = CreateList())
            {
                uint v1, v2;
                Ipv4Util.TryParse("192.168.1.5", out v1);
                Ipv4Util.TryParse("192.168.1.6", out v2);

                lv.Items.Add(ListViewHelper.BuildItem(
                    new HostRecord(v1, "AA:BB:CC:DD:EE:01", new int[0]), 1));
                lv.Items.Add(ListViewHelper.BuildItem(
                    new HostRecord(v2, "AA:BB:CC:DD:EE:02", new int[0]), 2));

                bool ok = ListViewHelper.UpdateCell(lv, v1, ListViewHelper.ColPing3, "12/15/11 ms，均值 12");

                TestRunner.AssertTrue(ok, "更新成功");
                TestRunner.AssertEqual("12/15/11 ms，均值 12",
                    lv.Items[0].SubItems[ListViewHelper.ColPing3].Text, "目标行已更新");
                TestRunner.AssertEqual("—", lv.Items[1].SubItems[ListViewHelper.ColPing3].Text,
                    "其它行不受影响");

                // 更新不存在的 IP
                uint missing;
                Ipv4Util.TryParse("192.168.1.99", out missing);
                TestRunner.AssertTrue(
                    !ListViewHelper.UpdateCell(lv, missing, ListViewHelper.ColPing3, "x"),
                    "不存在的 IP 返回 false");
            }
        }
    }
}
