using UnityEngine;

namespace BarPromenade.Rendering
{
    public enum Ps1ResolutionPreset
    {
        Readable426x240,
        Authentic320x180,
        Balanced640x360
    }

    [CreateAssetMenu(
        fileName = "Ps1PresentationProfile",
        menuName = "Bar Promenade/PS1 Presentation Profile")]
    public sealed class Ps1PresentationSettings : ScriptableObject
    {
        [SerializeField] private bool effectEnabled = true;
        [SerializeField] private Ps1ResolutionPreset resolutionPreset =
            Ps1ResolutionPreset.Balanced640x360;
        [SerializeField, Range(0f, 1f)]
        private float quantizationStrength = 0.35f;
        [SerializeField] private bool ditherEnabled = true;
        [SerializeField, Range(0f, 1f)]
        private float ditherStrength = 0.6f;
        [SerializeField, Range(0f, 0.6f)]
        private float scanlineIntensity = 0.22f;
        [SerializeField] private bool rainLensEnabled = true;
        [SerializeField, Range(0f, 1f)]
        private float rainLensStrength = 0.8f;

        public bool EffectEnabled => effectEnabled;
        public Ps1ResolutionPreset ResolutionPreset => resolutionPreset;
        public float QuantizationStrength =>
            Mathf.Clamp01(quantizationStrength);
        public float DitherStrength =>
            ditherEnabled ? Mathf.Clamp01(ditherStrength) : 0f;
        public float ScanlineIntensity =>
            Mathf.Clamp(scanlineIntensity, 0f, 0.6f);
        public float RainLensStrength =>
            rainLensEnabled ? Mathf.Clamp01(rainLensStrength) : 0f;

        public Vector2Int GetInternalResolution(int outputWidth, int outputHeight)
        {
            return CalculateInternalResolution(
                outputWidth,
                outputHeight,
                resolutionPreset);
        }

        public static Vector2Int CalculateInternalResolution(
            int outputWidth,
            int outputHeight,
            Ps1ResolutionPreset preset)
        {
            Vector2Int bounds;
            switch (preset)
            {
                case Ps1ResolutionPreset.Authentic320x180:
                    bounds = new Vector2Int(320, 180);
                    break;
                case Ps1ResolutionPreset.Readable426x240:
                    bounds = new Vector2Int(426, 240);
                    break;
                default:
                    bounds = new Vector2Int(640, 360);
                    break;
            }

            if (outputWidth <= 0 || outputHeight <= 0)
            {
                return bounds;
            }

            float outputAspect = outputWidth / (float)outputHeight;
            float boundsAspect = bounds.x / (float)bounds.y;
            int width;
            int height;

            if (outputAspect >= boundsAspect)
            {
                width = bounds.x;
                height = Mathf.Max(1, Mathf.RoundToInt(width / outputAspect));
            }
            else
            {
                height = bounds.y;
                width = Mathf.Max(1, Mathf.RoundToInt(height * outputAspect));
            }

            return new Vector2Int(width, height);
        }
    }
}
