using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One pure terrain-height contract shared by planning, validation and
    /// mesh construction. The soil is deliberately sunk below the road skin;
    /// the asphalt never relies on a coplanar terrain cutout.
    /// </summary>
    internal static class MountainRoadTerrainSampler
    {
        internal const float RoadBedClearance = 0.24f;
        internal const float PlateauExteriorBlendDistance = 12f;

        internal static float SampleHeight(
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau,
            Vector2 point)
        {
            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            if (plateau == null)
            {
                throw new ArgumentNullException(nameof(plateau));
            }

            if (plateau.Contains(point))
            {
                return plateau.Center.y - RoadBedClearance;
            }

            MountainRoadRouteSample start = route.Samples[0];
            Vector2 startXZ = new Vector2(
                start.Position.x,
                start.Position.z);
            Vector2 tunnelAxis = new Vector2(
                start.Forward.x,
                start.Forward.z).normalized;
            Vector2 tunnelRight = new Vector2(
                tunnelAxis.y,
                -tunnelAxis.x);
            Vector2 tunnelDelta = point - startXZ;
            float tunnelProgress = Vector2.Dot(tunnelDelta, tunnelAxis);
            float tunnelLateral = Mathf.Abs(Vector2.Dot(
                tunnelDelta,
                tunnelRight));
            if (tunnelProgress <= 0f &&
                tunnelProgress >= -MountainRoadPlanner.TunnelPhysicalDepth &&
                tunnelLateral <=
                CityMountainBoundaryDefinition.TunnelOpeningWidth * 0.5f)
            {
                return start.Position.y - RoadBedClearance;
            }

            FindClosest(
                route,
                point,
                true,
                out float distance,
                out Vector3 center,
                out Vector3 right,
                out float halfWidth);
            Vector2 delta = point - new Vector2(center.x, center.z);
            float signedLateral = Vector2.Dot(
                delta,
                new Vector2(right.x, right.z));
            float shoulderDistance = Mathf.Max(0f, distance - halfWidth);
            float worldSide = Mathf.Sign(signedLateral) * right.x;
            float bankSlope = worldSide >= 0f ? 0.22f : -0.18f;
            float roadBank = center.y - RoadBedClearance +
                             shoulderDistance * bankSlope;

            float horizontalRise = Mathf.Max(
                1f,
                route.End.x - route.Start.x);
            float macro = route.Start.y +
                          (point.x - route.Start.x) *
                          (route.ElevationGain / horizontalRise) +
                          point.y * 0.012f -
                          0.55f;
            float undulation = Mathf.Sin(point.x * 0.31f + point.y * 0.17f) *
                               0.20f +
                               Mathf.Sin(point.x * -0.11f + point.y * 0.27f) *
                               0.12f;
            float blend = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(4.8f, 7f, distance));
            float terrain = Mathf.Lerp(
                roadBank,
                macro + undulation,
                blend);
            terrain = ApplyBridgeGorge(route.Bridge, point, terrain);
            float plateauDistance = DistanceToPolygonEdge(
                plateau.VerticesXZ,
                point);
            if (plateauDistance >= PlateauExteriorBlendDistance)
            {
                return ApplyBrinkFall(plateau, point, terrain);
            }

            float exteriorBlend = Mathf.SmoothStep(
                0f,
                1f,
                plateauDistance / PlateauExteriorBlendDistance);
            float plateauBlended = Mathf.Lerp(
                plateau.Center.y - RoadBedClearance,
                terrain,
                exteriorBlend);
            float shoulderBlend = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    halfWidth + 0.3f,
                    halfWidth + 2f,
                    distance));
            return ApplyBrinkFall(
                plateau,
                point,
                Mathf.Lerp(terrain, plateauBlended, shoulderBlend));
        }

        /// <summary>
        /// Takes the ground away inside the brink's view corridor.
        ///
        /// It runs on the FINAL height rather than on the intermediate
        /// macro terrain, and that ordering is the whole of it: applied
        /// earlier, the plateau's own exterior blend would lift the cut
        /// straight back up to pad height over the twelve metres nearest
        /// the rim - which is exactly the stretch the cliff is made of.
        /// The interior early return above still answers first, so the
        /// pad, its seam with the road and the surface the car drives on
        /// are untouched by construction rather than by tolerance.
        /// </summary>
        private static float ApplyBrinkFall(
            MountainRoadPlateauDescriptor plateau,
            Vector2 point,
            float height)
        {
            MountainRoadBrinkDescriptor brink = plateau.Brink;
            if (brink == null)
            {
                return height;
            }

            float weight = brink.Corridor.Weight(
                point,
                brink.EdgeBlendDistance);
            if (weight <= 0f)
            {
                return height;
            }

            return height - brink.DropDepth * weight;
        }

        private static float DistanceToPolygonEdge(
            System.Collections.Generic.IReadOnlyList<Vector2> polygon,
            Vector2 point)
        {
            float bestSquared = float.PositiveInfinity;
            for (int index = 0; index < polygon.Count; index++)
            {
                Vector2 first = polygon[index];
                Vector2 second = polygon[(index + 1) % polygon.Count];
                Vector2 segment = second - first;
                float denominator = segment.sqrMagnitude;
                float amount = denominator <= 0.000001f
                    ? 0f
                    : Mathf.Clamp01(
                        Vector2.Dot(point - first, segment) / denominator);
                Vector2 closest = Vector2.Lerp(first, second, amount);
                bestSquared = Mathf.Min(
                    bestSquared,
                    (point - closest).sqrMagnitude);
            }

            return Mathf.Sqrt(bestSquared);
        }

        internal static void FindClosest(
            MountainRoadRoutePlan route,
            Vector2 point,
            out float distance,
            out Vector3 center,
            out Vector3 right,
            out float halfWidth)
        {
            FindClosest(
                route,
                point,
                false,
                out distance,
                out center,
                out right,
                out halfWidth);
        }

        /// <summary>
        /// The nearest route segment, found the way the terrain grid always
        /// found it - the first segment in route order whose point distance
        /// is strictly below every earlier one - but without visiting all
        /// six hundred segments per vertex. The segments sit in
        /// <see cref="RouteIndex"/> as run-length chunks with planar bounds;
        /// a chunk whose box is provably farther than the best answer can
        /// contain no segment that would ever have passed the strict
        /// comparison, so leaving it out changes no bit of the result.
        /// The scan otherwise runs the original body over the original
        /// order with the original comparison, and the pose of the winner
        /// is evaluated once at the end instead of at every provisional
        /// improvement: the old loop overwrote it on each one, so only the
        /// last evaluation ever reached the caller.
        /// </summary>
        private static void FindClosest(
            MountainRoadRoutePlan route,
            Vector2 point,
            bool skipBridgeSegments,
            out float distance,
            out Vector3 center,
            out Vector3 right,
            out float halfWidth)
        {
            RouteIndex index = RouteIndex.For(route);
            RouteSegment[] segments = index.Segments;
            RouteChunk[] chunks = skipBridgeSegments
                ? index.LandChunks
                : index.AllChunks;
            float px = point.x;
            float pz = point.y;

            // Every eligible segment of a chunk lies inside the chunk's box,
            // so the far corner of any non-empty box bounds the nearest
            // distance from above. Without this the first chunk at the
            // portal would set the bar, and for a vertex by the terminal
            // every chunk between would survive the box test.
            float upperSqr = float.PositiveInfinity;
            for (int chunk = 0; chunk < chunks.Length; chunk++)
            {
                ref RouteChunk box = ref chunks[chunk];
                if (box.Count == 0)
                {
                    continue;
                }

                float farX = Mathf.Max(px - box.MinX, box.MaxX - px);
                float farZ = Mathf.Max(pz - box.MinZ, box.MaxZ - pz);
                float farSqr = farX * farX + farZ * farZ;
                if (farSqr < upperSqr)
                {
                    upperSqr = farSqr;
                }
            }

            float reach = Mathf.Sqrt(upperSqr) + RouteIndex.PruneSafety;
            float reachSqr = reach * reach;
            float reachBestSqr = float.PositiveInfinity;
            float bestSqr = float.PositiveInfinity;
            float bestT = 0f;
            int bestSegment = -1;
            for (int chunk = 0; chunk < chunks.Length; chunk++)
            {
                ref RouteChunk box = ref chunks[chunk];
                if (box.Count == 0)
                {
                    continue;
                }

                if (bestSqr < reachBestSqr)
                {
                    reachBestSqr = bestSqr;
                    float tightened = Mathf.Sqrt(bestSqr) +
                                      RouteIndex.PruneSafety;
                    float tightenedSqr = tightened * tightened;
                    if (tightenedSqr < reachSqr)
                    {
                        reachSqr = tightenedSqr;
                    }
                }

                float nearX = Mathf.Max(
                    Mathf.Max(box.MinX - px, px - box.MaxX),
                    0f);
                float nearZ = Mathf.Max(
                    Mathf.Max(box.MinZ - pz, pz - box.MaxZ),
                    0f);
                if (nearX * nearX + nearZ * nearZ > reachSqr)
                {
                    continue;
                }

                int last = Mathf.Min(
                    box.First + RouteIndex.ChunkSize,
                    segments.Length);
                for (int segment = box.First; segment < last; segment++)
                {
                    ref RouteSegment candidate = ref segments[segment];
                    if (skipBridgeSegments && candidate.InsideBridge)
                    {
                        continue;
                    }

                    float denominator = candidate.Denominator;
                    float t = denominator <= 0.000001f
                        ? 0f
                        : Mathf.Clamp01(
                            Vector2.Dot(point - candidate.A, candidate.AB) /
                            denominator);
                    Vector2 closest = Vector2.Lerp(
                        candidate.A,
                        candidate.B,
                        t);
                    float sqr = (point - closest).sqrMagnitude;
                    if (sqr >= bestSqr)
                    {
                        continue;
                    }

                    bestSqr = sqr;
                    bestT = t;
                    bestSegment = segment;
                }
            }

            center = route.Start;
            right = route.Samples[0].Right;
            halfWidth = route.Samples[0].Width * 0.5f;
            if (bestSegment >= 0)
            {
                MountainRoadRouteSample first = route.Samples[bestSegment];
                MountainRoadRouteSample second =
                    route.Samples[bestSegment + 1];
                center = Vector3.Lerp(first.Position, second.Position, bestT);
                Vector3 forward = Vector3.Slerp(
                    first.Forward,
                    second.Forward,
                    bestT).normalized;
                right = Vector3.Cross(Vector3.up, forward).normalized;
                halfWidth = Mathf.Lerp(first.Width, second.Width, bestT) *
                            0.5f;
            }

            distance = Mathf.Sqrt(bestSqr);
        }

        /// <summary>
        /// One route segment as the nearest-segment scan reads it. The
        /// planar endpoints, their difference and its squared length are the
        /// very values the scan used to rebuild from the samples on every
        /// vertex; computed once by the same expressions they are the same
        /// floats, so the per-vertex arithmetic downstream is unchanged.
        /// </summary>
        private readonly struct RouteSegment
        {
            internal RouteSegment(
                Vector2 a,
                Vector2 b,
                Vector2 ab,
                float denominator,
                bool insideBridge)
            {
                A = a;
                B = b;
                AB = ab;
                Denominator = denominator;
                InsideBridge = insideBridge;
            }

            internal readonly Vector2 A;
            internal readonly Vector2 B;
            internal readonly Vector2 AB;
            internal readonly float Denominator;

            /// <summary>
            /// True where the terrain scan skips the segment because the
            /// gorge, not the deck, owns the ground under it.
            /// </summary>
            internal readonly bool InsideBridge;
        }

        /// <summary>
        /// The planar box around one run of consecutive segments, holding
        /// only the segments a scan mode may visit. Consecutive runs keep
        /// the visiting order equal to the route order, which the strict
        /// first-wins comparison depends on.
        /// </summary>
        private struct RouteChunk
        {
            internal int First;
            internal int Count;
            internal float MinX;
            internal float MinZ;
            internal float MaxX;
            internal float MaxZ;
        }

        /// <summary>
        /// Per-route acceleration data, built once per route plan and held
        /// by reference to it. The plan is immutable, so the index can never
        /// go stale; the cache keeps one slot because every consumer walks
        /// a single route at a time, and even a caller alternating between
        /// two plans pays a rebuild that costs no more than the linear scan
        /// it replaces.
        /// </summary>
        private sealed class RouteIndex
        {
            internal const int ChunkSize = 16;

            /// <summary>
            /// Metres a chunk box must stand beyond the best distance before
            /// the chunk is skipped. The box test is exact geometry and the
            /// scan is rounded float arithmetic; half a metre is thousands
            /// of ulps at any coordinate the mountain uses, so no segment
            /// the rounded scan could have preferred is ever left out.
            /// </summary>
            internal const float PruneSafety = 0.5f;

            private static RouteIndex cached;

            private RouteIndex(MountainRoadRoutePlan route)
            {
                Route = route;
                IReadOnlyList<MountainRoadRouteSample> samples = route.Samples;
                int count = Mathf.Max(0, samples.Count - 1);
                Segments = new RouteSegment[count];
                float bridgeStart = route.Bridge.StartDistance + 0.05f;
                float bridgeEnd = route.Bridge.EndDistance - 0.05f;
                for (int index = 1; index < samples.Count; index++)
                {
                    MountainRoadRouteSample first = samples[index - 1];
                    MountainRoadRouteSample second = samples[index];
                    float segmentDistance =
                        (first.Distance + second.Distance) * 0.5f;
                    Vector2 a = new Vector2(
                        first.Position.x,
                        first.Position.z);
                    Vector2 b = new Vector2(
                        second.Position.x,
                        second.Position.z);
                    Vector2 ab = b - a;
                    Segments[index - 1] = new RouteSegment(
                        a,
                        b,
                        ab,
                        ab.sqrMagnitude,
                        segmentDistance > bridgeStart &&
                        segmentDistance < bridgeEnd);
                }

                AllChunks = BuildChunks(Segments, false);
                LandChunks = BuildChunks(Segments, true);
            }

            internal MountainRoadRoutePlan Route { get; }
            internal RouteSegment[] Segments { get; }
            internal RouteChunk[] AllChunks { get; }
            internal RouteChunk[] LandChunks { get; }

            internal static RouteIndex For(MountainRoadRoutePlan route)
            {
                RouteIndex index = cached;
                if (index == null || !ReferenceEquals(index.Route, route))
                {
                    index = new RouteIndex(route);
                    cached = index;
                }

                return index;
            }

            private static RouteChunk[] BuildChunks(
                RouteSegment[] segments,
                bool skipBridgeSegments)
            {
                int chunkCount = (segments.Length + ChunkSize - 1) / ChunkSize;
                var chunks = new RouteChunk[chunkCount];
                for (int chunk = 0; chunk < chunkCount; chunk++)
                {
                    RouteChunk box = new RouteChunk
                    {
                        First = chunk * ChunkSize,
                        Count = 0,
                        MinX = float.PositiveInfinity,
                        MinZ = float.PositiveInfinity,
                        MaxX = float.NegativeInfinity,
                        MaxZ = float.NegativeInfinity
                    };
                    int last = Mathf.Min(
                        box.First + ChunkSize,
                        segments.Length);
                    for (int segment = box.First; segment < last; segment++)
                    {
                        ref RouteSegment candidate = ref segments[segment];
                        if (skipBridgeSegments && candidate.InsideBridge)
                        {
                            continue;
                        }

                        box.Count++;
                        box.MinX = Mathf.Min(
                            box.MinX,
                            Mathf.Min(candidate.A.x, candidate.B.x));
                        box.MinZ = Mathf.Min(
                            box.MinZ,
                            Mathf.Min(candidate.A.y, candidate.B.y));
                        box.MaxX = Mathf.Max(
                            box.MaxX,
                            Mathf.Max(candidate.A.x, candidate.B.x));
                        box.MaxZ = Mathf.Max(
                            box.MaxZ,
                            Mathf.Max(candidate.A.y, candidate.B.y));
                    }

                    chunks[chunk] = box;
                }

                return chunks;
            }
        }

        private static float ApplyBridgeGorge(
            MountainRoadBridgeDescriptor bridge,
            Vector2 point,
            float terrainHeight)
        {
            Vector2 start = new Vector2(bridge.Start.x, bridge.Start.z);
            Vector2 forward = new Vector2(
                bridge.Forward.x,
                bridge.Forward.z);
            Vector2 right = new Vector2(bridge.Right.x, bridge.Right.z);
            Vector2 delta = point - start;
            float along = Vector2.Dot(delta, forward);
            if (along <= 0f || along >= bridge.Length)
            {
                return terrainHeight;
            }

            float lateral = Mathf.Abs(Vector2.Dot(delta, right));
            if (lateral >= bridge.GorgeHalfWidth)
            {
                return terrainHeight;
            }

            float enter = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(along / bridge.AbutmentBlendLength));
            float exit = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(
                    (bridge.Length - along) /
                    bridge.AbutmentBlendLength));
            float lateralCore = bridge.DeckWidth * 0.5f + 1.2f;
            float lateralWeight = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    lateralCore,
                    bridge.GorgeHalfWidth,
                    lateral));
            float weight = Mathf.Min(enter, exit) * lateralWeight;
            float floor = bridge.GorgeFloorY +
                          Mathf.Sin(point.x * 0.29f + point.y * 0.21f) *
                          0.35f;
            return Mathf.Lerp(terrainHeight, floor, weight);
        }
    }
}
