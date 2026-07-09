using System;
using System.Diagnostics;
using System.IO;

namespace ClaudeMultiAccount
{
    /// <summary>
    /// Runtime configuration, resolved from environment variables with sensible
    /// defaults. Paths are derived from where this executable currently lives, so
    /// the tool works regardless of where it is installed.
    /// </summary>
    internal sealed class AppConfig
    {
        public string ProfileName { get; private set; }
        public string AppUserModelId { get; private set; }
        public string ProductName { get; private set; }

        public static AppConfig FromEnvironment()
        {
            return new AppConfig
            {
                ProfileName = GetEnvironmentVariableOrDefault("CLAUDE_WORK_PROFILE", "Claude-Work"),
                AppUserModelId = GetEnvironmentVariableOrDefault("CLAUDE_WORK_AUMID", "ClaudeMultiAccount.Work"),
                ProductName = GetEnvironmentVariableOrDefault("CLAUDE_WORK_NAME", "Claude Work"),
            };
        }

        public string UserDataDirectory
        {
            get { return Path.Combine(LocalAppDataDirectory, ProfileName); }
        }

        public string CacheDirectory
        {
            get { return Path.Combine(LocalAppDataDirectory, ProfileName + "-Cache"); }
        }

        public string ExecutablePath
        {
            get { return Process.GetCurrentProcess().MainModule.FileName; }
        }

        public string InstallDirectory
        {
            get { return Path.GetDirectoryName(ExecutablePath); }
        }

        public string IconPath
        {
            get { return Path.Combine(InstallDirectory, "claude-work.ico"); }
        }

        public string ClaudeExeCacheFile
        {
            get { return Path.Combine(UserDataDirectory, ".claude-exe-path"); }
        }

        public string StartMenuShortcutPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs",
                    ProductName + ".lnk");
            }
        }

        public string DesktopShortcutPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    ProductName + ".lnk");
            }
        }

        private static string LocalAppDataDirectory
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
        }

        private static string GetEnvironmentVariableOrDefault(string name, string fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
    }
}
