using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace MQNetScan.Application
{
    internal enum LocalInfoCategory
    {
        System = 0,
        Network = 1,
        Hardware = 2,
        Runtime = 3
    }

    internal sealed class LocalInfoRow
    {
        public string Name { get; private set; }
        public string Value { get; private set; }

        public LocalInfoRow(string name, string value)
        {
            Name = name ?? "";
            Value = value ?? "";
        }
    }

    internal sealed class LocalInfoSnapshot
    {
        public LocalInfoCategory Category { get; private set; }
        public IList<LocalInfoRow> Rows { get; private set; }
        public string Error { get; private set; }
        public DateTime CollectedAt { get; private set; }

        public LocalInfoSnapshot(LocalInfoCategory category, IList<LocalInfoRow> rows, string error)
        {
            Category = category;
            List<LocalInfoRow> copy = new List<LocalInfoRow>();
            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++) { copy.Add(rows[i]); }
            }
            Rows = copy.AsReadOnly();
            Error = error ?? "";
            CollectedAt = DateTime.Now;
        }
    }

    /// <summary>按需读取本机信息；每个类别独立失败，不影响其它类别。</summary>
    internal static class LocalMachineInfoService
    {
        public const int HardwareQueryTimeoutSeconds = 5;

        public static LocalInfoSnapshot Read(LocalInfoCategory category)
        {
            try
            {
                if (category == LocalInfoCategory.System) { return ReadSystem(); }
                if (category == LocalInfoCategory.Network) { return ReadNetwork(); }
                if (category == LocalInfoCategory.Hardware) { return ReadHardware(); }
                if (category == LocalInfoCategory.Runtime) { return ReadRuntime(); }
                return Failure(category, "不支持的信息类别。");
            }
            catch (Exception ex)
            {
                return Failure(category, "读取失败：" + ex.Message);
            }
        }

        private static LocalInfoSnapshot ReadSystem()
        {
            List<LocalInfoRow> rows = new List<LocalInfoRow>();
            rows.Add(new LocalInfoRow("计算机名称", Environment.MachineName));
            rows.Add(new LocalInfoRow("当前用户", Environment.UserDomainName + "\\" + Environment.UserName));
            rows.Add(new LocalInfoRow("操作系统", ReadRegistryString(
                Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", Environment.OSVersion.VersionString)));
            rows.Add(new LocalInfoRow("版本", ReadRegistryString(
                Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion",
                ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ReleaseId", "未知"))));
            rows.Add(new LocalInfoRow("内部版本", ReadRegistryString(
                Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber",
                Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture))));
            rows.Add(new LocalInfoRow("系统架构", Environment.Is64BitOperatingSystem ? "64 位" : "32 位"));
            rows.Add(new LocalInfoRow("程序运行时", Environment.Version.ToString()));
            try
            {
                ulong tick = Infrastructure.NativeMethods.GetTickCount64();
                DateTime boot = DateTime.Now - TimeSpan.FromMilliseconds(tick);
                rows.Add(new LocalInfoRow("本次启动时间", boot.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            }
            catch (Exception ex)
            {
                rows.Add(new LocalInfoRow("本次启动时间", "无法读取：" + ex.Message));
            }
            return Success(LocalInfoCategory.System, rows);
        }

        private static LocalInfoSnapshot ReadNetwork()
        {
            List<LocalInfoRow> rows = new List<LocalInfoRow>();
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                NetworkInterface ni = interfaces[i];
                string prefix = ni.Name + " / " + ni.Description;
                rows.Add(new LocalInfoRow("网卡", prefix));
                rows.Add(new LocalInfoRow("状态", ni.OperationalStatus + " / " + ni.NetworkInterfaceType));
                try
                {
                    PhysicalAddress mac = ni.GetPhysicalAddress();
                    string macText = FormatMac(mac);
                    if (macText.Length > 0) { rows.Add(new LocalInfoRow("MAC", macText)); }
                }
                catch (Exception) { }

                try
                {
                    IPInterfaceProperties properties = ni.GetIPProperties();
                    for (int a = 0; a < properties.UnicastAddresses.Count; a++)
                    {
                        UnicastIPAddressInformation unicast = properties.UnicastAddresses[a];
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork
                            || unicast.Address.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            rows.Add(new LocalInfoRow("IP 地址", unicast.Address.ToString()));
                            if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                rows.Add(new LocalInfoRow("子网掩码", unicast.IPv4Mask.ToString()));
                            }
                        }
                    }
                    for (int g = 0; g < properties.GatewayAddresses.Count; g++)
                    {
                        rows.Add(new LocalInfoRow("默认网关", properties.GatewayAddresses[g].Address.ToString()));
                    }
                    for (int d = 0; d < properties.DnsAddresses.Count; d++)
                    {
                        rows.Add(new LocalInfoRow("DNS", properties.DnsAddresses[d].ToString()));
                    }
                    rows.Add(new LocalInfoRow("DHCP", properties.GetIPv4Properties() != null
                        && properties.GetIPv4Properties().IsDhcpEnabled ? "已启用" : "未启用 / 不适用"));
                }
                catch (Exception ex)
                {
                    rows.Add(new LocalInfoRow("详细信息", "无法读取：" + ex.Message));
                }
                rows.Add(new LocalInfoRow("", ""));
            }
            if (interfaces.Length == 0) { rows.Add(new LocalInfoRow("网卡", "未检测到网络接口。")); }
            return Success(LocalInfoCategory.Network, rows);
        }

        private static LocalInfoSnapshot ReadHardware()
        {
            List<LocalInfoRow> rows = new List<LocalInfoRow>();
            try
            {
                QueryWmi("SELECT Name, NumberOfLogicalProcessors FROM Win32_Processor", delegate(ManagementBaseObject item)
                {
                    rows.Add(new LocalInfoRow("处理器", GetWmiString(item, "Name")));
                    rows.Add(new LocalInfoRow("逻辑处理器", GetWmiString(item, "NumberOfLogicalProcessors")));
                });
            }
            catch (Exception ex) { rows.Add(new LocalInfoRow("处理器", "无法读取：" + ex.Message)); }

            try
            {
                QueryWmi("SELECT TotalPhysicalMemory, Manufacturer, Model FROM Win32_ComputerSystem", delegate(ManagementBaseObject item)
                {
                    rows.Add(new LocalInfoRow("内存总量", FormatBytes(ParseLong(GetWmiString(item, "TotalPhysicalMemory")))));
                    rows.Add(new LocalInfoRow("制造商", GetWmiString(item, "Manufacturer")));
                    rows.Add(new LocalInfoRow("型号", GetWmiString(item, "Model")));
                });
            }
            catch (Exception ex) { rows.Add(new LocalInfoRow("硬件信息", "无法完整读取：" + ex.Message)); }
            return Success(LocalInfoCategory.Hardware, rows);
        }

        private static void QueryWmi(string query, Action<ManagementBaseObject> consume)
        {
            EnumerationOptions options = new EnumerationOptions();
            options.Timeout = TimeSpan.FromSeconds(HardwareQueryTimeoutSeconds);
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                new ManagementScope("\\\\.\\root\\CIMV2"), new ObjectQuery(query), options))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementObject item in results)
                {
                    using (item) { consume(item); }
                }
            }
        }

        private static LocalInfoSnapshot ReadRuntime()
        {
            List<LocalInfoRow> rows = new List<LocalInfoRow>();
            rows.Add(new LocalInfoRow("当前程序目标", ".NET Framework 4.0"));
            rows.Add(new LocalInfoRow("当前 CLR", Environment.Version.ToString()));
            try { AddFrameworkRows(rows); }
            catch (Exception ex) { rows.Add(new LocalInfoRow(".NET Framework", "无法读取注册信息：" + ex.Message)); }

            int vcStart = rows.Count;
            AddVCRuntimeRows(rows, RegistryView.Registry32);
            if (Environment.Is64BitOperatingSystem) { AddVCRuntimeRows(rows, RegistryView.Registry64); }
            if (rows.Count == vcStart) { rows.Add(new LocalInfoRow("VC++ 运行库", "未在卸载登记项中发现。")); }
            return Success(LocalInfoCategory.Runtime, rows);
        }

        private static void AddFrameworkRows(List<LocalInfoRow> rows)
        {
            string path = @"SOFTWARE\Microsoft\NET Framework Setup\NDP";
            using (RegistryKey root = Registry.LocalMachine.OpenSubKey(path))
            {
                if (root == null) { rows.Add(new LocalInfoRow(".NET Framework", "未读取到安装登记。")); return; }
                string[] names = root.GetSubKeyNames();
                for (int i = 0; i < names.Length; i++)
                {
                    if (names[i].Length == 0 || names[i][0] != 'v') { continue; }
                    using (RegistryKey versionKey = root.OpenSubKey(names[i]))
                    {
                        if (versionKey == null) { continue; }
                        object installed = versionKey.GetValue("Install");
                        if (installed != null && Convert.ToInt32(installed, CultureInfo.InvariantCulture) == 1)
                        {
                            rows.Add(new LocalInfoRow(".NET Framework", names[i]));
                        }
                        using (RegistryKey full = versionKey.OpenSubKey("Full"))
                        {
                            if (full != null)
                            {
                                object release = full.GetValue("Release");
                                if (release != null)
                                {
                                    rows.Add(new LocalInfoRow(".NET Framework 4.x", "Release "
                                        + Convert.ToString(release, CultureInfo.InvariantCulture)));
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void AddVCRuntimeRows(List<LocalInfoRow> rows, RegistryView view)
        {
            try
            {
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey uninstall = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (uninstall == null) { return; }
                    string[] names = uninstall.GetSubKeyNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        using (RegistryKey item = uninstall.OpenSubKey(names[i]))
                        {
                            if (item == null) { continue; }
                            string display = Convert.ToString(item.GetValue("DisplayName", ""), CultureInfo.InvariantCulture);
                            if (display.IndexOf("Microsoft Visual C++", StringComparison.OrdinalIgnoreCase) < 0
                                || display.IndexOf("Redistributable", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                continue;
                            }
                            string version = Convert.ToString(item.GetValue("DisplayVersion", ""), CultureInfo.InvariantCulture);
                            rows.Add(new LocalInfoRow("VC++ 运行库（" + (view == RegistryView.Registry64 ? "64 位" : "32 位") + ")",
                                display + (version.Length > 0 ? " / " + version : "")));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                rows.Add(new LocalInfoRow("VC++ 运行库", "无法读取注册信息：" + ex.Message));
            }
        }

        private static string ReadRegistryString(RegistryKey hive, string path, string name, string fallback)
        {
            try
            {
                using (RegistryKey key = hive.OpenSubKey(path))
                {
                    object value = key == null ? null : key.GetValue(name);
                    return value == null ? fallback : Convert.ToString(value, CultureInfo.InvariantCulture);
                }
            }
            catch (Exception) { return fallback; }
        }

        private static string GetWmiString(ManagementBaseObject item, string property)
        {
            object value = item[property];
            return value == null ? "未知" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static long ParseLong(string value)
        {
            long result;
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : 0;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) { return "未知"; }
            double gb = bytes / (1024.0 * 1024.0 * 1024.0);
            return gb.ToString("0.##", CultureInfo.InvariantCulture) + " GB ("
                + bytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes)";
        }

        private static string FormatMac(PhysicalAddress address)
        {
            if (address == null) { return ""; }
            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length == 0) { return ""; }
            string[] values = new string[bytes.Length];
            for (int i = 0; i < bytes.Length; i++) { values[i] = bytes[i].ToString("X2", CultureInfo.InvariantCulture); }
            return String.Join(":", values);
        }

        private static LocalInfoSnapshot Success(LocalInfoCategory category, IList<LocalInfoRow> rows)
        {
            return new LocalInfoSnapshot(category, rows, "");
        }

        private static LocalInfoSnapshot Failure(LocalInfoCategory category, string error)
        {
            return new LocalInfoSnapshot(category, new List<LocalInfoRow>(), error);
        }
    }
}
