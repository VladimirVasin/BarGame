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
        private readonly Packet[] causalTail = new Packet[32];
        private int causalTailNext, causalTailCount;
        private readonly List<GameLogField> fields = new List<GameLogField>(48);
        private readonly GameLogFormatter.Buffer recordBuffer = new GameLogFormatter.Buffer(16384, 16384);
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
        private long bytes, telemetryBytes, written, fileDropped, telemetryDropped, causalDropped,
            oversized, seenProducerDrops, summarizedRejections;
        private long detailBytes, essentialBytes, forcedBytes, detailSampledOut, essentialSampledOut;
        private long forcedRoutineBytes, forcedUrgentBytes, forcedTerminalBytes;
        private long firstSampledSequence, lastSampledSequence;
        private int detailFrame = -1, essentialFrame = -1, detailStride = 1, essentialStride = 1;
        private int stateFrame = -1, stateStride = 1, essentialFrameIndex, stateFrameIndex;
        private bool fileLimited, telemetryLimited, causalLimited, limitMarkerWritten, marked;

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
                                case Kind.Flush:
                                    output?.Flush();
                                    if (fileLimited && roundDirectory != null)
                                    {
                                        long losses = Math.Max(0, lastPacket.Dropped - roundHeader.Dropped) + fileDropped;
                                        File.WriteAllText(Path.Combine(roundDirectory, "summary.txt"),
                                            LimitUtf8(BuildSummary(lastPacket, losses, false, live: true), (int)SummaryBudget), Utf8);
                                    }
                                    lastPacket.Barrier.TrySetResult(true);
                                    break;
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
        private long RecordBudget => maxRoundBytes - SummaryBudget - FooterBudget - 64;
        // Samples cannot consume the room reserved for commands, contacts and
        // results. Unused sample space remains available to causal records.
        private long TelemetryBudget => RecordBudget * 7 / 10;
        private long DetailBudget => TelemetryBudget * 55 / 100;
        private long EssentialBudget => TelemetryBudget * 30 / 100;
        private long ForcedBudget => TelemetryBudget - DetailBudget - EssentialBudget;
        private long ForcedRoutineBudget => ForcedBudget * 7 / 10;
        private long ForcedUrgentBudget => ForcedBudget / 5;
        private long ForcedTerminalBudget => ForcedBudget - ForcedRoutineBudget - ForcedUrgentBudget;

        // Packets carry eight inline fields; pass by reference through IO/error
        // boundaries instead of copying the large struct across worker frames.
        private void OpenRound(in Packet packet)
        {
            if (roundDirectory != null) { Packet end = packet; end.Name = "reset"; CloseRound(end); }
            roundHeader = packet; lastPacket = packet;
            events.Clear(); rejections.Clear(); longest.Clear(); milestones.Clear(); Array.Clear(slowest, 0, slowest.Length);
            Array.Clear(rejectionSeries, 0, rejectionSeries.Length); finishedSeries.Clear();
            Array.Clear(causalTail, 0, causalTail.Length); causalTailNext = causalTailCount = 0;
            bytes = telemetryBytes = written = fileDropped = telemetryDropped = causalDropped = oversized = summarizedRejections = 0;
            detailBytes = essentialBytes = forcedBytes = detailSampledOut = essentialSampledOut = 0;
            forcedRoutineBytes = forcedUrgentBytes = forcedTerminalBytes = 0;
            firstSampledSequence = lastSampledSequence = 0;
            detailFrame = essentialFrame = -1; detailStride = essentialStride = 1;
            stateFrame = -1; stateStride = 1; essentialFrameIndex = stateFrameIndex = 0;
            seenProducerDrops = packet.Dropped;
            fileLimited = telemetryLimited = causalLimited = limitMarkerWritten = marked = false; roundIoError = null;
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
            if (CausalTailEvent(packet.Name)) RememberCausalTail(packet);
            bool sample = SampleEvent(packet.Name);
            if (roundIoError != null || causalLimited)
            { DropFileRecord(sample); return; }
            bool forced = sample && packet.ForcedSampleReason != null;
            bool essential = sample && EssentialSampleEvent(packet.Name);
            bool frameSample = essential && EssentialFrameEvent(packet.Name);
            if (sample && !forced)
            {
                // Decide once per priority/frame so correlated fields and actor
                // poses use the same stride. Snapshot and frame encounter clocks
                // are separate: fixed snapshot cadence must not alias a stride.
                if (frameSample && essentialFrame != packet.Frame)
                { essentialFrame = packet.Frame; essentialFrameIndex++; essentialStride = SamplingStride(essentialBytes, EssentialBudget); }
                if (essential && !frameSample && stateFrame != packet.Frame)
                { stateFrame = packet.Frame; stateFrameIndex++; stateStride = SamplingStride(essentialBytes, EssentialBudget); }
                if (!essential && detailFrame != packet.Frame)
                { detailFrame = packet.Frame; detailStride = SamplingStride(detailBytes, DetailBudget); }
                packet.SampleStride = essential ? frameSample ? essentialStride : stateStride : detailStride;
                int sampleIndex = essential ? frameSample ? essentialFrameIndex : stateFrameIndex : packet.Frame;
                if (sampleIndex % packet.SampleStride != 0)
                { SampleOut(packet, essential); return; }
            }
            else if (sample) packet.SampleStride = 1;
            if (!FormatPacket(packet)) { DropFileRecord(sample); return; }
            if (FormattedByteLength > 16384) { oversized++; DropFileRecord(sample); return; }
            bool terminal = forced && TerminalSnapshot(packet.ForcedSampleReason);
            bool urgent = forced && UrgentSnapshot(packet.ForcedSampleReason);
            long sampleBytes = forced ? terminal ? forcedTerminalBytes : urgent ? forcedUrgentBytes : forcedRoutineBytes :
                essential ? essentialBytes : detailBytes;
            long sampleBudget = forced ? terminal ? ForcedTerminalBudget : urgent ? ForcedUrgentBudget : ForcedRoutineBudget :
                essential ? EssentialBudget : DetailBudget;
            if (sample && sampleBytes + FormattedByteLength > sampleBudget)
            {
                telemetryLimited = fileLimited = true;
                if (forced) DropFileRecord(true); else SampleOut(packet, essential);
                WriteLimitMarker(packet, "telemetry_budget");
                return;
            }
            long before = bytes;
            if (WritePacket(packet, false, alreadyFormatted: true))
            {
                if (sample)
                {
                    long added = bytes - before;
                    telemetryBytes += added;
                    if (forced)
                    {
                        forcedBytes += added;
                        if (terminal) forcedTerminalBytes += added;
                        else if (urgent) forcedUrgentBytes += added;
                        else forcedRoutineBytes += added;
                    }
                    else if (essential) essentialBytes += added;
                    else detailBytes += added;
                }
                return;
            }
            DropFileRecord(sample);
            causalLimited = fileLimited = true;
            WriteLimitMarker(packet, "round_budget");
        }

        private static int SamplingStride(long used, long budget) =>
            used < budget / 2 ? 1 : used < budget * 3 / 4 ? 4 : used < budget * 9 / 10 ? 16 : 64;

        private static bool TerminalSnapshot(string reason) => reason == "round_result" || reason == "round_ended";
        private static bool UrgentSnapshot(string reason) => reason == "marked" || reason == "recovery_stall";

        private void SampleOut(in Packet packet, bool essential)
        {
            if (essential) essentialSampledOut++; else detailSampledOut++;
            if (firstSampledSequence == 0) firstSampledSequence = packet.Sequence;
            lastSampledSequence = packet.Sequence;
        }

        private void DropFileRecord(bool sample)
        {
            fileDropped++;
            if (sample) telemetryDropped++; else causalDropped++;
            Interlocked.Increment(ref dropped);
        }

        private void WriteLimitMarker(in Packet packet, string cause)
        {
            // One warning fits even a small test budget. A later hard limit is
            // explicit in the terminal causal_limit and omission counters.
            if (limitMarkerWritten) return;
            Packet marker = packet; marker.Name = "file_limit"; marker.Metadata = null;
            marker.F0 = GameLog.Field("omitted_event", packet.Name);
            marker.F1 = GameLog.Field("max_round_bytes", maxRoundBytes);
            marker.F2 = GameLog.Field("cause", cause);
            marker.F3 = GameLog.Field("telemetry_budget_bytes", TelemetryBudget);
            marker.F4 = GameLog.Field("causal_reserve_bytes", RecordBudget - TelemetryBudget);
            marker.F5 = GameLog.Field("sample_priority", packet.ForcedSampleReason != null ? "forced" :
                EssentialSampleEvent(packet.Name) ? "essential" : "detail");
            marker.F6 = marker.F7 = default;
            marker.ForcedSampleReason = null; marker.SampleStride = 0;
            limitMarkerWritten = WritePacket(marker, true);
            Prune(directory);
        }

        private int FormattedByteLength => Utf8.GetByteCount(recordBuffer.Characters, 0, recordBuffer.Count) + 1;

        private bool FormatPacket(in Packet packet, bool compactTail = false)
        {
            fields.Clear();
            fields.Add(GameLog.Field("round", roundHeader.Round));
            fields.Add(GameLog.Field("frame", packet.Frame)); fields.Add(GameLog.Field("tick", packet.Tick));
            fields.Add(GameLog.Field("duel_seconds", packet.Seconds)); fields.Add(GameLog.Field("actor", packet.Actor));
            fields.Add(GameLog.Field("target", packet.Target)); fields.Add(GameLog.Field("action", packet.Action));
            fields.Add(GameLog.Field("request", packet.Request));
            if (packet.Kind == Kind.Begin && packet.Name == "round_begin")
            {
                fields.Add(GameLog.Field("duel_schema_version", 3));
                fields.Add(GameLog.Field("stage_sample_encoding", "absent_numeric_zero"));
                fields.Add(GameLog.Field("zero_stage_events", ZeroStageEvents));
                fields.Add(GameLog.Field("sample_absence", "unknown_on_thinned_or_limited_frames"));
                fields.Add(GameLog.Field("sampling_strides", "1,4,16,64_at_0,50,75,90_percent_per_priority"));
                fields.Add(GameLog.Field("detail_budget_bytes", DetailBudget));
                fields.Add(GameLog.Field("essential_budget_bytes", EssentialBudget));
                fields.Add(GameLog.Field("forced_budget_bytes", ForcedBudget));
                fields.Add(GameLog.Field("forced_reserve_percent", "routine70/marked-stall20/terminal10"));
                fields.Add(GameLog.Field("telemetry_budget_bytes", TelemetryBudget));
                fields.Add(GameLog.Field("causal_tail", "summary_original_envelopes"));
            }
            if (packet.SampleStride > 0)
            {
                fields.Add(GameLog.Field("sample_priority", packet.ForcedSampleReason != null ? "forced" :
                    EssentialSampleEvent(packet.Name) ? "essential" : "detail"));
                fields.Add(GameLog.Field("sample_group", packet.ForcedSampleReason != null ? "forced_snapshot" :
                    EssentialFrameEvent(packet.Name) ? "frame" : EssentialSampleEvent(packet.Name) ? "snapshot" : "detail_frame"));
                fields.Add(GameLog.Field("sample_group_stride", packet.SampleStride));
                if (packet.ForcedSampleReason != null) fields.Add(GameLog.Field("snapshot_reason", packet.ForcedSampleReason));
            }
            if (packet.Dropped > seenProducerDrops)
                fields.Add(GameLog.Field("queue_dropped_before", packet.Dropped - seenProducerDrops));
            if (compactTail) fields.Add(GameLog.Field("causal_tail_copy", true));
            if (packet.Metadata != null)
                foreach (GameLogField field in packet.Metadata) AddField(field, compactTail);
            for (int i = 0; i < 8; i++) AddField(FieldAt(packet, i), compactTail);
            double elapsed = Math.Max(0d, (packet.Stamp - started) / (double)Stopwatch.Frequency);
            bool formatted = GameLogFormatter.TryFormat(recordBuffer, utcStarted.AddSeconds(elapsed),
                (long)(elapsed * 1000d), packet.Sequence, packet.Name == "file_limit" ? GameLogLevel.Warning : GameLogLevel.Info,
                "duel", packet.Name, SessionId, "CombatTest", null, fields);
            if (!formatted) { oversized++; return false; }
            return true;
        }

        private bool WritePacket(in Packet packet, bool terminal, bool alreadyFormatted = false)
        {
            if (!alreadyFormatted && !FormatPacket(packet)) return false;
            int length = FormattedByteLength;
            long limit = maxRoundBytes - SummaryBudget - 64 - (terminal ? 0 : FooterBudget);
            if (length > 16384 || bytes + length > limit)
            { if (length > 16384) oversized++; return false; }
            output.Write(recordBuffer.Characters, 0, recordBuffer.Count); output.Write('\n');
            bytes += length; written++; seenProducerDrops = packet.Dropped;
            return true;
        }

        private void AddField(GameLogField field, bool compactTail = false)
        {
            if (string.IsNullOrEmpty(field.Name)) return;
            // Correlation fields are owned by the envelope; payload cannot spoof them.
            foreach (GameLogField existing in fields) if (existing.Name == field.Name) return;
            if (compactTail && field.Type == GameLogFieldType.String && field.StringValue != null && field.StringValue.Length > 64)
            {
                field = GameLog.Field(field.Name, Clip(field.StringValue, 64));
                bool recorded = false;
                foreach (GameLogField existing in fields) if (existing.Name == "tail_strings_clipped") { recorded = true; break; }
                if (!recorded) fields.Add(GameLog.Field("tail_strings_clipped", true));
            }
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
                    footer.Metadata = new[] {
                        GameLog.Field("summarized_rejections", summarizedRejections),
                        GameLog.Field("telemetry_dropped_records", telemetryDropped),
                        GameLog.Field("causal_dropped_records", causalDropped),
                        GameLog.Field("producer_dropped_records", Math.Max(0, packet.Dropped - roundHeader.Dropped)),
                        GameLog.Field("detail_sampled_out_records", detailSampledOut),
                        GameLog.Field("essential_sampled_out_records", essentialSampledOut),
                        GameLog.Field("first_sampled_out_seq", firstSampledSequence),
                        GameLog.Field("last_sampled_out_seq", lastSampledSequence),
                        GameLog.Field("telemetry_limit", telemetryLimited), GameLog.Field("causal_limit", causalLimited) };
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

        private string BuildSummary(in Packet packet, long losses, bool complete, bool live = false)
        {
            var text = new StringBuilder(2048);
            text.AppendLine("Duel journal " + SessionId + ", round " + roundHeader.Round);
            text.AppendLine("Status: " + (live ? "live" : complete ? "completed" : "incomplete") + "; reason: " + Clip(packet.Name, 128));
            text.AppendLine("Ticks: " + roundHeader.Tick + ".." + packet.Tick + "; records written: " + written);
            text.AppendLine("Losses: " + losses + "; file_limit: " + fileLimited + "; oversized: " + oversized +
                "; io_error: " + (roundIoError ?? "none") + "; marked: " + marked);
            text.AppendLine("File omissions: telemetry=" + telemetryDropped + "; causal=" + causalDropped +
                "; telemetry_budget_bytes=" + TelemetryBudget + "; causal_reserve_bytes=" + (RecordBudget - TelemetryBudget));
            text.AppendLine("Repeated rejection records summarized (not lost): " + summarizedRejections);
            text.AppendLine("Adaptive samples omitted (not losses): detail=" + detailSampledOut + "; essential=" + essentialSampledOut +
                "; first_seq=" + firstSampledSequence + "; last_seq=" + lastSampledSequence);
            text.AppendLine("Sample bytes used/budget: detail=" + detailBytes + "/" + DetailBudget +
                "; essential=" + essentialBytes + "/" + EssentialBudget + "; forced=" + forcedBytes + "/" + ForcedBudget);
            text.AppendLine("Forced bytes used/budget: routine=" + forcedRoutineBytes + "/" + ForcedRoutineBudget +
                "; marked-stall=" + forcedUrgentBytes + "/" + ForcedUrgentBudget + "; terminal=" + forcedTerminalBytes + "/" + ForcedTerminalBudget);
            text.AppendLine("Absent numeric-zero stage samples (not lost): " + ZeroStageEvents);
            text.AppendLine("Collector stopwatch ticks: " + CollectorTicks + "/" + Stopwatch.Frequency +
                "; maximum queued: " + MaximumQueued);
            AppendCausalTail(text);
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
            Array.Clear(causalTail, 0, causalTail.Length); causalTailNext = causalTailCount = 0;
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

        private static bool SampleEvent(string name) => name == "frame" || name == "frame_work" || name == "frame_detail" ||
            name == "frame_delivery" || name == "pose_work" || name == "snapshot" || name == "ragdoll_snapshot" ||
            name == "state" || name == "pose" || name == "vectors" || name == "presentation" || name == "balance" ||
            name == "impulse_movement" ||
            name.EndsWith("_snapshot", StringComparison.Ordinal) || name.EndsWith("_sample", StringComparison.Ordinal);

        private static bool EssentialSampleEvent(string name) => name == "state" || name == "presentation" || name == "balance" ||
            EssentialFrameEvent(name) || name.EndsWith("_snapshot", StringComparison.Ordinal);

        private static bool EssentialFrameEvent(string name) => name == "frame" || name == "frame_work" ||
            name == "frame_detail" || name == "frame_delivery" || name == "frame_stage_sample";

        // Keep actions and outcomes separate from repeated solver corrections,
        // discarded time, AI decisions and clearance candidate geometry. The
        // existing milestone list still describes those diagnostic transitions.
        private static bool CausalTailEvent(string name) => name == "command_requested" || name == "command_result" ||
            name == "input" || name == "input_rejected" || name == "input_gate" || name == "focus" ||
            name == "application_pause" || name == "phase" || name == "impact_applied" || name == "impact_kind" ||
            name == "mark" || name == "round_result" || name == "snapshot_forced" ||
            name.StartsWith("weapon_", StringComparison.Ordinal) && name != "weapon_constraint" && !SampleEvent(name) ||
            name.StartsWith("ammo_", StringComparison.Ordinal) || name.StartsWith("reload_", StringComparison.Ordinal) ||
            name.StartsWith("carry_", StringComparison.Ordinal) || name.EndsWith("_fired", StringComparison.Ordinal) ||
            name.IndexOf("_shot_", StringComparison.Ordinal) >= 0 || name.IndexOf("_reload_", StringComparison.Ordinal) >= 0;

        private void RememberCausalTail(in Packet packet)
        {
            causalTail[causalTailNext] = packet;
            causalTailNext = (causalTailNext + 1) % causalTail.Length;
            causalTailCount = Math.Min(causalTailCount + 1, causalTail.Length);
        }

        private void AppendCausalTail(StringBuilder text)
        {
            // Finalization only: producer packets retain bounded inline fields,
            // and the latest complete JSON envelopes use half the existing
            // summary allowance. No extra file, sequence or disk budget is added.
            const string title = "Causal/action tail (latest original envelopes; copies, not additional duel records):";
            int remaining = (int)SummaryBudget / 2 - Utf8.GetByteCount(title) - 130;
            var rows = new List<string>(causalTailCount);
            for (int offset = 1; offset <= causalTailCount; offset++)
            {
                Packet packet = causalTail[(causalTailNext - offset + causalTail.Length) % causalTail.Length];
                if (!FormatPacket(packet, compactTail: true)) continue;
                int length = FormattedByteLength;
                if (length > remaining) break;
                rows.Add(new string(recordBuffer.Characters, 0, recordBuffer.Count));
                remaining -= length;
            }
            text.AppendLine(title);
            for (int i = rows.Count - 1; i >= 0; i--) text.AppendLine(rows[i]);
            text.AppendLine("End causal/action tail; retained=" + rows.Count + "; older_or_budget_omitted=" + (causalTailCount - rows.Count));
        }

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
