using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class MountainRoadTerminalPlanner
    {
        public const float VehicleTurningRadius = 7.5f;

        /// <summary>
        /// How far up the pad the vehicle apron's centre sits, in the
        /// plateau's own frame - and therefore where the car stops, since the
        /// parked pose is the apron centre exactly.
        ///
        /// Named because a second thing now measures itself from it: the
        /// apron floodlight stands in front of this point and aims back at it
        /// (<c>MountainRoadTerminalSitePlanner.CreateApronFloodlight</c>). A
        /// bare `1.5f` in two files is how a lamp ends up pointing at bare
        /// asphalt after somebody nudges the apron.
        /// </summary>
        public const float ApronForwardOffset = 1.5f;
        public const float CafeHeight = 4.4f;
        public const float CafeDoorWidth = 1.6f;
        /// <summary>
        /// The visible line runs on past the scene's draw range - the
        /// mountain road draws `120 m`, the village `140 m` - so that from
        /// the platform and from the seat the rope simply dissolves into the
        /// haze and the far turn is clipped before it is drawn. The ride's
        /// cut lands at `RideCutDistance`, mid-span, with better than a far
        /// plane of rope still ahead of it.
        /// </summary>
        public const float CablewayLineLength = 230f;

        public const float CablewayTrackSeparation = 2.9f;
        public const float CablewayCabinSpeed = 2.05f;

        /// <summary>Eight on a `470 m` loop: a descending cabin passes the
        /// passenger about every half minute, which is what keeps a line
        /// this long from reading as a rope to nowhere.</summary>
        public const int CablewayCabinCount = 8;

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
                LocalToWorld(plateau, 0f, 0f, ApronForwardOffset),
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
                    "map.mountain_road.cableway"),
                new MountainRoadTerminalLandmark(
                    "terminal-landmark-brink",
                    MountainRoadTerminalLandmarkKind.Brink,
                    LocalToWorld(plateau, 3f, 0f, 15.3f),
                    "map.mountain_road.brink")
            };
            return new MountainRoadTerminalPlan(
                vehicle,
                cafe,
                cableway,
                landmarks,
                MountainRoadTerminalSitePlanner.Create(plateau, cafe));
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
            // The first four nodes are the climb out of the terminal as it
            // has always been; past them the rope goes on up the mountainside
            // at about the ground's own grade (`0.154` per metre along the
            // line, measured), so every tower stands `14-19 m` tall and the
            // cabin keeps `14 m` or more of air, until the turn at `230` -
            // nearly two far planes out from the cut at `73`.
            float[] distances =
            {
                0f, 18f, 37f, 44f, 62f, 84f, 110f, 138f, 168f, 200f, 230f
            };
            float[] rises =
            {
                0f, 14.3f, 19.3f, 21.2f, 24.0f, 27.0f, 30.3f, 33.6f, 37.0f,
                41.0f, 44.5f
            };
            var heights = new float[rises.Length];
            for (int index = 0; index < rises.Length; index++)
            {
                heights[index] = lower.y + rises[index];
            }
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

            var cabins = new List<MountainCablewayCabinDescriptor>(
                CablewayCabinCount);
            for (int index = 0; index < CablewayCabinCount; index++)
            {
                cabins.Add(new MountainCablewayCabinDescriptor(
                    $"cableway-cabin-{index:00}",
                    index / (float)CablewayCabinCount));
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
                cabins);
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
