using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The stream itself: three particle systems at the hero's mouth — the
    /// liquid as velocity-aligned rods, the dark lumps tumbling inside it,
    /// and the droplets that jump where it lands — plus the hand-rolled
    /// collision that turns a landing into a mark on the floor.
    ///
    /// The schedule is not here. The vomit controller tells this effect when
    /// a burst begins (<see cref="SetFlow"/> with a strength) and when it ends
    /// (strength zero); the effect only shapes the flow, pulses it, follows
    /// the mouth and reports what it hit. It is silent as well — the splat is
    /// the controller's to play, rate limit and all, off <see cref="OnImpact"/>.
    ///
    /// Collision is done by hand rather than by the particle collision module
    /// for two reasons the module cannot be talked out of: the mouth sits
    /// inside the hero's own capsule and ragdoll proxies, which the module
    /// cannot exclude, and the stair treads are triggers on the FootProbe
    /// layer, which the module never sees. So every frame each live rod is
    /// swept back over the step it just took; the first solid thing on that
    /// segment that is not the hero and not the residue itself is where it
    /// landed. Walls and furniture count — a normal is never rejected for
    /// pointing sideways.
    ///
    /// Order 280: after the character presentation has put the head down,
    /// so the emitter is at the lowered mouth before it emits.
    /// </summary>
    [DefaultExecutionOrder(280)]
    [DisallowMultipleComponent]
    public sealed class HeroVomitStreamEffect : MonoBehaviour
    {
        public const string RuntimeObjectName = "Hero Vomit";
        public const string EmitterObjectName = "Hero Vomit Emitter";
        public const float MouthForwardOffset = 0.02f;

        // The stream: rods along their own velocity.
        public const int StreamMaxParticles = 512;
        public const float StreamLifetimeSeconds = 1.4f;
        public const float RodDiameter = 0.04f;
        public const float RodLength = 0.14f;
        public const float FullStrength = 1f;
        public const float WeakStrength = 0.55f;
        public const float FullRatePerSecond = 160f;
        public const float WeakRatePerSecond = 90f;
        // The folded torso and neck put the mouth nearly downward. Give
        // the launch a small forward kick before gravity takes the stream.
        public const float ForwardBiasDegrees = 25f;
        public const float FullSpeedMinimum = 2.8f;
        public const float FullSpeedMaximum = 3.4f;
        public const float WeakSpeedMinimum = 1.8f;
        public const float WeakSpeedMaximum = 2.3f;
        public const float FullConeDegrees = 5f;
        public const float WeakConeDegrees = 8f;
        public const float WeakSizeScale = 0.85f;
        public const float PulseHertz = 3.2f;
        public const float PulseDepth = 0.45f;
        public const float NoiseStrength = 0.12f;
        public const float NoiseFrequency = 1.3f;

        // The lumps.
        public const int ChunkMaxParticles = 40;
        public const float FullChunkRatePerSecond = 9f;
        public const float WeakChunkRatePerSecond = 5f;
        public const float ChunkSpeedScale = 0.9f;
        public const float ChunkSizeMinimum = 0.025f;
        public const float ChunkSizeMaximum = 0.04f;
        public const float ChunkSpinRadiansPerSecond = Mathf.PI * 2f;

        // The splash.
        public const int SplashMaxParticles = 64;
        public const float SplashSize = 0.015f;
        public const float SplashLifetimeSeconds = 0.25f;
        public const float SplashMinimumIntervalSeconds = 0.05f;
        public const int SplashDropletsMinimum = 2;
        public const int SplashDropletsMaximum = 4;
        public const float SplashReflectShare = 0.3f;
        public const float SplashNormalShare = 0.7f;
        public const float SplashTangentialJitter = 0.3f;
        public const float SplashLiftMetres = 0.01f;

        // The sweep.
        public const float SweepPadding = 1.15f;
        public const float SweepExtraMetres = 0.02f;
        public const float RodVolumeFactor = 0.6f;
        public const float ChunkVolumeFactor = 0.3f;
        /// <summary>Unity's built-in Water layer: the sea, the river, the fountain.</summary>
        public const int WaterLayerIndex = 4;

        private const string CubeMeshResource = "Cube.fbx";
        private const uint StreamSeedSalt = 0x53545245u;
        private const uint ChunkSeedSalt = 0x4348554Eu;
        private const uint SplashSeedSalt = 0x53504C41u;
        private const uint DropletSeedSalt = 0x44524F50u;

        private static readonly RaycastHit[] Hits = new RaycastHit[8];
        private static ParticleSystem.Particle[] particleBuffer;
        private static Mesh cubeMesh;

        private Transform mouthAnchor;
        private Transform heroRoot;
        private Player3DAssetRegistry registry;
        private Transform emitterTransform;
        private ParticleSystem stream;
        private ParticleSystem chunks;
        private ParticleSystem splash;
        private ParticleSystemRenderer streamRenderer;
        private ParticleSystemRenderer chunkRenderer;
        private ParticleSystemRenderer splashRenderer;
        private AudioSource streamAudio;
        private uint seed;
        private float pulseTime;
        private float lastSplashTime = float.NegativeInfinity;
        private uint splashOrdinal;

        /// <summary>
        /// A rod or a lump landed on something solid: where, which way the
        /// surface faces, and which burst it came from. Water counts (it
        /// splashes) although it leaves no mark.
        /// </summary>
        public event Action<Vector3, Vector3, int> OnImpact;

        public bool IsInitialized { get; private set; }
        public bool IsEmitting { get; private set; }
        /// <summary>True while the hero's head is off screen and the flow is held at zero.</summary>
        public bool IsMuted { get; private set; }
        public float CurrentStrength { get; private set; }
        public int CurrentBurstIndex { get; private set; } = -1;
        public int ImpactCount { get; private set; }
        public int SplashEmitCount { get; private set; }
        /// <summary>
        /// <see cref="Time.time"/> of the most recent impact; negative
        /// infinity before the first.
        /// </summary>
        public float LastImpactTime { get; private set; } = float.NegativeInfinity;
        public Vector3 LastImpactPoint { get; private set; }
        public HeroVomitResidue Residue { get; private set; }
        public Transform Host => transform;
        public Transform MouthAnchor => mouthAnchor;

        /// <summary>
        /// Where the stream leaves this frame: the emitter, placed after
        /// the presentation's fold in the last LateUpdate. The sounds at
        /// his mouth take this rather than the anchor, which reads unbent
        /// from an Update.
        /// </summary>
        public Vector3 MouthPosition =>
            emitterTransform != null
                ? emitterTransform.position
                : mouthAnchor != null
                    ? mouthAnchor.position
                    : transform.position;
        public ParticleSystem Stream => stream;
        public ParticleSystem Chunks => chunks;
        public ParticleSystem Splash => splash;
        /// <summary>The looping gurgle at the mouth, or null before Initialize.</summary>
        public AudioSource StreamAudio => streamAudio;

        /// <summary>The stream sound's current loudness, 0..MaximumVolume.</summary>
        public float StreamSoundVolume { get; private set; }

        public int StreamAliveCount =>
            stream != null ? stream.particleCount : 0;
        public int ChunkAliveCount =>
            chunks != null ? chunks.particleCount : 0;

        /// <summary>
        /// Builds the three systems and the residue. The registry may be null
        /// (tests): with no registry the head is assumed drawn.
        /// </summary>
        public void Initialize(
            Transform mouthAnchor,
            Transform heroRoot,
            Player3DAssetRegistry registry,
            int seed)
        {
            IsInitialized = false;
            this.mouthAnchor = mouthAnchor != null
                ? mouthAnchor
                : throw new ArgumentNullException(nameof(mouthAnchor));
            this.heroRoot = heroRoot;
            this.registry = registry;
            this.seed = unchecked((uint)seed);

            StopAndClear();
            EnsureSystems();
            EnsureStreamAudio();
            ConfigureStream();
            ConfigureChunks();
            ConfigureSplash();
            // Speed, cone and size are shaped by strength across both the
            // stream and the lumps, so they are set once both systems exist.
            ApplyStrength(FullStrength);
            if (Residue == null)
            {
                Residue = HeroVomitResidue.Create(transform, seed);
            }
            else
            {
                Residue.Initialize(seed);
            }

            pulseTime = 0f;
            lastSplashTime = float.NegativeInfinity;
            splashOrdinal = 0;
            ImpactCount = 0;
            SplashEmitCount = 0;
            LastImpactTime = float.NegativeInfinity;
            LastImpactPoint = Vector3.zero;
            FollowMouth();
            IsInitialized = true;
            ApplyEmissionRates();
        }

        /// <summary>
        /// A strength above zero starts or reshapes a burst — the rate, the
        /// speed, the cone and the rod size all follow it, and the 3.2 Hz
        /// pulse restarts. Zero or less stops emission and lets what is in
        /// the air finish falling.
        /// </summary>
        public void SetFlow(float strength, int burstIndex)
        {
            if (!IsInitialized)
            {
                return;
            }

            if (float.IsNaN(strength) || strength <= 0f)
            {
                IsEmitting = false;
                CurrentStrength = 0f;
                CurrentBurstIndex = -1;
                ApplyEmissionRates();
                return;
            }

            CurrentStrength = Mathf.Min(strength, FullStrength);
            CurrentBurstIndex = burstIndex;
            IsEmitting = true;
            pulseTime = 0f;
            ApplyStrength(CurrentStrength);
            // No FollowMouth here either: SetFlow arrives from the status
            // controller's Update (order 5), with the bones in the raw
            // clip pose. The emitter keeps last LateUpdate's folded place.
            if (!stream.isPlaying)
            {
                stream.Play(false);
            }

            if (!chunks.isPlaying)
            {
                chunks.Play(false);
            }

            if (!splash.isPlaying)
            {
                splash.Play(false);
            }

            ApplyEmissionRates();
        }

        /// <summary>
        /// Stops emitting and removes every live particle. The residue stays:
        /// the floor keeps what already landed.
        /// </summary>
        public void StopAndClear()
        {
            IsEmitting = false;
            CurrentStrength = 0f;
            CurrentBurstIndex = -1;
            StopSystem(stream);
            StopSystem(chunks);
            StopSystem(splash);
            SetStreamSound(0f);
        }

        /// <summary>
        /// The stream's own sound, following the flow the controller reads
        /// off the model every frame: silent at zero, the full gurgle at
        /// one. Starts and stops the loop itself; nothing plays outside
        /// play mode, where an edit-mode fixture drives the same bout.
        /// </summary>
        public void SetStreamSound(float flow)
        {
            float volume = HeroVomitStreamSound.MaximumVolume *
                           (float.IsNaN(flow) ? 0f : Mathf.Clamp01(flow));
            StreamSoundVolume = volume;
            if (streamAudio == null)
            {
                return;
            }

            streamAudio.volume = volume;
            if (!Application.isPlaying)
            {
                return;
            }

            if (volume > 0.002f)
            {
                if (!streamAudio.isPlaying)
                {
                    streamAudio.Play();
                }
            }
            else if (streamAudio.isPlaying)
            {
                streamAudio.Stop();
            }
        }

        /// <summary>Rods per second at a strength, before the pulse.</summary>
        public static float StreamRateFor(float strength)
        {
            return strength >= WeakStrength
                ? Blend(WeakRatePerSecond, FullRatePerSecond, strength)
                : WeakRatePerSecond * Mathf.Max(0f, strength) / WeakStrength;
        }

        /// <summary>Lumps per second at a strength, before the pulse.</summary>
        public static float ChunkRateFor(float strength)
        {
            return strength >= WeakStrength
                ? Blend(WeakChunkRatePerSecond, FullChunkRatePerSecond, strength)
                : WeakChunkRatePerSecond * Mathf.Max(0f, strength) / WeakStrength;
        }

        /// <summary>The 3.2 Hz throb of the flow, 0.55..1.</summary>
        public static float Pulse(float seconds)
        {
            return 1f - PulseDepth +
                   PulseDepth *
                   Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * PulseHertz * seconds));
        }

        /// <summary>
        /// Deliberately NO <see cref="FollowMouth"/> here. This Update runs
        /// after the presentation's (order 0), which has just evaluated the
        /// graph and left every bone in the raw clip pose - the head still
        /// UP - and before the presentation's LateUpdate folds the neck,
        /// the head and the torso over the ground. The particle systems
        /// begin their step between the two, from wherever the emitter
        /// stands; following the mouth here put the emitter on the
        /// unbent mouth every frame, and with the bout's head-down
        /// head and twenty-two-degree fold the stream left from where the
        /// head HAD been. The emitter is placed once per frame in
        /// LateUpdate, after the fold, and the next frame's emission
        /// leaves from there: one frame of lag on a held pose.
        /// </summary>
        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            if (IsEmitting)
            {
                pulseTime += Time.deltaTime;
            }

            ApplyEmissionRates();
        }

        private void LateUpdate()
        {
            if (!IsInitialized)
            {
                return;
            }

            FollowMouth();
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f || Residue == null)
            {
                return;
            }

            Sweep(stream, false, deltaTime);
            Sweep(chunks, true, deltaTime);
        }

        private void OnDisable()
        {
            StopAndClear();
        }

        private void OnDestroy()
        {
            StopAndClear();
        }

        // ---- shaping -----------------------------------------------------

        private void ApplyStrength(float strength)
        {
            float speedMinimum = Blend(WeakSpeedMinimum, FullSpeedMinimum, strength);
            float speedMaximum = Blend(WeakSpeedMaximum, FullSpeedMaximum, strength);
            float cone = Blend(WeakConeDegrees, FullConeDegrees, strength);
            float sizeScale = Blend(WeakSizeScale, 1f, strength);

            ParticleSystem.MainModule streamMain = stream.main;
            streamMain.startSpeed =
                new ParticleSystem.MinMaxCurve(speedMinimum, speedMaximum);
            streamMain.startSizeX =
                new ParticleSystem.MinMaxCurve(RodDiameter * sizeScale);
            streamMain.startSizeY =
                new ParticleSystem.MinMaxCurve(RodDiameter * sizeScale);
            streamMain.startSizeZ =
                new ParticleSystem.MinMaxCurve(RodLength * sizeScale);
            ParticleSystem.ShapeModule streamShape = stream.shape;
            streamShape.angle = cone;

            ParticleSystem.MainModule chunkMain = chunks.main;
            chunkMain.startSpeed = new ParticleSystem.MinMaxCurve(
                speedMinimum * ChunkSpeedScale,
                speedMaximum * ChunkSpeedScale);
            chunkMain.startSize = new ParticleSystem.MinMaxCurve(
                ChunkSizeMinimum * sizeScale,
                ChunkSizeMaximum * sizeScale);
            ParticleSystem.ShapeModule chunkShape = chunks.shape;
            chunkShape.angle = cone;
        }

        private void ApplyEmissionRates()
        {
            if (stream == null || chunks == null)
            {
                return;
            }

            // A hidden head has no mouth to pour from: every first-person
            // seat takes the head off, and the schedule runs on regardless.
            IsMuted = registry != null &&
                      !Player3DHeadVisibility.IsHeadDrawn(registry);
            float pulse = IsEmitting && !IsMuted ? Pulse(pulseTime) : 0f;
            ParticleSystem.EmissionModule streamEmission = stream.emission;
            streamEmission.rateOverTime = new ParticleSystem.MinMaxCurve(
                StreamRateFor(CurrentStrength) * pulse);
            ParticleSystem.EmissionModule chunkEmission = chunks.emission;
            chunkEmission.rateOverTime = new ParticleSystem.MinMaxCurve(
                ChunkRateFor(CurrentStrength) * pulse);
        }

        private static float Blend(float weak, float full, float strength)
        {
            return Mathf.Lerp(
                weak,
                full,
                Mathf.Clamp01(
                    Mathf.InverseLerp(WeakStrength, FullStrength, strength)));
        }

        // ---- the sweep ---------------------------------------------------

        private void Sweep(ParticleSystem system, bool lumps, float deltaTime)
        {
            if (system == null)
            {
                return;
            }

            if (particleBuffer == null ||
                particleBuffer.Length < StreamMaxParticles)
            {
                particleBuffer =
                    new ParticleSystem.Particle[StreamMaxParticles];
            }

            int count = system.GetParticles(particleBuffer);
            if (count <= 0)
            {
                return;
            }

            int survivors = 0;
            bool changed = false;
            for (int index = 0; index < count; index++)
            {
                ParticleSystem.Particle particle = particleBuffer[index];
                if (particle.remainingLifetime <= 0f)
                {
                    changed = true;
                    continue;
                }

                Vector3 velocity = particle.totalVelocity;
                float speed = velocity.magnitude;
                if (speed > 0.0001f)
                {
                    Vector3 direction = velocity / speed;
                    float travel = speed * deltaTime * SweepPadding;
                    Vector3 from = particle.position - direction * travel;
                    if (TryFindHit(
                            from,
                            direction,
                            travel + SweepExtraMetres,
                            out RaycastHit hit))
                    {
                        float size = lumps
                            ? particle.startSize
                            : particle.startSize3D.x;
                        RegisterImpact(hit, velocity, size, lumps);
                        changed = true;
                        continue;
                    }
                }

                particleBuffer[survivors++] = particle;
            }

            // Written back only when something died: SetParticles is the
            // one call that actually removes a particle, and skipping it on
            // a quiet frame spares the copy.
            if (changed)
            {
                system.SetParticles(particleBuffer, survivors);
            }
        }

        private bool TryFindHit(
            Vector3 from,
            Vector3 direction,
            float length,
            out RaycastHit nearest)
        {
            nearest = default;
            int hitCount = Physics.RaycastNonAlloc(
                from,
                direction,
                Hits,
                length,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            float nearestDistance = float.PositiveInfinity;
            bool found = false;
            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit hit = Hits[index];
                Collider collider = hit.collider;
                if (collider == null ||
                    hit.distance >= nearestDistance ||
                    (collider.isTrigger &&
                     !FootProbeSurface.IsProbeSurface(collider)))
                {
                    continue;
                }

                Transform colliderTransform = collider.transform;
                if ((heroRoot != null &&
                     colliderTransform.IsChildOf(heroRoot)) ||
                    colliderTransform.IsChildOf(transform))
                {
                    continue;
                }

                nearestDistance = hit.distance;
                nearest = hit;
                found = true;
            }

            return found;
        }

        private void RegisterImpact(
            in RaycastHit hit,
            Vector3 velocity,
            float size,
            bool lump)
        {
            Vector3 point = hit.point;
            Vector3 normal = hit.normal;
            bool water = hit.collider.gameObject.layer == WaterLayerIndex;
            if (!water)
            {
                if (lump)
                {
                    Residue.AddChunk(point, normal);
                }
                else
                {
                    Residue.AddImpact(
                        point,
                        normal,
                        RodVolumeFactor * size * size);
                }
            }

            EmitSplash(point, normal, velocity);
            ImpactCount++;
            LastImpactTime = Time.time;
            LastImpactPoint = point;
            OnImpact?.Invoke(point, normal, CurrentBurstIndex);
        }

        private void EmitSplash(Vector3 point, Vector3 normal, Vector3 velocity)
        {
            if (splash == null ||
                Time.time - lastSplashTime < SplashMinimumIntervalSeconds)
            {
                return;
            }

            lastSplashTime = Time.time;
            if (!splash.isPlaying)
            {
                splash.Play(false);
            }

            uint hash = CitySoundStableHash.Combine(
                seed,
                splashOrdinal++ ^ DropletSeedSalt);
            int droplets = SplashDropletsMinimum +
                           (int)(hash %
                                 (uint)(SplashDropletsMaximum -
                                        SplashDropletsMinimum + 1));
            Vector3 reflected = Vector3.Reflect(velocity, normal);
            Vector3 tangent = HeroVomitResidueModel.TangentFor(normal);
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            for (int droplet = 0; droplet < droplets; droplet++)
            {
                uint dropletHash = CitySoundStableHash.Combine(
                    hash,
                    unchecked((uint)droplet));
                float angle = Unit(dropletHash) * Mathf.PI * 2f;
                float spread = Unit(CitySoundStableHash.Combine(dropletHash, 7u));
                Vector3 jitter =
                    (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) *
                    (SplashTangentialJitter * spread);
                var emit = new ParticleSystem.EmitParams
                {
                    position = point + normal * SplashLiftMetres,
                    velocity = reflected * SplashReflectShare +
                               normal * SplashNormalShare +
                               jitter,
                    startSize = SplashSize,
                    startLifetime = SplashLifetimeSeconds,
                    startColor = Color.white,
                    applyShapeToPosition = false
                };
                splash.Emit(emit, 1);
            }

            SplashEmitCount += droplets;
        }

        // ---- construction ------------------------------------------------

        private void EnsureSystems()
        {
            if (emitterTransform == null)
            {
                var emitter = new GameObject(EmitterObjectName);
                emitter.layer = gameObject.layer;
                emitterTransform = emitter.transform;
                emitterTransform.SetParent(transform, false);
            }

            stream = EnsureSystem("Stream", stream, out streamRenderer);
            chunks = EnsureSystem("Chunks", chunks, out chunkRenderer);
            splash = EnsureSystem("Splash", splash, out splashRenderer);
        }

        private ParticleSystem EnsureSystem(
            string name,
            ParticleSystem existing,
            out ParticleSystemRenderer renderer)
        {
            ParticleSystem system = existing;
            if (system == null)
            {
                var systemObject = new GameObject(name);
                systemObject.layer = gameObject.layer;
                systemObject.transform.SetParent(emitterTransform, false);
                system = systemObject.AddComponent<ParticleSystem>();
            }

            renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer == null)
            {
                renderer =
                    system.gameObject.AddComponent<ParticleSystemRenderer>();
            }

            return system;
        }

        private void ConfigureStream()
        {
            ParticleSystem.MainModule main =
                ConfigureCommon(stream, StreamSeedSalt);
            main.maxParticles = StreamMaxParticles;
            main.startLifetime =
                new ParticleSystem.MinMaxCurve(StreamLifetimeSeconds);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(1f);
            main.startSize3D = true;
            main.startRotation3D = false;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f);

            ParticleSystem.EmissionModule emission = stream.emission;
            emission.enabled = true;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
            emission.rateOverDistance = new ParticleSystem.MinMaxCurve(0f);
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());

            ParticleSystem.ShapeModule shape = stream.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = FullConeDegrees;
            shape.radius = 0.008f;
            shape.length = 0.02f;

            ParticleSystem.NoiseModule noise = stream.noise;
            noise.enabled = true;
            noise.separateAxes = false;
            noise.strength = new ParticleSystem.MinMaxCurve(NoiseStrength);
            noise.frequency = NoiseFrequency;
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Low;

            ParticleSystem.RotationOverLifetimeModule rotation =
                stream.rotationOverLifetime;
            rotation.enabled = false;

            ConfigureRenderer(
                streamRenderer,
                HeroVomitResources.LiquidMaterial,
                ParticleSystemRenderSpace.Velocity);
        }

        private void ConfigureChunks()
        {
            ParticleSystem.MainModule main =
                ConfigureCommon(chunks, ChunkSeedSalt);
            main.maxParticles = ChunkMaxParticles;
            main.startLifetime =
                new ParticleSystem.MinMaxCurve(StreamLifetimeSeconds);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(1f);
            main.startSize3D = false;
            main.startRotation3D = true;
            main.startRotationX =
                new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY =
                new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ =
                new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            ParticleSystem.EmissionModule emission = chunks.emission;
            emission.enabled = true;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
            emission.rateOverDistance = new ParticleSystem.MinMaxCurve(0f);
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());

            ParticleSystem.ShapeModule shape = chunks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = FullConeDegrees;
            shape.radius = 0.008f;
            shape.length = 0.02f;

            ParticleSystem.NoiseModule noise = chunks.noise;
            noise.enabled = false;

            ParticleSystem.RotationOverLifetimeModule rotation =
                chunks.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(
                -ChunkSpinRadiansPerSecond,
                ChunkSpinRadiansPerSecond);
            rotation.y = new ParticleSystem.MinMaxCurve(
                -ChunkSpinRadiansPerSecond,
                ChunkSpinRadiansPerSecond);
            rotation.z = new ParticleSystem.MinMaxCurve(
                -ChunkSpinRadiansPerSecond,
                ChunkSpinRadiansPerSecond);

            ConfigureRenderer(
                chunkRenderer,
                HeroVomitResources.ChunkMaterial,
                ParticleSystemRenderSpace.World);
        }

        private void ConfigureSplash()
        {
            ParticleSystem.MainModule main =
                ConfigureCommon(splash, SplashSeedSalt);
            main.maxParticles = SplashMaxParticles;
            main.startLifetime =
                new ParticleSystem.MinMaxCurve(SplashLifetimeSeconds);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f);
            main.startSize3D = false;
            main.startSize = new ParticleSystem.MinMaxCurve(SplashSize);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(1f);
            main.startRotation3D = false;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f);

            // Fed only by Emit(EmitParams) from the sweep.
            ParticleSystem.EmissionModule emission = splash.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = splash.shape;
            shape.enabled = false;
            ParticleSystem.NoiseModule noise = splash.noise;
            noise.enabled = false;
            ParticleSystem.RotationOverLifetimeModule rotation =
                splash.rotationOverLifetime;
            rotation.enabled = false;

            ConfigureRenderer(
                splashRenderer,
                HeroVomitResources.LiquidMaterial,
                ParticleSystemRenderSpace.World);
        }

        private ParticleSystem.MainModule ConfigureCommon(
            ParticleSystem system,
            uint salt)
        {
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.useAutoRandomSeed = false;
            system.randomSeed = CitySoundStableHash.Combine(seed, salt);

            ParticleSystem.MainModule main = system.main;
            main.duration = 5f;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = false;
            main.startDelay = new ParticleSystem.MinMaxCurve(0f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.useUnscaledTime = false;
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
            main.stopAction = ParticleSystemStopAction.None;

            ParticleSystem.CollisionModule collision = system.collision;
            collision.enabled = false;
            ParticleSystem.LightsModule lights = system.lights;
            lights.enabled = false;
            ParticleSystem.TriggerModule trigger = system.trigger;
            trigger.enabled = false;
            ParticleSystem.TrailModule trails = system.trails;
            trails.enabled = false;
            ParticleSystem.ExternalForcesModule externalForces =
                system.externalForces;
            externalForces.enabled = false;
            ParticleSystem.SubEmittersModule subEmitters = system.subEmitters;
            subEmitters.enabled = false;
            ParticleSystem.TextureSheetAnimationModule textureSheet =
                system.textureSheetAnimation;
            textureSheet.enabled = false;
            ParticleSystem.ColorOverLifetimeModule color =
                system.colorOverLifetime;
            color.enabled = false;
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = false;
            ParticleSystem.VelocityOverLifetimeModule velocity =
                system.velocityOverLifetime;
            velocity.enabled = false;
            return main;
        }

        private static void ConfigureRenderer(
            ParticleSystemRenderer renderer,
            Material material,
            ParticleSystemRenderSpace alignment)
        {
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = CubeMesh;
            renderer.alignment = alignment;
            renderer.sortMode = ParticleSystemSortMode.None;
            // URP Lit has no procedural particle instancing path; left on,
            // the renderer would draw nothing. Off, Unity builds one mesh.
            renderer.enableGPUInstancing = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = true;
        }

        private static Mesh CubeMesh
        {
            get
            {
                if (cubeMesh == null)
                {
                    cubeMesh = Resources.GetBuiltinResource<Mesh>(
                        CubeMeshResource);
                }

                return cubeMesh;
            }
        }

        private static void StopSystem(ParticleSystem system)
        {
            if (system != null)
            {
                system.Stop(
                    false,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        /// <summary>
        /// One looping source on the emitter, so it rides the mouth with
        /// the rods. Spatialised like the one-shot pool's voices and
        /// routed with them; volume zero until the flow says otherwise.
        /// </summary>
        private void EnsureStreamAudio()
        {
            if (streamAudio != null || emitterTransform == null)
            {
                return;
            }

            var host = new GameObject("Hero Vomit Stream Sound");
            host.transform.SetParent(emitterTransform, false);
            streamAudio = host.AddComponent<AudioSource>();
            streamAudio.clip = HeroVomitStreamSound.LoopClip;
            streamAudio.loop = true;
            streamAudio.playOnAwake = false;
            streamAudio.spatialBlend = 1f;
            streamAudio.dopplerLevel = 0f;
            streamAudio.rolloffMode = AudioRolloffMode.Linear;
            streamAudio.minDistance = HeroVomitStreamSound.MinimumDistanceMetres;
            streamAudio.maxDistance = HeroVomitStreamSound.MaximumDistanceMetres;
            streamAudio.bypassReverbZones = true;
            streamAudio.volume = 0f;
            GameAudioMixer.Route(streamAudio, GameAudioGroup.SfxWorld);
        }

        private void FollowMouth()
        {
            if (mouthAnchor == null || emitterTransform == null)
            {
                return;
            }

            Vector3 outward = mouthAnchor.up;
            if (outward.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            outward.Normalize();
            Vector3 launch = heroRoot != null
                ? Vector3.RotateTowards(outward, heroRoot.forward,
                    ForwardBiasDegrees * Mathf.Deg2Rad, 0f).normalized
                : outward;
            Vector3 worldUp = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(launch, worldUp)) > 0.98f)
            {
                worldUp = mouthAnchor.forward;
            }

            emitterTransform.SetPositionAndRotation(
                mouthAnchor.position + outward * MouthForwardOffset,
                Quaternion.LookRotation(launch, worldUp));
            emitterTransform.localScale = Vector3.one;
        }

        private static float Unit(uint hash)
        {
            return (hash >> 8) / (float)(1u << 24);
        }
    }
}
