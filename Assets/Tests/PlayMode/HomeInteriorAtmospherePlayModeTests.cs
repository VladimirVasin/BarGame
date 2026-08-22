using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeInteriorAtmospherePlayModeTests
    {
        private GameObject atmosphereObject;

        [UnityTest]
        public IEnumerator Initialize_CreatesBoundedLightsGradeAndDust()
        {
            atmosphereObject =
                new GameObject("Home Atmosphere Test");
            HomeInteriorAtmosphere atmosphere =
                atmosphereObject.AddComponent<
                    HomeInteriorAtmosphere>();

            atmosphere.Initialize();
            yield return null;

            Assert.That(atmosphere.IsInitialized, Is.True);
            Assert.That(
                atmosphere.PracticalLights,
                Has.Count.EqualTo(
                    HomeInteriorAtmosphere
                        .MaximumPracticalLights));
            Assert.That(
                atmosphere.PracticalLights.Count,
                Is.LessThanOrEqualTo(2));

            Light mainLight = atmosphere.PracticalLights[0];
            Light bathroomLight =
                atmosphere.PracticalLights[1];
            AssertPracticalLight(
                mainLight,
                new Vector3(-2.35f, 2.00f, 0.45f),
                new Color(0.95f, 0.52f, 0.22f),
                3.50f,
                9.0f);
            AssertPracticalLight(
                bathroomLight,
                new Vector3(3.15f, 2.04f, 3.38f),
                new Color(0.52f, 0.68f, 0.72f),
                2.20f,
                4.0f);
            Assert.That(
                mainLight.color.r,
                Is.GreaterThan(mainLight.color.b));
            Assert.That(
                bathroomLight.color.b,
                Is.GreaterThan(bathroomLight.color.r));

            Light windowLight = atmosphere.WindowLight;
            Assert.That(windowLight, Is.Not.Null);
            Assert.That(windowLight.enabled, Is.True);
            Assert.That(
                windowLight.type,
                Is.EqualTo(LightType.Spot));
            Assert.That(
                windowLight.shadows,
                Is.EqualTo(LightShadows.Hard));
            Assert.That(
                windowLight.renderMode,
                Is.EqualTo(LightRenderMode.ForcePixel));
            Assert.That(
                windowLight.transform.localPosition.x,
                Is.GreaterThan(
                    PlayerHomeBalconyGeometry.HomeFacadeX));
            Vector3 windowDirection =
                windowLight.transform.localRotation *
                Vector3.forward;
            Assert.That(
                Vector3.Dot(windowDirection, Vector3.left),
                Is.GreaterThan(0.85f),
                "The window light must face inward from the +X facade.");
            Assert.That(
                windowDirection.y,
                Is.LessThan(-0.15f));
            Assert.That(
                windowLight.color.b,
                Is.GreaterThan(windowLight.color.g)
                    .And.GreaterThan(windowLight.color.r));
            Assert.That(
                windowLight.intensity,
                Is.InRange(4.5f, 6.0f));
            Assert.That(
                windowLight.range,
                Is.InRange(8f, 14f));
            Assert.That(
                windowLight.innerSpotAngle,
                Is.GreaterThan(0f)
                    .And.LessThan(windowLight.spotAngle));
            Assert.That(
                windowLight.spotAngle,
                Is.InRange(45f, 75f));

            Light bathroomSpillLight =
                atmosphere.BathroomSpillLight;
            Assert.That(bathroomSpillLight, Is.Not.Null);
            Assert.That(bathroomSpillLight.enabled, Is.True);
            Assert.That(
                bathroomSpillLight.type,
                Is.EqualTo(LightType.Spot));
            Assert.That(
                bathroomSpillLight.shadows,
                Is.EqualTo(LightShadows.Hard));
            Assert.That(
                bathroomSpillLight.renderMode,
                Is.EqualTo(LightRenderMode.ForcePixel));
            Assert.That(
                bathroomSpillLight.transform.localPosition,
                Is.EqualTo(
                    new Vector3(2.15f, 2.05f, 0.82f)));
            Vector3 bathroomSpillDirection =
                bathroomSpillLight.transform.localRotation *
                Vector3.forward;
            Assert.That(
                Vector3.Dot(
                    bathroomSpillDirection,
                    (new Vector3(0f, 0.12f, -3.05f) -
                     new Vector3(2.15f, 2.05f, 0.82f)).normalized),
                Is.GreaterThan(0.999f));
            Assert.That(
                bathroomSpillLight.intensity,
                Is.EqualTo(10.0f).Within(0.001f));
            Assert.That(
                bathroomSpillLight.range,
                Is.EqualTo(6.6f).Within(0.001f));
            Assert.That(
                bathroomSpillLight.innerSpotAngle,
                Is.EqualTo(30f).Within(0.001f));
            Assert.That(
                bathroomSpillLight.spotAngle,
                Is.EqualTo(52f).Within(0.001f));
            Assert.That(
                bathroomSpillLight.color,
                Is.EqualTo(new Color(0.52f, 0.68f, 0.72f)));
            Assert.That(
                bathroomSpillLight.bounceIntensity,
                Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                bathroomSpillLight.shadowStrength,
                Is.EqualTo(0.62f).Within(0.001f));

            Light entryDoorLight = atmosphere.EntryDoorLight;
            Assert.That(entryDoorLight, Is.Not.Null);
            Assert.That(entryDoorLight.enabled, Is.True);
            Assert.That(
                entryDoorLight.type,
                Is.EqualTo(LightType.Spot));
            Assert.That(
                entryDoorLight.shadows,
                Is.EqualTo(LightShadows.None));
            Assert.That(
                entryDoorLight.renderMode,
                Is.EqualTo(LightRenderMode.ForcePixel));
            Assert.That(
                entryDoorLight.transform.localPosition,
                Is.EqualTo(new Vector3(0f, 2.45f, -3.70f)));
            Vector3 entryDoorDirection =
                entryDoorLight.transform.localRotation *
                Vector3.forward;
            Assert.That(
                Vector3.Dot(
                    entryDoorDirection,
                    (new Vector3(0f, 0.55f, -2.75f) -
                     new Vector3(0f, 2.45f, -3.70f)).normalized),
                Is.GreaterThan(0.999f));
            Assert.That(
                entryDoorLight.intensity,
                Is.EqualTo(8.0f).Within(0.001f));
            Assert.That(
                entryDoorLight.range,
                Is.EqualTo(5.5f).Within(0.001f));
            Assert.That(
                entryDoorLight.innerSpotAngle,
                Is.EqualTo(72f).Within(0.001f));
            Assert.That(
                entryDoorLight.spotAngle,
                Is.EqualTo(100f).Within(0.001f));
            Assert.That(
                entryDoorLight.color,
                Is.EqualTo(new Color(1.0f, 0.46f, 0.16f)));
            Assert.That(
                entryDoorLight.bounceIntensity,
                Is.EqualTo(0f).Within(0.001f));

            HomeBathroomLightFlicker flicker =
                atmosphere.BathroomFlicker;
            Assert.That(flicker, Is.Not.Null);
            Assert.That(flicker.IsInitialized, Is.True);
            Assert.That(
                flicker.BathroomLight,
                Is.SameAs(bathroomLight));
            Assert.That(
                flicker.SpillLight,
                Is.SameAs(bathroomSpillLight));
            Assert.That(flicker.Fixture, Is.Null);
            Assert.That(
                HomeBathroomLightFlicker.EvaluateFactor(0f),
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(
                HomeBathroomLightFlicker.EvaluateFactor(4.72f),
                Is.EqualTo(0.18f).Within(0.001f));
            Assert.That(
                HomeBathroomLightFlicker.EvaluateFactor(5.00f),
                Is.EqualTo(
                    HomeBathroomLightFlicker.MinimumFactor)
                    .Within(0.001f));
            Assert.That(
                HomeBathroomLightFlicker.EvaluateFactor(5.30f),
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(
                HomeBathroomLightFlicker.EvaluateFactor(
                    HomeBathroomLightFlicker.CycleSeconds + 4.72f),
                Is.EqualTo(0.18f).Within(0.001f));

            Texture2D cookie =
                HomeBalconyResources.WindowLightCookie;
            Assert.That(windowLight.cookie, Is.SameAs(cookie));
            Assert.That(
                HomeBalconyResources.WindowLightCookie,
                Is.SameAs(cookie),
                "The window cookie must be generated only once.");
            Assert.That(
                cookie.width,
                Is.EqualTo(
                    HomeBalconyResources
                        .WindowLightCookieResolution));
            Assert.That(cookie.height, Is.EqualTo(cookie.width));
            Assert.That(
                cookie.wrapMode,
                Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(
                cookie.GetPixel(0, 0).grayscale,
                Is.LessThan(0.01f));
            Assert.That(
                cookie.GetPixel(16, 20).grayscale,
                Is.GreaterThan(0.85f));
            Assert.That(
                cookie.GetPixel(
                        cookie.width / 2,
                        20)
                    .grayscale,
                Is.LessThan(0.01f),
                "The generated cookie must preserve its vertical mullion.");

            Light[] ownedLights =
                atmosphere.GetComponentsInChildren<Light>();
            Assert.That(
                ownedLights,
                Has.Length.EqualTo(
                    HomeInteriorAtmosphere
                        .MaximumRealtimeLights));
            Assert.That(
                ownedLights.Length,
                Is.LessThanOrEqualTo(5));
            Assert.That(
                atmosphere.PracticalLights[0],
                Is.Not.SameAs(windowLight),
                "The window beam is separate from the two practicals.");
            Assert.That(
                atmosphere.PracticalLights[1],
                Is.Not.SameAs(windowLight),
                "The window beam is separate from the two practicals.");
            Assert.That(
                atmosphere.PracticalLights[0],
                Is.Not.SameAs(bathroomSpillLight),
                "The bathroom spill is not a room practical.");
            Assert.That(
                atmosphere.PracticalLights[1],
                Is.Not.SameAs(bathroomSpillLight),
                "The bathroom spill is not a room practical.");
            Assert.That(
                bathroomSpillLight,
                Is.Not.SameAs(windowLight));
            Assert.That(
                entryDoorLight,
                Is.Not.SameAs(windowLight)
                    .And.Not.SameAs(bathroomSpillLight));
            Assert.That(
                atmosphere.PracticalLights[0],
                Is.Not.SameAs(entryDoorLight));
            Assert.That(
                atmosphere.PracticalLights[1],
                Is.Not.SameAs(entryDoorLight));

            Material glass =
                HomeBalconyResources.GlassMaterial;
            Assert.That(glass, Is.Not.Null);
            Assert.That(
                HomeBalconyResources.GlassMaterial,
                Is.SameAs(glass),
                "Every pane must reuse one shared glass material.");
            Assert.That(glass.shader, Is.Not.Null);
            Assert.That(
                glass.shader.name,
                Is.EqualTo(
                    "Bar Promenade/Home Window Glass"));
            Assert.That(
                glass.GetTag("RenderType", false),
                Is.EqualTo("Transparent"));
            Assert.That(
                glass.renderQueue,
                Is.GreaterThanOrEqualTo(3000));

            Assert.That(
                atmosphere.PostProcessVolume,
                Is.Not.Null);
            Assert.That(
                atmosphere.PostProcessVolume.isGlobal,
                Is.True);
            Assert.That(
                atmosphere.PostProcessVolume.weight,
                Is.EqualTo(1f));
            VolumeProfile profile = atmosphere.RuntimeProfile;
            Assert.That(profile, Is.Not.Null);
            Assert.That(
                profile.TryGet(out Bloom bloom),
                Is.True);
            Assert.That(
                bloom.intensity.value,
                Is.InRange(0.05f, 0.2f));
            Assert.That(
                profile.TryGet(
                    out ColorAdjustments color),
                Is.True);
            Assert.That(
                color.postExposure.value,
                Is.InRange(0f, 0.5f));
            Assert.That(
                color.saturation.value,
                Is.InRange(-25f, -5f));
            Assert.That(
                profile.TryGet(out Vignette vignette),
                Is.True);
            Assert.That(
                vignette.intensity.value,
                Is.InRange(0.15f, 0.3f));
            Assert.That(
                profile.TryGet(out FilmGrain grain),
                Is.True);
            Assert.That(
                grain.intensity.value,
                Is.InRange(0.08f, 0.2f));
            Assert.That(
                profile.TryGet(out DepthOfField depthOfField),
                Is.True);
            Assert.That(
                depthOfField.mode.value,
                Is.EqualTo(DepthOfFieldMode.Gaussian));
            bool previousDofEnabled =
                GraphicsEffectsSettings.DepthOfFieldEnabled;
            try
            {
                GraphicsEffectsSettings.DepthOfFieldEnabled = false;
                yield return null;
                Assert.That(
                    depthOfField.active,
                    Is.False,
                    "The binder must deactivate the override when " +
                    "the player disables depth of field.");
                GraphicsEffectsSettings.DepthOfFieldEnabled = true;
                yield return null;
                Assert.That(depthOfField.active, Is.True);
            }
            finally
            {
                GraphicsEffectsSettings.DepthOfFieldEnabled =
                    previousDofEnabled;
            }

            Assert.That(atmosphere.Dust, Is.Not.Null);
            ParticleSystem.MainModule main =
                atmosphere.Dust.main;
            Assert.That(main.loop, Is.True);
            Assert.That(
                main.maxParticles,
                Is.EqualTo(
                    HomeInteriorAtmosphere
                        .MaximumDustParticles));
            Assert.That(
                main.maxParticles,
                Is.LessThanOrEqualTo(12));

            ParticleSystem.VelocityOverLifetimeModule velocity =
                atmosphere.Dust.velocityOverLifetime;
            Assert.That(velocity.enabled, Is.True);
            Assert.That(
                velocity.x.mode,
                Is.EqualTo(
                    ParticleSystemCurveMode.TwoConstants));
            Assert.That(velocity.y.mode, Is.EqualTo(velocity.x.mode));
            Assert.That(velocity.z.mode, Is.EqualTo(velocity.x.mode));

            ParticleSystemRenderer renderer =
                atmosphere.Dust.GetComponent<
                    ParticleSystemRenderer>();
            Assert.That(
                renderer.sharedMaterial,
                Is.SameAs(
                    CityNightResources.AtmosphereMaterial));
            Assert.That(
                renderer.shadowCastingMode,
                Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(renderer.receiveShadows, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Destroy_ReleasesRuntimeVolumeProfile()
        {
            atmosphereObject =
                new GameObject("Home Atmosphere Cleanup Test");
            HomeInteriorAtmosphere atmosphere =
                atmosphereObject.AddComponent<
                    HomeInteriorAtmosphere>();
            atmosphere.Initialize();
            VolumeProfile profile = atmosphere.RuntimeProfile;
            yield return null;

            Object.Destroy(atmosphereObject);
            atmosphereObject = null;
            yield return null;

            Assert.That(
                profile == null,
                Is.True,
                "The runtime-only VolumeProfile must be destroyed with " +
                "the home atmosphere.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (atmosphereObject != null)
            {
                Object.Destroy(atmosphereObject);
            }

            yield return null;
        }

        private static void AssertPracticalLight(
            Light light,
            Vector3 expectedPosition,
            Color expectedColor,
            float expectedIntensity,
            float expectedRange)
        {
            Assert.That(light, Is.Not.Null);
            Assert.That(light.enabled, Is.True);
            Assert.That(light.type, Is.EqualTo(LightType.Point));
            Assert.That(light.shadows, Is.EqualTo(LightShadows.None));
            Assert.That(
                light.renderMode,
                Is.EqualTo(LightRenderMode.ForcePixel));
            Assert.That(
                light.intensity,
                Is.EqualTo(expectedIntensity).Within(0.001f));
            Assert.That(
                light.range,
                Is.EqualTo(expectedRange).Within(0.001f));
            Assert.That(
                Vector3.Distance(
                    light.transform.localPosition,
                    expectedPosition),
                Is.LessThan(0.001f));
            Assert.That(
                light.color.r,
                Is.EqualTo(expectedColor.r).Within(0.001f));
            Assert.That(
                light.color.g,
                Is.EqualTo(expectedColor.g).Within(0.001f));
            Assert.That(
                light.color.b,
                Is.EqualTo(expectedColor.b).Within(0.001f));
        }
    }
}
