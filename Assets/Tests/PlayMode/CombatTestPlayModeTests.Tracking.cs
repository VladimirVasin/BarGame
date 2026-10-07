using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static BarPromenade.Tests.PlayMode.CombatTuning;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        private enum TrackingStrike { Light, Charged, ObservedCounter }

        [UnityTest]
        public IEnumerator Range_StrikesAimOnlyAtTheOpeningAndPreservePhysicalEvasion()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            yield return EnterRange();
            ConfigureDuelMovementCapsule(root.Hero);
            ConfigureDuelMovementCapsule(root.Opponent);

            foreach (bool heroActs in new[] { true, false })
            foreach (MeleeSwing swing in new[] { MeleeSwing.Forehand, MeleeSwing.Backhand })
            foreach (TrackingStrike strike in new[] { TrackingStrike.Light, TrackingStrike.Charged, TrackingStrike.ObservedCounter })
            {
                yield return PrepareTrackingPair(2.4f);
                CombatActor actor = heroActs ? root.Hero : root.Opponent;
                CombatActor target = heroActs ? root.Opponent : root.Hero;
                string subject = $"Test opening aim - {(heroActs ? "hero" : "opponent")} {swing} {strike}";
                actor.State.ObserveLateralCue(swing == MeleeSwing.Backhand ? 1 : -1);
                if (strike == TrackingStrike.Charged)
                {
                    Assert.That(actor.RequestCharge(), Is.True, subject);
                    Quaternion held = actor.transform.rotation;
                    target.Body.Move(actor.transform.right * .18f);
                    for (int tick = 0; tick < Mathf.CeilToInt(S.ChargeSeconds / CombatTestRoot.SimulationStep) + 2; tick++)
                        AdvanceTrackingTick(actor, subject);
                    Assert.That(actor.State.Charge01, Is.EqualTo(1f).Within(.001f), subject);
                    Assert.That(Quaternion.Angle(held, actor.transform.rotation), Is.GreaterThan(1f),
                        "Holding the weapon precedes the committed release and can still face the opponent.");
                    Assert.That(actor.ReleaseCharge(), Is.True, subject);
                }
                else Assert.That(strike == TrackingStrike.ObservedCounter ? actor.TryObservedCounterAttack() : actor.TryAttack(),
                    Is.True, subject);
                Assert.That(actor.State.Swing, Is.EqualTo(swing), subject);
                target.Body.Move(actor.transform.right * .16f);
                Quaternion entry = actor.transform.rotation;
                float lockAt = actor.State.AttackWindupSeconds * CombatActor.EarlyFacingFraction;
                while (actor.State.AttackElapsed + .000001f < lockAt) AdvanceTrackingTick(actor, subject);
                Assert.That(Quaternion.Angle(entry, actor.transform.rotation), Is.GreaterThan(.1f),
                    subject + ": the opening follows a changed live bearing.");
                Quaternion locked = actor.transform.rotation;
                if (heroActs && swing == MeleeSwing.Forehand && strike == TrackingStrike.Light)
                {
                    float elapsed = actor.State.AttackElapsed;
                    using (GameTimeScaleRuntime.AcquirePause())
                    {
                        root.Tick(.2f);
                        Assert.That(actor.State.AttackElapsed, Is.EqualTo(elapsed));
                        AssertTrackingPresentationStable(actor, subject + " paused");
                    }
                    yield return null;
                    PresentInertiaPose();
                    CaptureInertiaFrame(actor, subject, 0);
                }
                bool sawActive = false;
                target.Body.Move(actor.transform.right * .75f);
                for (int tick = 0; tick < 180 && actor.State.IsAttacking; tick++)
                {
                    if (tick == 18) target.Body.Move(-actor.transform.right * 1.5f);
                    AdvanceTrackingTick(actor, subject);
                    if (!actor.State.IsAttacking) break;
                    Assert.That(Quaternion.Angle(locked, actor.transform.rotation), Is.LessThan(.01f),
                        subject + ": late windup, Active and Recovery share the committed direction.");
                    AssertTrackingPresentationStable(actor, subject);
                    if (!sawActive && actor.State.Phase == MeleePhase.Active)
                    {
                        sawActive = true;
                        if (strike == TrackingStrike.Light && swing == MeleeSwing.Forehand)
                        {
                            yield return null;
                            PresentInertiaPose();
                            CaptureInertiaFrame(actor, subject, 1);
                        }
                    }
                }
                Assert.That(sawActive, Is.True, subject);
                Assert.That(actor.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Miss), subject);
                Assert.That(target.ReceivedImpactCount, Is.Zero, "Turning cannot invent reach for a distant opponent.");
            }
            yield return VerifyTrackingKick();
            yield return VerifyOpeningStepEvasion();
            root.ResetRound();
            Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Ready));
            AssertTrackingPresentationStable(root.Hero, "Test opening aim reset");
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator PrepareTrackingPair(float distance)
        {
            PlacePair(distance);
            yield return null;
            yield return null; // Finish ResetActor's visible handoff before observing support.
            PresentInertiaPose();
            root.CameraFollow.Snap();
        }

        private IEnumerator VerifyTrackingKick()
        {
            yield return PrepareTrackingPair(2.4f);
            CombatActor actor = root.Hero, target = root.Opponent;
            Assert.That(actor.TryKick(), Is.True);
            Transform supportFoot = CombatBone(actor, actor.Footwork.SelectedSupportSide == 0 ? "foot.L" : "foot.R");
            Vector3 support = supportFoot.position;
            Quaternion entry = actor.transform.rotation;
            target.Body.Move(actor.transform.right * .3f);
            float lockAt = S.KickWindupSeconds * CombatActor.EarlyFacingFraction;
            while (actor.State.KickElapsed + .000001f < lockAt) AdvanceTrackingTick(actor, "Test opening kick aim");
            Assert.That(Quaternion.Angle(entry, actor.transform.rotation), Is.GreaterThan(.1f));
            Quaternion locked = actor.transform.rotation;
            int side = actor.KickStrikingSide;
            target.Body.Move(-actor.transform.right * 1.5f);
            for (int tick = 0; tick < 180 && actor.State.IsKicking; tick++)
            {
                AdvanceTrackingTick(actor, "Test opening kick aim");
                if (!actor.State.IsKicking) break;
                Assert.That(actor.KickStrikingSide, Is.EqualTo(side), "A target reversal cannot switch the striking leg.");
                Assert.That(Quaternion.Angle(locked, actor.transform.rotation), Is.LessThan(.01f));
                Assert.That(Vector3.Distance(support, supportFoot.position), Is.LessThan(.025f));
            }
            Assert.That(target.ReceivedImpactCount, Is.Zero);
            Assert.That(actor.State.KickOutcome, Is.EqualTo(MeleeAttackOutcome.Miss));
        }

        private IEnumerator VerifyOpeningStepEvasion()
        {
            foreach (bool heroActs in new[] { true, false })
            foreach (MeleeSwing swing in new[] { MeleeSwing.Forehand, MeleeSwing.Backhand })
            {
                bool escaped = false;
                foreach (Vector2 direction in new[] { Vector2.right, Vector2.left })
                {
                    yield return PrepareTrackingPair(1.1f);
                    CombatActor actor = heroActs ? root.Hero : root.Opponent;
                    CombatActor target = heroActs ? root.Opponent : root.Hero;
                    actor.State.ObserveLateralCue(swing == MeleeSwing.Backhand ? 1 : -1);
                    Assert.That(actor.TryAttack(), Is.True);
                    Assert.That(target.TryStep(direction), Is.True);
                    Vector3 start = target.transform.position;
                    // Reproduce the old failure's approach during the tell, with
                    // achieved controller travel on the same duel clock.
                    Vector3 approach = actor.transform.forward;
                    for (int tick = 0; tick < 160 && (actor.State.IsAttacking || target.State.Phase == MeleePhase.Step); tick++)
                    {
                        if (actor.State.Phase == MeleePhase.Windup)
                            actor.Body.Move(approach * (.30f / S.WindupSeconds * CombatTestRoot.SimulationStep));
                        root.Tick(CombatTestRoot.SimulationStep);
                    }
                    if (target.ReceivedImpactCount != 0) continue;
                    Assert.That(Vector3.Distance(start, target.transform.position), Is.EqualTo(1f).Within(.035f));
                    Assert.That(actor.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Miss));
                    escaped = true;
                    break;
                }
                Assert.That(escaped, Is.True, "A one-metre early side step must have a physical escape from each swing.");
            }
        }

        private void AdvanceTrackingTick(CombatActor actor, string context)
        {
            Quaternion previous = actor.transform.rotation;
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(Quaternion.Angle(previous, actor.transform.rotation),
                Is.LessThanOrEqualTo(CombatActor.MaximumFacingSpeed * CombatTestRoot.SimulationStep + .05f),
                context + ": aim must spend only this simulation step's turn budget.");
        }

        private void AssertTrackingPresentationStable(CombatActor actor, string context)
        {
            Quaternion facing = actor.transform.rotation;
            var pose = new CombatInertiaPose(actor);
            for (int repeat = 0; repeat < 4; repeat++) PresentInertiaPose();
            Assert.That(Quaternion.Angle(facing, actor.transform.rotation), Is.LessThan(.01f),
                context + ": repeated presentation cannot advance aim.");
            pose.AssertMatches(actor, .0002f, .05f, context + ": repeated presentation cannot advance the rig.");
        }

        private static float TrackingBearing(CombatActor actor, CombatActor target) =>
            Mathf.Abs(Vector3.SignedAngle(actor.transform.forward,
                Vector3.ProjectOnPlane(target.transform.position - actor.transform.position, Vector3.up), Vector3.up));
    }
}
