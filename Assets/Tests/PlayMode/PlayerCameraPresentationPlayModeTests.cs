
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PlayerCameraPresentationPlayModeTests
    {
        private readonly List<GameObject> cleanupObjects =
            new List<GameObject>();


        [UnityTest]
        public IEnumerator ExteriorCamera_RotateYawPreservesPlayerHeadingAndAim()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Third Person Camera Target");
            player.transform.position = new Vector3(0f, 100f, 0f);
            player.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.transform, false);
            follow.SetOrbitInputEnabled(false);
            yield return null;

            Quaternion originalPlayerHeading = player.transform.rotation;
            Vector3 originalPlayerToCamera = Vector3.ProjectOnPlane(
                camera.transform.position - player.transform.position,
                Vector3.up).normalized;

            Assert.That(camera.orthographic, Is.False);
            Assert.That(
                Vector3.Angle(
                    Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up),
                    player.transform.forward),
                Is.LessThan(0.1f),
                "The initial camera pose should follow the actor heading.");
            Assert.That(camera.fieldOfView, Is.EqualTo(53f).Within(0.01f));

            follow.RotateYaw(135f);
            Assert.That(
                Quaternion.Angle(
                    player.transform.rotation,
                    originalPlayerHeading),
                Is.LessThan(0.001f));
            follow.Snap();
            yield return null;

            Vector3 focusPoint = player.transform.position + Vector3.up * 1.6f;
            Vector3 playerToCamera = Vector3.ProjectOnPlane(
                camera.transform.position - player.transform.position,
                Vector3.up).normalized;

            Assert.That(
                Quaternion.Angle(
                    player.transform.rotation,
                    originalPlayerHeading),
                Is.LessThan(0.001f),
                "Orbit yaw must not rotate the player root.");
            Assert.That(
                Vector3.Angle(
                    camera.transform.forward,
                    (focusPoint + camera.transform.right * .5f - camera.transform.position).normalized),
                Is.LessThan(0.1f),
                "Orbit yaw must retain the right-shoulder sight line.");
            Assert.That(
                Vector3.Angle(originalPlayerToCamera, playerToCamera),
                Is.EqualTo(135f).Within(0.1f),
                "RotateYaw must orbit the camera independently.");
        }



        [UnityTest]
        public IEnumerator ExteriorCamera_VerticalOrbitConsumesMouseInputAndClamps()
        {
            InputTestFixture inputFixture = null;
            Mouse mouse = null;
            try
            {
                inputFixture = new InputTestFixture();
                inputFixture.Setup();
                mouse = InputSystem.AddDevice<Mouse>();

                Camera camera = CreateCamera(Vector3.zero);
                GameObject player = CreateObject(
                    "Vertical Orbit Camera Target");
                player.transform.position = new Vector3(0f, 100f, 0f);
                player.transform.rotation = Quaternion.Euler(0f, 35f, 0f);

                PlayerCameraFollow follow =
                    camera.gameObject.AddComponent<PlayerCameraFollow>();
                follow.Initialize(camera, player.transform, false);
                follow.SetCinematicMotionEnabled(false);
                follow.Snap();
                yield return null;

                Quaternion playerHeading = player.transform.rotation;
                Vector3 initialPlanarForward = Vector3.ProjectOnPlane(
                    camera.transform.forward,
                    Vector3.up).normalized;
                float initialForwardY = camera.transform.forward.y;
                float initialCameraY = camera.transform.position.y;
                Assert.That(
                    follow.CurrentOrbitPitch,
                    Is.EqualTo(14f).Within(0.001f));

                follow.SetOrbitInputEnabled(false);
                inputFixture.Press(
                    mouse.rightButton,
                    queueEventOnly: true);
                inputFixture.Set(
                    mouse.delta,
                    new Vector2(0f, 60f),
                    queueEventOnly: true);
                yield return null;
                yield return null;

                Assert.That(
                    follow.TargetOrbitPitch,
                    Is.EqualTo(14f).Within(0.001f),
                    "A modal orbit lock must suppress vertical input too.");

                follow.SetOrbitInputEnabled(true);
                inputFixture.Set(
                    mouse.delta,
                    new Vector2(0f, 60f),
                    queueEventOnly: true);
                yield return null;
                yield return null;
                follow.Snap();

                Assert.That(
                    follow.CurrentOrbitPitch,
                    Is.LessThan(14f),
                    "Positive mouse Y must raise the ordinary chase view.");
                Assert.That(
                    camera.transform.forward.y,
                    Is.GreaterThan(initialForwardY + 0.05f));
                Assert.That(
                    camera.transform.position.y,
                    Is.LessThan(initialCameraY - 0.1f));
                Assert.That(
                    Vector3.Angle(
                        initialPlanarForward,
                        Vector3.ProjectOnPlane(
                            camera.transform.forward,
                            Vector3.up).normalized),
                    Is.LessThan(0.1f),
                    "Pure vertical input must not change orbit yaw.");
                Assert.That(
                    Quaternion.Angle(
                        player.transform.rotation,
                        playerHeading),
                    Is.LessThan(0.001f));
                AssertCameraAimsAtFocus(camera, player.transform, 1.6f, 1.9f);

                follow.RotatePitch(10000f);
                follow.Snap();
                Assert.That(
                    follow.TargetOrbitPitch,
                    Is.EqualTo(follow.MaximumOrbitPitch));
                Assert.That(
                    follow.CurrentOrbitPitch,
                    Is.EqualTo(55f).Within(0.001f));
                AssertCameraAimsAtFocus(camera, player.transform, 1.6f, 1.9f);

                follow.RotatePitch(-10000f);
                follow.Snap();
                Assert.That(
                    follow.TargetOrbitPitch,
                    Is.EqualTo(follow.MinimumOrbitPitch));
                Assert.That(
                    follow.CurrentOrbitPitch,
                    Is.EqualTo(-20f).Within(0.001f));
                AssertCameraAimsAtFocus(camera, player.transform, 1.6f, 1.9f);
            }
            finally
            {
                if (mouse != null && mouse.added)
                {
                    InputSystem.RemoveDevice(mouse);
                }

                inputFixture?.TearDown();
            }
        }




        [UnityTest]
        public IEnumerator ExteriorCamera_ArrowKeysOrbitAndRespectModalLock()
        {
            InputTestFixture inputFixture = null;
            Keyboard keyboard = null;
            try
            {
                inputFixture = new InputTestFixture();
                inputFixture.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();

                Camera camera = CreateCamera(Vector3.zero);
                GameObject player = CreateObject(
                    "Arrow Orbit Camera Target");
                player.transform.position = new Vector3(0f, 100f, 0f);

                PlayerCameraFollow follow =
                    camera.gameObject.AddComponent<PlayerCameraFollow>();
                follow.Initialize(camera, player.transform, false);
                follow.SetCinematicMotionEnabled(false);
                follow.Snap();
                yield return null;

                float initialPitch = follow.TargetOrbitPitch;
                Vector3 initialPlanarForward = Vector3.ProjectOnPlane(
                    camera.transform.forward,
                    Vector3.up).normalized;

                follow.SetOrbitInputEnabled(false);
                inputFixture.Press(
                    keyboard.upArrowKey,
                    queueEventOnly: true);
                yield return null;
                yield return null;

                Assert.That(
                    follow.TargetOrbitPitch,
                    Is.EqualTo(initialPitch).Within(0.001f),
                    "A modal orbit lock must suppress arrow-key look.");

                follow.SetOrbitInputEnabled(true);
                // The arrow axis is scaled by unscaled delta time, so
                // a fixed frame count means nothing in batch mode —
                // hold the key against a realtime deadline instead.
                float pitchDeadline = Time.realtimeSinceStartup + 1.5f;
                while (Time.realtimeSinceStartup < pitchDeadline &&
                       follow.TargetOrbitPitch > initialPitch - 0.2f)
                {
                    yield return null;
                }

                Assert.That(
                    follow.TargetOrbitPitch,
                    Is.LessThan(initialPitch - 0.19f),
                    "The up arrow must raise the view like stick-up.");

                follow.Snap();
                Assert.That(
                    Vector3.Angle(
                        initialPlanarForward,
                        Vector3.ProjectOnPlane(
                            camera.transform.forward,
                            Vector3.up).normalized),
                    Is.LessThan(0.1f),
                    "A pure vertical arrow must not change orbit yaw.");

                inputFixture.Release(
                    keyboard.upArrowKey,
                    queueEventOnly: true);
                yield return null;

                float pitchAfterVertical = follow.TargetOrbitPitch;
                inputFixture.Press(
                    keyboard.rightArrowKey,
                    queueEventOnly: true);
                float yawDeadline = Time.realtimeSinceStartup + 1.5f;
                float yawTravel = 0f;
                while (Time.realtimeSinceStartup < yawDeadline &&
                       yawTravel < 0.5f)
                {
                    yield return null;
                    follow.Snap();
                    yawTravel = Vector3.Angle(
                        initialPlanarForward,
                        Vector3.ProjectOnPlane(
                            camera.transform.forward,
                            Vector3.up).normalized);
                }

                Assert.That(
                    yawTravel,
                    Is.GreaterThanOrEqualTo(0.5f),
                    "The right arrow must orbit the camera's yaw.");
                Assert.That(
                    follow.TargetOrbitPitch,
                    Is.EqualTo(pitchAfterVertical).Within(0.001f),
                    "A pure horizontal arrow must not change pitch.");

                inputFixture.Release(
                    keyboard.rightArrowKey,
                    queueEventOnly: true);
                yield return null;
            }
            finally
            {
                if (keyboard != null && keyboard.added)
                {
                    InputSystem.RemoveDevice(keyboard);
                }

                inputFixture?.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator ExteriorCamera_OrbitYawHasHeavySmoothConvergence()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Heavy Orbit Camera Target");
            player.transform.position = new Vector3(0f, 100f, 0f);

            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.transform, false);
            follow.SetOrbitInputEnabled(false);
            follow.SetCinematicMotionEnabled(false);
            follow.Snap();

            Quaternion originalPlayerHeading = player.transform.rotation;
            Vector3 initialArm = Vector3.ProjectOnPlane(
                camera.transform.position - player.transform.position,
                Vector3.up).normalized;
            Vector3 targetArm =
                Quaternion.AngleAxis(90f, Vector3.up) * initialArm;
            FieldInfo smoothTimeField =
                typeof(PlayerCameraFollow).GetField(
                    "yawSmoothTime",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(smoothTimeField, Is.Not.Null);
            float configuredSmoothTime =
                (float)smoothTimeField.GetValue(follow);
            Assert.That(configuredSmoothTime, Is.EqualTo(0.2f));

            float sampleStartedAt = Time.unscaledTime;
            follow.RotateYaw(90f);
            yield return null;
            yield return null;

            float earlyProgress = Vector3.Angle(
                initialArm,
                Vector3.ProjectOnPlane(
                    camera.transform.position -
                    player.transform.position,
                    Vector3.up).normalized);
            Assert.That(
                earlyProgress,
                Is.GreaterThan(0f),
                "Orbit yaw should begin responding immediately.");
            float sampleElapsed = Time.unscaledTime - sampleStartedAt;
            if (sampleElapsed <= configuredSmoothTime)
            {
                Assert.That(
                    earlyProgress,
                    Is.LessThan(80f),
                    "A normal frame inside the smoothing window must " +
                    "retain visible rotational weight.");
            }

            float convergenceDeadline =
                Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < convergenceDeadline &&
                   Vector3.Angle(
                       targetArm,
                       Vector3.ProjectOnPlane(
                           camera.transform.position -
                           player.transform.position,
                           Vector3.up).normalized) >= 0.5f)
            {
                yield return null;
            }

            Assert.That(
                Vector3.Angle(
                    targetArm,
                    Vector3.ProjectOnPlane(
                        camera.transform.position -
                        player.transform.position,
                        Vector3.up).normalized),
                Is.LessThan(1f),
                "Heavy yaw smoothing must still settle on the requested " +
                "orbit angle.");
            Assert.That(
                Quaternion.Angle(
                    player.transform.rotation,
                    originalPlayerHeading),
                Is.LessThan(0.001f));
        }



        [UnityTest]
        public IEnumerator ExteriorCamera_ObstacleImmediatelyShortensCameraArm()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Obstructed Camera Target");
            player.transform.position = new Vector3(0f, 100f, 0f);

            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.transform, false);
            follow.SetOrbitInputEnabled(false);
            follow.SetCinematicMotionEnabled(false);
            follow.Snap();
            yield return null;


            Vector3 focusPoint = player.transform.position + Vector3.up * 1.6f;
            float unobstructedDistance = GetBackwardDistance(camera, focusPoint);
            Vector3 cameraPivot = focusPoint + camera.transform.right * .5f;
            GameObject wall = CreateObject("Camera Obstacle");
            wall.transform.position = cameraPivot - camera.transform.forward * 1.5f;
            BoxCollider wallCollider = wall.AddComponent<BoxCollider>();
            wallCollider.size = new Vector3(4f, 4f, 0.25f);
            Physics.SyncTransforms();
            yield return null;

            float actualDistance = GetBackwardDistance(camera, focusPoint);
            Assert.That(
                unobstructedDistance,
                Is.EqualTo(1.9f).Within(0.01f));
            Assert.That(
                actualDistance,
                Is.LessThan(1.5f),
                "An obstacle must pull the camera inward before rendering.");
            Assert.That(
                Vector3.Angle(
                    camera.transform.forward,
                    (cameraPivot - camera.transform.position).normalized),
                Is.LessThan(0.1f),
                "Backward clearance must preserve the right-shoulder sight line.");

            wallCollider.enabled = false;
            yield return null;

            float firstRecoveryDistance = GetBackwardDistance(camera, focusPoint);
            Assert.That(
                firstRecoveryDistance,
                Is.GreaterThan(actualDistance)
                    .And.LessThan(unobstructedDistance - 0.05f),
                "Leaving an obstacle must recover the closer camera arm " +
                "smoothly instead of popping outward.");

            float recoveryDeadline =
                Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < recoveryDeadline &&
                   GetBackwardDistance(camera, focusPoint) <
                   unobstructedDistance - 0.01f)
            {
                yield return null;
            }

            Assert.That(
                GetBackwardDistance(camera, focusPoint),
                Is.EqualTo(unobstructedDistance).Within(0.01f));
            follow.Snap();

            GameObject rightWall = CreateObject("Test right shoulder camera wall");
            rightWall.transform.position = focusPoint + Vector3.right * .55f;
            BoxCollider rightWallCollider = rightWall.AddComponent<BoxCollider>();
            rightWallCollider.size = new Vector3(.1f, 4f, 8f);
            // More overlapping distant panels than the initial query buffer.
            // The nearest wall must still win rather than whichever hits fit.
            for (int index = 0; index < 32; index++)
            {
                GameObject panel = CreateObject("Test right camera wall panel");
                panel.transform.position = focusPoint + Vector3.right * .7f;
                panel.AddComponent<BoxCollider>().size = new Vector3(.1f, 4f, 8f);
            }
            Physics.SyncTransforms();
            yield return null;
            Vector3 offset = Quaternion.Inverse(camera.transform.rotation) *
                (camera.transform.position - focusPoint);
            Assert.That(offset.x, Is.InRange(.1f, .25f),
                "The lateral sweep must select the nearest right wall even when its query buffer fills.");
            Assert.That(-offset.z, Is.EqualTo(1.9f).Within(.001f),
                "A wall alongside the hero must contract the shoulder offset while keeping the free backward arm.");
            Assert.That(Vector3.Distance(rightWallCollider.ClosestPoint(camera.transform.position),
                camera.transform.position), Is.GreaterThan(.2f),
                "The whole camera sphere must remain on the hero's side of the right wall.");
        }



        [UnityTest]
        public IEnumerator ExteriorCamera_DefaultArchVaultShortensCameraArm()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityArchShelterPlan plan =
                CityArchShelterPlanner.Create(layout);
            CityArchShelterPropDescriptor barrel = plan.Props.Single(
                prop => prop.Kind == CityArchShelterPropKind.BurnBarrel);
            CityArchShelterObstacleDescriptor vault =
                plan.Obstacles.Single(
                    obstacle => obstacle.Kind ==
                        CityArchShelterObstacleKind.VaultCrown);

            GameObject vaultObject = CreateObject("Arch Vault Crown");
            vaultObject.transform.position = vault.Bounds.center;
            BoxCollider vaultCollider =
                vaultObject.AddComponent<BoxCollider>();
            vaultCollider.size = vault.Bounds.size;

            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Arch Camera Target");
            player.transform.position = barrel.Position;
            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            Physics.SyncTransforms();
            follow.Initialize(camera, player.transform, false);
            follow.SetOrbitInputEnabled(false);
            follow.SetCinematicMotionEnabled(false);

            Vector3 focusPoint =
                player.transform.position + Vector3.up * 1.6f;
            Assert.That(
                GetBackwardDistance(camera, focusPoint),
                Is.EqualTo(1.9f).Within(0.01f),
                "The ordinary low-angle shot must retain its full arm.");

            follow.RotatePitch(
                follow.MaximumOrbitPitch - follow.TargetOrbitPitch);
            follow.Snap();
            yield return null;

            float obstructedDistance = GetBackwardDistance(camera, focusPoint);
            Assert.That(
                obstructedDistance,
                Is.LessThan(1.2f),
                "The masonry below the old roof proxy must pull the " +
                "camera close before it enters the visible vault.");
            Assert.That(
                camera.transform.position.y + 0.2f,
                Is.LessThan(vault.Bounds.min.y - 0.05f),
                "The complete camera collision sphere must remain below " +
                "the vault crown.");
            Assert.That(
                Vector3.Angle(
                    camera.transform.forward,
                    (focusPoint + camera.transform.right * .5f - camera.transform.position).normalized),
                Is.LessThan(0.1f),
                "Vault avoidance must preserve the right-shoulder sight line.");
        }



        [UnityTest]
        public IEnumerator CameraProfiles_KeepPlayerLowInExteriorAndInteriorFraming()
        {
            Camera exteriorCamera = CreateCamera(Vector3.zero);
            GameObject exteriorPlayer =
                CreateObject("Exterior Framing Target");
            exteriorPlayer.transform.position =
                new Vector3(0f, 100f, 0f);
            PlayerCameraFollow exteriorFollow =
                exteriorCamera.gameObject.AddComponent<PlayerCameraFollow>();

            exteriorFollow.Initialize(
                exteriorCamera,
                exteriorPlayer.transform,
                false);

            Vector3 exteriorFocus =
                exteriorPlayer.transform.position + Vector3.up * 1.6f;
            AssertCameraAimsAtFocus(exteriorCamera, exteriorPlayer.transform, 1.6f, 1.9f);
            Pose resolvedExterior = exteriorFollow.ResolveFollowPose(exteriorPlayer.transform.position);
            Assert.That(Vector3.Distance(resolvedExterior.position, exteriorCamera.transform.position),
                Is.LessThan(.001f), "A prospective contextual return must resolve the same shoulder pose.");
            Assert.That(Quaternion.Angle(resolvedExterior.rotation, exteriorCamera.transform.rotation),
                Is.LessThan(.01f));
            Assert.That(
                exteriorCamera.fieldOfView,
                Is.EqualTo(53f).Within(0.01f));
            Pose ordinaryPose = new Pose(exteriorCamera.transform.position, exteriorCamera.transform.rotation);
            float ordinaryFieldOfView = exteriorCamera.fieldOfView;
            Assert.That(
                exteriorCamera.WorldToViewportPoint(
                    exteriorPlayer.transform.position + Vector3.up).y,
                Is.InRange(0.15f, 0.3f),
                "The exterior profile must compose the player's center " +
                "below the middle of the frame.");

            Camera interiorCamera = CreateCamera(Vector3.zero);
            GameObject interiorPlayer =
                CreateObject("Interior Framing Target");
            interiorPlayer.transform.position =

                new Vector3(20f, 100f, 0f);
            PlayerCameraFollow interiorFollow =
                interiorCamera.gameObject.AddComponent<PlayerCameraFollow>();

            interiorFollow.Initialize(
                interiorCamera,
                interiorPlayer.transform,
                true);

            Vector3 interiorFocus =
                interiorPlayer.transform.position + Vector3.up * 1.5f;
            AssertCameraAimsAtFocus(interiorCamera, interiorPlayer.transform, 1.5f, 1.9f);
            Pose resolvedInterior = interiorFollow.ResolveFollowPose(interiorPlayer.transform.position);
            Assert.That(Vector3.Distance(resolvedInterior.position, interiorCamera.transform.position),
                Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(resolvedInterior.rotation, interiorCamera.transform.rotation),
                Is.LessThan(.01f));
            Assert.That(
                interiorCamera.fieldOfView,
                Is.EqualTo(57f).Within(0.01f));
            Assert.That(
                interiorCamera.WorldToViewportPoint(
                    interiorPlayer.transform.position + Vector3.up).y,
                Is.InRange(0.2f, 0.35f),
                "The interior profile must compose the player's center " +
                "below the middle of the frame.");

            Transform exteriorShoulder = CreateObject("Test exterior firearm shoulder").transform;
            exteriorShoulder.SetParent(exteriorPlayer.transform, false);
            exteriorShoulder.localPosition = Vector3.up * 1.1f;
            Transform interiorShoulder = CreateObject("Test interior firearm shoulder").transform;
            interiorShoulder.SetParent(interiorPlayer.transform, false);
            interiorShoulder.localPosition = Vector3.up * 1.1f;
            exteriorFollow.SetOrbitInputEnabled(false);
            interiorFollow.SetOrbitInputEnabled(false);
            exteriorFollow.SetCinematicMotionEnabled(false);
            interiorFollow.SetCinematicMotionEnabled(false);
            Assert.That(exteriorFollow.SetFreeAim(this, exteriorShoulder), Is.True);
            Assert.That(interiorFollow.SetFreeAim(this, interiorShoulder), Is.True);
            yield return new WaitForSeconds(.3f);
            // A long frame can finish WaitForSeconds before its LateUpdate.
            yield return null;
            foreach (var profile in new[]
            {
                (Camera: exteriorCamera, Player: exteriorPlayer),
                (Camera: interiorCamera, Player: interiorPlayer)
            })
            {
                Vector3 origin = profile.Player.transform.position + Vector3.up * 1.5f;
                Vector3 offset = Quaternion.Inverse(profile.Camera.transform.rotation) *
                    (profile.Camera.transform.position - origin);
                Assert.That(offset.x, Is.EqualTo(.5f).Within(.001f));
                Assert.That(offset.y, Is.EqualTo(0f).Within(.001f),
                    "Firearm aim must use the raised shoulder origin.");
                Assert.That(offset.z, Is.EqualTo(-1.4f).Within(.001f),
                    "Both ordinary camera profiles must push closer when firearm aim begins.");
            }
            Pose aimedPose = new Pose(exteriorCamera.transform.position, exteriorCamera.transform.rotation);
            float aimedFieldOfView = exteriorCamera.fieldOfView;

            exteriorFollow.ClearFreeAim(this);
            interiorFollow.ClearFreeAim(this);
            yield return new WaitForSeconds(.3f);
            yield return null;
            AssertCameraAimsAtFocus(exteriorCamera, exteriorPlayer.transform, 1.6f, 1.9f);
            AssertCameraAimsAtFocus(interiorCamera, interiorPlayer.transform, 1.5f, 1.9f);
            Assert.That(exteriorCamera.WorldToViewportPoint(exteriorFocus).x, Is.LessThan(.5f),
                "Returning from aim must keep the hero to the left of the right-shoulder sight line.");
            Assert.That(interiorCamera.WorldToViewportPoint(interiorFocus).x, Is.LessThan(.5f));
            Transform opponent = CreateObject("Test firearm camera opponent").transform;
            opponent.position = exteriorPlayer.transform.position + Vector3.forward * 4f;
            Transform opponentAim = CreateObject("Test firearm camera opponent shoulder").transform;
            opponentAim.SetParent(opponent, false);
            opponentAim.localPosition = Vector3.up * 1.1f;
            Assert.That(exteriorFollow.SetTargetLock(this, opponent, opponentAim, exteriorShoulder,
                stableFraming: true), Is.True);
            Assert.That(exteriorCamera.transform.position.z - exteriorPlayer.transform.position.z,
                Is.EqualTo(-1.4f).Within(.001f),
                "Target-focused firearm aim must share the closer backward arm.");
            Assert.That(exteriorCamera.transform.position.y - exteriorPlayer.transform.position.y,
                Is.EqualTo(1.5f).Within(.001f),
                "Target-focused firearm aim must share the raised shoulder origin.");
            Vector3 lockedPosition = exteriorCamera.transform.position;
            Quaternion lockedRotation = exteriorCamera.transform.rotation;
            exteriorFollow.ClearTargetLock(this, preservePose: true);
            Assert.That(exteriorFollow.SetFreeAim(this, exteriorShoulder, preserveCurrentPose: true), Is.True);
            yield return null;
            exteriorFollow.PrepareFreeAimFrame(this);
            Assert.That(Vector3.Distance(exteriorCamera.transform.position, lockedPosition), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(exteriorCamera.transform.rotation, lockedRotation), Is.LessThan(.01f),
                "Releasing a firearm target must preserve its accepted closer framing.");

            // Load/render the production hero only after the live transition
            // assertions, so a cold asset or render frame cannot consume their wait.
            exteriorFollow.enabled = false;
            Player3DAssetRegistry registry = Player3DResources.Instantiate(exteriorPlayer.transform);
            registry.gameObject.name = "Test camera presentation hero";
            foreach (MonoBehaviour driver in registry.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(driver is Player3DAssetRegistry) && !(driver is PlayerWardrobe)) driver.enabled = false;
            registry.Animator.enabled = false;
            foreach (Collider collider in registry.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Rigidbody body in registry.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            Assert.That(registry.TryGetAnimation("Idle", out Player3DAnimationBinding idle), Is.True);
            idle.Clip.SampleAnimation(registry.Animator.gameObject, .35f);
            GameObject lightObject = CreateObject("Test camera presentation key light");
            lightObject.transform.rotation = Quaternion.Euler(25f, -35f, 0f);
            Light keyLight = lightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.6f;
            exteriorCamera.clearFlags = CameraClearFlags.SolidColor;
            exteriorCamera.backgroundColor = new Color(.09f, .1f, .12f);
            exteriorCamera.transform.SetPositionAndRotation(ordinaryPose.position, ordinaryPose.rotation);
            exteriorCamera.fieldOfView = ordinaryFieldOfView;
            AreaCaptureFixture.CaptureCurrentCamera(exteriorCamera, "PlayerCameraShoulder",
                "Test ordinary right shoulder");
            exteriorCamera.transform.SetPositionAndRotation(aimedPose.position, aimedPose.rotation);
            exteriorCamera.fieldOfView = aimedFieldOfView;
            AreaCaptureFixture.CaptureCurrentCamera(exteriorCamera, "PlayerCameraShoulder",
                "Test aiming right shoulder");
        }



        [UnityTest]
        public IEnumerator CameraFollow_DampsFocusCapsLagAndSnapsTeleport()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Smoothed Camera Target");
            player.transform.position = new Vector3(0f, 100f, 0f);
            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.transform, false);
            follow.SetOrbitInputEnabled(false);
            follow.SetCinematicMotionEnabled(false);
            follow.Snap();

            // Walk, run, strafe, backpedal and changes of ground height
            // carry the shoulder framing with the body in the same frame.
            foreach (Vector3 velocity in new[]
            {
                Vector3.forward * 2.6f, Vector3.forward * 4.2f,
                Vector3.right * 2.6f, Vector3.left * 2.6f,
                Vector3.back * 1.4f, Vector3.up * .3f, Vector3.down * .3f
            })
            {
                for (int frame = 0; frame < 8; frame++)
                {
                    player.transform.position += velocity * Mathf.Min(Time.unscaledDeltaTime, .05f);
                    yield return null;
                    Assert.That(Vector3.Distance(follow.CurrentFocusPoint,
                        player.transform.position + Vector3.up * 1.6f), Is.LessThan(.0001f),
                        "Movement must not make the focus trail the hero or pull his camera backward.");
                    AssertCameraAimsAtFocus(camera, player.transform, 1.6f, 1.9f);
                }
                Vector3 stoppedCameraPosition = camera.transform.position;
                for (int frame = 0; frame < 3; frame++)
                {
                    yield return null;
                    Assert.That(Vector3.Distance(camera.transform.position, stoppedCameraPosition),
                        Is.LessThan(.0001f), "Stopping must not leave a delayed camera catch-up.");
                    AssertCameraAimsAtFocus(camera, player.transform, 1.6f, 1.9f);
                }
            }

            FieldInfo snapDistance = typeof(PlayerCameraFollow).GetField("teleportSnapDistance",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(snapDistance, Is.Not.Null);
            float configuredSnapDistance = (float)snapDistance.GetValue(follow);
            snapDistance.SetValue(follow, 0f);
            player.transform.position += Vector3.forward * .1f;
            yield return null;
            Assert.That(Vector3.Distance(follow.CurrentFocusPoint,
                player.transform.position + Vector3.up * 1.6f), Is.LessThan(.0001f),
                "Disabling teleport snapping must still carry ordinary movement into the camera focus.");
            AssertCameraAimsAtFocus(camera, player.transform, 1.6f, 1.9f);
            snapDistance.SetValue(follow, configuredSnapDistance);

            player.transform.position += Vector3.right * 2f;
            yield return null;

            Vector3 teleportedFocus =
                player.transform.position + Vector3.up * 1.6f;
            Assert.That(
                Vector3.Distance(
                    follow.CurrentFocusPoint,
                    teleportedFocus),
                Is.LessThan(0.0001f),
                "Large target jumps must snap instead of dragging the camera.");
            Assert.That(
                GetBackwardDistance(camera, teleportedFocus),
                Is.EqualTo(1.9f).Within(0.001f),
                "Teleport snap must apply the exact collision-safe pose.");

        }



        [UnityTest]
        public IEnumerator CinematicMotion_IsSubtleSpeedDrivenAndYawStable()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Cinematic Camera Target");
            player.transform.position = new Vector3(0f, 100f, 0f);
            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.transform, false);

            Quaternion baseRotation = Quaternion.Euler(14f, 0f, 0f);
            float maximumIdleExcursion = 0f;
            float idleDeadline = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < idleDeadline)
            {
                yield return null;
                maximumIdleExcursion = Mathf.Max(
                    maximumIdleExcursion,
                    CameraExcursionFromBase(
                        camera,
                        follow,
                        baseRotation,
                        1.9f));
                AssertCinematicCameraKeepsStableYawAndFov(camera, 53f);
            }

            float maximumWalkExcursion = 0f;
            float walkDeadline = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < walkDeadline)
            {
                float movementDeltaTime =
                    Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                player.transform.position +=
                    Vector3.right * (4.8f * movementDeltaTime);
                yield return null;
                maximumWalkExcursion = Mathf.Max(
                    maximumWalkExcursion,
                    CameraExcursionFromBase(
                        camera,
                        follow,
                        baseRotation,
                        1.9f));
                AssertCinematicCameraKeepsStableYawAndFov(camera, 53f);
            }

            Assert.That(
                maximumIdleExcursion,
                Is.GreaterThan(0.0001f).And.LessThan(0.02f),
                "Idle camera drift must be present but remain very subtle.");
            Assert.That(
                maximumWalkExcursion,
                Is.GreaterThan(0.008f).And.LessThan(0.04f),
                "Walking must add a bounded motion-driven bob.");

            follow.SetCinematicMotionEnabled(false);
            float fadeDeadline = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < fadeDeadline)
            {
                yield return null;
            }

            Assert.That(
                CameraExcursionFromBase(
                    camera,
                    follow,
                    baseRotation,
                    1.9f),
                Is.LessThan(0.002f),
                "Modal camera suppression must fade the sway to rest.");
        }



        [UnityTest]
        public IEnumerator DollyZoom_AtFullIntoxication_KeepsHeroSizeAndBreathesSmoothly()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Dolly Zoom Camera Target");
            player.transform.position = new Vector3(0f, 100f, 0f);
            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.transform, false);
            follow.ReseedDollyZoom(4242);
            follow.SetOrbitInputEnabled(false);
            bool previousLensFx =
                GraphicsEffectsSettings.IntoxicationLensFxEnabled;
            GraphicsEffectsSettings.IntoxicationLensFxEnabled = true;
            try
            {
                follow.SetIntoxication(1f);
                float sizeConstant = 1.9f * Mathf.Tan(26.5f * Mathf.Deg2Rad);
                float maximumDeviation = 0f;
                float previousFieldOfView = camera.fieldOfView;
                float deadline = Time.realtimeSinceStartup + 10f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    float frameSeconds = Time.unscaledDeltaTime;
                    yield return null;
                    float fieldOfView = camera.fieldOfView;
                    float distance = GetBackwardDistance(camera, follow.CurrentFocusPoint);
                    Assert.That(
                        fieldOfView,
                        Is.InRange(34f - 0.01f, 100f + 0.01f),
                        "The dolly zoom stays inside its lens limits.");
                    Assert.That(
                        distance * Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad),
                        Is.EqualTo(sizeConstant).Within(sizeConstant * 0.03f),
                        "The dolly zoom must keep the hero's apparent size.");
                    Assert.That(
                        Mathf.Abs(fieldOfView - previousFieldOfView),
                        Is.LessThanOrEqualTo(
                            200f * Mathf.Max(frameSeconds, 0.001f) + 0.01f),
                        "The lens must breathe smoothly, never jump.");
                    previousFieldOfView = fieldOfView;
                    maximumDeviation = Mathf.Max(
                        maximumDeviation,
                        Mathf.Abs(fieldOfView - 53f));
                }

                Assert.That(
                    maximumDeviation,
                    Is.GreaterThan(8f),
                    "At the top level the breath must be a strong one.");

                follow.SetIntoxication(0f);
                deadline = Time.realtimeSinceStartup + 12f;
                while (Time.realtimeSinceStartup < deadline &&
                       Mathf.Abs(camera.fieldOfView - 53f) > 0.05f)
                {
                    yield return null;
                }

                Assert.That(
                    camera.fieldOfView,
                    Is.EqualTo(53f).Within(0.05f),
                    "Sobering up must bring the lens back to base.");
                float settleDeadline = Time.realtimeSinceStartup + 0.5f;
                while (Time.realtimeSinceStartup < settleDeadline)
                {
                    yield return null;
                    Assert.That(
                        camera.fieldOfView,
                        Is.EqualTo(53f).Within(0.05f),
                        "A sober lens stays at base.");
                }
            }
            finally
            {
                GraphicsEffectsSettings.IntoxicationLensFxEnabled =
                    previousLensFx;
            }
        }

        [UnityTest]
        public IEnumerator DollyZoom_ReleasedLatchedFixedPose_AbsorbsTheFieldOfView()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Dolly Zoom Release Target");
            player.transform.position = new Vector3(0f, 100f, 0f);
            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.transform, false);
            follow.ReseedDollyZoom(4242);
            follow.SetOrbitInputEnabled(false);
            bool previousLensFx =
                GraphicsEffectsSettings.IntoxicationLensFxEnabled;
            GraphicsEffectsSettings.IntoxicationLensFxEnabled = true;
            try
            {
                follow.SetIntoxication(1f);
                float deadline = Time.realtimeSinceStartup + 10f;
                while (Time.realtimeSinceStartup < deadline &&
                       Mathf.Abs(camera.fieldOfView - 53f) < 10f)
                {
                    yield return null;
                }

                Assert.That(
                    Mathf.Abs(camera.fieldOfView - 53f),
                    Is.GreaterThanOrEqualTo(10f),
                    "The breath never reached a lens worth latching.");

                // An owner takes the camera exactly where the drunk lens
                // was, holds it, and gives it back on the same lens - the
                // bar shop's return leg.
                float latched = camera.fieldOfView;
                follow.SetFixedPose(
                    camera.transform.position,
                    camera.transform.rotation,
                    latched);
                follow.SetIntoxication(0f);
                yield return null;
                Assert.That(
                    camera.fieldOfView,
                    Is.EqualTo(latched).Within(0.001f));
                Assert.That(
                    follow.DollyZoomExponent,
                    Is.Zero,
                    "A fixed pose silences the breath.");
                float holdDeadline = Time.realtimeSinceStartup + 0.8f;
                while (Time.realtimeSinceStartup < holdDeadline)
                {
                    yield return null;
                }

                follow.ClearFixedPose();
                Assert.That(
                    camera.fieldOfView,
                    Is.EqualTo(latched).Within(0.5f),
                    "The release must start on the lens the owner returned to.");

                float previousFieldOfView = camera.fieldOfView;
                deadline = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    float frameSeconds = Time.unscaledDeltaTime;
                    yield return null;
                    Assert.That(
                        Mathf.Abs(camera.fieldOfView - previousFieldOfView),
                        Is.LessThanOrEqualTo(
                            200f * Mathf.Max(frameSeconds, 0.001f) + 0.01f),
                        "The absorbed lens must ease, never jump.");
                    previousFieldOfView = camera.fieldOfView;
                    if (Mathf.Abs(camera.fieldOfView - 53f) <= 0.05f)
                    {
                        break;
                    }
                }

                Assert.That(
                    camera.fieldOfView,
                    Is.EqualTo(53f).Within(0.05f),
                    "The absorbed lens must settle on base.");
            }
            finally
            {
                GraphicsEffectsSettings.IntoxicationLensFxEnabled =
                    previousLensFx;
            }
        }

        [UnityTest]
        public IEnumerator DollyZoom_ReleasedAuthoredFixedPose_HardCutsAsBefore()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Authored Pose Release Target");
            player.transform.position = new Vector3(0f, 100f, 0f);
            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.transform, false);
            follow.SetOrbitInputEnabled(false);
            yield return null;

            follow.SetFixedPose(
                new Vector3(0f, 101f, -3f),
                Quaternion.identity,
                48f);
            yield return null;
            Assert.That(camera.fieldOfView, Is.EqualTo(48f).Within(0.001f));

            follow.ClearFixedPose();
            Assert.That(
                camera.fieldOfView,
                Is.EqualTo(53f).Within(0.001f),
                "An authored shot lens is not absorbed: it cuts to base.");
            yield return null;
            Assert.That(camera.fieldOfView, Is.EqualTo(53f).Within(0.001f));
            Assert.That(follow.DollyZoomExponent, Is.Zero);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = cleanupObjects.Count - 1; i >= 0; i--)
            {
                if (cleanupObjects[i] != null)
                {
                    Object.Destroy(cleanupObjects[i]);
                }
            }

            cleanupObjects.Clear();
            yield return null;
            yield return null;
        }

        private static float CameraExcursionFromBase(
            Camera camera,
            PlayerCameraFollow follow,
            Quaternion baseRotation,
            float distance)
        {
            Vector3 basePosition =

                follow.CurrentFocusPoint + baseRotation * Vector3.right * .5f -
                baseRotation * Vector3.forward * distance;
            return Vector3.Distance(
                camera.transform.position,
                basePosition);
        }

        private static void AssertCinematicCameraKeepsStableYawAndFov(
            Camera camera,
            float expectedFieldOfView)
        {
            Vector3 planarForward = Vector3.ProjectOnPlane(
                camera.transform.forward,
                Vector3.up).normalized;
            Assert.That(
                Vector3.Angle(planarForward, Vector3.forward),
                Is.LessThan(0.05f),
                "Cinematic motion must not introduce yaw sway.");
            Assert.That(
                camera.fieldOfView,
                Is.EqualTo(expectedFieldOfView).Within(0.001f),
                "Cinematic motion must not pulse the field of view.");
        }



        private static void AssertCameraAimsAtFocus(
            Camera camera,
            Transform target,
            float focusHeight,
            float expectedDistance)
        {
            Vector3 focus = target.position + Vector3.up * focusHeight;
            Vector3 offset = Quaternion.Inverse(camera.transform.rotation) *
                (camera.transform.position - focus);
            Assert.That(
                Vector3.Angle(
                    camera.transform.forward,
                    (focus + camera.transform.right * .5f - camera.transform.position).normalized),
                Is.LessThan(0.1f));
            Assert.That(offset.x, Is.EqualTo(.5f).Within(.001f),
                "An unobstructed ordinary view must sit on the hero's right shoulder.");
            Assert.That(-offset.z, Is.EqualTo(expectedDistance).Within(.001f));
        }

        private static float GetBackwardDistance(Camera camera, Vector3 focus)
        {
            return Vector3.Dot(focus - camera.transform.position, camera.transform.forward);
        }



        [UnityTest]
        public IEnumerator FocusOverride_FollowsThePointAndBlendsBack()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Focus Override Camera Target");
            player.transform.position = new Vector3(0f, 100f, 0f);
            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.transform, false);
            follow.SetOrbitInputEnabled(false);
            follow.SetCinematicMotionEnabled(false);
            follow.Snap();
            yield return null;

            Vector3 rootFocus = player.transform.position + Vector3.up * 1.6f;
            Assert.That(AimError(camera, rootFocus), Is.LessThan(0.5f));

            // A body lying a stride to the side: the focus pans over to
            // it, damped, and never cuts.
            Vector3 lying = player.transform.position + new Vector3(0.7f, 0f, 0.3f);
            Vector3 lyingFocus = lying + Vector3.up * PlayerCameraFollow.FocusOverrideHeight;
            follow.SetFocusOverride(lying, 1f);
            Assert.That(follow.FocusOverrideWeight, Is.EqualTo(1f));
            float largestStep = 0f;
            Vector3 previousForward = camera.transform.forward;
            float deadline = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                largestStep = Mathf.Max(
                    largestStep,
                    Vector3.Angle(previousForward, camera.transform.forward));
                previousForward = camera.transform.forward;
                if (AimError(camera, lyingFocus) < 0.5f)
                {
                    break;
                }
            }

            Assert.That(
                AimError(camera, lyingFocus),
                Is.LessThan(0.5f),
                "the camera must come to aim at the overridden focus");
            Assert.That(
                largestStep,
                Is.LessThan(6f),
                "the pan to the body is damped, not a cut");

            // Half weight lands half way; clearing returns to the root.
            follow.SetFocusOverride(lying, 0.5f);
            deadline = Time.realtimeSinceStartup + 4f;
            Vector3 halfFocus = Vector3.Lerp(rootFocus, lyingFocus, 0.5f);
            while (Time.realtimeSinceStartup < deadline &&
                   AimError(camera, halfFocus) >= 0.5f)
            {
                yield return null;
            }

            Assert.That(AimError(camera, halfFocus), Is.LessThan(0.5f));

            follow.ClearFocusOverride();
            Assert.That(follow.FocusOverrideWeight, Is.Zero);
            deadline = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < deadline &&
                   AimError(camera, rootFocus) >= 0.5f)
            {
                yield return null;
            }

            Assert.That(
                AimError(camera, rootFocus),
                Is.LessThan(0.5f),
                "clearing the override returns the focus to the root");
        }

        private static float AimError(Camera camera, Vector3 focus)
        {
            return Vector3.Angle(
                camera.transform.forward,
                (focus + camera.transform.right * .5f - camera.transform.position).normalized);
        }

        private Camera CreateCamera(Vector3 position)
        {
            GameObject cameraObject = CreateObject("Presentation Test Camera");
            cameraObject.transform.position = position;
            return cameraObject.AddComponent<Camera>();
        }


        private GameObject CreateObject(string objectName)
        {
            GameObject gameObject = new GameObject(objectName);
            cleanupObjects.Add(gameObject);
            return gameObject;
        }
    }
}
