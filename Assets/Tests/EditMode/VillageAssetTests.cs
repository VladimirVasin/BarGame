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
        /// four variants are actually used rather than one being picked for
        /// every plot on the lane.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void VariantSelection_IsStableAndSpreadAcrossTheLane()
        {
            AlpineVillagePlan plan = CreatePlan();
            var used = new HashSet<int>();
            foreach (AlpineVillagePlotDescriptor plot in plan.Plots)
            {
                if (plot.Kind != AlpineVillagePlotKind.House &&
                    plot.Kind != AlpineVillagePlotKind.MothersHouse)
                {
                    continue;
                }

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
            }

            Assert.That(
                used.Count,
                Is.GreaterThanOrEqualTo(3),
                "A lane of identical houses is not a village.");
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
                    if (plot.Kind == AlpineVillagePlotKind.Adit ||
                        plot.Kind == AlpineVillagePlotKind.Cemetery)
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
                    if (plot.Kind == AlpineVillagePlotKind.Adit ||
                        plot.Kind == AlpineVillagePlotKind.Cemetery)
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
