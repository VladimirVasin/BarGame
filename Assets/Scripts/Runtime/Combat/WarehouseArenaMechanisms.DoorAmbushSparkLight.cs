using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    public sealed partial class WarehouseArenaMechanisms
    {
        private const float DoorAmbushSparkLightDuration = .10f;
        private const float DoorAmbushSparkLightPeakIntensity = 36f;
        private float doorAmbushSparkLightRemaining;
        private bool doorAmbushSparkLightPaused;

        public Light DoorAmbushSparkLight { get; private set; }

        private void InitializeDoorAmbushSparkLight()
        {
            if (DoorAmbushSparkLight != null) return;
            var host = new GameObject("Warehouse fan bearing sparks light");
            host.transform.SetParent(transform, false);
            host.transform.position = doorAmbushSparkOrigin;
            DoorAmbushSparkLight = host.AddComponent<Light>();
            DoorAmbushSparkLight.type = LightType.Point;
            DoorAmbushSparkLight.color = new Color(1f, .46f, .16f);
            DoorAmbushSparkLight.range = 4.8f;
            DoorAmbushSparkLight.intensity = 0f;
            DoorAmbushSparkLight.bounceIntensity = 0f;
            DoorAmbushSparkLight.shadows = LightShadows.None;
            DoorAmbushSparkLight.renderMode = LightRenderMode.ForcePixel;
            DoorAmbushSparkLight.enabled = false;
            host.AddComponent<UniversalAdditionalLightData>().usePipelineSettings = true;
        }

        private void EmitDoorAmbushSparkLight()
        {
            if (DoorAmbushSparkLight == null) return;
            // A burst reuses the bearing's one practical source. Its reach stays
            // local to the motor, duct and nearby roof, without a shadow pass.
            doorAmbushSparkLightRemaining = DoorAmbushSparkLightDuration;
            ApplyDoorAmbushSparkLight();
        }

        private void AdvanceDoorAmbushSparkLight(float seconds)
        {
            if (doorAmbushSparkLightPaused || !float.IsFinite(seconds) || seconds <= 0f ||
                doorAmbushSparkLightRemaining <= 0f) return;
            doorAmbushSparkLightRemaining = Mathf.Max(0f, doorAmbushSparkLightRemaining - seconds);
            ApplyDoorAmbushSparkLight();
        }

        private void SetDoorAmbushSparkLightPaused(bool paused)
        {
            if (doorAmbushSparkLightPaused == paused) return;
            doorAmbushSparkLightPaused = paused;
            ApplyDoorAmbushSparkLight();
        }

        private void ApplyDoorAmbushSparkLight()
        {
            if (DoorAmbushSparkLight == null) return;
            float remaining = doorAmbushSparkLightRemaining / DoorAmbushSparkLightDuration;
            bool visible = remaining > 0f && !doorAmbushSparkLightPaused && isActiveAndEnabled &&
                owner != null && owner.isActiveAndEnabled;
            DoorAmbushSparkLight.intensity = visible
                ? DoorAmbushSparkLightPeakIntensity * remaining * remaining : 0f;
            DoorAmbushSparkLight.enabled = visible;
        }

        private void ClearDoorAmbushSparkLight()
        {
            doorAmbushSparkLightRemaining = 0f;
            doorAmbushSparkLightPaused = false;
            ApplyDoorAmbushSparkLight();
        }

        private void DisposeDoorAmbushSparkLight()
        {
            ClearDoorAmbushSparkLight();
            if (DoorAmbushSparkLight != null) Destroy(DoorAmbushSparkLight.gameObject);
            DoorAmbushSparkLight = null;
        }
    }
}
