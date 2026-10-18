using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Dedicated Generic crane bank and compact passive suspension geometry.</summary>
    public sealed class CombatCraneAssetSetup : AssetPostprocessor
    {
        public const string Folder = "Assets/Resources/Combat/";
        public const string BankPath = Folder + "CraneActions.fbx";
        public const string BodyPath = Folder + "CraneBody.fbx";
        private const string WorkerSource = "Assets/Resources/VillageLife/StationWorker.fbx";
        public override uint GetVersion() => 3;
        private bool IsBank => assetPath == BankPath;
        private bool IsBody => assetPath == BodyPath;
        private bool IsPassive => assetPath == Folder + "CraneSuspensionHead.fbx" || assetPath == Folder + "CraneCableSegment.fbx" ||
            assetPath == Folder + "CraneRails.fbx" || assetPath == Folder + "CraneBridge.fbx" ||
            assetPath == Folder + "CraneHook.fbx" || assetPath == Folder + "CraneWristSocket.fbx";

        private void OnPreprocessModel()
        {
            if ((!IsBank && !IsBody && !IsPassive) || !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            importer.importBlendShapes = IsBody; importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = IsBank;
            importer.animationType = IsBank || IsBody ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            if (IsBody)
            {
                context.DependsOnArtifact(WorkerSource);
                importer.isReadable = true; importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
            }
            if (!IsBank) return;
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(Player3DV2AssetSetup.ModelPath).OfType<Avatar>().FirstOrDefault();
            importer.avatarSetup = avatar != null ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = avatar;
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!IsBody) return;
            GameObject ordinary = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerSource);
            // The artifact dependency schedules this skin again after its rig
            // reference completes on a clean first import.
            if (ordinary == null) return;
            var originals = ordinary.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .ToDictionary(renderer => renderer.name, StringComparer.Ordinal);
            var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform bone in model.GetComponentsInChildren<Transform>(true))
                if (!bones.TryAdd(bone.name, bone))
                    throw new InvalidOperationException("Crane body import requires unambiguous bone and mesh names: " + bone.name);
            foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!originals.TryGetValue(renderer.name, out SkinnedMeshRenderer original))
                    throw new InvalidOperationException("Crane body lost its original anatomical source: " + renderer.name);
                CanonicalizeBodySkin(renderer, original, bones);
            }
        }

        private static void CanonicalizeBodySkin(SkinnedMeshRenderer renderer, SkinnedMeshRenderer original,
            Dictionary<string, Transform> hierarchy)
        {
            Mesh mesh = renderer.sharedMesh, reference = original.sharedMesh;
            if (mesh == null || reference == null || mesh.vertexCount == 0 || mesh.vertexCount > 100000 ||
                mesh.triangles.Length == 0 || mesh.normals.Length != mesh.vertexCount || mesh.boneWeights.Length != mesh.vertexCount)
                throw new InvalidOperationException("Crane sculpt requires finite authored geometry and skin: " + renderer.name);
            // New topology is independent. Its vertices stay in the retained
            // renderer's local frame so the existing skeleton can own it.
            Transform[] previous = renderer.bones, canonical = original.bones;
            Matrix4x4[] previousBind = mesh.bindposes, canonicalBind = reference.bindposes;
            if (previous.Length != previousBind.Length || canonical.Length != canonicalBind.Length)
                throw new InvalidOperationException("Crane body has an incomplete imported skin: " + renderer.name);
            var canonicalIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            var bound = new Transform[canonical.Length];
            for (int bone = 0; bone < canonical.Length; bone++)
            {
                string name = canonical[bone].name;
                if (!canonicalIndices.TryAdd(name, bone) || !hierarchy.TryGetValue(name, out bound[bone]))
                    throw new InvalidOperationException("Crane body lost a retained rig bone: " + name);
            }
            var remap = new int[previous.Length];
            for (int bone = 0; bone < previous.Length; bone++)
            {
                if (previous[bone] == null || !canonicalIndices.TryGetValue(previous[bone].name, out remap[bone]))
                    throw new InvalidOperationException("Crane body introduced a noncanonical skin bone: " + renderer.name);
                for (int cell = 0; cell < 16; cell++)
                    if (Mathf.Abs(previousBind[bone][cell] - canonicalBind[remap[bone]][cell]) > .0001f)
                        throw new InvalidOperationException("Crane body changed its retained local bind frame: " + renderer.name);
            }
            BoneWeight[] weights = mesh.boneWeights;
            for (int vertex = 0; vertex < weights.Length; vertex++)
            {
                BoneWeight weight = weights[vertex];
                if (!float.IsFinite(weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3) ||
                    Mathf.Abs(weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3 - 1f) > .002f)
                    throw new InvalidOperationException("Crane sculpt has a missing or nonnormalized skin weight: " + renderer.name);
                weight.boneIndex0 = CanonicalIndex(weight.boneIndex0, weight.weight0, remap, renderer.name);
                weight.boneIndex1 = CanonicalIndex(weight.boneIndex1, weight.weight1, remap, renderer.name);
                weight.boneIndex2 = CanonicalIndex(weight.boneIndex2, weight.weight2, remap, renderer.name);
                weight.boneIndex3 = CanonicalIndex(weight.boneIndex3, weight.weight3, remap, renderer.name);
                weights[vertex] = weight;
            }
            mesh.boneWeights = weights; mesh.bindposes = canonicalBind; renderer.bones = bound;
            mesh.RecalculateBounds(); renderer.localBounds = mesh.bounds;
            if (original.rootBone != null)
            {
                if (!hierarchy.TryGetValue(original.rootBone.name, out Transform root))
                    throw new InvalidOperationException("Crane body lost its retained skin root.");
                renderer.rootBone = root;
            }
            CombatCraneAppearance.ValidateBodyBinding(original, renderer);
        }

        private static int CanonicalIndex(int index, float weight, int[] remap, string name)
        {
            if (weight == 0f) return 0;
            if (weight < 0f || (uint)index >= remap.Length)
                throw new InvalidOperationException("Crane sculpt has an invalid source bone weight: " + name);
            return remap[index];
        }

        private static void ValidateRetainedGeometry(Mesh mesh, Mesh reference, string name)
        {
            if (mesh == null || reference == null || mesh.triangles.Length != reference.triangles.Length)
                throw new InvalidOperationException("Crane surface changed its retained geometry: " + name);
            var positions = new HashSet<Vector3Int>();
            foreach (Vector3 vertex in reference.vertices) positions.Add(Vector3Int.RoundToInt(vertex * 1000000f));
            var imported = new HashSet<Vector3Int>();
            foreach (Vector3 vertex in mesh.vertices) imported.Add(Vector3Int.RoundToInt(vertex * 1000000f));
            if (!positions.SetEquals(imported))
                throw new InvalidOperationException("Crane surface changed its retained local vertex positions: " + name);
        }

        private void OnPreprocessTexture()
        {
            bool albedo = assetPath == Folder + "CraneBodyAtlas.png";
            if ((!albedo && assetPath != Folder + "CraneMaterialResponse.png") || !(assetImporter is TextureImporter importer)) return;
            int size = albedo ? 2048 : 1024;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = albedo; importer.mipmapEnabled = true; importer.isReadable = false;
            importer.alphaSource = albedo ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false; importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear; importer.anisoLevel = 1; importer.maxTextureSize = size;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            var windows = importer.GetPlatformTextureSettings("Standalone");
            windows.overridden = true; windows.maxTextureSize = size; windows.format = TextureImporterFormat.BC7;
            importer.SetPlatformTextureSettings(windows);
        }

        private void OnPreprocessAnimation()
        {
            if (!IsBank || !(assetImporter is ModelImporter importer)) return;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                int separator = clip.name.LastIndexOf('|');
                if (separator >= 0) clip.name = clip.name.Substring(separator + 1);
                if (Array.IndexOf(CombatCraneAssetProvider.ClipNames, clip.name) < 0)
                    throw new InvalidOperationException("Unexpected crane action " + clip.name);
                clip.loopTime = CombatCraneAssetProvider.IsLoop(clip.name); clip.loopPose = false;
                clip.keepOriginalOrientation = true; clip.keepOriginalPositionXZ = true; clip.keepOriginalPositionY = true;
                clip.lockRootRotation = true; clip.lockRootPositionXZ = true; clip.lockRootHeightY = true;
                clip.events = Array.Empty<AnimationEvent>();
            }
            importer.clipAnimations = clips;
        }

        [MenuItem("Bar Promenade/Combat Test/Validate Crane Assets")]
        public static void BuildOrThrow()
        {
            foreach (string file in new[] { "CraneActions.fbx", "CraneSuspensionHead.fbx", "CraneCableSegment.fbx", "CraneRails.fbx", "CraneBridge.fbx",
                "CraneHook.fbx", "CraneWristSocket.fbx", "Crane3D.json", "CraneBody.fbx", "CraneBody.json",
                "CraneBodyAtlas.png", "CraneMaterialResponse.png" })
                AssetDatabase.ImportAsset(Folder + file, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "Crane3D.json"));
            if (manifest == null || !manifest.test_only || manifest.root_motion || manifest.animation_events != 0 ||
                manifest.rig != "NpcHumanV2" || !manifest.retained_rest_skeleton ||
                Mathf.Abs(manifest.body_pose_scale - CombatCraneAssetProvider.BodyPoseScale) > .0001f || manifest.clips == null ||
                manifest.clips.Length != CombatCraneAssetProvider.ClipNames.Length)
                throw new InvalidOperationException("Crane manifest violates its retained suspension contract.");
            foreach (string name in CombatCraneAssetProvider.ClipNames)
                CombatTestAssetSetup.ValidateInPlaceClip(CombatCraneAssetProvider.LoadClip(name));
            GameObject head = CombatCraneAssetProvider.LoadSuspensionHead();
            Transform swivel = CityPedestrianHandProps.FindSocket(head.transform, CombatCraneAssetProvider.HeadSwivelAnchor);
            if (swivel == null) throw new InvalidOperationException("Crane head lacks its lower cable swivel.");
            foreach (string name in CombatCraneAssetProvider.HeadAnchorNames)
            {
                Transform feed = CityPedestrianHandProps.FindSocket(head.transform, name);
                if (feed == null || !feed.IsChildOf(swivel))
                    throw new InvalidOperationException("Crane feed must belong to the lower swivel: " + name);
            }
            CombatCraneAssetProvider.LoadCableSegment();
            if (manifest.models == null || manifest.models.Length != 6)
                throw new InvalidOperationException("Crane manifest lacks its measured passive geometry.");
            foreach (Model model in manifest.models) ValidatePlacedModel(model);
            foreach (string name in new[] { CombatCraneAssetProvider.HookCableAnchor, CombatCraneAssetProvider.HookImpactAnchor })
                if (CityPedestrianHandProps.FindSocket(CombatCraneAssetProvider.LoadHook().transform, name) == null)
                    throw new InvalidOperationException("Crane hook is missing " + name);
            if (CityPedestrianHandProps.FindSocket(CombatCraneAssetProvider.LoadWristSocket().transform, CombatCraneAssetProvider.WristCableAnchor) == null)
                throw new InvalidOperationException("Crane wrist lacks its authored cable exit.");
            ValidateBodySurfaces();
            Debug.Log("COMBAT CRANE IMPORTED ACTIONS AND SUSPENSION OK");
        }

        private static void ValidateBodySurfaces()
        {
            CombatCraneAssetProvider.BodyManifest manifest = CombatCraneAssetProvider.LoadBodyManifest();
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath);
            if (model == null) throw new InvalidOperationException("Missing imported crane body.");
            var originals = DefaultNpcCatalog.GetPrefab().GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .ToDictionary(renderer => renderer.name, StringComparer.Ordinal);
            var meshes = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .ToDictionary(renderer => renderer.name, StringComparer.Ordinal);
            if (meshes.Count != manifest.meshes.Length)
                throw new InvalidOperationException("Crane manifest must account for every imported anatomical mesh.");
            foreach (CombatCraneAssetProvider.BodyMesh part in manifest.meshes)
            {
                if (!meshes.TryGetValue(part.name, out SkinnedMeshRenderer renderer) || !originals.TryGetValue(part.name, out SkinnedMeshRenderer original))
                    throw new InvalidOperationException("Crane body renderer lost its named original: " + part.name);
                CombatCraneAppearance.ValidateBodyBinding(original, renderer);
                Mesh mesh = renderer.sharedMesh;
                if (!mesh.isReadable || mesh.uv.Length != mesh.vertexCount || mesh.subMeshCount != part.surfaces.Length)
                    throw new InvalidOperationException("Crane sculpt must retain unique UV0 and authored material slots: " + part.name);
                foreach (Vector2 uv in mesh.uv)
                    if (!float.IsFinite(uv.x) || !float.IsFinite(uv.y) || uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
                        throw new InvalidOperationException("Crane body UV0 escaped its unique sheet: " + part.name);
                Bounds bounds = MeasureAuthoredBounds(renderer);
                Vector3 minimum = new Vector3(part.bounds_min[0], part.bounds_min[1], part.bounds_min[2]);
                Vector3 maximum = new Vector3(part.bounds_max[0], part.bounds_max[1], part.bounds_max[2]);
                if (Vector3.Distance(bounds.min, minimum) > .003f || Vector3.Distance(bounds.max, maximum) > .003f)
                    throw new InvalidOperationException("Crane sculpt differs from its authored metre envelope: " + part.name + "; actual=" + bounds + "; expected=" + minimum + ".." + maximum);
                if (part.visible && (part.name.StartsWith("GEO_Ear", StringComparison.Ordinal) || Array.IndexOf(CombatCraneAssetProvider.RightHandRendererNames, part.name) >= 0))
                    throw new InvalidOperationException("Crane body must keep its ears and replaced right hand hidden: " + part.name);
            }
            var passive = model.GetComponentsInChildren<MeshFilter>(true)
                .ToDictionary(filter => filter.name, StringComparer.Ordinal);
            if (passive.Count != manifest.passive_meshes.Length + (manifest.hardware?.Length ?? 0))
                throw new InvalidOperationException("Crane manifest must account for every imported passive surface.");
            var passiveModels = new Dictionary<string, Dictionary<string, MeshFilter>>(StringComparer.Ordinal);
            foreach (CombatCraneAssetProvider.PassiveMesh part in manifest.passive_meshes)
            {
                if (!passiveModels.TryGetValue(part.model, out Dictionary<string, MeshFilter> sources))
                {
                    GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + part.model + ".fbx");
                    if (template == null) throw new InvalidOperationException("Missing original passive crane model: " + part.model);
                    sources = template.GetComponentsInChildren<MeshFilter>(true).ToDictionary(filter => filter.name, StringComparer.Ordinal);
                    passiveModels.Add(part.model, sources);
                }
                if (!sources.TryGetValue(part.name, out MeshFilter original) || !passive.TryGetValue(part.name, out MeshFilter renderer))
                    throw new InvalidOperationException("Crane passive surface lost its named original: " + part.name);
                ValidateRetainedGeometry(renderer.sharedMesh, original.sharedMesh, part.name);
                if (renderer.sharedMesh.uv.Length != renderer.sharedMesh.vertexCount)
                    throw new InvalidOperationException("Crane passive surface lost its authored UV0: " + part.name);
            }
            if (manifest.hardware != null)
                foreach (CombatCraneAssetProvider.HardwareMesh part in manifest.hardware)
                {
                    if (!passive.TryGetValue(part.name, out MeshFilter fitting) || fitting.transform.parent == null || fitting.transform.parent.name != part.bone)
                        throw new InvalidOperationException("Crane fitting lost its authored bone-local frame: " + part.name);
                    Mesh mesh = fitting.sharedMesh;
                    if (mesh == null || mesh.vertexCount == 0 || mesh.uv.Length != mesh.vertexCount)
                        throw new InvalidOperationException("Crane fitting lost its authored atlas UV0: " + part.name);
                    foreach (Vector2 uv in mesh.uv)
                        if (!float.IsFinite(uv.x) || !float.IsFinite(uv.y) || uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
                            throw new InvalidOperationException("Crane fitting UV0 escaped its unique sheet: " + part.name);
                }
            long bytes = 0;
            foreach (CombatCraneAssetProvider.BodyTexture sheet in manifest.textures)
            {
                string path = Folder + sheet.name + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null || importer == null || texture.width != sheet.size || texture.height != sheet.size ||
                    importer.maxTextureSize != sheet.size || importer.sRGBTexture != sheet.sRGB || importer.isReadable ||
                    !importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Clamp)
                    throw new InvalidOperationException("Crane surface import differs from its bounded profile: " + sheet.name);
                TextureImporterPlatformSettings windows = importer.GetPlatformTextureSettings("Standalone");
                if (!windows.overridden || windows.format != TextureImporterFormat.BC7 || windows.maxTextureSize != sheet.size)
                    throw new InvalidOperationException("Crane body requires Windows BC7 with mipmaps: " + sheet.name);
                using (SHA256 hash = SHA256.Create())
                    if (!string.Equals(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""), sheet.sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Crane surface bytes differ from their authoring manifest: " + sheet.name);
                for (int size = sheet.size; ; size /= 2)
                {
                    long blocks = Math.Max(1, (size + 3) / 4); bytes += blocks * blocks * 16;
                    if (size == 1) break;
                }
            }
            if (manifest.estimated_gpu_bytes != bytes || bytes > 7 * 1024 * 1024)
                throw new InvalidOperationException("Crane body surfaces exceed their measured BC7/mipmap budget.");
            if (CombatCraneAssetProvider.LoadBodyMaterial().shader != Resources.Load<Shader>("Shaders/WarehouseSurface"))
                throw new InvalidOperationException("Crane body requires the shared authored surface shader.");
        }

        private static Bounds MeasureAuthoredBounds(SkinnedMeshRenderer renderer)
        {
            Vector3[] vertices = renderer.sharedMesh.vertices;
            if (vertices.Length == 0) throw new InvalidOperationException("Crane sculpt has no authored volume: " + renderer.name);
            Matrix4x4 world = renderer.transform.localToWorldMatrix;
            Bounds result = new Bounds(world.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            foreach (Vector3 vertex in vertices)
            {
                Vector3 point = world.MultiplyPoint3x4(vertex);
                if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z))
                    throw new InvalidOperationException("Crane sculpt has a nonfinite authored vertex: " + renderer.name);
                result.Encapsulate(point);
            }
            return result;
        }

        private static void ValidatePlacedModel(Model expected)
        {
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + expected.name + ".fbx");
            if (template == null || expected.bounds_min == null || expected.bounds_max == null)
                throw new InvalidOperationException("Missing measured crane model " + expected.name);
            GameObject placed = UnityEngine.Object.Instantiate(template);
            try
            {
                // Keep the FBX root scale. World renderer bounds detect a lost
                // centimetre factor even when all logical anchors still agree.
                Bounds actual = default; bool found = false;
                foreach (Renderer renderer in placed.GetComponentsInChildren<Renderer>(true))
                {
                    if (!found) { actual = renderer.bounds; found = true; }
                    else actual.Encapsulate(renderer.bounds);
                }
                Vector3 minimum = new Vector3(expected.bounds_min[0], expected.bounds_min[1], expected.bounds_min[2]);
                Vector3 maximum = new Vector3(expected.bounds_max[0], expected.bounds_max[1], expected.bounds_max[2]);
                if (!found || Vector3.Distance(actual.min, minimum) > .015f || Vector3.Distance(actual.max, maximum) > .015f)
                    throw new InvalidOperationException("Crane imported geometry differs from its measured bounds: " + expected.name +
                        "; actual=" + actual + "; expected=" + minimum + ".." + maximum);
                // Fixed hangers join the joists outside the fan aperture.
                // Only the moving bridge/head must remain below its rotor.
                if ((expected.name == "CraneSuspensionHead" || expected.name == "CraneBridge") && actual.max.y > .28f)
                    throw new InvalidOperationException("Crane gantry exceeds its fan clearance: " + expected.name);
                if (expected.name == "CraneRails" && actual.max.y > .73f)
                    throw new InvalidOperationException("Crane fixed hangers exceed the declared joist attachment.");
            }
            finally { UnityEngine.Object.DestroyImmediate(placed); }
        }

        [Serializable] private sealed class Manifest
        {
            public bool test_only, root_motion, retained_rest_skeleton;
            public int animation_events;
            public string rig;
            public float body_pose_scale;
            public Clip[] clips;
            public Model[] models;
        }
        [Serializable] private sealed class Clip { public string name; public float duration_seconds; public bool loop; }
        [Serializable] private sealed class Model { public string name; public float[] bounds_min, bounds_max; }
    }
}
