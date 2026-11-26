using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    // Keep the area's authored fill as the horizontal reference. The wider outdoor
    // gradient describes an overcast sky, the shallower one a room with bounced light.
    internal readonly struct SceneAmbientLight
    {
        private readonly Color sky, equator, ground;
        private readonly AmbientMode mode;
        private readonly bool followsPreference;
        private static SceneAmbientLight current;
        private static Scene owner;

        private SceneAmbientLight(Color sky, Color equator, Color ground, AmbientMode mode, bool followsPreference)
        {
            this.sky = sky;
            this.equator = equator;
            this.ground = ground;
            this.mode = mode;
            this.followsPreference = followsPreference;
        }

        public static Color ReferenceColor => RenderSettings.ambientMode == AmbientMode.Trilight
            ? RenderSettings.ambientEquatorColor : RenderSettings.ambientLight;

        public static void Apply(Color color, bool exterior = false)
        {
            float spread = exterior ? .27f : .16f;
            new SceneAmbientLight(Scale(color, 1f + spread), color, Scale(color, 1f - spread),
                AmbientMode.Trilight, true).Restore();
        }

        private static Color Scale(Color color, float gain) =>
            new Color(color.r * gain, color.g * gain, color.b * gain, color.a);

        public static SceneAmbientLight Capture()
        {
            if (OwnsActiveScene) return current;
            var middle = RenderSettings.ambientMode == AmbientMode.Flat ? RenderSettings.ambientLight : RenderSettings.ambientEquatorColor;
            return new SceneAmbientLight(RenderSettings.ambientSkyColor, middle,
                RenderSettings.ambientGroundColor, RenderSettings.ambientMode, false);
        }

        private static bool OwnsActiveScene => current.followsPreference && owner == SceneManager.GetActiveScene();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Release()
        {
            current = default;
            owner = default;
        }

        public static void RefreshPreference()
        {
            if (!OwnsActiveScene) return;
            current.Restore();
            DynamicGI.UpdateEnvironment();
        }

        public void Restore()
        {
            current = this;
            owner = SceneManager.GetActiveScene();
            var selected = followsPreference && !GraphicsEffectsSettings.DirectionalAmbientEnabled
                ? AmbientMode.Flat : mode;
            RenderSettings.ambientMode = selected;
            if (selected == AmbientMode.Flat)
                RenderSettings.ambientLight = equator;
            else
            {
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientEquatorColor = equator;
                RenderSettings.ambientGroundColor = ground;
            }
        }
    }
}
