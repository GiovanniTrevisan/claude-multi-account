using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using ClaudeMultiAccount.Interop;
using Microsoft.Win32;

namespace ClaudeMultiAccount
{
    /// <summary>
    /// Creates Start Menu / Desktop shortcuts carrying our own icon and
    /// AppUserModelID, and registers the AppUserModelID under
    /// HKCU\...\AppUserModelId so Windows can resolve a display name and icon for
    /// toast notifications even before the app has ever been launched.
    /// </summary>
    internal static class ShortcutInstaller
    {
        public static void InstallAll(AppConfig config)
        {
            RegisterAppUserModelId(config);
            CreateShortcut(config.StartMenuShortcutPath, config);
            CreateShortcut(config.DesktopShortcutPath, config);
        }

        public static void RegisterAppUserModelId(AppConfig config)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Classes\AppUserModelId\" + config.AppUserModelId))
                {
                    key.SetValue("DisplayName", config.ProductName, RegistryValueKind.String);
                    if (File.Exists(config.IconPath))
                        key.SetValue("IconUri", config.IconPath, RegistryValueKind.String);
                }
            }
            catch
            {
                // Not critical: only affects notifications shown before first launch.
            }
        }

        private static void CreateShortcut(string shortcutPath, AppConfig config)
        {
            IShellLinkW shellLink = null;
            try
            {
                shellLink = (IShellLinkW)new ShellLinkCoClass();
                shellLink.SetPath(config.ExecutablePath);
                shellLink.SetWorkingDirectory(config.InstallDirectory);
                shellLink.SetDescription(config.ProductName);
                if (File.Exists(config.IconPath))
                    shellLink.SetIconLocation(config.IconPath, 0);

                using (var propertyStore = ShellPropertyStore.ForShellLink(shellLink))
                {
                    propertyStore.SetString(PropertyKeys.AppUserModelId, config.AppUserModelId);
                    propertyStore.Commit();
                }

                ((IPersistFile)shellLink).Save(shortcutPath, true);
            }
            catch (Exception exception)
            {
                NativeMessageBox.Show(
                    "Could not create shortcut:\n" + shortcutPath + "\n" + exception.Message,
                    config.ProductName);
            }
            finally
            {
                if (shellLink != null)
                    Marshal.ReleaseComObject(shellLink);
            }
        }
    }
}
