using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BarPromenade;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Authoring-only recording of the production secondary-motion solvers.</summary>
    public sealed class SecondaryMotionBakeAssetSetup : IPreprocessBuildWithReport
    {
        private const string Output = "Assets/Resources/Player/SecondaryMotionBakes";
        private const string ManifestPath = Output + "/Provenance.json";
        private const int StepsPerSecond = 120, StepsPerSample = 4;
        private const int MaximumExtraWarmCycles = 6, SlingNodeValueCount = 123;
        private const float PreRollSeconds = .5f;
        public int callbackOrder => 10;

        [Serializable] private sealed class Source { public string path, hash; public bool raw; }
        [Serializable] private sealed class Provenance
        {
            public int version = 1, simulationHz = StepsPerSecond, recordedHz = StepsPerSecond / StepsPerSample;
            public string solver = "physical-player-secondary-motion-v1";
            public string[] recordings;
            public Source[] sources;
        }

        [MenuItem("Tools/Bar Promenade/Player/Bake secondary motion")]
        public static void BakeAll()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Bake secondary motion outside Play Mode.");
            GameObject prefab = Player3DResources.LoadPrefab();
            if (prefab == null) throw new InvalidOperationException("Package the production player before baking secondary motion.");
            var sources = new HashSet<string>(StringComparer.Ordinal) { AssetDatabase.GetAssetPath(prefab) };
            var paths = new List<string>();
            var clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            Player3DAssetRegistry template = prefab.GetComponent<Player3DAssetRegistry>();
            foreach (Player3DAnimationBinding binding in template.Animations)
            {
                if (binding.Clip == null || binding.ClipName.StartsWith("Face_", StringComparison.Ordinal) || binding.ClipName.Contains("Fall", StringComparison.Ordinal) ||
                    binding.ClipName.Contains("Rise", StringComparison.Ordinal) || binding.ClipName.Contains("Slip", StringComparison.Ordinal)) continue;
                clips[binding.Clip.name] = binding.Clip;
            }
            foreach (string name in CombatAssetProvider.ClipNames.Concat(CombatAssetProvider.LocomotionClipNames)
                .Concat(CombatAssetProvider.StepClipNames))
                if (!CombatAssetProvider.IsKickClip(name) && name != CombatAssetProvider.DefeatClip)
                    Add(CombatAssetProvider.LoadClip(name));
            foreach (string name in CombatPistolAssetProvider.ClipNames) Add(CombatPistolAssetProvider.LoadClip(name));
            foreach (string name in CombatShotgunAssetProvider.ClipNames) Add(CombatShotgunAssetProvider.LoadClip(name));
            foreach (string name in CombatAssetProvider.FirearmWalkClipNames) Add(CombatAssetProvider.LoadFirearmWalkClip(name));
            foreach (CombatWeaponId id in new[] { CombatWeaponId.Crowbar, CombatWeaponId.Pistol })
            { Add(CombatWeaponCarryAssetProvider.LoadClip(id, true)); Add(CombatWeaponCarryAssetProvider.LoadClip(id, false)); }
            AnimationClip draw = CombatWeaponCarryAssetProvider.LoadClip(CombatWeaponId.Shotgun, true);
            AnimationClip stow = CombatWeaponCarryAssetProvider.LoadClip(CombatWeaponId.Shotgun, false);
            Add(draw); Add(stow);
            foreach (AnimationClip clip in clips.Values) sources.Add(AssetDatabase.GetAssetPath(clip));
            foreach (string folder in new[] { "Assets/Resources/CombatShotgun", "Assets/Resources/CombatWeaponCarry" })
                foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { folder })) sources.Add(AssetDatabase.GUIDToAssetPath(guid));

            try
            {
                foreach (string variant in new[] { "no-gear", "stored-shotgun" })
                    foreach (AnimationClip clip in clips.Values.OrderBy(value => value.name, StringComparer.Ordinal))
                    {
                        if (clip == draw || clip == stow) continue;
                        Bake(prefab, clip, variant, paths);
                    }
                Bake(prefab, draw, "draw-shotgun", paths);
                Bake(prefab, stow, "stow-shotgun", paths);
                // Remove only obsolete recordings owned by this bank, together
                // with their Unity metadata; the production assets stay intact.
                var expected = new HashSet<string>(paths, StringComparer.Ordinal);
                string bankRoot = Path.GetFullPath(Output) + Path.DirectorySeparatorChar;
                foreach (string obsolete in Directory.EnumerateFiles(Output, "*.bytes", SearchOption.AllDirectories))
                {
                    string full = Path.GetFullPath(obsolete);
                    if (!full.StartsWith(bankRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Recording escaped the authoring directory.");
                    string relative = full.Substring(bankRoot.Length).Replace('\\', '/');
                    if (!expected.Contains(relative)) AssetDatabase.DeleteAsset(obsolete.Replace('\\', '/'));
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var manifest = new Provenance
                {
                    recordings = paths.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    sources = sources.Where(path => !string.IsNullOrEmpty(path)).OrderBy(path => path, StringComparer.Ordinal)
                        .Select(path => new Source { path = path, hash = AssetDatabase.GetAssetDependencyHash(path).ToString() })
                        .Concat(AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/Scripts/Runtime/Player3D" })
                            .Select(AssetDatabase.GUIDToAssetPath).Concat(new[]
                            { "Assets/Scripts/Runtime/Combat/CombatShotgunSling.cs", "Assets/Scripts/Runtime/Combat/CombatSlingProjectionJob.cs",
                                "Assets/Scripts/Runtime/Core/CharacterJointDeformation.cs",
                                "Assets/Scripts/Runtime/Combat/CombatWeaponCarryAssetProvider.cs", "Assets/Scripts/Runtime/Combat/CombatShotgunAssetProvider.cs",
                                "Assets/Scripts/Editor/Player3D/SecondaryMotionBakeAssetSetup.cs" })
                            .OrderBy(path => path, StringComparer.Ordinal).Select(path => new Source { path = path, raw = true, hash = ContentHash(path) })).ToArray()
                };
                File.WriteAllText(ManifestPath, JsonUtility.ToJson(manifest, true) + "\n");
                AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
                SecondaryMotionBakeRuntime.ReloadAssets();
                Debug.Log("Secondary motion physical recordings saved: " + paths.Count);
            }
            finally { EditorUtility.ClearProgressBar(); }
            void Add(AnimationClip clip) { if (clip == null) throw new InvalidOperationException("Missing authoring animation."); clips[clip.name] = clip; }
        }

        private static void Bake(GameObject prefab, AnimationClip clip, string variant, List<string> paths)
        {
            string relative = variant + "/" + clip.name + ".bytes", path = Output + "/" + relative;
            EditorUtility.DisplayProgressBar("Bake secondary motion", relative, 0f);
            var owner = new GameObject("Secondary motion authoring rig") { hideFlags = HideFlags.HideAndDontSave };
            CombatShotgunSling sling = null;
            PlayerHair hair = null;
            PlayerJacketCloth jacket = null;
            try
            {
                Player3DAssetRegistry rig = UnityEngine.Object.Instantiate(prefab, owner.transform).GetComponent<Player3DAssetRegistry>();
                foreach (MonoBehaviour driver in rig.GetComponentsInChildren<MonoBehaviour>(true)) driver.enabled = false;
                rig.Animator.enabled = false;
                hair = rig.GetComponent<PlayerHair>();
                jacket = rig.GetComponent<PlayerJacketCloth>();
                CharacterJointDeformation joints = rig.GetComponent<CharacterJointDeformation>();
                PlayerBootDeformation boots = rig.GetComponent<PlayerBootDeformation>();
                bool carryAction = variant == "draw-shotgun" || variant == "stow-shotgun";
                if (variant != "no-gear") sling = CombatShotgunSling.CreateBakeSling(rig);
                using IDisposable hairCapture = hair.BeginBakeCapture();
                using IDisposable jacketCapture = jacket.BeginBakeCapture();
                using IDisposable slingCapture = sling != null ? sling.BeginBakeCapture(variant) : null;
                using IDisposable slingQuality = sling != null ? sling.BeginBakeQuality() : null;
                bool loop = !carryAction && clip.isLooping;
                float physicalDuration = carryAction ? clip.length / 2f : clip.length;
                int recordedIntervals = Mathf.Max(1, Mathf.CeilToInt(physicalDuration * (StepsPerSecond / StepsPerSample)));
                int physicalSteps = recordedIntervals * StepsPerSample;
                float dt = physicalDuration / physicalSteps;
                // A partial first cycle can leave cuffs in a different contact
                // basin. Warm complete cycles before recording a looping gait.
                int preSteps = Mathf.CeilToInt(Mathf.Max(PreRollSeconds, loop ? physicalDuration * 2f : 0f) / dt);
                var tracks = new List<SecondaryMotionBakeRuntime.Track>
                {
                    Track(SecondaryMotionBakePart.Hair, hair.BakedPoseSignature, hair.BakedPoseValueCount),
                    Track(SecondaryMotionBakePart.Jacket, jacket.BakedPoseSignature, jacket.BakedPoseValueCount)
                };
                if (sling != null) tracks.Add(Track(SecondaryMotionBakePart.Sling, sling.BakedPoseSignature, sling.BakedPoseValueCount));
                SecondaryMotionBakeRuntime.Track slingTrack = tracks.FirstOrDefault(track => track.Part == SecondaryMotionBakePart.Sling);
                var guards = new List<SecondaryMotionBakeRuntime.PoseGuard>();
                float[] startPrevious = sling != null ? new float[sling.BakedStateValueCount] : null;
                float[] endPrevious = sling != null ? new float[sling.BakedStateValueCount] : null;
                bool candidateSafe = true, endpointSafe = true;
                string firstUnsafe = null;
                float maximumContact = 0f, maximumLink = 0f, seamPosition = 0f, seamVelocity = 0f;
                double simulation = 0d;
                for (int step = -preSteps; step <= physicalSteps; step++)
                {
                    Advance(step);
                    if (step < 0) continue;
                    Record(step);
                    if (sling != null && step == 0) sling.CaptureBakedState(startPrevious);
                    if (sling != null && step == physicalSteps) sling.CaptureBakedState(endPrevious);
                }
                int extraCycles = 0;
                if (sling != null && loop)
                {
                    MeasureSeam();
                    while ((!candidateSafe || seamPosition > .001f || seamVelocity > .02f) && extraCycles < MaximumExtraWarmCycles)
                    {
                        // The previous endpoint is this next cycle's real start
                        // at the same instant. Preserve it without another dt.
                        foreach (SecondaryMotionBakeRuntime.Track track in tracks)
                        {
                            Array.Copy(track.Values[recordedIntervals], track.Values[0], track.Values[0].Length);
                            track.Guards[0] = track.Guards[physicalSteps];
                        }
                        Array.Copy(endPrevious, startPrevious, startPrevious.Length);
                        candidateSafe = endpointSafe;
                        firstUnsafe = null;
                        maximumContact = sling.MaximumContactError; maximumLink = sling.MaximumLinkError;
                        for (int step = 1; step <= physicalSteps; step++) { Advance(step); Record(step); }
                        sling.CaptureBakedState(endPrevious);
                        extraCycles++;
                        MeasureSeam();
                    }
                }
                bool includedSling = sling != null && candidateSafe && (!loop || seamPosition <= .001f && seamVelocity <= .02f);
                if (sling != null && !includedSling)
                {
                    tracks.Remove(slingTrack);
                    Debug.LogWarning(FormattableString.Invariant($"Secondary motion bake {relative}: Sling omitted; physical fallback; safe={candidateSafe}; contact={maximumContact:F6}m; link={maximumLink:F6}m; loop position seam={seamPosition:F6}m; velocity seam={seamVelocity:F6}m/s; extra cycles={extraCycles}; first unsafe={firstUnsafe}."));
                }
                int warmupSteps = preSteps + extraCycles * physicalSteps;
                byte[] bytes = SecondaryMotionBakeRuntime.Encode(new SecondaryMotionBakeRuntime.ClipData
                { Duration = clip.length, Loop = loop, WarmupSteps = warmupSteps, Tracks = tracks.ToArray() });
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, bytes);
                paths.Add(relative);
                Debug.Log(FormattableString.Invariant($"Secondary motion bake {relative}: {bytes.Length} bytes, physical120Hz, samples={recordedIntervals + 1}; parts={string.Join(",", tracks.Select(track => track.Part))}; warmup steps={warmupSteps}; sling contact={maximumContact:F6}m; link={maximumLink:F6}m; loop position seam={seamPosition:F6}m; velocity seam={seamVelocity:F6}m/s."));

                void Advance(int step)
                {
                    float phase = step / (float)physicalSteps;
                    float normalized = loop ? Mathf.Repeat(phase, 1f) : Mathf.Clamp01(phase);
                    // Moving authored gaits are in-place clips. Supply real
                    // root translation to the same physical solver history.
                    rig.transform.position = rig.transform.TransformDirection(rig.Metrics.LocalForward) * SecondaryMotionBakeRuntime.AuthoredSpeed(clip.name) * (float)simulation;
                    clip.SampleAnimation(rig.Animator.gameObject, normalized * clip.length);
                    joints.ApplyPose();
                    boots.ApplyPose(FootGroundSample.None, FootGroundSample.None);
                    sling?.SimulateBakeStep(dt, normalized);
                    jacket.ApplyAt(simulation, false, false, new WindSample(0f, 0f));
                    hair.ApplyAt(simulation, false, false, new WindSample(0f, 0f));
                    simulation += dt;
                }

                void Record(int step)
                {
                    if (sling != null)
                    {
                        endpointSafe = sling.LastBakeQualityApplied && sling.LastBakeQualityConverged &&
                            float.IsFinite(sling.MaximumContactError) && float.IsFinite(sling.MaximumLinkError);
                        candidateSafe &= endpointSafe;
                        if (!endpointSafe && firstUnsafe == null)
                            firstUnsafe = FormattableString.Invariant($"phase={step / (float)physicalSteps:F6}, iterations={sling.LastBakeQualityIterations}, contact/link before final projection={sling.LastBakeQualityPreFallbackContactError:F6}/{sling.LastBakeQualityPreFallbackLinkError:F6}m, nodes={sling.LastBakeQualityPreFallbackContactIndex}/{sling.LastBakeQualityPreFallbackLinkIndex}");
                        maximumContact = Mathf.Max(maximumContact, sling.MaximumContactError);
                        maximumLink = Mathf.Max(maximumLink, sling.MaximumLinkError);
                    }
                    if (step % StepsPerSample == 0)
                        foreach (SecondaryMotionBakeRuntime.Track track in tracks)
                        {
                            float[] values = track.Values[step / StepsPerSample];
                            if (track.Part == SecondaryMotionBakePart.Hair) hair.CaptureBakedPose(values);
                            else if (track.Part == SecondaryMotionBakePart.Jacket) jacket.CaptureBakedPose(values);
                            else sling.CaptureBakedPose(values);
                            if (!values.Any(value => !float.IsFinite(value))) continue;
                            if (track.Part == SecondaryMotionBakePart.Sling) candidateSafe = endpointSafe = false;
                            else throw new InvalidOperationException("Non-finite physical bake: " + relative);
                        }
                    if (guards.Count == 0)
                        foreach (SecondaryMotionBakeRuntime.Track track in tracks) guards.Add(new SecondaryMotionBakeRuntime.PoseGuard(rig, track.Part));
                    for (int index = 0; index < tracks.Count; index++) tracks[index].Guards[step] = guards[index].Capture();
                }

                void MeasureSeam()
                {
                    seamPosition = seamVelocity = 0f;
                    float[] start = slingTrack.Values[0], end = slingTrack.Values[recordedIntervals];
                    for (int point = 0; point < SlingNodeValueCount; point += 3)
                    {
                        Vector3 first = Point(start), last = Point(end);
                        float position = Vector3.Distance(first, last);
                        seamPosition = float.IsFinite(position) ? Mathf.Max(seamPosition, position) : float.PositiveInfinity;
                        // The two attachment endpoints are projected, never
                        // integrated: their unused previous positions remain at
                        // Seed and are not physical Verlet velocity history.
                        if (point == 0 || point == SlingNodeValueCount - 3) continue;
                        float velocity = ((last - Point(endPrevious)) - (first - Point(startPrevious))).magnitude / dt;
                        seamVelocity = float.IsFinite(velocity) ? Mathf.Max(seamVelocity, velocity) : float.PositiveInfinity;
                        Vector3 Point(float[] values) => new Vector3(values[point], values[point + 1], values[point + 2]);
                    }
                }
                SecondaryMotionBakeRuntime.Track Track(SecondaryMotionBakePart part, string signature, int count)
                {
                    var values = new float[recordedIntervals + 1][];
                    for (int frame = 0; frame < values.Length; frame++) values[frame] = new float[count];
                    return new SecondaryMotionBakeRuntime.Track
                    { Part = part, Signature = signature, Values = values, Guards = new float[physicalSteps + 1][] };
                }
            }
            finally
            {
                // Ordinary MonoBehaviours on edit-mode clones may never receive
                // OnDestroy. Their explicitly-created native storage still has
                // an owner, including when capture initialization throws.
                try { if (sling != null) sling.DisposeBakeSling(); }
                finally
                {
                    try { if (jacket != null) jacket.DisposeOwnedBakeResources(owner); }
                    finally
                    {
                        try { if (hair != null) hair.DisposeOwnedBakeResources(owner); }
                        finally { UnityEngine.Object.DestroyImmediate(owner); }
                    }
                }
            }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!IsCurrent()) throw new BuildFailedException("Player secondary-motion recordings are stale. Run Tools/Bar Promenade/Player/Bake secondary motion.");
        }

        public static void EnsureCurrent()
        { if (!IsCurrent()) BakeAll(); }

        // Focused physical proof before publishing a whole bank. This never
        // updates provenance, so a partial recording cannot pass the build gate.
        public static void BakeProofAnimationSet()
        {
            GameObject prefab = Player3DResources.LoadPrefab();
            Player3DAssetRegistry template = prefab.GetComponent<Player3DAssetRegistry>();
            var paths = new List<string>();
            try
            {
                foreach (string name in new[] { "Idle", "Walk", "Run" })
                {
                    AnimationClip clip = template.Animations.Single(binding => binding.ClipName == name).Clip;
                    Bake(prefab, clip, "no-gear", paths);
                    if (name == "Idle") Bake(prefab, clip, "stored-shotgun", paths);
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                SecondaryMotionBakeRuntime.ReloadAssets();
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private static bool IsCurrent()
        {
            if (!File.Exists(ManifestPath)) return false;
            Provenance manifest = JsonUtility.FromJson<Provenance>(File.ReadAllText(ManifestPath));
            return !(manifest == null || manifest.version != 1 || manifest.sources == null || manifest.recordings == null ||
                manifest.recordings.Any(path => !File.Exists(Output + "/" + path)) ||
                manifest.sources.Any(source => !File.Exists(source.path) ||
                    (source.raw ? ContentHash(source.path) : AssetDatabase.GetAssetDependencyHash(source.path).ToString()) != source.hash));
        }

        private static string ContentHash(string path)
        {
            using var hash = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", string.Empty);
        }
    }
}
