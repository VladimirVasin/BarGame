using System;
using System.IO;
using BarPromenade;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Editor
{
    /// <summary>Imports passive FBX files directly into Resources; no generated prefab binding.</summary>
    public sealed class CityOffshoreBoatAssetSetup : AssetPostprocessor
    {
        public const string ModelFolder = "Assets/Resources/City/OffshoreBoats/";
        public const string ManifestPath = "Assets/City/Models/CityOffshoreBoats3D.json";
        private static readonly string[] RequiredParts =
        {
            "Hull", "SearchlightPivot", "SearchlightHousing", "Lens", "Beam",
            "CabinGlow", "Wake", "ANCHOR_Horn", "ANCHOR_Engine"
        };

        public override uint GetVersion() => 1;

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelFolder, StringComparison.Ordinal) ||
                !(assetImporter is ModelImporter importer))
            {
                return;
            }

            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
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
            importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!assetPath.StartsWith(ModelFolder, StringComparison.Ordinal))
            {
                return;
            }

            // The editable source holds both vessels. Blender's global object
            // names suffix the second vessel's semantic roles with .001.
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                int suffix = part.name.LastIndexOf('.');
                if (suffix > 0 && int.TryParse(part.name.Substring(suffix + 1), out _))
                {
                    part.name = part.name.Substring(0, suffix);
                }
            }

            // FBX preserved the world-space vertices and anchors but leaves an
            // axis-conversion rotation above the authored empty. Runtime drives
            // this one part with local Euler angles, so give it the model's
            // canonical +Z/+Y basis. Preserve every child's world transform while
            // doing so; otherwise the correctly imported beam would rotate too.
            Transform pivot = CityOffshoreBoatAssetProvider.FindPart(model, "SearchlightPivot");
            var children = new Transform[pivot.childCount];
            for (int index = 0; index < children.Length; index++)
            {
                children[index] = pivot.GetChild(index);
            }

            foreach (Transform child in children)
            {
                child.SetParent(model.transform, true);
            }

            pivot.SetParent(model.transform, true);
            pivot.localRotation = Quaternion.identity;
            pivot.localScale = Vector3.one;
            foreach (Transform child in children)
            {
                child.SetParent(pivot, true);
            }
        }

        [MenuItem("Bar Promenade/City Offshore Boats/Validate Imported Contract")]
        public static void ValidateOrThrow()
        {
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest?.variants == null || manifest.variants.Length != CityOffshoreBoatAssetProvider.VariantCount)
            {
                throw new InvalidOperationException("Offshore boat manifest requires two variants.");
            }

            foreach (Variant entry in manifest.variants)
            {
                string path = ModelFolder + entry.name + ".fbx";
                GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (template == null)
                {
                    throw new InvalidOperationException("Missing offshore model " + path);
                }

                GameObject model = UnityEngine.Object.Instantiate(template);
                try
                {
                    model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    foreach (string name in RequiredParts)
                    {
                        CityOffshoreBoatAssetProvider.FindPart(model, name);
                    }

                    Bounds bounds = default;
                    bool initialized = false;
                    Vector3 colorMin = Vector3.one;
                    Vector3 colorMax = Vector3.zero;
                    foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                        if (mesh.subMeshCount != 1)
                        {
                            throw new InvalidOperationException("Offshore boat roles require one material slot.");
                        }

                        if (renderer.name != "Hull" && renderer.name != "SearchlightHousing")
                        {
                            continue;
                        }

                        AccumulateColorRange(mesh, ref colorMin, ref colorMax);

                        if (!initialized)
                        {
                            bounds = renderer.bounds;
                            initialized = true;
                        }
                        else
                        {
                            bounds.Encapsulate(renderer.bounds);
                        }
                    }

                    CheckVector(bounds.min, entry.bounds_min, entry.name + " opaque minimum");
                    CheckVector(bounds.max, entry.bounds_max, entry.name + " opaque maximum");
                    CheckVector(colorMin, entry.opaque_color_min, entry.name + " linear palette minimum", .005f);
                    CheckVector(colorMax, entry.opaque_color_max, entry.name + " linear palette maximum", .005f);
                    Debug.Log($"{entry.name} imported linear vertex RGB range {colorMin.ToString("F4")} .. {colorMax.ToString("F4")}");
                    foreach (Anchor anchor in entry.anchors)
                    {
                        Transform part = CityOffshoreBoatAssetProvider.FindPart(model, anchor.name);
                        CheckVector(part.position, anchor.position, entry.name + " " + anchor.name);
                    }

                    Transform pivot = CityOffshoreBoatAssetProvider.FindPart(model, "SearchlightPivot");
                    if (pivot.parent != model.transform ||
                        Vector3.Dot(pivot.forward, Vector3.forward) < 0.999f ||
                        Vector3.Dot(pivot.up, Vector3.up) < 0.999f)
                    {
                        throw new InvalidOperationException($"Offshore searchlight must be a model-root child with +Z forward / +Y up; " +
                            $"actual forward={pivot.forward}, up={pivot.up}, parent={pivot.parent.name}.");
                    }

                    Bounds beamBounds = CityOffshoreBoatAssetProvider.FindPart(model, "Beam")
                        .GetComponent<MeshRenderer>().bounds;
                    Vector3 expectedBeamCenter = pivot.position + Vector3.forward * (entry.beam_length_m + .2f) * .5f;
                    if (Vector3.Distance(beamBounds.center, expectedBeamCenter) > .025f ||
                        Mathf.Abs(beamBounds.max.z - pivot.position.z - entry.beam_length_m) > .025f)
                    {
                        throw new InvalidOperationException("Offshore beam geometry must remain aligned with the normalized pivot's +Z axis.");
                    }

                    if (model.GetComponentsInChildren<Collider>(true).Length != 0 ||
                        model.GetComponentsInChildren<Light>(true).Length != 0 ||
                        model.GetComponentsInChildren<Animator>(true).Length != 0)
                    {
                        throw new InvalidOperationException("Offshore boat assets must remain passive.");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(model);
                }
            }

            Debug.Log("CITY OFFSHORE BOAT IMPORTED CONTRACT OK");
        }

        private static void AccumulateColorRange(Mesh mesh, ref Vector3 minimum, ref Vector3 maximum)
        {
            if (!mesh.HasVertexAttribute(VertexAttribute.Color))
            {
                throw new InvalidOperationException("Offshore opaque mesh lost its authored vertex palette.");
            }

            // Editor readback deliberately bypasses the passive runtime mesh's
            // Read/Write flag. Its imported colours must already be linear.
            using (Mesh.MeshDataArray data = MeshUtility.AcquireReadOnlyMeshData(mesh))
            using (var colors = new NativeArray<Color>(data[0].vertexCount, Allocator.Temp))
            {
                data[0].GetColors(colors);
                foreach (Color color in colors)
                {
                    Vector3 rgb = new Vector3(color.r, color.g, color.b);
                    minimum = Vector3.Min(minimum, rgb);
                    maximum = Vector3.Max(maximum, rgb);
                }
            }
        }

        private static void CheckVector(Vector3 actual, float[] expected, string label, float tolerance = .025f)
        {
            if (expected == null || expected.Length != 3 ||
                Vector3.Distance(actual, new Vector3(expected[0], expected[1], expected[2])) > tolerance)
            {
                throw new InvalidOperationException($"{label}: imported position/bounds {actual} differ from authored metres.");
            }
        }

        [Serializable] private sealed class Manifest { public Variant[] variants; }
        [Serializable] private sealed class Variant
        {
            public string name;
            public float[] bounds_min;
            public float[] bounds_max;
            public float beam_length_m;
            public float[] opaque_color_min;
            public float[] opaque_color_max;
            public Anchor[] anchors;
        }
        [Serializable] private sealed class Anchor { public string name; public float[] position; }
    }
}
