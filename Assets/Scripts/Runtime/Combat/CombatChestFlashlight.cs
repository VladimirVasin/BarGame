using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>One chest fixture with a directed beam and bounded, body-only scattered light.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(310)]
    public sealed class CombatChestFlashlight : MonoBehaviour
    {
        public const float Range = 16f;
        public const float OuterAngle = 60f;
        public const float InnerAngle = 28f;
        public const float Intensity = 60f;
        internal const float NearExposureDistance = 4f;
        internal const float BodyFillBaseIntensity = CombatFlashlightBodyFill.BaseIntensity;
        internal const float BodyFillMaximumIntensity = CombatFlashlightBodyFill.MaximumIntensity;
        private const float ExposureProbeInterval = .1f;
        private const float MinimumExposureFraction = .01f;
        private const float ExposureDownSeconds = .045f, ExposureUpSeconds = .22f;
        private const float ProbeSideOffset = .06992681f; // tan(4 degrees), inside the bright inner cone.
        public GameObject ModelRoot { get; private set; }
        public Transform Lens { get; private set; }
        public Light SpotLight { get; private set; }
        public CombatFlashlightGlow Glow { get; private set; }
        public Light BodyFill => bodyFill?.Source;
        internal float BodyFillBounce => bodyFill?.BounceAmount ?? 0f;
        private CombatFlashlightBodyFill bodyFill;
        private float bodyFillTargetBounce;
        private Player3DCharacterPresentation presentation;
        private PlayerEquipmentContacts equipment;
        private Renderer[] renderers;
        private bool contactsRegistered;
        private Transform exposureOwnerRoot;
        private RaycastHit[] exposureHits = new RaycastHit[16];
        private float exposureProbeRemaining;
        internal float NearSurfaceDistance { get; private set; } = NearExposureDistance;
        internal float ExposureTargetIntensity { get; private set; } = Intensity;
        private object inspectionAimOwner;
        private CombatActor inspectionActor;
        private Camera inspectionCamera;
        private Transform inspectionAimFrame;
        private Vector3 inspectionCavitySize;
        private float inspectionNeutralPitch;
        private Vector3 inspectionNeutralOriginLocal;
        private Func<bool> inspectionBrowsing;
        private Func<bool> inspectionPointerSelection;
        private Func<bool> inspectionAimSuspended;
        public bool HasInspectionAim => inspectionAimOwner != null;
        public Vector3 InspectionAimTarget { get; private set; }

        public bool BeginInspectionAim(object owner, CombatActor actor, Camera camera, Transform frame,
            Vector3 cavitySize, Func<bool> browsing, Func<bool> pointerSelection, Func<bool> suspended = null)
        {
            if (!isActiveAndEnabled || SpotLight == null || owner == null || actor == null || camera == null || frame == null ||
                browsing == null || inspectionAimOwner != null || !actor.BeginInspectionTorsoLook(owner)) return false;
            inspectionAimOwner = owner;
            inspectionActor = actor;
            inspectionCamera = camera;
            inspectionAimFrame = frame;
            inspectionCavitySize = cavitySize;
            // The existing authored mount already points below actor-forward.
            // Measure its live rest direction after the previous bend was cleared,
            // so the additive torso solver does not apply that downward angle twice.
            Vector3 rest = actor.transform.InverseTransformDirection(SpotLight.transform.forward);
            inspectionNeutralPitch = Mathf.Atan2(-rest.y, Mathf.Sqrt(rest.x * rest.x + rest.z * rest.z)) * Mathf.Rad2Deg;
            inspectionNeutralOriginLocal = actor.transform.InverseTransformPoint(Lens.position);
            inspectionBrowsing = browsing;
            inspectionPointerSelection = pointerSelection;
            inspectionAimSuspended = suspended;
            InspectionAimTarget = frame.position;
            return true;
        }

        public void EndInspectionAim(object owner)
        {
            if (!ReferenceEquals(inspectionAimOwner, owner)) return;
            inspectionActor?.EndInspectionTorsoLook(owner);
            inspectionAimOwner = null;
            inspectionActor = null;
            inspectionCamera = null;
            inspectionAimFrame = null;
            inspectionNeutralPitch = 0f;
            inspectionNeutralOriginLocal = Vector3.zero;
            inspectionBrowsing = null;
            inspectionPointerSelection = null;
            inspectionAimSuspended = null;
            InspectionAimTarget = Vector3.zero;
        }

        public void AdvanceInspectionAim(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!HasInspectionAim) return;
            if (inspectionActor == null || inspectionCamera == null || inspectionAimFrame == null ||
                inspectionAimOwner is UnityEngine.Object unityOwner && unityOwner == null)
            {
                EndInspectionAim(inspectionAimOwner);
                return;
            }
            if (GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused ||
                !GameInput.CanRead(GameInputContext.Menu) || (inspectionAimSuspended?.Invoke() ?? false)) return;
            // Establish the cavity direction as the door opens. Cursor following
            // belongs only to shelf browsing, never to the moving camera path.
            if (!inspectionBrowsing())
            {
                InspectionAimTarget = inspectionAimFrame.position;
                ApplyInspectionTorsoLook(seconds);
                return;
            }
            Rect pixels = inspectionCamera.pixelRect;
            Vector2 pointer = pixels.center;
            Mouse mouse = Mouse.current;
            if (mouse != null && (inspectionPointerSelection == null || inspectionPointerSelection()))
            {
                Vector2 position = mouse.position.ReadValue();
                if (pixels.Contains(position)) pointer = position;
            }
            Ray ray = inspectionCamera.ScreenPointToRay(pointer);
            var plane = new Plane(inspectionAimFrame.forward, inspectionAimFrame.position);
            Vector3 point = plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : inspectionAimFrame.position;
            Vector3 local = inspectionAimFrame.InverseTransformPoint(point);
            local.x = Mathf.Clamp(local.x, -inspectionCavitySize.x * .47f, inspectionCavitySize.x * .47f);
            local.y = Mathf.Clamp(local.y, -inspectionCavitySize.y * .47f, inspectionCavitySize.y * .47f);
            local.z = 0f;
            InspectionAimTarget = inspectionAimFrame.TransformPoint(local);
            ApplyInspectionTorsoLook(seconds);
        }

        private void ApplyInspectionTorsoLook(float seconds)
        {
            // Root-relative origin avoids feedback as the same real chest bends.
            Vector3 origin = inspectionActor.transform.TransformPoint(inspectionNeutralOriginLocal);
            inspectionActor.SetInspectionTorsoLook(inspectionAimOwner, InspectionAimTarget - origin, inspectionNeutralPitch, seconds);
        }

        public void Initialize(Player3DCharacterPresentation hero)
        {
            if (ModelRoot != null) throw new InvalidOperationException("The chest flashlight is already installed.");
            if (hero == null || hero.Registry == null) throw new ArgumentNullException(nameof(hero));
            presentation = hero;
            exposureOwnerRoot = transform;
            ModelRoot = CombatFlashlightAssetProvider.Create(hero.Registry);
            Lens = CombatFlashlightAssetProvider.FindLens(ModelRoot);
            equipment = hero.Registry.GetComponentInParent<PlayerEquipmentContacts>();
            if (equipment == null) equipment = hero.Registry.gameObject.AddComponent<PlayerEquipmentContacts>();
            SpotLight = Lens.gameObject.AddComponent<Light>();
            SpotLight.type = LightType.Spot;
            SpotLight.color = new Color(1f, .92f, .78f);
            SpotLight.range = Range;
            SpotLight.spotAngle = OuterAngle;
            SpotLight.innerSpotAngle = InnerAngle;
            SpotLight.intensity = Intensity;
            SpotLight.shadows = LightShadows.Hard;
            SpotLight.shadowStrength = 1f;
            SpotLight.shadowBias = .012f;
            SpotLight.shadowNormalBias = .035f;
            SpotLight.shadowNearPlane = .025f;
            UniversalAdditionalLightData data = Lens.gameObject.AddComponent<UniversalAdditionalLightData>();
            data.additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium;
            Glow = CombatFlashlightGlow.Create(Lens, SpotLight);
            // Include the source glow in the same visibility lease as the
            // physical lamp, so inspection and hero hiding cannot leave a halo.
            renderers = ModelRoot.GetComponentsInChildren<Renderer>(true);
            presentation.RegisterAccessoryRenderers(renderers);
            bodyFill = new CombatFlashlightBodyFill(presentation, Lens, SpotLight, Glow.LensRenderer);
            SetActive(isActiveAndEnabled);
        }

        private void SetActive(bool active)
        {
            if (ModelRoot == null) return;
            ModelRoot.SetActive(active);
            SpotLight.enabled = active;
            Glow.Synchronize();
            ResetExposure();
            bodyFill?.SetActive(active);
            if (active && !contactsRegistered)
            {
                WarehouseArenaLighting.BindHeroVisibility(transform);
                equipment.Register(this, ModelRoot);
                contactsRegistered = true;
            }
            else if (!active && contactsRegistered)
            {
                WarehouseArenaLighting.ClearHeroVisibility(transform);
                if (equipment != null) equipment.Unregister(this);
                contactsRegistered = false;
            }
        }

        private void OnEnable() => SetActive(true);

        // The physical central beam controls its own near-field power. Keeping
        // the inverse-square contribution approximately level up close retains
        // timber and pale-skin detail without changing materials or far reach.
        internal void AdvanceExposure(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (SpotLight == null || !SpotLight.enabled || !isActiveAndEnabled || seconds <= 0f ||
                GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused || SceneTransitionService.IsTransitioning)
            { SynchronizeBodyFill(0f); return; }
            exposureProbeRemaining -= seconds;
            if (exposureProbeRemaining <= 0f)
            {
                exposureProbeRemaining = ExposureProbeInterval;
                Transform beam = SpotLight.transform;
                Vector3 forward = beam.forward, right = beam.right * ProbeSideOffset, up = beam.up * ProbeSideOffset;
                Vector3 origin = beam.position;
                float central = NearExposureDistance, nearestSide = NearExposureDistance, secondSide = NearExposureDistance;
                float centralBounce = 0f, nearestSideBounce = 0f, secondSideBounce = 0f;
                for (int i = 0; i < 5; i++)
                {
                    Vector3 direction = i switch
                    {
                        1 => forward + right, 2 => forward - right,
                        3 => forward + up, 4 => forward - up, _ => forward
                    };
                    direction.Normalize();
                    float distance = ProbeNearSurface(origin, direction, out RaycastHit surface);
                    float bounce = SurfaceBounce(surface, direction);
                    if (i == 0) { central = distance; centralBounce = bounce; }
                    else
                    {
                        if (distance < nearestSide) { secondSide = nearestSide; nearestSide = distance; }
                        else secondSide = Mathf.Min(secondSide, distance);
                        if (bounce > nearestSideBounce) { secondSideBounce = nearestSideBounce; nearestSideBounce = bounce; }
                        else secondSideBounce = Mathf.Max(secondSideBounce, bounce);
                    }
                }
                // One glancing edge at a doorway must not dim the whole cone.
                // A central hit is sufficient; side hits need a second sample.
                float nearest = Mathf.Min(central, secondSide);
                NearSurfaceDistance = nearest;
                float fraction = nearest / NearExposureDistance;
                ExposureTargetIntensity = Intensity * Mathf.Clamp(fraction * fraction, MinimumExposureFraction, 1f);
                // Reuse the same physical probes: a lone edge cannot produce a
                // broad reflection, and a grazing surface contributes little.
                bodyFillTargetBounce = Mathf.Max(centralBounce, secondSideBounce);
            }
            float response = SpotLight.intensity > ExposureTargetIntensity ? ExposureDownSeconds : ExposureUpSeconds;
            float blend = 1f - Mathf.Exp(-seconds / response);
            float intensity = Mathf.Lerp(SpotLight.intensity, ExposureTargetIntensity, blend);
            SpotLight.intensity = Mathf.Abs(intensity - ExposureTargetIntensity) < .001f ? ExposureTargetIntensity : intensity;
            SynchronizeBodyFill(seconds);
        }

        private static float SurfaceBounce(RaycastHit surface, Vector3 direction)
        {
            if (surface.collider == null) return 0f;
            float facing = Mathf.Clamp01(Vector3.Dot(surface.normal, -direction));
            float proximity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.6f, 3f, surface.distance));
            return proximity * facing;
        }

        internal void SynchronizeBodyFill(float seconds) => bodyFill?.Advance(seconds, bodyFillTargetBounce,
            GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused || SceneTransitionService.IsTransitioning ||
            !GameInput.MovementFocused);

        private float ProbeNearSurface(Vector3 origin, Vector3 direction, out RaycastHit surface)
        {
            RaycastHit[] hits;
            int count;
            while (true)
            {
                hits = exposureHits;
                count = Physics.RaycastNonAlloc(origin, direction, hits, NearExposureDistance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (count < hits.Length) break;
                if (hits.Length < 128)
                {
                    Array.Resize(ref exposureHits, hits.Length * 2);
                    continue;
                }
                // An exceptional dense overlap must still find the real nearest
                // obstacle after discarding the owner's rig and carried props.
                hits = Physics.RaycastAll(origin, direction, NearExposureDistance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                count = hits.Length;
                break;
            }
            float nearest = NearExposureDistance;
            surface = default;
            for (int i = 0; i < count; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || collider.transform.IsChildOf(exposureOwnerRoot)) continue;
                if (hits[i].distance >= nearest) continue;
                nearest = hits[i].distance;
                surface = hits[i];
            }
            return nearest;
        }

        private void ResetExposure()
        {
            exposureProbeRemaining = 0f;
            NearSurfaceDistance = NearExposureDistance;
            ExposureTargetIntensity = Intensity;
            bodyFillTargetBounce = 0f;
            bodyFill?.Reset();
            if (SpotLight != null) SpotLight.intensity = Intensity;
        }

        private void OnDisable()
        {
            if (HasInspectionAim) EndInspectionAim(inspectionAimOwner);
            SetActive(false);
        }
        private void LateUpdate()
        {
            AdvanceInspectionAim(Time.deltaTime);
            AdvanceExposure(Time.deltaTime);
            WarehouseArenaLighting.UpdateHeroVisibility(transform);
        }
        private void OnDestroy()
        {
            if (HasInspectionAim) EndInspectionAim(inspectionAimOwner);
            WarehouseArenaLighting.ClearHeroVisibility(transform);
            bodyFill?.Dispose();
            if (contactsRegistered && equipment != null) equipment.Unregister(this);
            if (presentation != null && renderers != null) presentation.UnregisterAccessoryRenderers(renderers);
            if (ModelRoot != null) Destroy(ModelRoot);
        }
    }
}
