using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class HomeExteriorContextPlan
    {
        internal HomeExteriorContextPlan(
            CityLayout layout,
            BuildingLot playerHome,
            RoadEdge frontageEdge,
            IList<RoadEdge> nearbyRoads,
            IList<BuildingLot> nearbyLots,
            IList<CityDistrictPointOfInterestDescriptor>
                nearbyDistrictPointsOfInterest,
            IList<StreetLampDescriptor> nearbyStreetLamps,
            IList<TrafficSignalDescriptor> nearbyTrafficSignals,
            IList<CityDecorationDescriptor> nearbyDecorations)
        {
            Layout = layout ??
                throw new ArgumentNullException(nameof(layout));
            PlayerHome = playerHome ??
                throw new ArgumentNullException(nameof(playerHome));
            FrontageEdge = frontageEdge;
            NearbyRoads =
                new ReadOnlyCollection<RoadEdge>(
                    new List<RoadEdge>(nearbyRoads));
            NearbyLots =
                new ReadOnlyCollection<BuildingLot>(
                    new List<BuildingLot>(nearbyLots));
            NearbyDistrictPointsOfInterest =
                new ReadOnlyCollection<
                    CityDistrictPointOfInterestDescriptor>(
                    new List<
                        CityDistrictPointOfInterestDescriptor>(
                        nearbyDistrictPointsOfInterest));
            NearbyStreetLamps =
                new ReadOnlyCollection<StreetLampDescriptor>(
                    new List<StreetLampDescriptor>(
                        nearbyStreetLamps));
            NearbyTrafficSignals =
                new ReadOnlyCollection<TrafficSignalDescriptor>(
                    new List<TrafficSignalDescriptor>(
                        nearbyTrafficSignals));
            NearbyDecorations =
                new ReadOnlyCollection<CityDecorationDescriptor>(
                    new List<CityDecorationDescriptor>(
                        nearbyDecorations));
        }

        public CityLayout Layout { get; }
        public BuildingLot PlayerHome { get; }
        public RoadEdge FrontageEdge { get; }
        public IReadOnlyList<RoadEdge> NearbyRoads { get; }
        public IReadOnlyList<BuildingLot> NearbyLots { get; }
        public IReadOnlyList<CityDistrictPointOfInterestDescriptor>
            NearbyDistrictPointsOfInterest
        {
            get;
        }
        public IReadOnlyList<StreetLampDescriptor> NearbyStreetLamps
        {
            get;
        }
        public IReadOnlyList<TrafficSignalDescriptor> NearbyTrafficSignals
        {
            get;
        }
        public IReadOnlyList<CityDecorationDescriptor> NearbyDecorations
        {
            get;
        }
    }

    public static class HomeExteriorContextPlanner
    {
        public const float ViewRadius = 48f;

        public static HomeExteriorContextPlan Generate(int citySeed)
        {
            return Generate(
                CityBlueprintCatalog.Default,
                citySeed);
        }

        public static HomeExteriorContextPlan Generate(
            string cityBlueprintId,
            int citySeed)
        {
            return Generate(
                CityBlueprintCatalog.Resolve(cityBlueprintId),
                citySeed);
        }

        public static HomeExteriorContextPlan Generate(
            CityBlueprint blueprint,
            int citySeed)
        {
            if (blueprint == null)
            {
                throw new ArgumentNullException(nameof(blueprint));
            }

            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            CityLayout layout =
                CityLayoutGenerator.Generate(
                    blueprint,
                    settings,
                    citySeed);
            return Generate(layout);
        }

        public static HomeExteriorContextPlan Generate(
            CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            BuildingLot home = layout.PlayerHome;
            if (home == null ||
                !layout.TryGetFrontageEdge(
                    home,
                    out RoadEdge frontage))
            {
                throw new InvalidOperationException(
                    "A home exterior context requires a player home with street frontage.");
            }

            Vector2 anchor = new Vector2(
                home.DoorPosition.x,
                home.DoorPosition.z);
            var roads = new List<RoadEdge>();
            for (int index = 0;
                 index < layout.RoadEdges.Count;
                 index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                if (SquaredDistance(
                        layout.GetRoadRect(edge),
                        anchor) <=
                    ViewRadius * ViewRadius)
                {
                    roads.Add(edge);
                }
            }

            var lots = new List<BuildingLot>();
            for (int index = 0;
                 index < layout.BuildingLots.Count;
                 index++)
            {
                BuildingLot lot =
                    layout.BuildingLots[index];
                if (!lot.HasBuilding)
                {
                    continue;
                }

                Rect bounds = Rect.MinMaxRect(
                    lot.Center.x - lot.Size.x * 0.5f,
                    lot.Center.z - lot.Size.y * 0.5f,
                    lot.Center.x + lot.Size.x * 0.5f,
                    lot.Center.z + lot.Size.y * 0.5f);
                if (SquaredDistance(bounds, anchor) <=
                    ViewRadius * ViewRadius)
                {
                    lots.Add(lot);
                }
            }

            var districtPointsOfInterest =
                new List<
                    CityDistrictPointOfInterestDescriptor>();
            for (int index = 0;
                 index <
                 layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor descriptor =
                    layout.DistrictPointsOfInterest[index];
                if (SquaredDistance(
                        descriptor.PublicBounds,
                        anchor) <=
                    ViewRadius * ViewRadius)
                {
                    districtPointsOfInterest.Add(descriptor);
                }
            }

            CityNightFixturePlan night =
                CityNightFixturePlanner.CreatePlan(layout);
            var lamps = new List<StreetLampDescriptor>();
            for (int index = 0;
                 index < night.StreetLamps.Count;
                 index++)
            {
                StreetLampDescriptor lamp =
                    night.StreetLamps[index];
                if (PlanarSquaredDistance(
                        lamp.Position,
                        home.DoorPosition) <=
                    ViewRadius * ViewRadius)
                {
                    lamps.Add(lamp);
                }
            }

            var signals =
                new List<TrafficSignalDescriptor>();
            for (int index = 0;
                 index < night.TrafficSignals.Count;
                 index++)
            {
                TrafficSignalDescriptor signal =
                    night.TrafficSignals[index];
                if (PlanarSquaredDistance(
                        signal.Position,
                        home.DoorPosition) <=
                    ViewRadius * ViewRadius)
                {
                    signals.Add(signal);
                }
            }

            RoadFencePlan fence =
                RoadFencePlanner.CreatePlan(layout);
            CityDecorationPlan cityDecorations =
                CityDecorationPlanner.CreatePlan(
                    layout,
                    fence,
                    night);
            var decorations =
                new List<CityDecorationDescriptor>();
            for (int index = 0;
                 index < cityDecorations.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor decoration =
                    cityDecorations.Descriptors[index];
                if (PlanarSquaredDistance(
                        decoration.Position,
                        home.DoorPosition) <=
                    ViewRadius * ViewRadius)
                {
                    decorations.Add(decoration);
                }
            }

            return new HomeExteriorContextPlan(
                layout,
                home,
                frontage,
                roads,
                lots,
                districtPointsOfInterest,
                lamps,
                signals,
                decorations);
        }

        private static float SquaredDistance(
            Rect bounds,
            Vector2 point)
        {
            float x = Mathf.Clamp(
                point.x,
                bounds.xMin,
                bounds.xMax);
            float y = Mathf.Clamp(
                point.y,
                bounds.yMin,
                bounds.yMax);
            return (new Vector2(x, y) - point).sqrMagnitude;
        }

        private static float PlanarSquaredDistance(
            Vector3 first,
            Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return x * x + z * z;
        }
    }

    public static class HomeExteriorPedestrianPlanner
    {
        public static CityPedestrianPlan Create(
            HomeExteriorContextPlan context,
            int populationSeed)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            CityStreetSurfacePlan streetSurfaces =
                CityStreetSurfacePlanner.Create(context.Layout);
            CityPedestrianPlan cityPlan =
                CityPedestrianPlanner.Create(
                    context.Layout,
                    populationSeed,
                    streetSurfaces);
            var nodes = new List<CityPedestrianNode>(
                cityPlan.Nodes.Count);
            for (int index = 0; index < cityPlan.Nodes.Count; index++)
            {
                CityPedestrianNode source = cityPlan.Nodes[index];
                nodes.Add(new CityPedestrianNode(
                    source.Id,
                    PlayerHomeBalconyGeometry.ToHomeLocal(
                        context.PlayerHome,
                        source.Position),
                    source.IsCrosswalkEntry));
            }

            var links = new List<CityPedestrianLink>(
                cityPlan.Links.Count);
            for (int index = 0; index < cityPlan.Links.Count; index++)
            {
                CityPedestrianLink source = cityPlan.Links[index];
                links.Add(new CityPedestrianLink(
                    source.Id,
                    source.FirstNodeIndex,
                    source.SecondNodeIndex,
                    source.Kind));
            }

            var anchors = new List<CityPedestrianSpawnAnchor>();
            for (int index = 0;
                 index < cityPlan.SpawnAnchors.Count;
                 index++)
            {
                CityPedestrianSpawnAnchor source =
                    cityPlan.SpawnAnchors[index];
                Vector3 localPosition =
                    PlayerHomeBalconyGeometry.ToHomeLocal(
                        context.PlayerHome,
                        source.Position);
                if (!IsRenderedExteriorAnchor(
                        context,
                        source.Position,
                        localPosition,
                        cityPlan.AgentRadius))
                {
                    continue;
                }

                anchors.Add(new CityPedestrianSpawnAnchor(
                    source.Id,
                    localPosition,
                    source.FirstNodeIndex,
                    source.SecondNodeIndex));
            }

            var navigationRectangles = new List<Rect>(
                cityPlan.NavigationRectangles.Count);
            for (int index = 0;
                 index < cityPlan.NavigationRectangles.Count;
                 index++)
            {
                navigationRectangles.Add(
                    PlayerHomeBalconyGeometry.ToHomeLocalRect(
                        context.PlayerHome,
                        cityPlan.NavigationRectangles[index]));
            }

            return new CityPedestrianPlan(
                cityPlan.LayoutSeed,
                cityPlan.PopulationSeed,
                cityPlan.StableSeed,
                cityPlan.AgentRadius,
                nodes,
                links,
                anchors,
                navigationRectangles);
        }

        private static bool IsRenderedExteriorAnchor(
            HomeExteriorContextPlan context,
            Vector3 cityPosition,
            Vector3 localPosition,
            float radius)
        {
            if (localPosition.x - radius <
                HomeExteriorViewBuilder.ExteriorMinimumX)
            {
                return false;
            }

            float deltaX =
                cityPosition.x - context.PlayerHome.DoorPosition.x;
            float deltaZ =
                cityPosition.z - context.PlayerHome.DoorPosition.z;
            float viewRadius = HomeExteriorContextPlanner.ViewRadius;
            if ((deltaX * deltaX) + (deltaZ * deltaZ) >
                viewRadius * viewRadius)
            {
                return false;
            }

            for (int index = 0;
                 index < context.NearbyRoads.Count;
                 index++)
            {
                Rect road = context.Layout.GetRoadRect(
                    context.NearbyRoads[index]);
                if (cityPosition.x >= road.xMin &&
                    cityPosition.x <= road.xMax &&
                    cityPosition.z >= road.yMin &&
                    cityPosition.z <= road.yMax)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
