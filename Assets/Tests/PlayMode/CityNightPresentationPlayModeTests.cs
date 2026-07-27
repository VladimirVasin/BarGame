using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CityNightPresentationPlayModeTests
    {
        private const float TimeoutSeconds = 15f;

        [UnityTest]
        public IEnumerator CityNight_CreatesFogSharedGlowAndBudgetedFixtures()
        {
            CityGameRoot city = null;
            yield return LoadSceneAndWaitForRoot<CityGameRoot>(
                SceneIds.City,
                root => city = root);
            yield return null;

            Assert.That(city.IsInitialized, Is.True);
            Assert.That(RenderSettings.fog, Is.True);
            Assert.That(
                RenderSettings.fogMode,
                Is.EqualTo(FogMode.ExponentialSquared));
            Assert.That(
                RenderSettings.fogDensity,
                Is.EqualTo(RuntimeSceneSetup.CityFogDensity).Within(0.0001f));
            Assert.That(
                RenderSettings.fogColor.maxColorComponent,
                Is.GreaterThan(0.20f));
            Assert.That(Camera.main, Is.Not.Null);
            Assert.That(
                Camera.main.backgroundColor.maxColorComponent,
                Is.InRange(0.15f, 0.25f));
            Assert.That(RenderSettings.sun, Is.Not.Null);
            Assert.That(
                RenderSettings.sun.shadowStrength,
                Is.EqualTo(RuntimeSceneSetup.CityShadowStrength)
                    .Within(0.001f));

            Volume volume =
                UnityEngine.Object.FindAnyObjectByType<Volume>();
            Assert.That(volume, Is.Not.Null);
            Assert.That(volume.isGlobal, Is.True);
            Assert.That(volume.sharedProfile, Is.Not.Null);
            Assert.That(
                volume.sharedProfile.name,
                Is.EqualTo("CityNoirVolumeProfile"));
            Assert.That(
                volume.sharedProfile.TryGet(out Bloom bloom),
                Is.True);
            Assert.That(bloom.threshold.value, Is.EqualTo(0.52f));
            Assert.That(bloom.intensity.value, Is.EqualTo(0.78f));
            Assert.That(bloom.scatter.value, Is.EqualTo(0.84f));
            Assert.That(bloom.clamp.value, Is.EqualTo(10f));
            Assert.That(
                volume.sharedProfile.TryGet(
                    out ColorAdjustments colorAdjustments),
                Is.True);
            Assert.That(
                colorAdjustments.postExposure.value,
                Is.EqualTo(0.62f));
            Assert.That(
                colorAdjustments.saturation.value,
                Is.EqualTo(-24f));
            Assert.That(
                volume.sharedProfile.TryGet(out Vignette vignette),
                Is.True);
            Assert.That(vignette.intensity.value, Is.EqualTo(0.10f));
            Assert.That(
                volume.sharedProfile.TryGet(out FilmGrain grain),
                Is.True);
            Assert.That(grain.intensity.value, Is.EqualTo(0.08f));

            CityNightWorldResult night = city.Night;
            Assert.That(night, Is.Not.Null);
            Assert.That(
                night.LampAnchors.Count,
                Is.EqualTo(night.Plan.StreetLamps.Count));
            Assert.That(night.LampAnchors.Count, Is.GreaterThan(0));
            Assert.That(night.TrafficSignals.Count, Is.GreaterThan(0));
            Assert.That(
                night.Root.GetComponentsInChildren<Collider>(true),
                Is.Empty);

            CityNightAtmosphere atmosphere = night.Atmosphere;
            Assert.That(atmosphere, Is.Not.Null);
            Assert.That(night.FogField, Is.Not.Null);
            Assert.That(night.FogField.IsInitialized, Is.True);
            Assert.That(
                night.FogField.Particles.main.maxParticles,
                Is.EqualTo(CityFogField.MaximumParticles));
            Assert.That(
                night.FogField.FogRenderer.sharedMaterial,
                Is.SameAs(CityNightResources.AtmosphereMaterial));
            Assert.That(
                atmosphere.RealtimeLightCount,
                Is.LessThanOrEqualTo(
                    CityNightAtmosphere.MaximumRealtimeLights));
            Light[] realtimeLights =
                night.Root.GetComponentsInChildren<Light>(true);
            Assert.That(
                realtimeLights,
                Has.Length.EqualTo(atmosphere.RealtimeLightCount));
            for (int index = 0; index < realtimeLights.Length; index++)
            {
                Assert.That(
                    realtimeLights[index].shadows,
                    Is.EqualTo(LightShadows.None));
                CityLightHalo halo =
                    realtimeLights[index].GetComponentInChildren<
                        CityLightHalo>(true);
                Assert.That(halo, Is.Not.Null);
                Assert.That(
                    halo.HaloRenderer.sharedMaterial,
                    Is.SameAs(CityNightResources.AtmosphereMaterial));
                Assert.That(halo.IsVisible, Is.True);
            }

            for (int index = 0;
                 index < atmosphere.StreetLightPool.Count;
                 index++)
            {
                Light streetLight = atmosphere.StreetLightPool[index];
                Assert.That(streetLight.type, Is.EqualTo(LightType.Spot));
                Assert.That(streetLight.spotAngle, Is.EqualTo(105f));
                Assert.That(streetLight.innerSpotAngle, Is.EqualTo(55f));
            }

            for (int index = 0;
                 index < atmosphere.BarLights.Count;
                 index++)
            {
                Assert.That(
                    atmosphere.BarLights[index].type,
                    Is.EqualTo(LightType.Point));
            }

            Material sharedGlow = CityNightResources.EmissiveMaterial;
            Renderer[] renderers =
                night.Root.GetComponentsInChildren<Renderer>(true);
            int glowingBulbCount = 0;
            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index].name != "Glowing Bulb")
                {
                    continue;
                }

                glowingBulbCount++;
                Assert.That(
                    renderers[index].sharedMaterial,
                    Is.SameAs(sharedGlow));
            }

            Assert.That(
                glowingBulbCount,
                Is.EqualTo(night.Plan.StreetLamps.Count));

            TrafficSignalController signal = night.TrafficSignals[0];
            Assert.That(
                signal.AmberHalos.Count,
                Is.EqualTo(signal.AmberLenses.Count));
            signal.ApplyTime(-signal.PhaseOffset);
            Assert.That(signal.IsLit, Is.True);
            for (int index = 0; index < signal.AmberHalos.Count; index++)
            {
                Assert.That(signal.AmberHalos[index].IsVisible, Is.True);
            }

            signal.ApplyTime(
                TrafficSignalController.BlinkPeriod * 0.8f -
                signal.PhaseOffset);
            Assert.That(signal.IsLit, Is.False);
            for (int index = 0; index < signal.AmberLenses.Count; index++)
            {
                Assert.That(
                    signal.AmberLenses[index].sharedMaterial,
                    Is.SameAs(sharedGlow));
                Assert.That(
                    signal.AmberHalos[index].IsVisible,
                    Is.False);
            }
        }

        [UnityTest]
        public IEnumerator BarInterior_DisablesExteriorFog()
        {
            GameSessionState.EnterBar("bar-night-smoke-test");
            BarInteriorRoot interior = null;
            yield return LoadSceneAndWaitForRoot<BarInteriorRoot>(
                SceneIds.BarInterior,
                root => interior = root);

            Assert.That(interior.IsInitialized, Is.True);
            Assert.That(RenderSettings.fog, Is.False);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<CityNightAtmosphere>(
                    FindObjectsInactive.Include),
                Is.Empty);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<CityFogField>(
                    FindObjectsInactive.Include),
                Is.Empty);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<CityLightHalo>(
                    FindObjectsInactive.Include),
                Is.Empty);
        }

        private static IEnumerator LoadSceneAndWaitForRoot<T>(
            string sceneName,
            Action<T> capture)
            where T : Component
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!operation.isDone &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(operation.isDone, Is.True);
            deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                T root = UnityEngine.Object.FindAnyObjectByType<T>();
                if (root != null)
                {
                    capture(root);
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"Scene '{sceneName}' did not create {typeof(T).Name}.");
        }
    }
}
