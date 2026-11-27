using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MQNetScan.Infrastructure;
using MQNetScan.Engine.Contracts;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S1 配置系统单测。
    /// 覆盖 PRD FR-4.3.1 ~ FR-4.3.5 与 FR-4.1.1 / FR-4.1.2。
    /// 全部测试使用临时目录，不触碰 exe 同级的真实配置。
    /// </summary>
    internal static class IniConfigStoreTests
    {
        private static string _tempDir;

        public static void Run()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "MQNetScanTests_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(_tempDir);

                TestMissingFileGeneratesDefaults();
                    TestRoundTrip();
                TestProbeModeSettingsRoundTrip();
                TestScaleDefaultPortMigration();
                TestReportedScalePortMigration();
                TestInvalidValuesFallback();
                TestBrokenFileDoesNotThrow();
                TestReadOnlyDirectory();
                TestAtomicWriteNoTmpLeftover();
                TestNoDuplicatePortsInSave();
                TestPortRuleValidation();
                TestGetEnabledPortsDedup();
                TestNormalizeRanges();
            }
            finally
            {
                TryCleanup();
            }
        }

        private static string NewPath(string name)
        {
            return Path.Combine(_tempDir, name);
        }

        // -------------------------------------------------------------------
        // FR-4.3.2：ini 不存在时自动以默认值创建，不报错
        // -------------------------------------------------------------------
        private static void TestMissingFileGeneratesDefaults()
        {
            Console.WriteLine();
            Console.WriteLine("[S1-1] 配置文件不存在时回退默认值（FR-4.3.2）");
            TestRunner.BeginCase("缺失文件不抛异常且返回默认值");

            string path = NewPath("missing.ini");

            ConfigLoadResult result = SettingsService.Load(path);

            TestRunner.AssertTrue(result != null, "Load 返回非空结果");
            TestRunner.AssertEqual(false, result.FileExisted, "FileExisted 为 false");
            TestRunner.AssertEqual(IniSchema.DefaultMaxConcurrency, result.Settings.MaxConcurrency, "并发数为默认 32");
            TestRunner.AssertEqual(IniSchema.DefaultPortTimeoutMs, result.Settings.PortTimeoutMs, "超时为默认 800");
            TestRunner.AssertEqual(11, result.Settings.PortRules.Count, "端口规则为内置 11 条");

            // 保存后应真实生成文件
            string error;
            bool ok = SettingsService.Save(result.Settings, path, out error);

            TestRunner.AssertTrue(ok, "Save 返回成功");
            TestRunner.AssertTrue(File.Exists(path), "文件已生成");
        }

        // -------------------------------------------------------------------
        // FR-4.3.1：手工改 ini 后重启生效（往返一致性）
        // -------------------------------------------------------------------
        private static void TestRoundTrip()
        {
            Console.WriteLine();
            Console.WriteLine("[S1-2] 保存后重新读取保持一致（FR-4.3.1）");
            TestRunner.BeginCase("往返一致性");

            string path = NewPath("roundtrip.ini");

            AppSettings original = new AppSettings();
            original.MaxConcurrency = 48;
            original.PortTimeoutMs = 1200;
            original.SendArpRetry = 2;
            original.SaasDomain = "自定义.saas.example";
            original.Roles = new List<RoleDefinition>();
            original.Roles.Add(new RoleDefinition("打印机", new int[] { 9100 }, "测试"));
            original.Roles.Add(new RoleDefinition("本地服务器", new int[] { 1433 }, "SQL"));
            original.Roles.Add(new RoleDefinition("自定义设备", new int[] { 9999 }, "自定义角色"));

            string error;
            bool ok = SettingsService.Save(original, path, out error);
            TestRunner.AssertTrue(ok, "保存成功");

            ConfigLoadResult loaded = SettingsService.Load(path);

            TestRunner.AssertEqual(48, loaded.Settings.MaxConcurrency, "并发数往返一致");
            TestRunner.AssertEqual(1200, loaded.Settings.PortTimeoutMs, "超时往返一致");
            TestRunner.AssertEqual(2, loaded.Settings.SendArpRetry, "ARP 重试往返一致");
            TestRunner.AssertEqual("自定义.saas.example", loaded.Settings.SaasDomain, "中文/点号域名往返一致");
            TestRunner.AssertEqual(3, loaded.Settings.Roles.Count, "角色定义条数往返一致");
            TestRunner.AssertEqual("自定义设备", loaded.Settings.Roles[2].Name, "自定义角色名往返一致");
        }

        // -------------------------------------------------------------------
        // FR-4.3.3：键值非法时逐项回退默认值
        // -------------------------------------------------------------------
        private static void TestProbeModeSettingsRoundTrip()
        {
            Console.WriteLine();
            Console.WriteLine("[PING-INI] 探测模式参数往返和旧配置默认值");
            TestRunner.BeginCase("Ping 设置保存读回，缺键旧配置默认 ARP");

            string path = NewPath("probe-mode.ini");
            AppSettings settings = new AppSettings();
            settings.LastProbeMode = (int)ProbeMode.Ping;
            settings.PingTimeoutMs = 1200;
            settings.PingRetry = 2;
            string error;
            TestRunner.AssertTrue(SettingsService.Save(settings, path, out error), "Ping 设置保存成功");

            ConfigLoadResult loaded = SettingsService.Load(path);
            TestRunner.AssertEqual((int)ProbeMode.Ping, loaded.Settings.LastProbeMode, "模式往返一致");
            TestRunner.AssertEqual(1200, loaded.Settings.PingTimeoutMs, "Ping 超时往返一致");
            TestRunner.AssertEqual(2, loaded.Settings.PingRetry, "Ping 次数往返一致");

            string oldPath = NewPath("old-probe.ini");
            File.WriteAllText(oldPath, "[General]\r\nMaxConcurrency=32\r\n", new UTF8Encoding(false));
            ConfigLoadResult old = SettingsService.Load(oldPath);
            TestRunner.AssertEqual((int)ProbeMode.Arp, old.Settings.LastProbeMode, "旧配置默认 ARP");
            TestRunner.AssertEqual(IniSchema.DefaultPingTimeoutMs, old.Settings.PingTimeoutMs, "旧配置 Ping 超时用默认值");
            TestRunner.AssertEqual(IniSchema.DefaultPingRetry, old.Settings.PingRetry, "旧配置 Ping 次数用默认值");
        }

        private static void TestScaleDefaultPortMigration()
        {
            Console.WriteLine();
            Console.WriteLine("[SCALE-MIGRATION] 电子秤默认端口更新");
            TestRunner.BeginCase("旧默认补入 5001，自定义角色保持不变");

            string oldPath = NewPath("old-scale-default.ini");
            string oldIni = "[Role.1]\r\nName=电子秤\r\nPorts=3001,4001\r\nComment=计价秤 / 条码秤通讯\r\nEnabled=true\r\n";
            File.WriteAllText(oldPath, oldIni, new UTF8Encoding(false));
            ConfigLoadResult oldResult = SettingsService.Load(oldPath);
            RoleDefinition migrated = oldResult.Settings.FindRole("电子秤");
            TestRunner.AssertTrue(migrated != null, "找到旧电子秤规则");
            if (migrated != null)
            {
                TestRunner.AssertEqual("3001,4001,5001", migrated.PortsText, "旧默认规则增加 5001");
            }
            TestRunner.AssertTrue(Contains(oldResult.Warnings, "5001"), "提示规则已更新");

            string customPath = NewPath("custom-scale-ports.ini");
            string customIni = "[Role.1]\r\nName=电子秤\r\nPorts=3001,4001,8000\r\nComment=现场自定义\r\nEnabled=true\r\n";
            File.WriteAllText(customPath, customIni, new UTF8Encoding(false));
            ConfigLoadResult customResult = SettingsService.Load(customPath);
            RoleDefinition custom = customResult.Settings.FindRole("电子秤");
            TestRunner.AssertTrue(custom != null, "找到自定义电子秤规则");
            if (custom != null)
            {
                TestRunner.AssertEqual("3001,4001,8000", custom.PortsText, "不覆盖用户自定义端口");
            }
        }

        private static void TestReportedScalePortMigration()
        {
            Console.WriteLine();
            Console.WriteLine("[SCALE-MIGRATION-OLD] 迁移现场旧电子秤规则");
            TestRunner.BeginCase("3001、4000、5000、6000 自动迁移至指定三端口");
            string path = NewPath("reported-old-scale.ini");
            string ini = "[Role.1]\r\nName=电子秤\r\nPorts=3001,4000,5000,6000\r\nComment=计价秤\r\nEnabled=true\r\n";
            File.WriteAllText(path, ini, new UTF8Encoding(false));

            ConfigLoadResult result = SettingsService.Load(path);
            RoleDefinition scale = result.Settings.FindRole("电子秤");
            TestRunner.AssertTrue(scale != null, "找到电子秤规则");
            if (scale != null)
            {
                TestRunner.AssertEqual("3001,4001,5001", scale.PortsText, "迁移成目标端口组合");
            }
            TestRunner.AssertTrue(Contains(result.Warnings, "默认端口已更新"), "记录迁移警告供启动写回");
        }

        private static bool Contains(IList<string> values, string text)
        {
            if (values == null) { return false; }
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] != null && values[i].IndexOf(text, StringComparison.Ordinal) >= 0) { return true; }
            }
            return false;
        }

        private static void TestInvalidValuesFallback()
        {
            Console.WriteLine();
            Console.WriteLine("[S1-3] 键值非法时回退默认值（FR-4.3.3）");
            TestRunner.BeginCase("越界与非数字被回退");

            string path = NewPath("invalid.ini");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[General]");
            sb.AppendLine("MaxConcurrency=999");      // 越界
            sb.AppendLine("PortTimeoutMs=abc");       // 非数字
            sb.AppendLine("EnableRedBanner=maybe");   // 非布尔
            sb.AppendLine("[Probe]");
            sb.AppendLine("SendArpRetry=99");         // 越界
            sb.AppendLine("[PortRule.1]");
            sb.AppendLine("Port=9100");
            sb.AppendLine("Role=打印机");
            sb.AppendLine("[PortRule.2]");
            sb.AppendLine("Port=70000");              // 非法端口
            sb.AppendLine("Role=坏规则");
            sb.AppendLine("[PortRule.3]");
            sb.AppendLine("Port=1433");
            sb.AppendLine("Role=");                   // 空角色
            sb.AppendLine("[PortRule.4]");
            sb.AppendLine("Port=445");
            sb.AppendLine("Role=收银电脑");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));

            ConfigLoadResult result = SettingsService.Load(path);

            TestRunner.AssertEqual(IniSchema.DefaultMaxConcurrency, result.Settings.MaxConcurrency,
                "并发 999 回退为 32");
            TestRunner.AssertEqual(IniSchema.DefaultPortTimeoutMs, result.Settings.PortTimeoutMs,
                "超时 abc 回退为 800");
            TestRunner.AssertEqual(IniSchema.DefaultEnableRedBanner, result.Settings.EnableRedBanner,
                "非布尔回退为默认 true");
            TestRunner.AssertEqual(IniSchema.DefaultSendArpRetry, result.Settings.SendArpRetry,
                "ARP 重试 99 回退为 1");
            TestRunner.AssertEqual(2, result.Settings.Roles.Count,
                "旧版四条记录过滤非法项后合并为 2 个角色");
            TestRunner.AssertEqual("Windows 电脑", result.Settings.Roles[1].Name,
                "旧收银电脑迁移为 Windows 电脑");
            TestRunner.AssertEqual("445", result.Settings.Roles[1].PortsText,
                "Windows 角色保留 445 端口");
            TestRunner.AssertTrue(result.Warnings.Count > 0, "产生了迁移或校验提示（" + result.Warnings.Count + " 条）");
        }

        // -------------------------------------------------------------------
        // FR-4.3.3：乱码/半截文件不得崩溃
        // -------------------------------------------------------------------
        private static void TestBrokenFileDoesNotThrow()
        {
            Console.WriteLine();
            Console.WriteLine("[S1-4] 损坏文件不崩溃（FR-4.3.3）");
            TestRunner.BeginCase("乱码、半截、二进制内容均可安全加载");

            string[] cases = new string[]
            {
                "这是一个完全不是 INI 的文本",
                "[General\nMaxConcurrency=32\n没有闭合的节",
                "\0\0\0\0\u0001\u0002乱码字节",
                "[General]\nMaxConcurrency=32\n[PortRule.1]\nPort=",
                ""
            };

            for (int i = 0; i < cases.Length; i++)
            {
                string path = NewPath("broken" + i + ".ini");
                File.WriteAllText(path, cases[i], new UTF8Encoding(false));

                bool threw = false;
                try
                {
                    ConfigLoadResult r = SettingsService.Load(path);
                    threw = r == null;
                }
                catch (Exception)
                {
                    threw = true;
                }

                TestRunner.AssertTrue(!threw, "用例 " + i + " 加载未抛异常");
            }
        }

        // -------------------------------------------------------------------
        // FR-4.3.4：只读目录下保存失败但不抛异常
        // -------------------------------------------------------------------
        private static void TestReadOnlyDirectory()
        {
            Console.WriteLine();
            Console.WriteLine("[S1-5] 只读或不可写路径保存失败但不抛异常（FR-4.3.4）");
            TestRunner.BeginCase("写入不存在目录 / 非法路径时优雅失败");

            AppSettings settings = new AppSettings();

            // 路径父目录不存在 → 应返回 false 并给出中文原因，而不是抛异常
            string badPath = Path.Combine(_tempDir, "no_such_subdir", "deep", "MQNetScan.ini");

            bool threw = false;
            string error = null;
            bool ok = true;

            try
            {
                ok = SettingsService.Save(settings, badPath, out error);
            }
            catch (Exception)
            {
                threw = true;
            }

            TestRunner.AssertTrue(!threw, "保存到不存在的目录未抛异常");
            TestRunner.AssertEqual(false, ok, "返回失败");
            TestRunner.AssertTrue(!string.IsNullOrEmpty(error), "给出了失败原因：" + (error == null ? "(空)" : error));

            // 只读文件：先创建再置为只读
            string roPath = NewPath("readonly.ini");
            File.WriteAllText(roPath, "[General]\r\nMaxConcurrency=32\r\n", new UTF8Encoding(false));

            try
            {
                File.SetAttributes(roPath, FileAttributes.ReadOnly);
            }
            catch (Exception)
            {
                TestRunner.Skip("无法设置只读属性，跳过只读文件用例");
                return;
            }

            try
            {
                threw = false;
                ok = true;
                error = null;

                try
                {
                    ok = SettingsService.Save(settings, roPath, out error);
                }
                catch (Exception)
                {
                    threw = true;
                }

                TestRunner.AssertTrue(!threw, "保存到只读文件未抛异常");
                TestRunner.AssertEqual(false, ok, "只读文件保存返回失败");
            }
            finally
            {
                try { File.SetAttributes(roPath, FileAttributes.Normal); } catch (Exception) { }
            }
        }

        // -------------------------------------------------------------------
        // FR-4.3.5：原子写不残留 .tmp
        // -------------------------------------------------------------------
        private static void TestAtomicWriteNoTmpLeftover()
        {
            Console.WriteLine();
            Console.WriteLine("[S1-6] 原子写不残留临时文件（FR-4.3.5）");
            TestRunner.BeginCase("保存后目录内无 .tmp");

            string path = NewPath("atomic.ini");
            AppSettings settings = new AppSettings();

            string error;
            SettingsService.Save(settings, path, out error);

            // 连续保存多次，确认每次都清理干净
            for (int i = 0; i < 5; i++)
            {
                settings.MaxConcurrency = 20 + i;
                SettingsService.Save(settings, path, out error);
            }

            string[] tmpFiles = Directory.GetFiles(_tempDir, "*.tmp", SearchOption.TopDirectoryOnly);
            TestRunner.AssertEqual(0, tmpFiles.Length, "无 .tmp 残留");

            // 内容必须是完整的（能被再次解析）
            ConfigLoadResult r = SettingsService.Load(path);
            TestRunner.AssertEqual(24, r.Settings.MaxConcurrency, "最后一次保存的值已落盘");
        }

        // -------------------------------------------------------------------
        // 保存时端口规则整体重写，不产生孤儿节
        // -------------------------------------------------------------------
        private static void TestNoDuplicatePortsInSave()
        {
            Console.WriteLine();
            Console.WriteLine("[S1-7] 规则减少后不残留孤儿节");
            TestRunner.BeginCase("先 5 个角色后 2 个角色，文件内应只有 2 个 Role 节");

            string path = NewPath("shrink.ini");

            AppSettings s = new AppSettings();
            s.Roles = new List<RoleDefinition>();
            for (int i = 1; i <= 5; i++)
            {
                s.Roles.Add(new RoleDefinition("临时角色" + i, new int[] { 9000 + i }, ""));
            }

            string error;
            SettingsService.Save(s, path, out error);

            s.Roles = new List<RoleDefinition>();
            s.Roles.Add(new RoleDefinition("打印机", new int[] { 9100 }, ""));
            s.Roles.Add(new RoleDefinition("本地服务器", new int[] { 1433 }, ""));
            SettingsService.Save(s, path, out error);

            string content = File.ReadAllText(path);
            int sectionCount = CountOccurrences(content, "[Role.");

            TestRunner.AssertEqual(2, sectionCount, "文件中只剩 2 个 Role 节");
        }

        private static void TestPortRuleValidation()
        {
            Console.WriteLine();
            Console.WriteLine("[S1-8] 端口规则校验（FR-4.2.2）");
            TestRunner.BeginCase("端口范围、重复、空角色");

            List<PortRule> existing = new List<PortRule>();
            existing.Add(new PortRule(9100, "打印机", ""));
            existing.Add(new PortRule(1433, "本地服务器", ""));

            string e1 = SettingsService.ValidatePortRule(new PortRule(0, "测试", ""), existing, -1);
            TestRunner.AssertTrue(e1 != null, "端口 0 被拒绝：" + e1);

            string e2 = SettingsService.ValidatePortRule(new PortRule(65536, "测试", ""), existing, -1);
            TestRunner.AssertTrue(e2 != null, "端口 65536 被拒绝：" + e2);

            string e3 = SettingsService.ValidatePortRule(new PortRule(9100, "测试", ""), existing, -1);
            TestRunner.AssertTrue(e3 != null, "重复端口被拒绝：" + e3);

            string e4 = SettingsService.ValidatePortRule(new PortRule(8080, "  ", ""), existing, -1);
            TestRunner.AssertTrue(e4 != null, "空角色被拒绝：" + e4);

            string e5 = SettingsService.ValidatePortRule(new PortRule(8080, "自助点餐机", ""), existing, -1);
            TestRunner.AssertEqual(null, e5, "合法自定义角色通过");

            // 编辑自身时不应与自己冲突
            string e6 = SettingsService.ValidatePortRule(new PortRule(9100, "打印机", ""), existing, 0);
            TestRunner.AssertEqual(null, e6, "编辑自身端口不判重复");
        }

        private static void TestGetEnabledPortsDedup()
        {
            Console.WriteLine();
            Console.WriteLine("[S1-9] 启用端口集合去重升序");
            TestRunner.BeginCase("重复端口、禁用项、空角色");

            AppSettings s = new AppSettings();
            s.Roles = new List<RoleDefinition>();
            s.Roles.Add(new RoleDefinition("打印机", new int[] { 9100 }, "", true));
            s.Roles.Add(new RoleDefinition("打印机二", new int[] { 9100 }, "", true));
            s.Roles.Add(new RoleDefinition("本地服务器", new int[] { 1433 }, "", true));
            s.Roles.Add(new RoleDefinition("电子秤", new int[] { 5000 }, "", false));
            s.Roles.Add(new RoleDefinition("", new int[] { 6000 }, "", true));

            int[] ports = s.GetEnabledPorts();

            TestRunner.AssertEqual(2, ports.Length, "只有 2 个有效端口");
            TestRunner.AssertEqual(1433, ports[0], "升序第一个为 1433");
            TestRunner.AssertEqual(9100, ports[1], "升序第二个为 9100");
        }

        private static void TestNormalizeRanges()
        {
            Console.WriteLine();
            Console.WriteLine("[S1-10] 参数纠正（FR-4.1.1 / FR-4.1.2）");
            TestRunner.BeginCase("并发与超时的越界纠正");

            string w;

            int c1 = SettingsService.NormalizeConcurrency(5, out w);
            TestRunner.AssertEqual(IniSchema.ConcurrencyMin, c1, "并发 5 纠正为 16");
            TestRunner.AssertTrue(w != null, "给出纠正提示");

            int c2 = SettingsService.NormalizeConcurrency(999, out w);
            TestRunner.AssertEqual(IniSchema.ConcurrencyMax, c2, "并发 999 纠正为 128");

            int c3 = SettingsService.NormalizeConcurrency(100, out w);
            TestRunner.AssertEqual(100, c3, "并发 100 保持不变");
            TestRunner.AssertTrue(w != null, "超过 64 时给出性能提示");

            int c4 = SettingsService.NormalizeConcurrency(32, out w);
            TestRunner.AssertEqual(32, c4, "并发 32 保持不变");
            TestRunner.AssertTrue(w == null, "正常值无提示");

            int t1 = SettingsService.NormalizeTimeout(50, out w);
            TestRunner.AssertEqual(IniSchema.TimeoutMinMs, t1, "超时 50 纠正为 200");

            int t2 = SettingsService.NormalizeTimeout(99999, out w);
            TestRunner.AssertEqual(IniSchema.TimeoutMaxMs, t2, "超时 99999 纠正为 5000");
        }

        // -------------------------------------------------------------------
        // 工具
        // -------------------------------------------------------------------
        private static int CountOccurrences(string text, string token)
        {
            int count = 0;
            int index = 0;

            while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }

            return count;
        }

        private static void TryCleanup()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    // 清掉可能残留的只读属性
                    string[] files = Directory.GetFiles(_tempDir, "*", SearchOption.AllDirectories);
                    for (int i = 0; i < files.Length; i++)
                    {
                        try { File.SetAttributes(files[i], FileAttributes.Normal); } catch (Exception) { }
                    }

                    Directory.Delete(_tempDir, true);
                }
            }
            catch (Exception)
            {
                // 清理失败不影响测试结论
            }
        }
    }
}
