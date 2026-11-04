using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityBusNpcPassengerPlayModeTests
    {
        [UnityTest]
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
                var driver = CityBusDriverResources.Instantiate(root.transform);
                view.AttachDriver(driver);
                SetSeatedCaptureLayer(root);
                yield return null;
                var seat = new SeatedCushionFrame(bus.DriverSeatAnchor, view.transform.rotation);
                var support = SeatedSupportRenderers(driver.Renderers);
                var seatMesh = BusPart("INT_DriverSeat");
                var shell = BusPart("INT_InteriorSurfaces");
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
                    var floorVertices = DriverWorldMesh(shell, baked, out var floorTriangles).Select(seat.Coordinates).ToArray();
                    float floorDatum = seat.Coordinates(bus.StandingFloorAnchors[0].position).y;
                    float backClearance = float.PositiveInfinity;
                    Vector3 backContact = Vector3.zero;
                    string backPart = "";
                    foreach (var part in driver.RendererBindings)
                    {
                        bool shin = part.BoneName == "shin.L" || part.BoneName == "shin.R";
                        bool trunk = part.BoneName == "pelvis" || part.BoneName == "chest";
                        if (!shin && !trunk) continue;
                        if (trunk)
                        {
                            Vector3? point = DeepestDriverBackPoint(part.Renderer, baked, seat, halfWidth, backHeight);
                            if (point.HasValue && point.Value.z - backFront < backClearance)
                            {
                                backClearance = point.Value.z - backFront;
                                backContact = point.Value;
                                backPart = part.RendererName;
                            }
                        }
                        bool penetrates = shin
                            ? DriverIntersectsBox(part.Renderer, baked, seat, new Vector3(-halfWidth + .008f, underside + .008f, rear + .008f),
                                new Vector3(halfWidth - .008f, topY - .012f, front - .008f))
                            : DriverIntersectsBox(part.Renderer, baked, seat, new Vector3(-halfWidth + .01f, .035f, backRear + .01f),
                                new Vector3(halfWidth - .01f, backHeight - .01f, backFront - .012f));
                        if (penetrates) issues.Add($"{phase.name}: drawn {part.RendererName} penetrates the {(shin ? "cushion" : "backrest")}.");
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
                        var humanPalm = driver.RendererBindings.Single(b => b.RendererName == "GEO_Hand" + suffix).Renderer;
                        Vector3[] palm = DriverWorldMesh(humanPalm, baked, out _);
                        bool onButton = !left && phase.press;
                        if (onButton)
                        {
                            var index = driver.RendererBindings.Single(b => b.RendererName == "GEO_HandFinger0.R").Renderer;
                            var finger = DriverWorldMesh(index, baked, out var indexTriangles);
                            Vector3 tip = CityPedestrianHandProps.FindSocket(driver.ModelRoot, "ANCHOR_DriverIndexOpenTip.R").position;
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
                        var sole = driver.RendererBindings.Single(b => b.RendererName == "CLO_ShoeSole" + suffix).Renderer;
                        float minimumGap = float.PositiveInfinity;
                        foreach (var solePoint in DriverWorldMesh(sole, baked, out _))
                        {
                            Vector3 point = seat.Coordinates(solePoint);
                            for (int i = 0; i < floorTriangles.Length; i += 3)
                            {
                                Vector3 a = floorVertices[floorTriangles[i]], b = floorVertices[floorTriangles[i + 1]],
                                    c = floorVertices[floorTriangles[i + 2]];
                                if (Mathf.Abs(a.y - floorDatum) > .035f || Mathf.Abs(b.y - floorDatum) > .035f ||
                                    Mathf.Abs(c.y - floorDatum) > .035f) continue;
                                Vector3 plane = Vector3.Cross(b - a, c - a);
                                if (Mathf.Abs(plane.y) < .0001f) continue;
                                float y = a.y - ((point.x - a.x) * plane.x + (point.z - a.z) * plane.z) / plane.y;
                                var contact = new Vector3(point.x, y, point.z);
                                if (DriverInsideTriangle(contact, a, b, c, plane)) minimumGap = Mathf.Min(minimumGap, point.y - y);
                            }
                        }
                        readings.Add($"{suffix} sole/floor {minimumGap:F4} m");
                        if (minimumGap < -.010f || minimumGap > .018f)
                            issues.Add($"{phase.name}{suffix}: visible sole to drawn footwell {minimumGap:F4} m.");
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

        private static void CheckSteeringDigits(string phase, bool left, CityBusDriverAssetRegistry driver, Mesh baked,
            Vector3 centre, Vector3 shaft, float radius, float tubeRadius, Vector3 up, Vector3[] wheel,
            IReadOnlyList<int> rim, List<string> issues)
        {
            string side = left ? ".L" : ".R";
            Vector3 across = (driver.RightHand.position - driver.LeftHand.position).normalized * (left ? 1f : -1f);
            Vector3 indexRoot = default;
            for (int i = 0; i <= 4; i++)
            {
                bool thumb = i == 4;
                string digit = thumb ? "Thumb" : "Finger" + i;
                string meshName = (thumb ? "GEO_Thumb" : "GEO_HandFinger" + i) + side;
                var part = driver.RendererBindings.Single(b => b.RendererName == meshName).Renderer;
                var skin = DriverWorldMesh(part, baked, out var triangles);
                string anchor = "ANCHOR_Driver" + digit;
                Vector3 root = CityPedestrianHandProps.FindSocket(driver.ModelRoot, anchor + "Root" + side).position;
                Vector3 tip = CityPedestrianHandProps.FindSocket(driver.ModelRoot, anchor + "Tip" + side).position;
                float endCap = DriverSurfaceDistance(tip, skin, triangles);
                float contact = skin.Min(p => DriverSurfaceDistance(p, wheel, rim));
                if (endCap > .004f || contact > .012f)
                    issues.Add($"{phase}/{meshName}: fingertip to its closed skin {endCap:F4}, skin to rim {contact:F4} m.");
                if (thumb)
                {
                    float inward = Vector3.Dot(tip - root, across);
                    if (inward < .010f) issues.Add($"{phase}/{meshName}: thumb advances only {inward:F3} m toward the opposite hand.");
                    continue;
                }

                if (i == 0) indexRoot = root;
                if (i == 3 && Vector3.Dot(indexRoot - root, up) < .025f)
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
                if (sweep < 105f) issues.Add($"{phase}/{meshName}: visible finger spans only {sweep:F0}° around its rim tube.");
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

        private static void CaptureDriverCab(GameObject root, CityBusAssetRegistry bus, CityBusDriverAssetRegistry driver,
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
                    var cranial = driver.RendererBindings.Where(b => b.BoneName == "head" ||
                        b.BoneName == "neck" || b.BoneName.StartsWith("face.", System.StringComparison.Ordinal)).ToArray();
                    foreach (var part in cranial) part.Renderer.gameObject.layer = 0;
                    Quaternion frontLight = key.transform.rotation;
                    key.transform.rotation = Quaternion.LookRotation(seat.Forward - seat.Up * 1.3f, seat.Up);
                    camera.transform.position = target - seat.Forward * .43f + seat.Up * .82f;
                    Capture("grip-driver");
                    foreach (var part in cranial) part.Renderer.gameObject.layer = 31;
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
