using System;
using System.Collections;
using System.Collections.Generic;
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
        [Explicit("Focused warehouse locker placement, moving doors and shared ammunition pickup, with production lighting captures.")]
        public IEnumerator CombatWarehouseLockers()
        {
            CombatTestRoot root = null;
            float previousDelta = Time.captureDeltaTime;
            bool previousBackground = Application.runInBackground;
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Time.captureDeltaTime = 1f / 60f;
            Application.runInBackground = true;
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
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;

                WarehouseArenaPlan plan = root.WarehousePlan;
                WarehouseLocker[] lockers = root.GetComponentsInChildren<WarehouseLocker>();
                Assert.That(lockers.Length, Is.EqualTo(9), "Keep the room banks and add two wall banks on each storey.");
                Assert.That(plan.HallwayLockers.Count, Is.EqualTo(6));
                var area = new WarehouseArenaArea(plan);
                var route = new List<Vector3>();
                var perFloor = new int[3];
                foreach (WarehouseLockerPlacement placement in plan.HallwayLockers)
                {
                    WarehouseLocker bank = FindTestLocker(lockers, placement.Position);
                    perFloor[placement.Floor]++;
                    Assert.That(Mathf.Abs(placement.Position.x), Is.EqualTo(4f).Within(.001f));
                    Assert.That(placement.Position.z, Is.EqualTo(-11.45f).Within(.001f));
                    Assert.That(area.FindRoute(plan.HeroSpawn, placement.Approach, route), Is.True,
                        "Each wall bank must have an unobstructed approach from the entrance.");
                    Assert.That(area.TryGetHeight(placement.Approach, out float floor, .3f), Is.True);
                    Assert.That(floor, Is.EqualTo(placement.Floor * WarehouseArenaPlan.LevelRise));
                    Assert.That(bank.IsOpen || bank.IsMoving, Is.False);
                    Assert.That(bank.GetComponentsInChildren<MeshCollider>(), Is.Not.Empty);
                }
                Assert.That(perFloor, Is.EqualTo(new[] { 2, 2, 2 }));
                AssertTestLockerAmmoStorageSeeds(lockers);
                CombatAmmoPickup independentPack = root.AmmoSpawns.GetSlot(0);
                WarehouseLocker independentBank = Array.Find(lockers,
                    candidate => Vector3.Distance(candidate.ShelfPosition(), independentPack.transform.position) < .01f);
                Assert.That(independentBank, Is.Not.Null);
                yield return VerifyTestLockerIndependentDoors(root, independentBank, independentPack, input, keyboard);

                Camera camera = root.CameraFollow.Camera;
                Assert.That(root.Flashlight.SpotLight.isActiveAndEnabled, Is.True);
                for (int slot = 0; slot < root.AmmoSpawns.SlotCount; slot++)
                {
                    CombatAmmoPickup pack = root.AmmoSpawns.GetSlot(slot);
                    WarehouseLocker bank = Array.Find(lockers,
                        candidate => Vector3.Distance(candidate.ShelfPosition(), pack.transform.position) < .01f);
                    Assert.That(bank, Is.Not.Null, "Ammunition must rest inside an actual cabinet.");
                    Assert.That(pack.transform.position, Is.EqualTo(plan.AmmoAnchors[slot]));
                    Bounds bounds = WorldItemInspectionPresenter.CalculateWorldBounds(pack.Model);
                    Assert.That(bounds.min.y, Is.EqualTo(pack.transform.position.y + .015f).Within(.01f));
                    Assert.That(bounds.size.magnitude, Is.GreaterThan(.13f), "The enlarged pack must retain a readable physical silhouette.");
                    PlaceTestLockerHero(root, plan.AmmoApproaches[slot], bank);
                    Assert.That(pack.CanInteract(root.Player.Interactor), Is.False, "A closed bank must reject its concealed ammunition.");
                    Assert.That(bank.GetDoor(1).CanInteract(root.Player.Interactor), Is.True);
                    if (slot == 0)
                    {
                        SetTestLockerFollowCamera(root, bank);
                        for (int frame = 0; frame < 3; frame++) yield return null;
                        CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Closed ammunition locker");
                        AssertTestLockerDoorCollision(bank, true);
                        root.SendMessage("OnApplicationFocus", true);
                        GameInput.HandleApplicationFocus(true);
                    }
                    Assert.That(bank.BeginOpen(), Is.True);
                    bank.Advance(.12f);
                    float pausedOpening = bank.DoorOpenAmount;
                    Assert.That(pack.CanInteract(root.Player.Interactor), Is.False,
                        "A moving leaf must finish opening before its shelf becomes available.");
                    using (GameTimeScaleRuntime.AcquirePause())
                    {
                        bank.Advance(2f);
                        Assert.That(bank.DoorOpenAmount, Is.EqualTo(pausedOpening));
                    }
                    bank.Advance(2f);
                    Physics.SyncTransforms();
                    Assert.That(bank.IsOpen && !bank.IsMoving, Is.True);
                    SetTestLockerFollowCamera(root, bank);
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    CaptureCurrentCamera(camera, SceneIds.CombatTest,
                        "Test warehouse - Open ammunition locker on floor " + (Mathf.RoundToInt(bank.transform.position.y / WarehouseArenaPlan.LevelRise) + 1) +
                        (pack.WeaponId == CombatWeaponId.Pistol ? " pistol" : " shells"));
                    if (slot == 0)
                        yield return CaptureTestLockerCloseView(root, bank, "Test warehouse - Shelf ammunition close view");
                    // The Editor may lose native focus while writing a frame. This
                    // synthetic input fixture explicitly owns focus, as the ammo fixture does.
                    root.SendMessage("OnApplicationFocus", true);
                    GameInput.HandleApplicationFocus(true);
                    AssertTestLockerDoorCollision(bank, false);
                    Assert.That(pack.CanInteract(root.Player.Interactor), Is.True,
                        "The grounded hero must be able to take the shelf pack through the open doorway. " +
                        TestLockerPickupDiagnostic(root, bank, pack));
                    foreach (float side in new[] { -.2f, 0f, .2f })
                    {
                        root.Player.Motor.Teleport(plan.AmmoApproaches[slot] + bank.transform.right * side +
                            Vector3.up * PlayerFactory.GroundedRootOffset);
                        Physics.SyncTransforms();
                        Assert.That(pack.CanInteract(root.Player.Interactor), Is.True,
                            "The open shelf must be accessible across the normal standing approach. " +
                            TestLockerPickupDiagnostic(root, bank, pack));
                        root.Player.Interactor.SendMessage("Update");
                        Assert.That(root.Player.Interactor.ActiveInteractable, Is.SameAs(pack),
                            "The real interaction selection must offer Take across the middle shelf approach; side=" + side + ".");
                    }
                    PlaceTestLockerHero(root, plan.AmmoApproaches[slot], bank);
                }

                CombatAmmoPickup firstPack = root.AmmoSpawns.GetSlot(0);
                WarehouseLocker firstBank = Array.Find(lockers,
                    candidate => Vector3.Distance(candidate.ShelfPosition(), firstPack.transform.position) < .01f);
                PlaceTestLockerHero(root, plan.AmmoApproaches[0], firstBank);
                root.CameraFollow.ClearFixedPose();
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                IFirearmState firearm = root.Hero.CarriedFirearm(firstPack.WeaponId);
                WorldItemFoundScreen screen = WorldItemFoundScreen.For(root.Player.Interactor);
                Assert.That(firstPack.TryBeginPickup(root.Player.Interactor), Is.True);
                yield return AwaitTestLockerScreen(() => screen.IsShowing);
                yield return null;
                Assert.That(screen.ActiveItemId, Is.EqualTo(InventoryItemId.CombatPistolAmmo));
                Assert.That(firstBank.BeginClose(), Is.False, "A presenting shelf item keeps its source cabinet open.");
                yield return CaptureFishUi("Test warehouse - Locker ammunition shared screen");
                Assert.That(screen.Dismiss(), Is.True);
                yield return AwaitTestLockerScreen(() => !screen.IsPresenting);
                yield return null;
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                Assert.That(firstPack.IsAvailable && firearm.ReserveRounds == 0, Is.True);
                Assert.That(firstPack.Model.parent, Is.SameAs(firstPack.transform));

                Assert.That(firearm.AddReserveRounds(int.MaxValue), Is.EqualTo(firearm.ReserveCapacity));
                Assert.That(firstPack.TryBeginPickup(root.Player.Interactor), Is.True);
                yield return AwaitTestLockerScreen(() => screen.IsShowing);
                yield return null;
                Assert.That(screen.Confirm(), Is.True);
                Assert.That(screen.FeedbackKey, Is.EqualTo(WorldItemFoundScreen.NoRoomFeedbackKey));
                Assert.That(firstPack.IsAvailable && screen.IsShowing, Is.True);
                Assert.That(screen.Dismiss(), Is.True);
                yield return AwaitTestLockerScreen(() => !screen.IsPresenting);
                yield return null;
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                firearm.Reset();
                Assert.That(firstPack.TryBeginPickup(root.Player.Interactor), Is.True);
                yield return AwaitTestLockerScreen(() => screen.IsShowing);
                yield return null;
                Assert.That(screen.Confirm(), Is.True);
                Assert.That(screen.Confirm(), Is.False);
                Assert.That(firearm.ReserveRounds, Is.EqualTo(firstPack.RoundsPerPickup));
                yield return AwaitTestLockerScreen(() => !screen.IsPresenting);
                yield return null;
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                Assert.That(firstPack.IsAvailable, Is.False);
                Assert.That(firstBank.BeginClose(), Is.True);
                firstBank.Advance(2f);
                Assert.That(firstBank.IsOpen || firstBank.IsMoving, Is.False);
                AssertTestLockerDoorCollision(firstBank, true);

                WarehouseArenaPlacement roomPlacement = default;
                foreach (WarehouseArenaPlacement placement in plan.ModulePlacements)
                    if (placement.Kind == "LockerProps") { roomPlacement = placement; break; }
                WarehouseLocker roomBank = FindTestLocker(lockers, roomPlacement.Position);
                Vector3 roomApproach = roomBank.transform.TransformPoint(new Vector3(0f, 0f, -1.35f));
                PlaceTestLockerHero(root, roomApproach, roomBank);
                if (!roomBank.IsOpen)
                {
                    Assert.That(roomBank.GetDoor(1).CanInteract(root.Player.Interactor), Is.True);
                    Assert.That(roomBank.BeginOpen(), Is.True);
                    roomBank.Advance(2f);
                }
                yield return CaptureTestLockerCloseView(root, roomBank, "Test warehouse - Open existing room lockers");
                root.ResetRound();
                Assert.That(root.WarehousePlan, Is.SameAs(plan));
                foreach (WarehouseLocker bank in lockers)
                    for (int bay = 0; bay < 3; bay++)
                    {
                        WarehouseLockerDoor leaf = bank.GetDoor(bay);
                        Assert.That(leaf.IsOpen || leaf.IsMoving || leaf.DoorOpenAmount > 0f, Is.False,
                            "Reset restores every retained cabinet leaf to its closed pose.");
                    }
                Assert.That(firstPack.IsAvailable && firstPack.Model.parent == firstPack.transform, Is.True);
                Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            }
            finally
            {
                if (root != null)
                {
                    WorldItemFoundScreen.For(root.Player.Interactor)?.Abandon();
                    root.CameraFollow.ClearFixedPose();
                    root.AutomaticSimulation = false;
                }
                input.TearDown();
                Time.captureDeltaTime = previousDelta;
                Application.runInBackground = previousBackground;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
            }
        }

        private static IEnumerator VerifyTestLockerIndependentDoors(CombatTestRoot root, WarehouseLocker bank,
            CombatAmmoPickup pack, InputTestFixture input, Keyboard keyboard)
        {
            WarehouseLockerDoor left = bank.GetDoor(0), middle = bank.GetDoor(1), right = bank.GetDoor(2);
            WorldItemFoundScreen screen = WorldItemFoundScreen.For(root.Player.Interactor);
            int reserve = root.Hero.CarriedFirearm(pack.WeaponId).ReserveRounds;
            Assert.That(bank.GetComponentsInChildren<WarehouseLockerDoor>(true).Length, Is.EqualTo(3));
            bank.ResetState();
            PlaceTestLockerDoorHero(root, bank, 0);
            yield return null;
            AssertTestLockerDoorSelection(root, left);
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            Assert.That(left.IsMoving, Is.True, "The actual E press starts only the selected left leaf.");
            Assert.That(middle.IsOpen || middle.IsMoving || right.IsOpen || right.IsMoving, Is.False);
            bank.Advance(2f);
            yield return null; // Keep E held across the completed door motion and another world Update.
            Assert.That(left.IsOpen, Is.True);
            PlaceTestLockerDoorHero(root, bank, 1);
            yield return null;
            Assert.That(middle.IsOpen || middle.IsMoving || right.IsOpen || right.IsMoving, Is.False,
                "Moving to another bay with E held cannot reuse the opening press.");
            Assert.That(pack.CanInteract(root.Player.Interactor), Is.False,
                "Opening the adjacent left leaf must not unlock the middle shelf's ammunition.");
            Assert.That(pack.IsPresenting || screen.IsPresenting, Is.False);
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return null;
            yield return CaptureTestLockerCloseView(root, bank, "Test warehouse - Only left locker door open");

            PlaceTestLockerDoorHero(root, bank, 2);
            yield return null;
            AssertTestLockerDoorSelection(root, right);
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return null;
            bank.Advance(.12f);
            Assert.That(left.IsOpen && right.IsMoving && !middle.IsOpen && !middle.IsMoving, Is.True,
                "Neighbouring leaves must retain different independent states.");
            float leftAmount = left.DoorOpenAmount, rightAmount = right.DoorOpenAmount;
            Quaternion leftPose = bank.DoorPivot(0).rotation, rightPose = bank.DoorPivot(2).rotation;
            using (GameTimeScaleRuntime.AcquirePause())
            {
                bank.Advance(2f);
                Assert.That(left.DoorOpenAmount, Is.EqualTo(leftAmount));
                Assert.That(right.DoorOpenAmount, Is.EqualTo(rightAmount));
                Assert.That(Quaternion.Angle(leftPose, bank.DoorPivot(0).rotation), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(rightPose, bank.DoorPivot(2).rotation), Is.LessThan(.001f));
            }
            PlaceTestLockerDoorHero(root, bank, 1);
            yield return null;
            AssertTestLockerDoorSelection(root, middle);
            Assert.That(pack.CanInteract(root.Player.Interactor), Is.False,
                "An opening right leaf still cannot expose the closed middle shelf.");
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            Assert.That(middle.IsMoving && right.IsMoving && left.IsOpen, Is.True,
                "A second leaf can begin opening while its neighbour is still moving.");
            Assert.That(pack.CanInteract(root.Player.Interactor), Is.False);
            yield return CaptureTestLockerCloseView(root, bank, "Test warehouse - Independent locker door motion");
            bank.Advance(2f);
            yield return null; // The held door-opening E must not become Take after its own shelf becomes reachable.
            Assert.That(left.IsOpen && middle.IsOpen && right.IsOpen, Is.True);
            Assert.That(pack.CanInteract(root.Player.Interactor), Is.True);
            Assert.That(pack.IsPresenting || screen.IsPresenting, Is.False);
            Assert.That(root.Hero.CarriedFirearm(pack.WeaponId).ReserveRounds, Is.EqualTo(reserve));
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return null;
            PlaceTestLockerDoorHero(root, bank, 0);
            AssertTestLockerDoorSelection(root, left);
            Assert.That(left.BeginClose(), Is.True);
            PlaceTestLockerDoorHero(root, bank, 2);
            AssertTestLockerDoorSelection(root, right);
            Assert.That(right.BeginClose(), Is.True);
            bank.Advance(.12f);
            Assert.That(left.IsMoving && right.IsMoving && middle.IsOpen, Is.True,
                "Closing either side must preserve the middle door and its stock gate.");
            PlaceTestLockerDoorHero(root, bank, 1);
            Assert.That(pack.CanInteract(root.Player.Interactor), Is.True);
            bank.Advance(2f);
            Assert.That(left.IsOpen || left.IsMoving || right.IsOpen || right.IsMoving, Is.False);
            Assert.That(middle.IsOpen && pack.CanInteract(root.Player.Interactor), Is.True);
            root.Player.Interactor.SendMessage("Update");
            Assert.That(root.Player.Interactor.ActiveInteractable, Is.SameAs(pack));
            yield return CaptureTestLockerCloseView(root, bank, "Test warehouse - Middle locker door open alone");
            bank.ResetState();
            for (int bay = 0; bay < 3; bay++)
            {
                WarehouseLockerDoor leaf = bank.GetDoor(bay);
                Assert.That(leaf.IsOpen || leaf.IsMoving || leaf.DoorOpenAmount > 0f, Is.False);
            }
            Assert.That(pack.CanInteract(root.Player.Interactor), Is.False);
            root.CameraFollow.ClearFixedPose();
        }

        private static void PlaceTestLockerDoorHero(CombatTestRoot root, WarehouseLocker bank, int bay)
        {
            PlaceTestLockerHero(root, bank.transform.TransformPoint(new Vector3((bay - 1) * .74f, 0f, -1.45f)), bank);
        }

        private static void AssertTestLockerDoorSelection(CombatTestRoot root, WarehouseLockerDoor door)
        {
            root.Player.Interactor.SendMessage("Update");
            Assert.That(root.Player.Interactor.ActiveInteractable, Is.SameAs(door),
                "The real nearest-target selection must offer the individual leaf in front of the hero; bay=" + door.Bay + ".");
        }

        private static void AssertTestLockerAmmoStorageSeeds(WarehouseLocker[] lockers)
        {
            // Existing room and wall banks are invariant across layout seeds; stock alone varies.
            var reached = new HashSet<WarehouseLocker>();
            foreach (int seed in new[] { 0, 1, 3, 5, 9, -1, int.MinValue, int.MaxValue })
            {
                WarehouseArenaPlan plan = WarehouseArenaPlanner.Create(seed);
                WarehouseArenaPlan repeated = WarehouseArenaPlanner.Create(seed);
                Assert.That(repeated.AmmoAnchors, Is.EqualTo(plan.AmmoAnchors), "A seed retains its stock shelves.");
                Assert.That(repeated.AmmoApproaches, Is.EqualTo(plan.AmmoApproaches), "A seed retains its stock approaches.");
                Assert.That(plan.AmmoAnchors.Count, Is.EqualTo(4));
                Assert.That(plan.AmmoApproaches.Count, Is.EqualTo(4));
                var selected = new HashSet<WarehouseLocker>();
                var floors = new int[3];
                for (int slot = 0; slot < plan.AmmoAnchors.Count; slot++)
                {
                    WarehouseLocker bank = Array.Find(lockers,
                        candidate => Vector3.Distance(candidate.ShelfPosition(), plan.AmmoAnchors[slot]) < .01f);
                    Assert.That(bank, Is.Not.Null, "Seeded stock must use an actual middle shelf; seed=" + seed + ".");
                    Assert.That(selected.Add(bank), Is.True, "Each stock pack uses a distinct cabinet bank.");
                    reached.Add(bank);
                    int floor = Mathf.RoundToInt(bank.transform.position.y / WarehouseArenaPlan.LevelRise);
                    Assert.That(floor, Is.InRange(0, 2));
                    floors[floor]++;
                    Vector3 approach = bank.transform.TransformPoint(new Vector3(0f, 0f, -1.45f));
                    Assert.That(Vector3.Distance(plan.AmmoApproaches[slot], approach), Is.LessThan(.01f),
                        "The stock approach must face the same cabinet as its shelf.");
                }
                foreach (int count in floors)
                    Assert.That(count, Is.GreaterThanOrEqualTo(1), "Every floor retains reachable ammunition.");
            }
            Assert.That(reached.Count, Is.GreaterThan(4), "Changing the layout seed must vary which banks carry ammunition.");
        }

        private static WarehouseLocker FindTestLocker(WarehouseLocker[] lockers, Vector3 position)
        {
            WarehouseLocker bank = Array.Find(lockers,
                candidate => Vector3.Distance(candidate.transform.position, position) < .001f);
            Assert.That(bank, Is.Not.Null, "Every planned bank must be instantiated at its measured position.");
            return bank;
        }

        private static void PlaceTestLockerHero(CombatTestRoot root, Vector3 approach, WarehouseLocker bank)
        {
            root.Hero.ResetActor(approach + Vector3.up * PlayerFactory.GroundedRootOffset, bank.transform.forward);
            root.Hero.SetCombatFocused(false);
            root.SendMessage("OnApplicationFocus", true);
            GameInput.HandleApplicationFocus(true);
            Physics.SyncTransforms();
        }

        private static void SetTestLockerFollowCamera(CombatTestRoot root, WarehouseLocker bank)
        {
            root.CameraFollow.ClearFixedPose();
            root.CameraFollow.Snap();
            root.CameraFollow.RotateYaw(Mathf.DeltaAngle(root.CameraFollow.Camera.transform.eulerAngles.y,
                bank.transform.eulerAngles.y));
            root.CameraFollow.Snap();
        }

        private static IEnumerator CaptureTestLockerCloseView(CombatTestRoot root, WarehouseLocker bank, string name)
        {
            Vector3 eye = bank.transform.TransformPoint(new Vector3(.33f, 1.5f, -2.2f));
            Vector3 target = bank.transform.TransformPoint(new Vector3(0f, 1.02f, -.08f));
            root.CameraFollow.SetFixedPose(eye, Quaternion.LookRotation(target - eye), 58f);
            Renderer[] surfaces = root.Hero.GetComponentsInChildren<Renderer>(true);
            var previous = new bool[surfaces.Length];
            for (int index = 0; index < surfaces.Length; index++)
            {
                previous[index] = surfaces[index].forceRenderingOff;
                surfaces[index].forceRenderingOff = true;
            }
            try
            {
                yield return null;
                for (int index = 0; index < surfaces.Length; index++) surfaces[index].forceRenderingOff = true;
                CaptureCurrentCamera(root.CameraFollow.Camera, SceneIds.CombatTest, name);
            }
            finally
            {
                for (int index = 0; index < surfaces.Length; index++)
                    if (surfaces[index] != null) surfaces[index].forceRenderingOff = previous[index];
            }
        }

        private static void AssertTestLockerDoorCollision(WarehouseLocker bank, bool expected)
        {
            Physics.SyncTransforms();
            Vector3 start = bank.transform.TransformPoint(new Vector3(0f, 1.245f, -1f));
            Vector3 end = bank.ShelfPosition() + Vector3.up * .18f;
            bool intersectsLeaf = false;
            var details = new System.Text.StringBuilder($" from={start:F4}, target={end:F4}; open={bank.DoorOpenAmount:F4};");
            for (int bay = 0; bay < 3; bay++)
            {
                Transform hinge = bank.DoorPivot(bay);
                details.Append($" hinge{bay}: pos={hinge.position:F4}, localPos={hinge.localPosition:F4}, rot={hinge.rotation.eulerAngles:F3}, localRot={hinge.localRotation.eulerAngles:F3}, up={hinge.up:F4}, right={hinge.right:F4}, forward={hinge.forward:F4};");
            }
            foreach (RaycastHit hit in Physics.RaycastAll(start, (end - start).normalized, Vector3.Distance(start, end),
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                bool leaf = hit.transform.IsChildOf(bank.DoorPivot(1));
                if (leaf) intersectsLeaf = true;
                details.Append($" hit={hit.transform.name}, middleLeaf={leaf}, point={hit.point:F4}, bankLocal={bank.transform.InverseTransformPoint(hit.point):F4}, normal={hit.normal:F4}, distance={hit.distance:F4};");
            }
            Assert.That(intersectsLeaf, Is.EqualTo(expected), "Collision must follow the actual hinged leaf." + details);
        }

        private static IEnumerator AwaitTestLockerScreen(Func<bool> ready)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, "The cabinet item must finish its shared screen transition.");
        }

        private static string TestLockerPickupDiagnostic(CombatTestRoot root, WarehouseLocker bank, CombatAmmoPickup pack)
        {
            PlayerInteractor interactor = root.Player.Interactor;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            object enabled = typeof(CombatAmmoPickup).GetField("interactionEnabled", flags)?.GetValue(pack);
            var source = typeof(CombatAmmoPickup).GetField("sourceAvailable", flags)?.GetValue(pack) as Func<bool>;
            Vector3 start = interactor.transform.position + Vector3.up * .8f;
            Vector3 ray = pack.InteractionPosition - start;
            var text = new System.Text.StringBuilder();
            text.Append($"hero={interactor.transform.position:F4}; bankLocalHero={bank.transform.InverseTransformPoint(interactor.transform.position):F4}; ");
            text.Append($"start={start:F4}; target={pack.InteractionPosition:F4}; distance={ray.magnitude:F4}; ");
            text.Append($"packActive={pack.isActiveAndEnabled}; interactionEnabled={enabled}; available={pack.IsAvailable}; presenting={pack.IsPresenting}; ");
            text.Append($"ownerActive={root.Hero.isActiveAndEnabled}; defeated={root.Hero.State.IsDefeated}; input={interactor.InputEnabled}; keyClaimed={interactor.InteractKeyClaimed}; ");
            text.Append($"context={GameInput.CanRead(GameInputContext.Contextual)}; ui={CounterMenuInput.IsBlockedByOtherUi()}; transition={SceneTransitionService.IsTransitioning}; ");
            text.Append($"paused={GameTimeScaleRuntime.IsPaused}; source={source?.Invoke()}; door={bank.DoorOpenAmount:F4}; hits:");
            foreach (RaycastHit hit in Physics.RaycastAll(start, ray.normalized, ray.magnitude,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                string path = hit.transform.name;
                for (Transform parent = hit.transform.parent; parent != null; parent = parent.parent)
                    path = parent.name + "/" + path;
                text.Append($" [{path}; point={hit.point:F4}; local={bank.transform.InverseTransformPoint(hit.point):F4}; distance={hit.distance:F4}]");
            }
            return text.ToString();
        }
    }
}
