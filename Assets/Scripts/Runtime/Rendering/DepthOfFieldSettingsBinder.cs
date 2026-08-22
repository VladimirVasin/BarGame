using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Rendering
{
    /// <summary>
    /// Keeps a volume profile's DepthOfField override in sync with the
    /// player-facing graphics toggle. Attach next to each volume owner
    /// and hand it the runtime profile (or the volume's profile clone,
    /// never the shared asset).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DepthOfFieldSettingsBinder : MonoBehaviour
    {
        private DepthOfField depthOfField;
        private int appliedVersion = -1;

        public bool IsInitialized => depthOfField != null;

        public void Initialize(VolumeProfile profile)
        {
            if (profile == null ||
                !profile.TryGet(out depthOfField))
            {
                depthOfField = null;
                return;
            }

            Apply();
        }

        private void Update()
        {
            if (depthOfField == null ||
                appliedVersion == GraphicsEffectsSettings.Version)
            {
                return;
            }

            Apply();
        }

        private void Apply()
        {
            appliedVersion = GraphicsEffectsSettings.Version;
            depthOfField.active =
                GraphicsEffectsSettings.DepthOfFieldEnabled;
        }
    }
}
