using System;
using System.IO;
using System.Text;
using MQNetScan.Infrastructure;

namespace MQNetScan.Application
{
    /// <summary>
    /// 报告落盘结果。
    /// </summary>
    internal sealed class ReportSaveResult
    {
        private readonly bool _success;
        private readonly string _filePath;
        private readonly string _message;
        private readonly bool _usedFallback;

        public ReportSaveResult(bool success, string filePath, string message, bool usedFallback)
        {
            _success = success;
            _filePath = filePath;
            _message = message;
            _usedFallback = usedFallback;
        }

        public bool Success { get { return _success; } }
        public string FilePath { get { return _filePath; } }
        public string Message { get { return _message; } }
        public bool UsedFallback { get { return _usedFallback; } }
    }

    /// <summary>
    /// 报告保存服务。
    ///
    /// 保存策略（PRD FR-1.7.3）：
    ///   1. 桌面可写 → 存桌面；
    ///   2. 桌面不可写 → 回退到 exe 同级目录；
    ///   3. 都不写 → 返回失败，由调用方提示"另存为"。
    ///
    /// 编码（PRD FR-1.7.1）：
    ///   **UTF-8 带 BOM**，保证 Win7 记事本打开中文不乱码。
    /// </summary>
    internal static class ReportSaveService
    {
        /// <summary>
        /// 计算桌面路径；无法确定时返回 null。
        /// </summary>
        public static string GetDesktopPath()
        {
            try
            {
                string path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    return path;
                }
            }
            catch (Exception)
            {
                // 忽略，走回退
            }

            return null;
        }

        /// <summary>
        /// 计算 exe 同级目录。
        /// </summary>
        public static string GetAppDirectory()
        {
            try
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// 按策略保存报告。
        /// </summary>
        /// <param name="content">报告全文。</param>
        /// <param name="fileName">文件名（含扩展名）。</param>
        /// <param name="preferDesktop">是否优先存桌面。</param>
        public static ReportSaveResult Save(string content, string fileName, bool preferDesktop)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return new ReportSaveResult(false, "", "文件名为空，无法保存。", false);
            }

            // 清理文件名中的非法字符
            string safeName = SanitizeFileName(fileName);

            // ---- 尝试 1：桌面 ----
            if (preferDesktop)
            {
                string desktop = GetDesktopPath();

                if (!string.IsNullOrEmpty(desktop))
                {
                    ReportSaveResult r = TryWrite(Path.Combine(desktop, safeName), content);

                    if (r.Success) { return r; }
                }
            }

            // ---- 尝试 2：exe 同级 ----
            string appDir = GetAppDirectory();

            if (!string.IsNullOrEmpty(appDir))
            {
                ReportSaveResult r2 = TryWrite(Path.Combine(appDir, safeName), content);

                if (r2.Success)
                {
                    return new ReportSaveResult(
                        true, r2.FilePath,
                        "报告已保存到程序目录（桌面不可写）：" + r2.FilePath,
                        true);
                }
            }

            return new ReportSaveResult(
                false, "",
                "无法保存报告：桌面与程序目录都不可写。请用「另存为」选择其它位置。",
                false);
        }

        /// <summary>
        /// 写出到指定路径。
        /// </summary>
        public static ReportSaveResult TryWrite(string fullPath, string content)
        {
            try
            {
                string dir = Path.GetDirectoryName(fullPath);

                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    return new ReportSaveResult(false, fullPath, "目标目录不存在：" + dir, false);
                }

                // UTF-8 带 BOM：Win7 记事本据此识别编码，避免中文乱码
                File.WriteAllText(fullPath, content, new UTF8Encoding(true));

                FileInfo fi = new FileInfo(fullPath);

                return new ReportSaveResult(
                    true, fullPath,
                    "报告已保存：" + fullPath + "（" + fi.Length + " 字节）",
                    false);
            }
            catch (UnauthorizedAccessException)
            {
                return new ReportSaveResult(false, fullPath, "没有写入权限：" + fullPath, false);
            }
            catch (DirectoryNotFoundException)
            {
                return new ReportSaveResult(false, fullPath, "目标目录不存在：" + fullPath, false);
            }
            catch (PathTooLongException)
            {
                return new ReportSaveResult(false, fullPath, "路径过长，请选择更短的保存位置。", false);
            }
            catch (IOException ex)
            {
                return new ReportSaveResult(false, fullPath, "写入失败：" + ex.Message, false);
            }
            catch (Exception ex)
            {
                return new ReportSaveResult(false, fullPath, "保存报告失败：" + ex.Message, false);
            }
        }

        /// <summary>
        /// 清理 Windows 文件名中的非法字符。
        /// </summary>
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) { return "MQNetScan_报告.txt"; }

            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(name.Length);

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool bad = false;

                for (int k = 0; k < invalid.Length; k++)
                {
                    if (invalid[k] == c) { bad = true; break; }
                }

                sb.Append(bad ? '_' : c);
            }

            string result = sb.ToString().Trim();

            if (result.Length == 0) { return "MQNetScan_报告.txt"; }

            return result;
        }

        /// <summary>
        /// 避免重名：若目标已存在，追加序号。
        /// </summary>
        public static string EnsureUniquePath(string directory, string fileName)
        {
            string full = Path.Combine(directory, fileName);

            if (!File.Exists(full)) { return full; }

            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);

            for (int i = 2; i < 1000; i++)
            {
                string candidate = Path.Combine(directory,
                    baseName + "_" + i.ToString() + ext);

                if (!File.Exists(candidate)) { return candidate; }
            }

            return full;
        }
    }
}
