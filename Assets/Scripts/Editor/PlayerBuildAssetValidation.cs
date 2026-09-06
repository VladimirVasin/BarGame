using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Audio;

namespace BarPromenade.Editor
{
    /// <summary>
    /// A read-only gate shared by Build Profiles, BuildPipeline and batch builds.
    /// Repair commands are explicit: a player build never regenerates source assets.
    /// </summary>
    public sealed class PlayerBuildAssetValidation : IPreprocessBuildWithReport
    {
        private sealed class Check
        {
            public readonly Action Validate;
            public readonly string Repair;

            public Check(Action validate, string repair)
            {
                Validate = validate;
                Repair = repair;
            }
        }

        private static readonly SortedDictionary<string, Check> Checks =
            new SortedDictionary<string, Check>(StringComparer.Ordinal);

        public int callbackOrder => -1000;

        static PlayerBuildAssetValidation()
        {
            Register("Core runtime resources", ValidateCoreResources,
                "Restore the missing source asset and its .meta from version control.");
            Register("Area loading illustrations", AreaLoadingArtImporter.ValidateOrThrow,
                "Restore the four loading PNGs, then run BarPromenade.Editor.AreaLoadingArtImporter.ReimportAll.");
            Register("Hero V2", ValidateHero,
                "BarPromenade.Editor.Player3DV2AssetSetup.RunBatch");
            RegisterGenerated("Bar", BarAssetSetup.ValidateOrThrow, "BarAssetSetup");
            RegisterGenerated("Bartender", BarBartenderV2AssetSetup.ValidateOrThrow, "BarBartenderV2AssetSetup");
            RegisterGenerated("City buildings", CityBuildingAssetSetup.ValidateOrThrow, "CityBuildingAssetSetup");
            RegisterGenerated("City misc", CityMiscAssetSetup.ValidateOrThrow, "CityMiscAssetSetup");
            RegisterGenerated("Church", ChurchAssetSetup.ValidateOrThrow, "ChurchAssetSetup");
            RegisterGenerated("Player home exterior", PlayerHomeExteriorAssetSetup.ValidateOrThrow, "PlayerHomeExteriorAssetSetup");
            RegisterGenerated("Supermarket exterior", SupermarketExteriorAssetSetup.ValidateOrThrow, "SupermarketExteriorAssetSetup");
            RegisterGenerated("Supermarket interior", SupermarketInteriorAssetSetup.ValidateOrThrow, "SupermarketInteriorAssetSetup");
            RegisterGenerated("Supermarket products", SupermarketProductAssetSetup.ValidateOrThrow, "SupermarketProductAssetSetup");
            RegisterGenerated("Supermarket cashier", SupermarketCashierAssetSetup.ValidateOrThrow, "SupermarketCashierAssetSetup");
            RegisterGenerated("Mountain cafe", MountainRoadCafeAssetSetup.ValidateOrThrow, "MountainRoadCafeAssetSetup");
            RegisterGenerated("Mountain misc", MountainRoadMiscAssetSetup.ValidateOrThrow, "MountainRoadMiscAssetSetup");
            RegisterGenerated("Village", VillageAssetSetup.ValidateOrThrow, "VillageAssetSetup");
            RegisterGenerated("Village facade textures", VillageFacadeTextureSetup.ValidateOrThrow, "VillageFacadeTextureSetup");
            RegisterGenerated("Village rocks", VillageRockAssetSetup.ValidateOrThrow, "VillageRockAssetSetup");
            RegisterGenerated("Upper cableway canopy", UpperCablewayCanopyAssetSetup.ValidateOrThrow, "UpperCablewayCanopyAssetSetup");
            RegisterGenerated("Mother's house", MothersHouseInteriorAssetSetup.ValidateOrThrow, "MothersHouseInteriorAssetSetup");
            RegisterGenerated("Mother", MothersHouseMotherAssetSetup.ValidateOrThrow, "MothersHouseMotherAssetSetup");
            RegisterGenerated("Pedestrians", CityPedestrianAssetSetup.ValidateOrThrow, "CityPedestrianAssetSetup");
            RegisterGenerated("Shelter residents", CityArchShelterResidentAssetSetup.ValidateOrThrow, "CityArchShelterResidentAssetSetup");
            RegisterGenerated("Cafe cast", MountainRoadCafeCastAssetSetup.ValidateOrThrow, "MountainRoadCafeCastAssetSetup");
            RegisterGenerated("Hand props", CityPedestrianHandPropAssetSetup.ValidateOrThrow, "CityPedestrianHandPropAssetSetup");
            RegisterGenerated("Bus", CityBusAssetSetup.ValidateOrThrow, "CityBusAssetSetup");
            RegisterGenerated("Bus driver", CityBusDriverAssetSetup.ValidateOrThrow, "CityBusDriverAssetSetup");
            Register("Last Route car", BarPromenade.EditorTools.LastRouteCarAssetSetup.ValidateOrThrow,
                "BarPromenade.EditorTools.LastRouteCarAssetSetup.BuildOrThrow");
            RegisterGenerated("Raven", CemeteryRavenAssetSetup.ValidateOrThrow, "CemeteryRavenAssetSetup");
            RegisterGenerated("Stairwell cat", StairwellCatAssetSetup.ValidateOrThrow, "StairwellCatAssetSetup");
            RegisterGenerated("Exterior cloud", ExteriorCloudAssetSetup.ValidateOrThrow, "ExteriorCloudAssetSetup");
        }

        /// <summary>Register an additional read-only owner validator at Editor initialization.</summary>
        public static void Register(string name, Action validate, string repair)
        {
            if (string.IsNullOrWhiteSpace(name) || validate == null ||
                string.IsNullOrWhiteSpace(repair))
            {
                throw new ArgumentException("An asset check needs a name, validator and repair instruction.");
            }

            Checks.Add(name, new Check(validate, repair));
        }

        private static void RegisterGenerated(string name, Action validate, string setup)
        {
            Register(name, validate, $"BarPromenade.Editor.{setup}.BuildOrThrow");
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            ValidateOrThrow();
        }

        [MenuItem("Bar Promenade/Build/Validate Runtime Assets (Read Only)")]
        public static void ValidateOrThrow()
        {
            var labels = new List<string>();
            var validators = new List<Action>();
            foreach (KeyValuePair<string, Check> pair in Checks)
            {
                labels.Add($"{pair.Key}. Repair: {pair.Value.Repair}");
                validators.Add(pair.Value.Validate);
            }

            string[] failures = CollectFailures(labels, validators);
            if (failures.Length != 0)
            {
                throw new BuildFailedException(
                    "Runtime asset validation failed. No assets were regenerated. " +
                    "Run the named setup method explicitly after correcting its sources, then rebuild.\n" +
                    string.Join("\n", failures));
            }
        }

        // Keeping execution separate from registration also makes aggregation testable
        // without importing, loading or rebuilding the entire production catalogue.
        public static string[] CollectFailures(IReadOnlyList<string> labels, IReadOnlyList<Action> validators)
        {
            if (labels == null || validators == null || labels.Count != validators.Count)
            {
                throw new ArgumentException("Asset validation labels and callbacks must correspond.");
            }

            var failures = new List<string>();
            for (int index = 0; index < validators.Count; index++)
            {
                try
                {
                    validators[index]();
                }
                catch (Exception exception)
                {
                    Exception cause = exception is TargetInvocationException invocation &&
                        invocation.InnerException != null ? invocation.InnerException : exception;
                    failures.Add($"- {labels[index]}: {cause.Message}");
                }
            }

            return failures.ToArray();
        }

        public static void ValidateStamp(string assetPath, string actual, string expected)
        {
            if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(expected) ||
                !string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Generated asset '{assetPath}' is stale or has no dependency stamp " +
                    $"(stored '{actual}', expected '{expected}').");
            }
        }

        public static void ValidateHero()
        {
            foreach (string source in new[]
                     {
                         Player3DV2AssetSetup.ModelPath, Player3DV2AssetSetup.ManifestPath,
                         Player3DV2AssetSetup.AnimationPath, Player3DV2AssetSetup.AtlasPath,
                         Player3DV2AssetSetup.ClothingAtlasPath, Player3DV2AssetSetup.BareSkinAtlasPath,
                         Player3DV2AssetSetup.PortraitPath, Player3DV2AssetSetup.MaterialPath,
                         Player3DV2AssetSetup.ClothingMaterialPath
                     })
            {
                RequireAsset<UnityEngine.Object>(source);
            }

            GameObject prefab = RequireAsset<GameObject>(Player3DV2AssetSetup.PrefabPath);
            Player3DAssetRegistry registry = prefab.GetComponent<Player3DAssetRegistry>();
            if (registry == null || registry.Animator == null || registry.ModelRoot == null ||
                registry.Animator.avatar == null || !registry.HasFaceAtlas ||
                registry.Renderers.Count == 0 || registry.Animations.Count == 0)
            {
                throw new InvalidOperationException("Hero V2 prefab has missing production rig bindings.");
            }

            // Reuse the existing hash algorithm, including importer/setup dependencies.
            // This narrow adapter avoids duplicating its list or changing that stamped
            // setup file merely to expose a read-only method (and staling every prefab).
            MethodInfo signature = typeof(Player3DV2AssetSetup).GetMethod(
                "CalculateBuildSignature", BindingFlags.NonPublic | BindingFlags.Static);
            if (signature == null || signature.ReturnType != typeof(string) ||
                signature.GetParameters().Length != 0)
            {
                throw new InvalidOperationException("Hero V2 dependency signature API changed; update the prebuild adapter.");
            }

            ValidateStamp(Player3DV2AssetSetup.PrefabPath, registry.BuildSignature,
                (string)signature.Invoke(null, null));
        }

        private static void ValidateCoreResources()
        {
            RequireAsset<Material>("Assets/Resources/Materials/RuntimePrimitiveLit.mat");
            RequireAsset<Material>("Assets/Resources/Materials/Ps1Composite.mat");
            RequireAsset<BarPromenade.Rendering.Ps1PresentationSettings>("Assets/Resources/Rendering/Ps1PresentationProfile.asset");
            RequireAsset<AudioMixer>("Assets/Resources/Audio/Mixers/BarPromenadeAudio.mixer");
            RequireAsset<Font>("Assets/Resources/Fonts/Roboto-Regular.ttf");
            RequireAsset<TextAsset>("Assets/Resources/Localization/ru.json");
            RequireAsset<TextAsset>("Assets/Resources/Localization/en.json");
        }

        private static T RequireAsset<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new InvalidOperationException($"Required {typeof(T).Name} is missing: '{path}'.");
            }

            return asset;
        }
    }
}
