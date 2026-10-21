using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One existing road chunk, with reversible pigment-only diagnostics.</summary>
    [DisallowMultipleComponent]
    public sealed class MountainRoadCrownGradientPilot : MonoBehaviour
    {
        private static readonly int StrengthId = Shader.PropertyToID("_RoadCrownGradientStrength");
        private static Material sharedMaterial;
        private static int owners;
        private Renderer target;
        private Material original;
        private MaterialPropertyBlock properties;
        private bool registered;
        public Vector2Int Chunk { get; private set; }
        public Vector3 Anchor { get; private set; }
        public Renderer Target => target;
        public bool GradientEnabled { get; private set; }

        internal void Initialize(Renderer renderer, Vector2Int chunk, Vector3 anchor)
        {
            if (target != null || renderer == null) throw new InvalidOperationException("Invalid road crown pilot binding.");
            target = renderer; original = renderer.sharedMaterial; Chunk = chunk; Anchor = anchor;
            if (sharedMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/MountainRoadCrownGradient");
                if (shader == null || !shader.isSupported) throw new InvalidOperationException("Missing road crown gradient shader.");
                sharedMaterial = new Material(original) { name = "Road Crown Pigment (Shared)",
                    shader = shader, hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            }
            owners++;
            registered = true;
            target.sharedMaterial = sharedMaterial;
            properties = new MaterialPropertyBlock();
            SetGradientEnabled(true);
        }

        public void SetGradientEnabled(bool enabled)
        {
            if (target == null) return;
            target.GetPropertyBlock(properties);
            properties.SetFloat(StrengthId, enabled ? 1f : 0f);
            target.SetPropertyBlock(properties);
            GradientEnabled = enabled;
        }

        private void OnDestroy()
        {
            if (!registered) return;
            registered = false;
            if (target != null && target.sharedMaterial == sharedMaterial) target.sharedMaterial = original;
            if (--owners == 0 && sharedMaterial != null)
            {
                if (Application.isPlaying) Destroy(sharedMaterial); else DestroyImmediate(sharedMaterial);
                sharedMaterial = null;
            }
        }
    }
}
