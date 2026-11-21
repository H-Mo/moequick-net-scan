using System;
using System.Diagnostics;
using System.IO;

namespace MQNetScan.Infrastructure
{
    /// <summary>
    /// 外部程序启动器。
    ///
    /// 边界规则（project-structure.md §10 B-5）：
    /// **全项目只有本文件允许调用 Process.Start**。
    ///
    /// 设计约束（PRD FR-1.6 交互硬约束）：
    ///   · 任何外部动作失败都只能返回中文提示，**绝不允许让主程序退出**；
    ///   · 一律捕获异常并折算为 (false, 提示文本)。
    /// </summary>
    internal static class ProcessLauncher
    {
        /// <summary>
        /// 启动结果。
        /// </summary>
        internal sealed class LaunchResult
        {
            private readonly bool _success;
            private readonly string _message;

            public LaunchResult(bool success, string message)
            {
                _success = success;
                _message = message;
            }

            public bool Success { get { return _success; } }
            public string Message { get { return _message; } }
        }

        /// <summary>
        /// 用默认浏览器打开 URL。
        /// </summary>
        public static LaunchResult OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return new LaunchResult(false, "网址为空，无法打开。");
            }

            // 只允许 http/https，避免把任意协议交给系统执行
            string lower = url.Trim().ToLowerInvariant();

            if (!lower.StartsWith("http://") && !lower.StartsWith("https://"))
            {
                return new LaunchResult(false, "只能打开 http/https 地址。");
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = url;
                psi.UseShellExecute = true;

                Process p = Process.Start(psi);

                if (p == null)
                {
                    return new LaunchResult(false,
                        "无法启动浏览器。请手动复制地址在浏览器中打开：" + url);
                }

                return new LaunchResult(true, "已在浏览器中打开：" + url);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return new LaunchResult(false,
                    "未找到可用的默认浏览器。请手动复制地址在浏览器中打开：" + url);
            }
            catch (Exception ex)
            {
                return new LaunchResult(false,
                    "打开浏览器失败：" + ex.Message + "\r\n可手动复制地址打开：" + url);
            }
        }

        /// <summary>
        /// 构造某台设备的 Web 后台地址。
        /// 命中常见 Web 端口时带上端口号，否则用 80。
        /// </summary>
        public static string BuildWebUrl(uint ipAddress, int[] openPorts)
        {
            string ip = Engine.Ipv4Util.Format(ipAddress);

            if (openPorts != null)
            {
                // 优先 https 的常见端口
                if (Contains(openPorts, 443)) { return "https://" + ip; }
                if (Contains(openPorts, 8443)) { return "https://" + ip + ":8443"; }

                if (Contains(openPorts, 80)) { return "http://" + ip; }
                if (Contains(openPorts, 8080)) { return "http://" + ip + ":8080"; }
                if (Contains(openPorts, 8000)) { return "http://" + ip + ":8000"; }
                if (Contains(openPorts, 8081)) { return "http://" + ip + ":8081"; }
            }

            return "http://" + ip;
        }

        private static bool Contains(int[] ports, int port)
        {
            if (ports == null) { return false; }

            for (int i = 0; i < ports.Length; i++)
            {
                if (ports[i] == port) { return true; }
            }

            return false;
        }

        /// <summary>
        /// 启动远程桌面（mstsc /v:IP）。
        /// </summary>
        public static LaunchResult OpenRemoteDesktop(uint ipAddress)
        {
            string ip = Engine.Ipv4Util.Format(ipAddress);

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "mstsc.exe";
                psi.Arguments = "/v:" + ip;
                psi.UseShellExecute = true;

                Process p = Process.Start(psi);

                if (p == null)
                {
                    return new LaunchResult(false,
                        "无法启动远程桌面。请手动执行：mstsc /v:" + ip);
                }

                return new LaunchResult(true, "已启动远程桌面连接到 " + ip);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return new LaunchResult(false,
                    "系统未找到 mstsc.exe，无法启动远程桌面。\r\n可手动在运行框中执行：mstsc /v:" + ip);
            }
            catch (Exception ex)
            {
                return new LaunchResult(false,
                    "启动远程桌面失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 打开系统控制面板项（如 ncpa.cpl、services.msc）。
        /// </summary>
        public static LaunchResult OpenSystemPanel(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return new LaunchResult(false, "未指定要打开的项目。");
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = fileName;
                psi.UseShellExecute = true;

                Process p = Process.Start(psi);

                if (p == null)
                {
                    return new LaunchResult(false, "无法打开 " + fileName + "。");
                }

                return new LaunchResult(true, "已打开 " + fileName);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return new LaunchResult(false,
                    "无法打开 " + fileName + "，可能需要在管理员权限下运行。");
            }
            catch (Exception ex)
            {
                return new LaunchResult(false, "打开 " + fileName + " 失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 执行命令并等待结束（用于 netsh / ipconfig）。
        /// </summary>
        /// <param name="fileName">可执行文件名。</param>
        /// <param name="arguments">参数。</param>
        /// <param name="waitMs">等待超时（毫秒）。</param>
        /// <param name="standardOutput">标准输出（失败时为空串）。</param>
        public static LaunchResult RunAndWait(
            string fileName, string arguments, int waitMs, out string standardOutput)
        {
            standardOutput = "";

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = fileName;
                psi.Arguments = arguments;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;

                // 【重要】netsh/ipconfig 等系统命令输出的是**系统 ANSI 编码**
                // （简体中文 Windows 上是 GBK/936），不是 UTF-8。
                // 若按默认 UTF-8 读取，中文会变成乱码（如"����Ĳ�����Ҫ������"）。
                // 这里显式指定 OEM 代码页，保证错误提示对用户可读。
                try
                {
                    psi.StandardOutputEncoding = System.Text.Encoding.GetEncoding(
                        System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);

                    psi.StandardErrorEncoding = psi.StandardOutputEncoding;
                }
                catch (Exception)
                {
                    // 指定编码失败时退回默认行为（最坏情况只是提示文本乱码，不影响功能）
                }

                using (Process p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        return new LaunchResult(false, "无法启动 " + fileName + "。");
                    }

                    // 先读输出再等待，避免缓冲区满导致死锁
                    string outText = p.StandardOutput.ReadToEnd();
                    string errText = p.StandardError.ReadToEnd();

                    // 等待退出（ReadToEnd 已隐式等到流关闭，这里加超时保护）
                    if (!p.WaitForExit(waitMs))
                    {
                        try { p.Kill(); } catch (Exception) { }

                        return new LaunchResult(false,
                            fileName + " 执行超时（超过 " + waitMs + " 毫秒）。");
                    }

                    standardOutput = outText;

                    if (p.ExitCode != 0)
                    {
                        string detail = string.IsNullOrEmpty(errText) ? outText : errText;

                        return new LaunchResult(false,
                            fileName + " 返回错误码 " + p.ExitCode
                            + (string.IsNullOrEmpty(detail) ? "" : "：" + detail.Trim()));
                    }

                    return new LaunchResult(true, outText.Trim());
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return new LaunchResult(false,
                    "系统未找到 " + fileName + "，该命令可能在此系统上不可用。");
            }
            catch (Exception ex)
            {
                return new LaunchResult(false, "执行 " + fileName + " 失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 在资源管理器中定位文件。
        /// </summary>
        public static LaunchResult ShowInExplorer(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return new LaunchResult(false, "未指定文件路径。");
            }

            try
            {
                if (!File.Exists(filePath))
                {
                    return new LaunchResult(false, "文件不存在：" + filePath);
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "explorer.exe";
                psi.Arguments = "/select,\"" + filePath + "\"";
                psi.UseShellExecute = true;

                Process.Start(psi);

                return new LaunchResult(true, "已在资源管理器中定位文件。");
            }
            catch (Exception ex)
            {
                return new LaunchResult(false, "打开所在文件夹失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 判断当前是否以管理员身份运行。
        /// </summary>
        public static bool IsAdministrator()
        {
            try
            {
                System.Security.Principal.WindowsIdentity identity =
                    System.Security.Principal.WindowsIdentity.GetCurrent();

                System.Security.Principal.WindowsPrincipal principal =
                    new System.Security.Principal.WindowsPrincipal(identity);

                return principal.IsInRole(
                    System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
