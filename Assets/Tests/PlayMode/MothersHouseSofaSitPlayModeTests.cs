using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The hero walks to his mother's sofa, sits down, and gets up again.
    ///
    /// A separate file from `MothersHouseInteriorPlayModeTests` on purpose:
    /// that one is carrying live uncommitted work, and its own assertions
    /// (the exact contents of `World.GameplayColliders`) are what this
    /// feature must leave alone rather than join.
    /// </summary>
    public sealed class MothersHouseSofaSitPlayModeTests
    {
        private const string InteriorRootName =
            "[Bar Promenade] Mother's House Interior Runtime";
        private const float TimeoutSeconds = 60f;

        /// <summary>
        /// The walk plus the enter clip: 36 frames at 12 fps is 3.0 s, the
        /// guided walk is 2.394 m, and the clock is pinned at 1/60. A FRAME
        /// budget and not a realtime deadline - batch mode runs frames as
        /// fast as it can, so "wait two seconds" means nothing here.
        /// </summary>
        private const int SitFrameBudget = 900;

        private const int StandFrameBudget = 480;

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
        public IEnumerator HeroSitsOnTheSofaAndStandsBackUp()
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

            // Every authored seat constant in this feature is world space,
            // which is only true while the room's own root sits at identity.
            Assert.That(
                interior.Room.position,
                Is.EqualTo(Vector3.zero));

            CityBenchSitInteraction sofa = interior.Sofa;
            Assert.That(sofa, Is.Not.Null, "The room built no sofa seat.");
            Assert.That(interior.Seats, Has.Count.EqualTo(1));
            Assert.That(
                sofa.PromptKey,
                Is.EqualTo("interaction.sit_sofa"));

            CityBenchSitPlan plan = sofa.Plan;
            PlayerInteractor interactor = interior.Player.Interactor;

            // NOT teleported to the dock: the guided walk over the real
            // 2.394 m is the thing under test.
            Assert.That(
                sofa.CanInteract(interactor),
                Is.True,
                "The sofa must be offered from the room's own spawn.");

            bool previousShadow =
                interior.Player.ContactShadow != null &&
                interior.Player.ContactShadow.enabled;

            sofa.Interact(interactor);

            PlayerAnimatedInteractionController controller = sofa.Controller;
            int frames = 0;
            while (controller.Phase !=
                   PlayerAnimatedInteractionPhase.Looping &&
                   frames < SitFrameBudget)
            {
                frames++;
                yield return null;
            }

            Assert.That(
                controller.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Looping),
                $"The hero never settled onto the sofa in {frames} frames " +
                $"(phase {controller.Phase}). A blocked walk aborts with a " +
                "warning; a bad dock height stalls in silence.");

            Assert.That(sofa.IsSeated, Is.True);
            Assert.That(
                sofa.PromptKey,
                Is.EqualTo(CityBenchSitInteraction.StandPromptKey));

            Transform root = interior.Player.GameObject.transform;
            Assert.That(
                Vector3.Distance(root.position, plan.EntryRootPosition),
                Is.LessThan(PlayerMotor.InteractionPositionTolerance),
                "The capsule stays on the dock for the whole interaction; " +
                "only the drawn body is carried onto the cushion.");

            var presentation =
                interior.Player.Visual as Player3DCharacterPresentation;
            Assert.That(presentation, Is.Not.Null);
            Vector3 pelvis =
                presentation.Registry.Anchors.Pelvis.position;
            Assert.That(
                Vector3.Distance(pelvis, plan.ActionHipPosition),
                Is.LessThan(0.02f),
                $"The seated pelvis landed at {pelvis} against the " +
                $"authored {plan.ActionHipPosition}.");

            // The visible half of "the capsule never moved": without this
            // every seated bench in the game paints an oval on the floor
            // three quarters of a metre in front of the sitter.
            Assert.That(
                interior.Player.ContactShadow.enabled,
                Is.False,
                "The contact shadow must stop drawing while he is seated.");

            // Clause 6: no cut. The room keeps its own fixed shot.
            Assert.That(
                interior.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));

            Assert.That(interior.Player.Motor.InputEnabled, Is.False);
            Assert.That(interactor.InputEnabled, Is.True,
                "The stand-up press has to stay reachable.");

            // The pose, asserted rather than logged - `LogAssert
            // .NoUnexpectedReceived()` below fails on any Debug.Log, even
            // an informative one.
            //
            // MEASURED, not guessed: pelvis (-2.260, 0.600, -0.600) exactly
            // on the authored point, feet at y 0.168 and 0.146. The feet
            // hang because these are the BUS passenger clips, which every
            // seat in the game wears - the mountain brink bench, the park
            // bench and the discarded couch all sit the same way. The band
            // is stated so a re-authored clip or a moved cushion fails here
            // instead of shipping a hero perched in the air.
            float leftFootY =
                presentation.Registry.Anchors.LeftFoot.position.y;
            float rightFootY =
                presentation.Registry.Anchors.RightFoot.position.y;
            Assert.That(
                leftFootY,
                Is.InRange(0.05f, 0.30f),
                $"The left foot sits at y {leftFootY:0.###}.");
            Assert.That(
                rightFootY,
                Is.InRange(0.05f, 0.30f),
                $"The right foot sits at y {rightFootY:0.###}.");
            Assert.That(
                pelvis.y - Mathf.Max(leftFootY, rightFootY),
                Is.GreaterThan(0.25f),
                "He must be sitting on the cushion, not standing on it.");

            // Stand up. A FIXED window, never `while (sofa.IsSeated)`:
            // the seat leaves Looping on the same frame the exit is
            // requested, so that loop never runs and every assertion inside
            // it passes against nothing.
            Assert.That(sofa.CanInteract(interactor), Is.True);
            sofa.Interact(interactor);

            frames = 0;
            while (controller.Phase !=
                   PlayerAnimatedInteractionPhase.Idle &&
                   frames < StandFrameBudget)
            {
                frames++;
                yield return null;
            }

            Assert.That(
                controller.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Idle),
                $"The hero never stood back up in {frames} frames.");
            Assert.That(sofa.IsSeated, Is.False);
            Assert.That(
                Vector3.Distance(root.position, plan.EntryRootPosition),
                Is.LessThan(PlayerMotor.InteractionPositionTolerance));
            Assert.That(interior.Player.Motor.InputEnabled, Is.True);
            Assert.That(
                interior.Player.ContactShadow.enabled,
                Is.EqualTo(previousShadow),
                "The contact shadow must come back exactly as it was.");
            Assert.That(
                interior.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// The specific defect this feature was designed around: behind the
        /// sofa there is 0.425 m against a 0.64 m capsule, so a walk routed
        /// there can only stall. The offer must not reach those pockets.
        /// </summary>
        [UnityTest]
        public IEnumerator SofaIsNotOfferedFromBehindOrBesideTheStair()
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

            CityBenchSitInteraction sofa = interior.Sofa;
            Assert.That(sofa, Is.Not.Null);

            Vector3[] pockets =
            {
                new Vector3(-2.8f, 0.04f, 1.4f),
                new Vector3(-2.8f, 0.04f, -1.8f)
            };

            for (int index = 0; index < pockets.Length; index++)
            {
                interior.Player.Motor.Teleport(pockets[index]);
                Physics.SyncTransforms();
                yield return null;

                Assert.That(
                    sofa.CanInteract(interior.Player.Interactor),
                    Is.False,
                    $"The sofa is offered from {pockets[index]}, where the " +
                    "walk to it can only stall against the stair ramp.");
            }

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// Clause 8: a cancel restores everything it took. `InteractionCompleted`
        /// does not fire on one, so the restore has to hang off the phase.
        /// </summary>
        [UnityTest]
        public IEnumerator CancellingMidEnterRestoresTheHero()
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

            CityBenchSitInteraction sofa = interior.Sofa;
            bool previousShadow = interior.Player.ContactShadow.enabled;
            sofa.Interact(interior.Player.Interactor);

            int frames = 0;
            while (sofa.Controller.Phase !=
                   PlayerAnimatedInteractionPhase.Entering &&
                   frames < SitFrameBudget)
            {
                frames++;
                yield return null;
            }

            Assert.That(
                sofa.Controller.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Entering));

            sofa.enabled = false;
            yield return null;

            Assert.That(
                sofa.Controller.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Idle));
            Assert.That(sofa.IsSeated, Is.False);
            Assert.That(
                interior.Player.ContactShadow.enabled,
                Is.EqualTo(previousShadow));
            Assert.That(interior.Player.Motor.InputEnabled, Is.True);

            LogAssert.NoUnexpectedReceived();
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
