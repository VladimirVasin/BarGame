using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Imports exact pre-fractured bottle shells into the real retained chest frame.</summary>
    public sealed class CombatZombieBottleFractureAssetSetup : AssetPostprocessor
    {
        public const string BankPath = "Assets/Resources/Combat/ZombieBottleFracture.fbx";
        public const string ManifestPath = "Assets/Resources/Combat/ZombieBottleFracture.json";
        private bool IsBank => assetPath == BankPath;

        private void OnPreprocessModel()
        {
            if (!IsBank || !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            importer.importAnimation = false; importer.animationType = ModelImporterAnimationType.None;
            importer.importBlendShapes = false; importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.isReadable = true; importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            for (int variant = 0; variant < 3; variant++) context.DependsOnArtifact(BodyPath(variant));
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!IsBank) return;
            for (int variant = 0; variant < 3; variant++)
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath(variant));
                if (source == null) return;
                Transform chest = Find(source.transform, "chest");
                Transform anchor = Find(model.transform, "Variant" + variant);
                if (chest == null || anchor == null)
                    throw new InvalidOperationException("Bottle fracture lost its retained chest bind frame.");
                // FBX bones may carry a pre-rotation which an ordinary exported
                // Empty does not. Preserve authored world geometry while making
                // this anchor EXACTLY the imported bone's frame and unit scale.
                var children = new Transform[anchor.childCount];
                for (int i = 0; i < children.Length; i++) children[i] = anchor.GetChild(i);
                foreach (Transform child in children) child.SetParent(model.transform, true);
                anchor.SetPositionAndRotation(chest.position, chest.rotation);
                Vector3 parentScale = anchor.parent.lossyScale;
                Vector3 scale = chest.lossyScale;
                anchor.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, scale.z / parentScale.z);
                foreach (Transform child in children)
                {
                    child.SetParent(anchor, true);
                    string prefix = "V" + variant + "_";
                    if (child.name.StartsWith(prefix, StringComparison.Ordinal)) child.name = child.name.Substring(prefix.Length);
                }
            }
        }

        [MenuItem("Bar Promenade/Combat Test/Validate Bottle Fracture Assets")]
        public static void BuildOrThrow()
        {
            for (int variant = 0; variant < 3; variant++)
                if (AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath(variant)) == null)
                    AssetDatabase.ImportAsset(BodyPath(variant), ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(BankPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            TextAsset text = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            Manifest manifest = text != null ? JsonUtility.FromJson<Manifest>(text.text) : null;
            if (manifest == null || manifest.design_id != "zombie_bottle_fracture_v1" || manifest.cell_count != 35 ||
                manifest.variants == null || manifest.variants.Length != 3 || manifest.wall_thickness_m < .003f || manifest.wall_thickness_m > .01f)
                throw new InvalidOperationException("Missing bounded bottle fracture contract.");
            GameObject bank = AssetDatabase.LoadAssetAtPath<GameObject>(BankPath)
                ?? throw new InvalidOperationException("Missing authored bottle fracture bank.");
            foreach (Variant row in manifest.variants)
            {
                if (row == null || (uint)row.variant >= 3 || row.cell_count != manifest.cell_count || row.cells == null || row.cells.Length != row.cell_count ||
                    row.anchor != "Variant" + row.variant || !HashMatches(BodyPath(row.variant), row.source_body_sha256))
                    throw new InvalidOperationException("Bottle fracture source geometry changed.");
                Transform anchor = Find(bank.transform, row.anchor);
                GameObject original = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath(row.variant));
                Transform chest = Find(original.transform, "chest");
                if (anchor == null || chest == null || Vector3.Distance(anchor.position, chest.position) > .00001f ||
                    Quaternion.Angle(anchor.rotation, chest.rotation) > .01f || Vector3.Distance(anchor.lossyScale, chest.lossyScale) > .001f)
                    throw new InvalidOperationException("Bottle fracture does not share the imported chest frame.");
                Transform intact = anchor.Find("Intact");
                Mesh combined = intact != null ? intact.GetComponent<MeshFilter>()?.sharedMesh : null;
                if (combined == null || !combined.isReadable || combined.subMeshCount != row.cell_count || combined.vertexCount > 60000)
                    throw new InvalidOperationException("Bottle fracture lost its finite per-cell submeshes.");
                for (int i = 0; i < row.cell_count; i++)
                {
                    Cell cell = row.cells[i];
                    Transform part = anchor.Find("Cell_" + i.ToString("00"));
                    Mesh mesh = part != null ? part.GetComponent<MeshFilter>()?.sharedMesh : null;
                    if (cell == null || cell.index != i || cell.name != "Cell_" + i.ToString("00") ||
                        !float.IsFinite(cell.volume_m3) || cell.volume_m3 <= 0f || mesh == null || !mesh.isReadable ||
                        mesh.triangles.Length == 0 || mesh.triangles.Length != combined.GetTriangles(i).Length)
                        throw new InvalidOperationException("Bottle fracture lost an authored closed cell.");
                    Vector3[] detached = WorldVertices(part, mesh.vertices, mesh.triangles);
                    Vector3[] retained = WorldVertices(intact, combined.vertices, combined.GetTriangles(i));
                    float forwardError = MaximumNearestDistance(detached, retained);
                    float backwardError = MaximumNearestDistance(retained, detached);
                    float error = Mathf.Max(forwardError, backwardError);
                    if (error > ImportedSurfaceTolerance)
                        throw new InvalidOperationException($"Detached glass mismatch: variant={row.variant},cell={i}," +
                            $"maxDistance={error:G9}m,forward={forwardError:G9},backward={backwardError:G9}," +
                            $"tolerance={ImportedSurfaceTolerance:G9}m.");
                }
                // Actual imported source vertices must still lie on this bank,
                // proving units/axis/placement independently of the manifest.
                SkinnedMeshRenderer torso = Find(original.transform, "GEO_Torso").GetComponent<SkinnedMeshRenderer>();
                Vector3[] exterior = WorldVertices(torso.transform, torso.sharedMesh.vertices, torso.sharedMesh.GetTriangles(1));
                Vector3[] whole = WorldVertices(intact, combined.vertices, combined.triangles);
                float exteriorError = MaximumNearestDistance(exterior, whole);
                if (exteriorError > ImportedSurfaceTolerance)
                    throw new InvalidOperationException($"Bottle exterior shifted: variant={row.variant}," +
                        $"maxDistance={exteriorError:G9}m,tolerance={ImportedSurfaceTolerance:G9}m.");
            }
        }

        private static string BodyPath(int variant) => "Assets/Resources/Combat/ZombieDecayBody" + variant + ".fbx";
        // Exported cells have independent local origins; FBX float vertices
        // and hierarchy decomposition can differ by sub-micrometres in world
        // space. Quantized-bin equality rejects points straddling a bin edge
        // even when they occupy the same surface. Compare actual distances.
        private const float ImportedSurfaceTolerance = .000025f;
        private static Vector3[] WorldVertices(Transform space, Vector3[] vertices, int[] indices)
        {
            var used = new HashSet<int>(indices);
            var result = new Vector3[used.Count];
            int cursor = 0;
            foreach (int i in used) result[cursor++] = space.TransformPoint(vertices[i]);
            return result;
        }
        private static float MaximumNearestDistance(Vector3[] source, Vector3[] target)
        {
            // This is an editor-only bounded bank check. A millimetre spatial
            // hash keeps nearest matching local while the acceptance tolerance
            // remains twenty-five micrometres and is checked in both directions.
            const float bucketSize = .001f;
            var buckets = new Dictionary<Vector3Int, List<Vector3>>();
            foreach (Vector3 point in target)
            {
                Vector3Int key = Vector3Int.FloorToInt(point / bucketSize);
                if (!buckets.TryGetValue(key, out List<Vector3> values))
                { values = new List<Vector3>(); buckets.Add(key, values); }
                values.Add(point);
            }
            float maximumSquared = 0f;
            foreach (Vector3 point in source)
            {
                Vector3Int key = Vector3Int.FloorToInt(point / bucketSize);
                float nearestSquared = float.PositiveInfinity;
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                    if (buckets.TryGetValue(key + new Vector3Int(x, y, z), out List<Vector3> values))
                        foreach (Vector3 other in values)
                            nearestSquared = Mathf.Min(nearestSquared, (point - other).sqrMagnitude);
                // A shifted or permuted cell must still report the true nearest
                // distance instead of accepting an absent bucket as a match.
                if (float.IsPositiveInfinity(nearestSquared))
                    foreach (Vector3 other in target)
                        nearestSquared = Mathf.Min(nearestSquared, (point - other).sqrMagnitude);
                maximumSquared = Mathf.Max(maximumSquared, nearestSquared);
            }
            return Mathf.Sqrt(maximumSquared);
        }
        private static bool HashMatches(string path, string expected)
        {
            if (string.IsNullOrEmpty(expected) || expected.Length != 64) return false;
            using var sha = SHA256.Create();
            string actual = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            return actual == expected;
        }
        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = Find(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
        [Serializable] private sealed class Manifest
        {
            public string design_id;
            public int cell_count;
            public float wall_thickness_m;
            public Variant[] variants;
        }
        [Serializable] private sealed class Variant
        {
            public int variant, cell_count;
            public string anchor, source_body_sha256;
            public Cell[] cells;
        }
        [Serializable] private sealed class Cell { public int index; public string name; public float volume_m3; }
    }
}
