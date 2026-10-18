using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Only the Room body uses this bounded authored import profile.</summary>
    public sealed class CombatRoomAssetSetup : AssetPostprocessor
    {
        public const string Folder = "Assets/Resources/Combat/";
        public const string BodyPath = Folder + "RoomBody.fbx";
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
            bool albedo = assetPath == Folder + "RoomBodyAtlas.png";
            if ((!albedo && assetPath != Folder + "RoomMaterialResponse.png") || !(assetImporter is TextureImporter importer)) return;
            CombatAuthoredBodyAssetSetup.ConfigureTexture(importer, albedo);
        }
        [MenuItem("Bar Promenade/Combat Test/Validate Room Assets")]
        public static void BuildOrThrow() => CombatAuthoredBodyAssetSetup.BuildOrThrow(CombatRoomAssetProvider.Bank);
        public static void ValidateImportedOrThrow() => CombatAuthoredBodyAssetSetup.ValidateImportedOrThrow(CombatRoomAssetProvider.Bank);
    }
}
