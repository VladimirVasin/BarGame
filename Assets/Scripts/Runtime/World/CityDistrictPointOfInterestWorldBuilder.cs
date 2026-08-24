using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public readonly struct CityPointOfInterestSoundGeometry
    {
        public CityPointOfInterestSoundGeometry(
            Bounds loopOwnerBounds,
            Bounds detailOwnerBounds)
        {
            LoopOwnerBounds = loopOwnerBounds;
            DetailOwnerBounds = detailOwnerBounds;
        }

        public Bounds LoopOwnerBounds { get; }
        public Bounds DetailOwnerBounds { get; }
        public Vector3 LoopPosition => LoopOwnerBounds.center;
        public Vector3 DetailPosition => DetailOwnerBounds.center;
    }

    /// <summary>
    /// Builds the four district points of interest as open public places.
    /// The layout owns their position and access contract; this builder owns
    /// only the physical surface and the free-standing visual recipes.
    /// </summary>
    public static class CityDistrictPointOfInterestWorldBuilder
    {
        public const string CityRootName =
            "District Points Of Interest";
        public const string HomeExteriorRootName =
            "Home Exterior District Points Of Interest";
        public const string PublicGroundName = "Public Ground";

        private const float ReferencePublicWidth = 15f;
        private const float PublicGroundHeight = 0.12f;
        private const float MinimumPublicGroundFoundationDepth = 0.14f;

        // The Ferryman's car, mirrored from
        // tools/build-last-route-car-3d-model.py so the recipe maths and the
        // authored FBX cannot disagree about how much room it needs. The bay
        // radius is the paving's own 5.40 plus the body's half-width plus a
        // walking gap, so the car stands just off the circle rather than on
        // it - the island's empty centre is authored, not incidental.
        private const float CarBayLength = 4.83f;
        private const float CarBayWidth = 1.80f;
        private const float CarBayClearance = 0.40f;
        private const float CarBayPavingRadius = 5.40f;
        private const float CarBayNoseAngleDegrees = 38f;
        // Right of the arriving hero first; the mirrored bay is the fallback
        // for when that side is taken by another way in.
        private static readonly float[] CarBaySidePreference = { 1f, -1f };
        // Nearest the street first - he waits at the entrance, not at the
        // back of the lot.
        private static readonly float[] CarBayRadialSteps =
            { 6.9f, 6.3f, 5.7f, 5.1f };
        private static readonly float[] CarBayLateralSteps =
            { 3.5f, 4.1f, 4.7f, 5.3f };

        // The two sittable benches, shared between the visual recipes
        // below and <see cref="TryDescribeBenchSeat"/> so the seat the
        // hero docks against is always the seat that was drawn.
        private const float DryingBenchX = -3.25f;
        private const float DryingBenchSeatCenterY = 0.53f;
        private const float DryingBenchZ = 4.45f;
        private const float DryingBenchWidth = 2.40f;
        private const float DryingBenchSeatThickness = 0.18f;
        private const float DryingBenchDepth = 0.58f;
        // The Soviet carpet-beating rack on the drying yard's west
        // strip, upwind of the hanging wash. The two carpet-beating
        // babushkas stand on opposite sides of it, each squared to her
        // own hung carpet; the smoking one watches from the east edge.
        // Shared between the visual recipe and
        // <see cref="TryDescribeBabushkaStances"/> so the NPCs always
        // face the carpets that were actually drawn.
        private const float CarpetRackX = -6.05f;
        private const float CarpetRackBarHeight = 1.62f;
        private const float CarpetRackZSouth = -1.35f;
        private const float CarpetRackZNorth = 1.55f;
        private const float CarpetSouthZ = -0.55f;
        private const float CarpetNorthZ = 0.75f;
        private static readonly Vector3 BeaterSouthStanceLocal =
            new Vector3(-5.28f, 0f, CarpetSouthZ);
        private static readonly Vector3 BeaterNorthStanceLocal =
            new Vector3(-6.82f, 0f, CarpetNorthZ);
        // The smoker's stroll corridor: between the rack and the west
        // drying-frame posts, clear of every hung cloth row, passing
        // both beaters. She walks it back and forth, gesturing.
        private static readonly Vector3 SmokerPathStartLocal =
            new Vector3(-3.95f, 0f, -2.40f);
        private static readonly Vector3 SmokerPathEndLocal =
            new Vector3(-3.95f, 0f, 2.60f);

        // The weighbridge's authored pair. The weigher stands north
        // of the scale mechanism, east of the deck, facing the
        // indicator face — beside the axis, never across it, so the
        // site keeps reading as a working instrument and not a
        // checkpoint. The weighed worker paces the deck's long axis
        // and stands still at its centre as if being weighed. Shared
        // between the visual recipe and
        // <see cref="TryDescribeWeighbridgeStances"/> so the NPCs
        // always match the deck and mechanism that were drawn.
        private static readonly Vector3 WeigherStanceLocal =
            new Vector3(3.05f, 0f, 1.60f);
        private static readonly Vector3 WeighedPathStartLocal =
            new Vector3(0f, 0f, -4.60f);
        private static readonly Vector3 WeighedPathEndLocal =
            new Vector3(0f, 0f, 4.60f);
        // Top of the walkable deck above the recipe root: box centre
        // 0.16 plus half its 0.22 height. Vertical, so never
        // multiplied by the horizontal recipe scale.
        private const float WeighbridgeDeckTopLocalY = 0.27f;
        private const float WeighbridgeDeckHalfWidth = 1.80f;
        private const float WeighbridgeDeckHalfLength = 5.80f;

        private const float IslandBenchX = 2.85f;
        private const float IslandBenchSeatCenterY = 0.66f;
        private const float IslandBenchZ = 2.55f;
        private const float IslandBenchWidth = 2.50f;
        private const float IslandBenchSeatThickness = 0.22f;
        private const float IslandBenchDepth = 0.72f;
        private const float IslandBenchYaw = 22f;

        private static readonly Color OldTownPaving =
            new Color(0.255f, 0.235f, 0.190f);
        private static readonly Color OldStone =
            new Color(0.285f, 0.245f, 0.185f);
        private static readonly Color OldMetal =
            new Color(0.095f, 0.125f, 0.120f);
        private static readonly Color OldRepairMetal =
            new Color(0.315f, 0.355f, 0.325f);
        private static readonly Color OldWater =
            new Color(0.055f, 0.130f, 0.145f);
        private static readonly Color AmberGlow =
            new Color(1.10f, 0.54f, 0.18f);

        private static readonly Color ResidentialPaving =
            new Color(0.235f, 0.275f, 0.270f);
        private static readonly Color ResidentialFrame =
            new Color(0.145f, 0.190f, 0.185f);
        private static readonly Color ResidentialCloth =
            new Color(0.405f, 0.245f, 0.185f);
        private static readonly Color ResidentialClothCold =
            new Color(0.225f, 0.350f, 0.375f);
        private static readonly Color ResidentialPatch =
            new Color(0.600f, 0.500f, 0.285f);
        private static readonly Color CarpetOxblood =
            new Color(0.360f, 0.135f, 0.120f);
        private static readonly Color CarpetTeal =
            new Color(0.140f, 0.230f, 0.220f);

        // The drying yard's pole floodlight: a cold near-white communal
        // fixture on the Residential cool axis, scaled by the shared
        // night factor rather than burning by day like the bar-side
        // yard spotlight.
        private static readonly Color FloodlightLightColor =
            new Color(0.72f, 0.84f, 0.92f);
        // The lens follows the bar-side yard spotlight's recipe: the
        // light colour boosted well past 1 so the source reads as a
        // burning fixture through the PS1 composite, not a pale plate.
        private static readonly Color FloodlightGlow =
            new Color(3.17f, 3.70f, 4.05f);
        // Street practicals run at 31 over a short drop; this beam
        // throws 7-12 m across the whole yard, so it needs floodlight
        // wattage (the always-on bar-side yard spot needs 240) for the
        // far drying row to reach street-lamp brightness through the
        // night grade and fog.
        private const float FloodlightNightIntensity = 150f;
        private const float FloodlightRange = 16f;
        private const float FloodlightSpotAngle = 72f;
        private const float FloodlightInnerSpotAngle = 40f;
        private const float FloodlightPoleX = 4.10f;
        private const float FloodlightPoleZ = 4.55f;
        private const float FloodlightHeadHeight = 4.28f;
        private static readonly Vector3 FloodlightAimTarget =
            new Vector3(0f, 1.30f, 0.20f);

        private static readonly Color IndustrialPaving =
            new Color(0.200f, 0.220f, 0.210f);
        private static readonly Color IndustrialSteel =
            new Color(0.175f, 0.205f, 0.205f);
        private static readonly Color IndustrialDark =
            new Color(0.070f, 0.090f, 0.095f);
        private static readonly Color IndustrialRust =
            new Color(0.390f, 0.205f, 0.095f);
        private static readonly Color IndustrialMarking =
            new Color(0.585f, 0.505f, 0.210f);
        private static readonly Color IndustrialGlow =
            new Color(0.380f, 0.700f, 0.690f);

        private static readonly Color NightlifePaving =
            new Color(0.170f, 0.175f, 0.215f);
        private static readonly Color NightlifeIsland =
            new Color(0.245f, 0.235f, 0.285f);
        private static readonly Color NightlifeFrame =
            new Color(0.085f, 0.090f, 0.125f);
        private static readonly Color NightlifeSeat =
            new Color(0.225f, 0.145f, 0.245f);
        private static readonly Color NightlifeRoutePaper =
            new Color(0.385f, 0.335f, 0.255f);
        private static readonly Color NightlifeRouteInk =
            new Color(0.105f, 0.125f, 0.155f);
        private static readonly Color NightlifePosterRed =
            new Color(0.355f, 0.135f, 0.165f);
        private static readonly Color NightlifePosterBlue =
            new Color(0.145f, 0.245f, 0.295f);
        private static readonly Color NightlifeWaste =
            new Color(0.115f, 0.135f, 0.145f);
        private static readonly Color NightlifeRagCanvas =
            new Color(0.335f, 0.305f, 0.245f);
        private static readonly Color NightlifeRagFadedRed =
            new Color(0.295f, 0.140f, 0.150f);
        private static readonly Color NightlifeRagFadedBlue =
            new Color(0.140f, 0.205f, 0.245f);

        // The last route island's service floodlight on the route
        // mast, under the broken totem: the one electric fixture the
        // abandoned island still runs. The district's magenta/cyan
        // stay in paint and paper; the light itself is a cold
        // violet-grey service white aimed at the empty centre and the
        // empty bench — it serves the emptiness, not a stage. Like
        // the drying yard's communal floodlight it is night-scaled,
        // shadowless and needs floodlight wattage for a 7-9 m throw
        // to survive the night grade, fog and PS1 composite.
        private static readonly Color IslandFloodlightLightColor =
            new Color(0.80f, 0.74f, 0.92f);
        private static readonly Color IslandFloodlightGlow =
            new Color(3.52f, 3.26f, 4.05f);
        private const float IslandFloodlightNightIntensity = 150f;
        private const float IslandFloodlightRange = 16f;
        private const float IslandFloodlightSpotAngle = 72f;
        private const float IslandFloodlightInnerSpotAngle = 40f;
        // Bracketed off the mast at (-2.75, -1.25) below the totem's
        // underside (y 4.78), reaching toward the island interior.
        private static readonly Vector3 IslandFloodlightHeadLocal =
            new Vector3(-2.40f, 4.42f, -1.05f);
        private static readonly Vector3 IslandFloodlightAimTarget =
            new Vector3(1.60f, 0.60f, 1.30f);

        /// <summary>Underside of the broken canopy roof slabs, where
        /// the torn rags hang from.</summary>
        private const float CanopyRagHangHeight = 3.49f;

        // Authored like the rest of the island: the POI recipes take
        // no seed, so the rag set is a fixed dressing rather than a
        // new randomization pattern.
        private static readonly CanopyRagRecipe[] CanopyRagRecipes =
        {
            new CanopyRagRecipe(
                0, -1.20f, 0.52f, 0.55f, 1.15f,
                NightlifeRagCanvas, 1, -6f),
            new CanopyRagRecipe(
                0, 0.85f, -0.48f, 0.42f, 0.90f,
                NightlifeRagFadedRed, 2, 8f),
            new CanopyRagRecipe(
                1, 0.30f, 0.50f, 0.62f, 1.05f,
                NightlifeRagFadedBlue, 3, -4f),
            new CanopyRagRecipe(
                2, -1.35f, -0.55f, 0.72f, 1.40f,
                NightlifeRagCanvas, 4, 5f),
            new CanopyRagRecipe(
                2, 1.10f, 0.45f, 0.38f, 0.85f,
                NightlifeRagFadedBlue, 5, -9f),
            new CanopyRagRecipe(
                4, -0.40f, 0.55f, 0.50f, 1.25f,
                NightlifeRagFadedRed, 6, 10f),
        };

        public static GameObject Build(
            Transform parent,
            CityLayout layout)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            Transform root = new GameObject(CityRootName).transform;
            root.SetParent(parent, false);
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                BuildCitySite(
                    root,
                    layout,
                    layout.DistrictPointsOfInterest[index]);
            }

            return root.gameObject;
        }

        public static GameObject BuildHomeExterior(
            Transform parent,
            HomeExteriorContextPlan context)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            Transform root = new GameObject(
                HomeExteriorRootName).transform;
            root.SetParent(parent, false);
            for (int index = 0;
                 index < context.NearbyDistrictPointsOfInterest.Count;
                 index++)
            {
                BuildHomeExteriorSite(
                    root,
                    context,
                    context.NearbyDistrictPointsOfInterest[index]);
            }

            return root.gameObject;
        }

        public static string GetSiteName(string id)
        {
            return $"District Point Of Interest {id}";
        }

        public static string GetRecipeName(
            CityDistrictPointOfInterestKind kind)
        {
            switch (kind)
            {
                case CityDistrictPointOfInterestKind
                    .OldTownWaterworksCourt:
                    return "Old Town Waterworks Court";
                case CityDistrictPointOfInterestKind
                    .ResidentialDryingYard:
                    return "Residential Drying Yard";
                case CityDistrictPointOfInterestKind
                    .IndustrialWeighbridge:
                    return "Industrial Weighbridge";
                case CityDistrictPointOfInterestKind
                    .NightlifeLastRouteIsland:
                    return "Nightlife Last Route Island";
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>
        /// Where the Ferryman's car stands: pulled up just inside a way in,
        /// off to the right of it, angled, with its nose pointing back out
        /// at whoever is arriving.
        ///
        /// That angle is the whole read. Parked square against the paving the
        /// car looks abandoned; parked nose-out at a quarter turn it looks
        /// like it is waiting to leave, which is what the man beside it is
        /// doing. It also means the hero meets its front on the way in
        /// instead of finding its back after walking round the island.
        ///
        /// Candidates are tried nearest-the-street first and REJECTED rather
        /// than nudged: a bay that touches an approach strip, leaves the lot,
        /// or reaches over the paved circle is out. If nothing on the right
        /// survives, the mirrored bay on the left is tried; if that fails too
        /// the car is simply absent - a visible loss is better than a blocked
        /// way in, and the walkable mask is built from rectangles that know
        /// nothing about props, so it would never have reported it.
        ///
        /// The car is placed in world space at uniform scale, unlike the
        /// recipe's own parts: the recipe root carries a non-uniform
        /// horizontal scale, and stretching an authored vehicle 8 % along XZ
        /// at unchanged height would show.
        /// </summary>
        public static bool TryDescribeFerrymanCarStance(
            CityDistrictPointOfInterestDescriptor descriptor,
            out CityDryingYardNpcStance stance)
        {
            stance = default;
            if (descriptor.Kind !=
                CityDistrictPointOfInterestKind.NightlifeLastRouteIsland)
            {
                return false;
            }

            float groundY = descriptor.Center.y + PublicGroundHeight * 0.5f;
            for (int index = 0; index < descriptor.Accesses.Count; index++)
            {
                CityDistrictPointOfInterestAccessDescriptor access =
                    descriptor.Accesses[index];
                Vector3 inward = new Vector3(
                    access.OutwardNormal.x,
                    0f,
                    access.OutwardNormal.z);
                if (inward.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                inward.Normalize();
                Vector3 arrivalRight = Vector3.Cross(Vector3.up, inward);
                foreach (float side in CarBaySidePreference)
                {
                    if (TryFitCarBay(
                            descriptor,
                            inward,
                            arrivalRight * side,
                            side,
                            groundY,
                            out stance))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Walks the candidate bays on one side of one way in, nearest the
        /// street first, and reports the first that clears everything.
        /// </summary>
        private static bool TryFitCarBay(
            CityDistrictPointOfInterestDescriptor descriptor,
            Vector3 inward,
            Vector3 lateral,
            float side,
            float groundY,
            out CityDryingYardNpcStance stance)
        {
            stance = default;

            // Nose out at a quarter turn, leaning across the way in rather
            // than away from it, so an arriving hero meets the front three
            // quarters on and the car could pull straight out to the road.
            float yaw = CarBayNoseAngleDegrees * side;
            Vector3 facing =
                (Quaternion.AngleAxis(yaw, Vector3.up) * -inward).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, facing);
            float halfLength = CarBayLength * 0.5f;
            float halfWidth = CarBayWidth * 0.5f;

            foreach (float radial in CarBayRadialSteps)
            {
                foreach (float offset in CarBayLateralSteps)
                {
                    Vector3 center = descriptor.Center -
                        inward * radial + lateral * offset;
                    center.y = groundY;
                    if (!IsCarBayClear(
                            descriptor,
                            center,
                            facing,
                            right,
                            halfLength,
                            halfWidth))
                    {
                        continue;
                    }

                    stance = new CityDryingYardNpcStance(center, facing);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A bay is clear when the whole car is inside the lot, off the paved
        /// circle, and out of every approach strip.
        /// </summary>
        private static bool IsCarBayClear(
            CityDistrictPointOfInterestDescriptor descriptor,
            Vector3 center,
            Vector3 facing,
            Vector3 right,
            float halfLength,
            float halfWidth)
        {
            float paddedLength = halfLength + CarBayClearance;
            float paddedWidth = halfWidth + CarBayClearance;

            Rect lot = descriptor.PublicBounds;
            foreach (Vector3 corner in EnumerateBayCorners(
                         center, facing, right, paddedLength, paddedWidth))
            {
                if (!lot.Contains(new Vector2(corner.x, corner.z)))
                {
                    return false;
                }
            }

            // The island's empty middle is authored, not incidental, so the
            // nearest point of the bodywork has to stay off the paving.
            Vector3 toCenter = descriptor.Center - center;
            float alongFacing = Mathf.Clamp(
                Vector3.Dot(toCenter, facing), -halfLength, halfLength);
            float alongRight = Mathf.Clamp(
                Vector3.Dot(toCenter, right), -halfWidth, halfWidth);
            Vector3 nearest = center + facing * alongFacing + right * alongRight;
            float pavingDistance = Vector2.Distance(
                new Vector2(nearest.x, nearest.z),
                new Vector2(descriptor.Center.x, descriptor.Center.z));
            if (pavingDistance < CarBayPavingRadius + CarBayClearance)
            {
                return false;
            }

            for (int index = 0; index < descriptor.Accesses.Count; index++)
            {
                if (OverlapsBay(
                        descriptor.Accesses[index].ApproachBounds,
                        center,
                        facing,
                        right,
                        paddedLength,
                        paddedWidth))
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<Vector3> EnumerateBayCorners(
            Vector3 center,
            Vector3 forward,
            Vector3 right,
            float halfLength,
            float halfWidth)
        {
            yield return center + forward * halfLength + right * halfWidth;
            yield return center + forward * halfLength - right * halfWidth;
            yield return center - forward * halfLength + right * halfWidth;
            yield return center - forward * halfLength - right * halfWidth;
        }

        private static bool OverlapsBay(
            Rect area,
            Vector3 center,
            Vector3 forward,
            Vector3 right,
            float halfLength,
            float halfWidth)
        {
            // The bay is oriented and the approach is axis-aligned, so test
            // each one's corners against the other's frame.
            foreach (Vector2 corner in new[]
                     {
                         new Vector2(area.xMin, area.yMin),
                         new Vector2(area.xMin, area.yMax),
                         new Vector2(area.xMax, area.yMin),
                         new Vector2(area.xMax, area.yMax)
                     })
            {
                Vector3 offset =
                    new Vector3(corner.x, center.y, corner.y) - center;
                if (Mathf.Abs(Vector3.Dot(offset, forward)) <= halfLength &&
                    Mathf.Abs(Vector3.Dot(offset, right)) <= halfWidth)
                {
                    return true;
                }
            }

            foreach (Vector3 corner in EnumerateBayCorners(
                         center, forward, right, halfLength, halfWidth))
            {
                if (area.Contains(new Vector2(corner.x, corner.z)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Describes the sittable bench seat one point of interest
        /// carries, in world space, mirroring the recipe transform the
        /// city build applies. Only the drying yard and the last route
        /// island keep a bench; every other kind reports none.
        /// </summary>
        public static bool TryDescribeBenchSeat(
            CityDistrictPointOfInterestDescriptor descriptor,
            out CityBenchSeat seat)
        {
            Vector3 localSeatCenter;
            Vector3 localSeatSize;
            float localYaw;
            string id;
            switch (descriptor.Kind)
            {
                case CityDistrictPointOfInterestKind
                    .ResidentialDryingYard:
                    localSeatCenter = new Vector3(
                        DryingBenchX,
                        DryingBenchSeatCenterY,
                        DryingBenchZ);
                    localSeatSize = new Vector3(
                        DryingBenchWidth,
                        DryingBenchSeatThickness,
                        DryingBenchDepth);
                    localYaw = 0f;
                    id = "drying-yard-shared-bench";
                    break;
                case CityDistrictPointOfInterestKind
                    .NightlifeLastRouteIsland:
                    localSeatCenter = new Vector3(
                        IslandBenchX,
                        IslandBenchSeatCenterY,
                        IslandBenchZ);
                    localSeatSize = new Vector3(
                        IslandBenchWidth,
                        IslandBenchSeatThickness,
                        IslandBenchDepth);
                    localYaw = IslandBenchYaw;
                    id = "last-route-island-empty-bench";
                    break;
                default:
                    seat = default;
                    return false;
            }

            Quaternion recipeRotation = Quaternion.LookRotation(
                ResolveForward(descriptor),
                Vector3.up);
            float horizontalScale =
                ResolveHorizontalScale(descriptor.PublicBounds);
            Vector3 worldSeatCenter = descriptor.Center +
                recipeRotation * new Vector3(
                    localSeatCenter.x * horizontalScale,
                    localSeatCenter.y,
                    localSeatCenter.z * horizontalScale);

            // Both authored benches face their recipe's local -Z: the
            // shared bench looks at the drying frames, the empty bench
            // looks back across the island.
            Vector3 faceDirection = recipeRotation *
                (Quaternion.Euler(0f, localYaw, 0f) *
                 Vector3.back);
            seat = new CityBenchSeat(
                id,
                new Vector3(
                    worldSeatCenter.x,
                    worldSeatCenter.y +
                    localSeatSize.y * 0.5f,
                    worldSeatCenter.z),
                localSeatSize.x * horizontalScale,
                localSeatSize.z * horizontalScale,
                descriptor.Center.y + PublicGroundHeight * 0.5f,
                faceDirection);
            return true;
        }

        /// <summary>
        /// Describes the three authored babushka stances the drying
        /// yard carries, in world space, mirroring the same recipe
        /// transform the city build applies — exactly like
        /// <see cref="TryDescribeBenchSeat"/>. The two beater stances
        /// face their own hung carpet across the rack; the smoker's
        /// stance is the start of her stroll corridor and
        /// <paramref name="smokerPathEnd"/> its far end. Every other
        /// kind reports none.
        /// </summary>
        public static bool TryDescribeBabushkaStances(
            CityDistrictPointOfInterestDescriptor descriptor,
            out CityDryingYardNpcStance beaterSouth,
            out CityDryingYardNpcStance beaterNorth,
            out CityDryingYardNpcStance smoker,
            out Vector3 smokerPathEnd)
        {
            if (descriptor.Kind !=
                CityDistrictPointOfInterestKind.ResidentialDryingYard)
            {
                beaterSouth = default;
                beaterNorth = default;
                smoker = default;
                smokerPathEnd = default;
                return false;
            }

            Quaternion recipeRotation = Quaternion.LookRotation(
                ResolveForward(descriptor),
                Vector3.up);
            float horizontalScale =
                ResolveHorizontalScale(descriptor.PublicBounds);
            float groundY = descriptor.Center.y +
                PublicGroundHeight * 0.5f;
            beaterSouth = CreateStance(
                descriptor,
                recipeRotation,
                horizontalScale,
                groundY,
                BeaterSouthStanceLocal,
                Vector3.left);
            beaterNorth = CreateStance(
                descriptor,
                recipeRotation,
                horizontalScale,
                groundY,
                BeaterNorthStanceLocal,
                Vector3.right);
            smokerPathEnd = ToStanceWorld(
                descriptor,
                recipeRotation,
                horizontalScale,
                groundY,
                SmokerPathEndLocal);
            Vector3 smokerStart = ToStanceWorld(
                descriptor,
                recipeRotation,
                horizontalScale,
                groundY,
                SmokerPathStartLocal);
            Vector3 towardEnd = smokerPathEnd - smokerStart;
            towardEnd.y = 0f;
            smoker = new CityDryingYardNpcStance(
                smokerStart,
                towardEnd.normalized);
            return true;
        }

        /// <summary>
        /// Describes the two authored weighbridge stances, in world
        /// space, mirroring the same recipe transform the city build
        /// applies — exactly like
        /// <see cref="TryDescribeBabushkaStances"/>. The weigher
        /// stands beside the mechanism facing the indicator; the
        /// weighed worker's stance is the near end of the deck-axis
        /// corridor and <paramref name="weighedPathEnd"/> its far
        /// end, both on the deck top. Every other kind reports none.
        /// </summary>
        public static bool TryDescribeWeighbridgeStances(
            CityDistrictPointOfInterestDescriptor descriptor,
            out CityDryingYardNpcStance weigher,
            out CityDryingYardNpcStance weighedWorker,
            out Vector3 weighedPathEnd)
        {
            if (descriptor.Kind !=
                CityDistrictPointOfInterestKind.IndustrialWeighbridge)
            {
                weigher = default;
                weighedWorker = default;
                weighedPathEnd = default;
                return false;
            }

            Quaternion recipeRotation = Quaternion.LookRotation(
                ResolveForward(descriptor),
                Vector3.up);
            float horizontalScale =
                ResolveHorizontalScale(descriptor.PublicBounds);
            float groundY = descriptor.Center.y +
                PublicGroundHeight * 0.5f;
            // The indicator face looks down local +Z, so its reader
            // stands north of it looking local -Z.
            weigher = CreateStance(
                descriptor,
                recipeRotation,
                horizontalScale,
                groundY,
                WeigherStanceLocal,
                Vector3.back);
            // The worker walks the deck top, not the public ground.
            float deckTopY = descriptor.Center.y +
                WeighbridgeDeckTopLocalY;
            weighedPathEnd = ToStanceWorld(
                descriptor,
                recipeRotation,
                horizontalScale,
                deckTopY,
                WeighedPathEndLocal);
            Vector3 workerStart = ToStanceWorld(
                descriptor,
                recipeRotation,
                horizontalScale,
                deckTopY,
                WeighedPathStartLocal);
            Vector3 towardEnd = weighedPathEnd - workerStart;
            towardEnd.y = 0f;
            weighedWorker = new CityDryingYardNpcStance(
                workerStart,
                towardEnd.normalized);
            return true;
        }

        /// <summary>
        /// Describes the weighbridge's walkable deck as a world-space
        /// oriented rectangle at deck-top height, for the needle
        /// controller's weight test. Every other kind reports none.
        /// </summary>
        public static bool TryDescribeWeighbridgeDeck(
            CityDistrictPointOfInterestDescriptor descriptor,
            out CityWeighbridgeDeckRect deck)
        {
            if (descriptor.Kind !=
                CityDistrictPointOfInterestKind.IndustrialWeighbridge)
            {
                deck = default;
                return false;
            }

            Quaternion recipeRotation = Quaternion.LookRotation(
                ResolveForward(descriptor),
                Vector3.up);
            float horizontalScale =
                ResolveHorizontalScale(descriptor.PublicBounds);
            deck = new CityWeighbridgeDeckRect(
                new Vector3(
                    descriptor.Center.x,
                    descriptor.Center.y + WeighbridgeDeckTopLocalY,
                    descriptor.Center.z),
                recipeRotation,
                WeighbridgeDeckHalfWidth * horizontalScale,
                WeighbridgeDeckHalfLength * horizontalScale);
            return true;
        }

        /// <summary>
        /// Returns the exact visible fixture bounds that own this point of
        /// interest's continuous and event sounds. The same recipe transform
        /// used to draw the objects is used here, so audio cannot drift away
        /// from the standpipe, rack, scale or route mast.
        /// </summary>
        public static bool TryDescribeSoundGeometry(
            CityDistrictPointOfInterestDescriptor descriptor,
            out CityPointOfInterestSoundGeometry geometry)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            Bounds loopLocal;
            Bounds detailLocal;
            switch (descriptor.Kind)
            {
                case CityDistrictPointOfInterestKind
                    .OldTownWaterworksCourt:
                    loopLocal = new Bounds(
                        new Vector3(0.55f, 1.98f, 0.40f),
                        new Vector3(1.08f, 3.20f, 1.08f));
                    detailLocal = new Bounds(
                        new Vector3(0.55f, 2.62f, 1.58f),
                        new Vector3(0.44f, 0.60f, 0.32f));
                    break;
                case CityDistrictPointOfInterestKind
                    .ResidentialDryingYard:
                    loopLocal = new Bounds(
                        new Vector3(0f, 2.30f, 0f),
                        new Vector3(9.30f, 1.50f, 6.30f));
                    detailLocal = new Bounds(
                        new Vector3(
                            CarpetRackX,
                            CarpetRackBarHeight * 0.55f,
                            (CarpetRackZSouth + CarpetRackZNorth) * 0.5f),
                        new Vector3(
                            0.25f,
                            CarpetRackBarHeight * 1.10f,
                            CarpetRackZNorth - CarpetRackZSouth + 0.20f));
                    break;
                case CityDistrictPointOfInterestKind
                    .IndustrialWeighbridge:
                    loopLocal = new Bounds(
                        new Vector3(3.25f, 0.34f, 0.20f),
                        new Vector3(1.20f, 0.56f, 1.28f));
                    detailLocal = new Bounds(
                        new Vector3(0f, 0.16f, 0f),
                        new Vector3(3.60f, 0.22f, 11.60f));
                    break;
                case CityDistrictPointOfInterestKind
                    .NightlifeLastRouteIsland:
                    loopLocal = new Bounds(
                        new Vector3(-2.75f, 0.52f, -1.25f),
                        new Vector3(1.12f, 0.78f, 1.12f));
                    detailLocal = new Bounds(
                        new Vector3(-2.75f, 4.18f, -0.90f),
                        new Vector3(0.78f, 0.48f, 0.18f));
                    break;
                default:
                    geometry = default;
                    return false;
            }

            Quaternion rotation = Quaternion.LookRotation(
                ResolveForward(descriptor),
                Vector3.up);
            float scale = ResolveHorizontalScale(
                descriptor.PublicBounds);
            geometry = new CityPointOfInterestSoundGeometry(
                TransformRecipeBounds(
                    descriptor.Center,
                    rotation,
                    scale,
                    loopLocal),
                TransformRecipeBounds(
                    descriptor.Center,
                    rotation,
                    scale,
                    detailLocal));
            return true;
        }

        private static Bounds TransformRecipeBounds(
            Vector3 recipeCenter,
            Quaternion recipeRotation,
            float horizontalScale,
            Bounds localBounds)
        {
            Vector3 scaledCenter = new Vector3(
                localBounds.center.x * horizontalScale,
                localBounds.center.y,
                localBounds.center.z * horizontalScale);
            Vector3 scaledExtents = new Vector3(
                localBounds.extents.x * horizontalScale,
                localBounds.extents.y,
                localBounds.extents.z * horizontalScale);
            Vector3 xAxis = recipeRotation *
                new Vector3(scaledExtents.x, 0f, 0f);
            Vector3 yAxis = recipeRotation *
                new Vector3(0f, scaledExtents.y, 0f);
            Vector3 zAxis = recipeRotation *
                new Vector3(0f, 0f, scaledExtents.z);
            Vector3 worldExtents = new Vector3(
                Mathf.Abs(xAxis.x) +
                Mathf.Abs(yAxis.x) +
                Mathf.Abs(zAxis.x),
                Mathf.Abs(xAxis.y) +
                Mathf.Abs(yAxis.y) +
                Mathf.Abs(zAxis.y),
                Mathf.Abs(xAxis.z) +
                Mathf.Abs(yAxis.z) +
                Mathf.Abs(zAxis.z));
            return new Bounds(
                recipeCenter + recipeRotation * scaledCenter,
                worldExtents * 2f);
        }

        private static CityDryingYardNpcStance CreateStance(
            CityDistrictPointOfInterestDescriptor descriptor,
            Quaternion recipeRotation,
            float horizontalScale,
            float groundY,
            Vector3 localPosition,
            Vector3 localFacing)
        {
            return new CityDryingYardNpcStance(
                ToStanceWorld(
                    descriptor,
                    recipeRotation,
                    horizontalScale,
                    groundY,
                    localPosition),
                (recipeRotation * localFacing).normalized);
        }

        private static Vector3 ToStanceWorld(
            CityDistrictPointOfInterestDescriptor descriptor,
            Quaternion recipeRotation,
            float horizontalScale,
            float groundY,
            Vector3 localPosition)
        {
            Vector3 worldPosition = descriptor.Center + recipeRotation *
                new Vector3(
                    localPosition.x * horizontalScale,
                    0f,
                    localPosition.z * horizontalScale);
            return new Vector3(
                worldPosition.x,
                groundY,
                worldPosition.z);
        }

        private static void BuildCitySite(
            Transform parent,
            CityLayout layout,
            CityDistrictPointOfInterestDescriptor descriptor)
        {
            Transform site = CreateSiteRoot(parent, descriptor);
            Rect publicBounds = descriptor.PublicBounds;
            CreatePublicGround(
                site,
                new Vector3(
                    publicBounds.center.x,
                    descriptor.Center.y,
                    publicBounds.center.y),
                new Vector2(
                    publicBounds.width,
                    publicBounds.height),
                ResolvePavingColor(descriptor.Kind),
                true,
                false,
                ResolvePublicGroundFoundationDepth(
                    layout,
                    descriptor));

            Vector3 forward = ResolveForward(descriptor);
            Transform recipe = CreateRecipeRoot(
                site,
                descriptor,
                descriptor.Center,
                forward,
                ResolveHorizontalScale(publicBounds));
            BuildRecipe(recipe, descriptor.Kind, true, false);
        }

        private static void BuildHomeExteriorSite(
            Transform parent,
            HomeExteriorContextPlan context,
            CityDistrictPointOfInterestDescriptor descriptor)
        {
            Rect localBounds =
                PlayerHomeBalconyGeometry.ToHomeLocalRect(
                    context.PlayerHome,
                    descriptor.PublicBounds);
            if (localBounds.xMin <
                HomeExteriorViewBuilder.ExteriorMinimumX)
            {
                return;
            }

            Transform site = CreateSiteRoot(parent, descriptor);
            Vector3 localCenter =
                PlayerHomeBalconyGeometry.ToHomeLocal(
                    context.PlayerHome,
                    descriptor.Center);
            CreatePublicGround(
                site,
                new Vector3(
                    localBounds.center.x,
                    localCenter.y,
                    localBounds.center.y),
                new Vector2(
                    localBounds.width,
                    localBounds.height),
                ResolvePavingColor(descriptor.Kind),
                false,
                true,
                0f);

            Vector3 localForward =
                PlayerHomeBalconyGeometry.ToHomeLocalDirection(
                    context.PlayerHome,
                    ResolveForward(descriptor));
            Transform recipe = CreateRecipeRoot(
                site,
                descriptor,
                localCenter,
                localForward,
                ResolveHorizontalScale(localBounds));
            BuildRecipe(recipe, descriptor.Kind, false, true);
        }

        private static Transform CreateSiteRoot(
            Transform parent,
            CityDistrictPointOfInterestDescriptor descriptor)
        {
            Transform site = new GameObject(
                GetSiteName(descriptor.Id)).transform;
            site.SetParent(parent, false);
            return site;
        }

        private static Transform CreateRecipeRoot(
            Transform parent,
            CityDistrictPointOfInterestDescriptor descriptor,
            Vector3 center,
            Vector3 forward,
            float horizontalScale)
        {
            Transform recipe = new GameObject(
                GetRecipeName(descriptor.Kind)).transform;
            recipe.SetParent(parent, false);
            recipe.localPosition = center;
            recipe.localRotation = Quaternion.LookRotation(
                forward,
                Vector3.up);
            recipe.localScale = new Vector3(
                horizontalScale,
                1f,
                horizontalScale);
            return recipe;
        }

        private static void CreatePublicGround(
            Transform parent,
            Vector3 center,
            Vector2 size,
            Color color,
            bool collider,
            bool homeExterior,
            float foundationDepth)
        {
            GameObject ground = RuntimePrimitiveFactory.CreateBox(
                PublicGroundName,
                parent,
                center - Vector3.up * (foundationDepth * 0.5f),
                new Vector3(
                    size.x,
                    PublicGroundHeight + foundationDepth,
                    size.y),
                color,
                RuntimePrimitiveFactory.DefaultMaterial,
                collider);
            ConfigureRenderer(ground, homeExterior);
            CityPointOfInterestSurfaceAppearance.Apply(
                ground.GetComponent<Renderer>(),
                CityPointOfInterestSurfaceKind.Paving,
                SurfaceProjection.BoxXZ,
                color);
        }

        private static float ResolvePublicGroundFoundationDepth(
            CityLayout layout,
            CityDistrictPointOfInterestDescriptor descriptor)
        {
            CitySurfaceDescriptor surface = default;
            bool found = false;
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                if (layout.Surfaces[index].Cell != descriptor.Cell)
                {
                    continue;
                }

                surface = layout.Surfaces[index];
                found = true;
                break;
            }

            if (!found || !CityTerrainSurfacePlan.UsesContinuousTop(surface))
            {
                return MinimumPublicGroundFoundationDepth;
            }

            Rect bounds = descriptor.PublicBounds;
            Vector2[] samples =
            {
                new Vector2(bounds.xMin, bounds.yMin),
                new Vector2(bounds.xMax, bounds.yMin),
                new Vector2(bounds.xMin, bounds.yMax),
                new Vector2(bounds.xMax, bounds.yMax)
            };
            float lowestTop = float.PositiveInfinity;
            for (int index = 0; index < samples.Length; index++)
            {
                lowestTop = Mathf.Min(
                    lowestTop,
                    CityTerrainSurfacePlan.SampleTop(
                        layout,
                        surface,
                        samples[index]));
            }

            float authoredTop = descriptor.Center.y +
                                PublicGroundHeight * 0.5f;
            return Mathf.Max(
                MinimumPublicGroundFoundationDepth,
                authoredTop - lowestTop);
        }

        private static void BuildRecipe(
            Transform parent,
            CityDistrictPointOfInterestKind kind,
            bool colliders,
            bool homeExterior)
        {
            switch (kind)
            {
                case CityDistrictPointOfInterestKind
                    .OldTownWaterworksCourt:
                    BuildWaterworks(
                        parent,
                        colliders,
                        homeExterior);
                    return;
                case CityDistrictPointOfInterestKind
                    .ResidentialDryingYard:
                    BuildDryingYard(
                        parent,
                        colliders,
                        homeExterior);
                    return;
                case CityDistrictPointOfInterestKind
                    .IndustrialWeighbridge:
                    BuildWeighbridge(
                        parent,
                        colliders,
                        homeExterior);
                    return;
                case CityDistrictPointOfInterestKind
                    .NightlifeLastRouteIsland:
                    BuildLastRouteIsland(
                        parent,
                        colliders,
                        homeExterior);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static void BuildWaterworks(
            Transform parent,
            bool colliders,
            bool homeExterior)
        {
            AddBox(parent, "Basin Floor", -0.80f, 0.15f, 0.40f,
                4.15f, 0.30f, 1.55f, OldStone, false, homeExterior);
            AddBox(parent, "Basin North Rim", -0.80f, 0.43f, 1.10f,
                4.35f, 0.56f, 0.24f, OldStone, false, homeExterior);
            AddBox(parent, "Basin South Rim", -0.80f, 0.43f, -0.30f,
                4.35f, 0.56f, 0.24f, OldStone, false, homeExterior);
            AddBox(parent, "Basin West Rim", -2.86f, 0.43f, 0.40f,
                0.24f, 0.56f, 1.18f, OldStone, false, homeExterior);
            AddBox(parent, "Basin East Rim", 1.26f, 0.43f, 0.40f,
                0.24f, 0.56f, 1.18f, OldStone, false, homeExterior);
            AddBox(parent, "Dark Water", -1.02f, 0.32f, 0.40f,
                3.45f, 0.045f, 1.04f, OldWater, false, homeExterior);

            AddCylinder(parent, "Standpipe Pedestal", 0.55f, 0.45f, 0.40f,
                1.08f, 0.45f, 1.08f, OldStone, false, homeExterior);
            AddCylinder(parent, "Cast Iron Standpipe", 0.55f, 1.98f, 0.40f,
                0.58f, 1.52f, 0.58f, OldMetal, false, homeExterior);
            AddCylinder(parent, "Standpipe Cap", 0.55f, 3.55f, 0.40f,
                1.02f, 0.12f, 1.02f, OldMetal, false, homeExterior);
            AddBox(parent, "Water Spout", 0.55f, 2.82f, 0.98f,
                0.30f, 0.28f, 1.28f, OldMetal, false, homeExterior);
            AddBox(parent, "Water Spout Mouth", 0.55f, 2.62f, 1.58f,
                0.42f, 0.58f, 0.30f, OldMetal, false, homeExterior);
            AddBox(parent, "Repair Riser", 0.98f, 1.82f, 0.40f,
                0.20f, 2.20f, 0.20f, OldRepairMetal, false, homeExterior);
            AddBox(parent, "Repair Bridge", 0.77f, 2.73f, 0.40f,
                0.62f, 0.18f, 0.22f, OldRepairMetal, false, homeExterior);
            AddBox(parent, "Lower Pipe Clamp", 0.55f, 1.32f, 0.40f,
                0.78f, 0.15f, 0.78f, OldRepairMetal, false, homeExterior);
            AddBox(parent, "Upper Pipe Clamp", 0.55f, 2.42f, 0.40f,
                0.76f, 0.15f, 0.76f, OldRepairMetal, false, homeExterior);
            AddBox(parent, "Valve Crossbar", -0.02f, 2.05f, 0.40f,
                0.95f, 0.14f, 0.14f, OldRepairMetal, false, homeExterior);
            AddBox(parent, "Valve Handle", -0.47f, 2.05f, 0.40f,
                0.12f, 0.72f, 0.12f, OldRepairMetal, false, homeExterior);
            AddBox(parent, "Working Lamp", 0.55f, 3.88f, 0.40f,
                0.34f, 0.20f, 0.34f, AmberGlow, true, homeExterior);

            AddBox(parent, "Drain Channel A", -1.80f, 0.075f, -1.08f,
                0.16f, 0.025f, 2.00f, OldMetal, false, homeExterior);
            AddBox(parent, "Drain Channel B", -0.95f, 0.075f, -1.28f,
                0.16f, 0.025f, 1.55f, OldMetal, false, homeExterior);
            AddBox(parent, "Drain Channel C", -0.10f, 0.075f, -1.02f,
                0.16f, 0.025f, 1.95f, OldMetal, false, homeExterior);

            if (colliders)
            {
                AddObstacleCollider(
                    parent,
                    "Waterworks Basin Collider",
                    new Vector3(-0.80f, 0.55f, 0.40f),
                    new Vector3(4.40f, 1.10f, 1.85f));
            }
        }

        private static void BuildDryingYard(
            Transform parent,
            bool colliders,
            bool homeExterior)
        {
            float[] rows = { -3f, 0f, 3f };
            for (int row = 0; row < rows.Length; row++)
            {
                float z = rows[row];
                string rowName = $"Drying Frame {row + 1}";
                AddBox(parent, rowName + " West Post", -4.55f, 1.35f, z,
                    0.20f, 2.70f, 0.20f, ResidentialFrame, false, homeExterior,
                    surface: CityPointOfInterestSurfaceKind.PaintedMetal);
                AddBox(parent, rowName + " East Post", 4.55f, 1.35f, z,
                    0.20f, 2.70f, 0.20f, ResidentialFrame, false, homeExterior,
                    surface: CityPointOfInterestSurfaceKind.PaintedMetal);
                AddBox(parent, rowName + " Header", 0f, 2.66f, z,
                    9.30f, 0.18f, 0.20f, ResidentialFrame, false, homeExterior,
                    surface: CityPointOfInterestSurfaceKind.PaintedMetal);
                AddBox(parent, rowName + " Front Line", 0f, 2.34f, z - 0.16f,
                    9.05f, 0.045f, 0.045f, ResidentialFrame, false, homeExterior,
                    surface: CityPointOfInterestSurfaceKind.PaintedMetal);
                AddBox(parent, rowName + " Back Line", 0f, 2.20f, z + 0.16f,
                    9.05f, 0.045f, 0.045f, ResidentialFrame, false, homeExterior,
                    surface: CityPointOfInterestSurfaceKind.PaintedMetal);

                if (colliders)
                {
                    AddObstacleCollider(
                        parent,
                        rowName + " West Post Collider",
                        new Vector3(-4.55f, 1.35f, z),
                        new Vector3(0.28f, 2.70f, 0.28f));
                    AddObstacleCollider(
                        parent,
                        rowName + " East Post Collider",
                        new Vector3(4.55f, 1.35f, z),
                        new Vector3(0.28f, 2.70f, 0.28f));
                }
            }

            if (homeExterior)
            {
                // The balcony vista keeps the wash as cheap static
                // boxes: at that distance the pieces are a few pixels
                // and the exterior scene runs no wind driver.
                AddBox(parent, "Large Faded Blanket", 1.15f, 1.55f, 0f,
                    3.20f, 1.45f, 0.075f, ResidentialCloth, false, homeExterior,
                    surface: CityPointOfInterestSurfaceKind.Cloth);
                AddBox(parent, "Blanket Repair Patch", 1.72f, 1.66f, -0.045f,
                    0.72f, 0.52f, 0.035f, ResidentialPatch, false, homeExterior,
                    surface: CityPointOfInterestSurfaceKind.Cloth);
                AddBox(parent, "Cold Sheet", -2.75f, 1.78f, -3f,
                    1.70f, 0.94f, 0.065f, ResidentialClothCold, false, homeExterior,
                    surface: CityPointOfInterestSurfaceKind.Cloth);
                AddBox(parent, "Small Towel", 2.75f, 1.94f, 3f,
                    0.90f, 0.58f, 0.065f, ResidentialPatch, false, homeExterior,
                    surface: CityPointOfInterestSurfaceKind.Cloth);
            }
            else
            {
                // In the city the wash is real cloth pinned to the
                // lines — front line at y 2.34 / z-0.16, back line at
                // y 2.20 / z+0.16 per frame row — and the weather wind
                // sways it. The repair patch dries as its own ragged
                // offcut on the back line instead of floating rigidly
                // over the moving blanket.
                AddLaundryCloth(parent, "Large Faded Blanket",
                    1.15f, 2.34f, -0.16f,
                    3.20f, 1.45f, ResidentialCloth,
                    tornVariant: 0, columns: 9, rows: 7);
                AddLaundryCloth(parent, "Blanket Repair Patch",
                    -1.35f, 2.20f, 0.16f,
                    0.72f, 0.52f, ResidentialPatch,
                    tornVariant: 3, columns: 4, rows: 4);
                AddLaundryCloth(parent, "Cold Sheet",
                    -2.75f, 2.20f, -2.84f,
                    1.70f, 0.94f, ResidentialClothCold,
                    tornVariant: 0, columns: 6, rows: 5);
                AddLaundryCloth(parent, "Small Towel",
                    2.75f, 2.34f, 2.84f,
                    0.90f, 0.58f, ResidentialPatch,
                    tornVariant: 0, columns: 4, rows: 4);
            }

            AddBox(parent, "Shared Bench Seat",
                DryingBenchX, DryingBenchSeatCenterY, DryingBenchZ,
                DryingBenchWidth, DryingBenchSeatThickness,
                DryingBenchDepth, ResidentialCloth, false, homeExterior,
                surface: CityPointOfInterestSurfaceKind.Timber,
                projection: SurfaceProjection.BoxXZ);
            AddBox(parent, "Shared Bench Leg A", -4.02f, 0.28f, 4.45f,
                0.18f, 0.50f, 0.42f, ResidentialFrame, false, homeExterior,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);
            AddBox(parent, "Shared Bench Leg B", -2.48f, 0.28f, 4.45f,
                0.18f, 0.50f, 0.42f, ResidentialFrame, false, homeExterior,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);

            BuildDryingYardFloodlight(parent, colliders, homeExterior);
            BuildDryingYardCarpetRack(parent, colliders, homeExterior);

            // The blanket used to carry an obstacle collider from its
            // static-box days; simulated cloth is something the hero
            // walks through, so only the timber keeps its collider.
            if (colliders)
            {
                AddObstacleCollider(
                    parent,
                    "Shared Bench Collider",
                    new Vector3(DryingBenchX, 0.38f, DryingBenchZ),
                    new Vector3(DryingBenchWidth, 0.76f, 0.62f));
            }
        }

        /// <summary>
        /// The Soviet carpet-beating rack: two galvanized posts, one
        /// crossbar and two carpets hung over it, on the west strip so
        /// the beaten dust stays away from the drying wash. The rug
        /// albedo comes from the shared Home pipeline — a hung carpet
        /// is the same object indoors and out.
        /// </summary>
        private static void BuildDryingYardCarpetRack(
            Transform parent,
            bool colliders,
            bool homeExterior)
        {
            AddBox(parent, "Carpet Rack Post South",
                CarpetRackX, CarpetRackBarHeight * 0.5f, CarpetRackZSouth,
                0.14f, CarpetRackBarHeight, 0.14f,
                ResidentialFrame, false, homeExterior,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);
            AddBox(parent, "Carpet Rack Post North",
                CarpetRackX, CarpetRackBarHeight * 0.5f, CarpetRackZNorth,
                0.14f, CarpetRackBarHeight, 0.14f,
                ResidentialFrame, false, homeExterior,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);
            AddBox(parent, "Carpet Rack Bar",
                CarpetRackX, CarpetRackBarHeight,
                (CarpetRackZSouth + CarpetRackZNorth) * 0.5f,
                0.10f, 0.10f,
                CarpetRackZNorth - CarpetRackZSouth + 0.14f,
                ResidentialFrame, false, homeExterior,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);
            if (homeExterior)
            {
                // The balcony vista keeps the carpets as cheap static
                // boxes: at that distance they are a few pixels and
                // the exterior scene runs no strike driver.
                AddCarpet(parent, "Beaten Carpet South",
                    CarpetSouthZ, 1.28f, 1.10f, CarpetOxblood,
                    homeExterior);
                AddCarpet(parent, "Beaten Carpet North",
                    CarpetNorthZ, 1.12f, 0.92f, CarpetTeal,
                    homeExterior);
            }
            else
            {
                // In the city each carpet is real simulated cloth
                // pinned over the bar, so the babushka strikes ripple
                // through it. Heavy pile: stiff, damped, deliberately
                // outside the laundry's weather-wind registry.
                AddCarpetCloth(parent, "Beaten Carpet South",
                    CarpetSouthZ, 1.24f, 1.10f, CarpetOxblood,
                    CityDryingYardCarpetRegistry.SouthCarpetId);
                AddCarpetCloth(parent, "Beaten Carpet North",
                    CarpetNorthZ, 1.08f, 0.92f, CarpetTeal,
                    CityDryingYardCarpetRegistry.NorthCarpetId);
            }

            if (colliders)
            {
                AddObstacleCollider(
                    parent,
                    "Carpet Rack Post South Collider",
                    new Vector3(
                        CarpetRackX,
                        CarpetRackBarHeight * 0.5f,
                        CarpetRackZSouth),
                    new Vector3(0.20f, CarpetRackBarHeight, 0.20f));
                AddObstacleCollider(
                    parent,
                    "Carpet Rack Post North Collider",
                    new Vector3(
                        CarpetRackX,
                        CarpetRackBarHeight * 0.5f,
                        CarpetRackZNorth),
                    new Vector3(0.20f, CarpetRackBarHeight, 0.20f));
                AddObstacleCollider(
                    parent,
                    "Beaten Carpet South Collider",
                    new Vector3(
                        CarpetRackX,
                        CarpetRackBarHeight - 0.60f,
                        CarpetSouthZ),
                    new Vector3(0.16f, 1.24f, 1.10f));
                AddObstacleCollider(
                    parent,
                    "Beaten Carpet North Collider",
                    new Vector3(
                        CarpetRackX,
                        CarpetRackBarHeight - 0.52f,
                        CarpetNorthZ),
                    new Vector3(0.16f, 1.08f, 0.92f));
            }
        }

        /// <summary>
        /// One simulated carpet: a heavy cloth panel pinned just over
        /// the bar, textured with the shared Home rug albedo, plus a
        /// small static fold cap over the bar itself. Registered so
        /// the babushka strike driver can find it; deliberately not
        /// wind-registered — a heavy pile carpet does not flap like
        /// the laundry.
        /// </summary>
        private static void AddCarpetCloth(
            Transform parent,
            string name,
            float z,
            float height,
            float width,
            Color tint,
            string carpetId)
        {
            GameObject panel = ClothPanelFactory.CreateHangingRag(
                name,
                parent,
                new Vector3(CarpetRackX, CarpetRackBarHeight + 0.04f, z),
                90f,
                width,
                height,
                tint,
                tornVariant: 0,
                columns: 6,
                rows: 6);
            var renderer =
                panel.GetComponent<SkinnedMeshRenderer>();
            ApplyCarpetRugAppearance(renderer, tint, width, height);

            Cloth cloth = panel.GetComponent<Cloth>();
            cloth.stretchingStiffness = 1f;
            cloth.bendingStiffness = 0.85f;
            cloth.damping = 0.82f;
            CityDryingYardCarpetRegistry.Register(carpetId, cloth);

            GameObject foldCap = RuntimePrimitiveFactory.CreateBox(
                name + " Fold",
                parent,
                new Vector3(CarpetRackX, CarpetRackBarHeight + 0.05f, z),
                new Vector3(0.16f, 0.10f, width),
                tint,
                RuntimePrimitiveFactory.DefaultMaterial,
                false);
            HomeSurfaceAppearance.Apply(
                foldCap.GetComponent<Renderer>(),
                HomeSurfaceKind.Rug,
                SurfaceProjection.BoxZY,
                tint);
        }

        /// <summary>Rug albedo over a cloth panel's plain 0..1 UVs,
        /// keeping the panel's shared two-sided material and matte
        /// specular.</summary>
        private static void ApplyCarpetRugAppearance(
            Renderer renderer,
            Color tint,
            float widthMeters,
            float heightMeters)
        {
            HomeSurfaceRecipe recipe =
                HomeSurfaceAppearance.GetRecipe(HomeSurfaceKind.Rug);
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(
                Shader.PropertyToID("_BaseMap"),
                HomeSurfaceAppearance.GetTexture(HomeSurfaceKind.Rug));
            Color displayTint = HomeSurfaceAppearance.CreateDisplayTint(
                tint,
                HomeSurfaceKind.Rug);
            properties.SetColor(
                Shader.PropertyToID("_BaseColor"),
                displayTint);
            properties.SetColor(
                Shader.PropertyToID("_Color"),
                displayTint);
            properties.SetVector(
                Shader.PropertyToID("_BaseMap_ST"),
                SurfaceAppearanceCore.CreateBaseMapTransform(
                    renderer.transform,
                    widthMeters,
                    heightMeters,
                    recipe.MetersPerTile,
                    0.35f,
                    6000));
            properties.SetFloat(
                Shader.PropertyToID("_Smoothness"),
                0f);
            properties.SetFloat(
                Shader.PropertyToID("_Metallic"),
                0f);
            renderer.SetPropertyBlock(properties);
        }

        /// <summary>One carpet hung over the rack bar, textured with
        /// the shared Home rug albedo.</summary>
        private static void AddCarpet(
            Transform parent,
            string name,
            float z,
            float height,
            float width,
            Color tint,
            bool homeExterior)
        {
            GameObject carpet = RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                new Vector3(
                    CarpetRackX,
                    CarpetRackBarHeight + 0.04f - height * 0.5f,
                    z),
                new Vector3(0.07f, height, width),
                tint,
                RuntimePrimitiveFactory.DefaultMaterial,
                false);
            ConfigureRenderer(carpet, homeExterior);
            HomeSurfaceAppearance.Apply(
                carpet.GetComponent<Renderer>(),
                HomeSurfaceKind.Rug,
                SurfaceProjection.BoxZY,
                tint);
        }

        /// <summary>
        /// The communal floodlight on its own pole at the street-side
        /// corner opposite the shared bench, washing all three drying
        /// frames and their hanging laundry. The city build carries one
        /// real shadowless night-scaled Spot plus a fog halo; the home
        /// exterior vista keeps only the pole, head and dead-by-day
        /// lens geometry.
        /// </summary>
        private static void BuildDryingYardFloodlight(
            Transform parent,
            bool colliders,
            bool homeExterior)
        {
            AddCylinder(parent, "Drying Yard Floodlight Pole",
                FloodlightPoleX, FloodlightHeadHeight * 0.5f,
                FloodlightPoleZ,
                0.22f, FloodlightHeadHeight * 0.5f, 0.22f,
                ResidentialFrame, false, homeExterior,
                CityPointOfInterestSurfaceKind.PaintedMetal);

            var headPosition = new Vector3(
                FloodlightPoleX,
                FloodlightHeadHeight,
                FloodlightPoleZ);
            Transform head = new GameObject("Floodlight Head").transform;
            head.SetParent(parent, false);
            head.localPosition = headPosition;
            head.localRotation = Quaternion.LookRotation(
                (FloodlightAimTarget - headPosition).normalized,
                Vector3.up);

            GameObject housing = RuntimePrimitiveFactory.CreateBox(
                "Floodlight Housing",
                head,
                new Vector3(0f, 0f, -0.16f),
                new Vector3(0.46f, 0.30f, 0.38f),
                ResidentialFrame,
                RuntimePrimitiveFactory.DefaultMaterial,
                false);
            ConfigureRenderer(housing, homeExterior);
            CityPointOfInterestSurfaceAppearance.Apply(
                housing.GetComponent<Renderer>(),
                CityPointOfInterestSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxXY,
                ResidentialFrame);

            GameObject lens = RuntimePrimitiveFactory.CreateBox(
                "Floodlight Lens",
                head,
                new Vector3(0f, 0f, 0.04f),
                new Vector3(0.36f, 0.22f, 0.03f),
                FloodlightGlow,
                CityNightResources.EmissiveMaterial,
                false);
            ConfigureRenderer(lens, homeExterior);
            CityNightGlowRegistry.Register(
                lens.GetComponent<Renderer>(),
                FloodlightGlow);

            if (!homeExterior)
            {
                GameObject emitter = new GameObject(
                    "Drying Yard Floodlight Light");
                emitter.transform.SetParent(head, false);
                Light light = emitter.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = FloodlightLightColor;
                light.intensity = FloodlightNightIntensity;
                light.range = FloodlightRange;
                light.spotAngle = FloodlightSpotAngle;
                light.innerSpotAngle = FloodlightInnerSpotAngle;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                light.lightmapBakeType = LightmapBakeType.Realtime;

                GameObject haloObject = new GameObject(
                    "Floodlight Source Halo");
                haloObject.transform.SetParent(
                    emitter.transform,
                    false);
                CityLightHalo halo =
                    haloObject.AddComponent<CityLightHalo>();
                halo.Initialize(
                    CityNightResources.AtmosphereMaterial,
                    0.70f,
                    1.95f,
                    new Color(
                        FloodlightLightColor.r * 4.2f,
                        FloodlightLightColor.g * 4.2f,
                        FloodlightLightColor.b * 4.2f,
                        0.18f),
                    new Color(
                        FloodlightLightColor.r * 2.1f,
                        FloodlightLightColor.g * 2.1f,
                        FloodlightLightColor.b * 2.1f,
                        0.05f));
                CityNightSiteLightRegistry.Register(
                    light,
                    FloodlightNightIntensity,
                    halo);
            }

            if (colliders)
            {
                AddObstacleCollider(
                    parent,
                    "Drying Yard Floodlight Pole Collider",
                    new Vector3(
                        FloodlightPoleX,
                        FloodlightHeadHeight * 0.5f,
                        FloodlightPoleZ),
                    new Vector3(0.30f, FloodlightHeadHeight, 0.30f));
            }
        }

        private static void BuildWeighbridge(
            Transform parent,
            bool colliders,
            bool homeExterior)
        {
            AddBox(parent, "Weighbridge Deck", 0f, 0.16f, 0f,
                3.60f, 0.22f, 11.60f, IndustrialSteel, false, homeExterior);
            AddBox(parent, "Deck Dark Channel West", -1.48f, 0.285f, 0f,
                0.20f, 0.035f, 10.80f, IndustrialDark, false, homeExterior);
            AddBox(parent, "Deck Dark Channel East", 1.48f, 0.285f, 0f,
                0.20f, 0.035f, 10.80f, IndustrialDark, false, homeExterior);
            AddBox(parent, "Axle Marking North", 0f, 0.305f, 3.62f,
                3.05f, 0.025f, 0.20f, IndustrialMarking, false, homeExterior);
            AddBox(parent, "Axle Marking South", 0f, 0.305f, -3.62f,
                3.05f, 0.025f, 0.20f, IndustrialMarking, false, homeExterior);
            AddBox(parent, "Deck Repair Plate", 0.62f, 0.31f, -1.25f,
                1.05f, 0.035f, 1.45f, IndustrialRust, false, homeExterior);

            AddBox(parent, "Scale Mechanism Base", 3.25f, 0.34f, 0.20f,
                1.20f, 0.56f, 1.28f, IndustrialDark, false, homeExterior);
            AddBox(parent, "Scale Indicator Mast", 3.25f, 2.52f, 0.20f,
                0.30f, 4.35f, 0.34f, IndustrialSteel, false, homeExterior);
            AddBox(parent, "Scale Indicator Head", 3.25f, 4.63f, 0.20f,
                2.25f, 0.82f, 0.62f, IndustrialDark, false, homeExterior);
            AddBox(parent, "Scale Indicator Face", 3.25f, 4.66f, 0.525f,
                1.78f, 0.52f, 0.035f, IndustrialGlow, true, homeExterior,
                alwaysLit: true);
            GameObject needle = AddBox(parent, "Scale Needle",
                3.25f, 4.66f, 0.55f,
                0.10f, 0.42f, 0.035f, IndustrialDark, false, homeExterior, 28f);
            if (!homeExterior)
            {
                // The bounded Home view never runs the needle
                // controller; only the City build's needle answers
                // weight, so only it may claim the registry slot.
                CityWeighbridgeIndicatorRegistry.Register(
                    CityWeighbridgeIndicatorRegistry.NeedleId,
                    needle.transform);
            }
            AddBox(parent, "Mechanical Linkage", 2.65f, 1.10f, 0.20f,
                1.08f, 0.20f, 0.22f, IndustrialRust, false, homeExterior);
            AddBox(parent, "Cold Service Lamp", 3.25f, 5.34f, 0.20f,
                1.15f, 0.16f, 0.38f, IndustrialGlow, true, homeExterior);

            for (int side = -1; side <= 1; side += 2)
            {
                AddBox(parent, $"Load Cell {side} North", side * 1.62f, 0.19f, 4.45f,
                    0.42f, 0.28f, 0.72f, IndustrialRust, false, homeExterior);
                AddBox(parent, $"Load Cell {side} South", side * 1.62f, 0.19f, -4.45f,
                    0.42f, 0.28f, 0.72f, IndustrialRust, false, homeExterior);
            }

            AddBox(parent, "Wheel Chock A", -0.92f, 0.42f, -5.10f,
                0.62f, 0.26f, 0.42f, IndustrialMarking, false, homeExterior, 14f);
            AddBox(parent, "Wheel Chock B", 0.92f, 0.42f, -5.10f,
                0.62f, 0.26f, 0.42f, IndustrialMarking, false, homeExterior, -14f);

            if (colliders)
            {
                AddObstacleCollider(
                    parent,
                    "Walkable Weighbridge Collider",
                    new Vector3(0f, 0.16f, 0f),
                    new Vector3(3.60f, 0.22f, 11.60f));
                AddObstacleCollider(
                    parent,
                    "Scale Mechanism Collider",
                    new Vector3(3.25f, 0.50f, 0.20f),
                    new Vector3(1.20f, 1.00f, 1.28f));
            }
        }

        private static void BuildLastRouteIsland(
            Transform parent,
            bool colliders,
            bool homeExterior)
        {
            AddCylinder(parent, "Last Route Island", 0f, 0.12f, 0f,
                10.80f, 0.09f, 10.80f, NightlifeIsland,
                colliders, homeExterior,
                CityPointOfInterestSurfaceKind.Paving,
                SurfaceProjection.CylinderCapXZ);
            // The inner ring is paint over the same paving, so it
            // carries the paving grain under its dark marking tint;
            // a flat untextured disc read as a missing texture next
            // to the textured platform and centre.
            AddCylinder(parent, "Inner Route Ring", 0f, 0.225f, 0f,
                7.20f, 0.025f, 7.20f, NightlifeFrame, false, homeExterior,
                CityPointOfInterestSurfaceKind.Paving,
                SurfaceProjection.CylinderCapXZ);
            AddCylinder(parent, "Empty Island Centre", 0f, 0.255f, 0f,
                4.20f, 0.02f, 4.20f, NightlifePaving, false, homeExterior,
                CityPointOfInterestSurfaceKind.Paving,
                SurfaceProjection.CylinderCapXZ);

            float[] segmentAngles = { 48f, 102f, 168f, 226f, 292f };
            for (int index = 0; index < segmentAngles.Length; index++)
            {
                float angle = segmentAngles[index];
                float radians = angle * Mathf.Deg2Rad;
                float x = Mathf.Sin(radians) * 4.70f;
                float z = Mathf.Cos(radians) * 4.70f;
                string name = $"Broken Canopy Segment {index + 1}";
                AddBox(parent, name + " Post", x, 1.70f, z,
                    0.30f, 3.40f, 0.30f, NightlifeFrame, false, homeExterior,
                    surface: CityPointOfInterestSurfaceKind.PaintedMetal);
                AddBox(parent, name + " Beam", x, 3.36f, z,
                    3.25f, 0.26f, 0.42f, NightlifeFrame, false, homeExterior, angle,
                    surface: CityPointOfInterestSurfaceKind.PaintedMetal);
                AddBox(parent, name + " Roof", x, 3.58f, z,
                    3.45f, 0.18f, 1.25f, NightlifeFrame, false, homeExterior, angle,
                    surface: CityPointOfInterestSurfaceKind.PaintedMetal,
                    projection: SurfaceProjection.BoxXZ);
                if (!homeExterior)
                {
                    // Cloth is a city-only dressing: at vista distance
                    // the rags are subpixel and the balcony scene has
                    // no wind driver.
                    BuildCanopyRags(parent, name, index, x, z, angle);
                }

                if (index == 1 || index == 4)
                {
                    float plateOffset = 0.18f;
                    Color plateColor = index == 1
                        ? NightlifePosterBlue
                        : NightlifePosterRed;
                    AddBox(
                        parent,
                        name + " Weathered Route Plate",
                        x + Mathf.Sin(radians) * plateOffset,
                        2.42f,
                        z + Mathf.Cos(radians) * plateOffset,
                        0.62f,
                        0.44f,
                        0.07f,
                        plateColor,
                        false,
                        homeExterior,
                        angle,
                        surface: CityPointOfInterestSurfaceKind.Paper);
                }

                if (colliders)
                {
                    AddObstacleCollider(
                        parent,
                        name + " Post Collider",
                        new Vector3(x, 1.70f, z),
                        new Vector3(0.36f, 3.40f, 0.36f));
                }
            }

            AddBox(parent, "Last Route Mast Base", -2.75f, 0.52f, -1.25f,
                1.12f, 0.78f, 1.12f, NightlifeFrame, false, homeExterior,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);
            AddBox(parent, "Last Route Mast", -2.75f, 3.35f, -1.25f,
                0.34f, 5.70f, 0.34f, NightlifeFrame, false, homeExterior,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);
            AddBox(parent, "Broken Route Totem", -2.75f, 5.55f, -1.25f,
                1.58f, 1.55f, 0.42f, NightlifeFrame, false, homeExterior,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);
            AddBox(parent, "Totem Route Map Backing", -2.75f, 5.55f, -1.02f,
                1.28f, 1.20f, 0.04f, NightlifeRoutePaper, false, homeExterior,
                surface: CityPointOfInterestSurfaceKind.Paper);
            AddBox(parent, "Totem Torn Poster A", -2.95f, 5.66f, -0.99f,
                0.64f, 0.70f, 0.025f, NightlifePosterBlue, false, homeExterior,
                -4f,
                surface: CityPointOfInterestSurfaceKind.Paper);
            AddBox(parent, "Totem Torn Poster B", -2.52f, 5.33f, -0.98f,
                0.50f, 0.43f, 0.025f, NightlifePosterRed, false, homeExterior,
                6f,
                surface: CityPointOfInterestSurfaceKind.Paper);
            AddBox(parent, "Totem Route Number Plate", -2.71f, 5.97f, -0.97f,
                0.42f, 0.20f, 0.025f, NightlifeRouteInk, false, homeExterior);
            // The incomplete departure signal has a physical mouth. This
            // small municipal speaker is bolted to the mast below the map;
            // its grille is deliberately readable before it is heard.
            AddBox(parent, "Last Route Speaker Housing",
                -2.75f, 4.18f, -0.99f,
                0.78f, 0.48f, 0.18f,
                NightlifeFrame, false, homeExterior,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);
            AddBox(parent, "Last Route Speaker Grille",
                -2.75f, 4.18f, -0.885f,
                0.62f, 0.32f, 0.035f,
                NightlifeWaste, false, homeExterior,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);
            AddBox(parent, "Departure Board", 2.45f, 2.10f, -2.55f,
                2.65f, 1.10f, 0.28f, NightlifeFrame, false, homeExterior, -12f,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);
            AddBox(parent, "Departure Board Support West", 1.61f, 0.885f, -2.73f,
                0.20f, 1.33f, 0.24f, NightlifeFrame, false, homeExterior, -12f,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);
            AddBox(parent, "Departure Board Support East", 3.29f, 0.885f, -2.37f,
                0.20f, 1.33f, 0.24f, NightlifeFrame, false, homeExterior, -12f,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);
            AddBox(parent, "Departure Board Foot West", 1.61f, 0.27f, -2.73f,
                0.48f, 0.12f, 0.46f, NightlifeFrame, false, homeExterior, -12f,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);
            AddBox(parent, "Departure Board Foot East", 3.29f, 0.27f, -2.37f,
                0.48f, 0.12f, 0.46f, NightlifeFrame, false, homeExterior, -12f,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);
            AddBox(parent, "Departure Board Glass", 2.45f, 2.10f, -2.39f,
                2.30f, 0.78f, 0.035f, NightlifeRouteInk, false, homeExterior,
                -12f);
            AddBox(parent, "Departure Schedule Row A", 2.45f, 2.30f, -2.365f,
                1.78f, 0.07f, 0.025f, NightlifeRoutePaper, false, homeExterior,
                -12f,
                surface: CityPointOfInterestSurfaceKind.Paper);
            AddBox(parent, "Departure Schedule Row B", 2.45f, 2.10f, -2.365f,
                1.42f, 0.07f, 0.025f, NightlifePosterBlue, false, homeExterior,
                -12f,
                surface: CityPointOfInterestSurfaceKind.Paper);
            AddBox(parent, "Departure Schedule Row C", 2.45f, 1.90f, -2.365f,
                1.92f, 0.07f, 0.025f, NightlifeRoutePaper, false, homeExterior,
                -12f,
                surface: CityPointOfInterestSurfaceKind.Paper);
            AddBox(parent, "Empty Bench",
                IslandBenchX, IslandBenchSeatCenterY, IslandBenchZ,
                IslandBenchWidth, IslandBenchSeatThickness,
                IslandBenchDepth, NightlifeSeat, false, homeExterior,
                IslandBenchYaw,
                surface: CityPointOfInterestSurfaceKind.Timber,
                projection: SurfaceProjection.BoxXZ);
            AddBox(parent, "Empty Bench Base",
                IslandBenchX, 0.33f, IslandBenchZ,
                0.38f, 0.66f, 0.48f, NightlifeFrame, false, homeExterior,
                IslandBenchYaw,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);
            AddBox(parent, "Island Waste Bin", 4.15f, 0.71f, 2.20f,
                0.72f, 1.00f, 0.72f, NightlifeWaste, false, homeExterior, 8f,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);
            AddBox(parent, "Island Waste Bin Rim", 4.15f, 1.23f, 2.20f,
                0.82f, 0.08f, 0.82f, NightlifeFrame, false, homeExterior, 8f,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);
            AddBox(parent, "Island Waste Bin Opening", 4.15f, 1.275f, 2.20f,
                0.54f, 0.018f, 0.50f, NightlifeRouteInk, false, homeExterior,
                8f);
            AddCylinder(parent, "Discarded Bottle Standing", 2.08f, 0.31f,
                3.82f, 0.13f, 0.09f, 0.13f, NightlifeRoutePaper, false,
                homeExterior);
            AddBox(parent, "Discarded Bottle Fallen", 1.72f, 0.255f, 3.68f,
                0.34f, 0.07f, 0.12f, NightlifePosterBlue, false, homeExterior,
                28f);
            AddBox(parent, "Lost Scarf", -0.35f, 0.292f, 1.05f,
                1.10f, 0.025f, 0.34f, NightlifePosterRed, false, homeExterior,
                -18f,
                surface: CityPointOfInterestSurfaceKind.Cloth,
                projection: SurfaceProjection.BoxXZ);
            AddBox(parent, "Discarded Timetable", -1.20f, 0.228f, -3.65f,
                0.72f, 0.025f, 0.50f, NightlifeRoutePaper, false,
                homeExterior, 12f,
                surface: CityPointOfInterestSurfaceKind.Paper,
                projection: SurfaceProjection.BoxXZ);

            BuildIslandMastFloodlight(parent, homeExterior);

            if (colliders)
            {
                AddObstacleCollider(
                    parent,
                    "Last Route Mast Collider",
                    new Vector3(-2.75f, 0.72f, -1.25f),
                    new Vector3(1.12f, 1.42f, 1.12f));
                AddObstacleCollider(
                    parent,
                    "Departure Board Collider",
                    new Vector3(2.45f, 1.35f, -2.55f),
                    new Vector3(2.65f, 2.70f, 0.38f),
                    -12f);
                AddObstacleCollider(
                    parent,
                    "Empty Bench Collider",
                    new Vector3(IslandBenchX, 0.44f, IslandBenchZ),
                    new Vector3(
                        IslandBenchWidth,
                        0.88f,
                        IslandBenchDepth),
                    IslandBenchYaw);
                AddObstacleCollider(
                    parent,
                    "Island Waste Bin Collider",
                    new Vector3(4.15f, 0.71f, 2.20f),
                    new Vector3(0.78f, 1.00f, 0.78f),
                    8f);
            }
        }

        /// <summary>
        /// The abandoned island's one working electric fixture: an old
        /// service floodlight bracketed off the route mast under the
        /// broken totem, aimed across the empty centre at the empty
        /// bench. The city build carries one real shadowless
        /// night-scaled Spot plus a fog halo; the home exterior vista
        /// keeps only the bracket, housing and dead-by-day lens
        /// geometry. The mast base already owns the obstacle collider,
        /// so the light adds none.
        /// </summary>
        private static void BuildIslandMastFloodlight(
            Transform parent,
            bool homeExterior)
        {
            // Bracket arm from the mast face to the head, yawed to the
            // mast-to-head direction.
            AddBox(parent, "Island Floodlight Bracket",
                -2.575f, IslandFloodlightHeadLocal.y, -1.15f,
                0.14f, 0.14f, 0.60f, NightlifeFrame, false, homeExterior,
                60f,
                surface: CityPointOfInterestSurfaceKind.PaintedMetal);

            Transform head = new GameObject(
                "Island Floodlight Head").transform;
            head.SetParent(parent, false);
            head.localPosition = IslandFloodlightHeadLocal;
            head.localRotation = Quaternion.LookRotation(
                (IslandFloodlightAimTarget -
                 IslandFloodlightHeadLocal).normalized,
                Vector3.up);

            GameObject housing = RuntimePrimitiveFactory.CreateBox(
                "Island Floodlight Housing",
                head,
                new Vector3(0f, 0f, -0.16f),
                new Vector3(0.46f, 0.30f, 0.38f),
                NightlifeFrame,
                RuntimePrimitiveFactory.DefaultMaterial,
                false);
            ConfigureRenderer(housing, homeExterior);
            CityPointOfInterestSurfaceAppearance.Apply(
                housing.GetComponent<Renderer>(),
                CityPointOfInterestSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxXY,
                NightlifeFrame);

            GameObject lens = RuntimePrimitiveFactory.CreateBox(
                "Island Floodlight Lens",
                head,
                new Vector3(0f, 0f, 0.04f),
                new Vector3(0.36f, 0.22f, 0.03f),
                IslandFloodlightGlow,
                CityNightResources.EmissiveMaterial,
                false);
            ConfigureRenderer(lens, homeExterior);
            CityNightGlowRegistry.Register(
                lens.GetComponent<Renderer>(),
                IslandFloodlightGlow);

            if (!homeExterior)
            {
                GameObject emitter = new GameObject(
                    "Island Mast Floodlight Light");
                emitter.transform.SetParent(head, false);
                Light light = emitter.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = IslandFloodlightLightColor;
                light.intensity = IslandFloodlightNightIntensity;
                light.range = IslandFloodlightRange;
                light.spotAngle = IslandFloodlightSpotAngle;
                light.innerSpotAngle = IslandFloodlightInnerSpotAngle;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                light.lightmapBakeType = LightmapBakeType.Realtime;

                GameObject haloObject = new GameObject(
                    "Island Floodlight Source Halo");
                haloObject.transform.SetParent(
                    emitter.transform,
                    false);
                CityLightHalo halo =
                    haloObject.AddComponent<CityLightHalo>();
                halo.Initialize(
                    CityNightResources.AtmosphereMaterial,
                    0.70f,
                    1.95f,
                    new Color(
                        IslandFloodlightLightColor.r * 4.2f,
                        IslandFloodlightLightColor.g * 4.2f,
                        IslandFloodlightLightColor.b * 4.2f,
                        0.18f),
                    new Color(
                        IslandFloodlightLightColor.r * 2.1f,
                        IslandFloodlightLightColor.g * 2.1f,
                        IslandFloodlightLightColor.b * 2.1f,
                        0.05f));
                CityNightSiteLightRegistry.Register(
                    light,
                    IslandFloodlightNightIntensity,
                    halo);
            }
        }

        private static void BuildCanopyRags(
            Transform parent,
            string segmentName,
            int segmentIndex,
            float x,
            float z,
            float angleDegrees)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            // The roof and beam are yawed by the segment angle: their
            // long axis is the rotated +X, their depth the rotated +Z.
            Vector3 along = new Vector3(
                Mathf.Cos(radians),
                0f,
                -Mathf.Sin(radians));
            Vector3 outward = new Vector3(
                Mathf.Sin(radians),
                0f,
                Mathf.Cos(radians));
            int ragNumber = 0;
            for (int index = 0;
                 index < CanopyRagRecipes.Length;
                 index++)
            {
                CanopyRagRecipe recipe = CanopyRagRecipes[index];
                if (recipe.SegmentIndex != segmentIndex)
                {
                    continue;
                }

                ragNumber++;
                Vector3 position =
                    new Vector3(x, CanopyRagHangHeight, z) +
                    (along * recipe.AlongOffset) +
                    (outward * recipe.OutOffset);
                GameObject rag = ClothPanelFactory.CreateHangingRag(
                    $"{segmentName} Rag {ragNumber}",
                    parent,
                    position,
                    angleDegrees + recipe.ExtraYawDegrees,
                    recipe.Width,
                    recipe.Height,
                    recipe.Color,
                    recipe.TornVariant);
                CityPointOfInterestSurfaceAppearance.ApplyClothPanel(
                    rag.GetComponent<SkinnedMeshRenderer>(),
                    recipe.Color,
                    recipe.Width,
                    recipe.Height);
                CityClothWindRegistry.Register(
                    rag.GetComponent<Cloth>());
            }
        }

        /// <summary>
        /// One washed piece pinned to a drying line: a simulated cloth
        /// panel hanging down from the line height, facing the frame's
        /// ±Z, swayed by the deterministic weather wind.
        /// </summary>
        private static void AddLaundryCloth(
            Transform parent,
            string name,
            float x,
            float lineHeight,
            float z,
            float width,
            float height,
            Color color,
            int tornVariant,
            int columns,
            int rows)
        {
            GameObject rag = ClothPanelFactory.CreateHangingRag(
                name,
                parent,
                new Vector3(x, lineHeight, z),
                0f,
                width,
                height,
                color,
                tornVariant,
                columns,
                rows);
            CityPointOfInterestSurfaceAppearance.ApplyClothPanel(
                rag.GetComponent<SkinnedMeshRenderer>(),
                color,
                width,
                height);
            Cloth cloth = rag.GetComponent<Cloth>();
            CityClothWindRegistry.Register(cloth);

            // Laundry hangs at body height and the yard is walkable
            // right through it: the hero's capsule parts the cloth
            // instead of clipping.
            CityClothBodyRegistry.RegisterCloth(cloth);
        }

        private readonly struct CanopyRagRecipe
        {
            public CanopyRagRecipe(
                int segmentIndex,
                float alongOffset,
                float outOffset,
                float width,
                float height,
                Color color,
                int tornVariant,
                float extraYawDegrees)
            {
                SegmentIndex = segmentIndex;
                AlongOffset = alongOffset;
                OutOffset = outOffset;
                Width = width;
                Height = height;
                Color = color;
                TornVariant = tornVariant;
                ExtraYawDegrees = extraYawDegrees;
            }

            public int SegmentIndex { get; }
            public float AlongOffset { get; }
            public float OutOffset { get; }
            public float Width { get; }
            public float Height { get; }
            public Color Color { get; }
            public int TornVariant { get; }
            public float ExtraYawDegrees { get; }
        }

        private static GameObject AddBox(
            Transform parent,
            string name,
            float x,
            float y,
            float z,
            float width,
            float height,
            float depth,
            Color color,
            bool emissive,
            bool homeExterior,
            float yaw = 0f,
            bool alwaysLit = false,
            CityPointOfInterestSurfaceKind? surface = null,
            SurfaceProjection projection = SurfaceProjection.BoxXY)
        {
            Material material = emissive
                ? CityNightResources.EmissiveMaterial
                : RuntimePrimitiveFactory.DefaultMaterial;
            GameObject part = RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                new Vector3(x, y, z),
                new Vector3(width, height, depth),
                color,
                material,
                false);
            part.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            ConfigureRenderer(part, homeExterior);
            if (surface.HasValue && !emissive)
            {
                CityPointOfInterestSurfaceAppearance.Apply(
                    part.GetComponent<Renderer>(),
                    surface.Value,
                    projection,
                    color);
            }

            // Site lamps die by day with every other electric glow;
            // only a working instrument face may stay always lit.
            if (emissive && !alwaysLit)
            {
                CityNightGlowRegistry.Register(
                    part.GetComponent<Renderer>(),
                    color);
            }

            return part;
        }

        private static void AddCylinder(
            Transform parent,
            string name,
            float x,
            float y,
            float z,
            float width,
            float halfHeight,
            float depth,
            Color color,
            bool collider,
            bool homeExterior,
            CityPointOfInterestSurfaceKind? surfaceKind = null,
            SurfaceProjection projection =
                SurfaceProjection.CylinderSide)
        {
            Material material =
                RuntimePrimitiveFactory.DefaultMaterial;
            GameObject part = RuntimePrimitiveFactory.CreateCylinder(
                name,
                parent,
                new Vector3(x, y, z),
                new Vector3(width, halfHeight, depth),
                color,
                material,
                false);
            if (collider && !homeExterior)
            {
                MeshCollider surface =
                    part.AddComponent<MeshCollider>();
                surface.sharedMesh =
                    part.GetComponent<MeshFilter>().sharedMesh;
            }
            ConfigureRenderer(part, homeExterior);
            if (surfaceKind.HasValue)
            {
                CityPointOfInterestSurfaceAppearance.Apply(
                    part.GetComponent<Renderer>(),
                    surfaceKind.Value,
                    projection,
                    color);
            }
        }

        private static void AddObstacleCollider(
            Transform parent,
            string name,
            Vector3 center,
            Vector3 size,
            float yaw = 0f)
        {
            GameObject obstacle = new GameObject(name);
            obstacle.transform.SetParent(parent, false);
            obstacle.transform.localPosition = center;
            obstacle.transform.localRotation =
                Quaternion.Euler(0f, yaw, 0f);
            BoxCollider collider = obstacle.AddComponent<BoxCollider>();
            collider.size = size;
        }

        private static void ConfigureRenderer(
            GameObject gameObject,
            bool homeExterior)
        {
            Renderer renderer = gameObject.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            if (homeExterior)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private static Vector3 ResolveForward(
            CityDistrictPointOfInterestDescriptor descriptor)
        {
            Vector2Int streetSide = descriptor.Accesses.Count > 0
                ? descriptor.Accesses[0].StreetSideDirection
                : Vector2Int.down;
            var forward = new Vector3(
                streetSide.x,
                0f,
                streetSide.y);
            if (!IsFinite(forward.x) ||
                !IsFinite(forward.z) ||
                forward.sqrMagnitude < 0.25f)
            {
                return Vector3.back;
            }

            return Mathf.Abs(forward.x) > Mathf.Abs(forward.z)
                ? new Vector3(Mathf.Sign(forward.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(forward.z));
        }

        private static float ResolveHorizontalScale(Rect bounds)
        {
            float minimum = Mathf.Min(bounds.width, bounds.height);
            return Mathf.Clamp(
                minimum / ReferencePublicWidth,
                0.72f,
                1.08f);
        }

        private static Color ResolvePavingColor(
            CityDistrictPointOfInterestKind kind)
        {
            switch (kind)
            {
                case CityDistrictPointOfInterestKind
                    .OldTownWaterworksCourt:
                    return OldTownPaving;
                case CityDistrictPointOfInterestKind
                    .ResidentialDryingYard:
                    return ResidentialPaving;
                case CityDistrictPointOfInterestKind
                    .IndustrialWeighbridge:
                    return IndustrialPaving;
                case CityDistrictPointOfInterestKind
                    .NightlifeLastRouteIsland:
                    return NightlifePaving;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
