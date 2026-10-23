using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class BarInteriorSpawnPlayModeTests
    {
        [UnityTest]
        public IEnumerator BarPrompts_KeepDoorReachableAndCounterLocal()
        {
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                BarInteriorRoot bar = null;
                yield return LoadBar(value => bar = value);
                bar.ArrivalPresentation?.Skip();
                yield return null;
                PlayerInteractor interactor = bar.Player.Interactor;
                foreach (BarCounterStation station in bar.CounterStations)
                {
                    Vector3 entry = station.Seat.Plan.EntryPose.RootPosition;
                    Vector3 forward = station.Seat.Plan.EntryPose.RootRotation * Vector3.forward;
                    bar.Player.Motor.Teleport(entry - forward * 1.5f);
                    Physics.SyncTransforms();
                    yield return null;
                    yield return null;
                    Assert.That(station.CanInteract(interactor), Is.False,
                        "A seat must not offer interaction across the aisle.");
                    Assert.That(interactor.ActiveInteractable, Is.Not.InstanceOf<BarCounterStation>());
                    bar.Player.Motor.Teleport(entry - forward * .54f);
                    Physics.SyncTransforms();
                    yield return null;
                    yield return null;
                    Assert.That(interactor.ActiveInteractable, Is.SameAs(station),
                        "The authored approach must still offer its seat.");
                }

                BarWashroom washroom = bar.Washroom;
                InteriorHingedDoorInteraction door = washroom.Door;
                bar.Player.Motor.Teleport(new Vector3(-4.2f, PlayerFactory.GroundedRootOffset, 3.15f));
                yield return LookAtWashroom(bar, door.InteractionPosition);
                Assert.That(interactor.ActiveInteractable, Is.SameAs(door));
                yield return PressWashroomUse(input, keyboard);
                yield return WaitForWashroom(() => !door.OwnsActiveInteraction, "Opening did not finish.");
                Assert.That(door.IsOpen, Is.True);
                yield return WalkWashroom(bar, new Vector3(-4.2f, 0f, 4.48f));
                yield return WalkWashroom(bar, new Vector3(-3.5f, 0f, 4.65f));
                yield return LookAtWashroom(bar, washroom.Host.Toilet.BowlLookTarget);
                Assert.That(washroom.Choice.CanInteract(interactor), Is.False,
                    "The toilet must not advertise from the doorway even when viewed.");

                // Here the toilet's approach is closer than the open door's
                // handle. Turning back must select the door through normal E.
                yield return WalkWashroom(bar, new Vector3(-4.4f, 0f, 5.35f));
                yield return LookAtWashroom(bar, door.InteractionPosition);
                Assert.That(washroom.Choice.CanInteract(interactor), Is.False);
                Assert.That(interactor.ActiveInteractable, Is.SameAs(door));
                Assert.That(interactor.PromptView.PromptKey, Is.EqualTo(door.PromptKey));
                yield return PressWashroomUse(input, keyboard);
                yield return WaitForWashroom(() => !door.OwnsActiveInteraction, "Closing did not finish.");
                Assert.That(door.IsClosed, Is.True, "E must close the door behind the hero.");
                Assert.That(washroom.Choice.IsOpen, Is.False);

                yield return WalkWashroom(bar, washroom.Host.Toilet.EntryPosition);
                yield return LookAtWashroom(bar, door.InteractionPosition);
                Assert.That(washroom.Choice.CanInteract(interactor), Is.False);
                Assert.That(interactor.ActiveInteractable, Is.Not.SameAs(washroom.Choice));
                yield return LookAtWashroom(bar, washroom.Host.Toilet.BowlLookTarget);
                Assert.That(interactor.ActiveInteractable, Is.SameAs(washroom.Choice));
                yield return PressWashroomUse(input, keyboard);
                Assert.That(washroom.Choice.IsOpen, Is.True, "The nearby toilet must still respond to E.");
                washroom.Choice.Cancel();
            }
            finally
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator Washroom_WalksThroughSolidDoorUsesToiletAndRestoresCamera()
        {
            BarInteriorRoot bar = null;
            yield return LoadBar(value => bar = value);
            bar.ArrivalPresentation?.Skip();
            yield return null;
            BarWashroom washroom = bar.Washroom;
            Assert.That(washroom, Is.Not.Null);
            Assert.That(bar.Patrons.Count, Is.EqualTo(8));
            Assert.That(bar.Room.Find("Pub Table Left Rear"), Is.Null);
            Assert.That(bar.Layout.TryGetFurniture("pub-table-left-rear", out _), Is.False);
            Assert.That(bar.Layout.LightAnchors.Count, Is.EqualTo(8));
            Transform sign = bar.Room.Find("Bar Washroom WC Sign");
            Transform letters = bar.Room.Find("Bar Washroom WC Letters");
            Assert.That(sign, Is.Not.Null);
            Assert.That(letters, Is.Not.Null);
            Assert.That(letters.GetComponent<Renderer>().bounds.min.y,
                Is.GreaterThan(BarWashroomLayout.DoorHeight), "WC must be readable above the doorway.");
            Assert.That(letters.GetComponent<Renderer>().sharedMaterial,
                Is.SameAs(Resources.Load<Material>("Materials/CityNoirEmission")));
            Assert.That(bar.Atmosphere.PracticalLights.Count(light => light.shadows != LightShadows.None),
                Is.EqualTo(3), "Only the counter key, WC ceiling and soft entrance fill own shadow maps.");
            Assert.That(bar.Atmosphere.PracticalLights.Single(light => light.name == "washroom-sign")
                .transform.forward.y, Is.LessThan(-.9f), "The sign must wash its doorway and approach from above.");
            Assert.That(bar.Lighting.BounceRegionCount, Is.EqualTo(4));
            Assert.That(bar.Lighting.TryGetBounceRegion(2, out var wcBounce), Is.True);
            Assert.That(wcBounce.Source.name, Is.EqualTo("washroom-ceiling"));
            Assert.That(wcBounce.Bounds.min.z, Is.GreaterThan(BarWashroomLayout.DoorCenter.z));
            Assert.That(bar.Lighting.TryGetBounceRegion(3, out var cornerBounce), Is.True);
            Assert.That(cornerBounce.Source.name, Is.EqualTo("washroom-sign"));
            Assert.That(cornerBounce.Source.shadows, Is.EqualTo(LightShadows.Soft));
            Assert.That(cornerBounce.Bounds.max.z, Is.LessThan(BarWashroomLayout.DoorCenter.z));
            Assert.That(bar.Lighting.Reflection.Washroom, Is.Not.Null);
            BarInteriorLayoutValidator.ValidateOrThrow(bar.Layout);
            var area = new BarInteriorWalkableArea(bar.Layout);
            Assert.That(area.Contains(new Vector3(1f, 0f, 5.5f), .32f), Is.False,
                "The addition must not turn the outside of the L contour into walkable floor.");
            Assert.That(area.Contains(washroom.Host.Toilet.EntryPosition, .32f), Is.True);
            Assert.That(area.Contains(washroom.Host.Toilet.ExitPosition, .32f), Is.True);
            var body = bar.Player.GameObject.GetComponent<CharacterController>();
            InteriorHingedDoorInteraction door = washroom.Door;
            Assert.That(door.IsClosed, Is.True);
            Assert.That(door.GetComponent<BoxCollider>().enabled, Is.True);

            bar.Player.Motor.Teleport(new Vector3(-3.15f, PlayerFactory.GroundedRootOffset, 2.8f));
            yield return WalkWashroom(bar, new Vector3(-4.2f, 0f, 3.15f));
            Light signLight = bar.Atmosphere.PracticalLights.Single(light => light.name == "washroom-sign");
            try
            {
                signLight.enabled = false;
                CaptureBarWipeReview(bar, "Test washroom entrance sign off", new Vector3(-2.9f, 1.6f, 1.8f),
                    new Vector3(-4.2f, 1.2f, 3.95f), 65f);
            }
            finally { signLight.enabled = true; }
            CaptureBarWipeReview(bar, "Test washroom entrance closed", new Vector3(-2.9f, 1.6f, 1.8f),
                new Vector3(-4.2f, 1.2f, 3.95f), 65f);
            Assert.That(door.Begin(), Is.True, "The door must open from the hall.");
            yield return WaitForWashroom(() => !door.OwnsActiveInteraction, "Door opening did not finish.");
            Assert.That(door.IsOpen, Is.True);
            Assert.That(bar.Player.Motor.InputEnabled, Is.True);
            yield return WalkWashroom(bar, new Vector3(-4.2f, 0f, 4.48f));
            Assert.That(door.TrySetOpenFraction(0f), Is.False, "The leaf must not sweep through the hero.");
            Assert.That(door.IsOpen, Is.True);
            yield return WalkWashroom(bar, new Vector3(-3.5f, 0f, 4.65f));
            Assert.That(body.isGrounded, Is.True);
            yield return null;
            AreaCaptureFixture.CaptureCurrentCamera(Camera.main, SceneIds.BarInterior,
                "Test washroom actual player camera");
            Assert.That(door.Begin(), Is.True, "The door must close from inside.");
            yield return WaitForWashroom(() => !door.OwnsActiveInteraction, "Inside closing did not finish.");
            Assert.That(door.IsClosed, Is.True);
            CaptureBarWipeReview(bar, "Test washroom fixtures", new Vector3(-2.95f, 2.55f, 4.55f),
                new Vector3(-3.85f, .8f, 6.15f), 72f);
            yield return ReviewWashroomMirror(bar);

            GameSessionState.UpdateNeeds(0, 40);
            yield return WalkWashroom(bar, washroom.Host.Toilet.EntryPosition);
            yield return WaitForWashroom(() => Vector3.Distance(Camera.main.transform.position,
                bar.Player.GameObject.transform.position + Vector3.up * 1.3f) > 1f,
                "The ordinary camera never found clearance behind the hero at the toilet approach.");
            Assert.That(Vector3.Distance(Camera.main.transform.position,
                bar.Player.GameObject.transform.position + Vector3.up * 1.3f), Is.GreaterThan(1f),
                "The ordinary camera must fit behind the hero at the usable toilet approach.");
            AreaCaptureFixture.CaptureCurrentCamera(Camera.main, SceneIds.BarInterior,
                "Test washroom normal camera at toilet");
            yield return LookAtWashroom(bar, washroom.Host.Toilet.BowlLookTarget);
            washroom.Choice.Interact(bar.Player.Interactor);
            washroom.Choice.SelectChoice(HomeToiletChoice.Small);
            Assert.That(washroom.Choice.Confirm(), Is.True);
            yield return WaitForWashroom(() => washroom.Toilet.Timeline.Phase == HomeToiletScenePhase.Urinating,
                "The shared small action did not reach its bar fixture.");
            AreaCaptureFixture.CaptureCurrentCamera(Camera.main, SceneIds.BarInterior, "Test washroom toilet action");
            Assert.That(washroom.Host.ResidueScope, Does.StartWith(BarWashroom.ResidueScope));
            yield return WaitForWashroom(() => bar.Player.Motor.InputEnabled,
                "The small action did not release camera and input.");
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(34));
            Assert.That(bar.Player.Interactor.InputEnabled, Is.True);
            Assert.That(bar.Player.Visual.InteractionHandoffLocked, Is.False);
            Assert.That(Camera.main.GetComponent<PlayerCameraFollow>().FixedPoseActive, Is.False);

            // The second branch shares the same local frame. Its normal stop
            // exercises dressing/camera cleanup without a separate test fixture.
            yield return null;
            yield return LookAtWashroom(bar, washroom.Host.Toilet.BowlLookTarget);
            washroom.Choice.Interact(bar.Player.Interactor);
            washroom.Choice.SelectChoice(HomeToiletChoice.Large);
            Assert.That(washroom.Choice.Confirm(), Is.True);
            yield return WaitForWashroom(() => washroom.Plunge.Sequence.Phase == HomeToiletSeatedPhase.Seated,
                "The shared seated action did not reach the bar toilet.");
            washroom.Plunge.RequestStop();
            yield return WaitForWashroom(() => bar.Player.Motor.InputEnabled,
                "The seated action did not restore the hero and camera.");
            Assert.That(washroom.Plunge.IsActive, Is.False);
            Assert.That(bar.Player.Interactor.InputEnabled, Is.True);
            Assert.That(bar.Player.Visual.InteractionHandoffLocked, Is.False);
            Assert.That(door.Begin(), Is.True, "The door must reopen from inside.");
            yield return WaitForWashroom(() => !door.OwnsActiveInteraction, "Inside reopening did not finish.");
            yield return WalkWashroom(bar, new Vector3(-4.2f, 0f, 4.5f));
            yield return WalkWashroom(bar, new Vector3(-4.2f, 0f, 3.15f));
            Assert.That(door.Begin(), Is.True, "The door must close from the hall after leaving.");
            yield return WaitForWashroom(() => !door.OwnsActiveInteraction, "Outside closing did not finish.");
            Assert.That(door.IsClosed, Is.True);
            Assert.That(body.isGrounded, Is.True);
            CaptureWashroomExterior(bar);
            Renderer fallback = washroom.Mirror.Fallback;
            Object.Destroy(washroom.Mirror.gameObject);
            yield return null;
            Assert.That(fallback.enabled, Is.True, "Mirror teardown must close the wall opening.");
            Assert.That(bar.Room.GetComponentsInChildren<Renderer>(true)
                .Where(value => value.name.StartsWith(BarWashroomMirrorWorld.SourcePrefix, StringComparison.Ordinal))
                .All(value => value.forceRenderingOff), Is.True,
                "Renderer-only mirror templates must stay hidden after their consumer is destroyed.");
        }

        private static IEnumerator ReviewWashroomMirror(BarInteriorRoot bar)
        {
            BarWashroomMirrorWorld mirror = bar.Washroom.Mirror;
            Assert.That(mirror, Is.Not.Null);
            Assert.That(mirror.IsInitialized && mirror.HasTwin, Is.True);
            Assert.That(mirror.TwinUnpairedBoneCount, Is.Zero);
            Assert.That(mirror.TwinUnpairedRendererCount, Is.Zero);
            Assert.That(mirror.transform.IsChildOf(bar.Room), Is.False,
                "Virtual geometry must stay outside the hall environment and GI source scans.");
            Assert.That(bar.Room.Find("Bar Washroom Wastebasket"), Is.Not.Null);
            Assert.That(bar.Room.Find("Bar Washroom Discarded Paper"), Is.Not.Null);

            Camera camera = Camera.main;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            Pose previousCamera = new Pose(camera.transform.position, camera.transform.rotation);
            Pose previousHero = new Pose(bar.Player.GameObject.transform.position, bar.Player.GameObject.transform.rotation);
            float previousFov = camera.fieldOfView;
            bool previousFollow = follow.enabled;
            follow.enabled = false;
            try
            {
                bar.Player.Motor.Teleport(new Vector3(-3.20f, PlayerFactory.GroundedRootOffset, 5.18f));
                bar.Player.GameObject.transform.rotation = Quaternion.LookRotation(Vector3.right);
                camera.transform.SetPositionAndRotation(new Vector3(-3.70f, 1.70f, 5.95f),
                    Quaternion.LookRotation(new Vector3(-2.416f, 1.65f, 5.18f) - new Vector3(-3.70f, 1.70f, 5.95f)));
                camera.fieldOfView = 62f;
                yield return null;
                yield return null;
                Assert.That(mirror.IsActive, Is.True);
                Assert.That(mirror.Fallback.enabled, Is.False);
                var presentation = (Player3DCharacterPresentation)bar.Player.Visual;
                Vector3 expected = bar.Room.TransformPoint(BarWashroomMirrorWorld.Reflect(
                    bar.Room.InverseTransformPoint(presentation.Registry.transform.position)));
                Assert.That(Vector3.Distance(mirror.TwinRoot.position, expected), Is.LessThan(.001f));
                Assert.That(mirror.GetComponentsInChildren<Collider>(true).All(value => !value.enabled), Is.True);
                Assert.That(mirror.GetComponentsInChildren<Light>(true).All(value => !value.enabled), Is.True);
                Assert.That(mirror.GetComponentsInChildren<Renderer>(true).All(value => value.gameObject.layer != 31), Is.True);
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.BarInterior, "Test washroom broken mirror and sink");

                camera.transform.SetPositionAndRotation(new Vector3(-4.70f, 1.65f, 4.65f),
                    Quaternion.LookRotation(new Vector3(-2.416f, 1.62f, 5.18f) - new Vector3(-4.70f, 1.65f, 4.65f)));
                yield return null;
                yield return null;
                Assert.That(mirror.IsActive, Is.True, "The free camera must retain reflection at an oblique angle.");
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.BarInterior, "Test washroom oblique mirror and litter");
                mirror.enabled = false;
                Assert.That(mirror.Fallback.enabled, Is.True);
                Assert.That(mirror.IsActive, Is.False);
                mirror.enabled = true;
                yield return null;
                Assert.That(mirror.IsActive, Is.True);
                yield return WaitForWashroom(() => bar.Lighting.Reflection.Washroom.IsReady,
                    "The WC environment capture never completed.");
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.BarInterior, "Test washroom local material response");
                Assert.That(Shader.GetGlobalInt("_SceneSurfaceReflectionCount"), Is.EqualTo(2));
            }
            finally
            {
                mirror.enabled = true;
                bar.Player.Motor.Teleport(previousHero.position);
                bar.Player.GameObject.transform.rotation = previousHero.rotation;
                camera.transform.SetPositionAndRotation(previousCamera.position, previousCamera.rotation);
                camera.fieldOfView = previousFov;
                follow.enabled = previousFollow;
            }
        }

        private static IEnumerator LookAtWashroom(BarInteriorRoot bar, Vector3 target)
        {
            Camera camera = Camera.main;
            var follow = camera.GetComponent<PlayerCameraFollow>();
            follow.Snap();
            Vector3 view = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
            Vector3 direction = Vector3.ProjectOnPlane(target - bar.Player.GameObject.transform.position, Vector3.up);
            follow.RotateYaw(Vector3.SignedAngle(view, direction, Vector3.up));
            follow.Snap();
            Physics.SyncTransforms();
            yield return null;
            yield return null;
        }

        private static IEnumerator PressWashroomUse(InputTestFixture input, Keyboard keyboard)
        {
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return null;
        }

        private static IEnumerator WalkWashroom(BarInteriorRoot bar, Vector3 target)
        {
            target.y = PlayerFactory.GroundedRootOffset;
            PlayerMotor motor = bar.Player.Motor;
            motor.SetInputEnabled(false);
            Vector3 direction = new Vector3(target.x - motor.transform.position.x,
                0f, target.z - motor.transform.position.z);
            Quaternion facing = direction.sqrMagnitude > .0001f ? Quaternion.LookRotation(direction) : motor.transform.rotation;
            float deadline = Time.realtimeSinceStartup + 12f;
            while (!motor.MoveTowardsInteractionPose(target, facing, Time.deltaTime))
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline),
                    "The real capsule could not traverse the washroom route to " + target);
                yield return null;
            }
            motor.SetInputEnabled(true);
            yield return null;
        }

        private static IEnumerator WaitForWashroom(Func<bool> ready, string message)
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!ready())
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), message);
                yield return null;
            }
            yield return null;
        }

        private static void CaptureWashroomExterior(BarInteriorRoot bar)
        {
            var stage = new GameObject("Test bar exterior review");
            Camera camera = Camera.main;
            Pose previous = new Pose(camera.transform.position, camera.transform.rotation);
            float fov = camera.fieldOfView;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            bool postProcessing = data.renderPostProcessing;
            AmbientMode mode = RenderSettings.ambientMode;
            Color ambient = RenderSettings.ambientLight;
            try
            {
                BuildingLot lot = HomeExteriorContextPlanner.Generate(GameSessionState.CitySeed)
                    .Layout.BuildingLots.First(value => value.IsBar);
                CityBarFacadeWorldBuilder.BuildCity(stage.transform, lot);
                Vector3 outward = new Vector3(lot.FrontageDirection.x, 0f, lot.FrontageDirection.y).normalized;
                Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(outward, Vector3.up));
                Vector3 position = lot.DoorPosition + rotation * new Vector3(-16f, 5.3f, -11f);
                Vector3 target = lot.DoorPosition + rotation * new Vector3(-9.7f, 1.8f, -3.9f);
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
                camera.fieldOfView = 58f;
                data.renderPostProcessing = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.55f, .57f, .60f);
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.City, "Test bar washroom exterior");
                position = lot.DoorPosition + rotation * new Vector3(-13f, 7f, -8f);
                target = lot.DoorPosition + rotation * new Vector3(-9.8f, 2.7f, -3.9f);
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
                camera.fieldOfView = 55f;
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.City, "Test bar washroom roof joint");
            }
            finally
            {
                Object.DestroyImmediate(stage);
                camera.transform.SetPositionAndRotation(previous.position, previous.rotation);
                camera.fieldOfView = fov;
                data.renderPostProcessing = postProcessing;
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientLight = ambient;
            }
        }
    }
}
