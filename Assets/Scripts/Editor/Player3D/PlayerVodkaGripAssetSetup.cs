using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Explicit setup only: do not invalidate unrelated model importers for this optional hand bank.</summary>
    public static class PlayerVodkaGripAssetSetup
    {
        public const string ModelPath = "Assets/Resources/Player/PlayerVodkaGrip.fbx";
        private const string CatalogPath = "Assets/Resources/Player/VodkaGripCatalog.asset";
        private const string MeshFolder = "Assets/Player3D/V2/Models/VodkaGrip";
        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None || !importer.bakeAxisConversion ||
                importer.importAnimation || !importer.isReadable || !importer.preserveHierarchy)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.bakeAxisConversion = true; importer.importAnimation = false; importer.isReadable = true;
                importer.globalScale = 1f; importer.useFileScale = true;
                importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
                importer.importCameras = false; importer.importLights = false;
                importer.SaveAndReimport();
            }
            GameObject authored = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player/Player3DV2.prefab");
            if (authored == null || prefab == null) throw new InvalidOperationException("Bottle grip requires its original hero and authored hand bank.");
            var donors = authored.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToDictionary(r => r.name);
            Vector3 centre = Marker(donors, "Centre");
            Vector3 axis = (Marker(donors, "Axis") - centre).normalized;
            Vector3 palm = (Marker(donors, "Palm") - centre).normalized;
            Vector3 thumbBase = Marker(donors, "ThumbBase"), thumbTip = Marker(donors, "ThumbTip");
            var entries = new List<PlayerVodkaGripCatalog.Entry>(6);
            if (!AssetDatabase.IsValidFolder(MeshFolder)) AssetDatabase.CreateFolder("Assets/Player3D/V2/Models", "VodkaGrip");
            foreach (SkinnedMeshRenderer renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!donors.TryGetValue(renderer.name, out SkinnedMeshRenderer donor)) continue;
                // Rebuild an existing extension from its canonical source.
                Mesh source = renderer.sharedMesh;
                if (PlayerVodkaGripCatalog.TryGetEntry(source, out PlayerVodkaGripCatalog.Entry previous)) source = previous.Source;
                Mesh target = UnityEngine.Object.Instantiate(source);
                target.name = source.name + " Bottle Grip";
                int shape = CharacterJointDeformation.FindShape(donor.sharedMesh, "BottleGrip");
                if (shape < 0) throw new InvalidOperationException("Authored BottleGrip is absent on " + donor.name);
                var positions = donor.sharedMesh.vertices;
                var delta = new Vector3[positions.Length]; var normals = new Vector3[positions.Length]; var tangents = new Vector3[positions.Length];
                donor.sharedMesh.GetBlendShapeFrameVertices(shape, 0, delta, normals, tangents);
                int donorHand = HandBone(donor), sourceHand = HandBone(renderer);
                Vector3[] sourceVertices = source.vertices;
                var authoredPoints = new Vector3[positions.Length];
                for (int i = 0; i < positions.Length; i++) authoredPoints[i] = Point(donor, donorHand, positions[i]);
                var mapped = new Vector3[source.vertexCount]; var mappedNormals = new Vector3[mapped.Length]; var mappedTangents = new Vector3[mapped.Length];
                Quaternion normalRotation = Quaternion.Inverse(renderer.transform.rotation) * donor.transform.rotation;
                for (int i = 0; i < mapped.Length; i++)
                {
                    Vector3 point = Point(renderer, sourceHand, sourceVertices[i]);
                    int nearest = 0; float distance = float.PositiveInfinity;
                    for (int j = 0; j < authoredPoints.Length; j++)
                    { float candidate = (point - authoredPoints[j]).sqrMagnitude; if (candidate < distance) { nearest = j; distance = candidate; } }
                    if (distance > .000002f * .000002f) throw new InvalidOperationException("Bottle grip lost original hand basis: " + renderer.name + "/" + i + "/" + Mathf.Sqrt(distance));
                    Vector3 handDelta = Direction(donor, donorHand, delta[nearest]);
                    mapped[i] = source.bindposes[sourceHand].inverse.MultiplyVector(
                        renderer.bones[sourceHand].InverseTransformVector(renderer.bones[sourceHand].rotation * handDelta));
                    mappedNormals[i] = normalRotation * normals[nearest]; mappedTangents[i] = normalRotation * tangents[nearest];
                }
                target.AddBlendShapeFrame("BottleGrip", 100f, mapped, mappedNormals, mappedTangents);
                string path = MeshFolder + "/" + renderer.name + ".asset";
                Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing == null) { AssetDatabase.CreateAsset(target, path); existing = target; }
                else { EditorUtility.CopySerialized(target, existing); UnityEngine.Object.DestroyImmediate(target); EditorUtility.SetDirty(existing); }
                int[] palmPlane = Array.Empty<int>(), thumbBaseRing = Array.Empty<int>(), thumbTipRing = Array.Empty<int>();
                if (renderer.name == "GEO_Hand.R")
                {
                    var shaped = new Vector3[sourceVertices.Length];
                    float maximum = float.NegativeInfinity;
                    for (int i = 0; i < shaped.Length; i++)
                    { shaped[i] = Point(renderer, sourceHand, sourceVertices[i] + mapped[i]); maximum = Mathf.Max(maximum, Vector3.Dot(shaped[i], palm)); }
                    var plane = new List<int>();
                    BoneWeight[] weights = source.boneWeights;
                    // The proximal wrist seam blends forearm/hand. Its
                    // deformation is not a rigid palm-orientation witness.
                    for (int i = 0; i < shaped.Length; i++)
                        if (HandWeight(weights[i], sourceHand) > .99999f &&
                            maximum - Vector3.Dot(shaped[i], palm) < .00001f) plane.Add(i);
                    float area = 0f;
                    foreach (int a in plane) foreach (int b in plane) foreach (int c in plane)
                    {
                        float candidate = Vector3.Cross(shaped[b] - shaped[a], shaped[c] - shaped[a]).sqrMagnitude;
                        if (candidate > area) { area = candidate; palmPlane = new[] { a, b, c }; }
                    }
                    if (palmPlane.Length != 3 || area < .0000001f) throw new InvalidOperationException("Mapped bottle palm lost its actual tangent plane.");
                }
                if (renderer.name == "GEO_Thumb.R")
                {
                    Vector3 direction = (thumbTip - thumbBase).normalized;
                    var stations = new float[sourceVertices.Length];
                    for (int i = 0; i < stations.Length; i++) stations[i] = Vector3.Dot(Point(renderer, sourceHand, sourceVertices[i]) - thumbBase, direction);
                    float first = stations.Min(), last = stations.Max();
                    thumbBaseRing = Enumerable.Range(0, stations.Length).Where(i => stations[i] - first < .000001f).ToArray();
                    thumbTipRing = Enumerable.Range(0, stations.Length).Where(i => last - stations[i] < .000001f).ToArray();
                }
                entries.Add(new PlayerVodkaGripCatalog.Entry(source, existing, palmPlane, thumbBaseRing, thumbTipRing));
            }
            if (entries.Count != 6) throw new InvalidOperationException("Bottle grip must extend precisely the original palm, thumb and four fingers.");
            PlayerVodkaGripCatalog catalog = AssetDatabase.LoadAssetAtPath<PlayerVodkaGripCatalog>(CatalogPath);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<PlayerVodkaGripCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
            // All contact vectors use the same skinned bind-space mapping as
            // the morph. Bone-parented FBX Empty axes are deliberately unused.
            catalog.Configure(entries.ToArray(), centre, axis, palm);
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
        }
        private static float HandWeight(BoneWeight weight, int hand) =>
            (weight.boneIndex0 == hand ? weight.weight0 : 0f) + (weight.boneIndex1 == hand ? weight.weight1 : 0f) +
            (weight.boneIndex2 == hand ? weight.weight2 : 0f) + (weight.boneIndex3 == hand ? weight.weight3 : 0f);
        private static Vector3 Marker(IReadOnlyDictionary<string, SkinnedMeshRenderer> donors, string label)
        {
            SkinnedMeshRenderer renderer = donors["MARKER_BottleGrip" + label];
            int hand = HandBone(renderer); Vector3 sum = Vector3.zero;
            foreach (Vector3 vertex in renderer.sharedMesh.vertices) sum += Point(renderer, hand, vertex);
            return sum / renderer.sharedMesh.vertexCount;
        }
        private static int HandBone(SkinnedMeshRenderer renderer)
        {
            for (int i = 0; i < renderer.bones.Length; i++) if (renderer.bones[i].name == "hand.R") return i;
            throw new InvalidOperationException("The original hand bone was lost: " + renderer.name);
        }
        private static Vector3 Point(SkinnedMeshRenderer renderer, int hand, Vector3 vertex) =>
            Quaternion.Inverse(renderer.bones[hand].rotation) * renderer.bones[hand].TransformVector(renderer.sharedMesh.bindposes[hand].MultiplyPoint3x4(vertex));
        private static Vector3 Direction(SkinnedMeshRenderer renderer, int hand, Vector3 delta) =>
            Quaternion.Inverse(renderer.bones[hand].rotation) * renderer.bones[hand].TransformVector(renderer.sharedMesh.bindposes[hand].MultiplyVector(delta));
    }
}
