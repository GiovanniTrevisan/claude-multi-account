using System;
using System.Collections.Generic;
using System.Threading;

namespace ClaudeMultiAccount
{
    /// <summary>
    /// Watches the isolated Claude instance for as long as it runs: stamps newly
    /// created windows with our taskbar identity, keeps reapplying the icon, and
    /// returns once the instance has fully exited.
    /// </summary>
    internal sealed class InstanceIdentityWatcher
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(45);
        private const int ConsecutiveEmptyChecksBeforeExit = 3;

        private readonly AppConfig _config;
        private readonly TaskbarIdentityStamper _stamper;

        public InstanceIdentityWatcher(AppConfig config, TaskbarIdentityStamper stamper)
        {
            _config = config;
            _stamper = stamper;
        }

        public void WatchUntilInstanceExits()
        {
            var stampedWindows = new HashSet<IntPtr>();
            var consecutiveEmptyChecks = 0;
            var startupDeadline = DateTime.UtcNow.Add(StartupTimeout);
            var instanceHasAppeared = false;

            while (true)
            {
                var mainProcessIds = ClaudeProcessInspector.GetMainProcessIdsUsingProfile(_config.UserDataDirectory);
                if (mainProcessIds.Count > 0)
                    instanceHasAppeared = true;

                if (instanceHasAppeared && mainProcessIds.Count == 0)
                {
                    consecutiveEmptyChecks++;
                    if (consecutiveEmptyChecks >= ConsecutiveEmptyChecksBeforeExit)
                        return;
                }
                else
                {
                    consecutiveEmptyChecks = 0;
                }

                // Cold starts can take a while; only give up early if the instance
                // never showed up at all within the timeout.
                if (!instanceHasAppeared && DateTime.UtcNow > startupDeadline)
                    return;

                StampOwnedWindows(stampedWindows);
                Thread.Sleep(PollInterval);
            }
        }

        private void StampOwnedWindows(HashSet<IntPtr> stampedWindows)
        {
            var ownedProcessIds = ClaudeProcessInspector.GetProcessIdsUsingProfile(_config.UserDataDirectory);

            foreach (var window in ClaudeWindowFinder.FindVisibleWindows())
            {
                if (!ownedProcessIds.Contains(window.ProcessId))
                    continue;

                if (stampedWindows.Add(window.Handle))
                    _stamper.ApplyIdentity(window.Handle);
                else
                    _stamper.ReapplyIcon(window.Handle);
            }
        }
    }
}
