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
            IList<StreetLampDescriptor> nearbyStreetLamps,
            IList<TrafficSignalDescriptor> nearbyTrafficSignals)
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
            NearbyStreetLamps =
                new ReadOnlyCollection<StreetLampDescriptor>(
                    new List<StreetLampDescriptor>(
                        nearbyStreetLamps));
            NearbyTrafficSignals =
                new ReadOnlyCollection<TrafficSignalDescriptor>(
                    new List<TrafficSignalDescriptor>(
                        nearbyTrafficSignals));
        }

        public CityLayout Layout { get; }
        public BuildingLot PlayerHome { get; }
        public RoadEdge FrontageEdge { get; }
        public IReadOnlyList<RoadEdge> NearbyRoads { get; }
        public IReadOnlyList<BuildingLot> NearbyLots { get; }
        public IReadOnlyList<StreetLampDescriptor> NearbyStreetLamps
        {
            get;
        }
        public IReadOnlyList<TrafficSignalDescriptor> NearbyTrafficSignals
        {
            get;
        }
    }

    public static class HomeExteriorContextPlanner
    {
        public const float ViewRadius = 48f;

        public static HomeExteriorContextPlan Generate(int citySeed)
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            CityLayout layout =
                CityLayoutGenerator.Generate(
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

            return new HomeExteriorContextPlan(
                layout,
                home,
                frontage,
                roads,
                lots,
                lamps,
                signals);
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
}
