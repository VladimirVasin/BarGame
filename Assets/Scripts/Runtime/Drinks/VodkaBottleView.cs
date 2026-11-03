using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>The one authored shop/inventory/held bottle; liquid is bounded by its inner profile.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(33000)]
    public sealed class VodkaBottleView : MonoBehaviour
    {
        private static readonly int PlaneId = Shader.PropertyToID("_FillPlaneLocal");
        private static readonly int ToBottleId = Shader.PropertyToID("_BottleFromObject");
        private static readonly int SurfaceId = Shader.PropertyToID("_IsSurface");
        private static readonly int ProfileId = Shader.PropertyToID("_BottleProfile");
        private const int Slices = 64;
        private sealed class VolumeSamples
        {
            public readonly float[] Y = new float[Slices], Radius = new float[Slices], Weight = new float[Slices];
            public float Capacity;
        }
        private static readonly VolumeSamples[] Volumes = new VolumeSamples[3];
        private static Material liquidMaterial;
        private MaterialPropertyBlock properties;
        private Renderer liquid, surface, canSeal;
        private Quaternion closedCapRotation;
        public InventoryItemId ItemId { get; private set; } = InventoryItemId.VodkaBottle;
        public DrinkContainerProfile Profile => DrinkContainerCatalog.Get(ItemId);
        private Renderer[] glass = Array.Empty<Renderer>();
        private Renderer[] visibleBody = Array.Empty<Renderer>();
        public bool BodyIsVisible
        {
            get { foreach (Renderer renderer in visibleBody) if (renderer != null && renderer.isVisible) return true; return false; }
        }
        private Renderer[] labels = Array.Empty<Renderer>();
        private Transform surfacePivot, bottleSpace;
        private bool waterRipple;
        private Vector3 previousNormal;
        private float previousFill = -1f, planeDistance;
        private bool initialized;
        public Transform Grip { get; private set; }
        public Transform Mouth { get; private set; }
        public Transform Cap { get; private set; }
        public float Remaining { get; private set; } = 1f;
        public bool IsOpened { get; private set; }
        public IReadOnlyList<Renderer> GlassCells => glass;
        public float LiquidPlaneDistance => planeDistance;
        public Vector3 LiquidPlaneNormal => previousNormal;
        public static Material LiquidMaterial => liquidMaterial != null ? liquidMaterial :
            liquidMaterial = Resources.Load<Material>("Materials/VodkaBottleLiquid") ??
                throw new InvalidOperationException("Missing authored vodka liquid material.");

        public static VodkaBottleView Create(Transform parent) => Create(parent, InventoryItemId.VodkaBottle);

        public static VodkaBottleView Create(Transform parent, InventoryItemId itemId)
        {
            Transform root = SupermarketProductModelResources.Instantiate(itemId,
                parent, new Vector3(.5f, .5f, .5f), "Carried");
            return Bind(root);
        }

        public static VodkaBottleView Bind(Transform root)
        {
            VodkaBottleView view = root.GetComponent<VodkaBottleView>();
            if (view == null) view = root.gameObject.AddComponent<VodkaBottleView>();
            if (view.initialized) return view;
            SupermarketProductAssetRegistry registry = root.GetComponentInChildren<SupermarketProductAssetRegistry>();
            if (registry == null || !DrinkContainerCatalog.IsDrink(registry.ItemId))
                throw new InvalidOperationException("Vodka view requires the authored product registry.");
            view.ItemId = registry.ItemId;
            view.properties = new MaterialPropertyBlock();
            view.bottleSpace = registry.transform;
            var cells = new List<Renderer>(8);
            var paper = new List<Renderer>(2);
            foreach (SupermarketProductPartBinding part in registry.Parts)
            {
                switch (part.Role)
                {
                    case "glass": cells.Add(part.Renderer); break;
                    case "label": paper.Add(part.Renderer); break;
                    case "liquid": view.liquid = part.Renderer; break;
                    case "liquid_surface": view.surface = part.Renderer; break;
                    case "cap": view.Cap = part.Renderer.transform; break;
                    case "can_seal": view.canSeal = part.Renderer; break;
                }
            }
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == view.Profile.GripAnchorName) view.Grip = child;
                if (child.name == view.Profile.MouthAnchorName) view.Mouth = child;
            }
            if ((!view.Profile.IsCan && cells.Count != 8) || (view.Profile.IsCan && view.canSeal == null) || view.liquid == null || view.surface == null ||
                view.Cap == null || view.Grip == null || view.Mouth == null)
                throw new InvalidOperationException("Vodka model lacks its measured glass/liquid/cap/contact contract.");
            view.closedCapRotation = view.Cap.localRotation;
            view.glass = cells.ToArray(); view.labels = paper.ToArray();
            view.visibleBody = registry.GetComponentsInChildren<Renderer>(true);
            // The imported surface remains at its exact metre scale beneath a scale-free pivot.
            var pivot = new GameObject("Liquid gravity plane").transform;
            pivot.SetParent(view.bottleSpace, false);
            pivot.position = view.surface.bounds.center;
            view.surface.transform.SetParent(pivot, true);
            view.surfacePivot = pivot;
            view.initialized = true;
            view.RefreshLiquid();
            return view;
        }

        public static Material MaterialFor(string role) => role == "glass" ?
            BarDrinkServiceResources.GlassMaterial : role == "liquid" || role == "liquid_surface" ?
            LiquidMaterial : RuntimePrimitiveFactory.DefaultMaterial;

        internal static void SetPassiveLiquid(Renderer renderer, Transform bottle, bool top,
            MaterialPropertyBlock block, InventoryItemId itemId = InventoryItemId.VodkaBottle)
        {
            DrinkContainerProfile profile = DrinkContainerCatalog.Get(itemId);
            block.SetVector(PlaneId, new Vector4(0f, 1f, 0f, SolveLevel(Vector3.up, 1f, profile)));
            block.SetMatrix(ToBottleId, bottle.worldToLocalMatrix * renderer.localToWorldMatrix);
            block.SetFloat(SurfaceId, top ? 1f : 0f);
            block.SetVectorArray(ProfileId, profile.InnerProfile);
        }

        public void SetRemaining(float fraction)
        {
            Remaining = Mathf.Clamp01(float.IsNaN(fraction) ? 0f : fraction);
            RefreshLiquid();
        }
        public void SetOpened(bool value) => SetCapPresentation(value, !value);
        public void SetCapPresentation(bool opened, bool visible)
        {
            IsOpened = opened;
            if (Profile.IsCan)
            {
                if (canSeal != null) canSeal.enabled = !opened;
                if (Cap != null)
                {
                    Cap.gameObject.SetActive(true);
                    Cap.localRotation = closedCapRotation * Quaternion.AngleAxis(opened ? -14f : 0f, Vector3.right);
                }
            }
            else if (Cap != null && Cap.gameObject.activeSelf != visible) Cap.gameObject.SetActive(visible);
        }
        private void LateUpdate() => RefreshLiquid();

        public void RefreshLiquid()
        {
            if (!initialized || waterRipple) return;
            bool visible = Remaining > .0001f && (!Profile.IsCan || IsOpened);
            liquid.enabled = surface.enabled = visible;
            if (!visible) return;
            Matrix4x4 world = bottleSpace.localToWorldMatrix;
            Vector3 normal = new Vector3(world.m10, world.m11, world.m12).normalized;
            if (Mathf.Abs(previousFill - Remaining) <= .00001f && Vector3.Distance(previousNormal, normal) <= .001f)
                return;
            planeDistance = SolveLevel(normal, Remaining, Profile);
            previousNormal = normal; previousFill = Remaining;
            Vector3 centre = new Vector3(0f, Profile.LiquidCentre, 0f);
            Vector3 point = centre + normal * (planeDistance - Vector3.Dot(normal, centre));
            surfacePivot.localPosition = point;
            surfacePivot.localRotation = Quaternion.FromToRotation(Vector3.up, normal);
            surfacePivot.localScale = new Vector3(Profile.SurfaceScale, 1f, Profile.SurfaceScale);
            UpdateLiquidRenderer(liquid, false, normal);
            UpdateLiquidRenderer(surface, true, normal);
            // Shader clips the broad authored surface to the bottle. Its culling/preview bounds must
            // describe that visible intersection, not the oversized unclipped disc.
            Matrix4x4 toSurface = surface.transform.worldToLocalMatrix * bottleSpace.localToWorldMatrix;
            float radius = Profile.InnerRadius;
            Bounds bound = new Bounds(toSurface.MultiplyPoint3x4(new Vector3(-radius, Profile.InnerBottom, -radius)), Vector3.zero);
            for (int corner = 1; corner < 8; corner++)
                bound.Encapsulate(toSurface.MultiplyPoint3x4(new Vector3((corner & 1) == 0 ? -radius : radius,
                    (corner & 2) == 0 ? Profile.InnerBottom : Profile.InnerTop,
                    (corner & 4) == 0 ? -radius : radius)));
            surface.localBounds = bound;
        }
        private void UpdateLiquidRenderer(Renderer renderer, bool top, Vector3 normal)
        {
            renderer.GetPropertyBlock(properties);
            properties.SetVector(PlaneId, new Vector4(normal.x, normal.y, normal.z, planeDistance));
            properties.SetMatrix(ToBottleId, bottleSpace.worldToLocalMatrix * renderer.localToWorldMatrix);
            properties.SetFloat(SurfaceId, top ? 1f : 0f);
            properties.SetVectorArray(ProfileId, Profile.InnerProfile);
            renderer.SetPropertyBlock(properties);
        }

        /// <summary>Fixed midpoint quadrature of circular slices; no frame allocations or fluid simulation.</summary>
        public static float SolveLevel(Vector3 normal, float fraction) =>
            SolveLevel(normal, fraction, DrinkContainerCatalog.Get(InventoryItemId.VodkaBottle));
        public static float SolveLevel(Vector3 normal, float fraction, DrinkContainerProfile profile)
        {
            VolumeSamples samples = PrepareVolume(profile);
            normal = normal.sqrMagnitude > .00001f ? normal.normalized : Vector3.up;
            float low = -profile.Height, high = profile.Height;
            float target = samples.Capacity * profile.FillFraction * Mathf.Clamp01(fraction);
            for (int step = 0; step < 15; step++)
            {
                float level = (low + high) * .5f;
                if (VolumeBelow(normal, level, profile) < target) low = level; else high = level;
            }
            return (low + high) * .5f;
        }
        public static float VolumeBelow(Vector3 normal, float distance) =>
            VolumeBelow(normal, distance, DrinkContainerCatalog.Get(InventoryItemId.VodkaBottle));
        public static float VolumeBelow(Vector3 normal, float distance, DrinkContainerProfile profile)
        {
            VolumeSamples samples = PrepareVolume(profile);
            float horizontal = Mathf.Sqrt(normal.x * normal.x + normal.z * normal.z), volume = 0f;
            float dy = (profile.InnerTop - profile.InnerBottom) / Slices;
            for (int i = 0; i < Slices; i++)
            {
                float offset = distance - normal.y * samples.Y[i];
                float radius = horizontal * samples.Radius[i];
                if (horizontal < .025f)
                {
                    float part = Mathf.Clamp01(.5f + offset / (Mathf.Abs(normal.y) * dy));
                    volume += samples.Weight[i] * part; continue;
                }
                if (offset >= radius) { volume += samples.Weight[i]; continue; }
                if (offset <= -radius) continue;
                float t = Mathf.Clamp(offset / Mathf.Max(radius, .0000001f), -1f, 1f);
                volume += samples.Weight[i] * (Mathf.Acos(-t) + t * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t))) / Mathf.PI;
            }
            return volume;
        }
        private static VolumeSamples PrepareVolume(DrinkContainerProfile profile)
        {
            int slot = profile.ItemId == InventoryItemId.BeerCan ? 1 : profile.ItemId == InventoryItemId.WineBottle ? 2 : 0;
            if (Volumes[slot] != null) return Volumes[slot];
            var samples = new VolumeSamples();
            float dy = (profile.InnerTop - profile.InnerBottom) / Slices;
            for (int i = 0; i < Slices; i++)
            {
                float y = samples.Y[i] = profile.InnerBottom + (i + .5f) * dy;
                int segment = 0;
                Vector4[] curve = profile.InnerProfile;
                while (segment < curve.Length - 2 && y > curve[segment + 1].x) segment++;
                float r = samples.Radius[i] = Mathf.Lerp(curve[segment].y, curve[segment + 1].y,
                    Mathf.InverseLerp(curve[segment].x, curve[segment + 1].x, y));
                samples.Capacity += samples.Weight[i] = Mathf.PI * r * r * dy;
            }
            Volumes[slot] = samples;
            return samples;
        }
        internal void BeginWaterRipple(Vector3 point)
        {
            waterRipple = true;
            surfacePivot.SetParent(null, true);
            surfacePivot.rotation = Quaternion.identity;
            surfacePivot.localScale = Vector3.one;
            surfacePivot.position += point + Vector3.up * .012f - surface.bounds.center;
            surface.sharedMaterial = CityFountainWaterResources.SplashMaterial;
            surface.SetPropertyBlock(null);
            surface.ResetLocalBounds(); surface.enabled = true;
        }
        internal void StepWaterRipple(float age)
        {
            if (!waterRipple) return;
            surface.enabled = age < .65f;
            surfacePivot.localScale = Vector3.one * Mathf.Lerp(1f, 4f, Mathf.Clamp01(age / .65f));
        }
        private void OnDestroy()
        {
            if (waterRipple && surfacePivot != null) Destroy(surfacePivot.gameObject);
        }

        internal void HideNonGlass()
        {
            SetRemaining(0f); SetOpened(true);
            foreach (Renderer renderer in labels) if (renderer != null) renderer.enabled = false;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetShared() => liquidMaterial = null;
    }
}
