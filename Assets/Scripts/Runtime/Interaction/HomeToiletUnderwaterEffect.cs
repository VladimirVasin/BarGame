using UnityEngine;
using UnityEngine.Audio;

namespace BarPromenade
{
    /// <summary>Camera-scoped optics, world-bus absorption and the sound of crossing the surface.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1100)]
    public sealed class HomeToiletUnderwaterEffect : MonoBehaviour
    {
        public const string CutoffParameter = "BowlWaterCutoffHz";
        public const string GainParameter = "BowlWaterGainDb";
        public const float SubmergedCutoffHz = 420f;
        public const float SubmergedGainDb = -5f;
        private AudioMixer mixer;
        private AudioSource entryVoice, submergedVoice;
        private float previousCutoff, previousGain;
        private float previousCameraY;
        private bool ownsAudio;
        private Renderer surface;
        private MaterialPropertyBlock properties;
        private float waterHeight;
        private static readonly int ClockId = Shader.PropertyToID("_BowlWaterClock");
        private static readonly int VortexId = Shader.PropertyToID("_BowlVortex");
        public bool IsActive { get; private set; }
        public float Amount { get; private set; }
        public float AudioAmount { get; private set; }
        public float Clock { get; private set; }
        public float VortexStrength { get; private set; }
        public float VortexAngle { get; private set; }
        public bool IsAudioConfigured => ownsAudio;
        public int EntryPlayCount { get; private set; }
        public AudioSource EntryVoice => entryVoice;
        public AudioSource SubmergedVoice => submergedVoice;

        public bool Prepare()
        {
            mixer = GameAudioMixer.Mixer;
            if (Resources.Load<Shader>("Shaders/HomeToiletUnderwater") == null || mixer == null ||
                !mixer.GetFloat(CutoffParameter, out _) || !mixer.GetFloat(GainParameter, out _))
                return false;
            if (entryVoice == null) entryVoice = CreateVoice("Bowl Water Entry", HomeToiletWaterAudioResources.Entry, false);
            if (submergedVoice == null) submergedVoice = CreateVoice("Bowl Water Interior", HomeToiletWaterAudioResources.Submerged, true);
            return true;
        }

        private AudioSource CreateVoice(string name, AudioClip clip, bool loop)
        {
            var carrier = new GameObject(name);
            carrier.transform.SetParent(transform, false);
            AudioSource voice = carrier.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.loop = loop;
            voice.clip = clip;
            voice.spatialBlend = 0f; // The sound belongs to the camera crossing the water, not the hero.
            voice.dopplerLevel = 0f;
            voice.bypassReverbZones = true;
            voice.volume = 0f;
            GameAudioMixer.Route(voice, GameAudioGroup.SfxGameplay);
            return voice;
        }

        public void Begin(Transform home, Transform water)
        {
            End();
            if (!Prepare()) return;
            mixer.GetFloat(CutoffParameter, out previousCutoff);
            mixer.GetFloat(GainParameter, out previousGain);
            ownsAudio = true;
            waterHeight = water.position.y;
            previousCameraY = float.PositiveInfinity;
            EntryPlayCount = 0;
            IsActive = true;
            surface = water.GetComponentInChildren<Renderer>(true);
            properties ??= new MaterialPropertyBlock();
            submergedVoice.Play();
        }

        public void Present(float cameraY, float clock)
        {
            if (!IsActive) return;
            Amount = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.012f, -.012f, cameraY - waterHeight));
            if (EntryPlayCount == 0 && previousCameraY > waterHeight && cameraY <= waterHeight)
            {
                entryVoice.volume = .48f;
                entryVoice.Play();
                EntryPlayCount++;
            }
            previousCameraY = cameraY;
            Clock = clock;
            if (surface != null)
            {
                surface.GetPropertyBlock(properties);
                properties.SetFloat(ClockId, Clock);
                surface.SetPropertyBlock(properties);
            }
        }

        public void PresentVortex(float strength, float angle)
        {
            if (!IsActive) return;
            VortexStrength = Mathf.Clamp01(strength);
            VortexAngle = angle;
            if (surface == null) return;
            surface.GetPropertyBlock(properties);
            properties.SetVector(VortexId, new Vector4(VortexStrength, VortexAngle, 0f, 0f));
            surface.SetPropertyBlock(properties);
        }

        private void LateUpdate()
        {
            if (!IsActive) return;
            // Log-frequency interpolation makes the whole crossing audible, rather than
            // leaving almost the entire transition above the useful hearing range.
            float response = Amount > AudioAmount ? .08f : .16f;
            float deltaTime = AudioListener.pause ? 0f : Time.deltaTime;
            AudioAmount = Mathf.Lerp(AudioAmount, Amount, 1f - Mathf.Exp(-deltaTime / response));
            float cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(10f, previousCutoff)),
                Mathf.Log(Mathf.Min(previousCutoff, SubmergedCutoffHz)), AudioAmount));
            mixer.SetFloat(CutoffParameter, cutoff);
            mixer.SetFloat(GainParameter, previousGain + SubmergedGainDb * AudioAmount);
            submergedVoice.volume = (.22f + .1f * VortexStrength) * AudioAmount;
        }

        public void End()
        {
            IsActive = false;
            Amount = AudioAmount = Clock = 0f;
            VortexStrength = VortexAngle = 0f;
            if (ownsAudio && mixer != null)
            {
                mixer.SetFloat(CutoffParameter, previousCutoff);
                mixer.SetFloat(GainParameter, previousGain);
                // These two controls have one scoped owner. Return ownership to the
                // scene snapshots, including a transition that interrupted the episode.
                mixer.ClearFloat(CutoffParameter);
                mixer.ClearFloat(GainParameter);
            }
            ownsAudio = false;
            StopVoice(entryVoice);
            StopVoice(submergedVoice);
            if (surface != null && properties != null)
            {
                surface.GetPropertyBlock(properties);
                properties.SetFloat(ClockId, 0f);
                properties.SetVector(VortexId, Vector4.zero);
                surface.SetPropertyBlock(properties);
            }
            surface = null;
        }
        private static void StopVoice(AudioSource voice)
        {
            if (voice == null) return;
            voice.Stop();
            voice.volume = 0f;
        }
        private void OnDisable() => End();
        private void OnDestroy()
        {
            End();
            if (entryVoice != null) Destroy(entryVoice.gameObject);
            if (submergedVoice != null) Destroy(submergedVoice.gameObject);
        }
    }
}
