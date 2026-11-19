using System;
using System.Collections.Generic;
using System.Management;

namespace ClaudeMultiAccount
{
    /// <summary>
    /// The claude.exe processes that belong to one isolated profile, captured
    /// from a single WMI query.
    /// </summary>
    internal sealed class ProfileProcessSnapshot
    {
        /// <summary>Main window process plus every Chromium helper.</summary>
        public HashSet<uint> AllProcessIds { get; private set; }

        /// <summary>
        /// Only the main (non-helper) process(es). Used to detect when the
        /// instance has fully exited: helpers (GPU, network, renderer, ...) can
        /// linger briefly after the main window closes.
        /// </summary>
        public HashSet<uint> MainProcessIds { get; private set; }

        public ProfileProcessSnapshot()
        {
            AllProcessIds = new HashSet<uint>();
            MainProcessIds = new HashSet<uint>();
        }
    }

    /// <summary>
    /// Queries the command lines of running claude.exe processes via WMI, used to
    /// tell which processes and windows belong to our isolated profile as opposed
    /// to the default (personal) instance or another isolated profile.
    /// </summary>
    internal static class ClaudeProcessInspector
    {
        private const string HelperProcessMarker = "--type=";
        private const string UserDataDirectoryFlag = "--user-data-dir=";

        public static ProfileProcessSnapshot CaptureProfileProcesses(string userDataDirectory)
        {
            var snapshot = new ProfileProcessSnapshot();
            foreach (var entry in GetClaudeCommandLines())
            {
                var commandLine = entry.Value;
                if (!UsesProfile(commandLine, userDataDirectory))
                    continue;

                snapshot.AllProcessIds.Add(entry.Key);
                if (commandLine.IndexOf(HelperProcessMarker, StringComparison.OrdinalIgnoreCase) < 0)
                    snapshot.MainProcessIds.Add(entry.Key);
            }
            return snapshot;
        }

        private static Dictionary<uint, string> GetClaudeCommandLines()
        {
            var commandLines = new Dictionary<uint, string>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'claude.exe'"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject process in results)
                    {
                        using (process)
                        {
                            var processId = (uint)process["ProcessId"];
                            commandLines[processId] = process["CommandLine"] as string;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                // WMI can be unavailable in locked-down environments; callers treat
                // an empty result the same as "nothing found yet".
                DiagnosticLog.Write("WMI process query failed: " + exception.Message);
            }
            return commandLines;
        }

        /// <summary>
        /// True when the command line's --user-data-dir is exactly this profile
        /// directory. A plain substring check is not enough: profile "Claude-Work"
        /// would otherwise also claim the processes of "Claude-Work2".
        /// The flag shows up in three shapes: --user-data-dir="C:\x" (our launch),
        /// --user-data-dir=C:\x (some helpers) and "--user-data-dir=C:\x"
        /// (Claude relaunching itself).
        /// </summary>
        internal static bool UsesProfile(string commandLine, string userDataDirectory)
        {
            var actualDirectory = ExtractUserDataDirectory(commandLine);
            if (actualDirectory == null)
                return false;

            return string.Equals(
                actualDirectory.TrimEnd('\\'),
                userDataDirectory.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractUserDataDirectory(string commandLine)
        {
            if (commandLine == null)
                return null;

            var flagIndex = commandLine.IndexOf(UserDataDirectoryFlag, StringComparison.OrdinalIgnoreCase);
            if (flagIndex < 0)
                return null;

            var valueStart = flagIndex + UserDataDirectoryFlag.Length;

            // When Claude relaunches itself (e.g. after sign-in) Electron quotes the
            // whole argument instead: "--user-data-dir=C:\x". The value then runs
            // up to the closing quote and may contain spaces.
            var isWholeArgumentQuoted = flagIndex > 0 && commandLine[flagIndex - 1] == '"';
            if (isWholeArgumentQuoted)
            {
                var closingQuote = commandLine.IndexOf('"', valueStart);
                return closingQuote < 0 ? null : commandLine.Substring(valueStart, closingQuote - valueStart);
            }

            if (valueStart < commandLine.Length && commandLine[valueStart] == '"')
            {
                var closingQuote = commandLine.IndexOf('"', valueStart + 1);
                return closingQuote < 0 ? null : commandLine.Substring(valueStart + 1, closingQuote - valueStart - 1);
            }

            var valueEnd = commandLine.IndexOf(' ', valueStart);
            return valueEnd < 0 ? commandLine.Substring(valueStart) : commandLine.Substring(valueStart, valueEnd - valueStart);
        }
    }
}
