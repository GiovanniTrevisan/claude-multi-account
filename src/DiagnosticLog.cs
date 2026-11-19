using System;
using System.IO;

namespace ClaudeMultiAccount
{
    /// <summary>
    /// Tiny append-only log file inside the isolated profile directory, so
    /// failures that are deliberately non-fatal (and therefore invisible, since
    /// this is a windowless app) can still be diagnosed later.
    /// </summary>
    internal static class DiagnosticLog
    {
        private const long MaxLogSizeBytes = 256 * 1024;

        private static string _logFilePath;

        public static void Initialize(string logFilePath)
        {
            _logFilePath = logFilePath;
        }

        public static void Write(string message)
        {
            if (_logFilePath == null)
                return;

            try
            {
                // Start over instead of growing forever; only recent entries matter.
                var logFile = new FileInfo(_logFilePath);
                if (logFile.Exists && logFile.Length > MaxLogSizeBytes)
                    logFile.Delete();

                File.AppendAllText(_logFilePath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch (IOException)
            {
                // Logging must never take the launcher down (e.g. file locked by
                // a second launcher instance writing at the same moment).
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above: the log is a diagnostic aid, not a requirement.
            }
        }
    }
}
