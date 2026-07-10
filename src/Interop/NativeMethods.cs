using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeMultiAccount.Interop
{
    internal delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr lParam);

    internal static class NativeMethods
    {
        public const int WM_SETICON = 0x0080;
        public const int IconSmall = 0;
        public const int IconBig = 1;

        public const uint ImageIcon = 1;
        public const uint LoadFromFile = 0x00000010;

        public const int SM_CXICON = 11;
        public const int SM_CYICON = 12;
        public const int SM_CXSMICON = 49;
        public const int SM_CYSMICON = 50;

        public const ushort VT_LPWSTR = 31;

        public const uint GW_OWNER = 4;
        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_TOOLWINDOW = 0x00000080;

        public static readonly Guid IID_IPropertyStore = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int MessageBox(IntPtr ownerWindow, string text, string caption, uint type);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadImage(IntPtr moduleHandle, string name, uint type, int width, int height, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

        [DllImport("shell32.dll")]
        public static extern int SHGetPropertyStoreForWindow(
            IntPtr windowHandle,
            ref Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

        [DllImport("ole32.dll")]
        public static extern int PropVariantClear(ref PropVariant propVariant);
    }
}
