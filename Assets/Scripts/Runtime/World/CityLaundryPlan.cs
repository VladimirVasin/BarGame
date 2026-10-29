using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One non-residential infill on the existing residential ground.
    /// No semantic housing lot, street or courtyard route is replaced.</summary>
    public sealed class CityLaundryPlan
    {
        public const float Width = 8.6f;
        public const float Depth = 6.6f;
        public const float MaximumGroundSpread = .18f;
        public const int MaximumCandidatesPerLot = 160;
        private const float GroundBlend = .45f;
        private static readonly ConditionalWeakTable<CityLayout, CityLaundryPlan> Plans =
            new ConditionalWeakTable<CityLayout, CityLaundryPlan>();
        private static readonly CityLaundryPlan Absent = new CityLaundryPlan();

        private CityLaundryPlan()
        {
            SolidFootprints = Array.Empty<Rect>();
            BodyFootprints = Array.Empty<Rect>();
            WalkableFootprints = Array.Empty<Rect>();
            AccessPath = Array.Empty<Vector3>();
        }

        private CityLaundryPlan(Vector2Int cell, Vector3 position, Quaternion rotation, Vector3 access)
        {
            IsEnabled = true; Cell = cell; Position = position; Rotation = rotation;
            Footprint = RectAt(-4.3f, -3.3f, 4.3f, 3.3f);
            ApproachBounds = RectAt(-3.65f, 3.25f, -1.95f, 4.9f);
            ReservationBounds = Union(Expanded(Footprint, .18f), ApproachBounds);
            GroundBounds = Expanded(Footprint, GroundBlend);
            BodyFootprints = new ReadOnlyCollection<Rect>(new[] {
                RectAt(-4f,-3f,1.5f,3f), RectAt(1.5f,-3f,4f,0f) });
            SolidFootprints = new ReadOnlyCollection<Rect>(new[] {
                // The front leaf is closed. The east service doorway stays
                // physically open between z=.3 and 1.9; no filled-room proxy.
                RectAt(-4f,2.88f,1.5f,3.12f), RectAt(-4f,-3.12f,4f,-2.88f),
                RectAt(-4.12f,-3f,-3.88f,3f), RectAt(1.38f,0f,1.62f,.3f),
                RectAt(1.38f,1.9f,1.62f,3f), RectAt(3.88f,-3f,4.12f,0f),
                RectAt(1.5f,-.12f,4f,.12f),
                RectAt(-3.5f,-.4f,-1.8f,.8f), RectAt(-3.81f,-2.275f,-3.03f,-.625f),
                RectAt(-1.36f,-2.67f,-.24f,-1.57f), RectAt(-.06f,-2.67f,1.06f,-1.57f),
                RectAt(2.54f,.60f,3.46f,1.475f),
                RectAt(3.93f,.06f,4.17f,.30f), RectAt(3.93f,2.78f,4.17f,3.02f) });
            WalkableFootprints = new ReadOnlyCollection<Rect>(new[] {
                RectAt(-4.3f,-3.3f,4.3f,3.3f), ApproachBounds });
            AccessPath = new ReadOnlyCollection<Vector3>(new[] { ApproachWorld, access });
        }

        public bool IsEnabled { get; }
        public Vector2Int Cell { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; } = Quaternion.identity;
        public Vector3 Forward => Rotation * Vector3.forward;
        public Vector3 ApproachWorld => World(new Vector3(-2.8f, 0f, 4.3f));
        public Rect Footprint { get; }
        public Rect GroundBounds { get; }
        public Rect ApproachBounds { get; }
        public Rect ReservationBounds { get; }
        public IReadOnlyList<Rect> SolidFootprints { get; }
        public IReadOnlyList<Rect> BodyFootprints { get; }
        public IReadOnlyList<Rect> WalkableFootprints { get; }
        public IReadOnlyList<Vector3> AccessPath { get; }
        public Vector3 World(Vector3 local) => Position + Rotation * local;
        public Vector3 InverseWorld(Vector3 world) => Quaternion.Inverse(Rotation) * (world - Position);

        public static CityLaundryPlan ForLayout(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            return Plans.GetValue(layout, Create);
        }

        // Terrain must never start placement: fitting and existing courtyard
        // routes themselves sample terrain. Composition primes the immutable
        // site before building the physical ground; only then does it grade it.
        internal static CityLaundryPlan CachedForSurface(CityLayout layout, CitySurfaceDescriptor surface)
        {
            return surface.Kind == CitySurfaceKind.BuildableGround &&
                Plans.TryGetValue(layout,out CityLaundryPlan plan) && plan.IsEnabled &&
                plan.GroundBounds.Overlaps(surface.WorldBounds) ? plan : null;
        }

        internal float ApplyGround(Vector2 point, float rawTop)
        {
            if (!GroundBounds.Contains(point)) return rawTop;
            float distance = Vector2.Distance(point, new Vector2(
                Mathf.Clamp(point.x, Footprint.xMin, Footprint.xMax),
                Mathf.Clamp(point.y, Footprint.yMin, Footprint.yMax)));
            return Mathf.Lerp(Position.y - .04f, rawTop,
                Mathf.SmoothStep(0f, 1f, distance / GroundBlend));
        }

        public void ValidateOrThrow(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (!IsEnabled) return;
            var context = new CityResidentialInfillPlacement(layout);
            if (!context.Fits(Footprint, ApproachBounds, Position, Rotation, new Vector3(-2.8f,0f,4.3f), out _))
                throw new InvalidOperationException("Laundry infill no longer fits the existing residential ground and clear routes.");
        }

        private static CityLaundryPlan Create(CityLayout layout)
        {
            if (!layout.SpatialPlan.IsCompactCoastal) return Absent;
            var context = new CityResidentialInfillPlacement(layout);
            var lots = new List<BuildingLot>();
            foreach (BuildingLot lot in layout.BuildingLots)
                if (lot.IsOrdinaryBuilding && lot.District == CityDistrictKind.Residential &&
                    !context.ProtectedCell(lot.Cell)) lots.Add(lot);
            lots.Sort((a, b) => {
                int order = Rank(layout.Seed,a.Cell).CompareTo(Rank(layout.Seed,b.Cell));
                if (order != 0) return order;
                order = a.Cell.y.CompareTo(b.Cell.y);
                return order != 0 ? order : a.Cell.x.CompareTo(b.Cell.x);
            });
            foreach (BuildingLot lot in lots)
            {
                Rect cell = layout.GetCellWorldBounds(lot.Cell);
                // Six stations per axis include both shared cell seams. A
                // second orientation can use an adjoining rear court while
                // the semantic owner remains this original residential cell.
                for (int z = 0; z < 6; z++)
                for (int x = 0; x < 6; x++)
                for (int side = 0; side < 4; side++)
                {
                    Vector3 position = new Vector3(Mathf.Lerp(cell.xMin + .5f, cell.xMax - .5f, x / 5f),
                        0f, Mathf.Lerp(cell.yMin + .5f, cell.yMax - .5f, z / 5f));
                    Quaternion rotation = Quaternion.Euler(0f, side * 90f, 0f);
                    Rect footprint = RectAt(position, rotation, -4.3f,-3.3f,4.3f,3.3f);
                    Rect approach = RectAt(position, rotation, -3.65f,3.25f,-1.95f,4.9f);
                    if (!context.Fits(footprint, approach, position, rotation, new Vector3(-2.8f,0f,4.3f), out Vector3 access)) continue;
                    if (!context.Foundation(footprint, out float top)) continue;
                    position.y = top + .04f;
                    return new CityLaundryPlan(lot.Cell, position, rotation, access);
                }
            }
            if (layout.Seed != GameSessionState.DefaultCitySeed) return Absent;
            throw new InvalidOperationException("No residual residential site fits the laundry without replacing buildings or blocking existing routes.");
        }

        private Rect RectAt(float x0, float z0, float x1, float z1) => RectAt(Position, Rotation, x0,z0,x1,z1);
        private static Rect RectAt(Vector3 origin, Quaternion rotation, float x0, float z0, float x1, float z1)
        {
            Vector3 a = origin + rotation * new Vector3(x0,0,z0), b = origin + rotation * new Vector3(x1,0,z1);
            return Rect.MinMaxRect(Mathf.Min(a.x,b.x), Mathf.Min(a.z,b.z), Mathf.Max(a.x,b.x), Mathf.Max(a.z,b.z));
        }
        internal static Rect Expanded(Rect source, float amount) => Rect.MinMaxRect(
            source.xMin-amount,source.yMin-amount,source.xMax+amount,source.yMax+amount);
        private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin,b.xMin),
            Mathf.Min(a.yMin,b.yMin),Mathf.Max(a.xMax,b.xMax),Mathf.Max(a.yMax,b.yMax));
        private static uint Rank(int seed, Vector2Int cell) => unchecked((uint)seed ^
            ((uint)cell.x * 73856093u) ^ ((uint)cell.y * 19349663u) ^ 0x4C41554Eu);
    }
}
