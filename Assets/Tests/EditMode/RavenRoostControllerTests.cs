using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The scene guard on the LIVE roost controller, the cemetery
    /// controller guard's shape restated for N roosts: whatever the
    /// pairs do, they may add no light to any scene's budget and no
    /// more than two AmbienceDetails voices per roost host; every
    /// director arms as already-sealed, so the first thing a hero
    /// can ever see is a perched pair and no arrival flight exists
    /// to replay; and the EditMode teardown must run clean through
    /// DestroyImmediate, because OnDestroy's play/edit branch is
    /// exactly what keeps the voices and the shared clip lease from
    /// leaking into the next test.
    ///
    /// The controller is built over a minimal hand-made two-roost
    /// plan rather than a planner's output on purpose: this file
    /// tests the adapter, and the planners have their own tests.
    /// </summary>
    public sealed class RavenRoostControllerTests
    {
        private readonly List<GameObject> spawned =
            new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = spawned.Count - 1; index >= 0; index--)
            {
                if (spawned[index] != null)
                {
                    Object.DestroyImmediate(spawned[index]);
                }
            }

            spawned.Clear();
        }

        [Test]
        public void Controller_SpawnsPerchedPairsInsideTheBudget()
        {
            GameObject root = CreateGameObject("Roost Test Root");
            GameObject hero = CreateGameObject("Roost Test Hero");
            // Between the two anchors, well inside the city's 88 m
            // activation radius of both and beyond the 3.5 m flush
            // of either: the first polls keep every pair sitting.
            hero.transform.position = new Vector3(20f, 0f, 0f);
            IReadOnlyList<RavenRoostDescriptor> roosts =
                CreateTwoRoosts();

            RavenRoostController controller =
                RavenRoostController.Create(
                    root.transform,
                    roosts,
                    RavenRoostSettings.City,
                    hero.transform,
                    () => false,
                    GameSessionState.DefaultCitySeed);
            Assert.That(controller, Is.Not.Null);

            // EditMode has no player loop; the poll is driven by
            // hand through the project's reflection idiom. The
            // deltas are zero here, which is exactly the point: a
            // spawn-perched pair needs no time to be presentable.
            MethodInfo update =
                typeof(RavenRoostController).GetMethod(
                    "Update",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);
            Assert.That(update, Is.Not.Null);
            update.Invoke(controller, null);
            update.Invoke(controller, null);

            // The budget half holds with or without the staged art:
            // an inert controller has an empty subtree, a live one
            // carries exactly its voices and nothing luminous.
            Assert.That(
                controller.GetComponentsInChildren<Light>(true),
                Is.Empty,
                "A roost may add nothing to any scene's light " +
                "budget.");

            // The behavior half needs the authored raven; when the
            // provider asset is absent the controller must have
            // degraded to inert instead of half-spawning.
            CemeteryRavenProvider provider =
                CemeteryRavenProvider.Load();
            bool artAvailable =
                provider != null && provider.RavenPrefab != null;
            if (!artAvailable)
            {
                Assert.That(
                    controller.RoostCount,
                    Is.Zero,
                    "A missing provider must inert the WHOLE " +
                    "controller, never half of it.");
                Assert.That(
                    controller
                        .GetComponentsInChildren<AudioSource>(true),
                    Is.Empty);
            }
            else
            {
                Assert.That(
                    controller.RoostCount,
                    Is.EqualTo(roosts.Count));
                AudioSource[] allSources = controller
                    .GetComponentsInChildren<AudioSource>(true);
                Assert.That(
                    allSources,
                    Has.Length.EqualTo(2 * roosts.Count),
                    "Two voices per roost, nothing else audible.");
                for (int index = 0;
                     index < controller.RoostCount;
                     index++)
                {
                    Transform host =
                        controller.GetRoostHost(index);
                    Assert.That(host, Is.Not.Null);
                    Assert.That(
                        host.name,
                        Is.EqualTo(
                            RavenRoostController
                                .RoostHostNamePrefix +
                            roosts[index].StableId),
                        "A hierarchy dump must read like the " +
                        "plan's table.");

                    // The <=2-AudioSources-per-host discipline is
                    // a per-roost property, countable because each
                    // roost owns its child host.
                    AudioSource[] hostSources = host
                        .GetComponentsInChildren<AudioSource>(true);
                    Assert.That(
                        hostSources,
                        Has.Length.EqualTo(2),
                        host.name);
                    for (int voice = 0;
                         voice < hostSources.Length;
                         voice++)
                    {
                        Assert.That(
                            hostSources[voice].maxDistance,
                            Is.EqualTo(
                                CemeteryRavenVoice
                                    .AudibleRadiusMeters),
                            "A caw is a detail of its street, " +
                            "not of the district.");
                        if (GameAudioMixer.IsAvailable)
                        {
                            Assert.That(
                                hostSources[voice]
                                    .outputAudioMixerGroup,
                                Is.SameAs(
                                    GameAudioMixer
                                        .AmbienceDetailsGroup));
                        }
                    }

                    // Every roost armed as already-sealed: perched
                    // from the first instant, no arrival flight in
                    // its history to replay.
                    Assert.That(
                        controller.GetRoostPhase(index),
                        Is.EqualTo(CemeteryRavenPhase.PerchedIdle),
                        roosts[index].StableId);
                    Assert.That(
                        controller
                            .DidRoostSpawnPerchedWithoutArrival(
                                index),
                        Is.True,
                        roosts[index].StableId);
                    Assert.That(
                        controller.IsRoostActive(index),
                        Is.True,
                        "Both anchors sit inside the activation " +
                        "radius of the hero.");
                }
            }

            // The AlpineVillage soundscape's teardown lesson:
            // OnDestroy must take the DestroyImmediate branch in
            // EditMode or this very call leaks the voices and the
            // clip lease.
            Object.DestroyImmediate(controller.gameObject);
            Assert.That(controller == null, Is.True);
        }

        [Test]
        public void Create_HonoursItsNullAndEmptyContracts()
        {
            GameObject root = CreateGameObject("Roost Test Root");
            GameObject hero = CreateGameObject("Roost Test Hero");
            IReadOnlyList<RavenRoostDescriptor> roosts =
                CreateTwoRoosts();

            // A plan with no legal roosts is a scene with no roost
            // birds — silent absence, never an empty controller.
            Assert.That(
                RavenRoostController.Create(
                    root.transform,
                    new List<RavenRoostDescriptor>(),
                    RavenRoostSettings.City,
                    hero.transform,
                    () => false,
                    GameSessionState.DefaultCitySeed),
                Is.Null);
            Assert.That(
                RavenRoostController.Create(
                    root.transform,
                    null,
                    RavenRoostSettings.City,
                    hero.transform,
                    () => false,
                    GameSessionState.DefaultCitySeed),
                Is.Null);

            // A missing parent or player is a wiring bug, not a
            // blueprint variation: it throws.
            Assert.Throws<System.ArgumentNullException>(() =>
                RavenRoostController.Create(
                    null,
                    roosts,
                    RavenRoostSettings.City,
                    hero.transform,
                    () => false,
                    GameSessionState.DefaultCitySeed));
            Assert.Throws<System.ArgumentNullException>(() =>
                RavenRoostController.Create(
                    root.transform,
                    roosts,
                    RavenRoostSettings.City,
                    null,
                    () => false,
                    GameSessionState.DefaultCitySeed));
        }

        /// <summary>Two roosts 40 m apart, companions a pair-band
        /// step off their anchors — the minimal honest plan.</summary>
        private static IReadOnlyList<RavenRoostDescriptor>
            CreateTwoRoosts()
        {
            return new List<RavenRoostDescriptor>
            {
                new RavenRoostDescriptor(
                    "roost-test-alpha",
                    new CemeteryRavenPerch(
                        true,
                        "roost-test-alpha",
                        new Vector3(0f, 0f, 0f),
                        0f),
                    new CemeteryRavenPerch(
                        true,
                        "roost-test-alpha",
                        new Vector3(4f, 0f, 0f),
                        270f)),
                new RavenRoostDescriptor(
                    "roost-test-beta",
                    new CemeteryRavenPerch(
                        true,
                        "roost-test-beta",
                        new Vector3(40f, 0f, 0f),
                        180f),
                    new CemeteryRavenPerch(
                        true,
                        "roost-test-beta",
                        new Vector3(40f, 0f, 4f),
                        180f))
            };
        }

        private GameObject CreateGameObject(string name)
        {
            var gameObject = new GameObject(name);
            spawned.Add(gameObject);
            return gameObject;
        }
    }
}
