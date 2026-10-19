using System;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Optional spatial recordings stream as mono in the background, regardless of the supplied format.</summary>
    public sealed class CombatWarehouseAmbienceImporter : AssetPostprocessor
    {
        private const string ResourcesRoot = "Assets/Resources/";

        public override uint GetVersion() => 1;

        private void OnPreprocessAudio()
        {
            string resource = System.IO.Path.ChangeExtension(assetPath, null).Replace('\\', '/');
            if (!string.Equals(resource, ResourcesRoot + CombatWarehouseAmbiencePlayer.ClicksResourcePath,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(resource, ResourcesRoot + CombatWarehouseAmbiencePlayer.WhispersResourcePath,
                    StringComparison.OrdinalIgnoreCase)) return;
            if (!(assetImporter is AudioImporter importer)) return;
            importer.forceToMono = true;
            importer.loadInBackground = true;
            AudioImporterSampleSettings sample = importer.defaultSampleSettings;
            sample.loadType = AudioClipLoadType.Streaming;
            sample.compressionFormat = AudioCompressionFormat.Vorbis;
            sample.preloadAudioData = true;
            importer.defaultSampleSettings = sample;
            if (importer.ContainsSampleSettingsOverride(BuildTargetGroup.Standalone))
                importer.ClearSampleSettingOverride(BuildTargetGroup.Standalone);
        }
    }
}
