using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public enum CityResidentialCourtyardPropKind
    {
        Bench,
        Planter,
        UtilityShelter,
        EntranceCanopy,
        Drain
    }

    /// <summary>Bounded passive Blender models; all placement and behaviour belongs to the court plan.</summary>
    public static class CityResidentialCourtyardAssetProvider
    {
        public const string ResourceFolder = "City/ResidentialCourtyard/";
        public const int PropKindCount = 5;
        public const string EntranceLightAnchorName = "IndustrialTaskLight_CourtyardEntrance";
        public static readonly Vector3 BenchSeatLocalPosition = new Vector3(0f, .46f, .05f);
        public static readonly Vector3 BenchDockLocalPosition = new Vector3(0f, 0f, .85f);
        public static readonly Vector3 ShelfBasketLocalPosition = new Vector3(-.67f, .85f, .25f);
        public static readonly Vector3 ShelfBasketEndLocalPosition = new Vector3(.67f, .85f, .25f);
        public static readonly Vector3 ServiceStandLocalPosition = new Vector3(-.67f, 0f, .85f);
        public static readonly Vector3 ServiceWaypointLocalPosition = new Vector3(0f, 0f, 1.25f);
        public static readonly Vector3 ServiceEndLocalPosition = new Vector3(.67f, 0f, .85f);
        public static readonly Vector3 BasketLeftGripLocalPosition = new Vector3(-.28f, .235f, 0f);
        public static readonly Vector3 BasketRightGripLocalPosition = new Vector3(.28f, .235f, 0f);
        public static readonly Vector3 EntranceLightLocalPosition = new Vector3(0f, 2.68f, .38f);

        private static readonly string[] Names =
        {
            "CourtyardBench", "CourtyardPlanter", "CourtyardUtilityShelter",
            "CourtyardEntranceCanopy", "CourtyardDrain", "CourtyardBasket"
        };
        private static readonly GameObject[] Templates = new GameObject[PropKindCount + 1];

        public static GameObject Create(Transform parent, CityResidentialCourtyardPropKind kind,
            Vector3 position, Quaternion rotation) => CreateModel(parent, ValidateKind(kind), position, rotation);

        public static GameObject CreateBasket(Transform parent, Vector3 position, Quaternion rotation)
            => CreateModel(parent, PropKindCount, position, rotation);

        /// <summary>Measured source bounds in metres, including the overhanging canopy and drainpipe.</summary>
        public static Bounds GetLocalBounds(CityResidentialCourtyardPropKind kind)
        {
            switch (kind)
            {
                case CityResidentialCourtyardPropKind.Bench:
                    return new Bounds(new Vector3(0f, .45f, 0f), new Vector3(1.90f, .90f, .70f));
                case CityResidentialCourtyardPropKind.Planter:
                    return new Bounds(new Vector3(0f, .225f, 0f), new Vector3(1.30f, .45f, .65f));
                case CityResidentialCourtyardPropKind.UtilityShelter:
                    return new Bounds(new Vector3(0f, 1.075f, 0f), new Vector3(2.50f, 2.15f, 1.25f));
                case CityResidentialCourtyardPropKind.EntranceCanopy:
                    return new Bounds(new Vector3(0f, 1.49775f, .52f), new Vector3(2.30f, 2.9045f, 1.16f));
                case CityResidentialCourtyardPropKind.Drain:
                    return new Bounds(new Vector3(0f, .0125f, 0f), new Vector3(.45f, .025f, .65f));
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public static Vector3 GetSize(CityResidentialCourtyardPropKind kind) => GetLocalBounds(kind).size;

        public static Rect GetFootprint(CityResidentialCourtyardPropKind kind)
        {
            Bounds bounds = GetLocalBounds(kind);
            return Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);
        }

        public static Transform FindPart(Transform root, string name)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                if (part.name == name) return part;
            throw new InvalidOperationException("Courtyard authored part is missing: " + name);
        }

        private static int ValidateKind(CityResidentialCourtyardPropKind kind)
        {
            int index = (int)kind;
            if (index < 0 || index >= PropKindCount) throw new ArgumentOutOfRangeException(nameof(kind));
            return index;
        }

        private static GameObject CreateModel(Transform parent, int index, Vector3 position, Quaternion rotation)
        {
            if (Templates[index] == null) Templates[index] = Resources.Load<GameObject>(ResourceFolder + Names[index]);
            if (Templates[index] == null) throw new InvalidOperationException("Missing authored courtyard model: " + Names[index]);
            var root = new GameObject(Names[index]);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, rotation);
            // Retain FBX's source wrapper and its unit factor. The outer root
            // gives all placements and the carried basket ordinary Unity axes.
            GameObject model = UnityEngine.Object.Instantiate(Templates[index], root.transform, false);
            foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                int split = renderer.name.LastIndexOf("__", StringComparison.Ordinal);
                if (split < 0) throw new InvalidOperationException("Courtyard mesh lacks a semantic surface: " + renderer.name);
                string role = renderer.name.Substring(split + 2);
                bool lens = role == "Lens";
                renderer.sharedMaterial = lens ? CityNightResources.EmissiveMaterial :
                    CityPortAssetProvider.GetSurfaceMaterial(role == "Soil" || role == "Foliage" ? "Plain" : role);
                renderer.shadowCastingMode = lens ? ShadowCastingMode.Off : ShadowCastingMode.On;
                renderer.receiveShadows = !lens;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (lens) CityNightGlowRegistry.Register(renderer, new Color(.77f, .91f, .88f));
                else
                {
                    if (role == "Soil") CityExteriorAppearance.ApplyGroundSurface(renderer, new Color(.32f, .30f, .25f));
                    else if (role == "Foliage") CityParkSurfaceAppearance.ApplyCombined(renderer,
                        CityParkSurfaceKind.Foliage, new Color(.25f, .32f, .23f));
                    CitySurfaceLibrary.ApplyRenderer(renderer);
                }
            }
            if (index == (int)CityResidentialCourtyardPropKind.EntranceCanopy)
            {
                Transform source = FindPart(root.transform, EntranceLightAnchorName);
                source.rotation = rotation * Quaternion.LookRotation(Vector3.down, Vector3.forward);
            }
            return root;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResources() => Array.Clear(Templates, 0, Templates.Length);
    }
}
