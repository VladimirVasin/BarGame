using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Shares the generated workshop atlas, clear glazing and metric collision manifest.</summary>
    public static class CityCobblerAssetProvider
    {
        public const string ResourcePath = "City/Cobbler/CobblerBus";
        private static GameObject template;
        private static Material paint, glass;
        private static Manifest geometry;

        [Serializable]
        internal sealed class MetricBox
        {
            public string name;
            public float[] center, size;
            internal Bounds MetricBounds => new(Vector(center), Vector(size));
        }

        [Serializable]
        internal sealed class Manifest
        {
            public float[] bodySize;
            public MetricBox[] boxes;
            internal Vector3 BodySize => Vector(bodySize);
        }

        private static Vector3 Vector(float[] xyz)
        {
            if (xyz == null || xyz.Length != 3) throw new InvalidOperationException("Invalid cobbler metric vector.");
            return new Vector3(xyz[0], xyz[1], xyz[2]);
        }

        internal static Manifest Geometry
        {
            get
            {
                if (geometry != null) return geometry;
                var manifest = JsonUtility.FromJson<Manifest>(Require<TextAsset>(ResourcePath).text);
                if (manifest?.boxes == null || manifest.boxes.Length == 0)
                    throw new InvalidOperationException("Missing cobbler collision manifest.");
                return geometry = manifest;
            }
        }

        internal static Transform Create(Transform root, bool interactive, out Collider roof)
        {
            if (!template) template = Require<GameObject>(ResourcePath);
            if (!paint) paint = Require<Material>("City/Cobbler/CobblerSurface");
            if (!glass) glass = Require<Material>("Pawnshop/Materials/PawnshopGlass");
            roof = null;
            // The FBX authoring root carries the unit and axis conversion. Keep it intact.
            Transform shell = UnityEngine.Object.Instantiate(template, root, false).transform;
            foreach (MeshRenderer renderer in shell.GetComponentsInChildren<MeshRenderer>(true))
            {
                string part = renderer.name;
                if (part.StartsWith("COL_", StringComparison.Ordinal))
                {
                    renderer.enabled = false;
                    if (!interactive) continue;
                    var bounds = renderer.GetComponent<MeshFilter>().sharedMesh.bounds;
                    var solid = renderer.gameObject.AddComponent<BoxCollider>();
                    solid.center = bounds.center; solid.size = bounds.size;
                    if (CityCobblerSitePlan.IsFloor(part)) FootstepGround.Stamp(solid.gameObject, FootstepGroundKind.Metal);
                    if (part == "COL_Roof") roof = solid;
                }
                else if (part.EndsWith("__Glass", StringComparison.Ordinal))
                {
                    renderer.sharedMaterial = glass;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                }
                else if (part.EndsWith("__Lens", StringComparison.Ordinal) || part == "SignLight__SignLens")
                {
                    renderer.sharedMaterial = CityNightResources.EmissiveMaterial;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    if (part == "SignLight__SignLens")
                        CityNightGlowRegistry.Register(renderer, new Color(.75f, .68f, .56f));
                }
                else if (part.EndsWith("__Paint", StringComparison.Ordinal)) renderer.sharedMaterial = paint;
                else throw new InvalidOperationException("Unclassified cobbler workshop mesh " + part);
            }
            if (interactive && !roof) throw new InvalidOperationException("The cobbler workshop requires a solid roof.");
            return shell;
        }

        private static T Require<T>(string path) where T : UnityEngine.Object
        {
            var asset = Resources.Load<T>(path);
            return asset ? asset : throw new InvalidOperationException("Missing cobbler resource " + path);
        }

        public static Transform FindPart(Transform shell, string name) =>
            CityPedestrianHandProps.FindSocket(shell, name) ??
            throw new InvalidOperationException("Missing cobbler model part " + name);
    }
}
