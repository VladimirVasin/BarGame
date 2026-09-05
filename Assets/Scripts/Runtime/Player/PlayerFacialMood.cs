using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What the moment asks of the face, over and above the drink's own
    /// level and the idle blinks: the strain of a fight for balance, the
    /// wince of the floor, the blank of the lie, the nod of the drink.
    /// </summary>
    public enum PlayerFacialMood
    {
        None = 0,
        Tense,
        Grimace,

        /// <summary>Eyes shut and staying shut: out cold on the floor.</summary>
        Out,
        Drowsy
    }

    /// <summary>Everything the presentation knows that bears on the face this frame.</summary>
    public readonly struct PlayerFacialMoodContext
    {
        public PlayerFacialMoodContext(
            float intoxication,
            BalancePhase phase,
            float braceWeight,
            float instability,
            bool ragdollActive,
            float ragdollSeconds,
            bool riseActive,
            PlayerRiseStage riseStage,
            float riseStageProgress,
            bool slumpActive,
            float nausea = 0f)
        {
            Intoxication = Mathf.Clamp01(intoxication);
            Phase = phase;
            BraceWeight = Mathf.Clamp01(braceWeight);
            Instability = Mathf.Clamp01(instability);
            RagdollActive = ragdollActive;
            RagdollSeconds = Mathf.Max(0f, ragdollSeconds);
            RiseActive = riseActive;
            RiseStage = riseStage;
            RiseStageProgress = Mathf.Clamp01(riseStageProgress);
            SlumpActive = slumpActive;
            Nausea = float.IsNaN(nausea) ? 0f : Mathf.Clamp01(nausea);
        }

        public float Intoxication { get; }
        public BalancePhase Phase { get; }
        public float BraceWeight { get; }
        public float Instability { get; }
        public bool RagdollActive { get; }
        public float RagdollSeconds { get; }
        public bool RiseActive { get; }
        public PlayerRiseStage RiseStage { get; }
        public float RiseStageProgress { get; }
        public bool SlumpActive { get; }

        /// <summary>How far the hand is up at the mouth in a nausea bout, 0..1.</summary>
        public float Nausea { get; }
    }

    /// <summary>The pure rules of the mood: the same context always gives the same face.</summary>
    public static class PlayerFacialMoodRules
    {
        /// <summary>The wince of hitting the floor lasts this long; then he is out.</summary>
        public const float RagdollGrimaceSeconds = 0.6f;

        /// <summary>The first half of the stir is a grimace, the rest a strain.</summary>
        public const float StirringGrimaceFraction = 0.5f;

        /// <summary>A recovering stagger this unsteady shows on the face.</summary>
        public const float RecoveringTenseInstability = 0.5f;

        /// <summary>Standing up from a fall at this level or more, the drowse is back at once.</summary>
        public const float DrowsyStandingLevel = 0.6f;

        /// <summary>With the hand this far up at the mouth, the face is a grimace.</summary>
        public const float NauseaGrimaceWeight = 0.5f;

        public static PlayerFacialMood Resolve(in PlayerFacialMoodContext context)
        {
            if (context.RagdollActive)
            {
                return context.RagdollSeconds < RagdollGrimaceSeconds
                    ? PlayerFacialMood.Grimace
                    : PlayerFacialMood.Out;
            }

            if (context.RiseActive)
            {
                switch (context.RiseStage)
                {
                    case PlayerRiseStage.Settling:
                    case PlayerRiseStage.Stunned:
                        return PlayerFacialMood.Out;
                    case PlayerRiseStage.Stirring:
                        return context.RiseStageProgress < StirringGrimaceFraction
                            ? PlayerFacialMood.Grimace
                            : PlayerFacialMood.Tense;
                    case PlayerRiseStage.PushingUp:
                        return context.SlumpActive
                            ? PlayerFacialMood.Grimace
                            : PlayerFacialMood.Tense;
                    case PlayerRiseStage.Crawling:
                        return PlayerFacialMood.Grimace;
                    case PlayerRiseStage.Kneeling:
                        return PlayerFacialMood.Tense;
                    case PlayerRiseStage.Standing:
                        return context.Intoxication >= DrowsyStandingLevel
                            ? PlayerFacialMood.Drowsy
                            : PlayerFacialMood.None;
                    default:
                        return PlayerFacialMood.None;
                }
            }

            switch (context.Phase)
            {
                case BalancePhase.Toppling:
                case BalancePhase.Fallen:
                    return context.BraceWeight > 0.01f
                        ? PlayerFacialMood.Grimace
                        : PlayerFacialMood.Tense;
                case BalancePhase.Recovering:
                    if (context.Instability > RecoveringTenseInstability)
                    {
                        return PlayerFacialMood.Tense;
                    }

                    break;
            }

            // The nausea: while the hand is up at the mouth the face is a
            // grimace. Only on his feet and steady enough — every face a
            // fall asks for comes before it.
            return context.Nausea >= NauseaGrimaceWeight
                ? PlayerFacialMood.Grimace
                : PlayerFacialMood.None;
        }
    }
}
