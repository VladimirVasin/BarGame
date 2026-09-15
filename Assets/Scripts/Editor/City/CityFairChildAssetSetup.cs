using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Imports the child body and passive toys; ChildActions has its own importer.</summary>
    public sealed class CityFairChildAssetSetup : AssetPostprocessor
    {
        public const string ModelFolder = "Assets/Resources/City/FairChild/";
        public const string ManifestPath = ModelFolder + "FairChild3D.json";
        private static readonly string[] Models = { "FairChild", "LowTable", "WoodenCar" };
        private static readonly string[] Textures =
        {
            "ChildFace", "ChildSkin", "ChildHair", "ToyWood",
            "RaincoatAtlas", "QuiltedAtlas", "VestAtlas"
        };

        public override uint GetVersion() => 1;

        private bool IsModel => Models.Any(name => string.Equals(assetPath,
            ModelFolder + name + ".fbx", StringComparison.OrdinalIgnoreCase));

        private void OnPreprocessModel()
        {
            if (!IsModel || !(assetImporter is ModelImporter importer)) return;
            bool body = string.Equals(assetPath, ModelFolder + "FairChild.fbx", StringComparison.OrdinalIgnoreCase);
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.animationType = body ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            if (body) importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
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

        private void OnPostprocessModel(GameObject model)
        {
            if (!IsModel) return;
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                int suffix = part.name.LastIndexOf('.');
                // Blender's duplicate suffix is numeric; anatomical .L/.R and
                // the finger-joint names remain part of the shared rig contract.
                if (suffix > 0 && int.TryParse(part.name.Substring(suffix + 1), out _))
                    part.name = part.name.Substring(0, suffix);
            }
        }

        private void OnPreprocessTexture()
        {
            string name = Path.GetFileNameWithoutExtension(assetPath);
            if (!Textures.Contains(name) || !string.Equals(Path.GetDirectoryName(assetPath)?.Replace('\\', '/') + "/",
                ModelFolder, StringComparison.OrdinalIgnoreCase) || !(assetImporter is TextureImporter importer)) return;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = name != "ChildFace";
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }

        [MenuItem("Bar Promenade/City Fair/Validate Child Models")]
        public static void ValidateOrThrow()
        {
            Require(File.Exists(ManifestPath), "Missing child art manifest: " + ManifestPath);
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            Require(manifest != null && Mathf.Abs(manifest.height_m - 1.30f) < .001f &&
                manifest.bone_count == 47 && manifest.bones?.Length == 47 && manifest.parts?.Length > 0 &&
                manifest.outfits?.Length == 3 && manifest.props?.Length == 2, "Incomplete child art manifest.");
            Require(manifest.bones.Select(bone => bone.name).Distinct().Count() == manifest.bone_count,
                "The child manifest repeats a bone name.");
            Require(manifest.parts.Select(part => part.name).Distinct().Count() == manifest.parts.Length,
                "The child manifest repeats a renderer name.");
            foreach (string name in Models)
                AssetDatabase.ImportAsset(ModelFolder + name + ".fbx", ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            foreach (string name in Textures)
            {
                string path = ModelFolder + name + ".png";
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                ValidateTexture(name, path);
            }
            var sharedBody = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            for (int i = 0; i < CityFairChildAssetProvider.OutfitNames.Length; i++)
            {
                string name = CityFairChildAssetProvider.OutfitNames[i];
                Outfit outfit = manifest.outfits.SingleOrDefault(entry => entry.name == name);
                Require(outfit != null && outfit.visible_parts != null && outfit.visible_triangles >= 9000 &&
                    outfit.visible_triangles <= 12000, "Invalid equipped child outfit: " + name);
                GameObject actor = CityFairChildAssetProvider.Create(i, null);
                var baked = new Mesh();
                try
                {
                    Animator animator = actor.GetComponentInChildren<Animator>(true);
                    Require(animator != null && animator.avatar != null && animator.avatar.isValid && !animator.avatar.isHuman,
                        "The child requires its own valid Generic avatar.");
                    animator.enabled = false;
                    actor.GetComponent<NpcWardrobe>().ValidateBindings();
                    ValidateBones(actor, manifest.bones);
                    var actualParts = new Dictionary<string, Renderer>(StringComparer.Ordinal);
                    foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>(true))
                    {
                        Require(!actualParts.ContainsKey(renderer.name), "Duplicate child renderer: " + renderer.name);
                        actualParts.Add(renderer.name, renderer);
                    }
                    Require(actualParts.Count == manifest.parts.Length, "Imported child renderer count differs from the manifest.");
                    Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
                    int triangles = 0;
                    var visible = new HashSet<string>(StringComparer.Ordinal);
                    var partBounds = new Dictionary<string, Bounds>(StringComparer.Ordinal);
                    foreach (Part part in manifest.parts)
                    {
                        Require(actualParts.TryGetValue(part.name, out Renderer renderer), "Missing child renderer: " + part.name);
                        bool shouldShow = part.visible_body || part.outfit == name;
                        Require(renderer.enabled == shouldShow && renderer.gameObject.activeInHierarchy,
                            "Child body coverage differs from the manifest: " + name + "/" + part.name);
                        Mesh source = SourceMesh(renderer);
                        Require(source != null && source.uv.Length == source.vertexCount && source.normals.Length == source.vertexCount,
                            "Child mesh lost geometry, normals or UVs: " + part.name);
                        int partTriangles = TriangleCount(source);
                        Require(partTriangles == part.triangles, "Child part triangle count differs: " + part.name);
                        Require(renderer.sharedMaterial != null && renderer.sharedMaterial.GetTexture("_BaseMap") ==
                            AssetDatabase.LoadAssetAtPath<Texture2D>(ModelFolder + part.texture + ".png"),
                            "Child painted surface differs: " + part.name);
                        if (string.IsNullOrEmpty(part.outfit))
                        {
                            if (sharedBody.TryGetValue(part.name, out Mesh shared))
                                Require(source == shared, "The three outfits must share the same child mesh: " + part.name);
                            else sharedBody.Add(part.name, source);
                        }
                        Measure(renderer, source, baked, out Vector3 partLow, out Vector3 partHigh);
                        Near(partLow, part.bounds_min, part.name + " minimum");
                        Near(partHigh, part.bounds_max, part.name + " maximum");
                        partBounds.Add(part.name, new Bounds((partLow + partHigh) * .5f, partHigh - partLow));
                        if (!shouldShow) continue;
                        visible.Add(part.name);
                        triangles += partTriangles;
                        low = Vector3.Min(low, partLow);
                        high = Vector3.Max(high, partHigh);
                    }
                    Require(visible.SetEquals(outfit.visible_parts), "Equipped child parts differ: " + name);
                    Require(triangles == outfit.visible_triangles && triangles >= 9000 && triangles <= 12000,
                        $"Equipped child {name}: {triangles} visible triangles, expected {outfit.visible_triangles} in [9000,12000].");
                    Near(low, outfit.bounds_min, name + " equipped minimum");
                    Near(high, outfit.bounds_max, name + " equipped maximum");
                    ValidateTrouserContinuity(name, actualParts, partBounds, baked);
                    Require(actor.GetComponentsInChildren<Collider>(true).Length == 0 && actor.GetComponentsInChildren<Light>(true).Length == 0,
                        "Child assets must not import gameplay collision or lights.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(baked);
                    UnityEngine.Object.DestroyImmediate(actor);
                }
            }
            foreach (Prop prop in manifest.props) ValidateProp(prop);
            Debug.Log("CITY FAIR CHILD MODELS: shared child rig/body, three measured equipped outfits, painted surfaces and physical toy anchors OK.");
        }

        private static void ValidateTexture(string name, string path)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            int width = name == "ChildFace" || name.EndsWith("Atlas", StringComparison.Ordinal) ? 512 : 256;
            int height = name == "ChildFace" ? 128 : width;
            Require(texture != null && texture.width == width && texture.height == height && texture.filterMode == FilterMode.Point,
                "Child texture dimensions/filter differ: " + path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Require(importer != null && importer.sRGBTexture && importer.mipmapEnabled == (name != "ChildFace") &&
                importer.textureCompression == TextureImporterCompression.Uncompressed && texture.wrapMode == TextureWrapMode.Clamp,
                "Child texture import differs: " + path);
        }

        private static void ValidateBones(GameObject actor, Bone[] bones)
        {
            Transform[] transforms = actor.GetComponentsInChildren<Transform>(true);
            foreach (Bone bone in bones)
            {
                Transform[] matches = transforms.Where(part => part.name == bone.name).ToArray();
                Require(matches.Length == 1, "Missing or duplicate child bone: " + bone.name);
                Near(matches[0].position, bone.head, "bone " + bone.name, .003f);
                if (!string.IsNullOrEmpty(bone.parent))
                    Require(matches[0].parent != null && matches[0].parent.name == bone.parent,
                        "Child bone hierarchy differs: " + bone.name);
                Require(bone.tail != null && bone.tail.Length == 3, "Child bone has no authored tail: " + bone.name);
            }
        }

        private static void ValidateTrouserContinuity(string outfit, Dictionary<string, Renderer> renderers,
            Dictionary<string, Bounds> bounds, Mesh baked)
        {
            string prefix = "OUTFIT_" + outfit + "_";
            string waistName = prefix + "TrouserWaist";
            Require(renderers.TryGetValue(waistName, out Renderer waistRenderer) && waistRenderer.enabled &&
                waistRenderer is SkinnedMeshRenderer, "Child trousers need a visible skinned waist: " + outfit);
            Bounds waist = bounds[waistName];
            Bounds torso = bounds[prefix + "Body"];
            Require(waist.max.y >= torso.min.y + .01f,
                "Child trouser waist must continue inside the upper garment's hem: " + outfit);
            foreach (string side in new[] { "L", "R" })
            {
                Bounds leg = bounds[prefix + "Trousers." + side];
                Vector3 overlap = Vector3.Min(waist.max, leg.max) - Vector3.Max(waist.min, leg.min);
                Require(overlap.x > .025f && overlap.y > .015f && overlap.z > .025f,
                    "Child trouser waist must overlap both skinned leg tops: " + outfit + "/" + side);
            }
            // The original short tops ended at .695 m and the trouser tubes
            // at .619 m. Global outfit bounds/triangle counts hid the empty
            // pelvis. Measure both faces of that actual band, including its
            // centre, rather than accepting another disconnected bounding box.
            var skin = (SkinnedMeshRenderer)waistRenderer;
            baked.Clear();
            skin.BakeMesh(baked, false);
            Vector3[] vertices = baked.vertices;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = skin.transform.TransformPoint(vertices[i]);
            int[] triangles = baked.triangles;
            foreach (float x in new[] { -.075f, 0f, .075f })
                foreach (float y in new[] { .63f, .66f, .69f })
                {
                    bool front = false, back = false;
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                        float denominator = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
                        if (Mathf.Abs(denominator) < .0000001f) continue;
                        float u = ((b.y - c.y) * (x - c.x) + (c.x - b.x) * (y - c.y)) / denominator;
                        float v = ((c.y - a.y) * (x - c.x) + (a.x - c.x) * (y - c.y)) / denominator;
                        float w = 1f - u - v;
                        if (u < -.0001f || v < -.0001f || w < -.0001f) continue;
                        float z = a.z * u + b.z * v + c.z * w;
                        front |= z > .025f;
                        back |= z < -.025f;
                    }
                    Require(front && back, $"Child {outfit} has no continuous trouser pelvis at x={x:F3}, y={y:F3}m.");
                }
        }

        private static void ValidateProp(Prop prop)
        {
            Require(prop.name == "LowTable" || prop.name == "WoodenCar", "Unexpected child prop: " + prop.name);
            GameObject model = CityFairChildAssetProvider.CreateProp(prop.name, null);
            try
            {
                Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
                int triangles = 0;
                foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter.sharedMesh;
                    Require(mesh != null && mesh.uv.Length == mesh.vertexCount, "Child toy lost mesh/UVs: " + filter.name);
                    triangles += TriangleCount(mesh);
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        Vector3 point = filter.transform.TransformPoint(vertex);
                        low = Vector3.Min(low, point); high = Vector3.Max(high, point);
                    }
                }
                Near(low, prop.bounds_min, prop.name + " minimum");
                Near(high, prop.bounds_max, prop.name + " maximum");
                Require(triangles == prop.triangles, "Child toy triangle count differs: " + prop.name);
                Require(prop.anchors != null, "Child toy anchors are absent: " + prop.name);
                foreach (Anchor anchor in prop.anchors)
                {
                    Transform point = CityFairChildAssetProvider.FindPart(model, anchor.name);
                    Near(point.position, anchor.position, prop.name + "/" + anchor.name, .005f);
                    if (!string.IsNullOrEmpty(anchor.parent))
                        Require(point.IsChildOf(CityFairChildAssetProvider.FindPart(model, anchor.parent)),
                            "Child toy anchor lost its parent: " + anchor.name);
                }
                if (prop.name == "WoodenCar")
                    foreach (string suffix in new[] { "FL", "FR", "RL", "RR" })
                    {
                        Transform pivot = CityFairChildAssetProvider.FindPart(model, "WheelPivot_" + suffix);
                        Require(Vector3.Dot(pivot.right, Vector3.right) > .999f &&
                            Vector3.Dot(pivot.up, Vector3.up) > .999f && Vector3.Distance(pivot.lossyScale, Vector3.one) < .0001f,
                            "Toy wheel requires canonical Unity X rotation: " + suffix);
                    }
                Require(model.GetComponentsInChildren<Collider>(true).Length == 0 && model.GetComponentsInChildren<Light>(true).Length == 0 &&
                    model.GetComponentsInChildren<Animator>(true).Length == 0, "Child toy must remain passive: " + prop.name);
            }
            finally { UnityEngine.Object.DestroyImmediate(model); }
        }

        private static Mesh SourceMesh(Renderer renderer) => renderer is SkinnedMeshRenderer skin
            ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;

        private static int TriangleCount(Mesh mesh)
        {
            int result = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) result += (int)mesh.GetIndexCount(i) / 3;
            return result;
        }

        private static void Measure(Renderer renderer, Mesh source, Mesh baked, out Vector3 low, out Vector3 high)
        {
            Mesh measured = source;
            if (renderer is SkinnedMeshRenderer skin)
            {
                Require(skin.bones.Length == source.bindposes.Length && skin.bones.All(bone => bone != null),
                    "Child skin lost its imported bones: " + renderer.name);
                baked.Clear();
                skin.BakeMesh(baked, false);
                measured = baked;
            }
            low = Vector3.positiveInfinity; high = Vector3.negativeInfinity;
            foreach (Vector3 vertex in measured.vertices)
            {
                Vector3 point = renderer.transform.TransformPoint(vertex);
                low = Vector3.Min(low, point); high = Vector3.Max(high, point);
            }
        }

        private static void Near(Vector3 actual, float[] expected, string label, float tolerance = .015f)
        {
            Require(expected != null && expected.Length == 3, "Child manifest has no measured point: " + label);
            var target = new Vector3(expected[0], expected[1], expected[2]);
            Require(Vector3.Distance(actual, target) <= tolerance, $"Child {label}: actual={actual:F6}, expected={target:F6} metres.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        [Serializable] private sealed class Manifest
        {
            public float height_m; public int bone_count; public Bone[] bones;
            public Part[] parts; public Outfit[] outfits; public Prop[] props;
        }
        [Serializable] private sealed class Bone { public string name, parent; public float[] head, tail; public bool deform; }
        [Serializable] private sealed class Part
        {
            public string name, texture, outfit; public bool visible_body; public int triangles; public float[] bounds_min, bounds_max;
        }
        [Serializable] private sealed class Outfit { public string name; public int visible_triangles; public string[] visible_parts; public float[] bounds_min, bounds_max; }
        [Serializable] private sealed class Prop { public string name; public int triangles; public float[] bounds_min, bounds_max; public Anchor[] anchors; }
        [Serializable] private sealed class Anchor { public string name, parent; public float[] position; }
    }
}
