using System;
using UnityEngine;

namespace BarPromenade
{
    public enum HomeShowerWashRegion
    {
        None = -1,
        Torso = 0,
        LeftArm = 1,
        RightArm = 2,
        LeftLeg = 3,
        RightLeg = 4,
        Intimate = 5
    }

    /// <summary>
    /// Cleaning comes from the held-button stroke's requested travel confirmed
    /// on the body in the same frame. Every reachable washing region contributes
    /// to one shared gauge; washing a single region can complete the action.
    /// </summary>
    public sealed class HomeShowerWashingProgress
    {
        // Stable body-region slots include the working right arm for body
        // collision classification; that slot cannot earn cleaning progress.
        public const int RegionCount = 6;
        public const float MinimumActiveSeconds = 10f;
        public const float MaximumCreditSpeed = 0.10f;
        public const float TotalRequiredDistance = MinimumActiveSeconds * MaximumCreditSpeed;
        public const float MaximumSampleSeconds = 0.25f;
        public const float MaximumContactSampleDistance = 0.10f;
        public const float MaximumContactSpeed = 1f;

        private double cleanedDistance;

        public float CleanedDistance => (float)cleanedDistance;
        public float Amount => Mathf.Clamp01(CleanedDistance / TotalRequiredDistance);
        public bool Complete => cleanedDistance >= TotalRequiredDistance;

        /// <summary>The contact marker shows the same overall progress on any body region.</summary>
        public float GetRegionAmount(HomeShowerWashRegion region) => IsWashableRegion(region) ? Amount : 0f;

        public static bool IsWashableRegion(HomeShowerWashRegion region) =>
            (int)region >= 0 && (int)region < RegionCount && region != HomeShowerWashRegion.RightArm;

        /// <summary>
        /// Both distances are measured for this frame only. A paused frame,
        /// missed contact, hitch or implausible contact jump earns nothing;
        /// discarded input is never saved for a later successful contact.
        /// </summary>
        public float Credit(
            HomeShowerWashRegion region,
            float commandedTravelMetres,
            float actualContactTravelMetres,
            bool contact,
            float seconds)
        {
            if (!contact || !IsWashableRegion(region) ||
                !float.IsFinite(commandedTravelMetres) || commandedTravelMetres <= 0f ||
                !float.IsFinite(actualContactTravelMetres) || actualContactTravelMetres <= 0f ||
                !float.IsFinite(seconds) || seconds <= 0f || seconds > MaximumSampleSeconds ||
                actualContactTravelMetres > MaximumContactSampleDistance ||
                actualContactTravelMetres > MaximumContactSpeed * seconds)
            {
                return 0f;
            }

            double available = Math.Max(0d, TotalRequiredDistance - cleanedDistance);
            double measured = Mathf.Min(commandedTravelMetres,
                Mathf.Min(actualContactTravelMetres, seconds * MaximumCreditSpeed));
            double credited = Math.Min(available, measured);
            cleanedDistance += credited;
            return (float)credited;
        }

        public void Reset() => cleanedDistance = 0d;
    }
}
