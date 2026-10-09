using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_ShotgunDistanceControlsLaunchAndHeadBreakup()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return EnterRange(false, CombatWeaponId.Shotgun);
            root.ResetRound();
            // Leave room for the stronger launch before the arena's perimeter wall.
            PlacePair(3f);
            Vector3 start = root.Opponent.Ragdoll.PelvisBody.position;
            List<CombatImpact> near = FireShotgunAtRegion(MeleeBodyRegion.Torso, 1f, 1);
            Assert.That(root.Opponent.State.IsDefeated, Is.True);
            Vector3 nearImpulse = ShotgunImpulse(near);
            Assert.That(nearImpulse.magnitude, Is.GreaterThan(300f),
                "The strengthened close volley must exceed the former 260 N s launch cap.");
            Assert.That(root.Opponent.ShotgunVolleyResponseCount, Is.EqualTo(1));
            Assert.That(root.HeadEffects.DetachedSectorCountFor(root.Opponent), Is.Zero);
            // Consume the single hitstop before observing real fixed-step physics.
            root.Tick(.2f);
            float travel = 0f;
            for (int frame = 0; frame < 60; frame++)
            {
                root.Tick(Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
                Vector3 displacement = root.Opponent.Ragdoll.PelvisBody.position - start;
                travel = Mathf.Max(travel, Vector3.ProjectOnPlane(displacement, Vector3.up).magnitude);
                if (frame == 10 || frame == 30) CaptureShotgunImpactView(start, "shotgun-close-body-" + frame);
            }
            Assert.That(travel, Is.GreaterThan(2f), "The strengthened close volley must carry the whole corpse away.");

            root.ResetRound();
            PlacePair(6f);
            List<CombatImpact> far = FireShotgunAtRegion(MeleeBodyRegion.Torso, 15f, 2);
            Assert.That(far.Count, Is.GreaterThan(0), "The distant comparison must actually contact the target.");
            Assert.That(root.Opponent.State.Health, Is.GreaterThan(0f));
            Assert.That(ShotgunImpulse(far).magnitude, Is.LessThan(15f));
            Assert.That(ShotgunImpulse(far).magnitude, Is.LessThan(nearImpulse.magnitude / 20f));
            CaptureShotgunImpactView(root.Opponent.Ragdoll.PelvisBody.position, "shotgun-far-body");

            root.ResetRound();
            PlacePair(6f);
            List<CombatImpact> head = FireShotgunAtRegion(MeleeBodyRegion.Head, 1f, 3);
            Assert.That(head.Count, Is.GreaterThan(0));
            Assert.That(root.Opponent.State.IsDefeated, Is.True);
            Assert.That(root.HeadEffects.DetachedSectorCountFor(root.Opponent), Is.InRange(10, 14));
            Assert.That(root.HeadEffects.BrainFragmentCountFor(root.Opponent), Is.GreaterThanOrEqualTo(5));
            AssertBrainPieceConservation(root.Opponent);
            float fragmentSpeed = 0f;
            foreach (Rigidbody body in root.HeadEffects.GetComponentsInChildren<Rigidbody>())
                fragmentSpeed = Mathf.Max(fragmentSpeed, body.linearVelocity.magnitude);
            // Hitstop stores fragment velocities; resume before measuring their actual flight.
            root.Tick(.2f);
            yield return new WaitForFixedUpdate();
            foreach (Rigidbody body in root.HeadEffects.GetComponentsInChildren<Rigidbody>())
                fragmentSpeed = Mathf.Max(fragmentSpeed, body.linearVelocity.magnitude);
            Assert.That(fragmentSpeed, Is.GreaterThan(4f), "Close buckshot must eject fragments faster than the pistol fracture.");
            CaptureGoreNearView(root.Opponent, Vector3.back, "shotgun-close-head", true, true);

            float debrisAge = root.HeadEffects.DebrisAgeFor(root.Opponent);
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(.4f);
            yield return null;
            Assert.That(root.HeadEffects.DebrisAgeFor(root.Opponent), Is.EqualTo(debrisAge));
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release shotgun gameplay.");

            // The same frozen real head contact, split across contact batches,
            // must consume the same finite trauma and momentum budget.
            int wholeMask = 0;
            Vector3 wholeImpulse = Vector3.zero;
            foreach (bool split in new[] { false, true })
            {
                root.ResetRound();
                PlacePair(6f);
                Assert.That(root.HeadEffects.DetachedSectorCountFor(root.Opponent), Is.Zero);
                List<CombatImpact> impacts = ApplyPreparedShotgunHead(12, split, 4);
                Assert.That(root.Opponent.State.Health, Is.Zero);
                Assert.That(root.Opponent.ShotgunVolleyResponseCount, Is.EqualTo(1));
                Assert.That(root.HeadEffects.DetachedSectorCountFor(root.Opponent), Is.EqualTo(14));
                if (!split)
                {
                    wholeMask = root.HeadEffects.DestroyedMaskFor(root.Opponent);
                    wholeImpulse = ShotgunImpulse(impacts);
                }
                else
                {
                    Assert.That(root.HeadEffects.DestroyedMaskFor(root.Opponent), Is.EqualTo(wholeMask));
                    Assert.That(Vector3.Distance(ShotgunImpulse(impacts), wholeImpulse), Is.LessThan(.001f));
                }
            }
            root.ResetRound();
            PlacePair(6f);
            FireShotgunAtRegion(MeleeBodyRegion.Torso, 1f, 5);
            ApplyPreparedShotgunHead(1, false, 6);
            Assert.That(root.HeadEffects.DetachedSectorCountFor(root.Opponent), Is.EqualTo(1),
                "A single close pellet grazing a defeated head cannot inherit a full shell's destruction.");
            root.ResetRound();
            Assert.That(root.HeadEffects.DetachedSectorCountFor(root.Opponent), Is.Zero);
            Assert.That(root.HeadEffects.RetainedBrainCountFor(root.Opponent), Is.EqualTo(8));
            LogAssert.NoUnexpectedReceived();
        }

        private List<CombatImpact> FireShotgunAtRegion(MeleeBodyRegion region, float distance, int sequence)
        {
            CombatActor target = root.Opponent;
            target.CaptureContactPose();
            Assert.That(target.Hurtboxes.GetRegionFrame(region, out Vector3 point, out _, out _), Is.True);
            if (region == MeleeBodyRegion.Torso) point = target.Ragdoll.PhysicsController.ChestBody.worldCenterOfMass;
            Vector3 outward = target.transform.forward;
            var impacts = new List<CombatImpact>();
            target.ImpactReceived += impacts.Add;
            try
            {
                Assert.That(root.Projectiles.TrySpawnVolley(root.Hero, point + outward * distance,
                    Quaternion.LookRotation(-outward), sequence, root.Hero.Shotgun.Settings), Is.True);
                for (int step = 0; step < 60 && root.Projectiles.ActiveCount > 0; step++)
                {
                    target.CaptureContactPose();
                    root.Projectiles.Advance(CombatTestRoot.SimulationStep, root.Hero, target);
                    root.Projectiles.ApplyContacts();
                }
                Assert.That(root.Projectiles.ActiveCount, Is.Zero);
            }
            finally { target.ImpactReceived -= impacts.Add; }
            return impacts;
        }

        private List<CombatImpact> ApplyPreparedShotgunHead(int count, bool split, int sequence)
        {
            CombatActor target = root.Opponent;
            target.CaptureContactPose();
            Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head,
                out Vector3 point, out Vector3 outward, out _), Is.True);
            Assert.That(target.Hurtboxes.SweepProjectile(point + outward * .75f, point - outward * .75f,
                CombatProjectilePool.Radius, -outward, out CombatHurtboxes.Hit hit), Is.True);
            Assert.That(hit.Location.Region, Is.EqualTo(MeleeBodyRegion.Head));
            var hits = new List<CombatProjectilePool.PelletHit>();
            for (int i = 0; i < count; i++) hits.Add(new CombatProjectilePool.PelletHit(hit,
                -outward * CombatProjectilePool.MuzzleSpeed, 1f, i, root.Hero.Shotgun.Settings));
            var impacts = new List<CombatImpact>();
            target.ImpactReceived += impacts.Add;
            try
            {
                if (split)
                    for (int i = 0; i < hits.Count; i++) target.ReceiveShotgunVolley(root.Hero, sequence,
                        new List<CombatProjectilePool.PelletHit> { hits[i] }, i == 0);
                else target.ReceiveShotgunVolley(root.Hero, sequence, hits, true);
            }
            finally { target.ImpactReceived -= impacts.Add; }
            return impacts;
        }

        private static Vector3 ShotgunImpulse(List<CombatImpact> impacts)
        {
            Vector3 impulse = Vector3.zero;
            foreach (CombatImpact impact in impacts) impulse += impact.Impulse;
            return impulse;
        }

        private void CaptureShotgunImpactView(Vector3 start, string name)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                Vector3 subject = Vector3.Lerp(start, root.Opponent.Ragdoll.PelvisBody.position, .5f) + Vector3.up * .2f;
                camera.fieldOfView = 58f;
                Vector3 eye = subject + Vector3.right * 4f + Vector3.up * 1.6f - Vector3.forward * 1.2f;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(subject - eye));
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, name + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
            }
            finally { camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = fov; }
        }

        [UnityTest]
        public IEnumerator Range_ShotgunTwoBarrelsVolleyReloadAndResetUseTheLiveDuel()
        {
            pistolGeometryIssues.Clear();
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            GameObject obstacle = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                var menu = Object.FindAnyObjectByType<StartMenuRoot>();
                Assert.That(menu, Is.Not.Null);
                Assert.That(menu.SelectOption(StartMenuOption.CombatTest), Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(menu.SelectCombatOption(CombatPreparationOption.Shotgun), Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(menu.SelectedCombatWeapon, Is.EqualTo(CombatWeaponId.Shotgun));
                Assert.That(menu.SelectCombatOption(CombatPreparationOption.Start), Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                yield return AwaitSelectedCombatRange();
                Assert.That(root.HeroWeapon, Is.EqualTo(CombatWeaponId.Shotgun));
                Assert.That(root.Hero.IsShotgun && root.Hero.IsFirearm, Is.True);
                Assert.That(root.Hero.Pistol, Is.Null);
                Assert.That(root.Hero.Firearm, Is.SameAs(root.Hero.Shotgun));
                Assert.That(root.Opponent.IsFirearm, Is.False);
                AssertShotgunImportedModel();

                PlacePair(8f);
                root.Opponent.ResetActor(new Vector3(6f, PlayerFactory.GroundedRootOffset, 8f), Vector3.back);
                Assert.That(root.SetOpponentFocus(false), Is.True);
                root.Hero.SetPistolAim(true, new Vector3(0f, 1.4f, 20f));
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.Shotgun.Rounds, Is.EqualTo(2), "A trigger during raising is discarded.");
                Assert.That(root.Projectiles.SpawnCount, Is.Zero);
                root.Tick(.6f);
                Assert.That(root.Hero.Shotgun.CanFire, Is.True);
                ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                yield return CaptureShotgunNearPose("aim");
                Assert.That(root.Hero.PistolAimAligned, Is.True,
                    "Shotgun aim error " + root.Hero.PistolAimErrorDegrees + "; support error " + root.Hero.PistolSupportError);
                Assert.That(root.Hero.PistolSupportError, Is.LessThan(.025f));
                Assert.That(root.Hero.State.IsBlocking, Is.False);
                ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                AssertPistolArmsClearBody("Shotgun aim");
                Assert.That(pistolGeometryIssues, Is.Empty, "Aimed sleeves and forearms must clear the original torso.");
                yield return CaptureFocusGameView("shotgun-aim-game");

                // Fill the live pool with slow remote flights. A volley needs all twelve slots
                // before the trigger may spend either chamber or increment its shot sequence.
                for (int flight = 0; flight < 63; flight++)
                    Assert.That(root.Projectiles.TrySpawn(root.Hero, new Vector3(-8f, 12f, -8f),
                        Vector3.right * .25f, flight), Is.True);
                Assert.That(root.Projectiles.HasCapacityFor(12), Is.False);
                int capacitySequence = root.Hero.Shotgun.ShotSequence;
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.Shotgun.Rounds, Is.EqualTo(2));
                Assert.That(root.Hero.Shotgun.ShotSequence, Is.EqualTo(capacitySequence));
                Assert.That(root.Hero.Shotgun.CooldownRemaining, Is.Zero);
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(63));
                Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(63));
                root.Projectiles.ResetRound();

                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.Shotgun.Rounds, Is.EqualTo(1));
                Assert.That(root.Hero.Shotgun.LastFiredBarrel, Is.Zero);
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(12));
                Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(12));
                Assert.That(root.Casings.EjectionCount + root.Casings.PendingCount, Is.Zero,
                    "A break-action shotgun retains its spent shell until the reload opens it.");
                root.Tick(.04f);
                yield return CaptureShotgunNearPose("fire");
                root.Tick(.8f);
                Assert.That(root.Opponent.State.Health, Is.EqualTo(root.Opponent.State.Settings.MaxHealth),
                    "Free aiming away from the opponent cannot redirect pellets toward it.");
                Assert.That(root.Hero.Shotgun.CanFire, Is.True);
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.Shotgun.Rounds, Is.Zero);
                Assert.That(root.Hero.Shotgun.LastFiredBarrel, Is.EqualTo(1));
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(24));
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(.8f);
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(24), "Two barrels permit exactly two shots before reloading.");
                Assert.That(root.Casings.EjectionCount + root.Casings.PendingCount, Is.Zero);

                Assert.That(root.Hero.TryReloadPistol(), Is.True);
                AdvanceShotgunReloadTo(.46f);
                Assert.That(root.Hero.Shotgun.BreakOpen01, Is.EqualTo(1f).Within(.001f));
                Assert.That(root.Casings.EjectionCount, Is.Zero);
                yield return CaptureShotgunNearPose("open");
                AdvanceShotgunReloadTo(.67f);
                Assert.That(root.Casings.EjectionCount, Is.EqualTo(2));
                Assert.That(root.Hero.Shotgun.ChamberSpent(0) || root.Hero.Shotgun.ChamberSpent(1), Is.False);
                Assert.That(root.Hero.Shotgun.Rounds, Is.Zero);
                AdvanceShotgunReloadTo(1.25f);
                yield return CaptureShotgunNearPose("shell");
                AdvanceShotgunReloadTo(1.6f);
                AssertShotgunReloadHandAtChamber(0, true);
                Assert.That(root.Hero.Shotgun.Rounds, Is.EqualTo(1));
                AdvanceShotgunReloadTo(2.2f);
                AssertShotgunReloadHandAtChamber(1, true);
                Assert.That(root.Hero.Shotgun.Rounds, Is.EqualTo(2));
                Assert.That(root.Hero.Shotgun.ReloadPending, Is.True);
                Assert.That(root.Hero.Shotgun.CanFire, Is.False, "Loaded shells cannot fire from the open action.");
                root.Tick(1f);
                Assert.That(root.Hero.Shotgun.ReloadPending, Is.False);
                Assert.That(root.Hero.Shotgun.BreakOpen01, Is.Zero);
                Assert.That(root.Casings.EjectionCount, Is.EqualTo(2));
                yield return CaptureShotgunNearPose("closed");

                // A single fired shell is the other reload transaction: the second live
                // shell stays in place through pause, interruption, and the explicit R resume.
                root.ResetRound();
                PlacePair(8f);
                root.Opponent.ResetActor(new Vector3(6f, PlayerFactory.GroundedRootOffset, 8f), Vector3.back);
                Assert.That(root.SetOpponentFocus(false), Is.True);
                root.Hero.SetPistolAim(true, new Vector3(0f, 1.4f, 20f));
                root.Tick(.6f);
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.Shotgun.Rounds, Is.EqualTo(1));
                input.Press(keyboard.rKey, queueEventOnly: true);
                root.AutomaticSimulation = true;
                yield return null;
                root.AutomaticSimulation = false;
                input.Release(keyboard.rKey, queueEventOnly: true);
                InputSystem.Update();
                Assert.That(root.Hero.Shotgun.IsReloading, Is.True, "The real R route reloads the selected shotgun.");
                AdvanceShotgunReloadTo(.67f);
                Assert.That(root.Hero.Shotgun.ReloadChamberMask, Is.EqualTo(1));
                Assert.That(root.Hero.Shotgun.ChamberLoaded(1), Is.True);
                Assert.That(root.Casings.EjectionCount, Is.EqualTo(1));
                float pausedElapsed = root.Hero.Shotgun.ReloadElapsed;
                float pausedAngle = root.Hero.Shotgun.BreakOpen01;
                Vector3 pausedCase = root.Casings.LastPosition;
                Assert.That(root.PauseMenu.Open(), Is.True);
                root.Tick(.8f);
                yield return null;
                Assert.That(root.Hero.Shotgun.ReloadElapsed, Is.EqualTo(pausedElapsed));
                Assert.That(root.Hero.Shotgun.BreakOpen01, Is.EqualTo(pausedAngle));
                Assert.That(root.Casings.LastPosition, Is.EqualTo(pausedCase));
                Assert.That(root.Hero.Shotgun.ChamberLoaded(1), Is.True);
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release shotgun gameplay.");

                // Inject only a nonlethal stagger; the runtime's ordinary body-availability
                // gate must suspend the actual authored reload and preserve its chamber state.
                root.Hero.State.ReceiveProjectileHit(1f);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.Shotgun.IsReloading, Is.False);
                Assert.That(root.Hero.Shotgun.ReloadPending, Is.True);
                float interruptedElapsed = root.Hero.Shotgun.ReloadElapsed;
                root.Tick(.4f);
                Assert.That(root.Hero.Shotgun.ReloadElapsed, Is.EqualTo(interruptedElapsed));
                Assert.That(root.Hero.Shotgun.ChamberLoaded(1), Is.True);
                Assert.That(root.Hero.Shotgun.Rounds, Is.EqualTo(1));
                Assert.That(root.Hero.TryReloadPistol(), Is.True);
                root.Tick(.05f);
                Assert.That(root.Hero.Shotgun.ReloadElapsed, Is.EqualTo(interruptedElapsed),
                    "Resume first settles the original rig back onto the retained reload pose.");
                root.Tick(.2f);
                AdvanceShotgunReloadTo(1.6f);
                AssertShotgunReloadHandAtChamber(0, true);
                Assert.That(root.Hero.Shotgun.ChamberLoaded(1), Is.True);
                yield return CaptureShotgunNearPose("reload-left-insert");
                AdvanceShotgunReloadTo(2.2f);
                AssertShotgunReloadHandAtChamber(1, false);
                Assert.That(root.Hero.Shotgun.ReloadChamberMask, Is.EqualTo(1));
                yield return CaptureShotgunNearPose("reload-left-skip-right");
                root.Tick(1f);
                Assert.That(root.Hero.Shotgun.Rounds, Is.EqualTo(2));
                Assert.That(root.Hero.Shotgun.ReloadPending, Is.False);
                Assert.That(root.Casings.EjectionCount, Is.EqualTo(1), "A resumed reload cannot eject the same case twice.");

                // Exercise the opposite chamber independently of the current left-first firing order.
                root.ResetRound();
                PlacePair(8f);
                root.Opponent.ResetActor(new Vector3(6f, PlayerFactory.GroundedRootOffset, 8f), Vector3.back);
                Assert.That(root.SetOpponentFocus(false), Is.True);
                const BindingFlags chamberFlags = BindingFlags.Instance | BindingFlags.NonPublic;
                ((bool[])typeof(ShotgunState).GetField("loaded", chamberFlags).GetValue(root.Hero.Shotgun))[1] = false;
                ((bool[])typeof(ShotgunState).GetField("spent", chamberFlags).GetValue(root.Hero.Shotgun))[1] = true;
                Assert.That(root.Hero.TryReloadPistol(), Is.True);
                Assert.That(root.Hero.Shotgun.ReloadChamberMask, Is.EqualTo(2));
                AdvanceShotgunReloadTo(1.6f);
                AssertShotgunReloadHandAtChamber(0, false);
                Assert.That(root.Hero.Shotgun.ChamberLoaded(0), Is.True);
                Assert.That(root.Hero.Shotgun.ChamberLoaded(1), Is.False);
                yield return CaptureShotgunNearPose("reload-right-skip-left");
                AdvanceShotgunReloadTo(2.2f);
                AssertShotgunReloadHandAtChamber(1, true);
                Assert.That(root.Hero.Shotgun.Rounds, Is.EqualTo(2));
                yield return CaptureShotgunNearPose("reload-right-insert");
                root.Tick(1f);
                Assert.That(root.Casings.EjectionCount, Is.EqualTo(1));
                Assert.That(root.Hero.Shotgun.ReloadPending, Is.False);

                root.ResetRound();
                PlacePair(3.5f);
                root.Hero.SetPistolAim(true);
                root.Tick(.6f);
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(12));
                root.Tick(.8f);
                Assert.That(root.Opponent.State.IsDefeated, Is.True,
                    "A close torso volley must defeat the standard target through actual anatomical contacts.");
                Assert.That(root.Opponent.ShotgunVolleyResponseCount, Is.EqualTo(1),
                    "Regional pellet wounds share one physical body response for their volley.");
                Assert.That(root.BloodEffects.ProjectileWoundCountFor(root.Opponent), Is.GreaterThan(1));

                // The full pellet batch must stop at a thin world wall before the target.
                root.ResetRound();
                PlacePair(6f);
                obstacle = new GameObject("Test shotgun wall");
                var wall = obstacle.AddComponent<BoxCollider>();
                wall.size = new Vector3(4f, 4f, .015f);
                obstacle.transform.position = new Vector3(0f, 1.4f, 2f);
                Physics.SyncTransforms();
                Assert.That(root.Projectiles.TrySpawnVolley(root.Hero, new Vector3(0f, 1.4f, 0f),
                    Quaternion.identity, root.Hero.Shotgun.ShotSequence, root.Hero.Shotgun.Settings), Is.True);
                root.Tick(.05f);
                Assert.That(root.Projectiles.ImpactCount, Is.EqualTo(12));
                Assert.That(root.Projectiles.ActiveCount, Is.Zero);
                Assert.That(root.Opponent.State.Health, Is.EqualTo(root.Opponent.State.Settings.MaxHealth));
                Assert.That(root.Opponent.ShotgunVolleyResponseCount, Is.Zero);
                Assert.That(root.Projectiles.SurfaceEffects.EmissionCount, Is.EqualTo(12));
                Assert.That(root.Projectiles.SurfaceEffects.HoleCount, Is.EqualTo(12));
                Object.DestroyImmediate(obstacle);
                obstacle = null;

                root.ResetRound();
                PlacePair(6f);
                root.Hero.SetPistolAim(true);
                root.Tick(.6f);
                obstacle = new GameObject("Test blocked shotgun muzzle");
                obstacle.AddComponent<BoxCollider>().size = Vector3.one * .15f;
                obstacle.transform.position = root.Hero.ShotgunMuzzle(0).position;
                Physics.SyncTransforms();
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Projectiles.SpawnCount, Is.Zero);
                Assert.That(root.Hero.Shotgun.Rounds, Is.EqualTo(2), "An obstructed muzzle cannot consume a shell.");
                Object.DestroyImmediate(obstacle);
                obstacle = null;

                root.ResetRound();
                Assert.That(root.HeroWeapon, Is.EqualTo(CombatWeaponId.Shotgun));
                Assert.That(root.Hero.Shotgun.Rounds, Is.EqualTo(2));
                Assert.That(root.Hero.Shotgun.ReloadPending || root.Hero.Shotgun.ChamberSpent(0) ||
                    root.Hero.Shotgun.ChamberSpent(1), Is.False);
                Assert.That(root.Projectiles.ActiveCount + root.Projectiles.SpawnCount +
                    root.Casings.ActiveCount + root.Casings.PendingCount + root.Casings.EjectionCount, Is.Zero);
                Assert.That(root.Projectiles.SurfaceEffects.HoleCount, Is.Zero);
                GameObject formerWeapon = root.Hero.Weapon;
                Assert.That(CombatTestStartService.ReturnToPreparation(root.HeroWeapon), Is.True);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                    !SceneTransitionService.IsTransitioning, "Shotgun preparation did not reopen.");
                menu = Object.FindAnyObjectByType<StartMenuRoot>();
                Assert.That(menu.IsChoosingCombatWeapon, Is.True);
                Assert.That(menu.SelectedCombatWeapon, Is.EqualTo(CombatWeaponId.Shotgun));
                Assert.That(formerWeapon == null, Is.True, "The selected weapon must leave with its combat scene.");
                Assert.That(Object.FindAnyObjectByType<CombatActor>(), Is.Null);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                if (obstacle != null) Object.DestroyImmediate(obstacle);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        private void AssertShotgunImportedModel()
        {
            Assert.That(root.Hero.Weapon.name, Does.Contain("Shotgun"));
            bool any = false;
            Bounds bounds = default;
            foreach (Renderer renderer in root.Hero.Weapon.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            Assert.That(any, Is.True);
            Assert.That(bounds.size.magnitude, Is.InRange(.7f, 1.8f),
                "Measure imported visible metres: correct anchors alone cannot catch a lost FBX unit scale.");
            foreach (string anchor in new[] { "Grip", "SupportGrip", "Muzzle", "Hinge", "MuzzleLeft", "MuzzleRight",
                "ChamberLeft", "ChamberRight", "ShellLeft", "ShellRight" })
                Assert.That(CombatShotgunAssetProvider.FindAnchor(root.Hero.Weapon, anchor), Is.Not.Null, anchor);
            Transform left = root.Hero.ShotgunMuzzle(0), right = root.Hero.ShotgunMuzzle(1);
            Assert.That(Vector3.Distance(left.position, right.position), Is.InRange(.015f, .08f));
            Assert.That(Vector3.Angle(left.forward, right.forward), Is.LessThan(.1f));
        }

        private void AssertShotgunReloadHandAtChamber(int barrel, bool seated)
        {
            var presentation = (Player3DCharacterPresentation)root.Player.Visual;
            presentation.ReapplyLatePresentationPose();
            var hands = root.Hero.GetComponentInChildren<NpcHandPose>();
            GameObject shell = CombatShotgunAssetProvider.CreateShell(null);
            shell.SetActive(false);
            try
            {
                // Measure the actual hand as if it held a shell, including when the real prop is hidden.
                // Hiding an extra shell must not conceal a second insertion gesture.
                CombatShotgunAssetProvider.PlaceShellInHand(shell, presentation.Registry.Anchors.LeftGrip, hands);
                Transform seat = CombatShotgunAssetProvider.FindAnchor(shell, "Seat");
                Transform chamber = CombatShotgunAssetProvider.FindAnchor(root.Hero.Weapon,
                    barrel == 0 ? "ChamberLeft" : "ChamberRight");
                float distance = Vector3.Distance(seat.position, chamber.position);
                if (seated) Assert.That(distance, Is.LessThan(.015f), "The real hand must seat the required shell.");
                else Assert.That(distance, Is.GreaterThan(.04f), "The real hand must skip an already loaded chamber.");
            }
            finally { Object.DestroyImmediate(shell); }
        }

        private void AdvanceShotgunReloadTo(float elapsed)
        {
            Assert.That(elapsed, Is.LessThan(root.Hero.Shotgun.Settings.ReloadSeconds));
            for (int step = 0; step < 420 && root.Hero.Shotgun.ReloadElapsed + .000001f < elapsed; step++)
            {
                Assert.That(root.Hero.Shotgun.IsReloading, Is.True, "The requested shotgun stage was interrupted.");
                root.Tick(CombatTestRoot.SimulationStep);
            }
            Assert.That(root.Hero.Shotgun.ReloadElapsed,
                Is.InRange(elapsed - .000001f, elapsed + CombatTestRoot.SimulationStep + .000001f));
        }

        private IEnumerator CaptureShotgunNearPose(string stage)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            float previousFov = camera.fieldOfView;
            bool followEnabled = root.CameraFollow.enabled;
            Transform actor = root.Hero.transform;
            Vector3 target = root.Hero.Ragdoll.PhysicsController.ChestBody.position + actor.forward * .3f;
            try
            {
                root.CameraFollow.enabled = false;
                camera.fieldOfView = 45f;
                foreach (bool side in new[] { false, true })
                {
                    Vector3 eye = actor.position + actor.up * 1.45f +
                        (side ? actor.right * 1.8f + actor.forward * .4f : actor.forward * 2.1f + actor.right * .35f);
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, actor.up));
                    yield return null;
                    ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                    var hands = root.Hero.GetComponentInChildren<NpcHandPose>();
                    Transform grip = CombatShotgunAssetProvider.FindAnchor(root.Hero.Weapon, "Grip");
                    Assert.That(Vector3.Distance(grip.position, hands.CylinderCentre(false)), Is.LessThan(.002f));
                    string shot = "shotgun-" + stage + (side ? "-side" : "-front");
                    string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, shot + ".png");
                    LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                    AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, shot);
                }
            }
            finally
            {
                root.CameraFollow.enabled = followEnabled;
                camera.fieldOfView = previousFov;
                camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
            }
        }
    }
}
