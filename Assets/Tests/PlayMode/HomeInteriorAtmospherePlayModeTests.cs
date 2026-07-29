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
            AssertPracticalLight(mainLight, 3.75f);
            AssertPracticalLight(bathroomLight, 2.4f);
            Assert.That(
                mainLight.color.r,
                Is.GreaterThan(mainLight.color.b));
            Assert.That(
                bathroomLight.color.b,
                Is.GreaterThan(bathroomLight.color.r));

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
                Is.InRange(-0.5f, 0f));
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
            float maximumIntensity)
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
                Is.GreaterThan(0f)
                    .And.LessThanOrEqualTo(maximumIntensity));
            Assert.That(light.range, Is.GreaterThan(0f));
        }
    }
}
