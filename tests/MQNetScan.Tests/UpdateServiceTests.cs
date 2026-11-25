using System;
using System.Text;
using MQNetScan.Application;

namespace MQNetScan.Tests
{
    internal static class UpdateServiceTests
    {
        public static void Run()
        {
            TestVersionComparison();
            TestInvalidManifestFallsBack();
            TestDownloadUrlAllowList();
            TestJsonDeserialization();
        }

        private static void TestVersionComparison()
        {
            Console.WriteLine();
            Console.WriteLine("[UPDATE-1] 版本比较");
            TestRunner.BeginCase("仅较新的合法版本显示更新");
            UpdateCheckResult result = UpdateService.Compare("1.1.0", new VersionManifest
            {
                Version = "1.2.0",
                DownloadUrl = "https://github.com/H-Mo/moequick-net-scan/releases/download/v1.2.0/MQNetScan-v1.2.0.zip",
                ReleaseDate = "2026-12-01",
                ReleaseNotes = new string[] { "更新检查", "添加关于区域" }
            });
            TestRunner.AssertTrue(result.HasUpdate, "1.2.0 高于 1.1.0");
            TestRunner.AssertEqual("1.2.0", result.LatestVersion, "版本字段正确");
            TestRunner.AssertEqual("2026-12-01", result.ReleaseDate, "日期正确");
            TestRunner.AssertEqual(2, result.ReleaseNotes.Length, "更新说明完整");

            TestRunner.AssertTrue(!UpdateService.Compare("1.2.0", new VersionManifest
            {
                Version = "1.2.0", DownloadUrl = "https://github.com/H-Mo/moequick-net-scan/releases/download/v1.2.0/file.zip",
                ReleaseDate = "2026-12-01"
            }).HasUpdate, "同版本不提示更新");
            TestRunner.AssertTrue(!UpdateService.Compare("1.3.0", new VersionManifest
            {
                Version = "1.2.0", DownloadUrl = "https://github.com/H-Mo/moequick-net-scan/releases/download/v1.2.0/file.zip",
                ReleaseDate = "2026-12-01"
            }).HasUpdate, "远端版本较旧不提示更新");
        }

        private static void TestInvalidManifestFallsBack()
        {
            Console.WriteLine();
            Console.WriteLine("[UPDATE-2] 无效版本信息视为无更新");
            TestRunner.BeginCase("格式错误、日期错误或缺字段不抛出");
            TestRunner.AssertTrue(!UpdateService.Compare("1.1.0", new VersionManifest
            {
                Version = "latest", DownloadUrl = "https://github.com/H-Mo/moequick-net-scan/releases/download/v1.2.0/file.zip",
                ReleaseDate = "2026-12-01"
            }).HasUpdate, "非法版本无更新");
            TestRunner.AssertTrue(!UpdateService.Compare("1.1.0", new VersionManifest
            {
                Version = "1.2.0", DownloadUrl = "https://github.com/H-Mo/moequick-net-scan/releases/download/v1.2.0/file.zip",
                ReleaseDate = "tomorrow"
            }).HasUpdate, "非法日期无更新");
            TestRunner.AssertTrue(!UpdateService.Compare("bad", new VersionManifest
            {
                Version = "1.2.0", DownloadUrl = "https://github.com/H-Mo/moequick-net-scan/releases/download/v1.2.0/file.zip",
                ReleaseDate = "2026-12-01"
            }).HasUpdate, "当前版本错误时无更新");
        }

        private static void TestDownloadUrlAllowList()
        {
            Console.WriteLine();
            Console.WriteLine("[UPDATE-3] 下载地址 HTTPS 白名单");
            TestRunner.BeginCase("只允许此项目的 GitHub Releases HTTPS 地址");
            TestRunner.AssertTrue(UpdateService.IsAllowedDownloadUrl(
                "https://github.com/H-Mo/moequick-net-scan/releases/download/v1.2.0/a.zip"), "合法 Release URL");
            TestRunner.AssertTrue(!UpdateService.IsAllowedDownloadUrl(
                "http://github.com/H-Mo/moequick-net-scan/releases/download/v1.2.0/a.zip"), "拒绝 HTTP");
            TestRunner.AssertTrue(!UpdateService.IsAllowedDownloadUrl(
                "https://example.com/H-Mo/moequick-net-scan/releases/download/v1.2.0/a.zip"), "拒绝其它域名");
            TestRunner.AssertTrue(!UpdateService.IsAllowedDownloadUrl(
                "https://github.com/attacker/repo/releases/download/v1/a.zip"), "拒绝其它仓库");
        }

        private static void TestJsonDeserialization()
        {
            Console.WriteLine();
            Console.WriteLine("[UPDATE-4] 版本 JSON 解析");
            TestRunner.BeginCase("解析实际接口字段并限制畸形数据");
            byte[] json = Encoding.UTF8.GetBytes("{\"version\":\"1.2.0\",\"downloadUrl\":\"https://github.com/H-Mo/moequick-net-scan/releases/download/v1.2.0/a.zip\",\"releaseDate\":\"2026-12-01\",\"releaseNotes\":[\"one\"]}");
            VersionManifest manifest = UpdateService.Deserialize(json);
            TestRunner.AssertEqual("1.2.0", manifest.Version, "版本解析");
            TestRunner.AssertEqual("2026-12-01", manifest.ReleaseDate, "发布日期解析");
            TestRunner.AssertEqual(1, manifest.ReleaseNotes.Length, "更新说明解析");
            bool threw = false;
            try { UpdateService.Deserialize(new byte[0]); }
            catch (Exception) { threw = true; }
            TestRunner.AssertTrue(threw, "空响应拒绝");
        }
    }
}
