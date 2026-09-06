using UnityEngine;

namespace BarPromenade
{
    /// <summary>A short mouth-origin foam burst whose world-space flight ends on the actual basin.</summary>
    public sealed class HomeBrushingSpitEffect : MonoBehaviour
    {
        private sealed class Particle
        {
            public Transform Root;
            public MeshRenderer Renderer;
            public Vector3 Position, Velocity;
            public float Age;
            public bool Flying, Splash;
        }
        private readonly Particle[] drops = new Particle[20];
        private readonly Particle[] splashes = new Particle[12];
        private HomeInteriorRoot home;
        private HomeUrineSurfaceMap surfaces;
        private int nextDrop, nextSplash;
        private float remainder;
        private AudioSource voice;
        private static AudioClip spitClip;
        public int EmittedCount { get; private set; }
        public int BasinHitCount { get; private set; }
        public Vector3 LastMouth { get; private set; }
        public Vector3 LastImpact { get; private set; }
        public void Initialize(HomeInteriorRoot root)
        {
            home = root;
            for (int index = 0; index < drops.Length; index++) drops[index] = Create("Brushing Foam Drop", "Droplet");
            for (int index = 0; index < splashes.Length; index++) splashes[index] = Create("Brushing Foam Splash", "Splash");
            voice = new GameObject("Brushing Spit Voice").AddComponent<AudioSource>();
            voice.transform.SetParent(transform, false);
            voice.playOnAwake = false; voice.spatialBlend = 1f; voice.minDistance = 0.7f; voice.maxDistance = 4f;
            voice.rolloffMode = AudioRolloffMode.Linear; voice.volume = 0.3f;
            GameAudioMixer.Route(voice, GameAudioGroup.SfxWorld);
            if (spitClip == null) spitClip = CreateSpitClip();
            voice.clip = spitClip;
        }
        public void Begin()
        {
            Clear(); EmittedCount = BasinHitCount = 0; remainder = 0f;
            surfaces = new HomeUrineSurfaceMap(home.Room, transform);
        }
        public void EmitStep(Vector3 mouth, Vector3 target, float seconds)
        {
            if (seconds <= 0f || surfaces == null) return;
            if (EmittedCount == 0) { voice.transform.position = mouth; voice.Play(); }
            LastMouth = mouth;
            surfaces.Refresh();
            float first = (1f - remainder) / 38f;
            remainder += seconds * 38f;
            int count = Mathf.FloorToInt(remainder); remainder -= count;
            for (int index = 0; index < count; index++)
            {
                Particle drop = drops[nextDrop++ % drops.Length];
                float phase = (EmittedCount + 1) * 2.399963f;
                Vector3 contact = target + new Vector3(Mathf.Sin(phase), 0f, Mathf.Cos(phase)) * 0.009f;
                const float flight = 0.24f;
                drop.Position = mouth;
                drop.Velocity = (contact - mouth - Vector3.down * (0.5f * HomeUrineTrajectory.Gravity * flight * flight)) / flight;
                drop.Age = 0f; drop.Flying = true; drop.Renderer.enabled = true;
                drop.Root.localScale = new Vector3(0.007f, 0.007f, 0.013f);
                EmittedCount++;
                AdvanceDrop(drop, Mathf.Max(0f, seconds - first - index / 38f));
            }
        }
        private void Update()
        {
            if (surfaces == null) return;
            surfaces.Refresh();
            foreach (Particle drop in drops) if (drop.Flying) AdvanceDrop(drop, Time.deltaTime);
            foreach (Particle splash in splashes)
            {
                if (!splash.Splash) continue;
                splash.Age += Time.deltaTime;
                if (splash.Age >= 0.24f) { splash.Splash = false; splash.Renderer.enabled = false; }
                else splash.Root.localScale = Vector3.one * Mathf.Lerp(0.012f, 0.05f, splash.Age / 0.24f);
            }
        }
        private void AdvanceDrop(Particle drop, float seconds)
        {
            while (seconds > 0.000001f && drop.Flying)
            {
                float step = Mathf.Min(seconds, HomeUrineTrajectory.MaximumStep);
                Vector3 before = drop.Position;
                HomeUrineTrajectory.Advance(ref drop.Position, ref drop.Velocity, step);
                drop.Age += step; seconds -= step;
                if (surfaces.Cast(before, drop.Position, out var hit))
                {
                    LastImpact = hit.Point;
                    if (hit.Surface.Transform.name.StartsWith("Home Bathroom Sink")) BasinHitCount++;
                    Particle splash = splashes[nextSplash++ % splashes.Length];
                    splash.Splash = true; splash.Age = 0f; splash.Renderer.enabled = true;
                    splash.Root.SetPositionAndRotation(hit.Point + hit.Normal * 0.002f, Quaternion.LookRotation(hit.Normal));
                    splash.Root.localScale = Vector3.one * 0.012f;
                    drop.Flying = false;
                }
                if (drop.Age > 1f) drop.Flying = false;
            }
            drop.Renderer.enabled = drop.Flying;
            if (drop.Flying) drop.Root.SetPositionAndRotation(drop.Position, Quaternion.LookRotation(drop.Velocity));
        }
        private Particle Create(string name, string mesh)
        {
            var root = new GameObject(name); root.transform.SetParent(transform, false);
            root.AddComponent<MeshFilter>().sharedMesh = HomeBrushingResources.Mesh(mesh);
            MeshRenderer renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = HomeBrushingResources.Foam;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.enabled = false;
            return new Particle { Root = root.transform, Renderer = renderer };
        }
        private void Clear()
        {
            foreach (Particle drop in drops) if (drop != null) { drop.Flying = false; drop.Renderer.enabled = false; }
            foreach (Particle splash in splashes) if (splash != null) { splash.Splash = false; splash.Renderer.enabled = false; }
            if (voice != null) voice.Stop();
        }
        private static AudioClip CreateSpitClip()
        {
            const int rate = 22050;
            float[] samples = new float[(int)(rate * 0.24f)];
            uint seed = 4917; float low = 0f;
            for (int index = 0; index < samples.Length; index++)
            {
                seed = seed * 1664525u + 1013904223u;
                float noise = ((seed >> 8) / 16777215f) * 2f - 1f;
                low = Mathf.Lerp(low, noise, 0.14f);
                float t = index / (float)rate;
                float envelope = Mathf.Min(1f, t / 0.008f) * Mathf.Exp(-t * 22f);
                samples[index] = (low * 0.65f + Mathf.Sin(t * 2f * Mathf.PI * 145f) * 0.12f) * envelope;
            }
            AudioClip clip = AudioClip.Create("Home Brushing Spit", samples.Length, 1, rate, false);
            clip.SetData(samples, 0); return clip;
        }
        private void OnDisable() => Clear();
    }
}
