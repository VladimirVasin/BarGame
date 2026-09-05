using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one thing a street walker ever says to him: a short insult on
    /// the last drunkenness stage, by the story bible's §6 registry row of
    /// 2026-09-05. Pure numbers and predicates; the controller owns the
    /// clock, the pick and the shared bubble view.
    ///
    /// Everything here is measured from the walker, the way the personal
    /// space is: how close the hero has to be, how squarely the walker
    /// has to be looking his way, and how far he has to go away before
    /// the same walker may say something again. Nothing here reads what
    /// the hero says or sees — the trigger is his level, never his
    /// muttering, which is how §16.2 stays literally true.
    /// </summary>
    public static class CityPedestrianInsultRules
    {
        /// <summary>Planar reach from the walker, metres. Inside the
        /// notice cone he has already turned his head in (3.6 m), well
        /// outside the shove (0.75 m): a remark in passing, not a blow.</summary>
        public const float SpeakDistance = 3f;

        /// <summary>The same walker says nothing more until the hero has
        /// gone this far. Past the attention release radius (4.2 m), so a
        /// hero drifting on the cone's edge is neither re-noticed nor
        /// re-insulted.</summary>
        public const float ReleaseDistance = 4.5f;

        /// <summary>Cosine gate on the walker's body facing: a man walking
        /// past sideways still counts, a back turned does not.</summary>
        public const float MinimumFacingDot = 0.2f;

        /// <summary>Between two lines from anybody on the street, from the
        /// moment one opens. Twice the bubble's own life.</summary>
        public const float CooldownSeconds = 8f;

        /// <summary>After the shared view refused a speaker or a line:
        /// try again soon, and never charge the walker for it.</summary>
        public const float RetryDelaySeconds = 0.5f;

        /// <summary>
        /// Street copies that keep their silence. The mourner's design is
        /// a woman in deep mourning: the bible keeps her mute by name, and
        /// her anonymous copy on the promenade stays mute with her.
        /// </summary>
        public static readonly string[] SilentDesignIds =
        {
            CityPedestrianResources.MournerDesignId
        };

        /// <summary>The last stage only: «В стельку», level 81 and up.</summary>
        public static bool IsInsultStage(int level)
        {
            return IntoxicationStageRules.GetStage(level) ==
                   IntoxicationStage.VeryDrunk;
        }

        public static bool MaySpeak(string designId)
        {
            if (string.IsNullOrEmpty(designId))
            {
                return false;
            }

            for (int index = 0; index < SilentDesignIds.Length; index++)
            {
                if (string.Equals(
                        SilentDesignIds[index],
                        designId,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether a body facing <paramref name="forward"/> at
        /// <paramref name="from"/> is looking roughly toward
        /// <paramref name="to"/>, in the ground plane.</summary>
        public static bool IsFacing(Vector3 forward, Vector3 from, Vector3 to)
        {
            Vector3 offset = to - from;
            offset.y = 0f;
            forward.y = 0f;
            if (offset.sqrMagnitude <= 0.0001f ||
                forward.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            return Vector3.Dot(forward.normalized, offset.normalized) >
                   MinimumFacingDot;
        }

        /// <summary>Planar distance, for the rearm rule.</summary>
        public static float PlanarDistance(Vector3 first, Vector3 second)
        {
            first.y = 0f;
            second.y = 0f;
            return Vector3.Distance(first, second);
        }
    }
}
