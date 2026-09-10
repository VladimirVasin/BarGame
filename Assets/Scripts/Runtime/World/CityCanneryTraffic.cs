using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One delivery vehicle reserves a narrow-street trip before leaving
    /// its loading bay. The bus finishes clearing the trip before the reservation
    /// is granted, and then waits outside it until the truck parks again.</summary>
    public sealed class CityCanneryTraffic
    {
        private const float BodyRadius = 1.5f;
        private const float SweepRadius = 2.05f;
        private readonly CityCanneryController owner;
        private readonly CityBusDirector director;
        private readonly Vector3[][] trips = new Vector3[3][];
        private int reservedTrip = -1;
        private long reservedBatch = -1;
        public bool HasReservation => reservedTrip >= 0;
        public int ReservedTrip => reservedTrip;

        public CityCanneryTraffic(CityCanneryController owner, CityBusDirector director)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.director = director;
            trips[0] = CreateSweep(CityCanneryTruckLeg.PortToFactory, CityCanneryTruckLeg.FactoryReverse);
            trips[1] = CreateSweep(CityCanneryTruckLeg.FactoryToShop);
            trips[2] = CreateSweep(CityCanneryTruckLeg.ShopToPort, CityCanneryTruckLeg.PortArrive,
                CityCanneryTruckLeg.PortReverse);
        }

        public bool TryAcquire(CityFishSupplySnapshot snapshot)
        {
            int trip = TripIndex(snapshot.Stage);
            if (trip < 0) { Release(); return true; }
            if (reservedTrip == trip && reservedBatch == snapshot.Batch) return true;
            Release();
            CityBusActor bus = director != null ? director.Actor : null;
            if (bus != null && bus.IsSpawned)
            {
                if (!CanReserveTripAt(trip, bus.Position, bus.Rotation, bus.LocalVisualBounds)) return false;
                float braking = bus.GetRequiredStoppingDistance() + 2f;
                float clearance = float.PositiveInfinity;
                Accumulate(bus, trips[trip], SweepRadius, braking, ref clearance);
                if (!float.IsPositiveInfinity(clearance)) return false;
            }
            reservedTrip = trip;
            reservedBatch = snapshot.Batch;
            return true;
        }

        public void Release() { reservedTrip = -1; reservedBatch = -1; }

        public static int TripIndex(CityFishSupplyStage stage)
        {
            switch (stage)
            {
                case CityFishSupplyStage.PortToFactory:
                case CityFishSupplyStage.FactoryReverse: return 0;
                case CityFishSupplyStage.FactoryToShop: return 1;
                case CityFishSupplyStage.ShopToPort:
                case CityFishSupplyStage.PortArrive:
                case CityFishSupplyStage.PortReverse: return 2;
                default: return -1;
            }
        }

        /// <summary>Also used to prove that each trip leaves a real place on the
        /// bus loop where the bus can wait, rather than starving delivery forever.</summary>
        public bool CanReserveTripAt(int trip, Vector3 busPosition, Quaternion busRotation, Bounds busBounds)
        {
            if (trip < 0 || trip >= trips.Length) throw new ArgumentOutOfRangeException(nameof(trip));
            return !Overlaps(trips[trip], SweepRadius, busPosition, busRotation, busBounds);
        }

        public bool BlocksSpawn(Vector3 position, Quaternion rotation, Bounds bounds)
        {
            if (owner == null || !owner.isActiveAndEnabled || owner.Truck == null) return false;
            for (int i = 0; i < 5; i++)
                if (Overlaps(BodyPoint(owner.Truck.position, owner.Truck.rotation, i), BodyRadius,
                    position, rotation, bounds)) return true;
            return reservedTrip >= 0 && Overlaps(trips[reservedTrip], SweepRadius, position, rotation, bounds);
        }

        public void AccumulateObstacle(CityBusActor bus, float lookAhead, ref float clearance)
        {
            if (owner == null || !owner.isActiveAndEnabled || owner.Truck == null) return;
            for (int i = 0; i < 5; i++)
                Accumulate(bus, BodyPoint(owner.Truck.position, owner.Truck.rotation, i),
                    BodyRadius, lookAhead, ref clearance);
            if (reservedTrip >= 0) Accumulate(bus, trips[reservedTrip], SweepRadius, lookAhead, ref clearance);
        }

        private Vector3[] CreateSweep(params CityCanneryTruckLeg[] legs)
        {
            var points = new List<Vector3>();
            foreach (CityCanneryTruckLeg leg in legs)
            {
                int count = Mathf.CeilToInt(owner.Route.Length(leg) / .5f);
                for (int i = 0; i <= count; i++)
                {
                    CityPortTruckPose pose = owner.Route.Sample(leg, i / (float)count);
                    for (int body = 0; body < 5; body++)
                        points.Add(BodyPoint(pose.RearAxle, pose.Rotation, body));
                }
            }
            return points.ToArray();
        }

        // Five overlapping circles enclose the complete 2.5 x 8 m body, with
        // its centre 1.4 m ahead of the rear axle. Sweep inflation covers the
        // half-metre route sampling interval, including rotating overhangs.
        private static Vector3 BodyPoint(Vector3 axle, Quaternion rotation, int index) =>
            axle + rotation * new Vector3(0, 1.4f, -1.8f + index * 1.6f);

        private static bool Overlaps(Vector3[] points, float radius, Vector3 position, Quaternion rotation, Bounds bounds)
        {
            foreach (Vector3 point in points)
                if (Overlaps(point, radius, position, rotation, bounds)) return true;
            return false;
        }

        private static bool Overlaps(Vector3 point, float radius, Vector3 position, Quaternion rotation, Bounds bounds)
        {
            Vector3 centre = position + rotation * bounds.center;
            if (Mathf.Abs(point.y - centre.y) > bounds.extents.y + radius) return false;
            float reach = bounds.extents.magnitude + radius + CityBusActor.ObstacleStopPadding;
            if ((point - centre).sqrMagnitude > reach * reach) return false;
            return CityBusActor.GetClosestPlanarBodyDistance(point, position, rotation, bounds) <
                radius + CityBusActor.ObstacleStopPadding;
        }

        private static void Accumulate(CityBusActor bus, Vector3[] points, float radius,
            float lookAhead, ref float clearance)
        {
            foreach (Vector3 point in points) Accumulate(bus, point, radius, lookAhead, ref clearance);
        }

        private static void Accumulate(CityBusActor bus, Vector3 point, float radius,
            float lookAhead, ref float clearance)
        {
            float reach = lookAhead + bus.LocalVisualBounds.size.magnitude + radius;
            if ((point - bus.Position).sqrMagnitude > reach * reach) return;
            if (bus.TryGetPathObstacleClearance(point, radius, lookAhead, out float candidate))
                clearance = Mathf.Min(clearance, candidate);
        }
    }
}
