using System.Diagnostics;
using System.IO;

namespace ClaudeMultiAccount
{
    /// <summary>
    /// Starts the isolated Claude Desktop instance. If it is already running,
    /// Electron's own single-instance-per-profile lock simply focuses the
    /// existing window instead of starting a second process.
    /// </summary>
    internal static class ClaudeInstanceLauncher
    {
        public static void Launch(string claudeExecutablePath, AppConfig config)
        {
            var arguments =
                "--user-data-dir=\"" + config.UserDataDirectory + "\" " +
                "--disk-cache-dir=\"" + config.CacheDirectory + "\"";

            var startInfo = new ProcessStartInfo(claudeExecutablePath, arguments)
            {
                UseShellExecute = false
            };

            if (!config.IsLocalDataShared)
            {
                // Only the child process sees the redirected folder; see
                // AppConfig.PrivateLocalAppDataDirectory for why.
                Directory.CreateDirectory(config.PrivateLocalAppDataDirectory);
                startInfo.EnvironmentVariables["LOCALAPPDATA"] = config.PrivateLocalAppDataDirectory;
            }

            Process.Start(startInfo);
        }
    }
}
