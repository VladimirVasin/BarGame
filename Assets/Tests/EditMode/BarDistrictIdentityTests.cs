using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarDistrictIdentityTests
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");

        private static readonly CityDistrictKind[] BarDistricts =
        {
            CityDistrictKind.OldTown,
            CityDistrictKind.Residential,
            CityDistrictKind.Industrial,
            CityDistrictKind.Nightlife
        };

        [Test]
        public void Catalog_CoversTheFourBarDistrictsDistinctly()
        {
            var nameKeys = new HashSet<string>(StringComparer.Ordinal);
            var moods = new HashSet<BarDistrictMood>();
            var wallTints = new HashSet<Color>();
            var counterTints = new HashSet<Color>();
            var metalTints = new HashSet<Color>();
            var pendantColors = new HashSet<Color>();
            var pendantIntensities = new HashSet<float>();
            var signColors = new HashSet<Color>();
            foreach (CityDistrictKind district in BarDistricts)
            {
                BarDistrictIdentity identity =
                    BarDistrictIdentityCatalog.Get(district);
                Assert.That(identity.District, Is.EqualTo(district));
                Assert.That(
                    identity.DisplayNameKey,
                    Does.StartWith("bar.district."));
                Assert.That(
                    nameKeys.Add(identity.DisplayNameKey),
                    Is.True,
                    "Every bar district needs its own name key.");
                Assert.That(
                    moods.Add(identity.Mood),
                    Is.True,
                    "Every bar district answers its own mood.");
                Assert.That(
                    identity.PendantIntensityScale,
                    Is.GreaterThan(0f));
                Assert.That(
                    identity.CrowdDensityScale,
                    Is.GreaterThan(0f));
                Assert.That(
                    identity.CounterWoodTint.maxColorComponent,
                    Is.GreaterThan(0f));
                Assert.That(
                    wallTints.Add(identity.WallTint),
                    Is.True,
                    "Every district needs a distinct large wall field.");
                Assert.That(
                    counterTints.Add(identity.CounterWoodTint),
                    Is.True,
                    "Every district needs distinct counter materials.");
                Assert.That(
                    metalTints.Add(identity.MetalTint),
                    Is.True,
                    "Every district needs a distinct metal family.");
                Assert.That(
                    pendantColors.Add(identity.PendantColor),
                    Is.True,
                    "Every district needs its own counter light colour.");
                Assert.That(
                    pendantIntensities.Add(
                        identity.PendantIntensityScale),
                    Is.True,
                    "Every district needs its own counter light level.");
                Assert.That(
                    signColors.Add(identity.SignAccentColor),
                    Is.True,
                    "Every district needs its own readable sign accent.");
            }

            // Residential keeps the packaged worn sheets; the other
            // bars separate through authored block colours and dress.
            BarDistrictIdentity residential =
                BarDistrictIdentityCatalog.Get(
                    CityDistrictKind.Residential);
            Assert.That(
                residential.SurfaceSet,
                Is.EqualTo(BarSurfaceSetKind.Worn));
            Assert.That(
                residential.PendantIntensityScale,
                Is.LessThan(1f));
            BarDistrictIdentity nightlife =
                BarDistrictIdentityCatalog.Get(
                    CityDistrictKind.Nightlife);
            Assert.That(
                nightlife.SurfaceSet,
                Is.EqualTo(BarSurfaceSetKind.None));
            Assert.That(
                residential.PendantColor,
                Is.Not.EqualTo(nightlife.PendantColor));
        }

        [TestCase(
            CityDistrictKind.OldTown,
            "Old Town Ledger Field")]
        [TestCase(
            CityDistrictKind.Residential,
            "Residential Curtain Field")]
        [TestCase(
            CityDistrictKind.Industrial,
            "Industrial Safety Band")]
        [TestCase(
            CityDistrictKind.Nightlife,
            "Nightlife Neon Cyan")]
        public void WorldBuilder_AppliesTheDistrictVisualSignature(
            CityDistrictKind district,
            string expectedSignalName)
        {
            var parent = new GameObject("Bar District Visual Test");
            try
            {
                BarInteriorLayoutPlan plan =
                    BarInteriorLayoutPlanner.Generate(
                        20260824,
                        $"bar-visual-{district}",
                        BarActivityKind.Cocktail,
                        district);
                Vector3 counterBefore = plan.CounterPosition;
                Vector3 stationBefore = plan.CounterStationPosition;
                Transform room = BarInteriorWorldBuilder.Build(
                    parent.transform,
                    plan);
                Transform dress = room.Find("District Identity");

                Assert.That(dress, Is.Not.Null);
                Transform signal = dress.Find(expectedSignalName);
                Assert.That(signal, Is.Not.Null);
                Renderer signalRenderer = signal.GetComponent<Renderer>();
                Assert.That(signalRenderer, Is.Not.Null);
                Assert.That(
                    Mathf.Max(
                        signalRenderer.bounds.size.y,
                        signalRenderer.bounds.size.z),
                    Is.GreaterThanOrEqualTo(2.3f),
                    "The district signal must survive the 640x360 view.");
                Assert.That(
                    dress.GetComponentsInChildren<Collider>(true),
                    Is.Empty,
                    "Visual identity cannot alter bar traversal.");
                Assert.That(
                    plan.CounterPosition,
                    Is.EqualTo(counterBefore));
                Assert.That(
                    plan.CounterStationPosition,
                    Is.EqualTo(stationBefore));

                BarDistrictIdentity identity = plan.DistrictIdentity;
                //  The ceiling lives inside the authored shell model
                //  now, not as a direct child of the room, so it has to
                //  be found by name rather than by path.
                AssertRendererColor(
                    FindDescendant(room, "Ceiling"),
                    identity.CeilingTint);
                AssertRendererColor(
                    room.Find("Backbar Amber Sign"),
                    identity.SignGlowColor);
                AssertRendererColor(
                    room.Find("Practical Bulb 1"),
                    identity.PendantColor * 2.2f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Normalize_MapsNonBarDistrictsToTheFallback()
        {
            foreach (CityDistrictKind district in BarDistricts)
            {
                Assert.That(
                    BarDistrictIdentityCatalog.Normalize(district),
                    Is.EqualTo(district));
            }

            CityDistrictKind[] nonBar =
            {
                CityDistrictKind.CentralPark,
                CityDistrictKind.NorthWaterfront,
                CityDistrictKind.Cemetery,
                CityDistrictKind.Yard,
                CityDistrictKind.Church
            };
            foreach (CityDistrictKind district in nonBar)
            {
                Assert.That(
                    BarDistrictIdentityCatalog.Normalize(district),
                    Is.EqualTo(
                        BarDistrictIdentityCatalog.FallbackDistrict));
            }
        }

        [Test]
        public void LayoutPlan_CarriesItsDistrictIdentity()
        {
            BarInteriorLayoutPlan oldTown =
                BarInteriorLayoutPlanner.Generate(
                    20260816,
                    "bar-test",
                    BarActivityKind.Cocktail,
                    CityDistrictKind.OldTown);
            Assert.That(
                oldTown.District,
                Is.EqualTo(CityDistrictKind.OldTown));
            Assert.That(
                oldTown.DistrictIdentity.Mood,
                Is.EqualTo(BarDistrictMood.Memory));

            // The pre-district entry point keeps its old behavior.
            BarInteriorLayoutPlan legacy =
                BarInteriorLayoutPlanner.Generate(
                    20260816,
                    "bar-test",
                    BarActivityKind.Cocktail);
            Assert.That(
                legacy.District,
                Is.EqualTo(
                    BarDistrictIdentityCatalog.FallbackDistrict));

            // A park district can never leak into an interior.
            BarInteriorLayoutPlan normalized =
                BarInteriorLayoutPlanner.Generate(
                    20260816,
                    "bar-test",
                    BarActivityKind.Cocktail,
                    CityDistrictKind.CentralPark);
            Assert.That(
                normalized.District,
                Is.EqualTo(
                    BarDistrictIdentityCatalog.FallbackDistrict));
        }

        [Test]
        public void Session_TracksTheActiveBarDistrict()
        {
            GameSessionState.BeginNewGame();
            Assert.That(
                GameSessionState.ActiveBarDistrict,
                Is.EqualTo(
                    BarDistrictIdentityCatalog.FallbackDistrict));

            GameSessionState.EnterBar(
                "bar-oldtown-test",
                BarActivityKind.Cocktail,
                CityDistrictKind.OldTown);
            Assert.That(
                GameSessionState.ActiveBarDistrict,
                Is.EqualTo(CityDistrictKind.OldTown));

            GameSessionState.EnterHome();
            Assert.That(
                GameSessionState.ActiveBarDistrict,
                Is.EqualTo(
                    BarDistrictIdentityCatalog.FallbackDistrict));

            GameSessionState.EnterBar(
                "bar-park-test",
                BarActivityKind.Cocktail,
                CityDistrictKind.CentralPark);
            Assert.That(
                GameSessionState.ActiveBarDistrict,
                Is.EqualTo(
                    BarDistrictIdentityCatalog.FallbackDistrict),
                "A non-bar district must normalize on entry.");
            GameSessionState.BeginNewGame();
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform candidate in
                     root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == name)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void AssertRendererColor(
            Transform part,
            Color expected)
        {
            Assert.That(part, Is.Not.Null);
            Renderer renderer = part.GetComponent<Renderer>();
            Assert.That(renderer, Is.Not.Null);
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Color actual = properties.GetColor(BaseColorId);
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
        }
    }
}
