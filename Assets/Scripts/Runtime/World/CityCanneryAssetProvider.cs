using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Authored metre-space cannery kit sharing the port's material
    /// instances. Imported FBX correction remains intact under a unit wrapper.</summary>
    public static class CityCanneryAssetProvider
    {
        public const string ResourceFolder = "City/Cannery/";
        public static readonly string[] ModelNames =
            { "Hall", "Equipment", "Truck", "Pallet", "RetortBasket", "CanTray", "CartonStack", "Trolley", "Yard" };
        private static readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>();
        private static Material glassMaterial;

        public static GameObject Create(string name, Transform parent)
        {
            if (Array.IndexOf(ModelNames, name) < 0)
                throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown cannery model.");
            if (!Templates.TryGetValue(name, out GameObject template) || template == null)
            {
                template = Resources.Load<GameObject>(ResourceFolder + name);
                if (template == null) throw new InvalidOperationException("Missing cannery model: " + name);
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
                    MeshCollider collider = renderer.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    continue;
                }
                bool lamp = renderer.name == "CanneryLampGlass";
                bool glass = !lamp && renderer.name.EndsWith("Glass", StringComparison.Ordinal);
                int separator = renderer.name.LastIndexOf("__", StringComparison.Ordinal);
                string role = separator < 0 ? "Plain" : renderer.name.Substring(separator + 2);
                renderer.sharedMaterial = lamp ? CityNightResources.EmissiveMaterial :
                    glass ? GlassMaterial : CityPortAssetProvider.GetSurfaceMaterial(role);
                renderer.shadowCastingMode = glass || lamp ? ShadowCastingMode.Off : ShadowCastingMode.On;
                renderer.receiveShadows = !glass && !lamp;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (lamp) CityNightGlowRegistry.Register(renderer, new Color(1f, .72f, .47f) * 2.5f);
            }
            return wrapper;
        }

        public static Transform FindPart(GameObject model, string name)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
                if (string.Equals(part.name, name, StringComparison.Ordinal)) return part;
            throw new InvalidOperationException($"Cannery model '{model.name}' has no '{name}' part.");
        }

        /// <summary>Fits the authored driveway and its collider to the road.
        /// Yard-local metres also work for the transformed home exterior view.</summary>
        public static void ConfigureYard(GameObject yard, CityCanneryPlan plan)
        {
            if (yard == null) throw new ArgumentNullException(nameof(yard));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            MeshFilter[] authoredFilters = Templates["Yard"].GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter filter in yard.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.name.StartsWith("YardDriveway__", StringComparison.Ordinal) &&
                    !filter.name.StartsWith("COL_Yard9", StringComparison.Ordinal)) continue;
                Mesh mesh = filter.sharedMesh;
                RuntimeGeneratedMeshOwner owner = filter.GetComponent<RuntimeGeneratedMeshOwner>();
                if (owner == null)
                {
                    mesh = UnityEngine.Object.Instantiate(mesh);
                    mesh.name = filter.sharedMesh.name + " Road Profile";
                    mesh.hideFlags = HideFlags.DontSave;
                    owner = filter.gameObject.AddComponent<RuntimeGeneratedMeshOwner>();
                    owner.Initialize(mesh);
                    filter.sharedMesh = mesh;
                }
                Matrix4x4 toYard = yard.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Matrix4x4 fromYard = toYard.inverse;
                Mesh authored = Array.Find(authoredFilters, item => item.name == filter.name).sharedMesh;
                Vector3[] vertices = authored.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 point = toYard.MultiplyPoint3x4(vertices[i]);
                    // Only two authored levels exist. Preserve its 20 cm solid
                    // thickness while setting an absolute, repeatable profile.
                    float depth = point.y >= 0f ? 0f : .2f;
                    point.y = plan.ApronTop(point.x, point.z) - plan.Origin.y - depth;
                    vertices[i] = fromYard.MultiplyPoint3x4(point);
                }
                mesh.vertices = vertices;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                MeshCollider collider = filter.GetComponent<MeshCollider>();
                if (collider != null)
                {
                    collider.sharedMesh = null;
                    collider.sharedMesh = mesh;
                }
            }
        }

        private static Material GlassMaterial
        {
            get
            {
                if (glassMaterial != null) return glassMaterial;
                glassMaterial = new Material(CityPortAssetProvider.OpaqueMaterial)
                {
                    name = "Cannery Window Glass Shared", hideFlags = HideFlags.HideAndDontSave,
                    renderQueue = (int)RenderQueue.Transparent
                };
                glassMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, .10f));
                glassMaterial.SetFloat("_Smoothness", .40f);
                glassMaterial.SetFloat("_Surface", 1f);
                glassMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                glassMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                glassMaterial.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                glassMaterial.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                glassMaterial.SetFloat("_ZWrite", 0f);
                glassMaterial.SetFloat("_Cull", (float)CullMode.Off);
                glassMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                return glassMaterial;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResources()
        {
            Templates.Clear();
            if (glassMaterial != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(glassMaterial);
                else UnityEngine.Object.DestroyImmediate(glassMaterial);
            }
            glassMaterial = null;
        }
    }
}
