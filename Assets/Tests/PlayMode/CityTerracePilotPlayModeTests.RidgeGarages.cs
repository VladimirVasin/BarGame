using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest, Timeout(480000)]
        public IEnumerator LiveCity_GarageRoofAndWorkshopHaveSeparateArrivals()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(14 * 60), Is.True);
            yield return LoadProductionCity();
            var site = RidgeGaragePlan.ForLayout(city.Layout);
            var ground = new CityMapCityTeleportGround(city.Layout);
            var motor = city.Player.Motor;
            var follow = city.Camera.GetComponent<PlayerCameraFollow>();
            Vector3 offset = Vector3.up * PlayerFactory.GroundedRootOffset;
            var district = CityTerraceDistrictPlan.ForLayout(city.Layout);
            foreach (var arrival in new[] { site.LowerArrival, site.RoofArrival })
            {
                Assert.That(ground.TryClampArrival(arrival + offset, out var result), Is.True, "Garage map arrival " + arrival);
                Assert.That(result.y, Is.EqualTo(arrival.y + offset.y).Within(.015f));
            }
            Vector2 stacked = new(site.LowerArrival.x, site.LowerArrival.z);
            Assert.That(ground.TryResolveStandingPosition(stacked, out Vector3 roof), Is.True);
            Assert.That(roof.y, Is.EqualTo(site.Origin.y + RidgeGaragePlan.RoofHeight + offset.y).Within(.015f));
            motor.SetInputEnabled(false);
            var route = site.Path;
            motor.Teleport(route[0] + offset);
            yield return null;
            var garage = GameObject.Find("West Ridge Garages");
            Assert.That(garage, Is.Not.Null);
            var fixtures = garage.GetComponentsInChildren<Light>();
            foreach (int minute in new[] { 14 * 60, 23 * 60 })
            {
                AdvanceRidgeClock(minute);
                bool night = minute > 1000;
                string time = night ? "night" : "day";
                yield return Shot("garage-front-" + time, site.Origin + new Vector3(7.65f, 1.68f, 6.6f),
                    site.Origin + new Vector3(.25f, 1.35f, -.1f), 65f, Fixture("Entry"));
                yield return Shot("garage-repair-" + time, site.Origin + new Vector3(4.22f, 1.6f, .1f),
                    site.Origin + new Vector3(5.50f, 1.10f, -3.60f), 63f, Fixture("Inspection"));
                yield return Shot("garage-diffuse-" + time, site.Origin + new Vector3(4.22f, 1.6f, .1f),
                    site.Origin + new Vector3(5.50f, 1.10f, -3.60f), 63f, Fixture("Ceiling", LightType.Point));
                yield return Shot("garage-engine-" + time, site.Origin + new Vector3(4.0f, 1.10f, -2.01f),
                    site.Origin + new Vector3(5.11f, .54f, -2.66f), 60f);
                yield return Shot("garage-bench-" + time, site.Origin + new Vector3(4.20f, 1.57f, -3.33f),
                    site.Origin + new Vector3(5.67f, 1.38f, -5.48f), 63f, Fixture("Work"));

                Light Fixture(string id, LightType type = LightType.Spot)
                {
                    var spot = fixtures.Single(f => f.name == "Garage " + id + " light");
                    Assert.That(spot.enabled && spot.type == type && spot.intensity > 0f, Is.True, id);
                    return night ? spot : null;
                }
            }
            AdvanceRidgeClock(14 * 60);
            yield return Shot("garage-threshold", site.Origin + new Vector3(4.25f, 1.58f, .75f),
                site.Origin + new Vector3(5.5f, .92f, -3.6f));
            yield return Shot("garage-roof", site.Origin + new Vector3(-5.6f, 4.66f, -8f), site.Origin + new Vector3(6f, 3.12f, -3.3f));
#if UNITY_EDITOR
            if (System.Environment.GetEnvironmentVariable("BP_EXPORT_CITY_GARAGES_MAP") == "1")
                System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("ExportCurrent").Invoke(null, null);
#endif
            Assert.That(Physics.Raycast(site.Origin + new Vector3(4.28f, 1f, .85f), Vector3.back, 1.7f,
                ~0, QueryTriggerInteraction.Ignore), Is.False, "Clear doorway between the forecourt and the repair bay");
            motor.Teleport(route[0] + offset);
            for (int i = 0; i < 10; i++) yield return null;
            yield return WalkCityRoute(route, false, "garage-up", district);
            yield return WalkCityRoute(route, true, "garage-down", district);
            var mouth = site.LowerArrival + Vector3.forward * 2.3f;
            motor.Teleport(mouth + offset);
            yield return WalkCityRoute(new[] { mouth, site.LowerArrival }, false, "garage-door", district);
            Assert.That(Physics.Raycast(site.LowerArrival + Vector3.up, Vector3.up, 2f, ~0, QueryTriggerInteraction.Ignore),
                Is.True, "Roof above the open repair bay");
            var selectedLot = CityMetalWorkshop.Select(city.Layout);
            motor.Teleport(selectedLot.SidewalkArrivalPosition + offset);
            for (int frame = 0; frame < 10; frame++) yield return null;
            var halls = GameObject.Find("Glazed fabrication hall");
            for (int frame = 0; frame < 120 && !halls; frame++)
            {
                yield return null;
                halls = GameObject.Find("Glazed fabrication hall");
            }
            Assert.That(halls, Is.Not.Null, "Unique glazed fabrication hall");
            var pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(CityMetalWorkshop.Select(city.Layout));
            var pane = halls.GetComponentsInChildren<Renderer>().Single(r => r.name == "ClearGlass");
            Assert.That(Vector3.Distance(pane.bounds.center, pose.TransformPoint(new Vector3(-7.615f, 1.98f, .5f))), Is.LessThan(.02f));
            yield return Shot("hall-window", pose.TransformPoint(new Vector3(-11.4f, 1.67f, 1.1f)),
                pose.TransformPoint(CityMetalWorkshop.WindowLocal));
            yield return Shot("hall-roof", pose.TransformPoint(new Vector3(12f, 9f, 15f)),
                pose.TransformPoint(new Vector3(0f, 6.8f, .5f)), 60f);
            var tower = city.World.DecorationPlan.Descriptors.Single(d => d.Kind == CityDecorationKind.OldTownClockTower);
            var host = city.Layout.BuildingLots.Single(lot => lot.Cell == tower.LotCell);
            var towerPose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(host);
            var side = towerPose.TransformPoint(new Vector3(CityBuildingAssetProvider.GetExpectedEnvelope(host.District, host.BuildingVariant).x * .5f, 0f, -.28f));
            var outward = towerPose.Rotation * Vector3.right;
            var sign = GameObject.Find(CityFishCannerySign.ClockName).transform;
            var print = sign.GetComponentsInChildren<Renderer>().Single(part => part.name == "PrintedFace");
            Assert.That(Vector3.Distance(sign.position, side + Vector3.up * 3.66f), Is.LessThan(.02f));
            Assert.That(Vector3.Dot(sign.forward, outward), Is.GreaterThan(.99f));
            var material = new MaterialPropertyBlock();
            print.GetPropertyBlock(material);
            var advert = Resources.Load<Texture2D>("City/ClockFishSign/ClockFishSignAlbedo");
            Assert.That(advert, Is.Not.Null);
            Assert.That(material.GetTexture("_BaseMap"), Is.SameAs(advert));
            Assert.That(material.GetFloat("_Smoothness"), Is.LessThan(.1f));
            Assert.That(print.bounds.size.y, Is.EqualTo(3.8f).Within(.01f));
            Assert.That(print.bounds.size.z / print.bounds.size.y, Is.EqualTo((float)advert.width / advert.height).Within(.01f));
#if UNITY_EDITOR
            AreaCaptureFixture.AssertSignReadsFromStreet(sign, print);
#endif
            var lenses = sign.GetComponentsInChildren<Transform>().Where(part =>
                part.name == CityNightAtmosphere.IndustrialTaskLightPrefix + CityFishCannerySign.ClockPurpose).ToArray();
            Assert.That(lenses.Length, Is.EqualTo(2));
            var lane = side + outward * 1.7f + sign.right * 3.65f;
            Assert.That(city.World.WalkableArea.Contains(lane, .35f), Is.True, "Open alley beside the clock sign");
            motor.Teleport(lane + offset);
            var atmosphere = city.Night.Atmosphere;
            float dayStrength = 0f;
            foreach (int minute in new[] { 14 * 60, 23 * 60 })
            {
                AdvanceRidgeClock(minute);
                atmosphere.RefreshImmediate();
                var spots = lenses.Select(lens => atmosphere.StreetLightPool.Single(light => light.isActiveAndEnabled &&
                    Vector3.Distance(lens.position, light.transform.position) < .01f)).ToArray();
                Assert.That(spots.All(light => light.type == LightType.Spot && light.intensity > 0f), Is.True);
                foreach (float x in new[] { -2.35f, -1.25f, 1.25f, 2.35f })
                foreach (float y in new[] { -1.5f, 0f, 1.5f })
                {
                    var sample = print.bounds.center + sign.right * x + sign.up * y;
                    Assert.That(spots.Any(light => Vector3.Distance(light.transform.position, sample) < light.range &&
                        Vector3.Angle(light.transform.forward, sample - light.transform.position) < light.spotAngle * .45f),
                        Is.True, "Floods reach both the lettering and the fish at " + sample);
                }
                bool night = minute == 23 * 60;
                if (night) Assert.That(dayStrength, Is.GreaterThanOrEqualTo(spots[0].intensity * (2f / 3f) - .001f));
                else dayStrength = spots[0].intensity;
                yield return Shot("clock-facade-" + (night ? "night" : "day"), side + outward * 5.85f + Vector3.up * 1.29f,
                    print.bounds.center, 59f, night ? spots[0] : null, night ? spots[1] : null);
            }
            AdvanceRidgeClock(14 * 60);
            motor.SetInputEnabled(true);
            follow.ClearFixedPose();
            if (System.Environment.GetEnvironmentVariable("BP_GARAGE_AUDIT_PLATFORMS") == "1")
                yield return VerifyEveryBusStopCanBeBoardedFromThePublicPavement();

            IEnumerator Shot(string id, Vector3 eye, Vector3 target, float fov = 72f, Light omitForComparison = null,
                Light secondOmitted = null)
            {
                follow.SetFixedPose(eye, Quaternion.LookRotation(target - eye), fov);
                for (int frame = 0; frame < 12; frame++) yield return null;
                AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "CityGarageWorks", id, 1280, 720);
                if (omitForComparison)
                {
                    omitForComparison.enabled = false;
                    if (secondOmitted) secondOmitted.enabled = false;
                    try { AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "CityGarageWorks", id + "-unlit", 1280, 720); }
                    finally
                    {
                        omitForComparison.enabled = true;
                        if (secondOmitted) secondOmitted.enabled = true;
                    }
                }
                follow.ClearFixedPose();
            }
        }
    }
}
