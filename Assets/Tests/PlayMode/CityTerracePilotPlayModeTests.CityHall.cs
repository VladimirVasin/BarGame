using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator CityHall_BothStairsClockAndVestibuleReturnToUpperCourt()
        {
            yield return LoadProductionCity();
            var terraces = CityTerraceDistrictPlan.ForLayout(city.Layout);
            var site = terraces.CityHall;
            var hall = city.World.Root.GetComponentInChildren<CityHallWorld>();
            Assert.That(hall, Is.Not.Null);
            Assert.That(hall.GetComponentsInChildren<VillageResidentPresentation>().Length, Is.EqualTo(2));
            Assert.That(city.Map.PointsOfInterest.Single(p => p.Kind == CityMapPointOfInterestKind.CityHall).WorldPosition,
                Is.EqualTo(site.Arrival));
            var ground = new CityMapCityTeleportGround(city.Layout);
            Assert.That(ground.TryResolveStandingPosition(new Vector2(site.Arrival.x,site.Arrival.z), out var mapped), Is.True);
            Assert.That(Vector3.Distance(mapped, site.Arrival), Is.LessThan(.04f));
            var hands = hall.GetComponentsInChildren<Transform>();
            Transform hour = hands.Single(t => t.name == "HourHand"), minute = hands.Single(t => t.name == "MinuteHand");
            Assert.That(Mathf.Abs(hour.position.y - site.Threshold.y - 10.35f), Is.LessThan(.02f));
            var approachEye = terraces.EntryStartWorld + new Vector3(0,1.68f,0);
            var approachTarget = (hour.position + terraces.UpperArrivalWorld) * .5f;

            city.Player.Motor.Teleport(terraces.EntryStartWorld + Vector3.forward * 2.2f
                + Vector3.up * PlayerFactory.GroundedRootOffset);
            yield return null;
            Assert.That(GameSessionState.TryPrepareGameTimeAt(0), Is.True);
            city.DayNight.ApplyCurrentTime(true);
            city.Night.Atmosphere.RefreshImmediate();
            yield return null;
            var northHour = hour.rotation;
            var northMinute = minute.rotation;
            Assert.That(GameSessionState.TryPrepareGameTimeAt(22 * 60 + 12), Is.True);
            city.DayNight.ApplyCurrentTime(true);
            city.Night.Atmosphere.RefreshImmediate();
            yield return null;
            Vector3 gateEye = site.Threshold + new Vector3(-.74f,1.68f,6.6f);
            Vector3 gateTarget = site.Threshold + new Vector3(0,1.46f,.78f);
            var night = city.Night.Atmosphere;
            var fixtures = night.LampAnchors.Select((anchor,index) => (anchor,light: night.GetLightForAnchor(index)))
                .Where(pair => pair.anchor.IsChildOf(hall.transform)).ToArray();
            Assert.That(fixtures.Length,Is.EqualTo(3));
            foreach (var pair in fixtures)
            {
                Assert.That(pair.light.enabled && pair.light.intensity > 0f);
                Assert.That(pair.light.shadows,Is.EqualTo(LightShadows.Soft));
            }
            var gate = fixtures.Single(pair => pair.anchor.name.EndsWith("CityHallGate")).light;
            var canopy = fixtures.Single(pair => pair.anchor.name.EndsWith("CityHallEntrance")).light;
            var dial = fixtures.Single(pair => pair.anchor.name.EndsWith("CityHallDial")).light;
            AssertLitBy(gate, site.GuardStation(0) + Vector3.up * 1.45f);
            AssertLitBy(gate, site.GuardStation(1) + Vector3.up * 1.45f);
            AssertLitBy(canopy, hall.Entrance.InteractionPosition);
            AssertLitBy(dial, hour.position);
            CaptureCity("hall-night",approachEye,approachTarget);
            CaptureCity("hall-night-steps",terraces.LowerArrivalWorld + Vector3.up * 1.68f, terraces.UpperArrivalWorld);
            CaptureCity("hall-night-entrance",gateEye,gateTarget);
            CaptureCity("hall-night-dial",terraces.LowerArrivalWorld + Vector3.up * 1.68f,hour.position);
            Assert.That(GameSessionState.TryPrepareGameTimeAt(570), Is.True);
            city.DayNight.ApplyCurrentTime(true);
            city.Night.Atmosphere.RefreshImmediate();
            yield return null;
            Assert.That(hall.DisplayedMinute, Is.EqualTo(570));
            Assert.That(Physics.Linecast(approachEye, hour.position, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore), Is.False, "The dial must be visible from the street mouth.");
            Assert.That(Quaternion.Angle(hour.rotation, Quaternion.AngleAxis(285,Vector3.left)*northHour),Is.LessThan(.1f));
            Assert.That(Quaternion.Angle(minute.rotation, Quaternion.AngleAxis(180,Vector3.left)*northMinute),Is.LessThan(.1f));
            CaptureCity("hall-approach",approachEye,approachTarget);
            CaptureCity("hall-dial", terraces.LowerArrivalWorld + Vector3.up*1.68f, hour.position);
            CaptureCity("hall-entrance",site.Threshold+new Vector3(-1f,1.68f,5.6f),site.Threshold+Vector3.up*1.48f);
#if UNITY_EDITOR
            if (System.Environment.GetEnvironmentVariable("BP_EXPORT_CITY_HALL_SURVEY") == "1")
                System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor",true)
                    .GetMethod("ExportCurrent").Invoke(null,null);
#endif
            isolatedHeroCapsule = city.Player.GameObject.GetComponent<CharacterController>();
            city.Player.Motor.Teleport(terraces.LowerArrivalWorld + Vector3.up * PlayerFactory.GroundedRootOffset);
            city.Player.Motor.SetInputEnabled(false);
            for (int frame = 0; frame < 5; frame++) yield return null;
            yield return WalkCityRoute(DistrictRoute(terraces,terraces.Pilot.WestRoute),false,"hall-west-up",terraces);
            // Leave the spine on the southern platform only after crossing the bridge.
            var branch = new[] { terraces.UpperArrivalWorld, new Vector3(terraces.UpperArrivalWorld.x,
                site.Threshold.y,site.Arrival.z), site.Arrival - Vector3.up*PlayerFactory.GroundedRootOffset,
                site.DoorAction.EntryRootPosition - Vector3.up*PlayerFactory.GroundedRootOffset };
            CaptureCity("hall-upper-view", terraces.UpperArrivalWorld + new Vector3(.48f,1.68f,-1.30f),
                site.Threshold + new Vector3(-5,6,-1));
            yield return WalkCityRoute(branch,false,"hall-door",terraces);
            city.Player.Motor.SetInputEnabled(true);
            yield return UseDoor(city.Player, city.Camera, hall.Entrance, SceneIds.CityHallInterior);
            var room = Object.FindAnyObjectByType<CityHallInteriorRoot>();
            Assert.That(room, Is.Not.Null);
            Assert.That(city.IsDormant, Is.True);
            var follow = room.Camera.GetComponent<PlayerCameraFollow>();
            var roomLights = room.GetComponentsInChildren<Light>();
            AssertLitBy(roomLights.Single(l => l.name == "Counter hood"), new Vector3(.53f,1.64f,6.56f));
            AssertLitBy(roomLights.Single(l => l.name == "Entrance strip"), room.Exit.InteractionPosition);
            var eye = new Vector3(1.82f,1.66f,1.21f);
            follow.SetFixedPose(eye, Quaternion.LookRotation(new Vector3(-.42f,1.36f,5.55f)-eye),65f);
            yield return null;
            AreaCaptureFixture.CaptureCurrentCamera(room.Camera,"CityTerracePilot","hall-vestibule",1280,720);
            yield return UseDoor(room.Player,room.Camera,room.Exit,SceneIds.City);
            Assert.That(Vector3.Distance(city.Player.GameObject.transform.position,site.Return),Is.LessThan(.22f));
            city.Player.Motor.SetInputEnabled(false);
            yield return WalkCityRoute(new[] { site.Arrival - Vector3.up*PlayerFactory.GroundedRootOffset,
                branch[1],branch[0] },false,"hall-return",terraces);
            yield return WalkCityRoute(DistrictRoute(terraces,terraces.Pilot.EastRoute),true,"hall-east-down",terraces);
            yield return WalkCityRoute(new[] { terraces.LowerArrivalWorld, terraces.LowerPassStartWorld,
                terraces.LowerCrossingWorld,terraces.LowerPassEndWorld },false,"hall-underpass",terraces);
        }

        private static void AssertLitBy(Light light, Vector3 target)
        {
            Vector3 ray = target - light.transform.position;
            Assert.That(ray.magnitude, Is.LessThan(light.range), light.name);
            Assert.That(Vector3.Angle(light.transform.forward,ray),Is.LessThan(light.spotAngle * .5f),light.name);
        }

        private static IEnumerator UseDoor(PlayerRuntime player, Camera camera, CityHallDoor door, string destination)
        {
            var eye = player.GameObject.transform.position + new Vector3(0,1.45f,.60f);
            camera.GetComponent<PlayerCameraFollow>().SetFixedPose(eye,Quaternion.LookRotation(door.InteractionPosition-eye),60);
            Physics.SyncTransforms();
            yield return null;
            yield return null;
            Assert.That(player.Interactor.ActiveInteractable,Is.SameAs(door));
            door.Interact(player.Interactor);
            float deadline = Time.realtimeSinceStartup + 75f;
            while (SceneTransitionService.IsTransitioning || SceneManager.GetActiveScene().name != destination)
            {
                Assert.That(Time.realtimeSinceStartup,Is.LessThan(deadline),destination);
                yield return null;
            }
        }
    }
}
