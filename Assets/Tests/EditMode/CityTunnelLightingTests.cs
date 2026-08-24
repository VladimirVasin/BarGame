using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    [Category("CityTunnel")]
    public sealed class CityTunnelLightingTests
    {
        [Test]
        public void Controller_CreateUnderInactiveParent_AppliesLensState()
        {
            var owner = new GameObject("Tunnel Lighting Lifecycle Test");
            try
            {
                Transform player = new GameObject("Player").transform;
                player.SetParent(owner.transform, false);
                Transform streetAnchor =
                    new GameObject("Street Lamp Anchor").transform;
                streetAnchor.SetParent(owner.transform, false);
                Transform practicalAnchor =
                    new GameObject("Tunnel Practical Anchor").transform;
                practicalAnchor.SetParent(owner.transform, false);
                var practical = new CityFringePracticalAnchor(
                    CityFringeYardKind.SouthTunnelForecourt,
                    practicalAnchor);

                CityNightAtmosphere atmosphere = new GameObject(
                        "Night Atmosphere")
                    .AddComponent<CityNightAtmosphere>();
                atmosphere.transform.SetParent(owner.transform, false);
                atmosphere.Initialize(
                    player,
                    new[] { streetAnchor },
                    Array.Empty<Vector3>(),
                    new[] { practical });

                Transform mountainRoot =
                    new GameObject("Inactive Mountain Root").transform;
                mountainRoot.SetParent(owner.transform, false);
                mountainRoot.gameObject.SetActive(false);
                CityTunnelLightingController controller = null;
                Assert.DoesNotThrow(() =>
                    controller = CityTunnelLightingController.Create(
                        mountainRoot,
                        CreateTunnelDescriptor(),
                        atmosphere,
                        new[] { practical }));

                Assert.That(controller, Is.Not.Null);
                Assert.That(controller.IsInitialized, Is.True);
                Assert.That(
                    controller.Fixtures,
                    Has.Count.EqualTo(
                        CityTunnelLightingController.FixtureCount));
                Renderer[] fixtureRenderers = controller.FaultyFixture
                    .GetComponentsInChildren<Renderer>(true);
                Renderer faultyLens = Array.Find(
                    fixtureRenderers,
                    item => item.gameObject.name ==
                            "Faulty Emissive Lens");
                Assert.That(faultyLens, Is.Not.Null);
                var properties = new MaterialPropertyBlock();
                faultyLens.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetColor(
                        Shader.PropertyToID("_BaseColor"))
                        .maxColorComponent,
                    Is.GreaterThan(0f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void FlickerPattern_IsDeterministicMostlyOnAndBounded()
        {
            foreach (float interval in
                     CityTunnelLampFlickerEvaluator.FaultIntervals)
            {
                Assert.That(
                    interval,
                    Is.InRange(
                        CityTunnelLampFlickerEvaluator
                            .MinimumFaultInterval,
                        CityTunnelLampFlickerEvaluator
                            .MaximumFaultInterval));
            }

            const float step = 0.01f;
            const float duration = 240f;
            int sampleCount = 0;
            int steadyCount = 0;
            int dipEdgeCount = 0;
            long previousDip = -1L;
            for (float time = 0f; time < duration; time += step)
            {
                CityTunnelLampFlickerSample first =
                    CityTunnelLampFlickerEvaluator.Evaluate(time);
                CityTunnelLampFlickerSample second =
                    CityTunnelLampFlickerEvaluator.Evaluate(time);
                Assert.That(
                    first.PowerMultiplier,
                    Is.EqualTo(second.PowerMultiplier));
                Assert.That(first.DipEdgeId, Is.EqualTo(second.DipEdgeId));
                Assert.That(
                    first.PowerMultiplier,
                    Is.InRange(0.05f, 1f));
                Assert.That(
                    float.IsNaN(first.PowerMultiplier) ||
                    float.IsInfinity(first.PowerMultiplier),
                    Is.False);
                if (first.PowerMultiplier >= 0.999f)
                {
                    steadyCount++;
                }

                if (first.HasDipEdge && first.DipEdgeId != previousDip)
                {
                    dipEdgeCount++;
                    previousDip = first.DipEdgeId;
                }

                sampleCount++;
            }

            Assert.That(
                steadyCount / (float)sampleCount,
                Is.GreaterThan(0.96f));
            Assert.That(dipEdgeCount, Is.GreaterThan(20));

            CityTunnelLampFlickerSample invalid =
                CityTunnelLampFlickerEvaluator.Evaluate(double.NaN);
            CityTunnelLampFlickerSample zero =
                CityTunnelLampFlickerEvaluator.Evaluate(0d);
            Assert.That(
                invalid.PowerMultiplier,
                Is.EqualTo(zero.PowerMultiplier));
            Assert.That(invalid.DipEdgeId, Is.EqualTo(zero.DipEdgeId));
        }

        [Test]
        public void LampSynthesis_IsMonoDeterministicAndEdgeSafe()
        {
            float[] firstBuzz =
                CityTunnelLampSoundSynthesis
                    .GenerateBallastBuzzSamples();
            float[] secondBuzz =
                CityTunnelLampSoundSynthesis
                    .GenerateBallastBuzzSamples();
            Assert.That(
                firstBuzz,
                Has.Length.EqualTo(
                    (int)(
                        CityTunnelLampSoundSynthesis.SampleRate *
                        CityTunnelLampSoundSynthesis.BallastDuration)));
            CollectionAssert.AreEqual(firstBuzz, secondBuzz);
            Assert.That(
                firstBuzz[firstBuzz.Length - 1],
                Is.EqualTo(firstBuzz[0]));
            AssertSamples(firstBuzz, 0.08f);

            for (int variant = 0;
                 variant <
                 CityTunnelLampSoundSynthesis.CrackleVariantCount;
                 variant++)
            {
                float[] first =
                    CityTunnelLampSoundSynthesis
                        .GenerateContactCrackleSamples(variant);
                float[] second =
                    CityTunnelLampSoundSynthesis
                        .GenerateContactCrackleSamples(variant);
                Assert.That(
                    first,
                    Has.Length.EqualTo(
                        (int)(
                            CityTunnelLampSoundSynthesis.SampleRate *
                            CityTunnelLampSoundSynthesis
                                .CrackleDuration)));
                CollectionAssert.AreEqual(first, second);
                Assert.That(first[0], Is.EqualTo(0f));
                Assert.That(first[first.Length - 1], Is.EqualTo(0f));
                AssertSamples(first, 0.10f);
            }

            Assert.That(CityTunnelLampSoundSynthesis.Channels, Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CityTunnelLampSoundSynthesis
                    .GenerateContactCrackleSamples(-1));
        }

        private static void AssertSamples(
            float[] samples,
            float minimumPeak)
        {
            float peak = 0f;
            for (int index = 0; index < samples.Length; index++)
            {
                Assert.That(
                    float.IsNaN(samples[index]) ||
                    float.IsInfinity(samples[index]),
                    Is.False);
                peak = Math.Max(peak, Math.Abs(samples[index]));
                Assert.That(samples[index], Is.InRange(-0.72f, 0.72f));
            }

            Assert.That(peak, Is.GreaterThan(minimumPeak));
        }

        private static CityMountainTunnelDescriptor
            CreateTunnelDescriptor()
        {
            const float depth = 72f;
            var segments = new List<
                CityMountainTunnelSegmentDescriptor>
            {
                new CityMountainTunnelSegmentDescriptor(
                    "test-tunnel-segment",
                    0f,
                    depth,
                    Vector3.zero,
                    Vector3.back * depth,
                    true)
            };
            return new CityMountainTunnelDescriptor(
                "test-tunnel",
                "test-access",
                "test-area",
                Vector3.zero,
                Vector3.back,
                new Rect(-4f, -1f, 8f, 2f),
                new Rect(-4f, -4f, 8f, 4f),
                8f,
                5.5f,
                depth,
                11f,
                8f,
                6.5f,
                12f,
                false,
                false,
                segments);
        }
    }
}
