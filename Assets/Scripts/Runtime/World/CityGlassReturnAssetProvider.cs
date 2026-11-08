using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Fixed-metre Blender shells and their shared City surface bindings.</summary>
    public static class CityGlassReturnAssetProvider
    {
        public const string PavilionResource = "City/GlassReturn/Pavilion";
        public const string YardResource = "City/GlassReturn/Yard";
        public const string FloodlightResource = "City/GlassReturn/WallFloodlight";
        private static GameObject pavilion, yard, floodlight;
        private static Material brick, clearGlass;
        private static Texture2D brickPigment, brickResponse;
        private static readonly int Pigment = Shader.PropertyToID("_BaseMap");
        private static readonly int Response = Shader.PropertyToID("_SceneMaterialResponse");
        private static readonly int Tint = Shader.PropertyToID("_BaseColor");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");

        public static GameObject CreatePavilion(Transform parent) =>
            Create(parent, "Glass collection pavilion", "MOVE_Bottle", PavilionResource, ref pavilion);

        public static GameObject CreateYard(Transform parent) =>
            Create(parent, "Glass sorting yard", null, YardResource, ref yard);

        public static GameObject CreateFloodlight(Transform parent) =>
            Create(parent, "Glass return wall floodlight", "MOVE_Head", FloodlightResource, ref floodlight);

        private static GameObject Create(Transform parent, string name, string pivotName, string key, ref GameObject template)
        {
            if (template == null) template = Require<GameObject>(key);
            var root = new GameObject(name);
            bool building = key == PavilionResource;
            root.transform.SetParent(parent, false);
            // Preserve all importer axes and scales under the metre placement root.
            GameObject model = UnityEngine.Object.Instantiate(template, root.transform, false);
            var properties = new MaterialPropertyBlock();
            foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                string meshName = renderer.name;
                if (meshName == "LampLens" || meshName == "FloodlightLens")
                {
                    renderer.name = "LampLens";
                    renderer.sharedMaterial = CityNightResources.EmissiveMaterial;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    continue;
                }
                int split = meshName.LastIndexOf("__", StringComparison.Ordinal);
                if (split < 0) throw new InvalidOperationException("Unclassified glass return surface " + meshName);
                string role = meshName.Substring(split + 2);
                properties.Clear();
                if (role == "Brick")
                {
                    if (brick == null) brick = Require<Material>("Materials/SupermarketPhysicalSurface");
                    if (brickPigment == null) brickPigment = Require<Texture2D>("City/Surfaces/SupermarketBrickAlbedo");
                    if (brickResponse == null) brickResponse = Require<Texture2D>("City/Surfaces/SupermarketBrickResponse");
                    renderer.sharedMaterial = brick;
                    properties.SetTexture(Pigment, brickPigment);
                    properties.SetTexture(Response, brickResponse);
                    properties.SetFloat(Smoothness, .13f);
                    properties.SetFloat("_SceneResponseStrength", 1f);
                    properties.SetFloat("_SceneNormalStrength", .42f);
                    properties.SetVector("_BaseMap_ST", new Vector4(1.6f, 1.6f, 0f, 0f));
                    renderer.SetPropertyBlock(properties);
                }
                else if (role.StartsWith("Glass", StringComparison.Ordinal))
                {
                    // Only three clear bottles next to the actual window need
                    // transmission. Distant crate rows use one opaque batch per colour.
                    bool transparent = building && role == "GlassClear";
                    if (transparent && clearGlass == null) clearGlass = Require<Material>("Pawnshop/Materials/PawnshopGlass");
                    renderer.sharedMaterial = transparent ? clearGlass : RuntimePrimitiveFactory.DefaultMaterial;
                    Color color = role == "GlassBrown" ? new Color(.31f,.19f,.09f) :
                        role == "GlassClear" ? new Color(.55f,.63f,.60f,transparent ? .32f : 1f) :
                        new Color(.19f,.31f,.21f);
                    properties.SetColor(Tint, color.linear);
                    properties.SetFloat(Smoothness, .53f);
                    properties.SetFloat("_Metallic", 0f);
                    renderer.SetPropertyBlock(properties);
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                }
                else
                {
                    renderer.sharedMaterial = CityPortAssetProvider.GetSurfaceMaterial(role);
                    if (role == "Concrete") GroundAuthoredSurfaceAppearance.Apply(renderer,
                        GroundSurfaceKind.Concrete, cityClimate: !building);
                }
            }
            if (pivotName != null)
            {
                // Keep the imported object's world geometry, including its FBX unit
                // conversion, below a scale-free Unity pivot for hand or head rotation.
                Transform imported = FindPart(model.transform, pivotName);
                imported.name = "Authored " + pivotName;
                var pivot = new GameObject(pivotName).transform;
                pivot.SetParent(root.transform, false);
                pivot.position = imported.position;
                imported.SetParent(pivot, true);
                if (key == FloodlightResource)
                {
                    Transform source = FindPart(imported, "ANCHOR_Source");
                    source.SetParent(pivot, true);
                    source.localRotation = Quaternion.identity;
                    source.localScale = Vector3.one;
                }
            }
            return root;
        }

        public static Transform FindPart(Transform root, string name)
        {
            foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                if (part.name == name) return part;
            throw new InvalidOperationException("Glass return model is missing " + name);
        }

        private static T Require<T>(string path) where T : UnityEngine.Object
        {
            T asset = Resources.Load<T>(path);
            return asset != null ? asset : throw new InvalidOperationException("Missing glass return resource " + path);
        }
    }
}
