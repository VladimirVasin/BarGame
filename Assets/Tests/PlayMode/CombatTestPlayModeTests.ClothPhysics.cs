using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_CombatClothRemainsRigidAcrossMovementWeaponsAndLifecycle()
        {
            bool previousBakes = SecondaryMotionBakeRuntime.Enabled;
            int? previousSeed = CombatTestRoot.WarehouseSeedForTests;
            var input = new InputTestFixture();
            bool inputStarted = false;
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = 0;
                var pacing = new CombatEncounterSettings(3, 60f, 60f, 60f, 60f);
                Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                    new CombatLoadoutSelection(CombatWeaponId.Pistol), CombatTestMode.Endless, pacing)), Is.True);
                yield return AwaitSelectedCombatRange();
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                Assert.That(root.LiveOpponentCount, Is.Zero);

                var visual = (Player3DCharacterPresentation)root.Player.Visual;
                Player3DAssetRegistry rig = visual.Registry;
                PlayerClothPhysicsPolicy policy = root.GetComponent<PlayerClothPhysicsPolicy>();
                PlayerJacketCloth jacket = rig.GetComponent<PlayerJacketCloth>();
                PlayerScarfPresentation scarf = rig.GetComponent<PlayerScarfPresentation>();
                PlayerHair hair = rig.GetComponent<PlayerHair>();
                GameObject shotgun = root.Hero.CarriedWeapon(CombatWeaponId.Shotgun);
                CombatShotgunSling sling = shotgun.GetComponent<CombatShotgunSling>();
                Assert.That(policy, Is.Not.Null);
                Assert.That(jacket != null && scarf != null && hair != null && sling != null, Is.True);
                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.Scarf), Is.True);
                Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.Scarf, true), Is.True);
                yield return null;
                SynchronizeRigidCombatCloth(visual, scarf, sling);

                var off = new CombatClothSnapshot(jacket, scarf, sling);
                Assert.That(off.JacketSteps + off.ScarfSteps + off.SlingSteps, Is.Zero,
                    "Combat must choose its policy before the first character or weapon physics pass.");
                Assert.That(off.JacketBaked + off.JacketFallbacks + off.JacketTransitions + off.SlingBaked, Is.Zero);
                Assert.That(off.NativeSnapshots + off.ProjectionAllocations, Is.Zero);
                AssertCombatClothOff(policy, jacket, scarf, sling, off);
                Assert.That(scarf.IsEquipped && scarf.IsVisible, Is.True,
                    "The idle proof must actually display the equipped scarf.");
                AssertCombatJacketUsesOriginalSkin(jacket, rig);
                CaptureRigidCombatCloth("Test combat rigid cloth idle", rear: false);

                // Keep the ordinary recorded-hair path active while proving that neither
                // cloth owner even asks the shared recording service for a sample.
                SecondaryMotionBakeRuntime.Enabled = true;
                long hairCalls = hair.TotalApplyCalls;
                long hairResults = CombatBakeRequests(SecondaryMotionBakePart.Hair);
                yield return AdvanceRigidCombatCloth(12, policy, jacket, scarf, sling, off);
                Assert.That(hair.TotalApplyCalls, Is.GreaterThan(hairCalls));
                Assert.That(CombatBakeRequests(SecondaryMotionBakePart.Hair), Is.GreaterThan(hairResults),
                    "Disabling clothes must leave the existing recorded/live hair selection running.");

                // The live-hair phase also makes a decoder regression unambiguous:
                // no consumer is allowed to load a recording while playback is disabled.
                SecondaryMotionBakeRuntime.Enabled = false;
                long decodeCount = SecondaryMotionBakeRuntime.Diagnostics.DecodeCount;
                long hairSteps = hair.TotalSimulationSteps;
                input.Setup();
                inputStarted = true;
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                InputSystem.AddDevice<Mouse>();
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                root.Hero.ResetActor(new Vector3(0f, PlayerFactory.GroundedRootOffset, -6f), Vector3.forward);
                root.Hero.SetCombatFocused(false);
                yield return null; // Equipment gets its real late pass after the setup teleport.
                Vector3 walkStart = root.Hero.transform.position;
                Vector3 scarfStart = scarf.TailRenderer.transform.position;
                input.Press(keyboard.wKey, queueEventOnly: true);
                yield return AdvanceRigidCombatCloth(24, policy, jacket, scarf, sling, off);
                input.Release(keyboard.wKey, queueEventOnly: true);
                yield return null;
                Assert.That(Vector3.Distance(root.Hero.transform.position, walkStart), Is.GreaterThan(.25f));
                Assert.That(Vector3.Distance(scarf.TailRenderer.transform.position, scarfStart), Is.GreaterThan(.2f),
                    "A rigid scarf follows the moving head rather than remaining at its old world pose.");
                Assert.That(hair.TotalSimulationSteps, Is.GreaterThan(hairSteps),
                    "The same real frames must still integrate live hair.");

                // This fixture drives the simulation clock explicitly; its root
                // therefore does not poll combat input in Update. Request the same
                // actor aim used by that input owner without advancing it twice.
                root.Hero.SetPistolAim(true, root.Hero.transform.position +
                    root.Hero.transform.forward * 20f + Vector3.up * 1.3f);
                yield return AdvanceRigidCombatCloth(36, policy, jacket, scarf, sling, off);
                Assert.That(root.Hero.Firearm.AimRequested && root.Hero.Firearm.IsAiming, Is.True);
                CaptureRigidCombatCloth("Test combat rigid cloth aim", rear: false);
                root.Hero.SetPistolAim(false);
                yield return AdvanceRigidCombatCloth(30, policy, jacket, scarf, sling, off);

                // This closed route visits all six ordered pairs among the three
                // original weapons, including both long-weapon transfers.
                GameObject pistol = root.Hero.CarriedWeapon(CombatWeaponId.Pistol);
                GameObject crowbar = root.Hero.CarriedWeapon(CombatWeaponId.Crowbar);
                bool capturedAccess = false;
                foreach (CombatWeaponId target in new[] { CombatWeaponId.Crowbar, CombatWeaponId.Shotgun,
                    CombatWeaponId.Pistol, CombatWeaponId.Shotgun, CombatWeaponId.Crowbar, CombatWeaponId.Pistol })
                {
                    Assert.That(root.Hero.TrySelectWeapon(target), Is.True);
                    for (int frame = 0; frame < 360 && (root.Hero.IsSwitchingWeapon ||
                        !root.Hero.HasWeaponInHand || root.Hero.WeaponId != target); frame++)
                    {
                        root.Tick(1f / 30f);
                        yield return null;
                        SynchronizeRigidCombatCloth(visual, scarf, sling);
                        AssertCombatClothOff(policy, jacket, scarf, sling, off);
                        if (!capturedAccess && root.Hero.IsSwitchingWeapon &&
                            root.Hero.WeaponCarryActionWeapon == CombatWeaponId.Pistol &&
                            root.Hero.WeaponCarryActionIsDraw && root.Hero.WeaponCarryActionProgress > .48f)
                        {
                            AssertCombatJacketAccessMovesPanel(jacket);
                            CaptureRigidCombatCloth("Test combat pistol jacket access", rear: false);
                            capturedAccess = true;
                        }
                    }
                    Assert.That(root.Hero.IsSwitchingWeapon, Is.False);
                    Assert.That(root.Hero.WeaponId, Is.EqualTo(target));
                    Assert.That(root.Hero.HasWeaponInHand, Is.True);
                    Assert.That(root.Hero.CarriedWeapon(CombatWeaponId.Pistol), Is.SameAs(pistol));
                    Assert.That(root.Hero.CarriedWeapon(CombatWeaponId.Crowbar), Is.SameAs(crowbar));
                    Assert.That(root.Hero.CarriedWeapon(CombatWeaponId.Shotgun), Is.SameAs(shotgun));
                }
                Assert.That(capturedAccess, Is.True);
                yield return AdvanceRigidCombatCloth(3, policy, jacket, scarf, sling, off);
                AssertCombatJacketUsesOriginalSkin(jacket, rig);
                CaptureRigidCombatCloth("Test combat rigid stowed sling", rear: true);

                Assert.That(root.Hero.TrySelectWeapon(CombatWeaponId.Shotgun), Is.True);
                yield return AdvanceRigidCombatCloth(12, policy, jacket, scarf, sling, off);
                Assert.That(root.Hero.IsSwitchingWeapon, Is.True);
                float heldCarryProgress = root.Hero.WeaponCarryActionProgress;
                Assert.That(heldCarryProgress, Is.InRange(.01f, .99f));
                Vector3 heldRoot = root.Hero.transform.position;
                Assert.That(root.PauseMenu.Open(), Is.True);
                Transform head = rig.Anchors.Head;
                // Opening the modal changes the motor's animation target. The
                // rigid scarf must follow whichever final head pose owns that
                // frame; its attachment, world root and committed action stay
                // fixed. Use rotation space so this offset remains in metres
                // despite the imported model root's unit conversion scale.
                Vector3 heldTailOffset = Quaternion.Inverse(head.rotation) *
                    (scarf.TailRenderer.transform.position - head.position);
                Quaternion heldTailRotation = Quaternion.Inverse(head.rotation) * scarf.TailRenderer.transform.rotation;
                root.Tick(1f);
                yield return null;
                SynchronizeRigidCombatCloth(visual, scarf, sling);
                AssertCombatClothOff(policy, jacket, scarf, sling, off);
                Vector3 pausedTailOffset = Quaternion.Inverse(head.rotation) *
                    (scarf.TailRenderer.transform.position - head.position);
                Quaternion pausedTailRotation = Quaternion.Inverse(head.rotation) * scarf.TailRenderer.transform.rotation;
                Assert.That(Vector3.Distance(heldTailOffset, pausedTailOffset), Is.LessThan(.002f));
                Assert.That(Quaternion.Angle(heldTailRotation, pausedTailRotation), Is.LessThan(.1f));
                Assert.That(root.Hero.transform.position, Is.EqualTo(heldRoot));
                Assert.That(root.Hero.WeaponCarryActionProgress, Is.EqualTo(heldCarryProgress));
                Assert.That(root.Hero.IsSwitchingWeapon, Is.True);
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Cloth pause did not release input.");

                yield return CompleteRigidClothWeapon(CombatWeaponId.Shotgun, policy, jacket, scarf, sling, off);
                root.Hero.SendMessage("DropWeapon", SendMessageOptions.RequireReceiver);
                Assert.That(root.Hero.CarriedWeaponLocation(CombatWeaponId.Shotgun), Is.EqualTo(CombatWeaponCarryLocation.Dropped));
                Assert.That(shotgun.transform.IsChildOf(root.Hero.transform), Is.False);
                Rigidbody weaponBody = shotgun.GetComponent<Rigidbody>();
                Collider[] droppedShapes = shotgun.GetComponentsInChildren<Collider>();
                float floorY = root.Hero.transform.position.y - PlayerFactory.GroundedRootOffset;
                bool droppedOnFloor = false;
                for (int frame = 0; frame < 90; frame++)
                {
                    yield return new WaitForFixedUpdate();
                    SynchronizeRigidCombatCloth(visual, scarf, sling);
                    AssertCombatClothOff(policy, jacket, scarf, sling, off);
                    if (frame < 34 || !RigidCombatWeaponOnFloor(weaponBody, droppedShapes, floorY)) continue;
                    droppedOnFloor = true;
                    break;
                }
                Assert.That(droppedOnFloor, Is.True, "The sling capture must inspect the actual landed floor prop.");
                CaptureRigidCombatCloth("Test combat rigid dropped sling", rear: false,
                    focusOverride: weaponBody.worldCenterOfMass);

                // Reclaim the very same floor prop through the production receipt screen.
                weaponBody.linearVelocity = weaponBody.angularVelocity = Vector3.zero;
                Vector3 stand = shotgun.transform.position + Vector3.right * .75f;
                stand.y = root.Hero.transform.position.y;
                root.Player.Motor.Teleport(stand);
                Physics.SyncTransforms();
                yield return null;
                CombatDroppedWeaponPickup pickup = shotgun.GetComponent<CombatDroppedWeaponPickup>();
                Assert.That(pickup.TryBeginPickup(root.Player.Interactor), Is.True);
                WorldItemFoundScreen found = WorldItemFoundScreen.For(root.Player.Interactor);
                yield return WaitFor(() => found.IsShowing, "The dropped shotgun did not reach its receipt screen.");
                yield return null;
                Assert.That(found.Confirm(), Is.True);
                yield return WaitFor(() => !found.IsPresenting, "Shotgun pickup retained input ownership.");
                yield return CompleteRigidClothWeapon(CombatWeaponId.Shotgun, policy, jacket, scarf, sling, off);
                Assert.That(root.Hero.Weapon, Is.SameAs(shotgun));

                root.Hero.State.ReceiveProjectileHit(root.Hero.State.Settings.MaxHealth);
                root.Hero.Step(CombatAssetProvider.DefeatHandoffSeconds + .05f);
                Assert.That(root.Hero.IsRagdollActive, Is.True);
                bool bodyFallen = false;
                for (int frame = 0; frame < 120; frame++)
                {
                    yield return new WaitForFixedUpdate();
                    SynchronizeRigidCombatCloth(visual, scarf, sling);
                    AssertCombatClothOff(policy, jacket, scarf, sling, off);
                    if (frame < 34 || !root.Hero.Ragdoll.HasGroundContact ||
                        rig.Anchors.Head.position.y > floorY + .85f ||
                        root.Hero.Ragdoll.PelvisBody.position.y > floorY + .5f) continue;
                    bodyFallen = true;
                    break;
                }
                Assert.That(bodyFallen, Is.True, "The cloth capture must show a physically fallen body rather than its upright handoff.");
                Assert.That(scarf.TailRenderer.transform.IsChildOf(rig.transform), Is.True);
                CaptureRigidCombatCloth("Test combat rigid cloth fallen hero", rear: false);
                root.ResetRound();
                yield return AdvanceRigidCombatCloth(8, policy, jacket, scarf, sling, off);
                Assert.That(root.Hero.IsRagdollActive, Is.False);
                Assert.That(root.Hero.CarriedWeapon(CombatWeaponId.Shotgun), Is.SameAs(shotgun));
                AssertCombatJacketUsesOriginalSkin(jacket, rig);
                Assert.That(SecondaryMotionBakeRuntime.Diagnostics.DecodeCount, Is.EqualTo(decodeCount));
                CaptureRigidCombatCloth("Test combat rigid cloth reset", rear: false);

                // The area policy is reversible. Restoring ordinary simulation
                // exercises every solver once, then disabling it discards inertia.
                policy.SetSimulationEnabled(true);
                Assert.That(jacket.ClothPhysicsEnabled && scarf.PhysicsSimulationEnabled && sling.PhysicsSimulationEnabled, Is.True);
                yield return AdvanceRigidCombatCloth(8);
                Assert.That(jacket.TotalSimulationSteps, Is.GreaterThan(off.JacketSteps));
                Assert.That(scarf.TotalSimulationSteps, Is.GreaterThan(off.ScarfSteps));
                Assert.That(sling.TotalSimulationSteps, Is.GreaterThan(off.SlingSteps));
                policy.SetSimulationEnabled(false);
                var restoredOff = new CombatClothSnapshot(jacket, scarf, sling);
                SecondaryMotionBakeRuntime.Enabled = true;
                yield return AdvanceRigidCombatCloth(8, policy, jacket, scarf, sling, restoredOff);
                Assert.That(jacket.MaximumSpeed, Is.Zero);
                AssertCombatJacketUsesOriginalSkin(jacket, rig);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (inputStarted) input.TearDown();
                SecondaryMotionBakeRuntime.Enabled = previousBakes;
                CombatTestRoot.WarehouseSeedForTests = previousSeed;
            }
        }

        private IEnumerator AdvanceRigidCombatCloth(int frames, PlayerClothPhysicsPolicy policy = null,
            PlayerJacketCloth jacket = null, PlayerScarfPresentation scarf = null,
            CombatShotgunSling sling = null, CombatClothSnapshot expected = default)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                root.Tick(1f / 60f);
                yield return null;
                var visual = (Player3DCharacterPresentation)root.Player.Visual;
                if (policy == null) { visual.ReapplyLatePresentationPose(); continue; }
                SynchronizeRigidCombatCloth(visual, scarf, sling);
                AssertCombatClothOff(policy, jacket, scarf, sling, expected);
            }
        }

        private IEnumerator CompleteRigidClothWeapon(CombatWeaponId target, PlayerClothPhysicsPolicy policy,
            PlayerJacketCloth jacket, PlayerScarfPresentation scarf, CombatShotgunSling sling, CombatClothSnapshot expected)
        {
            for (int frame = 0; frame < 360 && (root.Hero.IsSwitchingWeapon ||
                !root.Hero.HasWeaponInHand || root.Hero.WeaponId != target); frame++)
                yield return AdvanceRigidCombatCloth(1, policy, jacket, scarf, sling, expected);
            Assert.That(root.Hero.IsSwitchingWeapon, Is.False);
            Assert.That(root.Hero.HasWeaponInHand && root.Hero.WeaponId == target, Is.True);
        }

        private readonly struct CombatClothSnapshot
        {
            internal readonly long JacketSteps, ScarfSteps, SlingSteps, JacketBaked, JacketFallbacks,
                JacketTransitions, SlingBaked, NativeSnapshots, ProjectionAllocations, JacketRequests, SlingRequests;
            internal CombatClothSnapshot(PlayerJacketCloth jacket, PlayerScarfPresentation scarf, CombatShotgunSling sling)
            {
                JacketSteps = jacket.TotalSimulationSteps; ScarfSteps = scarf.TotalSimulationSteps;
                SlingSteps = sling.TotalSimulationSteps; JacketBaked = jacket.TotalBakedPoseCalls;
                JacketFallbacks = jacket.TotalBakedFallbacks; JacketTransitions = jacket.TotalBakedTransitionCalls;
                SlingBaked = sling.BakedPlaybackFrames; NativeSnapshots = jacket.NativeSnapshotCaptureCount;
                ProjectionAllocations = sling.ProjectionWorkspaceAllocations;
                JacketRequests = CombatBakeRequests(SecondaryMotionBakePart.Jacket);
                SlingRequests = CombatBakeRequests(SecondaryMotionBakePart.Sling);
            }
        }

        private static long CombatBakeRequests(SecondaryMotionBakePart part)
        {
            long count = 0;
            foreach (SecondaryMotionBakeResult result in Enum.GetValues(typeof(SecondaryMotionBakeResult)))
                count += SecondaryMotionBakeRuntime.ResultCount(part, result);
            return count;
        }

        private static void SynchronizeRigidCombatCloth(Player3DCharacterPresentation visual,
            PlayerScarfPresentation scarf, CombatShotgunSling sling)
        {
            // The coroutine resumes before LateUpdate. Observe the same final
            // rig and rigid attachment transforms as a rendered late frame.
            // The off-only scarf branch changes transforms and publishes rest
            // vertices for hair contacts; it advances no presentation clock.
            Assert.That(scarf.PhysicsSimulationEnabled, Is.False);
            visual.ReapplyLatePresentationPose();
            scarf.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);
            sling.Present();
        }

        private static bool RigidCombatWeaponOnFloor(Rigidbody body, Collider[] shapes, float floorY)
        {
            if (body.linearVelocity.magnitude > .6f || body.angularVelocity.magnitude > 2f) return false;
            foreach (Collider shape in shapes)
                if (shape.enabled && !shape.isTrigger && shape.attachedRigidbody == body &&
                    shape.bounds.min.y <= floorY + .06f) return true;
            return false;
        }

        private static void AssertCombatClothOff(PlayerClothPhysicsPolicy policy, PlayerJacketCloth jacket,
            PlayerScarfPresentation scarf, CombatShotgunSling sling, CombatClothSnapshot expected)
        {
            Assert.That(policy.SimulationEnabled || jacket.ClothPhysicsEnabled || scarf.PhysicsSimulationEnabled || sling.PhysicsSimulationEnabled, Is.False);
            Assert.That(jacket.TotalSimulationSteps, Is.EqualTo(expected.JacketSteps));
            Assert.That(scarf.TotalSimulationSteps, Is.EqualTo(expected.ScarfSteps));
            Assert.That(sling.TotalSimulationSteps, Is.EqualTo(expected.SlingSteps));
            Assert.That(jacket.LastSimulationSteps + scarf.LastSimulationSteps + sling.LastSimulationSteps, Is.Zero);
            Assert.That(jacket.LastContactCount + scarf.LastSurfaceContactPassCount + sling.SteadyProjectionIterations + sling.SteadyLengthRepairIterations, Is.Zero);
            Assert.That(jacket.TotalBakedPoseCalls, Is.EqualTo(expected.JacketBaked));
            Assert.That(jacket.TotalBakedFallbacks, Is.EqualTo(expected.JacketFallbacks));
            Assert.That(jacket.TotalBakedTransitionCalls, Is.EqualTo(expected.JacketTransitions));
            Assert.That(sling.BakedPlaybackFrames, Is.EqualTo(expected.SlingBaked));
            Assert.That(jacket.NativeSnapshotCaptureCount, Is.EqualTo(expected.NativeSnapshots));
            Assert.That(sling.ProjectionWorkspaceAllocations, Is.EqualTo(expected.ProjectionAllocations));
            Assert.That(CombatBakeRequests(SecondaryMotionBakePart.Jacket), Is.EqualTo(expected.JacketRequests));
            Assert.That(CombatBakeRequests(SecondaryMotionBakePart.Sling), Is.EqualTo(expected.SlingRequests));
            Assert.That(scarf.TailSimulation == null || !scarf.TailSimulation.IsActive, Is.True);
            Assert.That(scarf.IsEquipped, Is.True);
            CollectionAssert.AreEqual(scarf.TailRestVertices, scarf.TailRenderer.sharedMesh.vertices,
                "Disabled scarf vertices must remain the authored skin rather than frozen physical output.");
            Assert.That(sling.Ribbon.transform.IsChildOf(sling.transform), Is.True);
            Transform stock = CombatAssetProvider.FindAnchor(sling.gameObject, "SlingStock");
            Transform foreend = CombatAssetProvider.FindAnchor(sling.gameObject, "SlingForeend");
            Assert.That(Vector3.Distance(sling.WorldCenterlinePoint(0), stock.position), Is.LessThan(.002f));
            Assert.That(Vector3.Distance(sling.WorldCenterlinePoint(sling.CenterlinePointCount - 1), foreend.position), Is.LessThan(.002f));
        }

        private static void AssertCombatJacketUsesOriginalSkin(PlayerJacketCloth jacket, Player3DAssetRegistry rig)
        {
            SkinnedMeshRenderer[] skins = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int surface = 0; surface < jacket.SurfaceCount; surface++)
            {
                bool found = false;
                foreach (SkinnedMeshRenderer skin in skins)
                    if (skin.sharedMesh == jacket.SourceMesh(surface))
                    {
                        found = true;
                        Assert.That(skin.bones.Length, Is.GreaterThan(0));
                        foreach (Transform bone in skin.bones) Assert.That(bone.IsChildOf(rig.ModelRoot), Is.True);
                    }
                Assert.That(found, Is.True, "The jacket returns to its original skinned surface outside deterministic hand access.");
            }
        }

        private static void AssertCombatJacketAccessMovesPanel(PlayerJacketCloth jacket)
        {
            Vector3[] source = jacket.SourceMesh(0).vertices;
            Vector3[] opened = jacket.DeformedMesh(0).vertices;
            float maximum = 0f;
            for (int vertex = 0; vertex < source.Length; vertex++)
                maximum = Mathf.Max(maximum, Vector3.Distance(source[vertex], opened[vertex]));
            Assert.That(maximum, Is.GreaterThan(.00001f), "The pistol hand still opens its jacket panel without a cloth solve.");
        }

        private void CaptureRigidCombatCloth(string name, bool rear, Vector3? focusOverride = null)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            float previousFov = camera.fieldOfView;
            try
            {
                var visual = (Player3DCharacterPresentation)root.Player.Visual;
                SynchronizeRigidCombatCloth(visual, visual.Registry.GetComponent<PlayerScarfPresentation>(),
                    root.Hero.CarriedWeapon(CombatWeaponId.Shotgun).GetComponent<CombatShotgunSling>());
                Vector3 focus = focusOverride ?? (root.Hero.IsRagdollActive ? root.Hero.BodyWorldPosition + Vector3.up * .4f :
                    root.Hero.transform.position + Vector3.up * 1.05f);
                Vector3 direction = root.Hero.transform.forward * (rear ? -1f : 1f);
                Vector3 offset = focusOverride.HasValue ? new Vector3(1.25f, .9f, 1.4f) :
                    direction * 2.7f + root.Hero.transform.right * .7f + Vector3.up * .25f;
                camera.transform.SetPositionAndRotation(focus + offset, Quaternion.LookRotation(-offset));
                camera.fieldOfView = 43f;
                const string folder = "Test combat rigid cloth";
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", folder, name + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, folder, name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                camera.fieldOfView = previousFov;
            }
        }
    }
}
