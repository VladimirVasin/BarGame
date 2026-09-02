using System.Collections.Generic;
using System.Linq;
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
        /// This area's exterior band. The two interior cafe keys need larger
        /// raw values because they throw across the room onto near-black
        /// clothing; their cones remain inside the cafe. A city value carried
        /// over to an exterior yard lamp once made that lamp three and a half
        /// times brighter than the rest of the summit.
        /// </summary>
        private const float MinimumIntensity = 1.5f;

        private const float MaximumIntensity = 18f;

        private const float MaximumCafeKeyIntensity = 60f;

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
                    bool isCafeInteriorKey =
                        light.name == "Sulphur Counter Light" ||
                        light.name == "Cold Service Light";
                    float maximumIntensity = isCafeInteriorKey
                        ? MaximumCafeKeyIntensity
                        : MaximumIntensity;
                    Assert.That(
                        light.intensity,
                        Is.InRange(MinimumIntensity, maximumIntensity),
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

                Light warm = cafeLights.Single(light =>
                    light.name == "Sulphur Counter Light");
                Light cold = cafeLights.Single(light =>
                    light.name == "Cold Service Light");
                Assert.That(cold.enabled, Is.True);
                Assert.That(
                    cold.spotAngle,
                    Is.EqualTo(110f).Within(0.001f));
                Assert.That(
                    cold.innerSpotAngle,
                    Is.EqualTo(100f).Within(0.001f));
                Assert.That(
                    Vector3.Dot(cold.transform.forward, Vector3.down),
                    Is.GreaterThan(0.70f),
                    "The stove practical still points across the room " +
                    "instead of down onto its task surface.");
                Assert.That(
                    world.Cafe.Model.TryGetAnchor(
                        "StovePanDock",
                        out Transform stovePanDock),
                    Is.True);
                Assert.That(
                    IsInsideInnerCone(cold, stovePanDock.position),
                    Is.True,
                    "The visible cold practical does not light the stove " +
                    "and frying pan below it.");
                Assert.That(
                    warm.shadows,
                    Is.EqualTo(LightShadows.None),
                    "The sleeping head and folded forearms self-occlude " +
                    "under the warm key when it casts a hard shadow.");
                MountainRoadCafeCastPlan castPlan =
                    MountainRoadCafeCastPlan.Create(plan.Terminal.Cafe);
                foreach (MountainRoadCafeCastMemberPlan member in
                         castPlan.Members)
                {
                    float readingHeight =
                        member.Role == MountainRoadCafeCastRole.LonePatron
                            ? 1.13f
                            : 1.25f;
                    Vector3 readingPoint =
                        member.Position + Vector3.up * readingHeight;
                    Assert.That(
                        IsInsideInnerCone(cold, readingPoint),
                        Is.True,
                        $"The service practical misses {member.Role} at " +
                        "counter-reading height.");
                    Assert.That(
                        IsInsideInnerCone(wash, readingPoint),
                        Is.True,
                        $"The shadowless wash leaves {member.Role} without " +
                        "the cafe's common fill.");

                    // The pair and sleeper face the service side; the
                    // attendant faces them from behind the counter. A point
                    // can sit inside a cone and remain black if that light is
                    // behind its visible surfaces, so pin the useful frontal
                    // source and a bounded incident-light proxy as well.
                    Light frontalFill =
                        member.Role == MountainRoadCafeCastRole.Attendant
                            ? wash
                            : cold;
                    Vector3 toLight =
                        frontalFill.transform.position - readingPoint;
                    float incidence = Vector3.Dot(
                        member.Facing,
                        toLight.normalized);
                    Assert.That(
                        incidence,
                        Is.GreaterThan(0.45f),
                        $"{member.Role} is inside a cone but turns away from " +
                        "its useful fill.");
                    Assert.That(
                        toLight.magnitude / frontalFill.range,
                        Is.LessThanOrEqualTo(0.65f),
                        $"{member.Role} sits in the steep end-of-range fade " +
                        "of its useful fill.");
                    float frontalFillLevel =
                        frontalFill.intensity * incidence /
                        Mathf.Max(0.01f, toLight.sqrMagnitude);
                    float minimumFillLevel =
                        member.Role == MountainRoadCafeCastRole.Attendant
                            ? 0.18f
                            : 0.75f;
                    float maximumFillLevel =
                        member.Role == MountainRoadCafeCastRole.Attendant
                            ? 0.40f
                            : 1.50f;
                    Assert.That(
                        frontalFillLevel,
                        Is.InRange(minimumFillLevel, maximumFillLevel),
                        $"{member.Role} receives {frontalFillLevel:0.000} " +
                        "from its useful fill, outside the role's authored " +
                        "dark-clothing/light-uniform compensation band.");
                }

                MountainRoadCafeCastMemberPlan lonePatron =
                    castPlan.Members.Single(member =>
                        member.Role == MountainRoadCafeCastRole.LonePatron);
                Vector3 sleepingHead =
                    lonePatron.Position + Vector3.up * 1.13f;
                Assert.That(
                    IsInsideInnerCone(warm, sleepingHead),
                    Is.True,
                    "The warm practical no longer reads the sleeping " +
                    "patron in his contact-frame pose.");

                Assert.That(
                    IsInsideInnerCone(wash, apron),
                    Is.True,
                    "The technical wash no longer covers the arrival apron.");
                float closestDarkBandMargin = plan.Terminal.Site.Parts
                    .Where(part =>
                        part.Group == MountainRoadSiteGroup.Terrace ||
                        part.Group == MountainRoadSiteGroup.Brink)
                    .Min(part =>
                        ConeAngleDegrees(wash, part.Center) -
                        wash.spotAngle * 0.5f);
                Assert.That(
                    closestDarkBandMargin,
                    Is.GreaterThanOrEqualTo(3f),
                    "The cafe wash reaches the terrace or the black brink " +
                    $"(nearest cone margin {closestDarkBandMargin:0.00} deg).");

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

        private static bool IsInsideInnerCone(
            Light light,
            Vector3 point)
        {
            return Vector3.Distance(light.transform.position, point) <=
                       light.range &&
                   ConeAngleDegrees(light, point) <=
                       light.innerSpotAngle * 0.5f;
        }

        private static float ConeAngleDegrees(
            Light light,
            Vector3 point)
        {
            return Vector3.Angle(
                light.transform.forward,
                point - light.transform.position);
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
