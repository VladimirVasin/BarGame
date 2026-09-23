using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Measured western side valleys. Coordinates are metres right/uphill
    /// of the unchanged village foot; terrain, paths and movement share this plan.</summary>
    public sealed class AlpineVillageExpansionPlan
    {
        private readonly AlpineVillagePlan village;
        private readonly Capsule[] regions =
        {
            new Capsule(new Vector2(-35f, -2f), new Vector2(-120f, 8f), 42f),
            new Capsule(new Vector2(-137f, 54f), new Vector2(-137f, 54f), 68f),
            new Capsule(new Vector2(-155f, 86f), new Vector2(-77f, 81f), 39f),
            new Capsule(new Vector2(-70f, 65f), new Vector2(-37f, -3f), 12f),
            new Capsule(new Vector2(-127f, -3f), new Vector2(-130f, -44f), 10f)
        };
        private readonly ReadOnlyCollection<AlpineVillagePathDescriptor> paths;
        private readonly ReadOnlyCollection<Bounds> obstacles;

        internal AlpineVillageExpansionPlan(AlpineVillagePlan plan)
        {
            village = plan ?? throw new ArgumentNullException(nameof(plan));
            LodgeCenter = ToWorld(new Vector2(-137f, 56f));
            ServiceShedCenter = ToWorld(new Vector2(-113f, 68f));
            LiftBasePosition = ToWorld(new Vector2(-154f, 86f));
            LiftTopPosition = ToWorld(new Vector2(-151f, 114f));
            CliffBarrierCenter = ToWorld(new Vector2(-130f, -53.5f));
            CliffEdge = ToWorld(new Vector2(-130f, -54f));
            ForestEntrance = ToWorld(new Vector2(-37f, -3f));
            LocalBounds = Rect.MinMaxRect(-205f, -54f, 7f, 125f);
            WorldBounds = TransformBounds(LocalBounds);
            CoveredBounds = TransformBounds(new Rect(-146f, 50f, 18f, 12f));

            var routes = new List<AlpineVillagePathDescriptor>();
            AddRoute(routes, "forest-approach", AlpineVillagePathKind.ForestTrail, 1f,
                new Vector2(-1f, 0f), new Vector2(-13f, -2f), new Vector2(-37f, -3f),
                new Vector2(-78f, 6f), new Vector2(-120f, 22f));
            AddRoute(routes, "ski-base-approach", AlpineVillagePathKind.SkiBaseAccess, 1.25f,
                new Vector2(-120f, 22f), new Vector2(-137f, 42f), new Vector2(-137f, 50f));
            AddRoute(routes, "forest-loop", AlpineVillagePathKind.ForestTrail, 1f,
                new Vector2(-137f, 42f), new Vector2(-166f, 50f), new Vector2(-157f, 84f),
                new Vector2(-112f, 93f));
            AddRoute(routes, "old-road-return", AlpineVillagePathKind.AbandonedRoad, 2.7f,
                new Vector2(-112f, 93f), new Vector2(-70f, 65f), new Vector2(-49f, 25f),
                new Vector2(-37f, -3f));
            AddRoute(routes, "ski-service", AlpineVillagePathKind.SkiBaseAccess, 1.1f,
                new Vector2(-157f, 84f), new Vector2(-153f, 106f));
            AddRoute(routes, "old-city-road", AlpineVillagePathKind.AbandonedRoad, 2.7f,
                new Vector2(-120f, 22f), new Vector2(-127f, -3f),
                new Vector2(-130f, -27f), new Vector2(-130f, -52f));
            paths = routes.AsReadOnly();

            // Axis-aligned in this plan's metre frame. The imported lodge uses
            // exactly these wall and furniture dimensions, including a real door.
            var blocks = new List<Bounds>();
            Vector2 lodge = new Vector2(-137f, 56f);
            Block(blocks, lodge + new Vector2(-8.84f, 0f), new Vector2(.32f, 12f));
            Block(blocks, lodge + new Vector2(8.84f, 0f), new Vector2(.32f, 12f));
            Block(blocks, lodge + new Vector2(0f, 5.84f), new Vector2(18f, .32f));
            Block(blocks, lodge + new Vector2(-5.15f, -5.84f), new Vector2(7.7f, .32f));
            Block(blocks, lodge + new Vector2(5.15f, -5.84f), new Vector2(7.7f, .32f));
            Block(blocks, lodge + new Vector2(-1.72f, -4.8f), new Vector2(.16f, 2.1f));
            Block(blocks, lodge + new Vector2(1.72f, -4.8f), new Vector2(.16f, 2.1f));
            Block(blocks, lodge + new Vector2(-6.4f, -.8f), new Vector2(.65f, 6.4f));
            Block(blocks, lodge + new Vector2(6.4f, -2.2f), new Vector2(.65f, 3.5f));
            Block(blocks, lodge + new Vector2(0f, 4.75f), new Vector2(11.8f, .8f));
            Block(blocks, lodge + new Vector2(4.7f, 1f), new Vector2(3.8f, .8f));
            Block(blocks, new Vector2(-113f, 68f), ServiceShedSize);
            Block(blocks, new Vector2(-154f, 86f), new Vector2(.7f, .7f));
            Block(blocks, new Vector2(-151f, 114f), new Vector2(.7f, .7f));
            Block(blocks, new Vector2(-130f, -53.5f), new Vector2(CliffBarrierWidth, .5f));
            obstacles = blocks.AsReadOnly();
        }

        public Vector3 LodgeCenter { get; }
        public Vector3 LodgeForward => village.Uphill;
        public Vector2 LodgeSize => new Vector2(18f, 12f);
        public float LodgeFloorHeight => LodgeCenter.y;
        public Vector3 LodgeEntrance => LodgeCenter - LodgeForward * 6f;
        public Vector3 LodgeApproach => LodgeCenter - LodgeForward * 9f;
        public Vector3 ServiceShedCenter { get; }
        public Vector2 ServiceShedSize => new Vector2(8f, 6f);
        public Vector3 LiftBasePosition { get; }
        public Vector3 LiftTopPosition { get; }
        public Vector3 CliffBarrierCenter { get; }
        public float CliffBarrierWidth => 12f;
        public Vector3 CliffEdge { get; }
        public Vector3 ForestEntrance { get; }
        public Rect LocalBounds { get; }
        public Rect WorldBounds { get; }
        public Rect CoveredBounds { get; }
        public IReadOnlyList<AlpineVillagePathDescriptor> Paths => paths;
        /// <summary>Blocking X/Z rectangles in the local metre frame.</summary>
        public IReadOnlyList<Bounds> LocalObstacles => obstacles;

        public Vector3 ToWorld(Vector2 local)
        {
            Vector3 point = village.SlopeOrigin + village.SlopeRight * local.x + village.Uphill * local.y;
            point.y = AlpineVillageTerrainSampler.SampleMacroHeight(
                village.SlopeOrigin, village.Uphill, village.Grade, new Vector2(point.x, point.z));
            return point;
        }

        public Vector2 ToLocal(Vector3 point) => ToLocal(new Vector2(point.x, point.z));
        public Vector2 ToLocal(Vector2 point)
        {
            Vector3 delta = new Vector3(point.x - village.SlopeOrigin.x, 0f,
                point.y - village.SlopeOrigin.z);
            return new Vector2(Vector3.Dot(delta, village.SlopeRight), Vector3.Dot(delta, village.Uphill));
        }

        public float DistanceToGround(Vector2 point)
        {
            Vector2 local = ToLocal(point);
            float distance = float.PositiveInfinity;
            foreach (Capsule region in regions) distance = Mathf.Min(distance, region.Distance(local));
            // The old road ends at a narrow, visibly closed throat. Its flanks
            // rise into rock, so walking around the barrier is not an escape.
            if (local.y < -42f && Mathf.Abs(local.x + 130f) < 20f)
                distance = Mathf.Max(distance, Mathf.Abs(local.x + 130f) - 6f);
            return Mathf.Max(distance, -54f - local.y);
        }

        public bool ContainsGround(Vector2 point, float radius = 0f) => DistanceToGround(point) <= -radius;

        public Vector2 ClosestGround(Vector2 point, float radius = 0f)
        {
            Vector2 local = ToLocal(point);
            Vector2 closest = local;
            float best = float.PositiveInfinity;
            foreach (Capsule region in regions)
            {
                Vector2 candidate = region.Closest(local, radius + .002f);
                candidate.y = Mathf.Max(candidate.y, -54f + radius + .002f);
                if (candidate.y < -42f && Mathf.Abs(candidate.x + 130f) < 20f)
                    candidate.x = Mathf.Clamp(candidate.x, -136f + radius + .002f, -124f - radius - .002f);
                float distance = (candidate - local).sqrMagnitude;
                if (distance >= best) continue;
                closest = candidate;
                best = distance;
            }
            Vector3 world = ToWorld(closest);
            return new Vector2(world.x, world.z);
        }

        public float DistanceOutsideLodge(Vector2 point)
        {
            Vector2 local = ToLocal(point) - new Vector2(-137f, 56f);
            return new Vector2(Mathf.Max(0f, Mathf.Abs(local.x) - 9f),
                Mathf.Max(0f, Mathf.Abs(local.y) - 6f)).magnitude;
        }

        public bool IsInterior(Vector2 point) => DistanceOutsideLodge(point) <= .001f;
        public bool IsInterior(Vector3 point) => IsInterior(new Vector2(point.x, point.z));

        internal float ShapeGround(Vector2 point, float height)
        {
            Vector2 local = ToLocal(point);
            float lodgeDistance = DistanceOutsideLodge(point);
            if (lodgeDistance < 6f)
            {
                float weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((lodgeDistance - 2f) / 4f));
                height = Mathf.Lerp(height, LodgeFloorHeight, weight);
                // The floor is authored at +.02; the bed sits eight cm below it.
                float inside = Mathf.Min(9f - Mathf.Abs(local.x + 137f), 6f - Mathf.Abs(local.y - 56f));
                height -= .08f * Mathf.Clamp01(inside / .2f);
            }
            float shedDistance = OutsideRect(local, new Vector2(-113f, 68f), new Vector2(4f, 3f));
            if (shedDistance < 4f)
                height = Mathf.Lerp(height, ServiceShedCenter.y,
                    1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((shedDistance - 1f) / 3f)));
            return height;
        }

        internal float ShapeCliff(Vector2 point, float height)
        {
            Vector2 local = ToLocal(point);
            if (local.y >= -54f) return height;
            float across = Mathf.Abs(local.x + 130f);
            if (across >= 20f) return height;
            float blend = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((across - 9f) / 11f));
            // Keep the lower remnant within the existing storm's readable
            // depth. A deeper cut erased the road behind the near lip and fog.
            float drop = Mathf.Min(12f, (-54f - local.y) * 1.8f);
            float floor = ToWorld(local).y - drop;
            return Mathf.Lerp(height, floor, blend);
        }

        internal bool ClearsFeatures(Vector2 point, float radius)
        {
            Vector2 local = ToLocal(point);
            if (DistanceOutsideLodge(point) < radius + 4f ||
                OutsideRect(local, new Vector2(-113f, 68f), new Vector2(4f, 3f)) < radius + 3f)
                return false;
            // The remnant beginner tow strip is kept open, equipment included.
            return OutsideRect(local, new Vector2(-152.5f, 100f), new Vector2(8f, 20f)) >= radius + 2f;
        }

        private void AddRoute(List<AlpineVillagePathDescriptor> target, string id,
            AlpineVillagePathKind kind, float halfWidth, params Vector2[] points)
        {
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 start = ToWorld(points[i - 1]);
                Vector3 end = ToWorld(points[i]);
                start.y = ShapeGround(new Vector2(start.x, start.z), start.y);
                end.y = ShapeGround(new Vector2(end.x, end.z), end.y);
                target.Add(new AlpineVillagePathDescriptor("village-" + id + "-" + i,
                    "village-ski-base", kind, start, end, halfWidth, halfWidth + .15f));
            }
        }

        private Rect TransformBounds(Rect rect)
        {
            Vector3 a = ToWorld(new Vector2(rect.xMin, rect.yMin));
            Vector3 b = ToWorld(new Vector2(rect.xMax, rect.yMin));
            Vector3 c = ToWorld(new Vector2(rect.xMin, rect.yMax));
            Vector3 d = ToWorld(new Vector2(rect.xMax, rect.yMax));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x, c.x, d.x), Mathf.Min(a.z, b.z, c.z, d.z),
                Mathf.Max(a.x, b.x, c.x, d.x), Mathf.Max(a.z, b.z, c.z, d.z));
        }

        private static float OutsideRect(Vector2 point, Vector2 center, Vector2 half) =>
            new Vector2(Mathf.Max(0f, Mathf.Abs(point.x - center.x) - half.x),
                Mathf.Max(0f, Mathf.Abs(point.y - center.y) - half.y)).magnitude;

        private static void Block(List<Bounds> target, Vector2 center, Vector2 size) =>
            target.Add(new Bounds(new Vector3(center.x, 0f, center.y), new Vector3(size.x, 1f, size.y)));

        private readonly struct Capsule
        {
            private readonly Vector2 start, end;
            private readonly float radius;
            public Capsule(Vector2 start, Vector2 end, float radius)
            { this.start = start; this.end = end; this.radius = radius; }
            private Vector2 AxisPoint(Vector2 point)
            {
                Vector2 delta = end - start;
                return start + delta * (delta.sqrMagnitude < .0001f ? 0f :
                    Mathf.Clamp01(Vector2.Dot(point - start, delta) / delta.sqrMagnitude));
            }
            public float Distance(Vector2 point) => Vector2.Distance(point, AxisPoint(point)) - radius;
            public Vector2 Closest(Vector2 point, float inset)
            {
                Vector2 axis = AxisPoint(point);
                return axis + Vector2.ClampMagnitude(point - axis, Mathf.Max(0f, radius - inset));
            }
        }
    }
}
