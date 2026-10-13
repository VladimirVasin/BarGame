using System.Collections;
using System.Reflection;
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
        public IEnumerator Range_CrowbarSegmentsMatchVisibleContactsAndBlockedSwingsRecover()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            yield return EnterRange();
            VerifyAttachedCrowbarForearmSweep();
            VerifyCompensatingForearmProductionSweep();
            VerifyCrowbarWorldSegments();
            VerifyCrowbarBodySegments();
            // These existing physical oracles retain first-surface ordering and
            // one hit across all segments, for either authored actor rig.
            VerifyWorldBeforeMovingMetal();
            VerifyBodyBeforeMetal();
            foreach (CombatActor actor in new[] { root.Hero, root.Opponent })
                ((CombatWeaponConstraint)typeof(CombatActor).GetField("weaponConstraint",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor)).CaptureRejectedPoseDiagnostics = true;
            VerifyUnoccludedPhysicalBodyContact(true);
            yield return VerifyRejectedCrowbarSwingReturnsAndRetries();
            VerifyUnoccludedPhysicalBodyContact(false);
            LogAssert.NoUnexpectedReceived();
        }

        private void VerifyCrowbarWorldSegments()
        {
            PlacePair(4f);
            var obstacle = new GameObject("Test obstacle beside the real crowbar shaft");
            SphereCollider shape = obstacle.AddComponent<SphereCollider>();
            shape.radius = .006f;
            var pose = new Pose(new Vector3(0f, 3f, 0f), Quaternion.identity);
            Vector3 shaft = pose.position + new Vector3(0f, .465f, 0f);
            shape.transform.position = shaft;
            try
            {
                Physics.SyncTransforms();
                Vector3 chordA = pose.position + CombatWeaponGeometry.StrikeBaseLocal;
                Vector3 chordB = pose.position + CombatWeaponGeometry.StrikeTipLocal;
                Assert.That(Vector3.Distance(ClosestCrowbarChord(chordA, chordB, shaft), shaft), Is.GreaterThan(.10f));
                Assert.That(Physics.CheckCapsule(chordA, chordB, .024f, ~0, QueryTriggerInteraction.Ignore), Is.False,
                    "The old damage chord completely misses the visible shaft contact.");
                MethodInfo gather = typeof(CombatActor).GetMethod("GatherWorldContact", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(gather, Is.Not.Null);
                object[] args = { pose, 0f, Vector3.zero, Vector3.zero };
                Assert.That((bool)gather.Invoke(root.Hero, args), Is.True,
                    "A tiny real obstacle touching the shaft must stop the same geometry as body/parry queries.");
                Assert.That((float)args[1], Is.EqualTo(1f));
                shape.transform.position = (chordA + chordB) * .5f;
                Physics.SyncTransforms();
                Assert.That((bool)gather.Invoke(root.Hero, args), Is.False,
                    "Empty space inside the old diagonal chord must not invent a world contact.");
            }
            finally { Object.DestroyImmediate(obstacle); }
        }

        private static void VerifyAttachedCrowbarForearmSweep()
        {
            var armHost = new GameObject("Test moving forearm query");
            var probeHost = new GameObject("Test crowbar segment query");
            armHost.transform.position = probeHost.transform.position = Vector3.up * 10000f;
            CapsuleCollider arm = armHost.AddComponent<CapsuleCollider>();
            CapsuleCollider probe = probeHost.AddComponent<CapsuleCollider>();
            arm.direction = probe.direction = 1; arm.height = 1f; arm.radius = .05f;
            arm.isTrigger = probe.isTrigger = true;
            try
            {
                var from = new Pose(Vector3.zero, Quaternion.identity);
                var to = new Pose(Vector3.zero, Quaternion.Euler(0f, 0f, 180f));
                var barFrom = new Pose(from.position + from.rotation * Vector3.up * .72f, from.rotation);
                var barTo = new Pose(to.position + to.rotation * Vector3.up * .72f, to.rotation);
                Assert.That(IntersectsArm(barFrom, from), Is.False);
                Assert.That(IntersectsArm(barTo, to), Is.False);
                Pose middle = CombatWeaponGeometry.At(from, to, .5f);
                Assert.That(IntersectsArm(CombatWeaponGeometry.At(barFrom, barTo, .5f), middle), Is.True,
                    "Independent world-space chords invent a forearm crossing despite clear attached endpoints.");
                for (int step = 0; step <= 16; step++)
                {
                    float t = step / 16f;
                    Assert.That(IntersectsArm(CombatWeaponConstraint.AttachedWeaponPoseAt(barFrom, barTo, from, to, t),
                        CombatWeaponGeometry.At(from, to, t)), Is.False,
                        "The actual palm linkage stays clear throughout the same rotation; no forearm collision is exempted.");
                }

                bool IntersectsArm(Pose bar, Pose forearm)
                {
                    foreach (CombatWeaponGeometry.Segment segment in CombatWeaponGeometry.Segments)
                    {
                        Vector3 a = bar.position + bar.rotation * segment.A, b = bar.position + bar.rotation * segment.B;
                        probe.radius = segment.Radius; probe.height = Vector3.Distance(a, b) + segment.Radius * 2f;
                        Quaternion rotation = (b - a).sqrMagnitude > .000001f ? Quaternion.FromToRotation(Vector3.up, b - a) : Quaternion.identity;
                        if (Physics.ComputePenetration(probe, (a + b) * .5f, rotation, arm,
                            forearm.position, forearm.rotation, out _, out float overlap) && overlap > .001f) return true;
                    }
                    return false;
                }
            }
            finally { Object.DestroyImmediate(armHost); Object.DestroyImmediate(probeHost); }
        }

        private void VerifyCrowbarBodySegments()
        {
            PlacePair(4f);
            CombatActor target = root.Opponent;
            target.CaptureContactPose();
            Assert.That(target.Hurtboxes.SweepSphere(root.Hero.transform.position + Vector3.up * 1.3f,
                target.transform.position + Vector3.up * 1.3f, .0001f, root.Hero.transform.forward, out var chest), Is.True);
            // Search only a small deterministic set of orientations around the
            // actual imported chest. This probes surface contact rather than a
            // hand-built substitute hurtbox, whose shape could mask the bug.
            bool foundOutsideChord = false;
            foreach (Vector3 local in new[] { new Vector3(0f, .465f, 0f), new Vector3(0f, .525f, .022f), new Vector3(0f, .562f, .152f) })
            for (int roll = 0; roll < 12 && !foundOutsideChord; roll++)
            {
                Quaternion rotation = Quaternion.AngleAxis(roll * 30f, chest.Normal) * Quaternion.LookRotation(chest.Normal, Vector3.up);
                var pose = new Pose(chest.Point + chest.Normal * .023f - rotation * local, rotation);
                bool chord = target.Hurtboxes.SweepSphere(pose.position + rotation * CombatWeaponGeometry.StrikeBaseLocal,
                    pose.position + rotation * CombatWeaponGeometry.StrikeTipLocal, .024f, -chest.Normal, out _);
                bool real = CombatWeaponGeometry.SweepAnatomy(target.Hurtboxes, pose, pose,
                    CombatTestRoot.SimulationStep, -chest.Normal, out var hit, out float fraction, out _);
                if (chord || !real) continue;
                foundOutsideChord = true;
                Assert.That(fraction, Is.EqualTo(1f));
                Assert.That(Vector3.Distance(hit.Point, chest.Point), Is.LessThan(.65f));
            }
            Assert.That(foundOutsideChord, Is.True,
                "The real imported body must admit a shaft/hook contact outside the old diagonal capsule.");
        }

        private void VerifyCompensatingForearmProductionSweep()
        {
            PlacePair(4f);
            CombatActor actor = root.Hero;
            var constraint = (CombatWeaponConstraint)typeof(CombatActor).GetField("weaponConstraint",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor);
            var shapes = (System.Collections.Generic.List<Collider>)typeof(CombatWeaponConstraint).GetField("anatomy",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(constraint);
            var previous = (Pose[])typeof(CombatWeaponConstraint).GetField("previousAnatomy",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(constraint);
            int index = (int)typeof(CombatWeaponConstraint).GetField("rightForearmIndex",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(constraint);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            var shape = (CapsuleCollider)shapes[index];
            Transform frame = shape.transform;
            frame.GetLocalPositionAndRotation(out Vector3 position, out Quaternion rotation);
            Pose oldPrevious = previous[index];
            var armFrom = new Pose(Vector3.up * 100f, Quaternion.identity);
            var armTo = new Pose(armFrom.position, Quaternion.Euler(0f, 0f, 180f));
            var fixedBar = new Pose(armFrom.position + Vector3.up * (shape.height * .5f + .20f), Quaternion.identity);
            MethodInfo sweep = typeof(CombatWeaponConstraint).GetMethod("SweepClear", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo clear = typeof(CombatWeaponConstraint).GetMethod("ClearAnatomyReuse", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                // A moving real own capsule plus an independently compensating
                // wrist has identical world bar endpoints. Its relative path
                // crosses the forearm; the full production sweep must see it.
                frame.SetPositionAndRotation(armTo.position, armTo.rotation);
                previous[index] = armFrom;
                Physics.SyncTransforms();
                clear.Invoke(constraint, null);
                Assert.That((bool)sweep.Invoke(constraint, new object[] { fixedBar, fixedBar }), Is.False,
                    "The production native pair cannot cull relative motion just because world weapon endpoints coincide.");
                Assert.That(constraint.BlockingShape, Is.EqualTo(shape.name));
            }
            finally
            {
                frame.SetLocalPositionAndRotation(position, rotation);
                previous[index] = oldPrevious;
                clear.Invoke(constraint, null);
                Physics.SyncTransforms();
            }
        }

        private IEnumerator VerifyRejectedCrowbarSwingReturnsAndRetries()
        {
            PlacePair(4f);
            CombatActor actor = root.Hero, target = root.Opponent;
            var constraint = (CombatWeaponConstraint)typeof(CombatActor).GetField("weaponConstraint",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor);
            CapsuleCollider forearm = null;
            foreach (var pair in actor.Ragdoll.PhysicsController.AnatomicalColliders)
                if (pair.Value == Player3DAnatomicalPart.RightForearm) forearm = pair.Key as CapsuleCollider;
            Assert.That(forearm, Is.Not.Null);
            Transform tip = CombatAssetProvider.FindAnchor(actor.Weapon, "StrikeTip");
            float originalRadius = forearm.radius;
            bool originalDiagnostics = constraint.CaptureRejectedPoseDiagnostics;
            constraint.CaptureRejectedPoseDiagnostics = true;
            try
            {
                for (int frame = 0; frame < 8; frame++) { actor.Step(1f / 60f); yield return null; }
                Assert.That(actor.RequestCharge(), Is.True);
                for (int frame = 0; frame < 20; frame++) { actor.Step(1f / 60f); yield return null; }
                CaptureCrowbarReturn("raised");
                // A real own-arm obstruction cannot become a world impact or
                // be bypassed by widening the damage proxy. Enlarging this
                // capsule also invalidates the preceding held pose, so recovery
                // starts from the actual stopped rig rather than that old pose.
                forearm.radius = .8f;
                Physics.SyncTransforms();
                Assert.That(actor.ReleaseCharge(), Is.True);
                bool rejected = false;
                for (int frame = 0; frame < 90 && actor.BlockedWeaponSwingCount == 0; frame++)
                {
                    actor.Step(1f / 120f); yield return null;
                    rejected |= constraint.MotionBlocked;
                }
                Assert.That(rejected, Is.True, "Exercise a physically rejected pose, not a fabricated state flag.");
                Assert.That(actor.BlockedWeaponSwingCount, Is.EqualTo(1));
                Assert.That(actor.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Obstacle));
                Assert.That(target.ReceivedImpactCount, Is.Zero);
                Assert.That(actor.LastRejectedWeaponGeometry.ownForearmPenetration, Is.True,
                    "The stopped action must come from real native own-forearm penetration.");
                Assert.That(actor.LastRejectedWeaponDepth, Is.GreaterThan(.001f));
                Vector3 stoppedTip = tip.position;
                CaptureCrowbarReturn("stopped");

                forearm.radius = originalRadius;
                Physics.SyncTransforms();
                float returnTravel = 0f;
                Vector3 previous = tip.position;
                for (int frame = 0; frame < 90; frame++)
                {
                    actor.Step(1f / 60f); yield return null;
                    returnTravel += Vector3.Distance(previous, tip.position); previous = tip.position;
                }
                Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Ready));
                Assert.That(returnTravel, Is.GreaterThan(.15f), "Recovery must visibly lower/move the real arm, not merely finish its rules clock.");
                Assert.That(Vector3.Distance(stoppedTip, tip.position), Is.GreaterThan(.08f));
                Assert.That(actor.HasWeaponInHand, Is.True);
                CaptureCrowbarReturn("returned");

                target.ResetActor(actor.transform.position + actor.transform.forward * 1.1f, -actor.transform.forward);
                target.Weapon.SetActive(false);
                Physics.SyncTransforms();
                for (int frame = 0; frame < 8; frame++) { actor.Step(1f / 60f); yield return null; }
                Assert.That(actor.TryAttack(), Is.True);
                float retryTravel = 0f;
                previous = tip.position;
                bool arcCaptured = false;
                bool retryContactCaptured = false;
                for (int frame = 0; frame < 120; frame++)
                {
                    actor.Step(1f / 120f); yield return null;
                    retryTravel += Vector3.Distance(previous, tip.position); previous = tip.position;
                    if (!retryContactCaptured && target.ReceivedImpactCount > 0)
                    {
                        retryContactCaptured = true;
                        VerifyCrowbarVisibleImpactContact(actor, target);
                        CaptureCrowbarReturn("retry-contact");
                    }
                    if (!arcCaptured && actor.State.Phase == MeleePhase.Active)
                    { CaptureCrowbarReturn("retry-arc"); arcCaptured = true; }
                }
                Assert.That(retryTravel, Is.GreaterThan(.15f), "The next free swing moves the visible weapon through its arc.");
                if (actor.BlockedWeaponSwingCount > 1)
                    System.IO.File.WriteAllText(System.IO.Path.Combine("TestResults", "crowbar-retry-block.json"),
                        JsonUtility.ToJson(actor.LastRejectedWeaponGeometry, true));
                Assert.That(actor.BlockedWeaponSwingCount, Is.EqualTo(1),
                    $"The free retry cannot inherit the old blocked action: outcome={actor.State.AttackOutcome}, " +
                    $"depth={actor.LastRejectedWeaponDepth:F6}, shape={actor.LastRejectedWeaponGeometry.finalBlockingShape}.");
                Assert.That(target.ReceivedImpactCount, Is.EqualTo(1),
                    $"The standing recipient receives one actual segment contact after the obstruction clears: " +
                    $"outcome={actor.State.AttackOutcome}, phase={actor.State.Phase}, reach={actor.AttackReach01:F3}, " +
                    $"actor={actor.transform.position}, target={target.transform.position}, travel={retryTravel:F3}m.");
            }
            finally
            {
                forearm.radius = originalRadius;
                constraint.CaptureRejectedPoseDiagnostics = originalDiagnostics;
                target.Weapon.SetActive(true);
            }
        }

        private static Vector3 ClosestCrowbarChord(Vector3 from, Vector3 to, Vector3 point)
        {
            Vector3 delta = to - from;
            return from + delta * Mathf.Clamp01(Vector3.Dot(point - from, delta) / delta.sqrMagnitude);
        }

        private void CaptureCrowbarReturn(string name)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            try
            {
                Vector3 hero = root.Hero.transform.position;
                Vector3 view = hero - root.Hero.transform.right * 2.8f - root.Hero.transform.forward * 1.7f + Vector3.up * 1.6f;
                camera.transform.SetPositionAndRotation(view, Quaternion.LookRotation(hero + Vector3.up * 1.25f - view));
                string path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest + "/CrowbarReturn", name + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest + "/CrowbarReturn", name);
            }
            finally { camera.transform.SetPositionAndRotation(position, rotation); }
        }

        private static void VerifyCrowbarRepeatPresentation(CombatActor actor, CombatActor target,
            CombatWeaponConstraint constraint)
        {
            NpcHandPose hands = actor.GetComponentInChildren<NpcHandPose>();
            Transform hand = null;
            foreach (var binding in hands.Hands) if (!binding.IsLeft) hand = binding.Hand;
            Assert.That(hand, Is.Not.Null);
            Quaternion rotation = hand.localRotation;
            Vector3 position = actor.Weapon.transform.position;
            Quaternion weaponRotation = actor.Weapon.transform.rotation;
            float desiredDepth = constraint.DesiredPoseDepth;
            float health = target.State.Health;
            for (int repeat = 0; repeat < 2; repeat++)
            {
                actor.Present();
                Assert.That(Quaternion.Angle(rotation, hand.localRotation), Is.LessThan(.05f),
                    "Repeating the same authored clock cannot accumulate a wrist correction.");
                Assert.That(Vector3.Distance(position, actor.Weapon.transform.position), Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(weaponRotation, actor.Weapon.transform.rotation), Is.LessThan(.05f));
                Assert.That(constraint.DesiredPoseDepth, Is.EqualTo(desiredDepth).Within(.00001f));
                Assert.That(constraint.RightWristWithinLimits, Is.True);
                Assert.That(target.State.Health, Is.EqualTo(health));
            }
        }
    }
}
