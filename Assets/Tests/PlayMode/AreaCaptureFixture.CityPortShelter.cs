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
        [UnityTest]
        [Explicit("Focused canopy positions, clear return walks and actual rest frames.")]
        public IEnumerator CityPortShelter() => CaptureFocusedPort(ValidatePortShelter);

        private static IEnumerator CaptureFocusedPort(
            Func<Camera, CityGameRoot, CityPortController, CityPortCrew, IEnumerator> capture)
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            CityGameRoot city = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                city = UnityEngine.Object.FindAnyObjectByType<CityGameRoot>();
                if (city != null && city.IsInitialized && !AreaTravelService.IsComposing) break;
                yield return null;
            }
            Assert.That(city != null && city.IsInitialized, Is.True);
            city.Cannery.AutoAdvance = false;
            city.Cannery.ForcePresentation = true;
            city.BusPassengers.enabled = city.Bus.enabled = false;
            city.Player.Motor.SetInputEnabled(false);
            foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            var port = city.World.Root.GetComponentInChildren<CityPortController>();
            port.AutoAdvance = false;
            port.ForcePresentation = true;
            var crew = port.GetComponentInChildren<CityPortCrew>();
            crew.UseManualClock = true;
            yield return capture(Camera.main, city, port, crew);
        }

        private static IEnumerator ValidatePortShelter(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            double savedSupply = city.Cannery.WorkingSeconds;
            double savedPort = port.ElapsedSeconds, savedLife = crew.LifeElapsedSeconds;
            bool savedManual = crew.UseManualClock;
            var roof = CityPortAssetProvider.FindPart(port.Dock.gameObject, "COL_Awning").GetComponent<MeshCollider>();
            Assert.That(roof, Is.Not.Null, "Use the existing authored canopy roof.");
            Bounds cover = roof.bounds;
            Assert.That(cover.size.x, Is.EqualTo(8f).Within(.05f));
            Assert.That(cover.size.z, Is.EqualTo(4.8f).Within(.05f));
            Assert.That(Vector3.Distance(cover.center, crew.RestCanopyBounds.center), Is.LessThan(.001f));
            var solids = new List<Collider>(port.Dock.GetComponentsInChildren<Collider>(true));
            foreach (Transform crane in port.CraneBases)
                solids.AddRange(crane.GetComponentsInChildren<Collider>(true));
            solids.AddRange(port.Trolley.GetComponentsInChildren<Collider>(true));
            Transform localCart = city.Cannery.transform.Find("Port warehouse trolley");
            if (localCart != null) solids.AddRange(localCart.GetComponentsInChildren<Collider>(true));
            var bodies = new CapsuleCollider[3];
            for (int role = 2; role < 5; role++)
                bodies[role - 2] = crew.GetWorker(role).GetComponent<CapsuleCollider>();
            try
            {
                crew.UseManualClock = true;
                double life = savedLife + 1000d;
                double held = CityPortCycle.CycleDurationSeconds - .001d;
                Sample(held, life);
                for (int role = 2; role < 5; role++)
                {
                    var actor = crew.GetWorker(role);
                    Assert.That(crew.IsRoleResting(role), Is.True);
                    Assert.That(Vector3.Distance(actor.transform.position, crew.RestPosition(role)), Is.LessThan(.02f));
                    float radius = bodies[role - 2].radius * actor.transform.lossyScale.x;
                    Assert.That(actor.Head.position.x, Is.InRange(cover.min.x + radius, cover.max.x - radius));
                    Assert.That(actor.Head.position.z, Is.InRange(cover.min.z + radius, cover.max.z - radius));
                    Vector3 aboveHead = new Vector3(actor.Head.position.x, cover.max.y + 1f, actor.Head.position.z);
                    Assert.That(roof.Raycast(new Ray(aboveHead, Vector3.down), out RaycastHit hit, cover.size.y + 2f),
                        Is.True, "The actual canopy mesh covers each resting head.");
                    Assert.That(hit.point.y, Is.GreaterThan(actor.Head.position.y + .1f));
                    Assert.That(hit.normal.y, Is.GreaterThan(.95f), "The ray hits the horizontal roof, not a post.");
                    Assert.That(actor.transform.position.y, Is.EqualTo(port.Plan.QuayTopY).Within(.02f));
                }
                CheckBodies("under canopy");
                Vector3 target = new Vector3(cover.center.x, port.Plan.QuayTopY + 1.15f, cover.center.z);
                yield return CapturePortSocialPose(camera, "port-shelter-00-rest-canopy",
                    target + new Vector3(6f, 1f, -6f), target, 52f);
                ValidatePortCanopyLight(camera, port, crew, roof);
                yield return CapturePortSocialPose(camera, "port-shelter-02-warm-work-lamp",
                    target + new Vector3(6f, 1f, -6f), target, 56f);

                var returned = new bool[3];
                var previous = new Vector3[3];
                for (int i = 0; i < 3; i++) previous[i] = bodies[i].transform.position;
                bool capturedReturn = false;
                double nextVisit = CityPortCycle.CycleDurationSeconds;
                int count = (int)Math.Ceiling((CityPortCycle.UnloadStartSeconds + .2d) * 10d);
                for (int sample = 0; sample <= count; sample++)
                {
                    double elapsed = sample * .1d;
                    Sample(nextVisit + elapsed, life + .001d + elapsed);
                    CheckBodies("canopy return " + elapsed.ToString("F1"));
                    for (int role = 2; role < 5; role++)
                    {
                        Vector3 position = bodies[role - 2].transform.position;
                        double required = role == 4 ? CityPortCycle.ApproachDurationSeconds : CityPortCycle.UnloadStartSeconds;
                        if (elapsed <= required)
                            Assert.That(Vector3.Distance(previous[role - 2], position), Is.LessThan(.3f),
                                "The canopy return remains a continuous walk for role " + role);
                        previous[role - 2] = position;
                        returned[role - 2] |= crew.IsRoleReturning(role);
                        if (elapsed >= required)
                        {
                            Assert.That(crew.IsRoleResting(role), Is.False);
                            Assert.That(crew.IsRoleReturning(role), Is.False, "Return precedes the real duty boundary.");
                        }
                    }
                    if (!capturedReturn && crew.IsRoleReturning(4) &&
                        Vector3.Distance(crew.ShoreWorker.transform.position, crew.RestPosition(4)) > 1f)
                    {
                        capturedReturn = true;
                        yield return CapturePortSocialPose(camera, "port-shelter-01-return-route",
                            target + new Vector3(6f, 1f, -6f), target, 56f);
                    }
                    if (sample % 150 == 0) yield return null;
                }
                foreach (bool didReturn in returned) Assert.That(didReturn, Is.True);
                Assert.That(capturedReturn, Is.True);
                Debug.Log("PORT SHELTER: three shore workers rest below the authored roof and return clear of tare, posts, carts and bodies.");
            }
            finally
            {
                city.Cannery.ApplyAt(savedSupply);
                port.ApplyAt(savedPort, 15f);
                crew.ApplyAt(savedPort, savedLife);
                crew.UseManualClock = savedManual;
            }

            void Sample(double portTime, double lifeTime)
            {
                port.ApplyAt(portTime, 15f);
                crew.ApplyAt(portTime, lifeTime);
                Physics.SyncTransforms();
            }

            void CheckBodies(string stage)
            {
                for (int i = 0; i < bodies.Length; i++)
                {
                    var body = bodies[i];
                    foreach (Collider solid in solids)
                    {
                        if (!solid.enabled || !solid.gameObject.activeInHierarchy || !body.bounds.Intersects(solid.bounds)) continue;
                        bool hit = Physics.ComputePenetration(body, body.transform.position, body.transform.rotation,
                            solid, solid.transform.position, solid.transform.rotation, out _, out float depth);
                        Assert.That(hit && depth > .005f, Is.False,
                            $"Canopy role {i + 2} crosses {solid.name} at {stage}: {depth:F3} m.");
                    }
                    for (int j = i + 1; j < bodies.Length; j++)
                    {
                        bool hit = Physics.ComputePenetration(body, body.transform.position, body.transform.rotation,
                            bodies[j], bodies[j].transform.position, bodies[j].transform.rotation, out _, out float depth);
                        Assert.That(hit && depth > .005f, Is.False,
                            $"Canopy roles {i + 2}/{j + 2} overlap at {stage}: {depth:F3} m.");
                    }
                }
            }
        }

        private static void ValidatePortCanopyLight(Camera camera, CityPortController port,
            CityPortCrew crew, MeshCollider roof)
        {
            Transform fixture = port.transform.Find("Port Canopy Work Lamp");
            Assert.That(fixture, Is.Not.Null);
            Light lamp = fixture.GetComponentInChildren<Light>(true);
            Assert.That(lamp, Is.Not.Null);
            Assert.That(lamp.type, Is.EqualTo(LightType.Spot));
            Assert.That(lamp.lightmapBakeType, Is.EqualTo(LightmapBakeType.Realtime));
            Assert.That(lamp.color.r, Is.GreaterThan(lamp.color.b * 1.5f));
            Assert.That(lamp.gameObject.activeInHierarchy, Is.True);
            Assert.That(lamp.range, Is.LessThanOrEqualTo(8f), "The working lamp remains local to the canopy.");
            Assert.That(roof.Raycast(new Ray(lamp.transform.position, Vector3.up), out RaycastHit ceiling, 1f), Is.True,
                "The emitter must be below the existing roof, not trapped above its shadow.");
            Assert.That(ceiling.point.y - lamp.transform.position.y, Is.GreaterThan(.2f));
            var models = CityMiscAssetProvider.LoadOrThrow();
            MeshFilter[] parts = fixture.GetComponentsInChildren<MeshFilter>();
            Assert.That(parts.Length, Is.EqualTo(2));
            Assert.That(parts[0].sharedMesh,
                Is.SameAs(models.GetPartOrThrow(CityMiscKind.YardSpotlightWallMount, 0, 0).Mesh));
            Assert.That(parts[1].sharedMesh,
                Is.SameAs(models.GetPartOrThrow(CityMiscKind.YardSpotlightHeadShell, 0, 0).Mesh));
            var heads = new Vector3[3];
            for (int role = 2; role < 5; role++)
            {
                heads[role - 2] = crew.GetWorker(role).Head.position;
                AssertInCanopyLight(heads[role - 2], "resting face " + role);
                AssertInCanopyLight(crew.GetWorker(role).transform.position + Vector3.up * .8f, "resting body " + role);
            }
            AssertInCanopyLight(port.Plan.World(new Vector3(6.72f, 2.2f, -7.6f)), "empty tare");
            AssertInCanopyLight(port.Plan.World(new Vector3(5.1f, 1.8f, -8.4f)), "parked jack");
            float savedNight = CityNightSiteLightRegistry.NightFactor;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool followEnabled = follow != null && follow.enabled;
            if (follow != null) follow.enabled = false;
            try
            {
                CityNightSiteLightRegistry.SetNightFactor(1f);
                float night = lamp.intensity;
                CityNightSiteLightRegistry.SetNightFactor(0f);
                Assert.That(lamp.enabled, Is.True);
                Assert.That(lamp.intensity, Is.EqualTo(night * GameTimeDayNightRules.DayFixtureFloor).Within(.001f));
                Assert.That(lamp.intensity, Is.GreaterThan(8f));
                Vector3 fixedCamera = camera.transform.position;
                Color32[] lit = ReadCanopyLightPixels(camera);
                lamp.enabled = false;
                Color32[] dark = ReadCanopyLightPixels(camera);
                lamp.enabled = true;
                Assert.That(Vector3.Distance(camera.transform.position, fixedCamera), Is.LessThan(.001f));
                double faces = 0d;
                foreach (Vector3 head in heads) faces += MeanCanopyDifference(head, 5, lit, dark);
                faces /= heads.Length;
                double floor = MeanCanopyDifference(port.Plan.World(new Vector3(9.8f, 1.52f, -7.05f)), 10, lit, dark);
                Assert.That(faces, Is.GreaterThan(.35d), "The real lamp brightens the visible faces at its overcast daytime floor.");
                Assert.That(floor, Is.GreaterThan(.35d), "The canopy has a real pool of light, independent of its halo.");
                Debug.Log($"CANOPY DAY LIGHT: intensity={lamp.intensity:F3}, face delta={faces:F3}, floor delta={floor:F3} / 255");
            }
            finally
            {
                lamp.enabled = true;
                CityNightSiteLightRegistry.SetNightFactor(savedNight);
                if (follow != null) follow.enabled = followEnabled;
            }

            void AssertInCanopyLight(Vector3 point, string purpose)
            {
                Vector3 delta = point - lamp.transform.position;
                Assert.That(delta.magnitude, Is.LessThan(lamp.range - .3f), purpose);
                Assert.That(Vector3.Angle(delta, lamp.transform.forward), Is.LessThan(lamp.spotAngle * .5f - 1f), purpose);
            }

            double MeanCanopyDifference(Vector3 point, int radius, Color32[] lit, Color32[] dark)
            {
                Vector3 screen = camera.WorldToViewportPoint(point);
                Assert.That(screen.z, Is.GreaterThan(0f));
                Assert.That(screen.x, Is.InRange(.03f, .97f));
                Assert.That(screen.y, Is.InRange(.03f, .97f));
                int x = Mathf.RoundToInt(screen.x * 640f), y = Mathf.RoundToInt(screen.y * 360f), count = 0;
                double sum = 0d;
                for (int row = y - radius; row <= y + radius; row++)
                for (int column = x - radius; column <= x + radius; column++)
                {
                    int index = row * 640 + column;
                    sum += ((int)lit[index].r + lit[index].g + lit[index].b - dark[index].r - dark[index].g - dark[index].b) / 3d;
                    count++;
                }
                return sum / count;
            }
        }

        private static Color32[] ReadCanopyLightPixels(Camera camera)
        {
            var target = new RenderTexture(640, 360, 24);
            var pixels = new Texture2D(640, 360, TextureFormat.RGB24, false);
            RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); pixels.Apply();
                return pixels.GetPixels32();
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                UnityEngine.Object.DestroyImmediate(pixels);
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
