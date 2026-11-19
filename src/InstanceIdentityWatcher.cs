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
        // Claude briefly has no main process while it relaunches itself (e.g.
        // right after sign-in); this grace period keeps the watcher alive across it.
        private const int ConsecutiveEmptyChecksBeforeExit = 5;

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
                // One WMI query per tick: it is the expensive part of this loop,
                // which runs for as long as the isolated instance stays open.
                var snapshot = ClaudeProcessInspector.CaptureProfileProcesses(_config.UserDataDirectory);
                if (snapshot.MainProcessIds.Count > 0)
                    instanceHasAppeared = true;

                if (instanceHasAppeared && snapshot.MainProcessIds.Count == 0)
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

                StampOwnedWindows(snapshot.AllProcessIds, stampedWindows);
                Thread.Sleep(PollInterval);
            }
        }

        private void StampOwnedWindows(HashSet<uint> ownedProcessIds, HashSet<IntPtr> stampedWindows)
        {
            var currentlyVisibleWindows = new HashSet<IntPtr>();

            foreach (var window in ClaudeWindowFinder.FindVisibleWindows())
            {
                if (!ownedProcessIds.Contains(window.ProcessId))
                    continue;

                currentlyVisibleWindows.Add(window.Handle);
                if (stampedWindows.Contains(window.Handle))
                {
                    _stamper.ReapplyIcon(window.Handle);
                    continue;
                }

                // Only remember the window once stamping succeeded, so a window
                // that was not ready yet gets retried on the next tick.
                if (_stamper.ApplyIdentity(window.Handle))
                    stampedWindows.Add(window.Handle);
            }

            // Forget closed windows: Windows reuses handle values, and a stale
            // entry could make a brand-new window skip its identity stamp.
            stampedWindows.IntersectWith(currentlyVisibleWindows);
        }
    }
}
