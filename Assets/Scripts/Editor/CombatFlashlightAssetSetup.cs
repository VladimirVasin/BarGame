using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Scoped lamp import proof and current Hero V2 chest-space measurement.</summary>
    public static class CombatFlashlightAssetSetup
    {
        public const string Folder = "Assets/Resources/Combat/Flashlight/";
        public const string ModelPath = Folder + "ChestFlashlight.fbx";
        public const string ManifestPath = Folder + "ChestFlashlight3D.json";
        public const string BindingsPath = Folder + "Bindings.asset";
        private const string HeroManifestPath = "Assets/Player3D/V2/Models/PlayerCharacter3DV2.json";

        // Intentionally a plain explicit setup, not another global model
        // postprocessor. CombatTestAssetSetup already imports this folder with
        // the passive metric model recipe; adding callbacks invalidates every
        // model's importer hash even though this change owns only one prop.
        public static void RebuildBatch() => BuildOrThrow();

        [MenuItem("Bar Promenade/Combat Test/Validate Chest Flashlight")]
        public static void BuildOrThrow()
        {
            if (!File.Exists(ManifestPath) || !File.Exists(ModelPath))
                throw new InvalidOperationException("Missing generated chest lamp model or metric manifest.");
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            Manifest manifest = ReadManifest();
            ValidateMetricGeometry(manifest.models[0]);
            MeasureBodyBinding(manifest, true);
            Debug.Log("CHEST FLASHLIGHT IMPORTED METRES / LEFT M65 CONTACT / CHEST BINDING / FORWARD LENS OK");
        }

        /// <summary>Build gate proof without imports or writing any asset.</summary>
        public static void ValidateOrThrow()
        {
            Manifest manifest = ReadManifest();
            ValidateMetricGeometry(manifest.models[0]);
            MeasureBodyBinding(manifest, false);
        }

        private static Manifest ReadManifest()
        {
            VerifyImporter();
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || !manifest.passive || !manifest.test_only ||
                !manifest.always_on_warehouse_only || manifest.units != "metres" ||
                manifest.models == null || manifest.models.Length != 1 || manifest.mount == null ||
                manifest.mount.physical_side != "Left" || manifest.mount.bone != "chest" ||
                manifest.models[0].name != "ChestFlashlight")
                throw new InvalidOperationException("Chest lamp lost the isolated passive left-chest contract.");
            if (manifest.mount.hero_manifest_sha256 != Sha256(HeroManifestPath))
                throw new InvalidOperationException("Hero V2 changed: regenerate the lamp's measured jacket contact.");
            return manifest;
        }

        private static void VerifyImporter()
        {
            if (!(AssetImporter.GetAtPath(ModelPath) is ModelImporter importer))
                throw new InvalidOperationException("Chest lamp was not imported as a model.");
            // Existing CombatTestAssetSetup owns this recipe. Prove that the
            // imported object still preserves units, anchors and passive scope.
            if (!importer.useFileScale || !importer.bakeAxisConversion || !importer.preserveHierarchy ||
                importer.globalScale != 1f || importer.optimizeGameObjects || !importer.isReadable ||
                importer.importAnimation || importer.importLights || importer.importCameras || importer.addCollider ||
                importer.materialImportMode != ModelImporterMaterialImportMode.None)
                throw new InvalidOperationException("Chest lamp requires the existing passive Combat metric importer recipe.");
        }

        private static void ValidateMetricGeometry(Model model)
        {
            GameObject lamp = CombatFlashlightAssetProvider.CreateUnbound(null);
            try
            {
                Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
                int triangles = 0, meshes = 0;
                foreach (MeshFilter filter in lamp.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.name.IndexOf("Flash", StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new InvalidOperationException("Lamp mesh would be excluded from equipment contacts: " + filter.name);
                    foreach (Vector3 vertex in filter.sharedMesh.vertices)
                    {
                        Vector3 point = filter.transform.TransformPoint(vertex);
                        low = Vector3.Min(low, point); high = Vector3.Max(high, point);
                    }
                    triangles += filter.sharedMesh.triangles.Length / 3;
                    meshes++;
                }
                Near(low, Read(model.bounds_min), .001f, "imported bounds minimum");
                Near(high, Read(model.bounds_max), .001f, "imported bounds maximum");
                if (triangles != model.triangle_count || triangles > 900 || meshes != model.mesh_count)
                    throw new InvalidOperationException("Chest lamp imported geometry budget or mesh count differs.");
                foreach (Anchor anchor in model.anchors)
                    Near(CombatFlashlightAssetProvider.FindAuthoredAnchor(lamp, anchor.name).position,
                        Read(anchor.position), .001f, anchor.name);
                Transform lens = CombatFlashlightAssetProvider.FindLens(lamp);
                if (lens.position.z <= high.z || Vector3.Dot(lens.forward, Vector3.forward) < .999f)
                    throw new InvalidOperationException("Chest lamp light origin must be outside its forward lens.");
                if (lamp.GetComponentsInChildren<Light>(true).Length != 0 ||
                    lamp.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException("The passive lamp asset cannot import lights or collision.");
            }
            finally { UnityEngine.Object.DestroyImmediate(lamp); }
        }

        private static void MeasureBodyBinding(Manifest manifest, bool rebuild)
        {
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player/Player3DV2.prefab");
            if (template == null) throw new InvalidOperationException("Chest lamp requires the production hero prefab.");
            GameObject actor = UnityEngine.Object.Instantiate(template);
            GameObject lamp = null;
            try
            {
                Player3DAssetRegistry registry = actor.GetComponentInChildren<Player3DAssetRegistry>();
                if (registry == null || !registry.TryGetAnimation(manifest.mount.reference_action, out Player3DAnimationBinding action))
                    throw new InvalidOperationException("Lamp binding requires the current production Relaxed pose.");
                if (registry.Animator != null) registry.Animator.enabled = false;
                action.Clip.SampleAnimation(registry.Animator.gameObject, manifest.mount.reference_time);
                Transform chest = registry.Anchors.Chest;
                lamp = CombatFlashlightAssetProvider.CreateUnbound(chest);
                Vector3 position = actor.transform.TransformPoint(Read(manifest.mount.hero_actor_position));
                Quaternion rotation = actor.transform.rotation * Quaternion.LookRotation(
                    Read(manifest.mount.hero_actor_forward), Read(manifest.mount.hero_actor_up));
                lamp.transform.SetPositionAndRotation(position, rotation);
                Vector3 measuredContact = CombatFlashlightAssetProvider.FindAuthoredAnchor(lamp, "ClipContact").position;
                Near(measuredContact, actor.transform.TransformPoint(Read(manifest.mount.hero_actor_contact)),
                    .001f, "physical clip contact");
                Vector3 actorPosition = actor.transform.InverseTransformPoint(lamp.transform.position);
                if (actorPosition.x > -.05f || actorPosition.y < 1.2f || actorPosition.y > 1.5f)
                    throw new InvalidOperationException("Lamp mount lost its physical-left upper-chest position.");
                if (Vector3.Dot(CombatFlashlightAssetProvider.FindLens(lamp).forward, actor.transform.forward) < .999f)
                    throw new InvalidOperationException("Chest lamp must face forward in the reference body pose.");
                Renderer jacket = registry.MeshBindings.Single(binding => binding.MeshName == manifest.mount.jacket_renderer).Renderer;
                float clipGap = SurfaceGap(jacket, measuredContact);
                if (clipGap > manifest.mount.maximum_imported_clip_gap_m)
                    throw new InvalidOperationException("Lamp clip floats away from actual imported M65: " + clipGap + "m.");
                CombatFlashlightBindings bindings = AssetDatabase.LoadAssetAtPath<CombatFlashlightBindings>(BindingsPath);
                bool create = bindings == null;
                if (!rebuild && create) throw new InvalidOperationException("Missing measured chest flashlight bindings.");
                if (create) bindings = ScriptableObject.CreateInstance<CombatFlashlightBindings>();
                string before = EditorJsonUtility.ToJson(bindings);
                if (rebuild)
                {
                    bindings.BoneLocalPosition = chest.InverseTransformPoint(position);
                    bindings.BoneLocalRotation = Quaternion.Inverse(chest.rotation) * rotation;
                    bindings.SourceSignature = manifest.build_signature + ":" + manifest.mount.hero_manifest_sha256;
                    if (create) AssetDatabase.CreateAsset(bindings, BindingsPath);
                    if (create || before != EditorJsonUtility.ToJson(bindings))
                    { EditorUtility.SetDirty(bindings); AssetDatabase.SaveAssetIfDirty(bindings); }
                }
                else if (bindings.SourceSignature != manifest.build_signature + ":" + manifest.mount.hero_manifest_sha256)
                    throw new InvalidOperationException("Chest flashlight bindings are stale; run RebuildBatch.");
                // Exercise the same serialized bone-local pose used at runtime,
                // including its inherited FBX scale and actual render bounds.
                lamp.transform.localPosition = bindings.BoneLocalPosition;
                lamp.transform.localRotation = bindings.BoneLocalRotation;
                Near(lamp.transform.position, position, .0001f, "serialized chest mount");
                if (Quaternion.Angle(lamp.transform.rotation, rotation) > .01f)
                    throw new InvalidOperationException("Serialized chest flashlight rotation differs from authored forward.");
                Debug.Log("CHEST FLASHLIGHT M65 CLIP GAP " + clipGap.ToString("F6") + "m");
            }
            finally
            {
                if (lamp != null) UnityEngine.Object.DestroyImmediate(lamp);
                UnityEngine.Object.DestroyImmediate(actor);
            }
        }

        private static float SurfaceGap(Renderer renderer, Vector3 worldPoint)
        {
            if (!(renderer is SkinnedMeshRenderer skin))
                throw new InvalidOperationException("Lamp contact requires the actual skinned M65 shell.");
            var mesh = new Mesh();
            try
            {
                skin.BakeMesh(mesh, true);
                Vector3 point = skin.transform.InverseTransformPoint(worldPoint);
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                float nearest = float.PositiveInfinity;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                    Vector3 plane = point - normal * Vector3.Dot(point - a, normal);
                    if (Vector3.Dot(Vector3.Cross(b - a, plane - a), normal) >= 0f &&
                        Vector3.Dot(Vector3.Cross(c - b, plane - b), normal) >= 0f &&
                        Vector3.Dot(Vector3.Cross(a - c, plane - c), normal) >= 0f)
                        nearest = Mathf.Min(nearest, Vector3.Distance(worldPoint, skin.transform.TransformPoint(plane)));
                    Edge(a, b); Edge(b, c); Edge(c, a);
                }
                return nearest;
                void Edge(Vector3 a, Vector3 b)
                {
                    Vector3 span = b - a;
                    Vector3 closest = a + span * Mathf.Clamp01(Vector3.Dot(point - a, span) / Mathf.Max(.0000000001f, span.sqrMagnitude));
                    nearest = Mathf.Min(nearest, Vector3.Distance(worldPoint, skin.transform.TransformPoint(closest)));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        private static Vector3 Read(float[] value)
        {
            if (value == null || value.Length != 3) throw new InvalidOperationException("Missing metric lamp vector.");
            return new Vector3(value[0], value[1], value[2]);
        }
        private static void Near(Vector3 actual, Vector3 expected, float tolerance, string label)
        {
            if (Vector3.Distance(actual, expected) > tolerance)
                throw new InvalidOperationException("Chest lamp " + label + " differs: " + actual + " / " + expected);
        }
        private static string Sha256(string path)
        {
            using SHA256 hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        [Serializable] private sealed class Manifest
        { public bool passive, test_only, always_on_warehouse_only; public string units, build_signature; public Model[] models; public Mount mount; }
        [Serializable] private sealed class Model
        { public string name; public float[] bounds_min, bounds_max; public int triangle_count, mesh_count; public Anchor[] anchors; }
        [Serializable] private sealed class Anchor { public string name; public float[] position; }
        [Serializable] private sealed class Mount
        {
            public string bone, reference_action, physical_side, jacket_renderer, hero_manifest_sha256;
            public float reference_time, maximum_imported_clip_gap_m;
            public float[] hero_actor_position, hero_actor_contact, hero_actor_forward, hero_actor_up;
        }
    }
}
