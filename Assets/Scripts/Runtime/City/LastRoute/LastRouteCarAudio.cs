using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>What the tyres are running on. The city is never dry; the
    /// mountain is snow-packed asphalt.</summary>
    public enum LastRouteCarRoadSurface
    {
        WetAsphalt = 0,
        PackedSnow = 1
    }

    public enum LastRouteCarLoopKind
    {
        Engine = 0,
        Cabin = 1,
        Tyres = 2,
        BridgeDeck = 3
    }

    public enum LastRouteCarCueKind
    {
        Starter = 0,
        Shutdown = 1,
        DeckJoint = 2,
        DoorLatch = 3
    }

    /// <summary>
    /// Canonical gain staging and distance law for the Ferryman's car, kept
    /// pure so the numbers can be asserted without a scene. The bus's
    /// <see cref="CityBusAudioMix"/> is the band this sits in: a saloon with
    /// one passenger reads a little quieter outside and a little closer
    /// inside, because the hero is sitting a metre from its bulkhead.
    /// </summary>
    public static class LastRouteCarAudioMix
    {
        public const float EngineMinimumDistance = 3f;

        /// <summary>The exterior tail ends where the city's visible slice
        /// does, the same tie the bus makes.</summary>
        public const float EngineMaximumDistance =
            RuntimeSceneSetup.CityFarClipPlane;

        public const float TyreMinimumDistance = 2f;
        public const float TyreMaximumDistance = 30f;

        /// <summary>
        /// The engine loop is authored at `42 Hz`, a four-cylinder turning
        /// about `1260 rpm`. Idle sits under that and the top of the
        /// audible range about two and a half thousand, which is as far as
        /// a resampled loop will go before it reads as a toy.
        /// </summary>
        public const float EnginePitchAtZero = 0.62f;

        public const float EnginePitchAtFull = 2.05f;
        public const float EngineIdleVolume = 0.24f;
        public const float EngineFullVolume = 0.56f;

        /// <summary>Overrun - throttle shut on a downhill or into a bend -
        /// is the same revs at a fraction of the voice.</summary>
        public const float EngineOverrunMultiplier = 0.72f;

        /// <summary>While the starter is turning it over the block is heard
        /// under the starter clip, not as an engine.</summary>
        public const float CrankingLoopMultiplier = 0.30f;

        /// <summary>An engine under load opens up: the exterior low-pass
        /// follows the throttle rather than the revs.</summary>
        public const float EngineClosedCutoff = 950f;

        public const float EngineOpenCutoff = 3400f;

        public const float CabinIdleVolume = 0.16f;
        public const float CabinFullVolume = 0.40f;
        public const float CabinPitchAtZero = 0.68f;
        public const float CabinPitchAtFull = 1.30f;

        /// <summary>What the bodywork takes off the exterior for the man
        /// inside it.</summary>
        public const float ExteriorCabinMultiplier = 0.70f;

        public const float CabinBlendSeconds = 0.35f;

        public const float TyreFullVolume = 0.36f;
        public const float TyreFullSpeed = 8f;
        public const float WetAsphaltCutoff = 4200f;
        public const float PackedSnowCutoff = 1500f;
        public const float PackedSnowGain = 0.55f;

        public const float DeckFullVolume = 0.30f;
        public const float DeckFullSpeed = 6f;
        public const float DeckBlendSeconds = 0.30f;

        /// <summary>How long the tunnel takes to close round the car, and
        /// to let go of it.</summary>
        public const float EnclosureBlendSeconds = 0.45f;

        public const float CueVolume = 0.62f;

        public const int EnginePriority = 80;
        public const int CabinPriority = 64;
        public const int TyrePriority = 96;
        public const int DeckPriority = 110;
        public const int CuePriority = 72;

        public static float EvaluateEnginePitch(float rpm01)
        {
            return Mathf.Lerp(
                EnginePitchAtZero,
                EnginePitchAtFull,
                Mathf.Clamp01(rpm01));
        }

        public static float EvaluateEngineVolume(float rpm01, float load01)
        {
            return Mathf.Lerp(
                       EngineIdleVolume,
                       EngineFullVolume,
                       Mathf.Clamp01(rpm01)) *
                   Mathf.Lerp(
                       EngineOverrunMultiplier,
                       1f,
                       Mathf.Clamp01(load01));
        }

        public static float EvaluateEngineCutoff(float load01)
        {
            return Mathf.Lerp(
                EngineClosedCutoff,
                EngineOpenCutoff,
                Mathf.Clamp01(load01));
        }

        public static float EvaluateCabinPitch(float rpm01)
        {
            return Mathf.Lerp(
                CabinPitchAtZero,
                CabinPitchAtFull,
                Mathf.Clamp01(rpm01));
        }

        public static float EvaluateCabinVolume(float rpm01, float load01)
        {
            float work = Mathf.Clamp01(
                (Mathf.Clamp01(rpm01) * 0.6f) +
                (Mathf.Clamp01(load01) * 0.4f));
            return Mathf.Lerp(CabinIdleVolume, CabinFullVolume, work);
        }

        public static float SurfaceGain(LastRouteCarRoadSurface surface)
        {
            return surface == LastRouteCarRoadSurface.PackedSnow
                ? PackedSnowGain
                : 1f;
        }

        public static float EvaluateTyreCutoff(
            LastRouteCarRoadSurface surface)
        {
            return surface == LastRouteCarRoadSurface.PackedSnow
                ? PackedSnowCutoff
                : WetAsphaltCutoff;
        }

        /// <summary>
        /// Road noise grows a touch faster than linearly with speed, and
        /// snow takes nearly half of it away - a packed road hisses less
        /// than a wet one.
        /// </summary>
        public static float EvaluateTyreVolume(
            float speed,
            LastRouteCarRoadSurface surface)
        {
            float clean = float.IsNaN(speed) || float.IsInfinity(speed)
                ? 0f
                : Mathf.Max(0f, speed);
            float speed01 = Mathf.Clamp01(clean / TyreFullSpeed);
            return TyreFullVolume *
                   Mathf.Pow(speed01, 1.2f) *
                   SurfaceGain(surface);
        }

        public static float EvaluateDeckVolume(float speed)
        {
            float clean = float.IsNaN(speed) || float.IsInfinity(speed)
                ? 0f
                : Mathf.Max(0f, speed);
            return DeckFullVolume * Mathf.Clamp01(clean / DeckFullSpeed);
        }
    }

    /// <summary>
    /// Deterministic low-rate mono clips for everything the car can be
    /// heard doing. Every loop is authored at frequencies that divide the
    /// four-second length exactly, so the tonal part is phase-continuous
    /// at the seam and only the noise needs the crossfade; every one-shot
    /// is a gesture with an attack and a tail rather than a sample.
    ///
    /// The engine is a tired petrol four: firing pulses at `42 Hz` with
    /// every fourth one weak, a stack of harmonics under them and a valve
    /// tick over. The bus is a diesel and sounds like one; this is the
    /// other kind of engine, and the two should never be confused across a
    /// street.
    /// </summary>
    public static class LastRouteCarSoundSynthesis
    {
        public const int SampleRate = 22050;
        public const float LoopDuration = 4f;
        public const float EngineFundamentalHz = 42f;
        public const int CrossfadeSamples = 1536;
        public const float StarterClipSeconds = 1.4f;
        public const float ShutdownClipSeconds = 0.85f;
        public const float DeckJointClipSeconds = 0.26f;
        public const float DoorLatchClipSeconds = 0.2f;

        private const float QuantizationSteps = 127f;
        private const float ClipLimit = 0.78f;

        public static float[] GenerateLoop(LastRouteCarLoopKind kind)
        {
            int sampleCount = Mathf.RoundToInt(SampleRate * LoopDuration);
            var extended = new float[sampleCount + CrossfadeSamples];
            uint noiseState = CreateSeed(0x4C4F4F50u, (int)kind);
            float lowNoise = 0f;
            float slowNoise = 0f;
            float band = 0f;
            float bandLower = 0f;
            for (int index = 0; index < extended.Length; index++)
            {
                float time = index / (float)SampleRate;
                float white = NextNoise(ref noiseState);
                lowNoise += (white - lowNoise) * 0.055f;
                slowNoise += (white - slowNoise) * 0.0018f;
                band += (white - band) * 0.30f;
                bandLower += (band - bandLower) * 0.08f;
                float sample;
                switch (kind)
                {
                    case LastRouteCarLoopKind.Engine:
                        sample = EngineLoop(time, white, lowNoise, slowNoise);
                        break;
                    case LastRouteCarLoopKind.Cabin:
                        sample = CabinLoop(time, lowNoise, slowNoise);
                        break;
                    case LastRouteCarLoopKind.Tyres:
                        sample = TyreLoop(time, band - bandLower, slowNoise);
                        break;
                    case LastRouteCarLoopKind.BridgeDeck:
                        sample = DeckLoop(time, lowNoise);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(kind),
                            kind,
                            "Unknown car loop.");
                }

                extended[index] = Quantize(sample);
            }

            var samples = new float[sampleCount];
            Array.Copy(extended, samples, sampleCount);
            for (int index = 0; index < CrossfadeSamples; index++)
            {
                float blend = index / (float)CrossfadeSamples;
                samples[index] = Quantize(
                    Mathf.Lerp(
                        extended[sampleCount + index],
                        samples[index],
                        blend));
            }

            return samples;
        }

        public static float[] GenerateCue(LastRouteCarCueKind kind)
        {
            float seconds;
            switch (kind)
            {
                case LastRouteCarCueKind.Starter:
                    seconds = StarterClipSeconds;
                    break;
                case LastRouteCarCueKind.Shutdown:
                    seconds = ShutdownClipSeconds;
                    break;
                case LastRouteCarCueKind.DeckJoint:
                    seconds = DeckJointClipSeconds;
                    break;
                case LastRouteCarCueKind.DoorLatch:
                    seconds = DoorLatchClipSeconds;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        "Unknown car cue.");
            }

            int sampleCount = Mathf.RoundToInt(SampleRate * seconds);
            var samples = new float[sampleCount];
            uint noiseState = CreateSeed(0x43554553u, (int)kind);
            float lowNoise = 0f;
            float whinePhase = 0f;
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float white = NextNoise(ref noiseState);
                lowNoise += (white - lowNoise) * 0.07f;
                float sample;
                switch (kind)
                {
                    case LastRouteCarCueKind.Starter:
                        sample = StarterCue(
                            time,
                            white,
                            lowNoise,
                            ref whinePhase);
                        break;
                    case LastRouteCarCueKind.Shutdown:
                        sample = ShutdownCue(time, white, lowNoise);
                        break;
                    case LastRouteCarCueKind.DeckJoint:
                        sample = DeckJointCue(time, white);
                        break;
                    default:
                        sample = DoorLatchCue(time, white);
                        break;
                }

                // Every gesture ends in silence rather than at a cut.
                float tail = Mathf.Clamp01((seconds - time) / 0.03f);
                samples[index] = Quantize(sample * tail);
            }

            return samples;
        }

        internal static AudioClip CreateLoopClip(LastRouteCarLoopKind kind)
        {
            return CreateClip(
                "LastRouteCar_" + kind,
                GenerateLoop(kind));
        }

        internal static AudioClip CreateCueClip(LastRouteCarCueKind kind)
        {
            return CreateClip(
                "LastRouteCar_" + kind,
                GenerateCue(kind));
        }

        private static float EngineLoop(
            float time,
            float white,
            float lowNoise,
            float slowNoise)
        {
            float cycles = time * EngineFundamentalHz;
            float cycle = cycles - Mathf.Floor(cycles);
            int firing = (int)Mathf.Floor(cycles);

            // A tired engine: one cylinder down on compression, so every
            // fourth pulse comes in weak and the whole thing lopes.
            float uneven = (firing & 3) == 1 ? 0.78f : 1f;

            float harmonics = 0f;
            for (int order = 1; order <= 7; order++)
            {
                harmonics += Mathf.Sin(
                                 (time * Mathf.PI * 2f * EngineFundamentalHz *
                                  order) + (order * 0.3f)) *
                             (0.30f / Mathf.Pow(order, 1.15f));
            }

            float pop = Mathf.Exp(-cycle * 7f) *
                        Mathf.Sin(time * Mathf.PI * 2f * 168f) *
                        0.22f;
            float tick = cycle < 0.05f ? white * 0.09f : 0f;
            float breath = 0.86f + (slowNoise * 0.14f);
            return (harmonics * breath * uneven) +
                   (pop * uneven) +
                   tick +
                   (lowNoise * 0.03f);
        }

        private static float CabinLoop(
            float time,
            float lowNoise,
            float slowNoise)
        {
            float boom =
                (Mathf.Sin(time * Mathf.PI * 2f * 21f) * 0.20f) +
                (Mathf.Sin(time * Mathf.PI * 2f * 63f + 0.4f) * 0.08f);
            float uneasy = 0.80f + (Mathf.Sin(time * Mathf.PI * 2f * 11f) * 0.20f);
            float rattleGate = Mathf.Max(0f, slowNoise + 0.10f) * 2f;
            float rattle =
                ((Mathf.Sin(time * Mathf.PI * 2f * 197f) * 0.05f) +
                 (Mathf.Sin(time * Mathf.PI * 2f * 311f + 1.3f) * 0.03f)) *
                rattleGate;
            return (boom * uneasy) + rattle + (lowNoise * 0.04f);
        }

        private static float TyreLoop(
            float time,
            float bandNoise,
            float slowNoise)
        {
            float flutter = 0.72f + (slowNoise * 0.28f);
            float hum = Mathf.Sin(time * Mathf.PI * 2f * 55f) * 0.06f;
            return (bandNoise * 0.55f * flutter) + hum;
        }

        private static float DeckLoop(float time, float lowNoise)
        {
            float beats = time * 6.5f;
            float beat = beats - Mathf.Floor(beats);
            float thrum = Mathf.Exp(-beat * 9f) *
                          Mathf.Sin(time * Mathf.PI * 2f * 48f) *
                          0.30f;
            return thrum + (lowNoise * 0.35f);
        }

        private static float StarterCue(
            float time,
            float white,
            float lowNoise,
            ref float whinePhase)
        {
            const float crank = LastRouteCarEngineModel.StarterSeconds;
            if (time < crank)
            {
                // The starter motor: a whine that sags on every compression
                // stroke, the strokes themselves as soft thumps, and the
                // ring gear as a little noise over both.
                float strokes = time * 7.5f;
                float stroke = strokes - Mathf.Floor(strokes);
                float sag = 1f - (Mathf.Exp(-stroke * 6f) * 0.12f);
                whinePhase += Mathf.PI * 2f * 390f * sag / SampleRate;
                float whine = Mathf.Sin(whinePhase) * 0.14f;
                float thump = Mathf.Exp(-stroke * 10f) *
                              Mathf.Sin(time * Mathf.PI * 2f * 70f) *
                              0.28f;
                float attack = Mathf.Min(1f, time * 30f);
                return (whine + thump + (white * 0.05f) + (lowNoise * 0.04f)) *
                       attack;
            }

            // The catch: one cough of noise and a low thump as it fires,
            // and the loop's own flare takes it from here.
            float since = time - crank;
            float cough = white * Mathf.Exp(-since * 26f) * 0.34f;
            float thud = Mathf.Sin(since * Mathf.PI * 2f * 62f) *
                         Mathf.Exp(-since * 14f) *
                         0.40f;
            return cough + thud;
        }

        private static float ShutdownCue(
            float time,
            float white,
            float lowNoise)
        {
            // The block shuddering to a stop: a wobble that slows as it
            // dies, one clank as the last compression lets go, and the
            // hiss of everything hot settling.
            float wobbleHz = 24f * (1f - (time * 0.6f));
            float shudder = Mathf.Sin(time * Mathf.PI * 2f * wobbleHz) *
                            Mathf.Exp(-time * 2.6f) *
                            0.30f;
            float clank = 0f;
            float sinceClank = time - 0.32f;
            if (sinceClank >= 0f)
            {
                clank = Mathf.Sin(sinceClank * Mathf.PI * 2f * 480f) *
                        Mathf.Exp(-sinceClank * 30f) *
                        0.22f;
            }

            float hiss = lowNoise * Mathf.Exp(-time * 3f) * 0.12f +
                         (white * 0.02f);
            return shudder + clank + hiss;
        }

        private static float DeckJointCue(float time, float white)
        {
            float thud = Mathf.Sin(time * Mathf.PI * 2f * 58f) *
                         Mathf.Exp(-time * 22f) *
                         0.55f;
            float click = white * Mathf.Exp(-time * 90f) * 0.25f;
            return thud + click;
        }

        private static float DoorLatchCue(float time, float white)
        {
            float click = Mathf.Sin(time * Mathf.PI * 2f * 1900f) *
                          Mathf.Exp(-time * 140f) *
                          0.30f;
            float thud = Mathf.Sin(time * Mathf.PI * 2f * 95f) *
                         Mathf.Exp(-time * 28f) *
                         0.40f;
            return click + thud + (white * Mathf.Exp(-time * 60f) * 0.08f);
        }

        private static AudioClip CreateClip(string clipName, float[] samples)
        {
            AudioClip clip = AudioClip.Create(
                clipName,
                samples.Length,
                1,
                SampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        private static uint CreateSeed(uint salt, int kind)
        {
            uint value = salt ^ (((uint)kind + 1u) * 0x9E3779B9u);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            return value == 0u ? 0xA341316Cu : value;
        }

        private static float NextNoise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return ((state & 0x00FFFFFFu) / 8388607.5f) - 1f;
        }

        private static float Quantize(float sample)
        {
            return Mathf.Round(
                       Mathf.Clamp(sample, -ClipLimit, ClipLimit) *
                       QuantizationSteps) /
                   QuantizationSteps;
        }
    }

    /// <summary>
    /// The Ferryman's car, heard.
    ///
    /// Bounded and source-first, the bus's rule: every voice belongs to a
    /// visible mechanism and plays from where it stands. The engine is in
    /// the bay under the bonnet he sits on, the cabin loop is the bodywork
    /// round the passenger seat, the tyres are the rear axle, the bridge
    /// deck drums under the same axle, and the one-shot voice - starter,
    /// key-off, door latches, the expansion joints at either end of the
    /// bridge - sits in the cabin where the man who hears them does.
    ///
    /// It follows the journey the way the headlights do, by polling rather
    /// than by events: the engine is wanted while the man is at the wheel
    /// and the car has not arrived, and turned over with the starter only
    /// when it was actually stopped. On the island that is the beat the
    /// hero hears while he walks round to his own door; on the mountain the
    /// car comes out of the tunnel already running and there is no starter
    /// to hear.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(280)]
    public sealed class LastRouteCarAudio : MonoBehaviour
    {
        /// <summary>
        /// Engine, cabin, tyres, deck and the one-shot voice. Scene audio
        /// budgets are asserted against owner constants rather than a
        /// hand-counted total, so this is the number they read.
        /// </summary>
        public const int OwnedSourceCount = 5;

        public const string EngineAnchorName = "Car Engine Bay Audio";
        public const string CabinAnchorName = "Car Cabin Audio";
        public const string AxleAnchorName = "Car Rear Axle Audio";

        /// <summary>A hitch longer than this is stepped rather than
        /// swallowed, the driver's own convention.</summary>
        public const float MaximumStepSeconds = 0.1f;

        /// <summary>
        /// A leaf standing further open than this arms its latch; the latch
        /// fires when it comes back under <see cref="DoorLatchOpenness"/>.
        /// The gap is what keeps a leaf that is merely jostled quiet.
        /// </summary>
        public const float DoorLatchArmOpenness = 0.15f;

        public const float DoorLatchOpenness = 0.02f;

        /// <summary>
        /// A jump along the road longer than this in one frame is a skip,
        /// not a drive, and the expansion joints it hops over stay silent
        /// under the black.
        /// </summary>
        public const float DeckSkipTolerance = 20f;

        private static readonly AudioClip[] LoopClips = new AudioClip[4];
        private static readonly AudioClip[] CueClips = new AudioClip[4];

        private readonly List<AudioSource> ownedSources =
            new List<AudioSource>();
        private readonly LastRouteCarEngineModel engine =
            new LastRouteCarEngineModel();

        private LastRouteCarAssetRegistry registry;
        private LastRouteCarDriver driver;
        private LastRouteCarDoors doors;
        private LastRouteCarSeatInteraction seat;
        private LastRouteFerrymanPresentation ferryman;
        private LastRouteRideController ride;
        private Func<bool> isEnclosed;
        private MountainRoadWindSoundPlayer windBed;
        private LastRouteCarRoadSurface surface;
        private AudioSource engineSource;
        private AudioSource cabinSource;
        private AudioSource tyreSource;
        private AudioSource deckSource;
        private AudioSource cueSource;
        private AudioLowPassFilter engineTone;
        private AudioLowPassFilter tyreTone;
        private AudioReverbFilter engineRoom;
        private AudioReverbFilter axleRoom;
        private float cabinBlend;
        private float enclosure;
        private float deckBlend;
        private bool hasDeck;
        private Vector3 deckStartWorld;
        private Vector3 deckEndWorld;
        private LastRouteCarDrivePath deckPath;
        private float deckStartDistance;
        private float deckEndDistance;
        private bool wasOnDeck;
        private float lastDistance;
        private bool driverLeafArmed;
        private bool passengerLeafArmed;

        public bool IsInitialized { get; private set; }
        public LastRouteCarEngineModel Engine => engine;
        public IReadOnlyList<AudioSource> OwnedSources => ownedSources;
        public AudioSource EngineSource => engineSource;
        public AudioSource CabinSource => cabinSource;
        public AudioSource TyreSource => tyreSource;
        public AudioSource DeckSource => deckSource;
        public AudioSource CueSource => cueSource;
        public AudioLowPassFilter EngineTone => engineTone;
        public AudioReverbFilter EngineRoom => engineRoom;
        public LastRouteCarRoadSurface Surface => surface;

        /// <summary>`1` with the hero in the seat, `0` outside.</summary>
        public float CabinBlend => cabinBlend;

        /// <summary>`1` inside a tunnel, `0` under open sky.</summary>
        public float Enclosure => enclosure;

        public bool IsOnDeck => wasOnDeck;
        public int StarterCueCount { get; private set; }
        public int ShutdownCueCount { get; private set; }
        public int DeckJointCueCount { get; private set; }
        public int DoorLatchCueCount { get; private set; }

        /// <summary>
        /// Hangs the five voices off the car. Anchors are placed along the
        /// RUNTIME root's own axes from the registry's dimensions, never
        /// from an imported node - the imported body's forward is nearly
        /// vertical, which is the trap the headlights record. The engine
        /// is under the bonnet at the front, where the Ferryman sits on it;
        /// the cabin is the midpoint of the two seats; the axle is the
        /// rear one.
        /// </summary>
        public void Initialize(LastRouteCarAssetRegistry carRegistry)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The car's audio is already initialized.");
            }

            registry = carRegistry ??
                throw new ArgumentNullException(nameof(carRegistry));
            driver = GetComponent<LastRouteCarDriver>();
            doors = GetComponent<LastRouteCarDoors>();
            LastRouteCarDimensions dimensions = registry.Dimensions;

            Transform engineAnchor = CreateAnchor(
                EngineAnchorName,
                new Vector3(
                    0f,
                    Mathf.Clamp(dimensions.Height * 0.42f, 0.45f, 0.85f),
                    (dimensions.Length * 0.5f) - 0.65f));
            Transform cabinAnchor = CreateAnchor(
                CabinAnchorName,
                ResolveCabinLocalPosition(dimensions));
            Transform axleAnchor = CreateAnchor(
                AxleAnchorName,
                new Vector3(
                    0f,
                    Mathf.Max(0.12f, dimensions.WheelRadius * 0.6f),
                    -(dimensions.Wheelbase * 0.5f)));

            engineSource = engineAnchor.gameObject.AddComponent<AudioSource>();
            ConfigureSpatialSource(
                engineSource,
                LastRouteCarAudioMix.EngineMinimumDistance,
                LastRouteCarAudioMix.EngineMaximumDistance,
                LastRouteCarAudioMix.EnginePriority);
            engineSource.loop = true;
            engineSource.clip = GetLoopClip(LastRouteCarLoopKind.Engine);
            engineSource.pitch = LastRouteCarAudioMix.EnginePitchAtZero;
            engineTone = engineAnchor.gameObject
                .AddComponent<AudioLowPassFilter>();
            engineTone.cutoffFrequency =
                LastRouteCarAudioMix.EngineClosedCutoff;
            engineTone.lowpassResonanceQ = 1f;
            engineRoom = CreateRoom(engineAnchor.gameObject);

            cabinSource = cabinAnchor.gameObject.AddComponent<AudioSource>();
            ConfigureSource(cabinSource, LastRouteCarAudioMix.CabinPriority);
            cabinSource.spatialBlend = 0f;
            cabinSource.loop = true;
            cabinSource.clip = GetLoopClip(LastRouteCarLoopKind.Cabin);
            cabinSource.pitch = LastRouteCarAudioMix.CabinPitchAtZero;

            cueSource = cabinAnchor.gameObject.AddComponent<AudioSource>();
            ConfigureSource(cueSource, LastRouteCarAudioMix.CuePriority);
            cueSource.spatialBlend = 1f;
            cueSource.rolloffMode = AudioRolloffMode.Linear;
            cueSource.minDistance = LastRouteCarAudioMix.TyreMinimumDistance;
            cueSource.maxDistance = LastRouteCarAudioMix.TyreMaximumDistance;
            cueSource.loop = false;
            cueSource.volume = 1f;

            tyreSource = axleAnchor.gameObject.AddComponent<AudioSource>();
            ConfigureSpatialSource(
                tyreSource,
                LastRouteCarAudioMix.TyreMinimumDistance,
                LastRouteCarAudioMix.TyreMaximumDistance,
                LastRouteCarAudioMix.TyrePriority);
            tyreSource.loop = true;
            tyreSource.clip = GetLoopClip(LastRouteCarLoopKind.Tyres);
            tyreTone = axleAnchor.gameObject.AddComponent<AudioLowPassFilter>();
            tyreTone.cutoffFrequency =
                LastRouteCarAudioMix.WetAsphaltCutoff;
            tyreTone.lowpassResonanceQ = 1f;
            axleRoom = CreateRoom(axleAnchor.gameObject);

            deckSource = axleAnchor.gameObject.AddComponent<AudioSource>();
            ConfigureSpatialSource(
                deckSource,
                LastRouteCarAudioMix.TyreMinimumDistance,
                LastRouteCarAudioMix.TyreMaximumDistance,
                LastRouteCarAudioMix.DeckPriority);
            deckSource.loop = true;
            deckSource.clip = GetLoopClip(LastRouteCarLoopKind.BridgeDeck);

            ownedSources.Add(engineSource);
            ownedSources.Add(cabinSource);
            ownedSources.Add(cueSource);
            ownedSources.Add(tyreSource);
            ownedSources.Add(deckSource);
            ApplyRoom(0f);
            IsInitialized = true;
        }

        /// <summary>
        /// Tells the voice what to listen to. Everything is optional: a car
        /// with only a driver bound still starts when the road starts, and
        /// a car with nothing bound is a parked car.
        /// </summary>
        public void Bind(
            LastRouteCarSeatInteraction carSeat,
            LastRouteFerrymanPresentation ferrymanPresentation,
            LastRouteRideController rideController,
            Func<bool> enclosedPredicate,
            LastRouteCarRoadSurface roadSurface,
            MountainRoadWindSoundPlayer wind = null)
        {
            seat = carSeat;
            ferryman = ferrymanPresentation;
            ride = rideController;
            isEnclosed = enclosedPredicate;
            surface = roadSurface;
            windBed = wind;
            if (tyreTone != null)
            {
                tyreTone.cutoffFrequency =
                    LastRouteCarAudioMix.EvaluateTyreCutoff(surface);
            }
        }

        /// <summary>
        /// The mountain's wind bed, which may be raised after the car is:
        /// whichever comes second binds it.
        /// </summary>
        public void BindWindBed(MountainRoadWindSoundPlayer wind)
        {
            windBed = wind;
        }

        /// <summary>
        /// The bridge, by its two abutments in world space. Resolved onto
        /// the car's own road the first frame it has one, because the
        /// path is built lazily and the deck's distance along it is only
        /// meaningful against that path.
        /// </summary>
        public void SetDeck(Vector3 start, Vector3 end)
        {
            hasDeck = true;
            deckStartWorld = start;
            deckEndWorld = end;
            deckPath = null;
        }

        /// <summary>
        /// Whether the block should be turning right now: the man is at the
        /// wheel and the journey is not over, or the road itself is
        /// running. Polled rather than raised, the headlights' rule: a
        /// "began" event fired at a car that then failed to move would
        /// leave the engine running on a parked car.
        /// </summary>
        public bool IsEngineWanted
        {
            get
            {
                bool driving = driver != null && driver.IsDriving;
                bool awaiting = ride != null && ride.IsAwaitingStart;
                bool arrived = driver != null && driver.HasArrived;
                bool atTheWheel = ferryman != null &&
                                  ferryman.IsDriving &&
                                  !arrived;
                return driving || awaiting || atTheWheel;
            }
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            float step = Mathf.Min(Time.deltaTime, MaximumStepSeconds);
            UpdateIgnition();
            ReadRoad(
                out float speed,
                out float acceleration,
                out float grade,
                out float distance);
            engine.Advance(step, speed, acceleration, grade);
            UpdateBlends(step);
            UpdateDeck(step, distance, speed);
            UpdateDoors();
            ApplyVoices(speed);
            lastDistance = distance;
            if (windBed != null)
            {
                windBed.SetEnclosure(cabinBlend);
            }
        }

        private void UpdateIgnition()
        {
            bool wanted = IsEngineWanted;
            if (wanted && !engine.IsAudible)
            {
                // Already rolling means it never stopped: the mountain leg,
                // or a ride that began before this voice was bound.
                bool alreadyRunning =
                    (driver != null && driver.IsDriving) ||
                    (ride != null && ride.IsAwaitingStart);
                engine.Start(alreadyRunning);
                if (!alreadyRunning)
                {
                    PlayCue(LastRouteCarCueKind.Starter);
                    StarterCueCount++;
                }

                return;
            }

            if (!wanted &&
                (engine.Phase == LastRouteCarEnginePhase.Running ||
                 engine.Phase == LastRouteCarEnginePhase.Starting))
            {
                engine.Stop();
                PlayCue(LastRouteCarCueKind.Shutdown);
                ShutdownCueCount++;
            }
        }

        private void ReadRoad(
            out float speed,
            out float acceleration,
            out float grade,
            out float distance)
        {
            speed = 0f;
            acceleration = 0f;
            grade = 0f;
            distance = lastDistance;
            if (driver == null || !driver.IsDriving || driver.Model == null)
            {
                return;
            }

            LastRouteCarDriveModel model = driver.Model;
            speed = model.Speed;
            acceleration = model.LongitudinalAcceleration;
            distance = model.Distance;
            model.Evaluate(out _, out Vector3 forward);
            float run = Mathf.Sqrt(
                (forward.x * forward.x) + (forward.z * forward.z));
            grade = run > 0.001f ? forward.y / run : 0f;
        }

        private void UpdateBlends(float step)
        {
            bool inside = seat != null &&
                          (seat.IsSeated || seat.IsAttachedToCar);
            cabinBlend = Mathf.MoveTowards(
                cabinBlend,
                inside ? 1f : 0f,
                step / LastRouteCarAudioMix.CabinBlendSeconds);

            bool enclosed = isEnclosed != null && isEnclosed();
            float previousEnclosure = enclosure;
            enclosure = Mathf.MoveTowards(
                enclosure,
                enclosed ? 1f : 0f,
                step / LastRouteCarAudioMix.EnclosureBlendSeconds);
            if (!enclosure.Equals(previousEnclosure))
            {
                ApplyRoom(enclosure);
            }
        }

        private void UpdateDeck(float step, float distance, float speed)
        {
            bool onDeck = false;
            if (hasDeck && driver != null && driver.Model != null)
            {
                LastRouteCarDrivePath path = driver.Model.Path;
                if (!ReferenceEquals(path, deckPath))
                {
                    deckPath = path;
                    deckStartDistance = path.FindNearestDistance(deckStartWorld);
                    deckEndDistance = path.FindNearestDistance(deckEndWorld);
                    if (deckEndDistance < deckStartDistance)
                    {
                        (deckStartDistance, deckEndDistance) =
                            (deckEndDistance, deckStartDistance);
                    }
                }

                onDeck = driver.IsDriving &&
                         distance >= deckStartDistance &&
                         distance <= deckEndDistance;
                bool jumped = Mathf.Abs(distance - lastDistance) >
                              DeckSkipTolerance;
                if (onDeck != wasOnDeck && !jumped && driver.IsDriving)
                {
                    PlayCue(LastRouteCarCueKind.DeckJoint);
                    DeckJointCueCount++;
                }
            }

            wasOnDeck = onDeck;
            deckBlend = Mathf.MoveTowards(
                deckBlend,
                onDeck ? 1f : 0f,
                step / LastRouteCarAudioMix.DeckBlendSeconds);
            deckSource.volume =
                LastRouteCarAudioMix.EvaluateDeckVolume(speed) * deckBlend;
            SyncLoop(deckSource, deckSource.volume > 0.001f);
        }

        private void UpdateDoors()
        {
            if (doors == null || !doors.IsInitialized)
            {
                return;
            }

            UpdateLatch(doors.DriverOpenness, ref driverLeafArmed);
            UpdateLatch(doors.PassengerOpenness, ref passengerLeafArmed);
        }

        private void UpdateLatch(float openness, ref bool armed)
        {
            if (openness > DoorLatchArmOpenness)
            {
                armed = true;
                return;
            }

            if (armed && openness < DoorLatchOpenness)
            {
                armed = false;
                PlayCue(LastRouteCarCueKind.DoorLatch);
                DoorLatchCueCount++;
            }
        }

        private void ApplyVoices(float speed)
        {
            float rpm = engine.Rpm01;
            float load = engine.Load01;
            bool audible = engine.IsAudible;
            bool cranking =
                engine.Phase == LastRouteCarEnginePhase.Starting &&
                engine.PhaseSeconds < LastRouteCarEngineModel.StarterSeconds;

            float exterior = audible
                ? LastRouteCarAudioMix.EvaluateEngineVolume(rpm, load) *
                  Mathf.Lerp(
                      1f,
                      LastRouteCarAudioMix.ExteriorCabinMultiplier,
                      cabinBlend) *
                  (cranking
                      ? LastRouteCarAudioMix.CrankingLoopMultiplier
                      : 1f)
                : 0f;
            engineSource.pitch = LastRouteCarAudioMix.EvaluateEnginePitch(rpm);
            engineSource.volume = exterior;
            engineTone.cutoffFrequency =
                LastRouteCarAudioMix.EvaluateEngineCutoff(load);
            SyncLoop(engineSource, audible);

            cabinSource.pitch = LastRouteCarAudioMix.EvaluateCabinPitch(rpm);
            cabinSource.volume = audible
                ? LastRouteCarAudioMix.EvaluateCabinVolume(rpm, load) *
                  cabinBlend
                : 0f;
            SyncLoop(cabinSource, audible && cabinBlend > 0.001f);

            float tyres = LastRouteCarAudioMix.EvaluateTyreVolume(
                              speed,
                              surface) *
                          Mathf.Lerp(1f, 0.8f, cabinBlend);
            tyreSource.volume = tyres;
            tyreTone.cutoffFrequency =
                LastRouteCarAudioMix.EvaluateTyreCutoff(surface) *
                Mathf.Lerp(1f, 0.6f, cabinBlend);
            SyncLoop(tyreSource, tyres > 0.001f);
        }

        private void PlayCue(LastRouteCarCueKind kind)
        {
            AudioClip clip = GetCueClip(kind);
            if (clip == null || !Application.isPlaying)
            {
                return;
            }

            cueSource.PlayOneShot(clip, LastRouteCarAudioMix.CueVolume);
        }

        private static void SyncLoop(AudioSource source, bool wanted)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (wanted && !source.isPlaying)
            {
                source.Play();
            }
            else if (!wanted && source.isPlaying)
            {
                source.Stop();
            }
        }

        /// <summary>
        /// The tunnel closing round the car. A late slap and a longish
        /// decay off hard walls, faded in by room level rather than by
        /// switching the filter, so driving under the portal is a swell
        /// and not a click.
        /// </summary>
        private void ApplyRoom(float amount)
        {
            ApplyRoom(engineRoom, amount);
            ApplyRoom(axleRoom, amount);
        }

        private static void ApplyRoom(AudioReverbFilter room, float amount)
        {
            if (room == null)
            {
                return;
            }

            float wet = Mathf.Clamp01(amount);
            room.room = Mathf.Lerp(-10000f, -1200f, wet);
            room.reflectionsLevel = Mathf.Lerp(-10000f, -900f, wet);
            room.reverbLevel = Mathf.Lerp(-10000f, -400f, wet);
        }

        private static AudioReverbFilter CreateRoom(GameObject owner)
        {
            AudioReverbFilter room = owner.AddComponent<AudioReverbFilter>();
            room.reverbPreset = AudioReverbPreset.User;
            room.dryLevel = 0f;
            room.roomHF = -800f;
            room.decayTime = 1.7f;
            room.decayHFRatio = 0.6f;
            room.reflectionsDelay = 0.02f;
            room.reverbDelay = 0.03f;
            room.diffusion = 100f;
            room.density = 100f;
            return room;
        }

        private Vector3 ResolveCabinLocalPosition(
            LastRouteCarDimensions dimensions)
        {
            Transform driverSeat = registry.DriverSeatAnchor;
            Transform passengerSeat = registry.PassengerSeatAnchor;
            if (driverSeat != null && passengerSeat != null)
            {
                Vector3 middle = (driverSeat.position + passengerSeat.position) *
                                 0.5f;
                return transform.InverseTransformPoint(middle) +
                       (Vector3.up * 0.35f);
            }

            return new Vector3(
                0f,
                Mathf.Clamp(dimensions.Height * 0.6f, 0.6f, 1.2f),
                -0.1f);
        }

        private Transform CreateAnchor(string anchorName, Vector3 localPosition)
        {
            Transform anchor = new GameObject(anchorName).transform;
            anchor.SetParent(transform, false);
            anchor.localPosition = localPosition;
            anchor.localRotation = Quaternion.identity;
            anchor.localScale = Vector3.one;
            return anchor;
        }

        private static void ConfigureSpatialSource(
            AudioSource source,
            float minimumDistance,
            float maximumDistance,
            int priority)
        {
            ConfigureSource(source, priority);
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minimumDistance;
            source.maxDistance = maximumDistance;
        }

        private static void ConfigureSource(AudioSource source, int priority)
        {
            source.playOnAwake = false;
            source.dopplerLevel = 0f;
            source.spread = 0f;
            source.priority = priority;
            source.volume = 0f;
            GameAudioMixer.Route(source, GameAudioGroup.SfxWorld);
        }

        private static AudioClip GetLoopClip(LastRouteCarLoopKind kind)
        {
            int index = (int)kind;
            if (LoopClips[index] == null)
            {
                LoopClips[index] =
                    LastRouteCarSoundSynthesis.CreateLoopClip(kind);
            }

            return LoopClips[index];
        }

        private static AudioClip GetCueClip(LastRouteCarCueKind kind)
        {
            int index = (int)kind;
            if (CueClips[index] == null)
            {
                CueClips[index] =
                    LastRouteCarSoundSynthesis.CreateCueClip(kind);
            }

            return CueClips[index];
        }

        // Domain reload is disabled on entering play mode, so a static
        // field survives from one run to the next - and a cached
        // UnityEngine.Object survives as a DESTROYED one, which reads as
        // null-ish but throws on use. The bus resets its clips the same way.
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedClips()
        {
            Array.Clear(LoopClips, 0, LoopClips.Length);
            Array.Clear(CueClips, 0, CueClips.Length);
        }
    }
}
