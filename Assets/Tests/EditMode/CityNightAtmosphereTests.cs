using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    [Category("CityFringeYard")]
    public sealed class CityNightAtmosphereTests
    {
        [Test]
        public void FringePractical_LeasesOneStreetSlotWithinTwelveLightBudget()
        {
            var owner = new GameObject("Night Atmosphere Test");
            try
            {
                Transform player = CreateAnchor(
                    owner.transform,
                    "Player",
                    Vector3.zero,
                    Vector3.forward);
                List<Transform> streetAnchors = CreateStreetAnchors(
                    owner.transform);
                List<Vector3> barPositions = CreateBarPositions();
                List<CityFringePracticalAnchor> practicals =
                    CreatePracticalAnchors(owner.transform);
                var nightRoot = new GameObject("Night With Practicals");
                nightRoot.transform.SetParent(owner.transform, false);
                CityNightAtmosphere atmosphere =
                    nightRoot.AddComponent<CityNightAtmosphere>();

                atmosphere.Initialize(
                    player,
                    streetAnchors,
                    barPositions,
                    practicals);

                AssertDefaultStreetAssignment(atmosphere, nightRoot);

                for (int index = 0; index < 4; index++)
                {
                    CityFringePracticalAnchor practical = practicals[index];
                    player.position = practical.Anchor.position;
                    atmosphere.RefreshImmediate();

                    Assert.That(
                        atmosphere.IsPracticalSlotLeased,
                        Is.True,
                        practical.Kind.ToString());
                    Assert.That(
                        atmosphere.ActivePracticalKind,
                        Is.EqualTo(practical.Kind));
                    Assert.That(
                        atmosphere.AssignedStreetLightCount,
                        Is.EqualTo(7));
                    Assert.That(
                        atmosphere.ActivePracticalLight,
                        Is.SameAs(atmosphere.StreetLightPool[7]));
                    foreach (Light pooled in atmosphere.StreetLightPool)
                    {
                        Assert.That(pooled.enabled, Is.True);
                    }
                    Assert.That(
                        nightRoot.GetComponentsInChildren<Light>(true),
                        Has.Length.EqualTo(
                            CityNightAtmosphere.MaximumRealtimeLights));
                    AssertPracticalProfile(
                        practical,
                        atmosphere.ActivePracticalLight);
                }

                CityFringePracticalAnchor tunnel = practicals[2];
                player.position = tunnel.Anchor.position;
                atmosphere.RefreshImmediate();
                Light practicalLight = atmosphere.ActivePracticalLight;
                CityLightHalo practicalHalo = practicalLight
                    .GetComponentInChildren<CityLightHalo>(true);

                atmosphere.SetNightFactor(0f);

                foreach (Light light in
                         nightRoot.GetComponentsInChildren<Light>(true))
                {
                    Assert.That(light.enabled, Is.False);
                    Assert.That(light.intensity, Is.EqualTo(0f));
                }

                Assert.That(practicalHalo.IntensityFactor, Is.EqualTo(0f));
                Assert.That(practicalHalo.IsVisible, Is.False);

                atmosphere.SetNightFactor(1f);

                Assert.That(atmosphere.IsPracticalSlotLeased, Is.True);
                Assert.That(
                    atmosphere.ActivePracticalKind,
                    Is.EqualTo(CityFringeYardKind.SouthTunnelForecourt));
                Assert.That(practicalLight.enabled, Is.True);
                Assert.That(practicalLight.intensity, Is.EqualTo(150f));
                Assert.That(practicalHalo.IntensityFactor, Is.EqualTo(1f));
                Assert.That(practicalHalo.IsVisible, Is.True);

                player.position = practicals[4].Anchor.position;
                atmosphere.RefreshImmediate();

                Assert.That(
                    atmosphere.IsPracticalSlotLeased,
                    Is.False,
                    "The east utility edge does not own a pooled profile.");
                Assert.That(atmosphere.ActivePracticalKind, Is.Null);
                Assert.That(atmosphere.ActivePracticalLight, Is.Null);
                Assert.That(
                    atmosphere.AssignedStreetLightCount,
                    Is.EqualTo(8));
                AssertStreetProfile(atmosphere.StreetLightPool[7]);

                Object.DestroyImmediate(nightRoot);
                var noFringeRoot = new GameObject("Night Without Practicals");
                noFringeRoot.transform.SetParent(owner.transform, false);
                CityNightAtmosphere noFringe =
                    noFringeRoot.AddComponent<CityNightAtmosphere>();
                player.position = practicals[0].Anchor.position;

                noFringe.Initialize(
                    player,
                    streetAnchors,
                    barPositions);

                Assert.That(noFringe.IsPracticalSlotLeased, Is.False);
                Assert.That(noFringe.ActivePracticalKind, Is.Null);
                Assert.That(noFringe.ActivePracticalLight, Is.Null);
                Assert.That(noFringe.AssignedStreetLightCount, Is.EqualTo(8));
                Assert.That(
                    noFringe.RealtimeLightCount,
                    Is.EqualTo(CityNightAtmosphere.MaximumRealtimeLights));
                Assert.That(
                    noFringeRoot.GetComponentsInChildren<Light>(true),
                    Has.Length.EqualTo(
                        CityNightAtmosphere.MaximumRealtimeLights));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        private static void AssertDefaultStreetAssignment(
            CityNightAtmosphere atmosphere,
            GameObject nightRoot)
        {
            Assert.That(atmosphere.BarLights.Count, Is.EqualTo(4));
            Assert.That(atmosphere.StreetLightPool.Count, Is.EqualTo(8));
            Assert.That(
                atmosphere.RealtimeLightCount,
                Is.EqualTo(CityNightAtmosphere.MaximumRealtimeLights));
            Assert.That(
                nightRoot.GetComponentsInChildren<Light>(true),
                Has.Length.EqualTo(
                    CityNightAtmosphere.MaximumRealtimeLights));
            Assert.That(atmosphere.IsPracticalSlotLeased, Is.False);
            Assert.That(atmosphere.ActivePracticalKind, Is.Null);
            Assert.That(atmosphere.ActivePracticalLight, Is.Null);
            Assert.That(atmosphere.AssignedStreetLightCount, Is.EqualTo(8));
            foreach (Light light in atmosphere.StreetLightPool)
            {
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
