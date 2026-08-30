using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The kettle's boil lives on a bone the PlayableGraph writes every
    /// frame, and its steam is a ParticleSystem that simulates in play
    /// mode only, so nothing short of a running scene proves that the
    /// lid actually moves on the head, keeps its centre while it does,
    /// keeps going in every locomotion state, survives a pool release
    /// and stays under the bus roof. Built on the airborne grounding
    /// skeleton: a real camera, AlwaysAnimate, one Advance per frame.
    /// </summary>
    public sealed class CityKettleHatBoilPlayModeTests
    {
        private const float SampleDeltaTime = 1f / 60f;
        private const int BoilSampleCount = 240;
        private const int SteamWarmupSamples = 60;
        private const int RebindSamples = 10;
        private const uint EffectSeed = 7u;
        private const float MinimumIdleTilt = 3f;
        private const float MaximumIdleTilt = 8f;
        private const float MinimumIdleLift = 0.010f;
        private const float MaximumIdleLift = 0.018f;
        private const float CentreTolerance = 0.001f;
        private const float SpoutTolerance = 0.0001f;
        private const float MinimumWalkHeadTravel = 0.01f;
        private const float RestAngleTolerance = 0.01f;
        private const float RestDistanceTolerance = 0.0001f;

        /// <summary>
        /// A batch-mode frame lasts well under a millisecond, so sixty of
        /// them are a few hundredths of a second of particle time and the
        /// rest rate of three a second emits nothing in that. The steam is
        /// therefore proved by fast-forwarding the system a fixed span,
        /// which is frame-rate independent, rather than by counting frames.
        /// </summary>
        private const float SteamProofSeconds = 1.5f;

        private readonly List<GameObject> owned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = owned.Count - 1; index >= 0; index--)
            {
                if (owned[index] != null)
                {
                    Object.DestroyImmediate(owned[index]);
                }
            }

            owned.Clear();
        }

        [UnityTest]
        public IEnumerator IdleWalker_TremblesVentsAndSteamsFromTheSpout()
        {
            CreateCamera();
            KettleWalker walker = CreateWalker(Vector3.zero);

            float peakTilt = 0f;
            float peakLift = 0f;
            for (int sample = 0; sample < BoilSampleCount; sample++)
            {
                walker.Presentation.Advance(SampleDeltaTime, false, true);
                yield return null;
                peakTilt = Mathf.Max(peakTilt, walker.PivotTiltDegrees);
                peakLift = Mathf.Max(peakLift, walker.Effect.LastLidLift);
                AssertLidCentrePreserved(walker);
                if (sample >= SteamWarmupSamples)
                {
                    AssertSteamOnTheSpout(walker);
                }
            }

            Assert.That(
                peakTilt,
                Is.InRange(MinimumIdleTilt, MaximumIdleTilt),
                "The lid's peak tilt left its authored band.");
            Assert.That(
                peakLift,
                Is.InRange(MinimumIdleLift, MaximumIdleLift),
                "The lid's peak lift left its authored band.");
            AssertSteamEmits(walker);
        }

        [UnityTest]
        public IEnumerator Boil_RunsInEveryStateAndRidesTheHead()
        {
            CreateCamera();
            KettleWalker idle = CreateWalker(new Vector3(-2f, 0f, 0f));
            KettleWalker walking = CreateWalker(Vector3.zero);
            KettleWalker seated = CreateWalker(new Vector3(2f, 0f, 0f));
            Transform seat = CreateSeat(new Vector3(2f, 0.41f, 0f));
            Assert.That(
                seated.Presentation.TrySeat(seat, seated.Archetype.SeatedRide),
                Is.True,
                "The kettle design declares a seated ride.");

            var walkers = new[] { idle, walking, seated };
            var peakTilt = new float[walkers.Length];
            Vector3 firstHead = Vector3.zero;
            float headTravel = 0f;
            for (int sample = 0; sample < BoilSampleCount; sample++)
            {
                idle.Presentation.Advance(SampleDeltaTime, false, true);
                walking.Presentation.Advance(SampleDeltaTime, true, true);
                seated.Presentation.Advance(SampleDeltaTime, false, true);
                yield return null;
                for (int index = 0; index < walkers.Length; index++)
                {
                    peakTilt[index] = Mathf.Max(
                        peakTilt[index],
                        walkers[index].PivotTiltDegrees);
                    AssertLidCentrePreserved(walkers[index]);
                }

                Vector3 head = walking.Head.position;
                if (sample == 0)
                {
                    firstHead = head;
                }
                else
                {
                    headTravel = Mathf.Max(
                        headTravel,
                        Vector3.Distance(head, firstHead));
                }
            }

            Assert.That(seated.Presentation.IsSeated, Is.True);
            Assert.That(
                headTravel,
                Is.GreaterThan(MinimumWalkHeadTravel),
                "The walking head never moved: the samples describe a " +
                "static pose and prove nothing about the lid riding it.");
            string[] names = { "idle", "walk", "seated" };
            for (int index = 0; index < walkers.Length; index++)
            {
                Assert.That(
                    peakTilt[index],
                    Is.GreaterThan(MinimumIdleTilt),
                    $"The lid stopped moving in {names[index]}.");
            }
        }

        [UnityTest]
        public IEnumerator PoolRelease_ClearsTheSteamAndSeatsTheLid()
        {
            CreateCamera();
            KettleWalker walker = CreateWalker(Vector3.zero);
            for (int sample = 0; sample < SteamWarmupSamples; sample++)
            {
                walker.Presentation.Advance(SampleDeltaTime, false, true);
                yield return null;
            }

            AssertSteamEmits(walker);

            walker.Root.SetActive(false);
            yield return null;
            Assert.That(walker.Effect.Steam.particleCount, Is.EqualTo(0));
            Assert.That(walker.Effect.Steam.isPlaying, Is.False);
            AssertPivotAtRest(walker);

            walker.Root.SetActive(true);
            float peakTilt = 0f;
            for (int sample = 0; sample < RebindSamples; sample++)
            {
                walker.Presentation.Advance(SampleDeltaTime, false, true);
                yield return null;
                peakTilt = Mathf.Max(peakTilt, walker.PivotTiltDegrees);
            }

            Assert.That(walker.Effect.Steam.isPlaying, Is.True);
            Assert.That(
                peakTilt,
                Is.GreaterThan(0f),
                "The lid did not resume after the rebind.");
            AssertSteamOnTheSpout(walker);
            AssertSteamEmits(walker);
        }

        [UnityTest]
        public IEnumerator Cabin_ClampsTheSteamUnderTheRoof()
        {
            CreateCamera();
            KettleWalker walker = CreateWalker(Vector3.zero);
            Transform seat = CreateSeat(new Vector3(0f, 0.41f, 0f));
            walker.Presentation.Advance(SampleDeltaTime, false, true);
            yield return null;
            Assert.That(
                walker.Effect.Steam.main.simulationSpace,
                Is.EqualTo(ParticleSystemSimulationSpace.World));

            Assert.That(
                walker.Presentation.TrySeat(
                    seat,
                    walker.Archetype.SeatedRide),
                Is.True);
            walker.Presentation.Advance(SampleDeltaTime, false, true);
            yield return null;
            Assert.That(walker.Effect.IsInCabin, Is.True);
            Assert.That(
                walker.Effect.Steam.main.simulationSpace,
                Is.EqualTo(ParticleSystemSimulationSpace.Local));
            Assert.That(
                walker.Effect.Steam.velocityOverLifetime.y.constantMax,
                Is.LessThanOrEqualTo(CityKettleHatBoilEffect.CabinRiseMaximum));
            Assert.That(walker.Effect.Steam.isPlaying, Is.True);

            walker.Presentation.ClearSeat();
            walker.Presentation.Advance(SampleDeltaTime, false, true);
            yield return null;
            Assert.That(walker.Effect.IsInCabin, Is.False);
            Assert.That(
                walker.Effect.Steam.main.simulationSpace,
                Is.EqualTo(ParticleSystemSimulationSpace.World));
            Assert.That(
                walker.Effect.Steam.velocityOverLifetime.y.constantMax,
                Is.EqualTo(CityKettleHatBoilEffect.StreetRiseMaximum)
                    .Within(SpoutTolerance));
            Assert.That(walker.Effect.Steam.isPlaying, Is.True);
        }

        private static void AssertLidCentrePreserved(KettleWalker walker)
        {
            Vector3 centre = walker.Anchors.LidCentreLocal;
            float displaced = Vector3.Distance(
                walker.Effect.LidPivot.TransformPoint(centre),
                walker.Head.TransformPoint(centre));
            Assert.That(
                displaced,
                Is.EqualTo(walker.Effect.LastLidLift).Within(CentreTolerance),
                "The lid's centre drifted off the kettle axis: the pivot " +
                "is rotating about something other than the lid.");
        }

        private static void AssertSteamOnTheSpout(KettleWalker walker)
        {
            ParticleSystem steam = walker.Effect.Steam;
            Assert.That(steam, Is.Not.Null, "No steam was created in play.");
            Assert.That(
                Vector3.Distance(
                    steam.transform.position,
                    walker.Effect.SpoutAnchor.position),
                Is.LessThanOrEqualTo(SpoutTolerance),
                "The steam host left the spout mouth.");
            Assert.That(steam.isPlaying, Is.True);
        }

        /// <summary>
        /// Fast-forwards the playing system by a fixed span from its current
        /// state, proves it produced particles, and lets it play on. The
        /// span is real particle time, so the proof does not depend on how
        /// fast the batch runner turns frames.
        /// </summary>
        private static void AssertSteamEmits(KettleWalker walker)
        {
            ParticleSystem steam = walker.Effect.Steam;
            Assert.That(steam, Is.Not.Null, "No steam was created in play.");
            Assert.That(steam.isPlaying, Is.True);
            steam.Simulate(
                SteamProofSeconds,
                withChildren: true,
                restart: false,
                fixedTimeStep: true);
            Assert.That(
                steam.particleCount,
                Is.GreaterThan(0),
                "The kettle emits nothing.");
            steam.Play(true);
        }

        private static void AssertPivotAtRest(KettleWalker walker)
        {
            Transform pivot = walker.Effect.LidPivot;
            Assert.That(
                Vector3.Distance(pivot.position, walker.Head.position),
                Is.LessThanOrEqualTo(RestDistanceTolerance));
            Assert.That(
                Quaternion.Angle(pivot.rotation, walker.Head.rotation),
                Is.LessThanOrEqualTo(RestAngleTolerance));
        }

        private void CreateCamera()
        {
            // The Animator is left on AlwaysAnimate below, but a camera is
            // still part of reproducing the runtime the effect lives in.
            var cameraObject = new GameObject("Kettle Boil Camera");
            owned.Add(cameraObject);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 1.4f, -5f);
            camera.transform.LookAt(new Vector3(0f, 1.4f, 0f));
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 50f;
        }

        private Transform CreateSeat(Vector3 position)
        {
            var seat = new GameObject("Kettle Boil Seat");
            owned.Add(seat);
            seat.transform.position = position;
            return seat.transform;
        }

        private KettleWalker CreateWalker(Vector3 position)
        {
            Assert.That(
                CityPedestrianResources.TryGetArchetype(
                    CityPedestrianResources.KettleHatDesignId,
                    out CityPedestrianArchetype archetype),
                Is.True);
            Assert.That(archetype.CarriesBoilingKettle, Is.True);
            GameObject prefab = CityPedestrianResources.LoadPrefab(archetype);
            Assert.That(prefab, Is.Not.Null);
            if (prefab.GetComponent<CityKettleHatRigAnchors>() == null)
            {
                Assert.Ignore(
                    "The Kettle Hat prefab carries no " +
                    "CityKettleHatRigAnchors yet: rebuild the pedestrian " +
                    "prefabs (NpcHumanV2AssetSetup.RunBatch) before this " +
                    "test means anything.");
            }

            var root = new GameObject("Kettle Boil Root");
            owned.Add(root);
            root.transform.position = position;
            GameObject instance = Object.Instantiate(
                prefab,
                root.transform,
                false);
            instance.transform.localPosition = Vector3.zero;
            CityPedestrianAssetRegistry registry =
                instance.GetComponent<CityPedestrianAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            CityKettleHatRigAnchors anchors =
                instance.GetComponent<CityKettleHatRigAnchors>();
            Assert.That(anchors, Is.Not.Null);
            Assert.That(anchors.LidPivot, Is.Not.Null);
            Assert.That(anchors.SpoutAnchor, Is.Not.Null);
            Assert.That(
                anchors.LidPivot.parent,
                Is.SameAs(registry.HeadAnchor));

            CityPedestrianPresentation presentation =
                instance.AddComponent<CityPedestrianPresentation>();
            presentation.Initialize(registry);
            registry.Animator.cullingMode =
                AnimatorCullingMode.AlwaysAnimate;
            CityKettleHatBoilEffect effect =
                instance.AddComponent<CityKettleHatBoilEffect>();
            effect.Initialize(presentation, anchors, EffectSeed);
            Assert.That(effect.IsInitialized, Is.True);
            return new KettleWalker(
                instance,
                registry,
                anchors,
                presentation,
                effect,
                archetype);
        }

        private sealed class KettleWalker
        {
            public KettleWalker(
                GameObject root,
                CityPedestrianAssetRegistry registry,
                CityKettleHatRigAnchors anchors,
                CityPedestrianPresentation presentation,
                CityKettleHatBoilEffect effect,
                CityPedestrianArchetype archetype)
            {
                Root = root;
                Registry = registry;
                Anchors = anchors;
                Presentation = presentation;
                Effect = effect;
                Archetype = archetype;
            }

            public GameObject Root { get; }
            public CityPedestrianAssetRegistry Registry { get; }
            public CityKettleHatRigAnchors Anchors { get; }
            public CityPedestrianPresentation Presentation { get; }
            public CityKettleHatBoilEffect Effect { get; }
            public CityPedestrianArchetype Archetype { get; }
            public Transform Head => Registry.HeadAnchor;

            public float PivotTiltDegrees => Quaternion.Angle(
                Quaternion.identity,
                Effect.LidPivot.localRotation);
        }
    }
}
