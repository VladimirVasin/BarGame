using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>One Blender-authored fabric, folded in the room and worn on the hero.</summary>
    public static class PlayerScarfResources
    {
        public const string ResourceFolder = "Player/Scarf/";
        public const string FoldedResourcePath = ResourceFolder + "ScarfFolded";
        public const string WornResourcePath = ResourceFolder + "ScarfWorn";
        public const string AtlasResourcePath = "MothersHouse/Textures/MothersHousePositiveAtlas";
        private static Material sharedMaterial;

        public static Material SharedMaterial
        {
            get
            {
                if (sharedMaterial != null) return sharedMaterial;
                Texture2D atlas = Resources.Load<Texture2D>(AtlasResourcePath);
                if (atlas == null) throw new InvalidOperationException("The scarf requires the mother's house cloth atlas.");
                sharedMaterial = new Material(RuntimePrimitiveFactory.DefaultMaterial)
                {
                    name = "Scarf Shared BookCloth",
                    hideFlags = HideFlags.HideAndDontSave
                };
                sharedMaterial.SetTexture("_BaseMap", atlas);
                // Compensate the blue BookCloth tile to obtain warm yellow
                // fabric while retaining its authored woven pattern.
                sharedMaterial.SetColor("_BaseColor", new Color(2f, 1f, .045f, 1f));
                sharedMaterial.SetFloat("_Cull", (float)CullMode.Off);
                sharedMaterial.SetFloat("_Smoothness", 0f);
                sharedMaterial.SetFloat("_Metallic", 0f);
                return sharedMaterial;
            }
        }

        public static GameObject CreateFolded(Transform parent)
        {
            GameObject container = new GameObject("Folded Scarf");
            container.transform.SetParent(parent, false);
            try
            {
                InstantiateModel(FoldedResourcePath, container.transform);
                return container;
            }
            catch
            {
                DestroyOwned(container);
                throw;
            }
        }

        internal static GameObject InstantiateModel(string resourcePath, Transform parent)
        {
            GameObject source = Resources.Load<GameObject>(resourcePath);
            if (source == null) throw new InvalidOperationException("Missing authored scarf: Resources/" + resourcePath);
            GameObject instance = UnityEngine.Object.Instantiate(source, parent, false);
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = SharedMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            }
            foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            return instance;
        }

        internal static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() { sharedMaterial = null; }
    }
}
