using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The view over the brink. Two things can silently destroy it and
    /// neither shows up as an error: a layer placed behind ground that the
    /// fog has already hidden, so it is invisible for a reason nobody can
    /// see; and a layer placed past the far plane, which pops.
    /// </summary>
    public sealed class MountainRoadVistaTests
    {
        [Test]
        [Category("MountainRoad")]
        public void Vista_StandsInsideTheFarPlaneAndAboveTheGround()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            MountainRoadVistaPlan vista = plan.Vista;
            Assert.That(vista, Is.Not.Null);
            Assert.That(
                vista.Parts.Count,
                Is.LessThanOrEqualTo(MountainRoadVistaPlan.MaximumPartCount));
            Assert.That(
                MountainRoadVistaResources.FadeEndDistance,
                Is.LessThan(RuntimeSceneSetup.MountainRoadFarClipPlane),
                "The view has to dissolve before the far plane clips it.");

            foreach (MountainRoadVistaPartKind kind in
                     System.Enum.GetValues(
                         typeof(MountainRoadVistaPartKind)))
            {
                Assert.That(
                    vista.GetCount(kind),
                    Is.GreaterThan(0),
                    $"The view has no {kind}.");
            }

            Vector3 eye = vista.Eye;
            float worstNear = float.PositiveInfinity;
            float worstFar = 0f;
            string worstOccluded = null;
            float worstMargin = float.PositiveInfinity;

            for (int index = 0; index < vista.Parts.Count; index++)
            {
                MountainRoadVistaPartDescriptor part = vista.Parts[index];
                Vector3 delta = part.Center - eye;
                var flat = new Vector2(delta.x, delta.z);
                float distance = flat.magnitude;
                worstNear = Mathf.Min(worstNear, distance);
                worstFar = Mathf.Max(
                    worstFar,
                    distance + Mathf.Max(part.Size.x, part.Size.z) * 0.5f);

                // Nothing may hide behind the ground. The ground in the
                // cut is measured, not assumed: this is the same sampler
                // the terrain mesh is built from.
                float partTopElevation = Mathf.Atan2(
                    part.Center.y + part.Size.y * 0.5f - eye.y,
                    distance) * Mathf.Rad2Deg;
                float terrainElevation = MaxTerrainElevation(
                    plan,
                    eye,
                    new Vector3(flat.x, 0f, flat.y).normalized,
                    distance);
                float margin = partTopElevation - terrainElevation;
                if (margin < worstMargin)
                {
                    worstMargin = margin;
                    worstOccluded = part.StableId;
                }
            }

            Assert.That(
                worstNear,
                Is.GreaterThan(60f),
                "A layer this near is described by the scene fog rather " +
                "than by its own haze.");
            Assert.That(
                worstFar,
                Is.LessThan(MountainRoadVistaResources.FadeEndDistance),
                "A layer reaches past its own dissolve band.");
            Assert.That(
                worstMargin,
                Is.GreaterThan(0.4f),
                $"'{worstOccluded}' stands only {worstMargin:0.00} degrees " +
                "above the ground between it and the brink.");
        }

        [Test]
        [Category("MountainRoad")]
        public void VistaLights_KeepTheLawByDayAndComeUpWithTheNight()
        {
            // §20: the city in the matte is the very city whose every
            // fixture burns always - at noon it gives two thirds of its
            // night strength, never nothing. This test used to REQUIRE the
            // valley dark by day, and the law repealed it.
            Assert.That(
                MountainRoadVistaLightsController.EvaluateIntensity(0f),
                Is.EqualTo(
                        GameTimeDayNightRules.DayFixtureFloor *
                        MountainRoadVistaLightsController.NightIntensity)
                    .Within(0.0001f),
                "The distant city must burn by day too.");
            Assert.That(
                MountainRoadVistaLightsController.EvaluateIntensity(1f),
                Is.EqualTo(
                    MountainRoadVistaLightsController.NightIntensity)
                    .Within(0.0001f));

            float previous = -1f;
            for (float night = 0f; night <= 1f; night += 0.05f)
            {
                float value = MountainRoadVistaLightsController
                    .EvaluateIntensity(night);
                Assert.That(
                    value,
                    Is.GreaterThanOrEqualTo(previous),
                    "The city must not flicker on the way to night.");
                previous = value;
            }
        }

        private static float MaxTerrainElevation(
            MountainRoadPlan plan,
            Vector3 eye,
            Vector3 direction,
            float toDistance)
        {
            float worst = -90f;
            for (float step = 4f; step <= toDistance; step += 2f)
            {
                Vector3 point = eye + direction * step;
                float height = MountainRoadTerrainSampler.SampleHeight(
                    plan.Route,
                    plan.Plateau,
                    new Vector2(point.x, point.z));
                worst = Mathf.Max(
                    worst,
                    Mathf.Atan2(height - eye.y, step) * Mathf.Rad2Deg);
            }

            return worst;
        }
    }
}
