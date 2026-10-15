using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_FishCrateMimicsKeepDisguiseRevealDamageAndLifecycle()
        {
            MeshFilter[] sourceMeshes = CombatZombieAssetProvider.LoadCrate().GetComponentsInChildren<MeshFilter>(true);
            Assert.That(sourceMeshes, Is.Not.Empty, "The passive crate includes the retained static fusion mesh.");
            foreach (MeshFilter source in sourceMeshes)
                Assert.That(source.sharedMesh.isReadable, Is.True,
                    source.name + " must allow the runtime CombineMeshes bake in a player.");
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            var selection = new CombatTestSelection(CombatLoadoutSelection.Default, CombatTestMode.Endless,
                new CombatEncounterSettings(3));
            Assert.That(CombatTestStartService.TryStart(selection), Is.True);
            yield return WaitFor(() =>
            {
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                return root != null && root.IsInitialized && !SceneTransitionService.IsTransitioning &&
                    GameInput.CanRead(GameInputContext.Gameplay);
            }, "The warehouse did not finish preparing its sleeping cargo.");
            root.AutomaticSimulation = false;
            Assert.That(root.LiveOpponentCount, Is.EqualTo(3), "The complete selected population exists immediately.");
            Assert.That(root.Opponents.Count, Is.EqualTo(3));
            Assert.That(root.WarehousePlan.FishCrateAnchors.Count, Is.EqualTo(18));
            foreach (CombatActor actor in root.Opponents)
            {
                Assert.That(actor.ZombieDormant, Is.True);
                Assert.That(actor.ActiveClipName, Is.EqualTo(CombatZombieAssetProvider.DormantClip));
                Assert.That(actor.TryZombieAttack(), Is.False);
                Assert.That(actor.GetComponent<CombatZombieLoad>().CatchClockSeconds, Is.Zero);
            }
            Assert.That(root.SetOpponentFocus(true), Is.False, "Lock-on must not identify disguised cargo.");
            CombatActor zombie = root.Opponents[0];
            CombatZombieLoad load = zombie.GetComponent<CombatZombieLoad>();
            AssertPassiveCrateScaleAndRoomEntries(load);
            CombatZombieAudio sound = zombie.GetComponent<CombatZombieAudio>();
            Assert.That(load.AttachedBounds.min.y - zombie.transform.position.y,
                Is.InRange(-.06f, .10f), "The disguised crate must rest at its authored floor, retaining the FBX unit scale.");
            Assert.That(load.BoardCount, Is.EqualTo(36));
            Transform originalCrate = load.Model;
            Rigidbody firstPanelBody = load.GetBoardBody(30);
            int bodyGraphs = zombie.NpcAnimationGraphBuilds, fishGraphs = load.CatchGraphBuildCount;
            Vector3 originalRoot = zombie.transform.position;
            root.Tick(.5f);
            Assert.That(zombie.transform.position, Is.EqualTo(originalRoot));
            Assert.That(load.CatchClockSeconds, Is.Zero);
            Assert.That(sound.RevealPlayCount + sound.AcceptedGroanCount, Is.Zero);
            // Compare a fresh standing sample with the same rig after folding.
            // Generic import may optimize constant channels out of older clips.
            zombie.ResetActor(Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.forward);
            zombie.SampleNpcClip(CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.IdleClip), 0f);
            string[] boneNames = { "pelvis", "spine", "chest", "neck", "head", "clavicle.L", "clavicle.R",
                "upper_arm.L", "upper_arm.R", "forearm.L", "forearm.R", "hand.L", "hand.R",
                "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R" };
            var bones = new Transform[boneNames.Length];
            var positions = new Vector3[bones.Length];
            var scales = new Vector3[bones.Length];
            var rotations = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = FindAnatomicalBone(zombie, boneNames[i]);
                bones[i].GetLocalPositionAndRotation(out positions[i], out rotations[i]);
                scales[i] = bones[i].localScale;
            }
            zombie.SetZombieDormant();
            originalRoot = zombie.transform.position;
            Vector3 front = zombie.transform.forward;
            root.Hero.ResetActor(originalRoot + Vector3.up * WarehouseArenaPlan.LevelRise, -front);
            root.Tick(.15f);
            Assert.That(zombie.ZombieDormant, Is.True, "A player on another floor cannot wake the crate through the ceiling.");
            root.Hero.ResetActor(originalRoot + front * 2.4f, -front);
            root.CameraFollow.Snap();
            yield return null;
            CaptureMimicView(zombie, "mimic-dormant");

            var wall = new GameObject("Тестовая перегородка");
            wall.transform.SetPositionAndRotation(originalRoot + front * 1.2f + Vector3.up,
                zombie.transform.rotation);
            BoxCollider barrier = wall.AddComponent<BoxCollider>();
            barrier.size = new Vector3(2.2f, 2.4f, .15f);
            Physics.SyncTransforms();
            root.Tick(.3f);
            Assert.That(zombie.ZombieDormant, Is.True, "Proximity through a wall cannot wake the crate.");
            Object.Destroy(wall);
            yield return null;
            Physics.SyncTransforms();
            root.Tick(.15f);
            Assert.That(zombie.ZombieRevealing, Is.True);
            Assert.That(sound.RevealPlayCount, Is.EqualTo(1));
            Assert.That(zombie.TryZombieAttack(), Is.False);
            Assert.That(root.IsOpponentFocused, Is.False);
            yield return AdvanceMimicTo(zombie, .4f);
            Assert.That(load.DetachedBoardCount, Is.EqualTo(5), "Feet and knee break their retained wooden panels first.");
            Assert.That(load.IsBoardDetached(30) && load.IsBoardDetached(31) && load.IsBoardDetached(35), Is.True);
            Assert.That(load.IsBoardDetached(32) || load.IsBoardDetached(33) || load.IsBoardDetached(34), Is.False);
            yield return null; // Let GPU skinning present the newly sampled frame.
            CaptureMimicView(zombie, "mimic-revealing");
            float pausedReveal = zombie.ZombieRevealElapsed;
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(1f);
            Assert.That(zombie.ZombieRevealElapsed, Is.EqualTo(pausedReveal));
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release the reveal.");
            yield return AdvanceMimicTo(zombie, .85f);
            Assert.That(load.DetachedBoardCount, Is.EqualTo(9), "Both hands punch through their own side panels.");
            Assert.That(load.IsBoardDetached(32) && load.IsBoardDetached(33), Is.True);
            Assert.That(load.IsBoardDetached(34), Is.False, "The head panel stays solid until the authored head contact.");
            yield return null;
            CaptureMimicView(zombie, "mimic-reveal-late");
            yield return AdvanceMimicTo(zombie, CombatZombieAssetProvider.RevealSeconds);
            for (int frame = 0; frame < 20; frame++) yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(zombie.ZombieAwake, Is.True);
            Assert.That(load.DetachedBoardCount, Is.EqualTo(10));
            Assert.That(load.IsBoardDetached(34), Is.True, "The head leaves a physical opening rather than appearing through timber.");
            Assert.That(load.GetBoardBody(30), Is.SameAs(firstPanelBody));
            Assert.That(load.GetBoardCollider(30).enabled, Is.False);
            Assert.That(load.GetBoardBody(30).isKinematic, Is.False);
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero, "Opening itself has no damage window.");
            CaptureMimicView(zombie, "mimic-awake");
            CaptureMimicView(zombie, "mimic-awake-opposite", -1f);
            CaptureMimicView(zombie, "mimic-awake-side", 0f);
            zombie.SampleNpcClip(CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.IdleClip), 0f);
            for (int i = 0; i < bones.Length; i++)
            {
                Assert.That(Vector3.Distance(bones[i].localPosition, positions[i]), Is.LessThan(.00001f), boneNames[i] + " translation retained from disguise.");
                Assert.That(Quaternion.Angle(bones[i].localRotation, rotations[i]), Is.LessThan(.05f), boneNames[i] + " rotation retained from disguise.");
                Assert.That(Vector3.Distance(bones[i].localScale, scales[i]), Is.LessThan(.00001f), boneNames[i] + " scale retained from disguise.");
            }
            Assert.That(zombie.TryZombieAttack(), Is.True, "The completed reveal hands the same rig to its existing attack.");
            Assert.That(load.Model, Is.SameAs(originalCrate));
            Assert.That(zombie.NpcAnimationGraphBuilds, Is.EqualTo(bodyGraphs));
            Assert.That(load.CatchGraphBuildCount, Is.EqualTo(fishGraphs));

            root.ResetRound();
            zombie = root.Opponents[0];
            load = zombie.GetComponent<CombatZombieLoad>();
            sound = zombie.GetComponent<CombatZombieAudio>();
            Assert.That(zombie.ZombieDormant, Is.True);
            Assert.That(load.DetachedBoardCount, Is.Zero, "Reset returns the same finite boards to the closed crate.");
            BoxCollider board = load.GetBoardCollider(0);
            Assert.That(CombatZombieLoad.TryReceiveHit(board, board.bounds.center, Vector3.zero, 25f), Is.True);
            Assert.That(zombie.ZombieRevealing, Is.True, "A real registered wooden contact also wakes the actor.");
            root.Tick(1.5f);
            Assert.That(load.IsBoardDetached(0), Is.False);
            Assert.That(CombatZombieLoad.TryReceiveHit(board, board.bounds.center, Vector3.zero, 25f), Is.True);
            Assert.That(load.IsBoardDetached(0), Is.True, "The first contact's damage survives reveal without a second transaction.");
            Assert.That(sound.RevealPlayCount, Is.EqualTo(1));

            root.ResetRound();
            zombie = root.Opponents[0];
            sound = zombie.GetComponent<CombatZombieAudio>();
            zombie.CaptureContactPose();
            Assert.That(zombie.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 head, out _, out _), Is.True);
            var hit = new CombatHurtboxes.Hit(head, Vector3.up, Vector3.down, 0f,
                new MeleeHitLocation(MeleeBodyRegion.Head, MeleeHitSide.Front), Player3DAnatomicalPart.Head, Vector3.zero);
            zombie.ReceiveProjectile(root.Hero, 1, hit, Vector3.down * CombatProjectilePool.MuzzleSpeed);
            Assert.That(zombie.State.IsDefeated, Is.True);
            Assert.That(sound.RevealPlayCount, Is.Zero, "A lethal hidden-body contact does not open a dead actor.");
            Assert.That(sound.DeathPlayCount, Is.EqualTo(1));
            zombie.ReceiveProjectile(root.Hero, 2, hit, Vector3.down * CombatProjectilePool.MuzzleSpeed);
            Assert.That(sound.DeathPlayCount, Is.EqualTo(1));

            root.ResetRound();
            zombie = root.Opponents[0];
            var items = new[] { InventoryItemId.RottenFishTailCan, InventoryItemId.RottenFishBellyCan, InventoryItemId.RottenFishHeadCan };
            for (int i = 0; i < 3; i++)
                Assert.That(GameSessionState.TryCollectWorldItem(root.WarehousePlan.Refrigerators[i].SourceId, items[i]), Is.True);
            Assert.That(root.EndlessSpawnsBlockedByFish, Is.True);
            Assert.That(root.LiveOpponentCount, Is.EqualTo(3));
            Assert.That(zombie.TryAwakenZombie(), Is.True, "Stopping future arrivals preserves already living dormant actors.");
            root.Tick(1.5f);
            Assert.That(zombie.ZombieAwake, Is.True);
            root.ResetRound();
            Assert.That(root.LiveOpponentCount, Is.Zero, "A completed warehouse cannot repopulate on Reset.");
            Assert.That(CombatZombieAudio.ActiveGroanCount, Is.Zero);
        }

        private void AssertPassiveCrateScaleAndRoomEntries(CombatZombieLoad liveLoad)
        {
            var population = root.GetComponentInChildren<WarehouseFishCratePopulation>();
            Assert.That(population, Is.Not.Null);
            int crateCount = 0;
            foreach (Transform cargo in population.transform)
            {
                if (cargo.name != "Fish crate") continue;
                crateCount++;
                MeshFilter crate = cargo.GetComponent<MeshFilter>();
                BoxCollider shape = crate.GetComponent<BoxCollider>();
                Assert.That(Vector3.Distance(shape.size, liveLoad.AttachedBounds.size), Is.LessThan(.10f),
                    "Passive collision " + shape.size + " must match the sleeping load's world bounds " + liveLoad.AttachedBounds);
                Bounds mesh = crate.sharedMesh.bounds;
                Assert.That(mesh.size.x, Is.InRange(1.2f, 1.65f), "Passive crate width: " + mesh);
                Assert.That(mesh.size.y, Is.InRange(.7f, 1.05f), "Passive crate height: " + mesh);
                Assert.That(mesh.size.z, Is.InRange(1.1f, 1.6f), "Passive crate depth: " + mesh);
                Assert.That(mesh.min.y, Is.InRange(-.05f, .08f), "Passive crate must rest on its anchor floor.");
            }
            Assert.That(crateCount, Is.EqualTo(root.WarehousePlan.FishCrateAnchors.Count));
            Vector3 originalPosition = root.Hero.transform.position;
            Vector3 originalForward = root.Hero.transform.forward;
            int rooms = 0;
            foreach (Transform room in population.transform)
            {
                if (room.name != "Warehouse RoomShell") continue;
                Transform entry = CityPedestrianHandProps.FindSocket(room, "Entry");
                Assert.That(entry, Is.Not.Null);
                Vector3 inward = room.position - entry.position; inward.y = 0f; inward.Normalize();
                Vector3 start = entry.position - inward * 1.2f + Vector3.up * PlayerFactory.GroundedRootOffset;
                root.Hero.ResetActor(start, inward);
                Physics.SyncTransforms();
                for (int step = 0; step < 48; step++) root.Hero.Body.Move(inward * .05f + Vector3.down * .01f);
                float travel = Vector3.Dot(root.Hero.transform.position - start, inward);
                Assert.That(travel, Is.GreaterThan(2.25f),
                    "The real hero capsule must cross numbered room " + (++rooms) + " at " + entry.position);
            }
            Assert.That(rooms, Is.EqualTo(9));
            root.Hero.ResetActor(originalPosition, originalForward);
            Physics.SyncTransforms();
        }

        private IEnumerator AdvanceMimicTo(CombatActor actor, float seconds)
        {
            // Let retained debris receive the same physical time as the visual
            // action; a few large ticks leave all broken panels in mid-flight.
            while (actor.ZombieRevealElapsed + .000001f < seconds)
            {
                root.Tick(Mathf.Min(.02f, seconds - actor.ZombieRevealElapsed));
                yield return new WaitForFixedUpdate();
            }
        }

        private void CaptureMimicView(CombatActor actor, string name, float side = 1f)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 oldPosition = camera.transform.position;
            Quaternion oldRotation = camera.transform.rotation;
            float oldFov = camera.fieldOfView;
            Vector3 focus = actor.transform.position + Vector3.up * .8f;
            Vector3 eye = actor.transform.position + (side == 0f ? actor.transform.right * 2.7f :
                actor.transform.forward * (2.7f * side) + actor.transform.right * .8f) + Vector3.up * 1.7f;
            try
            {
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                camera.fieldOfView = 48f;
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, name + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                camera.fieldOfView = oldFov;
            }
        }
    }
}
