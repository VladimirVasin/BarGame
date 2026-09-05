using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Places imported shapes into the existing plan-owned interaction hierarchy.
    /// Only explicitly authored hardware profiles can be resized.</summary>
    internal static class HomeAuthoredVisualFactory
    {
        public static GameObject CreateBox(string name, Transform parent,
            Vector3 localPosition, Vector3 size, Color color, bool collider = true) =>
            CreateBox(name, parent, localPosition, size, color, null, collider);

        public static GameObject CreateBox(string name, Transform parent,
            Vector3 localPosition, Vector3 size, Color color, Material sharedMaterial,
            bool collider = true) =>
            Create(name, parent, localPosition, size, color, sharedMaterial, collider, "Box");

        public static GameObject CreateCylinder(string name, Transform parent,
            Vector3 localPosition, Vector3 size, Color color, bool collider = true) =>
            CreateCylinder(name, parent, localPosition, size, color, null, collider);

        public static GameObject CreateCylinder(string name, Transform parent,
            Vector3 localPosition, Vector3 size, Color color, Material sharedMaterial,
            bool collider = true) =>
            Create(name, parent, localPosition, new Vector3(size.x, size.y * 2f, size.z),
                color, sharedMaterial, collider, "Cylinder");

        private static GameObject Create(string name, Transform parent, Vector3 position,
            Vector3 size, Color color, Material material, bool collider, string primitiveKind)
        {
            HomeAuthoredPart part = HomeInteriorModelLibrary.Load().Binding(name, primitiveKind);
            if (!part.IsParametric && Vector3.Distance(part.Size, size) > 0.006f)
                throw new InvalidOperationException($"Authored Home part '{name}' has size {part.Size}; " +
                    $"the live layout requires {size}.");
            var result = Place(part, name, parent, position,
                part.IsParametric ? Divide(size, part.Size) : Vector3.one,
                material, part.IsParametric || material != null || part.sheet == "Plain" ? color : part.Tint);
            if (collider)
            {
                // Box proxy remains exactly the plan's size, independent of bevels and relief.
                BoxCollider proxy = result.AddComponent<BoxCollider>();
                proxy.size = Divide(size, result.transform.localScale);
            }
            return result;
        }

        internal static GameObject Place(HomeAuthoredPart part, string name, Transform parent,
            Vector3 position, Vector3 scale, Material material = null, Color? tint = null)
        {
            if (part.mesh == null) throw new InvalidOperationException($"Missing Home mesh '{part.name}'.");
            var result = new GameObject(name);
            result.transform.SetParent(parent, false);
            result.transform.localPosition = position;
            result.transform.localScale = scale;
            result.AddComponent<MeshFilter>().sharedMesh = part.mesh;
            MeshRenderer renderer = result.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material != null ? material : RuntimePrimitiveFactory.DefaultMaterial;
            RuntimePrimitiveFactory.SetColor(renderer, tint ?? part.Tint);
            if (material == null && Enum.TryParse(part.sheet, out HomeSurfaceKind surface))
                ApplySurface(renderer, part, surface, SurfaceProjection.BoxXZ, tint ?? part.Tint);
            parent.GetComponentInParent<HomeApartmentDressing>()?.Register(part, result);
            return result;
        }

        internal static void ApplySurface(Renderer renderer, HomeAuthoredPart part,
            HomeSurfaceKind kind, SurfaceProjection projection, Color color)
        {
            if (!part.IsParametric && Enum.TryParse(part.sheet, out HomeSurfaceKind authored)) kind = authored;
            HomeSurfaceAppearance.Apply(renderer, kind, projection,
                part.IsParametric || part.sheet == "Plain" ? color : part.Tint);
            if (!part.IsParametric)
            {
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                properties.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
                renderer.SetPropertyBlock(properties);
            }
            if (part.role != "decor")
                renderer.GetComponentInParent<HomeApartmentDressing>()?.RegisterSurface(renderer, kind);
        }

        private static Vector3 Divide(Vector3 value, Vector3 divisor) => new Vector3(
            value.x / Mathf.Max(0.000001f, divisor.x),
            value.y / Mathf.Max(0.000001f, divisor.y),
            value.z / Mathf.Max(0.000001f, divisor.z));
    }
}
