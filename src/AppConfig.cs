using System;
using System.Diagnostics;
using System.IO;

namespace ClaudeMultiAccount
{
    /// <summary>
    /// Runtime configuration, resolved from command-line arguments and
    /// environment variables with sensible defaults. Paths are derived from
    /// where this executable currently lives, so the tool works regardless of
    /// where it is installed.
    /// </summary>
    internal sealed class AppConfig
    {
        public string ProfileName { get; private set; }
        public string AppUserModelId { get; private set; }
        public string ProductName { get; private set; }

        /// <summary>
        /// Resolves configuration with precedence command-line argument &gt;
        /// environment variable &gt; default. Accepts <c>--profile=</c>,
        /// <c>--aumid=</c> and <c>--name=</c>; any of them may be omitted.
        /// </summary>
        public static AppConfig FromArgumentsAndEnvironment(string[] args)
        {
            var profileName = ResolveValue(args, "--profile=", "CLAUDE_WORK_PROFILE", "Claude-Work");
            var appUserModelId = ResolveValue(args, "--aumid=", "CLAUDE_WORK_AUMID", "ClaudeMultiAccount.Work");
            var productName = ResolveValue(args, "--name=", "CLAUDE_WORK_NAME", "Claude Work");

            return new AppConfig
            {
                ProfileName = profileName,
                AppUserModelId = appUserModelId,
                ProductName = productName,
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

        /// <summary>
        /// Command-line fragment that reproduces this exact identity
        /// (<c>--profile=</c>/<c>--aumid=</c>/<c>--name=</c>, always all three).
        /// Embedded into shortcuts (<see cref="ShortcutInstaller"/>) and the
        /// taskbar RelaunchCommand (<see cref="TaskbarIdentityStamper"/>) so the
        /// same profile reopens deterministically no matter what environment
        /// variables happen to be set at relaunch time.
        /// </summary>
        public string LaunchArguments
        {
            get
            {
                return "--profile=" + Quote(ProfileName)
                    + " --aumid=" + Quote(AppUserModelId)
                    + " --name=" + Quote(ProductName);
            }
        }

        private static string LocalAppDataDirectory
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
        }

        private static string ResolveValue(string[] args, string flagName, string environmentVariableName, string fallback)
        {
            var argumentValue = GetArgumentValue(args, flagName);
            if (!string.IsNullOrEmpty(argumentValue))
                return argumentValue;

            return GetEnvironmentVariableOrDefault(environmentVariableName, fallback);
        }

        /// <summary>
        /// Scans args for an entry starting with flagName (e.g. "--profile="),
        /// case-insensitive on the flag itself, and returns everything after the
        /// first '='. Windows already de-quotes argv, so "--name=Claude Work"
        /// arrives as a single element even though the value has a space in it.
        /// Returns null when the flag is not present.
        /// </summary>
        private static string GetArgumentValue(string[] args, string flagName)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith(flagName, StringComparison.OrdinalIgnoreCase))
                    return args[i].Substring(flagName.Length);
            }
            return null;
        }

        private static string GetEnvironmentVariableOrDefault(string name, string fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        private static string Quote(string value)
        {
            return "\"" + value + "\"";
        }
    }
}
