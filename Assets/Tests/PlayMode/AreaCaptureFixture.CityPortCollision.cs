using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [Serializable]
        private sealed class PortTraversalReport
        {
            public Vector3 origin;
            public float radius, height, stepOffset;
            public List<PortTraversalRoute> routes = new List<PortTraversalRoute>();
            public List<Vector3> clearFloorOutsideMask = new List<Vector3>();
            public int supportedGridPoints;
        }

        [Serializable]
        private sealed class PortTraversalRoute
        {
            public string name, mode, result, colliders;
            public Vector3 start, target, stoppedAt;
            public float remaining;
        }

        [UnityTest]
        [Explicit("Dock/access regression and measured traversal report; run this selection alone.")]
        public IEnumerator CityPortTraversalAudit()
        {
            GameSessionState.BeginNewGame();
            yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            CityGameRoot city = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (city == null || !city.IsInitialized)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "City initialization timed out.");
                city = UnityEngine.Object.FindAnyObjectByType<CityGameRoot>();
                yield return null;
            }
            var port = UnityEngine.Object.FindAnyObjectByType<CityPortController>();
            Assert.That(port, Is.Not.Null);
            var access = port.Plan.Access;
            Assert.That(access, Is.Not.Null);
            var hero = city.Player.GameObject.GetComponent<CharacterController>();
            var report = new PortTraversalReport { origin = port.Plan.Origin,
                radius = hero.radius, height = hero.height, stepOffset = hero.stepOffset };
            Vector3 savedHero = hero.transform.position;
            bool savedForce = port.ForcePresentation;
            var dynamicBodies = new List<Collider>();
            // Diagnose static access separately from visible movable work bodies.
            // Restore every enabled state even when an assertion fails.
            foreach (Collider body in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                if (body.enabled && (body.GetComponentInParent<CityPedestrianActor>() != null ||
                    body.GetComponentInParent<CityPortCrew>() != null ||
                    city.Cannery != null && body.transform.IsChildOf(city.Cannery.Truck) ||
                    body.transform.IsChildOf(port.Trolley)))
                { dynamicBodies.Add(body); body.enabled = false; }
            city.Player.Motor.SetInputEnabled(false);
            using (GameTimeScaleRuntime.AcquirePause())
            try
            {
                port.ForcePresentation = true;
                port.RefreshPresentation();
                Physics.SyncTransforms();
                for (int side = -1; side <= 1; side++)
                {
                    var points = new List<Vector3>();
                    foreach (var sample in access.RoadSamples)
                    {
                        float lateral = side * (sample.halfWidth - .55f);
                        points.Add(sample.center + sample.right * lateral + Vector3.up * sample.crossfall * lateral);
                    }
                    AuditPortRouteBothWays("road-lane-" + side, points, city, hero, port, report);
                }
                AuditPortRouteBothWays("public-bypass", access.PublicPath, city, hero, port, report);
                AuditPortRouteBothWays("street-public-spur", access.PublicStreetSpur, city, hero, port, report);
                AuditPortRouteBothWays("west-ramp-rear-walk", new[] {
                    new Vector3(-31, .32f, -20.5f), new Vector3(-28, .91f, -20.5f),
                    new Vector3(-25, 1.5f, -20.5f), new Vector3(18, 1.5f, -20.5f) }, city, hero, port, report);
                AuditPortRouteBothWays("west-ramp-north-seam", new[] {
                    new Vector3(-29, .615f, -19.6f), new Vector3(-24.5f, 1.5f, -19.6f) }, city, hero, port, report);
                AuditPortRouteBothWays("west-quay-breakwater", new[] {
                    new Vector3(-19.5f, 1.5f, -20.5f), new Vector3(-19.5f, 1.5f, -1),
                    new Vector3(-23, 1.5f, -1), new Vector3(-23, 1.5f, 30),
                    new Vector3(-9, 1.5f, 30) }, city, hero, port, report);
                AuditPortRouteBothWays("yard-seam-east", new[] {
                    new Vector3(21, 1.5f, -30), new Vector3(21, 1.5f, -13) }, city, hero, port, report);
                AuditPortRouteBothWays("quay-east-public-seam", new[] {
                    new Vector3(17, 1.5f, -18), new Vector3(26, 1.5f, -18) }, city, hero, port, report);
                for (int side = -1; side <= 1; side += 2)
                {
                    AuditPortRouteBothWays("public-straight-edge-" + side, new[] {
                        new Vector3(25 + side * .6f, 1.512f, -16),
                        new Vector3(25 + side * .6f, 1.512f, -10) }, city, hero, port, report);
                    Vector3 a = access.PublicPath[5], b = access.PublicPath[6];
                    Vector3 tangent = b - a; tangent.y = 0; tangent.Normalize();
                    Vector3 offset = new Vector3(tangent.z, 0, -tangent.x) * (side * .6f);
                    AuditPortRouteBothWays("public-diagonal-edge-" + side, new[] {
                        Vector3.Lerp(a, b, .15f) + offset, Vector3.Lerp(a, b, .85f) + offset }, city, hero, port, report);
                }

                // A grid catches lateral boundaries which centreline routes miss.
                // Probe the centre and cardinal capsule edges on shallow ground,
                // plus free volume above step height. Boundary candidates still
                // need classification; isolated probes do not prove a whole route.
                hero.enabled = false;
                Physics.SyncTransforms();
                for (float x = -33; x <= 64; x += .5f)
                for (float z = -48; z <= 33; z += .5f)
                {
                    Vector3 point = port.Plan.World(new Vector3(x, 0, z));
                    if (!PortAuditFloor(point, port.Plan.Origin.y, out Vector3 floor)) continue;
                    bool supported = true;
                    foreach (Vector3 offset in new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                        if (!PortAuditFloor(point + offset * (hero.radius + .04f), port.Plan.Origin.y, out Vector3 edge) ||
                            Mathf.Abs(edge.y - floor.y) > .28f) { supported = false; break; }
                    if (!supported || PortAuditObstacles(hero, floor).Length != 0) continue;
                    report.supportedGridPoints++;
                    if (!city.World.WalkableArea.Contains(floor, hero.radius))
                        report.clearFloorOutsideMask.Add(floor - port.Plan.Origin);
                }
                string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults"));
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "port-traversal-audit.json"), JsonUtility.ToJson(report, true));
                Debug.Log($"PORT TRAVERSAL AUDIT: origin={report.origin:F3}; clear supported samples={report.supportedGridPoints}; " +
                    $"outside mask={report.clearFloorOutsideMask.Count}. See TestResults/port-traversal-audit.json. " +
                    "Isolated grid candidates still require boundary classification.");
                Assert.That(report.supportedGridPoints, Is.GreaterThan(100), "The audit must measure actual physical ground.");
                foreach (PortTraversalRoute route in report.routes)
                    if (!route.name.StartsWith("quay-east-public-seam/", StringComparison.Ordinal))
                        Assert.That(route.result, Is.EqualTo("clear"),
                            $"{route.name} {route.mode} stopped at {route.stoppedAt}: {route.colliders}");
                ValidatePortWalkClearance(city, port, hero.radius);
            }
            finally
            {
                hero.enabled = true;
                hero.transform.position = savedHero;
                port.ForcePresentation = savedForce;
                foreach (Collider body in dynamicBodies) if (body != null) body.enabled = true;
                city.Player.Motor.SetInputEnabled(true);
                port.RefreshPresentation();
                Physics.SyncTransforms();
            }
        }

        private static void ValidatePortWalkClearance(CityGameRoot city, CityPortController port, float radius)
        {
            CityPortAccessPlan access = port.Plan.Access;
            // Radius is applied once to the actual 2 m straight strip. These
            // points are over water, so beach rectangles cannot mask a mistake.
            foreach (float bodyRadius in new[] { 0f, .2f, radius, CityGroundTraversalPlanner.MaximumAgentRadius })
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 inside = port.Plan.World(new Vector3(25 + side * (1 - bodyRadius - .01f), 1.512f, -12));
                Vector3 outside = port.Plan.World(new Vector3(25 + side * (1 - bodyRadius + .03f), 1.512f, -12));
                Assert.That(city.World.WalkableArea.Contains(inside, bodyRadius), Is.True, "Full physical walk width must remain usable.");
                Assert.That(city.World.WalkableArea.ClosestPoint(inside, bodyRadius), Is.EqualTo(inside),
                    "An already valid walk point must not snap to the old narrow rectangle core.");
                Assert.That(city.World.WalkableArea.Contains(outside, bodyRadius), Is.False, "Do not open unsupported water beside the walk.");
            }
            var paving = CityPortAssetProvider.FindPart(port.gameObject, "COL_AccessRoad").GetComponent<Collider>();
            Assert.That(paving, Is.Not.Null);
            int supported = 0;
            // Compare new membership with the imported collider at diagonals,
            // joins and the capped sharp corner, not another copy of the formula.
            foreach (IReadOnlyList<Vector3> path in new[] { access.PublicPath, access.PublicStreetSpur })
            for (int i = 1; i < path.Count; i++)
            for (int step = 1; step <= 9; step++)
            for (int side = -1; side <= 1; side++)
            {
                Vector3 tangent = path[i] - path[i - 1]; tangent.y = 0; tangent.Normalize();
                Vector3 point = access.World(Vector3.Lerp(path[i - 1], path[i], step * .1f) +
                    new Vector3(tangent.z, 0, -tangent.x) * (side * .6f));
                Assert.That(access.ContainsPublicWalk(point, radius), Is.True, "The walk's side lanes must be connected: " + (point - access.Origin));
                AssertPortWalkFootprint(paving, point, radius);
                supported++;
            }
            Vector3 corner = access.PublicPath[5];
            for (float x = corner.x - 2; x <= corner.x + 2; x += .2f)
            for (float z = corner.z - 2; z <= corner.z + 2; z += .2f)
            {
                Vector3 point = access.World(new Vector3(x, corner.y, z));
                if (!access.ContainsPublicWalk(point, radius)) continue;
                AssertPortWalkFootprint(paving, point, radius);
                supported++;
            }
            Assert.That(supported, Is.GreaterThan(100));
        }

        private static void AssertPortWalkFootprint(Collider paving, Vector3 point, float radius)
        {
            for (int sample = 0; sample < 16; sample++)
            {
                float angle = sample * Mathf.PI / 8;
                Vector3 edge = point + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                Assert.That(paving.Raycast(new Ray(edge + Vector3.up, Vector3.down), out RaycastHit hit, 2), Is.True,
                    "The full admitted capsule needs real imported paving at " + edge);
                Assert.That(hit.normal.y, Is.GreaterThan(.72f));
            }
        }

        private static bool PortAuditFloor(Vector3 point, float seaY, out Vector3 floor)
        {
            floor = default;
            point.y = seaY + 3.6f;
            if (!Physics.Raycast(point, Vector3.down, out RaycastHit hit, 3.5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || hit.normal.y < .72f) return false;
            floor = hit.point;
            return true;
        }

        private static Collider[] PortAuditObstacles(CharacterController hero, Vector3 floor)
        {
            float radius = hero.radius - .03f;
            return Physics.OverlapCapsule(floor + Vector3.up * (hero.stepOffset + radius + .035f),
                floor + Vector3.up * (hero.height - radius), radius,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private static void AuditPortRouteBothWays(string name, IReadOnlyList<Vector3> points,
            CityGameRoot city, CharacterController hero, CityPortController port, PortTraversalReport report)
        {
            for (int direction = 0; direction < 2; direction++)
            for (int mask = 0; mask < 2; mask++)
            {
                int first = direction == 0 ? 0 : points.Count - 1, increment = direction == 0 ? 1 : -1;
                var row = new PortTraversalRoute { name = name + (direction == 0 ? "/forward" : "/reverse"),
                    mode = mask == 0 ? "physics" : "physics+mask", start = points[first], result = "clear" };
                hero.enabled = false;
                hero.transform.position = port.Plan.World(points[first]) + Vector3.up * .12f;
                hero.enabled = true;
                Physics.SyncTransforms();
                for (int settle = 0; settle < 10; settle++) hero.Move(Vector3.down * .08f);
                int stalled = 0;
                for (int index = first + increment; index >= 0 && index < points.Count; index += increment)
                {
                    Vector3 target = port.Plan.World(points[index]);
                    row.target = points[index];
                    for (int step = 0; step < 2000; step++)
                    {
                        Vector3 before = hero.transform.position;
                        Vector3 delta = target - before; delta.y = 0;
                        if (delta.magnitude <= .065f) break;
                        Vector3 desired = before + Vector3.ClampMagnitude(delta, .08f);
                        Vector3 constrained = mask == 0 ? desired :
                            city.World.WalkableArea.Constrain(before, desired, hero.radius);
                        Vector3 motion = constrained - before; motion.y = -.04f;
                        hero.Move(motion);
                        Vector3 moved = hero.transform.position - before; moved.y = 0;
                        stalled = moved.magnitude < .008f ? stalled + 1 : 0;
                        if (stalled < 8 && step < 1999) continue;
                        row.result = Vector2.Distance(new Vector2(desired.x, desired.z),
                            new Vector2(constrained.x, constrained.z)) > .01f ? "mask-stop" : "physical-stop";
                        row.stoppedAt = hero.transform.position - port.Plan.Origin;
                        row.remaining = delta.magnitude;
                        var names = new HashSet<string>();
                        foreach (Collider body in Physics.OverlapSphere(hero.transform.position + Vector3.up * .85f,
                            1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                            if (body != hero) names.Add(body.name);
                        row.colliders = string.Join(", ", names);
                        break;
                    }
                    if (row.result != "clear") break;
                }
                if (row.result == "clear") row.stoppedAt = hero.transform.position - port.Plan.Origin;
                report.routes.Add(row);
                Debug.Log($"PORT ROUTE {row.name} {row.mode}: {row.result} at {row.stoppedAt:F3}; nearby={row.colliders}");
            }
        }

        private static void ValidatePortTareClearance(CityPortController port, CityPortCrew crew)
        {
            // The caller advances the real journey densely. This check must
            // never seek its clocks or reconstruct the social state.
            if (port.Snapshot.Stage != CityPortCycleStage.Prepare && port.Snapshot.Stage != CityPortCycleStage.Secure &&
                port.Snapshot.Stage != CityPortCycleStage.Unmoor) return;
            var body = crew.ShoreWorker.GetComponent<CapsuleCollider>();
            Assert.That(body, Is.Not.Null);
            int checkedSolids = 0;
            foreach (var solid in port.Dock.GetComponentsInChildren<MeshCollider>())
                if (solid.name == "DockVisible__Tare" || solid.name.StartsWith("COL_Awning", StringComparison.Ordinal))
                {
                    checkedSolids++;
                    bool overlap = Physics.ComputePenetration(body, body.transform.position, body.transform.rotation,
                        solid, solid.transform.position, solid.transform.rotation, out _, out float depth);
                    Assert.That(overlap && depth > .005f, Is.False,
                        $"Shore worker crosses {solid.name} during {port.Snapshot.Stage} at {port.Snapshot.SecondsInStage:F1}s ({depth:F3}m).");
                }
            Assert.That(checkedSolids, Is.GreaterThanOrEqualTo(2), "Check the actual tare and its supporting awning.");
        }

        private static void ValidatePortPhysicalBodies(CityGameRoot city, CityPortController port, CityPortCrew crew)
        {
            var cannery = city.Cannery;
            Assert.That(cannery, Is.Not.Null, "Stored port fish belongs to the finite supply owner.");
            var hero = city.Player.GameObject.GetComponent<CharacterController>();
            double savedPort = port.ElapsedSeconds, savedLife = crew.LifeElapsedSeconds, savedSupply = cannery.WorkingSeconds;
            bool forcePort = port.ForcePresentation, forceSupply = cannery.ForcePresentation;
            Transform observer = port.PresentationObserver;
            var far = new GameObject("Port collision distance probe");
            try
            {
                port.ForcePresentation = cannery.ForcePresentation = true;
                port.ApplyAt(CityPortCycle.UnloadStartSeconds + CityPortCycle.LandedAtSeconds + .5d, 0f);
                crew.ApplyAt(port.ElapsedSeconds, savedLife + 1000d);
                AssertPortBodyBlocksHero(hero, CityPortAssetProvider.FindPart(port.Dock.gameObject, "DockVisible__Tare").GetComponent<Collider>());
                AssertPortBodyBlocksHero(hero, port.Cargo[0].GetComponent<Collider>());
                var trolley = port.Trolley.GetComponent<Collider>();
                AssertPortBodyBlocksHero(hero, trolley);
                AssertPortBodyBlocksHero(hero, crew.ShoreWorker.GetComponent<Collider>());
                for (int role = 0; role < crew.WorkerCount; role++)
                    Assert.That(crew.GetWorker(role).GetComponent<CapsuleCollider>(), Is.Not.Null);

                cannery.ApplyAt(CanneryTime(cannery, CityFishSupplyStage.LoadFish, 0f));
                var fish = new Collider[CityFishSupplyCycle.HandlingUnits];
                var storedBounds = new Bounds[fish.Length];
                for (int i = 0; i < fish.Length; i++)
                {
                    fish[i] = cannery.transform.Find("Fish handling unit " + i).GetComponent<Collider>();
                    Assert.That(fish[i], Is.Not.Null);
                    Assert.That(fish[i].gameObject.activeInHierarchy, Is.True);
                    storedBounds[i] = fish[i].bounds;
                }
                AssertPortBodyBlocksHero(hero, fish[0]);
                cannery.ApplyAt(TransferTime(cannery, CityFishSupplyStage.LoadFish, 0, .4f));
                Physics.SyncTransforms();
                Assert.That(Vector3.Distance(fish[0].bounds.center, storedBounds[0].center), Is.GreaterThan(.5f),
                    "The same physical fish unit follows the handoff out of storage.");
                AssertPortBodyAbsent(fish[0], storedBounds[0]);
                cannery.ApplyAt(CanneryTime(cannery, CityFishSupplyStage.LoadFinished, 0f));
                for (int i = 0; i < fish.Length; i++)
                {
                    Assert.That(fish[i].gameObject.activeInHierarchy, Is.False, "Consumed fish has no hidden body.");
                    AssertPortBodyAbsent(fish[i], storedBounds[i]);
                }
                foreach (Transform cargo in port.Cargo)
                    Assert.That(cargo.gameObject.activeInHierarchy, Is.False, "Stored cages leave no hidden collision host.");

                Bounds parked = trolley.bounds;
                far.transform.position = port.Plan.Origin + Vector3.one * 1000f;
                port.PresentationObserver = far.transform; port.ForcePresentation = false;
                port.RefreshPresentation(); Physics.SyncTransforms();
                Assert.That(port.ShorePresentationActive, Is.False);
                AssertPortBodyAbsent(trolley, parked);
                port.ForcePresentation = true; port.RefreshPresentation(); Physics.SyncTransforms();
                Assert.That(trolley.enabled, Is.True, "Approach restores the cart at its current logical pose.");
                Debug.Log("PORT COLLISION: real hero blocks on tare/cage/cart/worker/stored fish; finite handoff and distance leave no ghost body.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(far);
                cannery.ForcePresentation = forceSupply; port.PresentationObserver = observer;
                cannery.ApplyAt(savedSupply); port.ForcePresentation = forcePort;
                port.ApplyAt(savedPort, 0f); crew.ApplyAt(savedPort, savedLife); Physics.SyncTransforms();
            }
        }

        private static void AssertPortBodyAbsent(Collider body, Bounds previous)
        {
            Physics.SyncTransforms();
            Assert.That(Array.IndexOf(Physics.OverlapBox(previous.center, previous.extents * .95f,
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore), body), Is.EqualTo(-1),
                body.name + " must not leave a collider at its former visible position.");
        }

        private static void AssertPortBodyBlocksHero(CharacterController hero, Collider body)
        {
            Assert.That(hero, Is.Not.Null); Assert.That(body, Is.Not.Null);
            Assert.That(body.enabled && body.gameObject.activeInHierarchy && !body.isTrigger, Is.True, body.name);
            Assert.That(Physics.GetIgnoreLayerCollision(hero.gameObject.layer, body.gameObject.layer), Is.False);
            Vector3 saved = hero.transform.position;
            bool enabled = hero.enabled, ignoredTarget = Physics.GetIgnoreCollision(hero, body);
            var ignored = new List<Collider>();
            Physics.SyncTransforms();
            Bounds bounds = body.bounds;
            Vector3 direction = body is MeshCollider ? Vector3.forward : body.transform.right;
            direction.y = 0f; direction.Normalize();
            float travel = 2f * (bounds.extents.magnitude + hero.radius + .5f);
            Vector3 start = bounds.center - direction * travel * .5f;
            start.y = bounds.min.y - (hero.center.y - hero.height * .5f) + .02f;
            try
            {
                // Isolate only neighbours in this short sweep. Repeating it
                // with this exact body ignored proves which object stopped us.
                foreach (Collider other in Physics.OverlapBox(start + direction * travel * .5f + Vector3.up,
                    new Vector3(travel + 1f, 3f, travel + 1f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                    if (other != hero && other != body && !Physics.GetIgnoreCollision(hero, other))
                    { Physics.IgnoreCollision(hero, other, true); ignored.Add(other); }
                hero.enabled = true; hero.transform.position = start;
                Physics.IgnoreCollision(hero, body, false); Physics.SyncTransforms();
                hero.Move(direction * travel);
                Assert.That(Vector3.Dot(hero.transform.position - start, direction), Is.LessThan(travel - .2f), body.name);
                hero.transform.position = start;
                Physics.IgnoreCollision(hero, body, true); Physics.SyncTransforms();
                hero.Move(direction * travel);
                Assert.That(Vector3.Dot(hero.transform.position - start, direction), Is.GreaterThan(travel - .05f),
                    body.name + " must be the blocker, rather than an unrelated neighbouring wall.");
            }
            finally
            {
                hero.transform.position = saved; hero.enabled = enabled;
                Physics.IgnoreCollision(hero, body, ignoredTarget);
                foreach (Collider other in ignored) Physics.IgnoreCollision(hero, other, false);
                Physics.SyncTransforms();
            }
        }
    }
}
