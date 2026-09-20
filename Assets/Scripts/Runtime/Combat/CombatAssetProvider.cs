using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Test-only imported metric art; gameplay never manufactures visible geometry.</summary>
    public static class CombatAssetProvider
    {
        public const string ResourceFolder = "Combat/";
        public const string ReadyClip = "CombatReady", RestClip = "CombatRest", AttackClip = "CombatAttack",
            ChargeClip = "CombatCharge", ReleaseLightClip = "CombatReleaseLight", ReleaseHeavyClip = "CombatReleaseHeavy",
            BlockClip = "CombatBlock", HitClip = "CombatHit", GuardImpactClip = "CombatGuardImpact",
            GuardBreakClip = "CombatGuardBreak", RecoilClip = "CombatRecoil", DefeatClip = "CombatDefeat",
            StrafeLeftClip = "CombatStrafeLeft", StrafeRightClip = "CombatStrafeRight",
            StepForwardClip = "CombatStepForward", StepBackwardClip = "CombatStepBackward",
            StepLeftClip = "CombatStepLeft", StepRightClip = "CombatStepRight";
        public const float DefeatHandoffSeconds = .16f;
        public static readonly string[] ClipNames = { ReadyClip, RestClip, AttackClip, BlockClip, HitClip,
            GuardImpactClip, GuardBreakClip, RecoilClip, DefeatClip, ChargeClip, ReleaseLightClip, ReleaseHeavyClip };
        public static readonly string[] HeroLocomotionClipNames = { StrafeLeftClip, StrafeRightClip };
        /// <summary>Both banks carry the four defensive steps; the opponent steps too.</summary>
        public static readonly string[] StepClipNames = { StepForwardClip, StepBackwardClip, StepLeftClip, StepRightClip };
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>();

        public static bool IsLoop(string clip) => clip == ReadyClip || clip == RestClip || clip == BlockClip ||
            clip == StrafeLeftClip || clip == StrafeRightClip;
        public static float ClipDuration(string clip)
        {
            switch (clip)
            {
                case ReadyClip: case BlockClip: case RestClip: return 4f;
                case ChargeClip: return 1f;
                case StrafeLeftClip: case StrafeRightClip: return .8f;
                // The authored step is exactly the motor's committed travel plus settle.
                case StepForwardClip: case StepBackwardClip: case StepLeftClip: case StepRightClip:
                    return MeleeCombatSettings.Crowbar.StepDurationSeconds;
                case AttackClip: case ReleaseLightClip: case ReleaseHeavyClip: return 1.28f;
                case HitClip: return .36f;
                case GuardImpactClip: return .28f;
                case GuardBreakClip: return .70f;
                case RecoilClip: return .48f;
                case DefeatClip: return .36f;
                default: throw new ArgumentOutOfRangeException(nameof(clip), clip, "Unknown combat action.");
            }
        }

        public static GameObject CreateArena(Transform parent) => Create("Arena", parent, true);
        public static GameObject CreateCrowbar(Transform parent, NpcHandPose handPose)
        {
            if (parent == null || handPose == null)
                throw new ArgumentException("A crowbar requires its original hand and authored cylindrical grip.");
            GameObject result = Create("Crowbar", parent, false);
            // A bone can retain the FBX's centimetre unit scale. The wrapper
            // owns world metres; the imported child keeps its exact factors.
            Vector3 scale = parent.lossyScale;
            result.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            // The generic socket lies inside the neutral palm. The closed
            // fingers instead wrap the authored cylinder centre, with its
            // axis running across the palm toward the thumb.
            result.transform.position = handPose.CylinderCentre(false);
            result.transform.rotation = Quaternion.FromToRotation(result.transform.up,
                handPose.CylinderAxis(false)) * result.transform.rotation;
            return result;
        }

        public static GameObject Create(string name, Transform parent, bool collision = false)
        {
            if (name != "Arena" && name != "Crowbar") throw new ArgumentOutOfRangeException(nameof(name));
            GameObject template = Resources.Load<GameObject>(ResourceFolder + name);
            if (template == null) throw new InvalidOperationException("Missing combat model " + name);
            var wrapper = new GameObject(name);
            wrapper.transform.SetParent(parent, false);
            UnityEngine.Object.Instantiate(template, wrapper.transform, false);
            foreach (MeshRenderer renderer in wrapper.GetComponentsInChildren<MeshRenderer>(true))
            {
                int separator = renderer.name.LastIndexOf("__", StringComparison.Ordinal);
                if (separator < 0) throw new InvalidOperationException("Combat mesh lacks a surface role: " + renderer.name);
                renderer.sharedMaterial = Surface(renderer.name.Substring(separator + 2));
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (collision && renderer.name.StartsWith("Collision_", StringComparison.Ordinal))
                    renderer.gameObject.AddComponent<MeshCollider>().sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            }
            return wrapper;
        }

        public static Transform FindAnchor(GameObject model, string name)
        {
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            throw new InvalidOperationException("Combat model has no anchor " + name);
        }

        public static AnimationClip LoadClip(string name, bool npc = false)
        {
            string key = (npc ? "npc/" : "hero/") + name;
            if (Clips.TryGetValue(key, out AnimationClip cached) && cached != null) return cached;
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(ResourceFolder + (npc ? "CombatNpcActions" : "CombatActions")))
            {
                if (clip.name != name) continue;
                if (clip.isLooping != IsLoop(name) || Mathf.Abs(clip.length - ClipDuration(name)) > .003f || clip.events.Length != 0)
                    throw new InvalidOperationException("Invalid combat action " + name);
                Clips[key] = clip;
                return clip;
            }
            throw new InvalidOperationException("Missing combat action " + key);
        }

        private static Material Surface(string role)
        {
            if (Materials.TryGetValue(role, out Material cached) && cached != null) return cached;
            Color color;
            string texture = null;
            switch (role)
            {
                case "Concrete": color = new Color(.76f,.77f,.71f); texture = "HomeConcreteAlbedo"; break;
                case "ConcreteDark": color = new Color(.53f,.56f,.51f); texture = "HomeConcreteAlbedo"; break;
                case "Patch": color = new Color(.88f,.84f,.73f); texture = "HomeConcreteAlbedo"; break;
                case "Stripe": color = new Color(.64f,.54f,.31f); break;
                case "Steel": color = new Color(.25f,.28f,.27f); break;
                case "WornSteel": color = new Color(.52f,.55f,.52f); break;
                case "Rubber": color = new Color(.13f,.15f,.14f); break;
                default: throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown combat surface.");
            }
            Shader shader = Resources.Load<Shader>("Shaders/Ps1Lit");
            if (shader == null) throw new InvalidOperationException("Combat art requires the shared PS1 shader.");
            Texture2D sheet = texture == null ? Texture2D.whiteTexture : Resources.Load<Texture2D>("Home/Textures/" + texture);
            if (sheet == null) throw new InvalidOperationException("Combat art is missing its shared concrete sheet.");
            var material = new Material(shader) { name = "Combat " + role + " Shared", hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_BaseMap", sheet);
            material.SetColor("_BaseColor", color.linear);
            material.SetFloat("_Smoothness", role == "WornSteel" ? .3f : .08f);
            material.SetFloat("_Metallic", role == "Steel" || role == "WornSteel" ? .4f : 0f);
            Materials[role] = material;
            return material;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            Clips.Clear();
            foreach (Material material in Materials.Values)
                if (material != null) UnityEngine.Object.Destroy(material);
            Materials.Clear();
        }
    }
}
