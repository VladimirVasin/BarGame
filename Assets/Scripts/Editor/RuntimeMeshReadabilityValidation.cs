using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Read-only packaging contract for audited imported runtime CPU/physics sources.
    /// Editor geometry access and a successful prefab stamp do not prove player CPU data.
    /// This is an explicit consumer registry, not a rule to make every model readable.
    /// </summary>
    public static class RuntimeMeshReadabilityValidation
    {
        public const int MaximumReportedFailures = 16;

        private sealed class Requirement
        {
            internal readonly string Path, Consumer, Repair;
            internal Requirement(string path, string consumer, string repair)
            { Path = path; Consumer = consumer; Repair = repair; }
        }

        private static readonly Requirement[] Required = CreateRequirements();

        [MenuItem("Bar Promenade/Build/Reimport Audited Collision Fixes")]
        public static void ReimportCollisionFixes()
        {
            // Explicit repair only. The build gate itself remains read-only.
            StairwellLabyrinthAssetSetup.EnsureAssets();
            MountainRoadCafeAssetSetup.Run();
            ValidateOrThrow();
            Debug.Log("AUDITED PLAYER CPU/COLLISION IMPORTS OK");
        }

        [MenuItem("Bar Promenade/Build/Validate Runtime Mesh Readability (Read Only)")]
        public static void ValidateOrThrow()
        {
            string report = SummarizeFailures(InspectRequired());
            if (report.Length != 0)
                throw new InvalidOperationException("Imported runtime CPU/physics mesh contract failed. No assets were changed.\n" + report);
        }

        private static IEnumerable<string> InspectRequired()
        {
            foreach (Requirement entry in Required)
            {
                string failure;
                try
                {
                    var importer = AssetImporter.GetAtPath(entry.Path) as ModelImporter;
                    var meshes = new List<Mesh>();
                    foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(entry.Path))
                        if (asset is Mesh mesh) meshes.Add(mesh);
                    failure = InspectImportedModel(entry.Path, entry.Consumer, entry.Repair,
                        importer != null, importer != null && importer.isReadable, meshes.ToArray());
                }
                catch (Exception exception)
                {
                    failure = $"{entry.Path}; consumer: {entry.Consumer}; asset inspection failed ({exception.GetType().Name}). Repair: {entry.Repair}";
                }
                yield return failure;
            }
        }

        /// <summary>Metadata-only seam for negative fixtures; never reimports or reads vertex arrays.</summary>
        public static string InspectImportedModel(string path, string consumer, string repair,
            bool importerPresent, bool importerReadable, Mesh[] meshes)
        {
            var reasons = new List<string>(3);
            if (!importerPresent) reasons.Add("ModelImporter is missing");
            else if (!importerReadable) reasons.Add("ModelImporter Read/Write is disabled");
            if (meshes == null || meshes.Length == 0) reasons.Add("imported mesh sub-assets are missing");
            else
            {
                int invalid = 0;
                string first = null;
                foreach (Mesh mesh in meshes)
                {
                    if (mesh != null && mesh.isReadable && mesh.vertexCount > 0) continue;
                    invalid++;
                    if (first == null) first = mesh != null ? mesh.name : "<missing>";
                }
                if (invalid != 0)
                    reasons.Add($"{invalid} mesh(es) are missing, unreadable or empty (first '{first}')");
            }
            return reasons.Count == 0 ? string.Empty :
                $"{path}; consumer: {consumer}; {string.Join("; ", reasons)}. Repair: {repair}";
        }

        /// <summary>One bounded row per failed source, including the total after the visible rows.</summary>
        public static string SummarizeFailures(IEnumerable<string> failures)
        {
            var shown = new List<string>(MaximumReportedFailures);
            int total = 0;
            foreach (string failure in failures)
            {
                if (string.IsNullOrEmpty(failure)) continue;
                total++;
                if (shown.Count < MaximumReportedFailures) shown.Add("- " + failure);
            }
            return total == 0 ? string.Empty :
                $"{total} source(s) failed, showing {shown.Count}.\n" + string.Join("\n", shown);
        }

        private static Requirement[] CreateRequirements()
        {
            var entries = new SortedDictionary<string, Requirement>(StringComparer.Ordinal);
            void Add(string path, string consumer, string repair)
            {
                if (!entries.TryAdd(path, new Requirement(path, consumer, repair)))
                    throw new InvalidOperationException("Duplicate audited runtime mesh source: " + path);
            }
            void Family(string folder, string[] names, string consumer, string policy)
            {
                foreach (string name in names)
                {
                    string path = folder + name + ".fbx";
                    Add(path, consumer, $"Reimport '{path}' with Read/Write enabled; preserve {policy}.");
                }
            }

            // CPU readers: production rigs, cloth/contact sources and imported geometry fitting/combining.
            Add(Player3DV2AssetSetup.ModelPath, "PlayerJacketClothSurface / PlayerScarfCollisionWorld / NpcHandPose",
                "BarPromenade.Editor.Player3DV2AssetSetup.RunBatch");
            Add(PlayerScarfAssetSetup.WornPath, "PlayerScarfPresentation CPU cloth geometry",
                "BarPromenade.Editor.PlayerScarfAssetSetup.BuildOrThrow");
            Add(PlayerScarfAssetSetup.FoldedPath, "PlayerScarfPresentation CPU cloth geometry",
                "BarPromenade.Editor.PlayerScarfAssetSetup.BuildOrThrow");
            Add(DefaultNpcAssetSetup.ModelPath, "NpcHandPose / bartender garment geometry",
                "BarPromenade.Editor.DefaultNpcAssetSetup.BuildOrThrow");
            Add(DefaultNpcWomanAssetSetup.ModelPath, "CityPedestrianShoeContacts / seated trouser geometry",
                "BarPromenade.Editor.DefaultNpcWomanAssetSetup.BuildOrThrow");
            Add(BarAssetSetup.ServicePropsModelPath, "BarBartenderAssetRegistry.CacheTowelBounds",
                "BarPromenade.Editor.BarAssetSetup.BuildOrThrow (BarModelImporter must retain this CPU source)");
#if SURFACE_CACHE
            Add(BarAssetSetup.InteriorModelPath, "BarSurfaceCacheGiPilot native contributor CPU buffers",
                "Reimport BarInterior3D.fbx with the SURFACE_CACHE BarModelImporter policy");
#endif
            Add(CityChessSetAssetSetup.ModelPath, "CityChessBoardGeometry runtime Mesh.CombineMeshes",
                "BarPromenade.Editor.CityChessSetAssetSetup.BuildOrThrow");
            Add("Assets/Resources/City/StreetProfile/CityStreetProfile3D.fbx", "CityGroundSurfaceSystem imported profile deformation",
                "Reimport CityStreetProfile3D.fbx with CityStreetProfileModelImporter");
            Add(MountainRoadMiscAssetSetup.ModelPath, "MountainRoad model-library BakeImportedMesh",
                "BarPromenade.Editor.MountainRoadMiscAssetSetup.BuildOrThrow");
            Add(MountainCablewayCraftedAssetSetup.ModelPath, "MountainRoad authored cableway BakeImportedMesh",
                "BarPromenade.Editor.MountainCablewayCraftedAssetSetup.RunBatch");
            Add(VillageRockAssetSetup.ModelPath, "Village rock runtime geometry fitting",
                "BarPromenade.Editor.VillageRockAssetSetup.BuildOrThrow");
            Family("Assets/Resources/City/EastExit/", new[] { "CityLitter3D" },
                "CityLitterInstancer readable templates", "the authored readable import");

            // Closed damage-source families: topology, anatomical weights, tissue and wound deformation.
            string[] bodies = { "Hero", "Npc", "Crane", "Room", "Imprint", "LongExposure" };
            foreach (string kind in new[] { "Body", "Head", "Skull" })
                Family("Assets/Resources/CombatGore/", Array.ConvertAll(bodies, body => kind + body),
                    "CombatBodyDestruction / CombatHeadDestruction CPU topology and tissue",
                    "CombatGoreAssetSetup readable damage-source policy");
            Family("Assets/Resources/CombatBlood/", Array.ConvertAll(bodies, body => "Wounds" + body),
                "CombatDamageMarks CPU wound vertices/normals/triangles", "CombatBloodAssetSetup");
            Family("Assets/Resources/CombatBlood/", new[] { "BloodShapes" },
                "CombatBloodEffects / CombatSurfaceImpactEffects CPU shapes", "CombatBloodAssetSetup");
            Family("Assets/Resources/Combat/",
                new[] { "CraneBody", "RoomBody", "ImprintBody", "LongExposureBody", "ZombieDecayBody0", "ZombieDecayBody1", "ZombieDecayBody2", "ZombieBottleFracture" },
                "Combat anatomical hurtboxes / retained source geometry / bottle fracture CPU reads",
                "the authored body/fracture readable importer policy");
            Family("Assets/Resources/CombatWeaponCarry/", new[] { "ShotgunSling" },
                "CombatShotgunSling CPU vertices/UV deformation", "CombatWeaponCarryAssetSetup.OnPreprocessModel");

            // Direct imported MeshCollider families; generated .asset/runtime mesh copies are separate owners.
            Add(StairwellLabyrinthModelImporter.ModelPath, "StairwellLabyrinthWorldBuilder runtime collision cooking",
                "BarPromenade.Editor.StairwellLabyrinthAssetSetup.EnsureAssets");
            Add(MountainRoadCafeAssetSetup.ModelPath, "CityWorldBuilder cafe roof convex rain trigger",
                "BarPromenade.Editor.MountainRoadCafeAssetSetup.Run");
            Add(CityMiscAssetSetup.ModelPath, "CityWorldBuilder gazebo imported MeshColliders",
                "BarPromenade.Editor.CityMiscAssetSetup.BuildOrThrow");
            Add(VillageAssetSetup.ModelPath, "AlpineVillageRoadSurfaceBuilder CPU roads / resident house MeshColliders",
                "BarPromenade.Editor.VillageAssetSetup.BuildOrThrow");
            Add(VillageExpansionAssetSetup.ModelPath, "Village expansion MeshColliders / fitted footbridge / lodge shelter CPU geometry",
                "BarPromenade.Editor.VillageExpansionAssetSetup.BuildOrThrow");
            Add(VillageLifePropAssetSetup.ModelPath, "VillageLifePropLibrary imported MeshColliders",
                "BarPromenade.Editor.VillageLifePropAssetSetup.BuildOrThrow");
            Add(VillageResidentDoorAssetSetup.ModelPath, "VillageResidentDoorAssets imported wall/door MeshColliders",
                "BarPromenade.Editor.VillageResidentDoorAssetSetup.BuildOrThrow");
            Add(VillageWorkroomAssetSetup.ModelPath, "VillageWorkroomAssets imported MeshColliders",
                "BarPromenade.Editor.VillageWorkroomAssetSetup.BuildOrThrow");
            Add(VillageNarrativeAssetSetup.ModelPath, "VillageNarrativeLibrary imported MeshColliders",
                "BarPromenade.Editor.VillageNarrativeAssetSetup.BuildOrThrow");
            Family(CityLaundryAssetSetup.ModelFolder, new[] { "StaticLaundry" },
                "CityLaundryAssetProvider imported collision", "CityLaundryAssetSetup.OnPreprocessModel");
            Family("Assets/Resources/City/EastExit/", new[] { "CityEastExit3D", "CityEastExitDressing3D" },
                "CityEastExitWorldBuilder / dressing imported collision and CPU road fitting", "the authored readable metre import");
            Family(CityFairAssetSetup.ModelFolder, new[] { "Stall", "Bench", "Clutter" },
                "CityFairWorld.Place collision-enabled props", "CityFairAssetSetup.OnPreprocessModel");
            Family(CityFairChildAssetSetup.ModelFolder, new[] { "LowTable" },
                "CityFairChildren imported table collision", "CityFairChildAssetSetup.OnPreprocessModel");
            Family("Assets/Resources/Combat/", new[] { "Arena" },
                "CombatAssetProvider imported arena collision", "CombatTestAssetSetup.OnPreprocessModel");
            Family("Assets/Resources/" + WarehouseArenaAssetProvider.ResourceFolder, WarehouseArenaAssetProvider.ModelNames,
                "WarehouseArenaAssetProvider Collision_ mesh cooking", "CombatWarehouseModelImporter");
            Family("Assets/Resources/CombatWarehouse/", new[] { "BrokenStair" },
                "WarehouseBrokenStairFactory imported collision", "CombatWarehouseModelImporter");
            Family("Assets/Resources/CombatWarehouse/GateChain/", new[] { "GatePlatformLeft", "GatePlatformRight", "ValveChain" },
                "Warehouse gate-chain imported collision", "the authored readable import");
            Family("Assets/Resources/CombatWarehouse/Lockers/", new[] { "WarehouseLocker" },
                "WarehouseLockerFactory imported collision", "the authored readable import");
            Family("Assets/Resources/CombatWarehouse/FishAssembly/",
                new[] { "FishHead", "FishTail", "FishBelly", "FishPlaySurface", "WarehouseGateKey", "AssembledFish" },
                "WarehouseFishAssemblyAssets imported collision", "WarehouseFishAssemblyModelImporter");
            Family("Assets/Resources/CombatWarehouse/FishLampBurst/", new[] { "LampShards" },
                "Warehouse fish-lamp burst imported shard collision", "the authored readable import");
            // Closed provider lists own the admissible COL_ source families; additions inherit the obligation.
            Family("Assets/Resources/" + CityPortAssetProvider.ResourceFolder, CityPortAssetProvider.ModelNames,
                "CityPortAssetProvider COL_ collision / DockVisible__Tare / driveway fitting", "CityPortAssetSetup.OnPreprocessModel");
            Family("Assets/Resources/" + CityCanneryAssetProvider.ResourceFolder, CityCanneryAssetProvider.ModelNames,
                "CityCanneryAssetProvider COL_ collision / ConfigureYard CPU deformation", "CityCanneryAssetSetup.OnPreprocessModel");

            // Excluded deliberately: animation-only banks, scratch BakeMesh outputs, editor-normalized
            // Home/Stairwell mesh libraries, GPU-only passive kits and plan-owned primitive colliders.
            // No implicit prebake exception: every entry above has a current CPU/cooking consumer.
            var result = new Requirement[entries.Count];
            entries.Values.CopyTo(result, 0);
            return result;
        }
    }
}
