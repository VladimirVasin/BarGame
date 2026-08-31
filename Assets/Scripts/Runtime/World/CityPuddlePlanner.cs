using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Places a small, stable set of shallow road patches near gutters. Each
    /// patch uses the road texture while dry, so it only separates from the
    /// carriageway when the weather registry raises its smoothness and lowers
    /// its value.
    /// </summary>
    internal static class CityPuddlePlanner
    {
        public const int MaximumPuddleCount = 42;
        public const float Thickness = 0.006f;

        /// <summary>How far the sheet's plane stands over its road.
        /// The water shader heaves the sheet by
        /// <see cref="CityPuddleWaterResources.WaveHeight"/> (times
        /// 1.73 for the three summed trains), and the trough has to
        /// stay clear of the asphalt, or the depth test buries the
        /// middle of every patch: at 3 mm over a 4 mm wave the city's
        /// puddles drew as two slivers with a hole walking between
        /// them. Five millimetres over a sub-millimetre wave keeps the
        /// whole sheet above the road and its foam band below it.
        /// </summary>
        public const float SurfaceOffset = 0.005f;

        /// <summary>How much air the sheet needs under it. Any other
        /// surface whose top comes closer than this to the plane -
        /// or rises through it - covers the puddle: the pavement
        /// slab 60 mm up at the kerb, a crossing's stripes, or the
        /// flat intersection square a graded block's slab passes
        /// under. Coincident slabs of the same height pass, being a
        /// full <see cref="SurfaceOffset"/> below.</summary>
        private const float CoverClearance = 0.002f;

        /// <summary>How many puddles the flat open precincts may carry
        /// between them. Deliberately small: standing water in a yard
        /// is a place nobody drains, not weather.</summary>
        public const int MaximumOpenGroundPuddleCount = 16;

        private const float MinimumSurfaceSpan = 3.2f;
        private const float GutterInset = 0.28f;

        /// <summary>How far inside its own cell an open-ground puddle
        /// must sit. Only an open precinct's interior is guaranteed
        /// flat: its datum is shared across the whole area, but the
        /// terrain skin ramps toward whatever the neighbouring cell
        /// sits at, and a flat slab on a ramp buries one end.</summary>
        private const float OpenGroundEdgeInset = 4f;

        private const float MinimumOpenGroundSpan = 3f;

        /// <summary>How far the terrain skin may stray from a cell's
        /// datum under a pool before the cell is not level. The skin
        /// is a bilinear sheet between the cell's four corner
        /// elevations, so a yard whose corners disagree is a ramp
        /// from edge to edge, not a plateau with ramped edges - a
        /// slab planned on its datum hung 1.8 m over one such yard.
        /// </summary>
        private const float LevelSkinTolerance = 0.003f;

        public static IReadOnlyList<RuntimeOrientedBox> Create(
            CityStreetSurfacePlan streetPlan,
            int citySeed)
        {
            if (streetPlan == null)
            {
                throw new ArgumentNullException(nameof(streetPlan));
            }

            var candidates = new List<Candidate>();
            IReadOnlyList<RuntimeOrientedBox> streets =
                streetPlan.StreetGeometry;
            for (int index = 0; index < streets.Count; index++)
            {
                RuntimeOrientedBox surface = streets[index];
                float longest = Mathf.Max(surface.Size.x, surface.Size.z);
                float shortest = Mathf.Min(surface.Size.x, surface.Size.z);
                if (longest < MinimumSurfaceSpan || shortest < 0.7f)
                {
                    continue;
                }

                uint hash = CityExteriorAppearance.Mix(
                    unchecked((uint)citySeed),
                    unchecked((uint)(index + 1)));
                if ((hash % 100u) >= 76u)
                {
                    continue;
                }

                TryAddCandidate(
                    candidates,
                    hash,
                    index * 2,
                    CreatePatch(surface, hash),
                    index,
                    streetPlan);
                if (longest >= 16f &&
                    ((hash >> 8) & 1u) != 0u)
                {
                    uint secondHash = CityExteriorAppearance.Mix(
                        hash,
                        0x6D2B79F5u);
                    TryAddCandidate(
                        candidates,
                        secondHash,
                        (index * 2) + 1,
                        CreatePatch(surface, secondHash),
                        index,
                        streetPlan);
                }
            }

            candidates.Sort(CompareCandidates);
            int count = Mathf.Min(MaximumPuddleCount, candidates.Count);
            var result = new List<RuntimeOrientedBox>(count);
            for (int index = 0; index < count; index++)
            {
                result.Add(candidates[index].Patch);
            }

            return new ReadOnlyCollection<RuntimeOrientedBox>(result);
        }

        /// <summary>
        /// Standing water on the flat open precincts — the fringe
        /// yards, the cemetery terrace and the church ground. They are
        /// the only ground in the city that is dead level: every cell
        /// of an area declaring a street access is pinned to that one
        /// access datum, so a flat slab lies true on it. Streets get
        /// their own gutter patches from <see cref="Create"/>; the
        /// sloped buildable ground and the beach get none, because a
        /// six-millimetre box cannot follow a five-percent cross-fall.
        /// </summary>
        public static IReadOnlyList<RuntimeOrientedBox> CreateOpenGround(
            CityLayout layout,
            int citySeed)
        {
            return CreateOpenGround(layout, citySeed, null);
        }

        /// <summary>
        /// As above, minus the fringe yards. Since the landscape pass they
        /// are terrain - stone terraces, the forefield's compacted fill -
        /// and their <c>PhysicalTopY</c> is a datum the skin no longer lies
        /// on: a slab planned on it hung 1.8 m in the air over one yard
        /// and lay 1.5 m under another. Until a yard carries a height
        /// model this planner can read, its ground pools nothing; the
        /// cemetery terrace and the church ground stay. The world builder
        /// hands the plan in; <c>null</c> keeps the old behaviour.
        /// </summary>
        public static IReadOnlyList<RuntimeOrientedBox> CreateOpenGround(
            CityLayout layout,
            int citySeed,
            CityFringeYardPlan terrainYards)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var flatAreas = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                flatAreas.Add(layout.OpenAreaAccesses[index].AreaId);
            }

            if (terrainYards != null)
            {
                for (int index = 0; index < terrainYards.Yards.Count; index++)
                {
                    flatAreas.Remove(terrainYards.Yards[index].AreaId);
                }
            }

            var candidates = new List<Candidate>();
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (!IsLevelOpenGround(surface) ||
                    !flatAreas.Contains(surface.AreaId))
                {
                    continue;
                }

                Rect interior = Inset(
                    surface.WorldBounds,
                    OpenGroundEdgeInset);
                if (interior.width < MinimumOpenGroundSpan ||
                    interior.height < MinimumOpenGroundSpan)
                {
                    continue;
                }

                uint hash = CityExteriorAppearance.Mix(
                    unchecked((uint)citySeed),
                    unchecked((uint)(0x9E3779B9 + index)));
                if ((hash % 100u) >= 34u)
                {
                    continue;
                }

                RuntimeOrientedBox patch = CreateGroundPatch(
                    interior,
                    surface.PhysicalTopY,
                    hash);
                if (surface.Kind == CitySurfaceKind.OpenGround &&
                    !SkinIsLevelUnder(layout, surface, patch))
                {
                    continue;
                }

                candidates.Add(new Candidate(hash, index, patch));
            }

            candidates.Sort(CompareCandidates);
            int count = Mathf.Min(
                MaximumOpenGroundPuddleCount,
                candidates.Count);
            var result = new List<RuntimeOrientedBox>(count);
            for (int index = 0; index < count; index++)
            {
                result.Add(candidates[index].Patch);
            }

            return new ReadOnlyCollection<RuntimeOrientedBox>(result);
        }

        /// <summary>
        /// The open ground is a terrain skin, not a slab: the world
        /// builder lays it through <see cref="CityTerrainSurfacePlan.SampleTop"/>,
        /// so the pool asks the same function whether the skin lies on
        /// the datum at its centre and its four corners. The cemetery
        /// terrace and the church ground are solid slabs at their datum
        /// and are not asked.
        /// </summary>
        private static bool SkinIsLevelUnder(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            RuntimeOrientedBox patch)
        {
            float datumTop = surface.PhysicalTopY;
            for (int corner = 0; corner < 5; corner++)
            {
                Vector2 sample = new Vector2(patch.Center.x, patch.Center.z);
                if (corner > 0)
                {
                    sample.x += ((corner & 1) == 0 ? -0.5f : 0.5f) * patch.Size.x;
                    sample.y += ((corner & 2) == 0 ? -0.5f : 0.5f) * patch.Size.z;
                }

                float skinTop = CityTerrainSurfacePlan.SampleTop(
                    layout,
                    surface,
                    sample);
                if (Mathf.Abs(skinTop - datumTop) > LevelSkinTolerance)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsLevelOpenGround(
            CitySurfaceDescriptor surface)
        {
            if (!surface.IsWalkable || surface.IsWater)
            {
                return false;
            }

            switch (surface.Kind)
            {
                case CitySurfaceKind.OpenGround:
                case CitySurfaceKind.CemeteryGround:
                case CitySurfaceKind.ChurchGround:
                    return true;
                default:
                    // The park keeps its emptiness, the beach slopes to
                    // the waterline, and the buildable ground carries
                    // the valley's cross-fall.
                    return false;
            }
        }

        private static Rect Inset(Rect source, float amount)
        {
            return new Rect(
                source.xMin + amount,
                source.yMin + amount,
                Mathf.Max(0f, source.width - (amount * 2f)),
                Mathf.Max(0f, source.height - (amount * 2f)));
        }

        private static RuntimeOrientedBox CreateGroundPatch(
            Rect area,
            float topY,
            uint hash)
        {
            // Wider and squarer than a gutter patch: nothing channels
            // this water, so it spreads until the ground stops it.
            float sizeX = Mathf.Min(
                Mathf.Lerp(1.3f, 3.3f, Unit(hash, 0)),
                area.width);
            float sizeZ = Mathf.Min(
                Mathf.Lerp(1.1f, 2.6f, Unit(hash, 8)),
                area.height);
            float travelX = Mathf.Max(0f, (area.width - sizeX) * 0.5f);
            float travelZ = Mathf.Max(0f, (area.height - sizeZ) * 0.5f);
            var center = new Vector3(
                area.center.x +
                Mathf.Lerp(-travelX, travelX, Unit(hash, 16)),
                topY + SurfaceOffset,
                area.center.y +
                Mathf.Lerp(-travelZ, travelZ, Unit(hash, 24)));
            return new RuntimeOrientedBox(
                center,
                Quaternion.identity,
                new Vector3(sizeX, Thickness, sizeZ));
        }

        private static RuntimeOrientedBox CreatePatch(
            RuntimeOrientedBox surface,
            uint hash)
        {
            bool longAlongX = surface.Size.x >= surface.Size.z;
            float longSpan = longAlongX
                ? surface.Size.x
                : surface.Size.z;
            float crossSpan = longAlongX
                ? surface.Size.z
                : surface.Size.x;
            float longSize = Mathf.Min(
                Mathf.Lerp(0.9f, 2.45f, Unit(hash, 0)),
                Mathf.Max(0.55f, longSpan - 0.7f));
            float crossSize = Mathf.Min(
                Mathf.Lerp(0.38f, 0.92f, Unit(hash, 8)),
                Mathf.Max(0.28f, crossSpan - 0.55f));

            float longTravel = Mathf.Max(
                0f,
                (longSpan - longSize) * 0.5f - 0.35f);
            float longOffset = Mathf.Lerp(
                -longTravel,
                longTravel,
                Unit(hash, 16));
            float crossTravel = Mathf.Max(
                0f,
                (crossSpan - crossSize) * 0.5f - GutterInset);
            float crossOffset = (((hash >> 27) & 1u) == 0u
                    ? -1f
                    : 1f) *
                crossTravel;

            Vector3 localOffset = longAlongX
                ? new Vector3(longOffset, 0f, crossOffset)
                : new Vector3(crossOffset, 0f, longOffset);
            localOffset.y =
                (surface.Size.y * 0.5f) +
                SurfaceOffset;
            Vector3 size = longAlongX
                ? new Vector3(longSize, Thickness, crossSize)
                : new Vector3(crossSize, Thickness, longSize);
            return new RuntimeOrientedBox(
                surface.Center + surface.Rotation * localOffset,
                surface.Rotation,
                size);
        }

        private static void TryAddCandidate(
            List<Candidate> candidates,
            uint rank,
            int stableOrder,
            RuntimeOrientedBox patch,
            int sourceIndex,
            CityStreetSurfacePlan streetPlan)
        {
            if (IsCovered(patch, sourceIndex, streetPlan))
            {
                return;
            }

            candidates.Add(new Candidate(rank, stableOrder, patch));
        }

        // The sheet's plane, in half-size units: the centre and the
        // four corners. A 3x3 grid has nothing between them that the
        // corners and centre do not bound.
        private static readonly Vector3[] SheetSamplePoints =
        {
            Vector3.zero,
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, -0.5f),
            new Vector3(-0.5f, 0f, 0.5f),
            new Vector3(0.5f, 0f, 0.5f)
        };

        /// <summary>
        /// True when some other surface stands over the sheet or
        /// within <see cref="CoverClearance"/> under it. The gutter
        /// inset is measured from the STREET box, and at an
        /// intersection square that box runs the full right of way -
        /// its edge is under the pavement slab, 60 mm up. Half the
        /// city's puddles lay there, in the dark under the kerb.
        /// </summary>
        private static bool IsCovered(
            RuntimeOrientedBox patch,
            int sourceIndex,
            CityStreetSurfacePlan streetPlan)
        {
            for (int sample = 0; sample < SheetSamplePoints.Length; sample++)
            {
                Vector3 point = patch.Center + patch.Rotation *
                    Vector3.Scale(SheetSamplePoints[sample], patch.Size);
                float ceiling = point.y - CoverClearance;
                if (AnyTopAbove(
                        streetPlan.StreetGeometry, point, ceiling, sourceIndex) ||
                    AnyTopAbove(
                        streetPlan.SidewalkGeometry, point, ceiling, -1) ||
                    AnyTopAbove(
                        streetPlan.CrosswalkMarkingGeometry, point, ceiling, -1) ||
                    AnyTopAbove(
                        streetPlan.CenterMarkingGeometry, point, ceiling, -1))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AnyTopAbove(
            IReadOnlyList<RuntimeOrientedBox> surfaces,
            Vector3 point,
            float ceiling,
            int skipIndex)
        {
            for (int index = 0; index < surfaces.Count; index++)
            {
                if (index == skipIndex)
                {
                    continue;
                }

                if (surfaces[index].TrySampleTop(point, out float top) &&
                    top > ceiling)
                {
                    return true;
                }
            }

            return false;
        }

        private static float Unit(uint hash, int shift)
        {
            return ((hash >> shift) & 0xFFu) / 255f;
        }

        private static int CompareCandidates(
            Candidate first,
            Candidate second)
        {
            int hashOrder = first.Rank.CompareTo(second.Rank);
            return hashOrder != 0
                ? hashOrder
                : first.StableOrder.CompareTo(second.StableOrder);
        }

        private readonly struct Candidate
        {
            public Candidate(
                uint rank,
                int stableOrder,
                RuntimeOrientedBox patch)
            {
                Rank = rank;
                StableOrder = stableOrder;
                Patch = patch;
            }

            public uint Rank { get; }
            public int StableOrder { get; }
            public RuntimeOrientedBox Patch { get; }
        }
    }
}
