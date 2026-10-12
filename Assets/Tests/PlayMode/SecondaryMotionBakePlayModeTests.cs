using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class SecondaryMotionBakeAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            bool focusedProof = Array.Exists(Environment.GetCommandLineArgs(), value => value == "-secondaryMotionBakeProof");
            Type.GetType("BarPromenade.Editor.SecondaryMotionBakeAssetSetup, BarPromenade.Editor", true)
                .GetMethod(focusedProof ? "BakeProofAnimationSet" : "EnsureCurrent", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
#endif
        }
    }

    [PrebuildSetup(typeof(SecondaryMotionBakeAssetsSetup))]
    public sealed partial class SecondaryMotionBakePlayModeTests
    {
        private static readonly WindSample StillAir = new WindSample(0f, 0f);

        [UnityTest]
        public IEnumerator RecordedPhysicalMotionReplaysWithSafeFallbackAndRetainsPerformanceEvidence()
        {
            var owner = new GameObject("Test secondary motion hero");
            var report = new StringBuilder("Test secondary motion baking\nActual solver reference and imported production Idle, Walk and Run clips. CPU timings include secondary-motion ApplyAt only; animation sampling, probes and rendering are excluded. No absolute FPS or machine-specific duration gate.\n");
            string evidence = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults", "Test secondary motion baking"));
            string pictures = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", "secondary-motion-bake"));
            bool previousEnabled = SecondaryMotionBakeRuntime.Enabled;
            Player3DAssetRegistry rig = null;
            CaptureCamera camera = null;
            Directory.CreateDirectory(evidence);
            Directory.CreateDirectory(pictures);
            try
            {
                SecondaryMotionBakeRuntime.Enabled = true;
                SecondaryMotionBakeRuntime.ReloadAssets();
                VerifyRecordingCodec(report);
                rig = Player3DResources.Instantiate(owner.transform);
                foreach (MonoBehaviour driver in rig.GetComponentsInChildren<MonoBehaviour>(true)) driver.enabled = false;
                rig.Animator.enabled = false;
                PlayerHair hair = rig.GetComponent<PlayerHair>();
                PlayerJacketCloth jacket = rig.GetComponent<PlayerJacketCloth>();
                CharacterJointDeformation joints = rig.GetComponent<CharacterJointDeformation>();
                Assert.That(hair.HasAuthoredBindings && jacket.HasAuthoredBindings && joints.HasBindings, Is.True);
                Assert.That(hair.JointCount, Is.EqualTo(12));
                AssertProvenance(report);
                camera = new CaptureCamera(rig);
                var physicalCpu = new List<double>();
                var playbackCpu = new List<double>();
                float maximumHairMotion = 0f, maximumJacketMotion = 0f;
                double clock = 0d;

                foreach (string name in new[] { "Idle", "Walk", "Run" })
                {
                    AnimationClip clip = rig.Animations.Single(binding => binding.ClipName == name).Clip;
                    TextAsset asset = Resources.Load<TextAsset>(SecondaryMotionBakeRuntime.ResourceRoot + "no-gear/" + clip.name);
                    Assert.That(asset, Is.Not.Null, "Production physical recording missing for " + name);
                    var bank = (SecondaryMotionBakeRuntime.ClipData)typeof(SecondaryMotionBakeRuntime)
                        .GetMethod("Decode", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { asset.bytes });
                    Assert.That(bank, Is.Not.Null);
                    var hairTrack = bank.Tracks.Single(track => track.Part == SecondaryMotionBakePart.Hair);
                    var jacketTrack = bank.Tracks.Single(track => track.Part == SecondaryMotionBakePart.Jacket);
                    Assert.That(hairTrack.Signature, Is.EqualTo(hair.BakedPoseSignature));
                    Assert.That(jacketTrack.Signature, Is.EqualTo(jacket.BakedPoseSignature));
                    int intervals = hairTrack.Values.Length - 1;
                    Assert.That(intervals, Is.GreaterThanOrEqualTo(12));
                    Assert.That(jacketTrack.Values.Length, Is.EqualTo(intervals + 1));
                    int[] steps = Enumerable.Range(0, 12).SelectMany(index =>
                        new[] { Mathf.FloorToInt(index * intervals / 12f) * 4, Mathf.FloorToInt(index * intervals / 12f) * 4 + 2 }).ToArray();
                    Dictionary<int, PhysicalPose> reference = RecordReference(rig, clip, intervals, bank.WarmupSteps, steps, hair, jacket, joints, physicalCpu);
                    hair.RequestReset(); jacket.RequestReset();
                    float hairError = 0f, jacketError = 0f;
                    Vector3[] firstJacket = null;
                    Quaternion[] firstHair = null;
                    foreach (int step in steps)
                    {
                        float clipSeconds = clip.length * step / (intervals * 4f);
                        PreparePose(rig, clip, clipSeconds, joints);
                        long hairSteps = hair.TotalSimulationSteps, jacketSteps = jacket.TotalSimulationSteps;
                        long hairBakes = hair.TotalBakedPoseCalls, jacketBakes = jacket.TotalBakedPoseCalls;
                        clock += 1d / 60d;
                        jacket.ApplyAt(clock, false, false, StillAir);
                        hair.ApplyAt(clock, false, false, StillAir);
                        playbackCpu.Add(TicksToMilliseconds(jacket.LastApplyTicks + hair.LastApplyTicks));
                        Assert.That(hair.LastPoseWasBaked && jacket.LastPoseWasBaked, Is.True, name + " sample " + step + " must use the real recording. " +
                            (hair.LastPoseWasBaked ? "" : SecondaryMotionBakeRuntime.DescribeContext(rig, SecondaryMotionBakePart.Hair)) + " " +
                            (jacket.LastPoseWasBaked ? "" : SecondaryMotionBakeRuntime.DescribeContext(rig, SecondaryMotionBakePart.Jacket)));
                        Assert.That(hair.TotalBakedPoseCalls, Is.EqualTo(hairBakes + 1));
                        Assert.That(jacket.TotalBakedPoseCalls, Is.EqualTo(jacketBakes + 1));
                        Assert.That(hair.TotalSimulationSteps, Is.EqualTo(hairSteps));
                        Assert.That(jacket.TotalSimulationSteps, Is.EqualTo(jacketSteps));
                        Assert.That(hair.LastSimulationSteps + jacket.LastSimulationSteps, Is.Zero);
                        Quaternion[] rotations = HairRotations(hair);
                        Vector3[] surface = JacketSamples(jacket);
                        float angularError = MaximumAngle(rotations, reference[step].Hair);
                        float surfaceError = MaximumDistance(surface, reference[step].Jacket);
                        hairError = Mathf.Max(hairError, angularError); jacketError = Mathf.Max(jacketError, surfaceError);
                        if ((step & 3) == 0)
                        {
                            Assert.That(angularError, Is.LessThan(.2f), name + " key must retain independently simulated hair.");
                            Assert.That(surfaceError, Is.LessThan(.0002f), name + " key must retain independently simulated jacket geometry.");
                        }
                        if (firstHair == null) { firstHair = rotations; firstJacket = surface; }
                        maximumHairMotion = Mathf.Max(maximumHairMotion, MaximumAngle(rotations, firstHair));
                        maximumJacketMotion = Mathf.Max(maximumJacketMotion, MaximumDistance(surface, firstJacket));
                        Assert.That(hair.MaximumLengthError, Is.LessThan(.001f), "Playback must preserve hair strand lengths.");
                        if (step == steps[13])
                        {
                            Player3DJointSurfaceTests.AssertJacketHemClear(rig, "Test baked " + name + " between recorded frames at " + clipSeconds);
                            camera.Save(pictures, name, false);
                            camera.Save(pictures, name, true);
                        }
                        yield return null;
                    }
                    if (clip.isLooping)
                    {
                        PreparePose(rig, clip, clip.length - .00001f, joints);
                        jacket.ApplyAt(clock += 1d / 60d, false, false, StillAir); hair.ApplyAt(clock, false, false, StillAir);
                        Vector3[] endJacket = JacketSamples(jacket);
                        Quaternion[] endHair = HairRotations(hair);
                        PreparePose(rig, clip, 0f, joints);
                        jacket.ApplyAt(clock += 1d / 60d, false, false, StillAir); hair.ApplyAt(clock, false, false, StillAir);
                        Assert.That(hair.LastPoseWasBaked && jacket.LastPoseWasBaked, Is.True);
                        Assert.That(MaximumDistance(JacketSamples(jacket), endJacket), Is.LessThan(.002f), name + " must cross its loop seam without a garment snap.");
                        Assert.That(MaximumAngle(HairRotations(hair), endHair), Is.LessThan(.15f), name + " must cross its loop seam without a hair snap.");
                    }
                    using var digest = SHA256.Create();
                    report.AppendLine(FormattableString.Invariant($"{name}: resource SHA256={BitConverter.ToString(digest.ComputeHash(asset.bytes)).Replace("-", "")}; bytes={asset.bytes.Length}; frames={intervals + 1}; duration={clip.length:F6}s; hair reference error={hairError:F6} degrees; jacket reference error={jacketError:F6}m; twelve keys plus twelve intermediate rendered samples."));
                }
                Assert.That(maximumHairMotion, Is.GreaterThan(.01f), "Recorded hair must contain actual physically solved motion.");
                Assert.That(maximumJacketMotion, Is.GreaterThan(.000001f), "Recorded garment must contain actual changing geometry.");
                VerifyFallbackAndOwnership(rig, hair, jacket, joints, ref clock, report);
                VerifySlingBakedPlayback(rig, camera, pictures);
                report.AppendLine("Root translation, pause, changed head/arm/wind/equipment, return to recorded playback and isolated sling playback verified.");
                AppendCpu(report, "Physical reference", physicalCpu);
                AppendCpu(report, "Recorded playback", playbackCpu);
                report.AppendLine(FormattableString.Invariant($"Recorded motion maximum hair rotation={maximumHairMotion:F6} degrees; sampled garment displacement={maximumJacketMotion:F6}m."));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                try { File.WriteAllText(Path.Combine(evidence, "Report.txt"), report.ToString()); }
                finally
                {
                    camera?.Dispose();
                    if (rig != null) SecondaryMotionBakeRuntime.Release(rig);
                    SecondaryMotionBakeRuntime.Enabled = previousEnabled;
                    Object.DestroyImmediate(owner);
                }
            }
        }

        private static Dictionary<int, PhysicalPose> RecordReference(Player3DAssetRegistry rig, AnimationClip clip, int intervals, int preSteps,
            int[] requested, PlayerHair hair, PlayerJacketCloth jacket, CharacterJointDeformation joints, List<double> cpu)
        {
            var result = new Dictionary<int, PhysicalPose>();
            var selected = new HashSet<int>(requested);
            float dt = clip.length / (intervals * 4f);
            Assert.That(preSteps, Is.GreaterThan(0), "The recording must retain its actual physical warm-up.");
            float speed = clip.name == "Walk" ? 2.6f : clip.name == "Run" ? 4.2f : 0f;
            double seconds = 0d;
            using (hair.BeginBakeCapture())
            using (jacket.BeginBakeCapture())
                for (int step = -preSteps; step <= requested.Max(); step++, seconds += dt)
                {
                    float phase = step / (intervals * 4f);
                    float sourceTime = (clip.isLooping ? Mathf.Repeat(phase, 1f) : Mathf.Clamp01(phase)) * clip.length;
                    rig.transform.position = rig.transform.TransformDirection(rig.Metrics.LocalForward) * speed * (float)seconds;
                    PreparePose(rig, clip, sourceTime, joints);
                    jacket.ApplyAt(seconds, false, false, StillAir);
                    hair.ApplyAt(seconds, false, false, StillAir);
                    Assert.That(hair.LastPoseWasBaked || jacket.LastPoseWasBaked, Is.False, "Authoring reference must run the physical solvers.");
                    if (step >= 0) cpu.Add(TicksToMilliseconds(jacket.LastApplyTicks + hair.LastApplyTicks));
                    if (selected.Contains(step)) result.Add(step, new PhysicalPose { Hair = HairRotations(hair), Jacket = JacketSamples(jacket) });
                }
            rig.transform.position = Vector3.zero;
            return result;
        }

        private static void VerifyFallbackAndOwnership(Player3DAssetRegistry rig, PlayerHair hair, PlayerJacketCloth jacket,
            CharacterJointDeformation joints, ref double clock, StringBuilder report)
        {
            AnimationClip clip = rig.Animations.Single(binding => binding.ClipName == "Idle").Clip;
            PreparePose(rig, clip, 0f, joints);
            hair.RequestReset(); jacket.RequestReset();
            jacket.ApplyAt(clock += 1d / 60d, false, false, StillAir); hair.ApplyAt(clock, false, false, StillAir);
            Quaternion[] originalHair = HairRotations(hair);
            Vector3[] originalJacket = JacketSamples(jacket);
            Vector3 beforeRoot = rig.transform.position;
            rig.transform.position += new Vector3(3.25f, .125f, -2.5f);
            PreparePose(rig, clip, 0f, joints);
            jacket.ApplyAt(clock += 1d / 60d, false, false, StillAir); hair.ApplyAt(clock, false, false, StillAir);
            Assert.That(hair.LastPoseWasBaked && jacket.LastPoseWasBaked, Is.True);
            Assert.That(MaximumAngle(HairRotations(hair), originalHair), Is.LessThan(.001f));
            Assert.That(MaximumDistance(JacketSamples(jacket), originalJacket), Is.LessThan(.000001f));
            PreparePose(rig, clip, clip.length * .25f, joints);
            jacket.ApplyAt(clock += 1d / 60d, true, false, StillAir); hair.ApplyAt(clock, true, false, StillAir);
            Assert.That(MaximumAngle(HairRotations(hair), originalHair), Is.LessThan(.001f), "Pause retains the displayed hair rather than resampling.");
            Assert.That(MaximumDistance(JacketSamples(jacket), originalJacket), Is.LessThan(.000001f), "Pause retains garment geometry.");
            rig.transform.position = beforeRoot;

            PreparePose(rig, clip, 0f, joints);
            jacket.ApplyAt(clock += 1d / 60d, false, false, StillAir); hair.ApplyAt(clock, false, false, StillAir);
            Vector3[] groundedJacket = JacketWorldPoints(rig, jacket);
            Vector3[] groundedHair = HairWorldPoints(hair);
            rig.Anchors.Pelvis.position += Vector3.up * .006f;
            joints.ApplyPose();
            jacket.ApplyAt(clock += 1d / 60d, false, false, StillAir); hair.ApplyAt(clock, false, false, StillAir);
            Assert.That(hair.LastPoseWasBaked && jacket.LastPoseWasBaked, Is.True,
                "A certified whole-rig Idle ground adjustment must retain recorded playback.");
            Assert.That(MaximumDistance(JacketWorldPoints(rig, jacket), groundedJacket.Select(point => point + Vector3.up * .006f).ToArray()),
                Is.LessThan(.0002f), "Recorded cloth must follow the current skin after grounding.");
            Assert.That(MaximumDistance(HairWorldPoints(hair), groundedHair.Select(point => point + Vector3.up * .006f).ToArray()), Is.LessThan(.0002f));
            rig.Anchors.Head.position += Vector3.up * .004f;
            Assert.That(SecondaryMotionBakeRuntime.TrySample(rig, SecondaryMotionBakePart.Hair,
                hair.BakedPoseSignature, hair.BakedPoseValueCount, false, StillAir, out _), Is.False,
                "An isolated head translation must not inherit the whole-rig ground adjustment.");
            report.AppendLine("Certified six-millimetre whole-rig Idle ground adjustment preserves rendered cloth/hair; an isolated head shift retains physical fallback.");

            PreparePose(rig, clip, 0f, joints);
            int toeShapes = 0;
            foreach (Player3DMeshBinding mesh in rig.MeshBindings)
                if (mesh.Renderer is SkinnedMeshRenderer skin && mesh.MeshName.StartsWith("CLO_Boot", StringComparison.Ordinal))
                {
                    int shape = CharacterJointDeformation.FindShape(skin.sharedMesh, "BootToeRoll15.L");
                    if (shape < 0) continue;
                    skin.SetBlendShapeWeight(shape, 6.5f); toeShapes++;
                }
            Assert.That(toeShapes, Is.GreaterThan(0), "The proof must change real authored footwear, as grounded Idle does.");
            jacket.ApplyAt(clock += 1d / 60d, false, false, StillAir);
            Assert.That(jacket.LastPoseWasBaked, Is.True, "A distant grounded toe correction must not restart torso fabric physics. " +
                SecondaryMotionBakeRuntime.DescribeContext(rig, SecondaryMotionBakePart.Jacket));
            Player3DJointSurfaceTests.AssertJacketHemClear(rig, "Test Idle toe correction with recorded jacket");
            Transform foot = CityPedestrianHandProps.FindSocket(rig.Animator.transform, "foot.L");
            foot.position = rig.Anchors.Chest.position;
            Assert.That(SecondaryMotionBakeRuntime.TrySample(rig, SecondaryMotionBakePart.Jacket,
                jacket.BakedPoseSignature, jacket.BakedPoseValueCount, false, StillAir, out _), Is.False,
                "A foot raised into the garment cannot inherit the distant-toe exception.");
            report.AppendLine("Grounded Idle toe correction retains recorded jacket; a real foot moved into the garment retains the normal pose guard.");

            PreparePose(rig, clip, 0f, joints);
            rig.Anchors.Head.localRotation *= Quaternion.Euler(30f, 0f, 0f);
            jacket.ApplyAt(clock += 1d / 60d, false, false, StillAir); hair.ApplyAt(clock, false, false, StillAir);
            Assert.That(hair.LastPoseWasBaked || jacket.LastPoseWasBaked, Is.False, "An unrecorded head pose must use real clearance.");
            Assert.That(hair.MaximumLengthError, Is.LessThan(.001f));
            PreparePose(rig, clip, 0f, joints);
            Player3DJointSurfaceTests.SetBend(rig, "forearm.R", 90f); joints.ApplyPose();
            jacket.ApplyAt(clock += 1d / 60d, false, false, StillAir);
            Assert.That(jacket.LastPoseWasBaked, Is.False, "An unrecorded procedural arm must retain live fabric.");
            PreparePose(rig, clip, 0f, joints);
            jacket.ApplyAt(clock += 1d / 60d, false, true, new WindSample(4f, 1f));
            hair.ApplyAt(clock, false, true, new WindSample(4f, 1f));
            Assert.That(hair.LastPoseWasBaked || jacket.LastPoseWasBaked, Is.False, "Wind outside the recorded exposure must fall back.");

            PlayerEquipmentContacts equipment = rig.GetComponent<PlayerEquipmentContacts>();
            bool createdEquipment = equipment == null;
            if (createdEquipment) equipment = rig.gameObject.AddComponent<PlayerEquipmentContacts>();
            var gear = new GameObject("Test carried equipment");
            gear.transform.SetParent(rig.transform, false); gear.transform.localPosition = Vector3.right * 3f;
            gear.AddComponent<MeshFilter>().sharedMesh = jacket.SourceMesh(0);
            try
            {
                equipment.Register(gear, gear);
                PreparePose(rig, clip, 0f, joints);
                jacket.ApplyAt(clock += 1d / 60d, false, false, StillAir); hair.ApplyAt(clock, false, false, StillAir);
                Assert.That(hair.LastPoseWasBaked || jacket.LastPoseWasBaked, Is.False, "Changed real equipment boxes invalidate no-gear recordings.");
            }
            finally { equipment.Unregister(gear); Object.DestroyImmediate(gear); if (createdEquipment) Object.DestroyImmediate(equipment); }
            Vector3[] previousJacket = JacketWorldPoints(rig, jacket);
            Vector3[] previousHair = HairWorldPoints(hair);
            float maximumJacketStep = 0f, maximumHairStep = 0f;
            long transitions = jacket.TotalBakedTransitionCalls;
            // A continuous return retains live clearance for the .12 s blend.
            // The source pose remains fixed, so these are visible secondary-
            // motion changes rather than ordinary animated limb movement.
            for (int frame = 0; frame < 12; frame++)
            {
                PreparePose(rig, clip, 0f, joints);
                jacket.ApplyAt(clock += 1d / 60d, false, false, StillAir); hair.ApplyAt(clock, false, false, StillAir);
                Vector3[] currentJacket = JacketWorldPoints(rig, jacket), currentHair = HairWorldPoints(hair);
                float jacketStep = MaximumDistance(currentJacket, previousJacket), hairStep = MaximumDistance(currentHair, previousHair);
                maximumJacketStep = Mathf.Max(maximumJacketStep, jacketStep); maximumHairStep = Mathf.Max(maximumHairStep, hairStep);
                // At 60 Hz the physical speed caps allow 25/33 mm, with a
                // 10 mm allowance for a contact correction during the blend.
                Assert.That(jacketStep, Is.LessThan(.035f), "Returning to a recording must not snap visible jacket vertices.");
                Assert.That(hairStep, Is.LessThan(.044f), "Returning to a recording must not snap the rendered hair joints.");
                Assert.That(hair.MaximumLengthError, Is.LessThan(.001f));
                if (frame == 3 || frame == 11)
                    Player3DJointSurfaceTests.AssertJacketHemClear(rig, "Test live-to-recorded transition frame " + frame);
                previousJacket = currentJacket; previousHair = currentHair;
            }
            Assert.That(jacket.TotalBakedTransitionCalls, Is.GreaterThan(transitions), "A continuous live-to-recorded return must use clearance-aware transition frames.");
            for (int frame = 0; frame < 3; frame++)
            {
                PreparePose(rig, clip, 0f, joints);
                long hairSteps = hair.TotalSimulationSteps, jacketSteps = jacket.TotalSimulationSteps;
                jacket.ApplyAt(clock += 1d / 60d, false, false, StillAir); hair.ApplyAt(clock, false, false, StillAir);
                Assert.That(hair.LastPoseWasBaked && jacket.LastPoseWasBaked, Is.True, "Restoring source context must return to playback.");
                Assert.That(hair.LastSimulationSteps + jacket.LastSimulationSteps, Is.Zero);
                Assert.That(hair.TotalSimulationSteps, Is.EqualTo(hairSteps));
                Assert.That(jacket.TotalSimulationSteps, Is.EqualTo(jacketSteps));
            }
            Assert.That(MaximumAngle(HairRotations(hair), originalHair), Is.LessThan(.01f), "The transition must reach the recorded pose.");
            Assert.That(MaximumDistance(JacketSamples(jacket), originalJacket), Is.LessThan(.000001f), "The transition must reach the recorded garment geometry.");
            report.AppendLine(FormattableString.Invariant($"Live-to-recorded transition: maximum visible jacket step={maximumJacketStep:F6}m; maximum hair joint step={maximumHairStep:F6}m; intermediate and final hem clearance retained; pure playback verified after 0.2s."));
            report.AppendLine(FormattableString.Invariant($"Hair playback calls={hair.TotalBakedPoseCalls}; fallbacks={hair.TotalBakedFallbacks}; jacket playback calls={jacket.TotalBakedPoseCalls}; fallbacks={jacket.TotalBakedFallbacks}."));
        }

        private static void VerifyRecordingCodec(StringBuilder report)
        {
            // Small-range movement fits the quantized channel budget; an
            // interior value in the large-range channel requires raw floats.
            float[] movement = { 0f, .01f, .023f, .019f, .042f, .05f };
            float[] wide = { -1000f, 1000f, .1234567f, 12.34567f, -28.54321f, 817.2387f };
            var track = new SecondaryMotionBakeRuntime.Track
            {
                Part = SecondaryMotionBakePart.Hair, Signature = "Test recorded channels",
                Values = Enumerable.Range(0, movement.Length).Select(frame => new[] { .375f, movement[frame], wide[frame] }).ToArray(),
                Guards = Enumerable.Range(0, movement.Length).Select(frame => new[] { .625f, movement[frame] }).ToArray()
            };
            byte[] bytes = SecondaryMotionBakeRuntime.Encode(new SecondaryMotionBakeRuntime.ClipData
                { Duration = 1f, Loop = true, Tracks = new[] { track } });
            MethodInfo decode = typeof(SecondaryMotionBakeRuntime).GetMethod("Decode", BindingFlags.Static | BindingFlags.NonPublic);
            var restored = (SecondaryMotionBakeRuntime.ClipData)decode.Invoke(null, new object[] { bytes });
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.Tracks.Single().Signature, Is.EqualTo(track.Signature));
            for (int frame = 0; frame < track.Values.Length; frame++)
            {
                for (int channel = 0; channel < track.Values[frame].Length; channel++)
                    Assert.That(Math.Abs((double)restored.Tracks[0].Values[frame][channel] - track.Values[frame][channel]), Is.LessThanOrEqualTo(.00001d));
                for (int channel = 0; channel < track.Guards[frame].Length; channel++)
                    Assert.That(Math.Abs((double)restored.Tracks[0].Guards[frame][channel] - track.Guards[frame][channel]), Is.LessThanOrEqualTo(.00001d));
                Assert.That(restored.Tracks[0].Values[frame][2], Is.EqualTo(wide[frame]), "Large-range physical data must retain its raw-float precision.");
            }
            Assert.That(decode.Invoke(null, new object[] { bytes.Take(bytes.Length / 2).ToArray() }), Is.Null, "A truncated recording must be rejected safely.");
            report.AppendLine("Recording codec: constant, quantized temporal movement and raw large-range channels retain values and pose guards within 1e-5; truncated input rejected.");
        }

        private static void PreparePose(Player3DAssetRegistry rig, AnimationClip clip, float seconds, CharacterJointDeformation joints)
        { clip.SampleAnimation(rig.Animator.gameObject, seconds); joints.ApplyPose(); rig.GetComponent<PlayerBootDeformation>().ApplyPose(FootGroundSample.None, FootGroundSample.None); SecondaryMotionBakeRuntime.SetContext(rig, clip, seconds, "no-gear"); }
        private sealed class PhysicalPose { internal Quaternion[] Hair; internal Vector3[] Jacket; }
        private static Quaternion[] HairRotations(PlayerHair hair) => ((Transform[])typeof(PlayerHair)
            .GetField("joints", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hair)).Select(joint => joint.localRotation).ToArray();
        private static Vector3[] JacketSamples(PlayerJacketCloth jacket)
        {
            var result = new List<Vector3>();
            for (int surface = 0; surface < jacket.SurfaceCount; surface++)
            {
                Vector3[] vertices = jacket.DeformedMesh(surface).vertices;
                for (int vertex = 0; vertex < vertices.Length; vertex += 64) result.Add(vertices[vertex]);
            }
            return result.ToArray();
        }
        private static Vector3[] HairWorldPoints(PlayerHair hair) => Enumerable.Range(0, hair.JointCount).Select(hair.WorldPoint).ToArray();
        private static Vector3[] JacketWorldPoints(Player3DAssetRegistry rig, PlayerJacketCloth jacket)
        {
            var result = new List<Vector3>();
            var mesh = new Mesh();
            try
            {
                foreach (Player3DMeshBinding binding in rig.MeshBindings)
                    if (binding.Renderer is SkinnedMeshRenderer renderer && Enumerable.Range(0, jacket.SurfaceCount)
                        .Any(surface => jacket.DeformedMesh(surface) == renderer.sharedMesh))
                    {
                        renderer.BakeMesh(mesh, true);
                        foreach (Vector3 point in mesh.vertices) result.Add(renderer.transform.TransformPoint(point));
                    }
            }
            finally { Object.DestroyImmediate(mesh); }
            Assert.That(result, Is.Not.Empty, "The transition probe must measure actual rendered jacket vertices.");
            return result.ToArray();
        }
        private static float MaximumAngle(Quaternion[] a, Quaternion[] b) => a.Zip(b, Quaternion.Angle).DefaultIfEmpty().Max();
        private static float MaximumDistance(Vector3[] a, Vector3[] b) => a.Zip(b, Vector3.Distance).DefaultIfEmpty().Max();
        private static double TicksToMilliseconds(long ticks) => ticks * 1000d / System.Diagnostics.Stopwatch.Frequency;
        private static void AppendCpu(StringBuilder report, string label, List<double> samples)
        {
            samples.Sort();
            report.AppendLine(FormattableString.Invariant($"{label} jacket+hair CPU: samples={samples.Count}; mean={samples.Average():F6}ms; p50={samples[samples.Count / 2]:F6}ms; p95={samples[Mathf.CeilToInt(samples.Count * .95f) - 1]:F6}ms; max={samples.Last():F6}ms."));
        }
        [Serializable] private sealed class Provenance { public int simulationHz, recordedHz; public string solver; public string[] recordings; public ProvenanceSource[] sources; }
        [Serializable] private sealed class ProvenanceSource { public string path, hash; }
        private static void AssertProvenance(StringBuilder report)
        {
            TextAsset asset = Resources.Load<TextAsset>(SecondaryMotionBakeRuntime.ResourceRoot + "Provenance");
            Assert.That(asset, Is.Not.Null);
            Provenance source = JsonUtility.FromJson<Provenance>(asset.text);
            Assert.That(source.simulationHz, Is.EqualTo(120)); Assert.That(source.recordedHz, Is.EqualTo(30));
            Assert.That(source.solver, Is.EqualTo("physical-player-secondary-motion-v1"));
            Assert.That(source.recordings, Is.Not.Empty); Assert.That(source.sources, Is.Not.Empty);
            Assert.That(source.sources.All(item => !string.IsNullOrEmpty(item.path) && !string.IsNullOrEmpty(item.hash)), Is.True);
            report.AppendLine("Provenance: " + source.solver + "; simulation=120Hz; recordings=30Hz; source dependencies=" + source.sources.Length + ".");
        }

        private sealed class CaptureCamera : IDisposable
        {
            private readonly Player3DAssetRegistry rig;
            private readonly GameObject cameraObject, lightObject;
            private readonly Camera camera;
            private readonly RenderTexture target;
            private readonly AmbientMode ambientMode = RenderSettings.ambientMode;
            private readonly Color ambient = RenderSettings.ambientLight;
            private readonly Light sun = RenderSettings.sun;
            private readonly bool fog = RenderSettings.fog;
            internal CaptureCamera(Player3DAssetRegistry rig)
            {
                this.rig = rig;
                foreach (Transform part in rig.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = 28;
                cameraObject = new GameObject("Test baked motion camera"); camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 1.02f;
                camera.nearClipPlane = .03f; camera.farClipPlane = 20f; camera.cullingMask = 1 << 28;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.2f, .22f, .26f);
                camera.allowHDR = false; camera.allowMSAA = false;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                camera.GetUniversalAdditionalCameraData().volumeLayerMask = 0;
                target = new RenderTexture(640, 768, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                target.Create(); camera.targetTexture = target;
                lightObject = new GameObject("Test baked motion light"); Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.6f; light.color = new Color(1f, .86f, .74f); light.cullingMask = 1 << 28;
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.52f, .54f, .58f);
                RenderSettings.sun = light; RenderSettings.fog = false;
            }
            internal void Save(string folder, string clip, bool side)
            {
                Vector3 forward = rig.transform.TransformDirection(rig.Metrics.LocalForward).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                Vector3 direction = side ? (right + forward * .12f).normalized : (forward + right * .12f).normalized;
                Vector3 focus = rig.Anchors.Pelvis.position + Vector3.up * .12f;
                camera.transform.SetPositionAndRotation(focus + direction * 3f, Quaternion.LookRotation(-direction, Vector3.up));
                lightObject.transform.forward = camera.transform.forward;
                camera.Render(); RenderTexture previous = RenderTexture.active; Texture2D image = null;
                try
                {
                    RenderTexture.active = target;
                    image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
                    image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                    Color[] pixels = image.GetPixels(); for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].gamma;
                    image.SetPixels(pixels); image.Apply(false, false);
                    Assert.That(pixels.Count(pixel => Vector3.Distance(new Vector3(pixel.r, pixel.g, pixel.b), new Vector3(pixels[0].r, pixels[0].g, pixels[0].b)) > .05f), Is.GreaterThan(1000), "The actual posed hero must be visible.");
                    File.WriteAllBytes(Path.Combine(folder, clip + (side ? " side.png" : " front.png")), image.EncodeToPNG());
                }
                finally { RenderTexture.active = previous; if (image != null) Object.DestroyImmediate(image); }
            }
            public void Dispose()
            {
                RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient; RenderSettings.sun = sun; RenderSettings.fog = fog;
                camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target);
                Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject);
            }
        }
    }
}
