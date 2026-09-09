using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Fixed-metre Blender kit. The unit wrapper owns placement; imported
    /// FBX unit and axis corrections remain intact on its child hierarchy.</summary>
    public static class CityPortAssetProvider
    {
        public const string ResourceFolder = "City/Port/";
        public static readonly string[] ModelNames =
            { "Dock", "Trawler", "CraneBase", "CraneBoom", "Hook", "Cargo", "Trolley", "RopeSegment", "AccessRoad" };
        private static readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>();
        private static Material opaqueMaterial;
        private static Material glassMaterial;
        private static readonly Dictionary<string, Material> SurfaceMaterials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, SurfaceDefinition> Surfaces = new Dictionary<string, SurfaceDefinition>
        {
            { "Concrete", new SurfaceDefinition("PortConcreteAlbedo", new Color(.42f,.43f,.405f)) },
            { "ConcreteWall", new SurfaceDefinition("PortConcreteWallAlbedo", new Color(.36f,.39f,.37f)) },
            { "Steel", new SurfaceDefinition("PortPaintedSteelAlbedo", new Color(.285f,.385f,.37f)) },
            { "SteelDark", new SurfaceDefinition("PortPaintedSteelAlbedo", new Color(.235f,.275f,.26f)) },
            { "SteelLight", new SurfaceDefinition("PortPaintedSteelAlbedo", new Color(.60f,.615f,.57f)) },
            { "SteelRust", new SurfaceDefinition("PortPaintedSteelAlbedo", new Color(.34f,.245f,.20f)) },
            { "Plaster", new SurfaceDefinition("PortWarehousePlasterAlbedo", new Color(.60f,.615f,.57f)) },
            { "Timber", new SurfaceDefinition("PortTimberAlbedo", new Color(.32f,.28f,.23f)) },
            { "Roof", new SurfaceDefinition("PortRoofMetalAlbedo", new Color(.235f,.275f,.26f)) },
            { "Deck", new SurfaceDefinition("PortDeckAlbedo", new Color(.27f,.31f,.30f)) },
            { "Tare", new SurfaceDefinition("PortTareAlbedo", new Color(.285f,.385f,.37f)) },
            { "Fish", new SurfaceDefinition("PortFishAlbedo", new Color(.59f,.65f,.62f)) },
            { "Ice", new SurfaceDefinition("PortIceAlbedo", new Color(.78f,.82f,.80f)) },
            { "Rubber", new SurfaceDefinition("PortRubberAlbedo", new Color(.095f,.12f,.125f)) },
            { "Fabric", new SurfaceDefinition("PortFabricAlbedo", new Color(.31f,.35f,.33f)) },
            { "Asphalt", new SurfaceDefinition("PortAsphaltAlbedo", new Color(.25f,.27f,.27f)) },
            { "RoadMarking", new SurfaceDefinition("CityRoadMarkingAlbedo", new Color(.66f,.65f,.59f)) }
        };

        public static GameObject Create(string name, Transform parent)
        {
            if (Array.IndexOf(ModelNames, name) < 0)
                throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown port model.");
            if (!Templates.TryGetValue(name, out GameObject template) || template == null)
            {
                template = Resources.Load<GameObject>(ResourceFolder + name);
                if (template == null) throw new InvalidOperationException("Missing port model: " + name);
                Templates[name] = template;
            }
            var wrapper = new GameObject(name);
            wrapper.transform.SetParent(parent, false);
            GameObject model = UnityEngine.Object.Instantiate(template, wrapper.transform, false);
            foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool collision = renderer.name.StartsWith("COL_", StringComparison.Ordinal);
                renderer.enabled = !collision;
                if (collision)
                {
                    var collider = renderer.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    continue;
                }
                int regionSeparator = renderer.name.LastIndexOf("__", StringComparison.Ordinal);
                string region = regionSeparator >= 0 ? renderer.name.Substring(regionSeparator + 2) : "Plain";
                renderer.sharedMaterial = renderer.name == "CabinGlass" ? GlassMaterial : GetSurfaceMaterial(region);
                renderer.shadowCastingMode = renderer.name == "CabinGlass" ? ShadowCastingMode.Off : ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (region == "Asphalt") CityExteriorAppearance.ApplyRoadSurface(renderer);
            }
            return wrapper;
        }

        public static Transform FindPart(GameObject model, string name)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
                if (string.Equals(part.name, name, StringComparison.Ordinal)) return part;
            throw new InvalidOperationException($"Port model '{model.name}' has no '{name}' part.");
        }

        /// <summary>One shared material for each authored metre-UV surface.
        /// Missing generated sheets have an explicit flat-color fallback;
        /// their absence never silently reuses the palette's point UVs.</summary>
        public static Material GetSurfaceMaterial(string role)
        {
            if (role == "Plain") return OpaqueMaterial;
            if (!Surfaces.TryGetValue(role, out SurfaceDefinition surface))
                throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown port surface region.");
            if (SurfaceMaterials.TryGetValue(role, out Material shared) && shared != null) return shared;
            // The service branch shares the city's actual asphalt source.
            Texture2D texture = Resources.Load<Texture2D>(role == "Asphalt"
                ? CityExteriorAppearance.RoadTextureResourcePath
                : role == "RoadMarking" ? CityExteriorAppearance.RoadMarkingTextureResourcePath
                    : ResourceFolder + "Textures/" + surface.TextureName);
            Shader shader = Resources.Load<Shader>("Shaders/Ps1Lit");
            if (shader == null) throw new InvalidOperationException("Missing port PS1 Lit shader.");
            shared = new Material(shader)
            {
                name = "Port " + role + (texture != null ? " Shared" : " Shared Missing-Texture Fallback"),
                hideFlags = HideFlags.HideAndDontSave
            };
            shared.SetTexture("_BaseMap", texture != null ? texture : Texture2D.whiteTexture);
            shared.SetColor("_BaseColor", texture != null ? SurfaceTint(role).linear : surface.Fallback.linear);
            shared.SetFloat("_Smoothness", role == "Fish" || role == "Ice" ? .42f : role == "Steel" ? .23f : .10f);
            shared.SetFloat("_Metallic", role.StartsWith("Steel", StringComparison.Ordinal) || role == "Roof" ? .16f : 0f);
            SurfaceMaterials[role] = shared;
            return shared;
        }

        private static Color SurfaceTint(string role)
        {
            // Neutral generated albedo is deliberately tinted in one shared
            // owner. These visual sRGB values preserve the muted painted hull,
            // pale cabin and darker machinery instead of washing them white.
            switch (role)
            {
                case "Concrete": return new Color(.67f,.71f,.70f);
                case "ConcreteWall": return new Color(.68f,.75f,.72f);
                case "Steel": return new Color(.44f,.61f,.59f);
                case "SteelDark": return new Color(.38f,.46f,.43f);
                case "SteelLight": return new Color(.84f,.88f,.82f);
                case "SteelRust": return new Color(.59f,.44f,.35f);
                case "Plaster": return new Color(.85f,.90f,.84f);
                case "Timber": return new Color(.74f,.65f,.55f);
                case "Roof": return new Color(.57f,.66f,.64f);
                case "Deck": return new Color(.60f,.68f,.66f);
                case "Tare": return new Color(.54f,.72f,.69f);
                case "Rubber": return new Color(.27f,.32f,.32f);
                case "Fabric": return new Color(.64f,.71f,.68f);
                case "Asphalt": return Color.white;
                case "RoadMarking": return new Color(.78f,.77f,.71f);
                default: return Color.white;
            }
        }

        public static Material OpaqueMaterial
        {
            get
            {
                if (opaqueMaterial == null)
                {
                    Shader shader = Resources.Load<Shader>("Shaders/Ps1Lit");
                    Texture2D palette = Resources.Load<Texture2D>(ResourceFolder + "PortPalette");
                    if (shader == null) throw new InvalidOperationException("Port requires packaged Shaders/Ps1Lit.");
                    if (palette == null) throw new InvalidOperationException("Port requires City/Port/PortPalette.");
                    opaqueMaterial = new Material(shader)
                    {
                        name = "Port Painted Surfaces Shared", hideFlags = HideFlags.HideAndDontSave
                    };
                    opaqueMaterial.SetTexture("_BaseMap", palette);
                    opaqueMaterial.SetColor("_BaseColor", Color.white);
                    opaqueMaterial.SetFloat("_Smoothness", .21f);
                    opaqueMaterial.SetFloat("_Metallic", .10f);
                }
                return opaqueMaterial;
            }
        }

        private static Material GlassMaterial
        {
            get
            {
                if (glassMaterial == null)
                {
                    glassMaterial = new Material(OpaqueMaterial)
                    {
                        name = "Port Wheelhouse Glass Shared", hideFlags = HideFlags.HideAndDontSave,
                        renderQueue = (int)RenderQueue.Transparent
                    };
                    glassMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, .18f));
                    glassMaterial.SetFloat("_Smoothness", .55f);
                    glassMaterial.SetFloat("_Surface", 1f);
                    glassMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    glassMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    glassMaterial.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                    glassMaterial.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                    glassMaterial.SetFloat("_ZWrite", 0f);
                    glassMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                }
                return glassMaterial;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResources()
        {
            Templates.Clear();
            DestroyMaterial(opaqueMaterial); DestroyMaterial(glassMaterial);
            foreach (Material material in SurfaceMaterials.Values) DestroyMaterial(material);
            SurfaceMaterials.Clear();
            opaqueMaterial = null; glassMaterial = null;
        }

        private readonly struct SurfaceDefinition
        {
            internal readonly string TextureName;
            internal readonly Color Fallback;
            internal SurfaceDefinition(string textureName, Color fallback)
            { TextureName = textureName; Fallback = fallback; }
        }

        private static void DestroyMaterial(Material material)
        {
            if (material == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(material);
            else UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
