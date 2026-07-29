using System;
using System.IO;
using System.Text;
using System.Threading;

namespace BarPromenade
{
    internal sealed class GameLogFileSink : IDisposable
    {
        private static readonly UTF8Encoding Utf8WithoutBom =
            new UTF8Encoding(false);

        private readonly object sync = new object();
        private readonly string path;
        private readonly long maxFileBytes;
        private readonly int retainedArchives;
        private readonly TimeSpan flushInterval;
        private FileStream stream;
        private StreamWriter writer;
        private Timer flushTimer;
        private long currentBytes;
        private bool disposed;
        private bool faulted;

        internal GameLogFileSink(
            GameLogSettings settings,
            bool startFlushTimer = true)
        {
            path = settings?.FilePath ?? string.Empty;
            maxFileBytes = settings?.MaxFileBytes ??
                GameLogSettings.DefaultMaxFileBytes;
            retainedArchives = settings?.RetainedArchives ??
                GameLogSettings.DefaultRetainedArchives;
            flushInterval = settings?.FlushInterval ??
                TimeSpan.FromSeconds(
                    GameLogSettings.DefaultFlushIntervalSeconds);

            try
            {
                OpenNewSessionNoLock();
                if (startFlushTimer && !faulted)
                {
                    int intervalMilliseconds = (int)Math.Min(
                        int.MaxValue,
                        Math.Max(10d, flushInterval.TotalMilliseconds));
                    flushTimer = new Timer(
                        FlushFromTimer,
                        null,
                        intervalMilliseconds,
                        intervalMilliseconds);
                }
            }
            catch
            {
                FaultNoLock();
            }
        }

        internal bool IsOperational
        {
            get
            {
                lock (sync)
                {
                    return !disposed && !faulted && writer != null;
                }
            }
        }

        internal void Write(string line, bool flushImmediately)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            lock (sync)
            {
                if (disposed || faulted || writer == null)
                {
                    return;
                }

                try
                {
                    long encodedBytes =
                        Utf8WithoutBom.GetByteCount(line) + 1L;
                    if (currentBytes > 0L &&
                        currentBytes + encodedBytes > maxFileBytes)
                    {
                        RotateForSizeNoLock();
                    }

                    writer.WriteLine(line);
                    currentBytes += encodedBytes;
                    if (flushImmediately)
                    {
                        writer.Flush();
                    }
                }
                catch
                {
                    FaultNoLock();
                }
            }
        }

        internal void Flush()
        {
            lock (sync)
            {
                FlushNoLock();
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                try
                {
                    flushTimer?.Dispose();
                }
                catch
                {
                    // Logging must never interfere with gameplay shutdown.
                }

                flushTimer = null;
                CloseWriterNoLock();
            }
        }

        private void OpenNewSessionNoLock()
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                faulted = true;
                return;
            }

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(path) &&
                new FileInfo(path).Length > 0L)
            {
                RotateFilesNoLock();
            }

            OpenWriterNoLock();
        }

        private void OpenWriterNoLock()
        {
            stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite);
            writer = new StreamWriter(stream, Utf8WithoutBom)
            {
                AutoFlush = false,
                NewLine = "\n"
            };
            currentBytes = stream.Length;
        }

        private void RotateForSizeNoLock()
        {
            CloseWriterNoLock();
            RotateFilesNoLock();
            OpenWriterNoLock();
        }

        private void RotateFilesNoLock()
        {
            if (!File.Exists(path))
            {
                return;
            }

            if (retainedArchives <= 0)
            {
                TruncateActiveFileNoLock();
                return;
            }

            string oldest = GetArchivePath(retainedArchives);
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }

            for (int index = retainedArchives - 1; index >= 1; index--)
            {
                string source = GetArchivePath(index);
                if (!File.Exists(source))
                {
                    continue;
                }

                string destination = GetArchivePath(index + 1);
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                File.Move(source, destination);
            }

            string firstArchive = GetArchivePath(1);
            if (File.Exists(firstArchive))
            {
                File.Delete(firstArchive);
            }

            File.Copy(path, firstArchive);
            TruncateActiveFileNoLock();
        }

        private string GetArchivePath(int index)
        {
            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            string stem = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            return Path.Combine(
                directory,
                stem + "." + index + extension);
        }

        private void TruncateActiveFileNoLock()
        {
            using (FileStream ignored = new FileStream(
                       path,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.ReadWrite))
            {
            }
        }

        private void FlushFromTimer(object state)
        {
            lock (sync)
            {
                FlushNoLock();
            }
        }

        private void FlushNoLock()
        {
            if (disposed || faulted || writer == null)
            {
                return;
            }

            try
            {
                writer.Flush();
            }
            catch
            {
                FaultNoLock();
            }
        }

        private void CloseWriterNoLock()
        {
            try
            {
                writer?.Flush();
            }
            catch
            {
                // Preserve fail-safe shutdown even if the disk disappeared.
            }

            try
            {
                writer?.Dispose();
            }
            catch
            {
                // Preserve fail-safe shutdown even if the disk disappeared.
            }

            writer = null;
            stream = null;
        }

        private void FaultNoLock()
        {
            faulted = true;
            try
            {
                flushTimer?.Dispose();
            }
            catch
            {
                // No fallback logging: that could recurse into this sink.
            }

            flushTimer = null;
            CloseWriterNoLock();
        }
    }
}
