using System;

namespace DotCraft.Editor.ToolGateway
{
    internal static class ToolGatewayRelativeTime
    {
        /// <summary>"now" / "12s" / "3m" / "2h".</summary>
        public static string Since(DateTime utcTimestamp, DateTime utcNow)
        {
            var elapsed = Clamp(utcNow - utcTimestamp);
            return elapsed.TotalSeconds < 5 ? "now" : Format(elapsed);
        }

        /// <summary>Connection age: "45s" / "12m" / "2h".</summary>
        public static string DurationSince(DateTime utcTimestamp, DateTime utcNow) =>
            Format(Clamp(utcNow - utcTimestamp));

        private static string Format(TimeSpan elapsed)
        {
            if (elapsed.TotalSeconds < 60)
                return $"{(int)elapsed.TotalSeconds}s";
            if (elapsed.TotalMinutes < 60)
                return $"{(int)elapsed.TotalMinutes}m";
            return $"{(int)elapsed.TotalHours}h";
        }

        /// <summary>A gateway can report a timestamp slightly in the future; never render negatives.</summary>
        private static TimeSpan Clamp(TimeSpan elapsed) =>
            elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }
}
