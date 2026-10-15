using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("The shared framed apartment entrance in Home and the warehouse, real E inspection and closed-door lifecycle.")]
        public IEnumerator CombatWarehouseEntranceDoor()
        {
            float previousDelta = Time.captureDeltaTime;
            CombatTestRoot root = null;
            NarrativeInteractionController session = null;
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Time.captureDeltaTime = .05f;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            GameSessionState.BeginNewGame();
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root != null && root.IsInitialized, Is.True);
                root.AutomaticSimulation = false;
                root.SetSparring(false);
                root.SetDuelLogging(false);
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;
                Assert.That(root.Flashlight != null && root.Flashlight.SpotLight.enabled, Is.True,
                    "Use the real chest flashlight for the entrance captures.");
                foreach (var language in new[] { ("ru", "Обратно уже не выйти."), ("en", "There is no way back.") })
                {
                    var catalog = JsonUtility.FromJson<NameplateCatalog>(Resources.Load<TextAsset>("Localization/" + language.Item1).text);
                    Assert.That(catalog.entries.Single(entry => entry.key == "combat.warehouse.entrance_door.closed").value,
                        Is.EqualTo(language.Item2));
                }

                WarehouseArenaPlan plan = root.WarehousePlan;
                NarrativeInteraction target = FindWarehouseEntranceInspection(root);
                Transform door = target.SubjectRoot;
                BoxCollider blocker = door.GetComponent<BoxCollider>();
                AssertWarehouseEntranceAsset(target, plan);
                Mesh[] sharedDoorMeshes = ApartmentEntranceDoor.PartNames.Select(name =>
                    door.Find(name).GetComponent<MeshFilter>().sharedMesh).ToArray();
                Vector3 closedPosition = door.position;
                Quaternion closedRotation = door.rotation;
                Vector3 closedScale = door.lossyScale;
                PlayerInteractor interactor = root.Player.Interactor;
                session = NarrativeInteractionController.For(interactor);
                Camera camera = root.CameraFollow.Camera;

                root.Hero.ResetActor(plan.HeroSpawn + Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.back);
                root.CameraFollow.ClearFixedPose();
                root.CameraFollow.RotateYaw(Mathf.DeltaAngle(camera.transform.eulerAngles.y, 180f));
                root.CameraFollow.Snap();
                for (int frame = 0; frame < 3; frame++) yield return null;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Entrance door from spawn");

                yield return BeginEntranceReading(true);
                Assert.That(session.CurrentPage.TextKey, Is.EqualTo("combat.warehouse.entrance_door.closed"));
                Assert.That(session.PageIndex, Is.Zero, "The opening E must not dismiss the placeholder.");
                Assert.That(root.Prompt.IsHeldBy(session), Is.True);
                Assert.That(root.Prompt.IsSpeaking, Is.False, "The placeholder is a silent inspection page.");
                Assert.That(root.Prompt.GetDisplayedTextAt(Time.unscaledTime),
                    Is.EqualTo(LocalizationService.Get("combat.warehouse.entrance_door.closed")));
                Assert.That(root.Hero.CombatFocused, Is.False);
                Assert.That(interactor.InputEnabled || root.Player.Motor.InputEnabled, Is.False);
                Assert.That(root.CameraFollow.FixedPoseActive && session.CameraDirector.CurrentShotIsClear, Is.True);
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Entrance door inspection");
                yield return CaptureFishUi("Test warehouse - Entrance door placeholder");
                if (!Application.isBatchMode) Assert.That(root.Prompt.LastRenderedTextFits, Is.True);
                AssertClosed();

                yield return PressLodgeUse(input, keyboard);
                for (int frame = 0; frame < 120 && session.IsActive; frame++) yield return null;
                AssertReleased();
                Assert.That(root.Hero.CombatFocused, Is.True, "Inspection must restore the prior combat stance.");
                Assert.That(Vector3.Dot(Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized,
                    Vector3.back), Is.GreaterThan(.99f), "The follow camera returns to the approach orbit.");
                AssertClosed();
                Vector3 hardware = door.Find("Home Entry Door Levers").GetComponent<Renderer>().bounds.center;
                Vector3 detailEye = hardware + new Vector3(.45f, .2f, 1f);
                root.CameraFollow.SetFixedPose(detailEye, Quaternion.LookRotation(hardware - detailEye), 48f);
                for (int frame = 0; frame < 3; frame++) yield return null;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Entrance door hardware");
                root.CameraFollow.ClearFixedPose();

                root.ResetRound();
                yield return null;
                Assert.That(FindWarehouseEntranceInspection(root), Is.SameAs(target));
                AssertClosed();

                // Removing a presenting world object must release the same camera/input lease.
                yield return BeginEntranceReading(false);
                Assert.That(root.NewWarehouseLayout(1), Is.False, "A live inspection owns gameplay input.");
                door.gameObject.SetActive(false);
                yield return null;
                AssertReleased();
                Assert.That(root.Hero.CombatFocused, Is.True);
                door.gameObject.SetActive(true);
                yield return null;
                Assert.That(root.NewWarehouseLayout(1), Is.True);
                yield return null;
                Assert.That(door == null, Is.True, "The old layout must release its entrance object.");
                NarrativeInteraction replacement = FindWarehouseEntranceInspection(root);
                AssertWarehouseEntranceAsset(replacement, root.WarehousePlan);
                AssertReleased();

                // The actual apartment must use these same meshes, rather than retain its old slab.
                session = null;
                root = null;
                GameSessionState.PrepareHomeArrival(HomeArrivalKind.Normal);
                GameSessionState.TryStartGameTimeFromWake();
                yield return SceneManager.LoadSceneAsync(SceneIds.HomeInterior, LoadSceneMode.Single);
                HomeInteriorRoot home = null;
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while ((home == null || !home.IsInitialized) && Time.realtimeSinceStartup < deadline)
                {
                    home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                    yield return null;
                }
                Assert.That(home != null && home.IsInitialized, Is.True);
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;
                Transform homeDoor = home.Room.Find("Home Entrance Door");
                AssertSharedApartmentEntrance(homeDoor);
                Assert.That(homeDoor.localPosition, Is.EqualTo(new Vector3(0f, 0f, -4f)));
                for (int index = 0; index < ApartmentEntranceDoor.PartNames.Count; index++)
                    Assert.That(homeDoor.Find(ApartmentEntranceDoor.PartNames[index]).GetComponent<MeshFilter>().sharedMesh,
                        Is.SameAs(sharedDoorMeshes[index]), "Both scenes must use the same authored model asset.");
                AssertHomeEntranceWallAndTravel(home, homeDoor);

                Camera homeCamera = home.CameraFollow.Camera;
                home.Player.Motor.Teleport(new Vector3(-1.8f, PlayerFactory.GroundedRootOffset, -2f));
                home.FixedCamera.enabled = false;
                Vector3 homeEye = home.Room.TransformPoint(new Vector3(1.45f, 1.72f, -.75f));
                Vector3 homeFocus = home.Room.TransformPoint(new Vector3(0f, 1.16f, -4f));
                home.CameraFollow.SetFixedPose(homeEye, Quaternion.LookRotation(homeFocus - homeEye), 52f);
                for (int frame = 0; frame < 4; frame++) yield return null;
                CaptureCurrentCamera(homeCamera, SceneIds.HomeInterior, "Test apartment - Framed entrance door");
                Vector3 homeHardware = homeDoor.Find("Home Entry Door Levers").GetComponent<Renderer>().bounds.center;
                Vector3 homeDetailEye = homeHardware + new Vector3(.4f, .18f, 1.05f);
                home.CameraFollow.SetFixedPose(homeDetailEye, Quaternion.LookRotation(homeHardware - homeDetailEye), 48f);
                for (int frame = 0; frame < 3; frame++) yield return null;
                CaptureCurrentCamera(homeCamera, SceneIds.HomeInterior, "Test apartment - Entrance door hardware");
                home.FixedCamera.enabled = true;
                home.FixedCamera.ReapplyActiveShot();

                IEnumerator BeginEntranceReading(bool captureApproach)
                {
                    root.Hero.ResetActor(target.Staging.Entry.RootPosition, Vector3.back);
                    root.Hero.SetCombatFocused(true);
                    root.CameraFollow.ClearFixedPose();
                    root.CameraFollow.RotateYaw(Mathf.DeltaAngle(camera.transform.eulerAngles.y, 180f));
                    root.CameraFollow.Snap();
                    Physics.SyncTransforms();
                    for (int frame = 0; frame < 4; frame++) yield return null;
                    Assert.That(target.CanInteract(interactor), Is.True);
                    Assert.That(interactor.ActiveInteractable, Is.SameAs(target),
                        "The real nearby interaction search must select the entrance.");
                    if (captureApproach)
                        CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Entrance door approach");
                    yield return PressLodgeUse(input, keyboard);
                    for (int frame = 0; frame < 160 && session.IsActive &&
                        session.Phase != NarrativeInteractionPhase.Reading; frame++) yield return null;
                    Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Reading),
                        session.LastFailureReason + "; " + session.CameraDirector.LastRejectedShotReason);
                    yield return null;
                    yield return null;
                }

                void AssertClosed()
                {
                    Assert.That(blocker.enabled && !blocker.isTrigger, Is.True);
                    Assert.That(Vector3.Distance(door.position, closedPosition), Is.LessThan(.001f));
                    Assert.That(Quaternion.Angle(door.rotation, closedRotation), Is.LessThan(.001f));
                    Assert.That(door.lossyScale, Is.EqualTo(closedScale));
                    Assert.That(SceneTransitionService.IsTransitioning, Is.False);
                    Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneIds.CombatTest));
                }

                void AssertReleased()
                {
                    Assert.That(session.IsActive || root.Prompt.HasHeldPage || BarMinigameModalLock.IsAnyLocked, Is.False);
                    Assert.That(interactor.InputEnabled && root.Player.Motor.InputEnabled &&
                        root.CameraFollow.OrbitInputEnabled, Is.True);
                    Assert.That(root.CameraFollow.FixedPoseActive, Is.False);
                    Assert.That(BarPromenade.Rendering.CinematicDepthOfField.IsActive, Is.False);
                    Assert.That(SceneTransitionService.IsTransitioning, Is.False);
                }
            }
            finally
            {
                session?.RestoreImmediate();
                if (root != null) root.AutomaticSimulation = false;
                Time.captureDeltaTime = previousDelta;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
                input.TearDown();
            }
        }

        private static NarrativeInteraction FindWarehouseEntranceInspection(CombatTestRoot root)
        {
            var targets = root.GetComponentsInChildren<NarrativeInteraction>(true)
                .Where(candidate => candidate.Definition?.Id == "warehouse-entrance-door").ToArray();
            Assert.That(targets.Length, Is.EqualTo(1), "Each layout owns exactly one entrance inspection.");
            return targets[0];
        }

        private static void AssertWarehouseEntranceAsset(NarrativeInteraction target, WarehouseArenaPlan plan)
        {
            Transform door = target.SubjectRoot;
            Assert.That(door.name, Is.EqualTo("Warehouse Entrance Door"));
            Assert.That(door.localPosition, Is.EqualTo(plan.EntranceDoorPosition));
            Assert.That(plan.EntranceDoorPosition, Is.EqualTo(new Vector3(0f, 0f, -11.85f)));
            Assert.That(Vector3.Distance(plan.EntranceDoorPosition, plan.HeroSpawn), Is.LessThan(4f));
            Assert.That(Vector3.Dot(plan.EntranceDoorPosition - plan.HeroSpawn,
                plan.GoalDoorPosition - plan.HeroSpawn), Is.LessThan(0f));
            Assert.That(target.Definition.PromptKey, Is.EqualTo("interaction.warehouse_door"));
            Assert.That(target.Definition.RequiresRunningGameTime, Is.False);
            Assert.That(target.Definition.Pages.Count, Is.EqualTo(1));
            Assert.That(target.Activity, Is.Null);
            Assert.That(target.Staging.AuthoredFocusBoundsOnly, Is.True);
            Assert.That(target.Staging.CameraMode, Is.EqualTo(NarrativeCameraMode.ObjectSide));
            Assert.That(Vector3.Distance(target.Staging.Entry.RootRotation * Vector3.forward, Vector3.back), Is.LessThan(.001f));
            AssertSharedApartmentEntrance(door);
            BoxCollider blocker = door.GetComponent<BoxCollider>();
            Assert.That(blocker, Is.Not.Null);
            Assert.That(blocker.center, Is.EqualTo(new Vector3(0f, 1.15f, 0f)));
            Assert.That(blocker.size, Is.EqualTo(new Vector3(1.32f, 2.3f, .22f)));
            Bounds visualBounds = door.GetComponentInChildren<Renderer>().bounds;
            foreach (Renderer renderer in door.GetComponentsInChildren<Renderer>()) visualBounds.Encapsulate(renderer.bounds);
            Assert.That(visualBounds.min.z, Is.EqualTo(-12f).Within(.001f), "The rear architrave meets the inner wall plane.");
        }

        private static void AssertSharedApartmentEntrance(Transform door)
        {
            Assert.That(door, Is.Not.Null);
            Assert.That(door.Find("Home Exit Door Patch"), Is.Null, "The broad repair patch is retired.");
            Assert.That(door.GetComponentsInChildren<MeshFilter>().Length, Is.EqualTo(ApartmentEntranceDoor.PartNames.Count));
            var library = HomeInteriorModelLibrary.Load();
            foreach (string name in ApartmentEntranceDoor.PartNames)
            {
                Transform part = door.Find(name);
                Assert.That(part, Is.Not.Null);
                HomeAuthoredPart source = library.Binding(name, "Box");
                Assert.That(part.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(source.mesh));
                Assert.That(part.localPosition, Is.EqualTo(source.Position));
                Assert.That(part.localScale, Is.EqualTo(Vector3.one));
                Renderer renderer = part.GetComponent<Renderer>();
                Assert.That(renderer.sharedMaterial, Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
                Assert.That(Vector3.Distance(renderer.bounds.size, source.Size), Is.LessThan(.006f),
                    name + ": the assembly must retain the measured authored metre size.");
                if (name == "Home Exit Door")
                {
                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties);
                    Assert.That(properties.GetTexture("_BaseMap"),
                        Is.SameAs(Resources.Load<Texture2D>(HomeSurfaceAppearance.DarkWoodTextureResourcePath)));
                }
            }
            Assert.That(Vector3.Distance(door.Find("Home Exit Door").GetComponent<Renderer>().bounds.size,
                new Vector3(1.12f, 2.16f, .052f)), Is.LessThan(.006f));
        }

        private static void AssertHomeEntranceWallAndTravel(HomeInteriorRoot home, Transform door)
        {
            Renderer frame = door.Find("Home Entry Door Frame").GetComponent<Renderer>();
            Renderer left = home.Room.Find("Home Entry Wall Left Door Infill").GetComponent<Renderer>();
            Renderer right = home.Room.Find("Home Entry Wall Right Door Infill").GetComponent<Renderer>();
            Renderer transom = home.Room.Find("Home Entry Door Transom Infill").GetComponent<Renderer>();
            Assert.That(left.bounds.max.x, Is.EqualTo(frame.bounds.min.x).Within(.002f));
            Assert.That(right.bounds.min.x, Is.EqualTo(frame.bounds.max.x).Within(.002f));
            Assert.That(transom.bounds.min.y, Is.EqualTo(frame.bounds.max.y).Within(.002f));
            Assert.That(left.GetComponent<Collider>() != null && right.GetComponent<Collider>() != null &&
                transom.GetComponent<Collider>() != null, Is.True, "The smaller frame remains sealed into the existing wall.");
            Assert.That(door.GetComponentsInChildren<Collider>(), Is.Empty, "Home retains its plan-owned exit collision.");
            Assert.That(home.Exit.transform.localPosition, Is.EqualTo(new Vector3(0f, 1.05f, -3.48f)));
            Assert.That(home.Exit.GetComponent<BoxCollider>().size, Is.EqualTo(new Vector3(2.2f, 2.1f, .65f)));
            PlayerDoorActionPlan action = home.Exit.GetComponent<PlayerDoorActionTarget>().Plan;
            Assert.That(Vector3.Distance(action.EntryRootPosition,
                home.transform.TransformPoint(new Vector3(0f, PlayerFactory.GroundedRootOffset, -3.25f))), Is.LessThan(.001f));
            Assert.That(action.EntryFacingDirection, Is.EqualTo(Vector3.back));
            Assert.That(SceneTransitionService.IsTransitioning, Is.False);
        }
    }
}
