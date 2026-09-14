using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CityEastLitterPart
    {
        internal CityEastLitterPart(string id, CityLitterItem item, Vector3 position, Quaternion rotation,
            float scale, Rect footprint, int band)
        { Id = id; Item = item; Position = position; Rotation = rotation; Scale = scale; Footprint = footprint; Band = band; }
        public string Id { get; }
        public CityLitterItem Item { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public float Scale { get; }
        /// <summary>Conservative full projected body, including its lean and placement clearance.</summary>
        public Rect Footprint { get; }
        public int Band { get; }
    }

    /// <summary>Finite, seeded litter over the entire public street-to-fence strip; art §10e/story §6, 2026-09-14.</summary>
    public sealed class CityEastLitterPlan
    {
        public const int BandCount = 5;
        public const float MinimumItemClearance = .45f;
        private const float RowLength = 8f;
        private static readonly ConditionalWeakTable<CityEastExitPlan, CityEastLitterPlan> Plans =
            new ConditionalWeakTable<CityEastExitPlan, CityEastLitterPlan>();
        private readonly CityEastExitPlan exit;
        private readonly List<Rect> exclusions = new List<Rect>();
        public Rect Bounds { get; }
        public Rect PedestrianCorridor { get; }
        public IReadOnlyList<CityEastLitterPart> Parts { get; }

        public static CityEastLitterPlan Create(CityEastExitPlan exit)
        {
            if (exit == null) throw new ArgumentNullException(nameof(exit));
            return Plans.GetValue(exit, source => new CityEastLitterPlan(source,
                source.IsEnabled ? CityLitterCatalog.Load() : null));
        }

        /// <summary>Uncached reconstruction for authored catalogues and deterministic contract checks.</summary>
        public static CityEastLitterPlan Create(CityEastExitPlan exit, CityLitterCatalog catalog)
        {
            if (exit == null) throw new ArgumentNullException(nameof(exit));
            if (exit.IsEnabled && catalog == null) throw new ArgumentNullException(nameof(catalog));
            return new CityEastLitterPlan(exit, catalog);
        }

        private CityEastLitterPlan(CityEastExitPlan source, CityLitterCatalog catalog)
        {
            exit = source;
            var parts = new List<CityEastLitterPart>();
            Parts = new ReadOnlyCollection<CityEastLitterPart>(parts);
            if (!exit.IsEnabled) return;
            Bounds = Rect.MinMaxRect(exit.YardBounds.xMin + .35f, exit.YardBounds.yMin + .6f,
                exit.CheckpointPosition.x - .40f, exit.NorthYardBounds.yMax - .6f);
            PedestrianCorridor = Rect.MinMaxRect(exit.YardBounds.xMin + 2f, Bounds.yMin,
                exit.YardBounds.xMin + 5.5f, Bounds.yMax);
            exclusions.Add(Expand(exit.ClearanceBounds, .35f));
            exclusions.Add(Expand(exit.BoothPad, .35f));
            CityEastExitDressingPlan dressing = CityEastExitDressingPlan.Create(exit);
            foreach (CityEastExitDressingSolid solid in dressing.Solids)
                exclusions.Add(Expand(solid.Footprint, .45f));
            foreach (CityEastExitDressingPart part in dressing.Parts)
                if (part.GroupId == "Post Foot Traces" || part.GroupId == "Service Yard Traces")
                    exclusions.Add(Expand(part.Footprint, .35f));
            var duty = new CityEastGuardPlan(exit);
            for (int actor = 0; actor < 2; actor++)
            for (int waypoint = 1; waypoint < 5; waypoint++)
            {
                Vector3 a = duty.Target(actor, waypoint - 1), b = duty.Target(actor, waypoint);
                exclusions.Add(Expand(Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z),
                    Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z)), .85f));
            }

            var small = new List<CityLitterItem>();
            foreach (CityLitterItem item in catalog.Items)
            {
                if (!item.Solid) { small.Add(item); continue; }
                // Rare larger finds have their own streams and clear the whole
                // walking corridor. They cannot force the small litter back to the fence.
                var random = new System.Random(Seed(item.Name));
                for (int copy = 0; copy < 2; copy++)
                for (int attempt = 0; attempt < 90; attempt++)
                {
                    Vector2 point = new Vector2(Mathf.Lerp(Bounds.xMin, Bounds.xMax, Next(random)),
                        Mathf.Lerp(Bounds.yMin, Bounds.yMax, (copy + Next(random)) / 2f));
                    if (TryAdd("Litter " + item.Name + " " + copy, item, point, random)) break;
                }
            }
            if (small.Count == 0) throw new InvalidOperationException("The litter catalog needs small scatter items.");

            int rows = Mathf.Max(1, Mathf.CeilToInt(Bounds.height / RowLength));
            float bandWidth = Bounds.width / BandCount, rowLength = Bounds.height / rows;
            // Equal candidate density in every transverse band. An independent
            // seed per cell preserves the wider world and avoids visible lines.
            for (int row = 0; row < rows; row++)
            for (int band = 0; band < BandCount; band++)
            {
                var random = new System.Random(Seed("cell-" + row + "-" + band));
                Vector2 centre = new Vector2(Bounds.xMin + (band + .5f) * bandWidth,
                    Bounds.yMin + (row + .5f) * rowLength);
                // Two finds per cell, with a third in one cell out of four:
                // a quarter fewer than the first pass. Stratified long-axis
                // slots keep isolated finds from collapsing into small piles.
                int count = (row + band) % 4 == 0 ? 3 : 2;
                for (int slot = 0; slot < count; slot++)
                {
                    CityLitterItem item = small[(row * BandCount * 3 + band * 3 + slot) % small.Count];
                    for (int attempt = 0; attempt < 14; attempt++)
                    {
                        Vector2 point = centre + new Vector2((Next(random) - .5f) * bandWidth * .90f,
                            ((slot + .5f + (Next(random) - .5f) * .65f) / count - .5f) * rowLength * .92f);
                        if (TryAdd("Litter Cell " + row + " " + band + " " + slot, item, point, random)) break;
                    }
                }
            }

            bool TryAdd(string id, CityLitterItem item, Vector2 point, System.Random random)
            {
                float scale = .92f + Next(random) * .16f;
                Quaternion yaw = Quaternion.Euler(0f, Next(random) * 360f, 0f);
                Rect flat = Project(item.Bounds, new Vector3(point.x, 0f, point.y), yaw, scale);
                if (!IsPlacementAllowed(flat, item.Solid)) return false;
                float height = exit.SampleGroundTop(point);
                const float delta = .25f;
                float dx = (exit.SampleGroundTop(point + Vector2.right * delta) -
                    exit.SampleGroundTop(point - Vector2.right * delta)) / (2f * delta);
                float dz = (exit.SampleGroundTop(point + Vector2.up * delta) -
                    exit.SampleGroundTop(point - Vector2.up * delta)) / (2f * delta);
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, new Vector3(-dx, 1f, -dz).normalized) *
                    yaw;
                Vector3 position = new Vector3(point.x, height, point.y);
                Rect footprint = Project(item.Bounds, position, rotation, scale);
                if (!IsPlacementAllowed(footprint, item.Solid)) return false;
                foreach (CityEastLitterPart placed in parts)
                    if (Expand(placed.Footprint, MinimumItemClearance).Overlaps(footprint)) return false;
                int band = Mathf.Clamp(Mathf.FloorToInt((point.x - Bounds.xMin) / (Bounds.width / BandCount)), 0, BandCount - 1);
                parts.Add(new CityEastLitterPart(id, item, position, rotation, scale, footprint, band));
                return true;
            }
        }

        public bool IsPlacementAllowed(Rect footprint, bool solid)
        {
            if (!exit.IsEnabled || footprint.xMin < Bounds.xMin || footprint.xMax > Bounds.xMax ||
                footprint.yMin < Bounds.yMin || footprint.yMax > Bounds.yMax || exit.Swale.OverlapsCrossing(footprint)) return false;
            foreach (Rect excluded in exclusions)
                if (excluded.Overlaps(footprint)) return false;
            return !solid || !Expand(footprint, .4f).Overlaps(PedestrianCorridor);
        }

        private int Seed(string name) => CityLitterGeometry.Seed(name, exit.Layout.Seed, 0x51D3A927u);
        private static float Next(System.Random random) => (float)random.NextDouble();
        private static Rect Expand(Rect rect, float amount) => CityLitterGeometry.Expand(rect, amount);
        private static Rect Project(Bounds bounds, Vector3 position, Quaternion rotation, float scale) =>
            CityLitterGeometry.Project(bounds, position, rotation, scale);
    }
}
