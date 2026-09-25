using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace BarPromenade
{
    internal sealed partial class DuelJournal
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private readonly Dictionary<string, long> events = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> rejections = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, (double seconds, long sequence)> longest = new Dictionary<string, (double, long)>(StringComparer.Ordinal);
        private readonly List<string> milestones = new List<string>(24);
        private readonly List<GameLogField> fields = new List<GameLogField>(48);
        private readonly (int frame, double milliseconds)[] slowest = new (int, double)[10];
        private struct RejectionSeries
        {
            internal int Actor;
            internal string Reason;
            internal long Count, FirstSequence, LastSequence, FirstStamp, LastStamp;
            internal double FirstSeconds, LastSeconds;
        }
        private readonly RejectionSeries[] rejectionSeries = new RejectionSeries[16];
        private readonly List<string> finishedSeries = new List<string>(16);
        private StreamWriter output;
        private FileStream activeLock;
        private string roundDirectory, roundIoError;
        private Packet roundHeader, lastPacket;
        private long bytes, written, fileDropped, oversized, seenProducerDrops, summarizedRejections;
        private bool fileLimited, marked;

        private void Run()
        {
            try
            {
                long lastFlush = Stopwatch.GetTimestamp();
                while (true)
                {
                    while (Dequeue(out lastPacket))
                    {
                        try
                        {
                            switch (lastPacket.Kind)
                            {
                                case Kind.Begin: OpenRound(lastPacket); break;
                                case Kind.Event: ProcessEvent(ref lastPacket); break;
                                case Kind.End: CloseRound(lastPacket); break;
                                case Kind.Flush: output?.Flush(); lastPacket.Barrier.TrySetResult(true); break;
                            }
                        }
                        catch (Exception error)
                        {
                            roundIoError = lastError = "io_error:" + error.GetType().Name;
                            enabled = false;
                            lock (sync) { stopping = true; roundOpen = false; }
                            lastPacket.Barrier?.TrySetResult(false);
                            break;
                        }
                    }
                    lock (sync) { if (stopping || !enabled) break; }
                    if (Stopwatch.GetTimestamp() - lastFlush >= Stopwatch.Frequency / 2)
                    { output?.Flush(); lastFlush = Stopwatch.GetTimestamp(); }
                    ready.WaitOne(100);
                }
            }
            catch (Exception error) { roundIoError = lastError = "io_error:" + error.GetType().Name; enabled = false; }
            finally
            {
                // A normal shutdown drains the finite queue. A failed writer also
                // resolves barriers, so callers never wait for an abandoned queue.
                while (Dequeue(out lastPacket)) { lastPacket.Barrier?.TrySetResult(false); Interlocked.Increment(ref dropped); }
                if (roundDirectory != null)
                {
                    if (lastError != null) roundIoError = roundIoError ?? lastError;
                    lock (sync) { lastPacket = NewPacket(Kind.End, lastError ?? "shutdown"); lastPacket.Sequence = ++sequence; }
                    lastPacket.Round = roundHeader.Round;
                    try { CloseRound(lastPacket); } catch { ReleaseFiles(); }
                }
                ready.Dispose();
                Interlocked.Decrement(ref liveWorkers);
            }
        }

        private long SummaryBudget => Math.Min(16384L, maxRoundBytes / 4);
        private long FooterBudget => Math.Min(4096L, maxRoundBytes / 2);

        // Packets carry eight inline fields; pass by reference through IO/error
        // boundaries instead of copying the large struct across worker frames.
        private void OpenRound(in Packet packet)
        {
            if (roundDirectory != null) { Packet end = packet; end.Name = "reset"; CloseRound(end); }
            roundHeader = packet; lastPacket = packet;
            events.Clear(); rejections.Clear(); longest.Clear(); milestones.Clear(); Array.Clear(slowest, 0, slowest.Length);
            Array.Clear(rejectionSeries, 0, rejectionSeries.Length); finishedSeries.Clear();
            bytes = written = fileDropped = oversized = summarizedRejections = 0; seenProducerDrops = packet.Dropped;
            fileLimited = marked = false; roundIoError = null;
            roundDirectory = ReserveRound(directory, folderSession, packet.Round, maxRoundBytes, out activeLock);
            output = new StreamWriter(new FileStream(Path.Combine(roundDirectory, "duel.ndjson"),
                FileMode.CreateNew, FileAccess.Write, FileShare.Read), Utf8) { NewLine = "\n", AutoFlush = false };
            if (!WritePacket(packet, false)) throw new IOException("Round header exceeds the configured file budget.");
        }

        private void ProcessEvent(ref Packet packet)
        {
            if (roundDirectory == null || packet.Round != roundHeader.Round) return;
            Count(events, packet.Name);
            RememberInterval(packet);
            bool rejected = packet.Name.IndexOf("reject", StringComparison.OrdinalIgnoreCase) >= 0;
            string reason = null;
            for (int i = 0; i < 8; i++)
            {
                GameLogField field = FieldAt(packet, i);
                if (field.Name == "reason") reason = field.Type == GameLogFieldType.String ? field.StringValue ?? "unspecified" :
                    field.IntegerValue.ToString(CultureInfo.InvariantCulture);
                if (field.Name == "result") rejected |= field.StringValue == "rejected" || field.StringValue == "blocked" ||
                    field.StringValue == "failed" || (field.Type == GameLogFieldType.Boolean && !field.BooleanValue);
                if (field.Name == "frame_ms") RememberFrame(packet.Frame, Numeric(field));
            }
            if (rejected) Count(rejections, reason ?? "unspecified");
            if (rejected && packet.Name == "command_result" && SummarizeRejection(ref packet, reason ?? "unspecified")) return;
            if (!rejected && packet.Name == "command_result")
                for (int i = 0; i < rejectionSeries.Length; i++)
                    if (rejectionSeries[i].Count > 0 && rejectionSeries[i].Actor == packet.Actor)
                    { AttachSeries(ref packet, rejectionSeries[i]); rejectionSeries[i] = default; break; }
            if (packet.Name == "mark")
            {
                marked = true;
                using (File.Create(Path.Combine(roundDirectory, ".marked"))) { }
            }
            if (!SampleEvent(packet.Name))
            {
                if (milestones.Count == 24) milestones.RemoveAt(0);
                milestones.Add("tick " + packet.Tick + " #" + packet.Sequence + " " + packet.Name +
                    " actor=" + packet.Actor + " action=" + packet.Action + " request=" + packet.Request +
                    (reason == null ? string.Empty : " reason=" + reason));
            }
            if (fileLimited || roundIoError != null)
            { fileDropped++; Interlocked.Increment(ref dropped); return; }
            if (WritePacket(packet, false)) return;
            fileDropped++; Interlocked.Increment(ref dropped);
            fileLimited = true;
            Packet marker = packet; marker.Name = "file_limit"; marker.Metadata = null;
            marker.F0 = GameLog.Field("omitted_event", packet.Name);
            marker.F1 = GameLog.Field("max_round_bytes", maxRoundBytes);
            marker.F2 = marker.F3 = marker.F4 = marker.F5 = marker.F6 = marker.F7 = default;
            WritePacket(marker, true);
            Prune(directory);
        }

        private bool WritePacket(in Packet packet, bool terminal)
        {
            fields.Clear();
            fields.Add(GameLog.Field("round", roundHeader.Round));
            fields.Add(GameLog.Field("frame", packet.Frame)); fields.Add(GameLog.Field("tick", packet.Tick));
            fields.Add(GameLog.Field("duel_seconds", packet.Seconds)); fields.Add(GameLog.Field("actor", packet.Actor));
            fields.Add(GameLog.Field("target", packet.Target)); fields.Add(GameLog.Field("action", packet.Action));
            fields.Add(GameLog.Field("request", packet.Request));
            if (packet.Dropped > seenProducerDrops)
                fields.Add(GameLog.Field("queue_dropped_before", packet.Dropped - seenProducerDrops));
            if (packet.Metadata != null)
                foreach (GameLogField field in packet.Metadata) AddField(field);
            for (int i = 0; i < 8; i++) AddField(FieldAt(packet, i));
            double elapsed = Math.Max(0d, (packet.Stamp - started) / (double)Stopwatch.Frequency);
            string line = GameLogFormatter.Format(new GameLogEvent(utcStarted.AddSeconds(elapsed),
                (long)(elapsed * 1000d), packet.Sequence, packet.Name == "file_limit" ? GameLogLevel.Warning : GameLogLevel.Info,
                "duel", packet.Name, SessionId, "CombatTest", null, fields.ToArray()));
            int length = Utf8.GetByteCount(line) + 1;
            long limit = maxRoundBytes - SummaryBudget - 64 - (terminal ? 0 : FooterBudget);
            if (length > 16384 || bytes + length > limit)
            { if (length > 16384) oversized++; return false; }
            output.WriteLine(line); bytes += length; written++; seenProducerDrops = packet.Dropped;
            return true;
        }

        private void AddField(GameLogField field)
        {
            if (string.IsNullOrEmpty(field.Name)) return;
            // Correlation fields are owned by the envelope; payload cannot spoof them.
            foreach (GameLogField existing in fields) if (existing.Name == field.Name) return;
            fields.Add(field);
        }

        private void CloseRound(in Packet packet)
        {
            if (roundDirectory == null) return;
            string folder = roundDirectory;
            long losses = Math.Max(0, packet.Dropped - roundHeader.Dropped) + fileDropped;
            bool complete = roundIoError == null;
            try
            {
                if (output != null && roundIoError == null)
                {
                    Packet footer = packet; footer.Name = "round_end";
                    footer.Metadata = new[] { GameLog.Field("summarized_rejections", summarizedRejections) };
                    footer.F0 = GameLog.Field("reason", Clip(packet.Name, 128));
                    footer.F1 = GameLog.Field("dropped_records", losses);
                    footer.F2 = GameLog.Field("file_limit", fileLimited);
                    footer.F3 = GameLog.Field("records_written", written);
                    footer.F4 = GameLog.Field("maximum_queued", MaximumQueued);
                    footer.F5 = GameLog.Field("collector_ticks", CollectorTicks);
                    footer.F6 = GameLog.Field("collector_frequency", Stopwatch.Frequency);
                    footer.F7 = GameLog.Field("io_error", roundIoError);
                    complete = WritePacket(footer, true);
                    output.Flush();
                }
            }
            catch (Exception error) { complete = false; roundIoError = lastError = "io_error:" + error.GetType().Name; }
            try
            {
                string summary = BuildSummary(packet, losses, complete);
                File.WriteAllText(Path.Combine(folder, "summary.txt"), LimitUtf8(summary, (int)SummaryBudget), Utf8);
                if (complete) using (File.Create(Path.Combine(folder, ".complete"))) { }
            }
            catch (Exception error) { lastError = "summary_io_error:" + error.GetType().Name; }
            finally { ReleaseFiles(); }
            Prune(directory);
        }

        private string BuildSummary(in Packet packet, long losses, bool complete)
        {
            var text = new StringBuilder(2048);
            text.AppendLine("Duel journal " + SessionId + ", round " + roundHeader.Round);
            text.AppendLine("Status: " + (complete ? "completed" : "incomplete") + "; reason: " + Clip(packet.Name, 128));
            text.AppendLine("Ticks: " + roundHeader.Tick + ".." + packet.Tick + "; records written: " + written);
            text.AppendLine("Losses: " + losses + "; file_limit: " + fileLimited + "; oversized: " + oversized +
                "; io_error: " + (roundIoError ?? "none") + "; marked: " + marked);
            text.AppendLine("Repeated rejection records summarized (not lost): " + summarizedRejections);
            text.AppendLine("Collector stopwatch ticks: " + CollectorTicks + "/" + Stopwatch.Frequency +
                "; maximum queued: " + MaximumQueued);
            text.AppendLine("Event counts:"); foreach (var item in events) text.AppendLine("  " + item.Key + ": " + item.Value);
            text.AppendLine("Rejected reasons:"); foreach (var item in rejections) text.AppendLine("  " + item.Key + ": " + item.Value);
            text.AppendLine("Longest observed intervals (duel seconds; open_* may be unfinished):");
            foreach (var item in longest)
                text.AppendLine("  " + item.Key + ": " + item.Value.seconds.ToString("F3", CultureInfo.InvariantCulture) + " at #" + item.Value.sequence);
            text.AppendLine("Rejection series (count includes its first full record):");
            foreach (string item in finishedSeries) text.AppendLine("  " + item);
            foreach (RejectionSeries item in rejectionSeries) if (item.Count > 1) text.AppendLine("  " + DescribeSeries(item));
            text.AppendLine("Slowest frames (milliseconds):");
            foreach (var item in slowest) if (item.milliseconds > 0)
                text.AppendLine("  " + item.frame + ": " + item.milliseconds.ToString("F3", CultureInfo.InvariantCulture));
            text.AppendLine("Last major events:"); foreach (string milestone in milestones) text.AppendLine("  " + milestone);
            return text.ToString();
        }

        private void ReleaseFiles()
        {
            try { output?.Dispose(); } catch { }
            output = null;
            try { activeLock?.Dispose(); } catch { }
            activeLock = null;
            if (roundDirectory != null) try { File.Delete(Path.Combine(roundDirectory, ".active")); } catch { }
            roundDirectory = null;
        }

        private static void Count(Dictionary<string, long> counts, string name)
        {
            if (!counts.ContainsKey(name) && counts.Count >= 64) name = "[other]";
            counts.TryGetValue(name, out long count); counts[name] = count + 1;
        }

        private void RememberInterval(in Packet packet)
        {
            double duration = -1;
            string label = null;
            for (int i = 0; i < 8; i++)
            {
                GameLogField field = FieldAt(packet, i);
                if (field.Name == "duration_seconds") duration = Numeric(field);
                if (field.Name == "from" || field.Name == "label") label = field.StringValue;
            }
            if (duration < 0 || double.IsNaN(duration) || double.IsInfinity(duration)) return;
            string key = packet.Actor + "/" + packet.Name + "/" + (label ?? "");
            if (!longest.TryGetValue(key, out var previous) && longest.Count >= 64) return;
            if (duration > previous.seconds) longest[key] = (duration, packet.Sequence);
        }

        private static bool SampleEvent(string name) => name == "frame" || name == "frame_work" || name == "snapshot" ||
            name == "state" || name == "pose" || name == "vectors" || name == "presentation" || name == "balance" ||
            name == "impulse_movement" ||
            name.EndsWith("_snapshot", StringComparison.Ordinal) || name.EndsWith("_sample", StringComparison.Ordinal);

        private bool SummarizeRejection(ref Packet packet, string reason)
        {
            int slot = -1, empty = -1;
            for (int i = 0; i < rejectionSeries.Length; i++)
            {
                if (rejectionSeries[i].Count == 0) { if (empty < 0) empty = i; }
                else if (rejectionSeries[i].Actor == packet.Actor) { slot = i; break; }
            }
            if (slot < 0) slot = empty;
            if (slot < 0) return false; // Bounded table: additional actors retain full records.
            RejectionSeries series = rejectionSeries[slot];
            if (series.Count > 0 && series.Reason == reason && packet.Stamp - series.FirstStamp < Stopwatch.Frequency)
            {
                series.Count++; series.LastSequence = packet.Sequence; series.LastStamp = packet.Stamp;
                series.LastSeconds = packet.Seconds; rejectionSeries[slot] = series;
                summarizedRejections++; return true;
            }
            AttachSeries(ref packet, series);
            rejectionSeries[slot] = new RejectionSeries { Actor = packet.Actor, Reason = reason, Count = 1,
                FirstSequence = packet.Sequence, LastSequence = packet.Sequence, FirstStamp = packet.Stamp,
                LastStamp = packet.Stamp, FirstSeconds = packet.Seconds, LastSeconds = packet.Seconds };
            return false;
        }

        private void AttachSeries(ref Packet packet, RejectionSeries series)
        {
            if (series.Count > 1)
            {
                // Attach the completed series to this next full result. Its own
                // producer sequence stays unique; suppressed results leave gaps.
                packet.Metadata = new[] {
                    GameLog.Field("reject_series_count", series.Count), GameLog.Field("reject_series_reason", series.Reason),
                    GameLog.Field("reject_series_first_seq", series.FirstSequence), GameLog.Field("reject_series_last_seq", series.LastSequence),
                    GameLog.Field("reject_series_first_duel_seconds", series.FirstSeconds), GameLog.Field("reject_series_last_duel_seconds", series.LastSeconds),
                    GameLog.Field("reject_series_first_mono_ms", (series.FirstStamp - started) * 1000d / Stopwatch.Frequency),
                    GameLog.Field("reject_series_last_mono_ms", (series.LastStamp - started) * 1000d / Stopwatch.Frequency) };
                if (finishedSeries.Count == 16) finishedSeries.RemoveAt(0);
                finishedSeries.Add(DescribeSeries(series));
            }
        }

        private static string DescribeSeries(RejectionSeries series) => "actor=" + series.Actor + " reason=" + series.Reason +
            " count=" + series.Count + " seq=" + series.FirstSequence + ".." + series.LastSequence + " duel_seconds=" +
            series.FirstSeconds.ToString("F3", CultureInfo.InvariantCulture) + ".." + series.LastSeconds.ToString("F3", CultureInfo.InvariantCulture);

        private void RememberFrame(int frame, double milliseconds)
        {
            if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || milliseconds <= 0) return;
            for (int i = 0; i < slowest.Length; i++)
                if (milliseconds > slowest[i].milliseconds)
                {
                    for (int j = slowest.Length - 1; j > i; j--) slowest[j] = slowest[j - 1];
                    slowest[i] = (frame, milliseconds); break;
                }
        }

        private static double Numeric(GameLogField field) => field.Type == GameLogFieldType.Int32 ||
            field.Type == GameLogFieldType.Int64 ? field.IntegerValue : field.NumberValue;
        private static GameLogField FieldAt(in Packet p, int index) => index switch
        { 0 => p.F0, 1 => p.F1, 2 => p.F2, 3 => p.F3, 4 => p.F4, 5 => p.F5, 6 => p.F6, _ => p.F7 };
        private static string Clip(string value, int limit) => value == null ? string.Empty : value.Length <= limit ? value : value.Substring(0, limit);
        private static string LimitUtf8(string value, int limit)
        {
            if (Utf8.GetByteCount(value) <= limit) return value;
            const string tail = "\n[summary truncated to byte budget]\n";
            int low = 0, high = value.Length;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (Utf8.GetByteCount(value.Substring(0, middle)) + Utf8.GetByteCount(tail) <= limit) low = middle;
                else high = middle - 1;
            }
            return value.Substring(0, low) + tail;
        }
    }
}
