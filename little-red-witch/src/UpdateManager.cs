using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace LittleRedWitch
{
    internal sealed class GitHubReleaseResponse
    {
        public string tag_name { get; set; }
        public string name { get; set; }
        public string body { get; set; }
        public string html_url { get; set; }
        public bool draft { get; set; }
        public bool prerelease { get; set; }
        public GitHubAssetResponse[] assets { get; set; }
    }

    internal sealed class GitHubAssetResponse
    {
        public string name { get; set; }
        public string browser_download_url { get; set; }
        public string digest { get; set; }
        public long size { get; set; }
    }

    internal sealed class UpdateRelease
    {
        public string TagName;
        public Version Version;
        public string AssetUrl;
        public string AssetDigest;
        public long AssetSize;
    }

    internal sealed class UpdateCheckResult
    {
        public bool UpdateAvailable;
        public UpdateRelease Release;
    }

    internal sealed class PreparedUpdate
    {
        public string UpdaterPath;
        public string SourceDirectory;
        public string Version;
    }

    internal static class UpdateService
    {
        private const string RegistryPath = @"Software\LittleRedWitch";
        private const string AutoCheckValue = "AutoCheckUpdates";
        private const string LastCheckValue = "LastUpdateCheckUtc";
        private const string LatestReleaseApi = "https://api.github.com/repos/vky0212/pets/releases/latest";
        private const string ReleaseAssetName = "LittleRedWitch-win-x64.zip";
        private const long MaximumPackageBytes = 50L * 1024L * 1024L;

        public static readonly Version CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version;

        public static bool GetAutoCheckEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, false))
                {
                    if (key == null)
                    {
                        return true;
                    }

                    object value = key.GetValue(AutoCheckValue, 1);
                    return Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;
                }
            }
            catch
            {
                return true;
            }
        }

        public static void SetAutoCheckEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                key.SetValue(AutoCheckValue, enabled ? 1 : 0, RegistryValueKind.DWord);
            }
        }

        public static bool ShouldRunAutomaticCheck()
        {
            if (!GetAutoCheckEnabled())
            {
                return false;
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, false))
                {
                    if (key == null)
                    {
                        return true;
                    }

                    string value = key.GetValue(LastCheckValue, string.Empty) as string;
                    DateTime lastCheck;
                    if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out lastCheck))
                    {
                        return true;
                    }

                    return DateTime.UtcNow - lastCheck.ToUniversalTime() >= TimeSpan.FromHours(24);
                }
            }
            catch
            {
                return true;
            }
        }

        public static void RecordSuccessfulCheck()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    key.SetValue(LastCheckValue, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), RegistryValueKind.String);
                }
            }
            catch
            {
            }
        }

        public static UpdateCheckResult CheckLatestRelease()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(LatestReleaseApi);
            request.Method = "GET";
            request.Accept = "application/vnd.github+json";
            request.UserAgent = "LittleRedWitch-Updater/" + CurrentVersion;
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            request.Headers["X-GitHub-Api-Version"] = "2022-11-28";

            string json;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                json = reader.ReadToEnd();
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            GitHubReleaseResponse responseObject = serializer.Deserialize<GitHubReleaseResponse>(json);
            if (responseObject == null || responseObject.draft || responseObject.prerelease)
            {
                throw new InvalidDataException("GitHub 沒有可用的正式 Release。");
            }

            Version latestVersion = ParseVersion(responseObject.tag_name);
            GitHubAssetResponse asset = FindAsset(responseObject.assets, ReleaseAssetName);
            if (asset == null)
            {
                throw new InvalidDataException("最新版缺少 " + ReleaseAssetName + "。 ");
            }
            if (asset.size <= 0 || asset.size > MaximumPackageBytes)
            {
                throw new InvalidDataException("更新套件大小不合理。");
            }
            if (string.IsNullOrWhiteSpace(asset.digest) || !asset.digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("最新版缺少 SHA-256 digest，為了安全已停止更新。");
            }

            UpdateRelease release = new UpdateRelease();
            release.TagName = responseObject.tag_name;
            release.Version = latestVersion;
            release.AssetUrl = asset.browser_download_url;
            release.AssetDigest = asset.digest.Substring("sha256:".Length).Trim();
            release.AssetSize = asset.size;

            UpdateCheckResult result = new UpdateCheckResult();
            result.Release = release;
            result.UpdateAvailable = latestVersion.CompareTo(CurrentVersion) > 0;
            return result;
        }

        public static PreparedUpdate DownloadAndPrepare(UpdateRelease release)
        {
            if (release == null)
            {
                throw new ArgumentNullException("release");
            }

            string installDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string updatesRoot = Path.Combine(installDirectory, ".updates");
            string versionName = release.Version.ToString();
            string versionDirectory = Path.Combine(updatesRoot, versionName);
            string zipPath = Path.Combine(versionDirectory, ReleaseAssetName);
            string extractDirectory = Path.Combine(versionDirectory, "staging");

            if (Directory.Exists(versionDirectory))
            {
                Directory.Delete(versionDirectory, true);
            }
            Directory.CreateDirectory(versionDirectory);

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (WebClient client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "LittleRedWitch-Updater/" + CurrentVersion;
                client.Headers[HttpRequestHeader.Accept] = "application/octet-stream";
                client.DownloadFile(release.AssetUrl, zipPath);
            }

            FileInfo downloaded = new FileInfo(zipPath);
            if (downloaded.Length != release.AssetSize)
            {
                throw new InvalidDataException("下載大小和 GitHub Release 記錄不一致。");
            }

            string actualDigest = ComputeSha256(zipPath);
            if (!string.Equals(actualDigest, release.AssetDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("更新套件 SHA-256 驗證失敗。");
            }

            ExtractZipSafely(zipPath, extractDirectory);
            string sourceDirectory = Path.Combine(extractDirectory, "LittleRedWitch");
            string appPath = Path.Combine(sourceDirectory, "LittleRedWitch.exe");
            string updaterPath = Path.Combine(sourceDirectory, "LittleRedWitch.Updater.exe");
            if (!File.Exists(appPath) || !File.Exists(updaterPath))
            {
                throw new InvalidDataException("更新套件缺少主程式或 Updater。");
            }

            PreparedUpdate prepared = new PreparedUpdate();
            prepared.UpdaterPath = updaterPath;
            prepared.SourceDirectory = sourceDirectory;
            prepared.Version = release.Version.ToString();
            return prepared;
        }

        public static void LaunchUpdater(PreparedUpdate prepared)
        {
            if (prepared == null)
            {
                throw new ArgumentNullException("prepared");
            }

            string currentExecutable = Assembly.GetExecutingAssembly().Location;
            string targetDirectory = Path.GetDirectoryName(currentExecutable);
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = prepared.UpdaterPath;
            startInfo.WorkingDirectory = prepared.SourceDirectory;
            startInfo.UseShellExecute = false;
            startInfo.Arguments =
                "--wait-pid " + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) +
                " --source-dir " + Quote(prepared.SourceDirectory) +
                " --target-dir " + Quote(targetDirectory) +
                " --restart " + Quote(currentExecutable) +
                " --version " + Quote(prepared.Version);

            Process.Start(startInfo);
        }

        private static GitHubAssetResponse FindAsset(GitHubAssetResponse[] assets, string name)
        {
            if (assets == null)
            {
                return null;
            }

            foreach (GitHubAssetResponse asset in assets)
            {
                if (asset != null && string.Equals(asset.name, name, StringComparison.Ordinal))
                {
                    return asset;
                }
            }
            return null;
        }

        private static Version ParseVersion(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                throw new InvalidDataException("Release tag 沒有版本號。");
            }

            string value = tagName.Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(1);
            }
            int suffix = value.IndexOf('-');
            if (suffix >= 0)
            {
                value = value.Substring(0, suffix);
            }

            Version version;
            if (!Version.TryParse(value, out version))
            {
                throw new InvalidDataException("無法解析 Release 版本：" + tagName);
            }
            return version;
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] digest = sha.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest)
                {
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }
                return builder.ToString();
            }
        }

        private static void ExtractZipSafely(string zipPath, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            string destinationRoot = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;

            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    string destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, relativePath));
                    if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("更新套件包含不安全的路徑。");
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destinationPath);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    using (Stream input = entry.Open())
                    using (FileStream output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        input.CopyTo(output);
                    }
                }
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
