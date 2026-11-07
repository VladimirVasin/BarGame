using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>Measured strip and apron sources belonging to one physical platform.</summary>
    [DisallowMultipleComponent]
    public sealed class CityBusStopLighting : MonoBehaviour
    {
        public const int StripLightCount = 4;
        public const int LightCount = StripLightCount + 1;
        public const int MaximumShadowedStops = 2;
        public static readonly Color LightColor = new Color(.82f, .94f, .88f);
        internal static readonly Color LensColor = LightColor * 2.4f;
        private readonly Transform[] anchors = new Transform[LightCount];
        private readonly Light[] lights = new Light[LightCount];
        private Renderer[] strips = Array.Empty<Renderer>();
        public IReadOnlyList<Transform> SourceAnchors => anchors;
        public IReadOnlyList<Light> Lights => lights;
        public IReadOnlyList<Renderer> StripRenderers => strips;
        public Renderer PlatformLens { get; private set; }
        public Bounds WorldBounds { get; private set; }
        public float ShadowStrength { get; private set; }

        internal void Initialize(Vector3 routeForward)
        {
            var stripList = new List<Renderer>(1);
            foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.name.EndsWith(" BacklitSign", StringComparison.Ordinal)) continue;
                if (renderer.name.StartsWith("Imported Route 01 Shelter Shell", StringComparison.Ordinal)) stripList.Add(renderer);
                else if (renderer.name.StartsWith("Imported Route 01 Pole Shell", StringComparison.Ordinal)) PlatformLens = renderer;
            }
            strips = stripList.ToArray();
            // Every source lies on an authored diffuser, never in the empty
            // middle of the combined perimeter mesh. The import gate measures
            // these positions against the FBX's named empty anchors.
            Vector3 along = transform.InverseTransformDirection(routeForward);
            along.y = 0f;
            along = along.sqrMagnitude > .0001f ? along.normalized : Vector3.right;
            for (int i = 0; i < LightCount; i++)
            {
                Transform anchor = new GameObject(CityMiscAssetProvider.GetBusStopLightAnchorName(i)).transform;
                anchor.SetParent(transform, false);
                anchor.localPosition = CityMiscAssetProvider.GetBusStopLightAnchor(i);
                Vector3 aim = i < StripLightCount ? Vector3.down : along * 2.6f + new Vector3(0f, -2.45f, 1f);
                anchor.localRotation = Quaternion.LookRotation(aim, Vector3.forward);
                anchors[i] = anchor;
            }
            Bounds bounds = new Bounds(transform.position, Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
                bounds.Encapsulate(transform.TransformPoint(new Vector3((corner & 1) == 0 ? -5f : 4f,
                    (corner & 2) == 0 ? 0f : 2.75f, (corner & 4) == 0 ? -.55f : 3f)));
            WorldBounds = bounds;
        }

        internal float NightIntensity(int index) => index < StripLightCount ? 2.5f : 7f;

        internal void BindLight(int index, Light light)
        {
            lights[index] = light;
            light.transform.SetPositionAndRotation(anchors[index].position, anchors[index].rotation);
            light.type = LightType.Spot;
            light.color = LightColor;
            light.range = index < StripLightCount ? 5f : 6.5f;
            light.spotAngle = index < StripLightCount ? 165f : 105f;
            light.innerSpotAngle = index < StripLightCount ? 140f : 65f;
            light.shadowBias = .015f;
            light.shadowNormalBias = .03f;
            light.shadowNearPlane = .02f;
            var data = light.GetUniversalAdditionalLightData();
            data.usePipelineSettings = false;
            if (Application.isPlaying)
                data.additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
            light.shadows = LightShadows.None;
            light.shadowStrength = 0f;
        }

        internal void ApplyShadowStrength(float strength)
        {
            strength = Mathf.Clamp01(strength);
            if (Mathf.Approximately(strength, ShadowStrength)) return;
            ShadowStrength = strength;
            foreach (Light light in lights)
            {
                if (light == null) continue;
                light.shadowStrength = strength;
                light.shadows = strength > .0001f ? LightShadows.Soft : LightShadows.None;
            }
        }

        private void OnDisable()
        {
            ApplyShadowStrength(0f);
            foreach (Light light in lights) if (light != null) light.enabled = false;
        }
    }
}
