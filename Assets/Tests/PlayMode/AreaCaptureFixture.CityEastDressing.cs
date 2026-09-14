using System;
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
            foreach (CityEastExitDressingSolid solid in dressing.Solids)
            {
                Vector2 point = new Vector2(solid.Position.x, solid.Position.z);
                Assert.That(landing.TryResolveStandingPosition(point, out _), Is.False,
                    "Map arrival must not enter new checkpoint furniture or raised ground: " + solid.Id);
            }

            CheckSize("Booth Shelter", new Vector3(3.6f, 2.65f, 3.2f), new Vector3(.15f, .5f, .15f));
            CheckSize("Shelter Bench", new Vector3(1.9f, .882f, .62f), new Vector3(.06f, .12f, .06f));
            CheckSize("Booth Utility Cabinet", new Vector3(.82f, 1.45f, .54f), new Vector3(.06f, .15f, .06f));

            MeshCollider[] support = city.World.Root.GetComponentsInChildren<MeshCollider>(true)
                .Where(collider => collider.name == "Yard Ground" ||
                    collider.transform.IsChildOf(city.World.EastExit.Root.transform) &&
                    collider.name.StartsWith("EEX_Road_", StringComparison.Ordinal)).ToArray();
            Assert.That(support, Is.Not.Empty);
            float minimumClearance = float.PositiveInfinity;
            float maximumClearance = float.NegativeInfinity;
            // Use the actual rendered/colliding support, not the analytic
            // terrain function: distinct triangulations used to intersect
            // between otherwise-correct vertices. Check face interiors too.
            foreach (CityEastExitDressingPart part in dressing.Parts)
            {
                if (part.Assembly != "GravelPatch" && part.Assembly != "RoadRepair" && part.Assembly != "DryDrain") continue;
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
    }
}
