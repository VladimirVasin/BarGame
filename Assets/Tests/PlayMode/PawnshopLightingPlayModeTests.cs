using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class PawnshopMvpPlayModeTests
    {
        [UnityTest]
        public IEnumerator CityPawnshopLightingAndBenchPockets_UniformAndConnected()
        {
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            bool previousFocus = GameInput.MovementFocused;
            float previousStep = Time.captureDeltaTime;
            float previousScale = Time.timeScale;
            object cursor = new object();
            PlayerCameraFollow follow = null;
            bool capture = Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_PAWNSHOP") == "1" &&
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            try
            {
                GameInput.HandleApplicationFocus(true);
                GameCursor.Capture(cursor, CursorLockMode.Locked, false, 0);
                Time.captureDeltaTime = Step;
                Time.timeScale = 1f;
                GameSessionState.BeginNewGame();
                yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                CityGameRoot city = null;
                yield return WaitFor(() =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized && !SceneTransitionService.IsTransitioning;
                }, "City did not initialize for the lighting reproduction.");
                Assert.That(CityPawnshopPlan.TryCreate(city.Layout, out var site), Is.True);
                Transform exterior = city.World.Root.transform.Find(CityPawnshopWorld.RootName);
                follow = city.Camera.GetComponent<PlayerCameraFollow>();
                city.Player.Motor.Teleport(site.ReturnWorld);
                Physics.SyncTransforms();
                float[] night = null;
                Vector3 eye = site.Origin + site.DoorOutward * 10.5f + Vector3.up * 2.2f;
                follow.SetFixedPose(eye, Quaternion.LookRotation(site.Origin + Vector3.up * 2.05f - eye), 60f);
                yield return CaptureExteriorGameView(city, 0, capture ? "lighting-01-sign-night" : null, () =>
                {
                    night = AssertExteriorLighting(city, exterior);
                    var signs = exterior.GetComponentsInChildren<Transform>().Where(part =>
                        part.name == "IndustrialTaskLight_PawnshopSignLeft" ||
                        part.name == "IndustrialTaskLight_PawnshopSignRight").ToArray();
                    var lights = signs.Select(source => city.Night.Atmosphere.StreetLightPool.Single(light =>
                        light.enabled && Vector3.Distance(light.transform.position, source.position) < .01f)).ToArray();
                    // Inverse-square distance, incidence and the actual cone are
                    // evaluated across every glyph, rather than asserting only
                    // that two Unity Light components happen to exist.
                    float[] field = Enumerable.Range(0, 29).Select(index =>
                    {
                        Vector3 point = exterior.TransformPoint(new Vector3(-2.1f + index * .15f, 3.15f, -4.64f));
                        return lights.Sum(light => SignIlluminance(light, point, site.DoorOutward));
                    }).ToArray();
                    Assert.That(field.Min(), Is.GreaterThan(0f));
                    Assert.That(field.Max() / field.Min(), Is.LessThan(1.8f),
                        "The whole seven-letter sign must receive overlapping light without a central peak.");
                });
                yield return CaptureExteriorGameView(city, 720, capture ? "lighting-02-sign-day" : null, () =>
                {
                    float[] day = AssertExteriorLighting(city, exterior);
                    for (int i = 0; i < day.Length; i++)
                        Assert.That(day[i], Is.GreaterThanOrEqualTo(night[i] * (2f / 3f) - .01f));
                });

                var pockets = CityHillsideBenchPocketPlan.ForLayout(city.Layout);
                Assert.That(pockets.Pockets, Is.Not.Empty);
                Debug.Log("Bench approach review: bake=" + pockets.BuildMilliseconds.ToString("F1") +
                    "ms, expanded=" + pockets.SearchExpandedNodes + "; " + string.Join("; ", pockets.Pockets.Select(pocket =>
                    pocket.Placement.Id + " " + pocket.Placement.Position.ToString("F2") +
                    " path=" + pocket.ApproachPath.Count)));
                Assert.That(pockets.UnconnectedBenchIds, Is.Empty,
                    "Every maintained hillside bench needs an approach from a public footway: " +
                    string.Join(", ", pockets.UnconnectedBenchIds));
                Transform pocketWorld = city.World.Root.GetComponentsInChildren<Transform>().Single(part => part.name == "Ridge bench pockets");
                foreach (var pocket in pockets.Pockets)
                {
                    Assert.That(pocket.ApproachPath.Count, Is.GreaterThan(1), pocket.Placement.Id);
                    Assert.That(pocket.SurfacePolygons, Is.Not.Empty, pocket.Placement.Id);
                    Assert.That(Vector3.Distance(pocket.LampFoot, pocket.Placement.Position), Is.InRange(1.5f, 2.5f));
                    Assert.That(pocketWorld.Find("Bench lamp " + pocket.Placement.Id), Is.Not.Null);
                }
                var terrace = CityPawnshopBenchTerracePlan.ForLayout(city.Layout);
                Assert.That(terrace.IsEnabled, Is.True);
                Assert.That(terrace.Width, Is.EqualTo(10.4f).Within(.001f));
                Assert.That(terrace.StairCount, Is.EqualTo(6));
                Assert.That(terrace.Rise, Is.InRange(.15f, .18f));
                Assert.That(terrace.Tread, Is.GreaterThanOrEqualTo(.28f));
                var leftLamp = pockets.Pockets.Single(pocket => pocket.Placement.Id == CityPawnshopBenchTerracePlan.LeftBenchId);
                var rightLamp = pockets.Pockets.Single(pocket => pocket.Placement.Id == CityPawnshopBenchTerracePlan.RightBenchId);
                Assert.That(leftLamp.LampFoot.x + rightLamp.LampFoot.x,
                    Is.EqualTo(terrace.Flight.Start.x * 2f).Within(.002f));
                Assert.That(leftLamp.LampFoot.z, Is.EqualTo(rightLamp.LampFoot.z).Within(.002f));
                Assert.That(leftLamp.LampForward.x, Is.EqualTo(-rightLamp.LampForward.x).Within(.002f));
                Assert.That(leftLamp.LampForward.z, Is.EqualTo(rightLamp.LampForward.z).Within(.002f));
                Transform stair = city.World.Root.GetComponentsInChildren<Transform>().Single(part =>
                    part.name == "Broad bench access stair");
                Assert.That(BoundsOf(stair).size.x, Is.EqualTo(terrace.Width).Within(.035f),
                    "The broad imported flight retains authored world metres.");
                Transform statue = city.World.Root.GetComponentsInChildren<Transform>().Single(part =>
                    part.name == "Concrete pigeon between benches");
                Bounds statueBounds = BoundsOf(statue);
                Assert.That(statueBounds.size.y, Is.InRange(1.9f, 2.1f));
                Assert.That(statueBounds.min.y, Is.EqualTo(terrace.StatuePosition.y).Within(.02f));
                Assert.That(statueBounds.size.x, Is.LessThanOrEqualTo(1.1f));
                Assert.That(statueBounds.size.z, Is.LessThanOrEqualTo(1.1f));
                Assert.That(city.World.WalkableArea.Contains(terrace.StatuePosition + Vector3.up * PlayerFactory.GroundedRootOffset,
                    .32f), Is.False, "The statue's solid plinth is excluded from map and pedestrian arrivals.");
                foreach (string id in new[] { CityPawnshopBenchTerracePlan.LeftBenchId, CityPawnshopBenchTerracePlan.RightBenchId })
                    Assert.That(Vector3.Angle(pockets.Pockets.Single(pocket => pocket.Placement.Id == id).Placement.Forward,
                        Vector3.forward), Is.LessThan(.01f), "Both benches face straight towards the broad stair.");
                Assert.That(terrace.UpperLanding.Bounds.yMin, Is.LessThanOrEqualTo(-309f));
                for (int index = 0; index < terrace.FlowerbedPositions.Count; index++)
                {
                    Transform flowers = city.World.Root.GetComponentsInChildren<Transform>().Single(part =>
                        part.name == "Bench flowerbed " + index);
                    Bounds bounds = BoundsOf(flowers);
                    Assert.That(bounds.min.y, Is.EqualTo(terrace.UpperLanding.Center.y).Within(.02f));
                    Assert.That(bounds.size.y, Is.InRange(.5f, .7f));
                    Assert.That(city.World.WalkableArea.Contains(terrace.FlowerbedPositions[index] +
                        Vector3.up * PlayerFactory.GroundedRootOffset, .32f), Is.False);
                }
                foreach (float lane in new[] { -.3f, 0f, .3f })
                {
                    Vector3 side = Vector3.Cross(Vector3.up, terrace.Flight.Direction) * (terrace.Width * lane);
                    var route = new List<Vector3>();
                    for (int segment = 1; segment < terrace.RouteWorld.Count; segment++)
                    {
                        Vector3 a = terrace.RouteWorld[segment - 1] + side;
                        Vector3 b = terrace.RouteWorld[segment] + side;
                        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / .3f));
                        for (int step = segment == 1 ? 0 : 1; step <= steps; step++)
                        {
                            Vector3 point = Vector3.Lerp(a, b, (float)step / steps);
                            Assert.That(terrace.TrySampleTop(new Vector2(point.x, point.z), out float top), Is.True);
                            point.y = top;
                            Assert.That(city.World.WalkableArea.Contains(point + Vector3.up * PlayerFactory.GroundedRootOffset, .32f),
                                Is.True, "The broad stair needs full capsule clearance across its width: " + point);
                            route.Add(point + Vector3.up * PlayerFactory.GroundedRootOffset);
                        }
                    }
                    city.Player.Motor.Teleport(route[0]);
                    Physics.SyncTransforms();
                    for (int frame = 0; frame < 5; frame++) yield return null;
                    foreach (Vector3 point in route) yield return WalkTo(city.Player, point);
                    for (int point = route.Count - 1; point >= 0; point--) yield return WalkTo(city.Player, route[point]);
                }
                eye = terrace.Flight.Start - terrace.Flight.Direction * 6f + Vector3.up * 2.8f;
                follow.SetFixedPose(eye, Quaternion.LookRotation(terrace.UpperLanding.Center + Vector3.up * .8f - eye), 68f);
                yield return CaptureExteriorGameView(city, 0, capture ? "lighting-07-wide-stair-night" : null);
                yield return CaptureExteriorGameView(city, 720, capture ? "lighting-08-wide-stair-day" : null);
                Vector3 reported = new Vector3(-83.4f, 30.7f, -303.3f);
                var nearest = pockets.Pockets.OrderBy(pocket => (pocket.Placement.Position - reported).sqrMagnitude).First();
                Assert.That(Vector2.Distance(new Vector2(nearest.Placement.Position.x, nearest.Placement.Position.z),
                    new Vector2(reported.x, reported.z)), Is.LessThan(6f), "Review the bench reported in the player's frame.");
                Debug.Log("Lighting review bench: " + nearest.Placement.Id + " " + nearest.Placement.Position.ToString("F2"));
                city.Player.Motor.Teleport(nearest.ApproachPath[0] + Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                for (int i = 0; i < 5; i++) yield return null;
                foreach (Vector3 point in nearest.ApproachPath)
                    yield return WalkTo(city.Player, point + Vector3.up * PlayerFactory.GroundedRootOffset);
                eye = nearest.Placement.Position + nearest.Placement.Forward * 5f +
                    Vector3.Cross(Vector3.up, nearest.Placement.Forward) * 3.5f + Vector3.up * 2.2f;
                follow.SetFixedPose(eye, Quaternion.LookRotation(nearest.Placement.Position + Vector3.up * .8f - eye), 62f);
                yield return CaptureExteriorGameView(city, 0, capture ? "lighting-03-bench-night" : null, () =>
                {
                    foreach (var pocket in pockets.Pockets.Where(pocket => pocket.Placement.Id == nearest.Placement.Id ||
                        pocket.Placement.Id == "ridge-West-ParkBench-11"))
                    {
                        Light light = city.Night.Atmosphere.StreetLightPool.SingleOrDefault(candidate => candidate.enabled &&
                            Vector3.Distance(candidate.transform.position, pocket.LampSource) < .01f);
                        Assert.That(light, Is.Not.Null, "Both benches in the frame need actual pooled light: " + pocket.Placement.Id);
                        Assert.That(SignIlluminance(light, pocket.Placement.Position + Vector3.up * .71f, Vector3.up),
                            Is.GreaterThan(2f), "The bench itself must stay within the working beam: " + pocket.Placement.Id);
                    }
                });
                yield return CaptureExteriorGameView(city, 720, capture ? "lighting-04-bench-day" : null);
                for (int i = nearest.ApproachPath.Count - 1; i >= 0; i--)
                    yield return WalkTo(city.Player, nearest.ApproachPath[i] + Vector3.up * PlayerFactory.GroundedRootOffset);
                var second = pockets.Pockets.Single(pocket => pocket.Placement.Id == CityPawnshopBenchTerracePlan.LeftBenchId);
                city.Player.Motor.Teleport(second.ApproachPath[0] + Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                for (int frame = 0; frame < 5; frame++) yield return null;
                foreach (Vector3 point in second.ApproachPath)
                    yield return WalkTo(city.Player, point + Vector3.up * PlayerFactory.GroundedRootOffset);
                eye = second.Placement.Position + second.Placement.Forward * 4.5f -
                    Vector3.Cross(Vector3.up, second.Placement.Forward) * 3f + Vector3.up * 2.1f;
                follow.SetFixedPose(eye, Quaternion.LookRotation(second.Placement.Position + Vector3.up * .8f - eye), 65f);
                yield return CaptureExteriorGameView(city, 0, capture ? "lighting-09-second-bench-night" : null);
                for (int point = second.ApproachPath.Count - 1; point >= 0; point--)
                    yield return WalkTo(city.Player, second.ApproachPath[point] + Vector3.up * PlayerFactory.GroundedRootOffset);

                var entrance = exterior.GetComponentInChildren<PawnshopEntrance>();
                var entryAction = entrance.GetComponent<PlayerDoorActionTarget>();
                Assert.That(city.World.Root.GetComponentsInChildren<Renderer>().Any(renderer =>
                    renderer.name == "Pawnshop forecourt paving"), Is.True);
                Assert.That(CityPawnshopPlan.ApproachWidth, Is.EqualTo(2.4f));
                city.Player.Motor.Teleport(site.ApproachPath[0] + Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                for (int i = 0; i < 5; i++) yield return null;
                eye = site.Origin + site.DoorOutward * 11f + site.Rotation * Vector3.right * 4f + Vector3.up * 2.7f;
                follow.SetFixedPose(eye, Quaternion.LookRotation(site.DoorWorld + Vector3.up * .35f - eye), 65f);
                yield return CaptureExteriorGameView(city, 0, capture ? "lighting-05-path-to-door" : null);
                foreach (Vector3 point in site.ApproachPath)
                    yield return WalkTo(city.Player, point + Vector3.up * PlayerFactory.GroundedRootOffset);
                yield return WalkTo(city.Player, entryAction.Plan.EntryRootPosition);
                yield return AimAt(city.Player, follow, entrance.InteractionPosition);
                Assert.That(city.Player.Interactor.ActiveInteractable, Is.SameAs(entrance));
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                PawnshopInteriorRoot interior = null;
                yield return WaitFor(() =>
                {
                    interior = Object.FindAnyObjectByType<PawnshopInteriorRoot>();
                    return interior != null && interior.IsInitialized && !SceneTransitionService.IsTransitioning;
                }, "The lit pawnshop interior did not initialize.");
                Assert.That(interior.LightDiffusers.Count, Is.EqualTo(3));
                Assert.That(interior.Lights.All(light => light.enabled && light.intensity >= 18f), Is.True);
                var block = new MaterialPropertyBlock();
                foreach (Renderer lens in interior.LightDiffusers)
                {
                    lens.GetPropertyBlock(block);
                    Assert.That(block.GetColor("_BaseColor").maxColorComponent, Is.GreaterThan(1f), lens.name);
                    Assert.That(lens.sharedMaterial, Is.SameAs(CityNightResources.EmissiveMaterial));
                    Assert.That(lens.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
                }
                follow = interior.CameraFollow;
                follow.SetFixedPose(new Vector3(0f, 1.62f, -3.05f),
                    Quaternion.LookRotation(new Vector3(0f, 1.65f, 1.4f) - new Vector3(0f, 1.62f, -3.05f)), 72f);
                yield return null;
                if (capture) yield return CaptureGameView("lighting-06-lit-interior");
            }
            finally
            {
                if (follow != null) follow.ClearFixedPose();
                input.TearDown();
                GameCursor.Release(cursor);
                GameInput.HandleApplicationFocus(previousFocus);
                Time.captureDeltaTime = previousStep;
                Time.timeScale = previousScale;
            }
        }

        private static float SignIlluminance(Light light, Vector3 point, Vector3 normal)
        {
            Vector3 ray = point - light.transform.position;
            float distanceSquared = ray.sqrMagnitude;
            float cosine = Vector3.Dot(light.transform.forward, ray.normalized);
            float outer = Mathf.Cos(light.spotAngle * .5f * Mathf.Deg2Rad);
            float inner = Mathf.Cos(light.innerSpotAngle * .5f * Mathf.Deg2Rad);
            float cone = Mathf.Clamp01((cosine - outer) / (inner - outer));
            float fade = Mathf.Clamp01(1f - Mathf.Pow(distanceSquared / (light.range * light.range), 2f));
            return light.intensity * Mathf.Max(0f, Vector3.Dot(normal, -ray.normalized)) *
                cone * cone * fade * fade / Mathf.Max(.001f, distanceSquared);
        }
    }
}
