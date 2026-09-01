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
    /// The mother is in her chair when the room opens, her hips are on the
    /// cushion, and the chair is rocking.
    ///
    /// A separate file from `MothersHouseInteriorPlayModeTests` for the same
    /// reason the sofa test is: that one pins the exact contents of
    /// `World.GameplayColliders` and the room's renderer count, and this
    /// feature's job is to leave both untouched rather than to join them.
    /// </summary>
    public sealed class MothersHouseMotherPlayModeTests
    {
        private const string InteriorRootName =
            "[Bar Promenade] Mother's House Interior Runtime";
        private const float TimeoutSeconds = 60f;

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = 1f / 60f;
            GameSessionState.BeginNewGame();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
        }

        [UnityTest]
        public IEnumerator SheIsSeatedAndTheChairRocks()
        {
            MothersHouseInteriorRoot interior = null;
            yield return LoadRoom(found => interior = found);

            Assert.That(
                interior.Mother,
                Is.Not.Null,
                "She is present from the first visit.");
            Assert.That(interior.Mother.IsInitialized, Is.True);
            Assert.That(interior.ChairMotion, Is.Not.Null);
            Assert.That(interior.ChairMotion.IsInitialized, Is.True);
            Assert.That(
                interior.ChairMotion.RiderCount,
                Is.EqualTo(3),
                "The frame, the cushion and the woman ride one angle.");

            // THE CULLING TRAP. Batch mode renders nothing, so a rig left on
            // CullUpdateTransforms reads back in its BIND pose and every
            // assertion below would describe a standing A-pose while passing
            // happily. This must come before the first pose is read.
            CityPedestrianAssetRegistry registry =
                interior.Mother.Registry;
            registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            yield return null;
            yield return null;

            Transform room = interior.World.Root;
            Vector3 localPelvis = room.InverseTransformPoint(
                registry.PelvisAnchor.position);
            Assert.That(
                localPelvis.y,
                Is.EqualTo(
                        MothersHouseMotherPresentation.CushionTopY +
                        MothersHouseMotherPresentation.PerchPelvisLiftMeters)
                    .Within(0.02f),
                "Her hips must sit on the drawn cushion, not in it.");
            Assert.That(
                localPelvis.x,
                Is.InRange(-0.27f, 0.31f),
                "Her hips must be over the cushion in X.");
            Assert.That(
                localPelvis.z,
                Is.InRange(1.26f, 1.80f),
                "Her hips must be over the cushion in Z.");

            // Her soles reach the boards. The generator measured the seat at
            // 0.5714 m over her own soles against a 0.5700 m cushion, so if
            // the hips are right the feet are too - unless something has
            // moved her vertically since.
            float lowestSole = Mathf.Min(
                room.InverseTransformPoint(
                    registry.LeftFootAnchor.position).y,
                room.InverseTransformPoint(
                    registry.RightFootAnchor.position).y);
            Assert.That(
                lowestSole,
                Is.LessThan(0.25f),
                "She is not sitting with her feet in the air.");

            // SHE FACES THE ROOM, NOT THE HEARTH.
            //
            // Measured on her ROOT, not on the face patch. A skinned
            // renderer's `bounds` is the bind-pose box moved by its own
            // transform - `updateWhenOffscreen` is off for every pedestrian -
            // so it does not follow the animated skull and reported her nose
            // three millimetres BEHIND her head bone, which is neither true
            // nor false, just meaningless.
            Vector3 localForward = room.InverseTransformDirection(
                interior.Mother.transform.forward);
            Assert.That(
                Vector3.Dot(localForward, Vector3.back),
                Is.GreaterThan(0.999f),
                "The chair has its back to the hearth and she faces the " +
                "room; she cannot be turned the other way.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator TheChairKeepsRockingAndCarriesHerWithIt()
        {
            MothersHouseInteriorRoot interior = null;
            yield return LoadRoom(found => interior = found);

            MothersHouseRockingChairMotion motion = interior.ChairMotion;
            CityPedestrianAssetRegistry registry =
                interior.Mother.Registry;
            registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            yield return null;

            float firstAngle = motion.AngleDegrees;
            Vector3 firstHead = registry.HeadAnchor.position;

            // A quarter of the period at a pinned 1/60 clock.
            int frames = Mathf.CeilToInt(
                MothersHouseRockingChairMotion.PeriodSeconds * 0.25f * 60f);
            for (int index = 0; index < frames; index++)
            {
                yield return null;
            }

            Assert.That(
                Mathf.Abs(motion.AngleDegrees - firstAngle),
                Is.GreaterThan(0.2f),
                "The chair must actually be moving.");
            Assert.That(
                Mathf.Abs(motion.AngleDegrees),
                Is.LessThanOrEqualTo(
                    MothersHouseRockingChairMotion.AmplitudeDegrees + 0.001f),
                "The rock must stay inside its own amplitude.");

            // SHE RIDES IT. The whole design is one angle moving both, so her
            // head must travel with the timber rather than hang still while
            // the chair swings out from under her.
            Assert.That(
                Vector3.Distance(registry.HeadAnchor.position, firstHead),
                Is.GreaterThan(0.005f),
                "She must move with the chair, not sit through it.");
        }

        [UnityTest]
        public IEnumerator SheAddsNoCollisionNoAudioAndNoPrompt()
        {
            MothersHouseInteriorRoot interior = null;
            yield return LoadRoom(found => interior = found);

            GameObject instance = interior.Mother.gameObject;
            Assert.That(
                instance.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "The chair's own blocker already stands there.");
            Assert.That(
                instance.GetComponentsInChildren<AudioSource>(true),
                Is.Empty,
                "The room holds exactly three, and she is silent by canon.");
            Assert.That(
                instance.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                instance.GetComponentsInChildren<IInteractable>(true),
                Is.Empty,
                "The hero's reaction to his mother is not written.");

            // Her expression is set once and nothing ever changes it. The
            // atlas ships complete and undriven, exactly as the stairwell
            // cat's grin ships with no scheduler.
            Assert.That(
                interior.Mother.Expression,
                Is.EqualTo(PlayerFacialExpression.Neutral));

            LogAssert.NoUnexpectedReceived();
            yield return null;
        }

        private static IEnumerator LoadRoom(
            Action<MothersHouseInteriorRoot> capture)
        {
            MothersHouseInteriorRoot interior = null;
            yield return LoadSceneAndWaitForRoot<MothersHouseInteriorRoot>(
                SceneIds.MothersHouseInterior,
                InteriorRootName,
                found => interior = found);
            yield return WaitUntil(
                () => interior.IsInitialized &&
                      !SceneTransitionService.IsTransitioning,
                "The mother's house never finished initializing.");
            capture(interior);
        }

        private static IEnumerator LoadSceneAndWaitForRoot<T>(
            string sceneName,
            string exactRootName,
            Action<T> capture)
            where T : Component
        {
            Assert.That(
                Application.CanStreamedLevelBeLoaded(sceneName),
                Is.True,
                $"Scene '{sceneName}' must be enabled in Build Settings.");
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            yield return WaitUntil(
                () => operation.isDone,
                $"Scene '{sceneName}' did not load.");

            T found = null;
            yield return WaitUntil(
                () =>
                {
                    Scene scene = SceneManager.GetActiveScene();
                    found = FindExactRoot<T>(scene, exactRootName);
                    return scene.name == sceneName && found != null;
                },
                $"Scene '{sceneName}' did not create root " +
                $"'{exactRootName}'.");
            capture(found);
        }

        private static IEnumerator WaitUntil(
            Func<bool> predicate,
            string failureMessage)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!predicate() &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, failureMessage);
        }

        private static T FindExactRoot<T>(
            Scene scene,
            string exactRootName)
            where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                if (roots[index].name == exactRootName)
                {
                    return roots[index].GetComponent<T>();
                }
            }

            return null;
        }
    }
}
