using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Only the Imprint body uses this bounded authored import profile.</summary>
    public sealed class CombatImprintAssetSetup : AssetPostprocessor
    {
        public const string Folder = "Assets/Resources/Combat/";
        public const string BodyPath = Folder + "ImprintBody.fbx";
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
            bool albedo = assetPath == Folder + "ImprintBodyAtlas.png";
            if ((!albedo && assetPath != Folder + "ImprintMaterialResponse.png") || !(assetImporter is TextureImporter importer)) return;
            CombatAuthoredBodyAssetSetup.ConfigureTexture(importer, albedo);
        }
        [MenuItem("Bar Promenade/Combat Test/Validate Imprint Assets")]
        public static void BuildOrThrow() => CombatAuthoredBodyAssetSetup.BuildOrThrow(CombatImprintAssetProvider.Bank);
        public static void ValidateImportedOrThrow() => CombatAuthoredBodyAssetSetup.ValidateImportedOrThrow(CombatImprintAssetProvider.Bank);
    }
}
