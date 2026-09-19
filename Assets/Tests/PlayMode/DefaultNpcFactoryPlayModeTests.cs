using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class DefaultNpcFactoryPlayModeTests
    {
        // Authored colours pass through Unity's native material property block.
        private static readonly ColorEqualityComparer AppearanceColorComparer = new ColorEqualityComparer(1e-5f);
        private GameObject host;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (host != null)
            {
                foreach (var actor in host.GetComponentsInChildren<VillageResidentPresentation>(true))
                    NpcFootstepSources.Unregister(actor.transform);
                host.SetActive(false);
                Object.Destroy(host);
            }
            yield return null;
            NpcFootstepSources.Prune();
        }

        [UnityTest]
        [PrebuildSetup(typeof(DefaultNpcWardrobeAssetsSetup))]
        public IEnumerator OrdinaryWorker_ContactArmsKeepNaturalElbowsAcrossReachDirections()
        {
            host = new GameObject("Default NPC Arm Contact Regression");
            host.transform.SetPositionAndRotation(new Vector3(3f, 2f, -4f), Quaternion.Euler(0f, 37f, 0f));
            VillageResidentPresentation actor = DefaultNpcFactory.Create(host.transform);
            Transform[] all = actor.ModelRoot.GetComponentsInChildren<Transform>(true);
            var upper = new[] { all.Single(t => t.name == "upper_arm.R"), all.Single(t => t.name == "upper_arm.L") };
            var elbow = new[] { all.Single(t => t.name == "forearm.R"), all.Single(t => t.name == "forearm.L") };
            var wrist = new[] { all.Single(t => t.name == "hand.R"), all.Single(t => t.name == "hand.L") };
            Quaternion modelRotation = actor.ModelRoot.localRotation;
            // Components are anatomical outward, up, forward, in metres.
            var offsets = new[] { new Vector3(.02f, -.04f, .30f), new Vector3(.01f, -.13f, .18f),
                new Vector3(.06f, .28f, .24f), new Vector3(.38f, -.04f, .12f), new Vector3(-.16f, -.12f, .18f) };
            foreach (float yaw in new[] { 0f, 180f })
            {
                actor.ModelRoot.localRotation = modelRotation * Quaternion.Euler(0f, yaw, 0f);
                for (int pose = 0; pose < offsets.Length; pose++)
                {
                    actor.Apply(VillageResidentAction.Idle, 0f);
                    Vector3 right = Vector3.ProjectOnPlane(upper[0].position - upper[1].position, actor.transform.up).normalized;
                    Vector3 forward = Vector3.Cross(right, actor.transform.up).normalized;
                    var targets = new Vector3[2]; var upperLengths = new float[2]; var lowerLengths = new float[2];
                    for (int side = 0; side < 2; side++)
                    {
                        Vector3 offset = offsets[pose];
                        targets[side] = upper[side].position + right * ((side == 0 ? 1f : -1f) * offset.x) +
                            actor.transform.up * offset.y + forward * offset.z;
                        upperLengths[side] = Vector3.Distance(upper[side].position, elbow[side].position);
                        lowerLengths[side] = Vector3.Distance(elbow[side].position, wrist[side].position);
                    }
                    Assert.That(actor.ApplyHandContacts(targets[0], targets[1]), Is.True, "Reach pose " + pose + "/" + yaw);
                    for (int side = 0; side < 2; side++)
                    {
                        Assert.That(Vector3.Distance(upper[side].position, elbow[side].position), Is.EqualTo(upperLengths[side]).Within(.00001f));
                        Assert.That(Vector3.Distance(elbow[side].position, wrist[side].position), Is.EqualTo(lowerLengths[side]).Within(.00001f));
                        if (pose < 2)
                        {
                            Vector3 arm = elbow[side].position - upper[side].position;
                            Assert.That(Vector3.Dot(arm, actor.transform.up), Is.LessThan(-.06f), "Forward hands need lowered elbows.");
                            Assert.That(Vector3.Dot(arm, right) * (side == 0 ? 1f : -1f), Is.LessThan(.14f),
                                "Forward contact must not spread both upper arms sideways.");
                        }
                    }
                }
            }
            yield return null;
        }

        [UnityTest]
        [PrebuildSetup(typeof(DefaultNpcWardrobeAssetsSetup))]
        public IEnumerator OrdinaryWorker_SharesCatalogAssetAndActionsAcrossVisibilityChanges()
        {
            var library = VillageResidentLibrary.Load();
            Assert.That(library, Is.Not.Null);
            Assert.That(DefaultNpcCatalog.ModelIds, Does.Contain(DefaultNpcCatalog.OrdinaryWorker));
            Assert.That(DefaultNpcCatalog.ModelIds.Distinct().Count(), Is.EqualTo(DefaultNpcCatalog.ModelIds.Count));
            Assert.That(DefaultNpcCatalog.GetPrefab(),
                Is.SameAs(library.GetPrefab(VillageResidentRole.StationWorker)),
                "The village role and shared catalog must resolve the same upgraded asset.");
            Assert.That(Resources.Load<GameObject>(DefaultNpcCatalog.GetResourcePath(DefaultNpcCatalog.OrdinaryWorker)),
                Is.SameAs(DefaultNpcCatalog.GetPrefab()));
            Assert.That(() => DefaultNpcCatalog.GetResourcePath(null), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => DefaultNpcCatalog.GetResourcePath("missing-appearance"), Throws.InstanceOf<ArgumentException>());

            host = new GameObject("Default NPC Factory Regression");
            host.transform.SetPositionAndRotation(new Vector3(3f, 2f, -4f), Quaternion.Euler(0f, 37f, 0f));
            // Both supported entry points must agree after an intentional asset upgrade.
            var existing = library.Create(VillageResidentRole.StationWorker, host.transform);
            var shared = DefaultNpcFactory.Create(host.transform);
            Assert.That(shared.transform.parent, Is.SameAs(host.transform));
            Assert.That(shared.IsInitialized, Is.True, "Creation must return a ready animation sampler.");
            Assert.That(shared.GetComponentsInChildren<AlpineVillageLifeController>(true), Is.Empty);
            Assert.That(shared.GetComponentsInChildren<VillageResidentGreeting>(true), Is.Empty);
            Assert.That(shared.GetComponentsInChildren<Collider>(true), Is.Empty,
                "A model choice must not install the village's behavior or collision.");
            AssertRegisteredOnce(existing);
            AssertRegisteredOnce(shared);
            AssertEquivalent(existing, shared);

            // These are consumer-owned variations already used by the work crews.
            foreach (var actor in new[] { existing, shared })
            {
                actor.transform.localScale *= 1.08f;
                actor.ModelRoot.localRotation *= Quaternion.Euler(0f, 180f, 0f);
                var block = new MaterialPropertyBlock();
                foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.GetPropertyBlock(block);
                    block.SetColor("_BaseColor", new Color(.31f, .42f, .27f));
                    block.SetColor("_Color", new Color(.31f, .42f, .27f));
                    renderer.SetPropertyBlock(block);
                    block.Clear();
                }
            }
            SampleAndCompare(existing, shared);

            existing.gameObject.SetActive(false);
            shared.gameObject.SetActive(false);
            Assert.That(shared.IsInitialized, Is.False, "Hiding the actor must release its animation graph.");
            yield return null;
            existing.gameObject.SetActive(true);
            shared.gameObject.SetActive(true);
            SampleAndCompare(existing, shared);
            AssertRegisteredOnce(existing);
            AssertRegisteredOnce(shared);
        }

        [UnityTest]
        [PrebuildSetup(typeof(DefaultNpcWardrobeAssetsSetup))]
        public IEnumerator OrdinaryWorker_ModularOutfitsCoverLayersAndSurviveVisibilityChanges()
        {
            host = new GameObject("Default NPC Wardrobe Regression");
            VillageResidentPresentation actor = DefaultNpcFactory.Create(host.transform);
            VillageResidentPresentation other = DefaultNpcFactory.Create(host.transform);
            NpcWardrobe wardrobe = actor.GetComponent<NpcWardrobe>();
            Assert.That(wardrobe, Is.Not.Null);
            Assert.That(wardrobe.IsModular, Is.True);
            wardrobe.ValidateBindings();
            CollectionAssert.AreEquivalent(new[] { "everyday", "work", "warm" }, wardrobe.Presets.Select(p => p.Id));
            Assert.That(wardrobe.Items.Count, Is.EqualTo(18));
            Assert.That(wardrobe.BodyRenderers, Is.Not.Empty);
            Assert.That(wardrobe.Items.Select(p => p.Id).Distinct().Count(), Is.EqualTo(wardrobe.Items.Count));
            DefaultNpcAppearance appearance = actor.GetComponent<DefaultNpcAppearance>();
            Assert.That(appearance, Is.Not.Null);
            CollectionAssert.AreEquivalent(new[] { "face-01", "face-02", "face-03", "face-04" }, appearance.Faces.Select(f => f.Id));
            CollectionAssert.AreEquivalent(new[] { "gray", "brunette", "blond" }, appearance.HairColors.Select(h => h.Id));
            AssertStableAppearanceSelection(actor, other);

            var meshes = actor.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r, SharedMesh);
            NpcWardrobe otherWardrobe = other.GetComponent<NpcWardrobe>();
            foreach (NpcWardrobe.ItemBinding item in wardrobe.Items)
            {
                NpcWardrobe.ItemBinding counterpart = otherWardrobe.Items.Single(i => i.Id == item.Id);
                for (int i = 0; i < item.Parts.Count; i++)
                {
                    Assert.That(SharedMesh(item.Parts[i].Renderer), Is.Not.Null);
                    Assert.That(SharedMesh(item.Parts[i].Renderer), Is.SameAs(SharedMesh(counterpart.Parts[i].Renderer)), item.Id);
                    CollectionAssert.AreEqual(item.Parts[i].Renderer.sharedMaterials, counterpart.Parts[i].Renderer.sharedMaterials);
                }
            }

            foreach (NpcWardrobe.OutfitPreset preset in wardrobe.Presets)
            {
                wardrobe.ApplyOutfit(preset.Id);
                CollectionAssert.AreEquivalent(preset.ItemIds, wardrobe.EquippedItemIds);
                Assert.That(wardrobe.CurrentOutfitId, Is.EqualTo(preset.Id));
                Assert.That(wardrobe.BodyRenderers.Any(r => !r.enabled), Is.True, "Clothing must cover the body underneath.");
                AssertVisibleBudget(actor);
                AssertCoverage(wardrobe);
            }

            wardrobe.ApplyOutfit("everyday");
            wardrobe.SetSlot("outerwear", "outerwear.work");
            wardrobe.SetSlot("shirt", "shirt.warm");
            wardrobe.SetSlot("scarf", "scarf.warm");
            Assert.That(wardrobe.GetEquippedItem("trousers"), Is.EqualTo("trousers.everyday"));
            Assert.That(wardrobe.GetEquippedItem("boots"), Is.EqualTo("boots.everyday"));
            Assert.That(wardrobe.GetEquippedItem("headwear"), Is.EqualTo("headwear.everyday"));
            string[] mixed = wardrobe.EquippedItemIds.ToArray();
            wardrobe.SetSlot("headwear", "headwear.warm");
            CollectionAssert.AreEquivalent(mixed.Where(id => !id.StartsWith("headwear.", StringComparison.Ordinal)),
                wardrobe.EquippedItemIds.Where(id => !id.StartsWith("headwear.", StringComparison.Ordinal)),
                "Replacing a single slot must preserve every other item.");
            Assert.That(wardrobe.GetEquippedItem("headwear"), Is.EqualTo("headwear.warm"));
            AssertCoverage(wardrobe);
            AssertVisibleBudget(actor);

            string[] beforeFaceSelection = wardrobe.EquippedItemIds.ToArray();
            var faceHairAtlases = new System.Collections.Generic.HashSet<Texture2D>();
            foreach (DefaultNpcAppearance.FaceBinding face in appearance.Faces)
            {
                string previousHair = appearance.CurrentHairColorId;
                appearance.ApplyFace(face.Id);
                Assert.That(appearance.CurrentFaceId, Is.EqualTo(face.Id));
                Assert.That(appearance.CurrentHairColorId, Is.EqualTo(previousHair), "A face change preserves hair colour.");
                CollectionAssert.AreEqual(beforeFaceSelection, wardrobe.EquippedItemIds,
                    "Changing the face preserves every selected garment.");
                foreach (DefaultNpcAppearance.HairColorBinding hair in appearance.HairColors)
                {
                    appearance.ApplyHairColor(hair.Id);
                    Assert.That(appearance.CurrentHairColorId, Is.EqualTo(hair.Id));
                    Assert.That(appearance.CurrentFaceId, Is.EqualTo(face.Id), "Hair colour does not select a different face.");
                    CollectionAssert.AreEqual(beforeFaceSelection, wardrobe.EquippedItemIds, "Hair colour preserves every garment.");
                    Assert.That(appearance.CurrentAtlas, Is.SameAs(face.GetAtlas(hair.Id)));
                    Assert.That(wardrobe.Atlas, Is.SameAs(appearance.CurrentAtlas));
                    faceHairAtlases.Add(appearance.CurrentAtlas);
                }
            }
            Assert.That(faceHairAtlases.Count, Is.EqualTo(12), "Every face and hair-colour pairing has its authored shared atlas.");
            string selectedFace = appearance.CurrentFaceId;
            string selectedHair = appearance.CurrentHairColorId;
            Texture2D selectedAtlas = appearance.CurrentAtlas;
            Assert.That(() => appearance.ApplyFace("unknown-face"), Throws.ArgumentException);
            Assert.That(() => appearance.ApplyHairColor("unknown-hair-colour"), Throws.ArgumentException);
            Assert.That(appearance.CurrentFaceId, Is.EqualTo(selectedFace));
            Assert.That(appearance.CurrentHairColorId, Is.EqualTo(selectedHair));
            Assert.That(appearance.CurrentAtlas, Is.SameAs(selectedAtlas));
            CollectionAssert.AreEqual(beforeFaceSelection, wardrobe.EquippedItemIds);

            Renderer[] clothing = wardrobe.Items.SelectMany(i => i.Parts).Select(p => p.Renderer).ToArray();
            Renderer[] all = wardrobe.BodyRenderers.Concat(clothing).Distinct().ToArray();
            string[] equipped = wardrobe.EquippedItemIds.ToArray();
            bool[] visible = all.Select(r => r.enabled).ToArray();
            Material[][] materials = all.Select(r => r.sharedMaterials).ToArray();
            Assert.That(() => wardrobe.SetSlot("unknown-slot", "shirt.everyday"), Throws.ArgumentException);
            Assert.That(() => wardrobe.SetSlot("shirt", "boots.work"), Throws.ArgumentException);
            Assert.That(() => wardrobe.ApplyOutfit("unknown-preset"), Throws.ArgumentException);
            CollectionAssert.AreEqual(equipped, wardrobe.EquippedItemIds);
            CollectionAssert.AreEqual(visible, all.Select(r => r.enabled));

            actor.gameObject.SetActive(false);
            yield return null;
            actor.gameObject.SetActive(true);
            actor.Apply(VillageResidentAction.Reach, 1.5f);
            Assert.That(appearance.CurrentFaceId, Is.EqualTo(selectedFace));
            Assert.That(appearance.CurrentHairColorId, Is.EqualTo(selectedHair));
            CollectionAssert.AreEqual(equipped, wardrobe.EquippedItemIds);
            CollectionAssert.AreEqual(visible, all.Select(r => r.enabled));
            for (int i = 0; i < all.Length; i++) CollectionAssert.AreEqual(materials[i], all[i].sharedMaterials, all[i].name);
            foreach (var pair in meshes) Assert.That(SharedMesh(pair.Key), Is.SameAs(pair.Value), "Wardrobe must never duplicate meshes.");
            var block = new MaterialPropertyBlock();
            foreach (NpcWardrobe.GarmentBinding part in wardrobe.Items.Where(i => wardrobe.IsEquipped(i.Id)).SelectMany(i => i.Parts))
            {
                part.Renderer.GetPropertyBlock(block);
                Assert.That(block.GetTexture("_BaseMap"), Is.SameAs(wardrobe.Atlas), part.Renderer.name);
                Assert.That(block.GetColor("_BaseColor"), Is.EqualTo(part.Color).Using(AppearanceColorComparer), part.Renderer.name);
                block.Clear();
            }
            foreach (Renderer body in wardrobe.BodyRenderers)
            {
                body.GetPropertyBlock(block);
                Assert.That(block.GetTexture("_BaseMap"), Is.SameAs(selectedAtlas),
                    "The rendered face/body atlas survives visibility restoration: " + body.name);
                block.Clear();
            }
            Renderer[] hairMeshes = actor.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.name.StartsWith("HAIR_", StringComparison.Ordinal)).ToArray();
            Assert.That(hairMeshes, Is.Not.Empty);
            Color selectedHairColor = appearance.HairColors.Single(hair => hair.Id == selectedHair).Color;
            foreach (Renderer hairMesh in hairMeshes)
            {
                hairMesh.GetPropertyBlock(block);
                Assert.That(block.GetColor("_BaseColor"), Is.EqualTo(selectedHairColor).Using(AppearanceColorComparer),
                    "The rendered hair colour survives the sampler's appearance restoration: " + hairMesh.name);
                block.Clear();
            }
            AssertRegisteredOnce(actor);

            // Removing outerwear exposes the actual inner layer; it cannot leave a hole
            // or switch the selected shirt. Removing every garment exposes the whole body.
            wardrobe.SetSlot("outerwear", null);
            wardrobe.SetSlot("scarf", null);
            Assert.That(wardrobe.GetEquippedItem("shirt"), Is.EqualTo("shirt.warm"));
            Assert.That(wardrobe.Items.Single(i => i.Id == "shirt.warm").Parts.Any(p => p.Renderer.enabled), Is.True);
            foreach (string slot in wardrobe.Items.Select(i => i.Slot).Distinct()) wardrobe.SetSlot(slot, null);
            Assert.That(wardrobe.EquippedItemIds, Is.Empty);
            Assert.That(wardrobe.BodyRenderers.All(r => r.enabled), Is.True);
            Assert.That(clothing.Any(r => r.enabled), Is.False);
            Assert.That(appearance.CurrentFaceId, Is.EqualTo(selectedFace), "Changing clothes must not change the face.");
            Assert.That(appearance.CurrentHairColorId, Is.EqualTo(selectedHair), "Changing clothes must not change hair colour.");
            AssertPopulationAllocation(host.transform);
        }

        private static void AssertStableAppearanceSelection(VillageResidentPresentation actor, VillageResidentPresentation recreated)
        {
            DefaultNpcAppearance appearance = actor.GetComponent<DefaultNpcAppearance>();
            DefaultNpcAppearance again = recreated.GetComponent<DefaultNpcAppearance>();
            NpcWardrobe wardrobe = actor.GetComponent<NpcWardrobe>();
            NpcWardrobe againWardrobe = recreated.GetComponent<NpcWardrobe>();
            const string key = "default-npc-regression/stable-actor";
            appearance.Randomize(key);
            string face = appearance.CurrentFaceId;
            string hair = appearance.CurrentHairColorId;
            string[] clothes = wardrobe.EquippedItemIds.ToArray();
            appearance.Randomize("unrelated-actor-first");
            again.Randomize(key);
            Assert.That(again.CurrentFaceId, Is.EqualTo(face));
            Assert.That(again.CurrentHairColorId, Is.EqualTo(hair));
            CollectionAssert.AreEqual(clothes, againWardrobe.EquippedItemIds,
                "Recreating the same identity cannot depend on another actor's creation order.");
            appearance.Randomize(key);
            Assert.That(appearance.AppearanceKey, Is.EqualTo(key));
            Assert.That(appearance.CurrentFaceId, Is.EqualTo(face));
            Assert.That(appearance.CurrentHairColorId, Is.EqualTo(hair));
            CollectionAssert.AreEqual(clothes, wardrobe.EquippedItemIds);
            var faces = new System.Collections.Generic.HashSet<string>();
            var hairColors = new System.Collections.Generic.HashSet<string>();
            var combinations = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 8; i++)
            {
                appearance.Randomize("default-npc-regression/actor-" + i);
                faces.Add(appearance.CurrentFaceId);
                hairColors.Add(appearance.CurrentHairColorId);
                combinations.Add(string.Join("|", wardrobe.EquippedItemIds.OrderBy(id => id)));
            }
            Assert.That(faces.Count, Is.GreaterThan(1), "Different identities use the face pool.");
            Assert.That(hairColors.Count, Is.GreaterThan(1), "Different identities use the hair-colour pool.");
            Assert.That(combinations.Count, Is.GreaterThan(1), "Different identities mix the wardrobe.");
            face = appearance.CurrentFaceId;
            hair = appearance.CurrentHairColorId;
            wardrobe.Randomize("clothes-only-key");
            Assert.That(appearance.CurrentFaceId, Is.EqualTo(face), "Clothing randomization is independent of face selection.");
            Assert.That(appearance.CurrentHairColorId, Is.EqualTo(hair), "Clothing randomization is independent of hair colour.");
        }

        private static void AssertPopulationAllocation(Transform parent)
        {
            string[] ids = DefaultNpcPopulation.Characters.Select(character => character.Id).ToArray();
            Assert.That(ids.Length, Is.EqualTo(13 + DefaultNpcPopulation.FairVisitorCount + DefaultNpcPopulation.PedestrianCount),
                "Placed workers and fair visitors plus one permanent walker per pooled City slot.");
            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length));
            var remembered = new System.Collections.Generic.Dictionary<string, string>();
            var visibleLooks = new System.Collections.Generic.HashSet<string>();
            foreach (string id in ids.Concat(ids.Reverse()))
            {
                VillageResidentPresentation actor = DefaultNpcFactory.CreateForCharacter(parent, id);
                try
                {
                    var assignment = DefaultNpcPopulation.GetAssignment(id);
                    var appearance = actor.GetComponent<DefaultNpcAppearance>();
                    var wardrobe = actor.GetComponent<NpcWardrobe>();
                    Assert.That(assignment.CharacterId, Is.EqualTo(id));
                    DefaultNpcPopulation.CharacterDefinition definition = DefaultNpcPopulation.Characters.Single(c => c.Id == id);
                    if (!definition.UsesAnyCatalogModel)
                        Assert.That(assignment.ModelId, Is.EqualTo(definition.ModelId));
                    Assert.That(DefaultNpcCatalog.ModelIds, Does.Contain(assignment.ModelId));
                    Assert.That(appearance.AppearanceKey, Is.EqualTo(id));
                    Assert.That(appearance.CurrentFaceId, Is.EqualTo(assignment.FaceId));
                    Assert.That(appearance.CurrentHairColorId, Is.EqualTo(assignment.HairColorId));
                    CollectionAssert.AreEquivalent(assignment.ItemIds, wardrobe.EquippedItemIds);
                    string full = assignment.ModelId + "|" + appearance.CurrentFaceId + "|" + appearance.CurrentHairColorId + "|" +
                        string.Join("|", wardrobe.EquippedItemIds.OrderBy(item => item));
                    if (remembered.TryGetValue(id, out string previous))
                        Assert.That(full, Is.EqualTo(previous), "Recreation in reverse order keeps the assigned model, face and clothes: " + id);
                    else
                    {
                        remembered.Add(id, full);
                        string visible = assignment.ModelId + "|" + appearance.CurrentFaceId + "|" + appearance.CurrentHairColorId + "|" + string.Join("|",
                            wardrobe.Items.Where(item => wardrobe.IsEquipped(item.Id) && item.Parts.Any(part => part.Renderer.enabled))
                                .Select(item => item.Id).OrderBy(item => item));
                        Assert.That(visibleLooks.Add(visible), Is.True,
                            "The current population must not duplicate a visible look when an eligible alternative exists: " + id);
                    }
                }
                finally
                {
                    NpcFootstepSources.Unregister(actor.transform);
                    Object.DestroyImmediate(actor.gameObject);
                }
            }

            // Even identical starting hashes consume distinct choices first; after
            // exhaustion the allocator reuses a least-used visible combination.
            var useCounts = new System.Collections.Generic.Dictionary<string, int>();
            var dimensions = new[] { new[] { "first", "second" } };
            string[] first = DefaultNpcAppearanceSelection.Choose("same-key", dimensions, choice => choice[0], useCounts);
            string[] second = DefaultNpcAppearanceSelection.Choose("same-key", dimensions, choice => choice[0], useCounts);
            Assert.That(second[0], Is.Not.EqualTo(first[0]));
            DefaultNpcAppearanceSelection.Choose("same-key", dimensions, choice => choice[0], useCounts);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, useCounts.Values);
        }

        private static void AssertCoverage(NpcWardrobe wardrobe)
        {
            var worn = wardrobe.Items.Where(i => wardrobe.IsEquipped(i.Id)).ToArray();
            foreach (Renderer covered in worn.SelectMany(i => i.CoveredRenderers))
                Assert.That(covered.enabled, Is.False, "A selected layer covers " + covered.name);
            var inner = wardrobe.Items.Where(i => i.Slot == "shirt").SelectMany(i => i.Parts).Select(p => p.Renderer).ToArray();
            if (wardrobe.GetEquippedItem("outerwear") != "outerwear.everyday")
                Assert.That(worn.Where(i => i.Slot == "outerwear").SelectMany(i => i.CoveredRenderers).Intersect(inner),
                    Is.Not.Empty, "A closed jacket explicitly covers inner-shirt geometry.");
            else
                Assert.That(worn.Where(i => i.Slot == "shirt").SelectMany(i => i.Parts).Any(p => p.Renderer.enabled),
                    Is.True, "The open everyday vest preserves the shirt visible through its V opening.");
        }

        private static Mesh SharedMesh(Renderer renderer) => renderer is SkinnedMeshRenderer skin
            ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;

        private static void AssertVisibleBudget(VillageResidentPresentation actor)
        {
            int triangles = actor.GetComponentsInChildren<Renderer>().Where(r => r.enabled)
                .Select(SharedMesh).Where(mesh => mesh != null).Sum(mesh => mesh.triangles.Length / 3);
            Assert.That(triangles, Is.InRange(1, 8000), "The budget applies to the worn set, not the stored wardrobe.");
        }

        private static void SampleAndCompare(VillageResidentPresentation existing, VillageResidentPresentation shared)
        {
            foreach (var action in new[] { VillageResidentAction.Idle, VillageResidentAction.Walk,
                VillageResidentAction.StationWork, VillageResidentAction.CarryWalk })
            {
                Assert.That(shared.ClipLength(action), Is.EqualTo(existing.ClipLength(action)));
                foreach (float phase in new[] { 0f, .37f, .81f })
                {
                    float seconds = existing.ClipLength(action) * phase;
                    existing.Apply(action, seconds);
                    shared.Apply(action, seconds);
                    AssertEquivalent(existing, shared);
                }
            }
        }

        private static void AssertRegisteredOnce(VillageResidentPresentation actor) =>
            Assert.That(NpcFootstepSources.Prune().Count(root => root == actor.transform), Is.EqualTo(1),
                "Each model instance must remain a single footstep source across hiding and reappearing.");

        private static void AssertEquivalent(VillageResidentPresentation existing, VillageResidentPresentation shared)
        {
            Transform[] before = existing.GetComponentsInChildren<Transform>(true);
            Transform[] after = shared.GetComponentsInChildren<Transform>(true);
            Assert.That(after.Length, Is.EqualTo(before.Length));
            for (int i = 0; i < before.Length; i++)
            {
                Assert.That(after[i].name, Is.EqualTo(before[i].name));
                Assert.That(after[i].childCount, Is.EqualTo(before[i].childCount), before[i].name);
                AssertVector(before[i].localPosition, after[i].localPosition, before[i].name + " position");
                AssertVector(before[i].localScale, after[i].localScale, before[i].name + " scale");
                Assert.That(Mathf.Abs(Quaternion.Dot(before[i].localRotation, after[i].localRotation)),
                    Is.EqualTo(1f).Within(.00001f), before[i].name + " rotation");
            }
            AssertVector(existing.RightGrip.position, shared.RightGrip.position, "Right hand contact");
            AssertVector(existing.LeftGrip.position, shared.LeftGrip.position, "Left hand contact");
            AssertVector(existing.Head.position, shared.Head.position, "Head anchor");

            Renderer[] oldRenderers = existing.GetComponentsInChildren<Renderer>(true);
            Renderer[] newRenderers = shared.GetComponentsInChildren<Renderer>(true);
            Assert.That(newRenderers.Length, Is.EqualTo(oldRenderers.Length));
            var oldBlock = new MaterialPropertyBlock();
            var newBlock = new MaterialPropertyBlock();
            for (int i = 0; i < oldRenderers.Length; i++)
            {
                Renderer oldRenderer = oldRenderers[i], newRenderer = newRenderers[i];
                Assert.That(newRenderer.GetType(), Is.EqualTo(oldRenderer.GetType()), oldRenderer.name);
                Assert.That(newRenderer.enabled, Is.EqualTo(oldRenderer.enabled), oldRenderer.name);
                CollectionAssert.AreEqual(oldRenderer.sharedMaterials, newRenderer.sharedMaterials, oldRenderer.name);
                Mesh OldMesh(Renderer renderer) => renderer is SkinnedMeshRenderer skin
                    ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                Assert.That(OldMesh(newRenderer), Is.SameAs(OldMesh(oldRenderer)), oldRenderer.name);
                oldRenderer.GetPropertyBlock(oldBlock);
                newRenderer.GetPropertyBlock(newBlock);
                foreach (string color in new[] { "_BaseColor", "_Color" })
                    Assert.That(newBlock.GetColor(color), Is.EqualTo(oldBlock.GetColor(color)), oldRenderer.name);
                foreach (string texture in new[] { "_BaseMap", "_MainTex" })
                    Assert.That(newBlock.GetTexture(texture), Is.SameAs(oldBlock.GetTexture(texture)), oldRenderer.name);
                oldBlock.Clear();
                newBlock.Clear();
            }
        }

        private static void AssertVector(Vector3 before, Vector3 after, string message) =>
            Assert.That(Vector3.Distance(before, after), Is.LessThan(.00001f), message);
    }
}
