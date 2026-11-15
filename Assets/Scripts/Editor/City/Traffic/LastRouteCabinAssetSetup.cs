using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static class LastRouteCabinAssetSetup
    {
        internal const string DriverPath = "Assets/Resources/" + LastRouteFerrymanRigAnchors.DriverBank + ".fbx";
        internal const string PassengerPath = "Assets/Resources/" + LastRouteFerrymanRigAnchors.PassengerBank + ".fbx";

        [MenuItem("Bar Promenade/Last Route Car 3D/Rebuild Car and Driver Bindings")]
        public static void BuildOrThrow()
        {
            DefaultNpcAssetSetup.BuildOrThrow();
            AssetDatabase.ImportAsset(PassengerPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(DriverPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            EditorTools.LastRouteCarAssetSetup.BuildOrThrow();
            ValidateOrThrow();
            Debug.Log("Last Route sedan, default NPC wardrobe and cabin motion imported.");
        }

        public static void ValidateOrThrow()
        {
            ValidateBank(DriverPath, 5);
            ValidateBank(PassengerPath, 3);
            var source = DefaultNpcCatalog.GetPrefab(DefaultNpcCatalog.OrdinaryWorker).GetComponent<NpcWardrobe>();
            var coat = source.Items.SingleOrDefault(item => item.Id == DefaultNpcPopulation.FerrymanCoatItem);
            if (coat == null || coat.Parts.Count(part => part.Renderer.name.StartsWith("CLO_FerrymanCoatHem.", StringComparison.Ordinal)) != 2)
                throw new InvalidOperationException("Last Route's skinned coat is missing from its default wardrobe.");
        }

        private static void ValidateBank(string path, int expectedCount)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            var source = AssetDatabase.LoadAssetAtPath<TextAsset>(path.Replace(".fbx", ".json"));
            Bank bank = source != null ? JsonUtility.FromJson<Bank>(source.text) : null;
            if (bank == null || bank.signature?.Length != 64 || bank.clips?.Length != expectedCount ||
                asset == null || asset.GetComponentInChildren<Renderer>(true) != null || clips.Length != expectedCount)
                throw new InvalidOperationException($"The Last Route animation bank at {path} has an invalid import.");

            foreach (Take take in bank.clips)
            {
                AnimationClip clip = clips.SingleOrDefault(candidate => candidate.name == take.name);
                if (clip == null || clip.events.Length != 0 || clip.isLooping != take.loop ||
                    Mathf.Abs(clip.length - take.seconds) > .005f)
                    throw new InvalidOperationException($"Missing or invalid Last Route action {take.name} at {path}.");
            }
        }

        [Serializable]
        private sealed class Bank { public string signature; public Take[] clips; }

        [Serializable]
        private sealed class Take { public string name; public float seconds; public bool loop; }
    }

    public sealed class LastRouteCabinActionImporter : AssetPostprocessor
    {
        private bool IsBank => assetPath == LastRouteCabinAssetSetup.DriverPath ||
                               assetPath == LastRouteCabinAssetSetup.PassengerPath;

        private void OnPreprocessModel()
        {
            if (!IsBank || !(assetImporter is ModelImporter importer)) return;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(Player3DV2AssetSetup.ModelPath).OfType<Avatar>().FirstOrDefault();
            importer.avatarSetup = avatar != null
                ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = avatar;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsBank || !(assetImporter is ModelImporter importer)) return;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                int separator = clip.name.LastIndexOf('|');
                if (separator >= 0) clip.name = clip.name.Substring(separator + 1);
                clip.loopTime = clip.name is "FerrymanWait" or "FerrymanDrive" or "FerrymanTrudge" or
                    "LastRoutePassengerLoop";
                clip.loopPose = false;
            }
            importer.clipAnimations = clips;
        }
    }
}
