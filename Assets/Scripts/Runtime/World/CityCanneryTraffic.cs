using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Reserve only the approaching manoeuvre. Opposing street lanes
    /// remain usable; a bus already entering a crossing gets through first.</summary>
    public sealed class CityCanneryTraffic
    {
        public const float ReservationSeconds = 6f;
        private const int BodyIntervals = 13;
        private static readonly float BodyRadius = Mathf.Sqrt(
            CityCanneryTruckDimensions.HalfWidth * CityCanneryTruckDimensions.HalfWidth + .25f * .25f);
        private const float SweepPadding = .035f;
        private readonly CityCanneryController owner;
        private readonly CityBusDirector director;
        private readonly List<Vector3> candidate = new List<Vector3>(400);
        private readonly List<Vector3> reserved = new List<Vector3>(400);
        private Bounds reservedBounds;
        private int reservedTrip = -1;
        public bool HasReservation => reservedTrip >= 0;
        public int ReservedTrip => reservedTrip;

        public CityCanneryTraffic(CityCanneryController owner, CityBusDirector director)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.director = director;
        }

        public bool TryAcquire(CityFishSupplySnapshot snapshot)
        {
            if (!owner.HasSpawnedTruck) { Release(); return true; }
            int trip = TripIndex(snapshot.Stage);
            if (trip < 0) { Release(); return true; }
            CreateSweep(snapshot, candidate);
            Bounds sweepBounds = PointBounds(candidate);
            for (int i = 0; director != null && i < director.VehicleCount; i++)
            {
                CityBusActor bus = director.GetVehicle(i).Actor;
                if (bus == null || !bus.HasRoutePhase) continue;
                float reach = bus.LocalVisualBounds.size.magnitude + bus.GetRequiredStoppingDistance() + BodyRadius + 1f;
                if (candidate.Count == 0 || sweepBounds.SqrDistance(bus.Position) > reach * reach) continue;
                // The bus owns space it already occupies. A moving bus also
                // keeps its emergency stopping corridor. A stopped bus can
                // wait at the boundary while the truck clears the crossing.
                bool occupied = Overlaps(candidate, BodyRadius + SweepPadding,
                    bus.Position, bus.Rotation, bus.LocalVisualBounds);
                var obstacle = CityBusObstacleState.Clear;
                if (!occupied && bus.Speed > .05f)
                    Accumulate(bus, candidate, BodyRadius + SweepPadding,
                        bus.GetRequiredStoppingDistance() + .75f, ref obstacle);
                if (occupied || obstacle.MustStop)
                {
                    // Let the priority bus cross our future path. It still
                    // sees the actual truck through AccumulateObstacle.
                    Release();
                    return false;
                }
            }
            reserved.Clear();
            reserved.AddRange(candidate);
            reservedBounds = sweepBounds;
            reservedTrip = trip;
            return true;
        }

        public void Release() { reservedTrip = -1; reserved.Clear(); }

        public static int TripIndex(CityFishSupplyStage stage)
        {
            switch (stage)
            {
                case CityFishSupplyStage.PortToFactory:
                case CityFishSupplyStage.FactoryReverse: return 0;
                case CityFishSupplyStage.FactoryToShop: return 1;
                case CityFishSupplyStage.FactoryToPort:
                case CityFishSupplyStage.PortArrive:
                case CityFishSupplyStage.PortReverse: return 2;
                case CityFishSupplyStage.ShopToFactory:
                case CityFishSupplyStage.FactoryReturnReverse: return 3;
                default: return -1;
            }
        }

        public bool CanReserveAt(CityFishSupplySnapshot snapshot, Vector3 busPosition,
            Quaternion busRotation, Bounds busBounds)
        {
            CreateSweep(snapshot, candidate);
            return !Overlaps(candidate, BodyRadius + SweepPadding, busPosition, busRotation, busBounds);
        }

        public bool BlocksSpawn(Vector3 position, Quaternion rotation, Bounds bounds)
        {
            if (owner == null || !owner.isActiveAndEnabled || owner.Truck == null || !owner.HasSpawnedTruck) return false;
            for (int i = 0; i <= BodyIntervals; i++)
                if (Overlaps(BodyPoint(owner.Truck.position, owner.Truck.rotation, i), BodyRadius,
                    position, rotation, bounds)) return true;
            return Overlaps(reserved, BodyRadius + SweepPadding, position, rotation, bounds);
        }

        public void AccumulateObstacle(CityBusActor bus, float lookAhead, ref float clearance)
        {
            var obstacle = CityBusObstacleState.Clear;
            AccumulateObstacle(bus, lookAhead, ref obstacle);
            if (obstacle.MustStop) clearance = Mathf.Min(clearance, obstacle.ForwardClearance);
        }

        internal void AccumulateObstacle(CityBusActor bus, float lookAhead, ref CityBusObstacleState obstacle)
        {
            if (owner == null || !owner.isActiveAndEnabled || owner.Truck == null || !owner.HasSpawnedTruck) return;
            float reach = lookAhead + bus.LocalVisualBounds.size.magnitude + BodyRadius + SweepPadding;
            Vector3 axle = owner.Truck.position;
            float bodyReach = reach + Mathf.Max(Mathf.Abs(CityCanneryTruckDimensions.Rear), Mathf.Abs(CityCanneryTruckDimensions.Front)) + 1.4f;
            if ((axle - bus.Position).sqrMagnitude <= bodyReach * bodyReach)
            {
                Quaternion rotation = owner.Truck.rotation;
                for (int i = 0; i <= BodyIntervals; i++)
                    Accumulate(bus, BodyPoint(axle, rotation, i), BodyRadius, lookAhead,
                        CityBusBlockerKind.CanneryTruck, ref obstacle);
            }
            if (reserved.Count != 0 && reservedBounds.SqrDistance(bus.Position) <= reach * reach)
                Accumulate(bus, reserved, BodyRadius + SweepPadding, lookAhead, ref obstacle);
        }

        private static Bounds PointBounds(List<Vector3> points)
        {
            if (points.Count == 0) return default;
            var bounds = new Bounds(points[0], Vector3.zero);
            for (int i = 1; i < points.Count; i++) bounds.Encapsulate(points[i]);
            return bounds;
        }

        private void CreateSweep(CityFishSupplySnapshot snapshot, List<Vector3> points)
        {
            points.Clear();
            if (!snapshot.IsDriving) return;
            double at = owner.Cycle.StageStart(snapshot.Stage, snapshot.Batch) + snapshot.Seconds;
            // Quarter-second samples cover .75 m of straight travel; on the
            // tightest 4.5 m arc their chord error stays inside SweepPadding.
            for (int step = 0; step <= ReservationSeconds * 4; step++)
            {
                CityFishSupplySnapshot future = owner.Cycle.Sample(at + step * .25d);
                if (!future.IsDriving || TripIndex(future.Stage) != TripIndex(snapshot.Stage)) break;
                CityPortTruckPose pose = owner.TruckPose(future);
                for (int body = 0; body <= BodyIntervals; body++)
                    points.Add(BodyPoint(pose.RearAxle, pose.Rotation, body));
            }
        }

        // Close-spaced circles enclose every corner without inflating the
        // lateral footprint into the opposing lane. End caps deliberately
        // leave extra following distance for the closed box.
        private static Vector3 BodyPoint(Vector3 axle, Quaternion rotation, int index) =>
            axle + rotation * new Vector3(0, 1.4f, Mathf.Lerp(
                CityCanneryTruckDimensions.Rear, CityCanneryTruckDimensions.Front, index / (float)BodyIntervals));

        private static bool Overlaps(List<Vector3> points, float radius, Vector3 position, Quaternion rotation, Bounds bounds)
        {
            foreach (Vector3 point in points)
                if (Overlaps(point, radius, position, rotation, bounds)) return true;
            return false;
        }

        private static bool Overlaps(Vector3 point, float radius, Vector3 position, Quaternion rotation, Bounds bounds)
        {
            Vector3 centre = position + rotation * bounds.center;
            if (Mathf.Abs(point.y - centre.y) > bounds.extents.y + radius) return false;
            float reach = bounds.extents.magnitude + radius + .1f;
            if ((point - centre).sqrMagnitude > reach * reach) return false;
            return CityBusActor.GetClosestPlanarBodyDistance(point, position, rotation, bounds) < radius + .1f;
        }

        private static void Accumulate(CityBusActor bus, List<Vector3> points, float radius,
            float lookAhead, ref CityBusObstacleState obstacle)
        {
            foreach (Vector3 point in points)
                Accumulate(bus, point, radius, lookAhead, CityBusBlockerKind.CanneryReservation, ref obstacle);
        }

        private static void Accumulate(CityBusActor bus, Vector3 point, float radius,
            float lookAhead, CityBusBlockerKind kind, ref CityBusObstacleState obstacle)
        {
            float reach = lookAhead + bus.LocalVisualBounds.size.magnitude + radius;
            if ((point - bus.Position).sqrMagnitude > reach * reach) return;
            if (bus.TryGetPathObstacleClearance(point, radius, lookAhead, out float candidate) &&
                candidate < obstacle.ForwardClearance)
                obstacle = new CityBusObstacleState(true, candidate, kind, point, "cannery_truck");
        }
    }
}
