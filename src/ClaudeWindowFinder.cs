using System;
using System.Collections.Generic;
using ClaudeMultiAccount.Interop;

namespace ClaudeMultiAccount
{
    internal struct ClaudeWindow
    {
        public IntPtr Handle;
        public uint ProcessId;
    }

    /// <summary>
    /// Finds visible, non-owned, non-tool-window top-level windows. Every Claude main window
    /// is a top-level application window, regardless of which profile owns it. Windows are
    /// further filtered by owning process (see <see cref="ClaudeProcessInspector"/>).
    /// </summary>
    internal static class ClaudeWindowFinder
    {
        public static List<ClaudeWindow> FindVisibleWindows()
        {
            var windows = new List<ClaudeWindow>();
            NativeMethods.EnumWindows((handle, _) =>
            {
                if (!NativeMethods.IsWindowVisible(handle))
                    return true;

                if (NativeMethods.GetWindow(handle, NativeMethods.GW_OWNER) != IntPtr.Zero)
                    return true;

                if ((NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64() & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                    return true;

                uint processId;
                NativeMethods.GetWindowThreadProcessId(handle, out processId);
                windows.Add(new ClaudeWindow { Handle = handle, ProcessId = processId });
                return true;
            }, IntPtr.Zero);
            return windows;
        }
    }
}
