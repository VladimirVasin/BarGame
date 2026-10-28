using UnityEngine;

namespace BarPromenade
{
    internal static class StairwellAuthoredVisualFactory
    {
        public static GameObject CreateBox(string name, Transform parent, Vector3 position,
            Vector3 size, Color color, bool collider = true) =>
            Create(name, parent, position, size, color, collider, "Box", null);

        public static GameObject CreateBox(string name, Transform parent, Vector3 position,
            Vector3 size, Color color, Material material, bool collider = true) =>
            Create(name, parent, position, size, color, collider, "Box", material);

        public static GameObject CreateCylinder(string name, Transform parent, Vector3 position,
            Vector3 size, Color color, bool collider = true) =>
            Create(name, parent, position, size, color, collider, "Cylinder", null);

        public static GameObject CreateAnchoredPart(string name, Transform parent, Vector3 origin,
            Quaternion rotation, Color color)
        {
            StairwellAuthoredPart part = StairwellInteriorModelLibrary.Load().UniqueBinding(name, "Box");
            GameObject result = CreateBox(name, parent,
                origin + rotation * StairwellAuthoredPart.Vector(part.position), part.Size, color, false);
            result.transform.localRotation = rotation;
            return result;
        }

        private static GameObject Create(string name, Transform parent, Vector3 position,
            Vector3 size, Color color, bool collider, string kind, Material material)
        {
            StairwellAuthoredPart part = StairwellInteriorModelLibrary.Load().Binding(name, size, kind);
            var result = new GameObject(name);
            result.transform.SetParent(parent, false);
            result.transform.localPosition = position;
            result.transform.localScale = size;
            result.AddComponent<MeshFilter>().sharedMesh = part.mesh;
            MeshRenderer renderer = result.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material != null ? material : RuntimePrimitiveFactory.DefaultMaterial;
            RuntimePrimitiveFactory.SetColor(renderer, color);
            if (collider)
            {
                // Keep the original authoritative collision envelope, even
                // where authored rail/door/grille geometry is now open.
                if (kind == "Cylinder")
                {
                    CapsuleCollider proxy = result.AddComponent<CapsuleCollider>();
                    proxy.center = Vector3.zero;
                    proxy.radius = .5f;
                    proxy.height = 2f;
                }
                else
                {
                    BoxCollider proxy = result.AddComponent<BoxCollider>();
                    proxy.center = Vector3.zero;
                    proxy.size = Vector3.one;
                }
            }
            return result;
        }
    }
}
