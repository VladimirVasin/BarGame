using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A frame-local snapshot of road occupancy shared by every walker. Future
    /// poses follow the vehicles' own roads; current bodies remain occupied
    /// until their tails pass, including while a driver waits for a walker.
    /// </summary>
    public sealed class CityPedestrianVehicleTraffic
    {
        public const float ClearancePadding = CityBusActor.ObstacleLateralPadding;
        private const float PathStep = .75f;
        private const float MinimumLookAhead = 12f;
        private const float MaximumLookAhead = 24f;
        private const float TruckLookAheadSeconds = 6f;
        private const float TruckSampleSeconds = .25f;

        private readonly CityBusDirector buses;
        private readonly CityCanneryController cannery;
        private readonly LastRouteCarDriver car;
        private readonly Bounds carBounds;
        private readonly List<Footprint> footprints = new List<Footprint>(160);

        public CityPedestrianVehicleTraffic(CityBusDirector buses = null,
            CityCanneryController cannery = null, LastRouteCarDriver car = null)
        {
            this.buses = buses;
            this.cannery = cannery;
            this.car = car;
            // The driving car deliberately disables this collider. Cache its
            // shape once rather than treating that as an absent vehicle.
            BoxCollider collider = car != null ? car.GetComponent<BoxCollider>() : null;
            carBounds = collider != null ? new Bounds(collider.center, collider.size) : default;
        }

        public void Refresh(IReadOnlyList<CityPedestrianActor> walkers = null)
        {
            ClearFootprints();
            Bounds neighbourhood = default;
            bool anyWalker = false;
            if (walkers != null)
                for (int i = 0; i < walkers.Count; i++)
                {
                    var walker = walkers[i];
                    if (!walker.IsSpawned || walker.IsAttachedToVehicle) continue;
                    if (!anyWalker) neighbourhood = new Bounds(walker.Position, Vector3.zero);
                    else neighbourhood.Encapsulate(walker.Position);
                    if (walker.IsYieldingToVehicle) neighbourhood.Encapsulate(walker.VehicleReturnPosition);
                    anyWalker = true;
                }
            // Include the entire bus path, hull, a walker's capsule and the five-metre
            // refuge search. No distant bus can intersect a query outside this envelope.
            const float walkerMargin = 8f;
            for (int i = 0; buses != null && buses.isActiveAndEnabled && i < buses.VehicleCount; i++)
            {
                CityBusDirector line = buses.GetVehicle(i);
                CityBusActor bus = line != null && line.isActiveAndEnabled ? line.Actor : null;
                if (bus == null || !bus.isActiveAndEnabled || !bus.HasRoutePhase || bus.BodyCollider == null) continue;
                float range = MaximumLookAhead + bus.LocalVisualBounds.center.magnitude + bus.LocalVisualBounds.extents.magnitude + walkerMargin;
                if (walkers != null && (!anyWalker || neighbourhood.SqrDistance(bus.Position) > range * range)) continue;
                Bounds bounds = new Bounds(bus.BodyCollider.center, bus.BodyCollider.size);
                AddFootprint(bus.Position, bus.Rotation, bounds);
                if (bus.MotionState == CityBusMotionState.Dwelling ||
                    bus.MotionState == CityBusMotionState.RouteEnded) continue;
                float reach = Mathf.Clamp(bus.GetRequiredStoppingDistance() + bus.Speed + 2f,
                    MinimumLookAhead, MaximumLookAhead);
                for (float ahead = PathStep; ahead < reach + PathStep; ahead += PathStep)
                {
                    bus.SampleAhead(Mathf.Min(ahead, reach), out Vector3 point, out Vector3 forward);
                    AddFootprint(point, Rotation(forward, bus.Rotation), bounds, predicted: true);
                }
            }

            if (cannery != null && cannery.isActiveAndEnabled && cannery.IsInitialized &&
                cannery.HasSpawnedTruck && cannery.Truck != null)
            {
                Bounds bounds = new Bounds(CityCanneryTruckDimensions.BodyCenter,
                    CityCanneryTruckDimensions.BodySize);
                AddFootprint(cannery.Truck.position, cannery.Truck.rotation, bounds);
                if (cannery.Snapshot.IsDriving)
                {
                    int trip = CityCanneryTraffic.TripIndex(cannery.Snapshot.Stage);
                    for (float ahead = TruckSampleSeconds;
                         ahead <= TruckLookAheadSeconds; ahead += TruckSampleSeconds)
                    {
                        CityFishSupplySnapshot future = cannery.Cycle.Sample(cannery.WorkingSeconds + ahead);
                        if (!future.IsDriving || CityCanneryTraffic.TripIndex(future.Stage) != trip) break;
                        CityPortTruckPose pose = cannery.TruckPose(future);
                        // The route supplies the body's rotation even in reverse.
                        AddFootprint(pose.RearAxle, pose.Rotation, bounds, predicted: true);
                    }
                }
            }

            if (car == null || !car.isActiveAndEnabled || carBounds.size.sqrMagnitude <= 0f) return;
            AddFootprint(car.transform.position, car.transform.rotation, carBounds);
            if (!car.IsDriving || car.Model == null || car.HasArrived) return;
            LastRouteCarDriveModel model = car.Model;
            float stopping = car.Speed * car.Speed / (2f * model.Profile.Braking);
            float carReach = Mathf.Min(model.Path.Length - car.Distance,
                Mathf.Clamp(stopping + car.Speed + 2f, MinimumLookAhead, MaximumLookAhead));
            for (float ahead = PathStep; ahead < carReach + PathStep && carReach > 0f; ahead += PathStep)
            {
                model.Path.Sample(car.Distance + Mathf.Min(ahead, carReach),
                    out Vector3 point, out Vector3 forward);
                AddFootprint(point, Rotation(forward, car.transform.rotation), carBounds, predicted: true);
            }
        }

        public bool Blocks(Vector3 point, float radius)
        {
            float clearance = Mathf.Max(0f, radius) + ClearancePadding;
            for (int i = 0; i < footprints.Count; i++)
                if (footprints[i].Blocks(point, clearance)) return true;
            return false;
        }

        internal bool PhysicallyBlocks(Vector3 point, float radius)
        {
            float clearance = Mathf.Max(0f, radius) + ClearancePadding;
            foreach (var body in footprints)
                if (!body.Predicted && body.Blocks(point, clearance)) return true;
            return false;
        }

        internal bool BlocksCrossing(Vector3 from, Vector3 to, float radius, float walkerSpeed)
        {
            float clearance = Mathf.Max(0f, radius) + ClearancePadding;
            foreach (var body in footprints)
                if (body.IntersectsSegment(from, to, clearance)) return true;

            // The daily walker needs several seconds to clear both lanes. Only
            // an actual decision at a kerb needs this longer prediction; ordinary
            // street avoidance continues to use the shorter frame snapshot.
            float length = Vector3.Distance(from, to);
            float seconds = Mathf.Min(20f, (length + clearance) / Mathf.Max(.35f, walkerSpeed) + .5f);
            Vector3 middle = (from + to) * .5f;
            const float spacing = 1.5f;
            for (int i = 0; buses != null && buses.isActiveAndEnabled && i < buses.VehicleCount; i++)
            {
                var bus = buses.GetVehicle(i).Actor;
                if (bus == null || !bus.HasRoutePhase || bus.MotionState == CityBusMotionState.Dwelling ||
                    bus.MotionState == CityBusMotionState.RouteEnded) continue;
                float reach = Mathf.Min(CityBusActor.CruiseSpeed * seconds,
                    bus.Speed * seconds + .5f * CityBusActor.Acceleration * seconds * seconds);
                Bounds bounds = bus.LocalVisualBounds;
                float broad = reach + length * .5f + bounds.center.magnitude + bounds.extents.magnitude + clearance;
                if ((bus.Position - middle).sqrMagnitude > broad * broad) continue;
                for (float d = MinimumLookAhead; d < reach + spacing; d += spacing)
                {
                    bus.SampleAhead(Mathf.Min(d, reach), out var point, out var direction);
                    if (new Footprint(point, Rotation(direction, bus.Rotation), bounds).IntersectsSegment(from, to, clearance))
                        return true;
                }
            }
            if (car != null && car.isActiveAndEnabled && car.IsDriving && car.Model != null && !car.HasArrived)
            {
                var model = car.Model;
                float reach = Mathf.Min(model.Path.Length - car.Distance, model.Profile.CruiseSpeed * seconds);
                float broad = reach + length + carBounds.extents.magnitude + clearance;
                if ((car.transform.position - middle).sqrMagnitude <= broad * broad)
                    for (float d = MinimumLookAhead; d < reach + spacing; d += spacing)
                    {
                        model.Path.Sample(car.Distance + Mathf.Min(d, reach), out var point, out var direction);
                        if (new Footprint(point, Rotation(direction, car.transform.rotation), carBounds).IntersectsSegment(from, to, clearance))
                            return true;
                    }
            }
            if (cannery != null && cannery.isActiveAndEnabled && cannery.IsInitialized &&
                cannery.HasSpawnedTruck && cannery.Snapshot.IsDriving)
            {
                int trip = CityCanneryTraffic.TripIndex(cannery.Snapshot.Stage);
                var bounds = new Bounds(CityCanneryTruckDimensions.BodyCenter, CityCanneryTruckDimensions.BodySize);
                for (float t = TruckLookAheadSeconds; t <= seconds; t += TruckSampleSeconds)
                {
                    var future = cannery.Cycle.Sample(cannery.WorkingSeconds + t);
                    if (!future.IsDriving || CityCanneryTraffic.TripIndex(future.Stage) != trip) break;
                    var pose = cannery.TruckPose(future);
                    if (new Footprint(pose.RearAxle, pose.Rotation, bounds).IntersectsSegment(from, to, clearance)) return true;
                }
            }
            return false;
        }

        public bool BlocksEscapeSegment(Vector3 from, Vector3 to, float radius)
        {
            float clearance = Mathf.Max(0f, radius) + ClearancePadding;
            for (int i = 0; i < footprints.Count; i++)
            {
                Footprint footprint = footprints[i];
                // A walker must be able to leave the corridor currently
                // enclosing them, while another lane remains impassable.
                if (!footprint.Blocks(from, clearance) &&
                    footprint.IntersectsSegment(from, to, clearance)) return true;
            }
            return false;
        }

        public Vector3 EscapeDirection(Vector3 point)
        {
            float nearest = float.PositiveInfinity;
            Vector3 direction = Vector3.right;
            for (int i = 0; i < footprints.Count; i++)
            {
                Footprint footprint = footprints[i];
                if (!footprint.OverlapsHeight(point)) continue;
                Vector3 local = footprint.InverseRotation * (point - footprint.Position);
                Vector3 relative = local - footprint.Bounds.center;
                float dx = Mathf.Max(0f, Mathf.Abs(relative.x) - footprint.Bounds.extents.x);
                float dz = Mathf.Max(0f, Mathf.Abs(relative.z) - footprint.Bounds.extents.z);
                // The small centre term resolves overlapping samples to the
                // nearby piece of a bend, rather than its first distant pose.
                float distance = dx * dx + dz * dz + relative.sqrMagnitude * .0001f;
                if (distance >= nearest) continue;
                nearest = distance;
                direction = footprint.Rotation * (relative.x < 0f ? Vector3.left : Vector3.right);
            }
            direction.y = 0f;
            return direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.right;
        }

        internal void ClearFootprints() => footprints.Clear();

        internal void AddFootprint(Vector3 position, Quaternion rotation, Bounds bounds, bool predicted = false) =>
            footprints.Add(new Footprint(position, rotation, bounds, predicted));

        private static Quaternion Rotation(Vector3 forward, Quaternion fallback) =>
            forward.sqrMagnitude > .0001f ? Quaternion.LookRotation(forward, Vector3.up) : fallback;

        private readonly struct Footprint
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Quaternion InverseRotation;
            public readonly Bounds Bounds;
            public readonly bool Predicted;
            private readonly float minimumY;
            private readonly float maximumY;
            private readonly float reach;

            public Footprint(Vector3 position, Quaternion rotation, Bounds bounds, bool predicted = false)
            {
                Predicted = predicted;
                Position = position;
                Rotation = rotation;
                InverseRotation = Quaternion.Inverse(rotation);
                Bounds = bounds;
                Vector3 center = position + rotation * bounds.center;
                Vector3 half = bounds.extents;
                float vertical = Mathf.Abs((rotation * Vector3.right).y) * half.x +
                    Mathf.Abs((rotation * Vector3.up).y) * half.y +
                    Mathf.Abs((rotation * Vector3.forward).y) * half.z;
                minimumY = center.y - vertical;
                maximumY = center.y + vertical;
                reach = bounds.center.magnitude + half.magnitude;
            }

            public bool OverlapsHeight(Vector3 point) =>
                point.y <= maximumY && point.y + CityPedestrianActor.CollisionHeight >= minimumY;

            public bool Blocks(Vector3 point, float clearance)
            {
                if (!OverlapsHeight(point)) return false;
                Vector3 offset = point - Position;
                float expandedReach = reach + clearance;
                if (offset.x * offset.x + offset.z * offset.z > expandedReach * expandedReach) return false;
                Vector3 local = InverseRotation * offset;
                float dx = Mathf.Max(0f, Mathf.Abs(local.x - Bounds.center.x) - Bounds.extents.x);
                float dz = Mathf.Max(0f, Mathf.Abs(local.z - Bounds.center.z) - Bounds.extents.z);
                return dx * dx + dz * dz <= clearance * clearance;
            }

            public bool IntersectsSegment(Vector3 from, Vector3 to, float clearance)
            {
                Vector3 localFrom = InverseRotation * (from - Position);
                Vector3 localTo = InverseRotation * (to - Position);
                Vector3 delta = localTo - localFrom;
                Vector3 minimum = Bounds.min;
                Vector3 maximum = Bounds.max;
                float enter = 0f, exit = 1f;
                if (!Clip(localFrom.x, delta.x, minimum.x - clearance, maximum.x + clearance, ref enter, ref exit) ||
                    !Clip(localFrom.z, delta.z, minimum.z - clearance, maximum.z + clearance, ref enter, ref exit)) return false;
                float fromY = Mathf.Lerp(from.y, to.y, enter);
                float toY = Mathf.Lerp(from.y, to.y, exit);
                return Mathf.Min(fromY, toY) <= maximumY &&
                    Mathf.Max(fromY, toY) + CityPedestrianActor.CollisionHeight >= minimumY;
            }

            private static bool Clip(float origin, float delta, float minimum, float maximum,
                ref float enter, ref float exit)
            {
                if (Mathf.Abs(delta) < .00001f) return origin >= minimum && origin <= maximum;
                float first = (minimum - origin) / delta;
                float last = (maximum - origin) / delta;
                if (first > last) { float swap = first; first = last; last = swap; }
                enter = Mathf.Max(enter, first);
                exit = Mathf.Min(exit, last);
                return enter <= exit;
            }
        }
    }
}
