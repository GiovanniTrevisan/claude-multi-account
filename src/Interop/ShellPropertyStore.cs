using System;
using System.Runtime.InteropServices;

namespace ClaudeMultiAccount.Interop
{
    /// <summary>
    /// Thin wrapper around a COM <see cref="IPropertyStore"/> that hides PROPVARIANT
    /// marshalling behind a small string-oriented API.
    /// </summary>
    internal sealed class ShellPropertyStore : IDisposable
    {
        private readonly IPropertyStore _store;
        private readonly bool _ownsUnderlyingComObject;

        private ShellPropertyStore(IPropertyStore store, bool ownsUnderlyingComObject)
        {
            _store = store;
            _ownsUnderlyingComObject = ownsUnderlyingComObject;
        }

        /// <summary>
        /// Opens the property store of a top-level window belonging to any process.
        /// Returns null if the window does not expose one.
        /// </summary>
        public static ShellPropertyStore ForWindow(IntPtr windowHandle)
        {
            var interfaceId = NativeMethods.IID_IPropertyStore;
            IPropertyStore store;
            int result = NativeMethods.SHGetPropertyStoreForWindow(windowHandle, ref interfaceId, out store);
            return result == 0 && store != null
                ? new ShellPropertyStore(store, ownsUnderlyingComObject: true)
                : null;
        }

        /// <summary>
        /// Views an in-process shell link COM object as a property store. The
        /// caller keeps owning/releasing the shell link itself, so Dispose here
        /// is a no-op — releasing this wrapper's RCW would double-release theirs,
        /// since both are the same underlying COM object.
        /// </summary>
        public static ShellPropertyStore ForShellLink(IShellLinkW shellLink)
        {
            return new ShellPropertyStore((IPropertyStore)shellLink, ownsUnderlyingComObject: false);
        }

        public void SetString(PropertyKey key, string value)
        {
            var propVariant = new PropVariant
            {
                VariantType = NativeMethods.VT_LPWSTR,
                Pointer = Marshal.StringToCoTaskMemUni(value)
            };
            try
            {
                _store.SetValue(ref key, ref propVariant);
            }
            finally
            {
                NativeMethods.PropVariantClear(ref propVariant);
            }
        }

        public void Commit()
        {
            _store.Commit();
        }

        public void Dispose()
        {
            if (_ownsUnderlyingComObject)
                Marshal.ReleaseComObject(_store);
        }
    }
}
