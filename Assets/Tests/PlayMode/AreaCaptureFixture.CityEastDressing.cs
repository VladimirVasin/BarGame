using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private static void VerifyEastDressing(CityGameRoot city, CityEastExitPlan exit, CityMapCityTeleportGround landing)
        {
            GameObject root = GameObject.Find(CityEastExitDressingWorldBuilder.RootName);
            Assert.That(root, Is.Not.Null);
            CityEastExitDressingPlan dressing = CityEastExitDressingPlan.Create(exit);
            Assert.That(root.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Animator>(true), Is.Empty);
            VerifyEastFenceDistribution(root, dressing, exit);
            foreach (CityEastExitDressingSolid solid in dressing.Solids)
            {
                Vector2 point = new Vector2(solid.Position.x, solid.Position.z);
                Assert.That(landing.TryResolveStandingPosition(point, out _), Is.False,
                    "Map arrival must not enter new checkpoint furniture or raised ground: " + solid.Id);
            }

            CheckSize("Booth Shelter", new Vector3(3.6f, 2.65f, 3.2f), new Vector3(.15f, .5f, .15f));
            CheckSize("Shelter Bench", new Vector3(1.9f, .882f, .62f), new Vector3(.06f, .12f, .06f));
            CheckSize("Booth Utility Cabinet", new Vector3(.82f, 1.45f, .54f), new Vector3(.06f, .15f, .06f));
            CheckSize("Shed Repair Reserve", new Vector3(2f, .45f, .8f), new Vector3(.03f, .20f, .03f));
            CityEastExitDressingPart inspection = dressing.Parts.First(part => part.Assembly == "DrainInspection");
            CheckSize(inspection.Id, new Vector3(1.3f, .12f, .85f), new Vector3(.03f, .20f, .03f));
            Transform bench = root.GetComponentsInChildren<Transform>(true).Single(part => part.name == "Shelter Bench");
            Assert.That(Vector3.Dot(bench.rotation * Vector3.back, Vector3.left), Is.GreaterThan(.99f),
                "The bench faces the open western approach.");
            Assert.That(bench.position.x, Is.EqualTo(exit.CheckpointPosition.x - 4.85f).Within(.01f));
            Assert.That(bench.position.z, Is.EqualTo(exit.CheckpointPosition.z - 11.4f).Within(.01f));

            MeshCollider[] support = city.World.Root.GetComponentsInChildren<MeshCollider>(true)
                .Where(collider => collider.name == CityFringeYardGroundWorldBuilder.GenericGroundObjectName ||
                    collider.name == CityChurchGroundWorldBuilder.ObjectName ||
                    collider.transform.IsChildOf(city.World.EastExit.Root.transform) &&
                    collider.name.StartsWith("EEX_Road_", StringComparison.Ordinal)).ToArray();
            Assert.That(support, Is.Not.Empty);
            MeshFilter[] embedded = city.World.Root.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.sharedMesh != null && filter.sharedMesh.name.EndsWith(
                    CityEastExitDressingWorldBuilder.EmbeddedGroundMeshSuffix, StringComparison.Ordinal)).ToArray();
            Assert.That(embedded.Any(filter => filter.name == CityFringeYardGroundWorldBuilder.GenericGroundObjectName), Is.True);
            foreach (MeshFilter filter in embedded)
            {
                Mesh mesh = filter.sharedMesh;
                int slot = mesh.subMeshCount - 1;
                int[] triangles = mesh.GetTriangles(slot);
                Assert.That(triangles, Is.Not.Empty, "The embedded post surface needs actual ground faces.");
                var properties = new MaterialPropertyBlock(); filter.GetComponent<Renderer>().GetPropertyBlock(properties, slot);
                Assert.That(properties.GetTexture("_BaseMap"), Is.Not.Null);
                Vector3[] vertices = mesh.vertices;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 center = filter.transform.TransformPoint((vertices[triangles[index]] +
                        vertices[triangles[index + 1]] + vertices[triangles[index + 2]]) / 3f);
                    float ground = float.NegativeInfinity;
                    foreach (MeshCollider collider in support)
                        if (collider.Raycast(new Ray(center + Vector3.up, Vector3.down), out RaycastHit hit, 2f))
                            ground = Mathf.Max(ground, hit.point.y);
                    Assert.That(ground, Is.EqualTo(center.y).Within(.005f),
                        "Gravel is the standing ground itself, without a second hovering surface: " + filter.name + " at " + center);
                }
            }
            float minimumClearance = float.PositiveInfinity;
            float maximumClearance = float.NegativeInfinity;
            // Use the actual rendered/colliding support, not the analytic
            // terrain function: distinct triangulations used to intersect
            // between otherwise-correct vertices. Check face interiors too.
            foreach (CityEastExitDressingPart part in dressing.Parts)
            {
                if (CityEastExitDressingWorldBuilder.IsEmbeddedGroundPart(part))
                {
                    Assert.That(root.GetComponentsInChildren<Transform>(true).Any(item => item.name == part.Id), Is.False,
                        "The apron and post traces are material regions of the actual ground, never floating overlays: " + part.Id);
                    continue;
                }
                if (part.Assembly != "GravelPatch" && part.Assembly != "RoadRepair" && part.Assembly != "DryDrain" &&
                    part.Assembly != "CanopyApron" && part.Assembly != "FenceToe") continue;
                Transform placed = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == part.Id);
                foreach (MeshFilter filter in placed.GetComponentsInChildren<MeshFilter>(true))
                {
                    Vector3[] vertices = filter.sharedMesh.vertices;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        vertices[i] = filter.transform.TransformPoint(vertices[i]);
                        CheckClearance(vertices[i], part.Id);
                    }
                    int[] triangles = filter.sharedMesh.triangles;
                    for (int i = 0; i < triangles.Length; i += 3)
                        CheckClearance((vertices[triangles[i]] + vertices[triangles[i + 1]] +
                            vertices[triangles[i + 2]]) / 3f, part.Id);
                }
            }

            // Sweep the actual collision placed in the environment through both
            // authored duty routes, including the return, before moving the actors.
            CityEastGuardPlan duty = city.EastGuards.Plan;
            for (int actor = 0; actor < 2; actor++)
            for (int waypoint = 1; waypoint < 5; waypoint++)
            {
                Vector3 from = duty.Target(actor, waypoint - 1);
                Vector3 to = duty.Target(actor, waypoint);
                Vector3 delta = Vector3.ProjectOnPlane(to - from, Vector3.up);
                foreach (RaycastHit hit in Physics.CapsuleCastAll(from + Vector3.up * .36f,
                    from + Vector3.up * 1.59f, .31f, delta.normalized, delta.magnitude,
                    ~0, QueryTriggerInteraction.Ignore))
                    Assert.That(hit.collider.transform.IsChildOf(city.World.Root.transform) &&
                        !hit.collider.transform.IsChildOf(city.EastGuards.transform) &&
                        !hit.collider.transform.IsChildOf(city.Player.GameObject.transform) &&
                        hit.normal.y < .7f, Is.False,
                        "Checkpoint surroundings block duty " + actor + "/" + waypoint +
                        " from " + from + " to " + to + ": " + hit.collider.name + " at " + hit.point);
            }

            Debug.Log("EAST DRESSING: imported metre scale, passive furniture, map arrivals and physical duty-route clearance verified; " +
                "rendered ground clearance=" + minimumClearance + ".." + maximumClearance + " m.");

            void CheckClearance(Vector3 world, string id)
            {
                float ground = float.NegativeInfinity;
                var ray = new Ray(world + Vector3.up, Vector3.down);
                foreach (MeshCollider collider in support)
                    if (collider.Raycast(ray, out RaycastHit hit, 2f)) ground = Mathf.Max(ground, hit.point.y);
                Assert.That(float.IsNegativeInfinity(ground), Is.False,
                    "Ground dressing must have an actual supporting mesh beneath it: " + id + " at " + world);
                float clearance = world.y - ground;
                minimumClearance = Mathf.Min(minimumClearance, clearance);
                maximumClearance = Mathf.Max(maximumClearance, clearance);
                Assert.That(clearance, Is.InRange(.0015f, .125f),
                    "Thin dressing must clear its rendered support without floating: " + id + " at " + world);
            }

            void CheckSize(string id, Vector3 expected, Vector3 tolerance)
            {
                Transform placed = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == id);
                bool first = true;
                Bounds bounds = default;
                foreach (MeshFilter filter in placed.GetComponentsInChildren<MeshFilter>(true))
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 local = placed.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    if (first) { bounds = new Bounds(local, Vector3.zero); first = false; }
                    else bounds.Encapsulate(local);
                }
                Assert.That(first, Is.False, id + " has no visible authored geometry.");
                for (int axis = 0; axis < 3; axis++)
                    Assert.That(bounds.size[axis], Is.EqualTo(expected[axis]).Within(tolerance[axis]),
                        id + " lost its actual placed metre scale.");
            }
        }

        private static void VerifyEastFenceDistribution(GameObject root, CityEastExitDressingPlan dressing,
            CityEastExitPlan exit)
        {
            var plants = new HashSet<string>(StringComparer.Ordinal)
            {
                "Shrub", "LowShrub", "DryGrass", "CreepingScrub", "BranchShrub", "MattedGrass", "TallWeeds"
            };
            ILookup<string, Transform> placed = root.GetComponentsInChildren<Transform>(true).ToLookup(part => part.name);
            var frontIntervals = new List<Vector2>();
            var southIntervals = new List<Vector2>();
            // This is the usable strip between the street edge and the
            // planting. A clear endpoint is insufficient: preserve its
            // complete length and width, including every terrain-cell join.
            Rect corridor = Rect.MinMaxRect(exit.YardBounds.xMin + 2f, exit.YardBounds.yMin + 1f,
                exit.YardBounds.xMin + 5.5f, exit.NorthYardBounds.yMax - 1f);
            Rect authoredGround = Rect.MinMaxRect(exit.YardBounds.xMin, exit.YardBounds.yMin,
                exit.YardBounds.xMax, exit.NorthYardBounds.yMax);
            foreach (CityEastExitDressingSolid solid in dressing.Solids)
                Assert.That(solid.Footprint.Overlaps(corridor), Is.False,
                    "Landscape must leave the continuous pedestrian strip open: " + solid.Id);
            foreach (CityEastExitDressingPart part in dressing.Parts)
            {
                if (!plants.Contains(part.Assembly)) continue;
                Transform item = placed[part.Id].Single();
                Renderer[] renderers = item.GetComponentsInChildren<Renderer>();
                Assert.That(renderers, Is.Not.Empty, "A plant declaration must have actual visible geometry: " + part.Id);
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers)
                {
                    Assert.That(renderer.enabled && renderer.gameObject.activeInHierarchy, Is.True,
                        "Hidden plants cannot satisfy full-fence coverage: " + part.Id);
                    bounds.Encapsulate(renderer.bounds);
                }
                Rect footprint = Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);
                Assert.That(footprint.Overlaps(corridor), Is.False,
                    "The rendered leaves, not only their root, must clear the pedestrian strip: " + part.Id);
                Assert.That(footprint.xMin, Is.GreaterThanOrEqualTo(authoredGround.xMin - .02f), part.Id);
                Assert.That(footprint.xMax, Is.LessThanOrEqualTo(authoredGround.xMax + .02f), part.Id);
                Assert.That(footprint.yMin, Is.GreaterThanOrEqualTo(authoredGround.yMin - .02f), part.Id);
                Assert.That(footprint.yMax, Is.LessThanOrEqualTo(authoredGround.yMax + .02f), part.Id);
                if (part.Position.x < exit.CheckpointPosition.x && footprint.xMax > corridor.xMax)
                    frontIntervals.Add(new Vector2(footprint.yMin, footprint.yMax));
                if (footprint.yMin < exit.YardBounds.yMin + 6f && footprint.yMax > exit.YardBounds.yMin)
                    southIntervals.Add(new Vector2(footprint.xMin, footprint.xMax));
            }
            CheckGaps(frontIntervals, exit.CheckpointPosition.z + 15f,
                exit.NorthYardBounds.yMax - 4f, "accessible front");
            CheckGaps(southIntervals, exit.CheckpointPosition.x + 3f,
                exit.YardBounds.xMax - 3f, "south return");

            void CheckGaps(List<Vector2> intervals, float start, float end, string name)
            {
                intervals.RemoveAll(interval => interval.y <= start || interval.x >= end);
                Assert.That(intervals, Is.Not.Empty, "The " + name + " needs visible vegetation across its full length.");
                intervals.Sort((first, second) => first.x.CompareTo(second.x));
                float coveredTo = start, largestGap = 0f;
                foreach (Vector2 interval in intervals)
                {
                    float from = Mathf.Max(start, interval.x), to = Mathf.Min(end, interval.y);
                    largestGap = Mathf.Max(largestGap, from - coveredTo);
                    coveredTo = Mathf.Max(coveredTo, to);
                }
                largestGap = Mathf.Max(largestGap, end - coveredTo);
                Assert.That(largestGap, Is.LessThanOrEqualTo(20f),
                    "One decorated pocket must not substitute for the " + name +
                    "; largest empty interval is " + largestGap + " m.");
                Debug.Log("EAST FENCE COVERAGE " + name + ": rendered maximum gap=" + largestGap + " m.");
            }
        }
    }
}
