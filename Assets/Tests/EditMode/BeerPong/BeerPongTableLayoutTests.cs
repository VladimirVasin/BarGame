using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests
{
    public sealed class BeerPongTableLayoutTests
    {
        [Test]
        public void DefaultLayout_ContainsSixIndexedCupsInTriangle()
        {
            BeerPongTableLayout layout = BeerPongTableLayout.Default;

            Assert.That(
                layout.Cups,
                Has.Count.EqualTo(BeerPongTableLayout.CupCount));
            Assert.That(layout.AllCupsMask, Is.EqualTo(0b11_1111));

            for (int index = 0;
                 index < BeerPongTableLayout.CupCount;
                 index++)
            {
                BeerPongCupDefinition cup = layout.GetCup(index);
                Assert.That(cup.Index, Is.EqualTo(index));
                Assert.That(cup.BaseCenter.y, Is.EqualTo(
                    layout.TableSurfaceY).Within(0.0001f));
                Assert.That(layout.IsPointOverTable(cup.MouthCenter), Is.True);
                Assert.That(
                    layout.IsCupActive(layout.AllCupsMask, index),
                    Is.True);
            }

            Assert.That(
                layout.GetCup(0).MouthCenter.z,
                Is.LessThan(layout.GetCup(1).MouthCenter.z));
            Assert.That(
                layout.GetCup(1).MouthCenter.z,
                Is.EqualTo(layout.GetCup(2).MouthCenter.z).Within(0.0001f));
            Assert.That(
                layout.GetCup(3).MouthCenter.z,
                Is.EqualTo(layout.GetCup(4).MouthCenter.z).Within(0.0001f));
            Assert.That(
                layout.GetCup(4).MouthCenter.z,
                Is.EqualTo(layout.GetCup(5).MouthCenter.z).Within(0.0001f));
            Assert.That(
                layout.GetCup(1).MouthCenter.x,
                Is.EqualTo(-layout.GetCup(2).MouthCenter.x).Within(0.0001f));
            Assert.That(
                layout.GetCup(3).MouthCenter.x,
                Is.EqualTo(-layout.GetCup(5).MouthCenter.x).Within(0.0001f));
        }

        [Test]
        public void Constructor_RejectsAnythingOtherThanSixValidCups()
        {
            BeerPongCupDefinition valid =
                BeerPongTableLayout.Default.GetCup(0);

            Assert.Throws<ArgumentException>(() => new BeerPongTableLayout(
                1f,
                0f,
                4f,
                0f,
                0.075f,
                Vector3.up,
                new[] { valid }));
        }

        [Test]
        public void Aim_MapsYawPitchAndPowerToClampedVelocity()
        {
            Vector3 center = BeerPongAim.ToVelocity(0f, 45f, 0.5f);
            Vector3 right = BeerPongAim.ToVelocity(20f, 45f, 0.5f);
            Vector3 maximum = BeerPongAim.ToVelocity(999f, 999f, 999f);

            Assert.That(center.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(center.y, Is.GreaterThan(0f));
            Assert.That(center.z, Is.GreaterThan(0f));
            Assert.That(
                center.magnitude,
                Is.EqualTo(7.25f).Within(0.0001f));
            Assert.That(right.x, Is.GreaterThan(0f));
            Assert.That(
                right.magnitude,
                Is.EqualTo(center.magnitude).Within(0.0001f));
            Assert.That(
                maximum.magnitude,
                Is.EqualTo(BeerPongAim.MaximumLaunchSpeed).Within(0.0001f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BeerPongAim.ToVelocity(float.NaN, 30f, 0.5f));
        }
    }
}
