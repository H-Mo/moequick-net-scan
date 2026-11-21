using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MQNetScan.Application;
using MQNetScan.Engine;
using MQNetScan.Engine.Contracts;
using MQNetScan.Infrastructure;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S9 报告导出测试。
    /// 覆盖 PRD FR-1.7.1 ~ FR-1.7.6。
    /// </summary>
    internal static class ReportBuilderTests
    {
        public static void Run()
        {
            TestFourSections();
            TestEmptyResult();
            TestPing3SectionOmitted();
            TestPing3SectionPresent();
            TestAlertsRendering();
            TestUnknownDeviceSuggestion();
            TestFileNameTimestamp();
            TestUtf8Bom();
            TestSaveStrategy();
            TestSanitizeFileName();
            TestTruncatedNote();
        }

        // -------------------------------------------------------------------
        private static ReportInput CreateSampleInput()
        {
            ReportInput input = new ReportInput();

            input.GeneratedAt = new DateTime(2026, 11, 18, 22, 30, 15);
            input.TargetText = "192.168.1.0/24";
            input.AdapterText = "Realtek PCIe GbE Family Controller / 192.168.1.10/24";
            input.LocalIpText = "192.168.1.10";
            input.Concurrency = 32;
            input.PortTimeoutMs = 800;
            input.PortCount = 12;
            input.TargetCount = 254;
            input.AliveCount = 3;
            input.ElapsedText = "00:01:42";
            input.FinalStateText = "扫描完成";

            List<HostRecord> hosts = new List<HostRecord>();

            uint ip1, ip2, ip3;
            Ipv4Util.TryParse("192.168.1.1", out ip1);
            Ipv4Util.TryParse("192.168.1.21", out ip2);
            Ipv4Util.TryParse("192.168.1.31", out ip3);

            HostRecord h1 = new HostRecord(ip1, "00:1B:A9:11:22:33", new int[] { 80, 443 });
            h1.Role = RolePriority.RoleNetworkDevice;

            HostRecord h2 = new HostRecord(ip2, "00:26:66:AA:BB:CC", new int[] { 515, 631, 9100 });
            h2.Role = RolePriority.RolePrinter;

            HostRecord h3 = new HostRecord(ip3, "00:11:22:33:44:55", new int[] { 135, 445, 3389 });
            h3.Role = RolePriority.RoleCashierPc;

            hosts.Add(h1);
            hosts.Add(h2);
            hosts.Add(h3);

            input.Hosts = hosts;
            input.Alerts = new List<AlertItem>();

            return input;
        }

        // -------------------------------------------------------------------
        private static void TestFourSections()
        {
            Console.WriteLine();
            Console.WriteLine("[S9-1] 报告四段结构（PRD FR-1.7.4）");
            TestRunner.BeginCase("表头 + 明细 + 告警 + 结论，顺序固定");

            string text = ReportBuilder.Build(CreateSampleInput());

            TestRunner.AssertTrue(text.IndexOf("萌快网维") >= 0, "含产品名");
            TestRunner.AssertTrue(text.IndexOf("局域网诊断报告") >= 0, "含报告标题");

            // 表头字段
            TestRunner.AssertTrue(text.IndexOf("生成时间") >= 0, "含生成时间");
            TestRunner.AssertTrue(text.IndexOf("2026-11-18 22:30:15") >= 0, "时间格式正确");
            TestRunner.AssertTrue(text.IndexOf("扫描范围") >= 0, "含扫描范围");
            TestRunner.AssertTrue(text.IndexOf("192.168.1.0/24") >= 0, "含网段");
            TestRunner.AssertTrue(text.IndexOf("并发 32") >= 0, "含并发数");
            TestRunner.AssertTrue(text.IndexOf("800 ms") >= 0, "含超时");
            TestRunner.AssertTrue(text.IndexOf("目标地址数") >= 0, "含目标地址数");
            TestRunner.AssertTrue(text.IndexOf("存活主机数") >= 0, "含存活主机数");
            TestRunner.AssertTrue(text.IndexOf("00:01:42") >= 0, "含耗时");

            // 三段标题顺序
            int i1 = text.IndexOf("一、设备明细");
            int i2 = text.IndexOf("二、异常告警");
            int i3 = text.IndexOf("三、结论与建议");

            TestRunner.AssertTrue(i1 > 0, "含「一、设备明细」");
            TestRunner.AssertTrue(i2 > 0, "含「二、异常告警」");
            TestRunner.AssertTrue(i3 > 0, "含「三、结论与建议」");
            TestRunner.AssertTrue(i1 < i2, "明细在告警之前");
            TestRunner.AssertTrue(i2 < i3, "告警在结论之前");

            // 明细内容
            TestRunner.AssertTrue(text.IndexOf("192.168.1.21") >= 0, "明细含打印机 IP");
            TestRunner.AssertTrue(text.IndexOf("00:26:66:AA:BB:CC") >= 0, "明细含 MAC");
            TestRunner.AssertTrue(text.IndexOf("00:26:66") >= 0, "明细含 OUI");
            TestRunner.AssertTrue(text.IndexOf("9100") >= 0, "明细含端口");
            TestRunner.AssertTrue(text.IndexOf("打印机") >= 0, "明细含角色");

            // 结论段应含设备构成统计
            TestRunner.AssertTrue(text.IndexOf("本次共发现 3 台设备") >= 0,
                "结论含设备总数");

            // 报告结尾
            TestRunner.AssertTrue(text.IndexOf("报告结束") >= 0, "含报告结束标记");
        }

        // -------------------------------------------------------------------
        private static void TestEmptyResult()
        {
            Console.WriteLine();
            Console.WriteLine("[S9-2] 空结果报告（PRD FR-1.7.6）");
            TestRunner.BeginCase("0 台设备仍能导出，文件非空");

            ReportInput input = CreateSampleInput();
            input.Hosts = new List<HostRecord>();
            input.AliveCount = 0;

            string text = ReportBuilder.Build(input);

            TestRunner.AssertTrue(text.Length > 0, "报告非空（" + text.Length + " 字符）");
            TestRunner.AssertTrue(text.IndexOf("本次扫描未发现存活设备") >= 0,
                "明细段写明未发现设备");
            TestRunner.AssertTrue(text.IndexOf("一、设备明细") >= 0, "四段结构仍完整");
            TestRunner.AssertTrue(text.IndexOf("三、结论与建议") >= 0, "结论段仍在");
            TestRunner.AssertTrue(text.IndexOf("确认网线已连接") >= 0,
                "给出针对性排查建议");
        }

        // -------------------------------------------------------------------
        private static void TestPing3SectionOmitted()
        {
            Console.WriteLine();
            Console.WriteLine("[S9-3] 未测速时省略深度测速小节（PRD §6.7）");
            TestRunner.BeginCase("不出现空表");

            string text = ReportBuilder.Build(CreateSampleInput());

            TestRunner.AssertTrue(text.IndexOf("深度测速记录") < 0,
                "未测速时不出现「深度测速记录」小节");

            // 也不应出现大面积空白
            TestRunner.AssertTrue(text.IndexOf("3 次均失败") < 0, "不含测速失败记录");
        }

        private static void TestPing3SectionPresent()
        {
            Console.WriteLine();
            Console.WriteLine("[S9-4] 有测速时出现小节且只含测速过的主机");
            TestRunner.BeginCase("仅列出已测速主机");

            ReportInput input = CreateSampleInput();

            // 只对第一台（网关）测速
            Ping3Result r = new Ping3Result(new int[] { 12, 15, 11 }, 3, 80);
            input.Hosts[0].Ping3 = r;

            string text = ReportBuilder.Build(input);

            TestRunner.AssertTrue(text.IndexOf("深度测速记录") >= 0, "出现深度测速小节");
            TestRunner.AssertTrue(text.IndexOf("12/15/11 ms，均值 12") >= 0,
                "含测速明细");

            // 小节应位于结论之后
            int conclusionIdx = text.IndexOf("三、结论与建议");
            int ping3Idx = text.IndexOf("四、深度测速记录");

            TestRunner.AssertTrue(ping3Idx > conclusionIdx, "测速小节在结论之后");

            // 未测速的主机不应出现在该小节
            string afterPing3 = text.Substring(ping3Idx);
            TestRunner.AssertTrue(afterPing3.IndexOf("192.168.1.21") < 0,
                "未测速的主机不列入该小节");
        }

        // -------------------------------------------------------------------
        private static void TestAlertsRendering()
        {
            Console.WriteLine();
            Console.WriteLine("[S9-5] 异常告警段（PRD FR-1.7.4）");
            TestRunner.BeginCase("正常时给「正常」结论，异常时给警告");

            // 无告警 → 应输出"正常"
            string normal = ReportBuilder.Build(CreateSampleInput());
            TestRunner.AssertTrue(normal.IndexOf("[正常] 默认网关可达") >= 0,
                "无告警时说明网关可达");
            TestRunner.AssertTrue(normal.IndexOf("[正常] 未检测到 IP 冲突") >= 0,
                "无告警时说明无 IP 冲突");

            // 有网关告警
            ReportInput input = CreateSampleInput();
            input.Alerts = new List<AlertItem>();
            input.Alerts.Add(new AlertItem(AlertKind.GatewayUnreachable,
                "默认网关不可达", "请检查交换机与上联线路"));

            string text = ReportBuilder.Build(input);

            TestRunner.AssertTrue(text.IndexOf("[警告]") >= 0, "严重告警标记为[警告]");
            TestRunner.AssertTrue(text.IndexOf("默认网关不可达") >= 0, "含告警内容");
            TestRunner.AssertTrue(text.IndexOf("[正常] 默认网关可达") < 0,
                "有告警时不再输出「网关可达」");
            TestRunner.AssertTrue(text.IndexOf("请优先检查交换机") >= 0,
                "结论段给出针对性建议");

            // 有 IP 冲突
            ReportInput input2 = CreateSampleInput();
            input2.Alerts = new List<AlertItem>();
            input2.Alerts.Add(new AlertItem(AlertKind.IpConflict,
                "检测到 IP 冲突：192.168.1.50", "请为其中一台设备改用其它 IP"));

            string text2 = ReportBuilder.Build(input2);

            TestRunner.AssertTrue(text2.IndexOf("IP 冲突") >= 0, "含冲突告警");
            TestRunner.AssertTrue(text2.IndexOf("改用其它 IP") >= 0, "结论段给出冲突建议");
        }

        // -------------------------------------------------------------------
        private static void TestUnknownDeviceSuggestion()
        {
            Console.WriteLine();
            Console.WriteLine("[S9-6] 未知设备建议（PRD 风险 R2）");
            TestRunner.BeginCase("有未知设备时提示补充端口规则");

            ReportInput input = CreateSampleInput();

            uint ip;
            Ipv4Util.TryParse("192.168.1.99", out ip);

            HostRecord unknown = new HostRecord(ip, "AA:BB:CC:DD:EE:FF", new int[0]);
            unknown.Role = RolePriority.RoleUnknown;

            input.Hosts.Add(unknown);
            input.AliveCount = 4;

            string text = ReportBuilder.Build(input);

            TestRunner.AssertTrue(text.IndexOf("未能识别角色") >= 0, "告警段提示有未知设备");
            TestRunner.AssertTrue(text.IndexOf("补充对应端口规则") >= 0, "给出补充规则的建议");
            TestRunner.AssertTrue(text.IndexOf("未知 1 台") >= 0, "统计含未知 1 台");
        }

        // -------------------------------------------------------------------
        private static void TestFileNameTimestamp()
        {
            Console.WriteLine();
            Console.WriteLine("[S9-7] 文件名含时间戳（PRD FR-1.7.2）");
            TestRunner.BeginCase("两次导出不覆盖");

            DateTime t1 = new DateTime(2026, 11, 18, 22, 30, 15);
            DateTime t2 = new DateTime(2026, 11, 18, 22, 30, 16);

            string f1 = ReportBuilder.BuildFileName("yyyyMMdd_HHmmss", t1);
            string f2 = ReportBuilder.BuildFileName("yyyyMMdd_HHmmss", t2);

            Console.WriteLine("       文件名 1: " + f1);
            Console.WriteLine("       文件名 2: " + f2);

            TestRunner.AssertEqual("MQNetScan_报告_20261118_223015.txt", f1, "文件名格式正确");
            TestRunner.AssertTrue(f1 != f2, "不同秒的文件名不同");
            TestRunner.AssertTrue(f1.EndsWith(".txt"), "扩展名为 .txt");

            // 非法格式串应回退默认，不抛异常
            bool threw = false;
            string fallback = null;

            try
            {
                fallback = ReportBuilder.BuildFileName("yyyy-MM-dd HH:mm:ss", t1);
            }
            catch (Exception)
            {
                threw = true;
            }

            TestRunner.AssertTrue(!threw, "非法格式不抛异常");
            TestRunner.AssertTrue(fallback != null && fallback.EndsWith(".txt"),
                "仍返回合法文件名");

            // 空格式串用默认
            string def = ReportBuilder.BuildFileName(null, t1);
            TestRunner.AssertEqual("MQNetScan_报告_20261118_223015.txt", def,
                "空格式使用默认格式");
        }

        // -------------------------------------------------------------------
        private static void TestUtf8Bom()
        {
            Console.WriteLine();
            Console.WriteLine("[S9-8] UTF-8 带 BOM（PRD FR-1.7.1，Win7 记事本关键）");
            TestRunner.BeginCase("文件头为 EF BB BF");

            string dir = Path.Combine(Path.GetTempPath(), "MQReport_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(dir);

                string path = Path.Combine(dir, "test.txt");
                string content = ReportBuilder.Build(CreateSampleInput());

                ReportSaveResult r = ReportSaveService.TryWrite(path, content);

                TestRunner.AssertTrue(r.Success, "写入成功");

                byte[] bytes = File.ReadAllBytes(path);

                TestRunner.AssertTrue(bytes.Length > 3, "文件非空");
                TestRunner.AssertEqual((byte)0xEF, bytes[0], "首字节为 EF");
                TestRunner.AssertEqual((byte)0xBB, bytes[1], "次字节为 BB");
                TestRunner.AssertEqual((byte)0xBF, bytes[2], "第三字节为 BF");

                // 用 BOM 感知方式读回，中文应正确
                string readBack = File.ReadAllText(path, new UTF8Encoding(true));
                TestRunner.AssertTrue(readBack.IndexOf("萌快网维") >= 0, "中文可正确读回");
            }
            finally
            {
                try { if (Directory.Exists(dir)) { Directory.Delete(dir, true); } }
                catch (Exception) { }
            }
        }

        // -------------------------------------------------------------------
        private static void TestSaveStrategy()
        {
            Console.WriteLine();
            Console.WriteLine("[S9-9] 保存策略（PRD FR-1.7.3）");
            TestRunner.BeginCase("桌面优先；不可写时回退 exe 同级；都失败则报错");

            string dir = Path.Combine(Path.GetTempPath(), "MQSave_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(dir);

                // 桌面路径可获取（正常情况下）
                string desktop = ReportSaveService.GetDesktopPath();
                Console.WriteLine("       桌面路径: " + (desktop == null ? "(不可用)" : desktop));
                TestRunner.AssertTrue(desktop == null || Directory.Exists(desktop),
                    "桌面路径存在或为 null");

                // exe 同级目录应可获取
                string appDir = ReportSaveService.GetAppDirectory();
                TestRunner.AssertTrue(!string.IsNullOrEmpty(appDir), "exe 目录可获取");

                // 写不存在的目录应优雅失败
                string badPath = Path.Combine(dir, "no_such", "deep", "r.txt");

                bool threw = false;
                ReportSaveResult bad = null;

                try { bad = ReportSaveService.TryWrite(badPath, "x"); }
                catch (Exception) { threw = true; }

                TestRunner.AssertTrue(!threw, "写入不存在目录不抛异常");
                TestRunner.AssertTrue(!bad.Success, "返回失败");
                TestRunner.AssertTrue(bad.Message.Length > 0, "给出原因：" + bad.Message);

                // 唯一化路径
                string p1 = Path.Combine(dir, "dup.txt");
                File.WriteAllText(p1, "a", new UTF8Encoding(false));

                string unique = ReportSaveService.EnsureUniquePath(dir, "dup.txt");
                TestRunner.AssertTrue(unique != p1, "重名时生成新路径");
                TestRunner.AssertTrue(unique.IndexOf("dup_2") >= 0,
                    "新路径含序号（" + Path.GetFileName(unique) + "）");
            }
            finally
            {
                try { if (Directory.Exists(dir)) { Directory.Delete(dir, true); } }
                catch (Exception) { }
            }
        }

        // -------------------------------------------------------------------
        private static void TestSanitizeFileName()
        {
            Console.WriteLine();
            Console.WriteLine("[S9-10] 文件名清理");
            TestRunner.BeginCase("非法字符被替换");

            string cleaned = ReportSaveService.SanitizeFileName("a<b>c:d|e?f*g\"h.txt");

            Console.WriteLine("       清理结果: " + cleaned);

            TestRunner.AssertTrue(cleaned.IndexOf('<') < 0, "不含 <");
            TestRunner.AssertTrue(cleaned.IndexOf('>') < 0, "不含 >");
            TestRunner.AssertTrue(cleaned.IndexOf(':') < 0, "不含 :");
            TestRunner.AssertTrue(cleaned.IndexOf('|') < 0, "不含 |");
            TestRunner.AssertTrue(cleaned.IndexOf('?') < 0, "不含 ?");
            TestRunner.AssertTrue(cleaned.IndexOf('*') < 0, "不含 *");

            TestRunner.AssertEqual("MQNetScan_报告.txt",
                ReportSaveService.SanitizeFileName(""), "空名用默认");
            TestRunner.AssertEqual("MQNetScan_报告.txt",
                ReportSaveService.SanitizeFileName(null), "null 用默认");
        }

        // -------------------------------------------------------------------
        private static void TestTruncatedNote()
        {
            Console.WriteLine();
            Console.WriteLine("[S9-11] 截断提示（PRD NFR-S2）");
            TestRunner.BeginCase("超过上限时报告注明");

            ReportInput input = CreateSampleInput();
            input.Truncated = true;

            string text = ReportBuilder.Build(input);

            TestRunner.AssertTrue(text.IndexOf("超过上限") >= 0, "报告注明结果被截断");
            TestRunner.AssertTrue(text.IndexOf("5000") >= 0, "注明上限为 5000");

            // 未截断时不应出现
            string normal = ReportBuilder.Build(CreateSampleInput());
            TestRunner.AssertTrue(normal.IndexOf("超过上限") < 0, "未截断时不出现该提示");
        }
    }
}
