using Serilog;
using System;
using System.Runtime.InteropServices;

namespace RadioApp.Services
{
    /// <summary>
    /// Prevents Windows from entering system sleep while radio playback is active.
    /// The display is allowed to turn off — only the system stays awake.
    /// Uses the Win32 SetThreadExecutionState API.
    /// </summary>
    public class SleepPreventionService
    {
        [FlagsAttribute]
        private enum ExecutionState : uint
        {
            ES_CONTINUOUS = 0x80000000,
            ES_SYSTEM_REQUIRED = 0x00000001
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

        private bool _isSleepPrevented;

        /// <summary>
        /// Tells Windows to keep the system awake until <see cref="AllowSleep"/> is called.
        /// Safe to call multiple times in a row.
        /// </summary>
        public void PreventSleep()
        {
            if (_isSleepPrevented)
            {
                return;
            }

            ExecutionState previous = SetThreadExecutionState(
                ExecutionState.ES_CONTINUOUS | ExecutionState.ES_SYSTEM_REQUIRED
            );

            if (previous == 0)
            {
                Log.Warning(
                    "SetThreadExecutionState failed when trying to prevent sleep. Win32 error: {Error}",
                    Marshal.GetLastWin32Error()
                );

                return;
            }

            _isSleepPrevented = true;

            Log.Information("System sleep prevention enabled.");
        }

        /// <summary>
        /// Restores the default Windows power policy so the system can sleep again.
        /// Safe to call when sleep prevention is not currently active.
        /// </summary>
        public void AllowSleep()
        {
            if (!_isSleepPrevented)
            {
                return;
            }

            ExecutionState previous = SetThreadExecutionState(ExecutionState.ES_CONTINUOUS);

            if (previous == 0)
            {
                Log.Warning(
                    "SetThreadExecutionState failed when trying to allow sleep. Win32 error: {Error}",
                    Marshal.GetLastWin32Error()
                );

                return;
            }

            _isSleepPrevented = false;

            Log.Information("System sleep prevention disabled.");
        }
    }
}