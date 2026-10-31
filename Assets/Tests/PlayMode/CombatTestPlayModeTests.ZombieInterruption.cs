using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [Category("CurrentCombat")]
        [UnityTest]
        public IEnumerator Range_ZombieHitsInterruptLiveAttacksWithoutRefundingRecovery()
        {
            AssertLoaderRecoveryRules();
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            yield return WaitFor(() => !SceneTransitionService.IsTransitioning, "Menu transition did not settle.");
            Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(CombatLoadoutSelection.Default,
                CombatTestMode.Laboratory, laboratoryEnemy: CombatEnemyKind.Zombie)), Is.True);
            yield return AwaitSelectedCombatRange();
            root.AutomaticOpponentDecisions = false;
            root.SetSparring(false);
            Assert.That(root.IsLaboratory && root.Opponent.IsZombie && !root.Opponent.IsDocker, Is.True,
                "The laboratory's default target is the ordinary zombie, without a carried crate.");

            foreach (CombatEnemyKind kind in new[] { CombatEnemyKind.Zombie, CombatEnemyKind.Docker })
            {
                Assert.That(root.SpawnLaboratoryEnemy(kind), Is.True);
                MeleeCombatSettings settings = root.Opponent.State.Settings;
                float recovery = kind == CombatEnemyKind.Zombie ? 3.2f : 3.6f;
                Assert.That(new[] { settings.RecoverySeconds, settings.HitRecoverySeconds,
                    settings.BlockRecoverySeconds, settings.ObstacleRecoverySeconds, settings.ParriedRecoverySeconds },
                    Is.All.EqualTo(recovery).Within(.0001f), kind + ": every outcome retains the deliberate attack pause.");
                Assert.That(settings.AnimationRecoverySeconds,
                    Is.EqualTo(kind == CombatEnemyKind.Zombie ? .95f : 2.4f).Within(.0001f),
                    "Gameplay recovery remaps the existing authored return without regenerating the clip.");
                Assert.That(settings.StaggerSeconds, Is.EqualTo(.8f).Within(.0001f));

                // Four live contacts cover both damage paths and both phases without
                // replaying an AI chase: each body receives one normal and one capped hit.
                for (int capped = 0; capped < 2; capped++)
                {
                    bool exhausted = capped != 0;
                    bool pistol = kind == CombatEnemyKind.Zombie ? exhausted : !exhausted;
                    string label = kind + (pistol ? " pistol" : " melee") + (exhausted ? " capped arm" : " live arm");
                    root.ResetRound();
                    PlaceZombieChasePair(4f);
                    CombatActor target = root.Opponent;
                    if (exhausted)
                    {
                        // Exhaust only the regional HP budget. The live attached arm has
                        // no trauma yet, so the next actual contact is the regression.
                        Assert.That(target.BodyDamage.ResolveHealthDamage(BodyDamageRegion.LeftForearm,
                            1000f, settings.IncomingDamageScale), Is.GreaterThan(0f));
                        Assert.That(target.BodyDamage.ResolveHealthDamage(BodyDamageRegion.LeftForearm,
                            1f, settings.IncomingDamageScale), Is.Zero);
                        Assert.That(target.BodyDamage.IsAttached(BodyDamageRegion.LeftForearm), Is.True);
                    }

                    Assert.That(target.TryZombieAttack(), Is.True, label + ": the live actor starts its attack.");
                    float beforeHit = exhausted ? settings.WindupSeconds + settings.ActiveSeconds * .25f
                        : settings.WindupSeconds * .5f;
                    target.Step(beforeHit);
                    Assert.That(target.State.Phase, Is.EqualTo(exhausted ? MeleePhase.Active : MeleePhase.Windup), label);
                    int oldSequence = target.State.AttackSequence;
                    float cooldown = target.State.CooldownRemaining(MeleeBufferedAction.Attack);
                    Assert.That(beforeHit + cooldown, Is.GreaterThanOrEqualTo(4f),
                        label + ": attacks remain at least four seconds apart.");
                    float health = target.State.Health;
                    bool capture = kind == CombatEnemyKind.Zombie && !exhausted;
                    Transform reactionSpine = null, reactionChest = null;
                    Quaternion spineBeforeHit = Quaternion.identity, chestBeforeHit = Quaternion.identity;
                    if (capture)
                    {
                        yield return null;
                        reactionSpine = FindAnatomicalBone(target, "spine");
                        reactionChest = FindAnatomicalBone(target, "chest");
                        spineBeforeHit = reactionSpine.localRotation;
                        chestBeforeHit = reactionChest.localRotation;
                        CaptureLoaderView(target, "zombie-interruption-before", false);
                    }

                    Transform arm = FindAnatomicalBone(target, "forearm.L");
                    Vector3 direction = -target.transform.forward;
                    Vector3 point = arm.position;
                    var location = new MeleeHitLocation(MeleeBodyRegion.LeftArm, MeleeHitSide.Front);
                    if (pistol)
                    {
                        var hit = new CombatHurtboxes.Hit(point, -direction, direction, 0f, location,
                            Player3DAnatomicalPart.LeftForearm, Vector3.zero,
                            damageRegion: BodyDamageRegion.LeftForearm);
                        target.ReceiveProjectile(root.Hero, 900 + capped, hit, direction * 120f);
                    }
                    else
                    {
                        new CombatActor.Contact(root.Hero, target, point, -direction, direction, location,
                            Player3DAnatomicalPart.LeftForearm, Vector3.zero, 3f,
                            physicalBody: true, bodyRegion: BodyDamageRegion.LeftForearm).Apply();
                    }
                    Assert.That(target.State.Phase, Is.EqualTo(MeleePhase.Stagger), label);
                    Assert.That(target.State.IsAttacking, Is.False, label);
                    Assert.That(target.State.StaggerDuration, Is.GreaterThanOrEqualTo(pistol ? .45f : .8f), label);
                    Assert.That(target.State.TryRegisterHit(90210, oldSequence), Is.False,
                        label + ": the interrupted attack cannot register its old contact.");
                    Assert.That(target.State.CooldownRemaining(MeleeBufferedAction.Attack),
                        Is.EqualTo(cooldown).Within(.0001f), label + ": impact must not refund weaponReadyAt.");
                    if (exhausted) Assert.That(target.State.Health, Is.EqualTo(health), label);
                    else Assert.That(target.State.Health, Is.LessThan(health), label);
                    Assert.That(target.BodyDamage.IsAttached(BodyDamageRegion.LeftForearm), Is.True, label);
                    Assert.That(target.TryZombieAttack(), Is.False, label + ": stun interrupts rather than restarting the attack.");

                    // Let the real paired clock consume hit-stop, then inspect the fast
                    // onset. No wall-clock waiting or opponent decisions are involved.
                    for (int tick = 0; tick < 40 && target.State.Phase == MeleePhase.Stagger &&
                        target.State.StaggerElapsed < .09f; tick++) root.Tick(.01f);
                    string reactionState = label + ": phase=" + target.State.Phase +
                        ", knockedDown=" + target.IsKnockedDown + ", ragdoll=" + target.IsRagdollActive;
                    if (kind == CombatEnemyKind.Docker && exhausted && !pistol &&
                        target.State.Phase != MeleePhase.Stagger)
                    {
                        // The real 80 Ns crowbar impulse can carry the top-heavy Docker
                        // from the verified initial stagger into a live physical fall.
                        Assert.That(target.IsKnockedDown && target.IsRagdollActive, Is.True, reactionState);
                        Assert.That(target.State.Phase, Is.EqualTo(MeleePhase.KnockedDown), reactionState);
                        Assert.That(target.State.IsDefeated, Is.False, reactionState);
                        Assert.That(target.State.Health, Is.EqualTo(health), reactionState);
                        Assert.That(target.State.CooldownRemaining(MeleeBufferedAction.Attack), Is.GreaterThan(0f),
                            reactionState + ": physical fall cannot refund the interrupted attack's recovery.");
                        Assert.That(target.TryZombieAttack(), Is.False, reactionState);
                        Assert.That(target.State.TryRegisterHit(90210, oldSequence), Is.False, reactionState);
                        yield return null;
                        continue;
                    }
                    Assert.That(target.State.StaggerElapsed, Is.InRange(.089f, .11f), reactionState);
                    Assert.That(target.ActiveClipName, Is.EqualTo(kind == CombatEnemyKind.Zombie
                        ? CombatZombieAssetProvider.OrdinaryHitClip : CombatZombieAssetProvider.HitClip), label);
                    if (capture)
                    {
                        // The frozen simulation still needs a new render frame so the
                        // skinned mesh cache catches up with the sampled anatomical pose.
                        yield return null;
                        CaptureLoaderView(target, "zombie-interruption-onset", false);
                        float torsoReaction = Mathf.Max(Quaternion.Angle(spineBeforeHit, reactionSpine.localRotation),
                            Quaternion.Angle(chestBeforeHit, reactionChest.localRotation));
                        Assert.That(torsoReaction, Is.GreaterThan(2f),
                            label + ": the hit onset must visibly change the real spine or chest pose, not only the clip name.");
                    }

                    for (int tick = 0; tick < 100 && target.State.CooldownRemaining(MeleeBufferedAction.Attack) > .03f; tick++)
                        root.Tick(Mathf.Min(.05f, target.State.CooldownRemaining(MeleeBufferedAction.Attack) - .02f));
                    Assert.That(target.State.CooldownRemaining(MeleeBufferedAction.Attack), Is.InRange(.01f, .03f), label);
                    Assert.That(target.TryZombieAttack(), Is.False, label + ": the real actor cannot bypass its original deadline.");
                    root.Tick(.06f);
                    Assert.That(target.TryZombieAttack(), Is.True,
                        label + ": a surviving actor can attack again once its original recovery expires. Phase=" +
                        target.State.Phase + ", balance=" + target.HasAttackBalance);
                    yield return null;
                }
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}
