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
        public const string DetailMaterialPath = "VillageLife/DefaultNpcDetail";
        public const string FaceRendererName = "GEO_Head";
        public static readonly string[] OutfitNames = { "Raincoat", "Quilted", "Vest" };
        private static readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly HashSet<string> VisibleBody = new HashSet<string>
        { FaceRendererName, "GEO_Ears", "GEO_Neck", "GEO_Hand.L", "GEO_Hand.R" };
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransform = Shader.PropertyToID("_BaseMap_ST");
        private static Material detailMaterial;

        public static GameObject Create(int outfitIndex, Transform parent)
        {
            if (outfitIndex < 0 || outfitIndex >= OutfitNames.Length) throw new ArgumentOutOfRangeException(nameof(outfitIndex));
            GameObject wrapper = InstantiateModel("FairChild", parent);
            string outfit = OutfitNames[outfitIndex];
            string outfitPrefix = "OUTFIT_" + outfit + "_";
            var garments = new List<NpcWardrobe.GarmentBinding>();
            if (detailMaterial == null) detailMaterial = Resources.Load<Material>(DetailMaterialPath);
            if (detailMaterial == null) throw new InvalidOperationException("Missing shared default NPC detail material.");
            Texture2D skin = GetTexture("ChildSkin");
            var properties = new MaterialPropertyBlock();
            foreach (Renderer renderer in wrapper.GetComponentsInChildren<Renderer>(true))
            {
                string name = renderer.name;
                bool clothing = name.StartsWith("OUTFIT_", StringComparison.Ordinal);
                renderer.enabled = clothing ? name.StartsWith(outfitPrefix, StringComparison.Ordinal) : VisibleBody.Contains(name);
                renderer.sharedMaterial = detailMaterial;
                if (clothing)
                {
                    if (renderer.enabled) garments.Add(new NpcWardrobe.GarmentBinding(name, renderer, Color.white));
                }
                else
                {
                    properties.Clear();
                    if (name == FaceRendererName)
                    {
                        renderer.sharedMaterial = GetMaterial("ChildFace");
                        properties.SetVector(BaseMapTransform, new Vector4(.25f, .25f, 0f, .75f - .25f * outfitIndex));
                    }
                    else properties.SetTexture(BaseMap, skin);
                    renderer.SetPropertyBlock(properties);
                }
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            }
            wrapper.AddComponent<NpcWardrobe>().Configure("fair_child_" + outfit.ToLowerInvariant(),
                GetTexture(outfit + "Atlas"), detailMaterial, garments.ToArray());
            Animator animator = wrapper.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = wrapper.transform.GetChild(0).gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
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

        internal static void SetFace(Renderer face, MaterialPropertyBlock properties, int expression)
        {
            face.GetPropertyBlock(properties);
            Vector4 atlas = properties.GetVector(BaseMapTransform);
            atlas.z = Mathf.Clamp(expression, 0, 3) * .25f;
            properties.SetVector(BaseMapTransform, atlas);
            face.SetPropertyBlock(properties);
        }

        private static Texture2D GetTexture(string name)
        {
            if (Textures.TryGetValue(name, out Texture2D texture) && texture != null) return texture;
            texture = Resources.Load<Texture2D>(ResourceFolder + name);
            if (texture == null) throw new InvalidOperationException("Missing child texture: " + name);
            return Textures[name] = texture;
        }

        public static Material GetMaterial(string textureName)
        {
            if (Materials.TryGetValue(textureName, out Material material) && material != null) return material;
            Shader shader = Resources.Load<Shader>("Shaders/Lit");
            Texture2D texture = GetTexture(textureName);
            if (shader == null) throw new InvalidOperationException("Missing child Lit shader.");
            material = new Material(shader) { name = "Fair Child " + textureName + " Shared", hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", textureName == "ChildFace" ? .16f : .08f);
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
