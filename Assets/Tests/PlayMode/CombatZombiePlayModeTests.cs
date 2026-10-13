using System.Collections;
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
        public IEnumerator Range_ZombieLungeUsesHandsObstaclesPauseAndRecycledBodies()
        {
            CombatTestRoot.UseHumanOpponentsForTests = false;
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return EnterRange();
            CombatActor zombie = root.Opponent;
            Assert.That(zombie.IsZombie, Is.True);
            Assert.That(zombie.Weapon, Is.Null, "Production opponents have no hidden crowbar.");
            Assert.That(zombie.State.Settings, Is.EqualTo(MeleeCombatSettings.ZombieUnarmed));
            Assert.That(zombie.NpcAnimationGraphBuilds, Is.EqualTo(1));
            zombie.SetBlock(true);
            Assert.That(zombie.State.IsBlocking || zombie.GuardReady, Is.False);

            // Rear-head contact keeps the unarmed profile rather than the crowbar finisher.
            var rearHeadTarget = new MeleeCombatant();
            Assert.That(rearHeadTarget.ReceiveUnarmedHit(zombie.State.Settings.Damage,
                zombie.State.Settings.BlockCost, false,
                location: new MeleeHitLocation(MeleeBodyRegion.Head, MeleeHitSide.Rear)),
                Is.EqualTo(MeleeHitResult.Hit));
            Assert.That(rearHeadTarget.IsDefeated, Is.False);

            PlacePair(1.1f);
            root.CameraFollow.Snap();
            for (int frame = 0; frame < 8; frame++) yield return null;
            yield return CaptureEndlessGameView("zombie-ready", "ru");
            Assert.That(zombie.TryZombieAttack(), Is.True);
            zombie.Step(zombie.State.Settings.WindupSeconds * .55f);
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Windup));
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero,
                "The visible preparation cannot damage the target.");
            yield return CaptureEndlessGameView("zombie-prepare", "ru");
            for (int step = 0; step < 240 && root.Hero.ReceivedImpactCount == 0; step++)
                zombie.Step(CombatTestRoot.SimulationStep);
            Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1),
                "The authored palm must reach the hero's actual body.");
            Assert.That(root.Hero.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Unarmed));
            Assert.That(root.Hero.LastImpact.Damage, Is.GreaterThan(0f));
            Assert.That(root.Hero.State.IsDefeated, Is.False);
            float hitHealth = root.Hero.State.Health;
            yield return CaptureEndlessGameView("zombie-contact", "ru");
            zombie.Step(2f);
            Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1));
            Assert.That(root.Hero.State.Health, Is.EqualTo(hitHealth).Within(.001f));
            Assert.That(zombie.State.Stamina, Is.EqualTo(zombie.State.Settings.MaxStamina));
            Assert.That(root.SparkEffects.EmissionCount, Is.Zero,
                "Bare flesh cannot emit the crowbar's metal clash effects.");

            PlacePair(1.1f);
            Assert.That(zombie.TryZombieAttack(), Is.True);
            zombie.Step(zombie.State.Settings.WindupSeconds * .65f);
            Quaternion committedFacing = zombie.transform.rotation;
            root.Hero.ResetActor(root.Hero.transform.position + Vector3.right * 3f, Vector3.forward);
            Physics.SyncTransforms();
            zombie.Step(zombie.State.Settings.WindupSeconds + zombie.State.Settings.ActiveSeconds + .05f);
            Assert.That(Quaternion.Angle(committedFacing, zombie.transform.rotation), Is.LessThan(.01f),
                "A committed lunge cannot turn after the hero dodges.");
            Assert.That(zombie.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Miss));
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Recovery));
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero);
            Assert.That(zombie.TryZombieAttack(), Is.False, "A miss retains its recovery window.");
            yield return CaptureEndlessGameView("zombie-miss", "ru");

            GameObject wall = null;
            try
            {
                PlacePair(1.3f);
                wall = new GameObject("Test zombie lunge wall");
                wall.transform.SetParent(root.transform, false);
                wall.transform.position = root.Hero.transform.position + new Vector3(0f, 1.2f, .7f);
                wall.AddComponent<BoxCollider>().size = new Vector3(3f, 2.4f, .12f);
                Physics.SyncTransforms();
                Vector3 beforeWall = zombie.transform.position;
                Assert.That(zombie.TryZombieAttack(), Is.True);
                zombie.Step(2f);
                Assert.That(root.Hero.ReceivedImpactCount, Is.Zero, "A wall blocks the palm and the body.");
                Assert.That(root.Hero.State.Health, Is.EqualTo(root.Hero.State.Settings.MaxHealth));
                Assert.That(zombie.transform.position.z, Is.GreaterThan(.7f),
                    "The lunge's capsule must remain on its side of the obstacle.");
                Assert.That(Vector3.Distance(beforeWall, zombie.transform.position), Is.LessThan(.6f));
                yield return CaptureEndlessGameView("zombie-wall", "ru");
            }
            finally
            {
                if (wall != null) Object.DestroyImmediate(wall);
                Physics.SyncTransforms();
            }

            PlacePair(1.1f);
            root.Hero.SetBlock(true);
            root.Hero.State.Advance(root.Hero.State.Settings.ParryWindowSeconds + .01f);
            Assert.That(zombie.TryZombieAttack(), Is.True);
            zombie.Step(2f);
            Assert.That(root.Hero.State.Health, Is.EqualTo(root.Hero.State.Settings.MaxHealth));
            Assert.That(root.Hero.LastImpact.Result, Is.EqualTo(MeleeHitResult.Blocked));
            Assert.That(root.Hero.State.Stamina, Is.LessThan(root.Hero.State.Settings.MaxStamina));
            Assert.That(root.SparkEffects.EmissionCount, Is.Zero);

            PlacePair(1.1f);
            Assert.That(zombie.TryZombieAttack(), Is.True);
            zombie.Step(.2f);
            float elapsed = zombie.State.AttackElapsed;
            Vector3 heldPosition = zombie.transform.position;
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(2f);
            zombie.Step(2f);
            yield return null;
            Assert.That(zombie.State.AttackElapsed, Is.EqualTo(elapsed));
            Assert.That(zombie.transform.position, Is.EqualTo(heldPosition));
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero);
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => !root.PauseMenu.IsOpen && GameInput.CanRead(GameInputContext.Gameplay),
                "Zombie pause did not release gameplay input.");
            root.ResetRound();
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(zombie.State.AttackElapsed, Is.Zero);
            PlacePair(1.1f);
            ApplyKnownBodyContact(zombie, root.Hero, BodyDamageRegion.RightHand, 0, 2f, 1);
            Assert.That(zombie.TryZombieAttack(), Is.True);
            Assert.That(zombie.ZombieAttackUsesLeftHand, Is.True,
                "Losing the right hand selects the remaining authored left-hand attack.");
            zombie.Step(2f);
            Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1));
            ApplyKnownBodyContact(zombie, root.Hero, BodyDamageRegion.LeftHand, 0, 2f, 2);
            Assert.That(zombie.TryZombieAttack(), Is.False);
            root.ResetRound();
            Assert.That(zombie.BodyDamage.CanUseLeftHand && zombie.BodyDamage.CanUseRightHand, Is.True);
            Assert.That(zombie.NpcAnimationGraphBuilds, Is.EqualTo(1));

            // The actual AI and shared clock, rather than isolated actor stepping,
            // own the capsule's lunge travel and must preserve its chosen facing.
            root.SetSparring(true);
            PlaceZombieChasePair(1.5f);
            for (int step = 0; step < 360 && zombie.State.Phase != MeleePhase.Windup; step++)
                root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Windup),
                "The slow chase must commit to its close attack.");
            Vector3 lungeStart = zombie.transform.position;
            for (int step = 0; step < 240 && root.Hero.ReceivedImpactCount == 0; step++)
                root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1));
            Assert.That(root.Hero.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Unarmed));
            Assert.That(Vector3.Distance(lungeStart, zombie.transform.position), Is.InRange(.1f, .83f),
                "The visible lunge advances its real capsule a short bounded distance.");
            yield return CaptureEndlessGameView("zombie-lunge", "ru");

            root.ResetRound();
            PlaceZombieChasePair(1.5f);
            for (int step = 0; step < 360 && zombie.State.Phase != MeleePhase.Windup; step++)
                root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Windup));
            committedFacing = zombie.transform.rotation;
            root.Hero.ResetActor(root.Hero.transform.position + Vector3.right * 3f, Vector3.forward);
            Physics.SyncTransforms();
            for (int step = 0; step < 240 && zombie.State.Phase != MeleePhase.Recovery; step++)
                root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Recovery));
            Assert.That(zombie.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Miss));
            Assert.That(Quaternion.Angle(committedFacing, zombie.transform.rotation), Is.LessThan(.01f));
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero);
            yield return CaptureEndlessGameView("zombie-dodge", "ru");

            // Re-enter the real warehouse in Endless: a single prepared body is recycled.
            Assert.That(root.ReturnToWeapons(), Is.True);
            yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                !SceneTransitionService.IsTransitioning, "Zombie range did not release its scene.");
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            var pacing = new CombatEncounterSettings(1, .1f, .1f, .15f, .15f);
            Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                new CombatLoadoutSelection(CombatWeaponId.Crowbar), CombatTestMode.Endless, pacing)), Is.True);
            yield return AwaitSelectedCombatRange();
            root.Tick(.25f);
            Assert.That(root.WarehousePlan, Is.Not.Null);
            Assert.That(root.LiveOpponentCount, Is.EqualTo(1));
            zombie = root.Opponents[0];
            Assert.That(zombie.IsZombie && zombie.Weapon == null, Is.True);
            int graphBuilds = zombie.NpcAnimationGraphBuilds;
            yield return CaptureEndlessPopulationGameView("zombie-endless");
            zombie.State.ReceiveProjectileHit(zombie.State.Settings.MaxHealth);
            root.Tick(0f);
            Assert.That(root.EndlessKills, Is.EqualTo(1));
            root.Tick(.35f);
            Assert.That(root.LiveOpponentCount, Is.EqualTo(1));
            Assert.That(root.Opponents[0], Is.SameAs(zombie));
            Assert.That(zombie.State.IsDefeated || zombie.IsRagdollActive, Is.False);
            Assert.That(zombie.BodyDamage.CanUseLeftHand && zombie.BodyDamage.CanUseRightHand, Is.True);
            Assert.That(zombie.Weapon, Is.Null);
            Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(zombie.NpcAnimationGraphBuilds, Is.EqualTo(graphBuilds));
            Assert.That(root.EndlessKills, Is.EqualTo(1), "Reusing a body cannot credit its previous death twice.");
            root.ResetRound();
            Assert.That(root.EndlessKills, Is.Zero);
            Assert.That(root.LiveOpponentCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        private void PlaceZombieChasePair(float distance)
        {
            Assert.That(root.SetOpponentFocus(true), Is.True);
            Vector3 position = Vector3.up * PlayerFactory.GroundedRootOffset;
            root.Hero.ResetActor(position, Vector3.forward);
            root.Opponent.ResetActor(position + Vector3.forward * distance, Vector3.back);
            Physics.SyncTransforms();
        }
    }
}
