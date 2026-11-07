using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    [Category("CityFringeYard")]
    public sealed class CityNightAtmosphereTests
    {
        [Test]
        public void FringePracticals_KeepFixedSourcesAtAnyDistanceWithDayFloorAndCleanup()
        {
            var owner = new GameObject("Fixed Night Atmosphere Test");
            try
            {
                Transform player = CreateAnchor(owner.transform, "Player", Vector3.zero, Vector3.forward);
                List<Transform> streetAnchors = CreateStreetAnchors(owner.transform);
                List<Vector3> barPositions = CreateBarPositions();
                List<CityFringePracticalAnchor> practicals = CreatePracticalAnchors(owner.transform);
                var nightRoot = new GameObject("Night With Practicals");
                nightRoot.transform.SetParent(owner.transform, false);
                CityNightAtmosphere atmosphere = nightRoot.AddComponent<CityNightAtmosphere>();
                atmosphere.Initialize(player, streetAnchors, barPositions, practicals);
                AssertFixedStreetAssignment(atmosphere, nightRoot, streetAnchors, practicals.Count);

                var sources = new List<Light>(atmosphere.StreetLightPool);
                var positions = new Dictionary<Light, Vector3>();
                var rotations = new Dictionary<Light, Quaternion>();
                var nightIntensities = new Dictionary<Light, float>();
                Light[] allLights = nightRoot.GetComponentsInChildren<Light>(true);
                foreach (Light light in allLights)
                {
                    positions.Add(light, light.transform.position);
                    rotations.Add(light, light.transform.rotation);
                    nightIntensities.Add(light, light.intensity);
                    Assert.That(light.enabled, Is.True);
                }
                foreach (CityFringePracticalAnchor practical in practicals)
                {
                    Light fixedLight = atmosphere.GetPracticalLight(practical.Kind);
                    AssertPracticalProfile(practical, fixedLight);
                    player.position = practical.Anchor.position;
                    atmosphere.RefreshImmediate();
                    Assert.That(atmosphere.ActivePracticalKind, Is.EqualTo(practical.Kind),
                        "The nearest-practical diagnostic remains useful without controlling illumination.");
                    Assert.That(atmosphere.ActivePracticalLight, Is.SameAs(fixedLight));
                    Assert.That(atmosphere.AssignedStreetLightCount, Is.EqualTo(streetAnchors.Count),
                        "A practical cannot borrow or extinguish any street fixture.");
                    Assert.That(atmosphere.StreetLightPool, Is.EqualTo(sources));
                }

                // The old nearest pool left distant streets dark. Move beyond
                // every old lease distance and verify the actual sources stay on.
                player.position = new Vector3(10000f, 300f, -10000f);
                atmosphere.RefreshImmediate();
                Assert.That(atmosphere.IsPracticalSlotLeased, Is.False);
                Assert.That(atmosphere.ActivePracticalLight, Is.Null);
                Assert.That(atmosphere.ReassignmentCount, Is.Zero);
                foreach (Light light in allLights)
                {
                    Assert.That(light.enabled, Is.True, "Distance must not switch a built fixture off.");
                    Assert.That(light.transform.position, Is.EqualTo(positions[light]));
                    Assert.That(Quaternion.Angle(light.transform.rotation, rotations[light]), Is.LessThan(.001f));
                    Assert.That(light.intensity, Is.EqualTo(nightIntensities[light]));
                }

                Light tunnel = atmosphere.GetPracticalLight(CityFringeYardKind.SouthTunnelForecourt);
                CityLightHalo tunnelHalo = tunnel.GetComponentInChildren<CityLightHalo>(true);
                atmosphere.SetTunnelPracticalFlickerMultiplier(.4f);
                Assert.That(tunnel.intensity, Is.EqualTo(150f * .4f).Within(.001f),
                    "The distant tunnel fault addresses its own fixed source, without a nearby lease.");
                Assert.That(tunnelHalo.IntensityFactor, Is.EqualTo(.4f).Within(.001f));
                foreach (CityFringePracticalAnchor practical in practicals)
                    if (practical.Kind != CityFringeYardKind.SouthTunnelForecourt)
                        AssertPracticalProfile(practical, atmosphere.GetPracticalLight(practical.Kind));
                atmosphere.SetTunnelPracticalFlickerMultiplier(1f);

                atmosphere.SetNightFactor(0f);
                float floor = GameTimeDayNightRules.DayFixtureFloor;
                foreach (Light light in allLights)
                {
                    Assert.That(light.enabled, Is.True, "Noon retains every distant fixture.");
                    Assert.That(light.intensity, Is.EqualTo(nightIntensities[light] * floor).Within(.001f));
                }
                Assert.That(tunnelHalo.IntensityFactor, Is.EqualTo(floor).Within(.001f));
                Assert.That(tunnelHalo.IsVisible, Is.True);
                atmosphere.SetTunnelPracticalFlickerMultiplier(.4f);
                Assert.That(tunnel.intensity, Is.EqualTo(150f * floor * .4f).Within(.001f));
                Assert.That(tunnelHalo.IntensityFactor, Is.EqualTo(floor * .4f).Within(.001f));

                atmosphere.enabled = false;
                foreach (Light light in allLights) Assert.That(light.enabled, Is.False);
                Assert.That(tunnelHalo.IsVisible, Is.False);
                atmosphere.SetTunnelPracticalFlickerMultiplier(.6f);
                Assert.That(tunnel.enabled, Is.False, "A fault update must not revive a disabled exterior.");
                atmosphere.enabled = true;
                foreach (Light light in allLights) Assert.That(light.enabled, Is.True);
                Assert.That(atmosphere.StreetLightPool, Is.EqualTo(sources));
                Assert.That(tunnel.intensity, Is.EqualTo(150f * floor * .6f).Within(.001f));
                atmosphere.SetTunnelPracticalFlickerMultiplier(1f);
                atmosphere.SetNightFactor(1f);
                foreach (Light light in allLights)
                    Assert.That(light.intensity, Is.EqualTo(nightIntensities[light]).Within(.001f));

                Object.DestroyImmediate(nightRoot);
                foreach (Light light in allLights) Assert.That(light == null, Is.True);
                var noFringeRoot = new GameObject("Night Without Practicals");
                noFringeRoot.transform.SetParent(owner.transform, false);
                CityNightAtmosphere noFringe = noFringeRoot.AddComponent<CityNightAtmosphere>();
                noFringe.Initialize(player, streetAnchors, barPositions);
                AssertFixedStreetAssignment(noFringe, noFringeRoot, streetAnchors, 0);
                Assert.That(noFringe.GetPracticalLight(CityFringeYardKind.SouthTunnelForecourt), Is.Null);
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void QuayLampAnchors_KeepLowWideProfileAndIdentityAtAnyDistance()
        {
            var owner = new GameObject("Fixed Quay Lamp Test");
            try
            {
                Transform player = CreateAnchor(owner.transform, "Player", new Vector3(200f, 2f, 0f), Vector3.forward);
                List<Transform> streetAnchors = CreateStreetAnchors(owner.transform);
                List<Transform> quayAnchors = CreateQuayAnchors(owner.transform);
                var nightRoot = new GameObject("Night With Quay Lamps");
                nightRoot.transform.SetParent(owner.transform, false);
                CityNightAtmosphere atmosphere = nightRoot.AddComponent<CityNightAtmosphere>();
                atmosphere.Initialize(player, streetAnchors, CreateBarPositions(), null, quayAnchors);
                var fixedLights = new List<Light>(atmosphere.StreetLightPool);
                int expectedSources = streetAnchors.Count + quayAnchors.Count;
                Assert.That(fixedLights, Has.Count.EqualTo(expectedSources));
                Assert.That(atmosphere.RealtimeLightCount, Is.EqualTo(expectedSources + atmosphere.BarLights.Count));
                Assert.That(nightRoot.GetComponentsInChildren<Light>(true), Has.Length.EqualTo(atmosphere.RealtimeLightCount));
                Assert.That(atmosphere.RealtimeLightCount, Is.LessThanOrEqualTo(CityNightAtmosphere.MaximumRealtimeLights));

                foreach (Vector3 observer in new[] { player.position, Vector3.zero, new Vector3(-10000f, 500f, 10000f) })
                {
                    player.position = observer;
                    atmosphere.RefreshImmediate();
                    Assert.That(atmosphere.StreetLightPool, Is.EqualTo(fixedLights));
                    Assert.That(atmosphere.AssignedStreetLightCount, Is.EqualTo(expectedSources));
                    for (int index = 0; index < streetAnchors.Count; index++)
                    {
                        Light light = atmosphere.GetLightForAnchor(index);
                        Assert.That(light, Is.SameAs(fixedLights[index]));
                        Assert.That(light.enabled, Is.True);
                        Assert.That(light.transform.position, Is.EqualTo(streetAnchors[index].position));
                        AssertStreetProfile(light);
                        Assert.That(light.GetComponentInChildren<CityLightHalo>(true), Is.Null,
                            "The physical fixture already owns its halo; its fixed Light adds no duplicate.");
                    }
                    for (int index = 0; index < quayAnchors.Count; index++)
                    {
                        Light light = atmosphere.GetLightForAnchor(streetAnchors.Count + index);
                        Assert.That(light, Is.SameAs(fixedLights[streetAnchors.Count + index]));
                        Assert.That(light.enabled, Is.True);
                        Assert.That(light.type, Is.EqualTo(LightType.Spot));
                        Assert.That(light.intensity, Is.EqualTo(6f));
                        Assert.That(light.range, Is.EqualTo(10f));
                        Assert.That(light.spotAngle, Is.EqualTo(130f));
                        Assert.That(light.innerSpotAngle, Is.EqualTo(70f));
                        Assert.That(light.shadows, Is.EqualTo(LightShadows.None));
                        Assert.That(Vector3.Distance(light.transform.position, quayAnchors[index].position), Is.LessThan(.001f));
                        Assert.That(Vector3.Angle(light.transform.forward, quayAnchors[index].forward), Is.LessThan(.01f));
                    }
                }
            }
            finally { Object.DestroyImmediate(owner); }
        }

        private static void AssertFixedStreetAssignment(CityNightAtmosphere atmosphere,
            GameObject nightRoot, IReadOnlyList<Transform> streetAnchors, int practicalCount)
        {
            Assert.That(atmosphere.BarLights.Count, Is.EqualTo(4));
            Assert.That(atmosphere.StreetLightPool.Count, Is.EqualTo(streetAnchors.Count + practicalCount));
            Assert.That(atmosphere.RealtimeLightCount, Is.EqualTo(streetAnchors.Count + practicalCount + atmosphere.BarLights.Count));
            Assert.That(atmosphere.FixedFixtureCount, Is.EqualTo(atmosphere.RealtimeLightCount));
            Assert.That(atmosphere.FixedAnchorLightCount, Is.EqualTo(streetAnchors.Count));
            Assert.That(atmosphere.RealtimeLightCount, Is.LessThanOrEqualTo(CityNightAtmosphere.MaximumRealtimeLights));
            Assert.That(nightRoot.GetComponentsInChildren<Light>(true), Has.Length.EqualTo(atmosphere.RealtimeLightCount));
            Assert.That(atmosphere.IsPracticalSlotLeased, Is.False);
            Assert.That(atmosphere.ActivePracticalKind, Is.Null);
            Assert.That(atmosphere.ActivePracticalLight, Is.Null);
            Assert.That(atmosphere.AssignedStreetLightCount, Is.EqualTo(streetAnchors.Count));
            for (int index = 0; index < streetAnchors.Count; index++)
            {
                Light light = atmosphere.GetLightForAnchor(index);
                Assert.That(light, Is.SameAs(atmosphere.StreetLightPool[index]));
                Assert.That(light.transform.position, Is.EqualTo(streetAnchors[index].position));
                AssertStreetProfile(light);
            }
        }

        private static void AssertPracticalProfile(
            CityFringePracticalAnchor practical,
            Light light)
        {
            float expectedIntensity;
            float expectedRange;
            float expectedSpotAngle;
            float expectedInnerSpotAngle;
            switch (practical.Kind)
            {
                case CityFringeYardKind.WestStoneTerraces:
                case CityFringeYardKind.EastUtilityEdge:
                    expectedIntensity = 18f;
                    expectedRange = 8f;
                    expectedSpotAngle = 65f;
                    expectedInnerSpotAngle = 38f;
                    break;
                case CityFringeYardKind.WestIndustrialBelt:
                    expectedIntensity = 21f;
                    expectedRange = 9f;
                    expectedSpotAngle = 72f;
                    expectedInnerSpotAngle = 42f;
                    break;
                case CityFringeYardKind.SouthTunnelForecourt:
                    expectedIntensity = 150f;
                    expectedRange = 16f;
                    expectedSpotAngle = 72f;
                    expectedInnerSpotAngle = 40f;
                    break;
                case CityFringeYardKind.SouthFloodWorks:
                    expectedIntensity = 20f;
                    expectedRange = 9f;
                    expectedSpotAngle = 70f;
                    expectedInnerSpotAngle = 40f;
                    break;
                default:
                    Assert.Fail($"Unsupported practical kind {practical.Kind}.");
                    return;
            }

            Assert.That(light, Is.Not.Null);
            Assert.That(light.type, Is.EqualTo(LightType.Spot));
            Assert.That(light.shadows, Is.EqualTo(LightShadows.None));
            Assert.That(light.intensity, Is.EqualTo(expectedIntensity));
            Assert.That(light.range, Is.EqualTo(expectedRange));
            Assert.That(light.spotAngle, Is.EqualTo(expectedSpotAngle));
            Assert.That(
                light.innerSpotAngle,
                Is.EqualTo(expectedInnerSpotAngle));
            Assert.That(
                Vector3.Distance(
                    light.transform.position,
                    practical.Anchor.position),
                Is.LessThan(0.001f));
            Assert.That(
                Vector3.Angle(
                    light.transform.forward,
                    practical.Anchor.forward),
                Is.LessThan(0.01f));
            Assert.That(
                light.GetComponentInChildren<CityLightHalo>(true),
                Is.Not.Null);
        }

        private static void AssertStreetProfile(Light light)
        {
            Assert.That(light.type, Is.EqualTo(LightType.Spot));
            Assert.That(light.intensity, Is.EqualTo(31f));
            Assert.That(light.range, Is.EqualTo(16.5f));
            Assert.That(light.spotAngle, Is.EqualTo(105f));
            Assert.That(light.innerSpotAngle, Is.EqualTo(55f));
            Assert.That(light.shadows, Is.EqualTo(LightShadows.None));
        }

        private static List<Transform> CreateStreetAnchors(
            Transform parent)
        {
            var result = new List<Transform>();
            for (int index = 0; index < 10; index++)
            {
                result.Add(CreateAnchor(
                    parent,
                    $"Street Anchor {index + 1}",
                    new Vector3(index * 2f - 9f, 4.7f, index % 2),
                    Vector3.forward));
            }

            return result;
        }

        private static List<Transform> CreateQuayAnchors(
            Transform parent)
        {
            var result = new List<Transform>();
            for (int index = 0; index < 6; index++)
            {
                result.Add(CreateAnchor(
                    parent,
                    $"Quay Lamp Anchor {index + 1}",
                    new Vector3(200f, 1.2f, index * 13f - 32.5f),
                    new Vector3(0.78f, -0.63f, 0f)));
            }

            return result;
        }

        private static List<Vector3> CreateBarPositions()
        {
            return new List<Vector3>
            {
                new Vector3(-3f, 2.5f, -4f),
                new Vector3(-1f, 2.5f, -4f),
                new Vector3(1f, 2.5f, -4f),
                new Vector3(3f, 2.5f, -4f)
            };
        }

        private static List<CityFringePracticalAnchor>
            CreatePracticalAnchors(Transform parent)
        {
            return new List<CityFringePracticalAnchor>
            {
                CreatePractical(
                    parent,
                    CityFringeYardKind.WestStoneTerraces,
                    new Vector3(100f, 3f, 0f),
                    new Vector3(0.2f, -0.9f, 0.4f)),
                CreatePractical(
                    parent,
                    CityFringeYardKind.WestIndustrialBelt,
                    new Vector3(130f, 3f, 0f),
                    new Vector3(-0.3f, -0.8f, 0.5f)),
                CreatePractical(
                    parent,
                    CityFringeYardKind.SouthTunnelForecourt,
                    new Vector3(160f, 3f, 0f),
                    new Vector3(0.4f, -0.4f, -0.8f)),
                CreatePractical(
                    parent,
                    CityFringeYardKind.SouthFloodWorks,
                    new Vector3(190f, 3f, 0f),
                    new Vector3(-0.4f, -0.7f, 0.6f)),
                CreatePractical(
                    parent,
                    CityFringeYardKind.EastUtilityEdge,
                    new Vector3(220f, 3f, 0f),
                    new Vector3(0f, -0.8f, 0.2f))
            };
        }

        private static CityFringePracticalAnchor CreatePractical(
            Transform parent,
            CityFringeYardKind kind,
            Vector3 position,
            Vector3 forward)
        {
            Transform anchor = CreateAnchor(
                parent,
                $"{kind} Practical",
                position,
                forward);
            return new CityFringePracticalAnchor(kind, anchor);
        }

        private static Transform CreateAnchor(
            Transform parent,
            string name,
            Vector3 position,
            Vector3 forward)
        {
            var owner = new GameObject(name);
            owner.transform.SetParent(parent, false);
            owner.transform.position = position;
            owner.transform.rotation = Quaternion.LookRotation(
                forward.normalized,
                Vector3.up);
            return owner.transform;
        }
    }
}
