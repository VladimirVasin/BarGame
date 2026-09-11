using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Fixed metre industrial site, with an ordinary public through passage
    /// beside the production floor and a separate reversing/loading strip.</summary>
    public sealed class CityCanneryPlan
    {
        private static readonly ConditionalWeakTable<CityLayout, CityCanneryPlan> Plans =
            new ConditionalWeakTable<CityLayout, CityCanneryPlan>();
        private readonly CityLayout layout;
        public const float YardTop = .08f;
        public const float FloorTop = .18f;
        public const float TurningRadius = 6f;
        public CityDistrictPointOfInterestDescriptor Descriptor { get; }
        public Vector3 Origin { get; }
        public Quaternion Rotation { get; }
        public RoadEdge FrontageEdge { get; }
        public Vector3 Forward => Rotation * Vector3.forward;
        public Vector3 Right => Rotation * Vector3.right;
        public Vector3 TruckParkedRearAxle => World(new Vector3(5f, YardTop, -1.3f));
        public Vector3 ReverseStart => World(new Vector3(-1.25f, YardTop, 13f));
        public Bounds HallBounds => World(new Bounds(new Vector3(-4f, 2.1f, 0f), new Vector3(8f, 4.2f, 14f)));
        public Rect ProductionBounds => World(Rect.MinMaxRect(-8f, -7f, -2f, 7f));
        public Rect StreetOpening => World(Rect.MinMaxRect(1.5f, 8.95f, 8f, 10.05f));
        public IReadOnlyList<Rect> PublicRectangles { get; }

        private CityCanneryPlan(CityLayout layout, CityDistrictPointOfInterestDescriptor descriptor)
        {
            this.layout = layout;
            Descriptor = descriptor;
            if (descriptor.PublicBounds.width < 17.99f || descriptor.PublicBounds.height < 17.99f)
                throw new InvalidOperationException("The cannery needs its full 18 metre site.");
            CityDistrictPointOfInterestAccessDescriptor best = descriptor.Accesses[0];
            HashSet<RoadEdge> busRoads = layout.BlueprintId == CityBlueprintCatalog.DefaultBlueprintId
                ? CityBusPlanner.ServiceRoadEdges(CityBusPlanner.CreateRoadRouting(layout))
                : new HashSet<RoadEdge>();
            float minimumRise = float.PositiveInfinity;
            foreach (CityDistrictPointOfInterestAccessDescriptor access in descriptor.Accesses)
            {
                RoadEdge edge = access.FrontageEdge;
                minimumRise = Mathf.Min(minimumRise, Mathf.Abs(
                    layout.ElevationPlan.GetNodeElevation(edge.A) -
                    layout.ElevationPlan.GetNodeElevation(edge.B)));
            }
            float cost = float.PositiveInfinity;
            foreach (CityDistrictPointOfInterestAccessDescriptor access in descriptor.Accesses)
            {
                RoadEdge edge = access.FrontageEdge;
                float level = layout.ElevationPlan.SampleRoadDatum(edge, .5f);
                float slope = Mathf.Abs(layout.ElevationPlan.GetNodeElevation(edge.A) -
                    layout.ElevationPlan.GetNodeElevation(edge.B));
                // The level yard must meet a level road. Bus separation only
                // breaks ties between equally flat entrances; shared streets
                // already have the delivery-trip traffic reservation.
                if (slope > minimumRise + .01f) continue;
                float candidate = slope * 10f + Mathf.Abs(level - descriptor.Center.y) +
                    (busRoads.Contains(edge) ? 10000f : 0f);
                if (candidate >= cost) continue;
                cost = candidate;
                best = access;
            }
            FrontageEdge = best.FrontageEdge;
            Rotation = Quaternion.LookRotation(new Vector3(best.StreetSideDirection.x, 0f,
                best.StreetSideDirection.y));
            Origin = new Vector3(descriptor.Center.x,
                layout.ElevationPlan.SampleRoadDatum(FrontageEdge, .5f), descriptor.Center.z);
            PublicRectangles = new[] {
                World(Rect.MinMaxRect(-9,-9,9,-7)), World(Rect.MinMaxRect(-9,7,9,9)),
                World(Rect.MinMaxRect(-9,-9,-8,9)), World(Rect.MinMaxRect(0,-9,9,9)),
                World(Rect.MinMaxRect(-1.95f,-9,-.25f,9)),
                // The authored east wall has two floor-level openings.
                // Bridge its excluded strip to the public aisle and yard;
                // each rectangle must overlap both by a full body diameter
                // because RoadWalkableArea insets rectangles separately.
                World(Rect.MinMaxRect(-1.95f,-6.5f,1f,-4.5f)),
                World(Rect.MinMaxRect(-1.95f,3.9f,1f,6.1f)),
                // The west staff opening reaches the clear work aisle even
                // while finished stock occupies the north wall. Physical
                // equipment and wall colliders still bound this narrow strip.
                World(Rect.MinMaxRect(-9f,1.95f,-6.4f,3.35f)),
                World(Rect.MinMaxRect(-7.85f,-2.4f,-6.4f,5.65f))
            };
        }

        public static CityCanneryPlan Create(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (Plans.TryGetValue(layout, out CityCanneryPlan existing)) return existing;
            foreach (CityDistrictPointOfInterestDescriptor point in layout.DistrictPointsOfInterest)
            {
                if (point.Kind != CityDistrictPointOfInterestKind.IndustrialCannery) continue;
                if (point.PublicBounds.width < 17.99f || point.PublicBounds.height < 17.99f) return null;
                var plan = new CityCanneryPlan(layout, point);
                Plans.Add(layout, plan);
                return plan;
            }
            return null;
        }

        public Vector3 World(Vector3 local) => Origin + Rotation * local;
        public Vector3 Local(Vector3 world) => Quaternion.Inverse(Rotation) * (world - Origin);

        /// <summary>The authored one-metre apron meets the actual road profile
        /// along its outer edge, while its inner edge remains level with the yard.</summary>
        public float ApronTop(float localX, float localZ)
        {
            Vector3 outer = World(new Vector3(localX, 0f, 10f));
            if (!layout.ElevationPlan.TrySampleSurface(new Vector2(outer.x, outer.z),
                CitySurfaceRole.RoadTop, out float roadTop, out _))
                throw new InvalidOperationException($"Cannery driveway does not meet a road at {outer}.");
            return Mathf.Lerp(Origin.y + YardTop, roadTop, Mathf.InverseLerp(9f, 10f, localZ));
        }

        public bool TrySampleYardTop(Vector3 world, out float top)
        {
            Vector3 local = Local(world);
            if (local.x >= -9f && local.x <= 9f && local.z >= -9f && local.z <= 9f)
            {
                top = Origin.y + YardTop;
                return true;
            }
            if (local.x >= 1.5f && local.x <= 8f && local.z > 9f && local.z <= 10.001f)
            {
                top = ApronTop(local.x, local.z);
                return true;
            }
            top = 0f;
            return false;
        }
        public Rect World(Rect local)
        {
            Vector3 a = World(new Vector3(local.xMin, 0, local.yMin));
            Vector3 b = World(new Vector3(local.xMax, 0, local.yMax));
            return Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.z,b.z),Mathf.Max(a.x,b.x),Mathf.Max(a.z,b.z));
        }
        public Bounds World(Bounds local)
        {
            Rect rect = World(Rect.MinMaxRect(local.min.x,local.min.z,local.max.x,local.max.z));
            return new Bounds(World(local.center),new Vector3(rect.width,local.size.y,rect.height));
        }
    }
}
