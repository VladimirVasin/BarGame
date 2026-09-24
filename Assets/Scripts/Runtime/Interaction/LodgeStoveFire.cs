using UnityEngine;

namespace BarPromenade
{
    /// <summary>One physical fire; shader, light and sound share its paused ignition clock.</summary>
    public sealed class LodgeStoveFire : MonoBehaviour
    {
        public const float GrowSeconds = 2.2f;
        private Renderer[] flames;
        private MaterialPropertyBlock properties;
        private AudioSource crackle;
        private AudioSource clicks;
        private AudioClip crackleClip;
        private AudioClip clickClip;
        private float elapsed;
        private bool audioPaused;
        public float Strength { get; private set; }
        public Light FireLight { get; private set; }
        public int ClickCount { get; private set; }

        public void Initialize(Transform dock)
        {
            properties = new MaterialPropertyBlock();
            transform.SetPositionAndRotation(dock.position, dock.rotation);
            var model = VillageExpansionAssetProvider.LoadOrThrow().Create(
                "StoveFire", "Stove Fire Layers", transform, dock.position, dock.rotation);
            flames = model.GetComponentsInChildren<Renderer>(true);
            FireLight = new GameObject("Stove Fire Light").AddComponent<Light>();
            FireLight.transform.SetParent(transform, false);
            FireLight.transform.localPosition = new Vector3(0f, .13f, -.2f);
            FireLight.type = LightType.Point;
            FireLight.color = new Color(1f, .43f, .13f);
            FireLight.range = 4.5f;
            FireLight.shadows = LightShadows.Soft;
            crackleClip = MothersHouseInteriorSoundSynthesis.CreateHearthRuntimeClip(
                GameSessionState.CitySeed, enclosedStove: true);
            crackle = Source("Stove Wood Crackle", crackleClip, true);
            clickClip = MakeClick();
            clicks = Source("Stove Lighter Click", clickClip, false);
            Strength = LodgeStoveSessionState.IsBurning ? 1f : 0f;
            Apply();
        }

        public void PlayClick()
        {
            ClickCount++;
            clicks.PlayOneShot(clickClip, .52f);
        }

        public void ResetClickCount() => ClickCount = 0;

        private void Update()
        {
            if (flames == null) return;
            bool paused = GameTimeScaleRuntime.IsPaused || SceneTransitionService.IsTransitioning;
            if (paused != audioPaused)
            {
                audioPaused = paused;
                if (paused) { crackle.Pause(); clicks.Pause(); }
                else { crackle.UnPause(); clicks.UnPause(); }
            }
            if (paused) return;
            elapsed += Time.deltaTime;
            Strength = Mathf.MoveTowards(Strength, LodgeStoveSessionState.IsBurning ? 1f : 0f,
                Time.deltaTime / GrowSeconds);
            Apply();
        }

        private void Apply()
        {
            float flicker = 1f + Mathf.Sin(elapsed * 7.1f) * .065f + Mathf.Sin(elapsed * 13.7f) * .04f;
            foreach (Renderer flame in flames)
            {
                flame.enabled = Strength > .001f;
                flame.GetPropertyBlock(properties);
                properties.SetFloat("_FireTime", elapsed);
                properties.SetFloat("_FireStrength", Strength * flicker);
                properties.SetFloat("_Opacity", .84f * Strength);
                properties.SetFloat("_FirePhase", flame.name == "FlameBack" ? 0f : 2.19f);
                flame.SetPropertyBlock(properties);
                // Grow from the authored roots without changing their location.
                Vector3 scale = flame.transform.localScale;
                scale.y = flame.transform.localScale.x * Mathf.Lerp(.16f, 1f, Strength);
                flame.transform.localScale = scale;
            }
            FireLight.enabled = Strength > .001f;
            FireLight.intensity = 12f * Strength * flicker;
            crackle.volume = .24f * Strength;
            if (Strength > .001f && !crackle.isPlaying && !audioPaused) crackle.Play();
            else if (Strength <= .001f) crackle.Stop();
        }

        private AudioSource Source(string name, AudioClip clip, bool loop)
        {
            var source = new GameObject(name).AddComponent<AudioSource>();
            source.transform.SetParent(transform, false);
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 1.2f;
            source.maxDistance = 9f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.dopplerLevel = 0f;
            GameAudioMixer.Route(source, GameAudioGroup.SfxWorld);
            return source;
        }

        private static AudioClip MakeClick()
        {
            const int rate = 22050;
            var samples = new float[(int)(rate * .085f)];
            uint seed = 19371;
            for (int i = 0; i < samples.Length; i++)
            {
                seed = seed * 1664525u + 1013904223u;
                float noise = ((seed >> 8) & 65535) / 32767.5f - 1f;
                float t = i / (float)rate;
                float envelope = Mathf.Exp(-t * 75f) + .4f * Mathf.Exp(-Mathf.Abs(t - .025f) * 350f);
                samples[i] = (noise * .65f + Mathf.Sin(t * 18200f) * .22f) * envelope;
            }
            var clip = AudioClip.Create("Stove flint wheel", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDisable() { crackle?.Stop(); clicks?.Stop(); }
        private void OnDestroy()
        {
            if (crackleClip != null) Destroy(crackleClip);
            if (clickClip != null) Destroy(clickClip);
        }
    }
}
