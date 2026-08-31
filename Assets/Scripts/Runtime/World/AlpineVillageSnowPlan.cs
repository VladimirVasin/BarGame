using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The lying snow of the whole bowl, as one pure depth field.
    ///
    /// The village stands under a permanent gale and the ground under it was
    /// flat: the lane and the paths read only by material, so a route worn by
    /// feet looked exactly like untouched field except in colour.
    ///
    /// THE FIELD IS THE DEEP THING AND THE ROUTES ARE THE HOLES IN IT. Depth
    /// is zero on trodden ground and rises with distance from it until it
    /// reaches <see cref="UntouchedDepth"/>, and from there it stays - so the
    /// street and every path read as trenches worn down into knee-deep snow
    /// rather than as ribbons with banks laid along them. That direction is
    /// also what keeps it canon: the art bible's «Следы жизни» says the snow
    /// here is not raked into a wall the way it is on the terminal plateau,
    /// it is TRODDEN, and a field with holes worn in it is exactly that
    /// sentence in geometry. The gale still writes itself into the shape, but
    /// as the RUN each face takes to reach full depth rather than as two
    /// different depths - the far field is one depth because that is what a
    /// field is.
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
        /// <summary>
        /// How deep the lying snow is where nothing has walked. Knee-high on
        /// the hero, which is as deep as wading can look right without him
        /// needing to swim.
        ///
        /// IT IS A FLOOR THE FIELD RISES TO AND STAYS AT, not a crest it
        /// passes through. The first cut of this had the profile rise to a
        /// lip and die back to nothing over three metres, because the snow
        /// only existed beside the routes; that reads as a drift laid along a
        /// kerb rather than as a village standing in deep snow. Depth now
        /// only ever increases with distance from trodden ground, so the
        /// street and every path read as what they are - trenches worn down
        /// into a field that is knee-deep everywhere else.
        /// </summary>
        internal const float UntouchedDepth = 0.45f;

        /// <summary>
        /// How far from the trodden edge the snow reaches full depth on the
        /// face the gale unloads into - it packs right up against the worn
        /// ground - and on the face it scours, where it is pushed back.
        ///
        /// This is where the asymmetry lives now. It used to be two crest
        /// HEIGHTS, which cannot survive a profile that has to keep rising:
        /// a windward face that tops out lower than the field would need to
        /// come back DOWN somewhere, and there is nothing for it to come down
        /// to. Two rise RUNS say the same thing about the same wind and leave
        /// the far field one depth, which is what it physically is.
        /// </summary>
        internal const float LeeRiseRun = 1.3f;

        internal const float WindwardRiseRun = 3.2f;

        /// <summary>
        /// How far out the fitted ribbon along each route carries before the
        /// coarse field sheet takes over. Comfortably past the longest rise,
        /// so everything that VARIES is on the ribbon - which follows the
        /// route exactly - and the sheet only ever carries flat full depth.
        /// </summary>
        internal const float RibbonReach = 4.5f;

        /// <summary>
        /// Grid pitch of the field sheet. It carries no detail - the depth is
        /// already saturated out there and only <see cref="Variation"/> moves
        /// it, on `15 m` waves - so this is as coarse as the undulation can
        /// afford rather than as fine as a footprint would want.
        /// </summary>
        internal const float FieldCellSize = 1f;

        /// <summary>
        /// How far the sheet is drawn under its own true depth. The ribbons
        /// overlap it by a cell, and both are at full depth there, so without
        /// this the two surfaces z-fight along every route. Buried, the
        /// fitted ribbon always wins the overlap and the sheet is never the
        /// visible one where it matters.
        /// </summary>
        internal const float FieldBurial = 0.05f;

        /// <summary>Longitudinal pitch the path ribbons are re-sampled at.
        /// The lane uses its own `1 m` plan samples, which already carry a
        /// width and a right vector.</summary>
        internal const float PathSampleStep = 0.8f;

        /// <summary>
        /// How far a zero-depth edge is sunk under the ground it meets.
        ///
        /// Unlike the ground's two submeshes, this is an independently
        /// sampled mesh and cannot share exact boundary vertices with the
        /// terrain. A coplanar edge can therefore open a crawling hairline
        /// along a path even though both materials use `Ps1Lit`. Buried, the
        /// ground always wins that narrow seam.
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
        /// How far out the snow takes to reach full depth on a face of this
        /// exposure.
        /// </summary>
        internal static float RiseRun(float exposure)
        {
            return Mathf.Lerp(
                WindwardRiseRun,
                LeeRiseRun,
                Mathf.Clamp01(exposure));
        }

        /// <summary>
        /// The four cross-section offsets, measured outward from the trodden
        /// edge: buried inner toe, two along the rise, and the outer edge
        /// where the field sheet takes over. Both of the middle two sit
        /// inside the rise, because that is the only part of the profile that
        /// bends - past it the depth is flat and a vertex there buys nothing.
        /// </summary>
        internal static void CrossSection(
            float exposure,
            out float toe,
            out float near,
            out float far,
            out float edge)
        {
            float rise = RiseRun(exposure);
            toe = AlpineVillagePathPlanner.BareSkirtHalfWidth;
            near = toe + rise * 0.35f;
            far = toe + rise;
            edge = Mathf.Max(RibbonReach, far + FieldCellSize);
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
            float profile = SmoothRange(0f, RiseRun(exposure), past);
            if (profile <= 0f)
            {
                return 0f;
            }

            float suppression = MeasureSuppression(plan, point);
            if (suppression <= 0f)
            {
                return 0f;
            }

            return UntouchedDepth * profile * Variation(point) * suppression;
        }

        /// <summary>
        /// Everything that keeps snow off the ground, as one multiplier.
        /// Every predicate here already existed; none of them is re-derived.
        /// </summary>
        private static float MeasureSuppression(
            AlpineVillagePlan plan,
            Vector2 point)
        {
            // The bowl's rim. Snow drawn on a `74°` wall is a sheet hanging
            // in the air, so it stops before the rise - but it has to FADE
            // there now rather than stop dead. When the profile died back to
            // nothing on its own a hard cut at the standoff was invisible;
            // a field that is knee-deep right up to the rim would end in a
            // `0.45 m` cliff ringing the whole village.
            float rimWeight = 1f - SmoothRange(
                0f,
                AlpineVillageTerrainSampler.RidgeStandoff,
                DistanceOutsideRect(plan.TerrainBounds, point));
            if (rimWeight <= 0f)
            {
                return 0f;
            }

            float weight = rimWeight * SmoothRange(
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
        /// <summary>How far the point lies outside the rectangle, on the
        /// ground plane. Zero anywhere inside it.</summary>
        private static float DistanceOutsideRect(Rect rect, Vector2 point)
        {
            float x = Mathf.Max(
                0f,
                Mathf.Max(rect.xMin - point.x, point.x - rect.xMax));
            float y = Mathf.Max(
                0f,
                Mathf.Max(rect.yMin - point.y, point.y - rect.yMax));
            return Mathf.Sqrt(x * x + y * y);
        }

        private static float SmoothRange(float start, float end, float value)
        {
            return Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(start, end, value));
        }
    }
}
