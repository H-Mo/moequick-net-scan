using System;

namespace MQNetScan.Tests
{
    /// <summary>
    /// 测试入口。
    /// 顺序执行全部测试用例，失败则退出码为 1（便于 build.ps1 判断）。
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// WinForms 控件要求 STA 线程，因此测试入口标记 [STAThread]。
        /// </summary>
        [STAThread]
        private static int Main(string[] args)
        {
            Console.WriteLine("============================================================");
            Console.WriteLine(" MoeQuick NetScan - 自测");
            Console.WriteLine("============================================================");

            try
            {
                SmokeTests.Run();
                IniConfigStoreTests.Run();
                SettingsPageTests.Run();
                NetSegmentTests.Run();
                ScanPageTests.Run();
                ScanLayoutTests.Run();
                ScanSessionTests.Run();
                ScanPageScanTests.Run();
                ListViewTests.Run();
                IniRoundTripRegressionTests.Run();
                ListViewEndToEndTests.Run();
                PortProberTests.Run();
                PingProberTests.Run();
                RoleEndToEndTests.Run();
                ContextMenuTests.Run();
                ReportBuilderTests.Run();
                ReportEndToEndTests.Run();
                AlertTests.Run();
                ToolsTests.Run();
                SettingsPageRulesTests.Run();
                RoleModelMigrationTests.Run();
                UpdateServiceTests.Run();
                SettingsUpdateTests.Run();
                DeviceNameResolverTests.Run();

                // 真实 ARP 端到端用例默认跳过（耗时长），需显式加参数触发：
                //   MQNetScan.Tests.exe --arp-e2e
                bool runE2E = false;
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--arp-e2e", StringComparison.OrdinalIgnoreCase))
                    {
                        runE2E = true;
                    }
                }

                ArpProberTests.Run();

                if (runE2E)
                {
                    ArpEndToEndTests.Run(32);
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("[S5-E2E] 已跳过（真实 ARP 耗时长；加 --arp-e2e 参数可运行）");
                }

                // 报告样例：加 --report-sample 参数运行
                bool runSample = false;
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--report-sample", StringComparison.OrdinalIgnoreCase))
                    {
                        runSample = true;
                    }
                }

                if (runSample)
                {
                    ReportSampleGenerator.Run();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("[异常] 测试执行中断: " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                return 1;
            }

            TestRunner.Summarize();

            return TestRunner.FailedCount == 0 ? 0 : 1;
        }
    }
}
