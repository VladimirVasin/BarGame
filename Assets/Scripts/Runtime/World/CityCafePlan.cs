using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The relocated cafe owns a whole urban lot and a level patch
    /// of the existing terrain. Geometry, cast and contacts keep their metres.</summary>
    public sealed class CityCafePlan
    {
        private const float GroundMargin = .10f;
        private const float GroundBlend = 1.8f;
        private const float FrontSetback = 2f;
        private static readonly CityCafePlan Absent = new CityCafePlan();
        private static readonly ConditionalWeakTable<CityLayout, CityCafePlan> Plans =
            new ConditionalWeakTable<CityLayout, CityCafePlan>();
        private static readonly Vector2[] LocalFootprint =
        {
            new Vector2(-5.32f, -4.56f), new Vector2(1.68f, -4.56f),
            new Vector2(4.48f, -1.76f), new Vector2(4.48f, 5.44f),
            new Vector2(-5.32f, 5.44f)
        };
        internal static readonly Vector2Int[] CandidateCells =
        {
            new Vector2Int(10, 0), new Vector2Int(9, 0),
            new Vector2Int(10, 1), new Vector2Int(9, 1),
            new Vector2Int(10, 2), new Vector2Int(9, 2)
        };
        private readonly Rect groundCore;
        private readonly Rect groundReach;

        private CityCafePlan() { }

        private CityCafePlan(CityLayout layout, BuildingLot lot)
        {
            Cell = lot.Cell;
            CitySurfaceDescriptor surface = default;
            bool hasSurface = false;
            foreach (CitySurfaceDescriptor candidate in layout.Surfaces)
                if (candidate.Cell == Cell) { surface = candidate; hasSurface = true; break; }
            if (!hasSurface)
                throw new InvalidOperationException("The city cafe requires its existing ground surface.");
            Vector2 entrance = new Vector2(lot.DoorPosition.x, lot.DoorPosition.z);
            // This upstream sampler cannot read cafe support, preventing a
            // circular ground-plan dependency. The door meets the old grade.
            FloorY = CityTerrainSurfacePlan.SampleTopWithoutRigidSupports(layout, surface, entrance);
            Vector3 center = new Vector3(lot.Center.x, FloorY, lot.Center.z);
            Vector2[] footprint = CreateFootprint(center);
            Cafe = new MountainRoadCafePlan("terminal-cafe", center,
                Vector3.right, Vector3.forward, FloorY,
                MountainRoadTerminalPlanner.CafeHeight, 2.8f,
                new Vector3(entrance.x, FloorY, entrance.y),
                MountainRoadTerminalPlanner.CafeDoorWidth, footprint);
            Bounds = CityRoadPolygon.Bounds(footprint);
            float sidewalkEdge = layout.GetCellWorldBounds(Cell).yMin + layout.RoadWidth * .5f;
            ApproachBounds = Rect.MinMaxRect(entrance.x - 1.15f, sidewalkEdge - .15f,
                entrance.x + 1.15f, entrance.y + .35f);
            groundCore = Expanded(Bounds, GroundMargin);
            groundReach = Expanded(groundCore, GroundBlend);
        }

        public MountainRoadCafePlan Cafe { get; }
        public Vector2Int Cell { get; }
        public Rect Bounds { get; }
        public Rect ApproachBounds { get; }
        public float FloorY { get; }

        public static CityCafePlan Create(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (layout.BlueprintId != CityBlueprintCatalog.DefaultBlueprintId ||
                !layout.SpatialPlan.IsCompactCoastal) return null;
            CityCafePlan plan = Plans.GetValue(layout, value =>
            {
                foreach (BuildingLot lot in value.BuildingLots)
                    if (lot.IsCafe) return new CityCafePlan(value, lot);
                return Absent;
            });
            return ReferenceEquals(plan, Absent) ? null : plan;
        }

        internal static bool Supports(CityGenerationSettings settings) =>
            settings.Blueprint?.Id == CityBlueprintCatalog.DefaultBlueprintId &&
            settings.SpatialPlan?.IsCompactCoastal == true;

        internal static Vector2[] CreateFootprint(Vector3 center)
        {
            var footprint = new Vector2[LocalFootprint.Length];
            for (int index = 0; index < footprint.Length; index++)
                footprint[index] = new Vector2(center.x + LocalFootprint[index].x,
                    center.z + LocalFootprint[index].y);
            return footprint;
        }

        internal static BuildingLot CreateLot(CityGenerationSettings settings, Vector3 origin, Vector2Int cell)
        {
            Vector3 center = CityLayoutGenerator.GetLotCenter(settings, origin, cell);
            Vector2 cellMinimum = settings.GetCoordinateOffset(cell);
            center.z = origin.z + cellMinimum.y + settings.RoadWidth * .5f + FrontSetback + 4.56f;
            Vector3 door = center + new Vector3(-3.52f, 0f, -4.56f);
            Vector3 road = new Vector3(door.x, 0f, origin.z + cellMinimum.y);
            Vector3 sidewalk = road + Vector3.forward *
                (settings.RoadWidth * .5f - CityStreetSurfacePlanner.SidewalkWidth * .5f);
            if (!settings.Blueprint.TryGetArea(cell, out CityAreaDefinition area))
                throw new InvalidOperationException("The cafe lot requires an urban area.");
            return new BuildingLot(cell, center, new Vector2(9.8f, 10f),
                MountainRoadTerminalPlanner.CafeHeight, Color.white,
                area.Id, area.Archetype,
                CityLandUseKind.Cafe, false, false, false, string.Empty,
                BarActivityKind.None, Vector2Int.down, door, road, sidewalk);
        }

        public float ApplyGroundTop(Vector2 point, float rawTop)
        {
            if (!groundReach.Contains(point)) return rawTop;
            float dx = Mathf.Max(Mathf.Max(groundCore.xMin - point.x, 0f), point.x - groundCore.xMax);
            float dz = Mathf.Max(Mathf.Max(groundCore.yMin - point.y, 0f), point.y - groundCore.yMax);
            float distance = Mathf.Max(dx, dz);
            return Mathf.Lerp(FloorY, rawTop, Mathf.SmoothStep(0f, 1f, distance / GroundBlend));
        }

        internal bool BlocksGroundPosition(Vector3 position, float radius)
        {
            var point = new Vector2(position.x, position.z);
            return Expanded(Bounds, radius).Contains(point) ||
                   Expanded(ApproachBounds, radius).Contains(point);
        }

        internal void AppendAxisAnchors(Rect patch, ICollection<float> x, ICollection<float> z)
        {
            if (!groundReach.Overlaps(patch)) return;
            x.Add(groundCore.xMin); x.Add(groundCore.xMax);
            x.Add(groundReach.xMin); x.Add(groundReach.xMax);
            z.Add(groundCore.yMin); z.Add(groundCore.yMax);
            z.Add(groundReach.yMin); z.Add(groundReach.yMax);
            // Keep the shallow exterior blend close to its sampled grade;
            // the flat interior itself needs only its exact edge anchors.
            for (int step = 1; step < 4; step++)
            {
                float distance = GroundBlend * step * .25f;
                x.Add(groundCore.xMin - distance); x.Add(groundCore.xMax + distance);
                z.Add(groundCore.yMin - distance); z.Add(groundCore.yMax + distance);
            }
        }

        private static Rect Expanded(Rect bounds, float amount) => Rect.MinMaxRect(
            bounds.xMin - amount, bounds.yMin - amount, bounds.xMax + amount, bounds.yMax + amount);
    }
}
