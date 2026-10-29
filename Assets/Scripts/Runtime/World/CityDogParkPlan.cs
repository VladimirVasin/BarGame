using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One fenced dog yard in residual residential ground. Existing
    /// homes, authored places, laundry and walking routes keep their positions.</summary>
    public sealed class CityDogParkPlan
    {
        public const float Width = 8f;
        public const float Depth = 10f;
        public const float EntranceWidth = 1.6f;
        public const float FenceHeight = 1.25f;
        public const float FenceThickness = .12f;
        public const float MaximumGroundSpread = .28f;
        public const int StationsPerAxis = 13;
        public const int MaximumCandidatesPerLot = StationsPerAxis * StationsPerAxis * 4;
        private const float GroundBlend = .45f;
        private static readonly Vector3 LocalApproach = new Vector3(0f, 0f, 5.95f);
        private static readonly ConditionalWeakTable<CityLayout, CityDogParkPlan> Plans =
            new ConditionalWeakTable<CityLayout, CityDogParkPlan>();
        private static readonly CityDogParkPlan Absent = new CityDogParkPlan();
        private static readonly Rect[] LocalSolids =
        {
            Rect.MinMaxRect(-4f, -5f, -3.88f, 5f),
            Rect.MinMaxRect(3.88f, -5f, 4f, 5f),
            Rect.MinMaxRect(-3.88f, -5f, 3.88f, -4.88f),
            Rect.MinMaxRect(-3.88f, 4.88f, -.8f, 5f),
            Rect.MinMaxRect(.8f, 4.88f, 3.88f, 5f),
            Rect.MinMaxRect(-3.5f, -4.45f, -1.7f, -3.75f),
            Rect.MinMaxRect(1.75f, -2.48f, 3.15f, -2.12f)
        };

        private CityDogParkPlan()
        {
            SolidFootprints = Array.Empty<Rect>();
            WalkableFootprints = Array.Empty<Rect>();
            AccessPath = Array.Empty<Vector3>();
        }

        private CityDogParkPlan(Vector2Int cell, Vector3 position, Quaternion rotation, Vector3 access)
        {
            IsEnabled = true;
            Cell = cell;
            Position = position;
            Rotation = rotation;
            Footprint = RectAt(-4f, -5f, 4f, 5f);
            InteriorBounds = RectAt(-3.55f, -4.55f, 3.55f, 4.55f);
            ApproachBounds = RectAt(-.95f, 4.9f, .95f, 6.9f);
            GroundBounds = Expanded(Footprint, GroundBlend);
            ReservationBounds = Union(GroundBounds, ApproachBounds);
            var solids = new List<Rect>(LocalSolids.Length);
            foreach (Rect rect in LocalSolids) solids.Add(RectAt(rect.xMin, rect.yMin, rect.xMax, rect.yMax));
            SolidFootprints = new ReadOnlyCollection<Rect>(solids);
            AccessPath = new ReadOnlyCollection<Vector3>(new[] { EntranceWorld, ApproachWorld, access });
            var floors = new List<Rect> { Footprint, ApproachBounds };
            for (int i = 1; i < AccessPath.Count; i++)
            {
                Vector3 a = AccessPath[i - 1], b = AccessPath[i];
                floors.Add(Expanded(Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z),
                    Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z)), .8f));
            }
            WalkableFootprints = new ReadOnlyCollection<Rect>(floors);
        }

        public bool IsEnabled { get; }
        public Vector2Int Cell { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; } = Quaternion.identity;
        public Vector3 Forward => Rotation * Vector3.forward;
        public Rect Footprint { get; }
        public Rect InteriorBounds { get; }
        public Rect LocalInteriorBounds => Rect.MinMaxRect(-3.55f, -4.55f, 3.55f, 4.55f);
        public Rect GroundBounds { get; }
        public Rect ApproachBounds { get; }
        public Rect ReservationBounds { get; }
        public Vector3 EntranceWorld => World(new Vector3(0f, 0f, 5f));
        public Vector3 ApproachWorld => World(LocalApproach);
        public IReadOnlyList<Rect> SolidFootprints { get; }
        public IReadOnlyList<Rect> WalkableFootprints { get; }
        public IReadOnlyList<Vector3> AccessPath { get; }
        public Vector3 World(Vector3 local) => Position + Rotation * local;
        public Vector3 InverseWorld(Vector3 world) => Quaternion.Inverse(Rotation) * (world - Position);

        internal void AppendLocalCollisionBounds(ICollection<Bounds> target)
        {
            if (!IsEnabled) return;
            target.Add(new Bounds(new Vector3(0f, -.04f, 0f), new Vector3(Width, .08f, Depth)));
            for (int i = 0; i < LocalSolids.Length; i++)
            {
                Rect rect = LocalSolids[i];
                float height = i < 5 ? FenceHeight : i == 5 ? .85f : .44f;
                target.Add(new Bounds(new Vector3(rect.center.x, height * .5f, rect.center.y),
                    new Vector3(rect.width, height, rect.height)));
            }
        }

        public bool AllowsDogAt(Vector3 local, float radius = .32f)
        {
            Rect interior = LocalInteriorBounds;
            if (local.x < interior.xMin + radius || local.x > interior.xMax - radius ||
                local.z < interior.yMin + radius || local.z > interior.yMax - radius) return false;
            for (int i = 5; i < LocalSolids.Length; i++)
                if (Expanded(LocalSolids[i], radius).Contains(new Vector2(local.x, local.z))) return false;
            return true;
        }

        public static CityDogParkPlan ForLayout(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            return Plans.GetValue(layout, Create);
        }

        // Sampling must never start placement: fitting already samples terrain.
        // Composition primes the plan before the terrain mesh resolves contexts.
        internal static CityDogParkPlan CachedForSurface(CityLayout layout, CitySurfaceDescriptor surface)
        {
            return surface.Kind == CitySurfaceKind.BuildableGround &&
                Plans.TryGetValue(layout, out CityDogParkPlan plan) && plan.IsEnabled &&
                plan.GroundBounds.Overlaps(surface.WorldBounds) ? plan : null;
        }

        internal float ApplyGround(Vector2 point, float rawTop)
        {
            if (!GroundBounds.Contains(point)) return rawTop;
            float distance = Vector2.Distance(point, new Vector2(
                Mathf.Clamp(point.x, Footprint.xMin, Footprint.xMax),
                Mathf.Clamp(point.y, Footprint.yMin, Footprint.yMax)));
            return Mathf.Lerp(Position.y - .025f, rawTop,
                Mathf.SmoothStep(0f, 1f, distance / GroundBlend));
        }

        public void ValidateOrThrow(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (!IsEnabled) return;
            CityResidentialInfillPlacement context = CreateContext(layout);
            if (!context.Fits(Footprint, ApproachBounds, Position, Rotation, LocalApproach, out _) ||
                !context.Foundation(Footprint, out _, MaximumGroundSpread))
                throw new InvalidOperationException("Dog park no longer fits residential ground and clear existing routes.");
        }

        private static CityDogParkPlan Create(CityLayout layout)
        {
            if (!layout.SpatialPlan.IsCompactCoastal) return Absent;
            CityResidentialInfillPlacement context = CreateContext(layout);
            var lots = new List<BuildingLot>();
            foreach (BuildingLot lot in layout.BuildingLots)
                if (lot.IsOrdinaryBuilding && lot.District == CityDistrictKind.Residential &&
                    !context.ProtectedCell(lot.Cell)) lots.Add(lot);
            lots.Sort((a, b) =>
            {
                int order = DistanceToPark(layout, a.Center).CompareTo(DistanceToPark(layout, b.Center));
                if (order != 0) return order;
                order = Rank(layout.Seed, a.Cell).CompareTo(Rank(layout.Seed, b.Cell));
                if (order != 0) return order;
                order = a.Cell.y.CompareTo(b.Cell.y);
                return order != 0 ? order : a.Cell.x.CompareTo(b.Cell.x);
            });
            foreach (BuildingLot lot in lots)
            {
                Rect cell = layout.GetCellWorldBounds(lot.Cell);
                for (int z = 0; z < StationsPerAxis; z++)
                for (int x = 0; x < StationsPerAxis; x++)
                for (int side = 0; side < 4; side++)
                {
                    Vector3 position = new Vector3(Mathf.Lerp(cell.xMin + .25f, cell.xMax - .25f,
                        x / (StationsPerAxis - 1f)), 0f, Mathf.Lerp(cell.yMin + .25f, cell.yMax - .25f,
                        z / (StationsPerAxis - 1f)));
                    Quaternion rotation = Quaternion.Euler(0f, side * 90f, 0f);
                    Rect footprint = RectAt(position, rotation, -4f, -5f, 4f, 5f);
                    Rect approach = RectAt(position, rotation, -.95f, 4.9f, .95f, 6.9f);
                    if (!context.Fits(footprint, approach, position, rotation, LocalApproach, out Vector3 access)) continue;
                    if (!context.Foundation(footprint, out float top, MaximumGroundSpread)) continue;
                    position.y = top + .025f;
                    return new CityDogParkPlan(lot.Cell, position, rotation, access);
                }
            }
            if (layout.Seed != GameSessionState.DefaultCitySeed) return Absent;
            throw new InvalidOperationException("No residual residential site fits the dog park without replacing buildings or blocking routes.");
        }

        private static CityResidentialInfillPlacement CreateContext(CityLayout layout)
        {
            var context = new CityResidentialInfillPlacement(layout);
            CityLaundryPlan laundry = CityLaundryPlan.ForLayout(layout);
            if (laundry.IsEnabled)
            {
                context.Reserve(laundry.ReservationBounds, .5f);
                context.ReservePath(laundry.AccessPath, .8f);
            }
            // The neighbouring park keeps its trunks and benches; no vegetation
            // is silently removed to make the new yard fit.
            foreach (Vector3 tree in layout.Park.TreePositions)
                context.Reserve(new Rect(tree.x - .5f, tree.z - .5f, 1f, 1f), .3f);
            return context;
        }

        private static float DistanceToPark(CityLayout layout, Vector3 point)
        {
            Rect park = layout.Park.WalkableBounds;
            return new Vector2(point.x - Mathf.Clamp(point.x, park.xMin, park.xMax),
                point.z - Mathf.Clamp(point.z, park.yMin, park.yMax)).sqrMagnitude;
        }
        private Rect RectAt(float x0, float z0, float x1, float z1) => RectAt(Position, Rotation, x0, z0, x1, z1);
        private static Rect RectAt(Vector3 origin, Quaternion rotation, float x0, float z0, float x1, float z1)
        {
            Vector3 a = origin + rotation * new Vector3(x0, 0f, z0);
            Vector3 b = origin + rotation * new Vector3(x1, 0f, z1);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
        }
        private static Rect Expanded(Rect rect, float amount) => CityLaundryPlan.Expanded(rect, amount);
        private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin),
            Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        private static uint Rank(int seed, Vector2Int cell) => unchecked((uint)seed ^
            ((uint)cell.x * 73856093u) ^ ((uint)cell.y * 19349663u) ^ 0x444F4750u);
    }
}
