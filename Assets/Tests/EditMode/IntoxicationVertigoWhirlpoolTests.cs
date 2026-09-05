using System.IO;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The vertigo whirlpool's geometry, checked against the CPU mirror in
    /// <see cref="IntoxicationWhirlpool"/> and against the shader source that
    /// has to carry the same arithmetic.
    /// </summary>
    public sealed class IntoxicationVertigoWhirlpoolTests
    {
        private const float Widescreen = 16f / 9f;
        private const float Academy = 4f / 3f;
        private static readonly Vector2 InternalTexel =
            new Vector2(1f / 640f, 1f / 360f);

        [Test]
        public void CalmDisc_TakesNoneOfTheTwist()
        {
            Vector4 vertigo = Vertigo(
                new Vector2(0.5f, 0.5f),
                Widescreen,
                IntoxicationVertigoModel.MaximumTwistRadians);
            Vector4 shape = Shape(Vector2.zero);
            var centre = new Vector2(vertigo.x, vertigo.y);
            float outer = IntoxicationWhirlpool.FarthestCornerRadius(
                centre,
                Widescreen);

            for (int step = 0; step <= 28; step++)
            {
                float radius =
                    IntoxicationWhirlpool.InnerRadius * (step / 28f);
                Assert.That(
                    IntoxicationWhirlpool.Profile(radius, outer),
                    Is.Zero,
                    "The hero's own disc must take no twist at all.");

                for (int turn = 0; turn < 12; turn++)
                {
                    float angle = turn * Mathf.PI / 6f;
                    var uv = new Vector2(
                        centre.x + Mathf.Cos(angle) * radius / Widescreen,
                        centre.y + Mathf.Sin(angle) * radius);
                    Vector2 mapped = IntoxicationWhirlpool.Warp(
                        uv,
                        vertigo,
                        shape,
                        InternalTexel);
                    Assert.That(
                        Vector2.Distance(mapped, uv),
                        Is.LessThan(0.0001f),
                        $"The disc moved at radius {radius}.");
                }
            }
        }

        [Test]
        public void FarthestCorner_TakesTheWholeAngle()
        {
            foreach (float aspect in new[] { Widescreen, Academy })
            {
                foreach (float sign in new[] { 1f, -1f })
                {
                    float twist =
                        sign *
                        IntoxicationVertigoModel.MaximumTwistRadians;
                    var centre = new Vector2(0.5f, 0.5f);
                    Vector4 vertigo = Vertigo(centre, aspect, twist);
                    Vector2 mapped = IntoxicationWhirlpool.Warp(
                        Vector2.one,
                        vertigo,
                        Shape(Vector2.zero),
                        InternalTexel);

                    Assert.That(
                        Vector2.SignedAngle(
                            Corrected(Vector2.one - centre, aspect),
                            Corrected(mapped - centre, aspect)),
                        Is.EqualTo(
                            twist * Mathf.Rad2Deg).Within(0.05f),
                        "The frame corner has to take the full wind-up.");
                }
            }
        }

        [Test]
        public void EqualRadii_TakeEqualAngles_SoTheWaterIsRound()
        {
            foreach (float aspect in new[] { Widescreen, Academy })
            {
                var centre = new Vector2(0.5f, 0.5f);
                Vector4 vertigo = Vertigo(
                    centre,
                    aspect,
                    IntoxicationVertigoModel.MaximumTwistRadians);
                float radius = 0.45f;
                float reference = float.NaN;

                for (int turn = 0; turn < 16; turn++)
                {
                    float angle = turn * Mathf.PI / 8f;
                    var uv = new Vector2(
                        centre.x + Mathf.Cos(angle) * radius / aspect,
                        centre.y + Mathf.Sin(angle) * radius);
                    Vector2 mapped = IntoxicationWhirlpool.Warp(
                        uv,
                        vertigo,
                        Shape(Vector2.zero),
                        InternalTexel);
                    float turned = Vector2.SignedAngle(
                        Corrected(uv - centre, aspect),
                        Corrected(mapped - centre, aspect));
                    if (float.IsNaN(reference))
                    {
                        reference = turned;
                        continue;
                    }

                    Assert.That(
                        turned,
                        Is.EqualTo(reference).Within(0.05f),
                        "An elliptical whirlpool would twist by direction.");
                }
            }
        }

        [Test]
        public void MaximumTwist_NeverReadsOutsideTheFrame()
        {
            foreach (float aspect in new[] { Widescreen, Academy })
            {
                foreach (Vector2 eye in new[]
                         {
                             new Vector2(0.5f, 0.5f),
                             new Vector2(0.42f, 0.58f),
                             new Vector2(0.62f, 0.44f)
                         })
                {
                    Vector4 vertigo = Vertigo(
                        eye,
                        aspect,
                        IntoxicationVertigoModel.MaximumTwistRadians);
                    Vector4 shape = Shape(
                        new Vector2(
                            IntoxicationVertigoModel
                                .CoreWobbleInternalPixels,
                            IntoxicationVertigoModel
                                .CoreWobbleInternalPixels));

                    for (int y = 0; y <= 90; y++)
                    {
                        for (int x = 0; x <= 160; x++)
                        {
                            var uv = new Vector2(x / 160f, y / 90f);
                            Vector2 mapped = IntoxicationWhirlpool.Warp(
                                uv,
                                vertigo,
                                shape,
                                InternalTexel);
                            Assert.That(
                                mapped.x,
                                Is.InRange(0f, 1f),
                                $"Sampled off the frame at {uv}.");
                            Assert.That(
                                mapped.y,
                                Is.InRange(0f, 1f),
                                $"Sampled off the frame at {uv}.");
                        }
                    }
                }
            }
        }

        [Test]
        public void TheDisc_DriftsAtTheEyeAndIsSpentByItsRim()
        {
            var centre = new Vector2(0.5f, 0.5f);
            Vector4 vertigo = Vertigo(centre, Widescreen, 0f);
            Vector4 shape = Shape(new Vector2(2f, 0f));

            Vector2 atEye = IntoxicationWhirlpool.Warp(
                centre,
                vertigo,
                shape,
                InternalTexel);
            Assert.That(
                atEye.x - centre.x,
                Is.EqualTo(2f * InternalTexel.x).Within(1e-6f),
                "The eye drifts by exactly the published pixels.");

            var atRim = new Vector2(
                centre.x,
                centre.y + IntoxicationWhirlpool.InnerRadius);
            Vector2 mapped = IntoxicationWhirlpool.Warp(
                atRim,
                vertigo,
                shape,
                InternalTexel);
            Assert.That(
                Vector2.Distance(mapped, atRim),
                Is.LessThan(1e-5f),
                "The drift has to be spent by the disc's rim.");
        }

        [Test]
        public void Resolve_InvertsThe43CropAndKeepsTheEyeUpright()
        {
            const float fraction = 0.75f;
            var viewport = new Vector3(0.4f, 0.7f, 5f);

            Assert.That(
                IntoxicationWhirlpool.TryResolve(
                    viewport,
                    fraction,
                    1440,
                    1080,
                    0.5f,
                    Vector2.zero,
                    out Vector4 vertigo,
                    out Vector4 shape),
                Is.True);

            // The shader crops target space back to source space with the
            // same fraction, so the round trip has to land on the camera's
            // own viewport point.
            Assert.That(
                0.5f + (vertigo.x - 0.5f) * fraction,
                Is.EqualTo(viewport.x).Within(1e-5f));
            Assert.That(
                vertigo.y,
                Is.EqualTo(viewport.y).Within(1e-6f),
                "A flipped Y would put the whirlpool under his feet.");
            Assert.That(
                vertigo.z,
                Is.EqualTo(Academy).Within(1e-5f),
                "The frame aspect is the visible window's, not the output's.");
            Assert.That(vertigo.w, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(
                shape.x,
                Is.EqualTo(IntoxicationWhirlpool.InnerRadius));
            Assert.That(
                shape.y,
                Is.EqualTo(IntoxicationWhirlpool.RadialPull));
        }

        [Test]
        public void Resolve_KeepsStillWaterOffFrameAndBehindTheLens()
        {
            Assert.That(
                IntoxicationWhirlpool.TryResolve(
                    new Vector3(0.5f, 1f, 5f),
                    1f,
                    1920,
                    1080,
                    0.5f,
                    Vector2.one,
                    out Vector4 offFrame,
                    out Vector4 offFrameShape),
                Is.False,
                "The fade has to be spent before the eye leaves the frame.");
            Assert.That(offFrame.w, Is.Zero);
            Assert.That(offFrameShape.z, Is.Zero);
            Assert.That(offFrameShape.w, Is.Zero);

            Assert.That(
                IntoxicationWhirlpool.TryResolve(
                    new Vector3(0.5f, 0.5f, -2f),
                    1f,
                    1920,
                    1080,
                    0.5f,
                    Vector2.one,
                    out Vector4 behind,
                    out Vector4 _),
                Is.False,
                "Behind the lens there is no on-screen eye.");
            Assert.That(behind.w, Is.Zero);

            // Between the two it tapers rather than snapping off.
            IntoxicationWhirlpool.TryResolve(
                new Vector3(0.5f, 0.96f, 5f),
                1f,
                1920,
                1080,
                1f,
                Vector2.zero,
                out Vector4 tapered,
                out Vector4 _);
            Assert.That(tapered.w, Is.InRange(0.1f, 0.9f));
        }

        [Test]
        public void Shader_CarriesTheSameWhirlpool()
        {
            string source = File.ReadAllText(
                Path.Combine(
                    Application.dataPath,
                    "Resources/Shaders/Ps1Composite.shader"));

            AssertShaderCarries(source, "_IntoxicationVertigo;");
            AssertShaderCarries(source, "_IntoxicationVertigoShape;");
            AssertShaderCarries(
                source,
                "float2 sourceUv = ApplyVertigoWhirlpool(input.texcoord);");
            AssertShaderCarries(source, "max(length(far), inner + 0.001)");
            AssertShaderCarries(source, "t * t * (3.0 - 2.0 * t)");
            AssertShaderCarries(source, "sincos(twist * profile");
            AssertShaderCarries(source, "1.0 - pull * profile");
            AssertShaderCarries(source, "saturate(1.0 - radius / inner)");
            AssertShaderCarries(
                source,
                "delta.x >= 0.0 ? 0.999 - centre.x : centre.x - 0.001");
            AssertShaderCarries(source, "if (twist == 0.0 && dot(core, core) == 0.0)");
            Assert.That(
                IntoxicationWhirlpool.BoundaryMargin,
                Is.EqualTo(0.001f),
                "The shader spells the margin out as 0.999 and 0.001.");
        }

        /// <summary>
        /// Nothing else catches a broken composite: the material is loaded
        /// from Resources, so a shader that fails to compile still resolves,
        /// still binds, and simply eats the frame the first time the player
        /// looks at the game.
        /// </summary>
        [Test]
        public void Composite_StillCompiles()
        {
            var material = Resources.Load<Material>(
                "Materials/Ps1Composite");
            Assert.That(material, Is.Not.Null);
            Shader shader = material.shader;
            Assert.That(
                shader.name,
                Is.EqualTo("Hidden/BarPromenade/PS1Composite"));

            if (!UnityEditor.ShaderUtil.ShaderHasError(shader))
            {
                return;
            }

            var report = new System.Text.StringBuilder(
                "The PS1 composite does not compile:");
            foreach (var message in
                     UnityEditor.ShaderUtil.GetShaderMessages(shader))
            {
                report.Append("\n  ")
                    .Append(message.file)
                    .Append('(')
                    .Append(message.line)
                    .Append("): ")
                    .Append(message.message);
            }

            Assert.Fail(report.ToString());
        }

        private static void AssertShaderCarries(
            string source,
            string literal)
        {
            Assert.That(
                source,
                Does.Contain(literal),
                $"The shader no longer carries '{literal}' — the CPU mirror " +
                "and the composite have drifted apart.");
        }

        private static Vector4 Vertigo(
            Vector2 centre,
            float aspect,
            float twistRadians)
        {
            return new Vector4(centre.x, centre.y, aspect, twistRadians);
        }

        private static Vector4 Shape(Vector2 corePixels)
        {
            return new Vector4(
                IntoxicationWhirlpool.InnerRadius,
                IntoxicationWhirlpool.RadialPull,
                corePixels.x,
                corePixels.y);
        }

        private static Vector2 Corrected(Vector2 offset, float aspect)
        {
            return new Vector2(offset.x * aspect, offset.y);
        }
    }
}
