using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public enum QuestId
    {
        None = 0,
        FeedTheCat = 1
    }

    public enum QuestStatus
    {
        NotStarted = 0,
        Active = 1,
        Completed = 2
    }

    public readonly struct QuestDefinition
    {
        internal QuestDefinition(
            QuestId id,
            string titleLocalizationKey,
            string activeDescriptionLocalizationKey,
            string completedDescriptionLocalizationKey)
        {
            Id = id;
            TitleLocalizationKey = titleLocalizationKey;
            ActiveDescriptionLocalizationKey =
                activeDescriptionLocalizationKey;
            CompletedDescriptionLocalizationKey =
                completedDescriptionLocalizationKey;
        }

        public QuestId Id { get; }
        public string TitleLocalizationKey { get; }
        public string ActiveDescriptionLocalizationKey { get; }
        public string CompletedDescriptionLocalizationKey { get; }
    }

    public readonly struct QuestLogEntry
    {
        internal QuestLogEntry(QuestId id, QuestStatus status)
        {
            Id = id;
            Status = status;
        }

        public QuestId Id { get; }
        public QuestStatus Status { get; }
    }

    public static class QuestCatalog
    {
        private static readonly QuestDefinition[] Definitions =
        {
            new QuestDefinition(
                QuestId.FeedTheCat,
                "quest.feed_cat.title",
                "quest.feed_cat.description.active",
                "quest.feed_cat.description.completed")
        };

        private static readonly IReadOnlyList<QuestDefinition>
            DefinitionsView = Array.AsReadOnly(Definitions);

        public static IReadOnlyList<QuestDefinition> All =>
            DefinitionsView;

        public static bool TryGet(
            QuestId id,
            out QuestDefinition definition)
        {
            for (int index = 0; index < Definitions.Length; index++)
            {
                if (Definitions[index].Id == id)
                {
                    definition = Definitions[index];
                    return true;
                }
            }

            definition = default;
            return false;
        }

        public static QuestDefinition Get(QuestId id)
        {
            if (TryGet(id, out QuestDefinition definition))
            {
                return definition;
            }

            throw new ArgumentOutOfRangeException(
                nameof(id),
                id,
                "The quest is not present in the quest catalog.");
        }
    }
}
