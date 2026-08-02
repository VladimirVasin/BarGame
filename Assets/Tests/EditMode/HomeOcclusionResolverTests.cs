using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeOcclusionResolverTests
    {
        [Test]
        public void SegmentIntersectsBounds_OnlyAcceptsBlockerBetweenEndpoints()
        {
            Vector3 camera = new Vector3(0f, 1f, -10f);
            Vector3 player = new Vector3(0f, 1f, 0f);

            Assert.That(
                HomeOcclusionResolver.SegmentIntersectsBounds(
                    camera,
                    player,
                    new Bounds(
                        new Vector3(0f, 1f, -5f),
                        new Vector3(2f, 2f, 0.5f))),
                Is.True);
            Assert.That(
                HomeOcclusionResolver.SegmentIntersectsBounds(
                    camera,
                    player,
                    new Bounds(
                        new Vector3(4f, 1f, -5f),
                        new Vector3(1f, 2f, 1f))),
                Is.False,
                "A lateral object must not reveal just because it is camera-near.");
            Assert.That(
                HomeOcclusionResolver.SegmentIntersectsBounds(
                    camera,
                    player,
                    new Bounds(
                        new Vector3(0f, 1f, 3f),
                        new Vector3(2f, 2f, 1f))),
                Is.False,
                "Geometry behind the player is outside the protected segment.");
            Assert.That(
                HomeOcclusionResolver.SegmentIntersectsBounds(
                    camera,
                    player,
                    new Bounds(
                        new Vector3(0f, 1f, -12f),
                        new Vector3(2f, 2f, 1f))),
                Is.False,
                "Geometry behind the camera is outside the protected segment.");
        }

        [Test]
        public void ShouldFade_ReturnsTrueWhenAnyProtectedSampleIsBlocked()
        {
            Vector3 camera = new Vector3(0f, 1f, -10f);
            Vector3[] samples =
            {
                new Vector3(0f, 2f, 0f),
                new Vector3(-0.3f, 1.4f, 0f),
                new Vector3(0.3f, 1.4f, 0f),
                new Vector3(0f, 0.8f, 0f)
            };

            Assert.That(
                HomeOcclusionResolver.ShouldFade(
                    new Bounds(
                        new Vector3(0.18f, 1.24f, -4f),
                        new Vector3(0.3f, 0.3f, 0.2f)),
                    camera,
                    samples),
                Is.True);
            Assert.That(
                HomeOcclusionResolver.ShouldFade(
                    new Bounds(
                        new Vector3(4f, 1.4f, -4f),
                        Vector3.one),
                    camera,
                    samples),
                Is.False);
        }

        [Test]
        public void BuildPlayerSamples_ProtectsBodyBeforeFeet()
        {
            var playerBounds = new Bounds(
                new Vector3(2f, 1f, 3f),
                new Vector3(1f, 2f, 0.2f));
            var samples = new Vector3[
                HomeOcclusionResolver.PlayerSampleCount];

            HomeOcclusionResolver.BuildPlayerSamples(
                playerBounds,
                Vector3.right,
                Vector3.up,
                samples);

            Assert.That(
                HomeOcclusionResolver.PlayerSampleCount,
                Is.EqualTo(5));
            Assert.That(
                HomeOcclusionResolver.ProtectedPlayerSampleCount,
                Is.EqualTo(4));
            Assert.That(samples[0].y, Is.GreaterThan(samples[1].y));
            Assert.That(samples[1].x, Is.LessThan(playerBounds.center.x));
            Assert.That(samples[2].x, Is.GreaterThan(playerBounds.center.x));
            Assert.That(samples[3].y, Is.GreaterThan(samples[4].y));
            Assert.That(
                samples[4].y,
                Is.LessThan(playerBounds.center.y),
                "The final diagnostic sample represents the feet and is not protected.");
        }

        [Test]
        public void Resolver_RejectsMissingSampleBuffers()
        {
            var bounds = new Bounds(Vector3.zero, Vector3.one);

            Assert.That(
                () => HomeOcclusionResolver.BuildPlayerSamples(
                    bounds,
                    Vector3.right,
                    Vector3.up,
                    null),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => HomeOcclusionResolver.BuildPlayerSamples(
                    bounds,
                    Vector3.right,
                    Vector3.up,
                    new Vector3[
                        HomeOcclusionResolver.PlayerSampleCount - 1]),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => HomeOcclusionResolver.ShouldFade(
                    bounds,
                    Vector3.back,
                    null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void SegmentIntersectsBounds_InvalidOrDegenerateInputIsSafe()
        {
            var bounds = new Bounds(Vector3.zero, Vector3.one);

            Assert.That(
                HomeOcclusionResolver.SegmentIntersectsBounds(
                    Vector3.zero,
                    Vector3.zero,
                    bounds),
                Is.False);
            Assert.That(
                HomeOcclusionResolver.SegmentIntersectsBounds(
                    new Vector3(float.NaN, 0f, 0f),
                    Vector3.one,
                    bounds),
                Is.False);
            Assert.That(
                HomeOcclusionResolver.SegmentIntersectsBounds(
                    Vector3.zero,
                    Vector3.one,
                    new Bounds(
                        new Vector3(float.PositiveInfinity, 0f, 0f),
                        Vector3.one)),
                Is.False);
        }
    }
}
