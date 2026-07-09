using System;
using ClaudeMultiAccount.Interop;

namespace ClaudeMultiAccount
{
    internal static class NativeMessageBox
    {
        private const uint IconInformation = 0x40;

        public static void Show(string text, string caption)
        {
            NativeMethods.MessageBox(IntPtr.Zero, text, caption, IconInformation);
        }
    }
}
