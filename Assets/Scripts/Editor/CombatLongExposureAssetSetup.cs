using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Long Exposure's own retained-skeleton model and texture import profile.</summary>
    public sealed class CombatLongExposureAssetSetup : AssetPostprocessor
    {
        public const string Folder = "Assets/Resources/Combat/";
        public const string BodyPath = Folder + "LongExposureBody.fbx";
        public override uint GetVersion() => 1;
        private void OnPreprocessModel()
        {
            if (assetPath != BodyPath || !(assetImporter is ModelImporter importer)) return;
            context.DependsOnArtifact(CombatAuthoredBodyAssetSetup.WorkerSource);
            CombatAuthoredBodyAssetSetup.ConfigureModel(importer);
        }
        private void OnPostprocessModel(GameObject model)
        {
            if (assetPath == BodyPath) CombatAuthoredBodyAssetSetup.BindModel(model);
        }
        private void OnPreprocessTexture()
        {
            bool albedo = assetPath == Folder + "LongExposureBodyAtlas.png";
            if ((!albedo && assetPath != Folder + "LongExposureMaterialResponse.png") || !(assetImporter is TextureImporter importer)) return;
            CombatAuthoredBodyAssetSetup.ConfigureTexture(importer, albedo);
        }
        [MenuItem("Bar Promenade/Combat Test/Validate Long Exposure Assets")]
        public static void BuildOrThrow() => CombatAuthoredBodyAssetSetup.BuildOrThrow(CombatLongExposureAssetProvider.Bank);
        public static void ValidateImportedOrThrow() => CombatAuthoredBodyAssetSetup.ValidateImportedOrThrow(CombatLongExposureAssetProvider.Bank);
    }
}
