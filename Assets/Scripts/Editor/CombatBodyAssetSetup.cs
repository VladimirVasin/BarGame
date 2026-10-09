using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Restores offline-cut surfaces to production skin, including interpolated cut vertices.</summary>
    public sealed class CombatBodyAssetSetup : AssetPostprocessor
    {
        private const string Folder = "Assets/Resources/CombatGore/";
        private const string HeroSource = "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx";
        private const string NpcSource = "Assets/Resources/VillageLife/StationWorker.fbx";
        private static bool rebinding;
        private static readonly Dictionary<string, Hash128> processedSourceHashes = new Dictionary<string, Hash128>(StringComparer.Ordinal);
        private bool IsBody => assetPath == Folder + "BodyHero.fbx" || assetPath == Folder + "BodyNpc.fbx" ||
            assetPath == Folder + "SkullHero.fbx" || assetPath == Folder + "SkullNpc.fbx";
        private static string SourceFor(string path) => path.EndsWith("Hero.fbx", StringComparison.Ordinal) ? HeroSource : NpcSource;
        public override uint GetVersion() => 8;

        [Serializable]
        private sealed class TessellationManifest
        {
            public TessellationTolerance[] source_tessellation_tolerances;
        }

        [Serializable]
        private sealed class TessellationTolerance
        {
            public string kind;
            public string source;
            public float maximum_cut_tessellation_deviation_m;
        }

        private void OnPreprocessModel()
        {
            if (!IsBody || !(assetImporter is ModelImporter importer)) return;
            context.DependsOnArtifact(SourceFor(assetPath));
            if (assetPath.Contains("Body")) context.DependsOnSourceAsset(Folder + "CombatBody3D.json");
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.importCameras = importer.importLights = importer.addCollider = false;
            importer.importBlendShapes = importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.isReadable = true; importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        private readonly struct Sample
        {
            internal readonly int A, B, C;
            internal readonly Vector3 Barycentric;
            internal Sample(int a, int b, int c, Vector3 barycentric) { A = a; B = b; C = c; Barycentric = barycentric; }
            internal Vector3 Evaluate(Vector3[] values) => values[A] * Barycentric.x + values[B] * Barycentric.y + values[C] * Barycentric.z;
            internal Vector2 Evaluate(Vector2[] values) => values[A] * Barycentric.x + values[B] * Barycentric.y + values[C] * Barycentric.z;
            internal Vector4 Evaluate(Vector4[] values) => values[A] * Barycentric.x + values[B] * Barycentric.y + values[C] * Barycentric.z;
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!IsBody) return;
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceFor(assetPath));
            if (source == null) return; // Source completion below reimports this derivative.
            var originals = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
            foreach (SkinnedMeshRenderer renderer in source.GetComponentsInChildren<SkinnedMeshRenderer>(true)) originals.Add(renderer.name, renderer);
            // Axis baking and geometric origins differ between source renderers.
            // Measure each metadata frame before restoring any mesh vertices.
            Dictionary<string, Matrix4x4> torsoFrames = MeasureTorsoFrames(model, originals);
            var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform bone in model.GetComponentsInChildren<Transform>(true)) bones[bone.name] = bone;
            var allowances = new Dictionary<string, float>(StringComparer.Ordinal);
            if (assetPath.Contains("Body") && File.Exists(Folder + "CombatBody3D.json"))
            {
                TessellationManifest manifest = JsonUtility.FromJson<TessellationManifest>(File.ReadAllText(Folder + "CombatBody3D.json"));
                string kind = assetPath.EndsWith("Hero.fbx", StringComparison.Ordinal) ? "Hero" : "Npc";
                if (manifest.source_tessellation_tolerances != null)
                    foreach (TessellationTolerance value in manifest.source_tessellation_tolerances)
                        if (value.kind == kind) allowances[value.source] = value.maximum_cut_tessellation_deviation_m;
            }
            foreach (SkinnedMeshRenderer piece in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int separator = piece.name.IndexOf("__", StringComparison.Ordinal);
                if (separator < 0 || !originals.TryGetValue(piece.name.Substring(separator + 2), out SkinnedMeshRenderer original))
                    throw new InvalidOperationException("Body derivative lost its original surface: " + piece.name);
                bool exterior = piece.name.StartsWith("Region", StringComparison.Ordinal);
                float measuredWarp = allowances.TryGetValue(original.name, out float value) ? value : 0f;
                RestoreSkin(piece, original, bones, exterior, Mathf.Max(.0007f, measuredWarp * 1.05f + .0001f),
                    torsoFrames.TryGetValue(original.name, out Matrix4x4 torsoFrame) ? torsoFrame : Matrix4x4.identity);
            }
        }

        private static void RestoreSkin(SkinnedMeshRenderer piece, SkinnedMeshRenderer original,
            Dictionary<string, Transform> bones, bool exterior, float maximumProjectionMetres, Matrix4x4 torsoFrame)
        {
            Mesh mesh = piece.sharedMesh, skin = original.sharedMesh;
            if (mesh == null || skin == null) throw new InvalidOperationException("Body derivative lacks mesh.");
            Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            Vector2[] uv = mesh.uv, sourceUv = skin.uv;
            Vector3[] sourceVertices = skin.vertices, sourceNormals = skin.normals;
            Vector4[] sourceTangents = skin.tangents;
            Vector4[] tangents = exterior && sourceTangents.Length == skin.vertexCount ? new Vector4[vertices.Length] : null;
            int[] triangles = skin.triangles;
            BoneWeight[] sourceWeights = skin.boneWeights, weights = new BoneWeight[vertices.Length];
            var samples = new Sample[vertices.Length];
            Matrix4x4 toSource = original.transform.worldToLocalMatrix * piece.transform.localToWorldMatrix;
            Matrix4x4 normalToSource = toSource.inverse.transpose;
            float sourceScale = Mathf.Max(.0001f, original.transform.lossyScale.magnitude / Mathf.Sqrt(3f));
            float distanceTie = 1e-12f / (sourceScale * sourceScale);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 point = toSource.MultiplyPoint3x4(vertices[i]);
                float best = float.PositiveInfinity;
                float bestUv = float.PositiveInfinity;
                Sample closest = default;
                for (int j = 0; j < triangles.Length; j += 3)
                {
                    int a = triangles[j], b = triangles[j + 1], c = triangles[j + 2];
                    Vector3 bary = ClosestBarycentric(point, sourceVertices[a], sourceVertices[b], sourceVertices[c]);
                    Vector3 projected = sourceVertices[a] * bary.x + sourceVertices[b] * bary.y + sourceVertices[c] * bary.z;
                    float distance = (projected - point).sqrMagnitude;
                    // Disambiguate coincident source seams using the authored UV.
                    float uvDistance = exterior && sourceUv.Length == sourceVertices.Length ?
                        ((sourceUv[a] * bary.x + sourceUv[b] * bary.y + sourceUv[c] * bary.z) - uv[i]).sqrMagnitude : 0f;
                    if (distance > best + distanceTie ||
                        Mathf.Abs(distance - best) <= distanceTie && uvDistance >= bestUv) continue;
                    best = distance; bestUv = uvDistance; closest = new Sample(a, b, c, bary);
                }
                float worldDistance = original.transform.TransformVector(closest.Evaluate(sourceVertices) - point).magnitude;
                if (exterior && worldDistance > maximumProjectionMetres)
                    throw new InvalidOperationException("Offline body cut left its measured source tessellation envelope: " + piece.name +
                        ": " + worldDistance + " m; authored allowance=" + maximumProjectionMetres + " m");
                samples[i] = closest;
                vertices[i] = exterior ? closest.Evaluate(sourceVertices) : point;
                normals[i] = exterior ? closest.Evaluate(sourceNormals).normalized : normalToSource.MultiplyVector(normals[i]).normalized;
                if (tangents != null)
                {
                    Vector4 authoredTangent = closest.Evaluate(sourceTangents);
                    Vector3 tangent = new Vector3(authoredTangent.x, authoredTangent.y, authoredTangent.z);
                    tangent = (tangent - normals[i] * Vector3.Dot(normals[i], tangent)).normalized;
                    tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, authoredTangent.w < 0f ? -1f : 1f);
                }
                if (exterior && sourceUv.Length == sourceVertices.Length) uv[i] = closest.Evaluate(sourceUv);
                weights[i] = InterpolateWeights(sourceWeights[closest.A], sourceWeights[closest.B], sourceWeights[closest.C], closest.Barycentric);
            }
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.boneWeights = weights;
            if (tangents != null) mesh.tangents = tangents;
            else if (exterior) mesh.RecalculateTangents();
            mesh.bindposes = skin.bindposes;
            RestoreTorsoCells(mesh, torsoFrame);
            if (exterior)
            {
                // Offline source correspondence follows per-instance cloth without
                // a runtime nearest-triangle search or a second character mesh.
                var indices = new List<Vector4>(samples.Length);
                var barycentric = new List<Vector4>(samples.Length);
                foreach (Sample sample in samples)
                {
                    indices.Add(new Vector4(sample.A, sample.B, sample.C, 0f));
                    barycentric.Add(new Vector4(sample.Barycentric.x, sample.Barycentric.y, sample.Barycentric.z, 1f));
                }
                mesh.SetUVs(2, indices); mesh.SetUVs(3, barycentric);
            }
            mesh.ClearBlendShapes();
            bool interiorHand = piece.name.StartsWith("BoneRegion7__", StringComparison.Ordinal) ||
                piece.name.StartsWith("BoneRegion10__", StringComparison.Ordinal) ||
                piece.name.StartsWith("FleshRegion7Patch", StringComparison.Ordinal) ||
                piece.name.StartsWith("FleshRegion10Patch", StringComparison.Ordinal);
            if (exterior || interiorHand)
            {
                var sourceDelta = new Vector3[skin.vertexCount]; var sourceNormal = new Vector3[skin.vertexCount];
                var delta = new Vector3[vertices.Length]; var normalDelta = new Vector3[vertices.Length];
                for (int shape = 0; shape < skin.blendShapeCount; shape++)
                    for (int frame = 0; frame < skin.GetBlendShapeFrameCount(shape); frame++)
                    {
                        skin.GetBlendShapeFrameVertices(shape, frame, sourceDelta, sourceNormal, null);
                        for (int vertex = 0; vertex < vertices.Length; vertex++)
                        { delta[vertex] = samples[vertex].Evaluate(sourceDelta); normalDelta[vertex] = samples[vertex].Evaluate(sourceNormal); }
                        mesh.AddBlendShapeFrame(skin.GetBlendShapeName(shape), skin.GetBlendShapeFrameWeight(shape, frame), delta, normalDelta, null);
                    }
            }
            mesh.RecalculateBounds();
            var ordered = new Transform[original.bones.Length];
            for (int i = 0; i < ordered.Length; i++)
                if (!bones.TryGetValue(original.bones[i].name, out ordered[i]))
                    throw new InvalidOperationException("Body derivative lost production bone " + original.bones[i].name);
            piece.bones = ordered;
            piece.rootBone = original.rootBone != null && bones.TryGetValue(original.rootBone.name, out Transform root) ? root : ordered[0];
            piece.localBounds = mesh.bounds;
        }

        private sealed class TorsoFrameSamples
        {
            internal readonly List<Vector3> Authored = new List<Vector3>();
            internal readonly List<Vector3> Imported = new List<Vector3>();
        }

        private static Dictionary<string, Matrix4x4> MeasureTorsoFrames(GameObject model,
            Dictionary<string, SkinnedMeshRenderer> originals)
        {
            var samples = new Dictionary<string, TorsoFrameSamples>(StringComparer.Ordinal);
            foreach (SkinnedMeshRenderer piece in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var labels = new List<Vector2>(); piece.sharedMesh.GetUVs(1, labels);
                if (labels.Count == 0) continue;
                string name = piece.name.Substring(piece.name.IndexOf("__", StringComparison.Ordinal) + 2);
                SkinnedMeshRenderer original = originals[name];
                if (!samples.TryGetValue(name, out TorsoFrameSamples rows)) samples.Add(name, rows = new TorsoFrameSamples());
                Matrix4x4 toSource = original.transform.worldToLocalMatrix * piece.transform.localToWorldMatrix;
                Vector3[] vertices = piece.sharedMesh.vertices;
                var xy = new List<Vector2>(); var z = new List<Vector2>();
                if (piece.name.StartsWith("Flesh", StringComparison.Ordinal))
                {
                    piece.sharedMesh.GetUVs(4, xy); piece.sharedMesh.GetUVs(5, z);
                    for (int i = 0; i < labels.Count; i++) if (labels[i].y < .5f)
                    { rows.Authored.Add(new Vector3(xy[i].x, xy[i].y, z[i].x)); rows.Imported.Add(toSource.MultiplyPoint3x4(vertices[i])); }
                }
                else
                {
                    piece.sharedMesh.GetUVs(6, xy); piece.sharedMesh.GetUVs(7, z);
                    var cells = new Dictionary<int, HashSet<Vector3>>();
                    var centres = new Dictionary<int, Vector3>();
                    for (int i = 0; i < labels.Count; i++)
                    {
                        int cell = Mathf.RoundToInt(labels[i].x);
                        if (!cells.TryGetValue(cell, out HashSet<Vector3> points))
                        { cells.Add(cell, points = new HashSet<Vector3>()); centres.Add(cell, new Vector3(xy[i].x, xy[i].y, z[i].x)); }
                        points.Add(vertices[i]);
                    }
                    foreach (var cell in cells)
                    {
                        Vector3 centre = Vector3.zero; foreach (Vector3 point in cell.Value) centre += point;
                        rows.Authored.Add(centres[cell.Key]); rows.Imported.Add(toSource.MultiplyPoint3x4(centre / cell.Value.Count));
                    }
                }
            }
            var frames = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
            float handedness = 0f;
            // Volumetric sources establish FBX handedness before planar cloth.
            foreach (bool planar in new[] { false, true }) foreach (var entry in samples)
            {
                List<Vector3> authored = entry.Value.Authored, imported = entry.Value.Imported;
                int b = 0, c = 0, d = 0; float best = 0f;
                for (int i = 1; i < authored.Count; i++)
                { float distance = (authored[i] - authored[0]).sqrMagnitude; if (distance > best) { best = distance; b = i; } }
                best = 0f; Vector3 axis = authored[b] - authored[0];
                for (int i = 1; i < authored.Count; i++)
                { float area = Vector3.Cross(axis, authored[i] - authored[0]).sqrMagnitude; if (area > best) { best = area; c = i; } }
                best = 0f; Vector3 normal = Vector3.Cross(axis, authored[c] - authored[0]).normalized;
                for (int i = 1; i < authored.Count; i++)
                { float height = Mathf.Abs(Vector3.Dot(normal, authored[i] - authored[0])); if (height > best) { best = height; d = i; } }
                bool isPlanar = best < .000001f;
                if (isPlanar != planar) continue;
                Matrix4x4 from = Frame(authored[0], authored[b], authored[c], authored[d]);
                Matrix4x4 to = Frame(imported[0], imported[b], imported[c], imported[d]);
                if (isPlanar)
                {
                    if (handedness == 0f || normal.sqrMagnitude < .9f)
                        throw new InvalidOperationException("Torso metadata lacks an independent frame: " + entry.Key);
                    Vector3 importedNormal = Vector3.Cross(imported[b] - imported[0], imported[c] - imported[0]).normalized;
                    float scale = (imported[b] - imported[0]).magnitude / axis.magnitude;
                    from = Frame(authored[0], authored[b], authored[c], authored[0] + normal * .01f);
                    to = Frame(imported[0], imported[b], imported[c], imported[0] + importedNormal * (.01f * scale * handedness));
                }
                Matrix4x4 result = to * from.inverse;
                if (!isPlanar) handedness = Mathf.Sign(result.determinant);
                float metreScale = originals[entry.Key].transform.lossyScale.magnitude / Mathf.Sqrt(3f);
                for (int i = 0; i < authored.Count; i++)
                    if (Vector3.Distance(result.MultiplyPoint3x4(authored[i]), imported[i]) * metreScale > .0001f)
                        throw new InvalidOperationException("Torso metadata lost its affine FBX basis: " + entry.Key);
                frames.Add(entry.Key, result);
            }
            return frames;
        }

        private static Matrix4x4 Frame(Vector3 origin, Vector3 b, Vector3 c, Vector3 d)
        {
            Matrix4x4 result = Matrix4x4.identity;
            result.SetColumn(0, (Vector4)(b - origin)); result.SetColumn(1, (Vector4)(c - origin));
            result.SetColumn(2, (Vector4)(d - origin)); result.SetColumn(3, new Vector4(origin.x, origin.y, origin.z, 1f));
            return result;
        }

        private static void RestoreTorsoCells(Mesh mesh, Matrix4x4 torsoFrame)
        {
            var labels = new List<Vector2>(); mesh.GetUVs(1, labels);
            if (labels.Count == 0) return;
            var innerXY = new List<Vector2>(); var innerZ = new List<Vector2>();
            var centreXY = new List<Vector2>(); var centreZ = new List<Vector2>();
            mesh.GetUVs(4, innerXY); mesh.GetUVs(5, innerZ);
            mesh.GetUVs(6, centreXY); mesh.GetUVs(7, centreZ);
            if (labels.Count != mesh.vertexCount || innerXY.Count != labels.Count || innerZ.Count != labels.Count ||
                centreXY.Count != labels.Count || centreZ.Count != labels.Count)
                throw new InvalidOperationException("Authored torso cell metadata is incomplete: " + mesh.name);
            var targets = new List<Vector4>(labels.Count); var centres = new List<Vector4>(labels.Count);
            for (int i = 0; i < labels.Count; i++)
            {
                targets.Add(torsoFrame.MultiplyPoint3x4(new Vector3(innerXY[i].x, innerXY[i].y, innerZ[i].x)));
                centres.Add(torsoFrame.MultiplyPoint3x4(new Vector3(centreXY[i].x, centreXY[i].y, centreZ[i].x)));
            }
            mesh.SetUVs(4, targets); mesh.SetUVs(6, centres);
        }

        private static BoneWeight InterpolateWeights(BoneWeight a, BoneWeight b, BoneWeight c, Vector3 bary)
        {
            var values = new Dictionary<int, float>();
            Add(a, bary.x); Add(b, bary.y); Add(c, bary.z);
            var ordered = new List<KeyValuePair<int, float>>(values);
            ordered.Sort((x, y) => { int order = y.Value.CompareTo(x.Value); return order != 0 ? order : x.Key.CompareTo(y.Key); });
            float total = 0f;
            for (int i = 0; i < Mathf.Min(4, ordered.Count); i++) total += ordered[i].Value;
            BoneWeight result = default;
            for (int i = 0; i < Mathf.Min(4, ordered.Count); i++)
            {
                int index = ordered[i].Key; float weight = ordered[i].Value / total;
                if (i == 0) { result.boneIndex0 = index; result.weight0 = weight; }
                if (i == 1) { result.boneIndex1 = index; result.weight1 = weight; }
                if (i == 2) { result.boneIndex2 = index; result.weight2 = weight; }
                if (i == 3) { result.boneIndex3 = index; result.weight3 = weight; }
            }
            return result;
            void Add(BoneWeight weight, float amount)
            {
                Accumulate(weight.boneIndex0, weight.weight0 * amount); Accumulate(weight.boneIndex1, weight.weight1 * amount);
                Accumulate(weight.boneIndex2, weight.weight2 * amount); Accumulate(weight.boneIndex3, weight.weight3 * amount);
            }
            void Accumulate(int index, float weight)
            { if (weight > 0f) values[index] = values.TryGetValue(index, out float old) ? old + weight : weight; }
        }

        // Ericson's closest-point regions; handles new vertices on triangle edges
        // as well as interior flesh/bone samples outside the exterior surface.
        private static Vector3 ClosestBarycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return new Vector3(1, 0, 0);
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return new Vector3(0, 1, 0);
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) { float v = d1 / (d1 - d3); return new Vector3(1 - v, v, 0); }
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return new Vector3(0, 0, 1);
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) { float w = d2 / (d2 - d6); return new Vector3(1 - w, 0, w); }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            { float w = (d4 - d3) / (d4 - d3 + d5 - d6); return new Vector3(0, 1 - w, w); }
            float denominator = va + vb + vc;
            if (Mathf.Abs(denominator) < 1e-20f) return new Vector3(1, 0, 0);
            return new Vector3(va, vb, vc) / denominator;
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (rebinding) return;
            bool hero = Array.IndexOf(imported, HeroSource) >= 0, npc = Array.IndexOf(imported, NpcSource) >= 0;
            if (!hero && !npc) return;
            rebinding = true;
            try { if (hero) Reimport("Hero"); if (npc) Reimport("Npc"); }
            finally { rebinding = false; }
        }

        private static void Reimport(string kind)
        {
            string sourcePath = kind == "Hero" ? HeroSource : NpcSource;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath) == null) return;
            Hash128 dependencyHash = AssetDatabase.GetAssetDependencyHash(sourcePath);
            bool alreadyProcessed = processedSourceHashes.TryGetValue(sourcePath, out Hash128 previous) && previous == dependencyHash;
            foreach (string prefix in new[] { "Body", "Skull" })
            {
                string path = Folder + prefix + kind + ".fbx";
                if (!File.Exists(path)) continue;
                if (alreadyProcessed && AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) continue;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            processedSourceHashes[sourcePath] = dependencyHash;
        }

        [MenuItem("Bar Promenade/Combat Test/Validate Body Anatomy Assets")]
        public static void BuildOrThrow() => ValidateBodies(true);

        public static void ValidateImportedOrThrow() => ValidateBodies(false);

        private static void ValidateBodies(bool reimport)
        {
            if (!File.Exists(Folder + "CombatBody3D.json")) throw new InvalidOperationException("Missing body anatomy manifest.");
            if (!File.Exists(Folder + "CombatSkull3D.json")) throw new InvalidOperationException("Missing skull anatomy manifest.");
            foreach (string kind in new[] { "Hero", "Npc" })
            {
                string path = Folder + "Body" + kind + ".fbx";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(SourceFor(path)) == null)
                    AssetDatabase.ImportAsset(SourceFor(path), ImportAssetOptions.ForceSynchronousImport);
                var originals = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
                foreach (SkinnedMeshRenderer original in AssetDatabase.LoadAssetAtPath<GameObject>(SourceFor(path))
                    .GetComponentsInChildren<SkinnedMeshRenderer>(true)) originals.Add(original.name, original);
                if (reimport) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) throw new InvalidOperationException("Missing authored combat body " + kind);
                var flesh = new HashSet<string>(StringComparer.Ordinal); int boneCount = 0;
                foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Mesh mesh = renderer.sharedMesh;
                    if (mesh == null || mesh.vertexCount == 0 || mesh.boneWeights.Length != mesh.vertexCount ||
                        mesh.uv.Length != mesh.vertexCount || mesh.bindposes.Length != renderer.bones.Length)
                        throw new InvalidOperationException("Invalid body source skin/UV " + renderer.name);
                    if (renderer.name.StartsWith("FleshRegion", StringComparison.Ordinal)) flesh.Add(renderer.name);
                    if (renderer.name.StartsWith("BoneRegion", StringComparison.Ordinal)) boneCount++;
                    if (renderer.name.StartsWith("Region2Patch", StringComparison.Ordinal) ||
                        renderer.name.StartsWith("Region3Patch", StringComparison.Ordinal) ||
                        renderer.name.StartsWith("Region4Patch", StringComparison.Ordinal) ||
                        renderer.name.StartsWith("FleshRegion2Patch", StringComparison.Ordinal) ||
                        renderer.name.StartsWith("FleshRegion3Patch", StringComparison.Ordinal) ||
                        renderer.name.StartsWith("FleshRegion4Patch", StringComparison.Ordinal))
                        ValidateTorsoCells(renderer, originals[renderer.name.Substring(renderer.name.IndexOf("__", StringComparison.Ordinal) + 2)].transform);
                    if (renderer.bounds.size.magnitude > 3f || renderer.bounds.center.y > 2.2f)
                        throw new InvalidOperationException("Body derivative lost imported metre scale: " + renderer.name);
                }
                if (flesh.Count != 64 || boneCount != 16)
                    throw new InvalidOperationException("Combat body requires four closed flesh patches and visible bones for sixteen body regions.");
                string skullPath = Folder + "Skull" + kind + ".fbx";
                if (reimport) AssetDatabase.ImportAsset(skullPath, ImportAssetOptions.ForceSynchronousImport);
                GameObject skull = AssetDatabase.LoadAssetAtPath<GameObject>(skullPath);
                var sectors = new HashSet<int>();
                if (skull == null) throw new InvalidOperationException("Missing retained skull anatomy " + kind);
                foreach (SkinnedMeshRenderer renderer in skull.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    string[] names = renderer.name.Split(new[] { "__" }, StringSplitOptions.None);
                    Mesh mesh = renderer.sharedMesh;
                    if (names.Length != 2 || names[1] != "GEO_Head" || !names[0].StartsWith("SkullSector", StringComparison.Ordinal) ||
                        !int.TryParse(names[0].Substring(11), out int sector) || sector < 0 || sector >= 16 || !sectors.Add(sector) ||
                        mesh == null || mesh.boneWeights.Length != mesh.vertexCount || mesh.uv.Length != mesh.vertexCount ||
                        mesh.bindposes.Length != renderer.bones.Length)
                        throw new InvalidOperationException("Retained skull lost source skin or sector identity: " + renderer.name);
                }
                if (sectors.Count != 16) throw new InvalidOperationException("Skull anatomy must match all sixteen existing fracture sectors.");
            }
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "BoneSurface.png") == null)
                throw new InvalidOperationException("Missing shared bone surface.");
            Debug.Log("COMBAT BODY IMPORTED SOURCE INTERPOLATION, SKIN, UV, REGIONS AND METRES OK");
        }

        private static void ValidateTorsoCells(SkinnedMeshRenderer renderer, Transform sourceFrame)
        {
            Mesh mesh = renderer.sharedMesh; var labels = new List<Vector2>();
            var inner = new List<Vector4>(); var centres = new List<Vector4>();
            mesh.GetUVs(1, labels); mesh.GetUVs(4, inner); mesh.GetUVs(6, centres);
            if (labels.Count != mesh.vertexCount || inner.Count != labels.Count || centres.Count != labels.Count)
                throw new InvalidOperationException("Torso requires finite authored erosion cells: " + renderer.name);
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < labels.Count; i++)
            {
                // Restored geometry and metadata are both in the production
                // renderer's frame, which the runtime uses for these templates.
                float radius = sourceFrame.TransformVector(vertices[i] - (Vector3)centres[i]).magnitude;
                float depth = sourceFrame.TransformVector(vertices[i] - (Vector3)inner[i]).magnitude;
                float maximumRadius = renderer.name.StartsWith("Region", StringComparison.Ordinal) ? .05f : .45f;
                if (labels[i].x < 0f || Mathf.Abs(labels[i].x - Mathf.Round(labels[i].x)) > .001f ||
                    !float.IsFinite(radius) || radius > maximumRadius || !float.IsFinite(depth) || depth > .5f)
                    throw new InvalidOperationException("Torso cell lost its source frame or metre scale: " + renderer.name +
                        "; radius=" + radius + "; depth=" + depth + "; vertex=" + vertices[i].ToString("G9") + "; centre=" + centres[i].ToString("G9"));
            }
            int[] triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
                if (labels[triangles[i]].x != labels[triangles[i + 1]].x || labels[triangles[i]].x != labels[triangles[i + 2]].x)
                    throw new InvalidOperationException("Torso face crosses erosion cells: " + renderer.name);
        }
    }
}
