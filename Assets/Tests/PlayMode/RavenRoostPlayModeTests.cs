using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// Scene-level contracts of the outdoor raven roost pairs, end
    /// states only: flight timing and planner geometry live in
    /// EditMode on the pure models, because batchmode frame pacing
    /// makes trajectory assertions lies. What only a real scene can
    /// witness is wired here — that the roots actually raise the
    /// controllers, that the activation radius freezes what the hero
    /// cannot see and wakes it back PERCHED, and that a pair flushes
    /// and returns on the live polling loop. Run ONE fixture per
    /// invocation: each loads a whole area, and the project has
    /// already hit the test-runner's instant-step limit from
    /// stacking scene-loading fixtures.
    /// </summary>
    public sealed class RavenRoostPlayModeTests
    {
        private const float TimeoutSeconds = 30f;

        /// <summary>Generous: a city flush carries both birds 46 m
        /// out and back through the staggered machine, and batchmode
        /// paces itself.</summary>
        private const float FlightTimeoutSeconds = 90f;

        /// <summary>
        /// Roosts whose home lands within this margin of the
        /// activation radius are skipped by the spawn sweeps: the
        /// boundary itself belongs to the controller's hysteresis
        /// band, and a spawn that happens to graze it proves nothing
        /// either way.
        /// </summary>
        private const float ActivationBoundaryMarginMeters = 2f;

        /// <summary>
        /// The spawn sweeps assert PerchedIdle only on pairs the hero
        /// stands comfortably outside the 3.5 m flush circle of — a
        /// spawn point right beside a kerb roost legitimately
        /// startles it, and that is behaviour, not a defect.
        /// </summary>
        private const float FlushSafetyMarginMeters = 6f;

        /// <summary>
        /// The terrain-grounded city roosts — the only ones with
        /// standable ground inside the flush circle. Deck perches
        /// (mol coping, barge gunwale, bridge kerb, landing platform)
        /// hang over water or a carriageway, so the flush smoke walks
        /// to a land pair instead.
        /// </summary>
        private static readonly string[] CityLandRoostIds =
        {
            "city-roost-park-fountain",
            "city-roost-tunnel-forecourt",
            "city-roost-plain-kerb-a",
            "city-roost-plain-kerb-b"
        };

        [UnityTest]
        public IEnumerator
            CityRoostsFreezeByDistanceAndTheNearestLandPairFlushesAndReturns()
        {
            IgnoreUnlessRavenPrefabBuilt();
            GameSessionState.BeginNewGame();

            CityGameRoot city = null;
            yield return LoadSceneAndWaitForRoot<CityGameRoot>(
                SceneIds.City,
                root => city = root);
            yield return null;

            Assert.That(city.IsInitialized, Is.True);
            RavenRoostController roosts = city.CityRavenRoosts;
            Assert.That(
                roosts,
                Is.Not.Null,
                "The default city plans roosts, so the root must " +
                "raise the controller.");

            // The same pure planner run the root wired — the planner
            // is deterministic over the layout, so descriptor i here
            // IS controller roost i.
            IReadOnlyList<RavenRoostDescriptor> descriptors =
                CityRavenRoostPlanner.Create(
                    city.Layout,
                    city.World,
                    new CityMapCityTeleportGround(city.Layout),
                    GameSessionState.CitySeed);
            Assert.That(descriptors.Count, Is.GreaterThan(0));
            Assert.That(
                roosts.RoostCount,
                Is.EqualTo(descriptors.Count),
                "With the raven art built, every planned roost must " +
                "stand — a zero count means the controller went " +
                "inert.");

            // One more frame so the controller's first Update has
            // frozen whatever spawned beyond the activation radius.
            yield return null;

            float radius =
                RavenRoostSettings.City.ActivationRadiusMeters;
            Vector3 hero =
                city.Player.GameObject.transform.position;
            int frozenCount = 0;
            for (int index = 0; index < descriptors.Count; index++)
            {
                float home = PlanarDistance(
                    hero,
                    descriptors[index].HomeReference);
                if (home >= radius + ActivationBoundaryMarginMeters)
                {
                    AssertRoostFrozen(roosts, index);
                    frozenCount++;
                }
                else if (home <=
                         radius - ActivationBoundaryMarginMeters &&
                         DistanceToNearestPerch(
                             hero,
                             descriptors[index]) >
                         FlushSafetyMarginMeters)
                {
                    AssertRoostPerchedAndVisible(roosts, index);
                }
            }

            // The default city's roosts span several districts while
            // the activation radius is a two-block circle, so at
            // least one far roost must exist for the freeze contract
            // to have been observed at all.
            Assert.That(
                frozenCount,
                Is.GreaterThanOrEqualTo(1),
                "Every planned roost sat within the activation " +
                "radius of the spawn — the deactivation contract " +
                "was never exercised.");

            // Arm's length of the nearest LAND pair: both flush.
            int target = FindNearestLandRoost(descriptors, hero);
            Assert.That(
                target,
                Is.GreaterThanOrEqualTo(0),
                "The default city plans at least one terrain roost.");
            Vector3 perchA = descriptors[target].PerchA.Position;
            Vector3 perchB = descriptors[target].PerchB.Position;
            Vector3 awayFromB = perchA - perchB;
            awayFromB.y = 0f;
            awayFromB = awayFromB.sqrMagnitude > 0.0001f
                ? awayFromB.normalized
                : Vector3.right;
            // 2.6 m off perch A on its far side from B: inside A's
            // 3.5 m flush circle, and inside the reactivation band if
            // the roost had frozen — the same teleport both wakes and
            // startles it, which is exactly what walking up would do.
            city.Player.Motor.Teleport(
                perchA + awayFromB * 2.6f + Vector3.up * 0.5f);

            float deadline =
                Time.realtimeSinceStartup + FlightTimeoutSeconds;
            while (roosts.GetRoostPhase(target) !=
                   CemeteryRavenPhase.Startled &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                roosts.GetRoostPhase(target),
                Is.EqualTo(CemeteryRavenPhase.Startled),
                "Arm's length of the pair must flush it.");
            CemeteryRavenActor[] actors = GetActors(roosts, target);
            while ((!actors[0].HasFlight || !actors[1].HasFlight) &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                actors[0].HasFlight,
                Is.True,
                "The startle must put both birds in the air.");
            Assert.That(actors[1].HasFlight, Is.True);

            // Retreat past the 33.6 m return gate while staying well
            // inside the activation radius, so the return plays out
            // on a live roost instead of being snapped by a freeze.
            Vector3 homeReference = descriptors[target].HomeReference;
            city.Player.Motor.Teleport(
                homeReference + new Vector3(45f, 0.5f, 0f));
            deadline =
                Time.realtimeSinceStartup + FlightTimeoutSeconds;
            while (roosts.GetRoostPhase(target) !=
                   CemeteryRavenPhase.PerchedIdle &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                roosts.GetRoostPhase(target),
                Is.EqualTo(CemeteryRavenPhase.PerchedIdle),
                "The pair never came back after the retreat past " +
                "the return gate.");
            AssertRoostPerchedAndVisible(roosts, target);

            // The same points, not new ones: bird hosts are created
            // A then B, so child order matches the descriptor.
            Assert.That(
                Vector3.Distance(
                    actors[0].transform.position,
                    perchA),
                Is.LessThanOrEqualTo(0.05f),
                "Raven A returns to its own perch.");
            Assert.That(
                Vector3.Distance(
                    actors[1].transform.position,
                    perchB),
                Is.LessThanOrEqualTo(0.05f),
                "Raven B returns to its own perch.");
        }

        [UnityTest]
        public IEnumerator
            MountainRoadRoostsSpawnPerchedInsideTheBudgetWithThePortalPairAwake()
        {
            IgnoreUnlessRavenPrefabBuilt();
            GameSessionState.BeginNewGame();

            MountainRoadRoot road = null;
            yield return LoadSceneAndWaitForRoot<MountainRoadRoot>(
                SceneIds.MountainRoad,
                root => road = root);
            yield return null;

            Assert.That(road.IsInitialized, Is.True);
            RavenRoostController roosts = road.RavenRoosts;
            Assert.That(
                roosts,
                Is.Not.Null,
                "The default road plans roosts, so the root must " +
                "raise the controller.");
            IReadOnlyList<RavenRoostDescriptor> descriptors =
                MountainRoadRavenRoostPlanner.Create(
                    road.Plan,
                    new CityMapMountainRoadTeleportGround(
                        road.World.WalkableArea),
                    road.Plan.Seed);
            Assert.That(descriptors.Count, Is.GreaterThan(0));
            Assert.That(
                roosts.RoostCount,
                Is.EqualTo(descriptors.Count));
            yield return null;

            AssertBudget(roosts);

            float radius = RavenRoostSettings
                .MountainRoad.ActivationRadiusMeters;
            Vector3 hero =
                road.Player.GameObject.transform.position;
            int portal = -1;
            for (int index = 0; index < descriptors.Count; index++)
            {
                if (string.Equals(
                        descriptors[index].StableId,
                        "road-roost-exit-portal",
                        StringComparison.Ordinal))
                {
                    portal = index;
                }

                float home = PlanarDistance(
                    hero,
                    descriptors[index].HomeReference);
                if (home >= radius + ActivationBoundaryMarginMeters)
                {
                    AssertRoostFrozen(roosts, index);
                }
                else if (home <=
                         radius - ActivationBoundaryMarginMeters &&
                         DistanceToNearestPerch(
                             hero,
                             descriptors[index]) >
                         FlushSafetyMarginMeters)
                {
                    AssertRoostPerchedAndVisible(roosts, index);
                }
            }

            // The feature's first read: the hero spawns six metres
            // inside the tunnel and walks out past the portal pair,
            // so at the spawn point that roost must already be awake
            // and perched — never a bird popping in as he steps out.
            Assert.That(
                portal,
                Is.GreaterThanOrEqualTo(0),
                "The default road plans the exit-portal roost.");
            Assert.That(
                PlanarDistance(
                    hero,
                    descriptors[portal].HomeReference),
                Is.LessThan(
                    radius - ActivationBoundaryMarginMeters),
                "The tunnel spawn stands an easy walk from the " +
                "portal pair.");
            AssertRoostPerchedAndVisible(roosts, portal);

            // The deterministic freeze/thaw cycle, exercised HERE
            // rather than in the village: the motor constrains every
            // step to the walkable mask, and only this scene's 620 m
            // road ribbon holds standable ground far enough from a
            // home to carry the hero past the activation radius —
            // the summit terrace is a genuine walkable stand about
            // 160 m from the portal pair's home.
            int brink = -1;
            for (int index = 0; index < descriptors.Count; index++)
            {
                if (string.Equals(
                        descriptors[index].StableId,
                        "road-roost-summit-brink",
                        StringComparison.Ordinal))
                {
                    brink = index;
                }
            }

            Assert.That(
                brink,
                Is.GreaterThanOrEqualTo(0),
                "The default road plans the summit-brink roost.");
            Vector3 portalHome = descriptors[portal].HomeReference;
            Vector3 terraceStand =
                descriptors[brink].PerchB.Position +
                new Vector3(0f, 0.5f, 0f);
            Assert.That(
                PlanarDistance(terraceStand, portalHome),
                Is.GreaterThan(
                    radius + ActivationBoundaryMarginMeters),
                "The terrace stands beyond the portal pair's " +
                "activation radius by construction.");
            road.Player.Motor.Teleport(terraceStand);
            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (roosts.IsRoostActive(portal) &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            AssertRoostFrozen(roosts, portal);

            // Back inside the hysteresis band: the pair must simply
            // BE there again — PerchedIdle on a machine that still
            // says it spawned perched, with no flight in the air. A
            // replayed or fresh arrival here would show an event
            // nobody was present for.
            road.Player.Motor.Teleport(
                portalHome + new Vector3(6f, 0.5f, 0f));
            deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!roosts.IsRoostActive(portal) &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            AssertRoostPerchedAndVisible(roosts, portal);
        }

        [UnityTest]
        public IEnumerator
            AlpineVillageRoostsAllSitAwakeInsideTheBowl()
        {
            IgnoreUnlessRavenPrefabBuilt();
            GameSessionState.BeginNewGame();

            AlpineVillageRoot village = null;
            yield return LoadSceneAndWaitForRoot<AlpineVillageRoot>(
                SceneIds.AlpineVillage,
                root => village = root);
            yield return null;

            Assert.That(village.IsInitialized, Is.True);
            RavenRoostController roosts = village.RavenRoosts;
            Assert.That(
                roosts,
                Is.Not.Null,
                "The default village plans roosts, so the root must " +
                "raise the controller.");
            IReadOnlyList<RavenRoostDescriptor> descriptors =
                AlpineVillageRavenRoostPlanner.Create(
                    village.Plan,
                    new CityMapAlpineVillageTeleportGround(
                        village.World.WalkableArea),
                    village.Plan.Seed);
            Assert.That(descriptors.Count, Is.GreaterThan(0));
            Assert.That(
                roosts.RoostCount,
                Is.EqualTo(descriptors.Count));
            yield return null;

            AssertBudget(roosts);

            float radius = RavenRoostSettings
                .AlpineVillage.ActivationRadiusMeters;
            Vector3 hero =
                village.Player.GameObject.transform.position;
            for (int index = 0; index < descriptors.Count; index++)
            {
                float home = PlanarDistance(
                    hero,
                    descriptors[index].HomeReference);
                if (home >= radius + ActivationBoundaryMarginMeters)
                {
                    AssertRoostFrozen(roosts, index);
                }
                else if (home <=
                         radius - ActivationBoundaryMarginMeters &&
                         DistanceToNearestPerch(
                             hero,
                             descriptors[index]) >
                         FlushSafetyMarginMeters)
                {
                    AssertRoostPerchedAndVisible(roosts, index);
                }
            }

            // The freeze/thaw cycle is deliberately NOT exercised
            // here: the player motor constrains every step to the
            // walkable mask, and the village bowl holds no walkable
            // point far enough from either home (the whole lane sits
            // inside the 110 m activation radius), so a teleport past
            // the radius is dragged straight back onto the lane. The
            // cycle lives in the MountainRoad test, whose 620 m
            // ribbon of walkable road can genuinely carry the hero
            // out of a roost's radius. Here the honest assertion is
            // the opposite one: in a bowl this small every roost is
            // awake, perched and never flew an arrival.
            for (int index = 0; index < descriptors.Count; index++)
            {
                if (DistanceToNearestPerch(
                        village.Player.GameObject.transform.position,
                        descriptors[index]) >
                    FlushSafetyMarginMeters)
                {
                    AssertRoostPerchedAndVisible(roosts, index);
                }
            }
        }

        /// <summary>
        /// The canon budget, per roost host: exactly two
        /// AmbienceDetails voices at the shared audible radius and no
        /// light of any kind. Counted against each host rather than
        /// the controller so the discipline stays a per-roost
        /// property.
        /// </summary>
        private static void AssertBudget(RavenRoostController roosts)
        {
            Assert.That(GameAudioMixer.IsAvailable, Is.True);
            for (int index = 0; index < roosts.RoostCount; index++)
            {
                Transform host = roosts.GetRoostHost(index);
                Assert.That(
                    host.GetComponentsInChildren<Light>(true),
                    Is.Empty,
                    "A roost adds no light of any kind.");
                AudioSource[] sources =
                    host.GetComponentsInChildren<AudioSource>(true);
                Assert.That(
                    sources,
                    Has.Length.EqualTo(2),
                    "Exactly two voices under one roost host, " +
                    "never more.");
                for (int voice = 0; voice < sources.Length; voice++)
                {
                    Assert.That(
                        sources[voice].maxDistance,
                        Is.EqualTo(
                            CemeteryRavenVoice.AudibleRadiusMeters));
                    Assert.That(
                        sources[voice].outputAudioMixerGroup,
                        Is.SameAs(
                            GameAudioMixer.AmbienceDetailsGroup));
                }
            }
        }

        private static void AssertRoostPerchedAndVisible(
            RavenRoostController roosts,
            int index)
        {
            Assert.That(
                roosts.IsRoostActive(index),
                Is.True,
                $"Roost {index} should be awake here.");
            Assert.That(
                roosts.GetRoostPhase(index),
                Is.EqualTo(CemeteryRavenPhase.PerchedIdle),
                $"Roost {index} should sit in PerchedIdle here.");
            Assert.That(
                roosts.DidRoostSpawnPerchedWithoutArrival(index),
                Is.True,
                "Every roost director arms as already-sealed: no " +
                "arrival flight can ever have played.");
            CemeteryRavenActor[] actors = GetActors(roosts, index);
            for (int bird = 0; bird < actors.Length; bird++)
            {
                Assert.That(actors[bird].enabled, Is.True);
                Assert.That(actors[bird].IsPerched, Is.True);
                Assert.That(
                    actors[bird].HasFlight,
                    Is.False,
                    "A perched-idle bird holds no flight.");
                Assert.That(
                    actors[bird].Anchors.Renderers[0].enabled,
                    Is.True,
                    "An awake perched bird draws.");
            }
        }

        private static void AssertRoostFrozen(
            RavenRoostController roosts,
            int index)
        {
            Assert.That(
                roosts.IsRoostActive(index),
                Is.False,
                $"Roost {index} should be frozen here.");
            CemeteryRavenActor[] actors = GetActors(roosts, index);
            for (int bird = 0; bird < actors.Length; bird++)
            {
                Assert.That(
                    actors[bird].enabled,
                    Is.False,
                    "A frozen bird does not tick.");
                Assert.That(
                    actors[bird].IsPerched,
                    Is.True,
                    "Freezing seats the bird; any flight is " +
                    "dropped on the floor.");
                Assert.That(
                    actors[bird].Anchors.Renderers[0].enabled,
                    Is.False,
                    "A frozen bird draws nothing.");
            }
        }

        private static CemeteryRavenActor[] GetActors(
            RavenRoostController roosts,
            int index)
        {
            CemeteryRavenActor[] actors = roosts.GetRoostHost(index)
                .GetComponentsInChildren<CemeteryRavenActor>(true);
            Assert.That(actors, Has.Length.EqualTo(2));
            return actors;
        }

        private static int FindNearestLandRoost(
            IReadOnlyList<RavenRoostDescriptor> descriptors,
            Vector3 hero)
        {
            int best = -1;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < descriptors.Count; index++)
            {
                if (Array.IndexOf(
                        CityLandRoostIds,
                        descriptors[index].StableId) < 0)
                {
                    continue;
                }

                float distance = PlanarDistance(
                    hero,
                    descriptors[index].HomeReference);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = index;
                }
            }

            return best;
        }

        private static float DistanceToNearestPerch(
            Vector3 hero,
            in RavenRoostDescriptor descriptor)
        {
            return Mathf.Min(
                PlanarDistance(hero, descriptor.PerchA.Position),
                PlanarDistance(hero, descriptor.PerchB.Position));
        }

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// The scene guard the cemetery suites use too: until the
        /// raven prefab is built by the editor pipeline the roost
        /// controllers degrade to inert and there is nothing to
        /// observe, and an Ignore says so honestly where a failure
        /// would cry wolf.
        /// </summary>
        private static void IgnoreUnlessRavenPrefabBuilt()
        {
            CemeteryRavenProvider provider =
                CemeteryRavenProvider.Load();
            if (provider == null || provider.RavenPrefab == null)
            {
                Assert.Ignore(
                    "The cemetery raven prefab is not built yet.");
            }
        }

        private static IEnumerator LoadSceneAndWaitForRoot<T>(
            string sceneName,
            Action<T> capture)
            where T : Component
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (!operation.isDone &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(operation.isDone, Is.True);
            deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                T root = UnityEngine.Object.FindAnyObjectByType<T>();
                if (root != null)
                {
                    capture(root);
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"Scene '{sceneName}' never built its root.");
        }
    }
}
