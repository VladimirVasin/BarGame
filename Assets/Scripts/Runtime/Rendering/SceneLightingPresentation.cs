using System;
using UnityEngine;

namespace BarPromenade.Rendering
{
    /// <summary>A real scene-owned emitter; a non-positive strength disables it.</summary>
    public readonly struct SceneHalationSource
    {
        public readonly Vector3 Position;
        public readonly float Radius;
        public readonly float Strength;

        public SceneHalationSource(Vector3 position, float radius, float strength)
        {
            Position = position;
            Radius = radius;
            Strength = strength;
        }
    }

    /// <summary>Bounded scene ownership for optional lighting, without scene searches.</summary>
    public abstract class SceneLightingPresentation : MonoBehaviour
    {
        public const int MaximumPresentations = 4;
        private static readonly SceneLightingPresentation[] presentations =
            new SceneLightingPresentation[MaximumPresentations];
        private static readonly int BounceCountId = Shader.PropertyToID("_WarehouseBounceCount");
        private static readonly int SurfaceReflectionCountId = Shader.PropertyToID("_SceneSurfaceReflectionCount");
        private static readonly int InteriorAirCountId = Shader.PropertyToID("_MothersHouseAirSourceCount");
        private static readonly int LocalAirCountId = Shader.PropertyToID("_AlpineLocalLightCount");
        private static readonly int CleanEffectsId = Shader.PropertyToID("_WarehouseCleanEffects");
        private static int presentationCount;

        public abstract bool IsForCamera(Camera camera);
        public abstract void PrepareForCamera(Camera camera);
        public abstract Light VolumeLight { get; }
        public abstract int HalationSourceCount { get; }
        public abstract bool TryGetHalationSource(int index, out SceneHalationSource source);

        public static SceneLightingPresentation Resolve(Camera camera)
        {
            if (camera == null) return null;
            for (int i = presentationCount - 1; i >= 0; i--)
            {
                SceneLightingPresentation presentation = presentations[i];
                if (presentation != null && presentation.isActiveAndEnabled &&
                    presentation.IsForCamera(camera)) return presentation;
            }
            return null;
        }

        // The renderer calls this on the camera timeline. Disabling one scene
        // must not erase another live scene's lighting between camera renders.
        public static void ClearCameraGlobals()
        {
            Shader.SetGlobalInt(BounceCountId, 0);
            Shader.SetGlobalInt(SurfaceReflectionCountId, 0);
            Shader.SetGlobalInt(InteriorAirCountId, 0);
        }

        protected virtual void OnEnable()
        {
            for (int i = 0; i < presentationCount; i++)
                if (presentations[i] == this) return;
            if (presentationCount >= MaximumPresentations)
                throw new InvalidOperationException("Scene lighting presentation budget exceeds four live owners.");
            presentations[presentationCount++] = this;
        }

        protected virtual void OnDisable()
        {
            for (int i = 0; i < presentationCount; i++)
            {
                if (presentations[i] != this) continue;
                for (int next = i + 1; next < presentationCount; next++)
                    presentations[next - 1] = presentations[next];
                presentations[--presentationCount] = null;
                // A loading/menu camera may not have the world renderer feature.
                // Release the last scene's globals immediately, while retaining
                // another live owner's camera-scoped bindings during overlaps.
                if (presentationCount == 0)
                {
                    ClearCameraGlobals();
                    Shader.SetGlobalInt(LocalAirCountId, 0);
                    Shader.SetGlobalVector(CleanEffectsId, Vector4.zero);
                }
                return;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Array.Clear(presentations, 0, presentations.Length);
            presentationCount = 0;
            ClearCameraGlobals();
        }
    }
}
