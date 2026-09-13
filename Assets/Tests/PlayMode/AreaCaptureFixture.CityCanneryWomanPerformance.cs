using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("CPU/GC benchmark of the actual cannery woman at nearby idle and work poses; excludes rendering, capture and contact-test oracles.")]
        [PrebuildSetup(typeof(CanneryWomanAssetsSetup))]
        public IEnumerator CityCanneryWomanPerformance() => CaptureFocusedPort(MeasureCanneryWomanPerformance);

        [Serializable] private sealed class WomanPerformanceReport
        {
            public string unity, processor;
            public WomanPerformanceRow[] rows;
        }

        [Serializable] private sealed class WomanPerformanceRow
        {
            public string phase;
            public double meanMilliseconds, medianMilliseconds, p95Milliseconds, bytesPerCall;
        }

        private static IEnumerator MeasureCanneryWomanPerformance(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            var cannery = city.Cannery;
            var actor = cannery.GetFactoryWorker(CanneryWomanPresentation.WorkerSlot);
            var hair = actor.GetComponent<CanneryWomanPresentation>().Hair;
            var rows = new List<WomanPerformanceRow>();
            bool oldManual = cannery.FactoryConversation.UseManualClock;
            var follow = camera.GetComponent<PlayerCameraFollow>();
            bool oldFollow = follow != null && follow.enabled;
            try
            {
                cannery.AutoAdvance = false;
                cannery.FactoryConversation.UseManualClock = true;
                if (follow != null) follow.enabled = false;
                CityFishSupplySession.ResetForNewGame();
                cannery.ApplyAt(0d); cannery.ApplyLifeAt(10d);
                Vector3 from = actor.transform.position + actor.transform.forward * 2.4f + Vector3.up * 1.5f;
                city.Player.Motor.Teleport(from - Vector3.up * EyeHeight);
                camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(actor.Head.position - from));
                yield return null;
                foreach (bool work in new[] { false, true })
                {
                    cannery.ApplyAt(work ? CanneryTime(cannery, CityCanneryProductionStage.Fill, .45f) : 0d);
                    cannery.ApplyLifeAt(cannery.LifeSeconds + 2d);
                    string phase = work ? "work" : "idle";
                    Quaternion headRest = actor.Head.rotation;
                    double time = cannery.LifeSeconds + 2d;
                    int sample = 0;
                    Action step = () =>
                    {
                        actor.Head.rotation = Quaternion.AngleAxis((work ? 4f : 12f) * Mathf.Sin(sample++ * .055f),
                            actor.transform.up) * headRest;
                        hair.ApplyAt(time += 1d / 60d, false, !work, default);
                    };
                    rows.Add(MeasureWomanCpu(phase + "/hair", step, 240));
                    // Bind reflection once, outside measured loops. These fixed-pose
                    // stages explain the total; they are not added to it.
                    foreach (string method in new[] { "UpdateBody", "SolveSurfaceContacts", "MeasureSurfaceContacts", "RefreshSurfaceMatrices" })
                    {
                        var info = typeof(CanneryWomanHair).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
                        Assert.That(info, Is.Not.Null);
                        var stage = (Action)Delegate.CreateDelegate(typeof(Action), hair, info);
                        rows.Add(MeasureWomanCpu(phase + "/" + method, stage, 48));
                    }
                    foreach (string method in new[] { "AccumulateSampleBodyContacts", "AccumulateSampleStrandContacts", "AccumulateContinuousBodyContacts" })
                    {
                        var info = typeof(CanneryWomanHair).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
                        Assert.That(info, Is.Not.Null);
                        var stage = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), hair, info);
                        rows.Add(MeasureWomanCpu(phase + "/" + method, () => { stage(); }, 48));
                    }
                    actor.Head.rotation = headRest;
                    cannery.ApplyLifeAt(cannery.LifeSeconds + 2d);
                    yield return null;
                }
                float seconds = 0f;
                rows.Add(MeasureWomanCpu("idle/animation", () => actor.ApplyIdleVariation(seconds += 1f / 60f, true, .3f), 240));
                var report = new WomanPerformanceReport
                {
                    unity = Application.unityVersion, processor = SystemInfo.processorType, rows = rows.ToArray()
                };
                string directory = Path.Combine(Directory.GetCurrentDirectory(), "TestResults");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "cannery-woman-performance.json"), JsonUtility.ToJson(report, true));
                foreach (WomanPerformanceRow row in rows)
                    Debug.Log(FormattableString.Invariant($"CANNERY WOMAN CPU {row.phase}: mean {row.meanMilliseconds:F3} ms, p95 {row.p95Milliseconds:F3} ms, {row.bytesPerCall:F1} B/call."));
            }
            finally
            {
                cannery.FactoryConversation.UseManualClock = oldManual;
                if (follow != null) follow.enabled = oldFollow;
            }
        }

        private static WomanPerformanceRow MeasureWomanCpu(string phase, Action action, int samples)
        {
            for (int i = 0; i < 32; i++) action();
            var timings = new double[samples];
            double sum = 0d;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < samples; i++)
            {
                long start = Stopwatch.GetTimestamp();
                action();
                double milliseconds = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                timings[i] = milliseconds; sum += milliseconds;
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Array.Sort(timings);
            return new WomanPerformanceRow
            {
                phase = phase, meanMilliseconds = sum / samples, medianMilliseconds = timings[samples / 2],
                p95Milliseconds = timings[(int)(samples * .95f)], bytesPerCall = (double)allocated / samples
            };
        }
    }
}
