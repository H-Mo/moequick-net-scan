using System;
using System.Windows.Forms;
using MQNetScan.Presentation;

namespace MQNetScan
{
    /// <summary>
    /// 应用入口。
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// 应用主入口。
        /// </summary>
        [STAThread]
        private static void Main()
        {
            // 全局异常兜底：任何未捕获异常都转为中文提示，不允许弹英文堆栈（PRD UI-5）
            // 注意：本文件位于 MQNetScan 命名空间下，而项目里也有 MQNetScan.Application 命名空间，
            // 为免歧义一律用全限定名 System.Windows.Forms.Application。
            System.Windows.Forms.Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            System.Windows.Forms.Application.Run(new MainForm());
        }

        private static void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            ShowFatal(e.Exception);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            ShowFatal(ex);
        }

        private static void ShowFatal(Exception ex)
        {
            string detail = ex == null ? "未知错误" : ex.Message;

            try
            {
                MessageBox.Show(
                    "程序遇到未处理的错误：\r\n\r\n" + detail + "\r\n\r\n请记录该提示并联系技术支持。",
                    "萌快网维 - 错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
                // 报告错误本身失败时不得再次抛出
            }
        }
    }
}
