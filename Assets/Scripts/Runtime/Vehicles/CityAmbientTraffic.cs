using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A small persistent fleet on surveyed city lanes. All cars decide from
    /// one snapshot before any of them moves. Rendering never owns road occupancy.</summary>
    [DefaultExecutionOrder(260)]
    public sealed class CityAmbientTraffic : MonoBehaviour
    {
        private const float SampleSpacing = 1.25f;
        private const int SampleCapacity = 34;
        private const float CarPredictionReach = 40f;
        private const float DippedIntensity = 180f;
        private const int VehicleCapacity = CityBusDirector.MaximumActiveModels + AmbientTrafficRules.Capacity + 2;
        private readonly Car[] cars = new Car[AmbientTrafficRules.Capacity];
        private readonly Vehicle[] vehicles = new Vehicle[VehicleCapacity];
        private readonly float[] holds = new float[AmbientTrafficRules.Capacity];
        private CityAmbientCarPlan[] routes;
        private CityGameRoot city;
        private LastRouteCarDriver ferryman;
        private CharacterController hero;
        private Bounds ferrymanBounds;
        private int buildIndex, vehicleCount;
        private Collider[] obstacleScratch = new Collider[16];

        public int Count { get; private set; }
        public bool AutoAdvance { get; set; } = true;
        public Car GetCar(int index) => cars[index];

        public sealed class Car
        {
            public readonly CityAmbientCarPlan Plan;
            public readonly LastRouteCarDriver Driver;
            public readonly LastRouteCarAssetRegistry Registry;
            public readonly LastRouteFerrymanPresentation Person;
            public readonly Bounds Bounds;
            internal readonly Light[] Beams;
            internal readonly LastRouteCarAudio Audio;
            internal float ClearTime, PreviousHold = float.PositiveInfinity;
            internal int SnapshotIndex;
            public Vector3 Position => Driver.transform.position;
            public Collider PhysicalBlocker { get; internal set; }
            public CityTrafficHull Hull => new CityTrafficHull(Position, Driver.transform.rotation, Bounds, .06f);

            internal Car(CityAmbientCarPlan plan, LastRouteCarDriver driver, LastRouteCarAssetRegistry registry,
                LastRouteFerrymanPresentation person, Light[] beams)
            {
                Plan = plan; Driver = driver; Registry = registry; Person = person; Beams = beams;
                Audio = driver.GetComponent<LastRouteCarAudio>();
                var body = driver.GetComponent<BoxCollider>();
                Bounds = new Bounds(body.center, body.size);
            }
        }

        private sealed class Vehicle
        {
            public readonly CityTrafficHull[] Samples = new CityTrafficHull[SampleCapacity];
            public int Count, Priority;
            public float Spacing;
        }

        internal static CityAmbientTraffic Create(CityGameRoot city, LastRouteCarDriver ferryman)
        {
            var root = new GameObject("City Ambient Traffic");
            root.transform.SetParent(city.transform, false);
            var traffic = root.AddComponent<CityAmbientTraffic>();
            traffic.city = city;
            traffic.hero = city.Player.GameObject.GetComponent<CharacterController>();
            traffic.ferryman = ferryman;
            if (ferryman != null)
            {
                var body = ferryman.GetComponent<BoxCollider>();
                traffic.ferrymanBounds = new Bounds(body.center, body.size);
                ferryman.SafetyHold = traffic.FerrymanHold;
            }
            var routes = new System.Collections.Generic.List<CityAmbientCarPlan>(city.BusPlans.Count);
            var paving = CityStreetSupportPlan.ForLayout(city.Layout);
            foreach (var route in city.BusPlans)
                try { routes.Add(new CityAmbientCarPlan(route, paving)); }
                catch (ArgumentException failure) { GameLog.Warning("city", "ambient_route_unavailable", GameLog.Field("reason", failure.Message)); }
            traffic.routes = routes.ToArray();
            for (int i = 0; i < traffic.vehicles.Length; i++) traffic.vehicles[i] = new Vehicle();
            city.Cannery?.Traffic?.BindAmbient(traffic);
            return traffic;
        }

        // Composition yields between cars, including the default NPC allocation/import.
        internal bool BuildNext()
        {
            if (buildIndex >= cars.Length || routes.Length == 0) return false;
            int id = buildIndex++;
            CityAmbientCarPlan route = routes[id % routes.Length];
            float initial = route.Length * Mathf.Repeat(.17f + id * .271f + (city.Layout.Seed & 255) / 1024f, 1f);
            float selected = -1f;
            var nominal = new Bounds(Vector3.up * .8f, new Vector3(1.8f, 1.6f, 4.9f));
            ReadVehicles();
            for (int attempt = 0; attempt < 128; attempt++)
            {
                float distance = route.Length + Mathf.Repeat(initial + attempt * route.Length / 128f, route.Length);
                if (!route.CanWaitOutsideJunction(distance)) continue;
                route.Path.Sample(distance, out Vector3 position, out Vector3 direction);
                if ((position - city.Player.GameObject.transform.position).sqrMagnitude < 72f * 72f) continue;
                var hull = new CityTrafficHull(position, Quaternion.LookRotation(direction), nominal, 3f);
                bool clear = true;
                for (int i = 0; i < vehicleCount && clear; i++) clear = !hull.Overlaps(vehicles[i].Samples[0]);
                if (clear) { selected = distance; break; }
            }
            if (selected < 0f) return true; // No safe entry means fewer cars, never a forced spawn.
            route.Path.Sample(selected, out var start, out var forward);
            var registry = LastRouteCarFactory.Create(transform, LastRouteCarPlan.At(start, forward),
                default, null, LastRouteCarLamps.CityHalos, ambient: true);
            if (registry == null) return true;
            Transform root = registry.transform.parent;
            var driver = root.GetComponent<LastRouteCarDriver>();
            driver.ExternallyDriven = true;
            driver.KeepBodyCollider = true;
            var body = root.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var person = DefaultNpcFactory.CreateForCharacter(root, DefaultNpcPopulation.AmbientDriverId(id));
            person.ReleaseAnimation();
            NpcFootstepSources.Unregister(person.transform);
            var rig = person.gameObject.AddComponent<LastRouteFerrymanRigAnchors>();
            rig.Bind(person);
            var seated = person.gameObject.AddComponent<LastRouteFerrymanPresentation>();
            seated.Initialize(rig, LastRouteFerrymanPlan.Create(registry).Stance, registry);
            seated.BeginSeatedAtTheWheel();
            var profile = new LastRouteCarDriveProfile(5.4f + id * .6f, 1.45f, 3.2f, 1.7f, 2.2f);
            driver.Begin(route.Path, profile, 0f, selected);
            root.GetComponent<LastRouteCarAudio>().Bind(null, seated, null, null, LastRouteCarRoadSurface.WetAsphalt);
            cars[Count++] = new Car(route, driver, registry, seated, CreateBeams(root, registry));
            return true;
        }

        private static Light[] CreateBeams(Transform root, LastRouteCarAssetRegistry registry)
        {
            var beams = new Light[2];
            Transform[] lenses = { registry.DriverHeadlight, registry.PassengerHeadlight };
            for (int i = 0; i < beams.Length; i++)
            {
                var holder = new GameObject("Sedan dipped beam");
                holder.transform.SetParent(registry.Body, false);
                holder.transform.SetPositionAndRotation(lenses[i].position + root.forward * .12f,
                    Quaternion.LookRotation(root.forward - root.up * .21f, root.up));
                var beam = holder.AddComponent<Light>();
                beam.type = LightType.Spot;
                beam.range = 24f;
                beam.spotAngle = 72f;
                beam.innerSpotAngle = 42f;
                beam.color = new Color(1f, .955f, .865f);
                beam.renderMode = LightRenderMode.ForcePixel;
                beam.bounceIntensity = 0f;
                beam.intensity = 0f;
                beam.shadows = LightShadows.Soft;
                beam.shadowStrength = .7f;
                beam.shadowBias = .035f;
                beam.shadowNormalBias = .25f;
                beam.shadowNearPlane = .05f;
                beam.enabled = false;
                beams[i] = beam;
            }
            return beams;
        }

        private void Update()
        {
            if (AutoAdvance && city != null && city.IsInitialized && !city.IsDormant &&
                GameSessionState.IsGameTimeRunning && !GameTimeScaleRuntime.IsPaused)
                Advance(Time.deltaTime);
        }

        public void Advance(float deltaTime)
        {
            if (deltaTime <= 0f || !isActiveAndEnabled) return;
            // Bound a hitch, but consume it in small safety steps rather than tunnelling.
            float remaining = Mathf.Min(deltaTime, .25f);
            while (remaining > 0f)
            {
                float step = Mathf.Min(.05f, remaining);
                remaining -= step;
                ReadVehicles();
                for (int i = 0; i < Count; i++) holds[i] = ResolveHold(cars[i]);
                for (int i = 0; i < Count; i++)
                {
                    Car car = cars[i];
                    float hold = holds[i];
                    if (float.IsPositiveInfinity(hold))
                    {
                        car.ClearTime += step;
                        if (car.ClearTime < AmbientTrafficRules.ClearSeconds) hold = car.PreviousHold;
                    }
                    else car.ClearTime = 0f;
                    car.Driver.Model.SetHold(hold);
                    car.PreviousHold = hold;
                    car.Driver.Advance(step);
                    if (car.Driver.Distance >= 2f * car.Plan.Length)
                    {
                        car.Driver.Model.ReleaseHold();
                        car.Driver.Model.Resume(car.Driver.Speed, car.Driver.Distance - car.Plan.Length);
                        car.PreviousHold = float.PositiveInfinity;
                    }
                }
            }
            UpdatePresentation(deltaTime);
        }

        private Vehicle Next(int priority, CityTrafficHull first)
        {
            Vehicle v = vehicles[vehicleCount++];
            v.Count = 1; v.Priority = priority; v.Spacing = SampleSpacing; v.Samples[0] = first;
            return v;
        }

        private static void ReadPath(Vehicle v, LastRouteCarDriver driver, Bounds bounds)
        {
            if (driver == null || !driver.IsDriving || driver.Model == null || driver.HasArrived) return;
            // Wide junction links include both lane shifts. A speed-based 18 m
            // horizon loses the far exit when slowing, allowing opposing cars to
            // enter the same manoeuvre before either sees the conflict.
            for (float d = SampleSpacing; d <= CarPredictionReach && v.Count < SampleCapacity; d += SampleSpacing)
            {
                driver.Model.Path.Sample(driver.Distance + d, out var p, out var f);
                v.Samples[v.Count++] = new CityTrafficHull(p, Quaternion.LookRotation(f), bounds, .12f);
            }
        }

        private void ReadVehicles()
        {
            vehicleCount = 0;
            for (int i = 0; city.Bus != null && i < city.Bus.VehicleCount; i++)
            {
                var bus = city.Bus.GetVehicle(i).Actor;
                if (bus == null || !bus.HasRoutePhase) continue;
                var v = Next(i, new CityTrafficHull(bus.Position, bus.Rotation, bus.LocalVisualBounds, .12f));
                if (bus.MotionState == CityBusMotionState.Dwelling || bus.MotionState == CityBusMotionState.RouteEnded) continue;
                float reach = Mathf.Min(40f, Mathf.Max(18f, bus.Speed * 4f + bus.GetRequiredStoppingDistance()));
                // A bus queued behind a sedan cannot reserve the junction through
                // that sedan. Otherwise opposing queue leaders yield to each
                // other's following bus forever. Use the bus's own stop envelope;
                // its occupied body still participates even when no travel is free.
                var obstruction = CityBusObstacleState.Clear;
                AccumulateObstacle(bus, reach, ref obstruction, city.Bus.GetVehicle(i).Plan.RouteId);
                if (obstruction.MustStop)
                    reach = Mathf.Min(reach, Mathf.Max(0f,
                        obstruction.ForwardClearance - CityBusActor.ObstacleStopPadding));
                for (float d = SampleSpacing; d <= reach && v.Count < SampleCapacity; d += SampleSpacing)
                {
                    bus.SampleAhead(d, out var p, out var f);
                    v.Samples[v.Count++] = new CityTrafficHull(p, Quaternion.LookRotation(f), bus.LocalVisualBounds, .12f);
                }
            }
            var truck = city.Cannery;
            if (truck != null && truck.HasSpawnedTruck && truck.Truck != null)
            {
                var bounds = new Bounds(CityCanneryTruckDimensions.BodyCenter, CityCanneryTruckDimensions.BodySize);
                var v = Next(6, new CityTrafficHull(truck.Truck.position, truck.Truck.rotation, bounds, .12f));
                if (truck.Snapshot.IsDriving)
                    for (float t = .25f; t <= 6f; t += .25f)
                    {
                        var future = truck.Cycle.Sample(truck.WorkingSeconds + t);
                        if (!future.IsDriving || CityCanneryTraffic.TripIndex(future.Stage) != CityCanneryTraffic.TripIndex(truck.Snapshot.Stage)) break;
                        var p = truck.TruckPose(future);
                        v.Samples[v.Count++] = new CityTrafficHull(p.RearAxle, p.Rotation, bounds, .12f);
                    }
            }
            if (ferryman != null && ferryman.isActiveAndEnabled)
            {
                var v = Next(7, new CityTrafficHull(ferryman.transform.position, ferryman.transform.rotation, ferrymanBounds, .12f));
                ReadPath(v, ferryman, ferrymanBounds);
            }
            for (int i = 0; i < Count; i++)
            {
                Car car = cars[i];
                car.SnapshotIndex = vehicleCount;
                ReadPath(Next(8 + i, car.Hull), car.Driver, car.Bounds);
            }
        }

        private float ResolveHold(Car car)
        {
            Vehicle own = vehicles[car.SnapshotIndex];
            float conflict = float.PositiveInfinity;
            for (int i = 0; i < vehicleCount; i++)
            {
                if (i == car.SnapshotIndex) continue;
                Vehicle other = vehicles[i];
                if ((own.Samples[0].Centre - other.Samples[0].Centre).sqrMagnitude > 90f * 90f) continue;
                bool sameWay = Vector3.Dot(own.Samples[0].Rotation * Vector3.forward,
                    other.Samples[0].Rotation * Vector3.forward) > .85f;
                bool ownCommitted = IntersectsPrediction(own.Samples[0], other);
                bool otherCommitted = IntersectsPrediction(other.Samples[0], own);
                bool forecast = AmbientTrafficRules.YieldToPrediction(own.Priority, other.Priority,
                    ownCommitted, otherCommitted, sameWay);
                for (int a = 0; a < own.Count && a * SampleSpacing < conflict; a++)
                {
                    bool hit = own.Samples[a].Overlaps(other.Samples[0]);
                    if (!hit && forecast)
                        for (int b = 1; b < other.Count && !hit; b++) hit = own.Samples[a].Overlaps(other.Samples[b]);
                    if (hit) conflict = a * SampleSpacing;
                }
            }
            var player = city.Player.GameObject.transform;
            AccumulateWalker(own, player.position, hero != null ? hero.radius : .35f,
                hero != null ? hero.velocity : Vector3.zero, ref conflict);
            var people = city.Pedestrians.Actors;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (p.IsSpawned && !p.IsAttachedToVehicle)
                    AccumulateWalker(own, p.Position, p.AgentRadius, p.TravelDirection * p.MovementSpeed, ref conflict);
            }
            var ambient = city.Pedestrians.AmbientStreet;
            if (ambient != null)
                for (int i = 0; i < CityAmbientPedestrians.MaximumActive; i++)
                    if (ambient.TryGetWalker(i, out var p, out _, out _)) AccumulateWalker(own, p, .35f, Vector3.zero, ref conflict);
            float hold = AmbientTrafficRules.StopDistance(car.Driver.Distance, conflict,
                AmbientTrafficRules.FollowingGap + SampleSpacing);
            hold = car.Plan.KeepJunctionExitClear(car.Driver.Distance, hold);
            // Physics catches movable props/animals and late hero movement too. No speculative
            // empty buffer is accepted when the NonAlloc query saturates.
            if (ImmediateObstacle(car)) hold = car.Driver.Distance;
            return hold;
        }

        private static bool IntersectsPrediction(CityTrafficHull hull, Vehicle road)
        {
            for (int i = 1; i < road.Count; i++) if (hull.Overlaps(road.Samples[i])) return true;
            return false;
        }

        private static void AccumulateWalker(Vehicle road, Vector3 feet, float radius, Vector3 velocity, ref float conflict)
        {
            if ((feet - road.Samples[0].Position).sqrMagnitude > 48f * 48f) return;
            for (int i = 0; i < road.Count && i * SampleSpacing < conflict; i++)
                if (road.Samples[i].ContainsCapsule(feet, radius + .12f) ||
                    road.Samples[i].ContainsCapsule(feet + velocity * .8f, radius + .12f)) conflict = i * SampleSpacing;
        }

        private bool ImmediateObstacle(Car car)
        {
            car.Plan.Path.Sample(car.Driver.Distance + .6f, out var p, out var f);
            var rotation = Quaternion.LookRotation(f);
            // Wheels touch the road. A city-spanning sloped road MeshCollider can
            // have a high world AABB even here, so bounds.max.y cannot reject it.
            // Probe only the body above the wheel contact band, in the road's frame.
            float bottom = Mathf.Max(.4f, car.Bounds.min.y);
            float halfHeight = Mathf.Max(.1f, (car.Bounds.max.y - bottom) * .5f);
            Vector3 centre = p + rotation * new Vector3(car.Bounds.center.x, bottom + halfHeight, car.Bounds.center.z);
            Vector3 halfExtents = new Vector3(car.Bounds.extents.x + .04f, halfHeight, car.Bounds.extents.z + .04f);
            car.PhysicalBlocker = null;
            int count;
            while (true)
            {
                count = Physics.OverlapBoxNonAlloc(centre, halfExtents,
                    obstacleScratch, rotation, ~0, QueryTriggerInteraction.Ignore);
                if (count < obstacleScratch.Length || obstacleScratch.Length >= 128) break;
                Array.Resize(ref obstacleScratch, obstacleScratch.Length * 2);
            }
            Collider[] hits = count == obstacleScratch.Length
                ? Physics.OverlapBox(centre, halfExtents, rotation, ~0, QueryTriggerInteraction.Ignore)
                : obstacleScratch;
            int total = hits == obstacleScratch ? count : hits.Length;
            bool blocked = false;
            for (int i = 0; i < total; i++)
            {
                var hit = hits[i];
                if (hit != null && !hit.transform.IsChildOf(car.Driver.transform) && hit.bounds.max.y > p.y + .4f)
                { car.PhysicalBlocker = hit; blocked = true; break; }
            }
            Array.Clear(obstacleScratch, 0, count);
            return blocked;
        }

        internal bool BlocksSpawn(Vector3 position, Quaternion rotation, Bounds bounds)
        {
            if (!isActiveAndEnabled) return false;
            var candidate = new CityTrafficHull(position, rotation, bounds, .3f);
            for (int i = 0; i < Count; i++) if (cars[i].Hull.Overlaps(candidate)) return true;
            return false;
        }

        internal bool BlocksTruckSweep(System.Collections.Generic.List<Vector3> points, float radius)
        {
            if (!isActiveAndEnabled) return false;
            for (int i = 0; i < Count; i++)
            {
                var hull = cars[i].Hull;
                foreach (var point in points)
                    if (hull.ContainsCapsule(point - Vector3.up * radius, radius, radius * 2f)) return true;
            }
            return false;
        }

        internal void AccumulateObstacle(CityBusActor bus, float lookAhead, ref CityBusObstacleState obstacle,
            string followingRoute = null)
        {
            if (!isActiveAndEnabled) return;
            for (int i = 0; i < Count; i++)
            {
                Car car = cars[i];
                if ((car.Position - bus.Position).sqrMagnitude > 60f * 60f) continue;
                // Only a leader in this corridor limits a queue's prediction.
                // An opposing car still has to yield before entering a bus turn.
                if (followingRoute != null && car.Plan.RouteId != followingRoute &&
                    Vector3.Dot(car.Driver.transform.forward, bus.TravelDirection) < .85f) continue;
                // Probe full oriented bodies. The point/circle approximation
                // can miss a sedan beside the nose while the long bus turns.
                const float probeStep = .5f;
                float limit = Mathf.Min(lookAhead, obstacle.ForwardClearance);
                var carHull = car.Hull;
                for (float ahead = 0f; ahead <= limit; ahead += probeStep)
                {
                    bus.SampleAhead(ahead, out var p, out var f);
                    var hull = new CityTrafficHull(p, Quaternion.LookRotation(f), bus.LocalVisualBounds, .12f);
                    if (!hull.Overlaps(carHull)) continue;
                    float clearance = Mathf.Max(0f, ahead - probeStep);
                    obstacle = new CityBusObstacleState(true, clearance, CityBusBlockerKind.AmbientCar,
                        carHull.Centre, "ambient_sedan", i);
                    break;
                }
            }
        }

        private float FerrymanHold()
        {
            if (!isActiveAndEnabled || ferryman == null || ferryman.Model == null) return float.PositiveInfinity;
            float reach = Mathf.Max(18f, ferryman.Speed * ferryman.Speed / (2f * ferryman.Model.Profile.Braking) + 8f);
            for (float d = 0f; d <= reach; d += SampleSpacing)
            {
                ferryman.Model.Path.Sample(ferryman.Distance + d, out var p, out var f);
                if (BlocksSpawn(p, Quaternion.LookRotation(f), ferrymanBounds))
                    return AmbientTrafficRules.StopDistance(ferryman.Distance, d, AmbientTrafficRules.FollowingGap + SampleSpacing);
            }
            return float.PositiveInfinity;
        }

        private void UpdatePresentation(float step)
        {
            Vector3 eye = city.Camera.transform.position;
            int first = -1, second = -1;
            float a = float.PositiveInfinity, b = float.PositiveInfinity;
            for (int i = 0; i < Count; i++)
            {
                float distance = (cars[i].Position - eye).sqrMagnitude;
                if (distance < a) { second = first; b = a; first = i; a = distance; }
                else if (distance < b) { second = i; b = distance; }
            }
            int litPairs = 0;
            for (int i = 0; i < Count; i++) if (cars[i].Beams[0].intensity > .01f) litPairs++;
            for (int i = 0; i < Count; i++)
            {
                Car car = cars[i];
                float distance = Vector3.Distance(car.Position, eye);
                // Preserve authoritative movement and the visible car; only expensive cabin
                // animation/audio and the nearest two pairs of shadowed beams are gated.
                bool detail = distance < 64f;
                car.Person.enabled = detail;
                var voices = car.Audio.OwnedSources;
                for (int voice = 0; voice < voices.Count; voice++) voices[voice].mute = !detail;
                float target = (i == first || i == second) ? DippedIntensity * Mathf.Clamp01((38f - distance) / 10f) : 0f;
                if (target > 0f && car.Beams[0].intensity <= .01f)
                {
                    if (litPairs >= 2) target = 0f;
                    else litPairs++;
                }
                foreach (Light beam in car.Beams)
                {
                    beam.intensity = Mathf.MoveTowards(beam.intensity, target, step * DippedIntensity * 2f);
                    beam.enabled = beam.intensity > .01f;
                }
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < Count; i++)
                foreach (Light beam in cars[i].Beams) { beam.intensity = 0f; beam.enabled = false; }
        }

        private void OnDestroy()
        {
            if (ferryman != null) ferryman.SafetyHold = null;
        }
    }
}
