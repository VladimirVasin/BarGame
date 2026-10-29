using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Finite authored models, one shared dog palette, no runtime geometry.</summary>
    public static class CityDogParkAssetProvider
    {
        public const string ResourceFolder = "City/DogPark/";
        public const string ParkResourcePath = ResourceFolder + "StaticDogPark";
        public const int DogVariantCount = 3;
        private static readonly string[] Names = { "DogSmall", "DogShaggy", "DogLarge" };
        private static readonly GameObject[] Templates = new GameObject[DogVariantCount + 1];
        private static Material paletteMaterial;

        public static GameObject CreatePark(Transform parent) => Create(parent, 0, "StaticDogPark");

        public static GameObject CreateDog(Transform parent, int variant)
        {
            if (variant < 0 || variant >= DogVariantCount) throw new ArgumentOutOfRangeException(nameof(variant));
            return Create(parent, variant + 1, Names[variant]);
        }

        public static Transform FindPart(Transform root, string name)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                if (part.name == name) return part;
            throw new InvalidOperationException("Dog park authored part is missing: " + name);
        }

        private static GameObject Create(Transform parent, int slot, string name)
        {
            if (Templates[slot] == null) Templates[slot] = Resources.Load<GameObject>(ResourceFolder + name);
            if (Templates[slot] == null) throw new InvalidOperationException("Missing authored dog park model: " + name);
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            // Keep the source wrapper: FBX unit conversion belongs to its root.
            GameObject instance = UnityEngine.Object.Instantiate(Templates[slot], root.transform, false);
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.sharedMaterial = PaletteMaterial;
                renderer.shadowCastingMode = renderer.name.StartsWith("ParkFenceWire", StringComparison.Ordinal)
                    ? ShadowCastingMode.Off : ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (renderer.name.StartsWith("ParkGround", StringComparison.Ordinal))
                {
                    CityParkSurfaceAppearance.ApplyCombined(renderer, CityParkSurfaceKind.Path,
                        new Color(.34f, .315f, .265f));
                    FootstepGround.Stamp(renderer, FootstepGroundKind.Soil);
                }
            }
            return root;
        }

        private static Material PaletteMaterial
        {
            get
            {
                if (paletteMaterial != null) return paletteMaterial;
                Shader shader = Resources.Load<Shader>("Shaders/Lit");
                Texture2D palette = Resources.Load<Texture2D>(ResourceFolder + "DogParkPalette");
                if (shader == null)
                    throw new InvalidOperationException("Dog park shared shader resource is missing: Shaders/Lit.");
                if (palette == null)
                    throw new InvalidOperationException("Dog park palette resource is missing: " + ResourceFolder + "DogParkPalette.");
                paletteMaterial = new Material(shader)
                {
                    name = "Residential Dogs and Park Shared", hideFlags = HideFlags.HideAndDontSave,
                    enableInstancing = true
                };
                paletteMaterial.SetTexture("_BaseMap", palette);
                paletteMaterial.SetColor("_BaseColor", Color.white);
                paletteMaterial.SetFloat("_Smoothness", .10f);
                paletteMaterial.SetFloat("_Metallic", 0f);
                return paletteMaterial;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResources()
        {
            Array.Clear(Templates, 0, Templates.Length);
            if (paletteMaterial != null) UnityEngine.Object.Destroy(paletteMaterial);
            paletteMaterial = null;
        }
    }
}
