using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>One bounded, cached warehouse-only fish pack. Imported unit roots stay intact.</summary>
    public static class WarehouseFishAssemblyAssets
    {
        public const string ResourceFolder = "CombatWarehouse/FishAssembly/";
        public const float PlaySurfaceHeight = .78f;
        public const float ConvulseDuration = 4f;
        public const float DeathDuration = 1f;
        public static readonly Vector2 PlayAreaHalfExtents = new Vector2(.88f, .38f);
        private static readonly string[] PartNames = { "FishTail", "FishBelly", "FishHead" };
        private static readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>();
        private static AnimationClip convulse;
        private static AnimationClip death;

        public static Vector3 PartRestPosition(int index)
        {
            if (index < 0 || index > 2) throw new ArgumentOutOfRangeException(nameof(index));
            return new Vector3((index - 1) * .12f, 0f, 0f);
        }

        public static GameObject CreatePart(int index, Transform parent)
        {
            if (index < 0 || index > 2) throw new ArgumentOutOfRangeException(nameof(index));
            return Create(PartNames[index], parent, true);
        }

        public static GameObject CreateKey(Transform parent) => Create("WarehouseGateKey", parent, true);

        public static GameObject CreatePlaySurface(Transform station) => Create("FishPlaySurface", station, false);

        public static GameObject CreateAssembled(Transform parent)
        {
            WarmClips();
            GameObject result = Create("AssembledFish", parent, false);
            foreach (Animator animator in result.GetComponentsInChildren<Animator>(true))
            {
                animator.applyRootMotion = false;
                animator.enabled = false;
            }
            Sample(result, "Convulse", 0f);
            return result;
        }

        public static void Warm()
        {
            foreach (string name in PartNames) Template(name);
            Template("WarehouseGateKey");
            Template("FishPlaySurface");
            Template("AssembledFish");
            WarmClips();
        }

        public static void Sample(GameObject fish, string clipName, float seconds)
        {
            if (fish == null) throw new ArgumentNullException(nameof(fish));
            WarmClips();
            bool dying = clipName == "Death" || clipName == "FishDeath";
            if (!dying && clipName != "Convulse" && clipName != "FishConvulse")
                throw new ArgumentOutOfRangeException(nameof(clipName));
            AnimationClip clip = dying ? death : convulse;
            // Imported bindings are relative to the imported model, never its metre wrapper.
            clip.SampleAnimation(fish.transform.GetChild(0).gameObject, Mathf.Clamp(seconds, 0f, clip.length));
        }

        public static Transform FindAnchor(GameObject model, string name)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
                if (BaseName(child.name) == name) return child;
            throw new InvalidOperationException("Fish assembly model lacks authored anchor " + name);
        }

        private static GameObject Template(string name)
        {
            if (!Templates.TryGetValue(name, out GameObject result) || result == null)
            {
                result = Resources.Load<GameObject>(ResourceFolder + name);
                if (result == null) throw new InvalidOperationException("Missing warehouse fish model " + name);
                Templates[name] = result;
            }
            return result;
        }

        private static void WarmClips()
        {
            if (convulse != null && death != null) return;
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(ResourceFolder + "AssembledFish"))
            {
                if (clip.name.IndexOf("FishConvulse", StringComparison.Ordinal) >= 0) convulse = clip;
                if (clip.name.IndexOf("FishDeath", StringComparison.Ordinal) >= 0) death = clip;
            }
            if (convulse == null || death == null)
                throw new InvalidOperationException("Warehouse fish requires imported FishConvulse and FishDeath clips.");
        }

        private static GameObject Create(string name, Transform parent, bool convex)
        {
            var wrapper = new GameObject("Warehouse " + name);
            wrapper.transform.SetParent(parent, false);
            UnityEngine.Object.Instantiate(Template(name), wrapper.transform, false);
            foreach (Renderer renderer in wrapper.GetComponentsInChildren<Renderer>(true))
            {
                string node = BaseName(renderer.name);
                if (node.StartsWith("Collision_", StringComparison.Ordinal))
                {
                    renderer.enabled = false;
                    var collider = renderer.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    collider.convex = convex;
                    collider.contactOffset = .001f;
                    continue;
                }
                int separator = node.LastIndexOf("__", StringComparison.Ordinal);
                if (separator < 0) throw new InvalidOperationException("Fish mesh lacks a shared surface role: " + node);
                renderer.sharedMaterial = WarehouseArenaAssetProvider.GetPropSurface(node.Substring(separator + 2));
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    // Authored motion stays inside this metre envelope. Convert it into each
                    // imported renderer's actual unit frame rather than assuming a scale of one.
                    Bounds bounds = default;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 p = new Vector3((corner & 1) == 0 ? -.27f : .27f,
                            (corner & 2) == 0 ? -.12f : .18f, (corner & 4) == 0 ? -.2f : .2f);
                        p = skin.transform.InverseTransformPoint(wrapper.transform.TransformPoint(p));
                        if (corner == 0) bounds = new Bounds(p, Vector3.zero);
                        else bounds.Encapsulate(p);
                    }
                    skin.localBounds = bounds;
                    skin.updateWhenOffscreen = false;
                }
            }
            return wrapper;
        }

        private static string BaseName(string value)
        {
            int dot = value.LastIndexOf('.');
            if (dot >= 0 && int.TryParse(value.Substring(dot + 1), out _)) return value.Substring(0, dot);
            return value;
        }
    }
}
