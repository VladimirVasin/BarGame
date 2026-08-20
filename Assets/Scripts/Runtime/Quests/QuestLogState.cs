using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BarPromenade
{
    public sealed class QuestLogState
    {
        private readonly List<QuestLogEntry> entries =
            new List<QuestLogEntry>();
        private readonly ReadOnlyCollection<QuestLogEntry> entriesView;

        public QuestLogState()
        {
            entriesView = entries.AsReadOnly();
        }

        public IReadOnlyList<QuestLogEntry> Entries => entriesView;

        public void ResetWithStarterQuests()
        {
            entries.Clear();
            TryActivate(QuestId.FeedTheCat);
        }

        public QuestStatus GetStatus(QuestId questId)
        {
            int index = FindIndex(questId);
            return index >= 0
                ? entries[index].Status
                : QuestStatus.NotStarted;
        }

        /// <summary>
        /// Puts a quest up in the log. A repeatable one that was
        /// finished goes back up in its old place rather than as a
        /// second line — the watchman's next hole is the same job, not
        /// a new one, and the journal should read that way.
        /// </summary>
        public bool TryActivate(QuestId questId)
        {
            if (!QuestCatalog.TryGet(
                    questId,
                    out QuestDefinition definition))
            {
                return false;
            }

            int index = FindIndex(questId);
            if (index >= 0)
            {
                if (!definition.IsRepeatable ||
                    entries[index].Status != QuestStatus.Completed)
                {
                    return false;
                }

                entries[index] =
                    new QuestLogEntry(questId, QuestStatus.Active);
                return true;
            }

            entries.Add(
                new QuestLogEntry(questId, QuestStatus.Active));
            return true;
        }

        public bool TryComplete(QuestId questId)
        {
            int index = FindIndex(questId);
            if (index < 0 ||
                entries[index].Status != QuestStatus.Active)
            {
                return false;
            }

            entries[index] =
                new QuestLogEntry(questId, QuestStatus.Completed);
            return true;
        }

        private int FindIndex(QuestId questId)
        {
            for (int index = 0; index < entries.Count; index++)
            {
                if (entries[index].Id == questId)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
