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
        private static Material fittings;
        private static Texture2D soil, soilResponse;

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
                renderer.shadowCastingMode = renderer.name.StartsWith("ParkFenceWire", StringComparison.Ordinal)
                    ? ShadowCastingMode.Off : ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (renderer.name.StartsWith("ParkGround", StringComparison.Ordinal))
                {
                    if (!soil) soil = Texture("DogParkSoil");
                    if (!soilResponse) soilResponse = Texture("DogParkSoilResponse");
                    GroundSurfaceAppearance.Apply(renderer, GroundSurfaceKind.Soil, soil, responseOverride: soilResponse);
                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties);
                    properties.SetTexture("_BaseMap", soil);
                    properties.SetColor("_BaseColor", Color.white);
                    properties.SetFloat("_GroundCityClimate", 1f);
                    properties.SetFloat("_Smoothness", .22f);
                    renderer.SetPropertyBlock(properties);
                    FootstepGround.Stamp(renderer, FootstepGroundKind.Soil);
                }
                else renderer.sharedMaterial = Fittings;
            }
            return root;
        }

        private static Texture2D Texture(string name) => Resources.Load<Texture2D>(ResourceFolder + name)
            ?? throw new InvalidOperationException("Missing dog park finish " + name);

        private static Material Fittings
        {
            get
            {
                if (fittings) return fittings;
                fittings = new Material(Resources.Load<Shader>("Shaders/CitySurfaceLit"))
                {
                    name = "Dog coats and yard fittings", hideFlags = HideFlags.HideAndDontSave, enableInstancing = true
                };
                fittings.SetTexture("_BaseMap", Texture("DogParkPalette"));
                fittings.SetTexture("_SceneMaterialResponse", Texture("DogParkFinishResponse"));
                fittings.SetFloat("_SceneResponseStrength", 1f);
                fittings.SetFloat("_SceneNormalStrength", .45f);
                return fittings;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResources()
        {
            Array.Clear(Templates, 0, Templates.Length);
            if (fittings) UnityEngine.Object.Destroy(fittings);
            fittings = null;
            soil = soilResponse = null;
        }
    }
}
