using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade.Rendering
{
    /// <summary>Shared opt-out copies keep native materials and their assets untouched.</summary>
    [DisallowMultipleComponent]
    public sealed class SceneScreenSpaceReflectionScope : MonoBehaviour
    {
        public const int MaximumStockMaterials = 1024;
        private const int MaximumRenderers = 65536;
        private const string Keyword = "_SCREENSPACEREFLECTIONS_OFF";
        private sealed class Binding
        {
            internal Renderer Renderer;
            internal Material[] Sources, Copies;
        }
        private readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();
        private readonly List<Binding> bindings = new List<Binding>();
        private readonly List<Material> shared = new List<Material>(8);
        private bool requested, applied, initialized;
        public SceneLightingPresentation Owner { get; private set; }
        public int StockMaterialCount => materials.Count;
        public bool IsApplied => applied;

        public void Initialize(SceneLightingPresentation owner)
        {
            if (initialized)
            {
                if (Owner != owner) throw new InvalidOperationException("SSR material scope cannot change owner.");
                return;
            }
            Owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            Transform root = owner is CityLightingPresentation city ? city.SourceRoot : owner.transform;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > MaximumRenderers)
                throw new InvalidOperationException("SSR receiver scan exceeds 65536 renderers.");
            try
            {
                foreach (Renderer renderer in renderers)
                {
                    renderer.GetSharedMaterials(shared);
                    Material[] sources = null, copies = null;
                    for (int slot = 0; slot < shared.Count; slot++)
                    {
                        Material source = shared[slot];
                        if (source == null || source.shader == null || source.IsKeywordEnabled(Keyword)) continue;
                        string shader = source.shader.name;
                        if (shader != "Universal Render Pipeline/Lit" &&
                            shader != "Universal Render Pipeline/Simple Lit" &&
                            shader != "Universal Render Pipeline/Complex Lit") continue;
                        if (!materials.TryGetValue(source, out Material copy))
                        {
                            if (materials.Count >= MaximumStockMaterials)
                                throw new InvalidOperationException("SSR stock material budget exceeds 1024.");
                            copy = new Material(source) { name = source.name + " (SSR excluded)", hideFlags = HideFlags.DontSave };
                            copy.EnableKeyword(Keyword);
                            materials.Add(source, copy);
                        }
                        if (sources == null) { sources = shared.ToArray(); copies = shared.ToArray(); }
                        copies[slot] = copy;
                    }
                    if (sources != null) bindings.Add(new Binding { Renderer = renderer, Sources = sources, Copies = copies });
                }
                initialized = true;
            }
            catch { ReleaseCopies(); throw; }
        }

        public void SetRequested(bool value)
        {
            requested = value;
            Apply(value && isActiveAndEnabled && Owner != null && Owner.isActiveAndEnabled &&
                GraphicsEffectsSettings.CleanReflectionsEnabled);
        }

        private void LateUpdate() => SetRequested(requested);

        private void Apply(bool value)
        {
            if (applied == value) return;
            foreach (Binding binding in bindings)
            {
                if (binding.Renderer == null) continue;
                binding.Renderer.GetSharedMaterials(shared);
                bool changed = false;
                for (int slot = 0; slot < shared.Count && slot < binding.Sources.Length; slot++)
                {
                    Material expected = value ? binding.Sources[slot] : binding.Copies[slot];
                    Material replacement = value ? binding.Copies[slot] : binding.Sources[slot];
                    // An interaction may have acquired a different material meanwhile.
                    if (expected == replacement || shared[slot] != expected) continue;
                    shared[slot] = replacement;
                    changed = true;
                }
                if (changed) binding.Renderer.SetSharedMaterials(shared);
            }
            applied = value;
        }

        private void ReleaseCopies()
        {
            Apply(false);
            foreach (Material copy in materials.Values) if (copy != null) Destroy(copy);
            materials.Clear(); bindings.Clear(); shared.Clear();
        }
        private void OnDisable() => Apply(false);
        private void OnDestroy() => ReleaseCopies();
    }
}
