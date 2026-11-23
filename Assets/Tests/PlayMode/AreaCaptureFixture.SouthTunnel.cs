using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Explicit("South portal, drain and first bend in daylight and rain, then walk across the refusal plane.")]
        public IEnumerator CitySouthTunnel()
        {
            CityGameRoot city = null;
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeAt(12 * 60);
            float previousDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 30;
            try
            {
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city && city.IsInitialized && !CompositionDriver.IsComposing ? city : null;
                }, () =>
                {
                    var tunnel = city.World.MountainBoundaryPlan.Tunnel;
                    var drain = city.World.FringeYardPlan.Yards.SelectMany(y => y.Parts)
                        .First(p => p.Kind == CityFringeYardPartKind.DrainCover && p.StableId.EndsWith("-02"));
                    Transform portal = GameObject.Find("Open South Tunnel").transform.Find("Mountain Tunnel Portal");
                    var portalMesh = portal.GetComponent<MeshFilter>().sharedMesh;
                    Assert.That(portalMesh.name, Is.EqualTo("Portal"));
                    Assert.That(portal.GetComponent<MeshCollider>().sharedMesh, Is.SameAs(portalMesh));
                    Assert.That(portal.GetComponent<Renderer>().sharedMaterial.GetTexture("_BaseMap").name,
                        Is.EqualTo("SouthTunnelAlbedo"));
                    Assert.That(GameObject.Find("Tunnel Cross Drain").GetComponent<Renderer>().bounds.size.x,
                        Is.EqualTo(5.6f).Within(.05f), "The five castings meet across the road.");
                    var grass = city.World.Root.GetComponentInChildren<CityGrassField>(true);
                    Assert.That(grass.Surface.TrySample(new Vector2(drain.Center.x, drain.Center.z),
                        out _, out _, out _), Is.False, "The cross drain cannot support a tuft.");
                    var rut = city.World.FringeYardPlan.Yards.SelectMany(y => y.Parts)
                        .First(p => p.StableId.Contains("-tunnel-wheel-rut-") && p.Size.z > .6f);
                    Vector3 shoulder = rut.Rotation * Vector3.right * (rut.Size.x * .65f);
                    float verge = (GroundTop(rut.Center - shoulder) + GroundTop(rut.Center + shoulder)) * .5f;
                    Assert.That(verge - GroundTop(rut.Center), Is.InRange(.014f, .04f),
                        "Wheel pressure lowers the final terrain collider between both shoulders.");
                    Vector3 mouth = tunnel.PortalGroundCenter;
                    Vector3 axis = tunnel.Axis;
                    Vector3 right = Vector3.Cross(Vector3.up, axis);
                    city.Weather.enabled = false;
                    city.Camera.GetComponent<PlayerCameraFollow>().enabled = false;
                    city.Player.Motor.SetInputEnabled(false);
                    var shots = new List<Shot>();
                    foreach (bool rain in new[] { false, true })
                    {
                        Add("street", mouth - axis * 17f + right * 2.3f + Vector3.up * 1.8f,
                            mouth + Vector3.up * 2.3f, 55);
                        Add("masonry", mouth - axis * 7.6f - right * 1.35f + Vector3.up * 1.62f,
                            mouth + Vector3.up * 3.15f, 62);
                        Add("drain", drain.Center - axis * 2.4f - right * 1.8f + Vector3.up * 1.5f,
                            drain.Center + axis * 1.6f, 60);
                        Add("inside", mouth + axis * 8.5f + right * 1.8f + Vector3.up * 1.58f,
                            mouth + Vector3.up * 2.05f - right * 1.4f, 66);
                        void Add(string view, Vector3 eye, Vector3 look, float fov)
                        {
                            string name = "south-portal-" + (rain ? "rain-" : "day-") + view;
                            Shot pose = Shot.At(name, eye, look, fov);
                            int frames = 0;
                            shots.Add(Shot.At(name, eye, look, fov, readyWhen: () =>
                            {
                                if (frames++ == 0)
                                {
                                    if (rain && GameSessionState.GameTimeOfDayMinutes < 21 * 60)
                                        GameSessionState.AdvanceGameTime((float)((21 * 60 - GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond));
                                    city.DayNight.ApplyCurrentTime(true);
                                    city.Rain.SetIntensity(rain ? 1f : 0f);
                                    CityWaterResources.SetRainIntensity(rain ? 1f : 0f);
                                    CityWetSurfaceRegistry.SetImmediate(rain ? 1f : 0f);
                                    MoveCityLightingObserver(city, pose);
                                }
                                return frames >= (rain ? 35 : 8);
                            }));
                        }
                    }
                    return shots.ToArray();

                    float GroundTop(Vector3 point)
                    {
                        var hits = Physics.RaycastAll(point + Vector3.up, Vector3.down, 2f);
                        return hits.Where(h => h.collider.name.StartsWith("Ground sector "))
                            .OrderBy(h => h.distance).First().point.y;
                    }
                });
                var travel = Object.FindAnyObjectByType<CityTunnelTravelController>();
                var plan = travel.Plan;
                var motor = city.Player.Motor;
                var yard = city.World.FringeYardPlan.Yards.Single(y => y.Kind == CityFringeYardKind.SouthTunnelForecourt);
                Vector3 street = yard.Access.Center - plan.Axis * 1.2f;
                motor.Teleport(street + Vector3.up * PlayerFactory.GroundedRootOffset);
                yield return null;
                Vector3 landing = plan.PortalGroundCenter;
                landing.y = plan.FloorSurfaceY;
                yield return WalkChurchGardenLeg(motor, landing, "cross the graded approach and drain");
                motor.SetInputEnabled(true);
                city.Player.Interactor.SetInputEnabled(true);
                Physics.SyncTransforms();
                yield return null;
                Vector3 beyond = plan.DecisionPlaneCenter + plan.Axis + Vector3.up * PlayerFactory.GroundedRootOffset;
                for (int i = 0; i < 180 && !travel.IsTurningBack; i++)
                {
                    motor.MoveTowardsApproachWaypoint(beyond, .08f, .08f);
                    yield return null;
                }
                Assert.That(travel.IsTurningBack, Is.True, "The ordinary motor crosses the decision plane.");
                Assert.That(city.Player.Interactor.InputEnabled, Is.False);
                for (int i = 0; i < 180 && travel.IsTurningBack; i++) yield return null;
                Assert.That(Vector3.Distance(motor.transform.position, plan.ReturnRootPosition), Is.LessThan(.18f));
                Assert.That(Quaternion.Angle(motor.transform.rotation, plan.ReturnRootRotation), Is.LessThan(5f));
                Assert.That(motor.InputEnabled && city.Player.Interactor.InputEnabled, Is.True);
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            }
            finally
            {
                if (city)
                {
                    city.Weather.enabled = true;
                    city.Camera.GetComponent<PlayerCameraFollow>().enabled = true;
                    city.Player.Motor.SetInputEnabled(true);
                }
                Time.captureDeltaTime = previousDelta;
                GameSessionState.BeginNewGame();
            }
        }
    }
}
