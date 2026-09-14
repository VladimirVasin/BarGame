using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private static void VerifyEastReliefAndTrees(CityGameRoot city, CityEastExitPlan exit,
            CityMapCityTeleportGround landing)
        {
            foreach (CityFringeYardDescriptor yard in city.World.FringeYardPlan.Yards)
                if (yard.Kind == CityFringeYardKind.EastUtilityEdge)
                    Assert.That(yard.Parts.Any(part => part.Kind == CityFringeYardPartKind.ServiceTrack),
                        Is.False, "The redundant longitudinal road must leave the eastern ground entirely.");

            MeshFilter skin = GameObject.Find(CityFringeYardGroundWorldBuilder.GenericGroundObjectName)
                .GetComponent<MeshFilter>();
            MeshCollider support = skin.GetComponent<MeshCollider>();
            Assert.That(support, Is.Not.Null);
            CityEastReliefPlan relief = exit.Relief;
            Assert.That(relief, Is.Not.Null);
            Vector3[] vertices = skin.sharedMesh.vertices.Select(skin.transform.TransformPoint).ToArray();
            int[] triangles = skin.sharedMesh.triangles;

            for (int side = 0; side < 2; side++)
            {
                bool front = side == 0;
                float firstX = front ? exit.YardBounds.xMin + 1f : exit.CheckpointPosition.x + 2f;
                float lastX = front ? exit.YardBounds.xMin + 5f :
                    Mathf.Min(exit.YardBounds.xMax - 3f, exit.CheckpointPosition.x + 32f);
                float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
                Vector2 low = default, high = default;
                for (float x = firstX; x <= lastX; x += front ? 1f : 2f)
                for (float z = exit.CheckpointPosition.z + 13f; z < exit.NorthYardBounds.yMax - 6f; z += 4f)
                {
                    Vector2 point = new Vector2(x, z);
                    float offset = relief.SampleOffset(point);
                    Assert.That(Mathf.Abs(offset), Is.LessThanOrEqualTo(front ? .231f : .621f),
                        "The low municipal edge cannot become a steep embankment at " + point);
                    if (offset < minimum) { minimum = offset; low = point; }
                    if (offset > maximum) { maximum = offset; high = point; }
                }
                float requiredRange = front ? .12f : .30f;
                Assert.That(maximum - minimum, Is.GreaterThan(requiredRange),
                    "Both sides need actual gentle variation, not a flat ground with a textured mound.");
                float lowMeasured = Ground(low) - (exit.SampleGroundTop(low) - minimum);
                float highMeasured = Ground(high) - (exit.SampleGroundTop(high) - maximum);
                Assert.That(highMeasured - lowMeasured, Is.GreaterThan(requiredRange - .05f),
                    "The planned relief must be visible in the terrain triangles on the " + (front ? "public" : "closed") + " side.");
                Debug.Log("EAST RELIEF " + (front ? "public" : "closed") + ": actual relative range=" +
                    lowMeasured + ".." + highMeasured + " m.");
            }

            // These edges are shared by unchanged street, fence and garden
            // owners. New relief may not lift their footings or open a seam.
            for (float z = exit.YardBounds.yMin; z <= exit.NorthYardBounds.yMax; z += 12f)
            foreach (float x in new[] { exit.YardBounds.xMin, exit.CheckpointPosition.x, exit.YardBounds.xMax })
                Assert.That(relief.SampleOffset(new Vector2(x, z)), Is.EqualTo(0f).Within(.0001f));
            foreach (float z in new[] { exit.YardBounds.yMin, exit.NorthYardBounds.yMax })
            for (float x = exit.YardBounds.xMin; x <= exit.YardBounds.xMax; x += 6f)
                Assert.That(relief.SampleOffset(new Vector2(x, z)), Is.EqualTo(0f).Within(.0001f));
            for (int i = 0; i < exit.Fences.Count; i += 8)
            {
                Vector3 footing = exit.Fences[i].Start;
                Assert.That(Ground(new Vector2(footing.x, footing.z)), Is.EqualTo(footing.y).Within(.03f),
                    "Fence segments must stay planted in their visible ground.");
            }
            foreach (float z in exit.Swale.CrossingZ)
            foreach (float x in new[] { exit.YardBounds.xMin + 2.5f, exit.Swale.CenterX(z) })
                Assert.That(relief.SampleOffset(new Vector2(x, z)), Is.EqualTo(0f).Within(.0001f),
                    "Existing broad crossings retain their shallow grade.");
            for (float x = exit.RoadBounds.xMin + .1f; x < exit.RoadBounds.xMax; x += 5f)
                Assert.That(relief.SampleOffset(new Vector2(x, exit.CheckpointPosition.z)), Is.EqualTo(0f),
                    "The actual road to the distant city must keep its existing grade.");

            CityEastTreePlan trees = CityEastTreePlan.Create(exit);
            Transform treeRoot = GameObject.Find(CityEastTreeWorldBuilder.RootName).transform;
            Assert.That(treeRoot.childCount, Is.EqualTo(trees.Parts.Count));
            Assert.That(trees.Parts.Count(part => part.IsFront), Is.GreaterThan(0));
            Assert.That(trees.Parts.Count(part => !part.IsFront), Is.GreaterThan(trees.Parts.Count(part => part.IsFront)),
                "The closed ground carries slightly more trees while the public frontage remains open.");
            Assert.That(trees.Parts.Select(part => part.Variant).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(treeRoot.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(treeRoot.GetComponentsInChildren<AudioSource>(true), Is.Empty);
            Assert.That(treeRoot.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(treeRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty);
            CityMiscAssetProvider provider = CityMiscAssetProvider.LoadOrThrow();
            var firstMaterials = new Dictionary<int, Material[]>();
            foreach (CityEastTreePart tree in trees.Parts)
            {
                Transform instance = treeRoot.Find(tree.Id);
                Assert.That(instance, Is.Not.Null, tree.Id);
                Assert.That(Vector2.Distance(new Vector2(instance.position.x, instance.position.z),
                    new Vector2(tree.Position.x, tree.Position.z)), Is.LessThan(.001f), tree.Id);
                Assert.That(Quaternion.Angle(instance.rotation, tree.Rotation), Is.LessThan(.01f), tree.Id);
                MeshFilter[] meshes = instance.GetComponentsInChildren<MeshFilter>(true);
                int expectedParts = CityMiscAssetProvider.GetPartCount(CityMiscKind.ParkTree);
                Assert.That(meshes.Length, Is.EqualTo(expectedParts), tree.Id);
                var expected = Enumerable.Range(0, expectedParts).Select(index =>
                    provider.GetPartOrThrow(CityMiscKind.ParkTree, tree.Variant, index).Mesh).ToArray();
                Assert.That(meshes.Select(mesh => mesh.sharedMesh), Is.EquivalentTo(expected),
                    "Trees must retain the shared rigid Blender meshes: " + tree.Id);
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers.All(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy), Is.True, tree.Id);
                Bounds measured = renderers[0].bounds;
                foreach (Renderer renderer in renderers.Skip(1)) measured.Encapsulate(renderer.bounds);
                Assert.That(measured.size.y, Is.EqualTo(tree.Height).Within(.025f),
                    "Imported unit scale must survive the actual placement: " + tree.Id);
                Assert.That(measured.size.y, Is.InRange(2.95f, 5.05f), tree.Id);
                float ground = Ground(new Vector2(instance.position.x, instance.position.z));
                Assert.That(instance.position.y - ground, Is.InRange(-.14f, .01f),
                    "The trunk must meet its real relief surface, with only a small buried root collar: " + tree.Id);
                Material[] materials = renderers.SelectMany(renderer => renderer.sharedMaterials).ToArray();
                if (firstMaterials.TryGetValue(tree.Variant, out Material[] first))
                    Assert.That(materials, Is.EqualTo(first), "Trees must share materials: " + tree.Id);
                else firstMaterials.Add(tree.Variant, materials);
                Collider[] bodies = instance.GetComponentsInChildren<Collider>(true);
                Assert.That(bodies.Length, Is.EqualTo(1), "Only the visible trunk is solid: " + tree.Id);
                Assert.That(bodies[0], Is.TypeOf<BoxCollider>(), tree.Id);
                Assert.That(bodies[0].transform, Is.SameAs(instance), tree.Id);
                Assert.That(bodies[0].enabled && !bodies[0].isTrigger, Is.True, tree.Id);
                Assert.That(bodies[0].bounds.size.x, Is.LessThan(1f), tree.Id);
                Assert.That(bodies[0].bounds.size.z, Is.LessThan(1f), tree.Id);
                Assert.That(bodies[0].bounds.size.y, Is.LessThan(tree.Height * .6f),
                    "An invisible canopy box must not block the open frontage: " + tree.Id);
                Assert.That(landing.TryResolveStandingPosition(new Vector2(tree.Position.x, tree.Position.z), out _), Is.False,
                    "Map landing must exclude the same solid trunk: " + tree.Id);
                Assert.That(tree.CrownFootprint.Overlaps(exit.ClearanceBounds), Is.False,
                    "The road, booth and patrol sightline stay open: " + tree.Id);
            }
            for (int i = 0; i < trees.Parts.Count; i++)
            for (int j = i + 1; j < trees.Parts.Count; j++)
                Assert.That(trees.Parts[i].CrownFootprint.Overlaps(trees.Parts[j].CrownFootprint), Is.False,
                    "Sparse standalone trees cannot merge into a closed wall of foliage.");
            Debug.Log("EAST TREES: sparse shared Blender silhouettes on both sides, metre scale, actual ground contact and finite trunk/map collision verified.");

            float Ground(Vector2 point)
            {
                float planned = exit.SampleGroundTop(point);
                var ray = new Ray(new Vector3(point.x, planned + 3f, point.y), Vector3.down);
                Assert.That(support.Raycast(ray, out RaycastHit hit, 6f), Is.True,
                    "The real relief needs standing ground at " + point);
                Assert.That(hit.point.y, Is.EqualTo(planned).Within(.035f),
                    "Terrain triangles must resolve the shared relief sampler at " + point);
                // Transition materials replace rendered submeshes while the
                // original terrain collider stays intact. Read the rendered
                // triangles independently; collider triangle indices cannot
                // be used to address that separately refined colour skin.
                float rendered = float.NegativeInfinity;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 a = vertices[triangles[index]], b = vertices[triangles[index + 1]],
                        c = vertices[triangles[index + 2]];
                    if (point.x < Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - .00001f ||
                        point.x > Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + .00001f ||
                        point.y < Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - .00001f ||
                        point.y > Mathf.Max(a.z, Mathf.Max(b.z, c.z)) + .00001f) continue;
                    Vector2 ab = new Vector2(b.x - a.x, b.z - a.z), ac = new Vector2(c.x - a.x, c.z - a.z);
                    Vector2 ap = point - new Vector2(a.x, a.z);
                    float area = ab.x * ac.y - ab.y * ac.x;
                    if (Mathf.Abs(area) < .000001f) continue;
                    float u = (ap.x * ac.y - ap.y * ac.x) / area;
                    float v = (ab.x * ap.y - ab.y * ap.x) / area;
                    if (u < -.00001f || v < -.00001f || u + v > 1.00001f) continue;
                    rendered = Mathf.Max(rendered, a.y + u * (b.y - a.y) + v * (c.y - a.y));
                }
                Assert.That(float.IsNegativeInfinity(rendered), Is.False,
                    "The visible ground must cover the physical support point: " + point);
                Assert.That(rendered, Is.EqualTo(hit.point.y).Within(.03f),
                    "The rendered relief and collider must support the same height: " + point);
                return rendered;
            }
        }
    }
}
