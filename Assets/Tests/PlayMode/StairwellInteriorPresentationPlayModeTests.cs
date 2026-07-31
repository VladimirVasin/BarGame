using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class
        StairwellInteriorPresentationPlayModeTests
    {
        private const string RootName =
            "[Bar Promenade] Stairwell Interior Runtime";
        private const float TimeoutSeconds = 15f;
        private InputTestFixture inputFixture;
        private Keyboard keyboard;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            inputFixture = new InputTestFixture();
            inputFixture.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            GameSessionState.EnterBar(null);
            GameSessionState.CompleteCityReturn();
            GameSessionState.ConsumeStairwellArrival();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameSessionState.EnterBar(null);
            GameSessionState.CompleteCityReturn();
            GameSessionState.ConsumeStairwellArrival();
            if (keyboard != null && keyboard.added)
            {
                InputSystem.RemoveDevice(keyboard);
            }

            keyboard = null;
            inputFixture?.TearDown();
            inputFixture = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Scene_BootstrapsVerticalRouteAndAtmosphere()
        {
            GameSessionState.PrepareStairwellArrival(
                StairwellArrivalKind.ApartmentDoor);
            StairwellInteriorRoot root = null;
            yield return LoadSceneAndWaitForRoot(
                value => root = value);
            yield return WaitUntil(
                () => root.IsInitialized,
                "Stairwell root did not initialize.");
            yield return null;

            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(SceneIds.StairwellInterior));
            Assert.That(
                GameAudioMixer.CurrentProfile,
                Is.EqualTo(GameAudioProfile.Stairwell));
            Assert.That(
                root.Arrival,
                Is.EqualTo(
                    StairwellArrivalKind.ApartmentDoor));
            Assert.That(
                GameSessionState.StairwellArrival,
                Is.EqualTo(StairwellArrivalKind.StreetDoor));
            Assert.That(
                Vector3.Distance(
                    root.Player.GameObject.transform.position,
                    root.Layout.ApartmentSpawn),
                Is.LessThan(0.05f));
            Assert.That(
                root.World.StairColliders,
                Has.Count.EqualTo(3));
            Assert.That(root.World.UpperBlocker, Is.Not.Null);
            Assert.That(
                root.World.UpperBlocker.enabled,
                Is.True);
            Assert.That(
                root.World.UpperBlocker.isTrigger,
                Is.False);
            Assert.That(
                root.World.UpperBlocker.bounds.min.x,
                Is.LessThanOrEqualTo(
                    root.Layout.UpperFlightBounds.xMin));
            Assert.That(
                root.World.UpperBlocker.bounds.max.x,
                Is.GreaterThanOrEqualTo(
                    root.Layout.UpperFlightBounds.xMax));

            Assert.That(root.StreetExit, Is.Not.Null);
            Assert.That(
                root.StreetExit.PromptKey,
                Is.EqualTo("interaction.exit_building"));
            Assert.That(root.ApartmentEntrance, Is.Not.Null);
            Assert.That(
                root.ApartmentEntrance.PromptKey,
                Is.EqualTo("interaction.enter_apartment"));
            Assert.That(
                GameSessionState.ReturnKind,
                Is.EqualTo(CityReturnKind.None));

            Assert.That(root.Atmosphere, Is.Not.Null);
            Assert.That(
                root.Atmosphere.IsInitialized,
                Is.True);
            Assert.That(
                root.Atmosphere.PracticalLights,
                Has.Count.EqualTo(
                    StairwellInteriorAtmosphere
                        .MaximumPracticalLights));
            Assert.That(
                root.Atmosphere.Dust.main.maxParticles,
                Is.EqualTo(
                    StairwellInteriorAtmosphere
                        .MaximumDustParticles));
            Assert.That(
                root.Atmosphere.GetComponentsInChildren<
                    StairwellLightFlicker>(),
                Has.Length.EqualTo(3));
            AssertPracticalSources(root);
            Assert.That(root.Ambience, Is.Not.Null);
            Assert.That(root.Ambience.Source.loop, Is.True);
            Assert.That(
                root.Ambience.ActiveClip.name,
                Is.EqualTo("RetroAmbience_Stairwell"));
            Assert.That(root.Soundscape, Is.Not.Null);
            Assert.That(root.Soundscape.IsInitialized, Is.True);
            Assert.That(
                root.Soundscape.GetComponentsInChildren<
                    AudioSource>(true),
                Has.Length.EqualTo(
                    StairwellSoundscape.OwnedSourceCount));
            Assert.That(
                root.GetComponentsInChildren<AudioSource>(true),
                Has.Length.EqualTo(
                    2 +
                    StairwellSoundscape.OwnedSourceCount),
                "Stairwell audio must remain one music source, " +
                "one base ambience source and three soundscape " +
                "sources.");
            Assert.That(root.Music, Is.Not.Null);
            Assert.That(root.Music.Source, Is.Not.Null);
            Assert.That(root.Music.Source.loop, Is.True);
            Assert.That(
                root.Music.Source.playOnAwake,
                Is.False);
            Assert.That(
                root.Music.Source.spatialBlend,
                Is.Zero);
            Assert.That(root.Music.ToneFilter, Is.Not.Null);
            Assert.That(
                root.Music.transform.IsChildOf(root.transform),
                Is.True);
            Assert.That(
                StairwellMusicPlayer.ResourcePath,
                Is.EqualTo(
                    "Audio/StairwellMusic/" +
                    "stairwell_theme"));
            Assert.That(root.Cat, Is.Not.Null);
            Assert.That(root.Cat.IsInitialized, Is.True);
            Assert.That(root.Cat.Renderer, Is.Not.Null);
            Assert.That(root.Cat.Renderer.sprite, Is.Not.Null);
            Assert.That(root.Cat.Billboard, Is.Not.Null);
            Assert.That(
                root.Cat.Billboard.CameraPlaneAlignmentEnabled,
                Is.True);
            Assert.That(root.CatInteraction, Is.Not.Null);
            Assert.That(
                root.CatInteraction.IsInitialized,
                Is.True);
            Assert.That(root.CatTrigger, Is.Not.Null);
            Assert.That(root.CatTrigger.isTrigger, Is.True);
            Assert.That(
                root.Cat.transform.localPosition,
                Is.EqualTo(root.CatPlan.VisualLocalPosition));
            Assert.That(
                root.CatInteraction.InteractionPosition,
                Is.EqualTo(
                    root.transform.TransformPoint(
                        root.CatPlan.InteractionLocalPosition)));
            Assert.That(
                StairwellCatSpriteLibrary.DefaultResourcePath,
                Is.EqualTo(
                    "Stairwell/Cat/StairwellCatAtlas"));

            Assert.That(root.CameraFollow, Is.Not.Null);
            Assert.That(root.FixedCamera, Is.Not.Null);
            Assert.That(
                root.FixedCamera.IsInitialized,
                Is.True);
            Assert.That(
                root.CameraFollow.FixedPoseActive,
                Is.True);
            Assert.That(
                root.FixedCamera.ActiveShotKind,
                Is.EqualTo(
                    StairwellCameraShotKind
                        .ApartmentLanding));
            AssertEmitterVisible(
                Camera.main,
                root.World.Root.Find(
                    "Stairwell Dressing/" +
                    "Apartment Fluorescent Tube"));
            AssertCatVisible(Camera.main, root.Cat);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<Camera>(
                    FindObjectsInactive.Exclude),
                Has.Length.EqualTo(1));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<AudioListener>(
                    FindObjectsInactive.Exclude),
                Has.Length.EqualTo(1));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<
                    CityMusicPlayer>(
                    FindObjectsInactive.Include),
                Is.Empty);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<
                    BarMusicPlayer>(
                    FindObjectsInactive.Include),
                Is.Empty);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<
                    StairwellMusicPlayer>(
                    FindObjectsInactive.Include),
                Has.Length.EqualTo(1));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<
                    StairwellCatActor>(
                    FindObjectsInactive.Include),
                Has.Length.EqualTo(1));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<
                    StairwellCatInteraction>(
                    FindObjectsInactive.Include),
                Has.Length.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator
            Scene_PlayerMotorCrossesGroundFloorSeamWithForwardInput()
        {
            GameSessionState.PrepareStairwellArrival(
                StairwellArrivalKind.StreetDoor);
            StairwellInteriorRoot root = null;
            yield return LoadSceneAndWaitForRoot(
                value => root = value);
            yield return WaitUntil(
                () => root.IsInitialized,
                "Stairwell root did not initialize.");
            yield return null;

            Assert.That(
                root.FixedCamera.ActiveShotKind,
                Is.EqualTo(
                    StairwellCameraShotKind.GroundFlight));
            AssertCatVisible(Camera.main, root.Cat);
            Vector3 start =
                root.Player.GameObject.transform.position;
            inputFixture.Press(
                keyboard.wKey,
                queueEventOnly: true);
            float deadline =
                Time.realtimeSinceStartup + 4f;
            while (root.Player.GameObject.transform.position.y <
                       0.62f &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            inputFixture.Release(
                keyboard.wKey,
                queueEventOnly: true);
            yield return null;

            Vector3 reached =
                root.Player.GameObject.transform.position;
            Assert.That(
                reached.z,
                Is.GreaterThan(start.z + 1.2f),
                "PlayerMotor did not cross the lobby/flight seam.");
            Assert.That(
                reached.y,
                Is.GreaterThan(0.60f),
                "PlayerMotor reached the first flight but did not climb it.");
        }

        [UnityTest]
        public IEnumerator
            Scene_PlayerClimbsToApartmentButUpperDebrisBlocks()
        {
            GameSessionState.PrepareStairwellArrival(
                StairwellArrivalKind.StreetDoor);
            StairwellInteriorRoot root = null;
            yield return LoadSceneAndWaitForRoot(
                value => root = value);
            yield return WaitUntil(
                () => root.IsInitialized,
                "Stairwell root did not initialize.");
            yield return null;

            root.Player.Motor.SetInputEnabled(false);
            root.Player.Motor.enabled = false;
            CharacterController controller =
                root.Player.GameObject.GetComponent<
                    CharacterController>();
            Assert.That(controller, Is.Not.Null);
            AssertEmitterVisible(
                Camera.main,
                root.World.Root.Find(
                    "Stairwell Dressing/" +
                    "Ground Fluorescent Tube"));

            yield return MoveControllerTo(
                controller,
                new Vector2(-1.45f, -2.92f));
            yield return MoveControllerTo(
                controller,
                new Vector2(-1.45f, 0.70f));
            Assert.That(
                controller.transform.position.y,
                Is.GreaterThan(1.42f));

            yield return MoveControllerTo(
                controller,
                new Vector2(1.45f, 1.28f));
            Assert.That(
                root.FixedCamera.ActiveShotKind,
                Is.EqualTo(
                    StairwellCameraShotKind.MiddleFlight));
            AssertCatVisible(Camera.main, root.Cat);
            AssertEmitterVisible(
                Camera.main,
                root.World.Root.Find(
                    "Stairwell Dressing/" +
                    "Middle Fluorescent Tube"));
            yield return MoveControllerTo(
                controller,
                new Vector2(1.45f, 0.70f));
            yield return MoveControllerTo(
                controller,
                new Vector2(1.45f, -2.92f));
            Assert.That(
                controller.transform.position.y,
                Is.GreaterThan(3.0f));

            yield return MoveControllerTo(
                controller,
                new Vector2(3.12f, -3.52f));
            Assert.That(
                root.FixedCamera.ActiveShotKind,
                Is.EqualTo(
                    StairwellCameraShotKind
                        .ApartmentLanding));
            AssertCatVisible(Camera.main, root.Cat);
            AssertEmitterVisible(
                Camera.main,
                root.World.Root.Find(
                    "Stairwell Dressing/" +
                    "Apartment Fluorescent Tube"));
            Assert.That(
                Vector2.Distance(
                    new Vector2(
                        controller.transform.position.x,
                        controller.transform.position.z),
                    new Vector2(3.12f, -3.52f)),
                Is.LessThan(0.16f));

            yield return MoveControllerTo(
                controller,
                new Vector2(-1.45f, -3.52f));
            yield return MoveControllerTo(
                controller,
                new Vector2(-1.45f, -2.96f));
            for (int index = 0; index < 90; index++)
            {
                controller.Move(
                    new Vector3(0f, -0.08f, 0.055f));
                yield return null;
            }

            Bounds blocker =
                root.World.UpperBlocker.bounds;
            Assert.That(
                controller.transform.position.z,
                Is.LessThanOrEqualTo(
                    blocker.min.z -
                    controller.radius +
                    0.08f),
                "The player squeezed through the upper debris.");
        }

        [UnityTest]
        public IEnumerator
            Scene_CatInteractionShowsPlaceholderWithoutLockingPlayer()
        {
            GameSessionState.PrepareStairwellArrival(
                StairwellArrivalKind.StreetDoor);
            StairwellInteriorRoot root = null;
            yield return LoadSceneAndWaitForRoot(
                value => root = value);
            yield return WaitUntil(
                () => root.IsInitialized,
                "Stairwell root did not initialize.");

            root.Player.Motor.Teleport(
                root.transform.TransformPoint(
                    root.CatPlan.InteractionLocalPosition));
            Physics.SyncTransforms();
            yield return WaitUntil(
                () => ReferenceEquals(
                    root.Player.Interactor.ActiveInteractable,
                    root.CatInteraction),
                "The cat did not become the active interaction.");

            InteractionPromptView prompt =
                root.GetComponentInChildren<
                    InteractionPromptView>();
            Assert.That(prompt, Is.Not.Null);
            Assert.That(
                prompt.PromptKey,
                Is.EqualTo(
                    StairwellCatInteraction.DefaultPromptKey));
            Assert.That(prompt.IsClickable, Is.True);

            Assert.That(prompt.TryInvokePrompt(), Is.True);
            yield return null;

            Assert.That(
                root.CatInteraction.PromptKey,
                Is.EqualTo(
                    StairwellCatInteraction.ResponsePromptKey));
            Assert.That(
                prompt.PromptKey,
                Is.EqualTo(
                    StairwellCatInteraction.ResponsePromptKey));
            Assert.That(root.Player.Motor.InputEnabled, Is.True);
            Assert.That(root.Player.Interactor.InputEnabled, Is.True);
            Assert.That(
                root.CatInteraction.GetPromptKeyAt(
                    Time.unscaledTime +
                    StairwellCatInteraction
                        .ResponseDurationSeconds),
                Is.EqualTo(
                    StairwellCatInteraction.DefaultPromptKey));
        }

        private static void AssertPracticalSources(
            StairwellInteriorRoot root)
        {
            string[] labels =
            {
                "Ground",
                "Middle",
                "Apartment"
            };
            for (int index = 0; index < labels.Length; index++)
            {
                string prefix =
                    "Stairwell Dressing/" + labels[index];
                Transform tube = root.World.Root.Find(
                    prefix + " Fluorescent Tube");
                Transform housing = root.World.Root.Find(
                    prefix + " Fluorescent Housing");
                Transform halo = root.World.Root.Find(
                    prefix + " Fluorescent Halo");
                Assert.That(tube, Is.Not.Null);
                Assert.That(housing, Is.Not.Null);
                Assert.That(halo, Is.Not.Null);
                Renderer tubeRenderer =
                    tube.GetComponent<Renderer>();
                Renderer housingRenderer =
                    housing.GetComponent<Renderer>();
                Assert.That(tubeRenderer.enabled, Is.True);
                Assert.That(
                    housingRenderer.bounds.min.y,
                    Is.GreaterThan(
                        tubeRenderer.bounds.max.y),
                    $"{labels[index]} fixture housing hides its tube.");
                Assert.That(
                    halo.GetComponent<CityLightHalo>(),
                    Is.Not.Null);

                Light light =
                    root.Atmosphere.PracticalLights[index];
                Assert.That(light.enabled, Is.True);
                Assert.That(
                    light.type,
                    Is.EqualTo(LightType.Point));
                Assert.That(
                    light.intensity,
                    Is.GreaterThanOrEqualTo(4.0f));
                Assert.That(
                    light.range,
                    Is.GreaterThanOrEqualTo(5.8f));
                Assert.That(
                    Vector3.Distance(
                        light.transform.position,
                        tube.position),
                    Is.LessThan(0.22f));
            }
        }

        private static void AssertEmitterVisible(
            Camera camera,
            Transform emitter)
        {
            Assert.That(camera, Is.Not.Null);
            Assert.That(emitter, Is.Not.Null);
            Vector3 viewport =
                camera.WorldToViewportPoint(emitter.position);
            Assert.That(viewport.z, Is.GreaterThan(0f));
            Assert.That(
                viewport.x,
                Is.InRange(0.05f, 0.95f));
            Assert.That(
                viewport.y,
                Is.InRange(0.05f, 0.95f));
        }

        private static void AssertCatVisible(
            Camera camera,
            StairwellCatActor cat)
        {
            Assert.That(camera, Is.Not.Null);
            Assert.That(cat, Is.Not.Null);
            Assert.That(cat.Renderer, Is.Not.Null);
            Vector3 target = cat.Renderer.bounds.center;
            Vector3 viewport =
                camera.WorldToViewportPoint(
                    target);
            Assert.That(viewport.z, Is.GreaterThan(0f));
            Assert.That(
                viewport.x,
                Is.InRange(0.05f, 0.95f));
            Assert.That(
                viewport.y,
                Is.InRange(0.05f, 0.95f));

            Vector3 toCat =
                target - camera.transform.position;
            bool occluded = Physics.Raycast(
                camera.transform.position,
                toCat.normalized,
                out RaycastHit hit,
                Mathf.Max(0f, toCat.magnitude - 0.04f),
                ~0,
                QueryTriggerInteraction.Ignore);
            Assert.That(
                occluded,
                Is.False,
                occluded && hit.collider != null
                    ? $"Cat is hidden by {hit.collider.name}."
                    : "Cat is hidden in its fixed-camera shot.");
        }

        private static IEnumerator LoadSceneAndWaitForRoot(
            Action<StairwellInteriorRoot> capture)
        {
            Assert.That(
                Application.CanStreamedLevelBeLoaded(
                    SceneIds.StairwellInterior),
                Is.True,
                "StairwellInterior must be enabled in Build Settings.");
            AsyncOperation operation =
                SceneManager.LoadSceneAsync(
                    SceneIds.StairwellInterior,
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
            deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                Scene scene = SceneManager.GetActiveScene();
                if (scene.name == SceneIds.StairwellInterior)
                {
                    GameObject[] roots =
                        scene.GetRootGameObjects();
                    for (int index = 0;
                         index < roots.Length;
                         index++)
                    {
                        if (roots[index].name != RootName)
                        {
                            continue;
                        }

                        StairwellInteriorRoot root =
                            roots[index].GetComponent<
                                StairwellInteriorRoot>();
                        if (root != null)
                        {
                            capture(root);
                            yield break;
                        }
                    }
                }

                yield return null;
            }

            Assert.Fail(
                "StairwellInterior did not create its runtime root.");
        }

        private static IEnumerator WaitUntil(
            Func<bool> predicate,
            string failureMessage)
        {
            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (!predicate() &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                predicate(),
                Is.True,
                failureMessage);
        }

        private static IEnumerator MoveControllerTo(
            CharacterController controller,
            Vector2 target)
        {
            const int maximumSteps = 180;
            const float stepDistance = 0.055f;
            for (int index = 0;
                 index < maximumSteps;
                 index++)
            {
                Vector3 position =
                    controller.transform.position;
                Vector2 remaining =
                    target -
                    new Vector2(position.x, position.z);
                if (remaining.magnitude < 0.08f)
                {
                    yield break;
                }

                Vector2 planar =
                    Vector2.ClampMagnitude(
                        remaining,
                        stepDistance);
                controller.Move(
                    new Vector3(
                        planar.x,
                        -0.08f,
                        planar.y));
                yield return null;
            }

            Assert.Fail(
                $"Controller did not reach {target} from " +
                $"{controller.transform.position}. " +
                BuildCollisionReport(controller, target));
        }

        private static string BuildCollisionReport(
            CharacterController controller,
            Vector2 target)
        {
            Vector3 origin =
                controller.transform.position +
                Vector3.up * 0.85f;
            Vector3 targetWorld =
                new Vector3(target.x, origin.y, target.y);
            Vector3 direction =
                (targetWorld - origin).normalized;
            RaycastHit[] hits = Physics.SphereCastAll(
                origin,
                controller.radius,
                direction,
                2f,
                ~0,
                QueryTriggerInteraction.Ignore);
            string result = "Forward colliders:";
            for (int index = 0; index < hits.Length; index++)
            {
                Collider collider = hits[index].collider;
                if (collider == null ||
                    collider == controller)
                {
                    continue;
                }

                result +=
                    $" {collider.name}@{hits[index].distance:F2};";
            }

            return result;
        }
    }
}
