using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What a village plot is for. The kind drives dressing and nothing else;
    /// every plot is placed, sized and grounded the same way.
    /// </summary>
    public enum AlpineVillagePlotKind
    {
        House = 0,

        /// <summary>
        /// The one at the top of the lane. Exactly one plot carries this kind,
        /// and the validator refuses a plan where that is not true.
        /// </summary>
        MothersHouse = 1,

        /// <summary>The chapel over the source, on a side spur.</summary>
        Chapel = 2,

            // `3` and `4` were the adit and the burial ground. Both are gone
        // from the village and from the story, by the lead's decision; the
        // numbers stay as holes exactly as the deleted city lake's did, so
        // nothing that ever wrote one down reads a different place back.

        /// <summary>
        /// Where the water comes out of the hill. The chapel further down
        /// stands over the same water's outlet - this is its head.
        /// </summary>
        Spring = 5
    }

    /// <summary>
    /// One point on the village lane. The lane is the only route through the
    /// village and the only thing the walkable mask is built from, so a sample
    /// carries its own width rather than trusting a constant.
    /// </summary>
    public readonly struct AlpineVillageLaneSample
    {
        internal AlpineVillageLaneSample(
            float distance,
            Vector3 position,
            Vector3 forward,
            float width)
        {
            Distance = distance;
            Position = position;
            Forward = forward;
            Width = width;
        }

        public float Distance { get; }

        /// <summary>Ground point on the lane centreline.</summary>
        public Vector3 Position { get; }

        /// <summary>Uphill along the lane.</summary>
        public Vector3 Forward { get; }

        public Vector3 Right => Vector3.Cross(Vector3.up, Forward).normalized;
        public float Width { get; }
    }

    /// <summary>
    /// The single crooked street, from the cableway station up to the mother's
    /// house. Sampled by arc length so the mask, the surface mesh and every
    /// plot agree on where the middle of the road is.
    /// </summary>
    public sealed class AlpineVillageLanePlan
    {
        private readonly ReadOnlyCollection<AlpineVillageLaneSample> samples;
        private AlpineVillagePolylineIndex nearestIndex;
        private float maximumWidth = float.NaN;

        internal AlpineVillageLanePlan(
            IList<AlpineVillageLaneSample> sourceSamples,
            float length)
        {
            if (sourceSamples == null)
            {
                throw new ArgumentNullException(nameof(sourceSamples));
            }

            if (sourceSamples.Count < 2)
            {
                throw new ArgumentException(
                    "A village lane needs at least two samples.",
                    nameof(sourceSamples));
            }

            samples = new ReadOnlyCollection<AlpineVillageLaneSample>(
                new List<AlpineVillageLaneSample>(sourceSamples));
            Length = length;
        }

        public IReadOnlyList<AlpineVillageLaneSample> Samples => samples;
        public float Length { get; }

        /// <summary>The lane foot, on the station terrace.</summary>
        public Vector3 Start => samples[0].Position;

        /// <summary>The lane head, at the mother's door.</summary>
        public Vector3 End => samples[samples.Count - 1].Position;

        public float ElevationGain => End.y - Start.y;

        /// <summary>
        /// Average grade as a fraction. The whole point of the place is that
        /// this stays gentle, so the validator reads it and the tests pin it.
        /// </summary>
        public float AverageGrade => Length <= 0.0001f
            ? 0f
            : ElevationGain / Length;

        public AlpineVillageLaneSample Sample(float distance)
        {
            Locate(
                distance,
                out float clamped,
                out AlpineVillageLaneSample first,
                out AlpineVillageLaneSample second,
                out float amount);
            return new AlpineVillageLaneSample(
                clamped,
                Vector3.Lerp(first.Position, second.Position, amount),
                Vector3.Slerp(first.Forward, second.Forward, amount)
                    .normalized,
                Mathf.Lerp(first.Width, second.Width, amount));
        }

        /// <summary>
        /// The centreline height and the width at a distance - what the
        /// terrain sampler reads of a sample, and nothing else. It is
        /// <see cref="Sample"/> without the forward: the same search, the
        /// same lerps, and no <c>Vector3.Slerp</c>, which is a native call
        /// per ground vertex spent on a direction nobody then looks at.
        /// </summary>
        internal void SampleGround(
            float distance,
            out float centreHeight,
            out float width)
        {
            Locate(
                distance,
                out _,
                out AlpineVillageLaneSample first,
                out AlpineVillageLaneSample second,
                out float amount);
            centreHeight = Vector3.Lerp(
                first.Position,
                second.Position,
                amount).y;
            width = Mathf.Lerp(first.Width, second.Width, amount);
        }

        private void Locate(
            float distance,
            out float clamped,
            out AlpineVillageLaneSample first,
            out AlpineVillageLaneSample second,
            out float amount)
        {
            clamped = Mathf.Clamp(distance, 0f, Length);
            int low = 0;
            int high = samples.Count - 1;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (samples[middle].Distance <= clamped)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            first = samples[low];
            second = samples[high];
            float span = Mathf.Max(
                0.0001f,
                second.Distance - first.Distance);
            amount = Mathf.Clamp01(
                (clamped - first.Distance) / span);
        }

        /// <summary>
        /// The widest the lane gets anywhere, or positive infinity when any
        /// width is not a number. A bound for readers that want to know
        /// whether the lane can reach a point at all before they ask exactly
        /// where it is; an unbounded one tells them nothing, which is the
        /// right answer for a lane with no width in it.
        /// </summary>
        internal float MaximumWidth
        {
            get
            {
                if (float.IsNaN(maximumWidth))
                {
                    float widest = 0f;
                    for (int index = 0; index < samples.Count; index++)
                    {
                        float width = samples[index].Width;
                        if (float.IsNaN(width) || float.IsInfinity(width))
                        {
                            widest = float.PositiveInfinity;
                            break;
                        }

                        widest = Mathf.Max(widest, width);
                    }

                    maximumWidth = widest;
                }

                return maximumWidth;
            }
        }

        /// <summary>
        /// Pruning data over the centreline on the ground plane, built on
        /// first use. The samples never change after construction, so the
        /// index never goes stale.
        /// </summary>
        internal AlpineVillagePolylineIndex NearestIndex
        {
            get
            {
                if (nearestIndex == null)
                {
                    var points = new Vector2[samples.Count];
                    for (int index = 0; index < points.Length; index++)
                    {
                        Vector3 position = samples[index].Position;
                        points[index] = new Vector2(position.x, position.z);
                    }

                    nearestIndex = new AlpineVillagePolylineIndex(points);
                }

                return nearestIndex;
            }
        }

        /// <summary>
        /// Distance along the lane of the nearest centreline point, and how
        /// far the query sits from it on the ground plane.
        ///
        /// The answer is the EARLIEST segment with the smallest computed
        /// lateral, and that is a contract the terrain hashes rest on. The
        /// loop below is the original scan in the original order with the
        /// original per-segment arithmetic; the index only lets it skip
        /// runs of segments that provably cannot beat a lateral it has
        /// already computed - see <see cref="AlpineVillagePolylineIndex"/>
        /// for why that cannot move the winner. The first bound comes from
        /// the segment under the query's grid cell; it is a bound and not a
        /// record, so the record still starts empty and fills in index
        /// order, and a tie between that segment and an earlier one still
        /// goes to the earlier one.
        /// </summary>
        public float FindNearest(Vector2 pointXZ, out float lateralDistance)
        {
            AlpineVillagePolylineIndex index = NearestIndex;
            float pruneBound = float.PositiveInfinity;
            int guess = index.GuessSegment(pointXZ);
            if (guess >= 0)
            {
                pruneBound = LateralTo(guess, pointXZ, out _);
            }

            float bestDistance = 0f;
            float bestLateral = float.PositiveInfinity;
            int segmentCount = samples.Count - 1;
            for (int chunk = 0; chunk < index.ChunkCount; chunk++)
            {
                if (index.ChunkCannotWin(chunk, pointXZ, pruneBound))
                {
                    continue;
                }

                int end = Math.Min(
                    segmentCount,
                    (chunk + 1) * AlpineVillagePolylineIndex.ChunkSize);
                for (int segment = chunk * AlpineVillagePolylineIndex.ChunkSize;
                     segment < end;
                     segment++)
                {
                    if (index.CannotWin(segment, pointXZ, pruneBound))
                    {
                        continue;
                    }

                    float lateral = LateralTo(
                        segment,
                        pointXZ,
                        out float amount);
                    if (lateral >= bestLateral)
                    {
                        continue;
                    }

                    bestLateral = lateral;
                    bestDistance = Mathf.Lerp(
                        samples[segment].Distance,
                        samples[segment + 1].Distance,
                        amount);
                    if (lateral < pruneBound)
                    {
                        pruneBound = lateral;
                    }
                }
            }

            lateralDistance = bestLateral;
            return bestDistance;
        }

        /// <summary>
        /// The ground-plane distance from the query to one segment, and
        /// where along the segment the nearest point falls. This is the
        /// per-segment arithmetic of the scan, operation for operation.
        /// </summary>
        private float LateralTo(int index, Vector2 pointXZ, out float amount)
        {
            AlpineVillageLaneSample first = samples[index];
            AlpineVillageLaneSample second = samples[index + 1];
            Vector2 a = new Vector2(
                first.Position.x,
                first.Position.z);
            Vector2 b = new Vector2(
                second.Position.x,
                second.Position.z);
            Vector2 segment = b - a;
            float lengthSquared = segment.sqrMagnitude;
            amount = lengthSquared <= 0.000001f
                ? 0f
                : Mathf.Clamp01(
                    Vector2.Dot(pointXZ - a, segment) / lengthSquared);
            Vector2 closest = a + segment * amount;
            return (pointXZ - closest).magnitude;
        }
    }

    /// <summary>
    /// A building or feature standing beside the lane. Every world-space point
    /// a plot needs is solved once, here, so the builder never measures.
    /// </summary>
    public sealed class AlpineVillagePlotDescriptor
    {
        internal AlpineVillagePlotDescriptor(
            string stableId,
            AlpineVillagePlotKind kind,
            float laneDistance,
            int side,
            Vector3 groundCenter,
            Vector3 facing,
            Vector2 footprintSize,
            Vector3 doorGroundPosition,
            Vector3 doorDockPosition,
            float height)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            LaneDistance = laneDistance;
            Side = side;
            GroundCenter = groundCenter;
            Facing = facing.normalized;
            FootprintSize = footprintSize;
            DoorGroundPosition = doorGroundPosition;
            DoorDockPosition = doorDockPosition;
            Height = height;
        }

        public string StableId { get; }
        public AlpineVillagePlotKind Kind { get; }

        /// <summary>Where along the lane the plot is anchored.</summary>
        public float LaneDistance { get; }

        /// <summary>`-1` downhill-left of the lane, `+1` right.</summary>
        public int Side { get; }

        public Vector3 GroundCenter { get; }

        /// <summary>Outward, from the plot towards the lane.</summary>
        public Vector3 Facing { get; }

        public Vector2 FootprintSize { get; }
        public float Height { get; }

        /// <summary>The threshold, on the ground.</summary>
        public Vector3 DoorGroundPosition { get; }

        /// <summary>
        /// Where the hero stands to use the door. Kept in the plan because a
        /// dock further than the motor's vertical tolerance from his root is
        /// refused silently - the prompt shows and the key does nothing.
        /// </summary>
        public Vector3 DoorDockPosition { get; }

        /// <summary>
        /// Signed distance from the centre of the front wall to the actual
        /// door, along the building's local right axis. The world builder
        /// reads this for any door whose authored placement is plan-owned.
        /// </summary>
        public float DoorAcrossOffset
        {
            get
            {
                Vector3 frontCenter = GroundCenter +
                                      Facing * (FootprintSize.y * 0.5f);
                Vector3 right = Vector3.Cross(Vector3.up, Facing).normalized;
                return Vector3.Dot(
                    DoorGroundPosition - frontCenter,
                    right);
            }
        }

        /// <summary>
        /// Axis-aligned envelope of the actual rotated ground footprint.
        /// Precise overlap uses SAT in the validator; terrain bounds and map
        /// envelopes use this conservative rectangle.
        /// </summary>
        public Rect BoundsXZ
        {
            get
            {
                var forward = new Vector2(Facing.x, Facing.z).normalized;
                var right = new Vector2(forward.y, -forward.x);
                float halfWidth = FootprintSize.x * 0.5f;
                float halfDepth = FootprintSize.y * 0.5f;
                float extentX = Mathf.Abs(right.x) * halfWidth +
                                Mathf.Abs(forward.x) * halfDepth;
                float extentZ = Mathf.Abs(right.y) * halfWidth +
                                Mathf.Abs(forward.y) * halfDepth;
                return new Rect(
                    GroundCenter.x - extentX,
                    GroundCenter.z - extentZ,
                    extentX * 2f,
                    extentZ * 2f);
            }
        }
    }

    /// <summary>
    /// The upper terminal. Unlike the mountain terminal this is a RETURN
    /// station: bullwheel and tension weight, no motor and no reducer, because
    /// the drive is at the bottom of the line.
    /// </summary>
    public sealed class AlpineVillageStationPlan
    {
        internal AlpineVillageStationPlan(
            MountainRoadTerminalRect padArea,
            float padTopY,
            MountainRoadCablewayPlan cableway)
        {
            PadArea = padArea;
            PadTopY = padTopY;
            Cableway = cableway ??
                throw new ArgumentNullException(nameof(cableway));
        }

        public MountainRoadTerminalRect PadArea { get; }
        public float PadTopY { get; }
        public MountainRoadCablewayPlan Cableway { get; }

        // Boarding geometry is derived by the cableway plan and only read
        // here. Both terminals are the same building problem - a cabin
        // hanging a fixed height over a pad - and solving it twice is how the
        // two ends drift apart.

        /// <summary>
        /// The raised strip the hero steps off. It exists because the cabin
        /// floor hangs well above a bare pad and the step would otherwise be a
        /// climb rather than a threshold.
        /// </summary>
        public float PlatformTopY => Cableway.BoardingPlatformTopY;

        public Vector3 BoardingDockPosition => Cableway.BoardingDockPosition;
        public Vector3 BoardingFacing => Cableway.BoardingFacing;

        /// <summary>Height of the cabin floor over the boarding platform. A
        /// step, not a climb.</summary>
        public float BoardingStepHeight =>
            MountainRoadCablewayPlan.BoardingStepHeight;
    }

    /// <summary>
    /// The snow ridge closing the village on three sides. It is the reason
    /// "only by cableway" is true in geometry and not only in fiction.
    /// </summary>
    public readonly struct AlpineVillageRidgeDescriptor
    {
        internal AlpineVillageRidgeDescriptor(
            string stableId,
            Vector3 center,
            Vector3 forward,
            float length,
            float height,
            float thickness)
        {
            StableId = stableId ?? string.Empty;
            Center = center;
            Forward = forward.normalized;
            Length = length;
            Height = height;
            Thickness = thickness;
        }

        public string StableId { get; }
        public Vector3 Center { get; }
        public Vector3 Forward { get; }
        public Vector3 Right => Vector3.Cross(Vector3.up, Forward).normalized;
        public float Length { get; }
        public float Height { get; }
        public float Thickness { get; }
    }

    /// <summary>
    /// The village above the cableway, as pure validated data.
    ///
    /// One shape carries the whole place: a very gentle slope with one crooked
    /// lane climbing it, the cableway station on the lowest terrace and the
    /// mother's house on the highest shelf. Everything else hangs off a side
    /// spur, because the head of the lane belongs to the house.
    /// </summary>
    public sealed class AlpineVillagePlan
    {
        private readonly ReadOnlyCollection<AlpineVillagePlotDescriptor> plots;
        private readonly ReadOnlyCollection<AlpineVillageRidgeDescriptor>
            ridges;

        internal AlpineVillagePlan(
            int seed,
            Vector3 slopeOrigin,
            Vector3 uphill,
            float grade,
            AlpineVillageLanePlan lane,
            AlpineVillageStationPlan station,
            AlpineVillagePlotDescriptor mothersHouse,
            Vector3 mothersHouseReturnPosition,
            IList<AlpineVillagePlotDescriptor> sourcePlots,
            IList<AlpineVillageRidgeDescriptor> sourceRidges,
            Rect terrainBounds,
            Rect terrainMeshBounds,
            Bounds worldBounds,
            Vector3 spawnPosition,
            Vector3 spawnForward)
        {
            Seed = seed;
            SlopeOrigin = slopeOrigin;
            Uphill = uphill.normalized;
            SlopeRight = Vector3.Cross(Vector3.up, Uphill).normalized;
            Grade = grade;
            Lane = lane ?? throw new ArgumentNullException(nameof(lane));
            Station = station ??
                throw new ArgumentNullException(nameof(station));
            MothersHouse = mothersHouse ??
                throw new ArgumentNullException(nameof(mothersHouse));
            MothersHouseReturnPosition = mothersHouseReturnPosition;
            plots = new ReadOnlyCollection<AlpineVillagePlotDescriptor>(
                new List<AlpineVillagePlotDescriptor>(sourcePlots));
            ridges = new ReadOnlyCollection<AlpineVillageRidgeDescriptor>(
                new List<AlpineVillageRidgeDescriptor>(sourceRidges));
            TerrainBounds = terrainBounds;
            TerrainMeshBounds = terrainMeshBounds;
            WorldBounds = worldBounds;
            SpawnPosition = spawnPosition;
            SpawnForward = spawnForward.normalized;
        }

        public int Seed { get; }

        /// <summary>Ground point at the foot of the slope, by the station.
        /// </summary>
        public Vector3 SlopeOrigin { get; }

        /// <summary>Horizontal direction the ground rises in.</summary>
        public Vector3 Uphill { get; }

        public Vector3 SlopeRight { get; }

        /// <summary>Macro rise per horizontal metre, as a fraction.</summary>
        public float Grade { get; }

        public AlpineVillageLanePlan Lane { get; }
        public AlpineVillageStationPlan Station { get; }

        /// <summary>
        /// The house at the head of the lane. Also present in
        /// <see cref="Plots"/>; this is the named handle, because the whole
        /// composition points at it.
        /// </summary>
        public AlpineVillagePlotDescriptor MothersHouse { get; }

        /// <summary>
        /// Ground point used only after leaving the house interior. It is
        /// farther out than the interaction dock, beyond the exterior trigger
        /// and the player's capsule clearance.
        /// </summary>
        public Vector3 MothersHouseReturnPosition { get; }

        public IReadOnlyList<AlpineVillagePlotDescriptor> Plots => plots;
        public IReadOnlyList<AlpineVillageRidgeDescriptor> Ridges => ridges;

        /// <summary>
        /// The spring's water, or null before it has been traced.
        ///
        /// It arrives after construction, and that is deliberate rather than
        /// untidy. The brook is traced by walking downhill on
        /// <c>AlpineVillageTerrainSampler.SampleHeight</c>, and that sampler
        /// dishes a swale under the brook - so the trace has to happen while
        /// this is still null, or the brook would be following the channel it
        /// is in the middle of deciding. Null here means exactly one thing:
        /// "the ground has no channel in it yet".
        /// </summary>
        public AlpineVillageBrookPlan Brook { get; private set; }

        /// <summary>
        /// Hands the plan its traced water. Once, and only from the planner,
        /// between tracing and validation.
        /// </summary>
        internal void AttachBrook(AlpineVillageBrookPlan brook)
        {
            if (brook == null)
            {
                throw new ArgumentNullException(nameof(brook));
            }

            if (Brook != null)
            {
                throw new InvalidOperationException(
                    "The village already carries its brook.");
            }

            Brook = brook;
        }

        /// <summary>
        /// The village's conifers, or null before they have been placed.
        ///
        /// They live on the plan rather than in the builder for one reason:
        /// the walkable mask is built from a plan and nothing else, in twelve
        /// places - the world builder, the tests, and the map's teleport
        /// ground. A copse trunk known only to the builder would leave the map
        /// with a mask that has no trees in it, and it would set the hero down
        /// inside one.
        /// </summary>
        public AlpineVillageTreePlan Trees { get; private set; }

        /// <summary>
        /// Hands the plan its trees. Once, and only from the planner, after
        /// the water and before validation.
        /// </summary>
        internal void AttachTrees(AlpineVillageTreePlan trees)
        {
            if (trees == null)
            {
                throw new ArgumentNullException(nameof(trees));
            }

            if (Trees != null)
            {
                throw new InvalidOperationException(
                    "The village already carries its trees.");
            }

            Trees = trees;
        }

        /// <summary>
        /// The inhabited inner extent. Shelves, plots and the walkable mask
        /// live inside it; the enclosing mountain starts outside it.
        /// </summary>
        public Rect TerrainBounds { get; }

        /// <summary>
        /// The complete physical ground mesh, including the rise from the
        /// inner extent to the enclosing crest and the cableway brink below
        /// the station. Keeping this separate is essential: sampling only
        /// <see cref="TerrainBounds"/> cuts the mesh off at the exact point
        /// where <c>SampleRidgeRise</c> first becomes non-zero.
        /// </summary>
        public Rect TerrainMeshBounds { get; }

        public Bounds WorldBounds { get; }

        /// <summary>Where the hero stands when he has not arrived by cabin.
        /// </summary>
        public Vector3 SpawnPosition { get; }

        public Vector3 SpawnForward { get; }

        public void ValidateOrThrow()
        {
            AlpineVillageValidator.ValidateOrThrow(this);
        }
    }
}
