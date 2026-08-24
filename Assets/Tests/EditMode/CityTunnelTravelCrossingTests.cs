using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityTunnelTravelCrossingTests
    {
        [Test]
        public void InwardCrossing_RequiresCorridorAndRetreatToRearm()
        {
            var plan = new CityTunnelTravelPlan(
                "test-tunnel-travel",
                new Vector3(12f, 3f, -20f),
                Vector3.back,
                8f,
                8f,
                6.5f,
                11f,
                3f,
                false);
            var crossing = new CityTunnelTravelCrossingModel(plan);
            Vector3 right = Vector3.Cross(Vector3.up, plan.Axis).normalized;

            Assert.That(crossing.Observe(At(plan, 0f)), Is.False);
            Assert.That(crossing.IsArmed, Is.True);

            Assert.That(
                crossing.Observe(
                    At(plan, 7.9f) + right * (plan.OpeningHalfWidth + 0.1f)),
                Is.False);
            Assert.That(
                crossing.Observe(
                    At(plan, 8.2f) + right * (plan.OpeningHalfWidth + 0.1f)),
                Is.False,
                "Crossing outside the opening must not engage the blocker.");

            Assert.That(crossing.Observe(At(plan, 7.9f)), Is.False);
            Assert.That(crossing.Observe(At(plan, 8.2f)), Is.True);
            Assert.That(crossing.IsArmed, Is.False);
            Assert.That(crossing.Observe(At(plan, 9f)), Is.False);

            Assert.That(crossing.Observe(At(plan, 7f)), Is.False);
            Assert.That(crossing.IsArmed, Is.False);
            Assert.That(
                crossing.Observe(At(plan, 8.2f)),
                Is.False,
                "Backing only partway out must not rearm the same crossing.");

            Assert.That(crossing.Observe(At(plan, 6.5f)), Is.False);
            Assert.That(crossing.IsArmed, Is.True);
            Assert.That(crossing.Observe(At(plan, 8.2f)), Is.True);

            Assert.That(
                crossing.Observe(At(plan, 6.2f)),
                Is.False,
                "The reverse crossing must never fire the inward boundary.");
        }

        private static Vector3 At(
            CityTunnelTravelPlan plan,
            float distance)
        {
            return plan.PortalGroundCenter + plan.Axis * distance;
        }
    }
}
