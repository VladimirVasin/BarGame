using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>The approved camera episode's invisible, local fill; no scene exposure change.</summary>
    [DisallowMultipleComponent]
    public sealed class HomeToiletBowlLighting : MonoBehaviour
    {
        private const uint BowlLayer = 1u << 4;
        private readonly Dictionary<Renderer, uint> layers = new Dictionary<Renderer, uint>();
        private Light fill;
        private Vector3 bowlFillPosition;
        public bool IsActive { get; private set; }
        public float Amount { get; private set; }
        public Light Fill => fill;

        public void Begin(Vector3 cameraPosition, Vector3 right, Vector3 forward, params Transform[] roots)
        {
            End();
            if (fill == null)
            {
                var carrier = new GameObject("Toilet Bowl Camera Fill");
                carrier.transform.SetParent(transform, false);
                fill = carrier.AddComponent<Light>();
                fill.type = LightType.Point;
                fill.shadows = LightShadows.None;
                fill.range = .78f;
                fill.color = new Color(.88f, .89f, .76f);
                fill.bounceIntensity = 0f;
                fill.renderMode = LightRenderMode.ForcePixel;
                fill.GetUniversalAdditionalLightData().renderingLayers = BowlLayer;
            }
            // An off-axis fill keeps the near-camera object away from the singular
            // bright point of a tiny light, while still revealing downward surfaces.
            bowlFillPosition = cameraPosition + right * .06f - forward * .24f + Vector3.up * .025f;
            fill.transform.position = bowlFillPosition;
            fill.intensity = 0f;
            fill.enabled = true;
            foreach (Transform root in roots)
            {
                if (root == null) continue;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (layers.ContainsKey(renderer)) continue;
                    layers.Add(renderer, renderer.renderingLayerMask);
                    renderer.renderingLayerMask |= BowlLayer;
                }
            }
            IsActive = true;
        }

        public void Present(float amount, float inspectionAmount = 0f)
        {
            if (!IsActive || fill == null) return;
            Amount = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(amount));
            float inspection = Mathf.Clamp01(inspectionAmount);
            // Follow the bent inspection gently with the same invisible fill;
            // the face otherwise lies outside the small seated light's range.
            fill.transform.position = bowlFillPosition + Vector3.up * (.32f * inspection);
            fill.range = .78f + .42f * inspection;
            fill.intensity = (.17f + .08f * inspection) * Amount;
        }

        public void End()
        {
            IsActive = false;
            Amount = 0f;
            if (fill != null) { fill.intensity = 0f; fill.enabled = false; }
            foreach (var state in layers)
                if (state.Key != null) state.Key.renderingLayerMask = state.Value;
            layers.Clear();
        }
        private void OnDisable() => End();
        private void OnDestroy() => End();
    }
}
