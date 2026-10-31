using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bottle-dominated bodies on the retained rig; civilian garments never cover their impossible anatomy.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatZombieOrdinaryAppearance : MonoBehaviour
    {
        private Renderer[] anatomy;
        private Renderer[] excluded;
        private Color[] colours;
        private MaterialPropertyBlock properties;
        public int Variant { get; private set; } = -1;

        public void Configure(int variant)
        {
            if ((uint)variant >= 3) throw new ArgumentOutOfRangeException(nameof(variant));
            if (Variant >= 0)
            {
                if (Variant != variant) throw new InvalidOperationException("An ordinary enemy retains one permanent authored body.");
                Restore(); return;
            }
            var wardrobe = GetComponent<NpcWardrobe>();
            var face = GetComponent<DefaultNpcAppearance>();
            if (wardrobe == null || !wardrobe.IsModular || face == null || GetComponent<CombatZombieAppearance>() != null)
                throw new InvalidOperationException("The ordinary enemy requires its retained worker rig and its own appearance owner.");
            GetComponent<DefaultNpcBodyShape>()?.PreserveAuthoredAnatomy();
            CharacterJointDeformation joints = GetComponent<CharacterJointDeformation>();
            if (joints != null) joints.enabled = false;
            Variant = variant;
            var permanent = new List<Renderer>();
            var hidden = new List<Renderer>();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.name.StartsWith("GEO_", StringComparison.Ordinal))
                { renderer.enabled = false; hidden.Add(renderer); continue; }
                if (renderer is SkinnedMeshRenderer target)
                {
                    SkinnedMeshRenderer source = CombatZombieOrdinaryAssets.LoadBodyRenderer(variant, target.name);
                    CombatAuthoredBodyAppearance.ValidateBodyBinding(target, source);
                    target.sharedMesh = source.sharedMesh;
                    // The rig crouches/rises without changing its authored root.
                    Bounds bounds = target.localBounds;
                    bounds.Encapsulate(source.localBounds);
                    Vector3 unit = source.transform.lossyScale;
                    bounds.Expand(.65f / Mathf.Max(.0001f, Mathf.Min(Mathf.Abs(unit.x), Mathf.Min(Mathf.Abs(unit.y), Mathf.Abs(unit.z)))));
                    target.localBounds = bounds;
                }
                permanent.Add(renderer);
            }
            anatomy = permanent.ToArray(); excluded = hidden.ToArray(); colours = new Color[anatomy.Length];
            for (int i = 0; i < anatomy.Length; i++)
                colours[i] = anatomy[i].name.StartsWith("GEO_Legs", StringComparison.Ordinal) ? new Color(.29f,.28f,.24f) :
                    anatomy[i].name.StartsWith("GEO_Foot", StringComparison.Ordinal) ? new Color(.24f,.21f,.18f) : Color.white;
            var items = new NpcWardrobe.ItemBinding[wardrobe.Items.Count];
            for (int i = 0; i < items.Length; i++)
            {
                NpcWardrobe.ItemBinding item = wardrobe.Items[i];
                var parts = new NpcWardrobe.GarmentBinding[item.Parts.Count];
                for (int j = 0; j < parts.Length; j++)
                {
                    parts[j] = new NpcWardrobe.GarmentBinding(item.Slot, item.Parts[j].Renderer, item.Parts[j].Color);
                }
                var covered = new List<Renderer>();
                foreach (Renderer renderer in item.CoveredRenderers)
                    if (permanent.Contains(renderer) || wardrobe.Owns(renderer)) covered.Add(renderer);
                items[i] = new NpcWardrobe.ItemBinding(item.Id, item.Slot, parts, covered.ToArray());
            }
            Texture2D atlas = CombatZombieOrdinaryAssets.LoadAtlas(variant);
            Material shared = wardrobe.Garments[0].Renderer.sharedMaterial;
            wardrobe.ConfigureModular(DefaultNpcCatalog.OrdinaryWorker, atlas, shared, anatomy, items,
                new[] { new NpcWardrobe.OutfitPreset("decay", Array.Empty<string>()) }, "decay");
            face.Configure(new[] { new DefaultNpcAppearance.FaceBinding("decay", atlas) }, "decay",
                new[] { new DefaultNpcAppearance.HairColorBinding("gray", Color.white) }, Array.Empty<Renderer>());
            GetComponent<NpcHandPose>()?.ResetGrip();
            Restore();
        }

        public void Restore()
        {
            if (anatomy == null || Variant < 0) return;
            properties ??= new MaterialPropertyBlock();
            Texture2D atlas = CombatZombieOrdinaryAssets.LoadAtlas(Variant);
            foreach (Renderer renderer in excluded) if (renderer != null) renderer.enabled = false;
            for (int i = 0; i < anatomy.Length; i++)
            {
                Renderer renderer = anatomy[i];
                if (renderer == null) continue;
                if (renderer.name == "GEO_Torso") renderer.sharedMaterials = CombatZombieOrdinaryAssets.LoadTorsoMaterials(Variant);
                renderer.GetPropertyBlock(properties);
                properties.SetColor("_BaseColor", colours[i]); properties.SetColor("_Color", colours[i]);
                properties.SetTexture("_BaseMap", atlas); properties.SetTexture("_MainTex", atlas);
                renderer.SetPropertyBlock(properties); properties.Clear();
            }
        }
        private void OnEnable() => Restore();
    }

    /// <summary>Shared immutable mesh/clip references, loaded at preparation rather than at the first scare.</summary>
    internal static class CombatZombieOrdinaryAssets
    {
        private static readonly Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>(16);
        private static readonly Dictionary<string, SkinnedMeshRenderer>[] Bodies = new Dictionary<string, SkinnedMeshRenderer>[3];
        private static readonly Texture2D[] Atlases = new Texture2D[3];
        private static readonly GameObject[,] Passive = new GameObject[3, 2];
        private static readonly Material[][] PassiveMaterials = new Material[3][];
        private static readonly Material[][] TorsoMaterials = new Material[3][];
        public static void Prepare()
        {
            LoadClip(CombatZombieAssetProvider.DegradedIdleClip);
            for (int variant = 0; variant < 3; variant++)
            {
                LoadBodyRenderer(variant, "GEO_Head"); LoadAtlas(variant); LoadTorsoMaterials(variant);
                for (int pose = 0; pose < 2; pose++)
                    if (Passive[variant, pose] == null)
                        Passive[variant, pose] = Resources.Load<GameObject>("Combat/ZombieDecay" + (pose == 1 ? "Standing" : "Dormant") + variant)
                            ?? throw new InvalidOperationException("Missing exact passive enemy pose.");
            }
        }
        public static AnimationClip LoadClip(string name)
        {
            if (Clips.Count == 0)
                foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>("Combat/ZombieDecayActions"))
                {
                    if (Array.IndexOf(CombatZombieAssetProvider.DegradedClipNames, clip.name) < 0) continue;
                    if (clip.events.Length != 0 || clip.isLooping != CombatZombieAssetProvider.IsDegradedLoop(clip.name) ||
                        Mathf.Abs(clip.length - CombatZombieAssetProvider.DegradedClipDuration(clip.name)) > .003f)
                        throw new InvalidOperationException("Invalid grounded decay clip: " + clip.name);
                    Clips.Add(clip.name, clip);
                }
            if (Clips.Count != CombatZombieAssetProvider.DegradedClipNames.Length || !Clips.TryGetValue(name, out AnimationClip result))
                throw new InvalidOperationException("Missing ordinary enemy action: " + name);
            return result;
        }
        public static SkinnedMeshRenderer LoadBodyRenderer(int variant, string name)
        {
            if (Bodies[variant] == null)
            {
                GameObject body = Resources.Load<GameObject>("Combat/ZombieDecayBody" + variant)
                    ?? throw new InvalidOperationException("Missing authored decay body: " + variant);
                var renderers = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
                foreach (SkinnedMeshRenderer renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    renderers.Add(renderer.name, renderer);
                Bodies[variant] = renderers;
            }
            return Bodies[variant].TryGetValue(name, out SkinnedMeshRenderer result) ? result :
                throw new InvalidOperationException("Authored decay body lost its original surface: " + name);
        }
        public static Texture2D LoadAtlas(int variant) => Atlases[variant] != null ? Atlases[variant] :
            Atlases[variant] = Resources.Load<Texture2D>("Combat/ZombieDecayBody" + variant + "Atlas")
                ?? throw new InvalidOperationException("Missing authored decay surface.");
        public static GameObject CreatePassive(Transform parent, int variant, bool standing)
        {
            int pose = standing ? 1 : 0;
            if (Passive[variant, pose] == null)
                Passive[variant, pose] = Resources.Load<GameObject>("Combat/ZombieDecay" + (standing ? "Standing" : "Dormant") + variant)
                    ?? throw new InvalidOperationException("Missing exact passive enemy pose.");
            LoadTorsoMaterials(variant);
            var root = new GameObject("Ordinary enemy " + variant + (standing ? " standing" : " dormant"));
            root.transform.SetParent(parent, false);
            // Match the ordinary prefab wrapper exactly; retain the imported FBX's own unit scale.
            root.transform.localScale = DefaultNpcCatalog.GetPrefab().transform.localScale;
            GameObject mesh = UnityEngine.Object.Instantiate(Passive[variant, pose], root.transform, false);
            mesh.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * mesh.transform.localRotation;
            foreach (Renderer renderer in mesh.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.sharedMaterials.Length != PassiveMaterials[variant].Length)
                    throw new InvalidOperationException("Passive enemy material order differs from its authored surface roles.");
                renderer.sharedMaterials = PassiveMaterials[variant];
            }
            return root;
        }
        public static Material[] LoadTorsoMaterials(int variant)
        {
            if (PassiveMaterials[variant] == null)
            {
                Material template = DefaultNpcCatalog.GetPrefab().GetComponentInChildren<Renderer>(true).sharedMaterial;
                Color skin = Color.white;
                Color boots = new Color(.24f,.21f,.18f);
                Color[] colours = { skin, new Color(.29f,.28f,.24f), boots, Color.white };
                var materials = new Material[colours.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    colours[i].a = 1f;
                    materials[i] = new Material(template) { name = "Decay passive " + variant + " surface " + i,
                        hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
                    materials[i].SetColor("_BaseColor", colours[i]); materials[i].SetColor("_Color", colours[i]);
                    materials[i].SetTexture("_BaseMap", LoadAtlas(variant)); materials[i].SetTexture("_MainTex", LoadAtlas(variant));
                    if (i == 3)
                    {
                        materials[i].SetFloat("_Smoothness", .68f);
                        materials[i].SetFloat("_Metallic", 0f);
                        materials[i].SetFloat("_SpecularHighlights", 1f);
                        materials[i].DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    }
                }
                PassiveMaterials[variant] = materials;
                TorsoMaterials[variant] = new[] { materials[0], materials[3] };
            }
            return TorsoMaterials[variant];
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Clips.Clear();
            Array.Clear(Bodies, 0, Bodies.Length); Array.Clear(Atlases, 0, Atlases.Length);
            Array.Clear(Passive, 0, Passive.Length);
            for (int i = 0; i < PassiveMaterials.Length; i++)
            {
                if (PassiveMaterials[i] != null)
                    foreach (Material material in PassiveMaterials[i])
                        if (material != null) UnityEngine.Object.Destroy(material);
                PassiveMaterials[i] = null;
                TorsoMaterials[i] = null;
            }
        }
    }
}
