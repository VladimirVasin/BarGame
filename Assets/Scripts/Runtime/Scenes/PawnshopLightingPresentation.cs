using System;
using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Two source-derived floor bounces, bounded by the ordinary room partition.</summary>
    [DisallowMultipleComponent]
    public sealed class PawnshopLightingPresentation : SceneLightingPresentation
    {
        private const int PatchCount = 2;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private readonly Vector4[] centers = new Vector4[4], colors = new Vector4[4], minima = new Vector4[4],
            maxima = new Vector4[4], normals = new Vector4[4];
        private readonly Light[] sources = new Light[PatchCount];
        private readonly float[] visibility = new float[PatchCount];
        private PawnshopInteriorRoot root;
        private Color floorPigment;
        private float refreshAt;
        private bool initialized, appliedBounce;
        public PawnshopReflectionController Reflection { get; private set; }
        public int BounceRegionCount => initialized ? PatchCount : 0;
        public override Light VolumeLight => null;
        public override int HalationSourceCount => 0;

        public void Initialize(PawnshopInteriorRoot owner)
        {
            root = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            Renderer floor = null;
            foreach (var renderer in owner.Model.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name == "PART_Floor") { floor = renderer; break; }
            if (floor == null) throw new InvalidOperationException("Pawnshop bounce requires its visible floor surface.");
            var properties = new MaterialPropertyBlock();
            floor.GetPropertyBlock(properties);
            Material material = floor.sharedMaterial;
            floorPigment = (properties.HasColor(BaseColorId) ? properties.GetColor(BaseColorId) :
                material != null && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.black).linear * .6f;
            float top = floor.bounds.max.y + .025f;
            // The patches and receiving regions are clipped on their own side of
            // the counter/partition. They never approximate light through a wall.
            Patch(0, owner.Lights[0], new Vector3(0f, top, -2.65f),
                new Vector3(-1.4f, 0f, -3.75f), new Vector3(1.4f, 1.65f, -.4f));
            Patch(1, owner.Lights[2], new Vector3(1.2f, top, 2.3f),
                new Vector3(.4f, 0f, 1.2f), new Vector3(2.4f, 1.65f, 3.6f));
            initialized = true;
            RefreshVisibility();
            Reflection = gameObject.AddComponent<PawnshopReflectionController>();
            Reflection.Initialize(root, this);

            void Patch(int index, Light source, Vector3 point, Vector3 min, Vector3 max)
            {
                sources[index] = source;
                Vector3 centre = root.World.Root.TransformPoint(point);
                centers[index] = new Vector4(centre.x, centre.y, centre.z, 1.6f);
                minima[index] = root.World.Root.TransformPoint(min);
                maxima[index] = root.World.Root.TransformPoint(max);
                normals[index] = Vector3.up;
            }
        }

        public override bool IsForCamera(Camera camera) => initialized && isActiveAndEnabled &&
            root != null && root.isActiveAndEnabled && camera != null && camera.cameraType == CameraType.Game &&
            camera.gameObject.scene == root.gameObject.scene && !camera.TryGetComponent(out ScenePresentationExclusion _);

        public override void PrepareForCamera(Camera camera)
        {
            bool eligible = IsForCamera(camera);
            Reflection?.Prepare(eligible && GraphicsEffectsSettings.CleanReflectionsEnabled);
            if (!eligible) { Shader.SetGlobalInt("_WarehouseBounceCount", 0); return; }
            bool enabled = GraphicsEffectsSettings.CleanBounceEnabled;
            if (appliedBounce != enabled || !GameTimeScaleRuntime.IsPaused && Time.unscaledTime >= refreshAt)
                RefreshVisibility();
            for (int i = 0; i < PatchCount; i++)
            {
                Light source = sources[i];
                Vector3 delta = (Vector3)centers[i] - source.transform.position;
                float energy = enabled && source.isActiveAndEnabled ? Mathf.Min(.07f,
                    source.intensity * .18f * Mathf.Max(0f, -delta.normalized.y) / (1f + delta.sqrMagnitude)) * visibility[i] : 0f;
                Color radiance = source.color.linear * floorPigment * energy;
                colors[i] = new Vector4(radiance.r, radiance.g, radiance.b, 0f);
            }
            Shader.SetGlobalInt("_WarehouseBounceCount", enabled ? PatchCount : 0);
            Shader.SetGlobalVectorArray("_WarehouseBounceCenters", centers);
            Shader.SetGlobalVectorArray("_WarehouseBounceColors", colors);
            Shader.SetGlobalVectorArray("_WarehouseBounceMin", minima);
            Shader.SetGlobalVectorArray("_WarehouseBounceMax", maxima);
            Shader.SetGlobalVectorArray("_SceneBounceNormals", normals);
        }

        private void RefreshVisibility()
        {
            refreshAt = Time.unscaledTime + .1f;
            appliedBounce = GraphicsEffectsSettings.CleanBounceEnabled;
            for (int i = 0; i < PatchCount; i++)
            {
                int visible = 0;
                if (appliedBounce && sources[i] != null && sources[i].isActiveAndEnabled)
                    for (int ray = -1; ray <= 1; ray++)
                    {
                        Light source = sources[i];
                        Vector3 point = (Vector3)centers[i] + Vector3.right * (ray * .10f);
                        Vector3 delta = point - source.transform.position;
                        float length = delta.magnitude;
                        Vector3 direction = delta / Mathf.Max(.001f, length);
                        if (length <= source.range && Vector3.Dot(direction, source.transform.forward) >=
                            Mathf.Cos(source.spotAngle * .5f * Mathf.Deg2Rad) &&
                            !Physics.Raycast(source.transform.position + direction * .06f, direction,
                                Mathf.Max(0f, length - .09f), ~0, QueryTriggerInteraction.Ignore)) visible++;
                    }
                visibility[i] = visible / 3f;
            }
        }

        public override bool TryGetHalationSource(int index, out SceneHalationSource source)
        { source = default; return false; }
        protected override void OnEnable()
        { base.OnEnable(); refreshAt = 0f; if (Reflection != null) Reflection.enabled = true; }
        protected override void OnDisable()
        {
            if (Reflection != null) Reflection.enabled = false;
            Array.Clear(colors, 0, colors.Length);
            base.OnDisable();
        }
    }
}
