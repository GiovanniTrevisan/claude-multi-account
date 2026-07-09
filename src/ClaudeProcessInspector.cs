using System;
using System.Collections.Generic;
using System.Management;

namespace ClaudeMultiAccount
{
    /// <summary>
    /// Queries the command lines of running claude.exe processes via WMI, used to
    /// tell which processes and windows belong to our isolated profile as opposed
    /// to the default (personal) instance.
    /// </summary>
    internal static class ClaudeProcessInspector
    {
        private const string HelperProcessMarker = "--type=";

        public static Dictionary<uint, string> GetClaudeCommandLines()
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
                        var processId = (uint)process["ProcessId"];
                        commandLines[processId] = process["CommandLine"] as string;
                    }
                }
            }
            catch
            {
                // WMI can be unavailable in locked-down environments; callers treat
                // an empty result the same as "nothing found yet".
            }
            return commandLines;
        }

        /// <summary>
        /// Process IDs of any claude.exe process (main window or Chromium helper)
        /// whose command line references the given profile directory.
        /// </summary>
        public static HashSet<uint> GetProcessIdsUsingProfile(string userDataDirectory)
        {
            var matches = new HashSet<uint>();
            foreach (var entry in GetClaudeCommandLines())
            {
                if (ReferencesProfile(entry.Value, userDataDirectory))
                    matches.Add(entry.Key);
            }
            return matches;
        }

        /// <summary>
        /// Process IDs of the main (non-helper) claude.exe process(es) for the
        /// given profile. Used to detect when the instance has fully exited:
        /// Chromium helper processes (GPU, network, renderer, ...) can linger
        /// briefly after the main window closes, so they are excluded here.
        /// </summary>
        public static List<uint> GetMainProcessIdsUsingProfile(string userDataDirectory)
        {
            var matches = new List<uint>();
            foreach (var entry in GetClaudeCommandLines())
            {
                var commandLine = entry.Value;
                if (commandLine == null)
                    continue;

                bool isHelperProcess = commandLine.IndexOf(HelperProcessMarker, StringComparison.OrdinalIgnoreCase) >= 0;
                if (ReferencesProfile(commandLine, userDataDirectory) && !isHelperProcess)
                    matches.Add(entry.Key);
            }
            return matches;
        }

        private static bool ReferencesProfile(string commandLine, string userDataDirectory)
        {
            return commandLine != null &&
                   commandLine.IndexOf(userDataDirectory, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
