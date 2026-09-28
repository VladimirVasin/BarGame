using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatPerformancePlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_StrongShoveKeepsRecipientGripWhileMoving() => CheckShoveArmMotion(false);

        [UnityTest]
        public IEnumerator Range_ObstructedShoveRecipientArmRecoversWhileMoving() => CheckShoveArmMotion(true);

        private IEnumerator CheckShoveArmMotion(bool obstructContact)
        {
            var failures = new List<string>();
            for (int rig = 0; rig < 2; rig++)
            {
                PlacePair(.80f);
                for (int warmup = 0; warmup < 6; warmup++)
                { root.Tick(TickSeconds); yield return new WaitForEndOfFrame(); }
                CombatActor victim = rig == 0 ? root.Hero : root.Opponent;
                CombatActor source = rig == 0 ? root.Opponent : root.Hero;
                string label = victim.IsHero ? "hero" : "opponent";
                string subject = "balance/arm-motion/" + (obstructContact ? "obstructed/" : "retained/") + label;
                string folder = Path.GetFullPath(Path.Combine("Captures", SceneIds.CombatTest, subject));
                Directory.CreateDirectory(folder);
                Transform upper = ArmMotionBone(victim, "upper_arm.L");
                Transform lower = ArmMotionBone(victim, "forearm.L");
                Transform hand = ArmMotionBone(victim, "hand.L");
                Transform chest = ArmMotionBone(victim, "chest");
                NpcHandPose hands = victim.GetComponentInChildren<NpcHandPose>();
                Assert.That(hands, Is.Not.Null);
                float upperLength = Vector3.Distance(upper.position, lower.position);
                float lowerLength = Vector3.Distance(lower.position, hand.position);
                Quaternion previousUpper = upper.localRotation, previousLower = lower.localRotation, previousHand = hand.localRotation;
                Quaternion upperInChest = Quaternion.Inverse(chest.rotation) * upper.rotation;
                Vector3 upperAxisLocal = Quaternion.Inverse(upper.rotation) * (lower.position - upper.position).normalized;
                Vector3 bendLocal = Quaternion.Inverse(upper.rotation) *
                    (-Vector3.ProjectOnPlane(hand.position - lower.position, lower.position - upper.position)).normalized;
                Quaternion startFacing = victim.transform.rotation;
                float health = victim.State.Health, moved = 0f, turned = 0f;
                float impulse = 0f, contactGap = float.PositiveInfinity;
                float maxRadial = 0f, maxFlexion = 0f, maxElbow = 0f, maxJointDelta = 0f, maxLengthError = 0f;
                float maxShoulderRoll = 0f, minSignedElbow = float.PositiveInfinity;
                int contacts = 0, freeFrames = 0, regripFrames = 0, releasingFrames = 0, unsupportedSamples = 0;
                string previousRejection = null;
                string obstructionRejection = null;
                bool obstructed = false, obstructionReleased = false, contactFrame = false;
                bool firstCatchFrame = false, secondCatchFrame = false, lateFreeFrame = false;
                bool releaseFrame = false, freeFrame = false, regripFrame = false, restoredFrame = false;
                var csv = new StringBuilder("frame,seconds,state,has_support,catch_steps,landed_steps,movement_scale,turn_scale,wrist_radial,wrist_flexion,elbow_bend,joint_delta,length_error,shaft_gap," +
                    "upper_x,upper_y,upper_z,upper_qx,upper_qy,upper_qz,upper_qw," +
                    "lower_x,lower_y,lower_z,lower_qx,lower_qy,lower_qz,lower_qw," +
                    "hand_x,hand_y,hand_z,hand_qx,hand_qy,hand_qz,hand_qw,shoulder_roll,elbow_signed,rejection_change\n");
                void Receive(CombatImpact impact)
                {
                    contacts++;
                    impulse = impact.Impulse.magnitude;
                    contactGap = Vector3.Distance(source.ShovePalmPosition, impact.Point);
                }
                victim.ImpactReceived += Receive;
                try
                {
                    CaptureDuelFrame(subject, "00-supported");
                    if (!victim.HasTwoHandSupport) failures.Add(label + ": scenario must start with the actual two-hand grip.");
                    bool started = source.RequestAttack();
                    if (!started) failures.Add(label + ": RequestAttack did not start the real shove.");
                    for (int frame = 0; frame < 240; frame++)
                    {
                        // Script only requested capsule travel/facing. The current
                        // combat gates still decide when either is permitted.
                        if (frame < 120 && victim.Body.enabled && !victim.IsKnockedDown)
                        {
                            Vector3 before = victim.transform.position;
                            victim.Body.Move(victim.transform.right * (.25f * victim.MovementScale * TickSeconds));
                            Vector3 achieved = victim.transform.position - before;
                            achieved.y = 0f;
                            moved += achieved.magnitude;
                            victim.SetLocomotion(achieved / TickSeconds);
                            Quaternion target = Quaternion.AngleAxis(20f, Vector3.up) * startFacing;
                            Quaternion facing = victim.transform.rotation;
                            victim.transform.rotation = Quaternion.RotateTowards(facing, target,
                                (contacts == 0 ? 12f : 40f) * victim.TurnScale * TickSeconds);
                            turned += Quaternion.Angle(facing, victim.transform.rotation);
                        }
                        else victim.SetLocomotion(Vector3.zero);
                        root.Tick(TickSeconds);
                        // HasTwoHandSupport includes the balance gate for the
                        // guard. Measure the actual hand contact here.
                        if (!obstructContact && !victim.SupportGrip.IsSupportingWeapon) unsupportedSamples++;
                        if (obstructContact && contacts > 0 && !obstructed)
                        {
                            // Only real geometry forces the release. Keep the
                            // obstruction for one complete presentation, then
                            // let normal recovery regain an unobstructed shaft.
                            var obstacle = new GameObject("ShoveGripPalmObstacle");
                            try
                            {
                                obstacle.transform.position = victim.SupportGripWorldPosition;
                                obstacle.AddComponent<SphereCollider>().radius = .015f;
                                Physics.SyncTransforms();
                                victim.Present();
                                obstructionReleased = !victim.SupportGrip.IsSupportingWeapon;
                                obstructionRejection = victim.SupportGrip.LastPoseRejection;
                            }
                            finally { Object.DestroyImmediate(obstacle); Physics.SyncTransforms(); }
                            obstructed = true;
                        }
                        // Update may restore an additive layer. Read and capture
                        // only the actual rig that completed every LateUpdate.
                        yield return new WaitForEndOfFrame();
                        if (!obstructContact && !victim.SupportGrip.IsSupportingWeapon) unsupportedSamples++;
                        Vector3 palm = hands.PalmNormal(true);
                        Vector3 fingers = Vector3.ProjectOnPlane(hands.CylinderCentre(true) - hand.position, palm).normalized;
                        Vector3 forearm = (hand.position - lower.position).normalized;
                        float radial = Mathf.Abs(Mathf.Asin(Mathf.Clamp(Vector3.Dot(forearm,
                            Vector3.Cross(palm, fingers).normalized), -1f, 1f)) * Mathf.Rad2Deg);
                        float flexion = Mathf.Abs(Mathf.Atan2(Vector3.Dot(forearm, palm), Vector3.Dot(forearm, fingers)) * Mathf.Rad2Deg);
                        float elbow = Vector3.Angle(lower.position - upper.position, hand.position - lower.position);
                        Vector3 upperAxis = (lower.position - upper.position).normalized;
                        Quaternion reference = chest.rotation * upperInChest;
                        Quaternion aimed = Quaternion.FromToRotation(reference * upperAxisLocal, upperAxis) * reference;
                        float shoulderRoll = Vector3.SignedAngle(Vector3.ProjectOnPlane(aimed * bendLocal, upperAxis),
                            Vector3.ProjectOnPlane(upper.rotation * bendLocal, upperAxis), upperAxis);
                        Vector3 reachAxis = (hand.position - upper.position).normalized;
                        float signedElbow = elbow * (Vector3.Dot(Vector3.ProjectOnPlane(lower.position - upper.position, reachAxis),
                            Vector3.ProjectOnPlane(upper.rotation * bendLocal, reachAxis)) < 0f ? -1f : 1f);
                        float jointDelta = Mathf.Max(Quaternion.Angle(previousUpper, upper.localRotation),
                            Mathf.Max(Quaternion.Angle(previousLower, lower.localRotation), Quaternion.Angle(previousHand, hand.localRotation)));
                        float lengthError = Mathf.Max(Mathf.Abs(Vector3.Distance(upper.position, lower.position) - upperLength),
                            Mathf.Abs(Vector3.Distance(lower.position, hand.position) - lowerLength));
                        float gap = Vector3.Distance(hands.CylinderCentre(true), victim.SupportGripWorldPosition);
                        csv.AppendFormat(CultureInfo.InvariantCulture, "{0},{1:F5},{2},{3},{4},{5},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4},{12:F6},{13:F6}",
                            frame, (frame + 1) * TickSeconds, victim.SupportArmState, victim.SupportGrip.IsSupportingWeapon,
                            victim.Footwork.CatchStepCount, victim.ImpactMotion.LandedRecoverySteps, victim.MovementScale, victim.TurnScale,
                            radial, flexion, elbow, jointDelta, lengthError, gap);
                        AppendArmTransform(csv, upper); AppendArmTransform(csv, lower); AppendArmTransform(csv, hand);
                        csv.AppendFormat(CultureInfo.InvariantCulture, ",{0:F4},{1:F4}", shoulderRoll, signedElbow);
                        csv.Append(',');
                        string rejection = victim.SupportGrip.LastPoseRejection;
                        if (rejection != previousRejection) csv.Append(rejection ?? "cleared");
                        previousRejection = rejection;
                        csv.AppendLine();
                        previousUpper = upper.localRotation; previousLower = lower.localRotation; previousHand = hand.localRotation;
                        if (contacts > 0)
                        {
                            maxRadial = Mathf.Max(maxRadial, radial); maxFlexion = Mathf.Max(maxFlexion, flexion);
                            maxElbow = Mathf.Max(maxElbow, elbow); maxJointDelta = Mathf.Max(maxJointDelta, jointDelta);
                            maxLengthError = Mathf.Max(maxLengthError, lengthError);
                            maxShoulderRoll = Mathf.Max(maxShoulderRoll, Mathf.Abs(shoulderRoll));
                            minSignedElbow = Mathf.Min(minSignedElbow, signedElbow);
                            if (!contactFrame) { CaptureDuelFrame(subject, "01-contact"); contactFrame = true; }
                            if (victim.SupportArmState == CombatArmSupportState.Releasing)
                            {
                                releasingFrames++;
                                if (!releaseFrame) { CaptureDuelFrame(subject, "02-release"); releaseFrame = true; }
                            }
                            if (victim.SupportArmState == CombatArmSupportState.Free) freeFrames++;
                            if (!freeFrame && freeFrames >= 3)
                            { CaptureDuelFrame(subject, "03-free"); freeFrame = true; }
                            if (!lateFreeFrame && freeFrames >= 35 && victim.SupportArmState == CombatArmSupportState.Free)
                            { CaptureDuelFrame(subject, "04-free-late"); lateFreeFrame = true; }
                            if (victim.SupportArmState == CombatArmSupportState.Regripping) regripFrames++;
                            if (!regripFrame && regripFrames >= 3)
                            { CaptureDuelFrame(subject, "05-regrip"); regripFrame = true; }
                            if (!obstructContact && victim.Footwork.CatchStepActive && victim.Footwork.CatchStepProgress >= .4f)
                            {
                                if (!firstCatchFrame && victim.Footwork.CatchStepCount == 1)
                                { CaptureDuelFrame(subject, "02-first-catch-supported"); firstCatchFrame = true; }
                                if (!secondCatchFrame && victim.Footwork.CatchStepCount == 2)
                                { CaptureDuelFrame(subject, "03-second-catch-supported"); secondCatchFrame = true; }
                            }
                            if ((!obstructContact || regripFrame) && !restoredFrame && victim.HasTwoHandSupport && frame >= 120)
                            { CaptureDuelFrame(subject, obstructContact ? "06-restored" : "04-still-supported"); restoredFrame = true; }
                            if (restoredFrame && frame >= 150) break;
                        }
                    }
                    if (!restoredFrame) CaptureDuelFrame(subject, "06-final-unrestored");
                    string report = FormattableString.Invariant($"{label}: obstructed={obstructed}, obstructionReleased={obstructionReleased}, obstructionRejection={obstructionRejection}, contacts={contacts}, impulse={impulse:F3}, palmGap={contactGap:F5}, travel={moved:F4}m, turn={turned:F2}deg, maxRadial={maxRadial:F3}, maxFlexion={maxFlexion:F3}, maxElbow={maxElbow:F3}, maxJointDelta={maxJointDelta:F3}, maxLengthError={maxLengthError:F6}, unsupportedSamples={unsupportedSamples}, releasingFrames={releasingFrames}, freeFrames={freeFrames}, regripFrames={regripFrames}, restored={restoredFrame}, {DescribeBalance(victim)}");
                    report += FormattableString.Invariant($", contactAngle={victim.SupportGrip.JournalContactAngle:F3}, wristSafe={victim.SupportGrip.JournalWristSafe}, weight={victim.SupportGrip.Weight:F3}, wait={victim.SupportGrip.JournalGripReason}");
                    report += FormattableString.Invariant($", maxShoulderRoll={maxShoulderRoll:F3}, minSignedElbow={minSignedElbow:F3}");
                    File.WriteAllText(Path.Combine(folder, "summary.txt"), report);
                    TestContext.Out.WriteLine(report);
                    void Check(bool valid, string reason) { if (!valid) failures.Add(label + ": " + reason); }
                    Check(contacts == 1 && Mathf.Abs(impulse - 165f) < .01f && contactGap <= .0801f, "requires one actual 165Ns palm contact");
                    Check(victim.State.Health == health, "shove must not damage health");
                    Check(moved > .02f && turned > 3f, "scenario must exercise allowed movement and turning");
                    if (obstructContact)
                    {
                        Check(obstructed && obstructionReleased && !string.IsNullOrEmpty(obstructionRejection),
                            "the real palm obstruction must cause the release");
                        Check(releaseFrame && freeFrame && regripFrame && restoredFrame, "release/free/regrip/restored must all be presented");
                    }
                    else
                    {
                        Check(unsupportedSamples == 0 && releasingFrames == 0 && freeFrames == 0 && regripFrames == 0,
                            "the modest body response must retain the real grip throughout movement and catch steps");
                        Check(victim.Footwork.CatchStepCount > 0 && victim.Footwork.CatchStepCount <= 2 &&
                            victim.ImpactMotion.LandedRecoverySteps == victim.Footwork.CatchStepCount && !victim.Footwork.CatchStepActive,
                            "scenario must complete every actual catch, with one or two bounded steps and no hand release");
                        Check(firstCatchFrame && (victim.Footwork.CatchStepCount < 2 || secondCatchFrame) && restoredFrame,
                            "each supported catch step and the settled grip must be presented");
                    }
                    Check(maxRadial <= 25.1f && maxFlexion <= 55.1f, $"visible wrist limits: radial={maxRadial:F2}, flexion={maxFlexion:F2}");
                    Check(maxElbow <= 120.1f, $"visible elbow bend={maxElbow:F2}");
                    Check(maxShoulderRoll <= 90.1f, $"upper arm must retain its natural shoulder branch: roll={maxShoulderRoll:F2}");
                    Check(minSignedElbow >= -5.1f, $"elbow must not bend backwards: signed={minSignedElbow:F2}");
                    Check(maxJointDelta <= 35f, $"visible local joint jump={maxJointDelta:F2} degrees per 60Hz frame");
                    Check(maxLengthError <= .002f, "IK must retain original limb lengths");
                    Check(victim.HasTwoHandSupport && Vector3.Distance(hands.CylinderCentre(true), victim.SupportGripWorldPosition) <= .025f,
                        "final actual cylinder contact must restore the grip");
                }
                finally
                {
                    victim.ImpactReceived -= Receive;
                    File.WriteAllText(Path.Combine(folder, "arm.csv"), csv.ToString());
                }
            }
            // Both rigs leave review evidence even when the first violates an
            // anatomical limit; do not turn a baseline capture into a false pass.
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
            LogAssert.NoUnexpectedReceived();
        }

        private static Transform ArmMotionBone(CombatActor actor, string name)
        {
            Transform bone = Array.Find(actor.DamageRigRoot.GetComponentsInChildren<Transform>(true), value => value.name == name);
            Assert.That(bone, Is.Not.Null, name);
            return bone;
        }

        private static void AppendArmTransform(StringBuilder output, Transform bone)
        {
            Vector3 p = bone.position; Quaternion q = bone.rotation;
            output.AppendFormat(CultureInfo.InvariantCulture, ",{0:F6},{1:F6},{2:F6},{3:F6},{4:F6},{5:F6},{6:F6}",
                p.x, p.y, p.z, q.x, q.y, q.z, q.w);
        }
    }
}
