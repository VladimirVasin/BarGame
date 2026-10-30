using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeBalconyAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            // Batch test startup can enter Play before delayed import setup.
            // Rebind the exact production assets this scene is about to use.
            Type.GetType("BarPromenade.Editor.HomeInteriorModelAssetSetup, BarPromenade.Editor", true)
                .GetMethod("EnsureAssets", Type.EmptyTypes).Invoke(null, null);
            Type.GetType("BarPromenade.Editor.Player3DV2AssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
            Type.GetType("BarPromenade.Editor.PlayerHomeExteriorAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [PrebuildSetup(typeof(HomeBalconyAssetsSetup))]
        [Explicit("Focused live balcony contacts, right-shoulder orbit, folding door and safe final seal; review the rendered frames.")]
        public IEnumerator HomeBalconyShoulderOrbitAndFoldingDoor()
        {
            float captureDelta = Time.captureDeltaTime;
            float timeScale = Time.timeScale;
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            HomeInteriorRoot home = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                keyboard.MakeCurrent();
                Time.timeScale = 1f;
                Time.captureDeltaTime = 1f / 60f;
                GameSessionState.BeginNewGame("balcony-acceptance");
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                GameSessionState.PrepareHomeArrival(HomeArrivalKind.Normal);
                GameSessionState.EnterHome();
                yield return SceneManager.LoadSceneAsync(SceneIds.HomeInterior, LoadSceneMode.Single);
                yield return WaitHomeBalcony(() =>
                {
                    home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                    return home != null && home.IsInitialized;
                }, "Home composition");
                Bounds rearCity = AssertBalconyRearCity(home);
                Assert.That(home.Balcony.Find("Player Home Front Roof Eave"), Is.Null,
                    "The overhead beam must be removed from the actual balcony geometry.");
                Assert.That(home.Room.Find("Home North Exterior Return Wall"), Is.Null,
                    "The optional concrete screen must leave the right-side city view open.");
                Assert.That(home.BalconyDoor, Is.Not.Null);
                HomeBalconyFoldingDoorInteraction door = home.BalconyDoor;
                Assert.That(door.Panels, Has.Length.EqualTo(4));
                Assert.That(door.PanelColliders, Has.Length.EqualTo(4));
                Assert.That(door.IsOpen, Is.True, "A normal arrival retains the open balcony.");
                AssertBalconyHandleMounts(door);
                AssertBalconyDoorwaySeams(home);
                AssertBalconyFoldClearance(home);
                AssertBalconyCapsuleCrossing(home, false);
                CaptureBalconyDoor(home, "balcony-01-folded-door");
                NpcHandPose handPose = home.Smoking.HandPose;
                handPose.SetGrip(false, .27f, NpcHandGripKind.WineBowl);
                float initialRightGrip = handPose.RightGripWeight;
                float initialLeftGrip = handPose.LeftGripWeight;
                NpcHandGripKind initialRightKind = handPose.GetGripKind(false);

                home.Player.Motor.Teleport(home.Room.TransformPoint(home.SmokingPlan.EntryRootPosition));
                home.Player.GameObject.transform.rotation = home.Room.rotation * home.SmokingPlan.EntryRotation;
                Physics.SyncTransforms();
                yield return WaitHomeBalcony(() => ReferenceEquals(
                    home.Player.Interactor.ActiveInteractable, home.Smoking), "smoking prompt");
                yield return PressBalconyInteract(input, keyboard);
                Assert.That(home.Smoking.OwnsInteraction, Is.True);
                yield return WaitHomeBalcony(() => home.AnimatedInteraction.Phase ==
                    PlayerAnimatedInteractionPhase.Looping, "smoking loop");
                HomeBalconySmokingCameraPose shoulder = home.SmokingPlan.EvaluateCamera(4d);
                Assert.That(shoulder.OrbitFraction, Is.Zero);
                Vector3 shoulderFromHero = shoulder.Position - home.SmokingPlan.EntryRootPosition;
                Assert.That(shoulderFromHero.x, Is.LessThan(-.5f));
                Assert.That(shoulderFromHero.z, Is.LessThan(-.4f), "The base camera belongs to the anatomical right shoulder.");
                Assert.That(Vector3.Dot(shoulder.Rotation * Vector3.forward, Vector3.right), Is.GreaterThan(.7f),
                    "The shoulder shot must look materially out into the city.");
                AssertBalconyLiveCamera(home);

                int start = home.Smoking.Definition.LoopStartFrame;
                yield return WaitHomeBalcony(() => home.AnimatedInteraction.FrameIndex - start ==
                    HomeBalconySmokingPlan.InhaleHoldLoopFrame, "held cigarette-to-mouth contact");
                var hero = (Player3DCharacterPresentation)home.Player.Visual;
                Transform mouth = hero.Registry.Anchors.Mouth;
                Transform cigarette = hero.Registry.Anchors.RightCigarette;
                home.Smoking.RefreshSmokingGrip();
                Assert.That(handPose.GetGripKind(false), Is.EqualTo(NpcHandGripKind.Cigarette));
                Assert.That(handPose.RightGripWeight, Is.EqualTo(1f).Within(.001f));
                CaptureCurrentCamera(Camera.main, SceneIds.HomeInterior, "balcony-05-inhale-right-shoulder");
                CaptureBalconyInhaleProfile(home);
                AssertSmokingFingerGrip(hero.Registry, cigarette);
                float mouthError = Vector3.Distance(mouth.position, cigarette.position);
                Assert.That(mouthError, Is.LessThanOrEqualTo(.035f),
                    "The imported live cigarette must reach the mouth rather than the forehead: " + mouthError);
                Assert.That(Vector3.Dot(mouth.up.normalized, cigarette.up.normalized), Is.GreaterThan(.8f),
                    "The cigarette ember must point away from the face during the drag.");
                Assert.That(home.Smoking.CigaretteProp.activeSelf, Is.True);
                Transform forearm = CityPedestrianHandProps.FindSocket(hero.Registry.ModelRoot, "forearm.R");
                Transform hand = CityPedestrianHandProps.FindSocket(hero.Registry.ModelRoot, "hand.R");
                Assert.That(Vector3.Angle(forearm.up, hand.up), Is.LessThanOrEqualTo(40f),
                    "The imported smoking hand must continue the forearm instead of folding back on itself.");

                yield return WaitHomeBalcony(() => home.AnimatedInteraction.FrameIndex - start >= 21,
                    "calm exhale for face view");
                // Sample the existing presentation clock only: the imported rig
                // stays in its genuine calm loop, with no alternate hero/camera.
                AdvanceBalconyCameraClock(home, 22d);
                yield return null;
                HomeBalconySmokingCameraPose profile = home.SmokingPlan.EvaluateCamera(
                    home.Smoking.Timeline.PresentationElapsedSeconds);
                Assert.That(profile.OrbitFraction, Is.EqualTo(1f));
                Vector3 profileFromHero = profile.Position - home.SmokingPlan.EntryRootPosition;
                Assert.That(profileFromHero.x, Is.GreaterThan(.15f));
                Assert.That(profileFromHero.z, Is.LessThan(-.8f));
                Assert.That(Vector3.Distance(profile.Position, shoulder.Position), Is.GreaterThan(1f),
                    "The periodic face reveal must travel around the hero, beyond the small breathing drift.");
                AssertBalconyLiveCamera(home);
                Vector3 mouthViewport = Camera.main.WorldToViewportPoint(mouth.position);
                Assert.That(mouthViewport.z, Is.GreaterThan(0f));
                Assert.That(mouthViewport.x, Is.InRange(.1f, .9f));
                Assert.That(mouthViewport.y, Is.InRange(.1f, .9f));
                CaptureCurrentCamera(Camera.main, SceneIds.HomeInterior, "balcony-06-right-profile");
                CaptureBalconyRearCity(home, rearCity);
                AdvanceBalconyCameraClock(home, 36d);
                yield return null;
                AssertBalconyLiveCamera(home);
                HomeBalconySmokingCameraPose returned = home.SmokingPlan.EvaluateCamera(36d);
                Assert.That(Vector3.Distance(returned.Position, shoulder.Position), Is.LessThan(.0001f));
                Assert.That(Vector3.Distance(home.SmokingPlan.EvaluateCamera(42d).Position, shoulder.Position),
                    Is.LessThan(.0001f), "The next orbit begins at the same shoulder pose.");
                CaptureCurrentCamera(Camera.main, SceneIds.HomeInterior, "balcony-07-return-to-city");

                Assert.That(home.Smoking.RequestExit(), Is.True);
                yield return WaitHomeBalcony(() => !home.Smoking.OwnsInteraction, "smoking exit and camera return");
                Assert.That(home.AnimatedInteraction.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Idle));
                Assert.That(home.Player.Motor.InputEnabled, Is.True);
                Assert.That(home.Player.Interactor.InputEnabled, Is.True);
                Assert.That(home.Smoking.CigaretteProp.activeSelf, Is.False);
                Assert.That(home.Smoking.Timeline.CameraBlend, Is.Zero);
                Assert.That(handPose.RightGripWeight, Is.EqualTo(initialRightGrip).Within(.001f));
                Assert.That(handPose.LeftGripWeight, Is.EqualTo(initialLeftGrip).Within(.001f));
                Assert.That(handPose.GetGripKind(false), Is.EqualTo(initialRightKind));
                foreach (NpcHandPose.HandBinding binding in handPose.Hands)
                {
                    if (binding.IsLeft) continue;
                    foreach (SkinnedMeshRenderer renderer in binding.Renderers)
                        for (int shape = 0; shape < renderer.sharedMesh.blendShapeCount; shape++)
                        {
                            string name = renderer.sharedMesh.GetBlendShapeName(shape);
                            if (name == "WineBowlGrip" || name.EndsWith(".WineBowlGrip", StringComparison.Ordinal))
                                Assert.That(renderer.GetBlendShapeWeight(shape),
                                    Is.EqualTo(initialRightGrip * 100f).Within(.001f), renderer.name);
                        }
                }

                // Both E actions use the real shared approach/clip/hand path.
                yield return OperateBalconyDoor(home, input, keyboard, false, false);
                Assert.That(door.IsClosed, Is.True);
                AssertBalconyCapsuleCrossing(home, true);
                CaptureBalconyDoor(home, "balcony-03-closed-door");
                yield return OperateBalconyDoor(home, input, keyboard, true, true);
                yield return OperateBalconyDoor(home, input, keyboard, false, true);
                yield return OperateBalconyDoor(home, input, keyboard, true, false);
                Assert.That(door.IsOpen, Is.True);
                AssertBalconyFoldClearance(home);

                Vector3 safeInside = home.Room.TransformPoint(new Vector3(
                    4.25f, PlayerFactory.GroundedRootOffset, -.5f));
                home.Player.Motor.Teleport(home.Room.TransformPoint(new Vector3(
                    PlayerHomeBalconyGeometry.HomeFacadeX, PlayerFactory.GroundedRootOffset, -.5f)));
                Physics.SyncTransforms();
                float beforeBlockedClose = door.OpenFraction;
                Assert.That(door.TrySetOpenFraction(0f), Is.False,
                    "The folding leaves must not close through the real hero capsule at the threshold.");
                Assert.That(door.OpenFraction, Is.EqualTo(beforeBlockedClose).Within(.0001f));
                home.Player.Motor.Teleport(safeInside);

                HomeBalconyCorruptionBarrier barrier = home.ApartmentDays.BalconyBarrier;
                home.Player.Motor.Teleport(home.Room.TransformPoint(new Vector3(
                    5.6f, PlayerFactory.GroundedRootOffset, -.5f)));
                barrier.RefreshAppearance(7);
                Assert.That(barrier.IsSealPending, Is.True);
                Assert.That(barrier.IsSealed, Is.False);
                Assert.That(door.IsOpen, Is.True, "Final dressing cannot strand a hero on the balcony.");
                home.Player.Motor.Teleport(safeInside);
                barrier.RefreshAppearance(7);
                Assert.That(barrier.IsSealed, Is.True);
                Assert.That(door.IsSealed, Is.True);
                Assert.That(barrier.DoorCollider.enabled, Is.True);
                Assert.That(door.TrySetOpenFraction(1f), Is.False);
                AssertBalconyCapsuleCrossing(home, true);
                CaptureBalconyDoor(home, "balcony-04-final-seal");
                barrier.RefreshAppearance(1);
                Assert.That(door.IsOpen, Is.True);
                Assert.That(door.IsSealed, Is.False);
                Assert.That(barrier.DoorCollider.enabled, Is.False);

                Debug.Log("HOME BALCONY ACCEPTANCE OK: imported mouth contact, periodic right-shoulder/profile camera, absent overhead beam, two-sided folding contacts, capsule clearance and safe final seal.");
            }
            finally
            {
                if (home != null)
                {
                    if (home.Smoking != null) home.Smoking.enabled = false;
                    if (home.BalconyDoor != null) home.BalconyDoor.enabled = false;
                    Object.Destroy(home.gameObject);
                }
                Time.captureDeltaTime = captureDelta;
                Time.timeScale = timeScale;
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
                GameSessionState.BeginNewGame("balcony-acceptance-cleanup");
            }
        }

        private static IEnumerator OperateBalconyDoor(HomeInteriorRoot home, InputTestFixture input,
            Keyboard keyboard, bool opening, bool fromOutside)
        {
            HomeBalconyFoldingDoorInteraction door = home.BalconyDoor;
            home.Player.Motor.Teleport(fromOutside ? door.GroundedOutsideDock : door.GroundedInsideDock);
            Vector3 towardHandle = door.InteractionPosition - home.Player.GameObject.transform.position;
            towardHandle.y = 0f;
            home.Player.GameObject.transform.rotation = Quaternion.LookRotation(towardHandle, Vector3.up);
            Physics.SyncTransforms();
            yield return WaitHomeBalcony(() => ReferenceEquals(home.Player.Interactor.ActiveInteractable, door),
                fromOutside ? "outside door prompt" : "inside door prompt");
            yield return PressBalconyInteract(input, keyboard);
            Assert.That(door.OwnsActiveInteraction, Is.True,
                "The shared E action must operate the folding door from " + (fromOutside ? "outside" : "inside") +
                ": " + door.LastBlockReason);
            bool contactSeen = false;
            for (int frame = 0; frame < 420 && door.OwnsActiveInteraction; frame++)
            {
                yield return null;
                door.RefreshHandContact();
                if (door.ContactWeight < .98f) continue;
                Transform target = fromOutside ? door.OutsideHandle : door.InsideHandle;
                Assert.That(Vector3.Distance(door.RightGrip.position, target.position), Is.LessThan(.05f),
                    "The real right hand must meet the folding leaf handle.");
                if (!contactSeen)
                {
                    CaptureBalconyDoor(home, "balcony-02-hand-" + (fromOutside ? "outside-" : "inside-") +
                        (opening ? "opening" : "closing"));
                    contactSeen = true;
                }
            }
            Assert.That(contactSeen, Is.True, "The visible door action must contain a real handle-contact sample. " + door.LastBlockReason +
                " phase=" + home.AnimatedInteraction.Phase + " root=" + home.Player.GameObject.transform.position);
            Assert.That(door.OwnsActiveInteraction, Is.False, "The folding action must finish and release ownership.");
            Assert.That(opening ? door.IsOpen : door.IsClosed, Is.True);
            Assert.That(home.Player.Motor.InputEnabled, Is.True);
        }

        private static IEnumerator PressBalconyInteract(InputTestFixture input, Keyboard keyboard)
        {
            keyboard.MakeCurrent();
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return null;
        }

        private static IEnumerator WaitHomeBalcony(Func<bool> ready, string stage)
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            for (int frame = 0; frame < 900 && !ready() && Time.realtimeSinceStartup < deadline; frame++)
                yield return null;
            Assert.That(ready(), Is.True, "Balcony acceptance stalled at " + stage);
        }

        private static void AdvanceBalconyCameraClock(HomeInteriorRoot home, double seconds)
        {
            double delta = seconds - home.Smoking.Timeline.PresentationElapsedSeconds;
            Assert.That(delta, Is.GreaterThanOrEqualTo(0d));
            home.Smoking.Timeline.Advance((float)delta);
        }

        private static void AssertBalconyLiveCamera(HomeInteriorRoot home)
        {
            HomeBalconySmokingCameraPose expected = home.SmokingPlan.EvaluateCamera(
                home.Smoking.Timeline.PresentationElapsedSeconds);
            HomeBalconySmokingCameraDriftSample drift = home.Smoking.Timeline.CameraDrift;
            Quaternion rotation = home.Room.rotation * expected.Rotation;
            Vector3 position = home.Room.TransformPoint(expected.Position) + rotation * drift.LocalPosition;
            Assert.That(Vector3.Distance(home.CameraFollow.FixedBasePosition, position), Is.LessThan(.002f));
            Assert.That(Quaternion.Angle(home.CameraFollow.FixedBaseRotation,
                rotation * Quaternion.Euler(drift.LocalEulerAngles)), Is.LessThan(.05f));
            Assert.That(home.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(HomeBalconySmokingPlan.CameraFieldOfView).Within(.01f));
        }

        private static Bounds AssertBalconyRearCity(HomeInteriorRoot home)
        {
            Transform buildings = home.ExteriorView.Find("Home Exterior Building Silhouettes");
            Assert.That(buildings, Is.Not.Null);
            bool found = false;
            float nearest = float.PositiveInfinity;
            Bounds nearestBounds = default;
            foreach (BuildingLot lot in home.ExteriorContext.NearbyLots)
            {
                if (!lot.IsOrdinaryBuilding || lot.IsPlayerHome) continue;
                Vector3 local = PlayerHomeBalconyGeometry.ToHomeLocal(home.ExteriorContext.PlayerHome, lot.Center);
                if (local.x >= home.Layout.RoomBounds.xMin ||
                    CityBuildingPrototypeWorldBuilder.ClassifyHomeExterior(home.ExteriorContext, lot) !=
                    CityBuildingExteriorFit.Full) continue;
                Transform building = buildings.Find($"Exterior Building {lot.Cell.x}-{lot.Cell.y}");
                Assert.That(building, Is.Not.Null, "Rear city lot " + lot.Cell + " was discarded at the facade.");
                CityBuildingAssetRegistry registry = building.GetComponentInChildren<CityBuildingAssetRegistry>(true);
                Assert.That(registry, Is.Not.Null, "Rear city lots must retain their imported production models.");
                Renderer[] renderers = registry.GetComponentsInChildren<Renderer>();
                Assert.That(renderers, Is.Not.Empty);
                Bounds rendered = default;
                bool hasVisibleMesh = false;
                foreach (Renderer renderer in renderers)
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    if (hasVisibleMesh) rendered.Encapsulate(renderer.bounds);
                    else rendered = renderer.bounds;
                    hasVisibleMesh = true;
                }
                Assert.That(hasVisibleMesh, Is.True, "The retained rear model must actually render.");
                float distance = (local - home.SmokingPlan.EntryRootPosition).sqrMagnitude;
                if (distance >= nearest) continue;
                found = true;
                nearest = distance;
                nearestBounds = rendered;
            }
            Assert.That(found, Is.True,
                "The real seeded neighborhood must continue behind the apartment, not stop at its front wall.");
            foreach (Collider collider in home.ExteriorView.GetComponentsInChildren<Collider>(true))
                Assert.That(collider.enabled, Is.False, "The larger city context must remain passive.");
            return nearestBounds;
        }

        private static void CaptureBalconyRearCity(HomeInteriorRoot home, Bounds rearCity)
        {
            Camera camera = Camera.main;
            Pose saved = new Pose(camera.transform.position, camera.transform.rotation);
            float fov = camera.fieldOfView;
            try
            {
                Vector3 localTarget = home.Room.InverseTransformPoint(rearCity.center);
                float side = localTarget.z < 0f ? home.Layout.RoomBounds.yMin - 10f : home.Layout.RoomBounds.yMax + 10f;
                Vector3 position = home.Room.TransformPoint(new Vector3(
                    home.SmokingPlan.EntryRootPosition.x, 2.4f, side));
                Vector3 target = rearCity.center;
                target.y = position.y;
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, home.Room.up));
                camera.fieldOfView = HomeBalconySmokingPlan.CameraFieldOfView;
                CaptureCurrentCamera(camera, SceneIds.HomeInterior, "balcony-08-rear-city-context");
            }
            finally
            {
                camera.transform.SetPositionAndRotation(saved.position, saved.rotation);
                camera.fieldOfView = fov;
            }
        }

        private static void AssertBalconyFoldClearance(HomeInteriorRoot home)
        {
            foreach (BoxCollider panel in home.BalconyDoor.PanelColliders)
            {
                Assert.That(panel.enabled, Is.True);
                Vector3 outside = home.Room.InverseTransformPoint(panel.bounds.max);
                Assert.That(outside.x, Is.LessThan(PlayerHomeBalconyGeometry.HomeFacadeX + .10f),
                    "The folded door must occupy the apartment side rather than the balcony deck.");
            }
        }

        private static void AssertBalconyHandleMounts(HomeBalconyFoldingDoorInteraction door)
        {
            Transform last = door.Panels[door.Panels.Length - 1];
            Transform glass = last.Find("Home Balcony Folding Glass");
            Bounds pane = glass.GetComponent<MeshFilter>().sharedMesh.bounds;
            float glassEdge = glass.localPosition.z + pane.max.z * glass.localScale.z;
            foreach (Transform handle in new[] { door.InsideHandle, door.OutsideHandle })
            {
                Assert.That(handle.parent, Is.SameAs(last), "The handle belongs to the free latch edge.");
                Bounds mesh = handle.GetComponent<MeshFilter>().sharedMesh.bounds;
                Matrix4x4 panelFromHandle = Matrix4x4.TRS(handle.localPosition, handle.localRotation, handle.localScale);
                float minimumZ = float.PositiveInfinity, maximumZ = float.NegativeInfinity;
                float minimumX = float.PositiveInfinity, maximumX = float.NegativeInfinity;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = panelFromHandle.MultiplyPoint3x4(new Vector3(
                        (corner & 1) == 0 ? mesh.min.x : mesh.max.x,
                        (corner & 2) == 0 ? mesh.min.y : mesh.max.y,
                        (corner & 4) == 0 ? mesh.min.z : mesh.max.z));
                    minimumZ = Mathf.Min(minimumZ, point.z); maximumZ = Mathf.Max(maximumZ, point.z);
                    minimumX = Mathf.Min(minimumX, point.x); maximumX = Mathf.Max(maximumX, point.x);
                }
                Assert.That(minimumZ, Is.GreaterThan(glassEdge + .002f), "The handle must mount entirely on wood.");
                Assert.That(maximumZ, Is.LessThanOrEqualTo(door.PanelColliders[3].size.z));
                float mountingX = ReferenceEquals(handle, door.OutsideHandle) ? minimumX : maximumX;
                Assert.That(Mathf.Abs(mountingX), Is.EqualTo(door.PanelColliders[3].size.x * .5f).Within(.002f),
                    "The mounting feet must touch the wooden face.");
            }
        }

        private static void AssertBalconyDoorwaySeams(HomeInteriorRoot home)
        {
            float openingMinimum = home.BalconyLayout.DoorCenter.z - home.BalconyLayout.DoorSize.z * .5f;
            float openingMaximum = home.BalconyLayout.DoorCenter.z + home.BalconyLayout.DoorSize.z * .5f;
            float skinFront = home.Balcony.Find("Player Home Frontage North Pier").GetComponent<Renderer>().bounds.max.x;
            int jambs = 0;
            foreach (Renderer renderer in home.Balcony.GetComponentsInChildren<Renderer>())
            {
                if (renderer.name != "Home Balcony Door Jamb") continue;
                Bounds bounds = renderer.bounds;
                float gap = bounds.center.z < home.BalconyLayout.DoorCenter.z
                    ? openingMinimum - bounds.max.z : bounds.min.z - openingMaximum;
                Assert.That(gap, Is.GreaterThanOrEqualTo(.030f),
                    "Timber and plaster reveal faces must not share a depth plane.");
                Assert.That(bounds.max.x - skinFront, Is.GreaterThanOrEqualTo(.030f));
                jambs++;
            }
            Assert.That(jambs, Is.EqualTo(2));
            Bounds header = home.Balcony.Find("Home Balcony Door Header").GetComponent<Renderer>().bounds;
            Assert.That(header.min.y - home.BalconyLayout.DoorSize.y, Is.GreaterThanOrEqualTo(.030f),
                "The timber header and plaster lintel must not have coplanar undersides.");
        }

        private static void CaptureBalconyInhaleProfile(HomeInteriorRoot home)
        {
            Camera camera = Camera.main;
            Pose saved = new Pose(camera.transform.position, camera.transform.rotation);
            float fov = camera.fieldOfView;
            try
            {
                // Inspect the live held inhale from the profile angle too;
                // leave the genuine presentation clock and rig untouched.
                HomeBalconySmokingCameraPose profile = home.SmokingPlan.EvaluateCamera(22d);
                camera.transform.SetPositionAndRotation(home.Room.TransformPoint(profile.Position),
                    home.Room.rotation * profile.Rotation);
                camera.fieldOfView = HomeBalconySmokingPlan.CameraFieldOfView;
                CaptureCurrentCamera(camera, SceneIds.HomeInterior, "balcony-05-inhale-right-profile");
                Vector3 mouth = home.Smoking.ExhaleEffect.MouthAnchor.position;
                Vector3 offset = (camera.transform.position - mouth).normalized;
                Vector3 closePosition = mouth + offset * .55f + home.Room.up * .05f;
                camera.transform.SetPositionAndRotation(closePosition,
                    Quaternion.LookRotation(mouth - closePosition, home.Room.up));
                camera.fieldOfView = 45f;
                CaptureCurrentCamera(camera, SceneIds.HomeInterior, "balcony-05-finger-grip-close");
            }
            finally
            {
                camera.transform.SetPositionAndRotation(saved.position, saved.rotation);
                camera.fieldOfView = fov;
            }
        }

        private static void AssertSmokingFingerGrip(Player3DAssetRegistry registry, Transform cigarette)
        {
            Vector3 outward = cigarette.up.normalized;
            Vector3[] centres = new Vector3[3];
            Vector3[] radialContacts = new Vector3[2];
            Transform hand = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "hand.R");
            string[] names = { "GEO_Finger0.R", "GEO_Finger1.R", "GEO_Thumb.R",
                "GEO_Hand.R", "GEO_Finger2.R", "GEO_Finger3.R" };
            for (int part = 0; part < names.Length; part++)
            {
                SkinnedMeshRenderer renderer = null;
                foreach (Renderer candidate in registry.Renderers)
                    if (candidate.name == names[part]) renderer = candidate as SkinnedMeshRenderer;
                Assert.That(renderer, Is.Not.Null);
                Vector3[] vertices = BarDrinkServiceWorldBuilderPlayModeTests.ReadVerifiedHandWorldVertices(
                    renderer, hand, "smoking grip");
                if (part < centres.Length)
                    for (int v = 0; v < vertices.Length; v++)
                        centres[part] += vertices[v] / vertices.Length;
                int[] triangles = renderer.sharedMesh.triangles;
                float shaftClearance = float.PositiveInfinity;
                for (int station = 0; station <= 36; station++)
                {
                    Vector3 point = cigarette.position + outward * Mathf.Lerp(.002f, .074f, station / 36f);
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        Vector3 surface = ClosestSmokingTrianglePoint(point, vertices[triangles[t]],
                            vertices[triangles[t + 1]], vertices[triangles[t + 2]]);
                        shaftClearance = Mathf.Min(shaftClearance, Vector3.Distance(point, surface));
                    }
                }
                Assert.That(shaftClearance, Is.GreaterThanOrEqualTo(.0025f),
                    names[part] + " must not pierce or swallow the cigarette shaft.");
                if (part >= 2) continue;
                float distance = float.PositiveInfinity;
                Vector3 closest = default, axisPoint = default;
                // Index and middle pads meet different stations on the
                // same finite filter cylinder; both must physically grip.
                for (int station = 0; station <= 16; station++)
                {
                    Vector3 point = cigarette.position + outward * Mathf.Lerp(.008f, .040f, station / 16f);
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        Vector3 surface = ClosestSmokingTrianglePoint(point, vertices[triangles[t]],
                            vertices[triangles[t + 1]], vertices[triangles[t + 2]]);
                        float candidateDistance = Vector3.Distance(point, surface);
                        if (candidateDistance >= distance) continue;
                        distance = candidateDistance; closest = surface; axisPoint = point;
                    }
                }
                Assert.That(distance, Is.InRange(.0020f, .0060f),
                    names[part] + " must meet the real 6.5mm cigarette, without floating or penetrating its axis.");
                radialContacts[part] = Vector3.ProjectOnPlane(closest - axisPoint, outward).normalized;
            }
            Assert.That(Vector3.Dot(radialContacts[0], radialContacts[1]), Is.LessThan(-.4f),
                "The cigarette must sit between opposing index/middle pads.");
            Assert.That(Vector3.Dot(centres[2] - (centres[0] + centres[1]) * .5f, outward),
                Is.LessThan(-.003f), "The thumb must be on the faceward side of the grip.");
        }

        private static Vector3 ClosestSmokingTrianglePoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + d5 - d6));
            float inverse = 1f / (va + vb + vc);
            return a + ab * (vb * inverse) + ac * (vc * inverse);
        }

        private static void AssertBalconyCapsuleCrossing(HomeInteriorRoot home, bool blocked)
        {
            Transform actor = home.Player.GameObject.transform;
            Vector3 saved = actor.position;
            Quaternion facing = actor.rotation;
            try
            {
                home.Player.Motor.Teleport(home.Room.TransformPoint(new Vector3(
                    4.25f, PlayerFactory.GroundedRootOffset, -.5f)));
                Physics.SyncTransforms();
                actor.GetComponent<CharacterController>().Move(home.Room.TransformDirection(Vector3.right * 1.4f));
                float localX = home.Room.InverseTransformPoint(actor.position).x;
                if (blocked)
                    Assert.That(localX, Is.LessThan(4.85f), "The shut balcony door must stop the real hero capsule.");
                else
                    Assert.That(localX, Is.GreaterThan(5.3f), "The folded door must leave the balcony walkable.");
            }
            finally
            {
                home.Player.Motor.Teleport(saved);
                actor.rotation = facing;
                Physics.SyncTransforms();
            }
        }

        private static void CaptureBalconyDoor(HomeInteriorRoot home, string name)
        {
            Camera camera = Camera.main;
            Pose saved = new Pose(camera.transform.position, camera.transform.rotation);
            float fov = camera.fieldOfView;
            try
            {
                Vector3 position = home.Room.TransformPoint(new Vector3(6.6f, 1.72f, .25f));
                Vector3 target = home.Room.TransformPoint(new Vector3(4.98f, 1.08f, -.52f));
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, home.Room.up));
                camera.fieldOfView = 60f;
                CaptureCurrentCamera(camera, SceneIds.HomeInterior, name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(saved.position, saved.rotation);
                camera.fieldOfView = fov;
            }
        }
    }
}
