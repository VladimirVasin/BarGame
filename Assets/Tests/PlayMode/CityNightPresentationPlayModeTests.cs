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
            AssertCityDecorations(city.World);

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
            Transform plaza = city.World.ParkRoot.transform.Find(
                "Park Central Plaza");
            Assert.That(lawn, Is.Not.Null);
            Assert.That(plaza, Is.Not.Null);
            BoxCollider lawnCollider =
                lawn.GetComponent<BoxCollider>();
            MeshCollider plazaCollider =
                plaza.GetComponent<MeshCollider>();
            Assert.That(lawnCollider, Is.Not.Null);
            Assert.That(plazaCollider, Is.Not.Null);
            Assert.That(
                plazaCollider.sharedMesh,
                Is.SameAs(
                    plaza.GetComponent<MeshFilter>().sharedMesh));

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

        private static void AssertPlayerShadow(PlayerRuntime player)
        {
            Assert.That(player.Shadow, Is.Not.Null);
            Assert.That(player.Shadow.IsInitialized, Is.True);
            Assert.That(player.Shadow.MainLight, Is.SameAs(RenderSettings.sun));
            Assert.That(
                player.Shadow.Renderer.shadowCastingMode,
                Is.EqualTo(ShadowCastingMode.ShadowsOnly));
            Assert.That(player.Shadow.Renderer.receiveShadows, Is.False);
            Assert.That(
                player.Shadow.Renderer.sharedMaterial,
                Is.SameAs(PlayerShadowResources.ShadowCasterMaterial));
        }

        private static void AssertCityDecorations(
            CityWorldResult world)
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

            Assert.That(
                world.DecorationRoot.GetComponentsInChildren<Collider>(true),
                Is.Empty);
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
        }
    }
}
