using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Explicit authoring/import and read-only packaging validation for the one line operator.</summary>
    public static class CanneryWomanAssetSetup
    {
        public const string Folder = "Assets/Resources/City/Cannery/Woman/";
        public const string ModelPath = Folder + "CanneryWoman.fbx";
        public const string ActionPath = Folder + "CanneryWomanActions.fbx";
        public const string PrefabPath = Folder + "CanneryWomanActor.prefab";
        private static readonly string[] RequiredActions = { "CanneryWomanIdle", "CanneryWomanWalk",
            "CanneryWomanWork", "CanneryWomanListen", "CanneryWomanBreak" };
        private static readonly string[] RequiredJoints = { "root", "pelvis", "spine", "chest", "neck", "head",
            "upper_arm.L", "forearm.L", "hand.L", "upper_arm.R", "forearm.R", "hand.R",
            "thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R",
            "SOCKET_Grip.L", "SOCKET_Grip.R", "SOCKET_Mouth" };

        [MenuItem("Bar Promenade/City Cannery/Rebuild Woman")]
        public static void RunBatch() => BuildOrThrow();

        public static void BuildOrThrow()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureModel(ModelPath, false, null);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid)
                throw new InvalidOperationException("The cannery woman's own narrower rig requires its generated Generic Avatar.");
            ConfigureModel(ActionPath, true, avatar);
            ConfigureTexture(Folder + "CanneryWomanAtlas.png");
            ConfigureTexture(Folder + "CanneryWomanFaceAtlas.png");
            AnimationClip[] own = LoadClips(ActionPath);
            AnimationClip[] ordinary = new[] { VillageResidentAssetSetup.AnimationPath,
                VillageResidentAssetSetup.LifeAnimationPath, VillageResidentAssetSetup.WorkroomAnimationPath,
                VillageResidentAssetSetup.ErrandAnimationPath }.SelectMany(LoadClips).ToArray();
            AnimationClip[] bindings = Enum.GetNames(typeof(VillageResidentAction))
                .Select(name => ordinary.Single(clip => clip.name == name)).ToArray();
            AnimationClip Own(string name) => own.Single(clip => clip.name == "CanneryWoman" + name);
            bindings[(int)VillageResidentAction.Idle] = Own("Idle");
            bindings[(int)VillageResidentAction.Walk] = Own("Walk");
            bindings[(int)VillageResidentAction.StationWork] = Own("Work");
            Manifest manifest = ReadManifest();
            GameObject root = new GameObject("Cannery Woman");
            try
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                model.name = "Model"; model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(0, 180, 0);
                // Keep imported root/bone unit conversion below the same wrapper as NpcHumanV2.
                model.transform.localScale = Vector3.one;
                root.transform.localScale = Vector3.one * manifest.height_scale;
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Player3D/Materials/Player3DLit.mat");
                if (material == null) throw new InvalidOperationException("Missing shared character material.");
                foreach (Renderer renderer in renderers)
                {
                    renderer.sharedMaterials = new[] { material };
                    if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
                }
                Renderer face = renderers.Single(renderer => renderer.name == "GEO_FaceSurface");
                CanneryWomanWardrobe.GarmentBinding[] outfit = manifest.outfit.parts.SelectMany(part =>
                    part.renderers.Select(name => new CanneryWomanWardrobe.GarmentBinding(part.slot,
                        renderers.Single(renderer => renderer.name == name), Tint(name)))).ToArray();
                var wardrobe = root.AddComponent<CanneryWomanWardrobe>();
                wardrobe.Configure(manifest.outfit.id,
                    AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + manifest.outfit.atlas), material, outfit);
                Renderer[] permanent = renderers.Where(renderer => renderer != face && !wardrobe.Owns(renderer)).ToArray();
                Color[] colors = permanent.Select(renderer => Tint(renderer.name)).ToArray();
                Color Tint(string name)
                {
                    Part part = manifest.parts.Single(entry => entry.name == name);
                    return new Color(part.color[0], part.color[1], part.color[2], part.color[3]);
                }
                Animator animator = model.GetComponentInChildren<Animator>() ?? model.AddComponent<Animator>();
                animator.avatar = avatar; animator.applyRootMotion = false; animator.runtimeAnimatorController = null;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Transform Find(string name) => CityPedestrianHandProps.FindSocket(model.transform, name)
                    ?? throw new InvalidOperationException("Missing cannery woman joint " + name);
                VillageResidentPresentation motion = root.AddComponent<VillageResidentPresentation>();
                motion.ConfigureAuthoredActor("CanneryWoman", animator, model.transform, Find("SOCKET_Grip.R"),
                    Find("SOCKET_Grip.L"), Find("head"), bindings, permanent, colors,
                    AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "CanneryWomanAtlas.png"), Own("Break"), Own("Listen"));
                CanneryWomanHair hair = root.AddComponent<CanneryWomanHair>();
                hair.Configure(motion);
                hair.ConfigureEnvelopes(manifest.hair_envelopes.Select(envelope => new CanneryWomanHair.HairEnvelope {
                    Chain = envelope.chain,
                    Rings = envelope.rings.Select(ring => new CanneryWomanHair.HairRing {
                        Progress = ring.progress,
                        Center = new Vector3(-ring.center_blender[0], ring.center_blender[2], -ring.center_blender[1]),
                        HalfWidth = ring.half_width, HalfDepth = ring.half_depth, HalfHeight = ring.half_height,
                        Bones = ring.weights.Select(weight => weight.bone).ToArray(),
                        Weights = ring.weights.Select(weight => weight.value).ToArray()
                    }).ToArray()
                }).ToArray());
                root.AddComponent<CanneryWomanPresentation>().Configure(motion, face);
                ValidateBody(root, manifest);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            ValidateOrThrow();
        }

        public static void ValidateOrThrow()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null || prefab.GetComponent<CanneryWomanPresentation>() == null)
                throw new InvalidOperationException("Missing authored cannery woman prefab.");
            var woman = prefab.GetComponent<CanneryWomanPresentation>();
            if (woman.Motion == null || woman.FaceRenderer == null || woman.Hair == null || !woman.Hair.HasAuthoredBindings || woman.Motion.Animator == null ||
                woman.Motion.Animator.applyRootMotion || woman.Motion.Animator.runtimeAnimatorController != null)
                throw new InvalidOperationException("Cannery woman lost her shared bone-only presentation bindings.");
            if (woman.Wardrobe == null || woman.Wardrobe.CurrentOutfitId != CanneryWomanWardrobe.WorkwearId)
                throw new InvalidOperationException("Cannery woman lost the explicit current outfit binding.");
            woman.Wardrobe.ValidateBindings();
            if (woman.Wardrobe.Owns(woman.FaceRenderer))
                throw new InvalidOperationException("The painted face cannot belong to a clothing outfit.");
            Manifest source = ReadManifest();
            foreach (CanneryWomanWardrobe.GarmentBinding garment in woman.Wardrobe.Garments)
                if (!IsGarment(source.parts.Single(part => part.name == garment.Renderer.name)))
                    throw new InvalidOperationException("Permanent skin or hair cannot belong to a clothing outfit.");
            foreach (string joint in RequiredJoints)
                if (CityPedestrianHandProps.FindSocket(woman.Motion.ModelRoot, joint) == null)
                    throw new InvalidOperationException("Cannery woman lost joint/socket " + joint);
            Texture2D face = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "CanneryWomanFaceAtlas.png");
            if (face == null || face.width != 512 || face.height != 256 || face.filterMode != FilterMode.Point)
                throw new InvalidOperationException("Cannery woman face must retain the shared 8 x 4 painted atlas contract.");
            AnimationClip[] clips = LoadClips(ActionPath);
            if (clips.Length != RequiredActions.Length || RequiredActions.Any(name => !clips.Any(clip => clip.name == name)))
                throw new InvalidOperationException("Cannery woman must retain her five authored actions.");
            foreach (AnimationClip clip in clips)
                if (clip.length <= 0f || !clip.isLooping || AnimationUtility.GetAnimationEvents(clip).Length != 0 ||
                    AnimationUtility.GetCurveBindings(clip).Any(binding => binding.type != typeof(Transform)))
                    throw new InvalidOperationException("Cannery woman's actions must be looping bone-only curves: " + clip.name);
            ValidateBody(prefab, source);
        }

        private static void ConfigureModel(string path, bool animation, Avatar avatar)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing generated cannery woman model " + path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = avatar != null ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = avatar;
            importer.importAnimation = animation; importer.globalScale = 1f; importer.useFileScale = true;
            importer.bakeAxisConversion = true; importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.isReadable = true; importer.importCameras = false; importer.importLights = false;
            importer.addCollider = false; importer.importBlendShapes = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.None;
            importer.SaveAndReimport();
            if (!animation) return;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                int separator = clip.name.LastIndexOf('|');
                if (separator >= 0) clip.name = clip.name.Substring(separator + 1);
                clip.loopTime = true; clip.loopPose = false; clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionXZ = clip.keepOriginalPositionY = true;
                clip.lockRootRotation = clip.lockRootPositionXZ = clip.lockRootHeightY = true;
            }
            importer.clipAnimations = clips; importer.SaveAndReimport();
        }

        private static void ConfigureTexture(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing painted cannery woman texture " + path);
            importer.textureType = TextureImporterType.Default; importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true; importer.alphaSource = TextureImporterAlphaSource.None;
            importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false; importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None; importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
        }

        private static AnimationClip[] LoadClips(string path) => AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>().Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();

        private static Manifest ReadManifest()
        {
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "CanneryWoman.json"));
            if (manifest == null || manifest.triangle_count < 6000 || manifest.triangle_count > 8000 ||
                manifest.height_scale <= 0f || manifest.parts == null)
                throw new InvalidOperationException("Cannery woman source violates the revised 6–8 thousand triangle contract.");
            if (manifest.outfit == null || manifest.outfit.id != CanneryWomanWardrobe.WorkwearId ||
                string.IsNullOrEmpty(manifest.outfit.atlas) || manifest.outfit.parts == null || manifest.outfit.parts.Length == 0)
                throw new InvalidOperationException("Cannery woman source needs its one explicit outfit contract.");
            if (manifest.hair_envelopes == null || manifest.hair_envelopes.Length != 3 ||
                manifest.hair_envelopes.Any(envelope => envelope.rings == null || envelope.rings.Length != 13 ||
                    envelope.rings.Any(ring => ring.center_blender == null || ring.center_blender.Length != 3 ||
                        ring.half_width <= 0f || ring.half_depth <= 0f || ring.weights == null ||
                        ring.weights.Length == 0 || ring.weights.Length > 4 ||
                        Mathf.Abs(ring.weights.Sum(weight => weight.value) - 1f) > .0001f)))
                throw new InvalidOperationException("Cannery woman needs measured skinned envelopes for all three long hair surfaces.");
            string[] garments = manifest.outfit.parts.SelectMany(part => part.renderers).ToArray();
            string[] expected = manifest.parts.Where(IsGarment).Select(part => part.name).ToArray();
            if (garments.Distinct().Count() != garments.Length || !garments.OrderBy(name => name).SequenceEqual(expected.OrderBy(name => name)))
                throw new InvalidOperationException("Outfit bindings must contain every garment exactly once, and no permanent skin, hair or face.");
            return manifest;
        }

        private static bool IsGarment(Part part) => part.role == "clothing" || part.role == "headwear" || part.role == "footwear";

        private static void ValidateBody(GameObject actor, Manifest manifest)
        {
            Renderer[] renderers = actor.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length != manifest.mesh_count)
                throw new InvalidOperationException("Cannery woman imported renderer count differs from her source.");
            Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
            int triangles = 0;
            foreach (Renderer renderer in renderers)
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || mesh.uv.Length != mesh.vertexCount)
                    throw new InvalidOperationException("Cannery woman lost authored geometry/UVs: " + renderer.name);
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++) triangles += (int)mesh.GetIndexCount(submesh) / 3;
                foreach (Vector3 vertex in mesh.vertices)
                {
                    Vector3 point = renderer.transform.TransformPoint(vertex);
                    low = Vector3.Min(low, point); high = Vector3.Max(high, point);
                }
                if (renderer.name == "GEO_FaceSurface" && mesh.uv.Any(uv => uv.x < -.001f || uv.x > 1.001f || uv.y < -.001f || uv.y > 1.001f))
                    throw new InvalidOperationException("The painted curved face must retain face-local 0–1 UVs.");
            }
            Vector3 expected = new Vector3(manifest.bounds_max[0] - manifest.bounds_min[0],
                manifest.bounds_max[2] - manifest.bounds_min[2], manifest.bounds_max[1] - manifest.bounds_min[1]) * manifest.height_scale;
            Vector3 error = high - low - expected;
            if (Mathf.Abs(error.x) > .015f || Mathf.Abs(error.y) > .015f || Mathf.Abs(error.z) > .015f ||
                Mathf.Abs(low.y - actor.transform.position.y) > .015f || triangles != manifest.triangle_count)
                throw new InvalidOperationException($"Cannery woman imported metre/triangle mismatch: {high - low:F4} / {expected:F4}, bottom {low.y:F4}, triangles {triangles}/{manifest.triangle_count}.");
        }

        [Serializable] private sealed class Manifest
        {
            public int mesh_count, triangle_count; public float height_scale;
            public float[] bounds_min, bounds_max; public Part[] parts;
            public Outfit outfit;
            public HairEnvelope[] hair_envelopes;
        }
        [Serializable] private sealed class Part { public string name, role; public float[] color; }
        [Serializable] private sealed class Outfit { public string id, atlas; public OutfitPart[] parts; }
        [Serializable] private sealed class OutfitPart { public string slot; public string[] renderers; }
        [Serializable] private sealed class HairEnvelope { public string renderer, chain; public HairRing[] rings; }
        [Serializable] private sealed class HairRing
        {
            public float progress, half_width, half_depth, half_height;
            public float[] center_blender;
            public HairWeight[] weights;
        }
        [Serializable] private sealed class HairWeight { public string bone; public float value; }
    }
}
