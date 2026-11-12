using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static class DefaultNpcWomanAssetSetup
    {
        private const string Folder = "Assets/Resources/City/Residents/";
        public const string ModelPath = Folder + "OrdinaryWoman.fbx";
        private const string ActionPath = Folder + "OrdinaryWomanActions.fbx";
        private const string ManifestPath = Folder + "OrdinaryWoman.json";
        private const string PrefabPath = "Assets/Resources/" + DefaultNpcCatalog.OrdinaryWomanResourcePath + ".prefab";

        [MenuItem("Bar Promenade/Default NPC/Rebuild Ordinary Woman")]
        public static void BuildOrThrow()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Manifest data = ReadManifest();
            CanneryWomanAssetSetup.ConfigureModel(ModelPath, false, null, blendShapes: true);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().Single();
            if (!avatar.isValid || avatar.isHuman)
                throw new InvalidOperationException("The woman needs the Generic Avatar of her own rest skeleton.");
            CanneryWomanAssetSetup.ConfigureModel(ActionPath, true, avatar,
                loops: name => data.clips.Single(clip => clip.name == name).loop);
            foreach (string texture in data.faces.Select(face => face.texture).Append(data.clothing_atlas))
                CanneryWomanAssetSetup.ConfigureTexture(Folder + texture);

            AnimationClip[] bank = ReadClips(data);
            AnimationClip Clip(string name) => bank.Single(clip => clip.name == "OrdinaryWoman" + name);
            var shared = DefaultNpcCatalog.GetPrefab().GetComponent<VillageResidentPresentation>();
            AnimationClip[] actions = Enum.GetValues(typeof(VillageResidentAction)).Cast<VillageResidentAction>()
                .Select(shared.GetClip).ToArray();
            actions[(int)VillageResidentAction.Idle] = Clip("Idle");
            actions[(int)VillageResidentAction.Walk] = Clip("Walk");

            var actor = new GameObject("Ordinary Woman");
            try
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                model.name = "Model";
                model.transform.SetParent(actor.transform, false);
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                model.transform.localScale = Vector3.one;
                actor.transform.localScale = Vector3.one * data.height_scale;
                var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var byName = renderers.ToDictionary(renderer => renderer.name, StringComparer.Ordinal);
                var parts = data.parts.ToDictionary(part => part.name, StringComparer.Ordinal);
                Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Player3D/Materials/Player3DLit.mat");
                if (material == null) throw new InvalidOperationException("Missing shared Lit NPC material.");
                foreach (SkinnedMeshRenderer skin in renderers)
                {
                    skin.sharedMaterials = new[] { material };
                    skin.updateWhenOffscreen = true;
                }
                Color Tint(string name) => ReadColor(parts[name].color);
                var garments = data.items.Select(item => new NpcWardrobe.ItemBinding(item.id, item.slot,
                    item.renderers.Select(name => new NpcWardrobe.GarmentBinding(item.slot, byName[name], Tint(name))).ToArray(),
                    item.covered_renderers.Select(name => (Renderer)byName[name]).ToArray())).ToArray();
                var occupied = data.items.SelectMany(item => item.renderers).ToHashSet(StringComparer.Ordinal);
                Renderer[] anatomy = renderers.Where(renderer => !occupied.Contains(renderer.name)).ToArray();
                Texture2D clothing = LoadAtlas(data.clothing_atlas, 256);
                var wardrobe = actor.AddComponent<NpcWardrobe>();
                wardrobe.ConfigureModular(DefaultNpcCatalog.OrdinaryWoman, clothing, material, anatomy, garments,
                    data.outfits.Select(outfit => new NpcWardrobe.OutfitPreset(outfit.id, outfit.items)).ToArray(), "everyday");

                Animator animator = model.GetComponentInChildren<Animator>() ?? model.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Transform Joint(string name) => CityPedestrianHandProps.FindSocket(model.transform, name)
                    ?? throw new InvalidOperationException("Missing ordinary woman joint: " + name);
                actor.AddComponent<NpcHandPose>().Configure(new[] { "L", "R" }.Select(side =>
                    new NpcHandPose.HandBinding(side == "L", Joint("hand." + side), Joint("SOCKET_Grip." + side),
                        Joint("ANCHOR_HandGripCentre." + side), Joint("ANCHOR_HandGripAxis." + side),
                        Joint("ANCHOR_HandGripPalm." + side), renderers.Where(skin =>
                            skin.name.EndsWith("." + side, StringComparison.Ordinal) &&
                            (skin.name.StartsWith("GEO_Hand", StringComparison.Ordinal) ||
                             skin.name.StartsWith("CLO_Glove", StringComparison.Ordinal))).ToArray())).ToArray(),
                    data.grip_shape, data.grip_radius_m);
                actor.AddComponent<VillageResidentPresentation>().ConfigureAuthoredActor(DefaultNpcCatalog.OrdinaryWoman,
                    animator, model.transform, Joint("SOCKET_Grip.R"), Joint("SOCKET_Grip.L"), Joint("head"),
                    actions, anatomy, anatomy.Select(renderer => Tint(renderer.name)).ToArray(), clothing, null, null);
                actor.AddComponent<DefaultNpcAppearance>().Configure(data.faces.Select(face =>
                        new DefaultNpcAppearance.FaceBinding(face.id, LoadAtlas(face.texture, 64))).ToArray(),
                    data.faces[0].id, data.hair_colors.Select(hair => new DefaultNpcAppearance.HairColorBinding(hair.id,
                        ReadColor(hair.color))).ToArray(), Array.Empty<Renderer>(), byName["GEO_FaceSurface"]);
                actor.AddComponent<DefaultNpcBodyShape>().Configure(data.shaped_renderers.Select(name => byName[name]).ToArray(),
                    data.height_m, data.min_height_cm, data.max_height_cm);
                actor.AddComponent<DefaultNpcStreetClips>().Configure(Clip("Sit"), Clip("Guard"), Clip("Shove"));
                ValidateBody(actor, data);
                if (PrefabUtility.SaveAsPrefabAsset(actor, PrefabPath) == null)
                    throw new InvalidOperationException("Could not save ordinary woman prefab.");
                AssetDatabase.SaveAssets();
            }
            finally { UnityEngine.Object.DestroyImmediate(actor); }
            ValidateOrThrow();
        }

        public static void ValidateOrThrow()
        {
            Manifest data = ReadManifest();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null || prefab.GetComponent<CanneryWomanPresentation>() != null ||
                prefab.GetComponent<CanneryWomanHair>() != null)
                throw new InvalidOperationException("Missing the independent ordinary woman prefab.");
            ValidateBody(prefab, data);
            ReadClips(data);
            LoadAtlas(data.clothing_atlas, 256);
            foreach (Face face in data.faces) LoadAtlas(face.texture, 64);
        }

        private static void ValidateBody(GameObject actor, Manifest data)
        {
            var wardrobe = actor.GetComponent<NpcWardrobe>();
            var appearance = actor.GetComponent<DefaultNpcAppearance>();
            var stature = actor.GetComponent<DefaultNpcBodyShape>();
            var motion = actor.GetComponent<VillageResidentPresentation>();
            var street = actor.GetComponent<DefaultNpcStreetClips>();
            if (wardrobe == null || appearance == null || stature == null || motion == null || street == null ||
                actor.GetComponent<NpcHandPose>()?.Hands.Count != 2 ||
                motion.Animator.avatar == null || motion.Animator.applyRootMotion || street.Sit == null ||
                street.Guard == null || street.Shove == null ||
                !Mathf.Approximately(stature.ReferenceHeightMetres, data.height_m) ||
                stature.MinHeightCm != data.min_height_cm || stature.MaxHeightCm != data.max_height_cm ||
                !appearance.Faces.Select(face => face.Id).SequenceEqual(data.faces.Select(face => face.id)) ||
                !wardrobe.Items.Select(item => item.Id).SequenceEqual(data.items.Select(item => item.id)))
                throw new InvalidOperationException("Ordinary woman prefab differs from its Blender contract.");
            wardrobe.ValidateBindings();
            stature.ValidateBindings();
            var skins = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int triangles = skins.Sum(skin => (int)skin.sharedMesh.GetIndexCount(0) / 3);
            float crown = float.NegativeInfinity, sole = float.PositiveInfinity;
            foreach (SkinnedMeshRenderer skin in skins)
            {
                bool isCrown = skin.name == "GEO_Scalp";
                if (!isCrown && !skin.name.StartsWith("GEO_BareFoot.", StringComparison.Ordinal)) continue;
                foreach (Vector3 vertex in skin.sharedMesh.vertices)
                {
                    float y = skin.transform.TransformPoint(vertex).y;
                    if (isCrown) crown = Mathf.Max(crown, y);
                    else sole = Mathf.Min(sole, y);
                }
            }
            if (skins.Length != data.mesh_count || triangles != data.triangle_count ||
                !float.IsFinite(crown - sole) || Mathf.Abs(crown - sole - data.height_m) > .008f)
                throw new InvalidOperationException($"Ordinary woman import has {triangles} triangles and stature {crown - sole:F3} m.");
        }

        private static AnimationClip[] ReadClips(Manifest data)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(ActionPath).OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            if (clips.Length != data.clips.Length) throw new InvalidOperationException("Incomplete ordinary woman action bank.");
            foreach (ClipData expected in data.clips)
            {
                AnimationClip clip = clips.Single(candidate => candidate.name == expected.name);
                if (clip.isLooping != expected.loop || Mathf.Abs(clip.length - expected.seconds) > .043f ||
                    clip.events.Length != 0 || !AnimationUtility.GetCurveBindings(clip).Any(binding => binding.path.EndsWith("/head", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Invalid ordinary woman bone-only action: " + clip.name);
            }
            return clips;
        }

        private static Texture2D LoadAtlas(string name, int size)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + name);
            if (texture == null || texture.width != size || texture.height != size || texture.filterMode != FilterMode.Point)
                throw new InvalidOperationException("Missing imported ordinary woman atlas: " + name);
            return texture;
        }

        private static Color ReadColor(float[] rgba) => new Color(rgba[0], rgba[1], rgba[2], rgba[3]).gamma;

        private static Manifest ReadManifest()
        {
            Manifest value = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            var ride = CityPedestrianResources.OrdinaryWomanSeatedRide;
            if (value == null || value.catalog_model_id != DefaultNpcCatalog.OrdinaryWoman || value.bone_count != 31 ||
                value.parts?.Length != value.mesh_count || value.maximum_visible_triangles > 8000 || value.faces?.Length != 4 ||
                value.hair_colors?.Length != 3 || value.clips?.Length != 5 || value.items == null || value.outfits?.Length != 3 ||
                value.seat == null || !Mathf.Approximately(value.seat.lift, ride.SeatLift) ||
                !Mathf.Approximately(value.seat.headroom, ride.SeatedHeadroom))
                throw new InvalidOperationException("Regenerate the ordinary woman and match her measured seat contract.");
            return value;
        }

        [Serializable] private sealed class Manifest
        {
            public string catalog_model_id, clothing_atlas, grip_shape;
            public int bone_count, mesh_count, triangle_count, maximum_visible_triangles, min_height_cm, max_height_cm;
            public float height_m, height_scale, grip_radius_m;
            public Part[] parts;
            public string[] shaped_renderers;
            public Item[] items;
            public Face[] faces;
            public HairColor[] hair_colors;
            public Outfit[] outfits;
            public ClipData[] clips;
            public Seat seat;
        }

        [Serializable] private sealed class Part { public string name; public float[] color; }
        [Serializable] private sealed class Item { public string id, slot; public string[] renderers, covered_renderers; }
        [Serializable] private sealed class Face { public string id, texture; }
        [Serializable] private sealed class HairColor { public string id; public float[] color; }
        [Serializable] private sealed class Outfit { public string id; public string[] items; }
        [Serializable] private sealed class ClipData { public string name; public float seconds; public bool loop; }
        [Serializable] private sealed class Seat { public float lift, headroom; }
    }
}
