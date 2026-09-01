using System;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class MothersHouseInteriorAtmosphere : MonoBehaviour
    {
        public const string RootName = "Mother's House Atmosphere";
        public const int FireLightCount = 1;
        public const int WindowLightCount = 2;
        public const int LampLightCount = 1;
        public const int PracticalLightCount =
            FireLightCount + WindowLightCount + LampLightCount;
        public const float FloorLampIntensity = 3.1f;
        public const float FloorLampSpotAngle = 112f;

        public static readonly Color FireColor =
            new Color(1f, 0.48f, 0.18f);
        public static readonly Color WindowColor =
            new Color(0.52f, 0.65f, 0.82f);
        public static readonly Color FloorLampColor =
            new Color(1f, 0.70f, 0.40f);

        public Light FireLight { get; private set; }
        public Light FloorLampLight { get; private set; }
        public Light[] LampLights { get; private set; } =
            Array.Empty<Light>();
        public Light[] WindowLights { get; private set; } =
            Array.Empty<Light>();
        public MothersHouseFireFlicker FireFlicker { get; private set; }
        public AudioSource FireCrackleSource { get; private set; }
        public Volume PostProcessVolume { get; private set; }
        public VolumeProfile RuntimeProfile { get; private set; }

        private AudioClip crackleClip;

        public static MothersHouseInteriorAtmosphere Install(
            Transform parent,
            MothersHouseInteriorLayoutPlan plan,
            MothersHouseInteriorWorldResult world)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var holder = new GameObject(RootName);
            holder.transform.SetParent(parent, false);
            var atmosphere = holder.AddComponent<
                MothersHouseInteriorAtmosphere>();
            atmosphere.ConfigureSceneFill();
            atmosphere.FireLight = atmosphere.CreateFireLight(
                world.FireLightAnchor.position);
            atmosphere.FloorLampLight = atmosphere.CreateLampLight(
                "Fabric-Shaded Floor Lamp Light",
                world.FloorLampLightAnchor.position,
                world.Root.TransformDirection(
                    new Vector3(0.25f, -1f, -0.45f)),
                FloorLampColor,
                FloorLampIntensity,
                4.5f,
                FloorLampSpotAngle);
            atmosphere.LampLights = new[]
            {
                atmosphere.FloorLampLight
            };
            atmosphere.WindowLights = atmosphere.CreateWindowLights(
                parent,
                plan);

            Renderer[] flames =
            {
                RequireRenderer(
                    world.Registry,
                    "FIX_Fire.Flame.Back"),
                RequireRenderer(
                    world.Registry,
                    "FIX_Fire.Flame.Front")
            };
            Renderer embers = RequireRenderer(
                world.Registry,
                "FIX_Fire.Embers");
            atmosphere.FireFlicker = holder.AddComponent<
                MothersHouseFireFlicker>();
            atmosphere.FireFlicker.Initialize(
                atmosphere.FireLight,
                flames,
                embers,
                unchecked((uint)GameSessionState.CitySeed) ^ 0x4D4F5448u);
            atmosphere.ConfigureCrackle(
                world.FireLightAnchor.position,
                GameSessionState.CitySeed);
            atmosphere.CreatePostProcessVolume();
            return atmosphere;
        }

        private void ConfigureSceneFill()
        {
            Light sun = RenderSettings.sun;
            if (sun != null)
            {
                sun.color = new Color(0.53f, 0.60f, 0.68f);
                sun.intensity = 0.42f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.25f;
                sun.transform.rotation = Quaternion.Euler(48f, -128f, 0f);
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            // Keep only a restrained indirect floor. The visible hearth and
            // floor lamp now explain the readable pools of warmth.
            RenderSettings.ambientLight = new Color(0.17f, 0.145f, 0.115f);
            RenderSettings.reflectionIntensity = 0.36f;
            RenderSettings.fog = false;
            DynamicGI.UpdateEnvironment();
        }

        private Light CreateFireLight(Vector3 worldPosition)
        {
            GameObject lightObject = new GameObject("Hearth Fire Light");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.position = worldPosition;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = FireColor;
            light.intensity = 8.2f;
            light.range = 9.5f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.68f;
            light.shadowBias = RuntimeSceneSetup.PlayerMeshShadowBias;
            light.shadowNormalBias =
                RuntimeSceneSetup.PlayerMeshShadowNormalBias;
            light.shadowNearPlane =
                RuntimeSceneSetup.PlayerMeshShadowNearPlane;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0.15f;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            if (Application.isPlaying)
            {
                UniversalAdditionalLightData lightData =
                    light.GetUniversalAdditionalLightData();
                lightData.additionalLightsShadowResolutionTier =
                    UniversalAdditionalLightData
                        .AdditionalLightsShadowResolutionTierLow;
            }
            return light;
        }

        private Light[] CreateWindowLights(
            Transform roomRoot,
            MothersHouseInteriorLayoutPlan plan)
        {
            return new[]
            {
                CreateWindowLight(
                    roomRoot,
                    "West Window Spill",
                    plan.WestWindowPosition),
                CreateWindowLight(
                    roomRoot,
                    "East Window Spill",
                    plan.EastWindowPosition)
            };
        }

        private Light CreateLampLight(
            string name,
            Vector3 worldPosition,
            Vector3 worldDirection,
            Color color,
            float intensity,
            float range,
            float spotAngle)
        {
            GameObject lightObject = new GameObject(name);
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.position = worldPosition;
            lightObject.transform.rotation = Quaternion.LookRotation(
                worldDirection.normalized,
                Vector3.up);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = spotAngle;
            light.innerSpotAngle = spotAngle * 0.62f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0f;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            return light;
        }

        private Light CreateWindowLight(
            Transform roomRoot,
            string name,
            Vector3 localPosition)
        {
            GameObject lightObject = new GameObject(name);
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.position =
                roomRoot.TransformPoint(localPosition);
            Vector3 target = roomRoot.TransformPoint(
                new Vector3(localPosition.x * 0.35f, 0.45f, 0.35f));
            lightObject.transform.rotation = Quaternion.LookRotation(
                target - lightObject.transform.position,
                Vector3.up);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = WindowColor;
            light.intensity = 1.05f;
            light.range = 8f;
            light.spotAngle = 78f;
            light.innerSpotAngle = 50f;
            light.shadows = LightShadows.None;
            return light;
        }

        private void ConfigureCrackle(Vector3 position, int seed)
        {
            crackleClip = CityArchShelterPresentation.CreateCrackleClip(
                seed ^ 0x6D6F7468);
            GameObject soundObject = new GameObject("Hearth Crackle");
            soundObject.transform.SetParent(transform, false);
            soundObject.transform.position = position;
            FireCrackleSource = soundObject.AddComponent<AudioSource>();
            FireCrackleSource.clip = crackleClip;
            FireCrackleSource.loop = true;
            FireCrackleSource.playOnAwake = false;
            FireCrackleSource.spatialBlend = 1f;
            FireCrackleSource.dopplerLevel = 0f;
            FireCrackleSource.volume = 0.12f;
            FireCrackleSource.minDistance = 1.2f;
            FireCrackleSource.maxDistance = 11f;
            FireCrackleSource.rolloffMode = AudioRolloffMode.Logarithmic;
            GameAudioMixer.Route(
                FireCrackleSource,
                GameAudioGroup.AmbienceDetails);
            AudioLowPassFilter lowPass =
                soundObject.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = 4200f;
            FireCrackleSource.Play();
        }

        private void CreatePostProcessVolume()
        {
            GameObject volumeObject = new GameObject(
                "Mother's House Warm Grade");
            volumeObject.transform.SetParent(transform, false);
            PostProcessVolume = volumeObject.AddComponent<Volume>();
            PostProcessVolume.isGlobal = true;
            PostProcessVolume.priority = 4f;
            PostProcessVolume.weight = 1f;

            RuntimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            RuntimeProfile.name = "Runtime Mother's House Warm Grade";
            RuntimeProfile.hideFlags = HideFlags.HideAndDontSave;
            PostProcessVolume.profile = RuntimeProfile;

            Tonemapping tonemapping = RuntimeProfile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            Bloom bloom = RuntimeProfile.Add<Bloom>(true);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.25f);
            bloom.scatter.Override(0.55f);
            bloom.highQualityFiltering.Override(false);

            ColorAdjustments color =
                RuntimeProfile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.15f);
            color.contrast.Override(0f);
            color.saturation.Override(-2f);
            color.colorFilter.Override(
                new Color(1f, 0.98f, 0.93f, 1f));

            Vignette vignette = RuntimeProfile.Add<Vignette>(true);
            vignette.intensity.Override(0.025f);
            vignette.smoothness.Override(0.42f);

            RuntimeSceneSetup.AddGaussianDepthOfField(
                RuntimeProfile,
                4.5f,
                13f,
                0.75f);
            volumeObject
                .AddComponent<DepthOfFieldSettingsBinder>()
                .Initialize(RuntimeProfile);
        }

        private static Renderer RequireRenderer(
            MothersHouseInteriorAssetRegistry registry,
            string sourceName)
        {
            if (registry.TryGetPart(
                    sourceName,
                    out MothersHouseInteriorPartBinding part) &&
                part != null &&
                part.Renderer != null)
            {
                return part.Renderer;
            }

            throw new InvalidOperationException(
                $"The mother's house prefab is missing renderer " +
                $"'{sourceName}'.");
        }

        private void OnDestroy()
        {
            DestroyRuntimeObject(ref crackleClip);
            if (RuntimeProfile != null)
            {
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

        private static void DestroyRuntimeObject(ref AudioClip clip)
        {
            AudioClip value = clip;
            clip = null;
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(value);
            }
            else
            {
                Object.DestroyImmediate(value);
            }
        }
    }
}
