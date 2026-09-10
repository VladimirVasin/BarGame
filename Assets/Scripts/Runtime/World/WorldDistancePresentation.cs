using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Presentation only: leaves collision, clocks and traffic owners alive.</summary>
    public sealed class WorldDistancePresentation : IDisposable
    {
        public const float EnterDistance = 80f;
        public const float ExitDistance = 96f;
        private readonly Renderer[] renderers;
        private readonly bool[] originalForcedOff;
        private readonly GameObject[] lights;
        private readonly bool[] originalLightActive;
        public bool IsVisible { get; private set; } = true;

        public WorldDistancePresentation(params Transform[] roots)
        {
            var meshes = new HashSet<Renderer>();
            var fixtures = new HashSet<GameObject>();
            foreach (Transform root in roots)
            {
                if (root == null) continue;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) meshes.Add(renderer);
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                {
                    // These authored fixtures have dedicated hosts. Never disable a
                    // host carrying physical geometry or the simulation owner.
                    if (light.GetComponentsInChildren<Collider>(true).Length == 0 &&
                        light.GetComponent<CityPortController>() == null &&
                        light.GetComponent<CityCanneryController>() == null)
                        fixtures.Add(light.gameObject);
                }
            }
            renderers = new Renderer[meshes.Count]; meshes.CopyTo(renderers);
            originalForcedOff = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) originalForcedOff[i] = renderers[i].forceRenderingOff;
            lights = new GameObject[fixtures.Count]; fixtures.CopyTo(lights);
            originalLightActive = new bool[lights.Length];
            for (int i = 0; i < lights.Length; i++) originalLightActive[i] = lights[i].activeSelf;
        }

        public void SetVisible(bool visible)
        {
            if (IsVisible == visible) return;
            IsVisible = visible;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].forceRenderingOff = !visible || originalForcedOff[i];
            // Day/night registries may re-enable Light and halo components;
            // a dedicated inactive fixture host remains dark in that case.
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) lights[i].SetActive(visible && originalLightActive[i]);
        }

        public static bool ShouldShow(Transform observer, Bounds influence, bool wasVisible, bool force = false)
        {
            if (force || observer == null) return true;
            float distance = wasVisible ? ExitDistance : EnterDistance;
            return influence.SqrDistance(observer.position) <= distance * distance;
        }

        public void Dispose() => SetVisible(true);
    }
}
