using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>One authored prop leaves the seated actor, crosses real water and approaches the lens.</summary>
    [DisallowMultipleComponent]
    public sealed class HomeToiletBowelEffect : MonoBehaviour
    {
        private const float Length = .08f;
        private const float Gravity = 9.81f;
        private const float WetDrag = 15f;
        private const float WetSpeed = .21f;
        private static readonly int ImpactId = Shader.PropertyToID("_BowlImpact");
        private static Material waterMaterial;
        private GameObject solid;
        private Renderer surface;
        private AudioSource contactVoice;
        private AudioSource flushVoice;
        private MaterialPropertyBlock surfaceProperties;
        private Transform outlet;
        private Camera sceneCamera;
        private Transform room;
        private readonly Transform[] bubbles = new Transform[6];
        private Renderer splash;
        private Vector3 releaseTip, waterPoint, lensPoint;
        private float waterHeight, releaseClock, airSeconds, wetSeconds, impactClock;
        private float hitClock;
        private bool released, lensReached;
        private bool emerging;
        private Quaternion initialRotation;
        private readonly HomeToiletFloatingBody floatingBody = new HomeToiletFloatingBody();
        private float floatingClock, flushClock;
        public bool IsActive { get; private set; }
        public bool HasReleased => released;
        public bool HasHitLens => lensReached;
        public int EmissionCount { get; private set; }
        public int WaterContactCount { get; private set; }
        public Transform Solid => solid != null ? solid.transform : null;
        public Vector3 LensContactPoint => lensPoint;
        public float ClosestLensDistance { get; private set; } = float.PositiveInfinity;
        public float WaterContactClock => impactClock;
        public int FlushPlayCount { get; private set; }
        public AudioSource FlushVoice => flushVoice;
        public bool IsFloating => floatingBody.IsActive && !floatingBody.IsDrained;
        public HomeToiletFloatingBody FloatingBody => floatingBody;
        public Vector3 Velocity => floatingBody.Velocity;
        public Vector3 AngularVelocity => floatingBody.AngularVelocity;
        public Vector3 Position => floatingBody.Position;

        public bool Prepare(HomeInteriorRoot home, Transform water, Camera camera)
        {
            room = home.transform;
            transform.position = water.position;
            sceneCamera = camera;
            waterHeight = water.position.y;
            surface = water.GetComponentInChildren<Renderer>(true);
            surfaceProperties ??= new MaterialPropertyBlock();
            if (solid == null)
            {
                GameObject asset = Resources.Load<GameObject>("HomeToiletSeated/Models/Stool01");
                if (asset == null) return false;
                solid = Instantiate(asset, transform, false);
                solid.name = "Toilet Seated Solid";
                foreach (Renderer renderer in solid.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                    RuntimePrimitiveFactory.SetColor(renderer, new Color(.19f, .12f, .062f));
                }
                initialRotation = solid.transform.localRotation;
                solid.SetActive(false);
            }
            if (contactVoice == null)
            {
                contactVoice = gameObject.AddComponent<AudioSource>();
                contactVoice.playOnAwake = false;
                contactVoice.loop = false;
                contactVoice.spatialBlend = .6f;
                contactVoice.dopplerLevel = 0f;
                contactVoice.minDistance = .12f;
                contactVoice.maxDistance = 1.8f;
                contactVoice.bypassReverbZones = true;
                contactVoice.clip = HomeToiletWaterAudioResources.Entry;
                contactVoice.pitch = .82f;
                contactVoice.volume = .42f;
                GameAudioMixer.Route(contactVoice, GameAudioGroup.SfxGameplay);
                for (int i = 0; i < bubbles.Length; i++)
                    bubbles[i] = CreateWaterPart("Bowl Contact Bubble " + i, "Droplet").transform;
                splash = CreateWaterPart("Bowl Contact Splash", "Splash");
            }
            if (flushVoice == null)
            {
                flushVoice = gameObject.AddComponent<AudioSource>();
                flushVoice.playOnAwake = false;
                flushVoice.loop = false;
                flushVoice.spatialBlend = .7f;
                flushVoice.dopplerLevel = 0f;
                flushVoice.minDistance = .2f;
                flushVoice.maxDistance = 2f;
                flushVoice.volume = .5f;
                flushVoice.bypassReverbZones = true;
                flushVoice.clip = home.Audio?.GetClip(RetroSfxId.ToiletFlush);
                GameAudioMixer.Route(flushVoice, GameAudioGroup.SfxGameplay);
            }
            return surface != null && sceneCamera != null && flushVoice.clip != null;
        }

        private Renderer CreateWaterPart(string name, string mesh)
        {
            var carrier = new GameObject(name);
            carrier.transform.SetParent(transform, false);
            carrier.AddComponent<MeshFilter>().sharedMesh = HomeUrineResources.Mesh(mesh);
            var renderer = carrier.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = WaterMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            carrier.SetActive(false);
            return renderer;
        }

        private static Material WaterMaterial
        {
            get
            {
                if (waterMaterial != null) return waterMaterial;
                waterMaterial = new Material(RuntimePrimitiveFactory.DefaultMaterial)
                    { name = "Toilet Water Contact Shared", hideFlags = HideFlags.HideAndDontSave,
                        renderQueue = (int)RenderQueue.Transparent };
                waterMaterial.SetColor("_BaseColor", new Color(.42f, .48f, .39f, .36f));
                waterMaterial.SetFloat("_Surface", 1f);
                waterMaterial.SetFloat("_Smoothness", .7f);
                waterMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                waterMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                waterMaterial.SetFloat("_ZWrite", 0f);
                waterMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                return waterMaterial;
            }
        }

        public void Begin(Transform bodyOutlet)
        {
            End();
            outlet = bodyOutlet;
            IsActive = outlet != null && solid != null;
            EmissionCount = WaterContactCount = 0;
            FlushPlayCount = 0;
            ClosestLensDistance = float.PositiveInfinity;
            impactClock = hitClock = -1f;
        }

        public void Present(float clock, float seatedTime, bool allowNewEmission)
        {
            if (!IsActive) return;
            if (!emerging && allowNewEmission && seatedTime >= HomeToiletSeatedTimeline.EmissionStartsAt)
            {
                emerging = true;
                EmissionCount++;
                solid.SetActive(true);
            }
            if (emerging && !released)
            {
                float progress = Mathf.Clamp01((seatedTime - HomeToiletSeatedTimeline.EmissionStartsAt) /
                    (HomeToiletSeatedTimeline.ReleaseAt - HomeToiletSeatedTimeline.EmissionStartsAt));
                solid.transform.position = outlet.position - Vector3.up * (Length * HomeToiletPlungeTimeline.Ease(progress));
                solid.transform.rotation = room.rotation * initialRotation;
                if (progress >= 1f)
                {
                    released = true;
                    releaseClock = clock;
                    releaseTip = solid.transform.position;
                    airSeconds = Mathf.Sqrt(2f * Mathf.Max(0f, releaseTip.y - waterHeight) / Gravity);
                    lensPoint = sceneCamera.transform.position + sceneCamera.transform.forward * .026f;
                    waterPoint = new Vector3(lensPoint.x, waterHeight, lensPoint.z);
                    // Solve the wet travel time once. Its speed is continuous across the
                    // surface, then relaxes toward the much slower underwater fall.
                    float distance = Mathf.Max(0f, waterHeight - lensPoint.y);
                    float low = 0f, high = 2f;
                    for (int i = 0; i < 24; i++)
                    {
                        float middle = (low + high) * .5f;
                        if (WetDistance(middle) < distance) low = middle; else high = middle;
                    }
                    wetSeconds = (low + high) * .5f;
                }
            }
            if (FlushPlayCount == 0)
            {
                if (released) PresentFlight(clock);
                PresentWaterContact(clock);
            }
        }

        public void BeginFlush()
        {
            if (!IsActive || FlushPlayCount != 0) return;
            FlushPlayCount = 1;
            flushClock = 0f;
            floatingBody.BeginFlush();
            flushVoice.Play();
            if (splash != null) splash.gameObject.SetActive(false);
        }

        public void PresentFlush(float seconds, float strength)
        {
            if (!IsActive || FlushPlayCount == 0) return;
            if (solid != null && released)
            {
                floatingBody.SetCamera(sceneCamera.transform.position);
                floatingBody.Advance(Mathf.Max(0f, seconds - flushClock), strength);
                flushClock = Mathf.Max(flushClock, seconds);
                PresentFloatingBody();
            }
            for (int i = 0; i < bubbles.Length; i++)
            {
                float age = Mathf.Repeat(seconds * 1.25f + i / (float)bubbles.Length, 1f);
                bool visible = strength > .002f;
                bubbles[i].gameObject.SetActive(visible);
                if (!visible) continue;
                float angle = seconds * 6f + i * 2.39996f;
                float radius = Mathf.Lerp(.115f, .025f, age);
                bubbles[i].position = new Vector3(transform.position.x + Mathf.Cos(angle) * radius,
                    waterHeight - .015f - age * .075f, transform.position.z + Mathf.Sin(angle) * radius * .92f);
                bubbles[i].localScale = Vector3.one * (.005f + i % 3 * .0015f) * strength * Mathf.Sin(age * Mathf.PI);
            }
        }

        private float WetDistance(float seconds) => WetSpeed * seconds +
            (Gravity * airSeconds - WetSpeed) * (1f - Mathf.Exp(-WetDrag * seconds)) / WetDrag;

        private void PresentFlight(float clock)
        {
            float flight = Mathf.Max(0f, clock - releaseClock);
            if (flight <= airSeconds)
            {
                float u = airSeconds > 0f ? flight / airSeconds : 1f;
                Vector3 position = Vector3.Lerp(releaseTip, waterPoint, HomeToiletPlungeTimeline.Ease(u));
                position.y = releaseTip.y - .5f * Gravity * flight * flight;
                solid.transform.position = position;
            }
            else
            {
                if (WaterContactCount == 0)
                {
                    WaterContactCount = 1;
                    impactClock = releaseClock + airSeconds;
                    contactVoice.Play();
                    surface.GetPropertyBlock(surfaceProperties);
                    surfaceProperties.SetVector(ImpactId, new Vector4(waterPoint.x, waterPoint.z, impactClock, 1f));
                    surface.SetPropertyBlock(surfaceProperties);
                }
                float wetTime = flight - airSeconds;
                if (wetTime <= wetSeconds)
                    solid.transform.position = new Vector3(lensPoint.x,
                        waterHeight - WetDistance(wetTime), lensPoint.z);
                else
                {
                    if (!lensReached)
                    {
                        lensReached = true;
                        hitClock = clock;
                        // Present one complete near-lens contact before the object rolls
                        // aside. A hitch cannot skip the requested impact frame.
                        solid.transform.position = lensPoint;
                    }
                    float afterHit = clock - hitClock;
                    float moveAside = HomeToiletPlungeTimeline.Ease(afterHit / .28f);
                    Vector3 side = lensPoint + room.forward * .07f;
                    if (afterHit < .28f)
                    {
                        solid.transform.position = Vector3.Lerp(lensPoint, side, moveAside);
                        solid.transform.rotation = room.rotation * initialRotation;
                    }
                    else
                    {
                        if (!floatingBody.IsActive)
                        {
                            floatingBody.Begin(new Vector3(transform.position.x, waterHeight, transform.position.z),
                                room.rotation, side, room.rotation, room.forward * .025f,
                                room.rotation * new Vector3(2.1f, .7f, -1.5f), sceneCamera.transform.position);
                            floatingClock = hitClock + .28f;
                        }
                        floatingBody.SetCamera(sceneCamera.transform.position);
                        floatingBody.Advance(Mathf.Max(0f, clock - floatingClock));
                        floatingClock = Mathf.Max(floatingClock, clock);
                        PresentFloatingBody();
                    }
                }
            }
            ClosestLensDistance = Mathf.Min(ClosestLensDistance,
                Vector3.Distance(sceneCamera.transform.position, solid.transform.position));
        }

        private void PresentFloatingBody()
        {
            if (!floatingBody.IsActive) return;
            solid.SetActive(!floatingBody.IsDrained);
            if (floatingBody.IsDrained) return;
            solid.transform.SetPositionAndRotation(floatingBody.TipPosition,
                floatingBody.Rotation * initialRotation);
        }

        private void PresentWaterContact(float clock)
        {
            if (WaterContactCount == 0) return;
            float time = clock - impactClock;
            bool splashVisible = time >= 0f && time < .3f;
            splash.gameObject.SetActive(splashVisible);
            if (splashVisible)
            {
                splash.transform.position = waterPoint + Vector3.up * .002f;
                splash.transform.rotation = Quaternion.LookRotation(Vector3.up);
                float size = Mathf.Lerp(.025f, .09f, time / .3f);
                splash.transform.localScale = new Vector3(size, size, .08f * (1f - time / .3f));
            }
            for (int i = 0; i < bubbles.Length; i++)
            {
                float age = time - i * .025f;
                bool visible = age >= 0f && age < .36f;
                bubbles[i].gameObject.SetActive(visible);
                if (!visible) continue;
                float angle = i * 2.39996f;
                bubbles[i].position = waterPoint + new Vector3(Mathf.Cos(angle) * (.016f + age * .05f),
                    -.034f + age * .092f, Mathf.Sin(angle) * (.016f + age * .05f));
                bubbles[i].localScale = Vector3.one * (.0035f + (i % 3) * .001f) *
                    Mathf.SmoothStep(1f, 0f, age / .36f);
            }
        }

        public void End()
        {
            IsActive = emerging = released = lensReached = false;
            floatingBody.Reset();
            floatingClock = flushClock = 0f;
            outlet = null;
            if (solid != null) solid.SetActive(false);
            if (contactVoice != null) contactVoice.Stop();
            if (flushVoice != null) flushVoice.Stop();
            foreach (Transform bubble in bubbles) if (bubble != null) bubble.gameObject.SetActive(false);
            if (splash != null) splash.gameObject.SetActive(false);
            if (surface != null && surfaceProperties != null)
            {
                surface.GetPropertyBlock(surfaceProperties);
                surfaceProperties.SetVector(ImpactId, Vector4.zero);
                surface.SetPropertyBlock(surfaceProperties);
            }
        }
        private void OnDisable() => End();
        private void OnDestroy() => End();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMaterial()
        {
            if (waterMaterial != null)
            {
                if (Application.isPlaying) Destroy(waterMaterial); else DestroyImmediate(waterMaterial);
            }
            waterMaterial = null;
        }
    }
}
