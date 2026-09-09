using System;
using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>
    /// Which line of the journal is under the cursor.
    ///
    /// Kept apart from the view for the same reason the pause menu is:
    /// the selection is the only part of a journal that can be wrong,
    /// and IMGUI cannot be asked about it. Entries the catalog does not
    /// carry are dropped here rather than skipped while drawing, so the
    /// index the player moves and the row he sees are the same number.
    /// </summary>
    public sealed class JournalMenuModel
    {
        private readonly List<QuestLogEntry> rows =
            new List<QuestLogEntry>();

        public int Count => rows.Count;
        public int SelectedIndex { get; private set; } = -1;
        public bool HasSelection =>
            SelectedIndex >= 0 && SelectedIndex < rows.Count;

        public QuestLogEntry SelectedEntry =>
            HasSelection ? rows[SelectedIndex] : default;

        public QuestLogEntry GetEntry(int index)
        {
            if (index < 0 || index >= rows.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return rows[index];
        }

        /// <summary>
        /// Takes the log as it stands and puts the cursor on the first
        /// line. Opening always starts at the top: the journal is short
        /// and read from the beginning, and a remembered cursor would
        /// point at whatever happened to be there last time.
        /// </summary>
        public void Open(IReadOnlyList<QuestLogEntry> entries)
        {
            rows.Clear();
            if (entries != null)
            {
                for (int index = 0; index < entries.Count; index++)
                {
                    QuestLogEntry entry = entries[index];
                    if (QuestCatalog.TryGet(entry.Id, out _))
                    {
                        rows.Add(entry);
                    }
                }
            }

            SelectedIndex = rows.Count > 0 ? 0 : -1;
        }

        public bool MoveSelection(int delta)
        {
            if (delta == 0 || rows.Count == 0)
            {
                return false;
            }

            int next =
                (SelectedIndex + Math.Sign(delta)) % rows.Count;
            if (next < 0)
            {
                next += rows.Count;
            }

            if (next == SelectedIndex)
            {
                return false;
            }

            SelectedIndex = next;
            return true;
        }

        public bool SelectIndex(int index)
        {
            if (index < 0 ||
                index >= rows.Count ||
                index == SelectedIndex)
            {
                return false;
            }

            SelectedIndex = index;
            return true;
        }

        /// <summary>
        /// Whether the description panel has anything to show.
        /// </summary>
        public bool TryGetSelectedDefinition(
            out QuestDefinition definition,
            out bool completed)
        {
            if (HasSelection)
            {
                QuestLogEntry entry = rows[SelectedIndex];
                completed = entry.Status == QuestStatus.Completed;
                return QuestCatalog.TryGet(entry.Id, out definition);
            }

            definition = default;
            completed = false;
            return false;
        }
    }
}
