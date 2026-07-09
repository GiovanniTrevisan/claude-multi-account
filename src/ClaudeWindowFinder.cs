using System;
using System.Collections.Generic;
using System.Text;
using ClaudeMultiAccount.Interop;

namespace ClaudeMultiAccount
{
    internal struct ClaudeWindow
    {
        public IntPtr Handle;
        public uint ProcessId;
    }

    /// <summary>
    /// Finds Claude Desktop's visible top-level windows. Every Claude main window
    /// is titled exactly "Claude" regardless of which profile owns it, so windows
    /// must be further filtered by owning process (see <see cref="ClaudeProcessInspector"/>).
    /// </summary>
    internal static class ClaudeWindowFinder
    {
        private const string ClaudeWindowTitle = "Claude";
        private const int MaxTitleLength = 512;

        public static List<ClaudeWindow> FindVisibleWindows()
        {
            var windows = new List<ClaudeWindow>();
            NativeMethods.EnumWindows((handle, _) =>
            {
                if (!NativeMethods.IsWindowVisible(handle))
                    return true;

                var title = new StringBuilder(MaxTitleLength);
                NativeMethods.GetWindowText(handle, title, title.Capacity);
                if (title.ToString() != ClaudeWindowTitle)
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
