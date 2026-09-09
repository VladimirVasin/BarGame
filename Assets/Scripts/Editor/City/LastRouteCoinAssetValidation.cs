using UnityEditor;

namespace BarPromenade.Editor
{
    /// <summary>The build gate reads the same metre assets as both coin users.</summary>
    [InitializeOnLoad]
    internal static class LastRouteCoinAssetValidation
    {
        static LastRouteCoinAssetValidation()
        {
            PlayerBuildAssetValidation.Register("Last Route coin", LastRouteCoinAsset.ValidateOrThrow,
                "Run tools/build-last-route-coin-3d-model.py through tools/run-blender.py and reimport LastRouteCoin3D.fbx.");
        }
    }
}
