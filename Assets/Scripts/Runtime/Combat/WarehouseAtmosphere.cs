using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Seeded, room-bounded dust and causal shedding; the warehouse owns their entire lifetime.</summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseAtmosphere : MonoBehaviour
    {
        public const int MaximumDustParticles = 192, MaximumBurstParticles = 32;
        public const float MaximumDebrisLifetime = 3.2f;
        private const float ParticleLifetime = 100000f, MotionClearance = .36f;
        private static Material sharedMaterial;
        private struct Mote
        {
            public Vector3 Anchor;
            public float Phase, Period, Size, Alpha;
            public bool Downwash;
        }
        private struct Grit
        {
            public Vector3 Position, Velocity;
            public float Floor, Remaining, Size, Alpha;
            public uint Seed;
        }
        private struct SheddingSource
        {
            public Vector3 Position;
            public float Period, Phase, Floor;
            public int Cycle;
        }

        private readonly Mote[] motes = new Mote[MaximumDustParticles];
        private readonly Grit[] grit = new Grit[MaximumBurstParticles];
        private readonly ParticleSystem.Particle[] dustParticles = new ParticleSystem.Particle[MaximumDustParticles];
        private readonly ParticleSystem.Particle[] debrisParticles = new ParticleSystem.Particle[MaximumBurstParticles];
        private readonly SheddingSource[] sources = new SheddingSource[3];
        private CombatTestRoot owner;
        private ParticleSystemRenderer dustRenderer, debrisRenderer;
        private int gritCount, sourceCount;
        private uint random;

        public bool IsInitialized { get; private set; }
        public double ElapsedSeconds { get; private set; }
        public int BurstEmissionCount { get; private set; }
        public ParticleSystem Dust { get; private set; }
        public ParticleSystem Debris { get; private set; }
        public WarehouseArenaMechanisms Mechanisms { get; private set; }

        public void Initialize(WarehouseArenaPlan plan, CombatTestRoot warehouse, WarehouseArenaMechanisms mechanisms)
        {
            if (IsInitialized) throw new InvalidOperationException("Warehouse atmosphere already initialized.");
            if (plan == null || warehouse == null || mechanisms == null) throw new ArgumentNullException();
            owner = warehouse;
            Mechanisms = mechanisms;
            random = unchecked((uint)plan.Seed) ^ 0x44555354u;
            Dust = CreateParticles("Warehouse suspended dust", MaximumDustParticles, out dustRenderer);
            Debris = CreateParticles("Warehouse mechanical shedding", MaximumBurstParticles, out debrisRenderer);
            // Each fixed world pocket stays clear of walls, decks, stairs and solid furniture.
            // Sampling uses its own stream, so atmosphere never changes the room or encounter seed.
            int index = 0;
            for (int level = 0; level < 3; level++)
            {
                float floor = level * WarehouseArenaPlan.LevelRise;
                AddPocket(plan, ref index, 20, new Vector3(0f, floor + 1.6f, 0f),
                    new Vector3(9f, 1.9f, 9f), true);
                AddPocket(plan, ref index, 18, new Vector3(0f, floor + 1.6f, -8.6f),
                    new Vector3(10f, 1.9f, 3f), false);
                AddPocket(plan, ref index, 8, new Vector3(0f, floor + 1.6f, 8.2f),
                    new Vector3(10f, 1.9f, 2.4f), false);
            }
            foreach (Vector3 room in plan.RoomCenters)
                AddPocket(plan, ref index, 4, room + Vector3.up * 1.6f, new Vector3(3.5f, 1.9f, 3.5f), false);
            AddPocket(plan, ref index, 18, new Vector3(0f, 8.7f, 0f), new Vector3(7f, .9f, 7f), true);
            if (index != MaximumDustParticles) throw new InvalidOperationException("Warehouse dust budget changed.");

            foreach (WarehouseArenaMechanism machine in plan.Mechanisms)
            {
                if (machine.Kind == "Fan") continue;
                Vector3 offset = machine.Kind == "Lift" ? new Vector3(1.85f, 8.88f, 0f) :
                    machine.Kind == "Drum" ? new Vector3(1.9f, 2.64f, .6f) : new Vector3(1.95f, 2.7f, .1f);
                Vector3 position = machine.Position + offset;
                float period = machine.Kind == "Lift" ? 12f : machine.Kind == "Drum" ? 13f : 4f;
                sources[sourceCount++] = new SheddingSource
                {
                    Position = position, Period = period, Phase = machine.Phase,
                    Floor = SupportingFloor(plan, position), Cycle = (int)((Mechanisms.MechanicalSeconds + machine.Phase) / period)
                };
            }
            IsInitialized = true;
            WriteDust();
            SetVisible(isActiveAndEnabled && owner.isActiveAndEnabled);
        }

        private void AddPocket(WarehouseArenaPlan plan, ref int index, int count, Vector3 center, Vector3 size, bool downwash)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 position = center;
                bool clear = false;
                for (int attempt = 0; attempt < 96; attempt++)
                {
                    position = center + new Vector3((Next() - .5f) * size.x, (Next() - .5f) * size.y, (Next() - .5f) * size.z);
                    if (!ClearPocket(plan, position)) continue;
                    clear = true;
                    break;
                }
                if (!clear) throw new InvalidOperationException("Warehouse dust pocket has no clear sample.");
                bool flake = index % 13 == 0;
                motes[index] = new Mote
                {
                    Anchor = position, Phase = Next() * Mathf.PI * 2f, Period = 8f + Next() * 6f,
                    Size = flake ? .085f + Next() * .04f : .025f + Next() * .04f,
                    Alpha = flake ? .17f : .35f + Next() * .25f, Downwash = downwash
                };
                dustParticles[index] = FixedParticle((uint)index + 1u);
                index++;
            }
        }

        private static bool ClearPocket(WarehouseArenaPlan plan, Vector3 position)
        {
            var motion = new Bounds(position, Vector3.one * (MotionClearance * 2f));
            foreach (WarehouseArenaObstacle obstacle in plan.Obstacles)
                if (motion.Intersects(obstacle.Bounds)) return false;
            foreach (WarehouseArenaSurface surface in plan.Surfaces)
                if (motion.min.x < surface.Bounds.xMax && motion.max.x > surface.Bounds.xMin &&
                    motion.min.z < surface.Bounds.yMax && motion.max.z > surface.Bounds.yMin &&
                    motion.min.y < surface.Height && motion.max.y > surface.Height - WarehouseArenaPlan.DeckThickness) return false;
            foreach (WarehouseArenaStair stair in plan.Stairs)
                if (position.x > stair.Bounds.xMin - MotionClearance && position.x < stair.Bounds.xMax + MotionClearance &&
                    position.z > stair.Bounds.yMin - MotionClearance && position.z < stair.Bounds.yMax + MotionClearance &&
                    motion.max.y > stair.Foot.y && motion.min.y < stair.Top.y + .2f) return false;
            return motion.min.y > .1f && motion.max.y < WarehouseArenaPlan.CeilingHeight - .15f;
        }

        private static float SupportingFloor(WarehouseArenaPlan plan, Vector3 position)
        {
            float floor = 0f;
            foreach (WarehouseArenaSurface surface in plan.Surfaces)
                if (surface.Height < position.y && surface.Bounds.Contains(new Vector2(position.x, position.z)))
                    floor = Mathf.Max(floor, surface.Height);
            return floor;
        }

        private void LateUpdate() => Advance(Time.deltaTime);

        public void Advance(float seconds)
        {
            if (!IsInitialized || !isActiveAndEnabled) return;
            bool running = owner != null && owner.isActiveAndEnabled;
            SetVisible(running);
            if (!running || GameTimeScaleRuntime.IsPaused || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            ElapsedSeconds += seconds;
            AdvanceGrit(seconds);
            for (int i = 0; i < sourceCount; i++)
            {
                ref SheddingSource source = ref sources[i];
                int cycle = (int)((Mechanisms.MechanicalSeconds + source.Phase) / source.Period);
                bool changed = cycle != source.Cycle;
                source.Cycle = cycle;
                // Background gaps and distant contacts never accumulate a burst backlog.
                if (!changed || seconds > .25f || Mechanisms.FanSpeedFactor <= 0f ||
                    (source.Period == 4f && cycle % 4 != 0) || owner.Hero == null ||
                    (owner.Hero.transform.position - source.Position).sqrMagnitude > 169f) continue;
                Shed(source);
            }
            WriteDust();
            for (int i = 0; i < gritCount; i++)
            {
                Grit grain = grit[i];
                debrisParticles[i] = FixedParticle(grain.Seed);
                debrisParticles[i].position = grain.Position;
                debrisParticles[i].startSize = grain.Size;
                float fade = Mathf.Clamp01(grain.Remaining / .7f) * Mathf.Clamp01((MaximumDebrisLifetime - grain.Remaining) / .18f);
                debrisParticles[i].startColor = new Color(.65f, .59f, .46f, grain.Alpha * fade);
            }
            Debris.SetParticles(debrisParticles, gritCount);
        }

        private void WriteDust()
        {
            float driftX = (float)(ElapsedSeconds * .13d % (Math.PI * 2d));
            float driftZ = (float)(ElapsedSeconds * .11d % (Math.PI * 2d));
            // Air retains a slow residual drift after the motor stops. Mixing
            // the two clocks preserves the powered trajectory and its continuous
            // shutdown integral instead of pinning an eddy at its final angle.
            double airSeconds = ElapsedSeconds * .25d + Mechanisms.MechanicalSeconds * .75d;
            float air = (float)(airSeconds % (Math.PI * 200d));
            for (int i = 0; i < motes.Length; i++)
            {
                Mote mote = motes[i];
                float age = (float)((ElapsedSeconds + mote.Phase * 4d) % mote.Period) / mote.Period;
                float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age * 7f)) *
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - age) * 7f));
                Vector3 drift = new Vector3(Mathf.Sin(driftX + mote.Phase) * .11f,
                    .14f - age * .28f, Mathf.Cos(driftZ + mote.Phase) * .11f);
                if (mote.Downwash)
                    drift += new Vector3(Mathf.Sin(air * .30f + mote.Phase) * .15f,
                        Mathf.Cos(air * .19f + mote.Phase) * .09f,
                        Mathf.Cos(air * .30f + mote.Phase) * .15f);
                dustParticles[i].position = mote.Anchor + drift;
                dustParticles[i].startSize = mote.Size;
                dustParticles[i].startColor = new Color(.70f, .66f, .55f, mote.Alpha * fade);
            }
            Dust.SetParticles(dustParticles, MaximumDustParticles);
        }

        private void AdvanceGrit(float seconds)
        {
            for (int i = gritCount - 1; i >= 0; i--)
            {
                ref Grit grain = ref grit[i];
                grain.Remaining -= seconds;
                grain.Position += grain.Velocity * seconds + Vector3.down * (.225f * seconds * seconds);
                grain.Velocity += Vector3.down * (.45f * seconds);
                if (grain.Remaining <= 0f || grain.Position.y < grain.Floor + .10f)
                    grit[i] = grit[--gritCount];
            }
        }

        private void Shed(SheddingSource source)
        {
            BurstEmissionCount++;
            for (int i = 0; i < 6; i++)
            {
                if (gritCount == MaximumBurstParticles)
                {
                    // Retire the oldest remaining grain; the pool never grows during play.
                    int oldest = 0;
                    for (int j = 1; j < gritCount; j++) if (grit[j].Remaining < grit[oldest].Remaining) oldest = j;
                    grit[oldest] = grit[--gritCount];
                }
                grit[gritCount++] = new Grit
                {
                    Position = source.Position + new Vector3((Next() - .5f) * .16f, 0f, (Next() - .5f) * .16f),
                    Velocity = new Vector3((Next() - .5f) * .07f, -.08f - Next() * .12f, (Next() - .5f) * .07f),
                    Floor = source.Floor, Remaining = MaximumDebrisLifetime,
                    Size = i == 0 ? .18f : .035f + Next() * .035f, Alpha = i == 0 ? .10f : .46f, Seed = random
                };
            }
        }

        private ParticleSystem CreateParticles(string name, int capacity, out ParticleSystemRenderer renderer)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            ParticleSystem particles = host.AddComponent<ParticleSystem>();
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.useAutoRandomSeed = false;
            particles.randomSeed = random == 0u ? 1u : random;
            var main = particles.main;
            main.playOnAwake = false; main.loop = false; main.maxParticles = capacity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.simulationSpeed = 0f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.startLifetime = ParticleLifetime; main.startSpeed = 0f; main.gravityModifier = 0f;
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.enabled = false;
            renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = SharedMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.None;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.renderingLayerMask = 1u;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.minParticleSize = 0f; renderer.maxParticleSize = .025f;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
            particles.Play(false);
            return particles;
        }

        private static ParticleSystem.Particle FixedParticle(uint seed) => new ParticleSystem.Particle
        { startLifetime = ParticleLifetime, remainingLifetime = ParticleLifetime, randomSeed = seed };

        private float Next()
        {
            random = unchecked(random * 1664525u + 1013904223u);
            return (random >> 8) * (1f / 16777216f);
        }

        private void SetVisible(bool visible)
        {
            if (dustRenderer != null && dustRenderer.forceRenderingOff == visible) dustRenderer.forceRenderingOff = !visible;
            if (debrisRenderer != null && debrisRenderer.forceRenderingOff == visible) debrisRenderer.forceRenderingOff = !visible;
        }

        private void OnEnable() { if (IsInitialized) SetVisible(owner != null && owner.isActiveAndEnabled); }
        private void OnDisable() => SetVisible(false);

        private static Material SharedMaterial
        {
            get
            {
                if (sharedMaterial != null) return sharedMaterial;
                Shader shader = Resources.Load<Shader>("Shaders/WarehouseAtmosphereParticle");
                if (shader == null) throw new InvalidOperationException("Missing warehouse atmosphere particle shader.");
                sharedMaterial = new Material(shader) { name = "Shared warehouse airborne dust", hideFlags = HideFlags.HideAndDontSave };
                return sharedMaterial;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            if (sharedMaterial != null) Destroy(sharedMaterial);
            sharedMaterial = null;
        }
    }
}
