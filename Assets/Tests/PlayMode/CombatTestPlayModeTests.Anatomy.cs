using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static BarPromenade.Tests.PlayMode.CombatTuning;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_AnatomicalContactsResolveHeadAndRearHeadForBothRigs()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return EnterRange();
            foreach (bool heroVictim in new[] { false, true })
            {
                CombatActor target = heroVictim ? root.Hero : root.Opponent;
                CombatActor source = heroVictim ? root.Opponent : root.Hero;
                foreach (bool charged in new[] { false, true })
                {
                    PlacePair(4f);
                    var hit = ProbeHead(target, MeleeHitSide.Front);
                    Assert.That(hit.Location.Region, Is.EqualTo(MeleeBodyRegion.Head));
                    Assert.That(hit.Location.Side, Is.EqualTo(MeleeHitSide.Front));
                    PrepareAnatomicalStrike(source, charged);
                    var contact = new CombatActor.Contact(source, target, hit.Point, hit.Normal, hit.Direction, hit.Location);
                    if (charged) source.State.ReceiveHit(1f, S.BlockCost, false);
                    contact.Apply();
                    Assert.That(target.State.Health, Is.EqualTo(charged ? 20f : 50f).Within(.001f));
                    Assert.That(target.LastImpact.IsCritical, Is.True);
                    Assert.That(target.LastImpact.IsFinisher, Is.False);
                    Assert.That(target.LastImpact.AttackPower, Is.EqualTo(charged ? 1f : 0f).Within(.001f));
                    Assert.That(target.ReceivedImpactCount, Is.EqualTo(1));
                    Assert.That(root.BloodEffects.WoundCountFor(target), Is.GreaterThan(0));
                    if (!heroVictim && !charged) CaptureAnatomicalContact(target, "head");
                }

                PlacePair(4f);
                Transform head = FindAnatomicalBone(target, "head");
                target.CaptureContactPose();
                Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out var center, out var forward, out var up), Is.True);
                Assert.That(Vector3.Dot(forward, target.transform.forward), Is.GreaterThan(.8f),
                    "Imported bone axes must agree with the visible face in the ready pose.");
                Vector3 fixedApproach = center + forward * .5f;
                Quaternion savedHead = head.localRotation;
                head.rotation = Quaternion.AngleAxis(180f, up) * head.rotation;
                // Until capture, even a moved rig must retain the contact-step snapshot.
                Assert.That(target.Hurtboxes.SweepSphere(fixedApproach, center, .025f, -forward, out var frozen), Is.True);
                Assert.That(frozen.Location.Side, Is.EqualTo(MeleeHitSide.Front));
                target.CaptureContactPose();
                Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out center, out var turnedForward, out up), Is.True);
                Assert.That(Vector3.Dot(turnedForward, forward), Is.LessThan(-.99f));
                Assert.That(target.Hurtboxes.SweepSphere(center + forward * .5f, center, .025f, -forward, out var turned), Is.True);
                Assert.That(turned.Location.Region, Is.EqualTo(MeleeBodyRegion.Head));
                Assert.That(turned.Location.Side, Is.EqualTo(MeleeHitSide.Rear), "The skull, not the actor root, owns its rear.");
                head.localRotation = savedHead;

                // An arm in front of the head absorbs the earliest physical contact.
                PlacePair(4f);
                target.CaptureContactPose();
                target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out center, out forward, out up);
                target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.LeftArm, out var armCenter, out _, out _);
                Transform arm = FindAnatomicalBone(target, "upper_arm.L");
                Vector3 savedArm = arm.localPosition;
                arm.position += center + forward * .3f - armCenter;
                target.CaptureContactPose();
                Assert.That(target.Hurtboxes.SweepSphere(center + forward * .8f, center, .025f, -forward, out var occluded), Is.True);
                Assert.That(occluded.Location.Region, Is.EqualTo(MeleeBodyRegion.LeftArm));
                arm.localPosition = savedArm;

                PlacePair(4f);
                var front = ProbeHead(target, MeleeHitSide.Front);
                target.SetBlock(true);
                target.State.Advance(S.ParryWindowSeconds + .01f);
                PrepareAnatomicalStrike(source, false);
                new CombatActor.Contact(source, target, front.Point, front.Normal, front.Direction, front.Location).Apply();
                Assert.That(target.LastImpact.Result, Is.EqualTo(MeleeHitResult.Blocked));
                Assert.That(target.State.Health, Is.EqualTo(100f));
                Assert.That(target.LastImpact.IsCritical, Is.False);
                Assert.That(root.BloodEffects.EmissionCount, Is.Zero);

                PlacePair(4f);
                var rear = ProbeHead(target, MeleeHitSide.Rear);
                source.ResetActor(target.transform.position - target.transform.forward * 2f, target.transform.forward);
                target.SetBlock(true);
                PrepareAnatomicalStrike(source, false);
                var recorded = new CombatActor.Contact(source, target, rear.Point, rear.Normal, rear.Direction, rear.Location);
                // Receiving another hit must not erase an already collected strike.
                source.State.ReceiveHit(1f, S.BlockCost, false);
                recorded.Apply();
                Assert.That(target.State.IsDefeated, Is.True);
                Assert.That(target.State.Health, Is.Zero);
                Assert.That(target.LastImpact.IsFinisher, Is.True);
                Assert.That(target.LastImpact.Location.Side, Is.EqualTo(MeleeHitSide.Rear));
                Assert.That(target.ActiveClipName, Is.EqualTo("CombatDefeat"));
                Assert.That(target.IsRagdollActive, Is.False, "One-shot retains the normal visible defeat handoff.");
                foreach (float step in new[] { 1f / 30f, 1f / 120f })
                    root.Tick(step);
                root.Tick(CombatAssetProvider.DefeatHandoffSeconds + .5f);
                Assert.That(target.IsRagdollActive, Is.True);
                Assert.That(target.IsWeaponDropped, Is.True);
                if (!heroVictim)
                {
                    // The rules tick starts physics; rendering the fall needs actual
                    // fixed frames, not another synchronous rules-only advance.
                    yield return new WaitForSeconds(.6f);
                    CaptureAnatomicalContact(target, "rear-head");
                }
                root.ResetRound();
                Assert.That(target.State.Health, Is.EqualTo(100f));
                Assert.That(target.IsRagdollActive, Is.False);
                Assert.That(target.ReceivedImpactCount, Is.Zero);
                Assert.That(root.BloodEffects.EmissionCount, Is.Zero);
            }

            // Exercise the production blade path too: arbitrary cloth/body colliders
            // cannot claim the anatomical hit or turn one sweep into multiple impacts.
            foreach (bool heroAttacks in new[] { true, false })
            {
                foreach (float step in new[] { 1f / 30f, 1f / 120f })
                {
                    PlacePair(1.1f);
                    CombatActor source = heroAttacks ? root.Hero : root.Opponent;
                    CombatActor target = heroAttacks ? root.Opponent : root.Hero;
                    var clothes = new GameObject("Anatomical clothing exclusion fixture");
                    clothes.transform.SetParent(target.transform, false);
                    SphereCollider trigger = clothes.AddComponent<SphereCollider>();
                    trigger.isTrigger = true; trigger.radius = 1.3f; trigger.center = Vector3.up;
                    Physics.SyncTransforms();
                    Assert.That(source.TryAttack(), Is.True);
                    for (float elapsed = 0; elapsed < 1.5f; elapsed += step) source.Step(step);
                    Assert.That(target.ReceivedImpactCount, Is.EqualTo(1), "The authored crowbar must reach the actual body exactly once.");
                    Assert.That(target.State.Health, Is.LessThan(100f));
                    Assert.That(target.LastImpact.Damage, Is.GreaterThan(0f));
                    Object.Destroy(clothes);
                }
            }
            LogAssert.NoUnexpectedReceived();
        }

        private static CombatHurtboxes.Hit ProbeHead(CombatActor target, MeleeHitSide side)
        {
            target.CaptureContactPose();
            Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out var center, out var forward, out _), Is.True);
            Vector3 outward = side == MeleeHitSide.Rear ? -forward : forward;
            Assert.That(target.Hurtboxes.SweepSphere(center + outward * .5f, center, .025f, -outward, out var hit), Is.True);
            Assert.That(hit.Location.Region, Is.EqualTo(MeleeBodyRegion.Head));
            Assert.That(hit.Location.Side, Is.EqualTo(side));
            return hit;
        }

        private static void PrepareAnatomicalStrike(CombatActor source, bool charged)
        {
            if (charged)
            {
                Assert.That(source.State.RequestCharge(), Is.True);
                source.State.Advance(S.ChargeSeconds);
                Assert.That(source.State.ReleaseCharge(), Is.True);
            }
            else Assert.That(source.TryAttack(), Is.True);
        }

        private static Transform FindAnatomicalBone(CombatActor actor, string name)
        {
            foreach (Transform bone in actor.DamageRigRoot.GetComponentsInChildren<Transform>(true))
                if (bone.name == name) return bone;
            Assert.Fail("Missing anatomical bone " + name);
            return null;
        }

        private void CaptureAnatomicalContact(CombatActor actor, string suffix)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            try
            {
                Vector3 subject = actor.transform.position + Vector3.up * 1.1f;
                camera.transform.SetPositionAndRotation(subject + actor.transform.forward * 2.6f + actor.transform.right * 1.6f + Vector3.up * .4f,
                    Quaternion.identity);
                camera.transform.LookAt(subject);
                string shot = "anatomical-" + suffix;
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, shot + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, shot);
            }
            finally { camera.transform.SetPositionAndRotation(position, rotation); }
        }
    }
}
