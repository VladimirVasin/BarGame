using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The water wave model is the shader's CPU mirror, and these
    /// tests are what makes "mirror" a contract rather than a hope:
    /// the constants are read back out of the shader source, the
    /// analytic slope is held to the finite difference of the height
    /// (shore envelope, product rule and all), and the sea's swell is
    /// pinned inside every clearance that stands in the water — the
    /// factory's culling headroom above, the pier deck's validated
    /// floor above, the inner shelf's silt below.
    /// </summary>
    public class CityWaterWaveModelTests
    {
        private const float ShaderTwoPi = 6.2831853f;

        private static CityWaterWaveProfile SeaProfile(
            float shoreFadeSouthZ = 240f)
        {
            return new CityWaterWaveProfile(
                CitySeaResources.WaveHeight,
                CitySeaResources.WaveLength,
                CitySeaResources.SeaFlowDirection,
                0.38f,
                shoreFadeSouthZ,
                CitySeaResources.ShoreFadeRampLength,
                CitySeaResources.ShoreFadeFloor,
                shoreFadeEnabled: true);
        }

        private static CityWaterWaveProfile StillProfile()
        {
            return new CityWaterWaveProfile(
                0.05f,
                4.8f,
                Vector2.zero,
                0.16f,
                0f,
                12f,
                1f,
                shoreFadeEnabled: false);
        }

        [Test]
        public void Model_IsDeterministicAcrossIdenticalSamples()
        {
            CityWaterWaveProfile profile = SeaProfile();
            for (int index = 0; index < 200; index++)
            {
                float x = -60f + index * 1.7f;
                float z = 200f + index * 0.9f;
                float time = index * 0.37f;
                Assert.That(
                    CityWaterWaveModel.SampleHeight(
                        profile, x, z, time),
                    Is.EqualTo(
                        CityWaterWaveModel.SampleHeight(
                            profile, x, z, time)),
                    "Identical samples must be bit-identical.");
            }
        }

        [Test]
        public void SeaSwell_StaysInsideTheCrestCeilingAndActuallySwells()
        {
            CityWaterWaveProfile profile = SeaProfile();
            float ceiling = CitySeaResources.WaveHeight *
                            CityWaterWaveModel.CrestFactor;
            float tallest = 0f;
            for (int step = 0; step < 40; step++)
            {
                float time = step * 1.13f;
                for (int xi = 0; xi < 30; xi++)
                {
                    for (int zi = 0; zi < 30; zi++)
                    {
                        float height = Mathf.Abs(
                            CityWaterWaveModel.SampleHeight(
                                profile,
                                -220f + xi * 14.7f,
                                260f + zi * 2.3f,
                                time));
                        Assert.That(
                            height,
                            Is.LessThanOrEqualTo(ceiling + 0.0001f),
                            "A crest broke the summed-train ceiling.");
                        tallest = Mathf.Max(tallest, height);
                    }
                }
            }

            // The point of the whole change: the water is not flat.
            // Somewhere in deep water the swell must actually approach
            // its authored height, or a retune has quietly flattened
            // the sea back into the placeholder it used to be.
            Assert.That(
                tallest,
                Is.GreaterThan(ceiling * 0.5f),
                "The sea never visibly swells.");
        }

        [Test]
        public void Model_SlopeIsTheExactDerivativeOfTheHeight()
        {
            // Central difference against the analytic slope, sampled
            // deliberately ACROSS the shore ramp: the envelope
            // multiplies the height, so a slope that forgot the
            // product rule is exact out at sea and wrong only where
            // the fade is live — which is exactly where a cracked
            // normal would meet the surf line.
            CityWaterWaveProfile[] profiles =
            {
                SeaProfile(shoreFadeSouthZ: 300f),
                StillProfile()
            };
            // Wide enough that float32 argument rounding at the sea's
            // 300 m coordinates stays far under the tolerance (at
            // 0.001 the realized spacing is 0.00201, a systematic
            // +0.7% that broke the sea profile's d/dz); a forgotten
            // product-rule term would still miss by 0.01-0.05.
            const float epsilon = 0.01f;
            foreach (CityWaterWaveProfile profile in profiles)
            {
                for (int index = 0; index < 400; index++)
                {
                    float x = -80f + index * 0.83f;
                    float z = 292f + index * 0.06f;
                    float time = index * 0.21f;
                    Vector2 analytic = CityWaterWaveModel.SampleSlope(
                        profile, x, z, time);
                    float numericX =
                        (CityWaterWaveModel.SampleHeight(
                             profile, x + epsilon, z, time) -
                         CityWaterWaveModel.SampleHeight(
                             profile, x - epsilon, z, time)) /
                        (2f * epsilon);
                    float numericZ =
                        (CityWaterWaveModel.SampleHeight(
                             profile, x, z + epsilon, time) -
                         CityWaterWaveModel.SampleHeight(
                             profile, x, z - epsilon, time)) /
                        (2f * epsilon);
                    Assert.That(
                        analytic.x,
                        Is.EqualTo(numericX).Within(0.002f),
                        $"d/dx detached from the surface at ({x}, {z}).");
                    Assert.That(
                        analytic.y,
                        Is.EqualTo(numericZ).Within(0.002f),
                        $"d/dz detached from the surface at ({x}, {z}).");
                }
            }
        }

        [Test]
        public void ShoreFade_KeepsTheTroughOffTheInnerShelf()
        {
            const float southZ = 240f;
            CityWaterWaveProfile profile = SeaProfile(southZ);

            // The inner shelf's top may rise to 0.15 m under the
            // surface (the seacoast test pins that number from the
            // other side). Everywhere south of the ramp — the whole
            // shelf — the deepest possible trough must stay inside it.
            float floorTrough = CitySeaResources.WaveHeight *
                                CityWaterWaveModel.CrestFactor *
                                CitySeaResources.ShoreFadeFloor;
            Assert.That(
                floorTrough,
                Is.LessThan(0.15f),
                "The faded trough undercuts the inner shelf.");
            for (float z = southZ - 30f; z <= southZ; z += 0.5f)
            {
                Assert.That(
                    CityWaterWaveModel.SampleShoreEnvelope(profile, z),
                    Is.EqualTo(CitySeaResources.ShoreFadeFloor)
                        .Within(0.0001f),
                    $"The envelope left its floor south of the ramp " +
                    $"at z={z}.");
            }

            // North of the ramp the swell must stand at full height —
            // a fade that never lets go would flatten the whole sea —
            // and the ramp between must only ever grow.
            Assert.That(
                CityWaterWaveModel.SampleShoreEnvelope(
                    profile,
                    southZ + CitySeaResources.ShoreFadeRampLength),
                Is.EqualTo(1f).Within(0.0001f));
            float previous = 0f;
            for (float z = southZ;
                 z <= southZ + CitySeaResources.ShoreFadeRampLength;
                 z += 0.25f)
            {
                float envelope =
                    CityWaterWaveModel.SampleShoreEnvelope(profile, z);
                Assert.That(
                    envelope,
                    Is.GreaterThanOrEqualTo(previous),
                    $"The shore ramp dips at z={z}.");
                previous = envelope;
            }
        }

        [Test]
        public void Model_MirrorsTheShaderConstants()
        {
            string shaderPath = Path.Combine(
                Application.dataPath,
                "Resources/Shaders/CityRiverWater.shader");
            string source = File.ReadAllText(shaderPath);

            // The model exists to stand where the shader stands; the
            // two share these numbers or they share nothing. Each
            // literal is asserted in the exact form the HLSL carries
            // it, so a retune of either side fails here first.
            Assert.That(
                CityWaterWaveModel.CrestFactor,
                Is.EqualTo(1.73f).Within(0.0001f),
                "CrestFactor drifted from 1 + 0.42 + 0.31.");
            AssertShaderCarries(source, "6.2831853");
            AssertShaderCarries(source, "_WaveLength * 0.53");
            AssertShaderCarries(source, "_WaveLength * 1.71");
            AssertShaderCarries(source, "_WaveHeight * 0.42");
            AssertShaderCarries(source, "_WaveHeight * 0.31");
            AssertShaderCarries(source, "time * 1.00 * flowing");
            AssertShaderCarries(source, "time * 1.63 * flowing");
            AssertShaderCarries(source, "time * 0.41 * flowing");
            AssertShaderCarries(source, "sin(time * 0.90)");
            AssertShaderCarries(source, "sin(time * 0.71 + 1.7)");
            AssertShaderCarries(source, "sin(time * 1.37 + 3.1)");
            AssertShaderCarries(source, "_WaveHeight * 1.73");
            AssertShaderCarries(source, "t * t * (3.0 - 2.0 * t)");
            AssertShaderCarries(source, "6.0 * t * (1.0 - t)");

            Assert.That(
                CityWaterWaveModel.SecondTrainAmplitudeRatio,
                Is.EqualTo(0.42f));
            Assert.That(
                CityWaterWaveModel.ThirdTrainAmplitudeRatio,
                Is.EqualTo(0.31f));
            Assert.That(
                CityWaterWaveModel.SecondTrainWaveLengthRatio,
                Is.EqualTo(0.53f));
            Assert.That(
                CityWaterWaveModel.ThirdTrainWaveLengthRatio,
                Is.EqualTo(1.71f));
            Assert.That(
                CityWaterWaveModel.SecondTrainRate, Is.EqualTo(1.63f));
            Assert.That(
                CityWaterWaveModel.ThirdTrainRate, Is.EqualTo(0.41f));
            Assert.That(ShaderTwoPi, Is.EqualTo(6.2831853f));
        }

        private static void AssertShaderCarries(
            string source,
            string literal)
        {
            Assert.That(
                source,
                Does.Contain(literal),
                $"The shader no longer carries '{literal}' — the CPU " +
                "mirror and the GPU surface have drifted apart.");
        }
    }
}
