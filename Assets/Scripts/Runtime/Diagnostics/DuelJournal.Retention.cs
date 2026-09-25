using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace BarPromenade
{
    internal sealed partial class DuelJournal
    {
        internal const long DirectoryByteLimit = 100L * 1024L * 1024L;
        private static readonly string[] OwnedFiles = { "duel.ndjson", "summary.txt", ".active", ".marked", ".complete" };
        private sealed class RetainedRound
        {
            internal string Path;
            internal long Bytes;
            internal bool Active, Protected, Priority;
            internal DateTime Modified;
        }

        private static string ReserveRound(string directory, string session, int round, long reservation, out FileStream lease)
        {
            lease = null;
            Directory.CreateDirectory(directory);
            using (Mutex mutex = BudgetMutex(directory))
            {
                EnterBudget(mutex);
                try
                {
                    long occupied = PruneCore(directory, 10, DirectoryByteLimit - reservation);
                    if (occupied > DirectoryByteLimit - reservation) throw new IOException("duel_directory_byte_limit");
                    string folder = Path.GetFullPath(Path.Combine(directory, "duel_" + session + "_" + round.ToString("D6", CultureInfo.InvariantCulture)));
                    if (!DirectChild(directory, folder) || Directory.Exists(folder)) throw new IOException("duel_round_path_exists");
                    Directory.CreateDirectory(folder);
                    try
                    {
                        lease = new FileStream(Path.Combine(folder, ".active"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                        byte[] bytes = Encoding.ASCII.GetBytes(reservation.ToString(CultureInfo.InvariantCulture));
                        lease.Write(bytes, 0, bytes.Length); lease.Flush();
                    }
                    catch { lease?.Dispose(); lease = null; throw; }
                    return folder;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        /// <summary>Only the worker/cold tests call this. Active leases and unrelated contents are never removed.</summary>
        internal static void Prune(string directory, int maxCompleted = 10, long maxBytes = DirectoryByteLimit)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;
            using (Mutex mutex = BudgetMutex(directory))
            {
                EnterBudget(mutex);
                try { PruneCore(Path.GetFullPath(directory), Math.Max(0, maxCompleted), Math.Max(0, maxBytes)); }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static long PruneCore(string directory, int maxCompleted, long maxBytes)
        {
            var rounds = new List<RetainedRound>();
            long bytes = 0;
            int closed = 0;
            foreach (string folder in Directory.EnumerateDirectories(directory, "duel_*", SearchOption.TopDirectoryOnly))
            {
                if (!OwnedRoundName(Path.GetFileName(folder)) || !DirectChild(directory, folder) ||
                    (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                var item = new RetainedRound { Path = folder, Modified = Directory.GetLastWriteTimeUtc(folder),
                    Priority = File.Exists(Path.Combine(folder, ".marked")) || !File.Exists(Path.Combine(folder, ".complete")) };
                foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    { item.Protected = true; continue; }
                    item.Bytes = AddBytes(item.Bytes, new FileInfo(entry).Length);
                    if (Array.IndexOf(OwnedFiles, Path.GetFileName(entry)) < 0) item.Protected = true;
                }
                string active = Path.Combine(folder, ".active");
                if (File.Exists(active))
                {
                    try { using (new FileStream(active, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
                    catch (IOException) { item.Active = true; }
                    catch (UnauthorizedAccessException) { item.Active = item.Protected = true; }
                    if (item.Active)
                    {
                        // Reserve each live round's maximum, not merely today's
                        // file size, so simultaneous journals cannot oversubscribe.
                        item.Bytes = Math.Max(item.Bytes, ReadReservation(active));
                    }
                }
                if (!item.Active) closed++;
                bytes = AddBytes(bytes, item.Bytes); rounds.Add(item);
            }
            rounds.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : a.Modified.CompareTo(b.Modified));
            foreach (RetainedRound item in rounds)
            {
                if (closed <= maxCompleted && bytes <= maxBytes) break;
                if (item.Active || item.Protected || !RemoveOwnedRound(directory, item.Path)) continue;
                // Once saturated, subtraction cannot recover the original sum.
                // Stay conservative until the next scan instead of undercounting.
                if (bytes != long.MaxValue) bytes -= item.Bytes;
                closed--;
            }
            return bytes;
        }

        private static long ReadReservation(string active)
        {
            const long maximum = 20L * 1024L * 1024L;
            try
            {
                using var stream = new FileStream(active, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length <= 0 || stream.Length > 32) return maximum;
                var buffer = new byte[32];
                int count = 0, read;
                while (count < buffer.Length && (read = stream.Read(buffer, count, buffer.Length - count)) > 0)
                    count += read;
                for (int i = 0; i < count; i++) if (buffer[i] < (byte)'0' || buffer[i] > (byte)'9') return maximum;
                if (long.TryParse(Encoding.ASCII.GetString(buffer, 0, count), NumberStyles.None,
                    CultureInfo.InvariantCulture, out long found)) return Math.Max(4096, Math.Min(maximum, found));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return maximum;
        }

        private static long AddBytes(long current, long added) =>
            added > long.MaxValue - current ? long.MaxValue : current + added;

        private static bool RemoveOwnedRound(string directory, string folder)
        {
            if (!DirectChild(directory, folder) || !OwnedRoundName(Path.GetFileName(folder)) ||
                (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) return false;
            string active = Path.Combine(folder, ".active");
            FileStream guard = null;
            try
            {
                if (File.Exists(active)) guard = new FileStream(active, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
                    if (Array.IndexOf(OwnedFiles, Path.GetFileName(entry)) < 0 ||
                        (File.GetAttributes(entry) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return false;
                foreach (string name in OwnedFiles) if (name != ".active") File.Delete(Path.Combine(folder, name));
                guard?.Dispose(); guard = null;
                File.Delete(active);
                Directory.Delete(folder, false);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            finally { guard?.Dispose(); }
        }

        private static bool OwnedRoundName(string name)
        {
            if (!name.StartsWith("duel_", StringComparison.Ordinal)) return false;
            int split = name.LastIndexOf('_');
            if (split < 6 || split > 69 || name.Length - split - 1 > 10) return false;
            for (int i = 5; i < split; i++)
                if (!(name[i] >= 'a' && name[i] <= 'z' || name[i] >= 'A' && name[i] <= 'Z' ||
                    name[i] >= '0' && name[i] <= '9' || name[i] == '-')) return false;
            return uint.TryParse(name.Substring(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _);
        }

        private static bool DirectChild(string directory, string folder)
        {
            string parent = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(folder);
            return full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase);
        }

        private static Mutex BudgetMutex(string directory)
        {
            uint hash = 2166136261;
            foreach (char c in Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant())
                hash = unchecked((hash ^ c) * 16777619);
            return new Mutex(false, "BarPromenade-DuelJournal-" + hash.ToString("x8", CultureInfo.InvariantCulture));
        }

        private static void EnterBudget(Mutex mutex)
        {
            try { if (!mutex.WaitOne(250)) throw new IOException("duel_retention_busy"); }
            catch (AbandonedMutexException) { }
        }
    }
}
