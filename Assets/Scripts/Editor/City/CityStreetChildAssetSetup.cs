using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Editor
{
    public static class CityStreetChildAssetSetup
    {
        private const string Folder = CityFairChildAssetSetup.ModelFolder;

        [MenuItem("Bar Promenade/City Fair/Build Street Child")]
        public static void BuildOrThrow() => Build("StreetChildActor", "FairChild", "street-child", "ChildFace",
            CityFairChildAssetProvider.OutfitNames, blink: false);

        [MenuItem("Bar Promenade/City Fair/Build Street Girls")]
        public static void BuildGirlsOrThrow() => Build("StreetGirlActor", "StreetGirls", "street-girl", "StreetGirlFace",
            new[] { "BobJacket", "BraidedCoat", "BibOveralls" }, blink: true);

        private static void Build(string prefabName, string modelName, string lookId, string faceAtlasName,
            string[] outfits, bool blink)
        {
            var root = new GameObject(prefabName);
            try
            {
                var model = UnityEngine.Object.Instantiate(Load<GameObject>(modelName + ".fbx"), root.transform);
                model.name = "FairChild";
                var animator = model.GetComponentInChildren<Animator>();
                var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
                var detail = Resources.Load<Material>(CityFairChildAssetProvider.DetailMaterialPath);
                var face = renderers.Single(renderer => renderer.name == CityFairChildAssetProvider.FaceRendererName);
                const string materialPath = Folder + "StreetChildFace.mat";
                var faceMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (faceMaterial == null)
                {
                    faceMaterial = new Material(Resources.Load<Shader>("Shaders/Lit"));
                    faceMaterial.SetFloat("_Smoothness", .16f);
                    AssetDatabase.CreateAsset(faceMaterial, materialPath);
                }

                var skin = renderers.Where(renderer => renderer.name == "GEO_Ears" ||
                    renderer.name == "GEO_Neck" || renderer.name.StartsWith("GEO_Hand.", StringComparison.Ordinal))
                    .Cast<Renderer>().ToArray();
                foreach (var renderer in renderers)
                {
                    renderer.sharedMaterial = renderer == face ? faceMaterial : detail;
                    renderer.enabled = renderer == face || skin.Contains(renderer);
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    // The short cycle extends further than the bind pose.
                    Bounds bounds = renderer.localBounds;
                    bounds.Expand(.25f / renderer.transform.lossyScale.x);
                    renderer.localBounds = bounds;
                }
                animator.applyRootMotion = false;

                var items = outfits.Select(Item).ToArray();
                var presets = items.Select(item => new NpcWardrobe.OutfitPreset(item.Id, new[] { item.Id })).ToArray();
                root.AddComponent<NpcWardrobe>().ConfigureModular(lookId, Load<Texture2D>("ChildSkin.png"),
                    detail, skin.Append(face).ToArray(), items, presets, presets[0].Id);
                Texture2D faces = Load<Texture2D>(faceAtlasName + ".png");
                var faceBindings = items.Select((_, index) =>
                    new DefaultNpcAppearance.FaceBinding(lookId + "-" + (index + 1), faces, index, blink)).ToArray();
                root.AddComponent<DefaultNpcAppearance>().Configure(faceBindings,
                    faceBindings[0].Id, new[] { new DefaultNpcAppearance.HairColorBinding("gray", Color.white) },
                    Array.Empty<Renderer>(), face, blink ? face : null);
                root.AddComponent<DefaultNpcBodyShape>().ConfigureFixedStature(130);

                AnimationClip[] bank = CityFairChildActions.LoadClips();
                var clips = new[] { bank[(int)CityFairChildAction.Idle], bank[(int)CityFairChildAction.StreetWalk] };
                Transform head = model.GetComponentsInChildren<Transform>(true).Single(bone => bone.name == "head");
                root.AddComponent<VillageResidentPresentation>().ConfigureAuthoredActor(lookId, animator,
                    model.transform, null, null, head, clips, skin,
                    Enumerable.Repeat(Color.white, skin.Length).ToArray(), Load<Texture2D>("ChildSkin.png"), null, null);
                if (PrefabUtility.SaveAsPrefabAsset(root, Folder + prefabName + ".prefab") == null)
                    throw new InvalidOperationException("Could not save " + prefabName + ".");

                NpcWardrobe.ItemBinding Item(string outfit)
                {
                    string prefix = "OUTFIT_" + outfit + "_";
                    var parts = Array.FindAll(renderers, renderer => renderer.name.StartsWith(prefix, StringComparison.Ordinal));
                    if (parts.Length == 0)
                        throw new InvalidOperationException("Missing street child outfit " + outfit);
                    Texture2D atlas = Load<Texture2D>(outfit + "Atlas.png");
                    return new NpcWardrobe.ItemBinding("outfit." + outfit.ToLowerInvariant(), "outfit", parts.Select(renderer =>
                        new NpcWardrobe.GarmentBinding("outfit", renderer, Color.white, atlas)).ToArray(), Array.Empty<Renderer>());
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static T Load<T>(string name) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(Folder + name) ??
            throw new InvalidOperationException("Missing street child asset " + name);
    }
}
