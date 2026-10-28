using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Descending passenger views and actual open-tunnel geometry, with the production car, lamps and mountain atmosphere.")]
        public IEnumerator MountainRoadCityTunnel()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime((float)(90f / GameTimeState.GameMinutesPerRealSecond));
            yield return SceneManager.LoadSceneAsync(SceneIds.MountainRoad, LoadSceneMode.Single);
            MountainRoadRoot mountain = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                mountain = Object.FindAnyObjectByType<MountainRoadRoot>();
                if (mountain != null && mountain.IsInitialized && !CompositionDriver.IsComposing) break;
                yield return null;
            }
            Assert.That(mountain != null && mountain.IsInitialized, Is.True);
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            Assert.That(mountain.LastRouteCar, Is.Not.Null);

            LastRouteCarAssetRegistry car = mountain.LastRouteCar;
            LastRouteCarDriver driver = car.GetComponentInParent<LastRouteCarDriver>();
            Assert.That(driver, Is.Not.Null);
            LastRouteCarHeadlights headlights = driver.GetComponent<LastRouteCarHeadlights>();
            Assert.That(headlights, Is.Not.Null);
            Transform carRoot = driver.transform;
            bool savedFollow = mountain.CameraFollow.enabled;
            bool savedRide = mountain.Ride.enabled;
            bool savedDriver = driver.enabled;
            bool savedHeadlights = headlights.enabled;
            float savedPower = headlights.Power;
            Vector3 savedCarPosition = carRoot.position;
            Quaternion savedCarRotation = carRoot.rotation;
            Vector3 savedPlayerPosition = mountain.Player.GameObject.transform.position;
            float savedFov = camera.fieldOfView;
            var hidden = new List<Renderer>();
            foreach (Renderer renderer in mountain.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled) { hidden.Add(renderer); renderer.enabled = false; }
            try
            {
                // Sample the existing return path without spending the full ride
                // or starting a scene transition. The native seated view, actual
                // cabin and full driving beams are identical at these positions.
                mountain.CameraFollow.enabled = false;
                mountain.Ride.enabled = false;
                driver.enabled = false;
                headlights.enabled = false;
                headlights.SetPower(1f);
                Assert.That(mountain.LastRouteFerryman.BeginSeatedAtTheWheel(), Is.True);
                LastRouteCarDrivePath descent = LastRouteMountainDrivePlanner.CreateDeparture(mountain.Plan);
                float[] remaining = { 30f, 17f, 7f, 1f, 0f };
                for (int index = 0; index < remaining.Length; index++)
                {
                    descent.Sample(descent.Length - remaining[index], out Vector3 position, out Vector3 forward);
                    carRoot.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
                    mountain.Player.Motor.Teleport(position);
                    for (int frame = 0; frame < 5; frame++) yield return null;
                    LastRouteCarSeatViewPlan.EvaluateCamera(car.PassengerSeatAnchor.position,
                        carRoot.forward, 0f, LastRouteCarSeatViewPlan.BasePitchDegrees,
                        out Vector3 eye, out Quaternion look);
                    camera.fieldOfView = LastRouteCarSeatViewPlan.FieldOfView;
                    camera.transform.SetPositionAndRotation(eye, look);
                    Assert.That(headlights.LeftBeam.enabled && headlights.RightBeam.enabled, Is.True);
                    Assert.That(headlights.LeftBeam.intensity,
                        Is.EqualTo(LastRouteCarHeadlights.BeamIntensity).Within(0.01f));
                    Assert.That(RenderSettings.fog, Is.True, "Keep the production mountain fog.");
                    CaptureCurrentCamera(camera, "MountainRoadCityTunnel",
                        $"{index + 1:00}-passenger-{remaining[index]:00}m-to-transfer");
                }

                MountainRoadRouteSample approach = mountain.Plan.Route.Sample(17f);
                mountain.Player.Motor.Teleport(approach.Position);
                camera.fieldOfView = savedFov;
                camera.transform.SetPositionAndRotation(
                    approach.Position + approach.Right * 1.6f + Vector3.up * EyeHeight,
                    Quaternion.LookRotation(mountain.Plan.Tunnel.PortalGroundCenter +
                        Vector3.up * 2f - (approach.Position + approach.Right * 1.6f +
                        Vector3.up * EyeHeight), Vector3.up));
                CaptureCurrentCamera(camera, "MountainRoadCityTunnel", "06-descending-road-approach");
                // Clear both actors before probing the world's surfaces. The
                // spawned hero's capsule is otherwise inside the first chord.
                carRoot.SetPositionAndRotation(savedCarPosition, savedCarRotation);
                mountain.Player.Motor.Teleport(mountain.Plan.Route.Sample(30f).Position);
                AssertMountainCityTunnelGeometry(mountain);
                AssertMountainRoadWalkingCorridor(mountain, carRoot);
            }
            finally
            {
                carRoot.SetPositionAndRotation(savedCarPosition, savedCarRotation);
                mountain.Player.Motor.Teleport(savedPlayerPosition);
                headlights.SetPower(savedPower);
                headlights.enabled = savedHeadlights;
                driver.enabled = savedDriver;
                mountain.Ride.enabled = savedRide;
                mountain.CameraFollow.enabled = savedFollow;
                camera.fieldOfView = savedFov;
                foreach (Renderer renderer in hidden) if (renderer != null) renderer.enabled = true;
            }
        }

        private static void AssertMountainRoadWalkingCorridor(MountainRoadRoot mountain, Transform carRoot)
        {
            PlayerMotor motor = mountain.Player.Motor;
            CharacterController body = motor.GetComponent<CharacterController>();
            Assert.That(body, Is.Not.Null);
            Assert.That(body.enabled, Is.True);
            Assert.That(body.radius, Is.GreaterThan(0f));
            Assert.That(body.skinWidth, Is.GreaterThan(0f));
            Assert.That(body.stepOffset, Is.GreaterThan(0f));
            Collider roadFloor = mountain.World.RoadSurface.GetComponent<Collider>();
            Assert.That(roadFloor, Is.Not.Null);
            bool savedMotor = motor.enabled;
            Vector3 savedPosition = motor.transform.position;
            var actorColliders = new List<Collider>();
            try
            {
                // Exercise the real capsule against the composed world in one
                // bounded pass. Only the two owned ride actors are removed:
                // their parked bodies are not defects in the road corridor.
                motor.enabled = false;
                foreach (Transform actor in new[] { carRoot, mountain.LastRouteFerryman.transform })
                    foreach (Collider collider in actor.GetComponentsInChildren<Collider>(true))
                        if (collider.enabled) { actorColliders.Add(collider); collider.enabled = false; }
                Physics.SyncTransforms();

                // Turning/widening ribbon rows form non-coplanar quads.
                // Their real triangles need not follow the centreline Lerp,
                // nor its maximum grade. Check the authored row datums
                // separately, then sweep the actual lower capsule sphere.
                float footOffset = body.transform.TransformVector(
                    body.center - Vector3.up * (body.height * .5f)).y;
                motor.Teleport(mountain.Plan.Tunnel.SpawnPosition + Vector3.up * (body.skinWidth - footOffset));
                MoveTo(mountain.Plan.Tunnel.SpawnPosition, "tunnel start");
                Vector3 previous = mountain.Plan.Tunnel.SpawnPosition;
                foreach (MountainRoadRouteSample sample in mountain.Plan.Route.Samples)
                    WalkChord(sample.Position, "climb " + sample.StableId);
                WalkChord(mountain.Plan.Terminal.VehicleApron.Center, "terminal apron");
                for (int index = mountain.Plan.Route.Samples.Count - 1; index >= 0; index--)
                    WalkChord(mountain.Plan.Route.Samples[index].Position, "descent " + mountain.Plan.Route.Samples[index].StableId);
                WalkChord(mountain.Plan.Tunnel.SpawnPosition, "tunnel return");
                Debug.Log("MOUNTAIN ROAD CORRIDOR: native hero capsule reached the terminal apron and returned " +
                    "through every serpentine chord with physical support; this is a geometry proof, not a timed ride.");

                void WalkChord(Vector3 destination, string stage)
                {
                    RaycastHit datum = FloorAt(destination, stage);
                    Assert.That(datum.point.y, Is.EqualTo(destination.y).Within(NumericTolerance(destination.y)),
                        $"The physical floor misses an authored route datum at {stage}: " +
                        $"planned={destination:F6}, collider={datum.collider.name}, floor={datum.point:F6}.");
                    Vector3 start = previous;
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, destination) / body.radius));
                    for (int step = 1; step <= steps; step++)
                        MoveTo(Vector3.Lerp(start, destination, step / (float)steps), stage);
                    previous = destination;
                }

                void MoveTo(Vector3 ground, string stage)
                {
                    Assert.That(mountain.World.WalkableArea.Contains(ground, body.radius), Is.True,
                        $"The planned hero corridor leaves the walkable area at {stage}: {ground:F4}.");
                    Vector3 delta = ground - motor.transform.position;
                    // Native slope/step handling must earn the height. No
                    // upward positioning or teleports may bridge a blocker
                    // or missing floor between the corridor's endpoints.
                    delta.y = -body.stepOffset;
                    CollisionFlags flags = body.Move(delta);
                    Vector3 actual = motor.transform.position;
                    Vector2 planarError = new Vector2(actual.x - ground.x, actual.z - ground.z);
                    Assert.That(planarError.magnitude, Is.LessThanOrEqualTo(body.skinWidth),
                        $"The actual capsule is blocked at {stage}: planned={ground:F4}, actual={actual:F4}, flags={flags}.");
                    Assert.That((flags & CollisionFlags.Below) != 0, Is.True,
                        $"The actual capsule has no physical support at {stage}: planned={ground:F4}, actual={actual:F4}.");
                    RaycastHit floor = FloorAt(new Vector3(actual.x, ground.y, actual.z), stage);
                    Vector3 lowerSphere = body.transform.TransformPoint(body.center -
                        Vector3.up * (body.height * .5f - body.radius));
                    float lift = body.stepOffset + body.skinWidth;
                    RaycastHit support = default;
                    foreach (RaycastHit hit in Physics.SphereCastAll(lowerSphere + Vector3.up * lift,
                        body.radius, Vector3.down, lift * 2f, ~0, QueryTriggerInteraction.Ignore))
                        if (hit.collider == floor.collider &&
                            (support.collider == null || hit.distance < support.distance)) support = hit;
                    Assert.That(support.collider, Is.Not.Null,
                        $"The actual capsule sphere cannot reach its intended floor at {stage}: {floor.collider.name}.");
                    Assert.That(support.normal.y, Is.GreaterThanOrEqualTo(
                        Mathf.Cos(body.slopeLimit * Mathf.Deg2Rad) - .00001f),
                        $"The actual supporting triangle is too steep at {stage}: normal={support.normal:F6}.");
                    // SphereCast already accounts for hemisphere rise and
                    // triangle edges. Only the native skin/contact pair and
                    // single-precision rounding remain as separation bounds.
                    float nativeGap = (body.skinWidth + body.contactOffset + support.collider.contactOffset) /
                        support.normal.y;
                    float supportGap = support.distance - lift;
                    float footY = body.transform.TransformPoint(body.center - Vector3.up * (body.height * .5f)).y;
                    Assert.That(Mathf.Abs(supportGap), Is.LessThanOrEqualTo(nativeGap + NumericTolerance(footY)),
                        $"The capsule separates from the actual road triangles at {stage}: " +
                        $"actualFootY={footY:F8}, floorY={floor.point.y:F8}, sphereGap={supportGap:F8}, " +
                        $"nativeSkinAndContactGap={nativeGap:F8}.");
                    Assert.That(mountain.World.WalkableArea.Contains(actual, body.radius), Is.True,
                        $"The actual capsule leaves the corridor at {stage}: {actual:F4}.");
                }

                RaycastHit FloorAt(Vector3 point, string stage)
                {
                    var ray = new Ray(point + Vector3.up, Vector3.down);
                    if (roadFloor.Raycast(ray, out RaycastHit roadHit, 2f)) return roadHit;
                    // The terminal's vehicle apron may extend beyond the
                    // ribbon; its actual terrain still owns physical support.
                    RaycastHit nearest = default;
                    foreach (RaycastHit hit in Physics.RaycastAll(ray, 2f, ~0, QueryTriggerInteraction.Ignore))
                        if (hit.collider.transform.IsChildOf(mountain.World.PhysicalRoot.transform) &&
                            hit.normal.y > 0f && (nearest.collider == null || hit.distance < nearest.distance)) nearest = hit;
                    Assert.That(nearest.collider, Is.Not.Null,
                        $"The planned corridor has no world floor at {stage}: {point:F6}.");
                    return nearest;
                }

                float NumericTolerance(float height) => Mathf.Max(.00001f,
                    Mathf.Abs(height) * 4f * 1.1920929e-7f);
            }
            finally
            {
                motor.Teleport(savedPosition);
                foreach (Collider collider in actorColliders) if (collider != null) collider.enabled = true;
                motor.enabled = savedMotor;
                Physics.SyncTransforms();
            }
        }

        private static void AssertMountainCityTunnelGeometry(MountainRoadRoot mountain)
        {
            MountainRoadTunnelDescriptor tunnel = mountain.Plan.Tunnel;
            Transform root = mountain.World.PhysicalRoot.transform.Find("Tunnel Exit");
            Assert.That(root, Is.Not.Null);
            Assert.That(root.Find("Tunnel Darkness"), Is.Null, "A black plate is a headlight-lit wall.");
            Assert.That(tunnel.PhysicalDepth, Is.EqualTo(9f));
            Assert.That(tunnel.VisualDepth, Is.EqualTo(72f));
            Assert.That(mountain.World.WalkableArea.Contains(tunnel.SpawnPosition, 0.32f), Is.True);
            Assert.That(mountain.World.WalkableArea.Contains(
                tunnel.PortalGroundCenter - tunnel.OutwardAxis * 15f, 0.32f), Is.False);
            LastRouteCarDrivePath descent = LastRouteMountainDrivePlanner.CreateDeparture(mountain.Plan);
            descent.Sample(descent.Length, out Vector3 endpoint, out Vector3 facing);
            Assert.That(Vector3.Distance(endpoint, tunnel.SpawnPosition), Is.LessThan(0.001f));
            Assert.That(Vector3.Dot(facing, -tunnel.OutwardAxis), Is.GreaterThan(0.999f));

            var probes = new List<MeshCollider>();
            try
            {
                // Probe the actual rendered triangles. The visual tail itself
                // must stay non-colliding and does not extend the player's area.
                foreach (string name in new[] { "Tunnel Lining Continuation", "Tunnel Floor Continuation" })
                {
                    Transform part = root.Find(name);
                    Assert.That(part, Is.Not.Null);
                    Assert.That(part.GetComponent<Collider>(), Is.Null);
                    MeshCollider probe = part.gameObject.AddComponent<MeshCollider>();
                    probe.sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
                    probes.Add(probe);
                }
                Physics.SyncTransforms();
                Vector3 right = Vector3.Cross(Vector3.up, tunnel.OutwardAxis);
                foreach (float side in new[] { -3f, 0f, 3f })
                    foreach (float height in new[] { 1f, 2.5f, 4f })
                        Assert.That(Physics.Raycast(tunnel.PortalGroundCenter + right * side +
                            Vector3.up * height, -tunnel.OutwardAxis, 12f, ~0,
                            QueryTriggerInteraction.Ignore), Is.False,
                            $"The entrance aperture must stay clear at {side}/{height}.");

                foreach (CityMountainTunnelSegmentDescriptor segment in tunnel.Segments)
                {
                    Vector3 segmentRight = Vector3.Cross(Vector3.up, segment.Forward);
                    // The old physical ribbon narrows to the 4.8 m road at
                    // the mouth; the soil shoulders are deliberately 24 cm
                    // below it. Probe its usable carriageway, not a shoulder.
                    float probeOffset = segment.HasCollision
                        ? MountainRoadPlanner.RoadWidth * 0.5f - 0.3f : 2.7f;
                    for (float t = 0.08f; t < 1f; t += 0.2f)
                        foreach (float side in new[] { -probeOffset, 0f, probeOffset })
                        {
                            Vector3 ground = Vector3.Lerp(segment.Start, segment.End, t) + segmentRight * side;
                            Assert.That(Physics.Raycast(ground + Vector3.up * 3f, Vector3.down,
                                out RaycastHit floor, 4f, ~0, QueryTriggerInteraction.Ignore), Is.True);
                            Assert.That(floor.point.y, Is.EqualTo(ground.y).Within(0.012f),
                                $"Tunnel floor/terrain intersection at {ground}: {floor.collider.name}.");
                            Assert.That(Physics.Raycast(ground + Vector3.up, Vector3.up,
                                out RaycastHit roof, 6f, ~0, QueryTriggerInteraction.Ignore), Is.True);
                            Assert.That(roof.point.y, Is.EqualTo(ground.y + tunnel.OpeningHeight).Within(0.02f));
                        }
                }
                int forestVerticesInside = 0;
                Transform forest = mountain.World.PhysicalRoot.transform.Find("Batched Melancholic Forest");
                Assert.That(forest, Is.Not.Null);
                foreach (MeshFilter mesh in forest.GetComponentsInChildren<MeshFilter>())
                    foreach (Vector3 local in ReadTunnelProbeVertices(mesh.sharedMesh))
                    {
                        Vector3 vertex = mesh.transform.TransformPoint(local);
                        if (vertex.y <= tunnel.PortalGroundCenter.y + 0.02f ||
                            vertex.y >= tunnel.PortalGroundCenter.y + tunnel.OpeningHeight - 0.02f) continue;
                        foreach (CityMountainTunnelSegmentDescriptor segment in tunnel.Segments)
                        {
                            Vector3 offset = vertex - segment.Start;
                            float along = Vector3.Dot(offset, segment.Forward);
                            float across = Vector3.Dot(offset, Vector3.Cross(Vector3.up, segment.Forward));
                            if (along >= 0f && along <= segment.Length &&
                                Mathf.Abs(across) < tunnel.OpeningWidth * 0.5f)
                            { forestVerticesInside++; break; }
                        }
                    }
                Assert.That(forestVerticesInside, Is.Zero,
                    "Actual forest crown/trunk vertices must not protrude into the visible tunnel.");
                CityMountainTunnelSegmentDescriptor last = tunnel.Segments[tunnel.Segments.Count - 1];
                Vector3 eyes = tunnel.SpawnPosition + Vector3.up * 1.7f;
                Vector3 toOpenEnd = last.End + Vector3.up * 1.7f - eyes;
                Assert.That(Physics.Raycast(eyes, toOpenEnd.normalized, out RaycastHit occlusion,
                    toOpenEnd.magnitude, ~0, QueryTriggerInteraction.Ignore), Is.True,
                    "The existing side lining must conceal the uncapped end around the bend.");
                Assert.That(occlusion.collider.name, Is.EqualTo("Tunnel Lining Continuation"));
                Assert.That(Physics.Raycast(last.End + Vector3.up * 1.7f - last.Forward,
                    last.Forward, 2f, ~0, QueryTriggerInteraction.Ignore), Is.False,
                    "There is no end cap hidden at the new far endpoint either.");
                Debug.Log($"MOUNTAIN CITY TUNNEL: physical={tunnel.PhysicalDepth:F1}, visual={tunnel.VisualDepth:F1}, " +
                    $"endpoint hidden by lining at {occlusion.distance:F2} m; existing descent endpoint={endpoint}.");
            }
            finally
            {
                foreach (MeshCollider probe in probes) if (probe != null) Object.DestroyImmediate(probe);
            }
        }

        private static Vector3[] ReadTunnelProbeVertices(Mesh mesh)
        {
#if UNITY_EDITOR
            // Like the existing brook/mother capture probes, inspect the real
            // uploaded mesh without retaining a production CPU vertex copy.
            using (Mesh.MeshDataArray data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh))
            using (var positions = new Unity.Collections.NativeArray<Vector3>(
                       data[0].vertexCount, Unity.Collections.Allocator.Temp))
            {
                data[0].GetVertices(positions);
                return positions.ToArray();
            }
#else
            return mesh.vertices;
#endif
        }
    }
}
