using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Independent import contract for the ordinary enemy; existing monster banks stay unchanged.</summary>
    public sealed class CombatZombieOrdinaryAssetSetup : AssetPostprocessor
    {
        private const string Prefix = "Assets/Resources/Combat/ZombieDecay";
        private bool IsBank => assetPath == Prefix + "Actions.fbx";
        private bool IsBody => assetPath.StartsWith(Prefix + "Body", StringComparison.Ordinal) && assetPath.EndsWith(".fbx", StringComparison.Ordinal);
        private bool IsModel => assetPath.StartsWith(Prefix, StringComparison.Ordinal) && assetPath.EndsWith(".fbx", StringComparison.Ordinal);
        public override uint GetVersion() => 2;
        private void OnPreprocessModel()
        {
            if (!IsModel || !(assetImporter is ModelImporter importer)) return;
            if (IsBody)
            {
                context.DependsOnArtifact(CombatAuthoredBodyAssetSetup.WorkerSource);
                CombatAuthoredBodyAssetSetup.ConfigureModel(importer);
                return;
            }
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.importCameras = importer.importLights = importer.addCollider = false;
            importer.importBlendShapes = !IsBank;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.isReadable = !IsBank;
            importer.importAnimation = IsBank;
            importer.animationType = IsBank ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            if (!IsBank) return;
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(Player3DV2AssetSetup.ModelPath).OfType<Avatar>().FirstOrDefault();
            importer.avatarSetup = avatar != null ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = avatar;
        }
        private void OnPostprocessModel(GameObject model)
        {
            if (IsBody) CombatAuthoredBodyAssetSetup.BindModel(model);
        }
        private void OnPreprocessAnimation()
        {
            if (!IsBank || !(assetImporter is ModelImporter importer)) return;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                int separator = clip.name.LastIndexOf('|');
                if (separator >= 0) clip.name = clip.name.Substring(separator + 1);
                if (Array.IndexOf(CombatZombieAssetProvider.DegradedClipNames, clip.name) < 0)
                    throw new InvalidOperationException("Unexpected recursive-body action: " + clip.name);
                clip.loopTime = CombatZombieAssetProvider.IsDegradedLoop(clip.name); clip.loopPose = false;
                clip.keepOriginalOrientation = clip.keepOriginalPositionXZ = clip.keepOriginalPositionY = true;
                clip.lockRootRotation = clip.lockRootPositionXZ = clip.lockRootHeightY = true;
                clip.events = Array.Empty<AnimationEvent>();
            }
            importer.clipAnimations = clips;
        }
    }
}
