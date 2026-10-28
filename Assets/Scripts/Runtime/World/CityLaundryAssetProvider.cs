using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>One passive, metre-authored laundry; shared City surfaces and retained FBX units.</summary>
    public static class CityLaundryAssetProvider
    {
        public const string ResourcePath = "City/Laundry/StaticLaundry";
        private static GameObject template;
        private static Material glass, sign;

        public static GameObject Create(Transform parent) => CreateModel(parent, true);
        public static GameObject CreateStatic(Transform parent) => CreateModel(parent, false);

        private static GameObject CreateModel(Transform parent, bool collision)
        {
            if (template == null) template = Resources.Load<GameObject>(ResourcePath);
            if (template == null) throw new InvalidOperationException("Missing authored City laundry.");
            var root = new GameObject("Household Laundry");
            root.transform.SetParent(parent, false);
            GameObject model = UnityEngine.Object.Instantiate(template, root.transform, false);
            foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool solid = renderer.name.StartsWith("COL_", StringComparison.Ordinal);
                renderer.enabled = !solid;
                if (solid)
                {
                    if (collision) renderer.gameObject.AddComponent<MeshCollider>().sharedMesh =
                        renderer.GetComponent<MeshFilter>().sharedMesh;
                    string surface = renderer.name == "COL_Floor" ? "Concrete" :
                        renderer.name == "COL_Walls" ? "Plaster" : renderer.name == "COL_Furniture" ? "Timber" :
                        renderer.name == "COL_Machines" ? "SteelLight" : renderer.name == "COL_Canopy" ? "Roof" : "Steel";
                    renderer.sharedMaterial = CityPortAssetProvider.GetSurfaceMaterial(surface);
                    if (collision && renderer.name == "COL_Floor") FootstepGround.Stamp(renderer, FootstepGroundKind.Concrete);
                    continue;
                }
                int separator = renderer.name.LastIndexOf("__", StringComparison.Ordinal);
                string role = separator < 0 ? "Plain" : renderer.name.Substring(separator + 2);
                bool lens = renderer.name.StartsWith("LaundryLampLens", StringComparison.Ordinal);
                renderer.sharedMaterial = role == "Sign" ? Sign : role == "Glass" ? Glass : lens ? CityNightResources.EmissiveMaterial :
                    CityPortAssetProvider.GetSurfaceMaterial(role);
                renderer.shadowCastingMode = role == "Glass" || lens ? ShadowCastingMode.Off : ShadowCastingMode.On;
                renderer.receiveShadows = role != "Glass" && !lens;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (lens && collision) CityNightGlowRegistry.Register(renderer,
                    renderer.name.EndsWith("Work", StringComparison.Ordinal) ? new Color(3f, 2.31f, 1.44f) :
                    renderer.name.EndsWith("Entrance", StringComparison.Ordinal) ? new Color(.0255f, .0279f, .0264f) :
                    new Color(2.55f, 2.79f, 2.64f));
                if (renderer.name.StartsWith("LaundryFloor", StringComparison.Ordinal))
                {
                    // Both room and service bay are below actual roof meshes.
                    GroundAuthoredSurfaceAppearance.Apply(renderer, GroundSurfaceKind.Concrete, cityClimate: false);
                    if (collision) FootstepGround.Stamp(renderer, FootstepGroundKind.Concrete);
                }
            }
            return root;
        }

        public static Transform FindPart(Transform root, string name)
        {
            foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                if (part.name == name) return part;
            throw new InvalidOperationException("Laundry part is missing: " + name);
        }

        private static Material Glass
        {
            get
            {
                if (glass != null) return glass;
                glass = new Material(CityPortAssetProvider.GetSurfaceMaterial("SteelLight"))
                {
                    name = "Laundry Muted Glass Shared", hideFlags = HideFlags.HideAndDontSave,
                    renderQueue = (int)RenderQueue.Transparent
                };
                glass.SetColor("_BaseColor", new Color(.72f, .78f, .74f, .14f));
                glass.SetFloat("_Surface", 1f);
                glass.SetFloat("_Smoothness", .3f);
                glass.SetFloat("_Metallic", 0f);
                glass.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                glass.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                glass.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                glass.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                glass.SetFloat("_ZWrite", 0f);
                glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                return glass;
            }
        }

        private static Material Sign
        {
            get
            {
                if (sign != null) return sign;
                Texture2D image = Resources.Load<Texture2D>("City/Laundry/LaundrySign");
                Texture2D response = Resources.Load<Texture2D>("City/Surfaces/PortPaintedSteelResponse");
                Shader shader = Resources.Load<Shader>("Shaders/CitySurfaceLit");
                if (image == null || response == null || shader == null)
                    throw new InvalidOperationException("Laundry enamel sign requires its pigment and shared physical response.");
                sign = new Material(shader)
                {
                    name = "Laundry Enamel Sign Shared", hideFlags = HideFlags.HideAndDontSave,
                    enableInstancing = true
                };
                sign.SetTexture("_BaseMap", image);
                sign.SetTexture("_SceneMaterialResponse", response);
                sign.SetColor("_BaseColor", Color.white);
                sign.SetFloat("_SceneResponseStrength", 1f);
                sign.SetFloat("_SceneNormalStrength", .2f);
                sign.SetFloat("_Metallic", .08f);
                sign.SetFloat("_Smoothness", .18f);
                return sign;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResources()
        {
            template = null;
            if (glass != null) UnityEngine.Object.Destroy(glass);
            if (sign != null) UnityEngine.Object.Destroy(sign);
            glass = null;
            sign = null;
        }
    }
}
