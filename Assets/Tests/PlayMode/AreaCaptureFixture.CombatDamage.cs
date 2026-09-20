using System.Collections;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        // Extend the existing playable-camera capture with the damage continuum.
        // Every wound is caused by the real crowbar; no presentation-only health setter.
        private IEnumerator CaptureCombatDamage(CombatTestRoot root, Camera camera)
        {
            foreach (bool hurtHero in new[] { false, true })
            {
                CombatCapturePair(root);
                CombatActor target = hurtHero ? root.Hero : root.Opponent;
                CombatActor attacker = hurtHero ? root.Opponent : root.Hero;
                string subject = hurtHero ? "hero" : "opponent";
                for (int frame = 0; frame < 18; frame++) yield return null;
                CaptureDamageFrame(root, camera, "damage-" + subject + "-100");
                float previousInjury = 0f;
                for (int strike = 1; strike <= 4; strike++)
                {
                    Vector3 facing = hurtHero ? Vector3.back : Vector3.forward;
                    attacker.ResetActor(target.transform.position - facing * 1.1f, facing);
                    Physics.SyncTransforms();
                    float before = target.State.Health;
                    int emissions = root.BloodEffects.EmissionCount;
                    Assert.That(attacker.TryAttack(), Is.True);
                    for (int step = 0; step < 100 && target.State.Health == before; step++)
                        root.Tick(1f / 120f);
                    Assert.That(target.State.Health, Is.EqualTo(before - 25f));
                    Assert.That(root.BloodEffects.EmissionCount, Is.EqualTo(emissions + 1));
                    Assert.That(root.BloodEffects.ActiveDropCount, Is.GreaterThan(0));
                    Assert.That(root.BloodEffects.WoundCountFor(target), Is.GreaterThan(0));
                    root.Tick(.065f);
                    yield return null;
                    CaptureDamageFrame(root, camera, "damage-" + subject + "-hit-" + strike);

                    if (strike == 1)
                    {
                        float injury = target.DamagePose.InjuryAmount;
                        int drops = root.BloodEffects.ActiveDropCount, stains = root.BloodEffects.StainCount;
                        Assert.That(root.PauseMenu.Open(), Is.True);
                        root.Tick(3f);
                        yield return null;
                        Assert.That(root.BloodEffects.ActiveDropCount, Is.EqualTo(drops));
                        Assert.That(root.BloodEffects.StainCount, Is.EqualTo(stains));
                        Assert.That(target.DamagePose.InjuryAmount, Is.EqualTo(injury));
                        Assert.That(root.PauseMenu.Cancel(), Is.True);
                        for (int frame = 0; frame < 60 && !GameInput.CanRead(GameInputContext.Gameplay); frame++)
                            yield return null;
                        Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.True);
                    }

                    for (int frame = 0; frame < 72; frame++)
                    {
                        root.Tick(1f / 60f);
                        yield return null;
                    }
                    Assert.That(root.BloodEffects.StainCount, Is.GreaterThan(0));
                    Assert.That(root.BloodEffects.MinimumStainNormalAlignment, Is.GreaterThan(.999f),
                        "A puddle lies on its actual support plane, including the imported mesh's local axes.");
                    Assert.That(root.BloodEffects.WoundCountFor(target), Is.GreaterThan(0),
                        "Wounds stay on the same visible skin after the impact and physics handoff.");
                    if (strike < 4)
                    {
                        ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                        Assert.That(target.DamagePose.InjuryAmount, Is.GreaterThan(previousInjury + .12f));
                        previousInjury = target.DamagePose.InjuryAmount;
                        Assert.That(target.DamagePose.AppliedWeight, Is.GreaterThan(.9f));
                    }
                    else Assert.That(target.IsRagdollActive, Is.True);
                    CaptureDamageFrame(root, camera,
                        "damage-" + subject + "-" + Mathf.RoundToInt(target.State.Health));
                }
                int remainingWounds = root.BloodEffects.WoundCountFor(target);
                for (int frame = 0; frame < 120; frame++)
                {
                    root.Tick(1f / 60f);
                    yield return null;
                }
                Assert.That(root.BloodEffects.WoundCountFor(target), Is.EqualTo(remainingWounds));
                CaptureDamageFrame(root, camera, "damage-" + subject + "-puddle");
                root.ResetRound();
                yield return null;
                Assert.That(root.BloodEffects.ActiveDropCount, Is.Zero);
                Assert.That(root.BloodEffects.StainCount, Is.Zero);
                Assert.That(root.BloodEffects.WoundCountFor(target), Is.Zero);
                Assert.That(target.DamagePose.InjuryAmount, Is.Zero);
                Assert.That(target.DamagePose.HasOffsets, Is.False);
            }
            CaptureDamageFrame(root, camera, "damage-reset");
        }

        private static void CaptureDamageFrame(CombatTestRoot root, Camera camera, string name)
        {
            // Unity's batch-mode coroutine resumes before LateUpdate. Compose
            // the same late pose explicitly, without advancing its game clock.
            ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
            CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
        }
    }
}
