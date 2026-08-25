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
