using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The closed mainland road and its visible municipal boundary.</summary>
    public sealed class CityEastExitPlan
    {
        internal CityEastExitPlan() { }
        internal CityEastExitPlan(CityLayout layout, Rect yard, Rect northYard)
        {
            IsEnabled = true;
            Layout = layout;
            YardBounds = yard;
            NorthYardBounds = northYard;
            float axisZ = yard.yMin + layout.NodeSpacing.y * .5f;
            float streetX = yard.xMin - layout.RoadWidth * .5f;
            float gateX = yard.xMax - 28f;
            float entryTop = CityEastExitPlanner.RawGroundTop(layout,
                new Vector2(yard.xMin, axisZ)) + .22f;
            if (layout.ElevationPlan.TrySampleSurface(new Vector2(streetX, axisZ),
                CitySurfaceRole.SidewalkTop, out float streetTop, out _))
                entryTop = streetTop;
            float gateTop = CityEastExitPlanner.RawGroundTop(layout,
                new Vector2(gateX, axisZ)) + RoadSurfaceLift;
            YardEntryTop = entryTop + .008f;
            float streetRoadTop = entryTop - .06f;
            layout.ElevationPlan.TrySampleSurface(new Vector2(streetX, axisZ),
                CitySurfaceRole.RoadTop, out streetRoadTop, out _);
            ApproachStart = new Vector3(streetX, streetRoadTop + .008f, axisZ);
            CheckpointPosition = new Vector3(gateX, gateTop, axisZ);
            RoadEnd = new Vector3(yard.xMax, gateTop, axisZ);
            RoadBounds = Rect.MinMaxRect(streetX, axisZ - 4f, yard.xMax, axisZ + 4f);
            ClearanceBounds = Rect.MinMaxRect(streetX, axisZ - 11f, yard.xMax, axisZ + 8f);
            // The three visible fence runs close the same outboard land the
            // walk mask excludes, including approaches from church and beach.
            ClosedGroundBounds = Rect.MinMaxRect(gateX - FenceThickness * .5f,
                yard.yMin - FenceThickness * .5f, yard.xMax + .25f,
                northYard.yMax + FenceThickness * .5f);
            BoothPosition = new Vector3(gateX - 2f, gateTop - RoadSurfaceLift, axisZ - 7f);
            BoothBounds = new Rect(BoothPosition.x - 1.5f, BoothPosition.z - 1.7f, 3f, 3.4f);
            BoothPad = new Rect(BoothPosition.x - 3.5f, BoothPosition.z - 3f, 7f, 6f);
            LampPosition = new Vector3(gateX - 4f, gateTop - RoadSurfaceLift, axisZ - 4.4f);
            var fences = new List<CityEastExitFence>();
            AddFence(fences, new Vector2(gateX, yard.yMin), new Vector2(gateX, axisZ - 4.4f));
            AddFence(fences, new Vector2(gateX, axisZ + 3f), new Vector2(gateX, northYard.yMax));
            AddFence(fences, new Vector2(gateX, yard.yMin), new Vector2(yard.xMax, yard.yMin));
            AddFence(fences, new Vector2(gateX, northYard.yMax), new Vector2(yard.xMax, northYard.yMax));
            // Close the exposed outer edge too; the church already owns its
            // eastern iron and the cemetery keeps its original smaller edge.
            AddFence(fences, new Vector2(yard.xMax - .12f, yard.yMin),
                new Vector2(yard.xMax - .12f, axisZ - 4.5f));
            AddFence(fences, new Vector2(yard.xMax - .12f, axisZ + 4.5f),
                new Vector2(yard.xMax - .12f, northYard.yMax));
            Fences = new ReadOnlyCollection<CityEastExitFence>(fences);
        }

        public const float RoadSurfaceLift = .035f;
        public const float FenceThickness = .20f;
        public bool IsEnabled { get; }
        public bool Enabled => IsEnabled;
        internal CityLayout Layout { get; }
        public Vector3 ApproachStart { get; }
        public Vector3 CheckpointPosition { get; }
        public Vector3 RoadEnd { get; }
        public Vector3 RoadAxis => Vector3.right;
        public Rect RoadBounds { get; }
        public Rect ClearanceBounds { get; }
        public Rect ClosedGroundBounds { get; }
        public Rect YardBounds { get; }
        public Rect NorthYardBounds { get; }
        public Rect BoothBounds { get; }
        public Rect BoothPad { get; }
        public Vector3 BoothPosition { get; }
        public Vector3 LampPosition { get; }
        private float YardEntryTop { get; }
        public IReadOnlyList<CityEastExitFence> Fences { get; } = Array.Empty<CityEastExitFence>();

        public float SampleRoadTop(float x)
        {
            // The stop apron is level; the approach absorbs the existing
            // yard elevation before the booth and the closed gate.
            if (x <= YardBounds.xMin)
                return Mathf.Lerp(ApproachStart.y, YardEntryTop,
                    Mathf.InverseLerp(ApproachStart.x, YardBounds.xMin, x));
            return Mathf.Lerp(YardEntryTop, CheckpointPosition.y,
                Mathf.InverseLerp(YardBounds.xMin, CheckpointPosition.x - 12f, x));
        }

        internal float ApplyGroundTop(Vector2 point, float original)
        {
            if (!IsEnabled || point.x < YardBounds.xMin || point.x > YardBounds.xMax ||
                point.y < YardBounds.yMin || point.y > NorthYardBounds.yMax) return original;
            float roadWeight = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(4f, 7f, Mathf.Abs(point.y - CheckpointPosition.z)));
            float top = Mathf.Lerp(original, SampleRoadTop(point.x) - RoadSurfaceLift, roadWeight);
            float dx = Mathf.Max(BoothPad.xMin - point.x, 0f, point.x - BoothPad.xMax);
            float dz = Mathf.Max(BoothPad.yMin - point.y, 0f, point.y - BoothPad.yMax);
            float padWeight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Max(dx, dz) / 2f));
            return Mathf.Lerp(top, BoothPosition.y, padWeight);
        }

        public float SampleGroundTop(Vector2 point) => ApplyGroundTop(point,
            CityEastExitPlanner.RawGroundTop(Layout, point));

        private void AddFence(ICollection<CityEastExitFence> fences, Vector2 start, Vector2 end)
        {
            int count = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(start, end) / 4f));
            for (int i = 0; i < count; i++)
            {
                Vector2 a = Vector2.Lerp(start, end, i / (float)count);
                Vector2 b = Vector2.Lerp(start, end, (i + 1f) / count);
                fences.Add(new CityEastExitFence(new Vector3(a.x, SampleGroundTop(a), a.y),
                    new Vector3(b.x, SampleGroundTop(b), b.y)));
            }
        }
    }

    public readonly struct CityEastExitFence
    {
        internal CityEastExitFence(Vector3 start, Vector3 end) { Start = start; End = end; }
        public Vector3 Start { get; }
        public Vector3 End { get; }
    }

    public static class CityEastExitPlanner
    {
        private static readonly ConditionalWeakTable<CityLayout, CityEastExitPlan> Plans =
            new ConditionalWeakTable<CityLayout, CityEastExitPlan>();
        public static CityEastExitPlan Create(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            return Plans.GetValue(layout, CreateUncached);
        }

        private static CityEastExitPlan CreateUncached(CityLayout layout)
        {
            if (layout.BlueprintId != CityBlueprintCatalog.DefaultBlueprintId)
                return new CityEastExitPlan();
            Rect yard = BoundsFor(layout, "yard-east");
            Rect north = BoundsFor(layout, "yard-north-east");
            var plan = new CityEastExitPlan(layout, yard, north);
            if (yard.width < 70f || yard.height < 30f || Mathf.Abs(yard.yMax - north.yMin) > .01f ||
                plan.RoadBounds.yMin <= yard.yMin || plan.BoothPad.yMin <= yard.yMin ||
                plan.CheckpointPosition.x <= plan.ApproachStart.x + 40f)
                throw new InvalidOperationException("The eastern road needs its contiguous north-of-church yards.");
            return plan;
        }

        internal static Rect BoundsFor(CityLayout layout, string areaId)
        {
            Rect result = default;
            bool found = false;
            foreach (CitySurfaceDescriptor surface in layout.Surfaces)
            {
                if (surface.AreaId != areaId) continue;
                Rect r = surface.WorldBounds;
                result = found ? Rect.MinMaxRect(Mathf.Min(result.xMin, r.xMin),
                    Mathf.Min(result.yMin, r.yMin), Mathf.Max(result.xMax, r.xMax),
                    Mathf.Max(result.yMax, r.yMax)) : r;
                found = true;
            }
            if (!found) throw new InvalidOperationException("Missing eastern yard " + areaId);
            return result;
        }

        // Bypass the eastern grade while constructing its immutable plan.
        internal static float RawGroundTop(CityLayout layout, Vector2 point)
        {
            foreach (CitySurfaceDescriptor surface in layout.Surfaces)
            {
                Rect bounds = surface.WorldBounds;
                if (surface.IsWater || point.x < bounds.xMin - .01f || point.x > bounds.xMax + .01f ||
                    point.y < bounds.yMin - .01f || point.y > bounds.yMax + .01f) continue;
                return CityTerrainSurfacePlan.SampleContinuousDatum(layout.ElevationPlan,
                    surface.Cell, point, surface.DatumY) + CityElevationPlan.GroundTopOffset;
            }
            if (layout.ElevationPlan.TrySampleSurface(point, CitySurfaceRole.RoadTop,
                out float road, out _)) return road;
            throw new InvalidOperationException("Eastern ground sample leaves authored land: " + point);
        }
    }
}
