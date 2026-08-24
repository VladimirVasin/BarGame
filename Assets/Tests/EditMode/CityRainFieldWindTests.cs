using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityRainFieldWindTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void ShelterHole_HugsTheBusButKeepsRainAtTheGlass()
        {
            // The sheltered donut exists so streaks never spawn through
            // the cabin roof — but the passenger judges the weather by
            // what stands right outside the glass, so the hole must hug
            // the body: half a diagonal plus a wind-drift margin, and
            // never the 10 m moat that once pushed every streak past
            // the fog and made rainy rides read dry.
            CityBusDesignVehicle vehicle = CityBusDesignVehicle.Default;
            float halfDiagonal = 0.5f * Mathf.Sqrt(
                (vehicle.BodyLength * vehicle.BodyLength) +
                (vehicle.BodyWidth * vehicle.BodyWidth));
            Assert.That(
                CityRainField.ShelterHoleRadius,
                Is.GreaterThan(halfDiagonal + 1f),
                "Streaks must clear the cabin roof with margin for " +
                "wind drift.");
            Assert.That(
                CityRainField.ShelterHoleRadius,
                Is.LessThan(8f),
                "Rain must stand close enough to read through the " +
                "windows.");
            Assert.That(
                CityRainField.ShelterHoleRadius,
                Is.LessThan(CityRainField.FieldExtent * 0.5f),
                "The sheltered donut must keep a spawn band.");
        }

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
