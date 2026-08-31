using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One deterministic query of the village's local visibility pressure.
    /// It is presentation data only: a later particle field may translate the
    /// normalized strength into emission or opacity, while traversal stays
    /// completely unchanged.
    /// </summary>
    public readonly struct AlpineVillagePeripheralStormSample
    {
        internal AlpineVillagePeripheralStormSample(
            float distanceOutsideTrodden,
            Vector2 trailOutward,
            float trailExposure01,
            float landmarkApertureProtection01,
            float rearClosure01,
            float stormStrength01)
        {
            DistanceOutsideTrodden = distanceOutsideTrodden;
            TrailOutward = trailOutward;
            TrailExposure01 = trailExposure01;
            LandmarkApertureProtection01 =
                landmarkApertureProtection01;
            RearClosure01 = rearClosure01;
            StormStrength01 = stormStrength01;
        }

        /// <summary>
        /// Metres outside the nearest visible lane or path surface. Negative
        /// values are on trodden ground.
        /// </summary>
        public float DistanceOutsideTrodden { get; }

        /// <summary>
        /// Ground-plane direction away from the nearest trodden route. This
        /// lets presentation move a side curtain away from the route without
        /// measuring the network a second time.
        /// </summary>
        public Vector2 TrailOutward { get; }

        /// <summary>
        /// Smooth route-distance ramp: calm on the network and fully exposed
        /// a few metres into untouched snow.
        /// </summary>
        public float TrailExposure01 { get; }

        /// <summary>
        /// One inside the clear station-to-house viewing aperture, zero
        /// outside it. This is a suppression mask, not storm strength.
        /// </summary>
        public float LandmarkApertureProtection01 { get; }

        /// <summary>
        /// The closing band beyond the mother's rear wall. It is independent
        /// of the route ramp so the world closes behind the landmark even
        /// when projected close to the central viewing axis.
        /// </summary>
        public float RearClosure01 { get; }

        /// <summary>
        /// Final normalized presentation strength after route exposure,
        /// aperture protection and the rear closure have been composed.
        /// </summary>
        public float StormStrength01 { get; }
    }

    /// <summary>
    /// Dimensioned rules shared by the pure plan and its future particle
    /// presentation. None of these values changes fog, collision or weather
    /// timing; they only describe where peripheral blowing snow may become
    /// dense.
    /// </summary>
    public static class AlpineVillagePeripheralStormRules
    {
        /// <summary>
        /// A small calm skirt outside the visible compacted surface prevents
        /// the curtain from beginning on the exact painted edge.
        /// </summary>
        public const float TrailCalmDistance = 0.2f;

        /// <summary>
        /// Untouched snow is fully exposed before the hero is four metres off
        /// a habitual route.
        /// </summary>
        public const float TrailFullStrengthDistance = 3.8f;

        /// <summary>
        /// The aperture begins slightly wider than the village lane at the
        /// station, then opens only enough to contain the distant house.
        /// </summary>
        public const float ApertureNearHalfWidth = 2.2f;

        public const float ApertureHousePadding = 1.1f;
        public const float ApertureBackCorePadding = 0.35f;
        public const float ApertureEdgeFeather = 1.4f;

        /// <summary>
        /// The clear cone releases smoothly after the rear wall instead of
        /// ending as a visible vertical seam.
        /// </summary>
        public const float ApertureRearFeatherDistance = 3.2f;

        /// <summary>
        /// The rear band is fully closed well before the enclosing ridge,
        /// whose toe stands about eighteen metres beyond the house envelope.
        /// </summary>
        public const float RearClosureFullDistance = 12f;

        public static float EvaluateTrailExposure(
            float distanceOutsideTrodden)
        {
            if (float.IsNaN(distanceOutsideTrodden) ||
                float.IsNegativeInfinity(distanceOutsideTrodden))
            {
                return 0f;
            }

            if (float.IsPositiveInfinity(distanceOutsideTrodden))
            {
                return 1f;
            }

            float amount = Mathf.InverseLerp(
                TrailCalmDistance,
                TrailFullStrengthDistance,
                distanceOutsideTrodden);
            return Mathf.SmoothStep(0f, 1f, amount);
        }

        public static float EvaluateRearClosure(float metresBehindRearWall)
        {
            if (float.IsNaN(metresBehindRearWall) ||
                float.IsNegativeInfinity(metresBehindRearWall))
            {
                return 0f;
            }

            if (float.IsPositiveInfinity(metresBehindRearWall))
            {
                return 1f;
            }

            float amount = Mathf.Clamp01(
                metresBehindRearWall / RearClosureFullDistance);
            return Mathf.SmoothStep(0f, 1f, amount);
        }

        public static float ComposeStrength(
            float trailExposure01,
            float apertureProtection01,
            float rearClosure01)
        {
            float sideStrength = ClampFinite01(trailExposure01) *
                                 (1f - ClampFinite01(
                                     apertureProtection01));
            return Mathf.Clamp01(Mathf.Max(
                sideStrength,
                ClampFinite01(rearClosure01)));
        }

        private static float ClampFinite01(float value)
        {
            if (float.IsNaN(value) || float.IsNegativeInfinity(value))
            {
                return 0f;
            }

            return float.IsPositiveInfinity(value)
                ? 1f
                : Mathf.Clamp01(value);
        }
    }

    /// <summary>
    /// Pure, immutable spatial plan for side whiteout and the closure behind
    /// the mother's house. It snapshots every path generated by
    /// <see cref="AlpineVillagePathPlanner"/> so all queries measure the same
    /// complete trodden network.
    /// </summary>
    public sealed class AlpineVillagePeripheralStormPlan
    {
        private readonly AlpineVillagePlan village;
        private readonly ReadOnlyCollection<AlpineVillagePathDescriptor>
            paths;
        private readonly Vector2 apertureRight;

        private AlpineVillagePeripheralStormPlan(
            AlpineVillagePlan villagePlan,
            IList<AlpineVillagePathDescriptor> sourcePaths)
        {
            village = villagePlan ??
                      throw new ArgumentNullException(nameof(villagePlan));
            paths = new ReadOnlyCollection<AlpineVillagePathDescriptor>(
                new List<AlpineVillagePathDescriptor>(sourcePaths));

            AlpineVillagePlotDescriptor house = village.MothersHouse;
            Vector2 houseFacing = ToXZ(house.Facing).normalized;
            RearDirection = -houseFacing;
            RearWallCenter = ToXZ(house.GroundCenter) +
                             RearDirection *
                             (house.FootprintSize.y * 0.5f);

            ApertureStart = ToXZ(village.Station.PadArea.Center);
            Vector2 toRearWall = RearWallCenter - ApertureStart;
            if (toRearWall.sqrMagnitude <= 0.000001f)
            {
                throw new InvalidOperationException(
                    "The village landmark aperture has no length.");
            }

            ApertureDirection = toRearWall.normalized;
            apertureRight = new Vector2(
                ApertureDirection.y,
                -ApertureDirection.x);

            Vector2[] corners = CreateHouseCorners(house);
            float furthestAlong = Vector2.Dot(
                RearWallCenter - ApertureStart,
                ApertureDirection);
            for (int index = 0; index < corners.Length; index++)
            {
                furthestAlong = Mathf.Max(
                    furthestAlong,
                    Vector2.Dot(
                        corners[index] - ApertureStart,
                        ApertureDirection));
            }

            ApertureCoreLength = furthestAlong +
                                 AlpineVillagePeripheralStormRules
                                     .ApertureBackCorePadding;
            ApertureFarHalfWidth = ResolveFarHalfWidth(corners);
        }

        public AlpineVillagePlan Village => village;
        public IReadOnlyList<AlpineVillagePathDescriptor> Paths => paths;

        /// <summary>Centre of the station-side mouth of the clear cone.</summary>
        public Vector2 ApertureStart { get; }

        public Vector2 ApertureDirection { get; }
        public float ApertureCoreLength { get; }
        public float ApertureFarHalfWidth { get; }

        /// <summary>Centre of the mother's actual rear wall.</summary>
        public Vector2 RearWallCenter { get; }

        /// <summary>Out of the house toward the enclosing head ridge.</summary>
        public Vector2 RearDirection { get; }

        public static AlpineVillagePeripheralStormPlan Create(
            AlpineVillagePlan villagePlan)
        {
            if (villagePlan == null)
            {
                throw new ArgumentNullException(nameof(villagePlan));
            }

            IReadOnlyList<AlpineVillagePathDescriptor> generated =
                AlpineVillagePathPlanner.Create(villagePlan);
            var snapshot = new List<AlpineVillagePathDescriptor>(
                generated.Count);
            for (int index = 0; index < generated.Count; index++)
            {
                snapshot.Add(generated[index]);
            }

            return new AlpineVillagePeripheralStormPlan(
                villagePlan,
                snapshot);
        }

        public AlpineVillagePeripheralStormSample Evaluate(
            Vector3 worldPosition)
        {
            return Evaluate(ToXZ(worldPosition));
        }

        public AlpineVillagePeripheralStormSample Evaluate(Vector2 worldXZ)
        {
            if (!IsFinite(worldXZ))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(worldXZ),
                    "A peripheral storm query must be finite.");
            }

            float distance = AlpineVillagePathPlanner
                .MeasureDistanceOutsideTrodden(
                    village,
                    paths,
                    worldXZ,
                    out Vector2 outward);
            if (!IsFinite(distance))
            {
                distance = float.MaxValue;
            }

            if (!IsFinite(outward) || outward.sqrMagnitude <= 0.000001f)
            {
                outward = Vector2.up;
            }

            float trailExposure = AlpineVillagePeripheralStormRules
                .EvaluateTrailExposure(distance);
            float apertureProtection =
                EvaluateLandmarkApertureProtection(worldXZ);
            float behind = Vector2.Dot(
                worldXZ - RearWallCenter,
                RearDirection);
            float rearClosure = AlpineVillagePeripheralStormRules
                .EvaluateRearClosure(behind);
            float strength = AlpineVillagePeripheralStormRules
                .ComposeStrength(
                    trailExposure,
                    apertureProtection,
                    rearClosure);

            return new AlpineVillagePeripheralStormSample(
                distance,
                outward,
                trailExposure,
                apertureProtection,
                rearClosure,
                strength);
        }

        public float EvaluateLandmarkApertureProtection(Vector2 worldXZ)
        {
            Vector2 delta = worldXZ - ApertureStart;
            float along = Vector2.Dot(delta, ApertureDirection);
            if (!IsFinite(along) || along < 0f)
            {
                return 0f;
            }

            float coreAmount = Mathf.Clamp01(
                along / ApertureCoreLength);
            float innerHalfWidth = Mathf.Lerp(
                AlpineVillagePeripheralStormRules.ApertureNearHalfWidth,
                ApertureFarHalfWidth,
                coreAmount);
            float lateral = Mathf.Abs(Vector2.Dot(delta, apertureRight));
            if (!IsFinite(lateral))
            {
                return 0f;
            }

            float edgeAmount = Mathf.Clamp01(
                (lateral - innerHalfWidth) /
                AlpineVillagePeripheralStormRules.ApertureEdgeFeather);
            float sideProtection = 1f -
                                   Mathf.SmoothStep(0f, 1f, edgeAmount);
            float beyondCore = Mathf.Max(0f, along - ApertureCoreLength);
            float rearAmount = Mathf.Clamp01(
                beyondCore /
                AlpineVillagePeripheralStormRules
                    .ApertureRearFeatherDistance);
            float longitudinalProtection = 1f -
                Mathf.SmoothStep(0f, 1f, rearAmount);
            return Mathf.Clamp01(
                sideProtection * longitudinalProtection);
        }

        private float ResolveFarHalfWidth(IReadOnlyList<Vector2> corners)
        {
            float farHalfWidth =
                AlpineVillagePeripheralStormRules.ApertureNearHalfWidth;
            for (int index = 0; index < corners.Count; index++)
            {
                Vector2 delta = corners[index] - ApertureStart;
                float along = Mathf.Max(
                    0.0001f,
                    Vector2.Dot(delta, ApertureDirection));
                float amount = Mathf.Clamp01(
                    along / ApertureCoreLength);
                float lateral = Mathf.Abs(
                    Vector2.Dot(delta, apertureRight));
                float requiredAtCorner = lateral +
                    AlpineVillagePeripheralStormRules
                        .ApertureHousePadding;
                float requiredFar = amount <= 0.0001f
                    ? requiredAtCorner
                    : AlpineVillagePeripheralStormRules
                          .ApertureNearHalfWidth +
                      (requiredAtCorner -
                       AlpineVillagePeripheralStormRules
                           .ApertureNearHalfWidth) /
                      amount;
                farHalfWidth = Mathf.Max(farHalfWidth, requiredFar);
            }

            return farHalfWidth;
        }

        private static Vector2[] CreateHouseCorners(
            AlpineVillagePlotDescriptor house)
        {
            Vector2 center = ToXZ(house.GroundCenter);
            Vector2 forward = ToXZ(house.Facing).normalized;
            var right = new Vector2(forward.y, -forward.x);
            float halfWidth = house.FootprintSize.x * 0.5f;
            float halfDepth = house.FootprintSize.y * 0.5f;
            return new[]
            {
                center + right * halfWidth + forward * halfDepth,
                center + right * halfWidth - forward * halfDepth,
                center - right * halfWidth + forward * halfDepth,
                center - right * halfWidth - forward * halfDepth
            };
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
