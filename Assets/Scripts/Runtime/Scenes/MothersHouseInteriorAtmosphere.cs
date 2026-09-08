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

        /// <summary>
        /// Five selected practical spills: two by the hearth, one per
        /// bedroom and one in the bathroom. The complete seventeen-window
        /// layout shares the room lighting; adding a pane does not
        /// add another realtime lamp or overpower the hearth.
        /// </summary>
        public const int WindowLightCount = 5;

        /// <summary>
        /// Five: the fabric-shaded floor lamp beside the sofa, the enamel
        /// bowl hanging in the parents' bedroom, and the bare bulb in the
        /// childhood room, the bathroom wall lamp and the corridor ceiling
        /// fitting. Both bedrooms are wired; what separates them is
        /// the KIND of light, not its absence - a shade over the bed that is
        /// still slept in, an unshaded flex over the one that is not.
        /// </summary>
        public const int LampLightCount = 5;
        public const float CorridorLampIntensity = 4.6f;
        public const float CorridorLampRange = 5f;
        public const float CorridorLampSpotAngle = 155f;

        /// <summary>
        /// Hung two metres over the bedroom floor and sized by
        /// `illumination x distance squared`, which lands near `0.9` on the
        /// boards below it. `Range` is the second, invisible divisor, so it
        /// is set to the room's own depth rather than opened up "to be safe".
        /// It stays far under the hearth's `8.2`, which the room's own test
        /// requires of every lamp.
        /// </summary>
        public const float UpperBedroomLampIntensity = 4.2f;
        public const float UpperBedroomLampRange = 4.2f;
        public const float UpperBedroomLampSpotAngle = 132f;

        /// <summary>
        /// The childhood room's bare bulb. A naked filament throws wider and
        /// harder than a fabric bowl but carries less glass, so it opens up
        /// and cools down rather than getting brighter: the disused room must
        /// never read as the cosier of the two.
        /// </summary>
        public const float UpperChildLampIntensity = 3.6f;
        public const float UpperChildLampRange = 4.4f;
        public const float UpperChildLampSpotAngle = 158f;
        private static readonly Color UpperChildLampColor =
            new Color(1f, 0.82f, 0.62f);
        public const int PracticalLightCount =
            FireLightCount + WindowLightCount + LampLightCount;
        // A bulb behind transmitting fabric throws light sideways as well
        // as down. One real point light keeps both seated faces in its pool.
        public const float FloorLampIntensity = 5.4f;
        public const float FloorLampRange = 5.5f;

        // The real lamp lights both faces from the front. A small nearby
        // reflection stands in for the hearth's bounce off the pale floor.
        public const float HearthBounceIntensity = 0.24f;
        public const float HearthBounceRange = 1.1f;
        public const float HearthBounceSpotAngle = 100f;
        public const int HearthBounceLightCount = 1;
        public static readonly Vector3 HearthBouncePosition =
            new Vector3(0.02f, 1.05f, 0.95f);
        public static readonly Vector3 HearthBounceTarget =
            new Vector3(0.02f, 1.33f, 1.62f);

        public static readonly Color FireColor =
            new Color(1f, 0.48f, 0.18f);

        /// <summary>
        /// Firelight that has already been off a pale floor once: the hearth's
        /// own colour, desaturated, because a bounce carries the surface it
        /// came off as much as the flame it started at.
        /// </summary>
        public static readonly Color HearthBounceColor =
            new Color(1f, 0.68f, 0.44f);
        public static readonly Color WindowColor =
            new Color(0.52f, 0.65f, 0.82f);
        public static readonly Color FloorLampColor =
            new Color(1f, 0.70f, 0.40f);

        public Light FireLight { get; private set; }
        public Light FloorLampLight { get; private set; }
        public Light UpperBedroomLampLight { get; private set; }
        public Light UpperChildLampLight { get; private set; }
        public Light BathroomLampLight { get; private set; }
        public Light CorridorLampLight { get; private set; }
        public Light[] LampLights { get; private set; } =
            Array.Empty<Light>();
        public Light[] WindowLights { get; private set; } =
            Array.Empty<Light>();

        // Reflected energy shares the visible lamp/hearth ownership;
        // it is counted separately from their direct practical lights.
        public Light HearthBounceLight { get; private set; }
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
            atmosphere.FloorLampLight = atmosphere.CreateFloorLampLight(
                world.FloorLampLightAnchor.position);
            atmosphere.UpperBedroomLampLight = atmosphere.CreateLampLight(
                "Upper Bedroom Bowl Lamp",
                world.Root.TransformPoint(
                    plan.UpperFloor.NorthLampPosition),
                world.Root.TransformDirection(Vector3.down),
                FloorLampColor,
                UpperBedroomLampIntensity,
                UpperBedroomLampRange,
                UpperBedroomLampSpotAngle);
            atmosphere.UpperChildLampLight = atmosphere.CreateLampLight(
                "Upper Childhood Room Bare Bulb",
                world.Root.TransformPoint(
                    plan.UpperFloor.SouthLampPosition),
                world.Root.TransformDirection(Vector3.down),
                UpperChildLampColor,
                UpperChildLampIntensity,
                UpperChildLampRange,
                UpperChildLampSpotAngle);
            atmosphere.BathroomLampLight = atmosphere.CreateLampLight(
                "Bathroom Opal Wall Lamp",
                world.Root.TransformPoint(MothersHouseInteriorLayoutPlanner.BathroomLampPosition),
                world.Root.TransformDirection(new Vector3(-0.4f, -1f, -0.1f)),
                new Color(1f, 0.81f, 0.59f), 3.8f, 3.8f, 150f);
            atmosphere.CorridorLampLight = atmosphere.CreateLampLight(
                "Upper Corridor Opal Ceiling Lamp",
                world.Root.TransformPoint(MothersHouseInteriorLayoutPlanner.UpperCorridorLampPosition),
                world.Root.TransformDirection(Vector3.down),
                new Color(1f, 0.79f, 0.55f),
                CorridorLampIntensity, CorridorLampRange, CorridorLampSpotAngle);
            atmosphere.LampLights = new[]
            {
                atmosphere.FloorLampLight,
                atmosphere.UpperBedroomLampLight,
                atmosphere.UpperChildLampLight,
                atmosphere.BathroomLampLight,
                atmosphere.CorridorLampLight
            };
            atmosphere.WindowLights = atmosphere.CreateWindowLights(
                parent,
                plan);
            atmosphere.HearthBounceLight =
                atmosphere.CreateHearthBounceLight(world.Root);

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
                unchecked((uint)GameSessionState.CitySeed) ^ 0x4D4F5448u,
                atmosphere.HearthBounceLight,
                atmosphere.FloorLampLight);
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
            light.shadowStrength = 0.9f;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0.15f;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            ConfigureLocalShadows(light, true);
            return light;
        }

        private Light CreateFloorLampLight(Vector3 position)
        {
            var holder = new GameObject("Fabric-Shaded Floor Lamp Light");
            holder.transform.SetParent(transform, false);
            holder.transform.position = position;
            Light light = holder.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = FloorLampColor;
            light.intensity = FloorLampIntensity;
            light.range = FloorLampRange;
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.bounceIntensity = 0f;
            ConfigureLocalShadows(light, true);
            return light;
        }

        private static void ConfigureLocalShadows(Light light, bool keyLight)
        {
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.9f;
            light.shadowBias = 0.02f;
            light.shadowNormalBias = 0.08f;
            light.shadowNearPlane = 0.05f;
            if (!Application.isPlaying)
            {
                return;
            }

            UniversalAdditionalLightData data = light.GetUniversalAdditionalLightData();
            // Otherwise URP replaces these indoor metre-scale biases with
            // the much larger project defaults and loses contact shadows.
            data.usePipelineSettings = false;
            data.additionalLightsShadowResolutionTier = keyLight
                ? UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium
                : UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
        }

        // This restrained reflection stays at the chair; the lamp itself
        // is responsible for the readable seated faces.
        private Light CreateHearthBounceLight(Transform roomRoot)
        {
            var lightObject = new GameObject("Hearth Floor Bounce");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.position =
                roomRoot.TransformPoint(HearthBouncePosition);
            lightObject.transform.rotation = Quaternion.LookRotation(
                roomRoot.TransformPoint(HearthBounceTarget) -
                    roomRoot.TransformPoint(HearthBouncePosition),
                roomRoot.up);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = HearthBounceColor;
            light.intensity = HearthBounceIntensity;
            light.range = HearthBounceRange;
            light.spotAngle = HearthBounceSpotAngle;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0f;
            light.lightmapBakeType = LightmapBakeType.Realtime;
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
                    plan.EastWindowPosition),

                // Upstairs the cold light has to fall INTO the room off a
                // wall the camera looks along, so each upper pane aims at
                // its own floor rather than at the ground-floor hearth line.
                CreateUpperWindowLight(
                    roomRoot,
                    "Upper North Window Spill",
                    plan.UpperFloor.NorthWindowPosition,
                    new Vector3(
                        plan.UpperFloor.NorthWindowPosition.x + 0.5f,
                        plan.UpperFloor.FloorElevation + 0.45f,
                        2.6f),
                    1.05f),
                CreateUpperWindowLight(
                    roomRoot,
                    "Upper South Window Spill",
                    plan.UpperFloor.SouthWindowPosition,
                    new Vector3(
                        plan.UpperFloor.SouthWindowPosition.x + 0.45f,
                        plan.UpperFloor.FloorElevation + 0.45f,
                        -2.6f),
                    1.05f),
                CreateUpperWindowLight(roomRoot, "Bathroom Frosted Window Spill",
                    MothersHouseInteriorLayoutPlanner.BathroomWindowLightPosition,
                    new Vector3(-3.3f, plan.UpperFloor.FloorElevation + 0.45f, 3.15f), 0.72f)
            };
        }

        /// <summary>
        /// Both upper panes stay secondary now that each bedroom owns a real
        /// fitting: the cold light widens the room, it does not light it.
        /// </summary>
        private Light CreateUpperWindowLight(
            Transform roomRoot,
            string name,
            Vector3 localPosition,
            Vector3 localTarget,
            float intensity)
        {
            GameObject lightObject = new GameObject(name);
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.position =
                roomRoot.TransformPoint(localPosition);
            lightObject.transform.rotation = Quaternion.LookRotation(
                roomRoot.TransformPoint(localTarget) -
                    lightObject.transform.position,
                Vector3.up);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = WindowColor;
            light.intensity = intensity;
            light.range = 7f;
            light.spotAngle = 76f;
            light.innerSpotAngle = 48f;
            ConfigureLocalShadows(light, false);
            light.bounceIntensity = 0f;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            return light;
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
            ConfigureLocalShadows(light, false);
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
            ConfigureLocalShadows(light, false);
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            return light;
        }

        private void ConfigureCrackle(Vector3 position, int seed)
        {
            crackleClip = MothersHouseInteriorSoundSynthesis.CreateHearthRuntimeClip(
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
            FireCrackleSource.priority = 160;
            // The fixed camera is also the listener, about 8.5 m from the
            // hearth even when the hero is seated beside it.
            FireCrackleSource.volume = 0.38f;
            FireCrackleSource.minDistance = 3f;
            FireCrackleSource.maxDistance = 14f;
            FireCrackleSource.rolloffMode = AudioRolloffMode.Linear;
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

            RuntimeSceneSetup.AddIndoorGaussianDepthOfField(
                RuntimeProfile,
                4.5f,
                13f);
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
