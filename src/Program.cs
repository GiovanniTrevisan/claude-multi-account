using System;
using System.IO;
using System.Threading;

namespace ClaudeMultiAccount
{
    /// <summary>
    /// Entry point. See README.md for what this tool does and why it is needed.
    /// </summary>
    internal static class Program
    {
        private const string InstallShortcutsArgument = "--install-shortcuts";
        private const string UninstallArgument = "--uninstall";

        [STAThread]
        private static int Main(string[] args)
        {
            var config = AppConfig.FromEnvironment();

            // Run before EnsureProfileDirectoriesExist: uninstalling should not
            // recreate the profile directory it might have just been asked to leave alone.
            if (Array.IndexOf(args, UninstallArgument) >= 0)
            {
                ShortcutInstaller.UninstallAll(config);
                return 0;
            }

            EnsureProfileDirectoriesExist(config);

            if (Array.IndexOf(args, InstallShortcutsArgument) >= 0)
            {
                ShortcutInstaller.InstallAll(config);
                return 0;
            }

            var claudeExecutablePath = ClaudeInstallationLocator.Resolve(config.ClaudeExeCacheFile);
            if (claudeExecutablePath == null)
            {
                NativeMessageBox.Show(
                    "Claude Desktop was not found.\n\nInstall it from the Microsoft Store and try again.",
                    config.ProductName);
                return 1;
            }

            ShortcutInstaller.RegisterAppUserModelId(config);

            // Always (re)launch: Electron's per-profile single-instance lock takes
            // care of just focusing an already-running window.
            ClaudeInstanceLauncher.Launch(claudeExecutablePath, config);

            RunAsSingleInstancePerProfile(config, () =>
            {
                var stamper = new TaskbarIdentityStamper(config);
                new InstanceIdentityWatcher(config, stamper).WatchUntilInstanceExits();
            });
            return 0;
        }

        private static void EnsureProfileDirectoriesExist(AppConfig config)
        {
            try
            {
                Directory.CreateDirectory(config.UserDataDirectory);
                Directory.CreateDirectory(config.CacheDirectory);
            }
            catch
            {
                // Nothing actionable if this races with another launch or the
                // directories already exist.
            }
        }

        /// <summary>
        /// Ensures only one resident watcher runs per profile. If another
        /// launcher process already owns the watcher, this process has already
        /// done its job above (focusing the window) and can exit immediately.
        /// </summary>
        private static void RunAsSingleInstancePerProfile(AppConfig config, Action watch)
        {
            bool isOwner;
            var mutexName = "ClaudeMultiAccountWatcher_" + Sanitize(config.ProfileName);
            using (new Mutex(initiallyOwned: true, name: mutexName, createdNew: out isOwner))
            {
                if (isOwner)
                    watch();
            }
        }

        private static string Sanitize(string value)
        {
            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]))
                    chars[i] = '_';
            }
            return new string(chars);
        }
    }
}
