using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The end-to-end proof that was missing while four separate defects each
    /// broke ambient boarding on their own: the pedestrian navigation area
    /// instead of the road-inclusive one, the hero-only "seat opposite the
    /// driver" invariant, an unreachable transfer timeout, and a waiter that
    /// stopped its own bus short of the stop. Every one of them left the
    /// planners, the occupancy rules and the asset contracts passing, because
    /// nothing walked a passenger from the pavement into a seat and back out.
    /// </summary>
    public sealed class CityBusNpcPassengerPlayModeTests
    {
        private const float Step = 0.05f;

        // Budgets are game seconds, never frame counts. Time.deltaTime in a
        // batch run has been observed anywhere from 0.006 s to the 6.7 s
        // ceiling depending on how fast the frames come, so a frame budget is
        // meaningless; the frame cap is only a runaway guard. One lap of the
        // test loop is roughly 37 s: 100 m at 6 m/s plus two 10 s dwells.
        private const float PreloadSeconds = 200f;
        private const float WaiterSeconds = 150f;
        private const float BoardSeconds = 90f;
        private const float AlightSeconds = 200f;
        private const int FrameCap = 40000;

        [UnityTest]
        public IEnumerator SeatedPassengers_StayOnActualCushionsAcrossAnimationAndBusMotion()
        {
            // No city, boarding loop or directors: exercise the production
            // presentation and the actual passenger update order in isolation.
            var root = new GameObject("Bus seated mesh regression");
            var report = new SeatedMeshReport();
            var failures = new List<string>();
            string directory = Path.GetFullPath("Captures/BusNpcSeating");
            Directory.CreateDirectory(directory);
            Color ambient = RenderSettings.ambientLight;
            UnityEngine.Rendering.AmbientMode ambientMode = RenderSettings.ambientMode;
            bool fog = RenderSettings.fog;
            try
            {
                RenderSettings.fog = false;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.70f, .70f, .70f);
                Camera camera = new GameObject("Seated mesh camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform, false);
                camera.enabled = false;
                camera.nearClipPlane = .025f;
                camera.farClipPlane = 40f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.16f, .18f, .20f);
                camera.cullingMask = 1 << 31;
                Light light = new GameObject("Seated mesh light").AddComponent<Light>();
                light.transform.SetParent(root.transform, false);
                light.type = LightType.Directional;
                light.intensity = 1.5f;
                light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

                CityBusAssetRegistry bus = CityBusResources.Instantiate(root.transform);
                Assert.That(bus, Is.Not.Null);
                CityBusPresentation busPresentation = bus.GetComponent<CityBusPresentation>() ??
                    bus.gameObject.AddComponent<CityBusPresentation>();
                busPresentation.Initialize(bus);
                CityBusActor busActor = new GameObject("Seated mesh bus actor").AddComponent<CityBusActor>();
                busActor.transform.SetParent(root.transform, false);
                busActor.Initialize(bus.LocalBounds, bus.Dimensions);
                CityBusPlan route = CreateTwoStopRoute();
                busActor.PrepareSpawn(route, route.SpawnAnchors[0], 0x53454154u);
                busActor.BindPresentation(busPresentation);
                busActor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Quaternion suspensionInBus = Quaternion.Inverse(busActor.transform.rotation) *
                    busPresentation.SuspensionVisual.rotation;
                CityBusDriverAssetRegistry driver = CityBusDriverResources.Instantiate(root.transform);
                Assert.That(driver, Is.Not.Null);
                busPresentation.AttachDriver(driver);
                SetSeatedCaptureLayer(root);
                Renderer passengerCushions = bus.RendererBindings.Single(
                    binding => binding.SourceName == "INT_PassengerSeats").Renderer;
                Renderer driverCushion = bus.RendererBindings.Single(
                    binding => binding.SourceName == "INT_DriverSeat").Renderer;
                Assert.That(CityBusRidePlan.TryCreateSeatedPose(busActor,
                    CityBusActor.NpcSeatIndices[0], out Vector3 ridePosition,
                    out Quaternion rideRotation, out Transform seat), Is.True);
                var passengerFrame = new SeatedCushionFrame(seat, busActor.transform.rotation);
                var driverFrame = new SeatedCushionFrame(bus.DriverSeatAnchor, busActor.transform.rotation);

                foreach (CityPedestrianArchetype archetype in CityPedestrianResources.AllArchetypes)
                {
                    if (!archetype.CanRideBus) continue;
                    busActor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    busPresentation.SuspensionVisual.rotation = busActor.transform.rotation * suspensionInBus;
                    var actorObject = new GameObject(archetype.DesignId + " seated mesh actor");
                    actorObject.layer = CityPedestrianCollision.LayerIndex;
                    actorObject.transform.SetParent(root.transform, false);
                    CityPedestrianActor actor = actorObject.AddComponent<CityPedestrianActor>();
                    CityPedestrianPlan plan = CreatePedestrianPlan(Vector3.zero, Vector3.forward);
                    actor.Initialize(new AlwaysWalkableArea(), plan.AgentRadius);
                    Assert.That(CityPedestrianResources.TryInstantiate(
                        Resources.Load<GameObject>(archetype.PrefabResourcePath), root.transform,
                        out CityPedestrianAssetRegistry npc), Is.True, archetype.DesignId);
                    CityPedestrianPresentation presentation = npc.GetComponent<CityPedestrianPresentation>() ??
                        npc.gameObject.AddComponent<CityPedestrianPresentation>();
                    presentation.Initialize(npc);
                    npc.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    actor.PrepareSpawn(plan, plan.SpawnAnchors[0], 1, 1f, 1f, 0f, 0, 1u);
                    actor.BindPresentation(presentation);
                    Quaternion standingModelRotation = npc.ModelRoot.localRotation;
                    actor.transform.SetPositionAndRotation(
                        busActor.transform.TransformPoint(ridePosition), busActor.transform.rotation * rideRotation);
                    SetSeatedCaptureLayer(actorObject);
                    Renderer[] support = SeatedSupportRenderers(npc.Renderers);
                    Assert.That(support.Length, Is.EqualTo(3), archetype.DesignId + " pelvis and both thighs");
                    Assert.That(Vector3.Distance(busPresentation.CabinUp, passengerFrame.Up), Is.LessThan(.0001f));
                    Assert.That(actor.BeginSeatedRide(seat, archetype.SeatedRide, busPresentation.CabinUp), Is.True);

                    // Must already be seated before either the next tick or a
                    // frame yield. A standing graph can have a correct pelvis
                    // anchor while its visible thighs still cut the cushion.
                    MeasurePassengerSeat(archetype, npc, actor, passengerFrame, passengerCushions,
                        support, "initial", report, failures);
                    yield return null;
                    CaptureSeatedMesh(camera, bus, passengerFrame, directory, archetype.DesignId + "-level");

                    const int samples = 16;
                    for (int phase = 1; phase <= samples; phase++)
                    {
                        actor.Advance(npc.SitClip.length / samples);
                        // Production order: pedestrians tick, bus advances,
                        // passenger controller updates the independent root.
                        // Include sprung body tilt as well as road/root tilt.
                        float angle = phase * Mathf.PI * 2f / samples;
                        busActor.transform.SetPositionAndRotation(
                            new Vector3(phase * .31f, .12f * Mathf.Sin(angle), -phase * .17f),
                            Quaternion.Euler(2f * Mathf.Sin(angle), phase * 3f, -2f * Mathf.Cos(angle)));
                        busPresentation.SuspensionVisual.rotation = busActor.transform.rotation * Quaternion.Euler(
                            CityBusPresentation.MaximumSuspensionPitch * Mathf.Cos(angle), 0f,
                            CityBusPresentation.MaximumSuspensionRoll * Mathf.Sin(angle)) * suspensionInBus;
                        actor.SetRidePose(busActor.transform.TransformPoint(ridePosition),
                            busActor.transform.rotation * rideRotation);
                        Assert.That(Vector3.Distance(busPresentation.CabinUp, passengerFrame.Up), Is.LessThan(.0001f), "Production cabin normal follows the drawn cushion.");
                        MeasurePassengerSeat(archetype, npc, actor, passengerFrame, passengerCushions,
                            support, "sit-" + phase, report, failures);
                    }

                    yield return null; // Refresh GPU skinning for the tilted view.
                    CaptureSeatedMesh(camera, bus, passengerFrame, directory, archetype.DesignId + "-tilted");
                    actor.ResumeRoaming(1);
                    actor.Advance(0f);
                    if (presentation.IsSeated ||
                        Quaternion.Angle(npc.ModelRoot.localRotation, standingModelRotation) > .01f)
                        failures.Add(archetype.DesignId + ": standing retained the seat's model tilt.");
                    File.WriteAllText(Path.Combine(directory, "report.json"), JsonUtility.ToJson(report, true));
                    Object.DestroyImmediate(actorObject);
                }

                Renderer[] driverSupport = SeatedSupportRenderers(driver.Renderers);
                Assert.That(driverSupport.Length, Is.EqualTo(3));
                for (int sample = 0; sample < 2; sample++)
                {
                    busActor.transform.SetPositionAndRotation(new Vector3(8f, 1f, -5f),
                        Quaternion.Euler(sample * 3f, 38f, sample * -3f));
                    busPresentation.SuspensionVisual.rotation = busActor.transform.rotation *
                        Quaternion.Euler(sample * .8f, 0f, sample) * suspensionInBus;
                    busPresentation.DriverPresentation.ApplyPose(sample, sample, 0f);
                    MeasureSeatedSurface("bus-driver", sample == 0 ? "wheel" : "button-tilted",
                        driverFrame, driverCushion, driverSupport, report, failures);
                    yield return null;
                    CaptureSeatedMesh(camera, bus, driverFrame, directory,
                        sample == 0 ? "driver-wheel" : "driver-button-tilted");
                }
            }
            finally
            {
                File.WriteAllText(Path.Combine(directory, "report.json"), JsonUtility.ToJson(report, true));
                RenderSettings.ambientLight = ambient;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.fog = fog;
                Object.DestroyImmediate(root);
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        private static void MeasurePassengerSeat(CityPedestrianArchetype archetype,
            CityPedestrianAssetRegistry npc, CityPedestrianActor actor, SeatedCushionFrame frame,
            Renderer cushion, Renderer[] support, string phase, SeatedMeshReport report,
            List<string> failures)
        {
            Vector3 expected = frame.Seat.position + frame.Up * archetype.SeatedRide.SeatLift -
                Vector3.ProjectOnPlane(actor.transform.forward, frame.Up).normalized * archetype.SeatedRide.SeatBackOffset;
            float error = Vector3.Distance(npc.PelvisAnchor.position, expected);
            if (error > .001f)
                failures.Add($"{archetype.DesignId}/{phase}: pelvis missed live seat by {error:F4} m.");
            MeasureSeatedSurface(archetype.DesignId, phase, frame, cushion, support, report, failures);
        }

        private static Renderer[] SeatedSupportRenderers(IReadOnlyList<Renderer> renderers)
        {
            return renderers.Where(renderer => renderer != null &&
                (renderer.name == "GEO_Pelvis" || renderer.name == "CLO_Seat" ||
                 renderer.name == "GEO_Thigh.L" || renderer.name == "GEO_Thigh.R" ||
                 renderer.name == "CLO_Thigh.L" || renderer.name == "CLO_Thigh.R")).ToArray();
        }

        private static void SetSeatedCaptureLayer(GameObject root)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.gameObject.layer = 31;
                if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
            }
        }

        private static void MeasureSeatedSurface(string design, string phase, SeatedCushionFrame frame,
            Renderer cushion, Renderer[] support, SeatedMeshReport report, List<string> failures)
        {
            var baked = new Mesh();
            try
            {
                Mesh seatMesh = SeatedMesh(cushion, baked);
                ReadSeatedMesh(seatMesh, out Vector3[] cushionVertices, out _);
                Vector3[] seatVertices = cushionVertices.Select(vertex =>
                    frame.Coordinates(cushion.transform.TransformPoint(vertex))).ToArray();
                // Find the actual top-face corners near this seat, excluding
                // the combined mesh's backrest and all neighboring cushions.
                Vector3[] top = seatVertices.Where(vertex => Mathf.Abs(vertex.y) < .01f &&
                    Mathf.Abs(vertex.x) < .36f && Mathf.Abs(vertex.z) < .36f).ToArray();
                Assert.That(top.Length, Is.GreaterThanOrEqualTo(4),
                    $"{design} actual cushion top; anchor={frame.Seat.position:F4}, normal={frame.Up:F4}, " +
                    $"meshScale={cushion.transform.lossyScale:F4}, bounds={cushion.bounds}; nearest seat-local vertices=" +
                    string.Join(", ", seatVertices.OrderBy(vertex => vertex.sqrMagnitude)
                        .Take(6).Select(vertex => vertex.ToString("F4"))));
                float surface = top.Average(vertex => vertex.y);
                Rect footprint = Rect.MinMaxRect(top.Min(vertex => vertex.x), top.Min(vertex => vertex.z),
                    top.Max(vertex => vertex.x), top.Max(vertex => vertex.z));
                Assert.That(footprint.width, Is.InRange(.48f, .59f));
                Assert.That(footprint.height, Is.InRange(.44f, .54f));
                float minimum = float.PositiveInfinity;
                int clippedTriangles = 0;
                foreach (Renderer renderer in support)
                {
                    Mesh mesh = SeatedMesh(renderer, baked);
                    ReadSeatedMesh(mesh, out Vector3[] meshVertices, out int[] triangles);
                    Vector3[] vertices = meshVertices.Select(vertex =>
                        frame.Coordinates(renderer.transform.TransformPoint(vertex))).ToArray();
                    for (int index = 0; index < triangles.Length; index += 3)
                    {
                        var polygon = new List<Vector3>
                        {
                            vertices[triangles[index]], vertices[triangles[index + 1]], vertices[triangles[index + 2]]
                        };
                        // Clip the surface, not just its vertices: a large
                        // triangle may cross a cushion with all corners outside.
                        // This removes lower legs, knees and coat hems beyond
                        // the actual seat support area from the measurement.
                        polygon = ClipSeatPolygon(polygon, 0, footprint.xMin + .002f, true);
                        polygon = ClipSeatPolygon(polygon, 0, footprint.xMax - .002f, false);
                        polygon = ClipSeatPolygon(polygon, 2, footprint.yMin + .002f, true);
                        polygon = ClipSeatPolygon(polygon, 2, footprint.yMax - .002f, false);
                        if (polygon.Count < 3) continue;
                        clippedTriangles++;
                        foreach (Vector3 point in polygon) minimum = Mathf.Min(minimum, point.y - surface);
                    }
                }

                report.samples.Add(new SeatedMeshSample
                {
                    design = design, phase = phase, minimum_clearance_m = minimum,
                    support_triangles = clippedTriangles, cushion_width_m = footprint.width,
                    cushion_depth_m = footprint.height, seat = frame.Seat.position, seat_up = frame.Up
                });
                if (clippedTriangles == 0 || float.IsInfinity(minimum))
                    failures.Add(design + "/" + phase + ": no hip/thigh surface overlaps the cushion.");
                else if (minimum < -.012f || minimum > .03f)
                    failures.Add($"{design}/{phase}: visible support clearance {minimum:F4} m; expected -0.012 to 0.030 m.");
            }
            finally { Object.DestroyImmediate(baked); }
        }

        private static Mesh SeatedMesh(Renderer renderer, Mesh baked)
        {
            if (renderer is SkinnedMeshRenderer skin)
            {
                skin.BakeMesh(baked, true); // Retain the imported FBX unit factor.
                return baked;
            }
            return renderer.GetComponent<MeshFilter>().sharedMesh;
        }

        private static void ReadSeatedMesh(Mesh mesh, out Vector3[] vertices, out int[] triangles)
        {
#if UNITY_EDITOR
            // Passive production bus meshes intentionally discard their CPU
            // copy. Editor readback inspects the imported geometry without
            // changing Read/Write or allocating a persistent production copy.
            using (Mesh.MeshDataArray data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh))
            using (var positions = new Unity.Collections.NativeArray<Vector3>(
                       data[0].vertexCount, Unity.Collections.Allocator.Temp))
            {
                data[0].GetVertices(positions);
                vertices = positions.ToArray();
                var combined = new List<int>();
                for (int submesh = 0; submesh < data[0].subMeshCount; submesh++)
                {
                    UnityEngine.Rendering.SubMeshDescriptor descriptor = data[0].GetSubMesh(submesh);
                    Assert.That(descriptor.topology, Is.EqualTo(MeshTopology.Triangles), mesh.name);
                    using (var indices = new Unity.Collections.NativeArray<int>(
                               descriptor.indexCount, Unity.Collections.Allocator.Temp))
                    {
                        data[0].GetIndices(indices, submesh);
                        combined.AddRange(indices.ToArray());
                    }
                }
                triangles = combined.ToArray();
            }
#else
            vertices = mesh.vertices;
            triangles = mesh.triangles;
#endif
        }

        private readonly struct SeatedCushionFrame
        {
            public readonly Transform Seat;
            private readonly Quaternion orientationInAnchor;
            private Quaternion Orientation => Seat.rotation * orientationInAnchor;
            public Vector3 Up => Orientation * Vector3.up;

            public SeatedCushionFrame(Transform seat, Quaternion busOrientation)
            {
                Seat = seat;
                // The imported FBX anchor's +Y is longitudinal. Calibrate the
                // neutral vehicle basis once, then carry it with the live seat
                // through road slope and suspension. Do not assume anchor.up.
                orientationInAnchor = Quaternion.Inverse(seat.rotation) * busOrientation;
            }

            public Vector3 Coordinates(Vector3 point)
            {
                return Quaternion.Inverse(Orientation) * (point - Seat.position);
            }
        }

        internal static List<Vector3> ClipSeatPolygon(List<Vector3> polygon, int axis, float edge, bool above)
        {
            var clipped = new List<Vector3>();
            if (polygon.Count == 0) return clipped;
            Vector3 previous = polygon[polygon.Count - 1];
            bool previousInside = above ? previous[axis] >= edge : previous[axis] <= edge;
            foreach (Vector3 current in polygon)
            {
                bool inside = above ? current[axis] >= edge : current[axis] <= edge;
                if (inside != previousInside)
                {
                    float t = (edge - previous[axis]) / (current[axis] - previous[axis]);
                    clipped.Add(Vector3.LerpUnclamped(previous, current, t));
                }
                if (inside) clipped.Add(current);
                previous = current;
                previousInside = inside;
            }
            return clipped;
        }

        private static void CaptureSeatedMesh(Camera camera, CityBusAssetRegistry bus, SeatedCushionFrame frame,
            string directory, string name)
        {
            // Stay inside the opposite window, looking from the aisle across
            // the cushion edge: its top and the occupant's hip are both visible.
            Vector3 right = bus.transform.right;
            Vector3 forward = bus.transform.forward;
            Transform seat = frame.Seat;
            float side = Mathf.Sign(Vector3.Dot(seat.position - bus.transform.position, right));
            camera.transform.position = seat.position - right * side * 1.15f +
                forward * .48f + frame.Up * .50f;
            camera.transform.LookAt(seat.position + frame.Up * .30f, frame.Up);
            camera.fieldOfView = 78f;
            camera.aspect = 4f / 3f;
            var target = new RenderTexture(1280, 960, 24);
            var pixels = new Texture2D(1280, 960, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0f, 0f, 1280f, 960f), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(target);
            }
        }

        [System.Serializable]
        private sealed class SeatedMeshReport
        {
            public List<SeatedMeshSample> samples = new List<SeatedMeshSample>();
        }

        [System.Serializable]
        private sealed class SeatedMeshSample
        {
            public string design, phase;
            public float minimum_clearance_m, cushion_width_m, cushion_depth_m;
            public int support_triangles;
            public Vector3 seat, seat_up;
        }

        [UnityTest]
        public IEnumerator AmbientPassenger_BoardsRidesAndAlightsAtALaterStop()
        {

            float previousTimeScale = Time.timeScale;
            float previousCaptureDeltaTime = Time.captureDeltaTime;
            GameObject root = null;
            CityBusDirector director = null;
            CityPedestrianDirector pedestrians = null;
            CityBusNpcPassengerController passengers = null;
            try
            {
                // One fixed step for every subsystem. Batch-mode frames come
                // at whatever rate the machine allows — Time.deltaTime was
                // seen at 0.006 s on one run and pinned to the 6.7 s ceiling
                // on another — and the bus, the walkers and the transfer
                // budget must all be measured against the same clock or a
                // hold expires under a passenger who is still walking.
                Time.timeScale = 1f;
                Time.captureDeltaTime = Step;
                root = new GameObject("City Bus NPC Passenger Root");
                var walkableArea = new AlwaysWalkableArea();
                CreateGround(root.transform);

                GameObject cameraObject = new GameObject("NPC Ride Camera");
                cameraObject.transform.SetParent(root.transform, false);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                GameObject uiObject = new GameObject("NPC Ride UI");
                uiObject.transform.SetParent(root.transform, false);
                InteractionPromptView prompt =
                    uiObject.AddComponent<InteractionPromptView>();

                // The hero stands far away: ambient waiters may only be
                // activated outright where the stop is already fog-hidden, and
                // an aboard hero would change the recycle and obstacle rules.
                PlayerRuntime player = PlayerFactory.Create(
                    root.transform,
                    new Vector3(0f, PlayerFactory.GroundedRootOffset, 300f),
                    camera,
                    walkableArea,
                    prompt);

                CityBusPlan route = CreateTwoStopRoute();
                Transform pool =
                    new GameObject("NPC Ride Bus Pool").transform;
                pool.SetParent(root.transform, false);
                CityBusAssetRegistry registry =
                    CityBusResources.Instantiate(pool);
                Assert.That(
                    registry,
                    Is.Not.Null,
                    "The production bus prefab must be available.");
                CityBusPresentation presentation =
                    registry.GetComponent<CityBusPresentation>();
                if (presentation == null)
                {
                    presentation = registry.gameObject
                        .AddComponent<CityBusPresentation>();
                }

                presentation.Initialize(registry);
                presentation.gameObject.SetActive(false);

                GameObject actorObject = new GameObject("NPC Ride Bus Actor");
                actorObject.layer = CityBusCollision.LayerIndex;
                actorObject.transform.SetParent(root.transform, false);
                CityBusActor actor =
                    actorObject.AddComponent<CityBusActor>();
                actor.Initialize(registry.LocalBounds, registry.Dimensions);

                director = root.AddComponent<CityBusDirector>();
                director.Initialize(
                    route,
                    actor,
                    presentation,
                    player.GameObject.transform,
                    null,
                    pool,
                    () => 0f);
                director.enabled = false;

                actor.PrepareSpawn(route, route.SpawnAnchors[0], 0x42555350u);
                actor.BindPresentation(presentation);
                AdvanceActorUntil(
                    actor,
                    () => actor.ServiceOrdinal == 1 && actor.DoorsFullyOpen);
                Assert.That(
                    actor.DoorsFullyOpen,
                    Is.True,
                    "The bus must reach its first stop with open doors.");

                // Take the wait slot from the real door dock rather than
                // guessing a side, then build a pedestrian graph beside it.
                Assert.That(
                    CityBusRidePlan.TryCreate(
                        actor,
                        walkableArea,
                        new Vector3(
                            0f,
                            CityBusNpcPassengerController
                                .PassengerPelvisHeight,
                            0f),
                        CityPedestrianPlanner.AgentRadius,
                        CityBusPassengerDoor.Front,
                        null,
                        CityBusActor.NpcSeatIndices[0],
                        0f,
                        false,
                        out CityBusRidePlan probe),
                    Is.True,
                    "An ambient seat must produce a transfer plan; requiring " +
                    "the hero's opposite-driver side here rejects most of " +
                    "the cabin.");

                Vector3 waitSlot = probe.EntryPose.RootPosition;
                Vector3 along = actor.transform.forward;
                CityPedestrianPlan pedestrianPlan =
                    CreatePedestrianPlan(waitSlot, along);
                pedestrians = CityPedestrianFactory.Create(
                    root.transform,
                    pedestrianPlan,
                    player.GameObject.transform,
                    walkableArea,
                    CityPedestrianPopulationProfile.City,
                    () => false);

                var waitPlan = new CityBusStopWaitPlan(new[]
                {
                    new CityBusStopWaitPoint(
                        0,
                        route.Stops[0].Id,
                        0,
                        -along,
                        new[] { waitSlot },
                        new[] { 0f, 4f, 8f })
                });
                passengers = CityBusNpcPassengerController.Create(
                    director,
                    pedestrians,
                    waitPlan,
                    walkableArea,
                    null,
                    player.GameObject.transform,
                    0x42555350,
                    () => false);

                // Every subsystem runs on the same clock: the pedestrian and
                // passenger directors advance themselves from LateUpdate, and
                // only the bus actor needs driving because its own director is
                // switched off. Disabling either director would call its
                // OnDisable and shut it down for good.
                Assert.That(passengers.IsInitialized, Is.True);

                // The bus was already running when the controller appeared, so
                // it seats its spawn preload first. Let that clear, otherwise
                // a full cabin legitimately refuses the waiter under test.
                // One frame first, so the controller's own LateUpdate can
                // seat its spawn preload before the wait for it to clear.
                yield return null;
                float elapsed = 0f;
                for (int frame = 0;
                     frame < FrameCap &&
                     elapsed < PreloadSeconds &&
                     (actor.NpcOccupantCount != 0 ||
                      passengers.PassengerCount != 0);
                     frame++)
                {
                    actor.Advance(
                        Time.deltaTime,
                        CityBusObstacleState.Clear,
                        0f);
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                Assert.That(
                    actor.NpcOccupantCount,
                    Is.Zero,
                    "Preloaded passengers must leave at their own stop. " +
                    Describe(actor, passengers));

                elapsed = 0f;
                for (int frame = 0;
                     frame < FrameCap &&
                     elapsed < WaiterSeconds &&
                     FindWaitingWalker(pedestrians) == null;
                     frame++)
                {
                    actor.Advance(
                        Time.deltaTime,
                        CityBusObstacleState.Clear,
                        0f);
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                CityPedestrianActor walker = FindWaitingWalker(pedestrians);
                Assert.That(
                    walker,
                    Is.Not.Null,
                    "A stop beyond the fog band must receive a waiter. " +
                    Describe(actor, passengers));

                CityPedestrianArchetype archetype =
                    CityPedestrianDirector.GetActorArchetype(walker);
                Assert.That(archetype, Is.Not.Null);
                Assert.That(
                    archetype.CanRideBus,
                    Is.True,
                    "Only a design that declares a seated ride may wait.");

                elapsed = 0f;
                for (int frame = 0;
                     frame < FrameCap &&
                     elapsed < BoardSeconds &&
                     walker.MotionState !=
                         CityPedestrianMotionState.Riding;
                     frame++)
                {
                    actor.Advance(
                        Time.deltaTime,
                        CityBusObstacleState.Clear,
                        0f);
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                Assert.That(
                    walker.MotionState,
                    Is.EqualTo(CityPedestrianMotionState.Riding),
                    "The waiter never reached a seat. Every ambient boarding " +
                    "defect so far has ended exactly here while the planners " +
                    "and asset contracts stayed green. " +
                    Describe(actor, passengers) +
                    $" walker={walker.MotionState}");
                Assert.That(actor.NpcOccupantCount, Is.EqualTo(1));
                Assert.That(actor.HasPlayerPassenger, Is.False);
                Assert.That(
                    actor.IsSeatOccupied(CityBusActor.PlayerSeatIndex),
                    Is.False,
                    "Seat 07 stays reserved for the hero.");
                Assert.That(
                    CountOccupiedAmbientSeats(actor),
                    Is.EqualTo(1),
                    "The passenger holds exactly one ambient seat.");
                Assert.That(
                    walker.Presentation.IsSeated,
                    Is.True,
                    "A riding walker plays its authored seated loop.");
                Assert.That(
                    IsInsideCabin(actor, registry, walker.Position),
                    Is.True,
                    "A seated passenger must be inside the body it rides in.");

                int boardedOrdinal = actor.ServiceOrdinal;
                elapsed = 0f;
                for (int frame = 0;
                     frame < FrameCap &&
                     elapsed < AlightSeconds &&
                     (walker.IsAttachedToVehicle ||
                      actor.NpcOccupantCount != 0);
                     frame++)
                {
                    actor.Advance(
                        Time.deltaTime,
                        CityBusObstacleState.Clear,
                        0f);
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                Assert.That(
                    walker.IsAttachedToVehicle,
                    Is.False,
                    "The passenger never got off again. " +
                    Describe(actor, passengers) +
                    $" walker={walker.MotionState}");
                Assert.That(
                    actor.ServiceOrdinal,
                    Is.GreaterThan(boardedOrdinal),
                    "A passenger may only leave at a later stop.");
                Assert.That(
                    walker.MotionState,
                    Is.EqualTo(CityPedestrianMotionState.Walking),
                    "An alighted walker rejoins ordinary roaming.");
                Assert.That(
                    walker.Presentation == null ||
                    !walker.Presentation.IsSeated,
                    Is.True);
                Assert.That(
                    IsInsideCabin(actor, registry, walker.Position),
                    Is.False,
                    "An alighted walker stands outside the bus.");
                Assert.That(
                    actor.HasServiceHold,
                    Is.False,
                    "A finished transfer hands its dwell hold back; a leaked " +
                    "one seals the doors at every later stop.");
            }
            finally
            {
                if (passengers != null)
                {
                    passengers.Shutdown();
                }

                if (pedestrians != null)
                {
                    pedestrians.Shutdown();
                }

                if (director != null)
                {
                    director.Shutdown();
                }

                if (root != null)
                {
                    Object.Destroy(root);
                }

                Time.captureDeltaTime = previousCaptureDeltaTime;
                Time.timeScale = previousTimeScale;
            }
        }

        private static string Describe(
            CityBusActor actor,
            CityBusNpcPassengerController passengers)
        {
            return $"[bus {actor.MotionState} stop={actor.CurrentStopIndex} " +
                   $"ordinal={actor.ServiceOrdinal} " +
                   $"doorsOpen={actor.DoorsFullyOpen} " +
                   $"hold={actor.HasServiceHold} " +
                   $"npc={actor.NpcOccupantCount} | tracked=" +
                   $"{passengers.TrackedCount} waiting={passengers.WaiterCount}" +
                   $" aboard={passengers.PassengerCount} dt={Time.deltaTime:F3}]";
        }

        private static CityPedestrianActor FindWaitingWalker(
            CityPedestrianDirector pedestrians)
        {
            IReadOnlyList<CityPedestrianActor> actors = pedestrians.Actors;
            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor candidate = actors[index];
                if (candidate.IsSpawned &&
                    (candidate.MotionState ==
                         CityPedestrianMotionState.WaitingAtStop ||
                     candidate.MotionState ==
                         CityPedestrianMotionState.ApproachingStop))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// The controller owns its passenger records privately, so occupancy
        /// is read back through the cabin itself rather than through a handle
        /// a test has no business holding.
        /// </summary>
        private static int CountOccupiedAmbientSeats(CityBusActor actor)
        {
            int count = 0;
            IReadOnlyList<int> seats = CityBusActor.NpcSeatIndices;
            for (int index = 0; index < seats.Count; index++)
            {
                if (actor.IsSeatOccupied(seats[index]))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsInsideCabin(
            CityBusActor actor,
            CityBusAssetRegistry registry,
            Vector3 position)
        {
            Vector3 local = actor.transform.InverseTransformPoint(position);
            Bounds bounds = registry.LocalBounds;
            return Mathf.Abs(local.x) <= bounds.extents.x &&
                   Mathf.Abs(local.z) <= bounds.extents.z;
        }

        private static void AdvanceActorUntil(
            CityBusActor actor,
            System.Func<bool> predicate)
        {
            for (int guard = 0; guard < 4000 && !predicate(); guard++)
            {
                actor.Advance(Step, CityBusObstacleState.Clear, 0f);
            }
        }

        private static CityPedestrianPlan CreatePedestrianPlan(
            Vector3 slot,
            Vector3 along)
        {
            Vector3 planar = new Vector3(along.x, 0f, along.z).normalized;
            var nodes = new List<CityPedestrianNode>
            {
                new CityPedestrianNode("npc-stop-node", slot, false),
                new CityPedestrianNode(
                    "npc-lane-node",
                    slot + (planar * 6f),
                    false),
                new CityPedestrianNode(
                    "npc-far-node",
                    slot + (planar * 12f),
                    false)
            };
            var links = new List<CityPedestrianLink>
            {
                new CityPedestrianLink(
                    "npc-link-a",
                    0,
                    1,
                    CityPedestrianLinkKind.Sidewalk),
                new CityPedestrianLink(
                    "npc-link-b",
                    1,
                    2,
                    CityPedestrianLinkKind.Sidewalk)
            };
            var anchors = new List<CityPedestrianSpawnAnchor>
            {
                new CityPedestrianSpawnAnchor(
                    "npc-anchor",
                    slot + (planar * 6f),
                    0,
                    1)
            };
            var rectangles = new List<Rect>
            {
                Rect.MinMaxRect(
                    slot.x - 60f,
                    slot.z - 60f,
                    slot.x + 60f,
                    slot.z + 60f)
            };
            return new CityPedestrianPlan(
                91,
                37,
                0x50454431u,
                CityPedestrianPlanner.AgentRadius,
                nodes,
                links,
                anchors,
                rectangles);
        }

        private static CityBusPlan CreateTwoStopRoute()
        {
            Vector3 start = new Vector3(
                0f,
                CityStreetSurfacePlanner.RoadTop,
                0f);
            Vector3 east = start + (Vector3.right * 30f);
            Vector3 southEast = east + (Vector3.back * 20f);
            Vector3 southWest = southEast + (Vector3.left * 30f);
            var edge = new RoadEdge(
                new Vector2Int(0, 0),
                new Vector2Int(0, 1));
            var samples = new List<CityBusPathSample>
            {
                new CityBusPathSample(start, Vector3.right, 0f),
                new CityBusPathSample(east, Vector3.back, 30f),
                new CityBusPathSample(southEast, Vector3.left, 50f),
                new CityBusPathSample(southWest, Vector3.forward, 80f),
                new CityBusPathSample(start, Vector3.right, 100f)
            };
            var clearance = new CityBusClearanceResult(
                true,
                CityBusClearanceFailureKind.None,
                -1,
                default,
                CityBusDesignVehicle.Default.ClearanceMargin);
            var nodes = new List<CityBusRouteNode>
            {
                new CityBusRouteNode(
                    "npc-ride-node",
                    start,
                    Vector3.right,
                    edge,
                    edge.A,
                    edge.B,
                    new[] { 0 })
            };
            var links = new List<CityBusRouteLink>
            {
                new CityBusRouteLink(
                    "npc-ride-link",
                    0,
                    0,
                    CityBusRouteLinkKind.Straight,
                    edge.B,
                    samples,
                    float.PositiveInfinity,
                    clearance)
            };
            var anchors = new List<CityBusSpawnAnchor>
            {
                new CityBusSpawnAnchor(
                    "npc-ride-anchor",
                    0,
                    0f,
                    start,
                    Vector3.right,
                    edge)
            };
            var stops = new List<CityBusStopDescriptor>
            {
                CreateStop("npc-ride-stop-a", start, 8f, edge),
                CreateStop("npc-ride-stop-b", start, 48f, edge)
            };
            return new CityBusPlan(
                91,
                37,
                0x42555332u,
                1.5f,
                CityBusDesignVehicle.Default,
                nodes,
                links,
                anchors,
                stops,
                new List<CityBusClearanceFailure>(),
                1,
                1);
        }

        private static CityBusStopDescriptor CreateStop(
            string id,
            Vector3 start,
            float distance,
            RoadEdge edge)
        {
            Vector3 position = distance <= 30f
                ? start + (Vector3.right * distance)
                : start + (Vector3.right * 30f) +
                  (Vector3.back * (distance - 30f));
            Vector3 forward = distance <= 30f
                ? Vector3.right
                : Vector3.back;
            return new CityBusStopDescriptor(
                id,
                $"{id}-shelter",
                position + (Vector3.forward * 3f),
                0,
                distance,
                position,
                forward,
                edge);
        }

        private static void CreateGround(Transform parent)
        {
            GameObject ground = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            ground.name = "NPC Ride Test Ground";
            ground.transform.SetParent(parent, false);
            // Its TOP must be the height this scene actually lives at, and
            // that is the ROAD, not the sidewalk. Everything synthetic here
            // - the route, the bus, and therefore every door dock derived
            // from it - is built on CityStreetSurfacePlanner.RoadTop, while
            // this slab used to be raised to SidewalkTop. Six centimetres of
            // difference buried every dock, and the pedestrian director's
            // spawn-clearance capsule correctly refused to materialise a
            // waiter inside terrain: its lowest point sat 12 mm under the
            // slab. That single line is why this test "failed on any code"
            // for as long as it existed. In the real city the road and the
            // pavement are separate meshes at their own heights, so nothing
            // there was ever wrong.
            ground.transform.position = new Vector3(
                10f,
                CityStreetSurfacePlanner.RoadTop * 0.5f,
                -10f);
            ground.transform.localScale = new Vector3(
                160f,
                CityStreetSurfacePlanner.RoadTop,
                160f);
        }

        private sealed class AlwaysWalkableArea : IWalkableArea
        {
            public bool Contains(Vector3 position, float radius = 0f)
            {
                return true;
            }

            public Vector3 Constrain(
                Vector3 currentPosition,
                Vector3 desiredPosition,
                float radius = 0f)
            {
                return desiredPosition;
            }

            public Vector3 ClosestPoint(
                Vector3 position,
                float radius = 0f)
            {
                return position;
            }
        }
    }
}
