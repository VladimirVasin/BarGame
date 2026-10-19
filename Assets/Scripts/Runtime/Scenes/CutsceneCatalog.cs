using System;
using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>
    /// Prepared content is manually sampled from the player's clock. Stop must
    /// restore owned content, or retain explicitly committed gameplay ownership.
    /// Failed Prepare always restores its actors, props and sound.
    /// Hero actions and speech still use the shared rig and speech owners.
    /// </summary>
    public interface ICutscenePlayback
    {
        double DurationSeconds { get; }
        void Prepare();
        void Sample(double elapsedSeconds);
        void SetPaused(bool paused);
        void Stop();
    }

    /// <summary>
    /// Optional grounded approach. The presentation clock remains at zero until
    /// the shared hero controller has presented its neutral entry frame.
    /// </summary>
    public interface ICutscenePreparationPlayback : ICutscenePlayback
    {
        bool IsReady { get; }
        void AdvancePreparation(float seconds);
    }

    /// <summary>
    /// Optional handoff from scripted presentation to live gameplay. The same
    /// scene and readout continue, while the ordinary pause menu owns pausing.
    /// </summary>
    public interface ICutsceneInteractivePlayback : ICutscenePlayback
    {
        bool WantsGameplay { get; }
        bool IsComplete { get; }
        bool KeepsGameplayOnCompletion { get; }
        void CompleteIntoGameplay();
        void AdvanceInteractive(float seconds);
    }

    public sealed class CutsceneDefinition
    {
        public CutsceneDefinition(string id, string titleKey, Func<ICutscenePlayback> factory)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A cutscene needs an ID.", nameof(id));
            if (string.IsNullOrWhiteSpace(titleKey)) throw new ArgumentException("A cutscene needs a localized title.", nameof(titleKey));
            Id = id;
            TitleKey = titleKey;
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public string Id { get; }
        public string TitleKey { get; }
        internal Func<ICutscenePlayback> Factory { get; }
    }

    /// <summary>Explicit bounded registrations owned by one loaded level, never a global scene-name lookup.</summary>
    public sealed class CutsceneCatalog
    {
        public const int Capacity = 64;
        private readonly List<CutsceneDefinition> entries = new List<CutsceneDefinition>();
        public int Count => entries.Count;
        public CutsceneDefinition this[int index] => entries[index];

        public void Register(CutsceneDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (Find(definition.Id) != null) throw new ArgumentException("Duplicate cutscene ID.", nameof(definition));
            if (entries.Count >= Capacity) throw new InvalidOperationException("The level cutscene catalog is full.");
            entries.Add(definition);
        }

        public CutsceneDefinition Find(string id)
        {
            for (int i = 0; i < entries.Count; i++)
                if (string.Equals(entries[i].Id, id, StringComparison.Ordinal)) return entries[i];
            return null;
        }

        public void Clear() => entries.Clear();
    }
}
