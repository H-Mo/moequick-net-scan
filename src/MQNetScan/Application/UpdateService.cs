using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace MQNetScan.Application
{
    [DataContract]
    internal sealed class VersionManifest
    {
        [DataMember(Name = "version")]
        public string Version { get; set; }

        [DataMember(Name = "downloadUrl")]
        public string DownloadUrl { get; set; }

        [DataMember(Name = "releaseDate")]
        public string ReleaseDate { get; set; }

        [DataMember(Name = "releaseNotes")]
        public string[] ReleaseNotes { get; set; }
    }

    internal sealed class UpdateCheckResult
    {
        public bool HasUpdate { get; private set; }
        public string LatestVersion { get; private set; }
        public string DownloadUrl { get; private set; }
        public string ReleaseDate { get; private set; }
        public string[] ReleaseNotes { get; private set; }

        public UpdateCheckResult(bool hasUpdate, string latestVersion, string downloadUrl,
            string releaseDate, string[] releaseNotes)
        {
            HasUpdate = hasUpdate;
            LatestVersion = latestVersion ?? "";
            DownloadUrl = downloadUrl ?? "";
            ReleaseDate = releaseDate ?? "";
            ReleaseNotes = releaseNotes ?? new string[0];
        }

        public static UpdateCheckResult NoUpdate()
        {
            return new UpdateCheckResult(false, "", "", "", new string[0]);
        }
    }

    /// <summary>启动时静默检查和设置页手动检查共用的版本服务。</summary>
    internal static class UpdateService
    {
        public const string ManifestUrl = "https://div.moe/app/moequick-net-scan/version.json";
        public const int RequestTimeoutMs = 4000;
        public const int MaxResponseBytes = 65536;

        public static UpdateCheckResult Check(string currentVersion)
        {
            try
            {
                WebRequest request = WebRequest.Create(ManifestUrl);
                request.Method = "GET";
                request.Timeout = RequestTimeoutMs;
                request.Credentials = null;
                request.Proxy = WebRequest.DefaultWebProxy;

                using (WebResponse response = request.GetResponse())
                {
                    if (response.ContentLength > MaxResponseBytes) { return UpdateCheckResult.NoUpdate(); }
                    using (Stream stream = response.GetResponseStream())
                    {
                        byte[] data = ReadBounded(stream, MaxResponseBytes);
                        VersionManifest manifest = Deserialize(data);
                        return Compare(currentVersion, manifest);
                    }
                }
            }
            catch (Exception)
            {
                return UpdateCheckResult.NoUpdate();
            }
        }

        internal static VersionManifest Deserialize(byte[] data)
        {
            if (data == null || data.Length == 0 || data.Length > MaxResponseBytes)
            {
                throw new InvalidDataException("Invalid manifest size.");
            }

            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(VersionManifest));
            using (MemoryStream stream = new MemoryStream(data))
            {
                VersionManifest manifest = serializer.ReadObject(stream) as VersionManifest;
                if (manifest == null) { throw new InvalidDataException("Invalid manifest."); }
                return manifest;
            }
        }

        internal static UpdateCheckResult Compare(string currentVersion, VersionManifest manifest)
        {
            Version current;
            Version latest;
            DateTime releaseDate;
            if (manifest == null
                || !TryParseVersion(currentVersion, out current)
                || !TryParseVersion(manifest.Version, out latest)
                || latest <= current
                || !IsAllowedDownloadUrl(manifest.DownloadUrl)
                || !DateTime.TryParseExact(manifest.ReleaseDate, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out releaseDate))
            {
                return UpdateCheckResult.NoUpdate();
            }

            string[] notes = manifest.ReleaseNotes ?? new string[0];
            return new UpdateCheckResult(true, latest.ToString(3), manifest.DownloadUrl,
                releaseDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), notes);
        }

        internal static bool TryParseVersion(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(value)) { return false; }
            string[] parts = value.Split('.');
            if (parts.Length != 3) { return false; }
            int major, minor, build;
            if (!int.TryParse(parts[0], out major)
                || !int.TryParse(parts[1], out minor)
                || !int.TryParse(parts[2], out build)
                || major < 0 || minor < 0 || build < 0)
            {
                return false;
            }
            version = new Version(major, minor, build);
            return true;
        }

        internal static bool IsAllowedDownloadUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri)) { return false; }
            return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.IndexOf("/H-Mo/moequick-net-scan/releases/download/",
                    StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static byte[] ReadBounded(Stream stream, int maxBytes)
        {
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[4096];
                int read;
                while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + read > maxBytes) { throw new InvalidDataException("Manifest too large."); }
                    buffer.Write(chunk, 0, read);
                }
                return buffer.ToArray();
            }
        }

        public static void CheckAsync(string currentVersion, Action<UpdateCheckResult> completed)
        {
            Thread worker = new Thread(delegate()
            {
                UpdateCheckResult result = Check(currentVersion);
                if (completed != null) { completed(result); }
            });
            worker.IsBackground = true;
            worker.Name = "MoeQuick update check";
            worker.Start();
        }
    }
}
