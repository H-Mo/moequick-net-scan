using System;
using System.Drawing;
using System.Windows.Forms;
using MQNetScan.Application;
using MQNetScan.Engine;
using MQNetScan.Infrastructure;
using MQNetScan.Presentation.Pages;

namespace MQNetScan.Presentation
{
    /// <summary>
    /// 主窗体：承载 3 个 Tab 页与底部状态栏。
    /// 负责配置的加载、首运行生成与提示转发。
    /// </summary>
    internal partial class MainForm : Form
    {
        private AppSettings _settings;
        private string _configPath;
        private Timer _statusResetTimer;
        private bool _updateCheckStarted;

        public MainForm()
        {
            InitializeComponent();
            LoadAppIcon();
            InitializeConfig();
            HookEvents();
            Shown += OnMainFormShown;
        }

        /// <summary>
        /// 从内嵌资源加载窗口图标。
        /// 资源缺失时静默跳过（不影响启动）。
        /// </summary>
        private void LoadAppIcon()
        {
            try
            {
                System.IO.Stream stream = typeof(MainForm).Assembly
                    .GetManifestResourceStream("MQNetScan.Resources.app.ico");

                if (stream != null)
                {
                    this.Icon = new Icon(stream);
                    stream.Close();
                }
            }
            catch (Exception)
            {
                // 图标加载失败不应阻止程序启动
            }
        }

        // -------------------------------------------------------------------
        // 配置
        // -------------------------------------------------------------------
        private void InitializeConfig()
        {
            _configPath = SettingsService.ResolveConfigPath();

            ConfigLoadResult result = SettingsService.Load(_configPath);
            _settings = result.Settings;

            settingsPage.BindSettings(_settings, _configPath);
            scanPage.Initialize(_settings.LastNetSegment, _settings, _configPath);
            toolsPage.Initialize(_settings, scanPage.CurrentAdapter);

            // 首次运行（或 ini 被删除）：立即生成默认配置文件（PRD FR-4.3.2）
            if (!result.FileExisted)
            {
                string error;
                if (SettingsService.Save(_settings, _configPath, out error))
                {
                    ShowStatus("已生成默认配置文件。", false);
                }
                else
                {
                    ShowStatus(error, true);
                }
            }
            else if (NeedsScalePortMigration(result) || NeedsSaasDomainMigration(result))
            {
                string error;
                if (!SettingsService.Save(_settings, _configPath, out error))
                {
                    ShowStatus("电子秤端口规则已更新，但无法保存配置：" + error, true);
                }
            }
            else if (NeedsRuleRepair(result))
            {
                // ini 存在但端口规则读不出来（例如文件被清空、只剩注释，
                // 或用户删掉了全部 PortRule 节）。
                //
                // 若不修复，每次启动都会报同一句"未读到任何端口规则"，用户无法消除。
                // 这里用当前设置（已回退为内置默认规则）重写文件，让配置文件自愈。
                string error;
                if (SettingsService.Save(_settings, _configPath, out error))
                {
                    ShowStatus("配置文件缺少端口规则，已自动补全为内置默认规则。", true);
                }
                else
                {
                    ShowStatus("配置文件缺少端口规则，且无法自动修复：" + error, true);
                }
            }

            // 读取期间发现的问题：一次性提示（PRD FR-4.3.3）
            // 注意：若上面已经因"规则修复"给过提示，此处不再重复念同一条。
            if (result.Warnings.Count > 0 && !NeedsRuleRepair(result))
            {
                string first = result.Warnings[0];
                string suffix = result.Warnings.Count > 1
                    ? "（共 " + result.Warnings.Count + " 项问题）"
                    : "";

                ShowStatus("配置文件部分内容无效，已使用默认值：" + first + suffix, true);
            }

            UpdateNetInfo();
        }

        /// <summary>
        /// 判断是否需要修复端口规则：
        /// 加载过程中出现了"未读到任何端口规则"的告警，
        /// 说明文件存在但规则缺失，需要回写默认规则。
        /// </summary>
        private static bool NeedsScalePortMigration(ConfigLoadResult result)
        {
            if (result == null || result.Warnings == null) { return false; }
            for (int i = 0; i < result.Warnings.Count; i++)
            {
                if (result.Warnings[i] != null
                    && result.Warnings[i].IndexOf("电子秤默认端口已更新", StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool NeedsSaasDomainMigration(ConfigLoadResult result)
        {
            if (result == null || result.Warnings == null) { return false; }
            for (int i = 0; i < result.Warnings.Count; i++)
            {
                if (result.Warnings[i] != null
                    && result.Warnings[i].IndexOf("连通性自测默认网址已更新", StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool NeedsRuleRepair(ConfigLoadResult result)
        {
            if (result == null || result.Warnings == null) { return false; }

            for (int i = 0; i < result.Warnings.Count; i++)
            {
                if (result.Warnings[i] != null
                    && result.Warnings[i].IndexOf("未读到任何端口规则", StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnMainFormShown(object sender, EventArgs e)
        {
            if (_updateCheckStarted) { return; }
            _updateCheckStarted = true;
            settingsPage.SetUpdateCheckInProgress();
            UpdateService.CheckAsync(typeof(MainForm).Assembly.GetName().Version.ToString(3),
                settingsPage.ApplyUpdateCheckResult);
        }

        private void HookEvents()
        {
            settingsPage.SettingsSaved += OnSettingsSaved;
            settingsPage.Notify += OnPageNotify;
            scanPage.Notify += OnPageNotify;
            toolsPage.Notify += OnPageNotify;
            scanPage.ScanStartRequested += OnScanStartRequested;
            scanPage.AlertsChanged += OnAlertsChanged;
            scanPage.SingleTestRequested += OnSingleTestRequested;
        }

        /// <summary>
        /// 从 Tab 1 右键"单点测通"跳转到 Tab 2 并带入参数（PRD FR-3.2.4）。
        /// </summary>
        private void OnSingleTestRequested(object sender, SingleTestEventArgs e)
        {
            if (e == null) { return; }

            toolsPage.SetTestTarget(e.IpText, e.Port);

            // 切到"便捷功能"页
            tabMain.SelectedIndex = 1;

            ShowStatus("已从设备列表带入目标 " + e.IpText + ":" + e.Port + "。", false);
        }

        /// <summary>
        /// 接收扫描页的告警，更新顶部红色横幅（PRD FR-1.5）。
        /// </summary>
        private void OnAlertsChanged(object sender, AlertsChangedEventArgs e)
        {
            if (alertBanner == null) { return; }

            if (e == null || e.Alerts == null || e.Alerts.Count == 0)
            {
                alertBanner.Clear();
                return;
            }

            alertBanner.SetAlerts(e.Alerts);
        }

        private void OnScanStartRequested(object sender, ScanStartEventArgs e)
        {
            // 记录本次扫描的目标网段，供下次启动恢复（PRD FR-4.3.1）
            if (e == null || e.Segment == null) { return; }

            if (e.Segment.Mode == SegmentMode.Cidr)
            {
                _settings.LastNetSegment = e.Segment.Cidr;
            }
        }

        private void OnSettingsSaved(object sender, EventArgs e)
        {
            UpdateNetInfo();
        }

        private void OnPageNotify(object sender, MessageEventArgs e)
        {
            ShowStatus(e.Message, e.IsError);
        }

        private void UpdateNetInfo()
        {
            lblNetInfo.Text = "并发: " + _settings.MaxConcurrency
                + " | 超时: " + _settings.PortTimeoutMs + "ms";
        }

        // -------------------------------------------------------------------
        // 状态栏
        // -------------------------------------------------------------------
        private void ShowStatus(string message, bool isError)
        {
            lblStatus.Text = message;
            lblStatus.ForeColor = isError ? Color.Firebrick : SystemColors.ControlText;

            // 普通提示 8 秒后回到"就绪"；错误提示保留更久
            if (_statusResetTimer == null)
            {
                _statusResetTimer = new Timer();
                _statusResetTimer.Interval = 8000;
                _statusResetTimer.Tick += OnStatusResetTick;
            }

            _statusResetTimer.Stop();
            _statusResetTimer.Interval = isError ? 20000 : 8000;
            _statusResetTimer.Start();
        }

        private void OnStatusResetTick(object sender, EventArgs e)
        {
            _statusResetTimer.Stop();
            lblStatus.Text = Common.UiText.StatusReady;
            lblStatus.ForeColor = SystemColors.ControlText;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_statusResetTimer != null)
            {
                _statusResetTimer.Stop();
                _statusResetTimer.Dispose();
                _statusResetTimer = null;
            }

            base.OnFormClosed(e);
        }
    }
}
