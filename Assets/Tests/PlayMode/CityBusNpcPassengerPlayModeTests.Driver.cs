using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CityBusRemodelAssetSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            if (System.Environment.GetEnvironmentVariable("BAR_PROMENADE_REBUILD_BUS") != "1") return;
            foreach (string owner in new[] { "DefaultNpcAssetSetup", "CityBusAssetSetup" })
                System.Type.GetType("BarPromenade.Editor." + owner + ", BarPromenade.Editor", true)
                    .GetMethod("BuildOrThrow", System.Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class CityBusNpcPassengerPlayModeTests
    {
        [UnityTest, PrebuildSetup(typeof(CityBusRemodelAssetSetup))]
        public IEnumerator Driver_SitsOnTheDrawnSeatAndWorksWheelAndButtonWithoutCrossingLimbs()
        {
            var root = new GameObject("Production cab contact survey");
            var baked = new Mesh();
            var issues = new List<string>();
            try
            {
                var bus = CityBusResources.Instantiate(root.transform);
                var view = bus.GetComponent<CityBusPresentation>() ?? bus.gameObject.AddComponent<CityBusPresentation>();
                view.Initialize(bus);
                var driver = CityBusDriverRig.Create(root.transform);
                view.AttachDriver(driver);
                SetSeatedCaptureLayer(root);
                yield return null;
                var seat = new SeatedCushionFrame(bus.DriverSeatAnchor, view.transform.rotation);
                var support = SeatedSupportRenderers(driver.Renderers);
                var seatMesh = BusPart("INT_DriverSeat");
                var pedals = BusPart("INT_ControlHardware");
                var wheel = BusPart("INT_SteeringWheel");
                var button = BusPart("INT_DoorButton");
                var report = new SeatedMeshReport();
                // A visible point on the real rim, above its hub in the neutral cab.
                // A turn must move the actual mesh through lock, independently of where
                // the driver regrips it. The left/right socket markers rotate with it.
                Vector3 top = DriverWorldMesh(wheel, baked, out _).OrderByDescending(p =>
                    Vector3.Dot(p - bus.SteeringWheelPivot.position, seat.Up)).First();
                Vector3 topInWheel = wheel.transform.InverseTransformPoint(top);
                Quaternion neutralWheelRotation = bus.SteeringWheelPivot.localRotation;
                foreach (var phase in new (string name, float steer, bool press)[] { ("straight", 0f, false),
                    ("left-lock", -CityBusPresentation.MaximumSteeringAngle, false),
                    ("right-lock", CityBusPresentation.MaximumSteeringAngle, false), ("button", 0f, true) })
                {
                    view.SetDriverDoorSample(phase.press ? CityBusDriverDoorTimeline.SampleDwell(0f, CityBusActor.DwellDuration, CityBusActor.DoorTransitionDuration) :
                        CityBusDriverDoorTimeline.SampleApproach(20f, 4f));
                    view.SetMotion(0f, 0f, 0f, phase.steer, false, .1f);
                    if (System.Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_FULL_BUS") == "1")
                        CaptureDriverCab(root, bus, driver, seat, phase.name);
                    var readings = new List<string>();
                    try
                    {
                        MeasureSeatedSurface("driver", phase.name, seat, seatMesh, support, report, issues);
                        readings.Add($"trousers/cushion {report.samples[report.samples.Count - 1].minimum_clearance_m:F4} m");
                    }
                    catch (AssertionException ex) { issues.Add(phase.name + ": " + ex.Message); }
                    Vector3[] drawnSeat = DriverWorldMesh(seatMesh, baked, out _).Select(seat.Coordinates).ToArray();
                    Vector3[] cushionTop = drawnSeat.Where(p => Mathf.Abs(p.y) < .01f).ToArray();
                    Vector3[] back = drawnSeat.Where(p => p.y > .2f && Mathf.Abs(p.x) > .22f).ToArray();
                    if (cushionTop.Length < 4 || back.Length < 4)
                    {
                        issues.Add(phase.name + ": the production seat has no measurable cushion or backrest.");
                        continue;
                    }
                    float halfWidth = cushionTop.Max(p => p.x), front = cushionTop.Max(p => p.z);
                    float rear = cushionTop.Min(p => p.z);
                    float topY = cushionTop.Average(p => p.y);
                    float underside = drawnSeat.Min(p => p.y);
                    float backFront = back.Max(p => p.z);
                    float backRear = back.Min(p => p.z);
                    float backHeight = back.Max(p => p.y);
                    float backClearance = float.PositiveInfinity;
                    Vector3 backContact = Vector3.zero;
                    string backPart = "";
                    foreach (Renderer part in driver.Renderers)
                    {
                        bool shin = part.name.StartsWith("CLO_Trousers_") && part.name.Contains("Lower.");
                        bool trunk = part.name.StartsWith("CLO_Outer_transit") || support.Contains(part);
                        if (!shin && !trunk) continue;
                        if (trunk)
                        {
                            Vector3? point = DeepestDriverBackPoint(part, baked, seat, halfWidth, backHeight);
                            if (point.HasValue && point.Value.z - backFront < backClearance)
                            {
                                backClearance = point.Value.z - backFront;
                                backContact = point.Value;
                                backPart = part.name;
                            }
                        }
                        bool penetrates = shin
                            ? DriverIntersectsBox(part, baked, seat, new Vector3(-halfWidth + .008f, underside + .008f, rear + .008f),
                                new Vector3(halfWidth - .008f, topY - .012f, front - .008f))
                            : DriverIntersectsBox(part, baked, seat, new Vector3(-halfWidth + .01f, .035f, backRear + .01f),
                                new Vector3(halfWidth - .01f, backHeight - .01f, backFront - .012f));
                        if (penetrates) issues.Add($"{phase.name}: drawn {part.name} penetrates the {(shin ? "cushion" : "backrest")}.");
                    }

                    readings.Add($"min backrest clearance {backClearance:F4} m on {backPart}, seat XYZ {backContact:F4}, front plane Z {backFront:F4}");
                    if (!float.IsFinite(backClearance) || backClearance < -.012f)
                        issues.Add($"{phase.name}: backrest clearance {backClearance:F4} m at {backPart}/{backContact:F4} (front z {backFront:F4}).");

                    Vector3 wheelCentre = bus.SteeringWheelPivot.position;
                    Vector3 normal = bus.SteeringWheelPivot.TransformDirection(bus.SteeringWheelAxisLocal).normalized;
                    Vector3[] wheelPoints = DriverWorldMesh(wheel, baked, out var wheelTriangles);
                    float rimRadius = wheelPoints.Max(p => Vector3.ProjectOnPlane(p - wheelCentre, normal).magnitude);
                    var rim = new List<int>();
                    for (int i = 0; i < wheelTriangles.Length; i += 3)
                    {
                        bool outer = true;
                        for (int j = 0; j < 3; j++)
                            outer &= Vector3.ProjectOnPlane(wheelPoints[wheelTriangles[i + j]] - wheelCentre, normal)
                                .magnitude > rimRadius * .75f;
                        if (outer) rim.AddRange(new[] { wheelTriangles[i], wheelTriangles[i + 1], wheelTriangles[i + 2] });
                    }
                    if (rim.Count == 0) issues.Add(phase.name + ": the drawn wheel has no measurable outer rim.");
                    if (phase.steer != 0f)
                    {
                        float angle = Quaternion.Angle(bus.SteeringWheelPivot.localRotation, neutralWheelRotation);
                        if (angle < 85f) issues.Add($"{phase.name}: wheel rotated only {angle:F1} degrees.");
                        float lateral = Vector3.Dot(wheel.transform.TransformPoint(topInWheel) - wheelCentre, seat.Right);
                        if (lateral * Mathf.Sign(phase.steer) < .11f) issues.Add($"{phase.name}: top of the rim moved to the wrong side ({lateral:F3} m).");
                    }
                    else if (!phase.press) neutralWheelRotation = bus.SteeringWheelPivot.localRotation;
                    Vector3[] buttonPoints = DriverWorldMesh(button, baked, out var buttonTriangles);
                    for (int side = 0; side < 2; side++)
                    {
                        bool left = side == 0;
                        string suffix = left ? ".L" : ".R";
                        var humanPalm = driver.Renderers.Single(r => r.name == "GEO_HandPalm" + suffix);
                        Vector3[] palm = DriverWorldMesh(humanPalm, baked, out _);
                        bool onButton = !left && phase.press;
                        if (onButton)
                        {
                            var index = driver.Renderers.Single(r => r.name == "GEO_HandFinger0.R");
                            var finger = DriverWorldMesh(index, baked, out var indexTriangles);
                            Vector3 tip = driver.RightIndexTip.position;
                            float skin = DriverSurfaceDistance(tip, finger, indexTriangles);
                            float buttonGap = DriverSurfaceDistance(tip, buttonPoints, buttonTriangles);
                            if (skin > .004f || buttonGap > .012f)
                                issues.Add($"{phase.name}: index tip to visible skin/button {skin:F4}/{buttonGap:F4} m.");
                        }
                        else
                        {
                            float palmGap = palm.Min(p => DriverSurfaceDistance(p, wheelPoints, rim));
                            if (palmGap > .025f) issues.Add($"{phase.name}{suffix}: palm to drawn rim {palmGap:F3} m.");
                            float majorRadius = Vector3.Distance(bus.LeftSteeringGrip.position, wheelCentre);
                            CheckSteeringDigits(phase.name, left, driver, baked, wheelCentre, normal, majorRadius,
                                rimRadius - majorRadius, seat.Up, wheelPoints, rim, issues);
                        }
                        float sign = left ? -1f : 1f;
                        Transform knee = left ? driver.LeftShin : driver.RightShin, ankle = left ? driver.LeftFoot : driver.RightFoot;
                        if (seat.Coordinates(knee.position).x * sign < .025f || seat.Coordinates(ankle.position).x * sign < .025f)
                            issues.Add($"{phase.name}{suffix}: knee or boot crossed the driver's midline.");
                        if (Vector3.Dot(knee.position - driver.Pelvis.position, seat.Forward) < .17f)
                            issues.Add($"{phase.name}{suffix}: knee is not in front of the driver's hips.");
                        var sole = driver.Renderers.Single(r => r.name == "CLO_Boot_workSole" + suffix);
                        Vector3 contact = (left ? driver.LeftSole : driver.RightSole).position;
                        Vector3[] drawnSole = DriverWorldMesh(sole, baked, out var soleTriangles);
                        var pads = DriverWorldMesh(pedals, baked, out var padTriangles);
                        float surface = DriverSurfaceDistance(contact, drawnSole, soleTriangles);
                        float gap = DriverSurfaceDistance(contact, pads, padTriangles);
                        readings.Add($"{suffix} sole/pedal {surface:F4}/{gap:F4} m");
                        if (surface > .007f || gap > .007f)
                            issues.Add($"{phase.name}{suffix}: planted point misses the drawn shoe/pedal {surface:F4}/{gap:F4} m.");
                    }
                    // The two forearms cannot exchange sides even at full lock.
                    float elbows = Vector3.Dot(driver.RightForearm.position - driver.LeftForearm.position, seat.Right);
                    float hands = Vector3.Dot(driver.RightHand.position - driver.LeftHand.position, seat.Right);
                    readings.Add($"elbows/hands separation {elbows:F3}/{hands:F3} m");
                    Debug.Log("Bus driver " + phase.name + ": " + string.Join(", ", readings));
                    if (elbows < .09f || hands < .035f)
                        issues.Add($"{phase.name}: crossed arms (elbow spacing {elbows:F3}, hand spacing {hands:F3}).");
                }
                Assert.That(issues, Is.Empty, string.Join("\n", issues));
                Renderer BusPart(string name) => bus.RendererBindings.Single(b => b.SourceName == name).Renderer;
            }
            finally { Object.DestroyImmediate(baked); Object.DestroyImmediate(root); }
        }

        private static void CheckSteeringDigits(string phase, bool left, CityBusDriverRig driver, Mesh baked,
            Vector3 centre, Vector3 shaft, float radius, float tubeRadius, Vector3 up, Vector3[] wheel,
            IReadOnlyList<int> rim, List<string> issues)
        {
            string side = left ? ".L" : ".R";
            Vector3 inward = (driver.RightHand.position - driver.LeftHand.position).normalized * (left ? 1f : -1f);
            Vector3 hand = (left ? driver.LeftHand : driver.RightHand).position;
            float indexHeight = 0f;
            for (int i = 0; i <= 4; i++)
            {
                bool thumb = i == 4;
                string meshName = (thumb ? "GEO_HandThumb" : "GEO_HandFinger" + i) + side;
                var part = driver.Renderers.Single(r => r.name == meshName);
                var skin = DriverWorldMesh(part, baked, out _);
                float contact = skin.Min(p => DriverSurfaceDistance(p, wheel, rim));
                if (contact > .012f)
                    issues.Add($"{phase}/{meshName}: skin to rim {contact:F4} m.");
                if (thumb)
                {
                    float advance = skin.Max(p => Vector3.Dot(p - hand, inward));
                    if (advance < .010f) issues.Add($"{phase}/{meshName}: thumb does not oppose the grip.");
                    continue;
                }

                float height = skin.Average(p => Vector3.Dot(p, up));
                if (i == 0) indexHeight = height;
                if (i == 3 && indexHeight - height < .025f)
                    issues.Add($"{phase}{side}: the index and little finger are not stacked upright on the side of the wheel.");

                // Measure how far the *skinned finger* wraps around the tube
                // cross-section. A flat hand touching one face has little sweep.
                var angles = new List<float>();
                foreach (Vector3 point in skin)
                {
                    Vector3 ray = Vector3.ProjectOnPlane(point - centre, shaft).normalized;
                    Vector3 offset = point - (centre + ray * radius);
                    if (offset.magnitude < tubeRadius * .60f || offset.magnitude > tubeRadius + .022f) continue;
                    angles.Add(Mathf.Atan2(Vector3.Dot(offset, shaft), Vector3.Dot(offset, ray)) * Mathf.Rad2Deg);
                }
                angles.Sort();
                float sweep = 0f;
                if (angles.Count > 1)
                {
                    float largestGap = 360f + angles[0] - angles[angles.Count - 1];
                    for (int j = 1; j < angles.Count; j++) largestGap = Mathf.Max(largestGap, angles[j] - angles[j - 1]);
                    sweep = 360f - largestGap;
                }
                // The shared hand has shorter distal segments. It must still
                // curve around most of a quadrant as well as touching the tube.
                if (sweep < 75f) issues.Add($"{phase}/{meshName}: visible finger spans only {sweep:F0}° around its rim tube.");
            }
        }

        private static Vector3[] DriverWorldMesh(Renderer renderer, Mesh baked, out int[] triangles)
        {
            ReadSeatedMesh(SeatedMesh(renderer, baked), out var vertices, out triangles);
            for (int i = 0; i < vertices.Length; i++) vertices[i] = renderer.transform.TransformPoint(vertices[i]);
            return vertices;
        }

        private static Vector3? DeepestDriverBackPoint(Renderer renderer, Mesh baked, SeatedCushionFrame frame,
            float halfWidth, float height)
        {
            Vector3[] vertices = DriverWorldMesh(renderer, baked, out var indices).Select(frame.Coordinates).ToArray();
            Vector3? deepest = null;
            for (int i = 0; i < indices.Length; i += 3)
            {
                var polygon = new List<Vector3> { vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]] };
                polygon = ClipSeatPolygon(polygon, 0, -halfWidth + .01f, true);
                polygon = ClipSeatPolygon(polygon, 0, halfWidth - .01f, false);
                polygon = ClipSeatPolygon(polygon, 1, .035f, true);
                polygon = ClipSeatPolygon(polygon, 1, height - .01f, false);
                foreach (var point in polygon)
                    if (!deepest.HasValue || point.z < deepest.Value.z) deepest = point;
            }
            return deepest;
        }

        private static bool DriverIntersectsBox(Renderer renderer, Mesh baked, SeatedCushionFrame frame,
            Vector3 min, Vector3 max)
        {
            Vector3[] vertices = DriverWorldMesh(renderer, baked, out var triangles).Select(frame.Coordinates).ToArray();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var polygon = new List<Vector3> { vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]] };
                for (int axis = 0; axis < 3 && polygon.Count > 0; axis++)
                {
                    polygon = ClipSeatPolygon(polygon, axis, min[axis], true);
                    polygon = ClipSeatPolygon(polygon, axis, max[axis], false);
                }
                if (polygon.Count > 0) return true;
            }
            return false;
        }

        private static bool DriverInsideTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, Vector3 normal) =>
            Vector3.Dot(normal, Vector3.Cross(b - a, p - a)) >= -.000001f &&
            Vector3.Dot(normal, Vector3.Cross(c - b, p - b)) >= -.000001f &&
            Vector3.Dot(normal, Vector3.Cross(a - c, p - c)) >= -.000001f;

        private static float DriverSurfaceDistance(Vector3 p, Vector3[] vertices, IReadOnlyList<int> triangles)
        {
            float minimum = float.PositiveInfinity;
            for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                if (normal.sqrMagnitude < .5f) continue;
                Vector3 projected = p - normal * Vector3.Dot(p - a, normal);
                if (DriverInsideTriangle(projected, a, b, c, normal))
                    minimum = Mathf.Min(minimum, (p - projected).sqrMagnitude);
                minimum = Mathf.Min(minimum, Edge(a, b), Edge(b, c), Edge(c, a));
            }
            return Mathf.Sqrt(minimum);

            float Edge(Vector3 from, Vector3 to)
            {
                Vector3 edge = to - from;
                float t = Mathf.Clamp01(Vector3.Dot(p - from, edge) / Mathf.Max(edge.sqrMagnitude, .0000001f));
                return (p - from - t * edge).sqrMagnitude;
            }
        }

        private static void CaptureDriverCab(GameObject root, CityBusAssetRegistry bus, CityBusDriverRig driver,
            SeatedCushionFrame seat, string phase)
        {
            var camera = new GameObject("Cab contact camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform, false);
            camera.enabled = false; camera.nearClipPlane = .025f; camera.farClipPlane = 15f;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.15f, .18f, .20f);
            var key = new GameObject("Cab study key").AddComponent<Light>();
            key.transform.SetParent(root.transform, false);
            key.type = LightType.Directional; key.intensity = 1.6f;
            key.cullingMask = camera.cullingMask;
            var fill = new GameObject("Cab study fill").AddComponent<Light>();
            fill.transform.SetParent(root.transform, false);
            fill.type = LightType.Spot; fill.range = 5f; fill.spotAngle = 100f; fill.intensity = 2.4f;
            fill.cullingMask = camera.cullingMask;
            var layers = root.GetComponentsInChildren<Renderer>(true).Select(r => (part: r.gameObject, layer: r.gameObject.layer)).ToArray();
            try
            {
                // The actual floor, dashboard and button still participate in
                // contact measurements. Isolate only the requested objects for
                // the review camera, including the rim, hub and moving spokes.
                foreach (var item in layers) item.part.layer = 0;
                foreach (var r in driver.Renderers) r.gameObject.layer = 31;
                foreach (var b in bus.RendererBindings)
                    if (b.SourceName == "INT_DriverSeat" || b.SourceName == "INT_SteeringWheel") b.Renderer.gameObject.layer = 31;
                Vector3 target = seat.Seat.position + seat.Up * .25f + seat.Forward * .13f;
                float aisleSide = Mathf.Sign(Vector3.Dot(bus.transform.position - seat.Seat.position, seat.Right));
                key.transform.rotation = Quaternion.LookRotation(-seat.Forward - seat.Up * 1.4f - seat.Right * aisleSide * .55f, seat.Up);
                camera.orthographic = true;
                camera.orthographicSize = .91f;
                foreach (bool front in new[] { true, false })
                {
                    camera.transform.position = target + seat.Forward * (front ? 2.15f : .35f) +
                        seat.Right * aisleSide * (front ? .47f : 2.02f) + seat.Up * (front ? .22f : .15f);
                    Capture(front ? "front" : "side");
                }
                if (phase == "straight")
                {
                    camera.transform.position = target + seat.Right * aisleSide * 1.53f +
                        seat.Forward * 1.45f + seat.Up * .52f;
                    Capture("three-quarter");
                }
                if (phase == "straight" || phase == "right-lock")
                {
                    // Look down between the seated eyes and the rim, then
                    // back from the windshield to show the other side of the fingers.
                    target = bus.SteeringWheelPivot.position;
                    camera.orthographicSize = .205f;
                    // Put the camera above the shoulders. Like a first-person
                    // view, omit the head behind the eyes for this shot only.
                    var cranial = driver.Renderers.Where(r => r.name == "GEO_FaceSurface" ||
                        r.name.StartsWith("GEO_Head") || r.name.StartsWith("GEO_Ear") ||
                        r.name.StartsWith("CLO_Hat") || r.name.StartsWith("HAIR_")).ToArray();
                    foreach (var part in cranial) part.gameObject.layer = 0;
                    Quaternion frontLight = key.transform.rotation;
                    key.transform.rotation = Quaternion.LookRotation(seat.Forward - seat.Up * 1.3f, seat.Up);
                    camera.transform.position = target - seat.Forward * .43f + seat.Up * .82f;
                    Capture("grip-driver");
                    foreach (var part in cranial) part.gameObject.layer = 31;
                    key.transform.rotation = frontLight;
                    camera.transform.position = target + seat.Forward * .62f + seat.Up * .12f;
                    Capture("grip-front");
                }

                void Capture(string angle)
                {
                    camera.transform.LookAt(target, seat.Up);
                    fill.transform.SetPositionAndRotation(camera.transform.position + seat.Up * .17f, camera.transform.rotation);
                    AreaCaptureFixture.CaptureCurrentCamera(camera, "BusFullCabin", $"driver-{phase}-{angle}", 1080, 810);
                }
            }
            finally
            {
                foreach (var item in layers) item.part.layer = item.layer;
                Object.DestroyImmediate(key.gameObject); Object.DestroyImmediate(fill.gameObject); Object.DestroyImmediate(camera.gameObject);
            }
        }
    }
}

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Explicit("Review the midibus from its City street and cabin camera.")]
        [PrebuildSetup(typeof(CityBusRemodelAssetSetup))]
        public IEnumerator CityMidibusRemodel()
        {
            GameObject stagedBus = null;
            try
            {
                GameSessionState.BeginNewGame("Midibus art review");
                Assert.That(GameSessionState.TryStartGameTimeAt(14 * 60), Is.True);
                yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(SceneIds.City);
                CityGameRoot city = null;
                yield return WaitFor(() => (city = Object.FindAnyObjectByType<CityGameRoot>()) != null && city.IsInitialized,
                    "The City did not compose.");
                foreach (var active in city.BusVehicles) active.enabled = false;
                var camera = city.Camera;
                camera.GetComponent<PlayerCameraFollow>().enabled = false;
                city.Player.Motor.SetInputEnabled(false);
                city.Player.Interactor.SetInputEnabled(false);
                city.Weather.enabled = false;
                city.Rain.SetIntensity(0f);
                CityWetSurfaceRegistry.SetImmediate(0f);
                var stop = city.BusPlan.Stops[0];
                var model = CityBusResources.Instantiate(city.transform);
                stagedBus = model.gameObject;
                Transform frame = model.transform;
                frame.SetPositionAndRotation(stop.Position, Quaternion.LookRotation(stop.Forward, Vector3.up));
                var view = stagedBus.AddComponent<CityBusPresentation>();
                view.Initialize(model);
                view.SetRouteNumber(city.BusPlan.Number);
                view.AttachDriver(CityBusDriverRig.Create(frame, city.BusPlan.Number));
                view.SetNightFactor(0f);
                Vector3 outside = new Vector3(5.4f, 2.8f, 6.4f);
                Shot(outside, new Vector3(0f,1.35f,0f), "01-closed-day");
                view.SetDoors(.18f);
                Shot(new Vector3(3.8f,1.7f,4.4f), new Vector3(1.05f,1.47f,3.05f), "02-unseating");
                view.SetDoors(1f);
                Shot(new Vector3(3.3f, 1.66f, 4.1f), new Vector3(.9f,1.45f,3.05f), "03-open-front");
                float cameraFov = camera.fieldOfView;
                camera.fieldOfView = 70f;
                float rearEyeX = Mathf.Min(3f, Vector3.Dot(stop.ShelterPosition - stop.Position, frame.right) - .75f);
                Shot(new Vector3(rearEyeX, 1.62f, -1.34f), new Vector3(.8f,1.40f,-1.34f), "04-open-rear");
                camera.fieldOfView = cameraFov;
                // From the door side of the centre aisle, looking into the working cab.
                Shot(new Vector3(.21f,1.98f,1.62f), new Vector3(-.54f,1.56f,3.34f), "05-cab-day");
                Shot(new Vector3(.39f,1.77f,2.28f), new Vector3(-.23f,1.28f,-1.70f), "06-saloon");
                view.SetDoors(0f);
                double toNight = (21 * 60 - GameSessionState.GameTimeOfDayMinutes + 1440) % 1440;
                GameSessionState.AdvanceGameTime((float)(toNight / GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                city.Night.Atmosphere.RefreshImmediate();
                city.Rain.SetIntensity(1f);
                CityWetSurfaceRegistry.SetImmediate(1f);
                view.SetNightFactor(1f);
                view.AdvanceWipers(1f,.48f);
                yield return null;
                Shot(outside, new Vector3(0f,1.04f,1.25f), "07-night-rain");
                Assert.That(view.HeadlightLights.Count, Is.EqualTo(2));
                foreach (var beam in view.HeadlightLights)
                {
                    Assert.That(beam.enabled, Is.True);
                    Assert.That(beam.range, Is.EqualTo(CityRoadHeadlightProfile.Range));
                    Assert.That(beam.color, Is.EqualTo(CityRoadHeadlightProfile.Color));
                }

                void Shot(Vector3 eye, Vector3 target, string label)
                {
                    camera.transform.SetPositionAndRotation(frame.TransformPoint(eye),
                        Quaternion.LookRotation(frame.TransformVector(target-eye), Vector3.up));
                    CaptureCurrentCamera(camera, "BusRemodel", label);
                }
            }
            finally
            {
                if (stagedBus != null) Object.DestroyImmediate(stagedBus);
                CityWetSurfaceRegistry.SetImmediate(0f);
                GameSessionState.BeginNewGame();
            }
        }
    }
}
