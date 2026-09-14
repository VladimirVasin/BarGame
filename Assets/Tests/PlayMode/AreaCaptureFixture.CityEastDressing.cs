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

            // Thin authored markings follow the actual terrain at every vertex;
            // a correct root point alone would still let their ends float above a slope.
            foreach (CityEastExitDressingPart part in dressing.Parts)
            {
                if (part.Assembly != "GravelPatch" && part.Assembly != "RoadRepair" && part.Assembly != "DryDrain") continue;
                Transform placed = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == part.Id);
                foreach (MeshFilter filter in placed.GetComponentsInChildren<MeshFilter>(true))
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 world = filter.transform.TransformPoint(vertex);
                    Vector2 xz = new Vector2(world.x, world.z);
                    float ground = exit.RoadBounds.Contains(xz) ? exit.SampleRoadTop(world.x) : exit.SampleGroundTop(xz);
                    Assert.That(world.y - ground, Is.InRange(-.035f, .125f),
                        "Ground dressing detaches from the standing surface: " + part.Id);
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
                    Assert.That(hit.collider.transform.IsChildOf(root.transform) && hit.normal.y < .7f, Is.False,
                        "New dressing blocks the physical duty route: " + hit.collider.name);
            }

            Debug.Log("EAST DRESSING: imported metre scale, passive furniture, map arrivals and physical duty-route clearance verified.");

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
