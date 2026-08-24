using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class MountainRoadTerminalPlanner
    {
        public const float VehicleTurningRadius = 7.5f;
        public const float CafeHeight = 4.4f;
        public const float CafeDoorWidth = 1.6f;
        public const float CablewayLineLength = 58f;
        public const float CablewayTrackSeparation = 2.9f;
        public const float CablewayCabinSpeed = 2.05f;
        public const string CablewayOccluderStableId =
            "far-snow-cableway-occluder";

        public static MountainRoadTerminalPlan Create(
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau)
        {
            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            if (plateau == null)
            {
                throw new ArgumentNullException(nameof(plateau));
            }

            MountainRoadRouteSample entry = route.Sample(
                plateau.EntryDistance);
            var vehicle = new MountainRoadVehicleApronPlan(
                LocalToWorld(plateau, 0f, 0f, 1.5f),
                entry.Position,
                plateau.Forward,
                MountainRoadPlanner.RoadWidth,
                VehicleTurningRadius);
            MountainRoadCafePlan cafe = CreateCafe(plateau);
            MountainRoadCablewayPlan cableway = CreateCableway(
                route,
                plateau);
            var landmarks = new List<MountainRoadTerminalLandmark>
            {
                new MountainRoadTerminalLandmark(
                    "terminal-landmark-cafe",
                    MountainRoadTerminalLandmarkKind.Cafe,
                    cafe.Center,
                    "map.mountain_road.cafe"),
                new MountainRoadTerminalLandmark(
                    "terminal-landmark-cableway",
                    MountainRoadTerminalLandmarkKind.Cableway,
                    cableway.StationArea.Center,
                    "map.mountain_road.cableway")
            };
            return new MountainRoadTerminalPlan(
                vehicle,
                cafe,
                cableway,
                landmarks);
        }

        private static MountainRoadCafePlan CreateCafe(
            MountainRoadPlateauDescriptor plateau)
        {
            Vector2[] localFootprint =
            {
                new Vector2(-18f, 2.3f),
                new Vector2(-11f, 2.3f),
                new Vector2(-8.2f, 5.1f),
                new Vector2(-8.2f, 12.3f),
                new Vector2(-18f, 12.3f)
            };
            var footprint = new List<Vector2>(localFootprint.Length);
            Vector3 average = Vector3.zero;
            for (int index = 0; index < localFootprint.Length; index++)
            {
                Vector3 world = LocalToWorld(
                    plateau,
                    localFootprint[index].x,
                    0f,
                    localFootprint[index].y);
                footprint.Add(new Vector2(world.x, world.z));
                average += world;
            }

            average /= localFootprint.Length;
            return new MountainRoadCafePlan(
                "terminal-cafe",
                average,
                plateau.Right,
                plateau.Forward,
                plateau.Center.y,
                CafeHeight,
                2.8f,
                LocalToWorld(plateau, -16.2f, 0f, 2.3f),
                CafeDoorWidth,
                footprint);
        }

        private static MountainRoadCablewayPlan CreateCableway(
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau)
        {
            Vector3 lineForward = (
                plateau.Right * 0.412f +
                plateau.Forward * 0.911f).normalized;
            Vector3 lineRight = (
                plateau.Right * 0.911f -
                plateau.Forward * 0.412f).normalized;
            Vector3 stationCenter = LocalToWorld(
                plateau,
                14f,
                0f,
                5.5f);
            var station = new MountainRoadTerminalRect(
                stationCenter,
                lineRight,
                lineForward,
                new Vector2(9f, 6.2f));
            Vector3 lower = LocalToWorld(
                plateau,
                15.86f,
                4f,
                9.60f);
            float[] distances = { 0f, 18f, 37f, 50f, 58f };
            float[] heights =
            {
                lower.y,
                27f,
                32f,
                35.5f,
                37.5f
            };
            var nodes = new List<MountainCablewayNodeDescriptor>(
                distances.Length);
            for (int index = 0; index < distances.Length; index++)
            {
                Vector3 cable = lower + lineForward * distances[index];
                cable.y = heights[index];
                Vector2 point = new Vector2(cable.x, cable.z);
                Vector3 ground = new Vector3(
                    cable.x,
                    MountainRoadTerrainSampler.SampleHeight(
                        route,
                        plateau,
                        point),
                    cable.z);
                MountainCablewayNodeKind kind = index == 0
                    ? MountainCablewayNodeKind.LowerStation
                    : index == distances.Length - 1
                        ? MountainCablewayNodeKind.UpperTurn
                        : MountainCablewayNodeKind.Support;
                nodes.Add(new MountainCablewayNodeDescriptor(
                    index == 0
                        ? "cableway-lower-station"
                        : index == distances.Length - 1
                            ? "cableway-upper-turn"
                            : $"cableway-support-{index:00}",
                    kind,
                    distances[index],
                    cable,
                    ground));
            }

            var cabins = new List<MountainCablewayCabinDescriptor>(4);
            for (int index = 0; index < 4; index++)
            {
                cabins.Add(new MountainCablewayCabinDescriptor(
                    $"cableway-cabin-{index:00}",
                    index / 4f));
            }

            return new MountainRoadCablewayPlan(
                "terminal-cableway",
                station,
                lineForward,
                lineRight,
                CablewayTrackSeparation,
                CablewayLineLength,
                CablewayCabinSpeed,
                new Vector3(1.75f, 2.05f, 1.55f),
                nodes,
                cabins,
                CablewayOccluderStableId);
        }

        internal static Vector3 LocalToWorld(
            MountainRoadPlateauDescriptor plateau,
            float right,
            float up,
            float forward)
        {
            return plateau.Center +
                   plateau.Right * right +
                   Vector3.up * up +
                   plateau.Forward * forward;
        }
    }
}
