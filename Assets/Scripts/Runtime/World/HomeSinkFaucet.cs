using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Authored sink hardware and preallocated causal water, driven by the hand's valve turn.</summary>
    [DisallowMultipleComponent]
    public sealed class HomeSinkFaucet : MonoBehaviour
    {
        public const float OpenDegrees = -70f;
        private const int StreamParts = 7;
        private const int SplashParts = 4;
        private readonly MeshRenderer[] stream = new MeshRenderer[StreamParts];
        private readonly MeshRenderer[] splashes = new MeshRenderer[SplashParts];
        private Transform grip;
        private Transform outlet;
        private Vector3 impact;
        private AudioSource waterSource;
        private static AudioClip waterClip;
        private float elapsed;

        public Transform Handle { get; private set; }
        public Vector3 GripPosition => grip != null ? grip.position : transform.position;
        public Quaternion GripRotation => grip != null ? grip.rotation : transform.rotation;
        public Vector3 OutletPosition => outlet != null ? outlet.position : transform.position;
        public Vector3 WaterImpactPosition => transform.TransformPoint(impact);
        public float OpenAmount { get; private set; }
        public AudioSource WaterSource => waterSource;

        public static HomeSinkFaucet Create(Transform room, Vector3 sinkCenter)
        {
            var root = new GameObject("Home Bathroom Sink Tap");
            root.transform.SetParent(room, false);
            root.transform.localPosition = sinkCenter;
            HomeSinkFaucet faucet = root.AddComponent<HomeSinkFaucet>();
            faucet.Initialize();
            return faucet;
        }

        private void Initialize()
        {
            Vector3 deck = new Vector3(0f, 0.88f, 0.10f);
            MeshRenderer body = CreateMesh("Home Bathroom Sink Tap Body", "FaucetBody", transform);
            body.transform.localPosition = deck;
            ApplyMetal(body, new Color(0.40f, 0.42f, 0.38f));
            Handle = CreateValve("Home Bathroom Sink Tap Handle", transform,
                deck + HomeBrushingResources.Anchor("FaucetBody", "HandlePivot"), new Color(0.55f, 0.56f, 0.49f));
            grip = new GameObject("Sink Tap Hand Grip").transform;
            grip.SetParent(Handle, false);
            grip.localPosition = HomeBrushingResources.Anchor("FaucetHandle", "HandGrip");
            // Left palm down on the valve: fingers forward, thumb inward.
            grip.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.right);
            outlet = new GameObject("Sink Tap Water Outlet").transform;
            outlet.SetParent(transform, false);
            outlet.localPosition = deck + HomeBrushingResources.Anchor("FaucetBody", "WaterOutlet");
            MeshRenderer aerator = CreateMesh("Home Bathroom Sink Tap Aerator", "FaucetOutlet", outlet);
            ApplyMetal(aerator, new Color(0.16f, 0.19f, 0.16f));
            // Authored ray-cast against SinkBasin triangles follows the actual
            // outlet XZ. A 2 mm surface offset prevents splash z-fighting.
            impact = deck + HomeBrushingResources.Anchor("FaucetBody", "WaterImpact") + Vector3.up * .002f;
            for (int index = 0; index < stream.Length; index++)
                stream[index] = CreateWater("Sink Tap Water Stream " + index, "WaterStream");
            for (int index = 0; index < splashes.Length; index++)
                splashes[index] = CreateWater("Sink Tap Water Splash " + index, "Splash");

            // A separate leaf keeps AudioSource out of the reflected mesh subtree.
            waterSource = new GameObject("Sink Tap Water Sound").AddComponent<AudioSource>();
            waterSource.transform.SetParent(transform, false);
            waterSource.transform.localPosition = impact;
            waterSource.playOnAwake = false;
            waterSource.loop = true;
            waterSource.spatialBlend = 1f;
            waterSource.rolloffMode = AudioRolloffMode.Linear;
            waterSource.minDistance = 0.5f;
            waterSource.maxDistance = 4f;
            GameAudioMixer.Route(waterSource, GameAudioGroup.SfxWorld);
            SetOpen(0f);
        }

        /// <summary>The same fixed-metre Blender cross wheel serves sink and shower controls.</summary>
        internal static Transform CreateValve(string name, Transform parent, Vector3 pivot, Color tint)
        {
            MeshRenderer renderer = CreateMesh(name, "FaucetHandle", parent);
            renderer.transform.localPosition = pivot;
            ApplyMetal(renderer, tint);
            return renderer.transform;
        }

        private static void ApplyMetal(Renderer renderer, Color tint)
        {
            HomeSurfaceAppearance.Apply(renderer, HomeSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxXY, tint);
            renderer.GetComponentInParent<HomeApartmentDressing>()?.RegisterSurface(
                renderer, HomeSurfaceKind.PaintedMetal);
        }

        public void SetOpen(float amount)
        {
            OpenAmount = isActiveAndEnabled ? Mathf.Clamp01(amount) : 0f;
            if (Handle != null) Handle.localRotation = Quaternion.AngleAxis(OpenDegrees * OpenAmount, Vector3.up);
            ApplyWater();
            if (waterSource == null) return;
            waterSource.volume = OpenAmount * 0.22f;
            if (OpenAmount <= 0.001f) { waterSource.Stop(); return; }
            if (waterSource.isPlaying) return;
            if (waterClip == null)
            {
                float[] samples = HomeSoundscapeSynthesis.GenerateShowerWaterLoopSamples();
                waterClip = AudioClip.Create("Home Sink Running Water", samples.Length, 1,
                    HomeSoundscapeSynthesis.SampleRate, false);
                waterClip.SetData(samples, 0);
            }
            waterSource.clip = waterClip;
            waterSource.Play();
        }

        private void Update()
        {
            if (OpenAmount <= 0.001f) return;
            elapsed += Time.deltaTime;
            ApplyWater();
        }

        private void ApplyWater()
        {
            bool visible = OpenAmount > 0.001f && isActiveAndEnabled && outlet != null;
            for (int index = 0; index < stream.Length; index++)
            {
                MeshRenderer renderer = stream[index];
                if (renderer == null) continue;
                renderer.enabled = visible;
                if (outlet == null) continue;
                Vector3 start = Vector3.Lerp(outlet.localPosition, impact, index / (float)StreamParts);
                Vector3 end = Vector3.Lerp(outlet.localPosition, impact, (index + 1f) / StreamParts);
                float ripple = 1f + Mathf.Sin(elapsed * 24f - index * 1.7f) * 0.10f;
                float diameter = Mathf.Lerp(0.002f, 0.010f, Mathf.Sqrt(OpenAmount)) * ripple;
                renderer.transform.localPosition = start;
                renderer.transform.localRotation = Quaternion.LookRotation(end - start);
                renderer.transform.localScale = new Vector3(diameter, diameter, (end - start).magnitude * 1.01f);
            }
            for (int index = 0; index < splashes.Length; index++)
            {
                MeshRenderer renderer = splashes[index];
                if (renderer == null) continue;
                renderer.enabled = visible;
                float phase = Mathf.Repeat(elapsed * 4f + index / (float)SplashParts, 1f);
                float angle = index * 2.399963f;
                renderer.transform.localPosition = impact + new Vector3(Mathf.Sin(angle), 0f,
                    Mathf.Cos(angle)) * (0.003f + phase * 0.012f);
                renderer.transform.localRotation = Quaternion.Euler(90f, index * 77f, 0f);
                renderer.transform.localScale = Vector3.one * ((0.012f + phase * 0.022f) * OpenAmount);
            }
        }

        private static MeshRenderer CreateMesh(string name, string mesh, Transform parent)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.AddComponent<MeshFilter>().sharedMesh = HomeBrushingResources.Mesh(mesh);
            return root.AddComponent<MeshRenderer>();
        }

        private MeshRenderer CreateWater(string name, string mesh)
        {
            MeshRenderer renderer = CreateMesh(name, mesh, transform);
            renderer.sharedMaterial = HomeBrushingResources.Water;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            return renderer;
        }

        private void OnDisable() => SetOpen(0f);
        private void OnDestroy() => SetOpen(0f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetClip()
        {
            if (waterClip == null) return;
            if (Application.isPlaying) Destroy(waterClip);
            else DestroyImmediate(waterClip);
            waterClip = null;
        }
    }
}
