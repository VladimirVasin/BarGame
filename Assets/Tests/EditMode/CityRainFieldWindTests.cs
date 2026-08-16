using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityRainFieldWindTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void SetWindDrift_AlignsVelocityOverLifetimeWithWind()
        {
            var host = new GameObject("Rain Wind Test");
            var target = new GameObject("Rain Wind Target");
            try
            {
                var field = host.AddComponent<CityRainField>();
                field.Initialize(
                    target.transform,
                    RuntimePrimitiveFactory.DefaultMaterial,
                    20260816);

                ParticleSystem.VelocityOverLifetimeModule velocity =
                    field.Particles.velocityOverLifetime;
                Assert.That(
                    velocity.x.constantMin,
                    Is.EqualTo(-0.2f).Within(Tolerance),
                    "Calm rain keeps only the cross jitter.");
                Assert.That(
                    velocity.x.constantMax,
                    Is.EqualTo(0.2f).Within(Tolerance));

                var wind = new Vector2(-1.2f, 0.8f);
                field.SetWindDrift(wind);
                Assert.That(
                    field.AppliedWindDrift,
                    Is.EqualTo(wind));

                velocity = field.Particles.velocityOverLifetime;
                Assert.That(
                    velocity.x.constantMin,
                    Is.EqualTo(
                        (-1.2f * CityRainField.DriftScaleMax) - 0.2f)
                        .Within(Tolerance));
                Assert.That(
                    velocity.x.constantMax,
                    Is.EqualTo(
                        (-1.2f * CityRainField.DriftScaleMin) + 0.2f)
                        .Within(Tolerance));
                Assert.That(
                    velocity.z.constantMin,
                    Is.EqualTo(
                        (0.8f * CityRainField.DriftScaleMin) - 0.2f)
                        .Within(Tolerance));
                Assert.That(
                    velocity.z.constantMax,
                    Is.EqualTo(
                        (0.8f * CityRainField.DriftScaleMax) + 0.2f)
                        .Within(Tolerance));

                // Wind below the change threshold keeps the applied
                // drift instead of rewriting the module every frame.
                field.SetWindDrift(
                    wind + new Vector2(0.01f, 0.01f));
                Assert.That(
                    field.AppliedWindDrift,
                    Is.EqualTo(wind));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(target);
            }
        }
    }
}
