using System;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class ChurchInteriorAtmosphere : MonoBehaviour
    {
        public const string RootName = "Church Interior Atmosphere";
        public const int WarmPracticalLightCount = 4;
        public const int StainedGlassLightCount = 2;
        public const int PracticalLightCount =
            WarmPracticalLightCount + StainedGlassLightCount;

        private static readonly Vector3[] WarmPositions =
        {
            new Vector3(-8.8f, 1.55f, 10.5f),
            new Vector3(8.8f, 1.55f, 10.5f),
            new Vector3(-1.8f, 2.8f, 17.0f),
            new Vector3(1.8f, 2.8f, 17.0f)
        };

        public Light[] Practicals { get; private set; } =
            Array.Empty<Light>();
        public Volume PostProcessVolume { get; private set; }
        public VolumeProfile RuntimeProfile { get; private set; }

        public static ChurchInteriorAtmosphere Install(
            Transform parent,
            ChurchInteriorLayoutPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            var atmosphere = root.AddComponent<
                ChurchInteriorAtmosphere>();
            atmosphere.Practicals = BuildPracticals(
                root.transform,
                plan);
            atmosphere.CreatePostProcessVolume();
            return atmosphere;
        }

        private static Light[] BuildPracticals(
            Transform parent,
            ChurchInteriorLayoutPlan plan)
        {
            var lights = new Light[PracticalLightCount];
            Color warmColor = new Color(1f, 0.55f, 0.25f);
            for (int index = 0;
                 index < WarmPositions.Length;
                 index++)
            {
                lights[index] = CreatePoint(
                    parent,
                    index < 2
                        ? $"Votive Light {index + 1}"
                        : $"High Altar Light {index - 1}",
                    WarmPositions[index],
                    warmColor,
                    0.95f,
                    6.5f);
            }

            Vector3[] daylightPositions =
            {
                new Vector3(-9.3f, 8.6f, -1.5f),
                new Vector3(9.3f, 8.6f, 2.0f)
            };
            for (int index = 0;
                 index < daylightPositions.Length;
                 index++)
            {
                Vector3 position = daylightPositions[index];
                Vector3 target = new Vector3(
                    index == 0 ? -2.5f : 2.5f,
                    1.2f,
                    position.z + 1.0f);
                lights[WarmPracticalLightCount + index] = CreateSpot(
                    parent,
                    $"Cool Stained Glass Light {index + 1}",
                    position,
                    target,
                    new Color(0.60f, 0.75f, 0.92f),
                    1.15f,
                    Mathf.Min(18f, plan.RoomSize.y * 0.45f));
            }

            return lights;
        }

        private static Light CreatePoint(
            Transform parent,
            string name,
            Vector3 localPosition,
            Color color,
            float intensity,
            float range)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPosition;
            Light light = holder.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        private static Light CreateSpot(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localTarget,
            Color color,
            float intensity,
            float range)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPosition;
            holder.transform.localRotation = Quaternion.LookRotation(
                (localTarget - localPosition).normalized,
                Vector3.up);
            Light light = holder.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = 72f;
            light.innerSpotAngle = 42f;
            light.shadows = LightShadows.None;
            return light;
        }

        private void CreatePostProcessVolume()
        {
            GameObject volumeObject = new GameObject(
                "Church Interior Grade");
            volumeObject.transform.SetParent(transform, false);
            PostProcessVolume = volumeObject.AddComponent<Volume>();
            PostProcessVolume.isGlobal = true;
            PostProcessVolume.priority = 4f;
            PostProcessVolume.weight = 1f;

            RuntimeProfile =
                ScriptableObject.CreateInstance<VolumeProfile>();
            RuntimeProfile.name = "Runtime Church Interior Grade";
            RuntimeProfile.hideFlags = HideFlags.HideAndDontSave;
            PostProcessVolume.profile = RuntimeProfile;

            Tonemapping tonemapping =
                RuntimeProfile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            Bloom bloom = RuntimeProfile.Add<Bloom>(true);
            bloom.threshold.Override(0.78f);
            bloom.intensity.Override(0.38f);
            bloom.scatter.Override(0.44f);
            bloom.highQualityFiltering.Override(false);

            ColorAdjustments color =
                RuntimeProfile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.22f);
            color.contrast.Override(-4f);
            color.saturation.Override(-8f);
            color.colorFilter.Override(
                new Color(1f, 0.97f, 0.92f, 1f));

            Vignette vignette = RuntimeProfile.Add<Vignette>(true);
            vignette.intensity.Override(0.08f);
            vignette.smoothness.Override(0.42f);

            RuntimeSceneSetup.AddGaussianDepthOfField(
                RuntimeProfile,
                7f,
                26f,
                1.1f);
            volumeObject
                .AddComponent<DepthOfFieldSettingsBinder>()
                .Initialize(RuntimeProfile);
        }

        private void OnDestroy()
        {
            if (RuntimeProfile == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(RuntimeProfile);
            }
            else
            {
                DestroyImmediate(RuntimeProfile);
            }

            RuntimeProfile = null;
        }
    }
}
