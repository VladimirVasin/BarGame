using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class VillageWindowsBrookAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
            new VillageArtAssetsSetup().Setup();
#if UNITY_EDITOR
            Type setup = Type.GetType("BarPromenade.Editor.MothersHouseInteriorAssetSetup, BarPromenade.Editor", true);
            setup.GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused paired-scene window and brook reproduction.")]
        [PrebuildSetup(typeof(VillageWindowsBrookAssetsSetup))]
        public IEnumerator VillageWindowsAndBrook()
        {
            GameSessionState.TryStartGameTimeFromWake();
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime(100f);
            AlpineVillageRoot village = null;
            float minimumWaterGap = float.PositiveInfinity;
            float maximumWaterGap = 0f;
            float minimumBedGap = float.PositiveInfinity;
            var outsideIds = new HashSet<string>();
            yield return Capture(SceneIds.AlpineVillage,
                () => village = UnityEngine.Object.FindAnyObjectByType<AlpineVillageRoot>(),
                () => WindowBrookOutsideShots(village, outsideIds, out minimumWaterGap, out maximumWaterGap, out minimumBedGap));

            MothersHouseInteriorRoot interior = null;
            yield return Capture(SceneIds.MothersHouseInterior,
                () => interior = UnityEngine.Object.FindAnyObjectByType<MothersHouseInteriorRoot>(),
                () => WindowInsideShots(interior, outsideIds));
            // Keep the diagnostic frames even when the ground still cuts water.
            Assert.That(minimumWaterGap, Is.GreaterThan(.06f),
                "The brook needs clearance for its raised bed skin and wave trough; see the recorded worst point.");
            Assert.That(maximumWaterGap, Is.LessThan(.45f),
                "Carving below the ribbon must not turn the shallow brook into a deep trench.");
            Assert.That(minimumBedGap, Is.GreaterThan(.008f),
                "Snow must stay below the stone bed and banks, including their triangle interiors.");
        }

        private static Shot[] WindowBrookOutsideShots(AlpineVillageRoot root,
            HashSet<string> outsideIds, out float minimumWaterGap, out float maximumWaterGap,
            out float minimumBedGap)
        {
            var shots = new List<Shot>();
            AlpineVillagePlan plan = root.Plan;
            AlpineVillagePlotDescriptor house = plan.MothersHouse;
            Vector3 right = Vector3.Cross(Vector3.up, house.Facing).normalized;
            Vector3 Local(float x, float y, float z) => house.GroundCenter +
                right * x + Vector3.up * y + house.Facing * z;
            Transform[] objects = root.World.Root.GetComponentsInChildren<Transform>(true);
            foreach (MothersHouseWindowDescriptor window in MothersHouseInteriorLayoutPlanner.Windows)
            {
                Transform group = Array.Find(objects,
                    candidate => candidate.name == "Mothers House Window - " + window.StableId);
                Assert.That(group, Is.Not.Null, window.StableId);
                var glass = group.Find("Lit Window").GetComponent<Renderer>();
                Vector3 delta = glass.bounds.center - house.GroundCenter;
                Assert.That(delta.y, Is.EqualTo(window.CenterPosition.y).Within(.06f), window.StableId);
                bool endWall = window.Wall == MothersHouseWindowWall.North ||
                    window.Wall == MothersHouseWindowWall.South;
                float across = Vector3.Dot(delta, endWall ? right : house.Facing);
                float expected = endWall ? -.36f - window.CenterPosition.x : -window.CenterPosition.z;
                Assert.That(across, Is.EqualTo(expected).Within(.06f), window.StableId);
                Assert.That(outsideIds.Add(window.StableId), Is.True);
            }
            Assert.That(outsideIds.Count, Is.EqualTo(16));
            shots.Add(Shot.At("50-mother-south-all-windows", Local(0, 2.7f, 13), Local(0, 3.2f, 3.735f), 61));
            shots.Add(Shot.At("51-mother-north-all-windows", Local(0, 2.7f, -13), Local(0, 3.2f, -3.735f), 61));
            shots.Add(Shot.At("52-mother-timber-side-windows", Local(-12.5f, 2.7f, 0), Local(-5.06f, 3.2f, 0), 68));
            shots.Add(Shot.At("53-mother-masonry-side-windows", Local(12.5f, 2.7f, 0), Local(5.17f, 3.2f, 0), 68));

            AlpineVillageBrookPlan brook = plan.Brook;
            shots.Add(Shot.At("54-spring-catch-and-natural-outlet",
                brook.ApproachPosition + brook.LedgeFacing * 2.3f +
                brook.BowlOutletDirection * 1.8f + Vector3.up * 1.9f,
                brook.BowlCenter + Vector3.up * .1f, 62, 20));
            foreach (int index in new[] { 10, brook.Samples.Count / 3, brook.Samples.Count * 2 / 3 })
            {
                AlpineVillageBrookSample sample = brook.Samples[Mathf.Clamp(index, 1, brook.Samples.Count - 2)];
                Vector3 eye = sample.Position + sample.Right * 2.6f + Vector3.up * 1.65f;
                Vector3 aim = sample.Position + Vector3.up * .05f;
                shots.Add(Shot.At($"55-brook-{index:000}-a", eye, aim, 64, 20));
                shots.Add(Shot.At($"55-brook-{index:000}-b", eye + sample.Right * .12f, aim, 64, 30));
            }
            minimumWaterGap = MeasureBrookGroundGap(root, out maximumWaterGap, out minimumBedGap);
            return shots.ToArray();
        }

        private static float MeasureBrookGroundGap(AlpineVillageRoot root, out float maximum,
            out float minimumBedGap)
        {
            Transform world = root.World.Root.transform;
            MeshCollider ground = world.Find("Village Ground").GetComponent<MeshCollider>();
            Debug.Log($"Brook capture terrain: {ground.sharedMesh.vertexCount} vertices.");
            Rect terrainBounds = root.Plan.TerrainMeshBounds;
            float samplerError = 0f;
            for (int x = 0; x < 16; x++)
            for (int z = 0; z < 16; z++)
            {
                var point = new Vector2(
                    Mathf.Lerp(terrainBounds.xMin, terrainBounds.xMax, (x + .37f) / 16f),
                    Mathf.Lerp(terrainBounds.yMin, terrainBounds.yMax, (z + .63f) / 16f));
                float expected = AlpineVillageTerrainSampler.SampleMeshHeight(root.Plan, point);
                Assert.That(ground.Raycast(new Ray(new Vector3(point.x, expected + 20f, point.y),
                    Vector3.down), out RaycastHit hit, 100f), Is.True);
                samplerError = Mathf.Max(samplerError, Mathf.Abs(expected - hit.point.y));
            }
            Assert.That(samplerError, Is.LessThan(.002f),
                "Ground fitting must use the actual terrain grid, including transitions in sampling pitch.");
            Transform bed = world.Find("Village Spring/Spring Brook Bed");
            Assert.That(bed.GetComponent<MeshCollider>(), Is.Not.Null,
                "The visible shallow bed and banks must also support the player's feet.");
            Assert.That(bed.GetComponent<MeshCollider>().sharedMesh,
                Is.SameAs(bed.GetComponent<MeshFilter>().sharedMesh));
            MeshFilter water = world.Find("Village Spring/Spring Brook Water").GetComponent<MeshFilter>();
            Vector3[] vertices;
            int[] triangles;
#if UNITY_EDITOR
            // The water releases its CPU copy after upload. Editor read-only
            // mesh access inspects that real surface without changing runtime
            // readability or the shared water factory.
            using (Mesh.MeshDataArray data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(water.sharedMesh))
            using (var positions = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp))
            using (var indices = new Unity.Collections.NativeArray<int>(data[0].GetSubMesh(0).indexCount, Unity.Collections.Allocator.Temp))
            {
                data[0].GetVertices(positions);
                data[0].GetIndices(indices, 0);
                vertices = positions.ToArray();
                triangles = indices.ToArray();
            }
#else
            vertices = water.sharedMesh.vertices;
            triangles = water.sharedMesh.triangles;
#endif
            float minimum = float.PositiveInfinity;
            maximum = 0f;
            Vector3 worst = Vector3.zero;
            Vector3 deepest = Vector3.zero;
            int probes = 0;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector3 a = vertices[triangles[index]], b = vertices[triangles[index + 1]], c = vertices[triangles[index + 2]];
                foreach (Vector3 local in new[] { a, (a + b + c) / 3f,
                             (a + b) * .5f, (b + c) * .5f, (c + a) * .5f })
                {
                    Vector3 point = water.transform.TransformPoint(local);
                    if (!ground.Raycast(new Ray(point + Vector3.up * 20f, Vector3.down), out RaycastHit hit, 100f))
                        continue;
                    probes++;
                    float gap = point.y - hit.point.y;
                    if (gap < minimum) { minimum = gap; worst = point; }
                    if (gap > maximum) { maximum = gap; deepest = point; }
                }
            }
            Assert.That(probes, Is.GreaterThan(100));
            Debug.Log($"Brook actual ground clearance: {minimum:0.000000} m at {worst.ToString("F4")}; {probes} probes.");
            Debug.Log($"Brook deepest ground clearance: {maximum:0.000000} m at {deepest.ToString("F4")}.");
            minimumBedGap = float.PositiveInfinity;
            Mesh bedMesh = bed.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] bedVertices = bedMesh.vertices;
            int[] bedTriangles = bedMesh.triangles;
            Vector3 bedWorst = Vector3.zero;
            for (int index = 0; index < bedTriangles.Length; index += 3)
            {
                Vector3 a = bedVertices[bedTriangles[index]],
                    b = bedVertices[bedTriangles[index + 1]], c = bedVertices[bedTriangles[index + 2]];
                foreach (Vector3 local in new[] { a, (a + b + c) / 3f,
                             (a + b) * .5f, (b + c) * .5f, (c + a) * .5f })
                {
                    Vector3 point = bed.TransformPoint(local);
                    if (ground.Raycast(new Ray(point + Vector3.up * 20f, Vector3.down), out RaycastHit hit, 100f))
                    {
                        float gap = point.y - hit.point.y;
                        if (gap < minimumBedGap) { minimumBedGap = gap; bedWorst = point; }
                    }
                }
            }
            Debug.Log($"Brook bed/bank ground clearance: {minimumBedGap:0.000000} m at {bedWorst.ToString("F4")}.");
            return minimum;
        }

        private static Shot[] WindowInsideShots(MothersHouseInteriorRoot root, HashSet<string> outsideIds)
        {
            var shots = new List<Shot>(MothersHouseShots(root));
            Transform[] objects = root.Room.GetComponentsInChildren<Transform>(true);
            foreach (MothersHouseWindowDescriptor window in MothersHouseInteriorLayoutPlanner.Windows)
            {
                Assert.That(outsideIds.Contains(window.StableId), Is.True, window.StableId);
                Transform glass = Array.Find(objects, candidate => candidate.name == window.GlassPartName);
                Transform frame = Array.Find(objects, candidate => candidate.name == window.FramePartName);
                Assert.That(glass, Is.Not.Null, window.StableId);
                Assert.That(frame, Is.Not.Null, window.StableId);
                Vector3 center = root.Room.InverseTransformPoint(glass.GetComponent<Renderer>().bounds.center);
                Assert.That(Vector3.Distance(center, window.CenterPosition), Is.LessThan(.12f), window.StableId);
                Vector3 worldCenter = root.Room.TransformPoint(window.CenterPosition);
                Vector3 normal = root.Room.TransformDirection(window.Outward);
                shots.Add(Shot.At("10-window-" + window.StableId,
                    worldCenter - normal * 2.1f + Vector3.up * .1f, worldCenter, 62, 1));
            }
            return shots.ToArray();
        }
    }
}
