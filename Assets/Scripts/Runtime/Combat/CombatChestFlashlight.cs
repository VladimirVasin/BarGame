using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>One passive chest attachment and one real, torso-directed warehouse light.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(310)]
    public sealed class CombatChestFlashlight : MonoBehaviour
    {
        public const float Range = 16f;
        public const float OuterAngle = 60f;
        public const float InnerAngle = 28f;
        public const float Intensity = 60f;
        public GameObject ModelRoot { get; private set; }
        public Transform Lens { get; private set; }
        public Light SpotLight { get; private set; }
        private Player3DCharacterPresentation presentation;
        private PlayerEquipmentContacts equipment;
        private Renderer[] renderers;
        private bool contactsRegistered;
        private object inspectionAimOwner;
        private CombatActor inspectionActor;
        private Camera inspectionCamera;
        private Transform inspectionAimFrame;
        private Vector3 inspectionCavitySize;
        private float inspectionNeutralPitch;
        private Func<bool> inspectionBrowsing;
        private Func<bool> inspectionPointerSelection;
        public bool HasInspectionAim => inspectionAimOwner != null;
        public Vector3 InspectionAimTarget { get; private set; }

        public bool BeginInspectionAim(object owner, CombatActor actor, Camera camera, Transform frame,
            Vector3 cavitySize, Func<bool> browsing, Func<bool> pointerSelection)
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
            inspectionBrowsing = browsing;
            inspectionPointerSelection = pointerSelection;
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
            inspectionBrowsing = null;
            inspectionPointerSelection = null;
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
                !GameInput.CanRead(GameInputContext.Menu) || !inspectionBrowsing()) return;
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
            // Root-relative origin avoids feedback as the same real chest bends.
            Vector3 origin = inspectionActor.transform.position + inspectionActor.transform.up * 1.4f;
            inspectionActor.SetInspectionTorsoLook(inspectionAimOwner, InspectionAimTarget - origin, inspectionNeutralPitch, seconds);
        }

        public void Initialize(Player3DCharacterPresentation hero)
        {
            if (ModelRoot != null) throw new InvalidOperationException("The chest flashlight is already installed.");
            if (hero == null || hero.Registry == null) throw new ArgumentNullException(nameof(hero));
            presentation = hero;
            ModelRoot = CombatFlashlightAssetProvider.Create(hero.Registry);
            Lens = CombatFlashlightAssetProvider.FindLens(ModelRoot);
            renderers = ModelRoot.GetComponentsInChildren<Renderer>(true);
            presentation.RegisterAccessoryRenderers(renderers);
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
            SetActive(isActiveAndEnabled);
        }

        private void SetActive(bool active)
        {
            if (ModelRoot == null) return;
            ModelRoot.SetActive(active);
            SpotLight.enabled = active;
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
        private void OnDisable()
        {
            if (HasInspectionAim) EndInspectionAim(inspectionAimOwner);
            SetActive(false);
        }
        private void LateUpdate()
        {
            AdvanceInspectionAim(Time.deltaTime);
            WarehouseArenaLighting.UpdateHeroVisibility(transform);
        }
        private void OnDestroy()
        {
            if (HasInspectionAim) EndInspectionAim(inspectionAimOwner);
            WarehouseArenaLighting.ClearHeroVisibility(transform);
            if (contactsRegistered && equipment != null) equipment.Unregister(this);
            if (presentation != null && renderers != null) presentation.UnregisterAccessoryRenderers(renderers);
            if (ModelRoot != null) Destroy(ModelRoot);
        }
    }
}
