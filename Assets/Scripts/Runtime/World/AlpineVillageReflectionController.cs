using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>One local spring environment, captured only after meaningful lighting drift.</summary>
    [DisallowMultipleComponent]
    public sealed class AlpineVillageReflectionController : SceneReflectionCapture
    {
        private static readonly int CubeId = Shader.PropertyToID("_ReflectionCube");
        private static readonly int StrengthId = Shader.PropertyToID("_ReflectionStrength");
        private static readonly int ReceiverId = Shader.PropertyToID("_SceneReflectionReceiver");
        private static readonly int DecodeId = Shader.PropertyToID("_SceneReflectionDecode");
        private static readonly int VillageCubeId = Shader.PropertyToID("_VillageReflectionCube");
        private static readonly int VillageDecodeId = Shader.PropertyToID("_VillageReflectionDecode");
        private static readonly int RegionId = Shader.PropertyToID("_VillageReflectionRegion");
        private static readonly int MinimumId = Shader.PropertyToID("_VillageReflectionMin");
        private static readonly int MaximumId = Shader.PropertyToID("_VillageReflectionMax");
        private static AlpineVillageReflectionController bindingOwner;
        private readonly List<LayerLease> layers = new List<LayerLease>();
        private AlpineVillageRoot owner;
        private SceneLightingPresentation presentation;
        private Material water;
        private Texture previousCube;
        private Vector4 previousDecode;
        private float previousStrength, previousReceiver, previousDistortion;
        private Color lightingReference, ambientReference;
        private Vector3 sunReference;
        private float nightReference, warmthReference;
        private uint lightingGeneration;
        private double lastChange;
        private Vector3 center;
        private Bounds wetBounds;
        private bool initialized;
        private struct LayerLease { internal GameObject Object; internal int Previous; }
        protected override SceneLightingPresentation Presentation => presentation;
        protected override bool CapturePermitted => owner != null && owner.IsInitialized && !owner.IsDormant;
        protected override bool StableForCapture => !CompositionDriver.IsComposing && !GameTimeScaleRuntime.IsPaused;

        public void Initialize(AlpineVillageRoot root)
        {
            owner = root;
            presentation = GetComponent<SceneLightingPresentation>();
            center = root.Plan.Brook.BowlCenter;
            center.y = root.Plan.Brook.BowlWaterTopY;
            wetBounds = new Bounds(center - Vector3.up * .18f, new Vector3(6f, 1.3f, 6f));
            water = AlpineSpringWaterResources.PoolMaterial;
            // A replacement can initialize while the outgoing capture still
            // owns this shared material. Retain the original baseline, never
            // the outgoing probe texture that its cleanup will destroy.
            bool replacingBinding = bindingOwner != null && bindingOwner.water == water;
            previousCube = replacingBinding ? bindingOwner.previousCube : water.GetTexture(CubeId);
            previousDecode = replacingBinding ? bindingOwner.previousDecode : water.GetVector(DecodeId);
            previousStrength = replacingBinding ? bindingOwner.previousStrength : water.GetFloat(StrengthId);
            previousReceiver = replacingBinding ? bindingOwner.previousReceiver : water.GetFloat(ReceiverId);
            previousDistortion = replacingBinding ? bindingOwner.previousDistortion : water.GetFloat("_ReflectionDistortion");
            // Mark only nearby static mesh objects; no hero, NPC, particles or water feedback.
            MeshRenderer[] renderers = root.World.Root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                if (renderer.bounds.SqrDistance(center) > 24f * 24f ||
                    renderer.GetComponentInParent<Rigidbody>() != null ||
                    renderer.GetComponentInParent<Animator>() != null) continue;
                Material material = renderer.sharedMaterial;
                if (material == null || material.shader == null ||
                    material.shader.name.Contains("Water") || material.renderQueue >= 3000) continue;
                layers.Add(new LayerLease { Object = renderer.gameObject, Previous = renderer.gameObject.layer });
                renderer.gameObject.layer = CaptureLayer;
            }
            initialized = true;
            InitializeCapture(new Bounds(center + Vector3.up * 4f, new Vector3(48f, 24f, 48f)),
                center + root.Plan.Brook.BowlOutletDirection * .25f + Vector3.up * 1.05f,
                1 << CaptureLayer, 24f, 16f, RenderSettings.fogColor,
                ReflectionProbeTimeSlicingMode.AllFacesAtOnce);
        }

        protected override uint ReadState()
        {
            Light sun = RenderSettings.sun;
            Color light = sun != null ? sun.color * sun.intensity : Color.black;
            Vector3 direction = sun != null ? sun.transform.forward : Vector3.down;
            Color ambient = RenderSettings.ambientLight * RenderSettings.ambientIntensity;
            float night = CityWaterResources.NightFactor;
            float warmth = owner != null ? owner.WarmthGrade : 0f;
            bool changed = lightingGeneration == 0 || Mathf.Abs(night - nightReference) >= .12f ||
                Mathf.Abs(warmth - warmthReference) >= .08f ||
                ColorDelta(light, lightingReference) > .12f || ColorDelta(ambient, ambientReference) > .08f ||
                Vector3.Dot(direction, sunReference) < .978f;
            // Compare to the retained reference, not rounded buckets: small oscillations cannot rearm captures.
            if (changed && (lightingGeneration == 0 || Time.realtimeSinceStartupAsDouble - lastChange >= 5d))
            {
                lightingGeneration++;
                lightingReference = light; ambientReference = ambient; sunReference = direction;
                nightReference = night; warmthReference = warmth;
                lastChange = Time.realtimeSinceStartupAsDouble;
            }
            return lightingGeneration;
        }

        private static float ColorDelta(Color a, Color b) =>
            Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));

        protected override void BeforeCapture() => SetCaptureBackground(RenderSettings.fogColor);

        protected override void Bind(bool requested)
        {
            bindingOwner = this;
            CurrentGain = requested && IsReady && CapturePermitted ? 1f : 0f;
            if (water != null)
            {
                water.SetTexture(CubeId, CurrentGain > 0f ? CapturedTexture : previousCube);
                water.SetVector(DecodeId, CapturedDecode);
                water.SetFloat(ReceiverId, 1f);
                water.SetFloat(StrengthId, .28f * CurrentGain);
                water.SetFloat("_ReflectionDistortion", .16f);
            }
            Shader.SetGlobalTexture(VillageCubeId, CapturedTexture);
            Shader.SetGlobalVector(VillageDecodeId, CapturedDecode);
            Shader.SetGlobalVector(RegionId, new Vector4(center.x, center.y, center.z, CurrentGain));
            Shader.SetGlobalVector(MinimumId, wetBounds.min);
            Shader.SetGlobalVector(MaximumId, wetBounds.max);
        }

        protected override void ClearBinding()
        {
            // An outgoing owner cannot erase its already-bound replacement.
            if (bindingOwner != this) return;
            bindingOwner = null;
            if (water != null)
            {
                water.SetTexture(CubeId, previousCube);
                water.SetVector(DecodeId, previousDecode);
                water.SetFloat(StrengthId, previousStrength);
                water.SetFloat(ReceiverId, previousReceiver);
                water.SetFloat("_ReflectionDistortion", previousDistortion);
            }
            Shader.SetGlobalVector(RegionId, Vector4.zero);
            Shader.SetGlobalTexture(VillageCubeId, null);
        }

        private void OnEnable()
        {
            if (!initialized) return;
            for (int i = 0; i < layers.Count; i++)
                if (layers[i].Object != null) layers[i].Object.layer = CaptureLayer;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            for (int i = 0; i < layers.Count; i++)
                if (layers[i].Object != null) layers[i].Object.layer = layers[i].Previous;
        }
    }
}
