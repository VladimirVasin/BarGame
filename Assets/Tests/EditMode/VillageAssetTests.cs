using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The village kit's contract, and the dressing built on top of it.
    ///
    /// The kit is geometry only. Everything a person touches - a door, a
    /// threshold, a collider - belongs to the plan, and the point of these
    /// tests is that neither half can quietly take over the other's job.
    /// </summary>
    public sealed class VillageAssetTests
    {
        private const string ManifestPath =
            "Assets/Village/Models/Village3D.json";
        private const double SignedVolumeEpsilon = 0.0000001d;

        private static AlpineVillagePlan CreatePlan()
        {
            return AlpineVillagePlanner.Create(
                GameSessionState.DefaultCitySeed);
        }

        /// <summary>
        /// The C# catalog and the generator's own `make_assemblies()` must
        /// describe the same kit. This is the seam they drift apart at, and
        /// the editor binder derives what it imports from exactly this.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Catalog_DescribesTheKitTheGeneratorBuilt()
        {
            Assert.That(
                VillageAssetProvider.GetExpectedMeshTotal(),
                Is.EqualTo(VillageAssetProvider.ExpectedMeshCount));

            int assemblies = 0;
            var names = new HashSet<string>();
            for (int index = 0;
                 index < VillageAssetProvider.SupportedKindCount;
                 index++)
            {
                VillageAssetKind kind =
                    VillageAssetProvider.GetSupportedKind(index);
                int variants = VillageAssetProvider.GetVariantCount(kind);
                assemblies += variants;
                foreach (VillageMeshRole role in
                         VillageAssetProvider.GetRoles(kind))
                {
                    for (int variant = 0; variant < variants; variant++)
                    {
                        Assert.That(
                            names.Add(
                                VillageAssetProvider.GetExpectedMeshName(
                                    kind,
                                    variant,
                                    role)),
                            Is.True,
                            "Two catalog entries claim one mesh name.");
                    }
                }
            }

            Assert.That(
                assemblies,
                Is.EqualTo(VillageAssetProvider.ExpectedAssemblyCount));
        }

        [Test]
        [Category("AlpineVillage")]
        public void Manifest_AgreesWithTheBoundProvider()
        {
            Assert.That(
                File.Exists(ManifestPath),
                Is.True,
                "The village kit has never been generated.");

            string json = File.ReadAllText(ManifestPath);
            Assert.That(
                json,
                Does.Contain(
                    $"\"generator_version\": " +
                    $"\"{VillageAssetProvider.GeneratorVersion}\""));
            Assert.That(json, Does.Contain(VillageAssetProvider.DesignId));
            Assert.That(
                json,
                Does.Contain("\"colliders\": false"),
                "The kit must ship as passive geometry.");

            VillageAssetProvider provider = VillageAssetProvider.Load();
            Assert.That(
                provider,
                Is.Not.Null,
                "The village provider asset is missing; run " +
                "Bar Promenade/Village/Bind Provider.");
            Assert.That(provider.HasCompleteMeshes, Is.True);
            Assert.That(
                provider.EntryCount,
                Is.EqualTo(VillageAssetProvider.ExpectedMeshCount));
            Assert.That(
                json,
                Does.Contain(provider.BuildSignature),
                "The bound provider carries a signature the manifest does " +
                "not know.");
            Assert.DoesNotThrow(() => provider.ValidateOrThrow());
        }

        /// <summary>
        /// A house keeps its shape across rebuilds of the same seed, and the
        /// two architectural families are both distributed along the lane.
        /// The house at its head remains a third, separate catalog kind.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void VariantSelection_IsStableAndSpreadAcrossTheLane()
        {
            AlpineVillagePlan plan = CreatePlan();
            Assert.That(
                VillageAssetProvider.GetVariantCount(
                    VillageAssetKind.House),
                Is.EqualTo(2),
                "Ordinary weathering variants must not become more " +
                "architectural families.");
            Assert.That(
                VillageAssetProvider.GetVariantCount(
                    VillageAssetKind.TopHouse),
                Is.EqualTo(1),
                "The house at the head of the lane is its own third type.");

            var used = new HashSet<int>();
            int[] counts = new int[VillageAssetProvider.HouseVariantCount];
            int previous = -1;
            int runLength = 0;
            int longestRun = 0;
            AlpineVillagePlotDescriptor[] ordinaryHouses = plan.Plots
                .Where(plot => plot.Kind == AlpineVillagePlotKind.House)
                .OrderBy(plot => plot.LaneDistance)
                .ToArray();
            foreach (AlpineVillagePlotDescriptor plot in ordinaryHouses)
            {
                int first = VillageAssetProvider.SelectVariant(
                    VillageAssetKind.House,
                    plot.StableId);
                int second = VillageAssetProvider.SelectVariant(
                    VillageAssetKind.House,
                    plot.StableId);
                Assert.That(second, Is.EqualTo(first));
                Assert.That(
                    first,
                    Is.InRange(
                        0,
                        VillageAssetProvider.HouseVariantCount - 1));
                used.Add(first);
                counts[first]++;
                runLength = first == previous ? runLength + 1 : 1;
                longestRun = Mathf.Max(longestRun, runLength);
                previous = first;
            }

            Assert.That(
                used.Count,
                Is.EqualTo(2),
                "Both ordinary architectural types must reach the lane.");
            Assert.That(
                System.Math.Abs(counts[0] - counts[1]),
                Is.LessThanOrEqualTo(2),
                "One ordinary house type overwhelms the other.");
            Assert.That(
                longestRun,
                Is.LessThanOrEqualTo(2),
                "Three identical house silhouettes form a cloned row.");
            Assert.That(
                VillageAssetProvider.SelectVariant(
                    VillageAssetKind.TopHouse,
                    plan.MothersHouse.StableId),
                Is.Zero);
        }

        /// <summary>
        /// The Blender contact sheet used to be two-sided, while the runtime
        /// Lit material culls back faces. That let a complete but inside-out
        /// building look correct in Blender and lose every wall and roof slab
        /// in the game.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void BuildingShells_ShareTheBoxAuthoredPlinthWinding()
        {
            VillageAssetProvider provider =
                VillageAssetProvider.LoadOrThrow();
            Mesh reference = provider.GetPartOrThrow(
                VillageAssetKind.House,
                0,
                VillageMeshRole.Plinth).Mesh;
            double referenceVolume = CalculateSignedVolume(reference);
            Assert.That(
                System.Math.Abs(referenceVolume),
                Is.GreaterThan(SignedVolumeEpsilon),
                "The box-authored plinth is not a usable winding reference.");
            int referenceSign = System.Math.Sign(referenceVolume);

            VillageAssetKind[] buildings =
            {
                VillageAssetKind.House,
                VillageAssetKind.Chapel,
                VillageAssetKind.TopHouse
            };
            VillageMeshRole[] shellRoles =
            {
                VillageMeshRole.Walls,
                VillageMeshRole.Roof
            };
            foreach (VillageAssetKind kind in buildings)
            {
                for (int variant = 0;
                     variant < VillageAssetProvider.GetVariantCount(kind);
                     variant++)
                {
                    foreach (VillageMeshRole role in shellRoles)
                    {
                        Mesh mesh = provider.GetPartOrThrow(
                            kind,
                            variant,
                            role).Mesh;
                        double volume = CalculateSignedVolume(mesh);
                        Assert.That(
                            System.Math.Abs(volume),
                            Is.GreaterThan(SignedVolumeEpsilon),
                            $"'{mesh.name}' has no reliable signed volume.");
                        Assert.That(
                            System.Math.Sign(volume),
                            Is.EqualTo(referenceSign),
                            $"'{mesh.name}' is inside-out relative to " +
                            $"'{reference.name}'.");
                    }
                }
            }
        }

        /// <summary>
        /// Collision is the plan's. The kit's importer forbids colliders, and
        /// adding one to an imported part is how a bar floor once became a
        /// two-kilometre slab lying on its side.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void World_PutsCollisionOnThePlanAndNotOnTheImportedMesh()
        {
            AlpineVillagePlan plan = CreatePlan();
            var host = new GameObject("Village Kit Test Host");
            try
            {
                AlpineVillageWorldResult world =
                    AlpineVillageWorldBuilder.Build(host.transform, plan);

                foreach (AlpineVillagePlotDescriptor plot in plan.Plots)
                {
                    if (plot.Kind == AlpineVillagePlotKind.Spring)
                    {
                        continue;
                    }

                    Transform root = world.SemanticObjects[plot.StableId];
                    Transform shell = root.Find("Physical Shell");
                    Assert.That(
                        shell,
                        Is.Not.Null,
                        $"'{plot.StableId}' has no plan-derived collider.");
                    var box = shell.GetComponent<BoxCollider>();
                    Assert.That(box, Is.Not.Null);
                    Assert.That(
                        box.size.y,
                        Is.EqualTo(plot.Height).Within(0.001f));
                }

                // No renderer that carries an imported mesh may also carry a
                // collider: that is the plan's job and only the plan's.
                MeshFilter[] filters =
                    world.Root.GetComponentsInChildren<MeshFilter>(true);
                foreach (MeshFilter filter in filters)
                {
                    if (filter.sharedMesh == null ||
                        !filter.sharedMesh.name.StartsWith("GEO_VIL_"))
                    {
                        continue;
                    }

                    Assert.That(
                        filter.GetComponent<Collider>(),
                        Is.Null,
                        $"'{filter.sharedMesh.name}' carries a collider.");
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// The warmth is emissive geometry with a handful of real lamps
        /// behind it. A bulb per light would blow URP's additional-light
        /// budget before the lane was half dressed.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Garlands_AreEmissiveGeometryOnASmallLightBudget()
        {
            AlpineVillagePlan plan = CreatePlan();
            var host = new GameObject("Village Garland Test Host");
            try
            {
                AlpineVillageWorldResult world =
                    AlpineVillageWorldBuilder.Build(host.transform, plan);

                Transform garlands = world.Root.transform
                    .GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == "Village Garlands");
                Assert.That(garlands, Is.Not.Null);

                MeshRenderer[] bulbs = garlands
                    .GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r => r.name.StartsWith("Garland Bulbs"))
                    .ToArray();
                Assert.That(
                    bulbs,
                    Is.Not.Empty,
                    "The lane carries no garlands at all.");
                foreach (MeshRenderer bulb in bulbs)
                {
                    Assert.That(
                        bulb.sharedMaterial,
                        Is.EqualTo(CityNightResources.EmissiveMaterial),
                        "A garland bulb must be emissive geometry.");
                    Assert.That(
                        bulb.GetComponent<Collider>(),
                        Is.Null);
                }

                Light[] lights =
                    world.Root.GetComponentsInChildren<Light>(true);
                Assert.That(
                    lights.Length,
                    Is.GreaterThan(0),
                    "Emissive geometry alone lights nothing.");
                Assert.That(
                    lights.Length,
                    Is.LessThanOrEqualTo(12),
                    "The village is over its realtime-light budget.");
                foreach (Light light in lights)
                {
                    Assert.That(
                        light.shadows,
                        Is.EqualTo(LightShadows.None),
                        "A garland lamp must not cast shadow maps.");
                }

                AlpineVillageWorldBuilder.GetGarlandSpan(
                    plan,
                    AlpineVillageDressingPlanner.AudibleGarlandSpanIndex,
                    out Vector3 left,
                    out Vector3 right);
                Vector3 expectedOwner =
                    AlpineVillageWorldBuilder.SampleGarlandPoint(
                        left,
                        right,
                        0.5f);
                Transform owner = world.SemanticObjects[
                    AlpineVillageDressingPlanner.GarlandOwnerStableId];
                Assert.That(
                    Vector3.Distance(owner.position, expectedOwner),
                    Is.LessThan(0.001f),
                    "The causal wire owner is a batching pivot, not the wire.");

                Transform stationMechanism = world.SemanticObjects[
                    AlpineVillageDressingPlanner
                        .StationMechanismOwnerStableId];
                Assert.That(
                    Vector3.Distance(
                        stationMechanism.position,
                        plan.Station.Cableway.Nodes[0].CableCenter),
                    Is.LessThan(0.001f),
                    "Station sound is owned by the pad, not the bullwheel.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// Every house on the lane gets a door at the same real size, which
        /// is the reason the kit ships none: a modelled door would scale from
        /// a hatch to a barn opening across these plots.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Doors_AreTheSameRealSizeOnEveryHouse()
        {
            AlpineVillagePlan plan = CreatePlan();
            var host = new GameObject("Village Door Test Host");
            try
            {
                AlpineVillageWorldResult world =
                    AlpineVillageWorldBuilder.Build(host.transform, plan);
                int found = 0;
                foreach (AlpineVillagePlotDescriptor plot in plan.Plots)
                {
                    if (plot.Kind == AlpineVillagePlotKind.Spring)
                    {
                        continue;
                    }

                    Transform leaf =
                        world.SemanticObjects[plot.StableId].Find("Door Leaf");
                    Assert.That(leaf, Is.Not.Null, plot.StableId);
                    Assert.That(
                        leaf.localScale.y,
                        Is.EqualTo(AlpineVillageWorldBuilder.DoorHeight)
                            .Within(0.001f),
                        $"'{plot.StableId}' has a door of its own size.");
                    found++;
                }

                Assert.That(found, Is.GreaterThan(10));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// Every house on the lane but the mother's is a door the hero can
        /// try, and every one of them is shut.
        ///
        /// What this actually pins is the four numbers agreeing. The leaf,
        /// the trigger, the dock the gesture walks him to and the trodden
        /// path that arrives there all come off the same plan-owned
        /// threshold, and the dock keeps the plot shelf's height - a dock
        /// more than the motor's vertical tolerance off his root is refused
        /// in SILENCE, which is a prompt that shows and a key that does
        /// nothing forever.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void HouseDoors_AreShutStandardDoorsOnEveryHouseButTheMothers()
        {
            AlpineVillagePlan plan = CreatePlan();
            var host = new GameObject("Village Door Interaction Host");
            try
            {
                AlpineVillageWorldResult world =
                    AlpineVillageWorldBuilder.Build(host.transform, plan);
                int houses = plan.Plots.Count(
                    plot => plot.Kind == AlpineVillagePlotKind.House);
                Assert.That(
                    world.HouseDoors.Count,
                    Is.EqualTo(houses),
                    "One shut door per house on the lane.");
                Assert.That(houses, Is.EqualTo(AlpineVillagePlanner.HouseCount));

                foreach (AlpineVillagePlotDescriptor plot in plan.Plots)
                {
                    Transform plotRoot = world.SemanticObjects[plot.StableId];
                    Transform doorRoot = plotRoot.Find(
                        AlpineVillageWorldBuilder.HouseDoorObjectName);
                    if (plot.Kind != AlpineVillagePlotKind.House)
                    {
                        Assert.That(
                            doorRoot,
                            Is.Null,
                            $"'{plot.StableId}' is not a house on the lane.");
                        continue;
                    }

                    Assert.That(doorRoot, Is.Not.Null, plot.StableId);
                    var door =
                        doorRoot.GetComponent<LockedDoorInteraction>();
                    Assert.That(door, Is.Not.Null, plot.StableId);
                    Assert.That(door.IsConfigured, Is.True, plot.StableId);
                    Assert.That(
                        door.PromptKey,
                        Is.EqualTo(
                            AlpineVillageWorldBuilder.HouseDoorPromptKey));
                    Assert.That(
                        door.LockedKey,
                        Is.EqualTo(
                            AlpineVillageWorldBuilder.HouseDoorLockedKey));
                    Assert.That(world.HouseDoors, Contains.Item(door));

                    var trigger = doorRoot.GetComponent<SphereCollider>();
                    Assert.That(trigger, Is.Not.Null, plot.StableId);
                    Assert.That(trigger.isTrigger, Is.True, plot.StableId);

                    var target =
                        doorRoot.GetComponent<PlayerDoorActionTarget>();
                    Assert.That(target, Is.Not.Null, plot.StableId);
                    Assert.That(target.IsConfigured, Is.True, plot.StableId);

                    PlayerDoorActionPlan actionPlan = target.Plan;
                    Assert.That(
                        Vector3.Distance(
                            actionPlan.EntryRootPosition,
                            plot.DoorDockPosition +
                            Vector3.up * PlayerFactory.GroundedRootOffset),
                        Is.LessThan(0.001f),
                        $"'{plot.StableId}' does not use the planned dock.");
                    Assert.That(
                        Vector3.Dot(
                            actionPlan.EntryFacingDirection,
                            plot.Facing),
                        Is.LessThan(-0.999f),
                        $"'{plot.StableId}' turns the hero away from itself.");

                    // The dock stands on the plot's own flattened shelf, and
                    // the gesture only ever starts because it does.
                    float dockGround =
                        AlpineVillageTerrainSampler.SampleHeight(
                            plan,
                            new Vector2(
                                plot.DoorDockPosition.x,
                                plot.DoorDockPosition.z));
                    Assert.That(
                        Mathf.Abs(plot.DoorDockPosition.y - dockGround),
                        Is.LessThan(PlayerMotor.InteractionVerticalTolerance),
                        $"'{plot.StableId}' docks off its own ground.");

                    // The trigger stands over the leaf the hero reaches for,
                    // not over the middle of a wall the plan never used.
                    Transform leaf = plotRoot.Find("Door Leaf");
                    Assert.That(
                        Mathf.Abs(
                            plotRoot.InverseTransformPoint(
                                doorRoot.position).x -
                            plotRoot.InverseTransformPoint(
                                leaf.position).x),
                        Is.LessThan(0.001f),
                        $"'{plot.StableId}' triggers beside its own leaf.");
                    Assert.That(
                        plotRoot.Find("Door Handle"),
                        Is.Not.Null,
                        $"'{plot.StableId}' has nothing to take hold of.");
                }

                // The one door that opens keeps its own component and its
                // own destination.
                Transform mothers =
                    world.SemanticObjects["village-mothers-house"];
                Assert.That(
                    mothers.GetComponentInChildren<LockedDoorInteraction>(),
                    Is.Null,
                    "The mother's house is not shut to her son.");
                Assert.That(
                    world.MothersHouseEntrance,
                    Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        [Category("AlpineVillage")]
        public void HouseDoorLines_ExistInBothLocalizationCatalogs()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                TextAsset catalog = Resources.Load<TextAsset>(
                    $"Localization/{language}");
                Assert.That(catalog, Is.Not.Null);
                foreach (string key in new[]
                         {
                             AlpineVillageWorldBuilder.HouseDoorPromptKey,
                             AlpineVillageWorldBuilder.HouseDoorLockedKey
                         })
                {
                    Assert.That(
                        catalog.text.Contains($"\"{key}\""),
                        Is.True,
                        $"{language}.json is missing '{key}'.");
                }
            }
        }

        private static double CalculateSignedVolume(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            double sixTimesVolume = 0d;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector3 first = vertices[triangles[index]];
                Vector3 second = vertices[triangles[index + 1]];
                Vector3 third = vertices[triangles[index + 2]];
                sixTimesVolume += Vector3.Dot(
                    first,
                    Vector3.Cross(second, third));
            }

            return sixTimesVolume / 6d;
        }
    }
}
