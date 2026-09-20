using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Imports and measures the test-only arena, prop and Generic action banks.</summary>
    public sealed class CombatTestAssetSetup : AssetPostprocessor
    {
        public const string Folder = "Assets/Resources/Combat/";
        public const string ManifestPath = Folder + "CombatTest3D.json";
        public override uint GetVersion() => 3;
        private bool IsCombat => assetPath.StartsWith(Folder, StringComparison.Ordinal);
        private bool IsBank => IsCombat && assetPath.EndsWith("Actions.fbx", StringComparison.Ordinal);

        private void OnPreprocessModel()
        {
            if (!IsCombat || !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            importer.importBlendShapes = false; importer.isReadable = true;
            importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = IsBank;
            importer.animationType = IsBank ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            if (!IsBank) return;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(Player3DV2AssetSetup.ModelPath).OfType<Avatar>().FirstOrDefault();
            importer.avatarSetup = avatar != null ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = avatar;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsBank || !(assetImporter is ModelImporter importer)) return;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                int separator = clip.name.LastIndexOf('|');
                if (separator >= 0) clip.name = clip.name.Substring(separator + 1);
                clip.loopTime = CombatAssetProvider.IsLoop(clip.name); clip.loopPose = false;
                clip.keepOriginalOrientation = true; clip.keepOriginalPositionXZ = true; clip.keepOriginalPositionY = true;
                clip.lockRootRotation = true; clip.lockRootPositionXZ = true; clip.lockRootHeightY = true;
            }
            importer.clipAnimations = clips;
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!IsCombat || IsBank) return;
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                int suffix = part.name.LastIndexOf('.');
                if (suffix > 0 && int.TryParse(part.name.Substring(suffix + 1), out _)) part.name = part.name.Substring(0, suffix);
            }
        }

        [MenuItem("Bar Promenade/Combat Test/Validate Imported Assets")]
        public static void BuildOrThrow()
        {
            CombatBloodAssetSetup.BuildOrThrow();
            foreach (string file in new[] { "Arena.fbx", "Crowbar.fbx", "CombatActions.fbx", "CombatNpcActions.fbx", "CombatTest3D.json" })
                AssetDatabase.ImportAsset(Folder + file, ImportAssetOptions.ForceSynchronousImport);
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || !manifest.test_only || manifest.models == null || manifest.models.Length != 2 ||
                manifest.actions == null || manifest.actions.root_motion || manifest.actions.animation_events != 0 ||
                manifest.actions.maximum_support_error > .001f || manifest.actions.maximum_support_angle_degrees > .1f ||
                manifest.actions.attack_pelvis_travel_m < .04f || manifest.actions.attack_knee_travel_degrees < 7f ||
                Mathf.Abs(manifest.actions.defeat_handoff_seconds - CombatAssetProvider.DefeatHandoffSeconds) > .0001f ||
                manifest.actions.reactions == null || manifest.actions.reactions.Length != 3 ||
                manifest.actions.minimum_reaction_tip_separation_m < .2f)
                throw new InvalidOperationException("Combat manifest violated its isolated, grounded, in-place contract.");
            DefensiveStep step = manifest.actions.defensive_step;
            MeleeCombatSettings tuning = MeleeCombatSettings.Crowbar;
            if (step == null || Mathf.Abs(step.duration_seconds - tuning.StepDurationSeconds) > .0001f ||
                Mathf.Abs(step.travel_seconds - tuning.StepTravelSeconds) > .0001f ||
                Mathf.Abs(step.settle_seconds - tuning.StepRecoverySeconds) > .0001f ||
                Mathf.Abs(step.distance_m - tuning.StepDistance) > .0001f || step.travel_curve != "smoothstep")
                throw new InvalidOperationException("Combat defensive step differs from its constrained motor travel.");
            Charging charging = manifest.actions.charging;
            if (charging == null || charging.charge_parameter != "linear" ||
                charging.maximum_entry_error > .00001f || charging.maximum_lower_track_error > .00001f ||
                charging.maximum_light_legacy_error > .00001f || charging.maximum_support_error > .001f ||
                charging.minimum_reach_m < .95f || Mathf.Abs(charging.release_seconds - 1.28f) > .0001f)
                throw new InvalidOperationException("Combat charge lost its continuous, grounded release contract.");
            foreach (Model entry in manifest.models)
            {
                GameObject model = CombatAssetProvider.Create(entry.name, null);
                try
                {
                    Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
                    int triangles = 0;
                    foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                    {
                        Mesh mesh = filter.sharedMesh;
                        if (mesh.uv.Length != mesh.vertexCount) throw new InvalidOperationException("Combat art has no metric UVs.");
                        foreach (Vector3 vertex in mesh.vertices)
                        {
                            Vector3 point = filter.transform.TransformPoint(vertex);
                            low = Vector3.Min(low, point); high = Vector3.Max(high, point);
                        }
                        triangles += mesh.triangles.Length / 3;
                    }
                    Near(low, entry.bounds_min, entry.name + " minimum"); Near(high, entry.bounds_max, entry.name + " maximum");
                    if (triangles != entry.triangle_count) throw new InvalidOperationException("Combat imported triangle count differs.");
                    foreach (Anchor anchor in entry.anchors) Near(CombatAssetProvider.FindAnchor(model, anchor.name).position, anchor.position, anchor.name);
                }
                finally { UnityEngine.Object.DestroyImmediate(model); }
            }
            foreach (bool npc in new[] { false, true })
            foreach (string name in npc ? CombatAssetProvider.ClipNames.Concat(CombatAssetProvider.StepClipNames) :
                CombatAssetProvider.ClipNames.Concat(CombatAssetProvider.HeroLocomotionClipNames).Concat(CombatAssetProvider.StepClipNames))
            {
                AnimationClip clip = CombatAssetProvider.LoadClip(name, npc);
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.path.Contains("/root/")) continue;
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve != null && curve.keys.Length > 1 && curve.keys.Max(k => k.value) - curve.keys.Min(k => k.value) > .00001f)
                        throw new InvalidOperationException("Combat contains animated object/root motion: " + binding.path);
                }
            }
            ValidateGripMotion(false);
            ValidateGripMotion(true);
            Debug.Log("COMBAT IMPORTED METRES, ANCHORS AND IN-PLACE ACTIONS OK");
        }

        private static void ValidateGripMotion(bool npc)
        {
            GameObject template = npc ? DefaultNpcCatalog.GetPrefab() :
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player/Player3DV2.prefab");
            GameObject actor = UnityEngine.Object.Instantiate(template);
            try
            {
                Animator animator;
                Transform grip;
                if (npc)
                {
                    VillageResidentPresentation presentation = actor.GetComponent<VillageResidentPresentation>();
                    presentation.ReleaseAnimation();
                    animator = presentation.Animator;
                    grip = presentation.RightGrip;
                }
                else
                {
                    Player3DAssetRegistry registry = actor.GetComponentInChildren<Player3DAssetRegistry>();
                    animator = registry.Animator;
                    grip = registry.Anchors.RightGrip;
                }
                animator.enabled = false;
                NpcHandPose handPose = grip.GetComponentInParent<NpcHandPose>();
                GameObject bar = CombatAssetProvider.CreateCrowbar(grip, handPose);
                handPose.SetGrip(false, 1f);
                Transform tip = CombatAssetProvider.FindAnchor(bar, "StrikeTip");
                Transform origin = CombatAssetProvider.FindAnchor(bar, "Grip");
                AnimationClip attack = CombatAssetProvider.LoadClip(CombatAssetProvider.AttackClip, npc);
                float reach = 0f, travel = 0f;
                attack.SampleAnimation(animator.gameObject, .45f);
                Vector3 windup = actor.transform.InverseTransformPoint(tip.position);
                for (int frame = 45; frame <= 63; frame++)
                {
                    attack.SampleAnimation(animator.gameObject, frame / 100f);
                    Vector3 point = actor.transform.InverseTransformPoint(tip.position);
                    reach = Mathf.Max(reach, point.z);
                    travel = Mathf.Max(travel, Vector3.Distance(windup, point));
                    if (Vector3.Distance(origin.position, handPose.CylinderCentre(false)) > .001f ||
                        Vector3.Dot(bar.transform.up, handPose.CylinderAxis(false)) < .999f ||
                        Vector3.Distance(origin.position, tip.position) < .60f || Vector3.Distance(origin.position, tip.position) > .63f)
                        throw new InvalidOperationException("Combat crowbar grip or unit factor differs for " + (npc ? "NPC" : "hero"));
                }
                if (reach < .95f || travel < 1f)
                    throw new InvalidOperationException($"Combat imported {(npc ? "NPC" : "hero")} attack cannot reach the front target: reach={reach}, travel={travel}.");
                ValidateReactions(animator, grip, tip, npc);
                ValidateWeightAndHandoff(animator, npc);
                ValidateChargedRelease(animator, actor.transform, handPose, bar, origin, tip, npc);
                if (!npc)
                {
                    ValidateSideSteps(animator);
                    ValidateDefensiveSteps(animator);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(actor); }
        }

        private static void ValidateWeightAndHandoff(Animator animator, bool npc)
        {
            Transform[] bones = animator.GetComponentsInChildren<Transform>(true);
            Transform pelvis = bones.First(bone => bone.name == "pelvis");
            Transform[] feet = { bones.First(bone => bone.name == "foot.L"), bones.First(bone => bone.name == "foot.R") };
            AnimationClip ready = CombatAssetProvider.LoadClip(CombatAssetProvider.ReadyClip, npc);
            ready.SampleAnimation(animator.gameObject, 0f);
            Vector3[] planted = feet.Select(foot => foot.position).ToArray();
            Quaternion[] flat = feet.Select(foot => foot.rotation).ToArray();
            Vector3 initialPelvis = pelvis.position;
            float shift = 0f;
            foreach (string name in CombatAssetProvider.ClipNames)
            {
                AnimationClip clip = CombatAssetProvider.LoadClip(name, npc);
                int count = Mathf.CeilToInt(clip.length * 100f);
                for (int frame = 0; frame <= count; frame++)
                {
                    clip.SampleAnimation(animator.gameObject, Mathf.Min(clip.length, frame / 100f));
                    for (int i = 0; i < feet.Length; i++)
                        if (Vector3.Distance(planted[i], feet[i].position) > .002f || Quaternion.Angle(flat[i], feet[i].rotation) > .15f)
                            throw new InvalidOperationException("Combat imported action slid a support foot: " + name);
                    if (name == CombatAssetProvider.AttackClip) shift = Mathf.Max(shift, Vector3.Distance(initialPelvis, pelvis.position));
                }
            }
            if (shift < .025f) throw new InvalidOperationException("Combat imported attack lost its hip weight shift.");
            AnimationClip defeat = CombatAssetProvider.LoadClip(CombatAssetProvider.DefeatClip, npc);
            CompareEndpoint(defeat, 0f, ready, 0f, animator, bones, "defeat entry");
            CompareEndpoint(defeat, CombatAssetProvider.DefeatHandoffSeconds, defeat, defeat.length, animator, bones, "defeat handoff hold");
            defeat.SampleAnimation(animator.gameObject, CombatAssetProvider.DefeatHandoffSeconds);
            float drop = initialPelvis.y - pelvis.position.y;
            if (drop < .04f || drop > .13f)
                throw new InvalidOperationException("Combat imported defeat lost balance or authored the physical fall prematurely.");
        }

        private static void ValidateChargedRelease(Animator animator, Transform actor, NpcHandPose handPose,
            GameObject bar, Transform origin, Transform tip, bool npc)
        {
            Transform[] bones = animator.GetComponentsInChildren<Transform>(true);
            Transform[] feet = { bones.First(bone => bone.name == "foot.L"), bones.First(bone => bone.name == "foot.R") };
            AnimationClip charge = CombatAssetProvider.LoadClip(CombatAssetProvider.ChargeClip, npc);
            AnimationClip light = CombatAssetProvider.LoadClip(CombatAssetProvider.ReleaseLightClip, npc);
            AnimationClip heavy = CombatAssetProvider.LoadClip(CombatAssetProvider.ReleaseHeavyClip, npc);
            AnimationClip attack = CombatAssetProvider.LoadClip(CombatAssetProvider.AttackClip, npc);
            var positions = new Vector3[bones.Length];
            var rotations = new Quaternion[bones.Length];
            var entryPositions = new Vector3[bones.Length];
            var entryRotations = new Quaternion[bones.Length];
            void Blend(float seconds, float q)
            {
                light.SampleAnimation(animator.gameObject, seconds);
                for (int i = 0; i < bones.Length; i++)
                { positions[i] = bones[i].localPosition; rotations[i] = bones[i].localRotation; }
                heavy.SampleAnimation(animator.gameObject, seconds);
                for (int i = 0; i < bones.Length; i++)
                {
                    bones[i].localPosition = Vector3.Lerp(positions[i], bones[i].localPosition, q);
                    bones[i].localRotation = Quaternion.Slerp(rotations[i], bones[i].localRotation, q);
                }
            }
            light.SampleAnimation(animator.gameObject, 0f);
            Vector3[] planted = feet.Select(foot => foot.position).ToArray();
            Quaternion[] flat = feet.Select(foot => foot.rotation).ToArray();
            foreach (float q in new[] { 0f, .25f, .5f, .75f, 1f })
            {
                charge.SampleAnimation(animator.gameObject, q * charge.length);
                for (int i = 0; i < bones.Length; i++)
                { entryPositions[i] = bones[i].localPosition; entryRotations[i] = bones[i].localRotation; }
                Blend(0f, q);
                for (int i = 0; i < bones.Length; i++)
                    if (Vector3.Distance(entryPositions[i], bones[i].localPosition) > .0001f ||
                        Quaternion.Angle(entryRotations[i], bones[i].localRotation) > .06f)
                        throw new InvalidOperationException("Charged entry differs from its released pose: " + q + "/" + bones[i].name);
                float reach = 0f;
                for (int frame = 0; frame <= 128; frame++)
                {
                    float seconds = frame / 100f;
                    if (q == 0f) CompareEndpoint(light, seconds, attack, seconds, animator, bones, "light legacy strike");
                    Blend(seconds, q);
                    for (int i = 0; i < feet.Length; i++)
                        if (Vector3.Distance(planted[i], feet[i].position) > .002f || Quaternion.Angle(flat[i], feet[i].rotation) > .15f)
                            throw new InvalidOperationException("Charged blend moved an authored support foot: " + q);
                    if (Vector3.Distance(origin.position, handPose.CylinderCentre(false)) > .001f ||
                        Vector3.Dot(bar.transform.up, handPose.CylinderAxis(false)) < .999f)
                        throw new InvalidOperationException("Charged blend lost the crowbar grip: " + q);
                    if (seconds >= .45f && seconds <= .63f) reach = Mathf.Max(reach, actor.InverseTransformPoint(tip.position).z);
                }
                if (reach < .95f) throw new InvalidOperationException("Charged blend cannot reach its target: " + q);
            }
        }

        private static void ValidateSideSteps(Animator animator)
        {
            Transform[] bones = animator.GetComponentsInChildren<Transform>(true);
            Transform[] feet = { bones.First(bone => bone.name == "foot.L"), bones.First(bone => bone.name == "foot.R") };
            AnimationClip ready = CombatAssetProvider.LoadClip(CombatAssetProvider.ReadyClip);
            foreach (string name in CombatAssetProvider.HeroLocomotionClipNames)
            {
                AnimationClip clip = CombatAssetProvider.LoadClip(name);
                bool left = name == CombatAssetProvider.StrafeLeftClip;
                int leading = left ? 0 : 1;
                Vector3 direction = left ? Vector3.left : Vector3.right;
                CompareEndpoint(clip, 0f, ready, 0f, animator, bones, name + " entry");
                CompareEndpoint(clip, clip.length, ready, 0f, animator, bones, name + " loop seam");
                ready.SampleAnimation(animator.gameObject, 0f);
                Vector3[] start = feet.Select(foot => foot.position).ToArray();
                var lifts = new float[2];
                for (int frame = 0; frame <= 80; frame++)
                {
                    float phase = frame / 80f;
                    clip.SampleAnimation(animator.gameObject, phase * clip.length);
                    int planted = phase < .5f ? 1 - leading : leading;
                    Vector3 virtualRoot = direction * (.60f * phase);
                    Vector3 contact = start[planted] + (phase < .5f ? Vector3.zero : direction * .60f);
                    if (Vector3.Distance(feet[planted].position + virtualRoot, contact) > .003f)
                        throw new InvalidOperationException("Combat side step slides its support foot: " + name);
                    for (int i = 0; i < feet.Length; i++)
                        lifts[i] = Mathf.Max(lifts[i], feet[i].position.y - start[i].y);
                }
                if (lifts.Any(lift => lift < .05f))
                    throw new InvalidOperationException("Combat side step must lift both feet: " + name);
            }
        }

        private static void ValidateDefensiveSteps(Animator animator)
        {
            Transform[] bones = animator.GetComponentsInChildren<Transform>(true);
            Transform[] feet = { bones.First(bone => bone.name == "foot.L"), bones.First(bone => bone.name == "foot.R") };
            AnimationClip ready = CombatAssetProvider.LoadClip(CombatAssetProvider.ReadyClip);
            MeleeCombatSettings tuning = MeleeCombatSettings.Crowbar;
            foreach (string name in CombatAssetProvider.StepClipNames)
            {
                AnimationClip clip = CombatAssetProvider.LoadClip(name);
                Vector3 direction = name == CombatAssetProvider.StepForwardClip ? Vector3.forward :
                    name == CombatAssetProvider.StepBackwardClip ? Vector3.back :
                    name == CombatAssetProvider.StepLeftClip ? Vector3.left : Vector3.right;
                int leading = name == CombatAssetProvider.StepForwardClip || name == CombatAssetProvider.StepLeftClip ? 0 : 1;
                CompareEndpoint(clip, 0f, ready, 0f, animator, bones, name + " entry");
                CompareEndpoint(clip, clip.length, ready, 0f, animator, bones, name + " settled exit");
                ready.SampleAnimation(animator.gameObject, 0f);
                Vector3[] start = feet.Select(foot => foot.position).ToArray();
                Quaternion[] flat = feet.Select(foot => foot.rotation).ToArray();
                var lifts = new float[2];
                for (int frame = 0; frame <= Mathf.RoundToInt(tuning.StepDurationSeconds * 200f); frame++)
                {
                    float seconds = frame / 200f;
                    float travel = Mathf.Clamp01(seconds / tuning.StepTravelSeconds);
                    clip.SampleAnimation(animator.gameObject, Mathf.Min(seconds, clip.length));
                    Vector3 virtualRoot = direction * Mathf.SmoothStep(0f, tuning.StepDistance, travel);
                    for (int i = 0; i < feet.Length; i++)
                    {
                        bool planted = travel >= 1f || (i == leading ? travel >= .5f : travel <= .5f);
                        bool landed = travel >= 1f || i == leading;
                        Vector3 contact = start[i] + (landed ? direction * tuning.StepDistance : Vector3.zero);
                        float rise = feet[i].position.y - start[i].y;
                        if (planted && (Vector3.Distance(feet[i].position + virtualRoot, contact) > .003f ||
                            Quaternion.Angle(flat[i], feet[i].rotation) > .15f))
                            throw new InvalidOperationException("Combat defensive step slides its loaded sole: " + name);
                        if (rise < -.002f || rise > .08f)
                            throw new InvalidOperationException("Combat defensive step lost its low grounded foot arc: " + name);
                        lifts[i] = Mathf.Max(lifts[i], rise);
                    }
                }
                if (lifts.Any(lift => lift < .05f))
                    throw new InvalidOperationException("Combat defensive step must visibly move both feet: " + name);
            }
        }

        private static void ValidateReactions(Animator animator, Transform grip, Transform tip, bool npc)
        {
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            Transform[] bones = animator.GetComponentsInChildren<Transform>(true);
            var peakTips = new Vector3[manifest.actions.reactions.Length];
            for (int index = 0; index < manifest.actions.reactions.Length; index++)
            {
                Reaction reaction = manifest.actions.reactions[index];
                AnimationClip clip = CombatAssetProvider.LoadClip(reaction.name, npc);
                CompareEndpoint(clip, 0f, CombatAssetProvider.LoadClip(reaction.entry_clip, npc), reaction.entry_seconds,
                    animator, bones, reaction.name + " entry");
                CompareEndpoint(clip, clip.length, CombatAssetProvider.LoadClip(reaction.exit_clip, npc), reaction.exit_seconds,
                    animator, bones, reaction.name + " exit");
                clip.SampleAnimation(animator.gameObject, 0f);
                Vector3 start = grip.position;
                clip.SampleAnimation(animator.gameObject, reaction.peak_seconds);
                peakTips[index] = tip.position;
                if (Vector3.Distance(start, grip.position) < .08f)
                    throw new InvalidOperationException("Combat imported reaction lost its visible impact: " + reaction.name);
            }
            for (int i = 0; i < peakTips.Length; i++)
            for (int j = i + 1; j < peakTips.Length; j++)
                if (Vector3.Distance(peakTips[i], peakTips[j]) < .19f)
                    throw new InvalidOperationException("Combat imported reactions lost their distinct silhouettes.");
        }

        private static void CompareEndpoint(AnimationClip a, float timeA, AnimationClip b, float timeB,
            Animator animator, Transform[] bones, string label)
        {
            a.SampleAnimation(animator.gameObject, timeA);
            var positions = bones.Select(bone => bone.localPosition).ToArray();
            var rotations = bones.Select(bone => bone.localRotation).ToArray();
            b.SampleAnimation(animator.gameObject, timeB);
            for (int i = 0; i < bones.Length; i++)
                if (Vector3.Distance(positions[i], bones[i].localPosition) > .0001f ||
                    Quaternion.Angle(rotations[i], bones[i].localRotation) > .04f)
                    throw new InvalidOperationException("Combat imported reaction endpoint differs: " + label + "/" + bones[i].name);
        }

        private static void Near(Vector3 actual, float[] expected, string label)
        {
            if (expected == null || expected.Length != 3 || Vector3.Distance(actual, new Vector3(expected[0], expected[1], expected[2])) > .003f)
                throw new InvalidOperationException("Combat imported metres differ for " + label + ": " + actual);
        }
        [Serializable] private sealed class Manifest { public bool test_only; public Model[] models; public Actions actions; }
        [Serializable] private sealed class Model { public string name; public float[] bounds_min, bounds_max; public int triangle_count; public Anchor[] anchors; }
        [Serializable] private sealed class Anchor { public string name; public float[] position; }
        [Serializable] private sealed class Actions
        {
            public bool root_motion; public int animation_events;
            public float maximum_support_error, maximum_support_angle_degrees, minimum_reaction_tip_separation_m;
            public float attack_pelvis_travel_m, attack_knee_travel_degrees, defeat_handoff_seconds;
            public Reaction[] reactions;
            public DefensiveStep defensive_step;
            public Charging charging;
        }
        [Serializable] private sealed class Charging
        {
            public string charge_parameter;
            public float release_seconds, maximum_entry_error, maximum_lower_track_error;
            public float maximum_light_legacy_error, maximum_support_error, minimum_reach_m;
        }
        [Serializable] private sealed class DefensiveStep
        {
            public float duration_seconds, travel_seconds, settle_seconds, distance_m;
            public string travel_curve;
        }
        [Serializable] private sealed class Reaction
        {
            public string name, entry_clip, exit_clip;
            public float peak_seconds, entry_seconds, exit_seconds;
        }
    }
}
