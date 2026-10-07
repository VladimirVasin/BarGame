using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatPerformancePlayModeTests
    {
        private static readonly bool[] RecoveryVictims = { false, true };
        private static readonly bool[] RecoveryDirections = { false, true };

        [UnityTest]
        public IEnumerator Range_InterruptedRiseHasBoundedWorkAndRecovers(
            [ValueSource(nameof(RecoveryVictims))] bool hero,
            [ValueSource(nameof(RecoveryDirections))] bool forward)
        {
            PlacePair(4f);
            for (int warm = 0; warm < 12; warm++) { root.Tick(TickSeconds); yield return null; }
            CombatActor victim = hero ? root.Hero : root.Opponent;
            string subject = $"{(hero ? "hero" : "opponent")}/{(forward ? "forward" : "backward")}";
            string captureSubject = (hero ? "hero-" : "opponent-") + (forward ? "forward-" : "backward-");
            var constraint = (CombatWeaponConstraint)typeof(CombatActor).GetField("weaponConstraint",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(victim);
            Assert.That(Physics.Raycast(victim.transform.position + Vector3.up * .5f, Vector3.down,
                out RaycastHit floor, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(floor.normal.y, Is.GreaterThan(.99f), "This recovery fixture uses the arena's level floor.");
            AssertRecoveryReadyPose(root.Hero, true);
            AssertRecoveryReadyPose(root.Opponent, false);
            AssertRecoveryAnimationEndpoints(root.Hero, true);
            AssertRecoveryAnimationEndpoints(root.Opponent, false);
            if (!hero && !forward) CaptureDuelFrame("rise", "ready");
            string folder = Path.GetFullPath("TestResults/recovery-" + (hero ? "hero-" : "opponent-") +
                (forward ? "forward-" : "backward-") + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            var anatomy = new RecoveryAnatomyObservation(victim);
            var anatomyCsv = new StringBuilder("frame,progress,side,thigh_length,shin_length,hip_flexion,hip_lateral,knee_flexion,ankle_deviation,sole_gap\n");
            root.SetDuelLogging(true, folder);
            var journal = root.JournalForDiagnostics;
            string[] names = { "Prepare", "Begin", "Advance", "Present", "Pose", "Sample", "Soles", "WeaponApply", "WeaponCommit" };
            var counters = new ProfilerRecorder[names.Length];
            var totals = new double[names.Length];
            var calls = new long[names.Length];
            var maxima = new double[names.Length];
            var cpu = new List<double>();
            var csv = new StringBuilder("frame,phase,progress,central_speed,ground,support,quiet_seconds,tick_ms,queries\n");
            int hits = 0, riseFrames = 0;
            long maximumQueries = 0;
            bool recovered = false, capturedBrace = false;
            float lyingSeconds = 0f, maximumLyingSeconds = 0f, stalledRiseSeconds = 0f;
            float lastRiseProgress = -1f;
            try
            {
                for (int i = 0; i < counters.Length; i++)
                    counters[i] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "BarPromenade.CombatRecovery." + names[i], 1,
                        ProfilerRecorderOptions.Default | ProfilerRecorderOptions.CollectOnlyOnCurrentThread |
                        ProfilerRecorderOptions.SumAllSamplesInFrame);
                Vector3 fallDirection = victim.transform.forward * (forward ? 1f : -1f);
                var first = RecoveryTestImpact(victim, fallDirection * 205f, 1);
                Assert.That(victim.TryBeginKnockdown(first, fallDirection * .8f,
                    victim.transform.right * (forward ? -1.1f : 1.1f)), Is.True);
                for (int frame = 0; frame < 1560 && !recovered; frame++)
                {
                    bool rising = victim.State.Phase == MeleePhase.Rising;
                    if (rising && hits < 2 && victim.JournalRiseProgress >= (hits == 0 ? .55f : .9f))
                    {
                        // The recorded failing duel contains two downward contacts
                        // during a rise. Preserve their momentum, using the live torso.
                        Vector3 impulse = hits == 0 ? new Vector3(70f, -173f, 127f) : new Vector3(-27f, -158f, -32f);
                        victim.ApplyImpactForDiagnostics(RecoveryTestImpact(victim, impulse, hits + 2));
                        hits++;
                    }
                    long queries = victim.JournalPhysicsQueries;
                    long stamp = Stopwatch.GetTimestamp();
                    root.Tick(TickSeconds);
                    double milliseconds = (Stopwatch.GetTimestamp() - stamp) * 1000d / Stopwatch.Frequency;
                    queries = victim.JournalPhysicsQueries - queries;
                    maximumQueries = Math.Max(maximumQueries, queries);
                    if (rising || victim.State.Phase == MeleePhase.Rising) { cpu.Add(milliseconds); riseFrames++; }
                    if (victim.State.Phase == MeleePhase.Rising)
                    {
                        float progress = victim.JournalRiseProgress;
                        stalledRiseSeconds = Mathf.Abs(progress - lastRiseProgress) < .00001f
                            ? stalledRiseSeconds + TickSeconds : 0f;
                        lastRiseProgress = progress;
                        Assert.That(stalledRiseSeconds, Is.LessThan(2f), subject +
                            $": the living rise stopped at {progress:F5}; shape={victim.WeaponBlockingShape}");
                        long choices = constraint.CandidateChecks;
                        victim.Present();
                        Assert.That(constraint.CandidateChecks - choices,
                            Is.InRange(0L, (long)CombatWeaponConstraint.MaximumCandidateChecksPerApply),
                            subject + ": each recovery presentation keeps its shoulder work bounded");
                        Assert.That(RecoveryWeaponFloorGap(victim, floor.point.y), Is.GreaterThanOrEqualTo(-.001f),
                            subject + ": the constrained held weapon cannot pass through the floor to advance a rise");
                        anatomy.SampleAndAssert(frame, progress, subject, floor.point.y, anatomyCsv);
                        if (!capturedBrace && progress >= .32f)
                        {
                            CaptureDuelFrame("rise", captureSubject + "brace");
                            CaptureRecoveryAnatomyFrame(victim, captureSubject + "brace-body");
                            capturedBrace = true;
                        }
                    }
                    else { stalledRiseSeconds = 0f; lastRiseProgress = -1f; }
                    lyingSeconds = victim.State.Phase == MeleePhase.KnockedDown ? lyingSeconds + TickSeconds : 0f;
                    maximumLyingSeconds = Mathf.Max(maximumLyingSeconds, lyingSeconds);
                    if (frame % 10 == 0 || rising)
                        csv.AppendFormat(CultureInfo.InvariantCulture, "{0},{1},{2:F4},{3:F4},{4},{5},{6:F4},{7:F4},{8}\n",
                            frame, victim.State.Phase, victim.JournalRiseProgress, victim.Ragdoll.CentralBodySpeed,
                            victim.Ragdoll.HasGroundContact, victim.Ragdoll.HasSupportContact, victim.Ragdoll.QuietSeconds, milliseconds, queries);
                    yield return null;
                    for (int i = 0; i < counters.Length; i++)
                    {
                        if (!counters[i].Valid || counters[i].Count == 0) continue;
                        var sample = counters[i].GetSample(counters[i].Count - 1);
                        double ms = sample.Value * .000001d;
                        totals[i] += ms; calls[i] += sample.Count; maxima[i] = Math.Max(maxima[i], ms);
                    }
                    recovered = hits == 2 && !victim.IsKnockedDown && victim.HasTwoHandSupport;
                }
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "rise.csv"), csv.ToString());
                var report = new StringBuilder();
                cpu.Sort();
                report.AppendLine($"Recovered={recovered}; interruptions={hits}; max lying={maximumLyingSeconds:F3}s; rising frames={riseFrames}; max queries={maximumQueries}");
                if (cpu.Count > 0) report.AppendLine($"Rise Tick CPU p50={cpu[cpu.Count / 2]:F4}ms; p95={cpu[(int)((cpu.Count - 1) * .95)]:F4}ms; max={cpu[cpu.Count - 1]:F4}ms");
                for (int i = 0; i < names.Length; i++) report.AppendLine($"{names[i]}: total={totals[i]:F4}ms; calls={calls[i]}; max/frame={maxima[i]:F4}ms");
                File.WriteAllText(Path.Combine(folder, "cpu.txt"), report.ToString());
                TestContext.Out.WriteLine(report.ToString());
                TestContext.Out.WriteLine(folder);
                Assert.That(hits, Is.EqualTo(2));
                Assert.That(recovered, Is.True, subject + ": the live interrupted rise must return to supported control. " + folder);
                Assert.That(victim.State.Health, Is.EqualTo(victim.State.Settings.MaxHealth), "Diagnostic impulses do not transact HP.");
                CaptureDuelFrame("rise", captureSubject + "recovered");
                victim.State.ReceiveHit(1000f, 0f, false);
                Assert.That(root.RoundFinished, Is.True);
                root.TickFrame(.25f);
            }
            finally
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "anatomy.csv"), anatomyCsv.ToString());
                for (int i = 0; i < counters.Length; i++) counters[i].Dispose();
                root.SetDuelLogging(false);
            }
            for (int wait = 0; wait < 90 && !journal.Completion.IsCompleted; wait++) yield return null;
            Assert.That(journal.Completion.IsCompleted, Is.True);
            Assert.That(journal.DroppedRecords, Is.Zero);
            bool postRoundDiscard = false, physicsSnapshot = false;
            foreach (string path in Directory.GetFiles(folder, "duel.ndjson", SearchOption.AllDirectories))
                foreach (string line in File.ReadLines(path))
                {
                    var record = JsonUtility.FromJson<DuelReadRecord>(line);
                    postRoundDiscard |= record.@event == "post_round_time_discarded";
                    physicsSnapshot |= record.@event == "ragdoll_snapshot";
                    if (record.@event == "suspected_stall")
                        Assert.That(record.data.phase, Is.Not.EqualTo("Rising"), "A progressing authored rise is not a stalled catch episode.");
                }
            Assert.That(postRoundDiscard && physicsSnapshot, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        private static void AssertRecoveryReadyPose(CombatActor actor, bool hero)
        {
            string subject = hero ? "hero" : "opponent";
            Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Ready), subject + ": startup stance is Ready.");
            Assert.That(actor.ActiveClipName, Does.Contain("CombatReady"), subject + ": startup samples the published Ready clip.");
            actor.Present();
            Transform weapon = actor.Weapon.transform;
            // CombatTest3D holding measurements are actor-local metres. Allow
            // two centimetres for imported curves, breathing and runtime sole IK.
            float minimum = hero ? .9220618f : .9388906f;
            float maximum = hero ? .9356226f : .9500965f;
            float height = actor.transform.InverseTransformPoint(weapon.position).y;
            Assert.That(height, Is.InRange(minimum - .02f, maximum + .02f),
                subject + ": the Ready grip retains its published height rather than falling to bind pose.");
            Assert.That(Vector3.Dot(weapon.up, actor.transform.forward), Is.GreaterThan(.2f),
                subject + ": the actual Ready shaft points forward.");
            Transform tip = CombatAssetProvider.FindAnchor(actor.Weapon, "StrikeTip");
            Assert.That(tip, Is.Not.Null);
            Assert.That(Vector3.Dot(tip.position - weapon.position, actor.transform.forward), Is.GreaterThan(.2f),
                subject + ": the actual Ready hook is presented in front of the grip.");
        }

        private static void AssertRecoveryAnimationEndpoints(CombatActor actor, bool hero)
        {
            Animator animator = hero
                ? actor.DamageRigRoot.GetComponentInParent<Player3DAssetRegistry>()?.Animator
                : actor.DamageRigRoot.GetComponentInParent<VillageResidentPresentation>()?.Animator;
            Assert.That(animator, Is.Not.Null, "Recovery endpoints use the actor's matching Animator hierarchy.");
            var sampler = new GameObject("Test recovery animation");
            var copies = new List<Transform>();
            var paths = new List<string>();
            try
            {
                // Copy only local transforms. Sampling this tree cannot mutate
                // the live actor's weapon, support or presentation caches.
                void CopyHierarchy(Transform source, Transform parent, string path)
                {
                    Transform copy = parent == null ? sampler.transform : new GameObject(source.name).transform;
                    copy.SetParent(parent, false);
                    copy.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
                    copy.localScale = source.localScale;
                    copies.Add(copy); paths.Add(path);
                    for (int i = 0; i < source.childCount; i++)
                    {
                        Transform child = source.GetChild(i);
                        CopyHierarchy(child, copy, path.Length == 0 ? child.name : path + "/" + child.name);
                    }
                }
                CopyHierarchy(animator.transform, null, string.Empty);
                AnimationClip ready = CombatAssetProvider.LoadClip(CombatAssetProvider.ReadyClip, !hero);
                ready.SampleAnimation(sampler, 0f);
                var positions = new Vector3[copies.Count];
                var rotations = new Quaternion[copies.Count];
                for (int i = 0; i < copies.Count; i++)
                { positions[i] = copies[i].localPosition; rotations[i] = copies[i].localRotation; }
                Transform pelvis = NpcAttentionHeadLayer.FindBone(sampler.transform, "pelvis");
                Assert.That(pelvis, Is.Not.Null);
                int pelvisIndex = copies.IndexOf(pelvis);
                foreach (string name in CombatAssetProvider.RecoveryClipNames)
                {
                    AnimationClip rise = CombatAssetProvider.LoadClip(name, !hero);
                    rise.SampleAnimation(sampler, 0f);
                    Assert.That(Quaternion.Angle(rotations[pelvisIndex], pelvis.localRotation), Is.GreaterThan(15f),
                        name + ": the copied paths must bind the imported fallen pose.");
                    rise.SampleAnimation(sampler, rise.length);
                    for (int i = 0; i < copies.Count; i++)
                    {
                        string context = (hero ? "hero/" : "opponent/") + name + "/" + paths[i];
                        Assert.That(Vector3.Distance(positions[i], copies[i].localPosition), Is.LessThanOrEqualTo(.001f),
                            context + ": the imported rise endpoint joins Ready0 within one millimetre.");
                        Assert.That(Quaternion.Angle(rotations[i], copies[i].localRotation), Is.LessThanOrEqualTo(.1f),
                            context + ": the imported rise endpoint joins Ready0 without an angular snap.");
                    }
                }
            }
            finally { UnityEngine.Object.Destroy(sampler); }
        }

        private void CaptureRecoveryAnatomyFrame(CombatActor actor, string label)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            Transform pelvis = NpcAttentionHeadLayer.FindBone(actor.DamageRigRoot, "pelvis");
            Vector3 focus = pelvis.position + Vector3.up * .22f;
            try
            {
                camera.transform.position = focus + actor.transform.right * 2.3f + Vector3.up * .6f;
                camera.transform.rotation = Quaternion.LookRotation(focus - camera.transform.position);
                string folder = SceneIds.CombatTest + "/rise";
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", folder, label + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, folder, label);
            }
            finally { camera.transform.SetPositionAndRotation(position, rotation); }
        }

        /// <summary>Checks the displayed rig against its skin bind pose, independent of the Ready stance.</summary>
        private sealed class RecoveryAnatomyObservation
        {
            private readonly CombatActor actor;
            private readonly Transform pelvis;
            private readonly Vector3 pelvisRight;
            private readonly Leg[] legs;
            private object observedRecovery;
            private Player3DFootGroundProbe soleProbe;

            public RecoveryAnatomyObservation(CombatActor actor)
            {
                this.actor = actor;
                var rest = new Dictionary<Transform, Matrix4x4>();
                foreach (SkinnedMeshRenderer skin in actor.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin.sharedMesh == null) continue;
                    Transform[] bones = skin.bones;
                    Matrix4x4[] bindings = skin.sharedMesh.bindposes;
                    for (int i = 0; i < bones.Length && i < bindings.Length; i++)
                        if (bones[i] != null && !rest.ContainsKey(bones[i]))
                            rest.Add(bones[i], skin.localToWorldMatrix * bindings[i].inverse);
                }
                Transform Bone(string name) => NpcAttentionHeadLayer.FindBone(actor.DamageRigRoot, name);
                pelvis = Bone("pelvis");
                Assert.That(pelvis, Is.Not.Null);
                Assert.That(rest.ContainsKey(pelvis), Is.True, "The production skin supplies the neutral pelvis frame.");
                Quaternion restPelvis = rest[pelvis].rotation;
                pelvisRight = Quaternion.Inverse(restPelvis) * actor.transform.right;
                legs = new[]
                {
                    new Leg("left", Bone("thigh.L"), Bone("shin.L"), Bone("foot.L"), rest, restPelvis, actor.transform.forward),
                    new Leg("right", Bone("thigh.R"), Bone("shin.R"), Bone("foot.R"), rest, restPelvis, actor.transform.forward)
                };
            }

            public void SampleAndAssert(int frame, float progress, string subject, float floor, StringBuilder csv)
            {
                object recovery = typeof(CombatActor).GetField("knockdownPose", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor);
                if (!ReferenceEquals(observedRecovery, recovery))
                {
                    Assert.That(recovery, Is.Not.Null);
                    observedRecovery = recovery;
                    soleProbe = (Player3DFootGroundProbe)typeof(CombatRecoveryPose).GetField("footProbe",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(recovery);
                    Assert.That(soleProbe, Is.Not.Null);
                }
                Vector3 right = pelvis.TransformDirection(pelvisRight).normalized;
                foreach (Leg leg in legs)
                {
                    Vector3 thigh = leg.Knee.position - leg.Hip.position;
                    Vector3 shin = leg.Ankle.position - leg.Knee.position;
                    Vector3 neutralThigh = pelvis.TransformDirection(leg.NeutralThigh).normalized;
                    float hip = -Vector3.SignedAngle(Vector3.ProjectOnPlane(neutralThigh, right),
                        Vector3.ProjectOnPlane(thigh, right), right);
                    float lateral = (Mathf.Asin(Mathf.Clamp(Vector3.Dot(thigh.normalized, right), -1f, 1f)) -
                        Mathf.Asin(Mathf.Clamp(Vector3.Dot(neutralThigh, right), -1f, 1f))) * Mathf.Rad2Deg;
                    // The generator's hinge measurement: the bend reference travels
                    // in the neutral thigh frame, so a fallen/yawed body keeps its sign.
                    Vector3 axis = (leg.Ankle.position - leg.Hip.position).normalized;
                    Vector3 offset = Vector3.ProjectOnPlane(thigh, axis);
                    Vector3 reference = Vector3.ProjectOnPlane(leg.Hip.TransformDirection(leg.KneeBendReference), axis);
                    float knee = Vector3.Angle(thigh, shin);
                    if (Vector3.Dot(offset, reference) < 0f) knee = -knee;
                    float ankle = Quaternion.Angle(leg.NeutralAnkle,
                        Quaternion.Inverse(leg.Knee.rotation) * leg.Ankle.rotation);
                    bool hasSole = soleProbe.TryGetSoleHeight(leg.Side == "left" ? FootSide.Left : FootSide.Right, out float sole);
                    csv.AppendFormat(CultureInfo.InvariantCulture, "{0},{1:F5},{2},{3:F6},{4:F6},{5:F3},{6:F3},{7:F3},{8:F3},{9:F6}\n",
                        frame, progress, leg.Side, thigh.magnitude, shin.magnitude, hip, lateral, knee, ankle, sole - floor);
                    string context = FormattableString.Invariant($"{subject}/{leg.Side}: frame={frame}, progress={progress:F5}, hip={hip:F2}, lateral={lateral:F2}, knee={knee:F2}, ankle={ankle:F2}");
                    Assert.That(thigh.magnitude, Is.EqualTo(leg.ThighLength).Within(.002f), context + ": the thigh retains its bind length");
                    Assert.That(shin.magnitude, Is.EqualTo(leg.ShinLength).Within(.002f), context + ": the shin retains its bind length");
                    Assert.That(hip, Is.InRange(-35f, 115f), context + ": the thigh stays within its pelvis's flexion and extension limits");
                    Assert.That(Mathf.Abs(lateral), Is.LessThanOrEqualTo(65f), context + ": hip spread remains anatomical");
                    Assert.That(knee, Is.InRange(-8.05f, 130.05f), context + ": the knee bends toward its calibrated front");
                    Assert.That(ankle, Is.LessThanOrEqualTo(75.05f), context + ": the boot cannot reverse or twist around the shin");
                    Assert.That(hasSole, Is.True);
                    Assert.That(sole - floor, Is.GreaterThanOrEqualTo(-.001f), context + ": the final constrained sole stays above the floor");
                }
            }

            private sealed class Leg
            {
                public readonly string Side;
                public readonly Transform Hip, Knee, Ankle;
                public readonly float ThighLength, ShinLength;
                public readonly Vector3 NeutralThigh, KneeBendReference;
                public readonly Quaternion NeutralAnkle;

                public Leg(string side, Transform hip, Transform knee, Transform ankle,
                    Dictionary<Transform, Matrix4x4> rest, Quaternion pelvis, Vector3 forward)
                {
                    Side = side; Hip = hip; Knee = knee; Ankle = ankle;
                    Assert.That(hip != null && knee != null && ankle != null, Is.True, "The production rig has the complete " + side + " leg.");
                    Assert.That(rest.ContainsKey(hip) && rest.ContainsKey(knee) && rest.ContainsKey(ankle), Is.True,
                        "The skin bind poses calibrate the complete " + side + " leg.");
                    Vector3 thigh = (Vector3)rest[knee].GetColumn(3) - (Vector3)rest[hip].GetColumn(3);
                    Vector3 shin = (Vector3)rest[ankle].GetColumn(3) - (Vector3)rest[knee].GetColumn(3);
                    ThighLength = thigh.magnitude; ShinLength = shin.magnitude;
                    NeutralThigh = Quaternion.Inverse(pelvis) * thigh.normalized;
                    KneeBendReference = Quaternion.Inverse(rest[hip].rotation) * forward;
                    NeutralAnkle = Quaternion.Inverse(rest[knee].rotation) * rest[ankle].rotation;
                }
            }
        }

        private static float RecoveryWeaponFloorGap(CombatActor actor, float floorHeight)
        {
            Transform weapon = actor.Weapon.transform;
            float lowest = float.PositiveInfinity;
            foreach (CombatWeaponGeometry.Segment segment in CombatWeaponGeometry.Segments)
            {
                float a = (weapon.position + weapon.rotation * segment.A).y;
                float b = (weapon.position + weapon.rotation * segment.B).y;
                lowest = Mathf.Min(lowest, Mathf.Min(a, b) - segment.Radius);
            }
            return lowest - floorHeight;
        }

        [UnityTest]
        public IEnumerator Range_RiseWaitsForNearbyCharacterAndResumesAfterRealStep()
        {
            PlacePair(4f);
            for (int warm = 0; warm < 12; warm++) { root.Tick(TickSeconds); yield return null; }
            CombatActor victim = root.Opponent;
            string folder = Path.GetFullPath(Path.Combine("TestResults", "rise-nearby-character-" + Guid.NewGuid().ToString("N")));
            root.SetDuelLogging(true, folder);
            var journal = root.JournalForDiagnostics;
            var poseField = typeof(CombatActor).GetField("knockdownPose", BindingFlags.Instance | BindingFlags.NonPublic);
            bool blocked = false, completed = false;
            Vector3 heldRoot = default;
            try
            {
                Assert.That(victim.TryBeginKnockdown(RecoveryTestImpact(victim, Vector3.down, 1),
                    -victim.transform.forward * .8f, victim.transform.right * 1.1f), Is.True);
                for (int frame = 0; frame < 900; frame++)
                {
                    root.Tick(TickSeconds);
                    yield return null;
                    if (victim.State.Phase == MeleePhase.Rising && victim.JournalRiseProgress >= .93f) break;
                }
                Assert.That(victim.IsKnockedDown && victim.State.Phase == MeleePhase.Rising, Is.True,
                    "The real ragdoll first reaches its authored standing/regrip arc.");
                CombatRecoveryPose pose = (CombatRecoveryPose)poseField.GetValue(victim);
                Assert.That(pose, Is.Not.Null);
                heldRoot = victim.transform.position;
                // A real standing hero crowds the capsule from behind, leaving
                // the NPC's forward crowbar arc clear. No fake enlarged blocker,
                // disabled collision or forced recovery clock supplies the gate.
                root.Hero.ResetActor(heldRoot - victim.transform.forward * .25f, victim.transform.forward);
                Physics.SyncTransforms();
                Assert.That(pose.HasStandingClearance(), Is.False);
                Assert.That(pose.ClearanceRefusalCount, Is.EqualTo(1));
                CombatRecoveryPose.ClearanceRefusal refusal = pose.GetClearanceRefusal(0);
                Assert.That(refusal.Reason, Is.EqualTo("capsule_overlap"));
                Assert.That(refusal.Obstacle, Is.SameAs(root.Hero.Body));
                Assert.That(refusal.CapsuleTested, Is.True);
                Assert.That(refusal.CandidateIndex, Is.Zero);
                Assert.That(Vector3.Distance(refusal.Candidate, heldRoot), Is.LessThan(.00001f));
                Assert.That(refusal.CapsuleRadius, Is.EqualTo(Mathf.Max(.08f,
                    victim.Body.radius - victim.Body.skinWidth)).Within(.00001f));
                for (int frame = 0; frame < 30; frame++)
                {
                    root.Tick(TickSeconds);
                    Assert.That(victim.IsKnockedDown, Is.True, "The obstructed standing capsule cannot finish the rise.");
                    Assert.That(Vector3.Distance(heldRoot, victim.transform.position), Is.LessThan(.002f),
                        "Waiting cannot relocate the NPC through the nearby hero.");
                    yield return null;
                }
                blocked = true;
                CaptureDuelFrame("rise-nearby-character", "blocked");
                Vector3 heroStart = root.Hero.transform.position;
                Assert.That(root.Hero.TryStep(Vector2.down), Is.True, "The blocking hero leaves through the actual supported Step.");
                // The NPC may safely finish as soon as the capsule is clear,
                // before the hero has completed the whole one-metre Step.
                for (int frame = 0; frame < 300 && (!completed || root.Hero.State.Phase == MeleePhase.Step); frame++)
                {
                    Vector3 before = victim.transform.position;
                    root.Tick(TickSeconds);
                    Assert.That(Vector3.Distance(before, victim.transform.position), Is.LessThan(.01f),
                        "Resuming the same rise cannot hide a root teleport.");
                    yield return null;
                    completed |= !victim.IsKnockedDown && victim.HasTwoHandSupport;
                }
                Assert.That(root.Hero.State.Phase, Is.Not.EqualTo(MeleePhase.Step));
                Assert.That(root.Hero.StepTravelBlocked, Is.False);
                Assert.That(Vector3.Distance(heroStart, root.Hero.transform.position),
                    Is.EqualTo(root.Hero.State.Settings.StepDistance).Within(.025f),
                    "The blocking capsule completes its actual Step; it was not ignored or disabled.");
                Assert.That(root.Hero.Body.enabled, Is.True);
                Assert.That(completed, Is.True, "Clearing the real obstacle resumes the same supported/regripped rise.");
                Assert.That(victim.State.Health, Is.EqualTo(victim.State.Settings.MaxHealth));
                CaptureDuelFrame("rise-nearby-character", "resumed");
            }
            finally { root.SetDuelLogging(false); }
            for (int wait = 0; wait < 90 && !journal.Completion.IsCompleted; wait++) yield return null;
            Assert.That(journal.Completion.IsCompleted, Is.True);
            Assert.That(journal.DroppedRecords, Is.Zero);
            int clearanceLogs = 0;
            foreach (string file in Directory.GetFiles(folder, "duel.ndjson", SearchOption.AllDirectories))
                foreach (string line in File.ReadLines(file))
                {
                    var record = JsonUtility.FromJson<RiseClearanceReadRecord>(line);
                    if (record.@event != "rise_clearance_blocked" || record.data.stage != "finish") continue;
                    clearanceLogs++;
                    Assert.That(record.data.reason, Is.EqualTo("capsule_overlap"));
                    Assert.That(record.data.category, Is.EqualTo("combat_actor"));
                    Assert.That(record.data.collider_type, Is.EqualTo(nameof(CharacterController)));
                    Assert.That(record.data.collider_id, Is.EqualTo(root.Hero.Body.GetEntityId().GetHashCode()));
                    Assert.That(record.data.collider_path, Does.Contain(root.Hero.name));
                    Assert.That(record.data.candidates_checked, Is.EqualTo(1));
                }
            Assert.That(blocked && completed, Is.True);
            Assert.That(clearanceLogs, Is.InRange(1, 3), "The stable obstruction is logged on change/sparse intervals, never each simulation tick.");
            LogAssert.NoUnexpectedReceived();
        }

        [Serializable]
        private sealed class RiseClearanceReadRecord
        {
            public string @event;
            public RiseClearanceReadData data;
        }

        [Serializable]
        private sealed class RiseClearanceReadData
        {
            public string stage, reason, category, collider_type, collider_path;
            public int collider_id, candidates_checked;
        }


        [UnityTest]
        public IEnumerator Range_QuietRagdollCanRiseFromFootSupport()
        {
            PlacePair(4f);
            for (int warm = 0; warm < 6; warm++) { root.Tick(TickSeconds); yield return null; }
            CombatActor victim = root.Opponent;
            var first = RecoveryTestImpact(victim, Vector3.down, 1);
            Assert.That(victim.TryBeginKnockdown(first, Vector3.zero, Vector3.zero), Is.True);
            var bodies = victim.Ragdoll.Bodies;
            var constraints = new RigidbodyConstraints[bodies.Count];
            var gravity = new bool[bodies.Count];
            GameObject support = null;
            try
            {
                // Isolate the support decision: a quiet anatomical body rests on
                // its real foot colliders while its torso stays above the ground.
                // Frozen constraints remove unrelated joint settling from this contract.
                float sole = float.PositiveInfinity;
                for (int i = 0; i < bodies.Count; i++)
                {
                    Rigidbody body = bodies[i];
                    constraints[i] = body.constraints; gravity[i] = body.useGravity;
                    body.constraints = RigidbodyConstraints.FreezeAll; body.useGravity = false;
                }
                foreach (var shape in victim.Ragdoll.PhysicsController.AnatomicalColliders)
                    if (shape.Value == Player3DAnatomicalPart.LeftFoot || shape.Value == Player3DAnatomicalPart.RightFoot)
                        sole = Mathf.Min(sole, shape.Key.bounds.min.y);
                Assert.That(float.IsFinite(sole), Is.True);
                support = new GameObject("Quiet ragdoll foot support");
                support.transform.position = new Vector3(victim.transform.position.x, sole - .045f, victim.transform.position.z);
                BoxCollider floor = support.AddComponent<BoxCollider>();
                floor.size = new Vector3(2f, .1f, 2f);
                Physics.SyncTransforms();
                for (int frame = 0; frame < 12 && !victim.Ragdoll.HasSupportContact; frame++)
                { root.Tick(TickSeconds); yield return null; }
                Assert.That(victim.Ragdoll.HasSupportContact, Is.True, "The anatomical foot, not its weapon, supplies real support.");
                Assert.That(victim.Ragdoll.HasGroundContact, Is.False, "A foot contact is not the central body's landing thud.");
                floor.enabled = false;
                Assert.That(victim.Ragdoll.HasSupportContact, Is.False, "Disabled support cannot leave a latched contact.");
                floor.enabled = true; Physics.SyncTransforms();
                for (int frame = 0; frame < 60 && victim.State.Phase != MeleePhase.Rising; frame++)
                { root.Tick(TickSeconds); yield return null; }
                Assert.That(victim.State.Phase, Is.EqualTo(MeleePhase.Rising),
                    $"Quiet support should permit gathering without waiting for the torso to hit: quiet={victim.Ragdoll.QuietSeconds:F3}, central={victim.Ragdoll.CentralBodySpeed:F3}");
                Assert.That(victim.Ragdoll.HasGroundContact, Is.False);
            }
            finally
            {
                for (int i = 0; i < bodies.Count; i++)
                { bodies[i].constraints = constraints[i]; bodies[i].useGravity = gravity[i]; }
                if (support != null) UnityEngine.Object.Destroy(support);
            }
            LogAssert.NoUnexpectedReceived();
        }

        private CombatImpact RecoveryTestImpact(CombatActor victim, Vector3 impulse, int sequence)
        {
            CombatActor source = victim == root.Hero ? root.Opponent : root.Hero;
            Transform chest = victim.Ragdoll.PhysicsController.ChestBody.transform;
            Vector3 point = chest.position + victim.transform.right * .1f;
            return new CombatImpact(source, victim, sequence, point, -impulse.normalized, impulse.normalized,
                victim.State.Health, victim.State.Health, MeleeHitResult.Hit,
                new MeleeHitLocation(MeleeBodyRegion.Torso, MeleeHitSide.Front), 1f,
                Player3DAnatomicalPart.Torso, chest.InverseTransformPoint(point), 3f, impulse);
        }
    }
}
