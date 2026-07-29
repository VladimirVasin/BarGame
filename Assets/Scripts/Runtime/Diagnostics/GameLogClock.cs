using System;
using System.Diagnostics;

namespace BarPromenade
{
    internal interface IGameLogClock
    {
        DateTimeOffset UtcNow { get; }
        long ElapsedMilliseconds { get; }
    }

    internal sealed class SystemGameLogClock : IGameLogClock
    {
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public long ElapsedMilliseconds => stopwatch.ElapsedMilliseconds;
    }
}
