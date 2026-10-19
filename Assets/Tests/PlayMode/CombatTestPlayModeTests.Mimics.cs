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
            CombatTestRoot.UseDockerPopulationForTests = true;
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
            Assert.That(root.HasCraneOpponent, Is.False, "This isolated disguise fixture explicitly retains only porters.");
            Assert.That(root.WarehousePlan.FishCrateAnchors.Count, Is.EqualTo(18));
            foreach (CombatActor actor in root.Opponents)
            {
                Assert.That(actor.IsDocker, Is.True);
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
            yield return AdvanceMimicTo(zombie, .22f);
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
            yield return AdvanceMimicTo(zombie, .40f);
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
            yield return VerifyMimicOwnDebrisTraversal(zombie, load);

            root.ResetRound();
            zombie = root.Opponents[0];
            load = zombie.GetComponent<CombatZombieLoad>();
            sound = zombie.GetComponent<CombatZombieAudio>();
            Assert.That(zombie.ZombieDormant, Is.True);
            Assert.That(load.DetachedBoardCount, Is.Zero, "Reset returns the same finite boards to the closed crate.");
            zombie.ResetActor(Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.forward);
            zombie.SetZombieDormant();
            BoxCollider board = load.GetBoardCollider(0);
            Assert.That(CombatZombieLoad.TryReceiveHit(board, board.bounds.center, Vector3.zero, 25f), Is.True);
            Assert.That(zombie.ZombieRevealing, Is.True, "A real registered wooden contact also wakes the actor.");
            yield return AdvanceMimicTo(zombie, CombatZombieAssetProvider.RevealSeconds);
            Assert.That(load.IsBoardDetached(0), Is.False);
            for (int contact = 2; contact <= 4; contact++)
            {
                Assert.That(CombatZombieLoad.TryReceiveHit(board, board.bounds.center, Vector3.zero, 25f), Is.True);
                Assert.That(load.IsBoardDetached(0), Is.EqualTo(contact == 4),
                    "The first contact's scaled damage survives reveal; the fourth pistol contact breaks the board.");
            }
            Assert.That(sound.RevealPlayCount, Is.EqualTo(1));
            yield return VerifyMimicOwnDebrisTraversal(zombie, load);
            yield return VerifyMimicFlashlightNearExposure();
            yield return CaptureMimicCombatMotion();

            root.ResetRound();
            zombie = root.Opponents[0];
            sound = zombie.GetComponent<CombatZombieAudio>();
            zombie.CaptureContactPose();
            Assert.That(zombie.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 head, out _, out _), Is.True);
            var hit = new CombatHurtboxes.Hit(head, Vector3.up, Vector3.down, 0f,
                new MeleeHitLocation(MeleeBodyRegion.Head, MeleeHitSide.Front), Player3DAnatomicalPart.Head, Vector3.zero);
            zombie.ReceiveProjectile(root.Hero, 1, hit, Vector3.down * CombatProjectilePool.MuzzleSpeed);
            Assert.That(zombie.State.Health, Is.EqualTo(zombie.State.Settings.MaxHealth * .5f));
            Assert.That(zombie.State.IsDefeated, Is.False);
            Assert.That(zombie.ZombieRevealing, Is.True, "A surviving hidden-body contact starts the real reveal.");
            Assert.That(sound.RevealPlayCount, Is.EqualTo(1));
            Assert.That(sound.DeathPlayCount, Is.Zero);
            zombie.ReceiveProjectile(root.Hero, 2, hit, Vector3.down * CombatProjectilePool.MuzzleSpeed);
            Assert.That(zombie.State.IsDefeated, Is.True);
            Assert.That(sound.RevealPlayCount, Is.EqualTo(1));
            Assert.That(sound.DeathPlayCount, Is.EqualTo(1));
            zombie.ReceiveProjectile(root.Hero, 3, hit, Vector3.down * CombatProjectilePool.MuzzleSpeed);
            Assert.That(sound.DeathPlayCount, Is.EqualTo(1));

            root.ResetRound();
            zombie = root.Opponents[0];
            var items = new[] { InventoryItemId.RottenFishTailCan, InventoryItemId.RottenFishBellyCan, InventoryItemId.RottenFishHeadCan };
            for (int i = 0; i < 3; i++)
                Assert.That(GameSessionState.TryCollectWorldItem(root.WarehousePlan.Refrigerators[i].SourceId, items[i]), Is.True);
            Assert.That(root.EndlessSpawnsBlockedByFish, Is.True);
            Assert.That(root.LiveOpponentCount, Is.Zero);
            Assert.That(root.CorpseCount, Is.Zero);
            Assert.That(zombie.gameObject.activeInHierarchy, Is.False);
            Assert.That(zombie.TryAwakenZombie(), Is.False, "The final source retires dormant actors with the live population.");
            root.Tick(1.5f);
            Assert.That(zombie.gameObject.activeInHierarchy, Is.False);
            Assert.That(root.LiveOpponentCount, Is.Zero);
            Assert.That(CombatZombieAudio.ActiveGroanCount, Is.Zero);
            root.ResetRound();
            Assert.That(root.LiveOpponentCount, Is.Zero, "A completed warehouse cannot repopulate on Reset.");
            Assert.That(CombatZombieAudio.ActiveGroanCount, Is.Zero);
        }

        private IEnumerator VerifyMimicOwnDebrisTraversal(CombatActor actor, CombatZombieLoad load)
        {
            Assert.That(actor.ZombieAwake, Is.True);
            Assert.That(actor.Body.height, Is.EqualTo(1.75f).Within(.00001f),
                "The regression must run after the final reveal capsule resize.");
            actor.State.CancelAction();
            Vector3 front = actor.transform.forward;
            root.Hero.ResetActor(actor.transform.position + front * 5f, -front);
            root.Hero.SetCombatFocused(false);
            // Finish scheduled exits before placing two retained physical shapes
            // across the lane. Kinematic blockers cannot be pushed out of the way.
            root.Tick(.2f);
            int fishIndex = 0;
            while (fishIndex < 8 && !load.IsFishReleased(fishIndex)) fishIndex++;
            Assert.That(fishIndex, Is.LessThan(8), "Reveal must spill its original fish.");
            Rigidbody panel = load.GetBoardBody(30), fish = load.GetFishBody(fishIndex);
            Assert.That(load.IsBoardDetached(30), Is.True);
            Collider panelShape = panel.GetComponent<Collider>(), fishShape = fish.GetComponent<Collider>();
            Vector3 start = actor.transform.position;
            PlaceMimicTraversalBlocker(panel, panelShape, start + front * .65f + Vector3.up * .9f, front);
            PlaceMimicTraversalBlocker(fish, fishShape, start + front * 1.2f + Vector3.up * .9f, front);
            Physics.SyncTransforms();
            Assert.That(panelShape.enabled && fishShape.enabled, Is.True);
            Assert.That(CombatCorpseTraversal.IsCorpse(panelShape) && CombatCorpseTraversal.IsCorpse(fishShape), Is.True);
            Assert.That(Physics.GetIgnoreCollision(actor.Body, panelShape), Is.True,
                "The final growing capsule must retain its own board exclusion.");
            Assert.That(Physics.GetIgnoreCollision(actor.Body, fishShape), Is.True,
                "The final growing capsule must retain its own fish exclusion.");
            yield return null;
            float maximumSideways = 0f;
            float speed = CombatZombieAssetProvider.WalkCycleDistance / CombatZombieAssetProvider.WalkCycleSeconds;
            int steps = Mathf.CeilToInt((2.2f / speed + 4f) * 60f);
            for (int step = 0; step < steps && Vector3.Dot(actor.transform.position - start, front) < 1.9f; step++)
            {
                root.Tick(1f / 60f);
                Vector3 moved = actor.transform.position - start; moved.y = 0f;
                maximumSideways = Mathf.Max(maximumSideways, Vector3.ProjectOnPlane(moved, front).magnitude);
            }
            Assert.That(Vector3.Dot(actor.transform.position - start, front), Is.GreaterThanOrEqualTo(1.9f),
                "Actual pursuit must cross its own broken board and released fish after reveal and pool Reset.");
            Assert.That(maximumSideways, Is.LessThan(.15f), "Pursuit must cross the debris rather than detour around it.");
        }

        private static void PlaceMimicTraversalBlocker(Rigidbody body, Collider shape, Vector3 centre, Vector3 front)
        {
            body.isKinematic = true;
            Vector3 localCentre;
            if (shape is BoxCollider board)
            {
                Vector3 size = Vector3.Scale(board.size, board.transform.lossyScale);
                Vector3 thin = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
                body.rotation = Quaternion.FromToRotation(thin, front);
                localCentre = board.center;
            }
            else
            {
                var fish = (CapsuleCollider)shape;
                Vector3 length = fish.direction == 0 ? Vector3.right : fish.direction == 1 ? Vector3.up : Vector3.forward;
                body.rotation = Quaternion.FromToRotation(length, Vector3.up);
                localCentre = fish.center;
            }
            body.position = centre - body.rotation * Vector3.Scale(localCentre, body.transform.lossyScale);
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

        private IEnumerator VerifyMimicFlashlightNearExposure()
        {
            root.ResetRound();
            CombatActor actor = root.Opponents[0];
            Vector3 origin = Vector3.up * PlayerFactory.GroundedRootOffset;
            root.Hero.ResetActor(origin, Vector3.forward);
            CombatChestFlashlight flashlight = root.Flashlight;
            Assert.That(flashlight, Is.Not.Null);
            Light spot = flashlight.SpotLight;
            Vector3 direction = Vector3.ProjectOnPlane(spot.transform.forward, Vector3.up).normalized;
            actor.ResetActor(origin + direction * 6f, -direction);
            Physics.SyncTransforms();
            for (int step = 0; step < 60; step++) flashlight.AdvanceExposure(1f / 60f);
            float distantPower = spot.intensity;
            yield return null;
            CaptureMimicView(actor, "mimic-flashlight-distant");

            actor.ResetActor(origin + direction * 1.5f, -direction);
            Physics.SyncTransforms();
            for (int step = 0; step < 60; step++) flashlight.AdvanceExposure(1f / 60f);
            float nearPower = spot.intensity;
            Assert.That(nearPower, Is.LessThan(distantPower * .4f),
                "A nearby real enemy must receive less beam power than the distant one, preserving pale skin and wood detail.");
            Assert.That(nearPower, Is.GreaterThan(0f));
            Assert.That(spot.shadows, Is.EqualTo(LightShadows.Hard));
            TestContext.WriteLine($"Chest flashlight: distant={distantPower:F3}, close={nearPower:F3}, surface={flashlight.NearSurfaceDistance:F3} m.");
            yield return null;
            spot.intensity = CombatChestFlashlight.Intensity;
            CaptureMimicView(actor, "mimic-flashlight-close-before");
            spot.intensity = nearPower;
            CaptureMimicView(actor, "mimic-flashlight-close-after");

            Assert.That(root.PauseMenu.Open(), Is.True);
            flashlight.AdvanceExposure(1f);
            Assert.That(spot.intensity, Is.EqualTo(nearPower), "Pause must freeze beam adaptation.");
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release flashlight exposure.");
            flashlight.enabled = false;
            Assert.That(spot.enabled, Is.False);
            flashlight.enabled = true;
            Assert.That(spot.intensity, Is.EqualTo(CombatChestFlashlight.Intensity));
        }

        private IEnumerator CaptureMimicCombatMotion()
        {
            root.ResetRound();
            CombatActor actor = root.Opponents[0];
            Vector3 origin = Vector3.up * PlayerFactory.GroundedRootOffset;
            actor.ResetActor(origin, Vector3.forward);
            actor.SetZombieDormant();
            root.Hero.ResetActor(origin + Vector3.forward * 2.5f, Vector3.back);
            Physics.SyncTransforms();
            root.CameraFollow.Snap();
            root.CameraFollow.RotateYaw(Mathf.DeltaAngle(root.CameraFollow.Camera.transform.eulerAngles.y, 180f));
            const string folder = "CombatTest/mimic-motion";
            float captureDelta = Time.captureDeltaTime;
            try
            {
                // One continuous gameplay view, with physics and the manual
                // combat clock receiving the same time as each rendered frame.
                Time.captureDeltaTime = 1f / 30f;
                for (int frame = 0; frame < 120; frame++)
                {
                    root.Tick(1f / 30f);
                    root.CameraFollow.Snap();
                    yield return null;
                    string name = "frame" + frame.ToString("D3");
                    string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", folder, name + ".png");
                    LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                    AreaCaptureFixture.CaptureCurrentCamera(root.CameraFollow.Camera, folder, name);
                }
                Assert.That(actor.ZombieAwake, Is.True);
                Assert.That(root.Hero.ReceivedImpactCount, Is.GreaterThan(0),
                    "The continuous reveal-to-pursuit capture must reach a real ram contact.");
            }
            finally { Time.captureDeltaTime = captureDelta; }
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
