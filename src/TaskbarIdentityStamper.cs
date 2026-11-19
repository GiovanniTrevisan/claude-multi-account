using System;
using System.Collections.Generic;
using System.IO;
using ClaudeMultiAccount.Interop;

namespace ClaudeMultiAccount
{
    /// <summary>
    /// Applies a taskbar identity (AppUserModelID, relaunch metadata, icon) to a
    /// specific window via the officially supported per-window property store API
    /// (SHGetPropertyStoreForWindow). This never touches the target process or any
    /// of its files — see README.md for why that matters here.
    /// </summary>
    internal sealed class TaskbarIdentityStamper
    {
        private readonly AppConfig _config;
        private readonly IntPtr _bigIcon;
        private readonly IntPtr _smallIcon;
        private readonly HashSet<IntPtr> _windowsWithLoggedFailure = new HashSet<IntPtr>();

        public TaskbarIdentityStamper(AppConfig config)
        {
            _config = config;
            _bigIcon = LoadIcon(NativeMethods.GetSystemMetrics(NativeMethods.SM_CXICON), NativeMethods.GetSystemMetrics(NativeMethods.SM_CYICON));
            _smallIcon = LoadIcon(NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON), NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSMICON));
        }

        /// <summary>
        /// Returns true when the taskbar identity was applied; false means the
        /// window did not accept it yet and the caller should retry later.
        /// </summary>
        public bool ApplyIdentity(IntPtr windowHandle)
        {
            var wasApplied = ApplyAppUserModelProperties(windowHandle);
            ApplyIcon(windowHandle);
            return wasApplied;
        }

        /// <summary>
        /// Re-applies just the icon. Electron can redraw its own icon over ours
        /// after the first stamp, so the caller polls this on a timer instead of
        /// stamping once and trusting it to stick.
        /// </summary>
        public void ReapplyIcon(IntPtr windowHandle)
        {
            ApplyIcon(windowHandle);
        }

        private bool ApplyAppUserModelProperties(IntPtr windowHandle)
        {
            var propertyStore = ShellPropertyStore.ForWindow(windowHandle);
            if (propertyStore == null)
                return false;

            using (propertyStore)
            {
                try
                {
                    propertyStore.SetString(PropertyKeys.AppUserModelId, _config.AppUserModelId);
                    propertyStore.SetString(PropertyKeys.AppUserModelRelaunchCommand, "\"" + _config.ExecutablePath + "\" " + _config.LaunchArguments);
                    propertyStore.SetString(PropertyKeys.AppUserModelRelaunchDisplayNameResource, _config.ProductName);
                    if (File.Exists(_config.IconPath))
                        propertyStore.SetString(PropertyKeys.AppUserModelRelaunchIconResource, _config.IconPath + ",0");
                    propertyStore.Commit();
                    return true;
                }
                catch (Exception exception)
                {
                    // Not fatal: the window keeps grouping with the default Claude
                    // taskbar entry until the next retry succeeds.
                    // Retried every tick, so log once per window to keep the log readable.
                    if (_windowsWithLoggedFailure.Add(windowHandle))
                        DiagnosticLog.Write("Could not stamp taskbar identity: " + exception.Message);
                    return false;
                }
            }
        }

        private void ApplyIcon(IntPtr windowHandle)
        {
            if (_bigIcon != IntPtr.Zero)
                NativeMethods.SendMessage(windowHandle, NativeMethods.WM_SETICON, (IntPtr)NativeMethods.IconBig, _bigIcon);
            if (_smallIcon != IntPtr.Zero)
                NativeMethods.SendMessage(windowHandle, NativeMethods.WM_SETICON, (IntPtr)NativeMethods.IconSmall, _smallIcon);
        }

        private IntPtr LoadIcon(int width, int height)
        {
            if (!File.Exists(_config.IconPath))
                return IntPtr.Zero;
            return NativeMethods.LoadImage(IntPtr.Zero, _config.IconPath, NativeMethods.ImageIcon, width, height, NativeMethods.LoadFromFile);
        }
    }
}
