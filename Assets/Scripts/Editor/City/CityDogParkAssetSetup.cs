using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Raw Resources FBX is the passive prefab, as with City laundry.</summary>
    public sealed class CityDogParkAssetSetup : AssetPostprocessor
    {
        public const string ModelFolder = "Assets/Resources/City/DogPark/";
        public const string ManifestPath = ModelFolder + "CityDogPark3D.json";
        public override uint GetVersion() => 1;

        private void OnPreprocessTexture()
        {
            if (assetPath != ModelFolder + "DogParkPalette.png" || !(assetImporter is TextureImporter importer)) return;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Point;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 32;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelFolder, StringComparison.Ordinal) || !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.animationType = ModelImporterAnimationType.None; importer.importAnimation = false;
            importer.importCameras = false; importer.importLights = false; importer.importBlendShapes = false;
            importer.addCollider = false; importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.weldVertices = true; importer.keepQuads = false;
            importer.generateSecondaryUV = false; importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        private sealed class PivotBinding
        {
            public Transform Pivot;
            public Transform Parent;
            public Transform[] Children;
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!assetPath.StartsWith(ModelFolder, StringComparison.Ordinal)) return;
            Transform[] parts = model.GetComponentsInChildren<Transform>(true);
            var bindings = new List<PivotBinding>();
            foreach (Transform part in parts)
            {
                int dot = part.name.LastIndexOf('.');
                if (dot > 0 && int.TryParse(part.name.Substring(dot + 1), out _)) part.name = part.name.Substring(0, dot);
                if (!part.name.StartsWith("MOVE_", StringComparison.Ordinal)) continue;
                var binding = new PivotBinding { Pivot = part, Parent = part.parent, Children = new Transform[part.childCount] };
                for (int i = 0; i < part.childCount; i++) binding.Children[i] = part.GetChild(i);
                bindings.Add(binding);
            }
            // First flatten, then normalize, then restore: changing a parent's
            // axes in place would silently rotate all of its child joints.
            foreach (PivotBinding binding in bindings)
                foreach (Transform child in binding.Children) child.SetParent(model.transform, true);
            foreach (PivotBinding binding in bindings)
            {
                binding.Pivot.SetParent(model.transform, true);
                binding.Pivot.localRotation = Quaternion.identity;
                binding.Pivot.localScale = Vector3.one;
            }
            foreach (PivotBinding binding in bindings)
                foreach (Transform child in binding.Children)
                    if (!child.name.StartsWith("MOVE_", StringComparison.Ordinal)) child.SetParent(binding.Pivot, true);
            foreach (PivotBinding binding in bindings)
                if (binding.Parent != null && binding.Parent.name.StartsWith("MOVE_", StringComparison.Ordinal))
                    binding.Pivot.SetParent(binding.Parent, true);
        }

        [MenuItem("Bar Promenade/City Dog Park/Validate Imported Contract")]
        public static void ValidateOrThrow()
        {
            EnsureImportedAssets();
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.design_id != "city_residential_dog_park_v1" || manifest.models == null || manifest.models.Length != 4)
                throw new InvalidOperationException("Dog park manifest is incomplete.");
            for (int index = 0; index < manifest.models.Length; index++)
            {
                GameObject root = index == 0 ? CityDogParkAssetProvider.CreatePark(null) : CityDogParkAssetProvider.CreateDog(null, index - 1);
                try { ValidateModel(root, manifest.models[index]); }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ModelFolder + "DogParkPalette.png");
            if (texture == null || texture.width != 16 || texture.height != 1 || texture.isReadable || texture.filterMode != FilterMode.Point)
                throw new InvalidOperationException("Dog park palette lost its finite shared import contract.");
            Debug.Log("CITY DOG PARK IMPORTED METRES, PAW CONTACTS AND SHARED SURFACES OK");
        }

        /// <summary>Finish the finite imported dependency set and verify typed
        /// Resources before constructing runtime presentation. A legacy empty
        /// texture importer can otherwise identify the palette as a cubemap.</summary>
        public static void EnsureImportedAssets()
        {
            string[] assets = { "DogParkPalette.png", "StaticDogPark.fbx", "DogSmall.fbx", "DogShaggy.fbx", "DogLarge.fbx" };
            foreach (string name in assets)
            {
                string path = ModelFolder + name;
                if (!File.Exists(path)) throw new InvalidOperationException("Dog park authored source is missing: " + path);
                if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                    throw new InvalidOperationException("Dog park source failed synchronous import: " + path);
            }
            // Resolve typed resources too: this gives an exact diagnostic
            // before PaletteMaterial or the City scene can run.
            EnsureResource<Texture2D>(CityDogParkAssetProvider.ResourceFolder + "DogParkPalette", ModelFolder + "DogParkPalette.png");
        }

        private static void EnsureResource<T>(string resource, string path) where T : UnityEngine.Object
        {
            if (Resources.Load<T>(resource) != null) return;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            if (Resources.Load<T>(resource) == null)
                throw new InvalidOperationException("Dog park dependency cannot resolve its " + typeof(T).Name +
                    " Resources path after synchronous import: " + resource + " (" + path + ").");
        }

        private static void ValidateModel(GameObject root, Model manifest)
        {
            Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
            int triangles = 0, count = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || mesh.uv.Length != mesh.vertexCount || mesh.colors.Length != mesh.vertexCount)
                    throw new InvalidOperationException("Dog park mesh lost authored UV/color data: " + filter.name);
                foreach (Vector3 vertex in mesh.vertices)
                {
                    Vector3 point = root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    low = Vector3.Min(low, point); high = Vector3.Max(high, point);
                }
                for (int sub = 0; sub < mesh.subMeshCount; sub++) triangles += (int)mesh.GetIndexCount(sub) / 3;
                count++;
            }
            Near(low, manifest.bounds_min, root.name + " minimum");
            Near(high, manifest.bounds_max, root.name + " maximum");
            if (triangles != manifest.triangles || triangles > manifest.maximum_triangles || count != manifest.renderer_count || count > manifest.maximum_renderers)
                throw new InvalidOperationException("Dog park imported mesh budget differs: " + root.name);
            foreach (Anchor anchor in manifest.anchors)
                Near(root.transform.InverseTransformPoint(CityDogParkAssetProvider.FindPart(root.transform, anchor.name).position), anchor.position, anchor.name);
            foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                if (part.name.StartsWith("MOVE_", StringComparison.Ordinal) &&
                    (Vector3.Dot(part.up, Vector3.up) < .999f || Vector3.Dot(part.forward, Vector3.forward) < .999f))
                    throw new InvalidOperationException("Dog park articulation axes must be Unity metres: " + part.name);
            if (root.GetComponentsInChildren<Collider>(true).Length != 0 || root.GetComponentsInChildren<Animator>(true).Length != 0 ||
                root.GetComponentsInChildren<Light>(true).Length != 0 || root.GetComponentsInChildren<AudioSource>(true).Length != 0)
                throw new InvalidOperationException("Dog park imported art must remain passive.");
        }

        private static void Near(Vector3 actual, float[] expected, string name)
        {
            if (expected == null || expected.Length != 3 || Vector3.Distance(actual, new Vector3(expected[0], expected[1], expected[2])) > .015f)
                throw new InvalidOperationException("Dog park " + name + " differs from its authored metres: " + actual);
        }
        [Serializable] private sealed class Manifest { public string design_id; public Model[] models; }
        [Serializable] private sealed class Model
        {
            public string name;
            public float[] bounds_min, bounds_max;
            public int triangles, renderer_count, maximum_triangles, maximum_renderers;
            public Anchor[] anchors;
        }
        [Serializable] private sealed class Anchor { public string name; public float[] position; }
    }
}
