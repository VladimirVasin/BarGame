using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One selected face atlas and clothing selection on the shared ordinary body.</summary>
    [DisallowMultipleComponent]
    public sealed class DefaultNpcAppearance : MonoBehaviour
    {
        [Serializable] public sealed class FaceBinding
        {
            [SerializeField] private string id;
            [SerializeField] private Texture2D atlas;
            [SerializeField] private Texture2D brunetteAtlas;
            [SerializeField] private Texture2D blondAtlas;
            public string Id => id;
            public Texture2D Atlas => atlas;
            public FaceBinding(string id, Texture2D atlas) : this(id, atlas, atlas, atlas) { }
            public FaceBinding(string id, Texture2D atlas, Texture2D brunetteAtlas, Texture2D blondAtlas)
            { this.id = id; this.atlas = atlas; this.brunetteAtlas = brunetteAtlas; this.blondAtlas = blondAtlas; }
            public Texture2D GetAtlas(string hairColorId) => hairColorId switch
            {
                "gray" => atlas, "brunette" => brunetteAtlas, "blond" => blondAtlas,
                _ => throw new ArgumentException("Unknown authored hair color: " + hairColorId, nameof(hairColorId))
            };
        }

        [Serializable] public sealed class HairColorBinding
        {
            [SerializeField] private string id;
            [SerializeField] private Color color;
            public string Id => id;
            public Color Color => color;
            public HairColorBinding(string id, Color color) { this.id = id; this.color = color; }
        }

        [SerializeField] private FaceBinding[] faces = Array.Empty<FaceBinding>();
        [SerializeField] private Renderer[] permanentRenderers = Array.Empty<Renderer>();
        [SerializeField] private string currentFaceId;
        [SerializeField] private string appearanceKey;
        [SerializeField] private HairColorBinding[] hairColors = Array.Empty<HairColorBinding>();
        [SerializeField] private Renderer[] hairRenderers = Array.Empty<Renderer>();
        [SerializeField] private string currentHairColorId = "gray";
        [SerializeField] private NpcWardrobe wardrobe;
        private MaterialPropertyBlock properties;
        public IReadOnlyList<FaceBinding> Faces => faces;
        public string CurrentFaceId => currentFaceId;
        public string AppearanceKey => appearanceKey;
        public IReadOnlyList<HairColorBinding> HairColors => hairColors;
        public string CurrentHairColorId => currentHairColorId;
        public Texture2D CurrentAtlas => RequireFace(currentFaceId).GetAtlas(currentHairColorId);

        public void Configure(FaceBinding[] bindings, string defaultFaceId)
            => Configure(bindings, defaultFaceId, new[] { new HairColorBinding("gray", Color.white),
                new HairColorBinding("brunette", Color.white), new HairColorBinding("blond", Color.white) }, Array.Empty<Renderer>());

        public void Configure(FaceBinding[] bindings, string defaultFaceId, HairColorBinding[] colors, Renderer[] hair)
        {
            if (bindings == null || bindings.Length == 0)
                throw new ArgumentException("The default NPC needs authored faces.", nameof(bindings));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (FaceBinding binding in bindings)
                if (binding == null || string.IsNullOrWhiteSpace(binding.Id) || binding.Atlas == null || !ids.Add(binding.Id))
                    throw new ArgumentException("Faces require unique IDs and authored atlases.", nameof(bindings));
            if (!ids.Contains(defaultFaceId)) throw new ArgumentException("Unknown default face.", nameof(defaultFaceId));
            if (colors == null || colors.Length == 0 || hair == null)
                throw new ArgumentException("Authored hair colors and renderer bindings are required.");
            var hairIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (HairColorBinding color in colors)
            {
                if (color == null || string.IsNullOrWhiteSpace(color.Id) || !hairIds.Add(color.Id))
                    throw new ArgumentException("Hair colors require unique IDs.", nameof(colors));
                foreach (FaceBinding face in bindings)
                    if (face.GetAtlas(color.Id) == null) throw new ArgumentException("Every face needs an atlas for each hair color.");
            }
            if (!hairIds.Contains("gray")) throw new ArgumentException("The initial gray hair color is required.", nameof(colors));
            NpcWardrobe nextWardrobe = GetComponent<NpcWardrobe>();
            if (nextWardrobe == null || !nextWardrobe.IsModular)
                throw new InvalidOperationException("Default NPC appearance requires a modular wardrobe.");
            var permanent = new List<Renderer>();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                if (!nextWardrobe.Owns(renderer)) permanent.Add(renderer);
            if (permanent.Count == 0) throw new InvalidOperationException("Default NPC appearance requires its permanent body.");
            var uniqueHair = new HashSet<Renderer>();
            foreach (Renderer renderer in hair)
                if (renderer == null || !permanent.Contains(renderer) || !uniqueHair.Add(renderer))
                    throw new ArgumentException("Hair renderers must be unique permanent parts of this body.", nameof(hair));
            wardrobe = nextWardrobe; faces = (FaceBinding[])bindings.Clone();
            permanentRenderers = permanent.ToArray(); currentFaceId = defaultFaceId;
            hairColors = (HairColorBinding[])colors.Clone(); hairRenderers = (Renderer[])hair.Clone();
            currentHairColorId = "gray";
            appearanceKey = null;
            RestoreFace();
        }

        public void ApplyFace(string id)
        {
            RequireFace(id);
            currentFaceId = id;
            RestoreFace();
        }

        public void SetFace(string id) => ApplyFace(id);

        public void ApplyHairColor(string id)
        {
            RequireHairColor(id);
            if (RequireFace(currentFaceId).GetAtlas(id) == null) throw new ArgumentException("The selected face has no hair-color atlas.", nameof(id));
            currentHairColorId = id;
            RestoreFace();
        }

        /// <summary>Authoring preview mixing; population allocations use ApplySelection.</summary>
        public void Randomize(string stableIdentity)
        {
            var ids = new List<string>();
            foreach (FaceBinding face in faces) ids.Add(face.Id);
            ids.Sort(StringComparer.Ordinal);
            string faceId = ids[DefaultNpcAppearanceSelection.Index(stableIdentity, "face", ids.Count)];
            var hairIds = new List<string>();
            foreach (HairColorBinding hair in hairColors) hairIds.Add(hair.Id);
            hairIds.Sort(StringComparer.Ordinal);
            string hairId = hairIds[DefaultNpcAppearanceSelection.Index(stableIdentity, "hair", hairIds.Count)];
            wardrobe.Randomize(stableIdentity);
            appearanceKey = stableIdentity; currentHairColorId = hairId;
            ApplyFace(faceId);
        }

        public void ApplySelection(string stableIdentity, string faceId, IReadOnlyList<string> itemIds)
            => ApplySelection(stableIdentity, faceId, currentHairColorId, itemIds);

        public void ApplySelection(string stableIdentity, string faceId, string hairColorId, IReadOnlyList<string> itemIds)
        {
            if (string.IsNullOrWhiteSpace(stableIdentity)) throw new ArgumentException("A stable character ID is required.", nameof(stableIdentity));
            FaceBinding face = RequireFace(faceId);
            RequireHairColor(hairColorId);
            if (face.GetAtlas(hairColorId) == null) throw new ArgumentException("The assigned face has no hair-color atlas.");
            wardrobe.ApplyItems(itemIds);
            appearanceKey = stableIdentity; currentFaceId = faceId; currentHairColorId = hairColorId;
            RestoreFace();
        }

        private FaceBinding RequireFace(string id)
        {
            FaceBinding face = Array.Find(faces, candidate => candidate.Id == id);
            if (face == null || face.Atlas == null) throw new ArgumentException("Unknown default NPC face: " + id, nameof(id));
            return face;
        }

        private HairColorBinding RequireHairColor(string id)
        {
            HairColorBinding color = Array.Find(hairColors, candidate => candidate.Id == id);
            if (color == null) throw new ArgumentException("Unknown default NPC hair color: " + id, nameof(id));
            return color;
        }

        /// <summary>Called after the compatible animation sampler restores its permanent appearance.</summary>
        public void RestoreFace()
        {
            if (string.IsNullOrEmpty(currentFaceId)) return;
            Texture2D texture = CurrentAtlas;
            properties ??= new MaterialPropertyBlock();
            foreach (Renderer renderer in permanentRenderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(properties);
                properties.SetTexture("_BaseMap", texture); properties.SetTexture("_MainTex", texture);
                renderer.SetPropertyBlock(properties); properties.Clear();
            }
            Color hairColor = RequireHairColor(currentHairColorId).Color;
            foreach (Renderer renderer in hairRenderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(properties);
                properties.SetColor("_BaseColor", hairColor); properties.SetColor("_Color", hairColor);
                renderer.SetPropertyBlock(properties); properties.Clear();
            }
            wardrobe.SetAtlas(texture);
        }

        private void OnEnable() => RestoreFace();
    }
}
