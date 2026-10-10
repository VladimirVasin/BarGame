using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Keep separately imported joint correction normals on their authored shared surface.</summary>
    public sealed class CharacterJointNormalImporter : AssetPostprocessor
    {
        // This module owns several FBXs; only its lower-body model carries joint skin.
        private const string SeatedManifestPath = "Assets/Resources/HomeToiletSeated/HomeToiletSeated.json";
        public override uint GetVersion() => 5;
        public override int GetPostprocessOrder() => 1000;

        private void OnPreprocessModel()
        {
            if (ReadJoints(out string manifestPath) == null) return;
            context.DependsOnSourceAsset(manifestPath);
            var importer = (ModelImporter)assetImporter;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importBlendShapeNormals = ModelImporterNormals.Calculate;
            importer.normalCalculationMode = ModelImporterNormalCalculationMode.AreaAndAngleWeighted;
            // Old/minimal model metas enable a legacy blend-shape path that
            // recalculates even the imported Basis normals from smoothing
            // groups. That discards the authored shared limb/cap distinction.
            // Pin both serialized switches before the very first model import.
            var settings = new SerializedObject(importer);
            SerializedProperty property = settings.GetIterator();
            bool legacyFound = false, deltaFound = false;
            while (property.Next(true))
            {
                if (property.name == "legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes")
                { property.boolValue = false; legacyFound = true; }
                else if (property.name == "calculateBlendshapeNormalsDeltaFromImportedNormals")
                { property.boolValue = false; deltaFound = true; }
            }
            if (!legacyFound || !deltaFound)
                throw new InvalidOperationException("Pinned model importer lost its authored normal switches: " + assetPath);
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        private void OnPostprocessModel(GameObject model)
        {
            Joints joints = ReadJoints(out _);
            if (joints == null) return;
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .ToDictionary(renderer => renderer.name, StringComparer.Ordinal);
            var meshes = new Dictionary<Mesh, MeshFrames>();
            foreach (Seam seam in joints.seams)
            {
                // Sleeve folds are composed and reconciled by PlayerJacketClothSurface.
                if (seam == null || string.IsNullOrEmpty(seam.corrective_shape) ||
                    (!seam.corrective_shape.StartsWith("JointVolume.", StringComparison.Ordinal) &&
                     !seam.corrective_shape.StartsWith("TrouserKneeFold.", StringComparison.Ordinal))) continue;
                if (seam.renderers == null || seam.renderers.Length != 2 ||
                    !renderers.TryGetValue(seam.renderers[0], out SkinnedMeshRenderer first) ||
                    !renderers.TryGetValue(seam.renderers[1], out SkinnedMeshRenderer second))
                    throw new InvalidOperationException("Joint normal correction lost its named surfaces: " + seam.id);
                // A continuous source can import separate UV vertices at the
                // same point. Its corrective lighting must remain shared too.
                MeshFrames a = Frames(first.sharedMesh, meshes), b = Frames(second.sharedMesh, meshes);
                ShapeFrames shapeA = a.Find(seam.corrective_shape), shapeB = b.Find(seam.corrective_shape);
                if (shapeA.Frames.Length != shapeB.Frames.Length)
                    throw new InvalidOperationException("Joint correction frame counts differ: " + seam.id);
                List<int[]> groups = SharedVertices(first, second, a, b);
                if (groups.Count == 0)
                {
                    if (first == second) continue; // No imported vertex split needs repair.
                    throw new InvalidOperationException("Joint correction has no shared outward surface vertices: " + seam.id);
                }
                Matrix4x4 normalA = first.transform.localToWorldMatrix.inverse.transpose;
                Matrix4x4 normalB = second.transform.localToWorldMatrix.inverse.transpose;
                Matrix4x4 localA = first.transform.localToWorldMatrix.transpose;
                Matrix4x4 localB = second.transform.localToWorldMatrix.transpose;
                for (int frame = 0; frame < shapeA.Frames.Length; frame++)
                {
                    Frame deltaA = shapeA.Frames[frame], deltaB = shapeB.Frames[frame];
                    if (Mathf.Abs(deltaA.Weight - deltaB.Weight) > .0001f)
                        throw new InvalidOperationException("Joint correction frame weights differ: " + seam.id);
                    foreach (int[] group in groups)
                    {
                        Vector3 normal = Vector3.zero;
                        foreach (int vertex in group)
                            normal += vertex < a.Normals.Length ?
                                normalA.MultiplyVector(a.Normals[vertex] + deltaA.Normals[vertex]).normalized :
                                normalB.MultiplyVector(b.Normals[vertex - a.Normals.Length] +
                                    deltaB.Normals[vertex - a.Normals.Length]).normalized;
                        if (!float.IsFinite(normal.sqrMagnitude) || normal.sqrMagnitude < .000001f)
                            throw new InvalidOperationException("Joint correction has an invalid shared normal: " + seam.id);
                        normal.Normalize();
                        foreach (int vertex in group)
                            if (vertex < a.Normals.Length)
                                deltaA.Normals[vertex] = localA.MultiplyVector(normal).normalized - a.Normals[vertex];
                            else
                            {
                                int index = vertex - a.Normals.Length;
                                deltaB.Normals[index] = localB.MultiplyVector(normal).normalized - b.Normals[index];
                            }
                    }
                }
                a.Changed = b.Changed = true;
            }
            foreach (MeshFrames mesh in meshes.Values)
                if (mesh.Changed) mesh.Restore();
        }

        private Joints ReadJoints(out string manifestPath)
        {
            manifestPath = string.Equals(assetPath, Player3DV2CharacterSurfaces.SeatedModelPath, StringComparison.OrdinalIgnoreCase)
                ? SeatedManifestPath : Path.ChangeExtension(assetPath, ".json");
            if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || !File.Exists(manifestPath)) return null;
            string json = File.ReadAllText(manifestPath);
            if (json.IndexOf("\"joint_surfaces\"", StringComparison.Ordinal) < 0) return null;
            Joints joints = JsonUtility.FromJson<Manifest>(json)?.joint_surfaces;
            if (joints == null || joints.contract != "character_joint_surfaces_v1" || joints.seams == null)
                throw new InvalidOperationException("Invalid imported joint normal contract: " + manifestPath);
            return joints;
        }

        private static MeshFrames Frames(Mesh mesh, IDictionary<Mesh, MeshFrames> cache)
        {
            if (mesh == null) throw new InvalidOperationException("Joint correction has no mesh.");
            if (!cache.TryGetValue(mesh, out MeshFrames frames))
            {
                frames = new MeshFrames(mesh);
                cache.Add(mesh, frames);
            }
            return frames;
        }

        private static List<int[]> SharedVertices(SkinnedMeshRenderer first, SkinnedMeshRenderer second,
            MeshFrames a, MeshFrames b)
        {
            const float tolerance = .00005f;
            bool self = first == second;
            var buckets = new Dictionary<Vector3Int, List<int>>();
            var worldB = new Vector3[b.Vertices.Length];
            var normalB = new Vector3[b.Normals.Length];
            Matrix4x4 transformB = second.transform.localToWorldMatrix;
            Matrix4x4 normalTransformB = transformB.inverse.transpose;
            for (int vertex = 0; vertex < worldB.Length; vertex++)
            {
                worldB[vertex] = transformB.MultiplyPoint3x4(b.Vertices[vertex]);
                normalB[vertex] = normalTransformB.MultiplyVector(b.Normals[vertex]).normalized;
                Vector3Int key = Cell(worldB[vertex], tolerance);
                if (!buckets.TryGetValue(key, out List<int> matches)) buckets.Add(key, matches = new List<int>());
                matches.Add(vertex);
            }
            // A self section uses one index space, so every imported duplicate
            // contributes once rather than once on each side of the pair.
            int[] parent = Enumerable.Range(0, self ? a.Vertices.Length : a.Vertices.Length + b.Vertices.Length).ToArray();
            int Root(int value)
            {
                while (parent[value] != value) { parent[value] = parent[parent[value]]; value = parent[value]; }
                return value;
            }
            Matrix4x4 transformA = first.transform.localToWorldMatrix;
            Matrix4x4 normalTransformA = transformA.inverse.transpose;
            for (int vertex = 0; vertex < a.Vertices.Length; vertex++)
            {
                Vector3 point = transformA.MultiplyPoint3x4(a.Vertices[vertex]);
                Vector3 normal = normalTransformA.MultiplyVector(a.Normals[vertex]).normalized;
                Vector3Int cell = Cell(point, tolerance);
                for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                        for (int z = -1; z <= 1; z++)
                        {
                            if (!buckets.TryGetValue(cell + new Vector3Int(x, y, z), out List<int> matches)) continue;
                            foreach (int other in matches)
                                if ((point - worldB[other]).sqrMagnitude <= tolerance * tolerance &&
                                    Vector3.Dot(normal, normalB[other]) > .9995f &&
                                    (!self || a.SkinWeights[vertex].Equals(b.SkinWeights[other])))
                                    parent[Root(vertex)] = Root(self ? other : a.Vertices.Length + other);
                        }
            }
            var groups = new Dictionary<int, List<int>>();
            for (int vertex = 0; vertex < parent.Length; vertex++)
            {
                int root = Root(vertex);
                if (!groups.TryGetValue(root, out List<int> values)) groups.Add(root, values = new List<int>());
                values.Add(vertex);
            }
            return groups.Values.Where(group => group.Count > 1).Select(group => group.ToArray()).ToList();
        }

        private static Vector3Int Cell(Vector3 point, float size) => new Vector3Int(
            Mathf.FloorToInt(point.x / size), Mathf.FloorToInt(point.y / size), Mathf.FloorToInt(point.z / size));

        private sealed class MeshFrames
        {
            private readonly Mesh mesh;
            private readonly ShapeFrames[] shapes;
            internal readonly Vector3[] Vertices, Normals;
            internal readonly BoneWeight[] SkinWeights;
            internal bool Changed;
            internal MeshFrames(Mesh mesh)
            {
                this.mesh = mesh;
                Vertices = mesh.vertices;
                Normals = mesh.normals;
                SkinWeights = mesh.boneWeights;
                if (Normals.Length != Vertices.Length) throw new InvalidOperationException("Joint surface lacks imported normals: " + mesh.name);
                if (SkinWeights.Length != Vertices.Length) throw new InvalidOperationException("Joint surface lacks imported skin weights: " + mesh.name);
                shapes = new ShapeFrames[mesh.blendShapeCount];
                for (int shape = 0; shape < shapes.Length; shape++)
                {
                    var frames = new Frame[mesh.GetBlendShapeFrameCount(shape)];
                    for (int frame = 0; frame < frames.Length; frame++)
                    {
                        var value = new Frame(mesh.vertexCount, mesh.GetBlendShapeFrameWeight(shape, frame));
                        mesh.GetBlendShapeFrameVertices(shape, frame, value.Vertices, value.Normals, value.Tangents);
                        frames[frame] = value;
                    }
                    shapes[shape] = new ShapeFrames(mesh.GetBlendShapeName(shape), frames);
                }
            }
            internal ShapeFrames Find(string name)
            {
                int shape = CharacterJointDeformation.FindShape(mesh, name);
                if (shape < 0) throw new InvalidOperationException("Joint surface lost its correction: " + mesh.name + "/" + name);
                return shapes[shape];
            }
            internal void Restore()
            {
                mesh.ClearBlendShapes();
                foreach (ShapeFrames shape in shapes)
                    foreach (Frame frame in shape.Frames)
                        mesh.AddBlendShapeFrame(shape.Name, frame.Weight, frame.Vertices, frame.Normals, frame.Tangents);
            }
        }

        private sealed class ShapeFrames
        {
            internal readonly string Name;
            internal readonly Frame[] Frames;
            internal ShapeFrames(string name, Frame[] frames) { Name = name; Frames = frames; }
        }

        private sealed class Frame
        {
            internal readonly float Weight;
            internal readonly Vector3[] Vertices, Normals, Tangents;
            internal Frame(int count, float weight)
            { Weight = weight; Vertices = new Vector3[count]; Normals = new Vector3[count]; Tangents = new Vector3[count]; }
        }

        [Serializable] private sealed class Manifest { public Joints joint_surfaces; }
        [Serializable] private sealed class Joints { public string contract; public Seam[] seams; }
        [Serializable] private sealed class Seam { public string id, corrective_shape; public string[] renderers; }
    }
}
