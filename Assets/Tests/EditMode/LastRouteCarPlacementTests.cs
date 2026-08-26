using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The car stands beside the last route island's paving, never on it and
    /// never across a way in. The walkable mask is built from rectangles and
    /// knows nothing about props, so it would never report a car parked
    /// across an approach - which is exactly why the placement rejects such a
    /// bay outright and reports the car absent rather than nudging it.
    /// </summary>
    public sealed class LastRouteCarPlacementTests
    {
        // The paving disc the recipe draws, and the lot it stands in.
        private const float PavingRadius = 5.40f;
        private const float CarLength = 4.83f;
        private const float CarWidth = 1.80f;

        // Where the man sits and where his boots rest, mirrored from
        // `PERCH_SEAT` and `PERCH_SOLES` in
        // `tools/build-last-route-car-3d-model.py`.
        private const float PerchLead = 2.02f;
        private const float PerchSolesLead = 2.44f;

        private static CityLayout GenerateLayout(int seed)
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                seed);
        }

        private static CityDistrictPointOfInterestDescriptor RequireIsland(
            CityLayout layout)
        {
            foreach (CityDistrictPointOfInterestDescriptor descriptor in
                     layout.DistrictPointsOfInterest)
            {
                if (descriptor.Kind ==
                    CityDistrictPointOfInterestKind.NightlifeLastRouteIsland)
                {
                    return descriptor;
                }
            }

            Assert.Fail("The generated city has no last route island.");
            return default;
        }

        [Test]
        public void Placement_ReportsNoneForEveryOtherKind()
        {
            CityLayout layout =
                GenerateLayout(GameSessionState.DefaultCitySeed);
            int checkedOthers = 0;
            foreach (CityDistrictPointOfInterestDescriptor descriptor in
                     layout.DistrictPointsOfInterest)
            {
                if (descriptor.Kind ==
                    CityDistrictPointOfInterestKind.NightlifeLastRouteIsland)
                {
                    continue;
                }

                checkedOthers++;
                Assert.That(
                    CityDistrictPointOfInterestWorldBuilder
                        .TryDescribeFerrymanCarStance(descriptor, out _),
                    Is.False,
                    $"{descriptor.Kind} must not carry the Ferryman's car.");
            }

            Assert.That(
                checkedOthers,
                Is.GreaterThan(0),
                "The city should plan more than one point of interest.");
        }

        [Test]
        public void Placement_StandsBesideThePavingAndInsideTheLot()
        {
            CityLayout layout =
                GenerateLayout(GameSessionState.DefaultCitySeed);
            CityDistrictPointOfInterestDescriptor island = RequireIsland(layout);

            Assert.That(
                CityDistrictPointOfInterestWorldBuilder
                    .TryDescribeFerrymanCarStance(
                        island,
                        out CityDryingYardNpcStance stance),
                Is.True,
                "The default city must be able to park the car.");

            float planarDistance = Vector2.Distance(
                new Vector2(stance.Position.x, stance.Position.z),
                new Vector2(island.Center.x, island.Center.z));
            Assert.That(
                planarDistance,
                Is.GreaterThan(PavingRadius + CarWidth * 0.5f),
                "The car must stand clear of the paved circle; the island's " +
                "empty centre is authored, not incidental.");

            foreach (Vector3 corner in Corners(stance))
            {
                Assert.That(
                    island.PublicBounds.Contains(
                        new Vector2(corner.x, corner.z)),
                    Is.True,
                    $"Corner {corner} left the island's lot.");
            }
        }

        [Test]
        public void Placement_NeverBlocksAWayIn()
        {
            // Every seed that parks a car must leave every approach clear;
            // a seed that cannot must report no car at all.
            int parked = 0;
            for (int seed = 1; seed <= 24; seed++)
            {
                CityLayout layout = GenerateLayout(seed);
                CityDistrictPointOfInterestDescriptor island =
                    RequireIsland(layout);
                if (!CityDistrictPointOfInterestWorldBuilder
                        .TryDescribeFerrymanCarStance(
                            island,
                            out CityDryingYardNpcStance stance))
                {
                    continue;
                }

                parked++;
                foreach (CityDistrictPointOfInterestAccessDescriptor access in
                         island.Accesses)
                {
                    foreach (Vector3 corner in Corners(stance))
                    {
                        Assert.That(
                            access.ApproachBounds.Contains(
                                new Vector2(corner.x, corner.z)),
                            Is.False,
                            $"Seed {seed} parked the car across an approach.");
                    }
                }
            }

            Assert.That(
                parked,
                Is.GreaterThan(0),
                "No seed managed to park the car; the bay radius is wrong.");
        }

        [Test]
        public void Placement_WaitsByAWayInWithItsNoseOut()
        {
            CityLayout layout =
                GenerateLayout(GameSessionState.DefaultCitySeed);
            CityDistrictPointOfInterestDescriptor island = RequireIsland(layout);
            Assert.That(
                CityDistrictPointOfInterestWorldBuilder
                    .TryDescribeFerrymanCarStance(
                        island,
                        out CityDryingYardNpcStance stance),
                Is.True);

            // Nearest way in, measured as the hero would meet it.
            CityDistrictPointOfInterestAccessDescriptor nearest = default;
            float nearestDistance = float.MaxValue;
            foreach (CityDistrictPointOfInterestAccessDescriptor access in
                     island.Accesses)
            {
                float distance =
                    Vector3.Distance(access.Center, stance.Position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = access;
                }
            }

            Assert.That(
                nearestDistance,
                Is.LessThan(7.5f),
                "The car waits at the entrance, not at the back of the lot.");

            // The nose points back out at whoever is arriving, which is also
            // the way it would leave for the road.
            Vector3 inward = new Vector3(
                nearest.OutwardNormal.x, 0f, nearest.OutwardNormal.z)
                .normalized;
            float noseOut = Vector3.Dot(stance.Facing, -inward);
            Assert.That(
                noseOut,
                Is.GreaterThan(0.6f),
                "The car must face out towards the road, not into the lot.");
            Assert.That(
                noseOut,
                Is.LessThan(0.98f),
                "It stands at an angle, not square to the approach.");
        }

        [Test]
        public void Placement_IsDeterministicForASeed()
        {
            CityDistrictPointOfInterestDescriptor first =
                RequireIsland(GenerateLayout(GameSessionState.DefaultCitySeed));
            CityDistrictPointOfInterestDescriptor second =
                RequireIsland(GenerateLayout(GameSessionState.DefaultCitySeed));

            CityDistrictPointOfInterestWorldBuilder
                .TryDescribeFerrymanCarStance(
                    first,
                    out CityDryingYardNpcStance firstStance);
            CityDistrictPointOfInterestWorldBuilder
                .TryDescribeFerrymanCarStance(
                    second,
                    out CityDryingYardNpcStance secondStance);

            Assert.That(
                Vector3.Distance(firstStance.Position, secondStance.Position),
                Is.LessThan(0.0001f));
            Assert.That(
                Vector3.Angle(firstStance.Facing, secondStance.Facing),
                Is.LessThan(0.01f));
        }

        [Test]
        public void Plan_FollowsThePlacementAndSurvivesAMissingIsland()
        {
            CityLayout layout =
                GenerateLayout(GameSessionState.DefaultCitySeed);
            LastRouteCarPlan plan = LastRouteCarPlan.Create(layout);
            CityDistrictPointOfInterestDescriptor island = RequireIsland(layout);
            CityDistrictPointOfInterestWorldBuilder
                .TryDescribeFerrymanCarStance(
                    island,
                    out CityDryingYardNpcStance stance);

            Assert.That(plan.IsPresent, Is.True);
            Assert.That(
                Vector3.Distance(plan.Position, stance.Position),
                Is.LessThan(0.0001f));
            Assert.That(
                LastRouteCarPlan.Create(null).IsPresent,
                Is.False,
                "A missing layout must report an absent car, not throw.");
        }

        [Test]
        public void Prefab_IsBoundPassiveAndPreparedForBothSeats()
        {
            GameObject prefab = LastRouteCarAssetRegistry.LoadPrefab();
            Assert.That(prefab, Is.Not.Null, "The car prefab is missing.");

            var registry = prefab.GetComponent<LastRouteCarAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.IsBound, Is.True);
            Assert.That(
                prefab.GetComponentInChildren<Collider>(true),
                Is.Null,
                "The obstacle box belongs on the runtime root, not the art.");

            // What the ride feature will need, proved before it exists.
            // Against the prefab root, not the imported Body node: the
            // model child is rotated 180 degrees, so its axes are not the
            // car's. That mistake has cost this project four bugs already.
            Vector3 right = registry.transform.right;
            Vector3 body = registry.transform.position;
            float driverSide = Vector3.Dot(
                registry.DriverSeatAnchor.position - body, right);
            float passengerSide = Vector3.Dot(
                registry.PassengerSeatAnchor.position - body, right);
            Assert.That(
                driverSide * passengerSide,
                Is.LessThan(0f),
                "The two seats must face each other across the car.");
            Assert.That(
                registry.LeftSteeringGrip.parent,
                Is.EqualTo(registry.SteeringWheelPivot),
                "A turned rim must carry the hand targets with it.");
            // He sits on the bonnet with his boots ahead of him on the
            // bumper - the pose the whole car is parked for.
            Vector3 forward = registry.transform.forward;
            float seatAhead = Vector3.Dot(
                registry.PerchSeatAnchor.position - body, forward);
            float solesAhead = Vector3.Dot(
                registry.PerchSolesAnchor.position - body, forward);
            Assert.That(
                seatAhead,
                Is.GreaterThan(0f),
                "The perch must be over the bonnet, not the boot.");
            Assert.That(
                solesAhead,
                Is.GreaterThan(seatAhead),
                "His boots rest ahead of him, on the bumper.");
            Assert.That(
                registry.PerchSeatAnchor.position.y -
                registry.PerchSolesAnchor.position.y,
                Is.EqualTo(registry.PerchDrop).Within(0.001f),
                "The seat-to-sole drop is the contract the Ferryman's own " +
                "pose is authored against.");
        }

        [Test]
        public void Docks_StayInsideTheIslandOnTheProductionSeed()
        {
            // Moving the hero's dock clear of the swinging passenger door
            // pushed it from 1.52 m out to 1.85 m out and a metre back along
            // the flank, and the bay placement only ever guaranteed 0.40 m
            // of clearance inside the lot. A dock that ends up off the
            // island is not a visible bug: the prompt simply never appears,
            // because the hero can never stand there.
            CityLayout layout =
                GenerateLayout(GameSessionState.DefaultCitySeed);
            LastRouteCarPlan plan = LastRouteCarPlan.Create(layout);
            Assert.That(
                plan.IsPresent,
                Is.True,
                "The production seed parks the car.");

            CityDistrictPointOfInterestDescriptor island =
                RequireIsland(layout);
            GameObject prefab = LastRouteCarAssetRegistry.LoadPrefab();
            Assert.That(prefab, Is.Not.Null);
            var root = new GameObject("Dock Placement Car");
            try
            {
                root.transform.SetPositionAndRotation(
                    plan.Position,
                    Quaternion.LookRotation(plan.Facing, Vector3.up));
                GameObject instance =
                    Object.Instantiate(prefab, root.transform);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                var registry = instance
                    .GetComponentInChildren<LastRouteCarAssetRegistry>(true);

                LastRouteCarSeatPlan seat = LastRouteCarSeatPlan.Create(
                    registry,
                    plan.Position.y);
                Assert.That(seat.IsPresent, Is.True);
                AssertOnTheIsland(
                    island,
                    seat.EntryRootPosition,
                    "the hero's passenger dock");

                LastRouteFerrymanBoardingPlan boarding =
                    LastRouteFerrymanBoardingPlan.Create(
                        registry,
                        plan.Position.y);
                Assert.That(boarding.IsPresent, Is.True);
                AssertOnTheIsland(
                    island,
                    boarding.LandingPosition,
                    "where the Ferryman lands");
                AssertOnTheIsland(
                    island,
                    boarding.ApproachCorner,
                    "the corner he rounds");
                AssertOnTheIsland(
                    island,
                    boarding.DoorDockPosition,
                    "where he stands to open his own door");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The light that lands on him is a FIXTURE, it is aimed, and it is
        /// IN FRONT OF HIM.
        ///
        /// All three have been wrong in turn. He carried a bare Point with
        /// nothing drawn for it, hung beside him and rewritten every frame
        /// so it walked the lot at his shoulder. That was replaced by a
        /// second head on the island's own route mast, which fixed the
        /// fixture and broke the light: the mast stands by the paving
        /// circle and the car is fitted per seed up to seven metres away
        /// with its nose pointing OUT, so the throw arrived from behind him
        /// across ten metres of inverse square. The post is now fitted to
        /// the bay it serves, and what is worth proving is the geometry
        /// that makes it work - close, ahead of the perch, above his cap,
        /// and pointed at him.
        /// </summary>
        [Test]
        public void FerrymanLamp_StandsInFrontOfHisCarAndIsAimedAtHim()
        {
            CityLayout layout =
                GenerateLayout(GameSessionState.DefaultCitySeed);
            CityDistrictPointOfInterestDescriptor island =
                RequireIsland(layout);
            Assert.That(
                CityDistrictPointOfInterestWorldBuilder
                    .TryDescribeFerrymanCarStance(
                        island,
                        out CityDryingYardNpcStance stance),
                Is.True,
                "The production seed is meant to park the car.");
            Assert.That(
                CityDistrictPointOfInterestWorldBuilder
                    .TryDescribeFerrymanLampStance(
                        island,
                        out Vector3 post,
                        out Vector3 aim),
                Is.True,
                "The production seed is meant to find room for the post; " +
                "the mast fallback is for hostile bays, not for this one.");

            // In front of the man, not of the car: he sits on the bonnet
            // roughly two metres ahead of the middle of it.
            Vector3 facing = stance.Facing.normalized;
            Vector3 perch = stance.Position + (facing * PerchLead);
            Vector3 toPost = post - perch;
            toPost.y = 0f;
            Assert.That(
                Vector3.Dot(toPost, facing),
                Is.GreaterThan(0.4f),
                $"The post stands {toPost} from a man looking {facing}. " +
                "Behind him is where the mast head already was.");
            Assert.That(
                toPost.magnitude,
                Is.LessThan(4.5f),
                "Close enough that inverse square is not the whole story.");

            // Off the paving and inside the lot, like everything else on
            // this island. The bay fitter's own two rules.
            Assert.That(
                Vector2.Distance(
                    new Vector2(post.x, post.z),
                    new Vector2(island.Center.x, island.Center.z)),
                Is.GreaterThan(PavingRadius),
                "The island's empty middle is authored, not incidental.");
            Assert.That(
                island.PublicBounds.Contains(new Vector2(post.x, post.z)),
                Is.True,
                "A post outside the lot is a post in the street.");
            foreach (CityDistrictPointOfInterestAccessDescriptor access in
                     island.Accesses)
            {
                Assert.That(
                    access.ApproachBounds.Contains(
                        new Vector2(post.x, post.z)),
                    Is.False,
                    "A post in a way in is the one thing the walkable " +
                    "mask would never report.");
            }

            // And it does not stand where the Ferryman's boots land when he
            // drops off his own bumper.
            Vector3 landing = stance.Position +
                (facing *
                 (PerchSolesLead +
                  LastRouteFerrymanBoardingPlan.LandingReach));
            Assert.That(
                Vector2.Distance(
                    new Vector2(post.x, post.z),
                    new Vector2(landing.x, landing.z)),
                Is.GreaterThan(0.7f),
                "He would walk into it on the way down.");

            var root = new GameObject("Ferryman Lamp Test");
            try
            {
                CityDistrictPointOfInterestWorldBuilder.Build(
                    root.transform,
                    layout);

                Transform head = FindDescendant(
                    root.transform,
                    "Ferryman Floodlight Head");
                Assert.That(
                    head,
                    Is.Not.Null,
                    "Nothing was built to light him.");
                Transform postObject = FindDescendant(
                    root.transform,
                    "Ferryman Lamp Post");
                Assert.That(
                    postObject,
                    Is.Not.Null,
                    "The head has to hang off something drawn. A light " +
                    "with no fixture is the thing this replaced.");

                Light[] lights = head.GetComponentsInChildren<Light>(true);
                Assert.That(lights.Length, Is.EqualTo(1));
                Assert.That(
                    lights[0].type,
                    Is.EqualTo(LightType.Spot),
                    "A directed fixture, not a bare bulb: the cone is " +
                    "what keeps the warmth on him rather than on the lot.");
                Assert.That(
                    lights[0].shadows,
                    Is.EqualTo(LightShadows.None));
                Assert.That(
                    lights[0].range,
                    Is.LessThan(12f),
                    "A short throw now that it is standing next to him; a " +
                    "long one only spills onto the paving the mast owns.");

                // Above his cap, because the design draws him no eyes and
                // leans on the brim's own shadow. Lighting that face from
                // below is the one angle that argues with it.
                Assert.That(
                    head.position.y,
                    Is.GreaterThan(stance.Position.y + 2.0f),
                    "The head has to rake down over the brim.");

                Vector3 toMan = aim - head.position;
                Assert.That(
                    Vector3.Angle(head.forward, toMan),
                    Is.LessThan(12f),
                    $"The head points {head.forward} while the man it is " +
                    $"there to light is {toMan.normalized} away. The " +
                    "recipe root is SCALED horizontally, so an aim that " +
                    "forgets to undo that scale lands beside the car.");

                // The post is a thing standing in the middle of a lot, so
                // unlike the mast head it owns an obstacle. The head still
                // owns none.
                Assert.That(
                    head.GetComponentsInChildren<Collider>(true),
                    Is.Empty,
                    "The head must not add a collider of its own.");
                Assert.That(
                    FindDescendant(
                        root.transform,
                        "Ferryman Lamp Post Collider"),
                    Is.Not.Null,
                    "A post people can walk through is not a post.");

                // And nothing about him emits any more.
                Assert.That(
                    FindDescendant(root.transform, "Ferryman Lamp"),
                    Is.Null,
                    "The light that followed him around should be gone.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The post is fitted rather than authored, so the thing that can
        /// quietly rot is how often it fits at all: a ladder that misses on
        /// most seeds leaves most cities back on the dim mast head with
        /// nothing failing anywhere. This walks a spread of seeds and holds
        /// the fit rate.
        /// </summary>
        [Test]
        public void FerrymanLamp_FitsOnAlmostEverySeedThatParksTheCar()
        {
            int parked = 0;
            int lit = 0;
            for (int seed = 1; seed <= 40; seed++)
            {
                CityLayout layout = GenerateLayout(seed);
                CityDistrictPointOfInterestDescriptor island = null;
                foreach (CityDistrictPointOfInterestDescriptor descriptor in
                         layout.DistrictPointsOfInterest)
                {
                    if (descriptor.Kind ==
                        CityDistrictPointOfInterestKind
                            .NightlifeLastRouteIsland)
                    {
                        island = descriptor;
                        break;
                    }
                }

                if (island == null ||
                    !CityDistrictPointOfInterestWorldBuilder
                        .TryDescribeFerrymanCarStance(island, out _))
                {
                    continue;
                }

                parked++;
                if (CityDistrictPointOfInterestWorldBuilder
                        .TryDescribeFerrymanLampStance(island, out _, out _))
                {
                    lit++;
                }
            }

            Assert.That(
                parked,
                Is.GreaterThan(4),
                "Too few seeds parked the car for this to prove anything.");
            Assert.That(
                lit,
                Is.GreaterThanOrEqualTo(Mathf.CeilToInt(parked * 0.75f)),
                $"Only {lit} of {parked} parked cars found room for a " +
                "post. The mast fallback is meant to be the exception.");
        }

        private static Transform FindDescendant(Transform parent, string name)
        {
            foreach (Transform child in
                     parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static void AssertOnTheIsland(
            CityDistrictPointOfInterestDescriptor island,
            Vector3 position,
            string what)
        {
            Assert.That(
                island.PublicBounds.Contains(
                    new Vector2(position.x, position.z)),
                Is.True,
                $"On the production seed {what} lands at {position}, " +
                $"outside the island's own {island.PublicBounds}.");
        }

        /// <summary>
        /// The imported-basis trap, seventh instance, and the first one that
        /// blacked the screen out.
        ///
        /// The beams hang off the sprung body so they dip under braking, but
        /// `LastRouteCarSuspension` copies that empty's `localRotation`
        /// straight off the IMPORTED body node — whose forward on this car is
        /// very nearly vertical. Aiming with a local Euler in that space threw
        /// both beams at the sky, and because the ride blackout had already
        /// put the sun out there was nothing else lighting the road: the
        /// mountain came up pure black. Nothing threw and nothing logged.
        ///
        /// So the aim is asserted against the RUNTIME ROOT, which is the one
        /// transform this project sets itself.
        /// </summary>
        [Test]
        public void BurningHeadlights_PointDownTheRoadAndRideTheSprings()
        {
            var parent = new GameObject("Headlight Aim Test");
            try
            {
                Vector3 facing = new Vector3(0.6f, 0f, -0.8f).normalized;
                LastRouteCarAssetRegistry car = LastRouteCarFactory.Create(
                    parent.transform,
                    LastRouteCarPlan.At(new Vector3(12f, 0f, -30f), facing),
                    default,
                    null,
                    true);
                Assert.That(car, Is.Not.Null, "The car failed to spawn.");

                Transform root = car.transform.parent != null
                    ? car.transform.parent
                    : car.transform;
                var headlights = root.GetComponent<LastRouteCarHeadlights>();
                Assert.That(
                    headlights,
                    Is.Not.Null,
                    "A burning car must carry real headlights.");

                var suspension = root.GetComponent<LastRouteCarSuspension>();
                Assert.That(
                    suspension?.SprungBody,
                    Is.Not.Null,
                    "The lamps need a sprung body to hang from.");

                foreach (Light beam in new[]
                         {
                             headlights.LeftBeam,
                             headlights.RightBeam,
                             headlights.Spill
                         })
                {
                    Assert.That(beam, Is.Not.Null);
                    Assert.That(
                        beam.transform.parent,
                        Is.EqualTo(suspension.SprungBody),
                        $"'{beam.name}' must ride the springs, so the beam " +
                        "dips when the car brakes.");
                    Assert.That(
                        Vector3.Dot(beam.transform.forward, root.forward),
                        Is.GreaterThan(0.95f),
                        $"'{beam.name}' points somewhere other than down " +
                        "the road. Its parent's axes are the imported " +
                        "model's, so the aim must come from the root.");
                    Assert.That(
                        beam.transform.forward.y,
                        Is.LessThan(0f),
                        $"'{beam.name}' must rake down onto the asphalt.");
                }

                float left = Vector3.Dot(
                    headlights.LeftBeam.transform.position - root.position,
                    root.right);
                float right = Vector3.Dot(
                    headlights.RightBeam.transform.position - root.position,
                    root.right);
                Assert.That(
                    left * right,
                    Is.LessThan(0f),
                    "The pair must straddle the car's centre line, or they " +
                    "read as one lamp.");

                // And every emitter stands OUTSIDE the car, in front of the
                // bodywork, which is where a headlight is.
                //
                // They used to sit `1.8 m` BEHIND the lens, to flatten an
                // inverse-square falloff that made the near field eleven
                // times the pool at fourteen metres. The arithmetic was
                // right and the place was wrong: `1.8 m` back from these
                // lamps is the windscreen, so both beams emitted from inside
                // the cabin and their cones opened out across the bonnet,
                // the pillars and the door card. The white blobs in the
                // frame were never the road - the car was lighting itself.
                Bounds shell = MeasureCarBounds(root);
                foreach (Light beam in new[]
                         {
                             headlights.LeftBeam,
                             headlights.RightBeam,
                             headlights.Spill
                         })
                {
                    Vector3 emitter = beam.transform.position;
                    Assert.That(
                        shell.Contains(emitter),
                        Is.False,
                        $"'{beam.name}' emits from inside the car's own " +
                        "bodywork.");

                    float ahead = Vector3.Dot(
                        emitter - shell.center,
                        root.forward);
                    Assert.That(
                        ahead,
                        Is.GreaterThan(0f),
                        $"'{beam.name}' sits behind the middle of the car. " +
                        "It has to be at the nose with the lamps.");
                }

                // Attached to the lamps, not merely somewhere in front: no
                // further from the lit face than a hand's breadth.
                Transform lensTransform = null;
                for (int index = 0; index < car.Bindings.Count; index++)
                {
                    if (car.Bindings[index].Role == "headlight")
                    {
                        lensTransform =
                            car.Bindings[index].Renderer.transform;
                        break;
                    }
                }

                Assert.That(lensTransform, Is.Not.Null, "no lens drawn");
                Bounds lens = lensTransform
                    .GetComponent<Renderer>()
                    .bounds;
                float halfDepth =
                    (Mathf.Abs(root.forward.x) * lens.extents.x) +
                    (Mathf.Abs(root.forward.y) * lens.extents.y) +
                    (Mathf.Abs(root.forward.z) * lens.extents.z);
                foreach (Light beam in new[]
                         {
                             headlights.LeftBeam,
                             headlights.RightBeam,
                             headlights.Spill
                         })
                {
                    float proud = Vector3.Dot(
                        beam.transform.position - lens.center,
                        root.forward) - halfDepth;
                    Assert.That(
                        proud,
                        Is.InRange(0f, 0.25f),
                        $"'{beam.name}' stands {proud:0.00} m off the lit " +
                        "face. It is meant to be the lamp, not a light " +
                        "floating near it.");
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// The car's drawn shell, off its renderers rather than off the
        /// generator's numbers, so a redrawn body moves the test with it.
        /// Lights, halos and the seat trigger are not bodywork.
        /// </summary>
        private static Bounds MeasureCarBounds(Transform root)
        {
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default;
            bool started = false;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null ||
                    renderer.GetComponent<CityLightHalo>() != null)
                {
                    continue;
                }

                if (!started)
                {
                    bounds = renderer.bounds;
                    started = true;
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }

            Assert.That(started, Is.True, "the car drew nothing at all");
            return bounds;
        }

        private static IEnumerable<Vector3> Corners(
            CityDryingYardNpcStance stance)
        {
            Vector3 forward = stance.Facing.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float halfLength = CarLength * 0.5f;
            float halfWidth = CarWidth * 0.5f;
            yield return stance.Position + forward * halfLength + right * halfWidth;
            yield return stance.Position + forward * halfLength - right * halfWidth;
            yield return stance.Position - forward * halfLength + right * halfWidth;
            yield return stance.Position - forward * halfLength - right * halfWidth;
        }
    }
}
