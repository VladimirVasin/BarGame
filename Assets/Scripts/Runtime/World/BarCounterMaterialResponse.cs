using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bar-owned physical fields on the existing counter and its nearby brass.</summary>
    public static class BarCounterMaterialResponse
    {
        public const string WoodResourcePath = "Bar/Response/BarCounterWoodResponse";
        public const string BrassResourcePath = "Bar/Response/BarCounterBrassResponse";
        public const int MaximumReceiverCount = 10;
        public const float ResponseStrength = 1f;
        public const float NormalStrength = 0f;
        private static readonly string[] receiverNames =
        {
            "Counter Top", "Counter Return Top", "Counter Foot Rail", "Counter Return Foot Rail",
            "Beer Tap Stem 1", "Beer Tap Spout 1", "Beer Tap Stem 2", "Beer Tap Spout 2",
            "Beer Tap Stem 3", "Beer Tap Spout 3"
        };
        private static readonly int ResponseId = Shader.PropertyToID("_SceneMaterialResponse");
        private static readonly int ResponseStrengthId = Shader.PropertyToID("_SceneResponseStrength");
        private static readonly int NormalStrengthId = Shader.PropertyToID("_SceneNormalStrength");
        private static Material sharedMaterial;
        private static Texture2D wood, brass;
        private static readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();

        public static int SharedMaterialCount => sharedMaterial != null ? 1 : BarInteriorSurfaceAppearance.SharedMaterialCount;

        internal static void Apply(Renderer renderer, BarSurfaceKind kind, MaterialPropertyBlock target)
        {
            if (!TryResolve(renderer.name, out bool isWood) ||
                kind != (isWood ? BarSurfaceKind.PolishedWood : BarSurfaceKind.AgedBrass)) return;
            target.SetTexture(ResponseId, LoadResponse(isWood));
            target.SetFloat(ResponseStrengthId, ResponseStrength);
            target.SetFloat(NormalStrengthId, NormalStrength);
            renderer.sharedMaterial = SharedMaterial;
        }

        /// <summary>Explicit diagnostic A/B; touches only the ten authored room parts, never a frame loop.</summary>
        public static int SetResponseEnabled(Transform room, bool enabled)
        {
            if (room == null) return 0;
            int count = 0;
            for (int i = 0; i < receiverNames.Length; i++)
            {
                Transform part = room.Find(receiverNames[i]);
                Renderer renderer = part != null ? part.GetComponent<Renderer>() : null;
                bool unique = BarInteriorSurfaceAppearance.UsesSharedMaterial(renderer);
                if (renderer == null || !unique && (renderer.sharedMaterial != sharedMaterial || sharedMaterial == null)) continue;
                properties.Clear(); renderer.GetPropertyBlock(properties);
                if (properties.GetTexture(ResponseId) == null) continue;
                properties.SetFloat(ResponseStrengthId, enabled ? ResponseStrength : 0f);
                properties.SetFloat(NormalStrengthId, unique && enabled ? BarInteriorSurfaceAppearance.ReliefStrength : NormalStrength);
                renderer.SetPropertyBlock(properties);
                count++;
            }
            return count;
        }

        public static bool TryGetResponse(Renderer renderer, out Texture2D map, out float strength)
        {
            map = null; strength = 0f;
            if (renderer == null || !BarInteriorSurfaceAppearance.UsesSharedMaterial(renderer) &&
                (sharedMaterial == null || renderer.sharedMaterial != sharedMaterial)) return false;
            properties.Clear(); renderer.GetPropertyBlock(properties);
            map = properties.GetTexture(ResponseId) as Texture2D;
            strength = properties.GetFloat(ResponseStrengthId);
            return map != null;
        }

        private static bool TryResolve(string name, out bool isWood)
        {
            for (int i = 0; i < receiverNames.Length; i++)
                if (name == receiverNames[i]) { isWood = i < 2; return true; }
            isWood = false;
            return false;
        }

        private static Texture2D LoadResponse(bool isWood)
        {
            ref Texture2D map = ref (isWood ? ref wood : ref brass);
            if (map == null) map = Resources.Load<Texture2D>(isWood ? WoodResourcePath : BrassResourcePath);
            if (map == null) throw new InvalidOperationException("Missing bar counter physical response map.");
            return map;
        }

        private static Material SharedMaterial
        {
            get
            {
                if (sharedMaterial != null) return sharedMaterial;
                // This existing shader owns the common response sample and continuous
                // Lit/bounce/shadow passes. Only bar-owned maps and one cached material vary.
                Shader shader = Resources.Load<Shader>("Shaders/CitySurfaceLit");
                if (shader == null || !shader.isSupported)
                    throw new InvalidOperationException("Missing supported physical surface shader for the bar.");
                sharedMaterial = new Material(RuntimePrimitiveFactory.DefaultMaterial)
                {
                    name = "Bar Counter Physical Response Shared", shader = shader,
                    hideFlags = HideFlags.HideAndDontSave, enableInstancing = true
                };
                return sharedMaterial;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (sharedMaterial != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(sharedMaterial);
                else UnityEngine.Object.DestroyImmediate(sharedMaterial);
            }
            sharedMaterial = null; wood = brass = null;
            properties.Clear();
        }
    }
}
