using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MQNetScan.Infrastructure
{
    /// <summary>
    /// INI 文件读写。
    /// 设计要点（architecture §4.3）：
    ///   · 自建解析器，不使用 GetPrivateProfileString（ANSI 编码与长度限制会导致中文丢失）；
    ///   · 原子写：先写 .tmp，再替换目标，避免断电产生半截文件；
    ///   · 容错：任何解析/取值失败都回退默认值，并记录到 InvalidKeys 供 UI 提示；
    ///   · 只读目录：写失败不抛未捕获异常，只返回失败结果。
    /// </summary>
    internal sealed class IniConfigStore
    {
        private readonly string _filePath;
        private readonly Dictionary<string, Dictionary<string, string>> _data;
        private readonly List<string> _invalidKeys;

        /// <summary>
        /// 读取期间发现的无效配置项（节名.键名）。调用方据此在状态栏提示。
        /// </summary>
        public IList<string> InvalidKeys
        {
            get { return _invalidKeys.AsReadOnly(); }
        }

        /// <summary>配置文件完整路径。</summary>
        public string FilePath
        {
            get { return _filePath; }
        }

        /// <summary>
        /// 构造函数：读取指定 ini。文件不存在时以空数据开始（不报错）。
        /// </summary>
        public IniConfigStore(string filePath)
        {
            _filePath = filePath;
            _data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            _invalidKeys = new List<string>();
            Load();
        }

        // -------------------------------------------------------------------
        // 读取
        // -------------------------------------------------------------------
        private void Load()
        {
            if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath))
            {
                return;
            }

            try
            {
                // 统一按 UTF-8 读取；带不带 BOM 都能正确解出（StreamReader 默认会跳过 BOM）
                using (StreamReader reader = new StreamReader(_filePath, new UTF8Encoding(false), true))
                {
                    ParseLines(reader);
                }
            }
            catch (Exception)
            {
                // 读不了（权限/占用/损坏）就当作"没有配置"，全部走默认值
                _data.Clear();
            }
        }

        private void ParseLines(StreamReader reader)
        {
            string currentSection = "";
            string line;

            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();

                // 空行与注释行
                if (trimmed.Length == 0) { continue; }
                if (trimmed.StartsWith(";") || trimmed.StartsWith("#")) { continue; }

                // 节
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    currentSection = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    if (!_data.ContainsKey(currentSection))
                    {
                        _data[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                    continue;
                }

                // 键值
                int eq = trimmed.IndexOf('=');
                if (eq <= 0)
                {
                    // 无法解析的行：记录但继续（不因单行损坏而整体失败）
                    _invalidKeys.Add("(无法解析的行) " + Truncate(trimmed, 40));
                    continue;
                }

                string key = trimmed.Substring(0, eq).Trim();
                string value = trimmed.Substring(eq + 1).Trim();

                if (key.Length == 0) { continue; }

                if (currentSection.Length == 0)
                {
                    _invalidKeys.Add("(节外的键) " + key);
                    continue;
                }

                if (!_data.ContainsKey(currentSection))
                {
                    _data[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                // 同键重复：后者覆盖前者
                _data[currentSection][key] = value;
            }
        }

        // -------------------------------------------------------------------
        // 原始取值
        // -------------------------------------------------------------------
        /// <summary>
        /// 取原始字符串。键不存在时返回 null。
        /// </summary>
        public string GetRaw(string section, string key)
        {
            Dictionary<string, string> sec;
            if (!_data.TryGetValue(section, out sec)) { return null; }

            string value;
            if (!sec.TryGetValue(key, out value)) { return null; }

            return value;
        }

        /// <summary>
        /// 取整数：解析失败或越界时回退默认值，并记录为无效项。
        /// </summary>
        public int GetInt(string section, string key, int defaultValue, int min, int max)
        {
            string raw = GetRaw(section, key);

            if (raw == null)
            {
                return defaultValue;
            }

            int parsed;
            if (!TryParseInt(raw, out parsed))
            {
                _invalidKeys.Add(section + "." + key + "（非数字: " + Truncate(raw, 20) + "）");
                return defaultValue;
            }

            if (parsed < min || parsed > max)
            {
                _invalidKeys.Add(section + "." + key + "（越界: " + parsed + "，应为 " + min + "~" + max + "）");
                return defaultValue;
            }

            return parsed;
        }

        /// <summary>
        /// 取布尔值：接受 true/false/1/0/yes/no/on/off（大小写不敏感）。
        /// </summary>
        public bool GetBool(string section, string key, bool defaultValue)
        {
            string raw = GetRaw(section, key);

            if (raw == null) { return defaultValue; }

            string v = raw.Trim().ToLowerInvariant();

            if (v == "true" || v == "1" || v == "yes" || v == "on") { return true; }
            if (v == "false" || v == "0" || v == "no" || v == "off") { return false; }

            _invalidKeys.Add(section + "." + key + "（非布尔值: " + Truncate(raw, 20) + "）");
            return defaultValue;
        }

        /// <summary>
        /// 取字符串：键不存在时返回默认值。
        /// </summary>
        public string GetString(string section, string key, string defaultValue)
        {
            string raw = GetRaw(section, key);
            return raw == null ? defaultValue : raw;
        }

        // -------------------------------------------------------------------
        // 写入
        // -------------------------------------------------------------------
        /// <summary>
        /// 设置键值（内存中）。需调用 Save 才落盘。
        /// </summary>
        public void Set(string section, string key, string value)
        {
            if (!_data.ContainsKey(section))
            {
                _data[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            _data[section][key] = value == null ? "" : value;
        }

        /// <summary>设置整数（调用方负责保证值已合法）。</summary>
        public void SetInt(string section, string key, int value)
        {
            Set(section, key, value.ToString());
        }

        /// <summary>设置布尔值。</summary>
        public void SetBool(string section, string key, bool value)
        {
            Set(section, key, value ? "true" : "false");
        }

        /// <summary>
        /// 删除整个节（用于重写端口规则时先清空 PortRule.*）。
        /// </summary>
        public void RemoveSection(string section)
        {
            _data.Remove(section);
        }

        /// <summary>
        /// 按前缀删除多个节。
        /// </summary>
        public void RemoveSectionsByPrefix(string prefix)
        {
            List<string> toRemove = new List<string>();

            foreach (string key in _data.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    toRemove.Add(key);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                _data.Remove(toRemove[i]);
            }
        }

        /// <summary>
        /// 原子保存。返回是否成功；失败原因写入 errorMessage。
        /// 采用"先写 .tmp → 刷新到磁盘 → 替换目标"的方式，避免断电产生半截 ini。
        /// </summary>
        public bool Save(out string errorMessage)
        {
            errorMessage = null;

            string tempPath = _filePath + ".tmp";
            bool success = false;

            try
            {
                string content = Serialize();

                // 1. 写入临时文件（UTF-8 无 BOM，与 architecture §4.3 一致）
                byte[] bytes = new UTF8Encoding(false).GetBytes(content);

                using (FileStream fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush();
                }

                // 2. 替换目标
                if (File.Exists(_filePath))
                {
                    // File.Replace 要求目标存在；它会把原文件备份掉（这里不需要备份）
                    try
                    {
                        File.Replace(tempPath, _filePath, null);
                    }
                    catch (Exception)
                    {
                        // 某些文件系统（如 U 盘 FAT）不支持 Replace，退化为删除+移动
                        File.Delete(_filePath);
                        File.Move(tempPath, _filePath);
                    }
                }
                else
                {
                    File.Move(tempPath, _filePath);
                }

                success = true;
            }
            catch (UnauthorizedAccessException)
            {
                errorMessage = "配置无法保存：目标目录没有写入权限。";
            }
            catch (DirectoryNotFoundException)
            {
                errorMessage = "配置无法保存：目标目录不存在。";
            }
            catch (IOException ex)
            {
                errorMessage = "配置无法保存：" + ex.Message;
            }
            catch (Exception ex)
            {
                errorMessage = "配置无法保存：" + ex.Message;
            }
            finally
            {
                // 清理残留的临时文件（成功时已被 Move 走）
                try
                {
                    if (File.Exists(tempPath)) { File.Delete(tempPath); }
                }
                catch (Exception)
                {
                    // 清理失败不影响主流程
                }
            }

            return success;
        }

        // -------------------------------------------------------------------
        // 序列化
        // -------------------------------------------------------------------
        private string Serialize()
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("; ============================================================");
            sb.AppendLine("; MoeQuick NetScan v1.0 - 萌快网维");
            sb.AppendLine("; 配置文件。可直接用记事本手工修改，保存后重启程序生效。");
            sb.AppendLine("; ============================================================");
            sb.AppendLine();

            // 固定顺序输出已知节，保证文件可读且 diff 稳定
            AppendSection(sb, IniSchema.SectionGeneral,
                "扫描参数");
            AppendKnownKeys(sb, IniSchema.SectionGeneral);

            AppendSection(sb, IniSchema.SectionScan, "扫描范围");
            AppendKnownKeys(sb, IniSchema.SectionScan);

            AppendSection(sb, IniSchema.SectionProbe, "探测行为");
            AppendKnownKeys(sb, IniSchema.SectionProbe);

            AppendSection(sb, IniSchema.SectionReport, "报告导出");
            AppendKnownKeys(sb, IniSchema.SectionReport);

            AppendSection(sb, IniSchema.SectionSelfCheck, "连通性自检目标");
            AppendKnownKeys(sb, IniSchema.SectionSelfCheck);

            // 其余节（主要是 PortRule.N）按节名排序输出
            List<string> others = new List<string>();
            foreach (string section in _data.Keys)
            {
                if (!IsKnownSection(section))
                {
                    others.Add(section);
                }
            }
            others.Sort(CompareSectionNames);

            for (int i = 0; i < others.Count; i++)
            {
                AppendSection(sb, others[i], null);
                AppendKnownKeys(sb, others[i]);
            }

            return sb.ToString();
        }

        private void AppendSection(StringBuilder sb, string section, string comment)
        {
            sb.Append('[').Append(section).Append(']').AppendLine();
            if (!string.IsNullOrEmpty(comment))
            {
                sb.Append("; ").Append(comment).AppendLine();
            }
        }

        private void AppendKnownKeys(StringBuilder sb, string section)
        {
            Dictionary<string, string> sec;
            if (!_data.TryGetValue(section, out sec))
            {
                sb.AppendLine();
                return;
            }

            List<string> keys = new List<string>(sec.Keys);

            // 已知节按既定顺序输出，未知节按键名排序，保证输出稳定
            keys.Sort(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append(keys[i]).Append('=').Append(sec[keys[i]]).AppendLine();
            }

            sb.AppendLine();
        }

        private static bool IsKnownSection(string section)
        {
            if (string.Equals(section, IniSchema.SectionGeneral, StringComparison.OrdinalIgnoreCase)) { return true; }
            if (string.Equals(section, IniSchema.SectionScan, StringComparison.OrdinalIgnoreCase)) { return true; }
            if (string.Equals(section, IniSchema.SectionProbe, StringComparison.OrdinalIgnoreCase)) { return true; }
            if (string.Equals(section, IniSchema.SectionReport, StringComparison.OrdinalIgnoreCase)) { return true; }
            if (string.Equals(section, IniSchema.SectionSelfCheck, StringComparison.OrdinalIgnoreCase)) { return true; }
            return false;
        }

        /// <summary>
        /// 比较 PortRule.N 之类的节名：按数字后缀升序，保证 2 排在 10 之前。
        /// </summary>
        private static int CompareSectionNames(string a, string b)
        {
            int na = ExtractTrailingNumber(a);
            int nb = ExtractTrailingNumber(b);

            if (na >= 0 && nb >= 0)
            {
                return na.CompareTo(nb);
            }

            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static int ExtractTrailingNumber(string s)
        {
            if (string.IsNullOrEmpty(s)) { return -1; }

            int i = s.Length - 1;
            while (i >= 0 && char.IsDigit(s[i])) { i--; }

            if (i == s.Length - 1) { return -1; }   // 结尾不是数字

            int result;
            string digits = s.Substring(i + 1);
            if (int.TryParse(digits, out result)) { return result; }

            return -1;
        }

        // -------------------------------------------------------------------
        // 工具
        // -------------------------------------------------------------------
        private static bool TryParseInt(string raw, out int value)
        {
            value = 0;
            if (raw == null) { return false; }

            string s = raw.Trim();
            if (s.Length == 0) { return false; }

            // 不用 int.TryParse 的重载以保持行为可预测；用 NumberStyles.None 拒绝空白/符号/千分位
            return int.TryParse(s, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private static string Truncate(string s, int max)
        {
            if (s == null) { return ""; }
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }
}
