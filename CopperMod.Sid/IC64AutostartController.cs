using System;

namespace CopperMod.Sid
{
    /// <summary>
    /// Allows tools to schedule automated C64 keyboard input for cartridge playback.
    /// </summary>
    public interface IC64AutostartController
    {
        /// <summary>
        /// Schedules a supported C64 key press after a render-time delay.
        /// </summary>
        void ScheduleAutostartKey(string key, TimeSpan delay, TimeSpan hold);
    }
}
