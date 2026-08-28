using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// What burns on the summit, and how hard.
    ///
    /// Two failures this pins have already happened. The cafe lit only its
    /// own room, so from the yard it was a glowing box rather than
    /// something to steer towards; and the cable station ran at `1.65`
    /// beside a cafe counter at `10.5`, which made it a night-light next
    /// to a lit window. Neither showed up as an error, because neither is
    /// one.
    /// </summary>
    public sealed class MountainRoadSummitLightingTests
    {
        /// <summary>
        /// This area's own band. The documented CITY practicals run `31`
        /// to `240` and are a different scale entirely: a number carried
        /// across from that list once put the yard lamp at `38`, three and
        /// a half times the brightest thing up here.
        /// </summary>
        private const float MinimumIntensity = 1.5f;

        private const float MaximumIntensity = 18f;

        [Test]
        [Category("MountainRoad")]
        public void Summit_IsLitByBothItsLandmarksAndOnItsOwnScale()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            var parent = new GameObject("Mountain Lighting Test Parent");
            var cameraObject = new GameObject("Mountain Lighting Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            try
            {
                MountainRoadWorldResult world =
                    MountainRoadWorldBuilder.Build(
                        parent.transform,
                        plan,
                        camera);

                Vector3 apron = plan.Terminal.VehicleApron.Center;
                var onTheSummit = new List<Light>();
                Light[] lights = parent.GetComponentsInChildren<Light>(true);
                for (int index = 0; index < lights.Length; index++)
                {
                    if (Vector3.Distance(
                            lights[index].transform.position,
                            apron) <= 45f)
                    {
                        onTheSummit.Add(lights[index]);
                    }
                }

                Assert.That(
                    onTheSummit,
                    Has.Count.EqualTo(6),
                    "The summit burns three in the cafe and three at the " +
                    "cable station - the pad lens, the yard flood and the " +
                    "boom over the boarding dock; the yard lamp belongs to " +
                    "the atmosphere and is not built here.");

                for (int index = 0; index < onTheSummit.Count; index++)
                {
                    Light light = onTheSummit[index];
                    Assert.That(
                        light.intensity,
                        Is.InRange(MinimumIntensity, MaximumIntensity),
                        $"'{light.name}' burns at {light.intensity:0.00}, " +
                        "outside this area's own scale.");
                    Assert.That(light.type, Is.EqualTo(LightType.Spot));
                }

                // The cafe has to reach past its own glass. A fixture
                // standing inside the footprint cannot, whatever its
                // range: the walls are opaque.
                Light wash = null;
                IReadOnlyList<Light> cafeLights = world.Cafe.Lights;
                for (int index = 0; index < cafeLights.Count; index++)
                {
                    Vector3 position = cafeLights[index].transform.position;
                    if (!plan.Terminal.Cafe.ContainsInterior(position, 0f))
                    {
                        wash = cafeLights[index];
                    }
                }

                Assert.That(
                    wash,
                    Is.Not.Null,
                    "Every cafe light stands inside the cafe, so the " +
                    "building throws none onto the yard.");
                Assert.That(
                    Vector3.Distance(wash.transform.position, apron),
                    Is.LessThan(wash.range),
                    "The facade wash does not reach where the car parks.");
                Assert.That(
                    wash.shadows,
                    Is.EqualTo(LightShadows.None),
                    "A wash that silhouettes its own cafe is wrong and " +
                    "expensive.");

                // And the station is the other half of the pair, cold
                // against the cafe's sulphur, reaching its own dock.
                Assert.That(
                    world.Cableway.StationLight.intensity,
                    Is.GreaterThan(5f),
                    "The station is a night-light beside the cafe again.");
                Assert.That(
                    plan.Terminal.Site.TryGetPart(
                        "site-loading-kerb",
                        out MountainRoadSitePartDescriptor kerb),
                    Is.True);

                float reach = float.PositiveInfinity;
                for (int index = 0; index < onTheSummit.Count; index++)
                {
                    Light light = onTheSummit[index];
                    if (!light.name.Contains("Station") &&
                        !light.name.Contains("Boarding"))
                    {
                        continue;
                    }

                    reach = Mathf.Min(
                        reach,
                        Vector3.Distance(
                            light.transform.position,
                            kerb.Center) - light.range);
                }

                Assert.That(
                    reach,
                    Is.LessThan(0f),
                    "No station fixture reaches the freight kerb it is " +
                    "supposed to be lighting.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        [Category("MountainRoad")]
        public void YardLamp_StaysOnTheAreaScale()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            MountainRoadSitePracticalDescriptor lamp =
                plan.Terminal.Site.YardLamp;

            Assert.That(
                plan.Terminal.Site.TryGetPart(
                    lamp.StableId,
                    out MountainRoadSitePartDescriptor fixture),
                Is.True,
                "The yard lamp must burn from a fixture you can see.");
            Assert.That(
                Vector3.Distance(lamp.Position, fixture.Center),
                Is.LessThan(0.4f),
                "The lamp and its shade have drifted apart.");
            Assert.That(
                Vector3.Dot(lamp.Direction, Vector3.down),
                Is.GreaterThan(0.9f),
                "A yard lamp points at the yard.");
            Assert.That(lamp.Range, Is.InRange(8f, 20f));
        }
    }
}
