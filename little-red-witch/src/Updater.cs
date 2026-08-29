using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]
[assembly: AssemblyInformationalVersion("1.2.0")]

namespace LittleRedWitchUpdater
{
    internal static class Program
    {
        private static readonly string[] UpdateFiles =
        {
            "LittleRedWitch.exe",
            "LittleRedWitch.Updater.exe",
            "README.md"
        };

        [STAThread]
        private static int Main(string[] args)
        {
            bool suppressUi = Array.IndexOf(args, "--no-ui") >= 0;
            try
            {
                Dictionary<string, string> options = ParseOptions(args);
                int waitPid = ParseRequiredInt(options, "wait-pid");
                string sourceDirectory = GetRequiredPath(options, "source-dir");
                string targetDirectory = GetRequiredPath(options, "target-dir");
                string restartPath = options.ContainsKey("restart") ? Path.GetFullPath(options["restart"]) : string.Empty;
                string version = options.ContainsKey("version") ? options["version"] : "unknown";
                bool noRestart = options.ContainsKey("no-restart");

                WaitForProcess(waitPid);
                InstallUpdate(sourceDirectory, targetDirectory, version);

                if (!noRestart && !string.IsNullOrWhiteSpace(restartPath))
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = restartPath;
                    startInfo.WorkingDirectory = targetDirectory;
                    startInfo.UseShellExecute = true;
                    Process.Start(startInfo);
                }

                WriteLog("Installed version " + version + " successfully.");
                return 0;
            }
            catch (Exception ex)
            {
                WriteLog("Update failed: " + ex);
                Console.Error.WriteLine(ex);
                if (!suppressUi)
                {
                    MessageBox.Show(
                        "小紅巫更新失敗，舊版本已盡可能保留。\n\n" + ex.Message,
                        "小紅巫 Updater",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                return 1;
            }
        }

        private static void InstallUpdate(string sourceDirectory, string targetDirectory, string version)
        {
            foreach (string fileName in UpdateFiles)
            {
                string sourcePath = Path.Combine(sourceDirectory, fileName);
                if (fileName != "README.md" && !File.Exists(sourcePath))
                {
                    throw new FileNotFoundException("更新套件缺少 " + fileName, sourcePath);
                }
            }

            Directory.CreateDirectory(targetDirectory);
            string backupRoot = Path.Combine(
                targetDirectory,
                ".update-backups",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-v" + SanitizeVersion(version));
            Directory.CreateDirectory(backupRoot);

            List<string> backedUpFiles = new List<string>();
            try
            {
                foreach (string fileName in UpdateFiles)
                {
                    string targetPath = Path.Combine(targetDirectory, fileName);
                    if (File.Exists(targetPath))
                    {
                        File.Copy(targetPath, Path.Combine(backupRoot, fileName), true);
                        backedUpFiles.Add(fileName);
                    }
                }

                foreach (string fileName in UpdateFiles)
                {
                    string sourcePath = Path.Combine(sourceDirectory, fileName);
                    if (!File.Exists(sourcePath))
                    {
                        continue;
                    }

                    string targetPath = Path.Combine(targetDirectory, fileName);
                    string temporaryPath = targetPath + ".updating";
                    File.Copy(sourcePath, temporaryPath, true);

                    if (File.Exists(targetPath))
                    {
                        File.Replace(temporaryPath, targetPath, null, true);
                    }
                    else
                    {
                        File.Move(temporaryPath, targetPath);
                    }
                }
            }
            catch
            {
                foreach (string fileName in backedUpFiles)
                {
                    string backupPath = Path.Combine(backupRoot, fileName);
                    string targetPath = Path.Combine(targetDirectory, fileName);
                    if (File.Exists(backupPath))
                    {
                        File.Copy(backupPath, targetPath, true);
                    }
                }
                throw;
            }
        }

        private static void WaitForProcess(int processId)
        {
            if (processId <= 0)
            {
                return;
            }

            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    if (!process.WaitForExit(30000))
                    {
                        throw new TimeoutException("等待小紅巫結束逾時。");
                    }
                }
            }
            catch (ArgumentException)
            {
            }
        }

        private static Dictionary<string, string> ParseOptions(string[] args)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < args.Length; index++)
            {
                string token = args[index];
                if (!token.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                string key = token.Substring(2);
                string value = string.Empty;
                if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[++index];
                }
                result[key] = value;
            }
            return result;
        }

        private static int ParseRequiredInt(Dictionary<string, string> options, string name)
        {
            string value;
            int parsed;
            if (!options.TryGetValue(name, out value) || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                throw new ArgumentException("缺少或無效的 --" + name);
            }
            return parsed;
        }

        private static string GetRequiredPath(Dictionary<string, string> options, string name)
        {
            string value;
            if (!options.TryGetValue(name, out value) || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("缺少 --" + name);
            }
            return Path.GetFullPath(value);
        }

        private static string SanitizeVersion(string version)
        {
            StringBuilder builder = new StringBuilder();
            foreach (char character in version)
            {
                if (char.IsLetterOrDigit(character) || character == '.' || character == '-' || character == '_')
                {
                    builder.Append(character);
                }
            }
            return builder.Length == 0 ? "unknown" : builder.ToString();
        }

        private static void WriteLog(string message)
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LittleRedWitch");
                Directory.CreateDirectory(directory);
                File.AppendAllText(
                    Path.Combine(directory, "updater.log"),
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}
