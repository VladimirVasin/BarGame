using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused broken upper flight, surviving physical route and stocked side compartments, with production lighting captures.")]
        public IEnumerator CombatWarehouseRoutesAndStock()
        {
            float previousDelta = Time.captureDeltaTime;
            bool previousBackground = Application.runInBackground;
            CombatTestRoot root = null;
            Time.captureDeltaTime = 1f / 60f;
            Application.runInBackground = true;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            GameSessionState.BeginNewGame();
            try
            {
                foreach (int seed in new[] { 0, 1, 3 })
                    AssertWarehouseBrokenStairRoutes(WarehouseArenaPlanner.Create(seed));

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
                WarehouseArenaStair broken = plan.Stairs[2], intact = plan.Stairs[3];
                AssertWarehousePhysicalSurfaces(root);
                Assert.That(Array.Find(root.GetComponentsInChildren<Transform>(),
                    item => item.name == "Warehouse Broken Stair"), Is.Not.Null);

                root.Hero.ResetActor(broken.Foot + Vector3.back * .6f + Vector3.up * PlayerFactory.GroundedRootOffset,
                    Vector3.forward);
                root.Hero.ApplyMotorConstraint();
                Physics.SyncTransforms();
                for (int frame = 0; frame < 75; frame++)
                {
                    root.Hero.ApplyMotorConstraint();
                    root.Player.Motor.ApplyOwnedImpulseDisplacement(root.Hero, Vector3.forward * .05f);
                    yield return null;
                }
                Assert.That(root.Hero.transform.position.z,
                    Is.LessThan(broken.Foot.z + WarehouseArenaStair.FirstMissingTread * WarehouseArenaStair.TreadDepth),
                    "The real hero capsule must stop at the broken flight's remaining guard.");
                Assert.That(root.Hero.transform.position.y, Is.LessThan(broken.Top.y - 1f));

                Vector3 upper = intact.Top + Vector3.forward * .6f;
                root.Hero.ResetActor(intact.Foot + Vector3.back * .6f + Vector3.up * PlayerFactory.GroundedRootOffset,
                    Vector3.forward);
                root.Hero.ApplyMotorConstraint();
                Physics.SyncTransforms();
                yield return WalkWarehouseHero(root, upper);
                Assert.That(root.Hero.transform.position.y,
                    Is.EqualTo(WarehouseArenaPlan.TopHeight + PlayerFactory.GroundedRootOffset).Within(.1f),
                    "The surviving right flight must physically reach the third floor.");

                yield return CaptureWarehouseRouteView(root,
                    broken.Foot + Vector3.back * 1.7f,
                    broken.Foot + new Vector3(1.1f, 1.7f, -1.4f),
                    broken.Foot + new Vector3(0f, 2f, 3.2f),
                    "Test warehouse - Broken upper flight from below");
                yield return CaptureWarehouseRouteView(root,
                    broken.Top + Vector3.forward * 1.1f,
                    broken.Top + new Vector3(.6f, 1.7f, 1.2f),
                    broken.Foot + new Vector3(0f, 1f, 2f),
                    "Test warehouse - Broken upper flight from above");
                yield return CaptureWarehouseRouteView(root,
                    intact.Foot + Vector3.back * 1.7f,
                    intact.Foot + new Vector3(-1.1f, 1.7f, -1.4f),
                    intact.Foot + new Vector3(0f, 2f, 3.2f),
                    "Test warehouse - Remaining upper stair route");

                WarehouseLocker[] lockers = root.GetComponentsInChildren<WarehouseLocker>();
                AssertTestLockerAmmoStorageSeeds(lockers);
                Assert.That(root.AmmoSpawns.SlotCount, Is.EqualTo(12));
                var stockPerBank = new Dictionary<WarehouseLocker, int>();
                CombatAmmoPickup sidePack = null;
                WarehouseLocker sideBank = null;
                int sideBay = -1, sideSlot = -1;
                for (int slot = 0; slot < root.AmmoSpawns.SlotCount; slot++)
                {
                    CombatAmmoPickup pack = root.AmmoSpawns.GetSlot(slot);
                    WarehouseLocker bank = FindTestStockLocker(lockers, pack.transform.position, out int bay);
                    stockPerBank.TryGetValue(bank, out int count);
                    stockPerBank[bank] = count + 1;
                    Assert.That(pack.transform.position, Is.EqualTo(plan.AmmoAnchors[slot]));
                    PlaceTestLockerHero(root, plan.AmmoApproaches[slot], bank);
                    Assert.That(pack.CanInteract(root.Player.Interactor), Is.False,
                        "Every compartment keeps its own stock hidden behind its closed leaf.");
                    if (bay != 1 && sidePack == null)
                    { sidePack = pack; sideBank = bank; sideBay = bay; sideSlot = slot; }
                }
                Assert.That(stockPerBank.Count, Is.EqualTo(lockers.Length), "Every existing cabinet bank must carry ammunition.");
                Assert.That(sidePack, Is.Not.Null, "Side compartments must carry real pickups as well as the middle one.");

                WarehouseLocker doubleStockBank = null;
                foreach (KeyValuePair<WarehouseLocker, int> pair in stockPerBank)
                    if (pair.Value > 1) { doubleStockBank = pair.Key; break; }
                Assert.That(doubleStockBank, Is.Not.Null);
                for (int bay = 0; bay < WarehouseLocker.DoorCount; bay++)
                    Assert.That(doubleStockBank.BeginOpen(bay), Is.True);
                doubleStockBank.Advance(2f);
                PlaceTestLockerDoorHero(root, doubleStockBank, 1);
                yield return CaptureTestLockerCloseView(root, doubleStockBank,
                    "Test warehouse - Ammunition across open compartments");

                if (!sideBank.GetDoor(sideBay).IsOpen)
                { Assert.That(sideBank.BeginOpen(sideBay), Is.True); sideBank.Advance(2f); }
                PlaceTestLockerHero(root, plan.AmmoApproaches[sideSlot], sideBank);
                yield return CaptureTestLockerCloseView(root, sideBank,
                    "Test warehouse - Stocked side compartment");
                root.CameraFollow.ClearFixedPose();
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                Assert.That(sidePack.CanInteract(root.Player.Interactor), Is.True,
                    "The side compartment must expose its own pack after its actual leaf opens.");
                IFirearmState firearm = root.Hero.CarriedFirearm(sidePack.WeaponId);
                int reserveBefore = firearm.ReserveRounds;
                WorldItemFoundScreen screen = WorldItemFoundScreen.For(root.Player.Interactor);
                Assert.That(sidePack.TryBeginPickup(root.Player.Interactor), Is.True);
                yield return AwaitTestLockerScreen(() => screen.IsShowing);
                Assert.That(screen.Confirm(), Is.True);
                Assert.That(screen.Confirm(), Is.False);
                yield return AwaitTestLockerScreen(() => !screen.IsPresenting);
                Assert.That(firearm.ReserveRounds, Is.EqualTo(reserveBefore + sidePack.RoundsPerPickup));
                Assert.That(sidePack.IsAvailable, Is.False);
            }
            finally
            {
                if (root != null)
                {
                    root.AutomaticSimulation = false;
                    root.CameraFollow.ClearFixedPose();
                }
                Time.captureDeltaTime = previousDelta;
                Application.runInBackground = previousBackground;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
                GameSessionState.BeginNewGame();
            }
        }

        private static void AssertWarehouseBrokenStairRoutes(WarehouseArenaPlan plan)
        {
            int brokenCount = 0;
            foreach (WarehouseArenaStair stair in plan.Stairs) if (stair.IsBroken) brokenCount++;
            Assert.That(brokenCount, Is.EqualTo(1));
            WarehouseArenaStair broken = plan.Stairs[2], intact = plan.Stairs[3];
            Assert.That(broken.IsBroken && !intact.IsBroken, Is.True);
            Assert.That(broken.Foot, Is.EqualTo(new Vector3(-7.5f, WarehouseArenaPlan.LevelRise, -3f)));
            var area = new WarehouseArenaArea(plan);
            for (int step = WarehouseArenaStair.FirstMissingTread; step < WarehouseArenaStair.MissingTreadEnd; step++)
            {
                Assert.That(broken.HasTread(step), Is.False);
                Vector3 missing = broken.Foot + new Vector3(0f, (step + 1) * WarehouseArenaStair.TreadRise,
                    (step + .5f) * WarehouseArenaStair.TreadDepth);
                Assert.That(area.TryGetHeight(missing, out _, .32f), Is.False,
                    "Removed treads cannot remain as logical supporting surfaces.");
            }
            Vector3 lower = broken.Foot + Vector3.back * .6f, upper = broken.Top + Vector3.forward * .6f;
            Assert.That(area.CanTraverse(lower, upper), Is.False);
            Assert.That(area.CanTraverse(upper, lower), Is.False);
            var route = new List<Vector3>();
            foreach (bool ascend in new[] { true, false })
            {
                Vector3 start = ascend ? lower : upper, goal = ascend ? upper : lower;
                Assert.That(area.FindRoute(start, goal, route), Is.True,
                    "The broken flight's landings remain connected through the opposite staircase.");
                bool usedRightFlight = false;
                Vector3 previous = start;
                foreach (Vector3 point in route)
                {
                    Assert.That(area.CanTraverse(previous, point), Is.True,
                        "Every returned route segment must retain real tread support.");
                    // Route smoothing can retain only the two ends of the
                    // straight flight. Inspect where the segment crosses its
                    // middle height instead of demanding an intermediate node.
                    float middleHeight = (intact.Foot.y + intact.Top.y) * .5f;
                    if (Mathf.Abs(point.y - previous.y) > .001f)
                    {
                        float fraction = (middleHeight - previous.y) / (point.y - previous.y);
                        if (fraction >= 0f && fraction <= 1f)
                        {
                            Vector3 crossing = Vector3.Lerp(previous, point, fraction);
                            if (intact.Bounds.Contains(new Vector2(crossing.x, crossing.z))) usedRightFlight = true;
                        }
                    }
                    previous = point;
                }
                Assert.That(usedRightFlight, Is.True, "Both directions must use the sole intact upper flight.");
            }
        }

        private static IEnumerator CaptureWarehouseRouteView(CombatTestRoot root, Vector3 heroFeet,
            Vector3 eye, Vector3 target, string name)
        {
            Vector3 facing = target - heroFeet; facing.y = 0f;
            root.Hero.ResetActor(heroFeet + Vector3.up * PlayerFactory.GroundedRootOffset, facing.normalized);
            root.Hero.SetCombatFocused(false);
            root.Hero.ApplyMotorConstraint();
            Physics.SyncTransforms();
            root.CameraFollow.SetFixedPose(eye, Quaternion.LookRotation(target - eye), 66f);
            Renderer[] renderers = root.Hero.GetComponentsInChildren<Renderer>(true);
            var previous = new bool[renderers.Length];
            for (int index = 0; index < renderers.Length; index++) previous[index] = renderers[index].forceRenderingOff;
            try
            {
                for (int frame = 0; frame < 3; frame++) yield return null;
                foreach (Renderer renderer in renderers) renderer.forceRenderingOff = true;
                CaptureCurrentCamera(root.CameraFollow.Camera, SceneIds.CombatTest, name);
            }
            finally
            {
                for (int index = 0; index < renderers.Length; index++)
                    if (renderers[index] != null) renderers[index].forceRenderingOff = previous[index];
                root.CameraFollow.ClearFixedPose();
            }
        }
    }
}
