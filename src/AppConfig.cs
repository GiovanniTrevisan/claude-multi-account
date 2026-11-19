using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

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
        /// When true, Claude sees the real %LOCALAPPDATA% (and therefore any saved
        /// third-party gateway). Meant for the one profile that should use it.
        /// </summary>
        public bool IsLocalDataShared { get; private set; }

        public const string SharedLocalDataFlag = "--shared-local-data";

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
            var isLocalDataShared = HasFlag(args, SharedLocalDataFlag) ||
                GetEnvironmentVariableOrDefault("CLAUDE_WORK_SHARED_LOCAL_DATA", "0") == "1";

            return new AppConfig
            {
                ProfileName = profileName,
                AppUserModelId = appUserModelId,
                ProductName = productName,
                IsLocalDataShared = isLocalDataShared,
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

        public string LogFilePath
        {
            get { return Path.Combine(UserDataDirectory, "claude-work-launcher.log"); }
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

        /// <summary>
        /// Private stand-in for %LOCALAPPDATA%, handed to Claude unless
        /// <see cref="IsLocalDataShared"/> is set. Claude keeps machine-wide
        /// state there that ignores --user-data-dir: most importantly a saved
        /// third-party gateway (Claude-3p), which otherwise forces every profile
        /// into one shared gateway window.
        /// </summary>
        public string PrivateLocalAppDataDirectory
        {
            get { return Path.Combine(UserDataDirectory, "LocalAppData"); }
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
                    + " --name=" + Quote(ProductName)
                    + (IsLocalDataShared ? " " + SharedLocalDataFlag : "");
            }
        }

        // The profile name becomes a folder under %LOCALAPPDATA%, so it must stay
        // a plain folder name (no "..", slashes or drive letters).
        private static readonly Regex ValidProfileName = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 _.-]{0,63}\z");

        // Windows limits an AppUserModelID to 128 characters with no spaces.
        private static readonly Regex ValidAppUserModelId = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z");

        /// <summary>
        /// Returns a user-facing description of the first invalid setting, or
        /// null when every setting is safe to use in paths and command lines.
        /// </summary>
        public string GetValidationError()
        {
            if (!ValidProfileName.IsMatch(ProfileName) || ProfileName.Contains("..") || ProfileName.EndsWith(".") || ProfileName.EndsWith(" "))
                return "Invalid profile name: \"" + ProfileName + "\".\nUse only letters, digits, spaces, '-', '_' and '.'.";

            if (!ValidAppUserModelId.IsMatch(AppUserModelId))
                return "Invalid AppUserModelID: \"" + AppUserModelId + "\".\nUse only letters, digits, '.', '-' and '_' (no spaces).";

            if (ProductName.Trim().Length == 0 || ProductName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return "Invalid name: \"" + ProductName + "\".\nIt is used as a shortcut file name, so it cannot contain \\ / : * ? \" < > |.";

            return null;
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

        private static bool HasFlag(string[] args, string flagName)
        {
            return Array.Exists(args, argument => string.Equals(argument, flagName, StringComparison.OrdinalIgnoreCase));
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
