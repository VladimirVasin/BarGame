using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Whole-world identities and appearances, allocated before any scene creates its cast.</summary>
    public static class DefaultNpcPopulation
    {
        public const string CanneryPreparation = "cannery.preparation";
        public const string CanneryRetort = "cannery.retort";
        public const string DeliveryDriver = "cannery.delivery-driver";
        public const string VillageStationWorker = "village.station-worker";
        // Sort after the narrative population so this test participant cannot reshuffle it.
        public const string CombatTestOpponent = "zz.combat-test.opponent";
        private static readonly string[] FairIds = { "fair.vendor.0", "fair.vendor.1", "fair.vendor.2", "fair.vendor.3" };
        public const int FairVisitorCount = 3;
        private static readonly string[] PortIds = { "port.captain", "port.deckhand", "port.crane.west", "port.crane.east", "port.docker" };
        /// <summary>
        /// The street pool since 2026-09-16: one permanent identity per pooled
        /// City walker, so a repeat encounter meets the same person. They declare
        /// no model - <see cref="AnyCatalogModel"/> - and take the least-used
        /// catalog model, which is how a model added to <see cref="DefaultNpcCatalog"/>
        /// joins the pavement without a registration here.
        /// </summary>
        public const string PedestrianIdPrefix = "city.pedestrian.";
        public static int PedestrianCount => CityPedestrianPopulationProfile.City.PoolSize;
        /// <summary>A character whose model the population picks from the whole catalog.</summary>
        public const string AnyCatalogModel = null;

        public sealed class SlotConstraint
        {
            public string Slot { get; }
            public IReadOnlyList<string> AllowedItemIds { get; }
            public SlotConstraint(string slot, params string[] allowedItemIds)
            {
                Slot = slot;
                AllowedItemIds = Array.AsReadOnly((string[])allowedItemIds.Clone());
            }
        }

        public sealed class CharacterDefinition
        {
            public string Id { get; }
            /// <summary>A catalog model, or <see cref="AnyCatalogModel"/> to let the population choose one.</summary>
            public string ModelId { get; }
            public bool UsesAnyCatalogModel => ModelId == AnyCatalogModel;
            public IReadOnlyList<SlotConstraint> Slots { get; }
            public CharacterDefinition(string id, string modelId, params SlotConstraint[] slots)
            {
                Id = id; ModelId = modelId;
                Slots = Array.AsReadOnly((SlotConstraint[])slots.Clone());
            }
        }

        public sealed class Assignment
        {
            public string CharacterId { get; }
            public string ModelId { get; }
            public string FaceId { get; }
            public string HairColorId { get; }
            public IReadOnlyList<string> ItemIds { get; }
            public string VisibleSignature { get; }
            internal Assignment(CharacterDefinition character, string modelId, string face, string hairColor, string[] items, string signature)
            {
                CharacterId = character.Id; ModelId = modelId; FaceId = face; HairColorId = hairColor;
                ItemIds = Array.AsReadOnly((string[])items.Clone()); VisibleSignature = signature;
            }
        }

        private sealed class ModelDefinition
        {
            public readonly SortedDictionary<string, string[]> Slots = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
            public readonly Dictionary<string, NpcWardrobe.ItemBinding> Items = new Dictionary<string, NpcWardrobe.ItemBinding>(StringComparer.Ordinal);
            public string[] Faces;
            public string[] HairColors;
        }

        public static IReadOnlyList<CharacterDefinition> Characters { get; } = Array.AsReadOnly(BuildCharacters());
        private static string cachedCatalog;
        private static IReadOnlyList<Assignment> assignments;

        public static string FairVendorId(int index) => FairIds[index];
        public static string FairVisitorId(int index)
        {
            if (index < 0 || index >= FairVisitorCount) throw new ArgumentOutOfRangeException(nameof(index));
            return "fair.visitor." + index;
        }
        public static string PortWorkerId(int index) => PortIds[index];

        public static string PedestrianId(int index)
        {
            if (index < 0 || index >= PedestrianCount)
                throw new ArgumentOutOfRangeException(nameof(index), "The street pool holds " + PedestrianCount + " permanent walkers.");
            return PedestrianIdPrefix + index.ToString("00");
        }

        private static CharacterDefinition[] BuildCharacters()
        {
            var roster = new List<CharacterDefinition>();
            const string model = DefaultNpcCatalog.OrdinaryWorker;
            foreach (string id in FairIds)
                roster.Add(new CharacterDefinition(id, model,
                    new SlotConstraint("apron", new string[] { null }),
                    new SlotConstraint("gloves", null, "gloves.work"),
                    new SlotConstraint("scarf", null, "scarf.warm")));
            for (int i = 0; i < FairVisitorCount; i++)
                roster.Add(new CharacterDefinition(FairVisitorId(i), AnyCatalogModel,
                    new SlotConstraint("outerwear", "outerwear.everyday", "outerwear.warm"),
                    new SlotConstraint("boots", "boots.everyday", "boots.warm"),
                    new SlotConstraint("headwear", null, "headwear.everyday", "headwear.warm"),
                    new SlotConstraint("apron", new string[] { null }),
                    new SlotConstraint("gloves", null, "gloves.work"),
                    new SlotConstraint("scarf", null, "scarf.warm")));
            for (int i = 0; i < PortIds.Length; i++)
                roster.Add(new CharacterDefinition(PortIds[i], model,
                    new SlotConstraint("outerwear", "outerwear.work"),
                    new SlotConstraint("boots", "boots.work", "boots.warm"),
                    new SlotConstraint("headwear", i < 2 ? new[] { "headwear.warm" } :
                        new[] { "headwear.everyday", "headwear.work", "headwear.warm" }),
                    new SlotConstraint("gloves", "gloves.work"),
                    new SlotConstraint("scarf", i < 2 ? new[] { "scarf.warm" } : new[] { null, "scarf.warm" }),
                    new SlotConstraint("apron", new string[] { null })));
            foreach (string id in new[] { CanneryPreparation, CanneryRetort })
                roster.Add(new CharacterDefinition(id, model,
                    new SlotConstraint("outerwear", "outerwear.work"), new SlotConstraint("boots", "boots.work"),
                    new SlotConstraint("headwear", "headwear.work"), new SlotConstraint("gloves", "gloves.work"),
                    new SlotConstraint("scarf", new string[] { null }), new SlotConstraint("apron", "apron.work")));
            roster.Add(new CharacterDefinition(DeliveryDriver, model,
                new SlotConstraint("outerwear", "outerwear.everyday", "outerwear.work"),
                new SlotConstraint("boots", "boots.everyday", "boots.work"),
                new SlotConstraint("scarf", new string[] { null }), new SlotConstraint("gloves", new string[] { null }),
                new SlotConstraint("apron", new string[] { null })));
            roster.Add(new CharacterDefinition(VillageStationWorker, model,
                new SlotConstraint("outerwear", "outerwear.warm"), new SlotConstraint("boots", "boots.warm"),
                new SlotConstraint("headwear", "headwear.warm"), new SlotConstraint("scarf", "scarf.warm"),
                new SlotConstraint("gloves", "gloves.work"), new SlotConstraint("apron", new string[] { null })));
            // Passers-by: any catalog model, street clothes only. Every worn
            // slot stays free so the allocator spreads coats, boots and hats;
            // an apron is work wear and a bare head, hands or neck are allowed.
            for (int i = 0; i < PedestrianCount; i++)
                roster.Add(new CharacterDefinition(PedestrianId(i), AnyCatalogModel,
                    new SlotConstraint("apron", new string[] { null }),
                    new SlotConstraint("gloves", null, "gloves.work"),
                    new SlotConstraint("scarf", null, "scarf.warm"),
                    new SlotConstraint("headwear", null, "headwear.everyday", "headwear.work", "headwear.warm")));
            roster.Add(new CharacterDefinition(CombatTestOpponent, model,
                new SlotConstraint("outerwear", "outerwear.work"),
                new SlotConstraint("boots", "boots.work"),
                new SlotConstraint("headwear", new string[] { null }),
                new SlotConstraint("gloves", "gloves.work"),
                new SlotConstraint("scarf", new string[] { null }),
                new SlotConstraint("apron", new string[] { null })));
            roster.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return roster.ToArray();
        }

        public static CharacterDefinition GetCharacter(string characterId)
        {
            foreach (CharacterDefinition character in Characters)
                if (character.Id == characterId) return character;
            throw new ArgumentException("Unknown default NPC character: " + characterId + ". Register its stable identity and appearance constraints.", nameof(characterId));
        }

        /// <summary>Refreshes on catalog/coverage changes; scene load order never participates.</summary>
        public static void Prepare()
        {
            var models = new SortedDictionary<string, ModelDefinition>(StringComparer.Ordinal);
            var signature = new StringBuilder();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterDefinition character in Characters)
            {
                if (string.IsNullOrWhiteSpace(character.Id) || !identities.Add(character.Id))
                    throw new InvalidOperationException("Default NPC character IDs must be unique.");
                // A free choice ranges over the whole catalog, so every model is
                // read and every model's authored bindings enter the signature.
                foreach (string modelId in character.UsesAnyCatalogModel ? DefaultNpcCatalog.ModelIds : new[] { character.ModelId })
                    if (!models.ContainsKey(modelId)) models.Add(modelId, ReadModel(modelId, signature));
                signature.Append(character.Id).Append(':').Append(character.ModelId ?? "<any>").Append(';');
                foreach (SlotConstraint slot in character.Slots)
                {
                    signature.Append(slot.Slot).Append('=');
                    foreach (string id in slot.AllowedItemIds) signature.Append(id ?? "<none>").Append(',');
                }
            }
            string catalog = signature.ToString();
            if (catalog == cachedCatalog && assignments != null) return;
            var next = new List<Assignment>();
            var usage = new Dictionary<string, int>(StringComparer.Ordinal);
            var modelUsage = new Dictionary<string, int>(StringComparer.Ordinal);
            string[] catalogModels = new List<string>(DefaultNpcCatalog.ModelIds).ToArray();
            foreach (CharacterDefinition character in Characters)
            {
                // The model is the first choice and follows the same rule as the
                // rest: an unused catalog model first, then the least used one, so
                // a second model splits the pavement instead of waiting its turn.
                string modelId = character.UsesAnyCatalogModel
                    ? DefaultNpcAppearanceSelection.Choose(character.Id, new[] { catalogModels }, choice => choice[0], modelUsage)[0]
                    : character.ModelId;
                ModelDefinition model = models[modelId];
                var dimensions = new List<string[]> { model.Faces, model.HairColors };
                var constraints = new Dictionary<string, SlotConstraint>(StringComparer.Ordinal);
                foreach (SlotConstraint slot in character.Slots)
                {
                    if (!model.Slots.ContainsKey(slot.Slot) || constraints.ContainsKey(slot.Slot))
                        throw new InvalidOperationException("Character constraints must name unique authored slots: " + character.Id);
                    constraints.Add(slot.Slot, slot);
                }
                foreach (var slot in model.Slots)
                {
                    if (!constraints.TryGetValue(slot.Key, out SlotConstraint constraint)) { dimensions.Add(slot.Value); continue; }
                    var allowed = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (string id in constraint.AllowedItemIds)
                    {
                        if (id != null && Array.IndexOf(slot.Value, id) < 0)
                            throw new InvalidOperationException("Character allows an unauthored item: " + character.Id + "/" + id);
                        allowed.Add(id ?? string.Empty);
                    }
                    dimensions.Add(new List<string>(allowed).ToArray());
                }
                string Visible(string[] candidate) => VisibleSignature(modelId, model, candidate);
                string[] chosen = DefaultNpcAppearanceSelection.Choose(character.Id, dimensions, Visible, usage);
                var equipped = new List<string>();
                for (int i = 2; i < chosen.Length; i++) if (!string.IsNullOrEmpty(chosen[i])) equipped.Add(chosen[i]);
                next.Add(new Assignment(character, modelId, chosen[0], chosen[1], equipped.ToArray(), Visible(chosen)));
            }
            assignments = next.AsReadOnly(); cachedCatalog = catalog;
        }

        private static ModelDefinition ReadModel(string modelId, StringBuilder signature)
        {
            GameObject prefab = DefaultNpcCatalog.GetPrefab(modelId);
            var wardrobe = prefab.GetComponent<NpcWardrobe>();
            var appearance = prefab.GetComponent<DefaultNpcAppearance>();
            if (wardrobe == null || !wardrobe.IsModular || appearance == null || appearance.Faces.Count == 0)
                throw new InvalidOperationException("A population model requires modular clothing and face bindings: " + modelId);
            wardrobe.ValidateBindings();
            var model = new ModelDefinition();
            var faces = new SortedSet<string>(StringComparer.Ordinal);
            foreach (DefaultNpcAppearance.FaceBinding face in appearance.Faces)
            {
                if (face == null || string.IsNullOrWhiteSpace(face.Id) || face.Atlas == null || !faces.Add(face.Id))
                    throw new InvalidOperationException("Population faces require unique IDs and atlases: " + modelId);
            }
            model.Faces = new List<string>(faces).ToArray();
            var hairColors = new SortedSet<string>(StringComparer.Ordinal);
            foreach (DefaultNpcAppearance.HairColorBinding hair in appearance.HairColors)
            {
                if (hair == null || string.IsNullOrWhiteSpace(hair.Id) || !hairColors.Add(hair.Id))
                    throw new InvalidOperationException("Population hair colors require unique IDs: " + modelId);
                foreach (DefaultNpcAppearance.FaceBinding face in appearance.Faces)
                    if (face.GetAtlas(hair.Id) == null) throw new InvalidOperationException("Population face/hair atlas is missing: " + modelId);
            }
            if (hairColors.Count == 0) throw new InvalidOperationException("Population model has no hair colors: " + modelId);
            model.HairColors = new List<string>(hairColors).ToArray();
            signature.Append(modelId).Append(':').Append(string.Join(",", model.Faces)).Append('/')
                .Append(string.Join(",", model.HairColors)).Append(';');
            var slots = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (NpcWardrobe.ItemBinding item in wardrobe.Items)
            {
                model.Items.Add(item.Id, item);
                if (!slots.TryGetValue(item.Slot, out List<string> choices)) slots.Add(item.Slot, choices = new List<string>());
                choices.Add(item.Id);
            }
            foreach (var slot in slots)
            {
                slot.Value.Sort(StringComparer.Ordinal); model.Slots.Add(slot.Key, slot.Value.ToArray());
                foreach (string id in slot.Value)
                {
                    NpcWardrobe.ItemBinding item = model.Items[id];
                    signature.Append(slot.Key).Append(':').Append(id).Append('=');
                    foreach (NpcWardrobe.GarmentBinding part in item.Parts) signature.Append(RendererPath(part.Renderer, prefab.transform)).Append(',');
                    signature.Append('>');
                    foreach (Renderer covered in item.CoveredRenderers) signature.Append(RendererPath(covered, prefab.transform)).Append(',');
                    signature.Append(';');
                }
            }
            return model;
        }

        private static string RendererPath(Renderer renderer, Transform root)
        {
            string path = renderer.name;
            for (Transform parent = renderer.transform.parent; parent != null && parent != root; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        private static string VisibleSignature(string modelId, ModelDefinition model, string[] candidate)
        {
            var covered = new HashSet<Renderer>();
            for (int i = 2; i < candidate.Length; i++)
                if (!string.IsNullOrEmpty(candidate[i]))
                    foreach (Renderer renderer in model.Items[candidate[i]].CoveredRenderers) covered.Add(renderer);
            var signature = new StringBuilder(modelId).Append('|').Append(candidate[0]).Append('|').Append(candidate[1]);
            for (int i = 2; i < candidate.Length; i++)
            {
                if (string.IsNullOrEmpty(candidate[i])) continue;
                foreach (NpcWardrobe.GarmentBinding part in model.Items[candidate[i]].Parts)
                    if (!covered.Contains(part.Renderer)) { signature.Append('|').Append(candidate[i]); break; }
            }
            return signature.ToString();
        }

        public static IReadOnlyList<Assignment> GetAssignments() { Prepare(); return assignments; }

        public static Assignment GetAssignment(string characterId)
        {
            GetCharacter(characterId);
            Prepare();
            foreach (Assignment assignment in assignments) if (assignment.CharacterId == characterId) return assignment;
            throw new InvalidOperationException("A registered default NPC has no assignment: " + characterId);
        }

        public static void Apply(VillageResidentPresentation actor, string characterId)
        {
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            Assignment assignment = GetAssignment(characterId);
            var appearance = actor.GetComponent<DefaultNpcAppearance>();
            if (appearance == null) throw new InvalidOperationException("The default NPC has no appearance owner.");
            appearance.ApplySelection(characterId, assignment.FaceId, assignment.HairColorId, assignment.ItemIds);
        }
    }
}
