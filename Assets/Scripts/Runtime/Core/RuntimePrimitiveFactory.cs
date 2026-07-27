using UnityEngine;

namespace BarPromenade
{
    public static class RuntimePrimitiveFactory
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public static GameObject CreateBox(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color,
            bool collider = true)
        {
            return CreatePrimitive(
                PrimitiveType.Cube,
                name,
                parent,
                localPosition,
                size,
                color,
                collider,
                null);
        }

        public static GameObject CreateBox(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color,
            Material sharedMaterial,
            bool collider = true)
        {
            return CreatePrimitive(
                PrimitiveType.Cube,
                name,
                parent,
                localPosition,
                size,
                color,
                collider,
                sharedMaterial);
        }

        public static GameObject CreateCylinder(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color,
            bool collider = true)
        {
            return CreatePrimitive(
                PrimitiveType.Cylinder,
                name,
                parent,
                localPosition,
                size,
                color,
                collider,
                null);
        }

        public static GameObject CreateCylinder(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color,
            Material sharedMaterial,
            bool collider = true)
        {
            return CreatePrimitive(
                PrimitiveType.Cylinder,
                name,
                parent,
                localPosition,
                size,
                color,
                collider,
                sharedMaterial);
        }

        public static void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetColor(BaseColorId, color);
            properties.SetColor(ColorId, color);
            renderer.SetPropertyBlock(properties);
        }

        private static GameObject CreatePrimitive(
            PrimitiveType type,
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color,
            bool collider,
            Material sharedMaterial)
        {
            GameObject result = GameObject.CreatePrimitive(type);
            result.name = name;
            result.transform.SetParent(parent, false);
            result.transform.localPosition = localPosition;
            result.transform.localScale = size;
            Renderer renderer = result.GetComponent<Renderer>();
            if (sharedMaterial != null)
            {
                renderer.sharedMaterial = sharedMaterial;
            }

            SetColor(renderer, color);

            if (!collider)
            {
                Collider primitiveCollider = result.GetComponent<Collider>();
                if (primitiveCollider != null)
                {
                    Object.Destroy(primitiveCollider);
                }
            }

            return result;
        }
    }
}
