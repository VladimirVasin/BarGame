using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The snow lying beside the trodden routes, as one pure depth field.
    ///
    /// The village stands under a permanent gale and the ground under it was
    /// flat: the lane and the paths read only by material, so a route worn by
    /// feet looked exactly like untouched field except in colour. This gives
    /// the snow a thickness, and it gives it where snow actually collects -
    /// against the one discontinuity the place has.
    ///
    /// IT IS A DRIFT AND NOT A BANK, and that distinction is canon rather
    /// than taste: the art bible's «Следы жизни» says the snow here is not
    /// raked into a wall the way it is on the terminal plateau - it is
    /// trodden. So the profile rises over a metre and takes THREE to die
    /// away. A crest without that tail is a kerb, and a kerb is the thing
    /// the bible refuses.
    ///
    /// The field is visual only. Nothing here is read by
    /// <see cref="AlpineVillageTerrainSampler.SampleHeight"/>, by the walkable
    /// mask or by any collider: the hero walks on the same flat ground he
    /// always did and the snow closes over his shins. That is deliberate -
    /// planar velocity is read back from achieved movement, so ground he can
    /// catch a boot on reads as a crawl.
    /// </summary>
    internal static class AlpineVillageSnowDrift
    {
        /// <summary>Full depth where the gale unloads. Knee-high on the
        /// hero, which is as deep as wading can look right without him
        /// needing to swim.</summary>
        internal const float LeeCrestHeight = 0.45f;

        /// <summary>And what is left where it scours. Never zero: the
        /// windward lip of a trodden trough still carries a lip.</summary>
        internal const float WindwardCrestHeight = 0.18f;

        internal const float LeeCrestOffset = 1.3f;
        internal const float WindwardCrestOffset = 1f;

        /// <summary>
        /// How far the drift takes to die back into the field. It is roughly
        /// three times the rise on purpose - that ratio IS the difference
        /// between a drift and a shovelled bank, and shortening it is how
        /// this becomes a kerb along every path.
        /// </summary>
        internal const float LeeTailRun = 3.5f;

        internal const float WindwardTailRun = 2f;

        /// <summary>Longitudinal pitch the path ribbons are re-sampled at.
        /// The lane uses its own `1 m` plan samples, which already carry a
        /// width and a right vector.</summary>
        internal const float PathSampleStep = 0.8f;

        /// <summary>
        /// How far a zero-depth edge is sunk under the ground it meets.
        ///
        /// The same reason the ground mesh buries its own toe ring by
        /// <c>SeamBurial</c>: `Ps1Lit` snaps clip position, two meshes snap
        /// differently, and a coplanar edge opens a crawling hairline along
        /// every path. Buried, the ground always wins the seam.
        /// </summary>
        internal const float ToeBurial = 0.06f;

        /// <summary>
        /// How far outside a door apron or the station shelf the snow is back
        /// to full depth. Nothing may lie on a threshold: a dock is refused
        /// silently past `2 cm` of vertical tolerance, and knee-deep snow
        /// drawn over one is a bug report about a door that does not work.
        /// </summary>
        internal const float ApronClearance = 1.2f;

        /// <summary>How much the crest wanders along a route. Without it the
        /// profile reads as extruded moulding rather than as weather.
        /// </summary>
        internal const float CrestVariation = 0.3f;

        /// <summary>
        /// The wind the drifts were laid by.
        ///
        /// Deliberately NOT <c>GameWeatherRules.EvaluateCurrentWind</c>: that
        /// direction is hashed per weather slot and swings the full circle
        /// over a session, while a drift is weeks old. The bowl's air runs
        /// down the slope and out over the cableway brink - the one opening
        /// it has - so the prevailing direction is the plan's own downhill.
        /// </summary>
        internal static Vector2 PrevailingWind(AlpineVillagePlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var downhill = new Vector2(-plan.Uphill.x, -plan.Uphill.z);
            return downhill.sqrMagnitude <= 0.000001f
                ? Vector2.down
                : downhill.normalized;
        }

        /// <summary>
        /// `0` on the face the gale scours, `1` on the face it unloads into.
        /// The argument is the outward normal of the trodden edge, so the
        /// side pointing downwind is the side that fills.
        /// </summary>
        internal static float MeasureExposure(
            AlpineVillagePlan plan,
            Vector2 outward)
        {
            if (outward.sqrMagnitude <= 0.000001f)
            {
                return 0.5f;
            }

            return Mathf.InverseLerp(
                -1f,
                1f,
                Vector2.Dot(outward.normalized, PrevailingWind(plan)));
        }

        /// <summary>
        /// The four cross-section offsets, measured outward from the trodden
        /// edge: buried inner toe, crest, tail knee, buried outer toe.
        /// </summary>
        internal static void CrossSection(
            float exposure,
            out float toe,
            out float crest,
            out float knee,
            out float tail)
        {
            float clamped = Mathf.Clamp01(exposure);
            float crestOffset = Mathf.Lerp(
                WindwardCrestOffset,
                LeeCrestOffset,
                clamped);
            float tailRun = Mathf.Lerp(
                WindwardTailRun,
                LeeTailRun,
                clamped);
            toe = AlpineVillagePathPlanner.BareSkirtHalfWidth;
            crest = toe + crestOffset;
            knee = crest + tailRun * 0.45f;
            tail = crest + tailRun;
        }

        /// <summary>
        /// Depth of lying snow over the sampled ground. Zero on any trodden
        /// surface, on its bare skirt, over a threshold, on the station and
        /// anywhere the enclosing rise has started.
        /// </summary>
        internal static float SampleDepth(
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths,
            Vector2 point)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            float outside =
                AlpineVillagePathPlanner.MeasureDistanceOutsideTrodden(
                    plan,
                    paths,
                    point,
                    out Vector2 outward);
            float past = outside -
                         AlpineVillagePathPlanner.BareSkirtHalfWidth;
            if (past <= 0f)
            {
                return 0f;
            }

            float exposure = MeasureExposure(plan, outward);
            float crestOffset = Mathf.Lerp(
                WindwardCrestOffset,
                LeeCrestOffset,
                exposure);
            float tailRun = Mathf.Lerp(
                WindwardTailRun,
                LeeTailRun,
                exposure);
            float crestHeight = Mathf.Lerp(
                WindwardCrestHeight,
                LeeCrestHeight,
                exposure);
            float profile = past <= crestOffset
                ? SmoothRange(0f, crestOffset, past)
                : 1f - SmoothRange(crestOffset, crestOffset + tailRun, past);
            if (profile <= 0f)
            {
                return 0f;
            }

            float suppression = MeasureSuppression(plan, point);
            if (suppression <= 0f)
            {
                return 0f;
            }

            return crestHeight * profile * Variation(point) * suppression;
        }

        /// <summary>
        /// Everything that keeps snow off the ground, as one multiplier.
        /// Every predicate here already existed; none of them is re-derived.
        /// </summary>
        private static float MeasureSuppression(
            AlpineVillagePlan plan,
            Vector2 point)
        {
            // Past the standoff the mountain starts. There is no route out
            // there and nothing to drift against, and snow drawn on a `74°`
            // wall is a sheet hanging in the air.
            if (AlpineVillageTerrainSampler.SampleRidgeRise(plan, point) > 0f)
            {
                return 0f;
            }

            float weight = SmoothRange(
                0f,
                ApronClearance,
                AlpineVillageTerrainSampler.DistanceOutsideStation(
                    plan.Station,
                    point));
            for (int index = 0;
                 index < plan.Plots.Count && weight > 0f;
                 index++)
            {
                weight = Mathf.Min(
                    weight,
                    SmoothRange(
                        0f,
                        ApronClearance,
                        AlpineVillageTerrainSampler.DistanceOutsidePlot(
                            plan.Plots[index],
                            point)));
            }

            return weight;
        }

        /// <summary>
        /// Wander, in world space rather than along a route, so two drifts
        /// meeting at a junction agree about the ground they share.
        /// </summary>
        private static float Variation(Vector2 point)
        {
            float wave =
                Mathf.Sin(point.x * 0.41f + point.y * 0.23f) * 0.6f +
                Mathf.Sin(point.x * -0.17f + point.y * 0.37f) * 0.4f;
            return Mathf.Clamp(1f + CrestVariation * wave, 0.35f, 1.4f);
        }

        /// <summary>
        /// `0` at <paramref name="start"/>, `1` at <paramref name="end"/>,
        /// eased between.
        ///
        /// This is the sampler's own <c>SmoothRange</c> and NOT the bare
        /// `Mathf.SmoothStep(start, end, value)`: Unity's third argument is a
        /// `0-1` fraction, not a distance, so that form returns metres and
        /// saturates at one metre of input. It ran the village's shelf
        /// blends at a tenth of their named width until it was found - see
        /// <see cref="AlpineVillageTerrainSampler.ShelfBlendDistance"/>.
        /// </summary>
        private static float SmoothRange(float start, float end, float value)
        {
            return Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(start, end, value));
        }
    }
}
