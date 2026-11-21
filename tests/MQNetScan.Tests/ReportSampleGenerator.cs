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
    /// S9 报告样例生成器。
    /// 用真实数据生成一份报告并打印全文，供人工检查排版与可读性。
    /// 加 --report-sample 参数运行。
    /// </summary>
    internal static class ReportSampleGenerator
    {
        public static void Run()
        {
            Console.WriteLine();
            Console.WriteLine("[S9-SAMPLE] 生成报告样例并输出全文");
            Console.WriteLine();

            ReportInput input = new ReportInput();

            input.GeneratedAt = DateTime.Now;
            input.TargetText = "192.168.122.0/24";
            input.AdapterText = "Red Hat VirtIO Ethernet Adapter / 192.168.122.39/24";
            input.LocalIpText = "192.168.122.39";
            input.Concurrency = 32;
            input.PortTimeoutMs = 800;
            input.PortCount = 12;
            input.TargetCount = 254;
            input.ElapsedText = "00:00:28";
            input.FinalStateText = "扫描完成";

            List<HostRecord> hosts = new List<HostRecord>();

            uint ip1, ip2, ip3, ip4;
            Ipv4Util.TryParse("192.168.122.1", out ip1);
            Ipv4Util.TryParse("192.168.122.20", out ip2);
            Ipv4Util.TryParse("192.168.122.21", out ip3);
            Ipv4Util.TryParse("192.168.122.31", out ip4);

            HostRecord h1 = new HostRecord(ip1, "52:54:00:63:FB:B8", new int[] { 53 });
            h1.Role = RolePriority.RoleUnknown;

            HostRecord h2 = new HostRecord(ip2, "00:26:66:AA:BB:CC", new int[] { 515, 631, 9100 });
            h2.Role = RolePriority.RolePrinter;

            HostRecord h3 = new HostRecord(ip3, "00:11:22:33:44:55", new int[] { 3000, 4001 });
            h3.Role = RolePriority.RoleScale;

            HostRecord h4 = new HostRecord(ip4, "00:1B:A9:77:88:99", new int[] { 135, 445, 3389 });
            h4.Role = RolePriority.RoleCashierPc;

            // 给打印机做一个测速
            h2.Ping3 = new Ping3Result(new int[] { 12, 15, 11 }, 3, 515);

            hosts.Add(h1);
            hosts.Add(h2);
            hosts.Add(h3);
            hosts.Add(h4);

            input.Hosts = hosts;
            input.AliveCount = hosts.Count;

            List<AlertItem> alerts = new List<AlertItem>();
            input.Alerts = alerts;

            string content = ReportBuilder.Build(input);

            // 输出全文供人工检查
            Console.WriteLine("---------- 报告全文开始 ----------");
            Console.Write(content);
            Console.WriteLine("---------- 报告全文结束 ----------");

            // 同时落盘一份到临时目录，便于查看编码
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "MQReportSample");
                Directory.CreateDirectory(dir);

                string path = Path.Combine(dir,
                    ReportBuilder.BuildFileName("yyyyMMdd_HHmmss", DateTime.Now));

                ReportSaveResult r = ReportSaveService.TryWrite(path, content);

                Console.WriteLine();
                Console.WriteLine("已保存到: " + (r.Success ? r.FilePath : "失败：" + r.Message));

                if (r.Success)
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    Console.WriteLine("文件大小: " + bytes.Length + " 字节，BOM: "
                        + bytes[0].ToString("X2") + " " + bytes[1].ToString("X2")
                        + " " + bytes[2].ToString("X2"));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("保存样例失败: " + ex.Message);
            }
        }
    }
}
