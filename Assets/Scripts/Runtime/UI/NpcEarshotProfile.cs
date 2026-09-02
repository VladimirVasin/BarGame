using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// How far away a line can still be read and heard.
    ///
    /// This policy used to live inside <c>CityParkQuarrelController</c>,
    /// which worked only because the two men it served sit at the same
    /// table: the bubble panel carried ONE opacity for the whole view,
    /// so two speakers standing at different distances were not
    /// expressible. Now every speaker declares a profile and every
    /// bubble fades on its own anchor.
    ///
    /// Three radii rather than two. Between <see cref="FaintRadiusMeters"/>
    /// and <see cref="CullRadiusMeters"/> a line stays faint but legible,
    /// which is what invites the player to walk over; past the cull
    /// radius it is not drawn at all and makes no sound. «Faint» and
    /// «absent» are different states and the park needs both.
    ///
    /// The curve is the one number the whole system spends twice: the
    /// bubble spends it on alpha, and both channels spend it on blip
    /// volume.
    /// </summary>
    public readonly struct NpcEarshotProfile
    {
        /// <summary>Inside this, the two men are close enough that the
        /// line is solid.</summary>
        public const float ShoutSolidRadiusMeters = 11f;

        /// <summary>
        /// The park's earshot: a shout across a path. Widened from `22`
        /// on 2026-09-03 — the first build put the far edge of the fade
        /// exactly where the quarrel's own engage gate is, so a line
        /// was at its faintest over the whole approach. The gate itself
        /// did NOT move; see <c>CityParkQuarrelController</c>.
        /// </summary>
        public const float ShoutFaintRadiusMeters = 26f;

        /// <summary>Past this the bubble is gone. Wider than the
        /// quarrel's own silence radius on purpose, so the two men stop
        /// arguing before their words start disappearing rather than
        /// the other way round.</summary>
        public const float ShoutCullRadiusMeters = 30f;

        /// <summary>A man answering somebody standing in front of him.
        /// The interaction radius is `1.65 m`, so five metres is still
        /// «right here».</summary>
        public const float ConversationSolidRadiusMeters = 5f;

        /// <summary>
        /// Widened from `7` on 2026-09-03. Seven metres is four or five
        /// paces: an answer went from full strength to gone inside the
        /// time it takes to turn round and walk off, which read as the
        /// line being cut rather than left behind.
        /// </summary>
        public const float ConversationFaintRadiusMeters = 13f;

        public const float RoomSolidRadiusMeters = 8f;

        /// <summary>
        /// Bounded by a measurement rather than chosen. The mountain
        /// cafe's footprint from
        /// <c>MountainRoadTerminalPlanner.CreateCafe</c> is `9.8 x 10 m`
        /// and its diagonal is `14.0 m`, so nothing shorter than that
        /// can promise a line spoken at the counter still reads from
        /// every corner of the room — and the story bible's §6 registry
        /// requires exactly that, «внутри физического объёма кафе».
        /// `18` puts the far corner inside the fade rather than at the
        /// very end of it, which is what the diagonal alone did.
        /// RECOMPUTE THE FLOOR if the cafe footprint ever changes.
        /// The cafe's own `ContainsInterior` gate is what keeps this
        /// generous radius from leaking through the wall.
        /// </summary>
        public const float RoomFaintRadiusMeters = 18f;

        /// <summary>How solid a line is at the far edge of earshot.
        /// Legible, but plainly not addressed to you.</summary>
        public const float DefaultFaintOpacity = 0.35f;

        public NpcEarshotProfile(
            float solidRadiusMeters,
            float faintRadiusMeters,
            float cullRadiusMeters,
            float faintOpacity)
        {
            SolidRadiusMeters = Mathf.Max(0f, solidRadiusMeters);
            FaintRadiusMeters = Mathf.Max(
                SolidRadiusMeters,
                faintRadiusMeters);
            CullRadiusMeters = Mathf.Max(
                FaintRadiusMeters,
                cullRadiusMeters);
            FaintOpacity = Mathf.Clamp01(faintOpacity);
        }

        public float SolidRadiusMeters { get; }
        public float FaintRadiusMeters { get; }
        public float CullRadiusMeters { get; }
        public float FaintOpacity { get; }

        /// <summary>Two men calling each other names across a path.
        /// </summary>
        public static NpcEarshotProfile Shout =>
            new NpcEarshotProfile(
                ShoutSolidRadiusMeters,
                ShoutFaintRadiusMeters,
                ShoutCullRadiusMeters,
                DefaultFaintOpacity);

        /// <summary>Somebody answering the man in front of him. The
        /// cull radius equals the faint radius: an answer has no
        /// business being half-visible from across the shore.</summary>
        public static NpcEarshotProfile Conversation =>
            new NpcEarshotProfile(
                ConversationSolidRadiusMeters,
                ConversationFaintRadiusMeters,
                ConversationFaintRadiusMeters,
                DefaultFaintOpacity);

        /// <summary>A private conversation inside one room.</summary>
        public static NpcEarshotProfile Room =>
            new NpcEarshotProfile(
                RoomSolidRadiusMeters,
                RoomFaintRadiusMeters,
                RoomFaintRadiusMeters,
                DefaultFaintOpacity);

        /// <summary>
        /// How solid a line is at this distance. Smoothstep between the
        /// two radii, the park's shipped curve, plus a hard zero past
        /// the cull radius.
        ///
        /// A NaN distance returns the faint value rather than zero:
        /// whatever went wrong upstream, a line that was said should
        /// not vanish silently.
        /// </summary>
        public float ResolveOpacity(float distanceMeters)
        {
            if (float.IsNaN(distanceMeters))
            {
                return FaintOpacity;
            }

            if (distanceMeters > CullRadiusMeters)
            {
                return 0f;
            }

            float approach = Mathf.InverseLerp(
                FaintRadiusMeters,
                SolidRadiusMeters,
                distanceMeters);
            approach = approach * approach * (3f - 2f * approach);
            return Mathf.Lerp(FaintOpacity, 1f, approach);
        }

        /// <summary>Whether a line at this distance exists at all.
        /// </summary>
        public bool IsAudible(float distanceMeters)
        {
            return ResolveOpacity(distanceMeters) > 0f;
        }
    }
}
