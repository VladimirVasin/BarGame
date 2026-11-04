using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator LiveCity_SummitHomeFacesTownBesideTheUpperBridge()
        {
            yield return LoadProductionCity();
            var home = city.Layout.PlayerHome;
            var quarter = CityHillsideQuarterPlan.ForLayout(city.Layout);
            var bridge = quarter.Bridges.Single(b => b.Id == "summit");
            var lot = CityHillsideNeighborhoodPlan.ForLayout(city.Layout).AllBuildings.Single(b => b.Lot.IsPlayerHome);
            Assert.That(lot.OnSummitStreet, Is.True);
            Assert.That(home, Is.SameAs(lot.Lot));
            Vector3 forward = PlayerHomeBalconyGeometry.GetFrontageDirection(home);
            Vector3 eye = home.DoorPosition + forward * (PlayerHomeBalconyGeometry.BalconyDepth - .5f) +
                PlayerHomeBalconyGeometry.GetTangent(home) * PlayerHomeBalconyGeometry.BalconyCenterZ +
                Vector3.up * (PlayerHomeBalconyGeometry.ApartmentFloorElevation + 1.60f);
            Vector3 towardCity = Vector3.ProjectOnPlane(city.Layout.Supermarket.Center - eye, Vector3.up).normalized;
            Assert.That(Vector3.Dot(forward, towardCity), Is.GreaterThan(.64f), "windows face the lower city");
            Vector3 edge = bridge.WestBearingWorld;
            float eyeToEast = Vector3.Distance(eye, bridge.EastBearingWorld);
            if (eyeToEast < Vector3.Distance(eye, edge)) edge = bridge.EastBearingWorld;
            Assert.That(Vector3.Distance(eye, edge), Is.LessThan(22f), "balcony next to a bridge abutment");
            var mapHome = city.Map.ActiveMapPoints.Single(p => p.StableId == "city:home");
            Assert.That(mapHome.WorldPosition, Is.EqualTo(home.Center));
            Assert.That(CityMapArtResources.TryGet(city.Map, out var sheet, out var bounds), Is.True, "The shipped charcoal map must bind to this summit home and pedestrian network.");
            Assert.That(sheet.isReadable, Is.False);
            Assert.That(bounds.Contains(new Vector2(home.Center.x, home.Center.z)), Is.True);
            if (System.Environment.GetEnvironmentVariable("BAR_PROMENADE_SUMMIT_HOME") != "1" ||
                SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            double toNoon = (2160d - GameSessionState.GameTimeOfDayMinutes) % 1440d;
            GameSessionState.AdvanceGameTime((float)(toNoon / GameTimeState.GameMinutesPerRealSecond));
            city.Player.Motor.Teleport(lot.StreetContactWorld + Vector3.up * PlayerFactory.GroundedRootOffset);
            Physics.SyncTransforms();
            yield return null;
            var camera = city.Camera;
            var pose = new Pose(camera.transform.position, camera.transform.rotation);
            try
            {
                Shot("city-balcony-straight", eye, forward - Vector3.up * .15f);
                Vector3 bridgeBearing = Vector3.ProjectOnPlane(bridge.DeckCenterWorld - eye, Vector3.up).normalized;
                Shot("city-balcony-bridge", eye, (bridgeBearing + towardCity).normalized - Vector3.up * .27f);
                Debug.Log($"Summit balcony {eye:F2}, facade {forward:F2}, bridge {bridge.DeckCenterWorld:F2}, fog {RenderSettings.fogDensity:F4}.");
            }
            finally { camera.transform.SetPositionAndRotation(pose.position, pose.rotation); }
            void Shot(string name, Vector3 at, Vector3 direction)
            {
                camera.transform.SetPositionAndRotation(at, Quaternion.LookRotation(direction));
                AreaCaptureFixture.CaptureCurrentCamera(camera, "SummitHome", name, 1280, 800);
            }
        }
    }
}
