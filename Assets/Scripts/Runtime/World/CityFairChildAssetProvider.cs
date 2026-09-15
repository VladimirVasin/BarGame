using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>One child-native skeleton/body, three real garment sets, shared painted surfaces.</summary>
    public static class CityFairChildAssetProvider
    {
        public const string ResourceFolder = "City/FairChild/";
        public static readonly string[] OutfitNames = { "Raincoat", "Quilted", "Vest" };
        private static readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly HashSet<string> VisibleBody = new HashSet<string>
        { "GEO_Head", "GEO_Face", "GEO_Ears", "GEO_Neck", "GEO_Hair", "GEO_Hand.L", "GEO_Hand.R" };

        public static GameObject Create(int outfitIndex, Transform parent)
        {
            if (outfitIndex < 0 || outfitIndex >= OutfitNames.Length) throw new ArgumentOutOfRangeException(nameof(outfitIndex));
            GameObject wrapper = InstantiateModel("FairChild", parent);
            string outfit = OutfitNames[outfitIndex];
            var garments = new List<NpcWardrobe.GarmentBinding>();
            Material outfitMaterial = GetMaterial(outfit + "Atlas");
            Texture2D outfitAtlas = Resources.Load<Texture2D>(ResourceFolder + outfit + "Atlas");
            foreach (Renderer renderer in wrapper.GetComponentsInChildren<Renderer>(true))
            {
                string name = renderer.name;
                bool clothing = name.StartsWith("OUTFIT_", StringComparison.Ordinal);
                renderer.enabled = clothing ? name.StartsWith("OUTFIT_" + outfit + "_", StringComparison.Ordinal) : VisibleBody.Contains(name);
                if (clothing)
                {
                    string garmentOutfit = name.Split('_')[1];
                    renderer.sharedMaterial = GetMaterial(garmentOutfit + "Atlas");
                    if (renderer.enabled) garments.Add(new NpcWardrobe.GarmentBinding(name, renderer, Color.white));
                }
                else renderer.sharedMaterial = GetMaterial(name == "GEO_Face" ? "ChildFace" : name == "GEO_Hair" ? "ChildHair" : "ChildSkin");
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
            }
            wrapper.AddComponent<NpcWardrobe>().Configure("fair_child_" + outfit.ToLowerInvariant(), outfitAtlas, outfitMaterial, garments.ToArray());
            Animator animator = wrapper.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = wrapper.transform.GetChild(0).gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            SetFace(wrapper, 0);
            return wrapper;
        }

        public static GameObject CreateProp(string kind, Transform parent)
        {
            if (kind != "LowTable" && kind != "WoodenCar") throw new ArgumentOutOfRangeException(nameof(kind));
            GameObject wrapper = InstantiateModel(kind, parent);
            foreach (Renderer renderer in wrapper.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = GetMaterial("ToyWood");
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            foreach (Transform pivot in wrapper.GetComponentsInChildren<Transform>(true))
            {
                if (!pivot.name.StartsWith("WheelPivot_", StringComparison.Ordinal) || pivot.GetComponent<MeshFilter>() != null) continue;
                var children = new Transform[pivot.childCount];
                for (int i = 0; i < children.Length; i++) children[i] = pivot.GetChild(i);
                foreach (Transform child in children) child.SetParent(wrapper.transform, true);
                pivot.SetParent(wrapper.transform, true);
                pivot.localRotation = Quaternion.identity;
                pivot.localScale = Vector3.one;
                foreach (Transform child in children) child.SetParent(pivot, true);
            }
            return wrapper;
        }

        public static Transform FindBone(GameObject model, string name) => FindPart(model, name);

        public static Transform FindPart(GameObject model, string name)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
                if (part.name == name) return part;
            throw new InvalidOperationException($"Child model '{model.name}' lacks '{name}'.");
        }

        public static void SetFace(GameObject model, int expression)
        {
            Renderer face = FindPart(model, "GEO_Face").GetComponent<Renderer>();
            var properties = new MaterialPropertyBlock();
            face.GetPropertyBlock(properties);
            properties.SetVector("_BaseMap_ST", new Vector4(.25f, 1f, Mathf.Clamp(expression, 0, 3) * .25f, 0f));
            face.SetPropertyBlock(properties);
        }

        public static Material GetMaterial(string textureName)
        {
            if (Materials.TryGetValue(textureName, out Material material) && material != null) return material;
            Shader shader = Resources.Load<Shader>("Shaders/Ps1Lit");
            Texture2D texture = Resources.Load<Texture2D>(ResourceFolder + textureName);
            if (shader == null || texture == null) throw new InvalidOperationException("Missing child surface: " + textureName);
            material = new Material(shader) { name = "Fair Child " + textureName + " Shared", hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", textureName == "ChildSkin" ? .16f : .08f);
            Materials[textureName] = material;
            return material;
        }

        private static GameObject InstantiateModel(string kind, Transform parent)
        {
            if (!Templates.TryGetValue(kind, out GameObject template) || template == null)
            {
                template = Resources.Load<GameObject>(ResourceFolder + kind);
                if (template == null) throw new InvalidOperationException("Missing fair child model: " + kind);
                Templates[kind] = template;
            }
            var wrapper = new GameObject(kind);
            wrapper.transform.SetParent(parent, false);
            UnityEngine.Object.Instantiate(template, wrapper.transform, false);
            return wrapper;
        }
    }
}
