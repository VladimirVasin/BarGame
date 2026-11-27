using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Timeout(300000)]
        [Explicit("Inspect the upper street motel and walk its stacked exterior galleries.")]
        public IEnumerator RidgeMotelCourtAndGalleries()
        {
            GameSessionState.BeginNewGame("Ridge motel inspection");
            GameSessionState.TryStartGameTimeAt(12 * 60);
            yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            var city = UnityEngine.Object.FindAnyObjectByType<CityGameRoot>();
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!city.IsInitialized && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(city.IsInitialized, Is.True);
            var site = RidgeMotelPlan.ForLayout(city.Layout);
            var motor = city.Player.Motor;
            var camera = city.Camera;
            var follow = camera.GetComponent<PlayerCameraFollow>();
            var capsule = motor.GetComponent<CharacterController>();
            var map = new CityMapCityTeleportGround(city.Layout);
            Assert.That(map.TryClampArrival(site.Arrival, out var start), Is.True);
            Assert.That(Vector3.Distance(start, site.Arrival), Is.LessThan(.06f));
            Assert.That(map.TryResolveStandingPosition(site.Shell.center, out _), Is.False);
            Assert.That(map.TryResolveStandingPosition(site.Office.center, out _), Is.False);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-verifyMotelMap") >= 0)
                Assert.That(CityMapArtResources.TryGet(city.Map, out _, out _), Is.True, "Motel map illustration");
            var collisions = new Dictionary<Collider, bool>();
            var hits = new RaycastHit[64];
            var hidden = new List<Renderer>();
            var route = new List<Vector3>();
            var origin = site.Origin;
            void Waypoint(float x, float y, float z) =>
                route.Add(origin + new Vector3(x, y + PlayerFactory.GroundedRootOffset, z));
            route.Add(start);
            Waypoint(-8, 0, 8); Waypoint(-10.7f, 0, .84f);
            for (int level = 0; level < 2; level++)
            {
                float y = level * RidgeMotelPlan.Storey;
                Waypoint(-15.55f, y, .84f);
                Waypoint(-15.55f, y + RidgeMotelPlan.Storey / 2, 5.54f);
                Waypoint(-13.90f, y + RidgeMotelPlan.Storey / 2, 5.54f);
                Waypoint(-13.90f, y + RidgeMotelPlan.Storey, .84f);
                Waypoint(13.68f, y + RidgeMotelPlan.Storey, .98f);
                Waypoint(-13.90f, y + RidgeMotelPlan.Storey, .84f);
            }
            bool followEnabled = follow.enabled, input = motor.InputEnabled;
            float captureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = .05f;
            try
            {
                foreach (var part in city.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                    if (part.enabled) { hidden.Add(part); part.enabled = false; }
                follow.enabled = false;
                motor.SetInputEnabled(false);
                void Frame(string id, Vector3 eye, Vector3 target)
                {
                    SetWestInfillCamera(city, eye, target);
                    CaptureCurrentCamera(camera, "RidgeMotel", id, 1280, 720);
                }
                Frame("street", origin + new Vector3(-4,1.7f,26), origin + new Vector3(0,4,0));
                Frame("reported-position", new Vector3(55.8f,41.5f,-364.4f), origin + new Vector3(11,2.25f,10));
                Frame("upper-gallery", origin + new Vector3(-12,7.8f,1.2f), origin + new Vector3(10,4.8f,8));
                var balcony = city.Layout.SummitHome;
                Frame("from-home", PlayerHomeBalconyGeometry.GetCityBalconyCenter(balcony.Lot) + Vector3.up * EyeHeight,
                    CityHillsideQuarterPlan.ForLayout(city.Layout).Bridges[1].EastBearingWorld);
                var life = GameObject.Find("Upper Street Motel").GetComponent<RidgeMotelCourtLife>();
                Vector3 patioEye = origin + new Vector3(7.6f, 1.66f, 17.1f);
                Vector3 patioLook = origin + new Vector3(9.4f, 1.07f, 12.1f);
                SetWestInfillCamera(city, patioEye, patioLook);
                life.Sample(true);
                Assert.That(life.At(0).Pose, Is.Null, "Daytime tables");
                Frame("patio-day", patioEye, patioLook);
                void AtMinute(double time)
                {
                    double jump = (time - GameSessionState.GameTimeOfDayMinutes + 1440d) % 1440d;
                    GameSessionState.AdvanceGameTime((float)(jump / GameTimeState.GameMinutesPerRealSecond));
                    city.DayNight.ApplyCurrentTime(true);
                    life.Sample(true);
                }
                AtMinute(12*60 + 22 + 17.5d * GameTimeState.GameMinutesPerRealSecond);
                Assert.That(life.At(1).Body.gameObject.activeInHierarchy, Is.True, "Traveller at the boot");
                Frame("patio-luggage", origin + new Vector3(-.8f,1.65f,9.4f), origin + new Vector3(4.3f,1.05f,7.1f));
                AtMinute(17*60 + 40);
                Assert.That(life.At(0).Body.IsSeated, Is.True);
                Assert.That(life.At(1).Beer.Root.gameObject.activeInHierarchy, Is.True, "Spare beer for the arriving friend");
                AtMinute(17*60 + 46 + 17.4d * GameTimeState.GameMinutesPerRealSecond);
                Assert.That(life.At(0).Cap.enabled, Is.False);
                Assert.That(life.At(1).Cap.enabled, Is.True, "Second cap waits for the opener");
                Frame("patio-opener", origin + new Vector3(11.3f,1.65f,14.22f), life.At(1).Dock);
                SetWestInfillCamera(city, patioEye, patioLook);
                double minutes = (21 * 60 - GameSessionState.GameTimeOfDayMinutes + 1440) % 1440;
                GameSessionState.AdvanceGameTime((float)(minutes / GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                life.Sample(true);
                int party = RidgeMotelCourtLife.PartySize(GameSessionState.GameDayIndex);
                for (int slot = 0; slot < party; slot++)
                {
                    Assert.That(life.At(slot).Body.IsSeated, Is.True);
                    Assert.That(life.At(slot).Beer.Root.gameObject.activeInHierarchy, Is.True);
                    Assert.That(life.At(slot).Cap.enabled, Is.False);
                }
                int exchanges = life.CompletedExchanges;
                bool caughtSip = false;
                for (int step = 0; step < 800 && (!caughtSip || exchanges == life.CompletedExchanges); step++)
                {
                    GameSessionState.AdvanceGameTime(.20f);
                    yield return null;
                    var g = life.At(0);
                    if (!caughtSip && g.Pose.Timeline.Phase == BarPatronDrinkPhase.Sip)
                    {
                        g.Pose.SampleFinalPose();
                        Assert.That(g.Pose.BottleMouthDistance, Is.LessThan(.11f), "Bottle to mouth at sip");
                        Frame("patio-beer", origin + new Vector3(11.4f, 1.49f, 14.42f),
                            g.Character.Head.position - Vector3.up * .12f);
                        caughtSip = true;
                    }
                }
                Assert.That(caughtSip, Is.True, "Bottle drinking at table");
                Assert.That(life.CompletedExchanges, Is.GreaterThan(exchanges), "One exchange with an audible reply");
                Frame("patio-evening", patioEye, patioLook);
                Frame("night", origin + new Vector3(-4,1.7f,26), origin + new Vector3(0,3.2f,0));
#if UNITY_EDITOR
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-exportCityMap") >= 0)
                    Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                        .GetMethod("ExportCurrent", Type.EmptyTypes).Invoke(null, null);
#endif
                Frame("night-middle-walk", origin + new Vector3(-12.4f,4.68f,1.12f), origin + new Vector3(12f,4.27f,.79f));
                Frame("night-upper-walk", origin + new Vector3(13.7f,7.8f,1.08f), origin + new Vector3(-9f,7.12f,.83f));
                var placed = GameObject.Find("Upper Street Motel");
                Assert.That(placed, Is.Not.Null);
                Transform motel = placed.transform;
                var faults = placed.GetComponentsInChildren<RidgeMotelGalleryLamp>();
                Assert.That(faults.Length, Is.EqualTo(1));
                var fault = faults[0];
                var source = fault.GetComponent<Light>();
                Assert.That(source.name, Is.EqualTo("Gallery11 motel lamp"));
                var lens = Array.Find(placed.GetComponentsInChildren<Renderer>(), r => r.name == "LensGallery11");
                var block = new MaterialPropertyBlock();
                fault.ApplyAt(0);
                float steady = source.intensity;
                lens.GetPropertyBlock(block);
                float steadyLens = block.GetColor("_BaseColor").r;
                var neighbour = motel.Find("Gallery10 motel lamp").GetComponent<Light>();
                float neighbourStrength = neighbour.intensity;
                Vector3 courtEye = origin + new Vector3(-2.5f, 1.68f, 11.5f);
                Vector3 galleryTarget = source.transform.position + Vector3.down * .68f;
                Frame("contact-steady", courtEye, galleryTarget);
                fault.ApplyAt(2.74d);
                float dim = source.intensity / steady;
                Assert.That(dim, Is.InRange(.35f, .6f));
                lens.GetPropertyBlock(block);
                Assert.That(block.GetColor("_BaseColor").r / steadyLens, Is.EqualTo(dim).Within(.001f));
                Assert.That(neighbour.intensity, Is.EqualTo(neighbourStrength));
                Frame("contact-dip", courtEye, galleryTarget);
                fault.ApplyAt(3.1d);
                Assert.That(source.intensity, Is.EqualTo(steady).Within(.001f));
                for (int level = 1; level <= 2; level++)
                {
                    var lamps = Array.FindAll(placed.GetComponentsInChildren<Light>(),
                        lamp => lamp.name.StartsWith("Gallery" + level, StringComparison.Ordinal));
                    Assert.That(lamps.Length, Is.EqualTo(3));
                    for (float x = -12.5f; x <= 14.5f; x += .5f)
                    {
                        Vector3 sample = origin + new Vector3(x, level * RidgeMotelPlan.Storey + .18f, .90f);
                        Assert.That(Array.Exists(lamps, lamp =>
                        {
                            Vector3 ray = sample - lamp.transform.position;
                            return lamp.enabled && lamp.intensity > 0 && ray.magnitude < lamp.range &&
                                Vector3.Angle(ray, lamp.transform.forward) < lamp.spotAngle * .5f &&
                                !Physics.Linecast(lamp.transform.position, sample, Physics.DefaultRaycastLayers,
                                    QueryTriggerInteraction.Ignore);
                        }), Is.True, "Unlit gallery floor at " + sample);
                    }
                }
                motor.Teleport(start);
                for (int i = 0; i < 3; i++) yield return null;
                yield return WalkMotelRoute();
                route.Reverse();
                yield return WalkMotelRoute();
                GameSessionState.AdvanceGameTime((float)((23 * 60 - GameSessionState.GameTimeOfDayMinutes) /
                    GameTimeState.GameMinutesPerRealSecond));
                life.Sample(true);
                for (int slot = 0; slot < party; slot++)
                {
                    Assert.That(life.At(slot).Body.gameObject.activeSelf, Is.False);
                    Assert.That(life.At(slot).Beer.Root.gameObject.activeSelf, Is.False);
                }

                AtMinute(21*60);
                Assert.That(RidgeMotelCourtLife.PartySize(GameSessionState.GameDayIndex), Is.EqualTo(2));
                Assert.That(life.At(2).Body.gameObject.activeSelf, Is.False, "Quiet evening");
                AtMinute(22*60);
                AtMinute(21*60);
                Assert.That(life.At(3).Table, Is.EqualTo(0), "Later evening around one table");
                Assert.That(life.At(2).Body.IsSeated && life.At(3).Body.IsSeated, Is.True);

                IEnumerator WalkMotelRoute()
                {
                    for (int next = 1; next < route.Count; next++)
                    {
                        Vector3 target = route[next];
                        bool arrived = false;
                        // Allow the full gallery traverse plus a standing 180-degree turn.
                        int stepBudget = 60 + Mathf.CeilToInt(Vector3.Distance(motor.transform.position, target) / .09f);
                        for (int step = 0; step < stepBudget && !arrived; step++)
                        {
                            IsolateHomeBarWalkers(city, capsule, collisions);
                            arrived = motor.MoveTowardsApproachWaypoint(target, .075f, .05f);
                            // Let normal player gravity run for every downhill step.
                            if (target.y < motor.transform.position.y - .12f || step % 3 == 0 || arrived) yield return null;
                            Vector3 position = motor.transform.position;
                            Assert.That(city.World.WalkableArea.Contains(position, capsule.radius), Is.True);
                            int count = Physics.RaycastNonAlloc(position + Vector3.up * .35f, Vector3.down,
                                hits, .69f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                            Assert.That(count, Is.LessThan(hits.Length));
                            bool supported = false;
                            for (int i = 0; i < count; i++)
                                if (hits[i].normal.y > .70f && hits[i].transform.IsChildOf(motel))
                                    supported = true;
                            Assert.That(supported, Is.True, "Motel floor support at " + position.ToString("F3"));
                        }
                        if (!arrived)
                        {
                            Vector3 feet = motor.transform.position;
                            var direction = Vector3.ProjectOnPlane(target - feet, Vector3.up).normalized;
                            Vector3 probe = feet + direction * .15f;
                            var contacts = Physics.CapsuleCastAll(feet + Vector3.up * .35f,
                                feet + Vector3.up * 1.36f, .31f, direction, 1f, Physics.DefaultRaycastLayers,
                                QueryTriggerInteraction.Ignore);
                            Assert.Fail($"Motel waypoint {next} from {route[next - 1]} stopped at {feet} toward {target}. " +
                                $"Walk mask allows step: {motor.IsInteractionApproachSupported(probe)}. Contacts: " +
                                string.Join(", ", Array.ConvertAll(contacts, h => h.collider.name + " at " + h.point)));
                        }
                        Assert.That(motor.transform.position.y, Is.EqualTo(target.y).Within(.16f), "Wrong motel floor at " + target);
                    }
                    motor.CancelInteractionPoseMove();
                }
            }
            finally
            {
                foreach (var pair in collisions)
                    if (pair.Key) Physics.IgnoreCollision(capsule, pair.Key, pair.Value);
                foreach (var part in hidden) if (part != null) part.enabled = true;
                follow.enabled = followEnabled;
                motor.SetInputEnabled(input);
                Time.captureDeltaTime = captureDelta;
                GameSessionState.BeginNewGame("Ridge motel inspection complete");
            }
        }
    }
}
