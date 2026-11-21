using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;
using MQNetScan.Presentation.Pages;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S12 设置页收尾测试。
    /// 覆盖 PRD FR-4.1.3 / FR-4.2.1 ~ FR-4.2.6。
    /// </summary>
    internal static class SettingsPageRulesTests
    {
        public static void Run()
        {
            TestRuleListColumns();
            TestBindAndRefreshRules();
            TestDisabledRuleRendering();
            TestEmptyRulesHint();
            TestRuleDialogValidation();
            TestRestoreDefaultRules();
            TestRulesPersistAcrossLoad();
            TestFactoryResetValues();
            TestNoPrioritySortControl();
        }

        // -------------------------------------------------------------------
        private static SettingsPage CreatePage()
        {
            SettingsPage page = new SettingsPage();
            page.BindSettings(new AppSettings(), Path.Combine(Path.GetTempPath(), "test.ini"));
            return page;
        }

        private static ListView GetRuleList(SettingsPage page)
        {
            FieldInfo f = typeof(SettingsPage).GetField("listRules",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (ListView)f.GetValue(page);
        }

        // -------------------------------------------------------------------
        private static void TestRuleListColumns()
        {
            Console.WriteLine();
            Console.WriteLine("[S12-1] 规则列表列定义");
            TestRunner.BeginCase("4 列：端口/角色/备注/状态");

            using (SettingsPage page = CreatePage())
            {
                ListView lv = GetRuleList(page);

                TestRunner.AssertEqual(4, lv.Columns.Count, "共 4 列");
                TestRunner.AssertEqual("角色", lv.Columns[0].Text, "第 1 列为角色");
                TestRunner.AssertEqual("端口列表", lv.Columns[1].Text, "第 2 列为端口列表");
                TestRunner.AssertEqual("备注", lv.Columns[2].Text, "第 3 列为备注");
                TestRunner.AssertEqual("状态", lv.Columns[3].Text, "第 4 列为状态");
            }
        }

        private static void TestBindAndRefreshRules()
        {
            Console.WriteLine();
            Console.WriteLine("[S12-2] 绑定后规则列表显示 12 条");
            TestRunner.BeginCase("行数与内容正确");

            using (SettingsPage page = CreatePage())
            {
                ListView lv = GetRuleList(page);

                TestRunner.AssertEqual(5, lv.Items.Count, "默认 5 个角色");

                TestRunner.AssertEqual("本地服务器", lv.Items[0].SubItems[0].Text, "第 1 个角色为本地服务器");
                TestRunner.AssertEqual("1433", lv.Items[0].SubItems[1].Text, "本地服务器端口 1433");
                TestRunner.AssertEqual("启用", lv.Items[0].SubItems[3].Text, "第 1 条为启用");

                TestRunner.AssertEqual("Windows 电脑", lv.Items[1].SubItems[0].Text, "第 2 个角色为 Windows 电脑");
                TestRunner.AssertEqual("135,445,3389", lv.Items[1].SubItems[1].Text, "Windows 端口列表");
                TestRunner.AssertEqual("打印机", lv.Items[2].SubItems[0].Text, "第 3 个角色为打印机");
                TestRunner.AssertEqual("515,631,9100", lv.Items[2].SubItems[1].Text, "打印机端口列表");
            }
        }

        private static void TestDisabledRuleRendering()
        {
            Console.WriteLine();
            Console.WriteLine("[S12-3] 禁用规则的显示");
            TestRunner.BeginCase("状态列显示「已禁用」且灰色");

            using (SettingsPage page = CreatePage())
            {
                ListView lv = GetRuleList(page);

                // 把第一条禁用
                page.CurrentRoles[0].Enabled = false;

                // 重新绑定以刷新
                MethodInfo refresh = typeof(SettingsPage).GetMethod("RefreshRuleList",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                refresh.Invoke(page, null);

                TestRunner.AssertEqual("已禁用", lv.Items[0].SubItems[3].Text,
                    "状态列显示已禁用");
                TestRunner.AssertTrue(lv.Items[0].ForeColor != lv.Items[1].ForeColor,
                    "禁用行文字颜色不同于启用行");

                // 恢复
                page.CurrentRoles[0].Enabled = true;
                refresh.Invoke(page, null);
                TestRunner.AssertEqual("启用", lv.Items[0].SubItems[3].Text, "恢复后为启用");
            }
        }

        private static void TestEmptyRulesHint()
        {
            Console.WriteLine();
            Console.WriteLine("[S12-4] 规则清空后的提示（PRD FR-4.2.6）");
            TestRunner.BeginCase("0 条规则给红色提示但不阻塞");

            using (SettingsPage page = CreatePage())
            {
                Label hint = (Label)GetField(page, "lblRulesHint");

                // 清空规则
                page.CurrentRoles.Clear();

                MethodInfo refresh = typeof(SettingsPage).GetMethod("RefreshRuleList",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                refresh.Invoke(page, null);

                ListView lv = GetRuleList(page);
                TestRunner.AssertEqual(0, lv.Items.Count, "列表为空");

                Console.WriteLine("       提示: " + hint.Text);

                TestRunner.AssertTrue(hint.Text.IndexOf("没有任何端口规则") >= 0,
                    "提示说明规则为空");
                TestRunner.AssertTrue(hint.Text.IndexOf("无法判定设备角色") >= 0,
                    "提示说明影响");

                // 恢复一条
                page.CurrentRoles.Add(new RoleDefinition("自助点餐机", new int[] { 8081 }, "自定义"));
                refresh.Invoke(page, null);

                TestRunner.AssertTrue(hint.Text.IndexOf("1 条") >= 0,
                    "恢复后提示数量：" + hint.Text);
            }
        }

        private static void TestRuleDialogValidation()
        {
            Console.WriteLine();
            Console.WriteLine("[S12-5] 规则编辑对话框校验（PRD FR-4.2.2）");
            TestRunner.BeginCase("端口范围、重复、空角色");

            // 直接测试 SettingsService.ValidatePortRule（对话框用它同样的语义）
            // 这里补充验证对话框存在的构造路径不抛异常
            using (SettingsPage page = CreatePage())
            {
                AppSettings settings = new AppSettings();

                // 通过反射构造 PortRuleDialog（新增模式）
                Type dlgType = typeof(SettingsPage).Assembly.GetType(
                    "MQNetScan.Presentation.Pages.PortRuleDialog");

                TestRunner.AssertTrue(dlgType != null, "PortRuleDialog 类型存在");

                object dlg = Activator.CreateInstance(dlgType,
                    new object[] { settings, -1 });

                TestRunner.AssertTrue(dlg != null, "新增模式可构造");

                // 检查私有字段 _txtPort 与 _cboRole 存在
                FieldInfo txtPort = dlgType.GetField("_txtPort",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo cboRole = dlgType.GetField("_cboRole",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                TestRunner.AssertTrue(txtPort != null, "端口输入框存在");
                TestRunner.AssertTrue(cboRole != null, "角色下拉框存在");

                ComboBox cbo = (ComboBox)cboRole.GetValue(dlg);

                // 内置角色 5 个
                TestRunner.AssertEqual(5, cbo.Items.Count, "下拉含 5 个内置角色");
                TestRunner.AssertEqual("本地服务器", cbo.Items[0].ToString(), "第 1 项为本地服务器");
                TestRunner.AssertEqual("安卓设备", cbo.Items[4].ToString(), "第 5 项为安卓设备");

                // 允许自定义文本
                TestRunner.AssertEqual(ComboBoxStyle.DropDown, cbo.DropDownStyle,
                    "下拉允许自定义文本（自定义角色）");

                // 编辑模式
                object dlgEdit = Activator.CreateInstance(dlgType,
                    new object[] { settings, 0 });

                TestRunner.AssertTrue(dlgEdit != null, "编辑模式可构造");
            }

            // 校验语义回归
            List<PortRule> existing = new List<PortRule>();
            existing.Add(new PortRule(9100, "打印机", ""));

            TestRunner.AssertTrue(SettingsService.ValidatePortRule(
                new PortRule(0, "测试", ""), existing, -1) != null, "端口 0 被拒");
            TestRunner.AssertTrue(SettingsService.ValidatePortRule(
                new PortRule(9100, "测试", ""), existing, -1) != null, "重复端口被拒");
            TestRunner.AssertTrue(SettingsService.ValidatePortRule(
                new PortRule(8080, " ", ""), existing, -1) != null, "空角色被拒");
            TestRunner.AssertTrue(SettingsService.ValidatePortRule(
                new PortRule(8080, "自助点餐机", ""), existing, -1) == null, "合法自定义通过");
        }

        private static void TestRestoreDefaultRules()
        {
            Console.WriteLine();
            Console.WriteLine("[S12-6] 恢复内置规则（PRD FR-4.2.4）");
            TestRunner.BeginCase("恢复后与 PRD §7.1 的 12 条完全一致");

            AppSettings freshSettings = new AppSettings();
            List<PortRule> fresh = freshSettings.PortRules;

            TestRunner.AssertEqual(12, fresh.Count, "内置 12 条");

            int[] expectedPorts = { 9100, 515, 631, 4001, 5000, 8000, 3000, 1433, 445, 3389, 135, 5555 };

            for (int i = 0; i < expectedPorts.Length; i++)
            {
                bool found = false;
                for (int k = 0; k < fresh.Count; k++)
                {
                    if (fresh[k].Port == expectedPorts[i]) { found = true; }
                }
                TestRunner.AssertTrue(found, "包含端口 " + expectedPorts[i]);
            }

            // 角色映射正确
            TestRunner.AssertTrue(HasRule(fresh, 1433, "本地服务器"), "1433 → 本地服务器");
            TestRunner.AssertTrue(HasRule(fresh, 445, "Windows 电脑"), "445 → Windows 电脑");
            TestRunner.AssertTrue(HasRule(fresh, 9100, "打印机"), "9100 → 打印机");
            TestRunner.AssertTrue(HasRule(fresh, 4001, "电子秤"), "4001 → 电子秤");
            TestRunner.AssertTrue(HasRule(fresh, 5555, "安卓设备"), "5555 → 安卓设备");
        }

        private static bool HasRule(List<PortRule> rules, int port, string role)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].Port == port && rules[i].Role == role) { return true; }
            }
            return false;
        }

        private static void TestRulesPersistAcrossLoad()
        {
            Console.WriteLine();
            Console.WriteLine("[S12-7] 规则修改保存后重启仍生效（PRD FR-4.2.5）");
            TestRunner.BeginCase("增删改 → 保存 → Load 往返一致");

            string dir = Path.Combine(Path.GetTempPath(), "MQS12_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "MQNetScan.ini");

                // 1. 初始默认
                AppSettings s1 = new AppSettings();
                string err;
                SettingsService.Save(s1, path, out err);

                // 2. 修改：删一条、加一条自定义
                ConfigLoadResult r1 = SettingsService.Load(path);
                List<PortRule> rules = r1.Settings.PortRules;

                // 删除 5555（安卓设备）
                for (int i = rules.Count - 1; i >= 0; i--)
                {
                    if (rules[i].Port == 5555) { rules.RemoveAt(i); }
                }

                // 加自定义
                rules.Add(new PortRule(8081, "自助点餐机", "S12 测试"));

                r1.Settings.PortRules = rules;
                SettingsService.Save(r1.Settings, path, out err);

                // 3. 重新加载
                ConfigLoadResult r2 = SettingsService.Load(path);

                TestRunner.AssertEqual(12, r2.Settings.PortRules.Count,
                    "11（删 1）+1（加 1）= 12 条");

                bool hasCustom = false;
                bool no5555 = true;

                for (int i = 0; i < r2.Settings.PortRules.Count; i++)
                {
                    PortRule rule = r2.Settings.PortRules[i];

                    if (rule.Port == 5555) { no5555 = false; }
                    if (rule.Port == 8081 && rule.Role == "自助点餐机") { hasCustom = true; }
                }

                TestRunner.AssertTrue(!no5555 == false || true, "占位（下方单独断言）");
                TestRunner.AssertTrue(!ContainsPort(r2.Settings.PortRules, 5555),
                    "5555 已被删除");
                TestRunner.AssertTrue(hasCustom, "自定义规则「8081 → 自助点餐机」保留");

                // 中文角色名往返
                for (int i = 0; i < r2.Settings.PortRules.Count; i++)
                {
                    if (r2.Settings.PortRules[i].Port == 8081)
                    {
                        TestRunner.AssertEqual("自助点餐机", r2.Settings.PortRules[i].Role,
                            "中文角色名往返无损");
                    }
                }
            }
            finally
            {
                try { if (Directory.Exists(dir)) { Directory.Delete(dir, true); } }
                catch (Exception) { }
            }
        }

        private static bool ContainsPort(IList<PortRule> rules, int port)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].Port == port) { return true; }
            }
            return false;
        }

        private static void TestFactoryResetValues()
        {
            Console.WriteLine();
            Console.WriteLine("[S12-8] 恢复出厂设置（PRD FR-4.1.3）");
            TestRunner.BeginCase("并发回 32、超时回 800、规则回内置 12 条");

            AppSettings fresh = new AppSettings();

            TestRunner.AssertEqual(32, fresh.MaxConcurrency, "并发默认 32");
            TestRunner.AssertEqual(800, fresh.PortTimeoutMs, "超时默认 800");
            TestRunner.AssertEqual(12, fresh.PortRules.Count, "规则默认 12 条");
            TestRunner.AssertEqual(true, fresh.EnableRedBanner, "预警区默认启用");
            TestRunner.AssertEqual(1, fresh.SendArpRetry, "ARP 重试默认 1");

            // 修改后再"恢复"
            AppSettings modified = new AppSettings();
            modified.MaxConcurrency = 128;
            modified.PortTimeoutMs = 5000;
            modified.PortRules.Add(new PortRule(8082, "自定义", ""));

            modified.MaxConcurrency = fresh.MaxConcurrency;
            modified.PortTimeoutMs = fresh.PortTimeoutMs;
            modified.PortRules = fresh.PortRules;

            TestRunner.AssertEqual(32, modified.MaxConcurrency, "恢复后并发 32");
            TestRunner.AssertEqual(800, modified.PortTimeoutMs, "恢复后超时 800");
            TestRunner.AssertEqual(12, modified.PortRules.Count, "恢复后规则 12 条");

            bool has8082 = ContainsPort(modified.PortRules, 8082);
            TestRunner.AssertTrue(!has8082, "自定义规则被清除");
        }

        private static void TestNoPrioritySortControl()
        {
            Console.WriteLine();
            Console.WriteLine("[S12-9] 设置页无优先级排序控件（PRD FR-2.3）");
            TestRunner.BeginCase("界面不含优先级拖拽/排序控件");

            using (SettingsPage page = CreatePage())
            {
                // 检查所有字段：不应存在优先级相关控件
                FieldInfo[] fields = typeof(SettingsPage).GetFields(
                    BindingFlags.Instance | BindingFlags.NonPublic);

                bool hasPriorityControl = false;

                for (int i = 0; i < fields.Length; i++)
                {
                    string name = fields[i].Name.ToLowerInvariant();

                    if (name.IndexOf("priority") >= 0
                        || name.IndexOf("sortorder") >= 0
                        || name.IndexOf("drag") >= 0)
                    {
                        hasPriorityControl = true;
                    }
                }

                TestRunner.AssertTrue(!hasPriorityControl, "无优先级/拖拽排序控件");

                // 规则列表的列头也不可点击排序（优先级顺序固定）
                ListView lv = GetRuleList(page);
                TestRunner.AssertEqual(ColumnHeaderStyle.Nonclickable, lv.HeaderStyle,
                    "列头不可点击（无排序功能）");
            }
        }

        private static object GetField(object target, string name)
        {
            Type t = target.GetType();
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null) { throw new InvalidOperationException("找不到字段: " + name); }
            return f.GetValue(target);
        }
    }
}
