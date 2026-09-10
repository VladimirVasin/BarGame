using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Passive model import, normalized moving pivots and measured
    /// geometry validation. Never derives metre measurements from empty anchors.</summary>
    public sealed class CityPortAssetSetup : AssetPostprocessor
    {
        public const string ModelFolder = "Assets/Resources/City/Port/";
        public const string ManifestPath = ModelFolder + "CityPort3D.json";
        public override uint GetVersion() => 5;

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelFolder, StringComparison.Ordinal) ||
                !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.addCollider = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.weldVertices = true;
            importer.keepQuads = false;
            importer.generateSecondaryUV = false;
            importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        private void OnPreprocessTexture()
        {
            bool palette = assetPath == ModelFolder + "PortPalette.png";
            bool surface = assetPath.StartsWith(ModelFolder + "Textures/", StringComparison.Ordinal) &&
                assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
            if ((!palette && !surface) || !(assetImporter is TextureImporter importer)) return;
            importer.textureType = TextureImporterType.Default;
            // A very wide, one-pixel image can otherwise be inferred as a
            // cubemap strip, making Resources.Load<Texture2D> return null.
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = !palette;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = palette ? FilterMode.Point : FilterMode.Bilinear;
            importer.wrapMode = palette ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.npotScale = TextureImporterNPOTScale.None;
            // Source originals remain full size; the small videogame slice
            // uses the same 512px material budget as the city road sheets.
            importer.maxTextureSize = palette ? 32 : 512;
            importer.anisoLevel = palette ? 0 : 4;
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!assetPath.StartsWith(ModelFolder, StringComparison.Ordinal)) return;
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                int suffix = part.name.LastIndexOf('.');
                if (suffix > 0 && int.TryParse(part.name.Substring(suffix + 1), out _))
                    part.name = part.name.Substring(0, suffix);
            }
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                if (part.name != "HatchA" && part.name != "HatchB" && part.name != "CraneHead" &&
                    !part.name.StartsWith("MOVE_", StringComparison.Ordinal)) continue;
                var children = new Transform[part.childCount];
                for (int i = 0; i < children.Length; i++) children[i] = part.GetChild(i);
                foreach (Transform child in children) child.SetParent(model.transform, true);
                part.SetParent(model.transform, true);
                part.localRotation = Quaternion.identity;
                part.localScale = Vector3.one;
                foreach (Transform child in children) child.SetParent(part, true);
            }
        }

        [MenuItem("Bar Promenade/City Port/Validate Imported Contract")]
        public static void ValidateOrThrow()
        {
            const string palettePath = ModelFolder + "PortPalette.png";
            var paletteImporter = AssetImporter.GetAtPath(palettePath) as TextureImporter;
            Texture2D palette = AssetDatabase.LoadAssetAtPath<Texture2D>(palettePath);
            if (paletteImporter == null || paletteImporter.textureShape != TextureImporterShape.Texture2D ||
                palette == null || palette.width != 13 || palette.height != 1)
                throw new InvalidOperationException("Port palette must import as the authored 13 x 1 Texture2D.");
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.design_id != "city_working_fishing_port_v1" ||
                manifest.parts == null || manifest.parts.Length != CityPortAssetProvider.ModelNames.Length)
                throw new InvalidOperationException("Port art manifest is incomplete.");
            foreach (Part part in manifest.parts)
            {
                GameObject model = CityPortAssetProvider.Create(part.name, null);
                try
                {
                    Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
                    int triangles = 0;
                    foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                    {
                        Mesh mesh = filter.sharedMesh;
                        if (mesh == null || mesh.colors.Length != mesh.vertexCount || mesh.uv.Length != mesh.vertexCount)
                            throw new InvalidOperationException("Port lost painted mesh/UV data: " + filter.name);
                        int region = filter.name.LastIndexOf("__", StringComparison.Ordinal);
                        if (region >= 0 && !filter.name.EndsWith("__Plain", StringComparison.Ordinal))
                        {
                            Vector2[] uv = mesh.uv;
                            Vector2 lowUv = uv[0], highUv = uv[0];
                            foreach (Vector2 point in uv) { lowUv = Vector2.Min(lowUv, point); highUv = Vector2.Max(highUv, point); }
                            if (highUv.x - lowUv.x < .001f || highUv.y - lowUv.y < .001f)
                                throw new InvalidOperationException("Port surface kept palette-point UVs: " + filter.name);
                        }
                        for (int i = 0; i < mesh.subMeshCount; i++) triangles += (int)mesh.GetIndexCount(i) / 3;
                        if (filter.name.StartsWith("COL_", StringComparison.Ordinal)) continue;
                        foreach (Vector3 vertex in mesh.vertices)
                        {
                            Vector3 point = filter.transform.TransformPoint(vertex);
                            low = Vector3.Min(low, point); high = Vector3.Max(high, point);
                        }
                    }
                    Near(low, part.bounds_min, part.name + " minimum");
                    Near(high, part.bounds_max, part.name + " maximum");
                    if (triangles != part.triangles) throw new InvalidOperationException("Port mesh triangle contract differs: " + part.name);
                    foreach (Anchor anchor in part.anchors)
                        Near(CityPortAssetProvider.FindPart(model, anchor.name).position, anchor.position, part.name + " " + anchor.name);
                    if (part.name == "Trawler")
                    {
                        foreach (string name in new[] { "HatchA", "HatchB" })
                        {
                            Transform pivot = CityPortAssetProvider.FindPart(model, name);
                            if (Vector3.Dot(pivot.up, Vector3.up) < .999f || Vector3.Dot(pivot.forward, Vector3.forward) < .999f)
                                throw new InvalidOperationException("Port hatch lacks normalized Unity rotation basis.");
                        }
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(model); }
            }
            Debug.Log("CITY PORT IMPORTED METRES, PALETTE, PIVOTS AND ANCHORS OK");
        }

        private static void Near(Vector3 actual, float[] expected, string label)
        {
            if (expected == null || expected.Length != 3 ||
                Vector3.Distance(actual, new Vector3(expected[0], expected[1], expected[2])) > .025f)
                throw new InvalidOperationException($"Port {label} differs from authored metres: {actual}.");
        }
        [Serializable] private sealed class Manifest { public string design_id; public Part[] parts; }
        [Serializable] private sealed class Part
        {
            public string name; public float[] bounds_min; public float[] bounds_max;
            public int triangles; public Anchor[] anchors;
        }
        [Serializable] private sealed class Anchor { public string name; public float[] position; }
    }
}
