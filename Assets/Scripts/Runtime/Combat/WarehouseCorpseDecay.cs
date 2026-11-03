using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>Finite, independent remains. Meshes are captured once, only after physics rests.</summary>
    internal sealed class WarehouseCorpseDecay : IDisposable
    {
        internal const int Capacity = 8;
        internal const float HoldSeconds = 6f, DissolveSeconds = 3.5f, TailSeconds = 5f;
        private const int MaximumSurfaces = 512, FlakeCount = 32;
        private static readonly int DecayId = Shader.PropertyToID("_CorpseDecay");
        private static readonly int SeedId = Shader.PropertyToID("_CorpseSeed");
        private sealed class Surface
        {
            internal MeshRenderer Renderer;
            internal MeshFilter Filter;
            internal Mesh Mesh;
            internal readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
            internal MaterialPropertyBlock[] Slots;
            internal Material[] Materials;
            internal CombineInstance[] Combination;
            internal bool Residue;
            internal Bounds Bounds;
        }
        private sealed class Remains
        {
            internal Transform Root;
            internal CombatActor Source;
            internal readonly List<Surface> Surfaces = new List<Surface>();
            internal int Used;
            internal float Age;
            internal float LastProgress = -1f, LastResidue = -1f;
            internal bool Active, SoundStarted;
            internal uint Seed;
            internal Bounds Bounds;
            internal AudioSource Sound;
            internal ParticleSystem Flakes;
            internal readonly ParticleSystem.Particle[] Particles = new ParticleSystem.Particle[FlakeCount];
            internal readonly Vector3[] FlakeOrigins = new Vector3[FlakeCount];
            internal readonly float[] FlakeFloors = new float[FlakeCount];
            internal Color FlakeColor;
        }
        private readonly Remains[] pool = new Remains[Capacity];
        private readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();
        private readonly List<Renderer> sources = new List<Renderer>(MaximumSurfaces);
        private readonly List<Renderer> props = new List<Renderer>(8);
        private readonly List<Material> sourceMaterials = new List<Material>(8);
        private RaycastHit[] supportHits = new RaycastHit[32];
        private readonly CombatTestRoot owner;
        private readonly Shader shader;
        private readonly Material flakeMaterial;
        private readonly AudioClip crumble;
        private uint sequence;
        private bool paused;
        internal int ActiveCount { get; private set; }
        internal bool HasSpace => ActiveCount < Capacity;

        internal WarehouseCorpseDecay(CombatTestRoot owner)
        {
            this.owner = owner;
            shader = Resources.Load<Shader>("Shaders/WarehouseCorpseSurface");
            if (shader == null) throw new InvalidOperationException("Missing warehouse corpse surface shader.");
            flakeMaterial = new Material(Resources.Load<Shader>("Shaders/WarehouseAtmosphereParticle"))
                { name = "Shared corpse flakes", hideFlags = HideFlags.HideAndDontSave };
            crumble = MakeCrumble();
            for (int i = 0; i < Capacity; i++)
            {
                var root = new GameObject("Warehouse remains " + i);
                root.transform.SetParent(owner.transform, false);
                var remains = new Remains { Root = root.transform };
                var sound = new GameObject("Material collapse sound"); sound.transform.SetParent(root.transform, false);
                remains.Sound = sound.AddComponent<AudioSource>();
                remains.Sound.playOnAwake = false;
                CityWorkAudio.Configure(remains.Sound, true, 4500f, 12f, false);
                remains.Sound.minDistance = 1.5f; remains.Sound.volume = .32f;
                remains.Sound.clip = crumble;
                remains.Flakes = MakeFlakes(root.transform);
                root.SetActive(false);
                pool[i] = remains;
            }
        }

        internal bool TryGetSnapshot(CombatActor source, out Transform root, out float progress)
        {
            foreach (Remains remains in pool)
                if (remains.Active && remains.Source == source)
                { root = remains.Root; progress = Mathf.Clamp01((remains.Age - HoldSeconds) / DissolveSeconds); return true; }
            root = null; progress = 0f; return false;
        }

        internal bool Capture(CombatActor actor)
        {
            Remains target = null;
            foreach (Remains remains in pool) if (!remains.Active) { target = remains; break; }
            if (target == null) return false;
            sources.Clear();
            actor.GetComponentsInChildren(false, sources);
            if (actor.Weapon != null && !actor.Weapon.transform.IsChildOf(actor.transform))
            {
                actor.Weapon.GetComponentsInChildren(false, props);
                sources.AddRange(props);
            }
            owner.BodyEffects.CollectCorpseRenderers(actor, sources);
            owner.HeadEffects.CollectCorpseRenderers(actor, sources);
            actor.CollectCorpsePropRenderers(sources);
            int bodyCount = sources.Count;
            owner.BloodEffects.CollectCorpsePoolRenderers(actor, sources);
            int visible = 0;
            foreach (Renderer source in sources) if (VisibleMesh(source)) visible++;
            // Keep the original body until a complete representation fits; never truncate remains.
            if (visible == 0 || visible > MaximumSurfaces) return false;
            target.Root.position = actor.BodyWorldPosition;
            target.Source = actor; target.Used = 0; target.Age = 0f; target.SoundStarted = false;
            target.LastProgress = target.LastResidue = -1f;
            float seed = ++sequence * .731f;
            target.Seed = sequence;
            bool measured = false;
            for (int i = 0; i < sources.Count; i++)
            {
                Renderer source = sources[i];
                if (!VisibleMesh(source)) continue;
                Surface surface = RequireSurface(target, target.Used++);
                surface.Residue = i >= bodyCount;
                CaptureSurface(source, surface, seed, target.Root);
                if (!surface.Residue)
                {
                    if (!measured) { target.Bounds = source.bounds; measured = true; }
                    else target.Bounds.Encapsulate(source.bounds);
                }
            }
            target.Sound.transform.position = actor.BodyWorldPosition + Vector3.up * .15f;
            target.Sound.pitch = actor.IsDocker ? .84f : 1f;
            target.FlakeColor = actor.IsDocker ? new Color(.16f, .12f, .10f, 1f) : new Color(.32f, .29f, .24f, 1f);
            PrepareFlakes(target);
            target.Root.gameObject.SetActive(true);
            target.Active = true; ActiveCount++;
            Present(target);
            sources.Clear(); props.Clear();
            return true;
        }

        private static bool VisibleMesh(Renderer source) => source != null && source.enabled &&
            source.gameObject.activeInHierarchy && !source.forceRenderingOff &&
            (source is SkinnedMeshRenderer skin ? skin.sharedMesh != null :
                source is MeshRenderer && source.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null);

        private static Surface RequireSurface(Remains remains, int index)
        {
            if (index < remains.Surfaces.Count) return remains.Surfaces[index];
            var host = new GameObject("Settled surface " + index);
            host.transform.SetParent(remains.Root, false);
            var surface = new Surface { Renderer = host.AddComponent<MeshRenderer>(), Filter = host.AddComponent<MeshFilter>(),
                Mesh = new Mesh { name = "Reusable corpse pose", indexFormat = IndexFormat.UInt32 } };
            surface.Mesh.MarkDynamic();
            remains.Surfaces.Add(surface);
            return surface;
        }

        private void CaptureSurface(Renderer source, Surface surface, float seed, Transform parent)
        {
            Transform placed = surface.Renderer.transform, original = source.transform;
            placed.SetParent(null, false);
            placed.SetPositionAndRotation(original.position, original.rotation);
            placed.localScale = original.lossyScale;
            placed.SetParent(parent, true);
            // Match the destruction owners' scale-aware bake. Imported rigs keep
            // their unit factor on the renderer; ignoring it bakes metre-sized
            // vertices that would then be scaled a second time by the frozen host.
            if (source is SkinnedMeshRenderer skin)
            { skin.BakeMesh(surface.Mesh, true); surface.Filter.sharedMesh = surface.Mesh; }
            else
            {
                Mesh sourceMesh = source.GetComponent<MeshFilter>().sharedMesh;
                if (sourceMesh.isReadable)
                {
                    surface.Mesh.Clear();
                    // Preserve submeshes; mutable detached surfaces belong to the source actor.
                    if (surface.Combination == null || surface.Combination.Length != sourceMesh.subMeshCount)
                        surface.Combination = new CombineInstance[sourceMesh.subMeshCount];
                    var parts = surface.Combination;
                    for (int i = 0; i < parts.Length; i++) parts[i] = new CombineInstance { mesh = sourceMesh, subMeshIndex = i, transform = Matrix4x4.identity };
                    surface.Mesh.CombineMeshes(parts, false, false);
                    surface.Filter.sharedMesh = surface.Mesh;
                }
                else surface.Filter.sharedMesh = sourceMesh; // Immutable imported GPU mesh, never read on the CPU.
            }
            source.GetSharedMaterials(sourceMaterials);
            if (surface.Materials == null || surface.Materials.Length != sourceMaterials.Count)
                surface.Materials = new Material[sourceMaterials.Count];
            var converted = surface.Materials;
            for (int i = 0; i < converted.Length; i++) converted[i] = ConvertMaterial(sourceMaterials[i]);
            surface.Renderer.sharedMaterials = converted;
            source.GetPropertyBlock(surface.Block);
            surface.Block.SetFloat(DecayId, 0f); surface.Block.SetFloat(SeedId, seed);
            surface.Renderer.SetPropertyBlock(surface.Block);
            if (surface.Slots == null || surface.Slots.Length != converted.Length)
                surface.Slots = new MaterialPropertyBlock[converted.Length];
            for (int i = 0; i < converted.Length; i++)
            {
                surface.Renderer.SetPropertyBlock(null, i);
                var block = surface.Slots[i] ?? (surface.Slots[i] = new MaterialPropertyBlock());
                source.GetPropertyBlock(block, i);
                if (block.isEmpty) continue;
                block.SetFloat(DecayId, 0f); block.SetFloat(SeedId, seed);
                surface.Slots[i] = block; surface.Renderer.SetPropertyBlock(block, i);
            }
            surface.Renderer.shadowCastingMode = source.shadowCastingMode;
            surface.Renderer.receiveShadows = source.receiveShadows;
            surface.Renderer.renderingLayerMask = source.renderingLayerMask;
            surface.Renderer.name = source.name;
            surface.Bounds = source.bounds;
            surface.Renderer.gameObject.layer = source.gameObject.layer;
            surface.Renderer.enabled = true;
        }

        private Material ConvertMaterial(Material source)
        {
            if (materials.TryGetValue(source, out Material result)) return result;
            if (source.shader.name == "Bar Promenade/Combat Wound")
            {
                result = new Material(source) { name = "Corpse " + source.name, hideFlags = HideFlags.HideAndDontSave };
                materials.Add(source, result);
                return result;
            }
            result = new Material(shader) { name = "Corpse " + source.name, hideFlags = HideFlags.HideAndDontSave };
            result.CopyPropertiesFromMaterial(source);
            result.shader = shader;
            result.SetFloat("_CorpseDecay", 0f); result.SetFloat("_WarehouseAuxiliary", 0f);
            result.SetFloat("_AlphaClip", 1f); result.EnableKeyword("_ALPHATEST_ON");
            result.DisableKeyword("_PARALLAXMAP");
            if (!source.IsKeywordEnabled("_ALPHATEST_ON")) result.SetFloat("_Cutoff", 0f);
            materials.Add(source, result);
            return result;
        }

        internal void Tick(float seconds)
        {
            if (paused || seconds <= 0f) return;
            foreach (Remains remains in pool)
            {
                if (!remains.Active) continue;
                remains.Age += seconds;
                if (remains.Age >= HoldSeconds + DissolveSeconds + TailSeconds) { Clear(remains); continue; }
                Present(remains);
            }
        }

        private void Present(Remains remains)
        {
            float progress = Mathf.Clamp01((remains.Age - HoldSeconds) / DissolveSeconds);
            float residue = Mathf.Clamp01((remains.Age - HoldSeconds - DissolveSeconds) / TailSeconds);
            bool changed = !Mathf.Approximately(progress, remains.LastProgress) || !Mathf.Approximately(residue, remains.LastResidue);
            if (changed) for (int i = 0; i < remains.Used; i++)
            {
                Surface surface = remains.Surfaces[i];
                float decay = surface.Residue ? residue : progress;
                surface.Renderer.enabled = decay < 1f;
                surface.Block.SetFloat(DecayId, decay);
                surface.Renderer.SetPropertyBlock(surface.Block);
                for (int slot = 0; slot < surface.Slots.Length; slot++)
                {
                    MaterialPropertyBlock block = surface.Slots[slot];
                    if (block == null || block.isEmpty) continue;
                    block.SetFloat(DecayId, decay); surface.Renderer.SetPropertyBlock(block, slot);
                }
            }
            if (progress > 0f && !remains.SoundStarted)
            { remains.SoundStarted = true; remains.Sound.Play(); }
            if (progress > 0f) WriteFlakes(remains);
            remains.LastProgress = progress; remains.LastResidue = residue;
        }

        private void WriteFlakes(Remains remains)
        {
            int count = 0;
            float elapsed = remains.Age - HoldSeconds;
            for (int i = 0; i < FlakeCount; i++)
            {
                float since = elapsed - i * (DissolveSeconds / FlakeCount);
                if (since < 0f || since > 2.4f) continue;
                uint hash = unchecked((uint)i * 747796405u + remains.Seed * 2891336453u);
                float x = (hash & 1023) / 1023f;
                Vector3 p = remains.FlakeOrigins[i];
                p.y = Mathf.Max(remains.FlakeFloors[i], p.y - .18f * since - .65f * since * since);
                p.x += Mathf.Sin(i + since * 3f) * .035f;
                float size = .025f + x * .035f;
                Color tint = remains.FlakeColor; tint.a = Mathf.Clamp01((2.4f - since) * 2f);
                remains.Particles[count++] = new ParticleSystem.Particle { position = p, startSize = size,
                    startColor = tint,
                    rotation = i * 137f + since * (20f + x * 80f), startLifetime = 1000f, remainingLifetime = 1000f,
                    randomSeed = hash };
            }
            remains.Flakes.SetParticles(remains.Particles, count);
        }

        private void PrepareFlakes(Remains remains)
        {
            int bodySurfaces = remains.Used;
            while (bodySurfaces > 0 && remains.Surfaces[bodySurfaces - 1].Residue) bodySurfaces--;
            for (int i = 0; i < FlakeCount; i++)
            {
                uint hash = unchecked((uint)i * 747796405u + remains.Seed * 2891336453u);
                Surface surface = remains.Surfaces[(int)(hash % (uint)Mathf.Max(1, bodySurfaces))];
                Bounds bounds = surface.Bounds;
                Vector3 origin = bounds.center + Vector3.Scale(bounds.extents * .7f,
                    new Vector3((hash & 1023) / 511.5f - 1f, ((hash >> 10) & 1023) / 511.5f - 1f, ((hash >> 20) & 1023) / 511.5f - 1f));
                remains.FlakeOrigins[i] = origin;
                float floor = bounds.min.y;
                int count;
                do
                {
                    count = Physics.RaycastNonAlloc(origin + Vector3.up * .05f, Vector3.down,
                        supportHits, 3f, ~0, QueryTriggerInteraction.Ignore);
                    if (count < supportHits.Length) break;
                    Array.Resize(ref supportHits, supportHits.Length * 2);
                } while (true);
                float nearest = float.PositiveInfinity;
                for (int hitIndex = 0; hitIndex < count; hitIndex++)
                {
                    RaycastHit hit = supportHits[hitIndex];
                    if (hit.collider.attachedRigidbody != null || hit.collider.GetComponentInParent<CombatActor>() != null || hit.distance >= nearest) continue;
                    floor = hit.point.y + .008f; nearest = hit.distance;
                }
                remains.FlakeFloors[i] = floor;
            }
        }

        private ParticleSystem MakeFlakes(Transform parent)
        {
            var host = new GameObject("Material flakes"); host.transform.SetParent(parent, false);
            var particles = host.AddComponent<ParticleSystem>();
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false; main.maxParticles = FlakeCount; main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.simulationSpeed = 0f;
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.enabled = false;
            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = flakeMaterial; renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
            renderer.minParticleSize = 0f; renderer.maxParticleSize = .02f;
            particles.Play(false); particles.Pause(false);
            return particles;
        }

        internal void SetPaused(bool value)
        {
            if (paused == value) return;
            paused = value;
            foreach (Remains remains in pool)
                if (value) remains.Sound.Pause(); else remains.Sound.UnPause();
        }

        internal void Clear()
        { foreach (Remains remains in pool) Clear(remains); sources.Clear(); }
        private void Clear(Remains remains)
        {
            if (!remains.Active) return;
            remains.Sound.Stop(); remains.Flakes.Clear(false);
            for (int i = 0; i < remains.Used; i++) remains.Surfaces[i].Renderer.enabled = false;
            remains.Root.gameObject.SetActive(false); remains.Active = false; remains.Source = null; ActiveCount--;
        }

        public void Dispose()
        {
            Clear();
            foreach (Remains remains in pool)
            {
                foreach (Surface surface in remains.Surfaces) Object.Destroy(surface.Mesh);
                Object.Destroy(remains.Root.gameObject);
            }
            foreach (Material material in materials.Values) Object.Destroy(material);
            materials.Clear(); Object.Destroy(flakeMaterial); Object.Destroy(crumble);
        }

        private static AudioClip MakeCrumble()
        {
            const int rate = 16000;
            var samples = new float[(int)(DissolveSeconds * rate)];
            uint seed = 0x53455454u; float low = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                seed = unchecked(seed * 1664525u + 1013904223u);
                float noise = (seed >> 8) / 8388608f - 1f;
                low += .065f * (noise - low);
                float time = i / (float)rate;
                float envelope = Mathf.Sin(Mathf.PI * i / samples.Length);
                float tear = Mathf.Exp(-time * 14f) * Mathf.Sin(time * 570f) * .18f;
                samples[i] = tear + (low * .65f + noise * .035f) * envelope * (.6f + .4f * Mathf.Sin(time * 37f));
            }
            AudioClip clip = AudioClip.Create("Material settling and tearing", samples.Length, 1, rate, false);
            clip.SetData(samples, 0); return clip;
        }
    }
}
