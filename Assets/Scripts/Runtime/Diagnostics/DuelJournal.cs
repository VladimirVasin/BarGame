using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BarPromenade
{
    /// <summary>A bounded producer queue. Only its single worker formats records or touches files.</summary>
    internal sealed partial class DuelJournal : IDisposable
    {
        private enum Kind { Begin, Event, End, Flush }
        private const string ZeroStageEvents = "damage_stage_sample,body_sync_sample,blood_stage_sample,head_stage_sample,projectile_defeat_stage_sample";
        private static int liveWorkers;
        private struct Packet
        {
            internal Kind Kind;
            internal string Name;
            internal int Round, Frame, Actor, Target, Action, Request;
            internal long Sequence, Stamp, Tick, Dropped;
            internal double Seconds;
            internal string ForcedSampleReason;
            internal int SampleStride;
            internal GameLogField F0, F1, F2, F3, F4, F5, F6, F7;
            internal GameLogField[] Metadata;
            internal TaskCompletionSource<bool> Barrier;
        }

        private readonly object sync = new object();
        private readonly AutoResetEvent ready = new AutoResetEvent(false);
        private readonly Packet[] queue;
        private readonly int eventCapacity;
        private readonly string directory, folderSession;
        private readonly long maxRoundBytes, started = Stopwatch.GetTimestamp();
        private readonly DateTimeOffset utcStarted = DateTimeOffset.UtcNow;
        private readonly Task worker;
        private int head, tail, queued, frame, round, maximumQueued;
        private long tick, sequence, dropped, producerDropped, collectorTicks;
        private double seconds;
        private bool roundOpen, stopping;
        private volatile bool enabled = true;
        private string lastError;
        private string forcedSampleReason;
        private long forcedScopeGeneration;

        internal DuelJournal(string outputDirectory, string sessionId = null, int capacity = 2048,
            long maxRoundBytes = 20L * 1024L * 1024L)
        {
            SessionId = string.IsNullOrEmpty(sessionId) ? Guid.NewGuid().ToString("N") : Clip(sessionId, 128);
            folderSession = SafeSession(SessionId);
            eventCapacity = Math.Max(8, Math.Min(4096, capacity));
            this.maxRoundBytes = Math.Max(4096, Math.Min(20L * 1024L * 1024L, maxRoundBytes));
            try { directory = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch (Exception error) { enabled = false; lastError = error.GetType().Name; }
            // A blocked OS write may outlive Dispose. Repeated scene loads must
            // not accumulate more threads and queues behind the same stuck disk.
            if (Interlocked.Increment(ref liveWorkers) > 2)
            {
                Interlocked.Decrement(ref liveWorkers);
                enabled = false; lastError = "worker_limit";
                queue = Array.Empty<Packet>(); worker = Task.CompletedTask;
                ready.Dispose(); return;
            }
            queue = new Packet[eventCapacity + 8];
            try
            {
                worker = Task.Factory.StartNew(Run, CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            catch (Exception error)
            {
                Interlocked.Decrement(ref liveWorkers); ready.Dispose();
                enabled = false; lastError = "worker_start:" + error.GetType().Name;
                worker = Task.CompletedTask;
            }
        }

        internal bool Enabled => enabled;
        internal string SessionId { get; }
        internal int Round { get { lock (sync) return round; } }
        internal long Tick { get { lock (sync) return tick; } }
        internal double DuelSeconds { get { lock (sync) return seconds; } }
        /// <summary>Queue/input losses plus records omitted by the worker's file cap.</summary>
        internal long DroppedRecords => Interlocked.Read(ref dropped);
        internal long CollectorTicks => Interlocked.Read(ref collectorTicks);
        internal int MaximumQueued => Volatile.Read(ref maximumQueued);
        internal string LastError => lastError;
        internal Task Completion => worker;

        internal void SetClock(int frame, long tick, double duelSeconds)
        {
            if (!enabled) return;
            lock (sync) { this.frame = frame; this.tick = tick; seconds = duelSeconds; }
        }

        internal void BeginRound(int round, params GameLogField[] metadata)
        {
            if (!enabled) return;
            lock (sync)
            {
                if (stopping) return;
                if (roundOpen) EnqueueControl(NewPacket(Kind.End, "reset"));
                this.round = Math.Max(0, round);
                forcedSampleReason = null;
                forcedScopeGeneration++;
                var packet = NewPacket(Kind.Begin, "round_begin");
                // This is a cold boundary. Never retain a caller-owned mutable array.
                if (metadata != null)
                {
                    int count = Math.Min(32, metadata.Length);
                    packet.Metadata = new GameLogField[count];
                    for (int i = 0; i < count; i++) packet.Metadata[i] = BoundedMetadata(metadata[i]);
                    if (count < metadata.Length) packet.F0 = GameLog.Field("metadata_truncated", true);
                }
                roundOpen = EnqueueControl(packet);
            }
        }

        internal void EndRound(string reason)
        {
            if (!enabled) return;
            lock (sync)
            {
                if (!roundOpen || stopping) return;
                EnqueueControl(NewPacket(Kind.End, string.IsNullOrEmpty(reason) ? "ended" : reason));
                roundOpen = false;
            }
        }

        internal long Record(string eventName, int actor = 0, int target = 0, int action = 0, int request = 0,
            GameLogField f0 = default, GameLogField f1 = default, GameLogField f2 = default, GameLogField f3 = default,
            GameLogField f4 = default, GameLogField f5 = default, GameLogField f6 = default, GameLogField f7 = default)
        {
            if (!enabled) return 0;
            long before = Stopwatch.GetTimestamp();
            try
            {
                lock (sync)
                {
                    if (!roundOpen || stopping || !enabled) return 0;
                    if (string.IsNullOrEmpty(eventName) || eventName.Length > 96 ||
                        !Bounded(f0) || !Bounded(f1) || !Bounded(f2) || !Bounded(f3) ||
                        !Bounded(f4) || !Bounded(f5) || !Bounded(f6) || !Bounded(f7))
                    { producerDropped++; Interlocked.Increment(ref dropped); return 0; }
                    // These measured stage deltas have an explicit sparse contract
                    // in round_begin. Preserve unavailable values and causal events.
                    if (ZeroStageEvent(eventName) &&
                        (HasField(f0) || HasField(f1) || HasField(f2) || HasField(f3) ||
                         HasField(f4) || HasField(f5) || HasField(f6) || HasField(f7)) &&
                        NumericZero(f0) && NumericZero(f1) && NumericZero(f2) && NumericZero(f3) &&
                        NumericZero(f4) && NumericZero(f5) && NumericZero(f6) && NumericZero(f7)) return 0;
                    if (queued >= eventCapacity)
                    { producerDropped++; Interlocked.Increment(ref dropped); return 0; }
                    Packet packet = NewPacket(Kind.Event, eventName);
                    packet.Actor = actor; packet.Target = target; packet.Action = action; packet.Request = request;
                    if (SampleEvent(eventName)) packet.ForcedSampleReason = forcedSampleReason;
                    packet.F0 = f0; packet.F1 = f1; packet.F2 = f2; packet.F3 = f3;
                    packet.F4 = f4; packet.F5 = f5; packet.F6 = f6; packet.F7 = f7;
                    Enqueue(ref packet);
                    return packet.Sequence;
                }
            }
            finally { Interlocked.Add(ref collectorTicks, Stopwatch.GetTimestamp() - before); }
        }

        internal void Mark(string label)
        {
            if (!enabled) return;
            lock (sync)
            {
                if (!roundOpen || stopping) return;
                Packet packet = NewPacket(Kind.Event, "mark");
                packet.F0 = GameLog.Field("label", Clip(label, 512));
                EnqueueControl(packet);
            }
        }

        // Main-thread snapshot hooks borrow a dedicated sample quota. The scope
        // does not allocate or relabel events, and restores an outer scope safely.
        internal ForcedSnapshotScope ForceSnapshot(string reason)
        {
            lock (sync)
            {
                if (!roundOpen || stopping || !enabled) return default;
                string previous = forcedSampleReason;
                forcedSampleReason = Clip(string.IsNullOrEmpty(reason) ? "requested" : reason, 64);
                Record("snapshot_forced", f0: GameLog.Field("reason", forcedSampleReason));
                return new ForcedSnapshotScope(this, previous, forcedScopeGeneration);
            }
        }

        internal readonly struct ForcedSnapshotScope : IDisposable
        {
            private readonly DuelJournal owner;
            private readonly string previous;
            private readonly long generation;
            internal ForcedSnapshotScope(DuelJournal owner, string previous, long generation)
            { this.owner = owner; this.previous = previous; this.generation = generation; }
            public void Dispose()
            {
                if (owner == null) return;
                lock (owner.sync)
                    if (owner.roundOpen && owner.forcedScopeGeneration == generation) owner.forcedSampleReason = previous;
            }
        }

        internal Task FlushAsync()
        {
            lock (sync)
            {
                if (stopping || !enabled) return worker;
                var barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Packet packet = NewPacket(Kind.Flush, "flush"); packet.Barrier = barrier;
                if (!EnqueueControl(packet)) barrier.TrySetResult(false);
                return barrier.Task;
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (!stopping)
                {
                    if (roundOpen && enabled) EnqueueControl(NewPacket(Kind.End, "shutdown"));
                    roundOpen = false; stopping = true; enabled = false;
                    if (!worker.IsCompleted) try { ready.Set(); } catch (ObjectDisposedException) { }
                }
            }
            // Shutdown only; never called from a sample hook. A stuck OS write
            // cannot hold gameplay indefinitely or create an unbounded queue.
            try { worker.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        }

        private Packet NewPacket(Kind kind, string name) => new Packet
        {
            Kind = kind, Name = name, Round = round, Frame = frame, Tick = tick, Seconds = seconds,
            Stamp = Stopwatch.GetTimestamp(), Dropped = producerDropped
        };

        private void Enqueue(ref Packet packet)
        {
            bool wake = queued == 0;
            packet.Sequence = ++sequence;
            queue[tail] = packet; tail = (tail + 1) % queue.Length; queued++;
            maximumQueued = Math.Max(maximumQueued, queued);
            if (wake) Signal();
        }

        private bool EnqueueControl(Packet packet)
        {
            if (!enabled || stopping) return false;
            // Even boundaries/marks must never wait for disk backpressure.
            if (queued == queue.Length)
            { enabled = false; lastError = "control_queue_limit"; stopping = true; Signal(); return false; }
            Enqueue(ref packet); return true;
        }

        private void Signal()
        {
            try { ready.Set(); }
            catch (ObjectDisposedException) { enabled = false; lastError = "worker_closed"; }
        }

        private bool Dequeue(out Packet packet)
        {
            lock (sync)
            {
                if (queued == 0) { packet = default; return false; }
                packet = queue[head]; queue[head] = default; head = (head + 1) % queue.Length; queued--;
                Monitor.PulseAll(sync); return true;
            }
        }

        private static bool Bounded(GameLogField field) =>
            (field.Name == null || field.Name.Length <= 64) && (field.StringValue == null || field.StringValue.Length <= 512);

        private static bool ZeroStageEvent(string name) => name == "damage_stage_sample" || name == "body_sync_sample" ||
            name == "blood_stage_sample" || name == "head_stage_sample" || name == "projectile_defeat_stage_sample";

        private static bool HasField(GameLogField field) => !string.IsNullOrEmpty(field.Name);

        private static bool NumericZero(GameLogField field)
        {
            if (!HasField(field)) return true;
            return field.Type switch
            {
                GameLogFieldType.Int32 or GameLogFieldType.Int64 => field.IntegerValue == 0,
                GameLogFieldType.Single or GameLogFieldType.Double => field.NumberValue == 0d,
                _ => false
            };
        }

        private static GameLogField BoundedMetadata(GameLogField field)
        {
            string name = Clip(field.Name, 64);
            return field.Type switch
            {
                GameLogFieldType.String => GameLog.Field(name, Clip(field.StringValue, 512)),
                GameLogFieldType.Int32 => GameLog.Field(name, (int)field.IntegerValue),
                GameLogFieldType.Int64 => GameLog.Field(name, field.IntegerValue),
                GameLogFieldType.Single => GameLog.Field(name, (float)field.NumberValue),
                GameLogFieldType.Double => GameLog.Field(name, field.NumberValue),
                _ => GameLog.Field(name, field.BooleanValue)
            };
        }

        private static string SafeSession(string value)
        {
            var chars = new char[Math.Min(64, value.Length)];
            for (int i = 0; i < chars.Length; i++)
            {
                char c = value[i];
                chars[i] = c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' ? c : '-';
            }
            return new string(chars);
        }
    }
}
