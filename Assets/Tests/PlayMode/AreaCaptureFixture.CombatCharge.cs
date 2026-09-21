using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using static BarPromenade.Tests.PlayMode.CombatTuning;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        // The same gameplay shoulder camera sees both original rigs preparing
        // and releasing a real charged strike. Camera.Render does not capture IMGUI.
        private IEnumerator CaptureCombatCharge(CombatTestRoot root, Camera camera)
        {
#if UNITY_EDITOR
            if (!Application.isBatchMode)
            {
                Type viewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
                Assert.That(viewType, Is.Not.Null);
                UnityEditor.EditorWindow gameView = UnityEditor.EditorWindow.GetWindow(viewType);
                gameView.Show();
                gameView.Focus();
                yield return null;
            }
#endif
            // Both swing sides: a target seen off to the right calls the backhand.
            foreach (MeleeSwing swing in new[] { MeleeSwing.Forehand, MeleeSwing.Backhand })
            foreach (bool chargeHero in new[] { true, false })
            {
                CombatActor actor = chargeHero ? root.Hero : root.Opponent;
                CombatActor target = chargeHero ? root.Opponent : root.Hero;
                string subject = (chargeHero ? "hero" : "opponent") + (swing == MeleeSwing.Backhand ? "-backhand" : "");
                Transform tip = CombatAssetProvider.FindAnchor(actor.Weapon, "StrikeTip");
                Vector3 previousTip = Vector3.zero;
                foreach (float power in new[] { 0f, .25f, .5f, .75f, 1f })
                {
                    CombatCapturePair(root);
                    for (int frame = 0; frame < 24; frame++) yield return null;
                    actor.State.ObserveLateralCue(swing == MeleeSwing.Backhand ? 1 : 0);
                    Assert.That(actor.RequestCharge(), Is.True);
                    Assert.That(actor.State.Swing, Is.EqualTo(swing));
                    root.Tick(actor.State.Settings.ChargeSeconds * power);
                    yield return null;
                    string sample = "charge-" + subject + "-" + Mathf.RoundToInt(power * 100f);
                    CaptureDamageFrame(root, camera, sample);
                    Assert.That(actor.State.IsCharging, Is.True);
                    Assert.That(actor.State.Charge01, Is.EqualTo(power).Within(.001f));
                    Assert.That(actor.State.Stamina, Is.EqualTo(ChargedStamina(power)).Within(.001f));
                    Assert.That(target.State.Health, Is.EqualTo(S.MaxHealth));
                    Assert.That(target.ReceivedImpactCount, Is.Zero, "Preparation must not open the weapon damage window.");
                    if (power > 0f)
                        Assert.That(Vector3.Distance(previousTip, tip.position), Is.GreaterThan(.01f),
                            sample + ": each quarter must visibly prepare the original held crowbar further.");
                    previousTip = tip.position;
                    if (power == 1f)
                    {
                        int heldSequence = actor.State.AttackSequence;
                        root.Tick(.25f);
                        yield return null;
                        ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                        Assert.That(actor.State.IsCharging, Is.True, "A completed held charge waits for release.");
                        Assert.That(actor.State.AttackSequence, Is.EqualTo(heldSequence));
                        Assert.That(target.State.Health, Is.EqualTo(S.MaxHealth));
                        if (chargeHero && swing == MeleeSwing.Forehand) yield return CaptureCombatChargeUi(root);
                    }
                    Vector3 loadedTip = tip.position;
                    Quaternion loadedRotation = actor.Weapon.transform.rotation;
                    Assert.That(actor.ReleaseCharge(), Is.True);
                    ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                    Assert.That(actor.State.IsCharging, Is.False);
                    Assert.That(actor.State.AttackPower, Is.EqualTo(power).Within(.001f));
                    // Quarter weights distinguish the hero Playable mixer from
                    // NPC Slerp; equal endpoints and the midpoint alone cannot.
                    Assert.That(Vector3.Distance(loadedTip, tip.position), Is.LessThan(.008f),
                        sample + ": release must preserve the loaded tip within eight millimetres.");
                    Assert.That(Quaternion.Angle(loadedRotation, actor.Weapon.transform.rotation), Is.LessThan(.75f),
                        sample + ": release must preserve the loaded crowbar angle.");
                    string releasePrefix = power == 1f ? "charge-" + subject : sample;
                    CaptureDamageFrame(root, camera, releasePrefix + "-release");
                    int emissions = root.BloodEffects.EmissionCount;
                    for (int step = 0; step < ContactTicks(120f) + 24 && target.State.Health == S.MaxHealth; step++) root.Tick(1f / 120f);
                    float expectedHealth = S.MaxHealth - ChargedDamage(power);
                    Assert.That(target.State.Health, Is.EqualTo(expectedHealth).Within(.001f),
                        sample + ": the visible weapon must deliver its latched damage on either rig.");
                    Assert.That(target.ReceivedImpactCount, Is.EqualTo(1));
                    Assert.That(actor.State.AttackElapsed, Is.GreaterThanOrEqualTo(actor.State.AttackWindupSeconds));
                    Assert.That(actor.State.AttackElapsed, Is.LessThanOrEqualTo(actor.State.AttackActiveEnd + 1f / 120f),
                        "The damage event must use the charged animation's actual active interval.");
                    Assert.That(root.BloodEffects.EmissionCount, Is.EqualTo(emissions + 1));
                    yield return null;
                    CaptureDamageFrame(root, camera, releasePrefix + "-contact");
                    root.Tick(.8f);
                    Assert.That(target.State.Health, Is.EqualTo(expectedHealth).Within(.001f));
                    Assert.That(target.ReceivedImpactCount, Is.EqualTo(1), "The remainder of the charged arc cannot hit twice.");
                    Assert.That(root.BloodEffects.EmissionCount, Is.EqualTo(emissions + 1));
                }
            }
            root.ResetRound();
        }

        private static IEnumerator CaptureCombatChargeUi(CombatTestRoot root)
        {
            Assert.That(root.AutomaticSimulation, Is.False, "Hold the charge clock while the actual Game view is captured.");
            Assert.That(root.ChargeMeterVisible, Is.True);
            // The original camera/rig capture remains runnable in batch mode.
            // Unity renders IMGUI only in the ordinary Editor Game view.
            if (Application.isBatchMode) yield break;
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", SceneIds.CombatTest,
                "charge-hero-100-ui.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            DateTime previous = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 3f;
            bool fresh = false;
            for (int frame = 0; frame < 180 && Time.realtimeSinceStartup < deadline; frame++)
            {
                // Keep the charge frozen while the deferred frame is written.
                yield return null;
                Assert.That(root.Hero.State.IsCharging && root.ChargeMeterVisible, Is.True);
                Assert.That(root.Hero.State.Charge01, Is.EqualTo(1f).Within(.001f));
                fresh = File.Exists(path) && File.GetLastWriteTimeUtc(path) > previous && new FileInfo(path).Length > 8;
                if (fresh) break;
            }
            Assert.That(fresh, Is.True, "Capture the actual game frame and charge indicator, not a reconstructed HUD.");
            Debug.Log("COMBAT CHARGE UI FRAME: " + path);
        }
    }
}
