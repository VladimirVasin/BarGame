using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatPerformancePlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_InterruptedRiseHasBoundedWorkAndRecovers()
        {
            PlacePair(4f);
            for (int warm = 0; warm < 12; warm++) { root.Tick(TickSeconds); yield return null; }
            CombatActor victim = root.Opponent;
            string folder = Path.GetFullPath("TestResults/recovery-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
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
            bool recovered = false;
            float lyingSeconds = 0f, maximumLyingSeconds = 0f;
            try
            {
                for (int i = 0; i < counters.Length; i++)
                    counters[i] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "BarPromenade.CombatRecovery." + names[i], 1,
                        ProfilerRecorderOptions.Default | ProfilerRecorderOptions.CollectOnlyOnCurrentThread |
                        ProfilerRecorderOptions.SumAllSamplesInFrame);
                var first = RecoveryTestImpact(victim, -victim.transform.forward * 205f, 1);
                Assert.That(victim.TryBeginKnockdown(first, -victim.transform.forward * .8f, victim.transform.right * 1.1f), Is.True);
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
                Assert.That(recovered, Is.True, "The live interrupted rise must return to supported control. " + folder);
                Assert.That(victim.State.Health, Is.EqualTo(victim.State.Settings.MaxHealth), "Diagnostic impulses do not transact HP.");
                CaptureDuelFrame("rise", "recovered");
                victim.State.ReceiveHit(1000f, 0f, false);
                Assert.That(root.RoundFinished, Is.True);
                root.TickFrame(.25f);
            }
            finally
            {
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
