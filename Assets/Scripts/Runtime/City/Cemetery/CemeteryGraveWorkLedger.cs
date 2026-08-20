using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BarPromenade
{
    /// <summary>
    /// One grave the hero was sent to dig: which plot the watchman
    /// signed over, how far the work on it has got, and the line cut
    /// into its board. Everything the world needs to stand that grave
    /// up again after a trip indoors.
    /// </summary>
    public readonly struct CemeteryGraveWorkRecord
    {
        internal CemeteryGraveWorkRecord(
            string plotId,
            CemeteryGraveWorkStage stage,
            string epitaph)
        {
            PlotId = plotId;
            Stage = stage;
            Epitaph = epitaph;
        }

        /// <summary>The plot, by the id the cemetery plan gives it.
        /// </summary>
        public string PlotId { get; }

        public CemeteryGraveWorkStage Stage { get; }

        /// <summary>The hero's own line, or empty while the board is
        /// bare.</summary>
        public string Epitaph { get; }

        /// <summary>Taken and not yet a grave: work he still owes.
        /// </summary>
        public bool IsUnfinished =>
            Stage >= CemeteryGraveWorkStage.Marked &&
            Stage < CemeteryGraveWorkStage.Sealed;
    }

    /// <summary>
    /// The old man's book of work.
    ///
    /// The job used to be one grave and one number: a single rung on a
    /// ladder in <see cref="GameSessionState"/>. He hands out more than
    /// one now — a man can be holding three holes at once and be owed
    /// for a fourth he closed yesterday — so the rung is per plot and
    /// they all live here, in the order they were taken.
    ///
    /// Nothing in it ever goes backwards. Every worksite in the
    /// cemetery is rebuilt from these records alone, and a stage that
    /// could fall would be a grave that closes and opens again.
    /// </summary>
    public sealed class CemeteryGraveWorkLedger
    {
        private readonly List<CemeteryGraveWorkRecord> records =
            new List<CemeteryGraveWorkRecord>();
        private readonly
            ReadOnlyCollection<CemeteryGraveWorkRecord> recordsView;

        public CemeteryGraveWorkLedger()
        {
            recordsView = records.AsReadOnly();
        }

        /// <summary>Every grave he was ever sent to, oldest first.
        /// </summary>
        public IReadOnlyList<CemeteryGraveWorkRecord> Records =>
            recordsView;

        /// <summary>How many holes he is holding open right now: taken
        /// and not yet closed with a stone at the head.</summary>
        public int UnfinishedCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < records.Count; index++)
                {
                    if (records[index].IsUnfinished)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public void Reset()
        {
            records.Clear();
        }

        public CemeteryGraveWorkStage GetStage(string plotId)
        {
            int index = FindIndex(plotId);
            return index >= 0
                ? records[index].Stage
                : CemeteryGraveWorkStage.Unclaimed;
        }

        public string GetEpitaph(string plotId)
        {
            int index = FindIndex(plotId);
            return index >= 0 ? records[index].Epitaph : string.Empty;
        }

        /// <summary>True once this plot has been signed over, whatever
        /// has been done to it since.</summary>
        public bool IsTaken(string plotId)
        {
            return FindIndex(plotId) >= 0;
        }

        /// <summary>
        /// Moves one grave up its ladder, opening a record for it the
        /// first time. Refuses anything that is not forward, and
        /// refuses a plot with no id at all — an unnamed grave could
        /// not be found again on the next city build.
        /// </summary>
        public bool TryAdvance(
            string plotId,
            CemeteryGraveWorkStage stage)
        {
            if (string.IsNullOrEmpty(plotId) ||
                stage <= CemeteryGraveWorkStage.Unclaimed ||
                !System.Enum.IsDefined(
                    typeof(CemeteryGraveWorkStage),
                    stage))
            {
                return false;
            }

            int index = FindIndex(plotId);
            if (index < 0)
            {
                records.Add(
                    new CemeteryGraveWorkRecord(
                        plotId,
                        stage,
                        string.Empty));
                return true;
            }

            CemeteryGraveWorkRecord record = records[index];
            if (stage <= record.Stage)
            {
                return false;
            }

            records[index] = new CemeteryGraveWorkRecord(
                plotId,
                stage,
                record.Epitaph);
            return true;
        }

        /// <summary>
        /// Cuts a line into one board. Written once and never revised,
        /// and only onto a grave he was actually sent to dig.
        /// </summary>
        public bool TrySetEpitaph(string plotId, string text)
        {
            int index = FindIndex(plotId);
            if (index < 0 ||
                !string.IsNullOrEmpty(records[index].Epitaph))
            {
                return false;
            }

            string cut = CemeteryEpitaph.Sanitize(text);
            if (string.IsNullOrEmpty(cut))
            {
                return false;
            }

            records[index] = new CemeteryGraveWorkRecord(
                plotId,
                records[index].Stage,
                cut);
            return true;
        }

        private int FindIndex(string plotId)
        {
            if (string.IsNullOrEmpty(plotId))
            {
                return -1;
            }

            for (int index = 0; index < records.Count; index++)
            {
                if (string.Equals(
                        records[index].PlotId,
                        plotId,
                        System.StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
