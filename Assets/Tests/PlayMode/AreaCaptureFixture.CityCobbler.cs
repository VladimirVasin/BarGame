using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Cobbler's bus, player-height hands-through-glass views, physical entry and home schedule.")]
        public IEnumerator CityCobbler()
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeAt(720), Is.True);
            CityGameRoot city = null;
            CityCobblerWorker cobbler = null;
            CityCobblerSitePlan site = null;
            PlayerCameraFollow follow = null;
            bool followEnabled = false, weatherEnabled = false, motorEnabled = false;
            float previousDelta = Time.captureDeltaTime;
            try
            {
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized && !CompositionDriver.IsComposing ? city : null;
                }, () =>
                {
                    site = CityCobblerSitePlan.ForLayout(city.Layout);
                    Assert.That(site.IsEnabled, Is.True);
                    cobbler = city.World.Root.GetComponentsInChildren<CityCobblerWorker>(true).Single();
                    cobbler.AutoAdvance = false;
                    follow = city.Camera.GetComponent<PlayerCameraFollow>();
                    followEnabled = follow.enabled; follow.enabled = false;
                    weatherEnabled = city.Weather.enabled; city.Weather.enabled = false;
                    motorEnabled = city.Player.Motor.InputEnabled; city.Player.Motor.SetInputEnabled(false);
                    var road = CityHillsideStreetPlan.ForLayout(city.Layout).Routes.Single(r => r.Bank == CityTerraceTransportBank.East);
                    var court = CityHillsideQuarterPlan.ForLayout(city.Layout).Links[0];
                    Vector3 target = Part("ANCHOR_ShoeRest").position + Vector3.up * .12f;
                    Vector3 eye = new Vector3(49f, court.PavementTopAt(49f) + EyeHeight, -303f);
                    var nearXZ = new Vector2(52.5f, -303f);
                    Assert.That(road.TrySampleTop(nearXZ, out float nearTop, out _), Is.True);
                    Vector3 nearEye = new Vector3(nearXZ.x, nearTop + EyeHeight, nearXZ.y);
                    Vector3 insideEye = site.ToWorld(new Vector3(.30f, .48f + EyeHeight, 1.65f));
                    var ground = CityHillsideGroundPlan.ForLayout(city.Layout);
                    var floorXZ = new Vector2(63.7f, -307.8f);
                    Assert.That(ground.TrySampleTop(floorXZ, out float floorGround), Is.True);
                    Vector3 floorEye = new(floorXZ.x, floorGround + EyeHeight, floorXZ.y);
                    var yardView = new Vector2(65.5f, -300.1f);
                    Assert.That(ground.TrySampleTop(yardView, out float yardTop), Is.True);
#if UNITY_EDITOR
                    if (Environment.GetEnvironmentVariable("BAR_EXPORT_COBBLER_MAP") == "1")
                        Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                            .GetMethod("ExportCurrent", Type.EmptyTypes).Invoke(null, null);
#endif
                    return new[]
                    {
                        Frame("cobbler-sidewalk-day", eye, target, 60f, 720, 1.5),
                        Frame("cobbler-sidewalk-night", eye, target, 60f, 1155, 1.5),
                        Frame("cobbler-yard-day", new Vector3(yardView.x, yardTop + EyeHeight, yardView.y),
                            new Vector3(69.3f, site.Origin.y + .95f, -306.3f), 65f, 720, 1.5),
                        Frame("cobbler-yard-night", new Vector3(yardView.x, yardTop + EyeHeight, yardView.y),
                            new Vector3(69.3f, site.Origin.y + .95f, -306.3f), 65f, 1155, 1.5),
                        Frame("cobbler-yard-south", new Vector3(57f, site.Origin.y + EyeHeight, -318f),
                            new Vector3(69.6f, site.Origin.y + 1.1f, -308f), 60f, 720, 1.5),
                        Frame("cobbler-sign-night", site.Approach + site.Rotation * new Vector3(1.2f, EyeHeight, -.35f),
                            Part("ANCHOR_SignAim").position, 60f, 1155, 1.5),
                        Frame("cobbler-sign-day", site.Approach + site.Rotation * new Vector3(1.2f, EyeHeight, -.35f),
                            Part("ANCHOR_SignAim").position, 60f, 720, 1.5),
                        Frame("cobbler-near-sidewalk-hands", nearEye, target, 60f, 1155, 1.5),
                        Frame("cobbler-hands-through-glass", nearEye, target, 32f, 1155, 5.5),
                        Frame("cobbler-inside-hands", insideEye, target, 60f, 1155, 1.5),
                        Frame("cobbler-lunch", insideEye, Part("ANCHOR_Lunch").position + Vector3.up, 60f, 855, 0),
                        Frame("cobbler-closed-empty-bench", eye, target, 60f, 1320, 0),
                        Frame("cobbler-floor-front", floorEye, site.ToWorld(new Vector3(0f, .48f, .5f)), 60f, 720, 1.5),
                        Frame("cobbler-floor-front-offset", floorEye + new Vector3(.15f, .03f, -.10f),
                            site.ToWorld(new Vector3(0f, .48f, .5f)), 60f, 720, 1.5),
                        Frame("cobbler-switch-off", insideEye, Part("ANCHOR_LampSwitch").position, 60f, 1230, 1.1),
                        Frame("cobbler-switch-on", insideEye, Part("ANCHOR_LampSwitch").position, 60f, 660, -.9)
                    };

                    Shot Frame(string name, Vector3 from, Vector3 aim, float fov, int minute, double phase)
                    {
                        var pose = Shot.At(name, from, aim, fov);
                        int frames = 0;
                        return Shot.At(name, from, aim, fov, readyWhen: () =>
                        {
                            if (frames++ == 0)
                            {
                                SetChurchRemodelTime(minute);
                                city.DayNight.ApplyCurrentTime(true);
                                float rain = CityEternalRainShaper.FloorIntensity(0f);
                                city.Rain.SetIntensity(rain);
                                CityWaterResources.SetRainIntensity(rain);
                                CityWetSurfaceRegistry.SetImmediate(rain);
                                MoveCityLightingObserver(city, pose);
                            }
                            cobbler.ApplyAt(minute / GameTimeState.GameMinutesPerRealSecond + phase);
                            return frames > SettleFrames;
                        });
                    }
                    Transform Part(string name) => CityCobblerAssetProvider.FindPart(cobbler.transform, name);
                });

                Assert.That(cobbler.HomeStableId, Is.EqualTo(CityCobblerWorkerPlan.ResidenceId));
                Assert.That(cobbler.CommutePlan.House.Lot.Cell, Is.EqualTo(new Vector2Int(21, -6)));
                foreach (int minute in new[] { 660, 720, 1155, 1229 })
                {
                    cobbler.ApplyAt(minute / GameTimeState.GameMinutesPerRealSecond + 1.5d);
                    Assert.That(cobbler.IsWorking, Is.True, "Working minute " + minute);
                    Assert.That(cobbler.HandContactError, Is.LessThan(.075f), "Hands must reach the same shoe/tool.");
                    Assert.That(cobbler.SoleContactError, Is.LessThan(.075f), "Seated feet keep their authored floor contacts.");
                }
                cobbler.ApplyAt(855d / GameTimeState.GameMinutesPerRealSecond);
                Assert.That(cobbler.IsAtLunch, Is.True);
                cobbler.ApplyAt(1320d / GameTimeState.GameMinutesPerRealSecond);
                Assert.That(cobbler.IsAtHome, Is.True);
                double closing = 1230d / GameTimeState.GameMinutesPerRealSecond;
                cobbler.ApplyAt(closing + cobbler.CommuteSeconds * .5d);
                Assert.That(cobbler.IsCommuting, Is.True);
                Vector3 evening = cobbler.Worker.transform.position;
                double opening = 660d / GameTimeState.GameMinutesPerRealSecond;
                cobbler.ApplyAt(opening - cobbler.CommuteSeconds * .5d);
                Assert.That(cobbler.IsCommuting, Is.True);
                Assert.That(Vector3.Distance(cobbler.Worker.transform.position, evening), Is.LessThan(3f),
                    "Both trips use the assigned house and same continuous sidewalk.");
                cobbler.ApplyAt(1155d / GameTimeState.GameMinutesPerRealSecond + 1.5d);
                Vector3 hand = cobbler.Worker.RightGrip.position;
                for (int frame = 0; frame < 4; frame++) yield return null;
                Assert.That(Vector3.Distance(hand, cobbler.Worker.RightGrip.position), Is.LessThan(.01f),
                    "Held work pose survives Animator and LateUpdate between samples.");

                var lamp = cobbler.GetComponentsInChildren<Light>(true).Single(l => l.name == "Cobbler desk light");
                Assert.That(lamp.enabled, Is.True);
                var signLight = cobbler.GetComponentsInChildren<Light>(true).Single(l => l.name == "Cobbler sign light");
                Assert.That(signLight.enabled, Is.True);
                Assert.That(signLight.intensity, Is.GreaterThan(0f).And.LessThan(lamp.intensity));
                float night = lamp.intensity;
                SetChurchRemodelTime(720); city.DayNight.ApplyCurrentTime(true);
                lamp.GetComponent<CityCobblerWorkLight>().RefreshImmediate();
                Assert.That(lamp.intensity, Is.GreaterThanOrEqualTo(night * 2f / 3f - .01f));
                VerifyCobblerLampCircuit(cobbler, lamp, signLight);
                var point = city.Map.PointsOfInterest.Single(p => p.Kind == CityMapPointOfInterestKind.Cobbler);
                Assert.That(point.StableId, Is.EqualTo(CityCobblerSitePlan.StableId));
                Assert.That(CityMapArtResources.TryGet(city.Map, out _, out _), Is.True,
                    "Courtyard props preserve the accepted City geography and drawing registration.");
                Assert.That(city.World.WalkableArea.Contains(site.Approach, .32f), Is.True);
                VerifyCobblerYardGeometry(cobbler, site);

                // Use the actual hero capsule and motor across the open doorway, both steps and aisle.
                Time.captureDeltaTime = 1f / 30f;
                SetChurchRemodelTime(720); city.DayNight.ApplyCurrentTime(true);
                cobbler.ApplyAt(720d / GameTimeState.GameMinutesPerRealSecond);
                for (int frame = 0; frame < 20; frame++) yield return null;
                var path = new[] { "ANCHOR_EntryOutside", "ANCHOR_Step1", "ANCHOR_Step2", "ANCHOR_Threshold", "ANCHOR_EntryInside" }
                    .Select(name => CityCobblerAssetProvider.FindPart(cobbler.transform, name).position)
                    .Prepend(site.Yard.PathStart).ToArray();
                city.Player.Motor.Teleport(path[0] + Vector3.up * (PlayerFactory.GroundedRootOffset + .02f));
                for (int direction = 0; direction < 2; direction++)
                for (int index = 1; index < path.Length; index++)
                {
                    Vector3 target = path[direction == 0 ? index : path.Length - 1 - index] +
                        Vector3.up * PlayerFactory.GroundedRootOffset;
                    bool reached = false;
                    for (int step = 0; step < 100 && !reached; step++)
                    {
                        reached = city.Player.Motor.MoveTowardsApproachWaypoint(target, .12f, .04f);
                        if (step % 3 == 0) yield return null;
                    }
                    Assert.That(reached, Is.True, "Player crosses step/threshold " + index + " direction " + direction);
                    // Short treads are narrower than the capsule diameter. Its lower hemisphere
                    // can already rest on the next riser while the centre reaches this waypoint.
                    float highestNeighbour = path.Skip(Math.Max(0, (direction == 0 ? index : path.Length - 1 - index) - 2))
                        .Take(5).Max(p => p.y) + PlayerFactory.GroundedRootOffset;
                    Assert.That(city.Player.Motor.transform.position.y, Is.InRange(target.y - .10f, highestNeighbour + .14f),
                        "Supported capsule at tread " + index + " direction " + direction);
                }
                city.Player.Motor.CancelInteractionPoseMove();
                Time.captureDeltaTime = 1f / 30f;
                var fittingSeat = city.BenchSits.Single(s => s.Plan.Id == CityCobblerYardPlan.SeatId);
                Assert.That(city.World.WalkableArea.Contains(fittingSeat.Plan.EntryRootPosition, .32f), Is.True);
                city.Player.Motor.Teleport(fittingSeat.Plan.EntryRootPosition);
                yield return null;
                Assert.That(fittingSeat.CanInteract(city.Player.Interactor), Is.True);
                fittingSeat.Interact(city.Player.Interactor);
                for (int frame = 0; frame < 240 && !fittingSeat.IsSeated; frame++) yield return null;
                Assert.That(fittingSeat.IsSeated, Is.True,
                    "The reclaimed seat uses the shared sit action. Phase=" + fittingSeat.Controller.Phase);
                fittingSeat.Controller.RefreshActiveClipAlignment();
                var hero = (Player3DCharacterPresentation)city.Player.Visual;
                hero.ReapplyLatePresentationPose();
                Assert.That(Vector3.Distance(hero.Registry.Anchors.Pelvis.position, fittingSeat.Plan.ActionHipPosition),
                    Is.LessThan(.04f), "The hero rests on the actual upholstery.");
                fittingSeat.Interact(city.Player.Interactor);
                for (int frame = 0; frame < 240 && fittingSeat.OwnsActiveInteraction; frame++) yield return null;
                Assert.That(fittingSeat.OwnsActiveInteraction, Is.False);
                Assert.That(Vector3.Distance(city.Player.Motor.transform.position, fittingSeat.Plan.EntryRootPosition), Is.LessThan(.09f));
            }
            finally
            {
                Time.captureDeltaTime = previousDelta;
                if (cobbler) cobbler.AutoAdvance = true;
                if (follow) follow.enabled = followEnabled;
                if (city)
                {
                    city.Weather.enabled = weatherEnabled;
                    city.Player.Motor.SetInputEnabled(motorEnabled);
                    city.Player.Motor.CancelInteractionPoseMove();
                }
            }
        }

        private static void VerifyCobblerYardGeometry(CityCobblerWorker worker, CityCobblerSitePlan site)
        {
            Transform yard = worker.transform.Find("Cobbler fitting yard");
            Assert.That(yard, Is.Not.Null);
            var left = CityCobblerAssetProvider.FindPart(yard, "ANCHOR_YardSeatLeft");
            var right = CityCobblerAssetProvider.FindPart(yard, "ANCHOR_YardSeatRight");
            Assert.That(Vector3.Distance(left.position, right.position), Is.EqualTo(.64f).Within(.01f),
                "Measure imported geometry in placed world metres.");
            Assert.That(Vector3.Distance((left.position + right.position) * .5f, site.Yard.Seat.SeatTopCenter), Is.LessThan(.01f));
            var start = CityCobblerAssetProvider.FindPart(yard, "ANCHOR_FeedStart");
            var end = CityCobblerAssetProvider.FindPart(yard, "ANCHOR_FeedEnd");
            Assert.That(Vector3.Distance(start.position, site.Yard.CableStart), Is.LessThan(.01f));
            Assert.That(Vector3.Distance(end.position, site.Yard.CableEnd), Is.LessThan(.01f));
            var ground = CityHillsideGroundPlan.ForLayout(site.Layout);
            foreach (var filter in yard.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.name != "GravelApproach__Paint") continue;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 p = filter.transform.TransformPoint(vertex);
                    Assert.That(ground.TrySampleTop(new Vector2(p.x, p.z), out float top), Is.True);
                    foreach (var street in CityHillsideStreetPlan.ForLayout(site.Layout).Routes)
                        if (street.TrySampleTop(new Vector2(p.x, p.z), out float paved, out _)) top = Mathf.Max(top, paved);
                    Assert.That(p.y - top, Is.InRange(.006f, .018f), "Gravel follows the actual ground without coplanar faces.");
                }
            }
        }

        private static void VerifyCobblerLampCircuit(CityCobblerWorker worker, Light lamp, Light sign)
        {
            var circuit = lamp.GetComponent<CityCobblerWorkLight>();
            var lens = CityCobblerAssetProvider.FindPart(worker.transform, "CobblerBus__Lens").GetComponent<Renderer>();
            var properties = new MaterialPropertyBlock();
            double closing = CityCobblerWorker.ShiftEndMinute / GameTimeState.GameMinutesPerRealSecond;
            double opening = CityCobblerWorker.ShiftStartMinute / GameTimeState.GameMinutesPerRealSecond;
            worker.ApplyAt(closing + .9d);
            Assert.That(worker.IsUsingLampSwitch, Is.True);
            Assert.That(worker.LampSwitchWeight, Is.GreaterThan(.99f));
            Assert.That(worker.LampSwitchContactError, Is.LessThan(.04f), "The index reaches the actual switch.");
            Assert.That(lamp.enabled, Is.True, "The circuit stays live until the press.");
            worker.ApplyAt(closing + 1.1d);
            Assert.That(worker.WorkLightOn, Is.False);
            Assert.That(circuit.IsPowered, Is.False);
            Assert.That(lamp.enabled, Is.False);
            Assert.That(lamp.intensity, Is.Zero);
            lens.GetPropertyBlock(properties);
            Assert.That(properties.GetColor("_BaseColor").grayscale, Is.LessThan(.3f));
            Assert.That(lamp.GetComponent<CityLightHalo>().GetComponentsInChildren<Renderer>(true).All(r => !r.enabled), Is.True);
            Assert.That(sign.enabled, Is.True);
            worker.ApplyAt(GameTimeState.RealSecondsPerGameDay + opening - 1.1d);
            Assert.That(lamp.enabled, Is.False, "The next morning starts with the switch released and lamp off.");
            worker.ApplyAt(GameTimeState.RealSecondsPerGameDay + opening - .9d);
            Assert.That(worker.IsUsingLampSwitch, Is.True);
            Assert.That(worker.LampSwitchContactError, Is.LessThan(.04f));
            Assert.That(lamp.enabled, Is.True);
            Assert.That(lamp.intensity, Is.GreaterThan(0f));
            worker.ApplyAt(855d / GameTimeState.GameMinutesPerRealSecond);
            Assert.That(lamp.enabled, Is.True, "Lunch inside the bus keeps the light on.");
            circuit.enabled = false;
            Assert.That(lamp.enabled, Is.False);
            circuit.enabled = true;
            Assert.That(lamp.enabled, Is.True);
        }
    }
}
