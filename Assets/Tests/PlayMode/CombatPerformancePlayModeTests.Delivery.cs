#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatPerformancePlayModeTests
    {
        // Keep the normal Editor and render loop: a manually ticked/headless
        // simulation cannot attribute the recorded post-render delivery gaps.
        [UnityTest, Explicit("Short rendered CPU Timeline with Editor work and exact duel-frame correlation.")]
        public IEnumerator Range_RenderedDeliverySeparatesEditorWaitFromCombatWork()
        {
            Assert.That(Application.isBatchMode, Is.False, "Use the rendered Editor, without -batchmode/-nographics.");
            string folder = Path.GetFullPath("TestResults/Test combat delivery");
            Directory.CreateDirectory(folder);
            bool wasEnabled = ProfilerDriver.enabled, wasEditor = ProfilerDriver.profileEditor;
            bool wasDeep = ProfilerDriver.deepProfiling;
            bool wasCpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU);
            float capture = Time.captureDeltaTime;
            const string prefix = "Test combat delivery frame ";
            var phases = new Dictionary<int, string>();
            int first = -1, last = -1;
            try
            {
                Time.captureDeltaTime = 0f;
                ProfilerDriver.enabled = false;
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.deepProfiling = false;
                ProfilerDriver.profileEditor = true;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
                ProfilerDriver.enabled = true;
                for (int epoch = 0; epoch < 2; epoch++)
                {
                    root.AutomaticSimulation = false;
                    root.SetDuelLogging(false);
                    PlacePair(1.3f);
                    root.SetSparring(true);
                    if (epoch == 1) root.SetDuelLogging(true, Path.Combine(folder, "Duel"));
                    root.AutomaticSimulation = true;
                    for (int warm = 0; warm < 12; warm++) yield return null;
                    for (int frame = 0; frame < 96; frame++)
                    {
                        if (root.Hero.State.Phase == MeleePhase.Ready && root.Hero.HasAttackBalance)
                            root.Hero.TryAttack();
                        // The sample name is intentionally test-only. Unlike the
                        // asynchronous LastValue counters it identifies this exact
                        // Time.frameCount in both the Timeline and duel.ndjson.
                        int unityFrame = Time.frameCount;
                        phases[unityFrame] = epoch == 0 ? "journal disabled" : "journal enabled";
                        Profiler.BeginSample(prefix + unityFrame.ToString(CultureInfo.InvariantCulture));
                        Profiler.EndSample();
                        yield return null;
                    }
                }
                for (int drain = 0; drain < 4; drain++) yield return null;
                root.AutomaticSimulation = false;
                root.SetDuelLogging(false);
                first = ProfilerDriver.firstFrameIndex;
                last = ProfilerDriver.lastFrameIndex;
                ProfilerDriver.enabled = false;
                Assert.That(ProfilerDriver.SaveProfile(Path.Combine(folder, "CPU Timeline.data")), Is.True);
                var frames = new StringBuilder("profiler_frame,unity_frame,phase,main_thread_ms\n");
                var samples = new StringBuilder("profiler_frame,unity_frame,phase,thread,sample,parent,start_ms,duration_ms,name\n");
                int correlated = 0;
                for (int frame = first; frame <= last; frame++)
                {
                    int unityFrame = -1;
                    using (var main = ProfilerDriver.GetRawFrameDataView(frame, 0))
                    {
                        if (!main.valid) continue;
                        for (int sample = 0; sample < main.sampleCount; sample++)
                        {
                            string name = main.GetSampleName(sample);
                            if (name.StartsWith(prefix, StringComparison.Ordinal) &&
                                int.TryParse(name.Substring(prefix.Length), out int parsed)) unityFrame = parsed;
                        }
                        if (!phases.TryGetValue(unityFrame, out string phase)) continue;
                        correlated++;
                        frames.AppendLine(FormattableString.Invariant($"{frame},{unityFrame},{phase},{main.frameTimeMs:F5}"));
                    }
                    for (int thread = 0; thread < 32; thread++)
                    {
                        using var data = ProfilerDriver.GetRawFrameDataView(frame, thread);
                        if (!data.valid) break;
                        if (data.threadName != "Main Thread" && data.threadName != "Render Thread") continue;
                        var parents = new Stack<(int sample, int end)>();
                        for (int sample = 0; sample < data.sampleCount; sample++)
                        {
                            while (parents.Count > 0 && sample > parents.Peek().end) parents.Pop();
                            int parent = parents.Count > 0 ? parents.Peek().sample : -1;
                            double duration = data.GetSampleTimeMs(sample);
                            if (duration >= .05d)
                            {
                                string name = data.GetSampleName(sample).Replace("\"", "\"\"");
                                samples.AppendLine(FormattableString.Invariant(
                                    $"{frame},{unityFrame},{phases[unityFrame]},{data.threadName},{sample},{parent},{data.GetSampleStartTimeMs(sample):F5},{duration:F5},\"{name}\""));
                            }
                            int children = data.GetSampleChildrenCountRecursive(sample);
                            if (children > 0) parents.Push((sample, sample + children));
                        }
                    }
                }
                File.WriteAllText(Path.Combine(folder, "Frames.csv"), frames.ToString());
                File.WriteAllText(Path.Combine(folder, "Timeline.csv"), samples.ToString());
                Assert.That(correlated, Is.GreaterThanOrEqualTo(180), "Keep both measured epochs in the bounded Profiler history.");
                TestContext.Out.WriteLine($"Rendered delivery Timeline: {folder}; exact correlated frames, journal disabled/enabled; target={Application.targetFrameRate}, vSync={QualitySettings.vSyncCount}.");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                root.AutomaticSimulation = false;
                root.SetDuelLogging(false);
                Time.captureDeltaTime = capture;
                ProfilerDriver.enabled = false;
                ProfilerDriver.deepProfiling = wasDeep;
                ProfilerDriver.profileEditor = wasEditor;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, wasCpu);
                ProfilerDriver.enabled = wasEnabled;
            }
        }
    }
}
#endif
