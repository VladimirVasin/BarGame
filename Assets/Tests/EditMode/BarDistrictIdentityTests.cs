using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarDistrictIdentityTests
    {
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
            }

            // The Residential bar («Огонёк») is the first authored
            // identity: the worn surface set with warmer, dimmer
            // pendants; the rest still share today's amber.
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
                CityDistrictKind.Lake,
                CityDistrictKind.Cemetery,
                CityDistrictKind.Yard
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
    }
}
