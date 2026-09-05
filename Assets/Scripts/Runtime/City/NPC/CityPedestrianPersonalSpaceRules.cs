using UnityEngine;

namespace BarPromenade
{
    public enum CityPedestrianPersonalSpaceReaction
    {
        None,
        Guard,
        Shove
    }

    /// <summary>Physical personal space, independent of the story's poisoning scale.</summary>
    public static class CityPedestrianPersonalSpaceRules
    {
        public const float GuardDistance = 1f;
        // The two capsules already occupy 0.67 m; the shoulder-assisted
        // palm must bridge the remaining space rather than shrink collision.
        public const float ShoveDistance = 0.75f;
        public const float ContactDistance = 0.75f;
        public const float ReleaseDistance = 1.5f;
        public const float MaximumHeightDifference = 0.3f;
        public const float CooldownSeconds = 3f;
        public const float ContactTime = 1f / 3f;
        public const float Duration = 1f;
        public const float ShoveMetres = 0.4f;
        public const float ShoveSeconds = 0.3f;
        public const float BalanceImpulse = 0.65f;

        public static CityPedestrianPersonalSpaceReaction ReactionFor(int level)
        {
            switch (IntoxicationStageRules.GetStage(level))
            {
                case IntoxicationStage.Unsteady:
                    return CityPedestrianPersonalSpaceReaction.Guard;
                case IntoxicationStage.VeryDrunk:
                    return CityPedestrianPersonalSpaceReaction.Shove;
                default:
                    return CityPedestrianPersonalSpaceReaction.None;
            }
        }

        public static bool WithinReach(Vector3 origin, Vector3 target, float distance)
        {
            Vector3 offset = target - origin;
            if (Mathf.Abs(offset.y) > MaximumHeightDifference)
            {
                return false;
            }

            offset.y = 0f;
            return offset.sqrMagnitude > 0.0001f &&
                   offset.sqrMagnitude <= distance * distance;
        }

    }
}
