using System;

namespace ClaudeMultiAccount.Interop
{
    /// <summary>
    /// Well-known PKEY_AppUserModel_* property keys (propkey.h, FMTID_AppUserModel),
    /// used to give a window or shortcut a taskbar identity of its own.
    /// </summary>
    internal static class PropertyKeys
    {
        private static readonly Guid FmtIdAppUserModel = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

        public static readonly PropertyKey AppUserModelId =
            new PropertyKey(FmtIdAppUserModel, 5);
        public static readonly PropertyKey AppUserModelRelaunchCommand =
            new PropertyKey(FmtIdAppUserModel, 2);
        public static readonly PropertyKey AppUserModelRelaunchIconResource =
            new PropertyKey(FmtIdAppUserModel, 3);
        public static readonly PropertyKey AppUserModelRelaunchDisplayNameResource =
            new PropertyKey(FmtIdAppUserModel, 4);
    }
}
