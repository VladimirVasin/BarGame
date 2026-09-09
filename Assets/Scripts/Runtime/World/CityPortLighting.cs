using UnityEngine;

namespace BarPromenade
{
    /// <summary>Two physical work lights share the coast's ordinary day/night law.</summary>
    public static class CityPortLighting
    {
        public static void Build(CityPortController port)
        {
            Color warm = new Color(1f, .72f, .47f);
            foreach (Renderer renderer in port.Dock.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name != "WorkLampGlass") continue;
                renderer.sharedMaterial = CityNightResources.EmissiveMaterial;
                CityNightGlowRegistry.Register(renderer, warm * 3.5f);
            }
            string[] fixtures = { "ANCHOR_WorkLightA", "ANCHOR_WorkLightB" };
            Vector3[] targets =
            {
                new Vector3(-4f, CityPortPlan.DeckHeight + .05f, -6.7f),
                new Vector3(1f, CityPortPlan.DeckHeight + .05f, -9.6f)
            };
            for (int index = 0; index < fixtures.Length; index++)
            {
                Transform fixture = CityPortAssetProvider.FindPart(port.Dock.gameObject, fixtures[index]);
                Transform root = new GameObject(index == 0 ? "Port Quay Work Light" : "Port Store Work Light").transform;
                root.SetParent(port.transform, false);
                root.position = fixture.position;
                // Each authored housing owns a definite patch: landing and
                // warehouse threshold. Neither beam searches the open beach.
                Vector3 target = port.Plan.Origin + targets[index];
                root.rotation = Quaternion.LookRotation(target - fixture.position, Vector3.up);
                Light light = root.gameObject.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = warm;
                light.range = index == 0 ? 12f : 10f;
                light.spotAngle = index == 0 ? 82f : 100f;
                light.innerSpotAngle = index == 0 ? 48f : 64f;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = .72f;
                light.shadowBias = .025f;
                light.shadowNormalBias = .12f;
                CityLightHalo halo = CityLightHalo.CreateNightRegistered(root, Vector3.zero,
                    .30f, .95f, new Color(1f, .72f, .47f, .10f), new Color(.8f, .51f, .3f, .035f));
                CityNightSiteLightRegistry.Register(light, index == 0 ? 3.8f : 3.3f, halo);
            }
        }
    }
}
