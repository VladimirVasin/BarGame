using UnityEngine;

namespace BarPromenade
{
    /// <summary>A shielded ground fixture washing nearby planting, with the City's day floor.</summary>
    [DisallowMultipleComponent]
    public sealed class ChurchGardenUplight : MonoBehaviour
    {
        public const float HedgeIntensity = 7.2f;
        public const float StatueIntensity = 2.8f;
        public const float LightRange = 3.2f;
        private static readonly Color Warm = new Color(1f, 0.72f, 0.42f);
        private Renderer lensRenderer;
        private MaterialPropertyBlock lensProperties;
        private float appliedFactor = -1f;

        public Light Emitter { get; private set; }
        public float NightIntensity { get; private set; }
        public bool LightsStatue { get; private set; }

        public void Initialize(bool lightsStatue)
        {
            LightsStatue = lightsStatue;
            NightIntensity = lightsStatue ? StatueIntensity : HedgeIntensity;
            lensRenderer = GetComponent<Renderer>();
            lensProperties = new MaterialPropertyBlock();
            var source = new GameObject("Ground Planting Light");
            source.transform.SetParent(transform, false);
            source.transform.localPosition = ChurchGardenModelProvider.UplightLensLocalPosition;
            source.transform.localRotation = Quaternion.LookRotation(
                ChurchGardenModelProvider.UplightLensLocalDirection, Vector3.up);
            Emitter = source.AddComponent<Light>();
            Emitter.type = LightType.Spot;
            Emitter.color = Warm;
            Emitter.range = LightRange;
            Emitter.spotAngle = lightsStatue ? 68f : 88f;
            Emitter.innerSpotAngle = lightsStatue ? 34f : 44f;
            Emitter.shadows = LightShadows.None;
            Emitter.renderMode = LightRenderMode.ForcePixel;
            Emitter.lightmapBakeType = LightmapBakeType.Realtime;
            // Small and attached to the real lens: no floating decorative orbs.
            CityLightHalo halo = CityLightHalo.CreateAlwaysBurning(source.transform,
                Vector3.zero, lightsStatue ? 0.10f : 0.16f, lightsStatue ? 0.34f : 0.58f,
                new Color(4f, 2.5f, 1.2f, lightsStatue ? 0.10f : 0.18f),
                new Color(2f, 1.25f, 0.6f, lightsStatue ? 0.025f : 0.05f));
            CityNightSiteLightRegistry.Register(Emitter, NightIntensity, halo);
            ApplyLens();
        }

        private void Update() => ApplyLens();

        private void ApplyLens()
        {
            if (lensRenderer == null || lensProperties == null) return;
            float factor = GameTimeDayNightRules.FixtureFactor(CityNightSiteLightRegistry.NightFactor);
            if (Mathf.Approximately(factor, appliedFactor)) return;
            appliedFactor = factor;
            Color glow = Warm * ((LightsStatue ? 2.4f : 4.5f) * factor);
            glow.a = 1f;
            lensRenderer.GetPropertyBlock(lensProperties, ChurchGardenModelProvider.UplightLensMaterialIndex);
            lensProperties.SetColor("_BaseColor", glow);
            lensProperties.SetColor("_Color", glow);
            lensRenderer.SetPropertyBlock(lensProperties, ChurchGardenModelProvider.UplightLensMaterialIndex);
        }
    }
}
