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
        public override uint GetVersion() => 7;

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
                RestoreSkin(piece, original, bones, exterior, Mathf.Max(.0007f, measuredWarp * 1.05f + .0001f));
            }
        }

        private static void RestoreSkin(SkinnedMeshRenderer piece, SkinnedMeshRenderer original,
            Dictionary<string, Transform> bones, bool exterior, float maximumProjectionMetres)
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
        public static void BuildOrThrow()
        {
            if (!File.Exists(Folder + "CombatBody3D.json")) throw new InvalidOperationException("Missing body anatomy manifest.");
            if (!File.Exists(Folder + "CombatSkull3D.json")) throw new InvalidOperationException("Missing skull anatomy manifest.");
            foreach (string kind in new[] { "Hero", "Npc" })
            {
                string path = Folder + "Body" + kind + ".fbx";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(SourceFor(path)) == null)
                    AssetDatabase.ImportAsset(SourceFor(path), ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
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
                    if (renderer.bounds.size.magnitude > 3f || renderer.bounds.center.y > 2.2f)
                        throw new InvalidOperationException("Body derivative lost imported metre scale: " + renderer.name);
                }
                if (flesh.Count != 64 || boneCount != 16)
                    throw new InvalidOperationException("Combat body requires four closed flesh patches and visible bones for sixteen body regions.");
                string skullPath = Folder + "Skull" + kind + ".fbx";
                AssetDatabase.ImportAsset(skullPath, ImportAssetOptions.ForceSynchronousImport);
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
    }
}
