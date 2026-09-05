using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// One modest garden fountain. Imported meshes own its water geometry;
    /// the existing City water materials own flow, rain and time of day.
    /// Its sole local voice is tied to the visible falling stream.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChurchGardenFountain : MonoBehaviour
    {
        public const float AudibleRadius = 4.5f;
        private AudioClip clip;
        public AudioSource Voice { get; private set; }

        public void Initialize(GameObject water, GameObject stream)
        {
            var basinProperties = new MaterialPropertyBlock();
            basinProperties.SetFloat("_WaveHeight", 0.004f);
            basinProperties.SetFloat("_WaveLength", 0.65f);
            basinProperties.SetFloat("_ReflectionStrength", 0.12f);
            basinProperties.SetFloat("_FlowSpeed", 0.07f);
            foreach (Renderer renderer in water.GetComponentsInChildren<Renderer>())
            {
                Configure(renderer, CityFountainWaterResources.BasinMaterial);
                renderer.SetPropertyBlock(basinProperties);
            }
            foreach (Renderer renderer in stream.GetComponentsInChildren<Renderer>())
                Configure(renderer, CityFountainWaterResources.StreamMaterial);

            var sourceObject = new GameObject("Garden Fountain Trickle");
            sourceObject.transform.SetParent(transform, false);
            sourceObject.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            Voice = sourceObject.AddComponent<AudioSource>();
            Voice.playOnAwake = false;
            Voice.loop = true;
            Voice.spatialBlend = 1f;
            Voice.rolloffMode = AudioRolloffMode.Linear;
            Voice.minDistance = 0.5f;
            Voice.maxDistance = AudibleRadius;
            Voice.dopplerLevel = 0f;
            Voice.spread = 0f;
            Voice.priority = 170;
            Voice.volume = 0.075f;
            clip = CitySourceSoundSynthesis.CreateRuntimeClip(CitySourceSoundId.ParkFountainLoop);
            Voice.clip = clip;
            GameAudioMixer.Route(Voice, GameAudioGroup.AmbienceDetails);
            sourceObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = 3200f;
            Voice.Play();
        }

        private static void Configure(Renderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void OnEnable()
        {
            if (Voice != null && !Voice.isPlaying) Voice.Play();
        }

        private void OnDisable()
        {
            if (Voice != null) Voice.Stop();
        }

        private void OnDestroy()
        {
            if (clip == null) return;
            if (Application.isPlaying) Destroy(clip);
            else DestroyImmediate(clip);
        }
    }
}
