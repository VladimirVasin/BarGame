using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>One passive chest attachment and one real, torso-directed warehouse light.</summary>
    [DisallowMultipleComponent]
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
        private void OnDisable() => SetActive(false);
        private void LateUpdate() => WarehouseArenaLighting.UpdateHeroVisibility(transform);
        private void OnDestroy()
        {
            WarehouseArenaLighting.ClearHeroVisibility(transform);
            if (contactsRegistered && equipment != null) equipment.Unregister(this);
            if (presentation != null && renderers != null) presentation.UnregisterAccessoryRenderers(renderers);
            if (ModelRoot != null) Destroy(ModelRoot);
        }
    }
}
