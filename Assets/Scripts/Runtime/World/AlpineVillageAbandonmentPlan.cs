using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The old settlement shares one placement/shelf/forest contract. It
    /// never enters the inhabited House catalog or acquires its light and life.</summary>
    public sealed class AlpineVillageAbandonedPlot
    {
        public string Id { get; }
        public string Model { get; }
        public string Yard { get; }
        public Vector2 Center { get; }
        public Vector2 Size { get; }
        public Rect YardBounds { get; }
        public float Yaw { get; }
        public float Height { get; }
        public bool Closed { get; }
        public bool IsBuilding => Model != "HouseholdFoundation";
        public Vector3 GroundCenter { get; }
        public Vector3 Forward { get; }
        public Quaternion Rotation => Quaternion.LookRotation(Forward, Vector3.up);
        private readonly float sine, cosine;

        internal AlpineVillageAbandonedPlot(AlpineVillageExpansionPlan expansion, string id,
            string model, string yard, Vector2 center, float yaw, Vector2 size,
            float height, bool closed, Rect yardBounds)
        {
            Id = id; Model = model; Yard = yard; Center = center; Yaw = yaw;
            Size = size; Height = height; Closed = closed; YardBounds = yardBounds;
            sine = Mathf.Sin(yaw * Mathf.Deg2Rad); cosine = Mathf.Cos(yaw * Mathf.Deg2Rad);
            GroundCenter = expansion.ToWorld(center);
            Vector3 ahead = expansion.ToWorld(ToMap(Vector2.up));
            Forward = new Vector3(ahead.x - GroundCenter.x, 0f, ahead.z - GroundCenter.z).normalized;
        }

        public Vector2 ToMap(Vector2 point) => Center +
            new Vector2(cosine * point.x + sine * point.y, -sine * point.x + cosine * point.y);
        public Vector2 ToPlot(Vector2 point)
        {
            Vector2 d = point - Center;
            return new Vector2(cosine * d.x - sine * d.y, sine * d.x + cosine * d.y);
        }
        public Vector3 World(Vector2 point) => GroundCenter +
            Rotation * new Vector3(point.x, 0f, point.y);
        public float OutsideYard(Vector2 point) => OutsideRect(ToPlot(point), YardBounds);
        public float OutsideBody(Vector2 point) => OutsideRect(ToPlot(point),
            new Rect(-Size * .5f, Size));
        internal static float OutsideRect(Vector2 p, Rect r) => new Vector2(
            Mathf.Max(Mathf.Max(r.xMin - p.x, 0f), p.x - r.xMax),
            Mathf.Max(Mathf.Max(r.yMin - p.y, 0f), p.y - r.yMax)).magnitude;
    }

    public sealed class AlpineVillageBuildingSite
    {
        public string Id { get; }
        public Vector2 Center { get; }
        public Vector2 Size { get; }
        public float Height { get; }
        public AlpineVillageAbandonedPlot Abandoned { get; }
        internal AlpineVillageBuildingSite(string id, Vector2 center, Vector2 size,
            float height, AlpineVillageAbandonedPlot abandoned = null)
        { Id = id; Center = center; Size = size; Height = height; Abandoned = abandoned; }
    }

    public readonly struct AlpineVillageBuildingSightline
    {
        public readonly Vector2 Start, End;
        internal AlpineVillageBuildingSightline(Vector2 start, Vector2 end)
        { Start = start; End = end; }
    }

    public sealed class AlpineVillageAbandonmentPlan
    {
        public const string RootName = "Abandoned Settlement";
        private readonly List<AlpineVillageAbandonedPlot> plots = new List<AlpineVillageAbandonedPlot>();
        private readonly List<AlpineVillageBuildingSite> buildings = new List<AlpineVillageBuildingSite>();
        private readonly List<AlpineVillageBuildingSightline> sightlines = new List<AlpineVillageBuildingSightline>();
        private readonly AlpineVillageExpansionPlan expansion;
        internal AlpineVillageAbandonedPlot RescueForecourtPlot { get; }
        // Measured Blender apron: OldForecourt is .14 m high; its exposed
        // flagstones reach .20 m. The .32 m side curbs remain separate edges.
        internal static readonly Rect RescueForecourtBounds = new Rect(-6.6f, 3.75f, 13.2f, 8.9f);
        internal const float RescueForecourtPavingTop = .20f;
        internal const float RescueForecourtSnowClearance = .02f;
        public IReadOnlyList<AlpineVillageAbandonedPlot> Plots => plots;
        public IReadOnlyList<AlpineVillageBuildingSite> Buildings => buildings;
        public IReadOnlyList<AlpineVillageBuildingSightline> Sightlines => sightlines;

        internal AlpineVillageAbandonmentPlan(AlpineVillageExpansionPlan expansion, AlpineVillagePlan village)
        {
            this.expansion = expansion;
            Civic("town-hall", "TownHall", -88, 38, 180, 14, 10, 7.6f, -10.5f, -8, 10.5f, 16);
            Civic("school", "School", -106, 109, 180, 14, 8, 6, -10.5f, -7.5f, 10.5f, 16);
            Civic("shop-bakery", "ShopBakery", -89, -6, 0, 12, 9, 6.5f, -9.5f, -10, 9.5f, 11);
            Civic("workshop", "Workshop", -114, -13, 180, 10, 8, 5.3f, -7.5f, -7, 10.5f, 15);
            Civic("mountain-rescue", "MountainRescue", -185, 62, 90, 12, 8, 5.4f, -7.5f, -8, 11, 14);
            RescueForecourtPlot = plots[plots.Count - 1];

            // Eighteen former households: fourteen still standing, three open
            // structural ruins and one low foundation. Both sides of the old
            // inhabited street belong to the settlement, not only the west bowl.
            House(1, "AbandonedHouseA", 29, 10, 270);
            House(2, "WornHouseB", 36, 37, 270);
            House(3, "AbandonedHouseA", 24, 83, 270);
            House(4, "WornHouseA", -31, 41, 90);
            House(5, "RuinedHouse", -50, 96, 90);
            House(6, "AbandonedHouseA", -63, -13, 0);
            House(7, "AbandonedHouseB", -60, 16, 270);
            House(8, "WornHouseB", -52, 72, 270);
            House(9, "RuinedHouse", -77, 100, 180);
            House(10, "WornHouseA", -78, -30, 0);
            House(11, "HouseholdFoundation", -43, -18, 0);
            House(12, "AbandonedHouseB", -155, 10, 90);
            House(13, "WornHouseA", -174, 34, 90);
            House(14, "AbandonedHouseA", -168, 95, 90);
            House(15, "AvalancheRuinedHouse", -137, 106, 270);
            House(16, "AbandonedHouseB", -112, 35, 90);
            House(17, "WornHouseA", -81, 62, 180);
            House(18, "AbandonedHouseA", -96, -28, 0);

            Shed(1, 40, 16, 0, false);
            Shed(2, 30, 61, 270, false);
            Shed(3, -62, 87, 0, true);
            Shed(4, -115, 84, 0, false);
            Shed(5, -152, -16, 90, false);
            Shed(6, -186, 83, 90, false);
            Shed(7, -61, 30, 270, false);
            Shed(8, -121, -31, 90, false);

            foreach (AlpineVillagePlotDescriptor plot in village.Plots)
                if (plot.Kind != AlpineVillagePlotKind.Spring)
                    buildings.Add(new AlpineVillageBuildingSite(plot.StableId,
                        expansion.ToLocal(plot.GroundCenter), plot.FootprintSize, plot.Height));
            Existing("ski-lodge", expansion.LodgeCenter, expansion.LodgeSize, 5.2f);
            Existing("service-shed", expansion.ServiceShedCenter, expansion.ServiceShedSize, 3.4f);
            Existing("trade-warehouse", expansion.WarehouseCenter, new Vector2(10, 14), 5.5f);
            BuildSightlines();
        }

        private void Civic(string id, string model, float x, float z, float yaw,
            float width, float depth, float height, float minX, float minZ, float maxX, float maxZ)
            => Add(id, model, model + "Yard", x, z, yaw, width, depth, height, true,
                Rect.MinMaxRect(minX, minZ, maxX, maxZ));
        private void House(int index, string model, float x, float z, float yaw)
        {
            bool wide = model.EndsWith("B", StringComparison.Ordinal);
            bool closed = !model.EndsWith("RuinedHouse", StringComparison.Ordinal) && model != "HouseholdFoundation";
            Add("homestead-" + index.ToString("00"), model,
                "HouseholdYard" + (char)('A' + (index - 1) % 3), x, z, yaw,
                wide ? 9 : 8, wide ? 8 : 7, closed ? (wide ? 6.3f : 5.4f) : 3.7f,
                closed, Rect.MinMaxRect(-7.5f, -5.5f, 7.5f, 10));
        }
        private void Shed(int index, float x, float z, float yaw, bool ruined)
            => Add("outbuilding-" + index.ToString("00"), ruined ? "RuinedShed" : "AbandonedShed",
                null, x, z, yaw, 6, 4, 3.3f, !ruined, Rect.MinMaxRect(-4, -3, 4, 3));
        private void Add(string id, string model, string yard, float x, float z, float yaw,
            float width, float depth, float height, bool closed, Rect bounds)
        {
            var plot = new AlpineVillageAbandonedPlot(expansion, id, model, yard,
                new Vector2(x, z), yaw, new Vector2(width, depth), height, closed, bounds);
            plots.Add(plot);
            if (plot.IsBuilding) buildings.Add(new AlpineVillageBuildingSite(id,
                plot.Center, plot.Size, height, plot));
        }
        private void Existing(string id, Vector3 center, Vector2 size, float height)
            => buildings.Add(new AlpineVillageBuildingSite(id, expansion.ToLocal(center), size, height));

        private void BuildSightlines()
        {
            // Clear the nearest architectural neighbours from each side, rather
            // than pretending that centre distance alone is visibility. Actual
            // terrain/mesh/camera exposure is checked by the scene capture.
            foreach (AlpineVillageBuildingSite site in buildings)
            for (int side = 0; side < 4; side++)
            {
                Vector2 direction = side == 0 ? Vector2.up : side == 1 ? Vector2.right :
                    side == 2 ? Vector2.down : Vector2.left;
                Vector2 local = Vector2.Scale(direction, site.Size * .5f + Vector2.one * 3f);
                Vector2 eye = site.Abandoned != null ? site.Abandoned.ToMap(local) : site.Center + local;
                var candidates = new List<AlpineVillageBuildingSite>();
                foreach (AlpineVillageBuildingSite target in buildings)
                {
                    if (ReferenceEquals(target, site)) continue;
                    Vector2 toward = target.Center - eye;
                    if (toward.sqrMagnitude > 62f * 62f) continue;
                    if (site.Abandoned == null)
                    {
                        if (Vector2.Dot(eye - site.Center, toward) < -2f) continue;
                    }
                    else if (site.Abandoned.Closed && CrossesBody(site.Abandoned, eye, target.Center)) continue;
                    candidates.Add(target);
                }
                candidates.Sort((a, b) => (a.Center - eye).sqrMagnitude.CompareTo((b.Center - eye).sqrMagnitude));
                // Two alternatives retain a neighbour when a partial ruined
                // wall hides the closest one; a sideways view can skirt a wall
                // even when the other building is behind its facade plane.
                for (int i = 0; i < Mathf.Min(2, candidates.Count); i++)
                    sightlines.Add(new AlpineVillageBuildingSightline(eye, candidates[i].Center));
            }
        }

        private static bool CrossesBody(AlpineVillageAbandonedPlot plot, Vector2 start, Vector2 end)
        {
            Vector2 a = plot.ToPlot(start), delta = plot.ToPlot(end) - a;
            float enter = 0f, leave = 1f;
            for (int axis = 0; axis < 2; axis++)
            {
                float half = plot.Size[axis] * .5f + .15f;
                if (Mathf.Abs(delta[axis]) < .0001f)
                {
                    if (Mathf.Abs(a[axis]) > half) return false;
                    continue;
                }
                float low = (-half - a[axis]) / delta[axis], high = (half - a[axis]) / delta[axis];
                enter = Mathf.Max(enter, Mathf.Min(low, high));
                leave = Mathf.Min(leave, Mathf.Max(low, high));
                if (enter > leave) return false;
            }
            return true;
        }

        internal bool ClearsFeatures(Vector2 local, float radius)
        {
            foreach (AlpineVillageAbandonedPlot plot in plots)
                if (plot.OutsideYard(local) < radius + 1.5f) return false;
            foreach (AlpineVillageBuildingSightline sightline in sightlines)
                if (DistanceToSegment(local, sightline.Start, sightline.End) < radius + 2.4f) return false;
            return true;
        }

        internal float ShapeGround(Vector2 local, float height)
        {
            foreach (AlpineVillageAbandonedPlot plot in plots)
            {
                float outside = plot.OutsideYard(local);
                if (outside >= 4f) continue;
                height = Mathf.Lerp(height, plot.GroundCenter.y,
                    1f - Mathf.SmoothStep(0f, 1f, outside / 4f));
            }
            return height;
        }

        internal float LimitSnow(Vector2 local, float depth)
        {
            foreach (AlpineVillageAbandonedPlot plot in plots)
            {
                if (plot.OutsideYard(local) > .5f) continue;
                Vector2 p = plot.ToPlot(local);
                // Half-buried paving and fragments remain readable; doors gain
                // no artificial trampled approach. Open ruins keep lying snow.
                float limit = plot.Model == "HouseholdFoundation" ? .17f :
                    plot.Yard != null && !plot.Yard.StartsWith("Household", StringComparison.Ordinal) ? .16f : .26f;
                float wind = .055f * Mathf.Sin(p.x * .7f + p.y * .4f);
                float outer = Mathf.SmoothStep(0f, 1f, plot.OutsideYard(local) * 2f);
                depth = Mathf.Lerp(Mathf.Min(depth, limit + wind), depth, outer);
            }
            return depth;
        }

        internal float SampleSnowSupport(Vector2 world, float ground)
        {
            Vector2 local = RescueForecourtPlot.ToPlot(expansion.ToLocal(world));
            float outside = AlpineVillageAbandonedPlot.OutsideRect(local, RescueForecourtBounds);
            // Keep the full support beyond one field-cell diagonal: every
            // vertex of a triangle crossing the apron must clear its paving,
            // including after treading. Only the outer collar blends away.
            float margin = AlpineVillageSnowDrift.FieldCellSize * 1.5f;
            float weight = 1f - Mathf.SmoothStep(0f, 1f, (outside - margin) / 1.2f);
            float support = RescueForecourtPlot.GroundCenter.y +
                RescueForecourtPavingTop + RescueForecourtSnowClearance;
            return Mathf.Lerp(ground, Mathf.Max(ground, support), weight);
        }

        internal static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            float t = delta.sqrMagnitude < .0001f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, delta) / delta.sqrMagnitude);
            return Vector2.Distance(p, a + delta * t);
        }
    }
}
