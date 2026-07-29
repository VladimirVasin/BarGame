using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityMapDistrictPresentationTests
    {
        [Test]
        public void DistrictColors_AreDistinctAndOpaque()
        {
            var colors = new HashSet<Color>();
            foreach (CityDistrictKind district in
                     Enum.GetValues(typeof(CityDistrictKind)))
            {
                Color color = InvokePrivate<Color>(
                    "GetDistrictColor",
                    district);
                Assert.That(
                    color.a,
                    Is.EqualTo(1f).Within(0.001f),
                    district.ToString());
                Assert.That(
                    colors.Add(color),
                    Is.True,
                    $"{district} must have a distinct map color.");
            }
        }

        [TestCase(
            CityDistrictKind.OldTown,
            "map.district.old_town")]
        [TestCase(
            CityDistrictKind.Residential,
            "map.district.residential")]
        [TestCase(
            CityDistrictKind.Industrial,
            "map.district.industrial")]
        [TestCase(
            CityDistrictKind.Nightlife,
            "map.district.nightlife")]
        [TestCase(
            CityDistrictKind.CentralPark,
            "map.district.central_park")]
        public void DistrictLocalizationKeys_AreStable(
            CityDistrictKind district,
            string expected)
        {
            Assert.That(
                InvokePrivate<string>(
                    "GetDistrictLocalizationKey",
                    district),
                Is.EqualTo(expected));
        }

        [Test]
        public void ParkPath_UsesNarrowerDistinctMapStyle()
        {
            const float streetWidth = 6f;
            Color streetColor = InvokePrivate<Color>(
                "GetPathColor",
                CityPathKind.Street);
            Color parkColor = InvokePrivate<Color>(
                "GetPathColor",
                CityPathKind.ParkPath);
            float street = InvokePrivate<float>(
                "GetPathWidth",
                CityPathKind.Street,
                streetWidth);
            float park = InvokePrivate<float>(
                "GetPathWidth",
                CityPathKind.ParkPath,
                streetWidth);

            Assert.That(parkColor, Is.Not.EqualTo(streetColor));
            Assert.That(street, Is.EqualTo(streetWidth));
            Assert.That(park, Is.LessThan(street));
            Assert.That(park, Is.GreaterThanOrEqualTo(2f));
        }

        private static T InvokePrivate<T>(
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = typeof(CityMapView).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, methodName);
            return (T)method.Invoke(null, arguments);
        }
    }
}
