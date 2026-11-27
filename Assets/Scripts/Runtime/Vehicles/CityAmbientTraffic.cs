using System;
using UnityEngine;

namespace BarPromenade
{
    // Shared with the midibus: the same warm, wide dipped beam on city streets.
    internal static class CityRoadHeadlightProfile
    {
        internal const float Intensity = 180f;
        internal const float Range = 24f;
        internal const float OuterAngle = 72f;
        internal const float InnerAngle = 42f;
        internal const float DownwardAim = .21f;
        internal static readonly Color Color = new Color(1f, .955f, .865f);
    }

    /// <summary>Circulating cars and occasional local pass-bys on surveyed city lanes.
    /// All active cars decide from one snapshot before any of them moves.</summary>
    [DefaultExecutionOrder(260)]
    public sealed class CityAmbientTraffic : MonoBehaviour
    {
        private const float SampleSpacing = 1.25f;
        private const int SampleCapacity = 34;
        private const float CarPredictionReach = 40f;
        private const float DippedIntensity = CityRoadHeadlightProfile.Intensity;
        private const int VehicleCapacity = CityBusDirector.MaximumActiveModels + AmbientTrafficRules.Capacity + 2;
        internal const float MinimumPassByDelay = 35f;
        internal const float MaximumPassByDelay = 55f;
        private const float PassByRetryDelay = 6f;
        private const int PassByProbeLimit = 8;
        private readonly Car[] cars = new Car[AmbientTrafficRules.Capacity];
        private readonly Vehicle[] vehicles = new Vehicle[VehicleCapacity];
        private readonly float[] holds = new float[AmbientTrafficRules.Capacity];
        private CityAmbientCarPlan[] routes;
        private CityGameRoot city;
        private LastRouteCarDriver ferryman;
        private CharacterController hero;
        private Transform heroTransform;
        private Car passingCar;
        private float passByCountdown;
        private uint passBySequence;
        private Bounds ferrymanBounds;
        private int buildIndex, vehicleCount;


        public int Count { get; private set; }
        public bool AutoAdvance { get; set; } = true;
        public Car GetCar(int index) => cars[index];
        private bool HasActivePassBy => Count > 0 && cars[Count - 1] == passingCar;

        public sealed class Car
        {
            public CityAmbientCarPlan Plan { get; internal set; }
            public readonly LastRouteCarDriver Driver;
            public readonly LastRouteCarAssetRegistry Registry;
            public readonly LastRouteFerrymanPresentation Person;
            public readonly Bounds Bounds;
            internal readonly Light[] Beams;
            internal readonly LastRouteCarAudio Audio;
            internal readonly LastRouteCarDriveProfile Profile;
            internal readonly CityRoadObstacleProbe ObstacleProbe = new CityRoadObstacleProbe();
            internal readonly CityRoadObstacleProbe.SamplePath SampleAhead;
            internal readonly CityRoadVehicleFeedback Feedback;
            internal bool HasRealBlocker, WaitingInTraffic;
            internal float ClearTime, PreviousHold = float.PositiveInfinity;
            internal int SnapshotIndex;
            internal float VehicleClearance = float.PositiveInfinity;
            public Vector3 Position => Driver.transform.position;
            public Collider PhysicalBlocker { get; internal set; }
            public CityTrafficHull Hull => new CityTrafficHull(Position, Driver.transform.rotation, Bounds, .06f);

            internal Car(CityAmbientCarPlan plan, LastRouteCarDriver driver, LastRouteCarAssetRegistry registry,
                LastRouteFerrymanPresentation person, Light[] beams, LastRouteCarDriveProfile profile, float repeatSeconds)
            {
                Plan = plan; Driver = driver; Registry = registry; Person = person; Beams = beams;
                Profile = profile;
                SampleAhead = SamplePathAhead;
                Feedback = driver.gameObject.AddComponent<CityRoadVehicleFeedback>();
                Feedback.Initialize(false, repeatSeconds, Vector3.up * .65f + Vector3.forward * 1.5f);
                Audio = driver.GetComponent<LastRouteCarAudio>();
                var body = driver.GetComponent<BoxCollider>();
                Bounds = new Bounds(body.center, body.size);
            }

            private void SamplePathAhead(float metres, out Vector3 position, out Vector3 forward) =>
                Plan.Path.Sample(Driver.Distance + metres, out position, out forward);

        }

        private sealed class Vehicle
        {
            public readonly CityTrafficHull[] Samples = new CityTrafficHull[SampleCapacity];
            public int Count, Priority;
            public float Spacing;
            public bool Queued;
        }

        internal static CityAmbientTraffic Create(CityGameRoot city, LastRouteCarDriver ferryman)
        {
            var root = new GameObject("City Ambient Traffic");
            root.transform.SetParent(city.transform, false);
            var traffic = root.AddComponent<CityAmbientTraffic>();
            traffic.city = city;
            traffic.heroTransform = city.Player.GameObject.transform;
            traffic.hero = traffic.heroTransform.GetComponent<CharacterController>();
            traffic.BindFerryman(ferryman);
            var routes = new System.Collections.Generic.List<CityAmbientCarPlan>(city.BusPlans.Count);
            var paving = CityStreetSupportPlan.ForLayout(city.Layout);
            foreach (var route in city.BusPlans)
                try { routes.Add(new CityAmbientCarPlan(route, paving)); }
                catch (ArgumentException failure) { GameLog.Warning("city", "ambient_route_unavailable", GameLog.Field("reason", failure.Message)); }
            traffic.routes = routes.ToArray();
            for (int i = 0; i < traffic.vehicles.Length; i++) traffic.vehicles[i] = new Vehicle();
            traffic.DelayNextPassBy();
            city.Cannery?.Traffic?.BindAmbient(traffic);
            return traffic;
        }

        internal void BindFerryman(LastRouteCarDriver driver)
        {
            if (ferryman != null) ferryman.SafetyHold = null;
            ferryman = driver;
            if (ferryman == null) return;
            var body = ferryman.GetComponent<BoxCollider>();
            ferrymanBounds = new Bounds(body.center, body.size);
            ferryman.SafetyHold = FerrymanHold;
        }

        // Composition yields between cars, including the default NPC allocation/import.
        internal bool BuildNext()
        {
            if (buildIndex >= cars.Length || routes.Length == 0) return false;
            int id = buildIndex++;
            CityAmbientCarPlan route = routes[id % routes.Length];
            if (id == AmbientTrafficRules.CirculatingCapacity)
            {
                passingCar = BuildCar(route, route.Length, id);
                if (passingCar != null) passingCar.Driver.gameObject.SetActive(false);
                return true;
            }

            float initial = route.Length * Mathf.Repeat(.17f + id * .271f + (city.Layout.Seed & 255) / 1024f, 1f);
            float selected = -1f;
            var nominal = new Bounds(Vector3.up * .8f, new Vector3(1.8f, 1.6f, 4.9f));
            ReadVehicles();
            for (int attempt = 0; attempt < 128; attempt++)
            {
                float distance = route.Length + Mathf.Repeat(initial + attempt * route.Length / 128f, route.Length);
                if (!route.CanWaitOutsideJunction(distance)) continue;
                route.Path.Sample(distance, out Vector3 position, out Vector3 direction);
                if ((position - heroTransform.position).sqrMagnitude < 72f * 72f) continue;
                var hull = new CityTrafficHull(position, Quaternion.LookRotation(direction), nominal, 3f);
                bool clear = true;
                for (int i = 0; i < vehicleCount && clear; i++) clear = !hull.Overlaps(vehicles[i].Samples[0]);
                if (clear) { selected = distance; break; }
            }
            if (selected < 0f) return true; // No safe entry means fewer cars, never a forced spawn.
            Car car = BuildCar(route, selected, id);
            if (car != null)
            {
                car.Driver.Begin(route.Path, car.Profile, 0f, selected);
                cars[Count++] = car;
            }
            return true;
        }

        private Car BuildCar(CityAmbientCarPlan route, float distance, int id)
        {
            route.Path.Sample(distance, out var start, out var forward);
            var registry = LastRouteCarFactory.Create(transform, LastRouteCarPlan.At(start, forward),
                default, null, LastRouteCarLamps.CityHalos, ambient: true);
            if (registry == null) return null;
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
            float cruise = id == AmbientTrafficRules.CirculatingCapacity ? 6.6f : 5.4f + id * .6f;
            var profile = new LastRouteCarDriveProfile(cruise, 1.45f, 3.2f, 2.65f, 2.2f, obstacleDeceleration: 2.35f);
            root.GetComponent<LastRouteCarAudio>().Bind(null, seated, null, null, LastRouteCarRoadSurface.WetAsphalt);
            return new Car(route, driver, registry, seated, CreateBeams(root, registry), profile, 4f + id % 3);
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
                    Quaternion.LookRotation(root.forward - root.up * CityRoadHeadlightProfile.DownwardAim, root.up));
                var beam = holder.AddComponent<Light>();
                beam.type = LightType.Spot;
                beam.range = CityRoadHeadlightProfile.Range;
                beam.spotAngle = CityRoadHeadlightProfile.OuterAngle;
                beam.innerSpotAngle = CityRoadHeadlightProfile.InnerAngle;
                beam.color = CityRoadHeadlightProfile.Color;
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
            float simulatedTime = Mathf.Min(deltaTime, .25f);
            Vector3 eye = city.Camera.transform.position;
            float remaining = simulatedTime;
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
                    float beforeSpeed = car.Driver.Speed;
                    float beforeDistance = car.Driver.Distance;
                    car.Driver.Advance(step);
                    var model = car.Driver.Model;
                    car.ObstacleProbe.Travel(car.Driver.Distance - beforeDistance);
                    bool braking = !float.IsPositiveInfinity(hold) && beforeSpeed > car.Driver.Speed &&
                        (model.IsObstacleBraking || car.Driver.Distance >= hold - .02f);
                    car.WaitingInTraffic = !float.IsPositiveInfinity(hold) &&
                        car.Driver.Speed < .08f && hold - car.Driver.Distance <= .45f;
                    bool audible = (car.Position - eye).sqrMagnitude <
                        CityRoadVehicleFeedback.AudibleDistance * CityRoadVehicleFeedback.AudibleDistance;
                    car.Feedback.Advance(step, beforeSpeed, (beforeSpeed - car.Driver.Speed) / step,
                        braking, car.HasRealBlocker && (braking || car.WaitingInTraffic), audible);
                    car.Person.SetHornReach(car.Driver, car.Feedback.HornReach);
                    if (car.Driver.Distance >= 2f * car.Plan.Length)
                    {
                        car.Driver.Model.ReleaseHold();
                        car.Driver.Model.Resume(car.Driver.Speed, car.Driver.Distance - car.Plan.Length);
                        car.PreviousHold = float.PositiveInfinity;
                        car.ObstacleProbe.Reset();
                    }
                }
            }
            AdvancePassBy(simulatedTime);
            UpdatePresentation(deltaTime);
        }

        private uint NextPassByHash() => CityPedestrianStableHash.Combine(unchecked((uint)city.Layout.Seed), ++passBySequence);

        private void DelayNextPassBy() => passByCountdown = Mathf.Lerp(MinimumPassByDelay, MaximumPassByDelay,
            (NextPassByHash() & 1023u) / 1023f);

        private void AdvancePassBy(float step)
        {
            if (passingCar == null) return;
            Vector3 player = heroTransform.position;
            if (HasActivePassBy)
            {
                const float radius = CityAmbientCarPlan.PassByReleaseRadius;
                if ((passingCar.Position - player).sqrMagnitude > radius * radius &&
                    CanChangePassByAt(passingCar.Position, passingCar.Driver.transform.rotation))
                    ReleasePassBy();
                return;
            }

            passByCountdown -= step;
            if (passByCountdown <= 0f && !TryBeginPassBy(player)) passByCountdown = PassByRetryDelay;
        }

        private bool TryBeginPassBy(Vector3 player)
        {
            ReadVehicles();
            uint hash = NextPassByHash();
            int startRoute = (int)(hash % (uint)routes.Length);
            int probes = 0;
            for (int r = 0; r < routes.Length; r++)
            {
                CityAmbientCarPlan route = routes[(startRoute + r) % routes.Length];
                int count = route.PassBySampleCount;
                int start = (int)(CityPedestrianStableHash.Combine(hash, (uint)r) % (uint)count);
                for (int i = 0; i < count; i++)
                {
                    if (!route.TryGetPassByEntry((start + i) % count, player, out float distance)) continue;
                    route.Path.Sample(distance, out var position, out var forward);
                    var rotation = Quaternion.LookRotation(forward);
                    if (!CanChangePassByAt(position, rotation)) continue;
                    if (++probes > PassByProbeLimit) return false;
                    if (!PassByLaneIsClear(position, rotation)) continue;

                    passingCar.Plan = route;
                    passingCar.ClearTime = 0f;
                    passingCar.PreviousHold = float.PositiveInfinity;
                    passingCar.PhysicalBlocker = null;
                    passingCar.WaitingInTraffic = false;
                    passingCar.HasRealBlocker = false;
                    passingCar.ObstacleProbe.Reset();
                    passingCar.VehicleClearance = float.PositiveInfinity;
                    passingCar.Driver.Begin(route.Path, passingCar.Profile, 4f, distance);
                    passingCar.Driver.gameObject.SetActive(true);
                    cars[Count++] = passingCar;
                    return true;
                }
            }
            return false;
        }

        private bool PassByLaneIsClear(Vector3 position, Quaternion rotation)
        {
            var hull = new CityTrafficHull(position, rotation, passingCar.Bounds, .75f);
            for (int i = 0; i < vehicleCount; i++)
                for (int j = 0; j < vehicles[i].Count; j++)
                    if (hull.Overlaps(vehicles[i].Samples[j])) return false;
            return passingCar.ObstacleProbe.Overlap(passingCar.Driver.transform, passingCar.Bounds, position, rotation, .6f) == null;
        }

        private bool CanChangePassByAt(Vector3 position, Quaternion rotation)
        {
            Bounds visual = passingCar.Registry.LocalBounds;
            float radius = visual.extents.magnitude + .5f;
            Vector3 centre = position + rotation * visual.center;
            // A car outside the shot can still be heard from the camera.
            float silentDistance = LastRouteCarAudioMix.EngineMaximumDistance + radius;
            return (centre - city.Camera.transform.position).sqrMagnitude >= silentDistance * silentDistance &&
                city.Visibility.CanChangeAmbientBodyAt(new Bounds(centre, Vector3.one * (2f * radius)));
        }

        private void ReleasePassBy()
        {
            cars[--Count] = null;
            passingCar.Driver.Halt();
            passingCar.Feedback.ResetPlayback();
            passingCar.Person.SetHornReach(passingCar.Driver, 0f);
            foreach (Light beam in passingCar.Beams) { beam.intensity = 0f; beam.enabled = false; }
            passingCar.Driver.gameObject.SetActive(false);
            DelayNextPassBy();
        }

        private Vehicle Next(int priority, CityTrafficHull first)
        {
            Vehicle v = vehicles[vehicleCount++];
            v.Count = 1; v.Priority = priority; v.Spacing = SampleSpacing; v.Samples[0] = first; v.Queued = false;
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
                v.Queued = bus.IsHeldByObstacle || bus.CurrentStopIndex >= 0;
                if (bus.MotionState == CityBusMotionState.Dwelling || bus.MotionState == CityBusMotionState.RouteEnded ||
                    bus.IsHeldByVehicle) continue;
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
                var vehicle = Next(8 + i, car.Hull);
                vehicle.Queued = car.WaitingInTraffic;
                ReadPath(vehicle, car.Driver, car.Bounds);
            }
        }

        private float ResolveHold(Car car)
        {
            Vehicle own = vehicles[car.SnapshotIndex];
            float conflict = float.PositiveInfinity;
            float actual = float.PositiveInfinity;
            bool queued = false;
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
                for (int a = 0; a < own.Count && a * SampleSpacing <= Mathf.Min(actual, conflict + SampleSpacing); a++)
                {
                    float along = a * SampleSpacing;
                    if (along < actual && own.Samples[a].Overlaps(other.Samples[0]))
                    {
                        actual = along;
                        queued = other.Queued;
                    }
                    if (!forecast || along >= conflict || along >= actual) continue;
                    for (int b = 1; b < other.Count; b++)
                        if (own.Samples[a].Overlaps(other.Samples[b]))
                        {
                            conflict = along;
                            break;
                        }
                }
            }

            conflict = Mathf.Min(conflict, actual);
            const float gap = AmbientTrafficRules.FollowingGap + SampleSpacing;
            float distance = car.Driver.Distance;
            car.VehicleClearance = car.Plan.KeepJunctionExitClear(distance,
                AmbientTrafficRules.StopDistance(distance, conflict, gap)) - distance;

            AddWalker(heroTransform.position, hero != null ? hero.radius : .35f,
                hero != null ? hero.velocity : Vector3.zero);
            var people = city.Pedestrians.Actors;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (p.IsSpawned && !p.IsAttachedToVehicle)
                    AddWalker(p.Position, p.AgentRadius, p.TrafficVelocity);
            }
            var ambient = city.Pedestrians.AmbientStreet;
            if (ambient != null)
                for (int i = 0; i < CityAmbientPedestrians.MaximumActive; i++)
                    if (ambient.TryGetWalker(i, out var p, out _, out _))
                        AddWalker(p, CityAmbientWalking.Radius, Vector3.zero);

            float reach = Mathf.Max(4f, CityRoadBraking.LookAhead(car.Driver.Speed,
                car.Profile.ObstacleDeceleration) + 2f);
            var hit = car.ObstacleProbe.Find(car.SampleAhead, car.Driver.transform, car.Bounds,
                reach, out float physical, checkAllNear: true);
            car.PhysicalBlocker = hit;
            if (hit != null && !CityRoadObstacleProbe.IsTrackedRoadUser(hit) && physical < actual)
            {
                actual = physical;
                queued = false;
            }
            float hold = car.Plan.KeepJunctionExitClear(distance,
                AmbientTrafficRules.StopDistance(distance, Mathf.Min(physical, conflict), gap));
            car.HasRealBlocker = !queued && !float.IsPositiveInfinity(actual) && actual <= Mathf.Min(conflict, physical) + SampleSpacing;
            return hold;

            void AddWalker(Vector3 feet, float radius, Vector3 velocity)
            {
                float here = WalkerClearance(own, feet, radius, actual);
                if (here < actual)
                {
                    actual = here;
                    queued = false;
                }
                conflict = Mathf.Min(conflict, here);
                if (velocity.sqrMagnitude > .001f)
                    conflict = Mathf.Min(conflict, WalkerClearance(own, feet + velocity * .8f, radius, conflict));
            }
        }

        private static bool IntersectsPrediction(CityTrafficHull hull, Vehicle road)
        {
            for (int i = 1; i < road.Count; i++) if (hull.Overlaps(road.Samples[i])) return true;
            return false;
        }

        private static float WalkerClearance(Vehicle road, Vector3 feet, float radius, float limit)
        {
            if ((feet - road.Samples[0].Position).sqrMagnitude > 48f * 48f) return float.PositiveInfinity;
            for (int i = 0; i < road.Count && i * SampleSpacing < limit; i++)
                if (road.Samples[i].ContainsCapsule(feet, radius + .12f)) return i * SampleSpacing;
            return float.PositiveInfinity;
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
                var carHull = new CityTrafficHull(car.Position, car.Driver.transform.rotation, car.Bounds, .18f);
                if (bus.TryGetBodyClearance(carHull, Mathf.Min(lookAhead, obstacle.ForwardClearance), out float clearance) &&
                    clearance < obstacle.ForwardClearance)
                    obstacle = new CityBusObstacleState(true, clearance, CityBusBlockerKind.AmbientCar,
                        carHull.Centre, "ambient_sedan", i, queued: car.WaitingInTraffic);
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
            if (gameObject.activeInHierarchy && HasActivePassBy) ReleasePassBy();
            for (int i = 0; i < Count; i++)
            {
                var car = cars[i];
                car.Feedback.ResetPlayback();
                car.Person.SetHornReach(car.Driver, 0f);
                car.ObstacleProbe.Reset();
                foreach (Light beam in car.Beams) { beam.intensity = 0f; beam.enabled = false; }
            }
        }

        private void OnDestroy()
        {
            if (ferryman != null) ferryman.SafetyHold = null;
        }
    }
}
