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
            GameSessionState.BeginNewGame();
            CityGameRoot city = null;
            yield return LoadSceneAndWaitForRoot<CityGameRoot>(
                SceneIds.City,
                root => city = root);
            yield return null;

            Assert.That(city.IsInitialized, Is.True);
            Assert.That(city.PauseMenu, Is.Not.Null);
            Assert.That(city.PauseMenu.IsInitialized, Is.True);
            Assert.That(city.Inventory, Is.Not.Null);
            Assert.That(city.Inventory.IsInitialized, Is.True);
            Assert.That(
                GameAudioMixer.CurrentProfile,
                Is.EqualTo(GameAudioProfile.City));
            Assert.That(RenderSettings.fog, Is.True);
            Assert.That(
                RenderSettings.fogMode,
                Is.EqualTo(FogMode.ExponentialSquared));
            Assert.That(
                RenderSettings.fogDensity,
                Is.EqualTo(RuntimeSceneSetup.CityFogDensity).Within(0.0001f));
            Assert.That(RenderSettings.fogDensity, Is.InRange(0.065f, 0.075f));
            Assert.That(
                RenderSettings.fogColor.maxColorComponent,
                Is.GreaterThan(0.20f));
            Assert.That(Camera.main, Is.Not.Null);
            Assert.That(
                Camera.main.clearFlags,
                Is.EqualTo(CameraClearFlags.SolidColor));
            Assert.That(Camera.main.allowMSAA, Is.False);
            Assert.That(
                Camera.main.farClipPlane,
                Is.EqualTo(RuntimeSceneSetup.CityFarClipPlane).Within(0.01f));
            Assert.That(Camera.main.farClipPlane, Is.LessThanOrEqualTo(50f));
            Assert.That(
                Camera.main.backgroundColor,
                Is.EqualTo(RenderSettings.fogColor),
                "Empty pixels beyond the finite city must resolve to " +
                "terminal fog instead of exposing a dark world edge.");
            Assert.That(
                Camera.main.backgroundColor.maxColorComponent,
                Is.InRange(0.35f, 0.40f));
            Assert.That(RenderSettings.sun, Is.Not.Null);
            Assert.That(
                RenderSettings.sun.shadowStrength,
                Is.EqualTo(RuntimeSceneSetup.CityShadowStrength)
                    .Within(0.001f));
            Assert.That(
                RenderSettings.sun.shadows,
                Is.EqualTo(LightShadows.Hard));
            AssertPlayerShadow(city.Player);
            AssertCityDecorations(city.World, city.Layout);

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
            Assert.That(bloom.threshold.value, Is.EqualTo(0.60f));
            Assert.That(bloom.intensity.value, Is.EqualTo(0.62f));
            Assert.That(bloom.scatter.value, Is.EqualTo(0.48f));
            Assert.That(bloom.clamp.value, Is.EqualTo(10f));
            Assert.That(bloom.highQualityFiltering.value, Is.False);
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
            Assert.That(grain.intensity.value, Is.EqualTo(0.015f));

            CityNightWorldResult night = city.Night;
            Assert.That(night, Is.Not.Null);
            Assert.That(
                night.LampAnchors.Count,
                Is.EqualTo(night.Plan.StreetLamps.Count));
            Assert.That(night.LampAnchors.Count, Is.GreaterThan(0));
            Assert.That(night.TrafficSignals.Count, Is.GreaterThan(0));
            Collider[] nightColliders =
                night.Root.GetComponentsInChildren<Collider>(true);
            Assert.That(nightColliders, Is.Not.Empty);
            for (int index = 0; index < nightColliders.Length; index++)
            {
                Assert.That(nightColliders[index], Is.TypeOf<BoxCollider>());
                Assert.That(nightColliders[index].isTrigger, Is.False);
                Assert.That(
                    nightColliders[index].bounds.size.y,
                    Is.EqualTo(2.30f).Within(0.01f),
                    "Night fixtures must block only around their lower pole.");
            }

            CityNightAtmosphere atmosphere = night.Atmosphere;
            Assert.That(atmosphere, Is.Not.Null);
            Assert.That(night.FogField, Is.Not.Null);
            Assert.That(night.FogField.IsInitialized, Is.True);
            Assert.That(
                night.FogField.Particles.main.maxParticles,
                Is.EqualTo(CityFogField.MaximumParticles));
            Gradient fogVisibility = night.FogField.Particles
                .colorOverLifetime.color.gradient;
            Assert.That(
                fogVisibility.Evaluate(0.62f).a,
                Is.InRange(0.11f, 0.13f));
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
            int fixtureBatchCount = 0;
            int bulbBatchCount = 0;
            Material sharedFixtureMaterial = null;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer.name == "Street Lamp Fixtures")
                {
                    fixtureBatchCount++;
                    Assert.That(
                        renderer.transform.parent.name.StartsWith(
                            "Street Lamp Chunk ",
                            StringComparison.Ordinal),
                        Is.True);
                    Assert.That(
                        renderer.GetComponent<MeshFilter>().sharedMesh.name,
                        Is.EqualTo(
                            "Street Lamp Fixtures Combined Mesh"));
                    if (sharedFixtureMaterial == null)
                    {
                        sharedFixtureMaterial = renderer.sharedMaterial;
                    }

                    Assert.That(
                        renderer.sharedMaterial,
                        Is.SameAs(sharedFixtureMaterial));
                    continue;
                }

                if (renderer.name == "Street Lamp Bulbs")
                {
                    bulbBatchCount++;
                    Assert.That(
                        renderer.transform.parent.name.StartsWith(
                            "Street Lamp Chunk ",
                            StringComparison.Ordinal),
                        Is.True);
                    Assert.That(
                        renderer.GetComponent<MeshFilter>().sharedMesh.name,
                        Is.EqualTo(
                            "Street Lamp Bulbs Combined Mesh"));
                    Assert.That(
                        renderer.sharedMaterial,
                        Is.SameAs(sharedGlow));
                    continue;
                }

                Assert.That(
                    renderer.name,
                    Is.Not.EqualTo("Pole")
                        .And.Not.EqualTo("Lamp Arm")
                        .And.Not.EqualTo("Lamp Hood")
                        .And.Not.EqualTo("Glowing Bulb"));
            }

            Assert.That(fixtureBatchCount, Is.GreaterThan(0));
            Assert.That(
                bulbBatchCount,
                Is.EqualTo(fixtureBatchCount));
            Assert.That(
                bulbBatchCount,
                Is.LessThan(night.Plan.StreetLamps.Count));
            for (int index = 0;
                 index < night.LampAnchors.Count;
                 index++)
            {
                Transform anchor = night.LampAnchors[index];
                Assert.That(
                    anchor.name,
                    Does.StartWith("Street Lamp Anchor "));
                Assert.That(anchor.childCount, Is.Zero);
                Assert.That(anchor.GetComponent<Renderer>(), Is.Null);
            }

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
        public IEnumerator CityDayNight_ChangesLightingWithoutChangingFog()
        {
            Time.timeScale = 0f;
            GameSessionState.BeginNewGame();
            Assert.That(
                GameSessionState.TryStartGameTimeFromWake(),
                Is.True);

            CityGameRoot city = null;
            yield return LoadSceneAndWaitForRoot<CityGameRoot>(
                SceneIds.City,
                root => city = root);
            yield return null;

            Assert.That(city.DayNight, Is.Not.Null);
            Assert.That(city.DayNight.IsInitialized, Is.True);
            Assert.That(city.Night.FogField, Is.Not.Null);
            CityFogField fogField = city.Night.FogField;
            ParticleSystem fogParticles = fogField.Particles;
            Material fogMaterial = fogField.FogRenderer.sharedMaterial;
            bool fogEnabled = RenderSettings.fog;
            Color fogColor = RenderSettings.fogColor;
            FogMode fogMode = RenderSettings.fogMode;
            float fogDensity = RenderSettings.fogDensity;
            Camera cityCamera = Camera.main;
            Color backgroundColor = cityCamera.backgroundColor;
            float farClipPlane = cityCamera.farClipPlane;
            Light directional = RenderSettings.sun;
            float nightIntensity = directional.intensity;
            Color nightAmbient = RenderSettings.ambientLight;
            float nightReflection =
                RenderSettings.reflectionIntensity;
            Material bulbSharedMaterial =
                city.Night.StreetLampBulbRenderers[0].sharedMaterial;
            Assert.That(
                city.World.OpenAreaDecorationPlan.YardSpotlight.HasValue,
                Is.True);
            HomeYardSpotlightDescriptor yardDescriptor =
                city.World.OpenAreaDecorationPlan.YardSpotlight.Value;
            Light[] openAreaLights = city.World.OpenAreaDecorationRoot
                .GetComponentsInChildren<Light>(true);
            Assert.That(openAreaLights, Has.Length.EqualTo(1));
            Light yardSpotlight = openAreaLights[0];
            Assert.That(
                yardSpotlight.transform.IsChildOf(
                    city.Night.Root.transform),
                Is.False,
                "The permanent yard light must stay outside NightFactor.");
            if (city.YardWheelchair != null)
            {
                Assert.That(
                    yardSpotlight.transform.IsChildOf(
                        city.YardWheelchair.transform),
                    Is.False,
                    "The passive wheelchair prefab must not own the light.");
            }

            AssertYardSpotlightMatchesPlan(
                yardSpotlight,
                yardDescriptor);

            GameSessionState.AdvanceGameTime(360f);
            city.DayNight.ApplyCurrentTime();

            Assert.That(GameSessionState.GameHour, Is.EqualTo(12));
            Assert.That(GameSessionState.GameMinute, Is.EqualTo(0));
            Assert.That(
                directional.intensity,
                Is.GreaterThan(nightIntensity));
            Assert.That(
                RenderSettings.ambientLight,
                Is.Not.EqualTo(nightAmbient));
            Assert.That(
                RenderSettings.reflectionIntensity,
                Is.GreaterThan(nightReflection));
            Assert.That(city.Night.NightFactor, Is.EqualTo(0f));
            Assert.That(
                city.Night.Atmosphere.NightFactor,
                Is.EqualTo(0f));
            Light[] dayNightLights = city.Night.Root
                .GetComponentsInChildren<Light>(true);
            Assert.That(
                dayNightLights,
                Has.Length.EqualTo(
                    city.Night.Atmosphere.RealtimeLightCount));
            for (int index = 0; index < dayNightLights.Length; index++)
            {
                Light light = dayNightLights[index];
                Assert.That(light.intensity, Is.EqualTo(0f));
                Assert.That(light.enabled, Is.False);
                CityLightHalo halo =
                    light.GetComponentInChildren<CityLightHalo>(true);
                Assert.That(halo.IntensityFactor, Is.EqualTo(0f));
                Assert.That(halo.IsVisible, Is.False);
            }
            CollectionAssert.DoesNotContain(
                dayNightLights,
                yardSpotlight);
            AssertYardSpotlightMatchesPlan(
                yardSpotlight,
                yardDescriptor);

            Renderer bulb = city.Night.StreetLampBulbRenderers[0];
            Assert.That(
                bulb.sharedMaterial,
                Is.SameAs(bulbSharedMaterial));
            var bulbProperties = new MaterialPropertyBlock();
            bulb.GetPropertyBlock(bulbProperties);
            Color dayBulbColor = bulbProperties.GetColor(
                Shader.PropertyToID("_BaseColor"));
            Assert.That(
                dayBulbColor.maxColorComponent,
                Is.LessThan(0.001f));

            int stableDayApplicationCount =
                city.DayNight.VisualApplicationCount;
            int darkPoolReassignmentCount =
                city.Night.Atmosphere.ReassignmentCount;
            directional.transform.hasChanged = false;
            GameSessionState.AdvanceGameTime(1f);
            city.DayNight.ApplyCurrentTime();
            city.Night.Atmosphere.RefreshImmediate();
            Assert.That(
                city.DayNight.AppliedMinute,
                Is.EqualTo(12 * 60 + 1));
            Assert.That(
                city.DayNight.VisualApplicationCount,
                Is.EqualTo(stableDayApplicationCount));
            Assert.That(directional.transform.hasChanged, Is.False);
            Assert.That(
                city.Night.Atmosphere.ReassignmentCount,
                Is.EqualTo(darkPoolReassignmentCount));

            city.Night.SetNightFactor(0.000095f);
            Assert.That(
                city.Night.Atmosphere.BarLights[0].enabled,
                Is.False);
            int thresholdReassignmentCount =
                city.Night.Atmosphere.ReassignmentCount;
            city.Night.SetNightFactor(0.000104f);
            Assert.That(
                city.Night.Atmosphere.BarLights[0].enabled,
                Is.True);
            Assert.That(
                city.Night.Atmosphere.ReassignmentCount,
                Is.GreaterThan(thresholdReassignmentCount));
            city.Night.SetNightFactor(0f, true);

            TrafficSignalController signal =
                city.Night.TrafficSignals[0];
            signal.ApplyTime(-signal.PhaseOffset);
            Assert.That(signal.IsLit, Is.True);
            Assert.That(signal.AmberHalos[0].IsVisible, Is.True);
            AssertCityFogUnchanged(
                city,
                fogField,
                fogParticles,
                fogMaterial,
                fogEnabled,
                fogColor,
                fogMode,
                fogDensity,
                cityCamera,
                backgroundColor,
                farClipPlane);

            GameSessionState.AdvanceGameTime(479f);
            city.DayNight.ApplyCurrentTime();

            Assert.That(GameSessionState.GameHour, Is.EqualTo(20));
            Assert.That(GameSessionState.GameMinute, Is.EqualTo(0));
            Assert.That(
                directional.intensity,
                Is.EqualTo(nightIntensity).Within(0.0001f));
            Assert.That(
                RenderSettings.ambientLight,
                Is.EqualTo(nightAmbient));
            Assert.That(
                RenderSettings.reflectionIntensity,
                Is.EqualTo(nightReflection).Within(0.0001f));
            Assert.That(city.Night.NightFactor, Is.EqualTo(1f));
            Assert.That(
                city.Night.Atmosphere.NightFactor,
                Is.EqualTo(1f));
            Assert.That(
                city.Night.Atmosphere.BarLights[0].enabled,
                Is.True);
            AssertYardSpotlightMatchesPlan(
                yardSpotlight,
                yardDescriptor);
            Assert.That(
                city.Night.Atmosphere.ReassignmentCount,
                Is.GreaterThan(darkPoolReassignmentCount));
            CityLightHalo restoredBarHalo =
                city.Night.Atmosphere.BarLights[0]
                    .GetComponentInChildren<CityLightHalo>(true);
            Assert.That(restoredBarHalo.IntensityFactor, Is.EqualTo(1f));
            Assert.That(restoredBarHalo.IsVisible, Is.True);
            AssertCityFogUnchanged(
                city,
                fogField,
                fogParticles,
                fogMaterial,
                fogEnabled,
                fogColor,
                fogMode,
                fogDensity,
                cityCamera,
                backgroundColor,
                farClipPlane);
        }

        [UnityTearDown]
        public IEnumerator RestoreTimeScale()
        {
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator City_PlayerStepsFromParkLawnOntoRaisedPath()
        {
            CityGameRoot city = null;
            yield return LoadSceneAndWaitForRoot<CityGameRoot>(
                SceneIds.City,
                root => city = root);
            yield return null;

            Assert.That(city.IsInitialized, Is.True);
            Transform roadNetwork =
                city.World.Root.transform.Find("Road Network");
            Assert.That(roadNetwork, Is.Not.Null);
            MeshCollider[] roadColliders =
                roadNetwork.GetComponentsInChildren<MeshCollider>(true);
            int streetColliderCount = 0;
            int parkPathColliderCount = 0;
            for (int index = 0;
                 index < roadColliders.Length;
                 index++)
            {
                MeshCollider roadCollider = roadColliders[index];
                if (roadCollider.name == "Street Surfaces")
                {
                    streetColliderCount++;
                }
                else if (roadCollider.name == "Park Paths")
                {
                    parkPathColliderCount++;
                }
                else
                {
                    continue;
                }

                Renderer renderer =
                    roadCollider.GetComponent<Renderer>();
                Assert.That(renderer, Is.Not.Null);
                Assert.That(
                    roadCollider.sharedMesh,
                    Is.SameAs(
                        roadCollider.GetComponent<MeshFilter>()
                            .sharedMesh));
                Assert.That(
                    roadCollider.bounds.max.y,
                    Is.EqualTo(renderer.bounds.max.y)
                        .Within(0.001f));
            }

            Assert.That(streetColliderCount, Is.GreaterThan(0));
            Assert.That(parkPathColliderCount, Is.GreaterThan(0));

            Transform lawn =
                city.World.ParkRoot.transform.Find("Park Lawn");
            Assert.That(lawn, Is.Not.Null);
            BoxCollider lawnCollider =
                lawn.GetComponent<BoxCollider>();
            Assert.That(lawnCollider, Is.Not.Null);
            for (int plazaIndex = 1; plazaIndex <= 2; plazaIndex++)
            {
                Transform plaza = city.World.ParkRoot.transform.Find(
                    $"Park Plaza {plazaIndex}");
                Assert.That(plaza, Is.Not.Null);
                MeshCollider plazaCollider =
                    plaza.GetComponent<MeshCollider>();
                Assert.That(plazaCollider, Is.Not.Null);
                Assert.That(
                    plazaCollider.sharedMesh,
                    Is.SameAs(
                        plaza.GetComponent<MeshFilter>().sharedMesh));
            }

            CharacterController controller =
                city.Player.GameObject.GetComponent<
                    CharacterController>();
            Assert.That(controller, Is.Not.Null);

            bool foundSample = false;
            RoadEdge sampleEdge = default;
            Vector3 sampleCenter = Vector3.zero;
            for (int index = 0;
                 index < city.Layout.RoadEdges.Count;
                 index++)
            {
                RoadEdge edge = city.Layout.RoadEdges[index];
                if (city.Layout.GetPathKind(edge) !=
                    CityPathKind.ParkPath)
                {
                    continue;
                }

                Vector3 center =
                    (city.Layout.GetNodeWorldPosition(edge.A) +
                     city.Layout.GetNodeWorldPosition(edge.B)) *
                    0.5f;
                bool liesOnCentralCross =
                    Mathf.Abs(
                        center.x -
                        city.Layout.Park.Center.x) < 0.01f ||
                    Mathf.Abs(
                        center.z -
                        city.Layout.Park.Center.z) < 0.01f;
                Vector3 centerOffset =
                    center - city.Layout.Park.Center;
                if (!liesOnCentralCross ||
                    centerOffset.sqrMagnitude < 11f * 11f ||
                    !city.Layout.Park.WalkableBounds.Contains(
                        new Vector2(center.x, center.z)))
                {
                    continue;
                }

                foundSample = true;
                sampleEdge = edge;
                sampleCenter = center;
                break;
            }

            Assert.That(
                foundSample,
                Is.True,
                "The park needs a raised path sample away from its plaza.");

            Vector3 perpendicular = sampleEdge.IsHorizontal
                ? Vector3.forward
                : Vector3.right;
            float startOffset =
                CityGenerationSettings.Default.RoadWidth * 0.5f +
                controller.radius +
                1.25f;
            Vector3 lawnStart =
                sampleCenter + perpendicular * startOffset;
            Rect walkableBounds =
                city.Layout.Park.WalkableBounds;
            if (!walkableBounds.Contains(
                    new Vector2(lawnStart.x, lawnStart.z)))
            {
                perpendicular = -perpendicular;
                lawnStart =
                    sampleCenter + perpendicular * startOffset;
            }

            Assert.That(
                walkableBounds.Contains(
                    new Vector2(lawnStart.x, lawnStart.z)),
                Is.True);
            Assert.That(
                city.World.WalkableArea.Contains(
                    lawnStart,
                    controller.radius),
                Is.True);
            Assert.That(
                0.08f - lawnCollider.bounds.max.y,
                Is.LessThan(controller.stepOffset));

            Physics.SyncTransforms();
            RaycastHit[] lawnHits = Physics.RaycastAll(
                lawnStart + Vector3.up * 3f,
                Vector3.down,
                6f);
            Collider topSurface = null;
            float topSurfaceY = float.NegativeInfinity;
            for (int index = 0;
                 index < lawnHits.Length;
                 index++)
            {
                Collider hitCollider = lawnHits[index].collider;
                if (hitCollider == null ||
                    hitCollider.isTrigger ||
                    hitCollider == controller ||
                    hitCollider.bounds.max.y <= topSurfaceY)
                {
                    continue;
                }

                topSurface = hitCollider;
                topSurfaceY = hitCollider.bounds.max.y;
            }

            Assert.That(
                topSurface,
                Is.SameAs(lawnCollider),
                $"Expected a lawn start, but the highest surface was " +
                $"'{topSurface?.name}' at y={topSurfaceY:0.###}.");

            city.Player.Motor.enabled = false;
            city.Player.Motor.Teleport(
                lawnStart + Vector3.up);
            Physics.SyncTransforms();
            controller.Move(Vector3.down * 2f);
            Physics.SyncTransforms();
            Assert.That(
                controller.transform.position.y,
                Is.EqualTo(
                    lawnCollider.bounds.max.y +
                    controller.skinWidth)
                    .Within(0.02f),
                "The controller must first settle on the park lawn.");

            for (int step = 0; step < 24; step++)
            {
                controller.Move(
                    -perpendicular * 0.08f +
                    Vector3.down * 0.02f);
            }

            Physics.SyncTransforms();
            Assert.That(
                controller.transform.position.y,
                Is.EqualTo(0.08f + controller.skinWidth)
                    .Within(0.02f),
                "The controller must climb onto the raised park path " +
                "instead of passing through it.");
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
            Assert.That(interior.PauseMenu, Is.Not.Null);
            Assert.That(interior.PauseMenu.IsInitialized, Is.True);
            Assert.That(interior.Inventory, Is.Not.Null);
            Assert.That(interior.Inventory.IsInitialized, Is.True);
            if (interior.ArrivalPresentation.IsPlaying)
            {
                Assert.That(
                    interior.PauseMenu.Open(),
                    Is.False,
                    "The pause menu must not skip the arrival reveal.");
            }
            Assert.That(
                GameAudioMixer.CurrentProfile,
                Is.EqualTo(GameAudioProfile.Bar));
            Assert.That(RenderSettings.fog, Is.False);
            Assert.That(Camera.main, Is.Not.Null);
            Assert.That(
                Camera.main.farClipPlane,
                Is.EqualTo(RuntimeSceneSetup.DefaultFarClipPlane)
                    .Within(0.01f));
            AssertPlayerShadow(interior.Player);
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

        private static void AssertCityFogUnchanged(
            CityGameRoot city,
            CityFogField fogField,
            ParticleSystem fogParticles,
            Material fogMaterial,
            bool fogEnabled,
            Color fogColor,
            FogMode fogMode,
            float fogDensity,
            Camera cityCamera,
            Color backgroundColor,
            float farClipPlane)
        {
            Assert.That(city.Night.FogField, Is.SameAs(fogField));
            Assert.That(fogField.Particles, Is.SameAs(fogParticles));
            Assert.That(
                fogField.FogRenderer.sharedMaterial,
                Is.SameAs(fogMaterial));
            Assert.That(RenderSettings.fog, Is.EqualTo(fogEnabled));
            Assert.That(RenderSettings.fogColor, Is.EqualTo(fogColor));
            Assert.That(RenderSettings.fogMode, Is.EqualTo(fogMode));
            Assert.That(
                RenderSettings.fogDensity,
                Is.EqualTo(fogDensity).Within(0.0001f));
            Assert.That(Camera.main, Is.SameAs(cityCamera));
            Assert.That(
                cityCamera.backgroundColor,
                Is.EqualTo(backgroundColor));
            Assert.That(
                cityCamera.farClipPlane,
                Is.EqualTo(farClipPlane).Within(0.0001f));
        }

        private static void AssertPlayerShadow(PlayerRuntime player)
        {
            Assert.That(
                player.Visual,
                Is.TypeOf<Player3DCharacterPresentation>());
            Assert.That(player.Visual.Renderers, Is.Not.Empty);
            for (int index = 0;
                 index < player.Visual.Renderers.Count;
                 index++)
            {
                Renderer renderer = player.Visual.Renderers[index];
                Assert.That(renderer, Is.Not.Null);
                Assert.That(
                    renderer.shadowCastingMode,
                    Is.EqualTo(ShadowCastingMode.On));
            }

            Assert.That(player.ContactShadow, Is.Not.Null);
            Assert.That(player.ContactShadow.IsInitialized, Is.True);
            Assert.That(
                player.ContactShadow.Renderer.sharedMaterial,
                Is.SameAs(PlayerShadowResources.ContactShadowMaterial));
        }

        private static void AssertCityDecorations(
            CityWorldResult world,
            CityLayout layout)
        {
            Assert.That(world, Is.Not.Null);
            Assert.That(world.DecorationPlan, Is.Not.Null);
            Assert.That(world.DecorationRoot, Is.Not.Null);
            Assert.That(
                world.DecorationPlan.Count,
                Is.GreaterThan(120).And.LessThanOrEqualTo(
                    CityDecorationPlan.MaximumDescriptorCount));
            Assert.That(
                world.DecorationPlan.GetCount(
                    CityDecorationAnchorKind.UrbanLandmark),
                Is.EqualTo(4));
            Assert.That(
                world.DecorationPlan.GetCount(
                    CityDecorationAnchorKind.ParkLandmark),
                Is.EqualTo(2));
            Assert.That(
                world.DecorationRoot.transform.parent,
                Is.SameAs(world.Root.transform));

            Renderer[] renderers =
                world.DecorationRoot.GetComponentsInChildren<Renderer>(
                    true);
            Assert.That(renderers, Is.Not.Empty);
            Assert.That(
                renderers.Length,
                Is.LessThan(world.DecorationPlan.Count),
                "Static city decorations must remain spatially batched.");
            for (int index = 0; index < renderers.Length; index++)
            {
                Material material = renderers[index].sharedMaterial;
                Assert.That(material, Is.Not.Null);
                Assert.That(
                    material == RuntimePrimitiveFactory.DefaultMaterial ||
                    material == CityNightResources.EmissiveMaterial,
                    Is.True,
                    $"'{renderers[index].name}' must reuse a packaged " +
                    "shared decoration material.");
                Assert.That(
                    renderers[index].shadowCastingMode,
                    Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(renderers[index].receiveShadows, Is.False);
            }

            Collider[] decorationColliders =
                world.DecorationRoot.GetComponentsInChildren<Collider>(true);
            Assert.That(decorationColliders, Is.Not.Empty);
            Assert.That(
                decorationColliders.Length,
                Is.LessThanOrEqualTo(
                    world.DecorationPlan.Count *
                    CityStaticCollisionBuilder.MaximumDecorationProxyCount));
            for (int index = 0;
                 index < decorationColliders.Length;
                 index++)
            {
                Assert.That(
                    decorationColliders[index],
                    Is.TypeOf<BoxCollider>());
                Assert.That(decorationColliders[index].isTrigger, Is.False);
                Assert.That(
                    decorationColliders[index].transform.name,
                    Does.StartWith("City Detail Chunk "),
                    "Logical collision proxies must stay on batched chunks.");
            }
            Assert.That(
                world.DecorationRoot.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                world.DecorationRoot.GetComponentsInChildren<AudioSource>(
                    true),
                Is.Empty);
            Assert.That(
                world.DecorationRoot.GetComponentsInChildren<ParticleSystem>(
                    true),
                Is.Empty);

            AssertDistrictPointsOfInterest(world, layout);
        }

        private static void AssertDistrictPointsOfInterest(
            CityWorldResult world,
            CityLayout layout)
        {
            GameObject root = world.DistrictPointOfInterestRoot;
            Assert.That(root, Is.Not.Null);
            Assert.That(
                root.transform.parent,
                Is.SameAs(world.Root.transform));
            Assert.That(
                layout.DistrictPointsOfInterest.Count,
                Is.EqualTo(4));
            Assert.That(
                root.transform.childCount,
                Is.EqualTo(
                    layout.DistrictPointsOfInterest.Count));

            string[] recipeNames =
            {
                "Old Town Waterworks Court",
                "Residential Drying Yard",
                "Industrial Weighbridge",
                "Nightlife Last Route Island"
            };
            int publicGroundCount = 0;
            int recipeCount = 0;
            Transform[] transforms =
                root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                Transform item = transforms[index];
                Assert.That(
                    item.name,
                    Is.Not.EqualTo("Building Mass"));
                Assert.That(
                    item.name.IndexOf(
                        "Fence",
                        StringComparison.OrdinalIgnoreCase),
                    Is.LessThan(0));
                Assert.That(
                    item.name.IndexOf(
                        "Gate",
                        StringComparison.OrdinalIgnoreCase),
                    Is.LessThan(0));
                if (item.name ==
                    CityDistrictPointOfInterestWorldBuilder
                        .PublicGroundName)
                {
                    publicGroundCount++;
                    Assert.That(
                        item.GetComponent<BoxCollider>(),
                        Is.Not.Null);
                }

                for (int recipe = 0;
                     recipe < recipeNames.Length;
                     recipe++)
                {
                    if (item.name == recipeNames[recipe])
                    {
                        recipeCount++;
                    }
                }
            }

            Assert.That(publicGroundCount, Is.EqualTo(4));
            Assert.That(recipeCount, Is.EqualTo(4));
            for (int descriptorIndex = 0;
                 descriptorIndex <
                 layout.DistrictPointsOfInterest.Count;
                 descriptorIndex++)
            {
                CityDistrictPointOfInterestDescriptor descriptor =
                    layout.DistrictPointsOfInterest[descriptorIndex];
                Transform site = root.transform.Find(
                    CityDistrictPointOfInterestWorldBuilder
                        .GetSiteName(descriptor.Id));
                Assert.That(site, Is.Not.Null);
                Transform ground = site.Find(
                    CityDistrictPointOfInterestWorldBuilder
                        .PublicGroundName);
                Assert.That(ground, Is.Not.Null);
                Renderer groundRenderer =
                    ground.GetComponent<Renderer>();
                Assert.That(groundRenderer, Is.Not.Null);
                Assert.That(
                    groundRenderer.bounds.center.x,
                    Is.EqualTo(descriptor.PublicBounds.center.x)
                        .Within(0.01f));
                Assert.That(
                    groundRenderer.bounds.center.z,
                    Is.EqualTo(descriptor.PublicBounds.center.y)
                        .Within(0.01f));
                Assert.That(
                    groundRenderer.bounds.size.x,
                    Is.EqualTo(descriptor.PublicBounds.width)
                        .Within(0.01f));
                Assert.That(
                    groundRenderer.bounds.size.z,
                    Is.EqualTo(descriptor.PublicBounds.height)
                        .Within(0.01f));
                AssertApproachesHaveNoSolidObstacle(
                    site,
                    descriptor);
                AssertReadableDetailsFacePrimaryStreet(
                    site,
                    descriptor);
            }

            Assert.That(
                root.GetComponentsInChildren<Renderer>(true),
                Is.Not.Empty);
            Assert.That(
                root.GetComponentsInChildren<Collider>(true).Length,
                Is.GreaterThan(4));
            Assert.That(
                root.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                root.GetComponentsInChildren<AudioSource>(true),
                Is.Empty);
            Assert.That(
                root.GetComponentsInChildren<ParticleSystem>(true),
                Is.Empty);
        }

        private static void AssertReadableDetailsFacePrimaryStreet(
            Transform site,
            CityDistrictPointOfInterestDescriptor descriptor)
        {
            Transform recipe = site.Find(
                CityDistrictPointOfInterestWorldBuilder.GetRecipeName(
                    descriptor.Kind));
            Assert.That(recipe, Is.Not.Null);
            Vector3 primaryStreetDirection =
                descriptor.Accesses[0].Center - descriptor.Center;
            primaryStreetDirection.y = 0f;
            Assert.That(
                Vector3.Dot(
                    recipe.forward,
                    primaryStreetDirection.normalized),
                Is.GreaterThan(0.99f),
                "Recipe +Z must face its primary street.");

            switch (descriptor.Kind)
            {
                case CityDistrictPointOfInterestKind
                    .OldTownWaterworksCourt:
                    AssertDetailFacesPositiveLocalZ(
                        recipe,
                        "Cast Iron Standpipe",
                        "Water Spout Mouth");
                    break;
                case CityDistrictPointOfInterestKind
                    .IndustrialWeighbridge:
                    AssertDetailFacesPositiveLocalZ(
                        recipe,
                        "Scale Indicator Head",
                        "Scale Indicator Face");
                    break;
                case CityDistrictPointOfInterestKind
                    .NightlifeLastRouteIsland:
                    AssertDetailFacesPositiveLocalZ(
                        recipe,
                        "Broken Route Totem",
                        "Totem Route Map Backing");
                    AssertDetailFacesPositiveLocalZ(
                        recipe,
                        "Departure Board",
                        "Departure Board Glass");
                    AssertLastRouteIslandDetails(recipe);
                    break;
            }
        }

        private static void AssertYardSpotlightMatchesPlan(
            Light light,
            HomeYardSpotlightDescriptor descriptor)
        {
            Assert.That(light, Is.Not.Null);
            Assert.That(light.enabled, Is.True);
            Assert.That(light.type, Is.EqualTo(LightType.Spot));
            Assert.That(light.color, Is.EqualTo(descriptor.Color));
            Assert.That(
                light.intensity,
                Is.EqualTo(descriptor.Intensity).Within(0.001f));
            Assert.That(
                light.range,
                Is.EqualTo(descriptor.Range).Within(0.001f));
            Assert.That(
                light.spotAngle,
                Is.EqualTo(descriptor.SpotAngle).Within(0.001f));
            Assert.That(
                light.innerSpotAngle,
                Is.EqualTo(descriptor.InnerSpotAngle).Within(0.001f));
            Assert.That(
                Vector3.Distance(
                    light.transform.position,
                    descriptor.MountPosition),
                Is.LessThan(0.001f));
            Assert.That(
                Vector3.Angle(
                    light.transform.forward,
                    (descriptor.TargetPosition -
                     descriptor.MountPosition).normalized),
                Is.LessThan(0.01f));
            CityLightHalo halo =
                light.GetComponentInChildren<CityLightHalo>(true);
            Assert.That(halo, Is.Not.Null);
            Assert.That(halo.IntensityFactor, Is.EqualTo(1f));
            Assert.That(halo.IsVisible, Is.True);
        }

        private static void AssertLastRouteIslandDetails(
            Transform recipe)
        {
            string[] removedEmissiveParts =
            {
                "Totem Cyan Half",
                "Totem Magenta Half",
                "Departure Board Line"
            };
            for (int index = 0;
                 index < removedEmissiveParts.Length;
                 index++)
            {
                Assert.That(
                    recipe.Find(removedEmissiveParts[index]),
                    Is.Null,
                    removedEmissiveParts[index]);
            }

            for (int segment = 1; segment <= 5; segment++)
            {
                Assert.That(
                    recipe.Find(
                        $"Broken Canopy Segment {segment} " +
                        "Dead Route Strip"),
                    Is.Null,
                    "The last-route canopy must not retain neon strips.");
            }

            Renderer[] renderers =
                recipe.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            for (int index = 0; index < renderers.Length; index++)
            {
                Assert.That(
                    renderers[index].sharedMaterial,
                    Is.Not.SameAs(
                        CityNightResources.EmissiveMaterial),
                    $"'{renderers[index].name}' must be non-emissive.");
            }

            Transform board = AssertRequiredChild(
                recipe,
                "Departure Board");
            Renderer boardRenderer = board.GetComponent<Renderer>();
            Assert.That(boardRenderer, Is.Not.Null);
            string[] supports =
            {
                "Departure Board Support West",
                "Departure Board Support East"
            };
            Renderer islandRenderer = AssertRequiredChild(
                    recipe,
                    "Last Route Island")
                .GetComponent<Renderer>();
            Assert.That(islandRenderer, Is.Not.Null);
            for (int index = 0; index < supports.Length; index++)
            {
                Renderer supportRenderer = AssertRequiredChild(
                        recipe,
                        supports[index])
                    .GetComponent<Renderer>();
                Assert.That(supportRenderer, Is.Not.Null);
                Assert.That(
                    supportRenderer.bounds.max.y,
                    Is.EqualTo(boardRenderer.bounds.min.y)
                        .Within(0.02f),
                    $"'{supports[index]}' must meet the board.");
                Assert.That(
                    supportRenderer.bounds.min.y,
                    Is.EqualTo(islandRenderer.bounds.max.y)
                        .Within(0.02f),
                    $"'{supports[index]}' must rest on the island.");
            }

            string[] addedDetails =
            {
                "Totem Torn Poster A",
                "Totem Torn Poster B",
                "Departure Schedule Row A",
                "Departure Schedule Row B",
                "Departure Schedule Row C",
                "Departure Board Foot West",
                "Departure Board Foot East",
                "Island Waste Bin",
                "Discarded Bottle Standing",
                "Discarded Bottle Fallen",
                "Lost Scarf",
                "Discarded Timetable"
            };
            for (int index = 0; index < addedDetails.Length; index++)
            {
                AssertRequiredChild(recipe, addedDetails[index]);
            }
        }

        private static Transform AssertRequiredChild(
            Transform parent,
            string childName)
        {
            Transform child = parent.Find(childName);
            Assert.That(child, Is.Not.Null, childName);
            return child;
        }

        private static void AssertDetailFacesPositiveLocalZ(
            Transform recipe,
            string bodyName,
            string detailName)
        {
            Transform body = recipe.Find(bodyName);
            Transform detail = recipe.Find(detailName);
            Assert.That(body, Is.Not.Null, bodyName);
            Assert.That(detail, Is.Not.Null, detailName);
            Assert.That(
                detail.localPosition.z,
                Is.GreaterThan(body.localPosition.z),
                $"'{detailName}' must face the recipe's primary-street side.");
        }

        private static void AssertApproachesHaveNoSolidObstacle(
            Transform site,
            CityDistrictPointOfInterestDescriptor descriptor)
        {
            Collider[] colliders =
                site.GetComponentsInChildren<Collider>(true);
            for (int accessIndex = 0;
                 accessIndex < descriptor.Accesses.Count;
                 accessIndex++)
            {
                Rect approach =
                    descriptor.Accesses[accessIndex].ApproachBounds;
                for (int colliderIndex = 0;
                     colliderIndex < colliders.Length;
                     colliderIndex++)
                {
                    Collider collider = colliders[colliderIndex];
                    if (collider.name ==
                        CityDistrictPointOfInterestWorldBuilder
                            .PublicGroundName)
                    {
                        continue;
                    }

                    Bounds bounds = collider.bounds;
                    var footprint = Rect.MinMaxRect(
                        bounds.min.x,
                        bounds.min.z,
                        bounds.max.x,
                        bounds.max.z);
                    Assert.That(
                        footprint.Overlaps(approach),
                        Is.False,
                        $"'{collider.name}' blocks public access " +
                        $"'{descriptor.Accesses[accessIndex].Id}'.");
                }
            }
        }
    }
}
