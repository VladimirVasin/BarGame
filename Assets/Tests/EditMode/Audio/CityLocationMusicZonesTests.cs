using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityLocationMusicZonesTests
    {
        private static readonly Rect Grounds =
            new Rect(10f, 10f, 20f, 20f);

        [Test]
        public void OutsideEveryGround_TheSceneThemeOwnsTheMix()
        {
            Assert.That(
                CityLocationMusicZones.Resolve(
                    new[] { Grounds },
                    CityLocationMusicZones.NoLocationIndex,
                    new Vector2(0f, 0f)),
                Is.EqualTo(CityLocationMusicZones.NoLocationIndex));
        }

        [Test]
        public void SteppingOntoGrounds_HandsTheMixToThatPlace()
        {
            Assert.That(
                CityLocationMusicZones.Resolve(
                    new[] { Grounds },
                    CityLocationMusicZones.NoLocationIndex,
                    new Vector2(11f, 11f)),
                Is.Zero);
        }

        [Test]
        public void LeavingByLessThanTheMargin_KeepsThePlaceActive()
        {
            float justOutside =
                Grounds.xMin - CityLocationMusicZones.ExitMarginMeters * 0.5f;

            Assert.That(
                CityLocationMusicZones.Resolve(
                    new[] { Grounds },
                    0,
                    new Vector2(justOutside, Grounds.center.y)),
                Is.Zero,
                "Walking the fence line must not flap the mix.");
        }

        [Test]
        public void LeavingByMoreThanTheMargin_ReturnsTheSceneTheme()
        {
            float clearlyOutside =
                Grounds.xMin - CityLocationMusicZones.ExitMarginMeters - 0.5f;

            Assert.That(
                CityLocationMusicZones.Resolve(
                    new[] { Grounds },
                    0,
                    new Vector2(clearlyOutside, Grounds.center.y)),
                Is.EqualTo(CityLocationMusicZones.NoLocationIndex));
        }

        [Test]
        public void OverlappingHolds_KeepTheActivePlaceUntilItIsLeft()
        {
            Rect neighbour = new Rect(31f, 10f, 10f, 20f);
            Rect[] grounds = { Grounds, neighbour };

            Assert.That(
                CityLocationMusicZones.Resolve(
                    grounds,
                    0,
                    new Vector2(32f, 15f)),
                Is.Zero,
                "The active place holds its margin over a neighbour.");
            Assert.That(
                CityLocationMusicZones.Resolve(
                    grounds,
                    0,
                    new Vector2(38f, 15f)),
                Is.EqualTo(1));
        }

        [Test]
        public void DegenerateOrMissingGrounds_NeverTakeTheMix()
        {
            Assert.That(
                CityLocationMusicZones.Resolve(
                    null,
                    CityLocationMusicZones.NoLocationIndex,
                    Vector2.zero),
                Is.EqualTo(CityLocationMusicZones.NoLocationIndex));
            Assert.That(
                CityLocationMusicZones.Resolve(
                    new[] { new Rect(5f, 5f, 0f, 0f) },
                    CityLocationMusicZones.NoLocationIndex,
                    new Vector2(5f, 5f)),
                Is.EqualTo(CityLocationMusicZones.NoLocationIndex));
        }

        [Test]
        public void NegativeMargin_IsRejected()
        {
            Assert.That(
                () => CityLocationMusicZones.Resolve(
                    new[] { Grounds },
                    0,
                    Vector2.zero,
                    -1f),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
