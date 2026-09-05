using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One grave a mourner may visit: the slab's ground point,
    /// yaw and top height read back from the cemetery plan. Local +Z
    /// points from the foot of the grave toward its monument, away from
    /// the gate, so the mourner stands on the local -Z side.</summary>
    public readonly struct CemeteryGraveAnchor
    {
        /// <summary>A grave with no raised slab: the laid bouquet then
        /// rests on the ground point itself.</summary>
        public CemeteryGraveAnchor(
            int ordinal,
            Vector3 ground,
            Quaternion yaw)
            : this(ordinal, ground, yaw, ground.y)
        {
        }

        public CemeteryGraveAnchor(
            int ordinal,
            Vector3 ground,
            Quaternion yaw,
            float slabTopY)
        {
            Ordinal = ordinal;
            Ground = ground;
            Yaw = yaw;
            SlabTopY = slabTopY;
        }

        public int Ordinal { get; }
        public Vector3 Ground { get; }
        public Quaternion Yaw { get; }

        /// <summary>World height of the slab's top face: the six grave
        /// variants stand slabs of 0.08-0.16 m on the ground, and a
        /// bouquet laid at a guessed height floats over the thin ones or
        /// sinks into the thick one.</summary>
        public float SlabTopY { get; }
    }

    /// <summary>
    /// Pure geometry and selection rules of the cemetery mourner's
    /// visit: which graves qualify, where she stands, how she enters
    /// through the one gate, where she may spawn out of the hero's
    /// sight and when the hero counts as "near the cemetery". No scene
    /// state — everything here is EditMode-testable.
    /// </summary>
    public static class CemeteryMournerPlan
    {
        /// <summary>The hero this close to the grounds (or inside them)
        /// summons a mourner; the band is generous enough to cover the
        /// street along the fence.</summary>
        public const float TriggerInflationMeters = 28f;

        /// <summary>How far before the slab's ground point the mourner
        /// stands: outside even an enclosure's foot rail at local
        /// z = -1.40 with its posts.</summary>
        public const float StandBackMeters = 1.75f;

        /// <summary>Spawn rules mirrored from the pedestrian director:
        /// far enough away, or outside the camera's horizontal cone.</summary>
        public const float MinimumSpawnDistance = 76f;

        /// <summary>Cosine of the half-angle outside which a spawn
        /// point counts as unseen (about 80 degrees off centre).</summary>
        public const float SpawnViewCosine = 0.17365f;

        /// <summary>How far out on the street side of the fence line
        /// the spawn candidates and the outer gate waypoint sit.</summary>
        public const float StreetSetbackMeters = 7f;
        public const float GateOutsideMeters = 1.6f;
        public const float GateInsideMeters = 1.2f;

        /// <summary>The fence inset plus the main alley half width —
        /// the same clamp the cemetery planner applies to the gate's
        /// lateral coordinate, mirrored here so the route's spine leg
        /// stays on the authored gravel.</summary>
        private const float GateLateralClearance = 2.9f;

        private static readonly float[] SpawnOffsets =
        {
            26f, 38f, 50f, 62f, 76f, 90f
        };

        /// <summary>The cemetery's one street access, or false for a
        /// blueprint without a cemetery.</summary>
        public static bool TryGetAccess(
            CityLayout layout,
            out CityOpenAreaAccessDescriptor access)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                if (layout.OpenAreaAccesses[index].Feature ==
                    CityAreaFeatureKind.Cemetery)
                {
                    access = layout.OpenAreaAccesses[index];
                    return true;
                }
            }

            access = default;
            return false;
        }

        /// <summary>
        /// Every grave a mourner may visit: one anchor per grave slab,
        /// excluding graves with an оградка. The enclosure is a fully
        /// closed iron box with no gate, so a visitor could never
        /// plausibly reach the slab — she simply grieves elsewhere.
        /// </summary>
        public static List<CemeteryGraveAnchor> CollectCandidateGraves(
            CityCemeteryPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var enclosed = new HashSet<int>();
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CityCemeteryPartDescriptor part = plan.Parts[index];
                if (part.Kind == CityCemeteryPartKind.GraveEnclosure)
                {
                    enclosed.Add(part.GraveOrdinal);
                }
            }

            var anchors = new List<CemeteryGraveAnchor>(plan.GraveCount);
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CityCemeteryPartDescriptor part = plan.Parts[index];
                if (part.Kind != CityCemeteryPartKind.GraveSlab ||
                    enclosed.Contains(part.GraveOrdinal))
                {
                    continue;
                }

                // The slab is emitted at the grave's ground point with
                // a zero planar offset, so its centre carries the
                // grave position and its rotation the grave yaw; the
                // planner raises every part by half its height, so the
                // top face is half a size above the centre.
                var ground = new Vector3(
                    part.Center.x,
                    plan.GroundTopY,
                    part.Center.z);
                anchors.Add(new CemeteryGraveAnchor(
                    part.GraveOrdinal,
                    ground,
                    part.Rotation,
                    part.Center.y + part.Size.y * 0.5f));
            }

            return anchors;
        }

        /// <summary>Where the mourner stands and which way she faces:
        /// at the foot of the grave, looking at the monument.</summary>
        public static Vector3 ComputeStandPoint(
            CemeteryGraveAnchor anchor,
            float groundTopY)
        {
            Vector3 stand = anchor.Ground +
                anchor.Yaw * new Vector3(0f, 0f, -StandBackMeters);
            stand.y = groundTopY;
            return stand;
        }

        public static Vector3 ComputeStandFacing(
            CemeteryGraveAnchor anchor)
        {
            Vector3 facing = anchor.Yaw * Vector3.forward;
            facing.y = 0f;
            return facing.normalized;
        }

        /// <summary>Deterministic per-visit random stream: the same
        /// city seed and visit index always mourn at the same grave.</summary>
        public static uint CreateVisitRandomState(
            int citySeed,
            int visitIndex)
        {
            unchecked
            {
                uint state = ((uint)citySeed * 2654435761u) ^
                             ((uint)(visitIndex + 1) * 2246822519u);
                return state == 0u ? 0x9E3779B9u : state;
            }
        }

        public static uint NextRandomState(ref uint state)
        {
            uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return value;
        }

        public static int SelectGraveIndex(
            int candidateCount,
            ref uint randomState)
        {
            if (candidateCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(candidateCount));
            }

            return (int)(NextRandomState(ref randomState) %
                         (uint)candidateCount);
        }

        /// <summary>True while the hero counts as near the cemetery:
        /// inside the grounds or within the trigger band around them.</summary>
        public static bool IsInsideTriggerBand(
            Rect grounds,
            Vector2 playerXZ)
        {
            return Inflate(grounds, TriggerInflationMeters)
                .Contains(playerXZ);
        }

        /// <summary>
        /// A street spawn point honouring the director's spawn spirit:
        /// candidates slide along the street axis away from the gate on
        /// both sides, and the first one far enough from the hero or
        /// outside the camera's horizontal cone wins. A hero parked at
        /// the gate of a straight street can defeat both rules, so the
        /// most-behind-the-camera far candidate is the fallback.
        /// </summary>
        public static Vector3 SelectSpawnPoint(
            CityOpenAreaAccessDescriptor access,
            Vector3 playerPosition,
            Vector3 cameraPosition,
            Vector3 cameraForward)
        {
            Vector3 inward = FlattenedNormal(access.OutwardNormal);
            Vector3 lateral = Mathf.Abs(inward.x) > 0.5f
                ? Vector3.forward
                : Vector3.right;
            Vector3 streetBase =
                access.Center - inward * StreetSetbackMeters;
            Vector3 cameraFlat = new Vector3(
                cameraForward.x, 0f, cameraForward.z);
            cameraFlat = cameraFlat.sqrMagnitude > 0.0001f
                ? cameraFlat.normalized
                : Vector3.forward;

            Vector3 fallback = streetBase +
                lateral * SpawnOffsets[SpawnOffsets.Length - 1];
            float fallbackDot = float.MaxValue;
            for (int index = 0; index < SpawnOffsets.Length; index++)
            {
                for (int side = 0; side < 2; side++)
                {
                    float sign = side == 0 ? 1f : -1f;
                    Vector3 candidate = streetBase +
                        lateral * (SpawnOffsets[index] * sign);
                    Vector3 toCandidate = candidate - cameraPosition;
                    toCandidate.y = 0f;
                    float viewDot = Vector3.Dot(
                        toCandidate.normalized,
                        cameraFlat);
                    float planarDistance = Vector2.Distance(
                        new Vector2(candidate.x, candidate.z),
                        new Vector2(playerPosition.x, playerPosition.z));
                    if (planarDistance >= MinimumSpawnDistance ||
                        viewDot < SpawnViewCosine)
                    {
                        return candidate;
                    }

                    if (index == SpawnOffsets.Length - 1 &&
                        viewDot < fallbackDot)
                    {
                        fallbackDot = viewDot;
                        fallback = candidate;
                    }
                }
            }

            return fallback;
        }

        /// <summary>
        /// The walk-in polyline: spawn point on the street, the strip
        /// before the gate, the gate threshold, the main alley's spine
        /// up to the grave's row and the sideways step to the stand
        /// point. Outside the fence the height follows the terrain;
        /// inside it is the cemetery's own ground top.
        /// </summary>
        public static Vector3[] BuildApproachRoute(
            CityLayout layout,
            CityOpenAreaAccessDescriptor access,
            CityCemeteryPlan plan,
            Vector3 spawnPoint,
            Vector3 standPoint)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Vector3 inward = FlattenedNormal(access.OutwardNormal);
            bool alongX = Mathf.Abs(inward.x) > 0.5f;
            float inwardSign = alongX
                ? Mathf.Sign(inward.x)
                : Mathf.Sign(inward.z);
            float gateEdge = alongX ? access.Center.x : access.Center.z;
            float accessLateral = alongX
                ? access.Center.z
                : access.Center.x;
            float lateralMinimum = alongX
                ? plan.Grounds.yMin
                : plan.Grounds.xMin;
            float lateralMaximum = alongX
                ? plan.Grounds.yMax
                : plan.Grounds.xMax;
            // The same clamp the cemetery planner applies to the main
            // alley, so the spine leg walks the authored gravel.
            float gateLateral = Mathf.Clamp(
                accessLateral,
                lateralMinimum + GateLateralClearance,
                lateralMaximum - GateLateralClearance);

            float standDepthCoordinate = alongX
                ? standPoint.x
                : standPoint.z;

            Vector3 outerGate = Compose(
                alongX,
                gateEdge - inwardSign * GateOutsideMeters,
                accessLateral,
                SampleStreetHeight(layout, access, spawnPoint));
            outerGate.y = SampleStreetHeight(layout, access, outerGate);
            Vector3 innerGate = Compose(
                alongX,
                gateEdge + inwardSign * GateInsideMeters,
                gateLateral,
                plan.GroundTopY);
            Vector3 rowJunction = Compose(
                alongX,
                standDepthCoordinate,
                gateLateral,
                plan.GroundTopY);

            return new[]
            {
                spawnPoint,
                outerGate,
                innerGate,
                rowJunction,
                new Vector3(standPoint.x, plan.GroundTopY, standPoint.z)
            };
        }

        public static float ComputeRouteLength(
            IReadOnlyList<Vector3> route)
        {
            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            float length = 0f;
            for (int index = 1; index < route.Count; index++)
            {
                length += Vector3.Distance(
                    route[index - 1],
                    route[index]);
            }

            return length;
        }

        /// <summary>Position along the polyline at the travelled
        /// distance, clamped to its ends, with the current segment's
        /// planar direction for facing.</summary>
        public static Vector3 EvaluateRoute(
            IReadOnlyList<Vector3> route,
            float travelledDistance,
            out Vector3 direction)
        {
            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            if (route.Count == 0)
            {
                throw new ArgumentException(
                    "A route needs at least one point.",
                    nameof(route));
            }

            direction = Vector3.forward;
            if (route.Count == 1)
            {
                return route[0];
            }

            float remaining = Mathf.Max(0f, travelledDistance);
            for (int index = 1; index < route.Count; index++)
            {
                Vector3 from = route[index - 1];
                Vector3 to = route[index];
                float segment = Vector3.Distance(from, to);
                Vector3 planar = to - from;
                planar.y = 0f;
                if (planar.sqrMagnitude > 0.0001f)
                {
                    direction = planar.normalized;
                }

                if (segment > 0.0001f && remaining <= segment)
                {
                    return Vector3.Lerp(from, to, remaining / segment);
                }

                remaining -= segment;
            }

            return route[route.Count - 1];
        }

        public static Vector3[] ReverseRoute(IReadOnlyList<Vector3> route)
        {
            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            var reversed = new Vector3[route.Count];
            for (int index = 0; index < route.Count; index++)
            {
                reversed[index] = route[route.Count - 1 - index];
            }

            return reversed;
        }

        /// <summary>Street-side height: the sampled terrain top where
        /// available, the access centre's own height otherwise.</summary>
        private static float SampleStreetHeight(
            CityLayout layout,
            CityOpenAreaAccessDescriptor access,
            Vector3 point)
        {
            if (CityTerrainSurfacePlan.TrySampleGroundTop(
                    layout,
                    new Vector2(point.x, point.z),
                    out float terrainTop,
                    out _))
            {
                return terrainTop;
            }

            return access.Center.y;
        }

        private static Vector3 Compose(
            bool alongX,
            float depthCoordinate,
            float lateral,
            float y)
        {
            return alongX
                ? new Vector3(depthCoordinate, y, lateral)
                : new Vector3(lateral, y, depthCoordinate);
        }

        private static Vector3 FlattenedNormal(Vector3 normal)
        {
            var flattened = new Vector3(normal.x, 0f, normal.z);
            return flattened.sqrMagnitude > 0.0001f
                ? flattened.normalized
                : Vector3.forward;
        }

        private static Rect Inflate(Rect source, float amount)
        {
            return new Rect(
                source.x - amount,
                source.y - amount,
                source.width + amount * 2f,
                source.height + amount * 2f);
        }
    }
}
