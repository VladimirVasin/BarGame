using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Editor
{
    /// <summary>Explicit measured import; packaging only validates the resulting authored assets.</summary>
    public static class EastGuardAssetSetup
    {
        public const string Folder = "Assets/Resources/City/EastExit/Guards/";
        public const string RifleModelPath = Folder + "EastGuardRifle.fbx";
        public const string RiflePrefabPath = Folder + "EastGuardRifleProp.prefab";
        private static readonly string[] Actions = { "Idle", "Walk", "Listen", "Break" };
        private static readonly string[] Joints = { "root", "pelvis", "spine", "chest", "neck", "head",
            "upper_arm.L", "forearm.L", "hand.L", "upper_arm.R", "forearm.R", "hand.R",
            "thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R",
            "SOCKET_Grip.L", "SOCKET_Grip.R", "SOCKET_Mouth", "SOCKET_ShoulderCarry" };

        [MenuItem("Bar Promenade/City East Exit/Rebuild Duty Guards")]
        public static void RunBatch() => BuildOrThrow();

        public static void BuildOrThrow()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            BuildRifle();
            for (int index = 0; index < 2; index++) BuildActor(index);
            AssetDatabase.SaveAssets();
            ValidateOrThrow();
        }

        private static void BuildActor(int index)
        {
            string name = EastGuardAssetProvider.Name(index);
            string modelPath = Folder + name + ".fbx", actionPath = Folder + name + "Actions.fbx";
            ConfigureModel(modelPath, false, null, true);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid) throw new InvalidOperationException(name + " lacks his own valid Generic Avatar.");
            ConfigureModel(actionPath, true, avatar, true);
            ConfigureTexture(Folder + name + "Atlas.png");
            ConfigureTexture(Folder + name + "FaceAtlas.png");
            Manifest source = ReadManifest(index);
            AnimationClip[] own = LoadClips(actionPath);
            AnimationClip Own(string action) => own.Single(clip => clip.name == name + action);
            // Only these four actions can be selected by the duty controller.
            // Unused shared sampler slots retain this actor's planted Idle;
            // no unrelated village gesture or anatomy is retargeted onto him.
            AnimationClip[] bindings = Enum.GetNames(typeof(VillageResidentAction)).Select(_ => Own("Idle")).ToArray();
            bindings[(int)VillageResidentAction.Walk] = Own("Walk");
            var root = new GameObject(name + "Actor");
            try
            {
                GameObject imported = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(imported);
                model.name = "Model"; model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * imported.transform.localRotation;
                model.transform.localScale = imported.transform.localScale;
                root.transform.localScale = Vector3.one * source.height_scale;
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                Material material = CharacterMaterial();
                foreach (Renderer renderer in renderers)
                {
                    renderer.sharedMaterial = material;
                    if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
                }
                Color Tint(string rendererName)
                {
                    Part part = source.parts.Single(value => value.name == rendererName);
                    return new Color(part.color[0], part.color[1], part.color[2], part.color[3]).gamma;
                }
                Renderer face = renderers.Single(renderer => renderer.name == "GEO_FaceSurface");
                var wardrobe = root.AddComponent<NpcWardrobe>();
                NpcWardrobe.GarmentBinding[] outfit = source.outfit.parts.SelectMany(part => part.renderers.Select(rendererName =>
                    new NpcWardrobe.GarmentBinding(part.slot, renderers.Single(renderer => renderer.name == rendererName),
                        Tint(rendererName)))).ToArray();
                wardrobe.Configure(source.outfit.id, AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + source.outfit.atlas), material, outfit);
                Renderer[] permanent = renderers.Where(renderer => renderer != face && !wardrobe.Owns(renderer)).ToArray();
                Animator animator = model.GetComponentInChildren<Animator>() ?? model.AddComponent<Animator>();
                animator.avatar = avatar; animator.applyRootMotion = false; animator.runtimeAnimatorController = null;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Transform Find(string joint) => FindOrThrow(model.transform, joint);
                var motion = root.AddComponent<VillageResidentPresentation>();
                motion.ConfigureAuthoredActor(name, animator, model.transform, Find("SOCKET_Grip.R"), Find("SOCKET_Grip.L"),
                    Find("head"), bindings, permanent, permanent.Select(renderer => Tint(renderer.name)).ToArray(),
                    AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + name + "Atlas.png"), Own("Break"), Own("Listen"));
                ValidateBody(model, root.transform, source);
                Transform socket = Find("SOCKET_ShoulderCarry");
                GameObject riflePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RiflePrefabPath);
                GameObject rifle = (GameObject)PrefabUtility.InstantiatePrefab(riflePrefab);
                rifle.name = "Slung Rifle";
                AlignRifle(rifle.transform, socket);
                var actor = root.AddComponent<EastGuardActor>();
                actor.Configure(index, motion, face, socket, rifle.transform, source.height_m,
                    -source.bounds_min[2] * source.height_scale);
                ValidateCarry(actor);
                PrefabUtility.SaveAsPrefabAsset(root, Folder + name + "Actor.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void BuildRifle()
        {
            ConfigureModel(RifleModelPath, false, null, false);
            RifleManifest source = ReadRifleManifest();
            string texturePath = Folder + source.texture;
            ConfigureTexture(texturePath);
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            GameObject imported = AssetDatabase.LoadAssetAtPath<GameObject>(RifleModelPath);
            if (imported == null) throw new InvalidOperationException("Missing authored duty rifle model.");
            var root = new GameObject("EastGuardRifleProp");
            try
            {
                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(imported);
                model.transform.SetParent(root.transform, false);
                // Keep the whole imported basis rather than separating its
                // centimetre meshes from their FBX conversion root.
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    Part part = source.parts.Single(value => value.name == renderer.name);
                    bool wood = part.role == "Furniture", dark = part.role == "Dark";
                    string path = Folder + (wood ? "EastGuardRifleWood.mat" : dark ? "EastGuardRifleDark.mat" : "EastGuardRifleMetal.mat");
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null)
                    {
                        material = new Material(RuntimePrimitiveFactory.DefaultMaterial.shader) { name = Path.GetFileNameWithoutExtension(path) };
                        AssetDatabase.CreateAsset(material, path);
                    }
                    Color tint = wood ? new Color(.24f, .17f, .105f) : dark ? new Color(.065f, .070f, .065f) : new Color(.115f, .135f, .125f);
                    material.SetColor("_BaseColor", tint); material.SetColor("_Color", tint);
                    material.SetTexture("_BaseMap", atlas); material.SetTexture("_MainTex", atlas);
                    material.SetFloat("_Smoothness", .14f); material.SetFloat("_Metallic", wood ? 0f : .42f);
                    EditorUtility.SetDirty(material); renderer.sharedMaterial = material;
                }
                ValidateRifle(root);
                PrefabUtility.SaveAsPrefabAsset(root, RiflePrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void AlignRifle(Transform rifle, Transform socket)
        {
            Transform anchor = FindOrThrow(rifle, "ANCHOR_Carry");
            Quaternion anchorBasis = Quaternion.Inverse(rifle.rotation) * anchor.rotation;
            rifle.SetParent(socket, true);
            rifle.rotation = socket.rotation * Quaternion.Inverse(anchorBasis);
            rifle.position += socket.position - anchor.position;
        }

        public static void ValidateOrThrow()
        {
            GameObject rifle = AssetDatabase.LoadAssetAtPath<GameObject>(RiflePrefabPath);
            if (rifle == null) throw new InvalidOperationException("Missing authored eastern rifle prefab.");
            ValidateRifle(rifle);
            for (int index = 0; index < 2; index++)
            {
                string name = EastGuardAssetProvider.Name(index);
                Manifest source = ReadManifest(index);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + name + "Actor.prefab");
                EastGuardActor actor = prefab != null ? prefab.GetComponent<EastGuardActor>() : null;
                if (actor == null || actor.Index != index || actor.Motion == null || actor.FaceRenderer == null || actor.Wardrobe == null ||
                    actor.Motion.Animator == null || actor.Motion.Animator.avatar == null || !actor.Motion.Animator.avatar.isValid ||
                    actor.Motion.Animator.applyRootMotion || actor.Motion.Animator.runtimeAnimatorController != null)
                    throw new InvalidOperationException(name + " lost his bone-only presentation, face or wardrobe.");
                actor.Wardrobe.ValidateBindings();
                if (actor.Wardrobe.CurrentOutfitId != source.outfit.id || actor.Wardrobe.Owns(actor.FaceRenderer))
                    throw new InvalidOperationException(name + " must keep clothing separate from his permanent painted face.");
                foreach (NpcWardrobe.GarmentBinding garment in actor.Wardrobe.Garments)
                    if (!IsGarment(source.parts.Single(part => part.name == garment.Renderer.name)))
                        throw new InvalidOperationException(name + " put skin, hair or rifle sling into a clothing slot.");
                foreach (string joint in Joints) FindOrThrow(actor.Motion.ModelRoot, joint);
                foreach (string slingName in source.carry.sling_renderers)
                {
                    Renderer sling = actor.Motion.ModelRoot.GetComponentsInChildren<Renderer>(true).Single(r => r.name == slingName);
                    if (actor.Wardrobe.Owns(sling) || !actor.CarrySocket.IsChildOf(FindOrThrow(actor.Motion.ModelRoot, source.carry.bone)))
                        throw new InvalidOperationException(name + " lost his permanent shoulder sling or chest-owned carry socket.");
                }
                Texture2D face = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + name + "FaceAtlas.png");
                if (face == null || face.width != 512 || face.height != 256 || face.filterMode != FilterMode.Point)
                    throw new InvalidOperationException(name + " face must retain the shared painted 8 x 4 atlas.");
                AnimationClip[] clips = LoadClips(Folder + name + "Actions.fbx");
                if (clips.Length != Actions.Length || Actions.Any(action => clips.All(clip => clip.name != name + action)))
                    throw new InvalidOperationException(name + " needs exactly his own Idle/Walk/Listen/Break actions.");
                foreach (AnimationClip clip in clips)
                    if (clip.length <= 0f || !clip.isLooping || AnimationUtility.GetAnimationEvents(clip).Length != 0 ||
                        AnimationUtility.GetCurveBindings(clip).Any(binding => binding.type != typeof(Transform)))
                        throw new InvalidOperationException(name + " action must remain looping, in-place bone-only data: " + clip.name);
                ValidateActions(name, clips);
                ValidateBody(actor.Motion.ModelRoot.gameObject, prefab.transform, source);
                ValidateCarry(actor);
            }
        }

        private static void ValidateCarry(EastGuardActor actor)
        {
            if (actor.RifleRoot == null || actor.CarrySocket == null || !actor.RifleRoot.IsChildOf(actor.CarrySocket))
                throw new InvalidOperationException("A duty rifle must remain attached to its author's shoulder socket.");
            Transform carry = FindOrThrow(actor.RifleRoot, "ANCHOR_Carry");
            if (Vector3.Distance(carry.position, actor.CarrySocket.position) > .002f ||
                Quaternion.Angle(carry.rotation, actor.CarrySocket.rotation) > .1f)
                throw new InvalidOperationException("Duty rifle carry anchors disagree after imported hierarchy placement.");
            Vector3 a = FindOrThrow(actor.RifleRoot, "ANCHOR_SlingUpper").position;
            Vector3 b = FindOrThrow(actor.RifleRoot, "ANCHOR_SlingLower").position;
            RifleManifest rifle = ReadRifleManifest();
            float authoredLength = Vector3.Distance(Point(rifle.anchors.ANCHOR_SlingUpper), Point(rifle.anchors.ANCHOR_SlingLower));
            if (Mathf.Abs(Vector3.Distance(a, b) - authoredLength) > .002f)
                throw new InvalidOperationException("The separate rifle lost its real metre scale at the shoulder.");
            Manifest body = ReadManifest(actor.Index);
            Transform hand = FindOrThrow(actor.Motion.ModelRoot, body.carry.hand_contact_anchor);
            float expectedReach = Vector3.Distance(Point(body.carry.socket_rest_blender), Point(body.carry.hand_contact_blender));
            if (Mathf.Abs(Vector3.Distance(actor.CarrySocket.position, hand.position) - expectedReach * body.height_scale) > .003f)
                throw new InvalidOperationException("Duty guard sling hand/socket anchors lost their authored relation.");
        }

        private static void ValidateRifle(GameObject root)
        {
            FindOrThrow(root.transform, "ANCHOR_Carry"); FindOrThrow(root.transform, "ANCHOR_SlingUpper");
            FindOrThrow(root.transform, "ANCHOR_SlingLower");
            if (root.GetComponentsInChildren<Collider>(true).Length != 0 || root.GetComponentsInChildren<Animator>(true).Length != 0)
                throw new InvalidOperationException("The separate duty rifle must remain a passive authored prop.");
            RifleManifest source = ReadRifleManifest();
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = Measure(renderers, root.transform, out int triangles);
            Vector3 sourceSize = Point(source.bounds_max) - Point(source.bounds_min);
            Vector3 expected = new Vector3(sourceSize.x, sourceSize.z, sourceSize.y);
            if ((bounds.size - expected).sqrMagnitude > .00001f || triangles != source.triangle_count ||
                renderers.Length != source.mesh_count || Mathf.Abs(bounds.min.y - source.bounds_min[2]) > .002f)
                throw new InvalidOperationException("Duty rifle imported metre/geometry contract failed: " + bounds);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + source.texture);
            if (texture == null || texture.width != 256 || texture.height != 256 || texture.filterMode != FilterMode.Point ||
                texture.wrapMode != TextureWrapMode.Clamp || renderers.Any(renderer => renderer.sharedMaterial == null ||
                    renderer.sharedMaterial.GetTexture("_BaseMap") != texture))
                throw new InvalidOperationException("Duty rifle must retain one shared authored wear atlas on all three materials.");
        }

        private static RifleManifest ReadRifleManifest()
        {
            RifleManifest source = JsonUtility.FromJson<RifleManifest>(File.ReadAllText(Folder + "EastGuardRifle.json"));
            if (source == null || source.mesh_count != 3 || source.triangle_count < 100 || source.triangle_count > 2000 ||
                source.texture != "EastGuardRifleAtlas.png" ||
                source.bounds_min?.Length != 3 || source.bounds_max?.Length != 3 || source.parts?.Length != source.mesh_count ||
                source.anchors == null || source.anchors.ANCHOR_Carry?.Length != 3 ||
                source.anchors.ANCHOR_SlingUpper?.Length != 3 || source.anchors.ANCHOR_SlingLower?.Length != 3)
                throw new InvalidOperationException("Duty rifle source lost its measured prop/anchor contract.");
            return source;
        }

        private static void ValidateActions(string name, AnimationClip[] imported)
        {
            ActionManifest source = JsonUtility.FromJson<ActionManifest>(File.ReadAllText(Folder + name + "Actions.json"));
            if (source == null || source.root_motion || source.bone_count != 31 || source.fps != 24 || source.clips?.Length != 4 ||
                source.validation == null || source.validation.max_ground_error_m > .002f ||
                source.validation.max_grip_error_m > .002f || source.validation.max_loop_endpoint_error > .002f)
                throw new InvalidOperationException(name + " source actions lost their planted ground, sling grip or closed loop.");
            foreach (ActionClip clip in source.clips)
                if (!clip.loop || Mathf.Abs(imported.Single(value => value.name == clip.name).length - clip.duration_seconds) > 1f / source.fps)
                    throw new InvalidOperationException(name + " imported clip duration differs from authored loop: " + clip.name);
        }

        private static void ValidateBody(GameObject model, Transform actorRoot, Manifest manifest)
        {
            Transform rifle = FindOptional(actorRoot, "Slung Rifle");
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => rifle == null || !renderer.transform.IsChildOf(rifle)).ToArray();
            if (renderers.Length != manifest.mesh_count)
                throw new InvalidOperationException("Duty guard renderer count differs from his source.");
            Bounds bounds = Measure(renderers, actorRoot, out int triangles);
            Vector3 expected = new Vector3(manifest.bounds_max[0] - manifest.bounds_min[0],
                manifest.bounds_max[2] - manifest.bounds_min[2], manifest.bounds_max[1] - manifest.bounds_min[1]);
            if ((bounds.size - expected).sqrMagnitude > .001f || Mathf.Abs(bounds.min.y - manifest.bounds_min[2]) > .015f ||
                triangles != manifest.triangle_count)
                throw new InvalidOperationException("Duty guard imported metre/triangle contract failed: " + bounds + "; expected " + expected);
        }

        private static Bounds Measure(Renderer[] renderers, Transform space, out int triangles)
        {
            triangles = 0; bool first = true; Bounds bounds = default;
            foreach (Renderer renderer in renderers)
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || mesh.uv.Length != mesh.vertexCount)
                    throw new InvalidOperationException("Duty model lost authored geometry/UVs: " + renderer.name);
                for (int i = 0; i < mesh.subMeshCount; i++) triangles += (int)mesh.GetIndexCount(i) / 3;
                foreach (Vector3 vertex in mesh.vertices)
                {
                    Vector3 point = space.InverseTransformPoint(renderer.transform.TransformPoint(vertex));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
                }
            }
            if (first) throw new InvalidOperationException("Duty model contains no visible mesh.");
            return bounds;
        }

        private static Manifest ReadManifest(int index)
        {
            string name = EastGuardAssetProvider.Name(index);
            Manifest value = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + name + ".json"));
            if (value == null || value.mesh_count < 8 || value.triangle_count < 3000 || value.triangle_count > 14000 ||
                value.height_scale <= 0f || value.bone_count != 31 || value.parts == null ||
                Mathf.Abs(value.height_m - (index == 0 ? 1.82f : 1.90f)) > .04f ||
                value.bounds_min?.Length != 3 || value.bounds_max?.Length != 3)
                throw new InvalidOperationException(name + " source lacks its measured anatomy/rig contract.");
            if (value.parts.Any(part => string.IsNullOrEmpty(part.name) || part.color?.Length != 4) ||
                value.parts.Select(part => part.name).Distinct().Count() != value.parts.Length || value.parts.Length != value.mesh_count ||
                value.outfit == null || value.outfit.id != EastGuardAssetProvider.OutfitId(index) ||
                string.IsNullOrEmpty(value.outfit.atlas) || value.outfit.parts == null)
                throw new InvalidOperationException(name + " source lacks its explicit parts/outfit contract.");
            if (value.carry == null || value.carry.socket != "SOCKET_ShoulderCarry" || value.carry.bone != "chest" ||
                value.carry.prop != "EastGuardRifle" || value.carry.prop_anchor != "ANCHOR_Carry" ||
                value.carry.socket_rest_blender?.Length != 3 || value.carry.hand_contact_blender?.Length != 3 ||
                string.IsNullOrEmpty(value.carry.hand_contact_anchor) || value.carry.sling_renderers?.Length != 2 ||
                value.carry.sling_renderers.Any(renderer => value.parts.Single(part => part.name == renderer).role != "accessory"))
                throw new InvalidOperationException(name + " source lost its independent slung prop and permanent sling contract.");
            string[] garments = value.outfit.parts.SelectMany(part => part.renderers).ToArray();
            string[] expected = value.parts.Where(IsGarment).Select(part => part.name).ToArray();
            if (garments.Distinct().Count() != garments.Length || !garments.OrderBy(s => s).SequenceEqual(expected.OrderBy(s => s)))
                throw new InvalidOperationException(name + " outfit must own each garment once and exclude skin, hair, face and sling.");
            return value;
        }

        private static void ConfigureModel(string path, bool animate, Avatar avatar, bool rigged)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing generated duty model " + path);
            importer.animationType = rigged ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            if (rigged)
            {
                importer.avatarSetup = avatar != null ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
                importer.sourceAvatar = avatar;
            }
            importer.importAnimation = animate; importer.globalScale = 1f; importer.useFileScale = true;
            importer.bakeAxisConversion = true; importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.isReadable = true; importer.importCameras = false; importer.importLights = false;
            importer.addCollider = false; importer.importBlendShapes = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off; importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.None;
            importer.SaveAndReimport();
            if (!animate) return;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                int split = clip.name.LastIndexOf('|'); if (split >= 0) clip.name = clip.name.Substring(split + 1);
                clip.loopTime = true; clip.loopPose = false; clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionXZ = clip.keepOriginalPositionY = true;
                clip.lockRootRotation = clip.lockRootPositionXZ = clip.lockRootHeightY = true;
            }
            importer.clipAnimations = clips; importer.SaveAndReimport();
        }

        private static void ConfigureTexture(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing painted duty atlas " + path);
            importer.textureType = TextureImporterType.Default; importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true; importer.alphaSource = TextureImporterAlphaSource.None;
            importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false; importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None; importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
        }

        private static Material CharacterMaterial() => AssetDatabase.LoadAssetAtPath<Material>("Assets/Player3D/Materials/Player3DLit.mat")
            ?? throw new InvalidOperationException("Missing shared character material.");
        private static AnimationClip[] LoadClips(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
        private static Transform FindOptional(Transform root, string name) => CityPedestrianHandProps.FindSocket(root, name);
        private static Transform FindOrThrow(Transform root, string name) => FindOptional(root, name)
            ?? throw new InvalidOperationException("Missing duty joint/anchor " + name);
        private static bool IsGarment(Part part) => part.role == "clothing" || part.role == "headwear" || part.role == "footwear";
        private static Vector3 Point(float[] value) => new Vector3(value[0], value[1], value[2]);
        [Serializable] private sealed class Manifest
        { public int mesh_count, triangle_count, bone_count; public float height_scale, height_m; public float[] bounds_min, bounds_max;
            public Part[] parts; public Outfit outfit; public Carry carry; }
        [Serializable] private sealed class Carry
        { public string socket, bone, prop, prop_anchor, hand_contact_anchor; public string[] sling_renderers;
            public float[] socket_rest_blender, hand_contact_blender; }
        [Serializable] private sealed class RifleManifest
        { public int mesh_count, triangle_count; public string texture; public float[] bounds_min, bounds_max; public Part[] parts; public RifleAnchors anchors; }
        [Serializable] private sealed class RifleAnchors { public float[] ANCHOR_Carry, ANCHOR_SlingUpper, ANCHOR_SlingLower; }
        [Serializable] private sealed class ActionManifest
        { public int bone_count, fps; public bool root_motion; public ActionClip[] clips; public ActionValidation validation; }
        [Serializable] private sealed class ActionClip { public string name; public float duration_seconds; public bool loop; }
        [Serializable] private sealed class ActionValidation
        { public float max_ground_error_m, max_grip_error_m, max_loop_endpoint_error; }
        [Serializable] private sealed class Part { public string name, role; public float[] color; }
        [Serializable] private sealed class Outfit { public string id, atlas; public OutfitPart[] parts; }
        [Serializable] private sealed class OutfitPart { public string slot; public string[] renderers; }
    }
}
