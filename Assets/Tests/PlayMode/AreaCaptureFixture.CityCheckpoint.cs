using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Explicit("Checkpoint construction, local finishes and both guard approaches.")]
        public IEnumerator CityCheckpointFinishes()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeAt(12 * 60);
            CityGameRoot city = null;
            CityEastExitPlan site = null;
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
                    site = CityEastExitPlanner.Create(city.Layout);
                    city.Camera.GetComponent<PlayerCameraFollow>().enabled = false;
                    city.Player.Motor.SetInputEnabled(false);
                    city.Weather.enabled = false;
                    var shots = new List<Shot>();
                    foreach (bool night in new[] {false, true})
                    {
                        Add("road", site.CheckpointPosition + new Vector3(-9.8f, 1.7f, 2.7f),
                            site.CheckpointPosition + new Vector3(-.6f, 1.25f, -3.6f), 61);
                        Add("window", site.BoothPosition + new Vector3(-4.65f, 1.6f, -3.4f),
                            site.BoothPosition + new Vector3(0, 1.46f, -.8f), 62);
                        Add("barrier", site.CheckpointPosition + new Vector3(-2.35f, 1.55f, -3.85f),
                            site.CheckpointPosition + new Vector3(.5f, 1.05f, -.75f), 65);
                        Add("approach", site.CheckpointPosition + new Vector3(-16.5f, 2.05f, .45f),
                            site.CheckpointPosition + new Vector3(-3.8f, .65f, 0f), 64);
                        Add("door", site.BoothPosition + new Vector3(.48f, 1.55f, -5.98f),
                            site.BoothPosition + new Vector3(.48f, 1.25f, -1.68f), 44);
                        void Add(string view, Vector3 eye, Vector3 target, float fov)
                        {
                            string name = "checkpoint-kit-" + (night ? "rain-" : "day-") + view;
                            Shot pose = Shot.At(name, eye, target, fov);
                            int frames = 0;
                            shots.Add(Shot.At(name, eye, target, fov, readyWhen: () =>
                            {
                                if (frames++ == 0)
                                {
                                    if (night && GameSessionState.GameTimeOfDayMinutes < 21 * 60)
                                        GameSessionState.AdvanceGameTime((float)((21 * 60 - GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond));
                                    city.DayNight.ApplyCurrentTime(true);
                                    city.Rain.SetIntensity(night ? 1f : 0f);
                                    CityWaterResources.SetRainIntensity(night ? 1f : 0f);
                                    CityWetSurfaceRegistry.SetImmediate(night ? 1f : 0f);
                                    MoveCityLightingObserver(city, pose);
                                }
                                return frames >= (night ? 36 : 10);
                            }));
                        }
                    }
                    return shots.ToArray();
                });
                CheckCheckpointMaterials(city);
                float previousAspect = city.Camera.aspect;
                try
                {
                    city.Camera.aspect = 640f / 360f;
                    CheckBoothDoorLight(city, site);
                    CheckCheckpointRoadLight(city, site);
                }
                finally { city.Camera.aspect = previousAspect; }
                yield return WalkCheckpoint(city, site);
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            }
            finally
            {
                if (city)
                {
                    city.Weather.enabled = true;
                    city.Player.Motor.SetInputEnabled(true);
                    city.Camera.GetComponent<PlayerCameraFollow>().enabled = true;
                }
                Time.captureDeltaTime = previousDelta;
                GameSessionState.BeginNewGame();
            }
        }

        private static void CheckCheckpointMaterials(CityGameRoot city)
        {
            var post = city.World.EastExit;
            var renderers = post.Root.GetComponentsInChildren<MeshRenderer>(true);
            var finish = Resources.Load<Material>("City/Surfaces/EastCheckpoint");
            Assert.That(finish.GetTexture("_BaseMap").name, Is.EqualTo("EastCheckpointAlbedo"));
            Assert.That(finish.GetTexture("_BumpMap").name, Is.EqualTo("EastCheckpointNormal"));
            Assert.That(finish.GetTexture("_MetallicGlossMap").name, Is.EqualTo("EastCheckpointMask"));
            foreach (string part in new[] { "EEX_Booth_Zinc", "EEX_Barrier_Paint", "EEX_Shelter_Roof",
                "EEX_Gate_Bright", "EEX_Booth_Glass" })
                Assert.That(renderers.First(r => r.name == part).sharedMaterial, Is.SameAs(finish), part);
            Assert.That(city.Rain.LocalShelters, Does.Contain(post.RainShelter));
            Assert.That(post.RainShelter.isTrigger, Is.True);
            Vector3 center = post.RainShelter.bounds.center;
            CheckGround(center, CityWetSurfaceKind.ShelteredGround, 0f);
            CheckGround(center + Vector3.left * 2.15f, CityWetSurfaceKind.Ground, 1f);

            void CheckGround(Vector3 origin, CityWetSurfaceKind expected, float wetness)
            {
                RaycastHit hit = Physics.RaycastAll(origin, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore)
                    .Where(h => h.collider.name.StartsWith("Ground sector ")).OrderBy(h => h.distance).First();
                var mesh = ((MeshCollider)hit.collider).sharedMesh;
                int triangle = hit.triangleIndex, slot = 0;
                while (triangle >= mesh.GetIndexCount(slot) / 3)
                    triangle -= (int)mesh.GetIndexCount(slot++) / 3;
                var renderer = hit.collider.GetComponent<Renderer>();
                Assert.That(CityWetSurfaceRegistry.TryReadRegistration(renderer, slot, out var kind, out _), Is.True,
                    "Missing ground exposure at " + hit.point);
                Assert.That(kind, Is.EqualTo(expected), hit.point.ToString());
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, slot);
                Assert.That(block.GetFloat(GroundSurfaceAppearance.WetnessId), Is.EqualTo(wetness).Within(.01f));
            }
        }

        private static void CheckBoothDoorLight(CityGameRoot city, CityEastExitPlan site)
        {
            var camera = city.Camera;
            Vector3 center = site.BoothPosition;
            var front = Shot.At("door samples", center + new Vector3(.48f, 1.55f, -5.98f),
                center + new Vector3(.48f, 1.25f, -1.68f), 44);
            MoveCityLightingObserver(city, front);
            var lamp = GameObject.Find(CityEastExitWorldBuilder.CanopyLightName).GetComponent<Light>();
            Assert.That(lamp.enabled, Is.True);
            // The observer is behind the camera; exclude his renderer from this paired exposure.
            var hero = city.Player.Motor.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            foreach (var renderer in hero) renderer.enabled = false;
            try
            {
                Color32[] lit = ReadCanopyLightPixels(camera);
                lamp.enabled = false;
                Color32[] dark = ReadCanopyLightPixels(camera);
                float minIrradiance = float.MaxValue, maxIrradiance = 0f;
                float outer = Mathf.Cos(.5f * lamp.spotAngle * Mathf.Deg2Rad);
                float inner = Mathf.Cos(.5f * lamp.innerSpotAngle * Mathf.Deg2Rad);
                foreach (float height in new[] { .25f, .76f, 1.23f, 1.78f, 2.26f })
                {
                    float dimmest = float.MaxValue, brightest = 0f;
                    foreach (float offset in new[] { -.34f, 0f, .34f })
                    {
                        Vector3 surface = center + new Vector3(.48f + offset, height, -1.68f);
                        Vector3 ray = surface - lamp.transform.position;
                        float cone = Mathf.InverseLerp(outer, inner, Vector3.Dot(ray.normalized, lamp.transform.forward));
                        float range = Mathf.Clamp01(1f - Mathf.Pow(ray.magnitude / lamp.range, 4f));
                        float irradiance = lamp.intensity * cone * cone * range * range * ray.normalized.z / ray.sqrMagnitude;
                        minIrradiance = Mathf.Min(minIrradiance, irradiance);
                        maxIrradiance = Mathf.Max(maxIrradiance, irradiance);
                        float delta = CheckpointLightPatchDelta(camera, surface, lit, dark);
                        dimmest = Mathf.Min(dimmest, delta);
                        brightest = Mathf.Max(brightest, delta);
                        Assert.That(delta, Is.GreaterThan(3f), "Night lamp contribution at " + surface);
                    }
                    Debug.Log($"BOOTH DOOR light at {height:F2} m: patch deltas {dimmest:F1}–{brightest:F1} / 255");
                }
                Assert.That(maxIrradiance / minIrradiance, Is.LessThan(1.9f),
                    "Upper and lower corners share a wash within one photographic stop.");
            }
            finally
            {
                lamp.enabled = true;
                foreach (var renderer in hero) renderer.enabled = true;
            }
        }

        private static void CheckCheckpointRoadLight(CityGameRoot city, CityEastExitPlan site)
        {
            var lamp = city.Night.Atmosphere.GetPracticalLight(CityFringeYardKind.EastUtilityEdge);
            Assert.That(lamp && lamp.isActiveAndEnabled, Is.True);
            Vector3 gate = site.CheckpointPosition;
            var camera = city.Camera;
            var survey = Shot.At("road illumination", gate + new Vector3(-5f, 17f, -.75f),
                gate + new Vector3(-5f, 0f, 0f), 58);
            // Keep the hero and other lights in the same positions for both exposures.
            ApplyCityLightingPose(camera, survey);
            Color32[] lit = ReadCanopyLightPixels(camera);
            try
            {
                lamp.enabled = false;
                Color32[] dark = ReadCanopyLightPixels(camera);
                float minimum = float.MaxValue;
                Vector3 dimmest = default;
                foreach (float x in new[] { site.ApproachStart.x + .25f, gate.x - 9f, gate.x - 6f,
                    gate.x - 3f, gate.x - .55f, gate.x + 1.25f })
                foreach (float offset in new[] { -2.78f, 0f, 2.78f })
                {
                    float z = gate.z + offset;
                    Vector3 asphalt = new Vector3(x, site.SampleRoadTop(x, z), z);
                    float delta = CheckpointLightPatchDelta(camera, asphalt, lit, dark);
                    if (delta < minimum) { minimum = delta; dimmest = asphalt; }
                }
                Assert.That(minimum, Is.GreaterThan(3f), "Road optic must reach the asphalt at " + dimmest);
                Debug.Log($"CHECKPOINT ROAD: minimum rendered lamp contribution across both verges and crown {minimum:F1} / 255");
            }
            finally { lamp.enabled = true; }
        }

        private static float CheckpointLightPatchDelta(Camera camera, Vector3 point, Color32[] lit, Color32[] dark)
        {
            Vector3 uv = camera.WorldToViewportPoint(point);
            Assert.That(uv.z, Is.Positive);
            Assert.That(uv.x, Is.InRange(.025f, .975f));
            Assert.That(uv.y, Is.InRange(.025f, .975f));
            int x = Mathf.RoundToInt(uv.x * 640f), y = Mathf.RoundToInt(uv.y * 360f);
            float delta = 0f;
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int pixel = (y + dy) * 640 + x + dx;
                Color32 a = lit[pixel], b = dark[pixel];
                delta += ((a.r - b.r) + (a.g - b.g) + (a.b - b.b)) / 75f;
            }
            return delta;
        }

        private static IEnumerator WalkCheckpoint(CityGameRoot city, CityEastExitPlan site)
        {
            var motor = city.Player.Motor;
            var follow = city.Camera.GetComponent<PlayerCameraFollow>();
            var guards = city.EastGuards;
            var interactor = city.Player.Interactor;
            bool previousFocus = GameInput.MovementFocused;
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = null;
            try
            {
                keyboard = InputSystem.AddDevice<Keyboard>();
                GameInput.HandleApplicationFocus(true);
                GameInput.ReadMovement();
                guards.AutoAdvance = false;
                interactor.SetInputEnabled(true);
                Vector3 gate = site.CheckpointPosition;
                motor.Teleport(guards.Plan.Ground(gate.x - 4f, gate.z) + Vector3.up * PlayerFactory.GroundedRootOffset);
                motor.SetInputEnabled(true);
                motor.transform.rotation = Quaternion.LookRotation(Vector3.right);
                follow.enabled = true;
                follow.Snap();
                follow.RotateYaw(Mathf.DeltaAngle(city.Camera.transform.eulerAngles.y, 90f));
                follow.Snap();
                yield return null;
                input.Press(keyboard.wKey, queueEventOnly: true);
                for (int frame = 0; frame < 95; frame++)
                {
                    yield return null;
                    Assert.That(GameInput.ReadMovement().y, Is.EqualTo(1f),
                        $"key={keyboard.wKey.isPressed}, focus={GameInput.MovementFocused}, neutral={GameInput.MovementAwaitingNeutral}, allowed={GameInput.CanRead(GameInputContext.Movement)}");
                }
                input.Release(keyboard.wKey, queueEventOnly: true);
                yield return null;
                Assert.That(motor.transform.position.x, Is.InRange(gate.x - 1.1f, gate.x - .25f),
                    "Walking straight into the closed boom stops at its visible face.");
                motor.SetInputEnabled(false);
                yield return WalkChurchGardenLeg(motor, guards.Plan.Ground(gate.x - 1.25f, gate.z + 6f), "along the public fence");
                for (int i = 0; i < 2; i++)
                {
                    var actor = guards.Actor(i);
                    Vector3 near = actor.transform.position + Vector3.left * 1.02f;
                    if (i == 0)
                        yield return WalkChurchGardenLeg(motor, guards.Plan.Ground(near.x, gate.z + 7f), "around the northern guard");
                    yield return WalkChurchGardenLeg(motor, guards.Plan.Ground(near.x, near.z), "to guard " + i);
                    Physics.SyncTransforms();
                    guards.Advance(.1f);
                    yield return null;
                    Assert.That(interactor.ActiveInteractable, Is.SameAs(actor.GetComponent<CityEastGuardInteraction>()));
                    input.Press(keyboard.eKey, queueEventOnly: true);
                    yield return null;
                    input.Release(keyboard.eKey, queueEventOnly: true);
                    for (int frame = 0; frame < 3; frame++) { guards.Advance(.1f); yield return null; }
                    Assert.That(guards.LastSpeaker, Is.EqualTo(i));
                    Assert.That(guards.LastLineKey, Does.StartWith("city.east.guard." + (i == 0 ? "senior" : "junior") + ".reply."));
                    Assert.That(guards.Bubbles.IsShowing(actor.Motion), Is.True);
                    for (int frame = 0; frame < 65; frame++) guards.Advance(.1f);
                }
            }
            finally
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
                GameInput.HandleApplicationFocus(previousFocus);
                guards.AutoAdvance = true;
                motor.SetInputEnabled(true);
            }
        }
    }
}
