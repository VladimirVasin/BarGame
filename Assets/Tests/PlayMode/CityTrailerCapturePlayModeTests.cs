using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The trailer operator. Six moving five-second shots of the city -
    /// a wet street, the quay lamps, the fountain, the cemetery at dawn,
    /// the church, the mol under the lighthouse - each at its own hour
    /// and weather, written as portrait JPEG frames for ffmpeg to cut
    /// to music. Explicit, like every capture in this project: it writes
    /// files and belongs to no sweep. The frames come off the scene's
    /// OWN camera so they carry the real PS1 composite and grade.
    /// </summary>
    public sealed class CityTrailerCapturePlayModeTests
    {
        private const int Width = 1080;
        private const int Height = 1920;
        private const int FramesPerSecond = 30;
        private const int ShotFrames = 150;
        private const int WarmupFrames = 90;
        private const float EyeHeight = 1.65f;
        private const float FieldOfView = 68f;
        private const float TimeoutSeconds = 90f;
        private const string OutDir =
            @"C:\Users\tushk\AppData\Local\Temp\claude\c--Users-tushk----------------\aea60a56-daf2-48b2-90df-47133fd06a05\scratchpad\trailer";

        private readonly StringBuilder report = new StringBuilder();

        private sealed class Shot
        {
            public string Name;
            public int MinuteOfDay;
            public float Rain;
            public float Wetness;
            public Vector3 HeroGround;
            public Func<float, Vector3> Eye;
            public Func<float, Vector3> Target;
        }

        [UnityTest]
        [Explicit("Capture, not a test. Writes trailer frames to the scratchpad.")]
        public IEnumerator SixMovingShots()
        {
            string frames = Path.Combine(OutDir, "frames");
            if (Directory.Exists(frames))
            {
                Directory.Delete(frames, true);
            }

            Directory.CreateDirectory(frames);

            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.City,
                LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!load.isDone && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(load.isDone, Is.True, "City did not load.");

            CityGameRoot root = null;
            deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                root = UnityEngine.Object.FindAnyObjectByType<CityGameRoot>();
                if (root != null && root.IsInitialized && Camera.main != null)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(root, Is.Not.Null, "No CityGameRoot.");
            Assert.That(root.IsInitialized, Is.True, "City never initialized.");
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "No main camera.");

            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            Shot[] shots = ResolveShots(root);
            Log($"shots resolved: {shots.Length}");

            PlayerMotor hero = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
            var hiddenRenderers = new List<Renderer>();
            if (hero != null)
            {
                foreach (Renderer renderer in hero.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.enabled)
                    {
                        renderer.enabled = false;
                        hiddenRenderers.Add(renderer);
                    }
                }
            }

            var target = new RenderTexture(Width, Height, 24);
            var buffer = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            float previousFov = camera.fieldOfView;
            float previousCapture = Time.captureDeltaTime;
            bool weatherWasEnabled = root.Weather != null && root.Weather.enabled;

            try
            {
                Time.captureDeltaTime = 1f / FramesPerSecond;
                camera.targetTexture = target;
                if (root.Weather != null)
                {
                    // The operator owns the weather: each shot names its
                    // rain and its film, and the schedule must not walk
                    // them back mid-shot.
                    root.Weather.enabled = false;
                }

                if (!GameSessionState.IsGameTimeRunning)
                {
                    GameSessionState.TryStartGameTimeFromWake();
                }

                Log($"clock running={GameSessionState.IsGameTimeRunning} minute={GameSessionState.GameMinuteOfDay}");

                int frameIndex = 0;
                for (int shotIndex = 0; shotIndex < shots.Length; shotIndex++)
                {
                    Shot shot = shots[shotIndex];
                    int now = GameSessionState.GameMinuteOfDay;
                    int delta = shot.MinuteOfDay - now;
                    if (delta < 0)
                    {
                        delta += 24 * 60;
                    }

                    if (delta > 0)
                    {
                        GameSessionState.AdvanceGameTime(delta);
                    }

                    if (hero != null)
                    {
                        hero.Teleport(shot.HeroGround + Vector3.up * 0.15f);
                    }

                    if (root.Rain != null)
                    {
                        root.Rain.SetIntensity(shot.Rain);
                    }

                    CityWetSurfaceRegistry.SetImmediate(shot.Wetness);
                    CityWaterResources.SetRainIntensity(shot.Rain);
                    Log($"shot {shotIndex} '{shot.Name}': minute={GameSessionState.GameMinuteOfDay} " +
                        $"(asked {shot.MinuteOfDay}) rain={shot.Rain} wet={shot.Wetness} " +
                        $"eye0={shot.Eye(0f):F1} eye1={shot.Eye(1f):F1} target0={shot.Target(0f):F1}");

                    for (int warm = 0; warm < WarmupFrames; warm++)
                    {
                        // Keep the camera on the shot's opening pose while
                        // the rain fills its volume and the lamps settle,
                        // so nothing about the first frame is a surprise.
                        Pose(camera, shot, 0f);
                        yield return null;
                    }

                    for (int frame = 0; frame < ShotFrames; frame++)
                    {
                        float t = frame / (ShotFrames - 1f);
                        Pose(camera, shot, t);
                        camera.Render();

                        RenderTexture previousActive = RenderTexture.active;
                        RenderTexture.active = target;
                        buffer.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                        buffer.Apply();
                        RenderTexture.active = previousActive;

                        if (frame == 0)
                        {
                            Assert.That(
                                IsBlank(buffer),
                                Is.False,
                                $"Shot '{shot.Name}' opened on a flat colour.");
                        }

                        File.WriteAllBytes(
                            Path.Combine(frames, $"{frameIndex:D5}.jpg"),
                            buffer.EncodeToJPG(92));
                        frameIndex++;
                        yield return null;
                    }
                }

                Log($"frames written: {frameIndex}");
            }
            finally
            {
                Time.captureDeltaTime = previousCapture;
                camera.targetTexture = previousTarget;
                camera.fieldOfView = previousFov;
                if (root.Weather != null)
                {
                    root.Weather.enabled = weatherWasEnabled;
                }

                foreach (Renderer renderer in hiddenRenderers)
                {
                    if (renderer != null)
                    {
                        renderer.enabled = true;
                    }
                }

                target.Release();
                UnityEngine.Object.Destroy(target);
                UnityEngine.Object.Destroy(buffer);
                File.WriteAllText(Path.Combine(OutDir, "report.txt"), report.ToString());
            }
        }

        private static void Pose(Camera camera, Shot shot, float t)
        {
            Vector3 eye = shot.Eye(t);
            Vector3 look = shot.Target(t) - eye;
            if (look.sqrMagnitude < 1e-4f)
            {
                look = Vector3.forward;
            }

            camera.transform.SetPositionAndRotation(
                eye,
                Quaternion.LookRotation(look.normalized, Vector3.up));
            camera.fieldOfView = FieldOfView;
        }

        private Shot[] ResolveShots(CityGameRoot root)
        {
            CityLayout layout = root.Layout;
            CityWorldResult world = root.World;
            var shots = new List<Shot>();

            // 1. The wet street: a walk along the gutter through the
            // largest puddle, night, downpour.
            {
                CityStreetSurfacePlan streets = CityStreetSurfacePlanner.Create(layout);
                IReadOnlyList<RuntimeOrientedBox> patches =
                    CityPuddlePlanner.Create(streets, layout.Seed);
                Assert.That(patches, Is.Not.Empty, "No gutter puddles.");
                var lamps = new List<Vector3>();
                if (root.Night != null && root.Night.LampAnchors != null)
                {
                    foreach (Transform anchor in root.Night.LampAnchors)
                    {
                        if (anchor != null)
                        {
                            lamps.Add(anchor.position);
                        }
                    }
                }

                // The puddle that lies nearest a street lamp, among the
                // larger ones: walking toward the lamp puts the water
                // between the eye and the light, and the light in the
                // water.
                RuntimeOrientedBox best = patches[0];
                Vector3 lamp = best.Center + Vector3.up * 4f;
                float bestScore = float.PositiveInfinity;
                foreach (RuntimeOrientedBox patch in patches)
                {
                    float area = patch.Size.x * patch.Size.z;
                    if (area < 0.6f)
                    {
                        continue;
                    }

                    foreach (Vector3 candidate in lamps)
                    {
                        float planar = Vector2.Distance(
                            new Vector2(candidate.x, candidate.z),
                            new Vector2(patch.Center.x, patch.Center.z));
                        float score = planar - Mathf.Sqrt(area);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = patch;
                            lamp = candidate;
                        }
                    }
                }

                Vector3 along = best.Rotation *
                    (best.Size.x >= best.Size.z ? Vector3.right : Vector3.forward);
                along.y = 0f;
                along.Normalize();
                Vector3 centre = best.Center;
                if (Vector3.Dot(lamp - centre, along) < 0f)
                {
                    along = -along;
                }

                Vector3 start = centre - along * 6.5f;
                Vector3 end = centre + along * 1.0f;
                Vector3 lampLook = new Vector3(lamp.x, centre.y + 2.5f, lamp.z);
                shots.Add(new Shot
                {
                    Name = "street-puddle-dusk-rain",
                    MinuteOfDay = 19 * 60 + 45,
                    Rain = 0.6f,
                    Wetness = 1f,
                    HeroGround = centre - along * 3f,
                    Eye = t => Vector3.Lerp(start, end, Smooth(t)) + Vector3.up * EyeHeight,
                    Target = t => Vector3.Lerp(
                        centre,
                        lampLook,
                        Smooth(Mathf.Clamp01((t - 0.4f) / 0.6f)))
                });
            }

            // 2. The quay: a dolly along one bank's lamps, night, rain.
            {
                IReadOnlyList<Transform> anchors = world.RiverQuayLampAnchors;
                Assert.That(anchors, Is.Not.Null.And.Count.GreaterThanOrEqualTo(4), "Too few quay lamps.");
                var positions = new List<Vector3>();
                foreach (Transform anchor in anchors)
                {
                    if (anchor != null)
                    {
                        positions.Add(anchor.position);
                    }
                }

                positions.Sort((a, b) => a.x.CompareTo(b.x));
                float medianX = positions[positions.Count / 2].x;
                var bank = positions.FindAll(p => p.x < medianX);
                if (bank.Count < 4)
                {
                    bank = positions.FindAll(p => p.x >= medianX);
                }

                Assert.That(bank.Count, Is.GreaterThanOrEqualTo(3), "A bank with fewer than three lamps.");
                bank.Sort((a, b) => a.z.CompareTo(b.z));
                int startIndex = Mathf.Max(0, bank.Count / 2 - 2);
                Vector3 a0 = bank[startIndex];
                Vector3 a1 = bank[Mathf.Min(bank.Count - 1, startIndex + 1)];
                Vector3 ahead = bank[Mathf.Min(bank.Count - 1, startIndex + 3)];
                float bankX = bank[0].x;
                // Which way is the water: toward the other bank.
                float towardRiver = medianX > bankX ? 1f : -1f;
                // The lamps hang on the quay wall over the water, under
                // the promenade's railing: from the pavement they are
                // hidden, so the shot is a boat's eye - low over the
                // river two metres off the wall, gliding along it toward
                // the lanterns and the bridge.
                Vector3 waterA = GroundAt(a0 + new Vector3(towardRiver * 2.2f, 0f, 0f), a0.y - 3f);
                Vector3 waterB = GroundAt(a1 + new Vector3(towardRiver * 2.2f, 0f, 0f), a1.y - 3f);
                Vector3 far = bank[Mathf.Min(bank.Count - 1, startIndex + 3)];
                Vector3 aheadTarget = new Vector3(
                    far.x + towardRiver * 1.0f,
                    far.y - 0.5f,
                    far.z + (far.z - a0.z) * 0.6f);
                // The hero stays on the promenade, so the rain and fog
                // volumes he carries still cover the water beside him.
                Vector3 promenade = GroundAt(a0 - new Vector3(towardRiver * 2.4f, 0f, 0f), a0.y - 3f);
                shots.Add(new Shot
                {
                    Name = "quay-lamps-dusk",
                    MinuteOfDay = 20 * 60 + 15,
                    Rain = 0.45f,
                    Wetness = 1f,
                    HeroGround = promenade,
                    Eye = t => Vector3.Lerp(waterA, waterB, Smooth(t)) + Vector3.up * 1.4f,
                    Target = t => aheadTarget
                });
            }

            // 3. The fountain: a slow orbit at dusk.
            {
                Transform fountain = FindByName(world.Root != null ? world.Root.transform : null, "Park Fountain Water");
                Assert.That(fountain, Is.Not.Null, "No park fountain water.");
                // "Park Fountain Water" is a container at the parent's
                // origin; the basin sheets are its children. Their
                // bounds, not its transform, say where the water is.
                Renderer[] sheets = fountain.GetComponentsInChildren<Renderer>();
                Assert.That(sheets, Is.Not.Empty, "The fountain has no water sheets.");
                Bounds basin = sheets[0].bounds;
                for (int i = 1; i < sheets.Length; i++)
                {
                    basin.Encapsulate(sheets[i].bounds);
                }

                Vector3 centre = basin.center;
                Vector3 ground = GroundAt(centre + new Vector3(0f, 0f, -7f), centre.y);
                shots.Add(new Shot
                {
                    Name = "fountain-dusk",
                    MinuteOfDay = 19 * 60 + 15,
                    Rain = 0.4f,
                    Wetness = 0.7f,
                    HeroGround = ground,
                    Eye = t =>
                    {
                        float angle = Mathf.Lerp(-55f, 55f, Smooth(t)) * Mathf.Deg2Rad;
                        return new Vector3(
                            centre.x + Mathf.Sin(angle) * 7.5f,
                            ground.y + EyeHeight,
                            centre.z - Mathf.Cos(angle) * 7.5f);
                    },
                    Target = t => new Vector3(centre.x, ground.y + 1.3f, centre.z)
                });
            }

            // 4. The cemetery at dawn: a slow push-in over the plots.
            {
                CityCemeteryPlan cemetery = world.CemeteryPlan;
                Assert.That(cemetery, Is.Not.Null, "No cemetery plan.");
                Assert.That(cemetery.Plots, Is.Not.Empty, "No plots.");
                Vector3 sum = Vector3.zero;
                foreach (CityCemeteryPlotDescriptor plot in cemetery.Plots)
                {
                    sum += plot.Ground;
                }

                Vector3 centre = sum / cemetery.Plots.Count;
                Vector3 from = world.ChurchPlan != null
                    ? world.ChurchPlan.DoorGroundPosition
                    : centre + Vector3.forward * 20f;
                Vector3 dir = centre - from;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1f)
                {
                    dir = Vector3.forward;
                }

                dir.Normalize();
                Vector3 start = GroundAt(centre - dir * 15f, centre.y);
                Vector3 end = GroundAt(centre - dir * 6f, centre.y);
                shots.Add(new Shot
                {
                    Name = "cemetery-dawn",
                    MinuteOfDay = 6 * 60 + 45,
                    Rain = 0.18f,
                    Wetness = 0.4f,
                    HeroGround = start,
                    Eye = t => Vector3.Lerp(start, end, Smooth(t)) + Vector3.up * EyeHeight,
                    Target = t => new Vector3(centre.x, centre.y + 0.8f, centre.z)
                });
            }

            // 5. The church: a crane up its west front in the morning.
            {
                CityChurchPlan church = world.ChurchPlan;
                Assert.That(church, Is.Not.Null, "No church plan.");
                Vector3 door = church.DoorGroundPosition;
                Vector3 outward = church.EntranceOutwardDirection;
                outward.y = 0f;
                outward.Normalize();
                Vector3 ground = GroundAt(door + outward * 14f, door.y);
                shots.Add(new Shot
                {
                    Name = "church-morning",
                    MinuteOfDay = 8 * 60 + 30,
                    Rain = 0.18f,
                    Wetness = 0.35f,
                    HeroGround = ground,
                    Eye = t => door + outward * Mathf.Lerp(17f, 10f, Smooth(t)) +
                               Vector3.up * Mathf.Lerp(EyeHeight, 5.5f, Smooth(t)),
                    Target = t => door + Vector3.up * Mathf.Lerp(2.5f, 8f, Smooth(t))
                });
            }

            // 6. The mol under the lighthouse: along the deck toward the
            // lantern, near midnight, in a downpour.
            {
                CitySeacoastPlan coast = world.SeacoastPlan;
                Assert.That(coast, Is.Not.Null, "No seacoast plan.");
                CityLighthouseIslandPlan island =
                    CityLighthouseIslandPlanner.Create(layout.Seed, coast);
                Assert.That(island, Is.Not.Null, "No lighthouse island.");
                Vector3 lantern = island.LanternPosition;
                CitySeacoastFrame frame = coast.Frame;
                // The island anchors 39 m past the waterline: from the
                // sand right opposite it the tower is a silhouette in
                // the fog with the lantern turning, which is the shot.
                float sandZ = frame.WaterlineZ - 4f;
                float xMin = frame.BeachRowBounds.xMin + 4f;
                float xMax = frame.BeachRowBounds.xMax - 4f;
                float x0 = Mathf.Clamp(lantern.x - 7f, xMin, xMax);
                float x1 = Mathf.Clamp(lantern.x + 7f, xMin, xMax);
                // Keep off the river channel's cut through the shore.
                if (x0 < frame.ChannelXMax + 3f && x1 > frame.ChannelXMin - 3f)
                {
                    float shift = frame.ChannelXMax + 3f - x0;
                    x0 += shift;
                    x1 += shift;
                }

                Vector3 start = GroundAt(new Vector3(x0, frame.BeachEdgeTopY, sandZ), frame.BeachEdgeTopY);
                Vector3 end = GroundAt(new Vector3(x1, frame.BeachEdgeTopY, sandZ), frame.BeachEdgeTopY);
                Vector3 look = new Vector3(lantern.x, lantern.y - 2.5f, lantern.z);
                shots.Add(new Shot
                {
                    Name = "beach-lighthouse-dusk",
                    MinuteOfDay = 20 * 60 + 45,
                    Rain = 0.5f,
                    Wetness = 1f,
                    HeroGround = Vector3.Lerp(start, end, 0.5f),
                    Eye = t => Vector3.Lerp(start, end, Smooth(t)) + Vector3.up * EyeHeight,
                    Target = t => look
                });
            }

            // The clock only runs forward: order the shots by their hour
            // so the day is walked once. The cut order is ffmpeg's.
            shots.Sort((a, b) => a.MinuteOfDay.CompareTo(b.MinuteOfDay));
            return shots.ToArray();
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static Vector3 GroundAt(Vector3 position, float fallbackY)
        {
            if (Physics.Raycast(
                    new Vector3(position.x, fallbackY + 30f, position.z),
                    Vector3.down,
                    out RaycastHit hit,
                    90f))
            {
                return new Vector3(position.x, hit.point.y, position.z);
            }

            return new Vector3(position.x, fallbackY, position.z);
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root == null)
            {
                GameObject found = GameObject.Find(name);
                return found != null ? found.transform : null;
            }

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            GameObject fallback = GameObject.Find(name);
            return fallback != null ? fallback.transform : null;
        }

        private static bool IsBlank(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            Color32 first = pixels[0];
            int step = Mathf.Max(1, pixels.Length / 400);
            for (int i = 0; i < pixels.Length; i += step)
            {
                Color32 p = pixels[i];
                if (Mathf.Abs(p.r - first.r) > 3 ||
                    Mathf.Abs(p.g - first.g) > 3 ||
                    Mathf.Abs(p.b - first.b) > 3)
                {
                    return false;
                }
            }

            return true;
        }

        private void Log(string line)
        {
            report.AppendLine(line);
            Debug.Log("[Trailer] " + line);
        }
    }
}
