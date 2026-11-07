using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using StopFixture = BarPromenade.CityBusStopLighting;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private const string StopLightingCaptureArea = "CityBusStopLighting";

        private sealed class StopLightingCase
        {
            internal CityBusPlan Plan;
            internal CityBusStopDescriptor Stop;
            internal Transform Root;
            internal StopFixture Fixture;
        }

        [UnityTest]
        [Timeout(180000)]
        [Explicit("Every physical bus platform: continuous roof strip, real receiver spill, day floor, stable distant light and bounded shadows.")]
        public IEnumerator CityBusStopLighting()
        {
            string[] keys = { "graphics.dof", "graphics.intoxication_fx", "graphics.aspect_4_3" };
            var preferences = keys.ToDictionary(key => key,
                key => PlayerPrefs.HasKey(key) ? (int?)PlayerPrefs.GetInt(key) : null);
            CityGameRoot city = null;
            GameTimeRuntime clock = null;
            PlayerCameraFollow follow = null;
            bool clockEnabled = false, followEnabled = false, motorInput = false, interactorInput = false;
            float previousCaptureDelta = Time.captureDeltaTime;
            var hidden = new List<Renderer>();
            var hiddenFleet = new Dictionary<Renderer, bool>();
            var fleetScan = new List<Renderer>(128);
            var owned = new List<Light>();
            try
            {
                Time.captureDeltaTime = 1f / 30f;
                GameSessionState.BeginNewGame("Test bus stop lighting");
                Assert.That(GameSessionState.TryStartGameTimeAt(21 * 60), Is.True);
                GraphicsEffectsSettings.DepthOfFieldEnabled = false;
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = false;
                GraphicsEffectsSettings.AspectRatio43Enabled = false;
                yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    if (city != null && city.IsInitialized) break;
                    yield return null;
                }
                Assert.That(city != null && city.IsInitialized, Is.True);
                city.DayNight.enabled = false; city.Weather.enabled = false;
                city.DayNight.ApplyCurrentTime(true);
                clock = Object.FindAnyObjectByType<GameTimeRuntime>();
                Assert.That(clock, Is.Not.Null);
                clockEnabled = clock.enabled; clock.enabled = false;
                follow = city.Camera.GetComponent<PlayerCameraFollow>();
                followEnabled = follow.enabled; follow.enabled = false;
                motorInput = city.Player.Motor.InputEnabled;
                interactorInput = city.Player.Interactor.InputEnabled;
                city.Player.Motor.SetInputEnabled(false);
                city.Player.Interactor.SetInputEnabled(false);
                foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                    if (renderer.enabled) { renderer.enabled = false; hidden.Add(renderer); }
                // Receiver photographs must not depend on a moving bus occupying
                // the line of sight. The separate boarding check keeps the full fleet.
                HideStopCaptureFleet(city, hiddenFleet, fleetScan);
                city.Night.Atmosphere.RefreshImmediate();
                Physics.SyncTransforms();

                var platforms = CityBusNetworkFurniture.Platforms(city.BusPlans).ToArray();
                Assert.That(platforms, Is.Not.Empty);
                var fixtures = city.BusStops.GetComponentsInChildren<StopFixture>(true);
                Assert.That(fixtures.Length, Is.EqualTo(platforms.Length),
                    "Every physical platform must own one light assembly, including separate interchange banks.");
                Assert.That(city.BusStops.transform.childCount, Is.EqualTo(platforms.Length));
                var cases = new List<StopLightingCase>();
                for (int i = 0; i < platforms.Length; i++)
                {
                    Transform root = city.BusStops.transform.GetChild(i);
                    Assert.That(Vector3.Distance(root.position, platforms[i].stop.ShelterPosition), Is.LessThan(.005f));
                    StopFixture fixture = root.GetComponent<StopFixture>();
                    Assert.That(fixture, Is.Not.Null, platforms[i].stop.Id);
                    AssertStopLightAssembly(root, fixture, city.Night.Atmosphere);
                    var item = new StopLightingCase { Plan = platforms[i].line, Stop = platforms[i].stop,
                        Root = root, Fixture = fixture };
                    cases.Add(item);
                    AssertStopReceiverCoverage(city, item);
                    owned.AddRange(fixture.Lights);
                }
                Assert.That(owned.Distinct().Count(), Is.EqualTo(owned.Count), "A platform cannot borrow another platform's sources.");
                StopLightingCase lower = cases.OrderBy(item => item.Stop.ShelterPosition.y).First();
                StopLightingCase highest = cases.OrderByDescending(item => item.Stop.ShelterPosition.y).First();
                StopLightingCase transfer = cases.First(item => item.Stop.TransferNumber > 0);
                Assert.That(highest.Stop.ShelterPosition.y - lower.Stop.ShelterPosition.y, Is.GreaterThan(30f));

                SetStopLightingCamera(city, lower);
                city.Night.Atmosphere.RefreshStopShadows(city.Camera, 1f, true);
                AssertStopShadowBudget(fixtures);
                Assert.That(lower.Fixture.ShadowStrength, Is.GreaterThan(.99f));
                CaptureStopLightingPair(city, lower, "01-lower-night", hiddenFleet, fleetScan);
                CaptureStopStripDetail(city, lower, hiddenFleet, fleetScan);
                yield return MeasureStopLightFrameCost(city, owned, hiddenFleet, fleetScan);
                Vector3 cameraPosition = city.Camera.transform.position;
                Quaternion cameraRotation = city.Camera.transform.rotation;
                Vector3[] positions = owned.Select(light => light.transform.position).ToArray();
                float[] powers = owned.Select(light => light.intensity).ToArray();
                city.Player.Motor.Teleport(cameraPosition + Vector3.right * 150f);
                city.Camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                city.Night.Atmosphere.RefreshImmediate();
                city.Night.Atmosphere.RefreshStopShadows(city.Camera, 1f, true);
                for (int i = 0; i < owned.Count; i++)
                {
                    Assert.That(owned[i].isActiveAndEnabled, Is.True);
                    Assert.That(owned[i].transform.position, Is.EqualTo(positions[i]));
                    Assert.That(owned[i].intensity, Is.EqualTo(powers[i]).Within(.0001f),
                        "Moving the hero cannot light or extinguish a platform in a fixed visible frame.");
                }
                CaptureStopLightingFrame(city, hiddenFleet, fleetScan, "02-fixed-view-hero-far");

                SetStopLightingCamera(city, highest);
                bool partialShadow = false;
                for (int frame = 0; frame < 40; frame++)
                {
                    HideStopCaptureFleet(city, hiddenFleet, fleetScan);
                    city.Night.Atmosphere.RefreshStopShadows(city.Camera, .05f);
                    AssertStopShadowBudget(fixtures);
                    partialShadow |= fixtures.Any(fixture => fixture.ShadowStrength > .001f && fixture.ShadowStrength < .999f);
                    yield return null;
                }
                Assert.That(partialShadow, Is.True, "A change of visible platforms must fade shadow detail rather than switch it abruptly.");
                Assert.That(highest.Fixture.ShadowStrength, Is.GreaterThan(.99f));
                CaptureStopLightingPair(city, highest, "03-ridge-night", hiddenFleet, fleetScan);
                SetStopLightingFarCamera(city, highest);
                city.Night.Atmosphere.RefreshStopShadows(city.Camera, 1f, true);
                CaptureStopLightingFrame(city, hiddenFleet, fleetScan, "03-ridge-far-back-oblique-night");
                SetStopLightingCamera(city, transfer);
                city.Night.Atmosphere.RefreshStopShadows(city.Camera, 1f, true);
                AssertStopShadowBudget(fixtures);
                CaptureStopLightingFrame(city, hiddenFleet, fleetScan, "04-transfer-night");

                double toNoon = (12 * 60 - GameSessionState.GameTimeOfDayMinutes + 1440) % 1440;
                GameSessionState.AdvanceGameTime((float)(toNoon / GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                city.Night.Atmosphere.RefreshImmediate();
                for (int i = 0; i < owned.Count; i++)
                    Assert.That(owned[i].intensity, Is.EqualTo(powers[i] * GameTimeDayNightRules.DayFixtureFloor).Within(.001f),
                        "Every canopy and apron source retains the common daytime fixture floor.");
                SetStopLightingCamera(city, lower);
                CaptureStopLightingFrame(city, hiddenFleet, fleetScan, "05-lower-noon");
                Assert.That(city.EnterDormant(), Is.True);
                foreach (Light light in owned) Assert.That(light.enabled, Is.False);
                city.ResumeFromDormant();
                city.DayNight.enabled = false; city.Weather.enabled = false; follow.enabled = false;
                city.Night.Atmosphere.RefreshImmediate();
                foreach (Light light in owned) Assert.That(light.isActiveAndEnabled, Is.True);

                double toNight = (21 * 60 - GameSessionState.GameTimeOfDayMinutes + 1440) % 1440;
                GameSessionState.AdvanceGameTime((float)(toNight / GameTimeState.GameMinutesPerRealSecond));
                foreach (KeyValuePair<Renderer, bool> entry in hiddenFleet)
                    if (entry.Key != null) entry.Key.forceRenderingOff = entry.Value;
                hiddenFleet.Clear();
                GameSessionState.EnterHome();
                yield return SceneManager.LoadSceneAsync(SceneIds.HomeInterior, LoadSceneMode.Single);
                yield return AssertHomeStopLighting();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return null;
                foreach (Light light in owned) Assert.That(light == null, Is.True, "Platform light lifetime belongs to the exterior.");
                Debug.Log("CityBusStopLighting: all physical platforms, continuous strips, supported receivers, fixed light/day floor, bounded fading shadows and Home verified.");
            }
            finally
            {
                if (city != null)
                {
                    if (city.IsDormant) city.ResumeFromDormant();
                    city.DayNight.enabled = true; city.Weather.enabled = true;
                    city.Player.Motor.SetInputEnabled(motorInput);
                    city.Player.Interactor.SetInputEnabled(interactorInput);
                }
                if (clock != null) clock.enabled = clockEnabled;
                if (follow != null) follow.enabled = followEnabled;
                foreach (Renderer renderer in hidden) if (renderer != null) renderer.enabled = true;
                foreach (KeyValuePair<Renderer, bool> entry in hiddenFleet)
                    if (entry.Key != null) entry.Key.forceRenderingOff = entry.Value;
                foreach (KeyValuePair<string, int?> preference in preferences)
                    if (preference.Value.HasValue) PlayerPrefs.SetInt(preference.Key, preference.Value.Value);
                    else PlayerPrefs.DeleteKey(preference.Key);
                PlayerPrefs.Save(); GraphicsEffectsSettings.ResetLoadedStateForTests();
                Time.captureDeltaTime = previousCaptureDelta;
                GameSessionState.BeginNewGame("Test bus stop lighting cleanup");
            }
        }

        private static void AssertStopLightAssembly(Transform root, StopFixture fixture, CityNightAtmosphere atmosphere)
        {
            Assert.That(fixture.SourceAnchors.Count, Is.EqualTo(StopFixture.LightCount));
            Assert.That(fixture.Lights.Count, Is.EqualTo(StopFixture.LightCount));
            Assert.That(fixture.StripRenderers.Count, Is.EqualTo(1), "One joined ring must illuminate all four inner roof edges.");
            Assert.That(fixture.PlatformLens, Is.Not.Null);
            Renderer ring = fixture.StripRenderers[0];
            Assert.That(ring != null && ring.enabled && ring.sharedMaterial == CityNightResources.EmissiveMaterial, Is.True);
            Assert.That(fixture.PlatformLens.enabled && fixture.PlatformLens.sharedMaterial == CityNightResources.EmissiveMaterial, Is.True);
            AssertStopStripGeometry(root, ring);
            for (int i = 0; i < fixture.Lights.Count; i++)
            {
                Light light = fixture.Lights[i];
                Transform anchor = fixture.SourceAnchors[i];
                Vector3 authored = root.TransformPoint(CityMiscAssetProvider.GetBusStopLightAnchor(i));
                Assert.That(anchor, Is.Not.Null);
                Assert.That(Vector3.Distance(anchor.position, authored), Is.LessThan(.002f), "Light sources must use the authored FBX anchors.");
                Assert.That(light != null && light.isActiveAndEnabled && light.intensity > 0f, Is.True);
                Assert.That(light.type, Is.EqualTo(LightType.Spot));
                Assert.That(Vector3.Distance(light.transform.position, anchor.position), Is.LessThan(.002f));
                Assert.That(light.range, Is.InRange(3f, 7f));
                Assert.That(light.spotAngle, Is.InRange(60f, 175f));
                Renderer lens = i < StopFixture.StripLightCount ? ring : fixture.PlatformLens;
                if (i < StopFixture.StripLightCount)
                    Assert.That(Mathf.Abs(root.InverseTransformPoint(anchor.position).x + 2.55f), Is.GreaterThan(.75f),
                        "The strip must emit from distributed rail positions, not a central cone above the bench.");
                Assert.That(Vector3.Distance(lens.bounds.ClosestPoint(anchor.position), anchor.position), Is.LessThan(.08f),
                    "A real source must leave a visible strip or diffuser, never the empty centre of the canopy.");
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                    if (collider.enabled && !collider.isTrigger)
                        Assert.That(Vector3.Distance(collider.ClosestPoint(anchor.position), anchor.position), Is.GreaterThan(.012f),
                            "The source must not be buried in the steel back panel or a structural post.");
                Assert.That(atmosphere.StreetLightPool.Contains(light), Is.True, "Platform sources belong to the shared fixed-light owner.");
            }
            var plateLights = atmosphere.StreetLightPool.Where(light => light.type == LightType.Spot && light.range < 1f &&
                Vector3.Distance(light.transform.position, root.TransformPoint(new Vector3(0f, 2.18f, 0f))) < .9f).ToArray();
            Assert.That(plateLights.Length, Is.EqualTo(2), "Both existing plate faces retain their separate grazing beams.");
            foreach (Light light in plateLights) Assert.That(light.isActiveAndEnabled && light.intensity > 0f, Is.True);
        }

        private static void AssertStopStripGeometry(Transform root, Renderer renderer)
        {
            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh, Is.Not.Null);
            if (!mesh.isReadable)
            {
                CityMiscAssetProvider provider = CityMiscAssetProvider.Load();
                for (int i = 0; i < CityMiscAssetProvider.GetPartCount(CityMiscKind.Route01ShelterShell); i++)
                {
                    CityMiscMeshPart part = provider.GetPartOrThrow(CityMiscKind.Route01ShelterShell, 0, i);
                    if (part.Role == CityMiscMeshRole.BacklitSign) { mesh = part.Mesh; break; }
                }
            }
            Assert.That(mesh.isReadable, Is.True, "The authored strip geometry must remain measurable in acceptance.");
            var vertices = mesh.vertices;
            var localVertices = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                localVertices[i] = root.InverseTransformPoint(renderer.transform.TransformPoint(vertices[i]));
            Assert.That(vertices.Length, Is.GreaterThan(12));
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            foreach (Vector3 point in localVertices)
            {
                minX = Mathf.Min(minX, point.x); maxX = Mathf.Max(maxX, point.x);
                minZ = Mathf.Min(minZ, point.z); maxZ = Mathf.Max(maxZ, point.z);
            }
            Assert.That(maxX - minX, Is.InRange(4.3f, 4.6f));
            Assert.That(maxZ - minZ, Is.InRange(.8f, 1.05f));
            var sides = new bool[4];
            foreach (Vector3 point in localVertices)
            {
                Assert.That(point.y, Is.InRange(2.46f, 2.52f));
                bool west = point.x < minX + .06f, east = point.x > maxX - .06f;
                bool rear = point.z < minZ + .06f, front = point.z > maxZ - .06f;
                Assert.That(west || east || rear || front, Is.True, "The luminous mesh must leave the middle of the roof empty.");
                if (west) sides[0] = true; if (east) sides[1] = true;
                if (rear) sides[2] = true; if (front) sides[3] = true;
            }
            Assert.That(sides.All(side => side), Is.True, "The strip must continue around all four inner edges.");
            int[] triangles = mesh.triangles;
            Assert.That(Covered(new Vector2((minX + maxX) * .5f, (minZ + maxZ) * .5f)), Is.False,
                "The joined strip cannot become a filled glowing roof panel.");
            for (int i = 0; i <= 8; i++)
            {
                float x = Mathf.Lerp(minX + .0225f, maxX - .0225f, i / 8f);
                float z = Mathf.Lerp(minZ + .0225f, maxZ - .0225f, i / 8f);
                Assert.That(Covered(new Vector2(x, minZ + .0225f)) && Covered(new Vector2(x, maxZ - .0225f)) &&
                    Covered(new Vector2(minX + .0225f, z)) && Covered(new Vector2(maxX - .0225f, z)), Is.True,
                    "All four narrow rails must be continuous through their corners.");
            }
            bool Covered(Vector2 at)
            {
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = localVertices[triangles[i]], b = localVertices[triangles[i + 1]], c = localVertices[triangles[i + 2]];
                    float area = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
                    if (Mathf.Abs(area) < .0000001f) continue;
                    float ab = (b.x - a.x) * (at.y - a.z) - (b.z - a.z) * (at.x - a.x);
                    float bc = (c.x - b.x) * (at.y - b.z) - (c.z - b.z) * (at.x - b.x);
                    float ca = (a.x - c.x) * (at.y - c.z) - (a.z - c.z) * (at.x - c.x);
                    if ((ab >= -.00001f && bc >= -.00001f && ca >= -.00001f) ||
                        (ab <= .00001f && bc <= .00001f && ca <= .00001f)) return true;
                }
                return false;
            }
        }

        private static void AssertStopReceiverCoverage(CityGameRoot city, StopLightingCase item)
        {
            foreach (var receiver in StopLightingReceivers(city, item))
                Assert.That(item.Fixture.Lights.Any(light => StopLightReaches(light, receiver.point)), Is.True,
                    item.Stop.Id + " must illuminate its " + receiver.name + " at " + receiver.point);
        }

        private static (string name, Vector3 point)[] StopLightingReceivers(CityGameRoot city, StopLightingCase item)
        {
            CityBenchSeat bench = CityBusStopWorldBuilder.DescribeShelterBenchSeat(item.Stop);
            Vector3 Ground(float along) => CityBusStopWaitPlanner.StandingPosition(item.Plan, item.Stop, city.Layout, along) + Vector3.up * .035f;
            return new[]
            {
                ("wooden bench", bench.SeatTopCenter + Vector3.up * .02f),
                ("steel back panel", item.Root.TransformPoint(new Vector3(-2.55f, 1.55f, -.415f))),
                ("waiting pavement", Ground(0f)),
                ("front boarding pavement", Ground(3.05f)),
                ("rear boarding pavement", Ground(-1.34f))
            };
        }

        private static bool StopLightReaches(Light light, Vector3 point)
        {
            Vector3 delta = point - light.transform.position;
            return delta.sqrMagnitude < light.range * light.range * .98f &&
                Vector3.Dot(light.transform.forward, delta.normalized) > Mathf.Cos(light.spotAngle * .5f * Mathf.Deg2Rad) + .01f;
        }

        private static void AssertStopShadowBudget(IReadOnlyList<StopFixture> fixtures)
        {
            int active = 0;
            foreach (StopFixture fixture in fixtures)
            {
                Assert.That(fixture.ShadowStrength, Is.InRange(0f, 1f));
                if (fixture.Lights.Any(light => light.shadows != LightShadows.None)) active++;
            }
            Assert.That(active, Is.LessThanOrEqualTo(2), "Only two visible platform assemblies may occupy the additional shadow atlas.");
        }

        private static void SetStopLightingCamera(CityGameRoot city, StopLightingCase item)
        {
            Vector3 along = item.Stop.Forward.normalized;
            Vector3 road = item.Stop.RoadsideForward.normalized;
            Vector3 target = item.Stop.ShelterPosition - along * 1.2f + Vector3.up * 1.05f;
            Vector3 eye = item.Stop.ShelterPosition + road * 9f + along * .5f + Vector3.up * 1.75f;
            city.Player.Motor.Teleport(eye - Vector3.up * EyeHeight);
            city.DayNight.ApplyCurrentTime(true);
            city.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            city.Camera.fieldOfView = 72f; city.Camera.aspect = 16f / 9f;
            city.Visibility.Refresh(true); city.Night.Atmosphere.RefreshImmediate();
            Physics.SyncTransforms();
        }

        private static void SetStopLightingFarCamera(CityGameRoot city, StopLightingCase item)
        {
            Vector3 along = item.Stop.Forward.normalized, road = item.Stop.RoadsideForward.normalized;
            Vector3 target = item.Stop.ShelterPosition - along * 1.2f + Vector3.up * 1.1f;
            Vector3 eye = target - road * 48f + along * 18f + Vector3.up * 1.4f;
            var hits = new RaycastHit[64];
            bool found = false;
            foreach (float height in new[] { 2.5f, 5f, 9f })
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 candidate = item.Stop.ShelterPosition - road * 12f + along * (side * 48f) + Vector3.up * height;
                    Vector3 delta = target - candidate;
                    int count = Physics.RaycastNonAlloc(candidate, delta.normalized, hits, delta.magnitude - .5f,
                        Physics.AllLayers, QueryTriggerInteraction.Ignore);
                    bool blocked = count == hits.Length;
                    for (int i = 0; i < count; i++)
                        if (!hits[i].collider.transform.IsChildOf(item.Root) &&
                            !hits[i].collider.transform.IsChildOf(city.Player.GameObject.transform) &&
                            hits[i].collider.GetComponentInParent<CityBusActor>() == null &&
                            hits[i].collider.GetComponentInParent<CityPedestrianActor>() == null) blocked = true;
                    if (blocked) continue;
                    eye = candidate; found = true; break;
                }
                if (found) break;
            }
            city.Player.Motor.Teleport(eye - Vector3.up * EyeHeight);
            city.DayNight.ApplyCurrentTime(true);
            city.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            city.Camera.fieldOfView = 72f; city.Camera.aspect = 16f / 9f;
            city.Visibility.Refresh(true); city.Night.Atmosphere.RefreshImmediate();
            Physics.SyncTransforms();
            Debug.Log("CityBusStopLighting far back/oblique view: " + Vector3.Distance(eye, item.Root.position).ToString("F1") +
                " m; clear surrounding sightline=" + found + ".");
        }

        private static IEnumerator MeasureStopLightFrameCost(CityGameRoot city, IReadOnlyList<Light> lights,
            Dictionary<Renderer, bool> hiddenFleet, List<Renderer> fleetScan)
        {
            if (!FrameTimingManager.IsFeatureEnabled())
            {
                Debug.Log("CityBusStopLighting: GPU frame cost unavailable; frame timing is disabled in the open Editor.");
                yield break;
            }
            var masks = new int[lights.Count];
            for (int i = 0; i < masks.Length; i++) masks[i] = lights[i].cullingMask;
            var timings = new FrameTiming[1];
            var cpu = new double[2]; var gpu = new double[2]; var samples = new int[2];
            Action<ScriptableRenderContext, Camera> beforeCamera = (_, camera) =>
            {
                if (camera == city.Camera) HideStopCaptureFleet(city, hiddenFleet, fleetScan);
            };
            RenderPipelineManager.beginCameraRendering += beforeCamera;
            try
            {
                // Keep fixture identity, power, emission and presentation stable.
                // The paired sample isolates real-light receiver/shadow work.
                for (int phase = 0; phase < 2; phase++)
                {
                    for (int i = 0; i < lights.Count; i++) lights[i].cullingMask = phase == 0 ? 0 : masks[i];
                    for (int frame = 0; frame < 36; frame++)
                    {
                        FrameTimingManager.CaptureFrameTimings();
                        yield return null;
                        if (frame < 12 || FrameTimingManager.GetLatestTimings(1, timings) == 0 ||
                            timings[0].gpuFrameTime <= 0d) continue;
                        cpu[phase] += timings[0].cpuFrameTime;
                        gpu[phase] += timings[0].gpuFrameTime;
                        samples[phase]++;
                    }
                }
            }
            finally
            {
                RenderPipelineManager.beginCameraRendering -= beforeCamera;
                for (int i = 0; i < lights.Count; i++) if (lights[i] != null) lights[i].cullingMask = masks[i];
            }
            if (samples[0] == 0 || samples[1] == 0)
                Debug.Log("CityBusStopLighting: GPU frame cost unavailable; the Editor returned no valid GPU timings.");
            else
                Debug.Log("CityBusStopLighting paired fixed view, direct-light/shadow work: off CPU=" +
                    (cpu[0] / samples[0]).ToString("F3") + " ms, GPU=" + (gpu[0] / samples[0]).ToString("F3") +
                    " ms; on CPU=" + (cpu[1] / samples[1]).ToString("F3") + " ms, GPU=" +
                    (gpu[1] / samples[1]).ToString("F3") + " ms. Editor samples are diagnostic, not a player FPS claim.");
        }

        private static void CaptureStopLightingPair(CityGameRoot city, StopLightingCase item, string name,
            Dictionary<Renderer, bool> hiddenFleet, List<Renderer> fleetScan)
        {
            var lights = item.Fixture.Lights;
            float[] powers = lights.Select(light => light.intensity).ToArray();
            for (int i = 0; i < lights.Count; i++) lights[i].intensity = 0f;
            try { CaptureStopLightingFrame(city, hiddenFleet, fleetScan, name + "-off"); }
            finally { for (int i = 0; i < lights.Count; i++) lights[i].intensity = powers[i]; }
            CaptureStopLightingFrame(city, hiddenFleet, fleetScan, name + "-on");
            foreach (var receiver in StopLightingReceivers(city, item))
                AssertStopLightingReceiverPatch(city.Camera, name, receiver.name, receiver.point);
            AssertStopBackPanelUniformity(city.Camera, item.Root, name);
        }

        private static void CaptureStopStripDetail(CityGameRoot city, StopLightingCase item,
            Dictionary<Renderer, bool> hiddenFleet, List<Renderer> fleetScan)
        {
            var pose = new Pose(city.Camera.transform.position, city.Camera.transform.rotation);
            try
            {
                Vector3 eye = item.Root.TransformPoint(new Vector3(1f, 1.6f, 3f));
                Vector3 target = item.Root.TransformPoint(new Vector3(-2.55f, 1.7f, .05f));
                city.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                city.Night.Atmosphere.RefreshStopShadows(city.Camera, 1f, true);
                CaptureStopLightingFrame(city, hiddenFleet, fleetScan, "01-lower-night-strip-detail");
            }
            finally
            {
                city.Camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
                city.Night.Atmosphere.RefreshStopShadows(city.Camera, 1f, true);
            }
        }

        private static void HideStopCaptureFleet(CityGameRoot city, Dictionary<Renderer, bool> hiddenFleet, List<Renderer> scan)
        {
            foreach (var vehicle in city.BusVehicles)
            {
                scan.Clear();
                // The director owns both its live actor and the deferred model
                // pool, including suspension geometry and newly built instances.
                vehicle.GetComponentsInChildren(true, scan);
                foreach (Renderer renderer in scan)
                {
                    if (!hiddenFleet.ContainsKey(renderer)) hiddenFleet.Add(renderer, renderer.forceRenderingOff);
                    renderer.forceRenderingOff = true;
                }
            }
        }

        private static void CaptureStopLightingFrame(CityGameRoot city, Dictionary<Renderer, bool> hiddenFleet,
            List<Renderer> fleetScan, string name)
        {
            // Renderer.enabled may be restored by presentation/capture code.
            // Force every current live or pooled body off independently now.
            HideStopCaptureFleet(city, hiddenFleet, fleetScan);
            CaptureCurrentCamera(city.Camera, StopLightingCaptureArea, name, 1920, 1080);
        }

        private static void AssertStopBackPanelUniformity(Camera camera, Transform root, string name)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", StopLightingCaptureArea);
            var before = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var after = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Assert.That(before.LoadImage(File.ReadAllBytes(Path.Combine(folder, name + "-off.png"))), Is.True);
                Assert.That(after.LoadImage(File.ReadAllBytes(Path.Combine(folder, name + "-on.png"))), Is.True);
                float left = Delta(-3.65f), center = Delta(-2.55f), right = Delta(-1.45f);
                Debug.Log("CityBusStopLighting " + name + " back-panel strip delta left/centre/right=" +
                    left.ToString("F4") + "/" + center.ToString("F4") + "/" + right.ToString("F4") + ".");
                Assert.That((left + right) * .5f, Is.GreaterThan(.003f), "Both sides of the back panel must receive real strip spill.");
                Assert.That(center, Is.LessThanOrEqualTo((left + right) * .5f * 1.8f + .002f),
                    "A continuous strip cannot produce an isolated bright central apex above the bench.");

                float Delta(float x)
                {
                    Vector3 viewport = camera.WorldToViewportPoint(root.TransformPoint(new Vector3(x, 1.55f, -.415f)));
                    Assert.That(viewport.z, Is.GreaterThan(0f));
                    Assert.That(viewport.x, Is.InRange(.035f, .965f));
                    Assert.That(viewport.y, Is.InRange(.035f, .965f));
                    const int radius = 10;
                    int px = Mathf.RoundToInt(viewport.x * before.width), py = Mathf.RoundToInt(viewport.y * before.height);
                    Color[] a = before.GetPixels(px - radius, py - radius, radius * 2, radius * 2);
                    Color[] b = after.GetPixels(px - radius, py - radius, radius * 2, radius * 2);
                    float sum = 0f;
                    for (int i = 0; i < a.Length; i++)
                        sum += Mathf.Max(0f, b[i].r + b[i].g + b[i].b - a[i].r - a[i].g - a[i].b);
                    return sum / a.Length;
                }
            }
            finally { Object.DestroyImmediate(before); Object.DestroyImmediate(after); }
        }

        private static void AssertStopLightingReceiverPatch(Camera camera, string name, string receiver, Vector3 point)
        {
            Vector3 viewport = camera.WorldToViewportPoint(point);
            Assert.That(viewport.z, Is.GreaterThan(0f), receiver);
            Assert.That(viewport.x, Is.InRange(.035f, .965f), receiver);
            Assert.That(viewport.y, Is.InRange(.035f, .965f), receiver);
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", StopLightingCaptureArea);
            var before = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var after = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Assert.That(before.LoadImage(File.ReadAllBytes(Path.Combine(folder, name + "-off.png"))), Is.True);
                Assert.That(after.LoadImage(File.ReadAllBytes(Path.Combine(folder, name + "-on.png"))), Is.True);
                const int radius = 10;
                int x = Mathf.RoundToInt(viewport.x * before.width), y = Mathf.RoundToInt(viewport.y * before.height);
                Color[] a = before.GetPixels(x - radius, y - radius, radius * 2, radius * 2);
                Color[] b = after.GetPixels(x - radius, y - radius, radius * 2, radius * 2);
                int brighter = 0;
                for (int i = 0; i < a.Length; i++)
                    if (b[i].r + b[i].g + b[i].b - a[i].r - a[i].g - a[i].b > 3f / 255f) brighter++;
                Debug.Log("CityBusStopLighting " + name + " " + receiver + ": " + brighter + " brighter pixels.");
                Assert.That(brighter, Is.GreaterThan(a.Length / 10), "The real platform source must visibly light its " + receiver + ".");
            }
            finally { Object.DestroyImmediate(before); Object.DestroyImmediate(after); }
        }

        private static IEnumerator AssertHomeStopLighting()
        {
            HomeInteriorRoot home = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                if (home != null && home.IsInitialized) break;
                yield return null;
            }
            Assert.That(home != null && home.IsInitialized, Is.True);
            Rect activation = home.BalconyLayout.BalconyCameraActivationBounds;
            home.Player.Motor.Teleport(new Vector3(activation.center.x, home.Layout.PlayerSpawn.y, activation.center.y));
            for (int frame = 0; frame < 5; frame++) yield return null;
            home.ExteriorAtmosphere.RefreshImmediate();
            Assert.That(home.ExteriorAtmosphere.IsBalconyVisibilityActive, Is.True);
            CityNightAtmosphere atmosphere = home.ExteriorAtmosphere.ExteriorLighting;
            atmosphere.RefreshImmediate();
            var fixtures = home.ExteriorView.GetComponentsInChildren<StopFixture>(true);
            Assert.That(fixtures.Length, Is.EqualTo(1), "The actual local-line stop is shared by City and the Home view.");
            AssertStopLightAssembly(fixtures[0].transform, fixtures[0], atmosphere);
            foreach (Collider collider in fixtures[0].GetComponentsInChildren<Collider>(true))
                Assert.That(collider.enabled, Is.False, "The balcony stop remains a passive exterior.");
            CaptureCurrentCamera(home.CameraFollow.GetComponent<Camera>(), StopLightingCaptureArea,
                "06-home-balcony-night", 1920, 1080);
        }
    }
}
