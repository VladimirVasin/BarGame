using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Who is talking: where the sound comes from, what he sounds like,
    /// and how far away he can still be heard. Ambient and interaction
    /// speech share this identity and the same head-bubble presentation.
    ///
    /// <see cref="None"/> is narration — a description of what the hero
    /// is looking at, the locked door, the cashier who does not blink.
    /// Those are not somebody talking, so they neither type nor tick.
    /// </summary>
    public readonly struct NpcSpeaker
    {
        public NpcSpeaker(
            Object owner,
            Transform anchor,
            string designId,
            in NpcEarshotProfile earshot)
            : this(owner, anchor, designId, earshot, NpcVoiceCatalog.ResolveGender(designId))
        {
        }

        public NpcSpeaker(
            Object owner,
            Transform anchor,
            string designId,
            in NpcEarshotProfile earshot,
            RussianVocalizerGender gender)
            : this(owner, anchor, designId, earshot, gender, ResolveVoiceIdentity(owner, designId))
        {
        }

        private NpcSpeaker(Object owner, Transform anchor, string designId,
            in NpcEarshotProfile earshot, RussianVocalizerGender gender, string voiceIdentity)
        {
            Owner = owner;
            Anchor = anchor;
            DesignId = designId ?? string.Empty;
            VoiceOrdinal = NpcVoiceCatalog.ResolveOrdinal(voiceIdentity, gender);
            Earshot = earshot;
        }

        private static string ResolveVoiceIdentity(Object owner, string designId)
        {
            // A shared male model is appearance metadata, not the person's
            // voice identity. Preserve explicit staged voice aliases unchanged.
            if (designId == DefaultNpcCatalog.OrdinaryWorker)
            {
                CityPedestrianDefaultNpcBody person = owner is Component component
                    ? component.GetComponent<CityPedestrianDefaultNpcBody>()
                    : owner is GameObject gameObject
                        ? gameObject.GetComponent<CityPedestrianDefaultNpcBody>() : null;
                if (person != null && !string.IsNullOrEmpty(person.CharacterId))
                    return person.CharacterId;
            }
            return designId;
        }

        /// <summary>The reference that owns a bubble slot. A reference
        /// rather than an index so two speakers can never end up
        /// sharing one.</summary>
        public Object Owner { get; }

        /// <summary>Usually a live head bone, so the line rides with
        /// the man rather than with where he was standing.</summary>
        public Transform Anchor { get; }

        public string DesignId { get; }

        public int VoiceOrdinal { get; }

        public NpcEarshotProfile Earshot { get; }

        public bool IsValid =>
            Owner != null &&
            VoiceOrdinal >= 0 &&
            VoiceOrdinal < NpcVoiceCatalog.Count;

        public NpcVoiceProfile Voice =>
            NpcVoiceCatalog.ProfileAt(VoiceOrdinal);

        public RussianVocalizerGender Gender => Voice.Gender;

        /// <summary>Nobody. Narration and the hero's own prompts.
        /// </summary>
        public static NpcSpeaker None => default;

        /// <summary>
        /// The usual construction: a staged NPC already carries both
        /// halves of its own identity on its asset registry — the head
        /// bone the line hangs off, and the design id the voice is
        /// chosen by.
        /// </summary>
        public static NpcSpeaker FromRegistry(
            Object owner,
            CityPedestrianAssetRegistry registry,
            in NpcEarshotProfile earshot)
        {
            if (registry == null) return None;
            CityPedestrianDefaultNpcBody person = registry.GetComponent<CityPedestrianDefaultNpcBody>();
            string identity = person != null && !string.IsNullOrEmpty(person.CharacterId)
                ? person.CharacterId : registry.DesignId;
            return new NpcSpeaker(owner, registry.HeadAnchor, registry.DesignId, earshot,
                NpcVoiceCatalog.ResolveGender(registry.DesignId), identity);
        }

        /// <summary>
        /// Where the sound is made. The head bone when there is one;
        /// otherwise the fallback the caller measured, because a line
        /// with no anchor should still come from the right end of the
        /// room rather than from the world origin.
        /// </summary>
        public Vector3 ResolvePosition(Vector3 fallback)
        {
            return Anchor != null ? Anchor.position : fallback;
        }

        public float ResolveDistance(
            Transform listener,
            Vector3 fallback)
        {
            return listener == null
                ? 0f
                : Vector3.Distance(
                    listener.position,
                    ResolvePosition(fallback));
        }
    }
}
