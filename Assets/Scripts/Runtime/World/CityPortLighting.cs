using UnityEngine;

namespace BarPromenade
{
    /// <summary>Quay fixtures and the vessel's lamp share the ordinary day/night law.</summary>
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
                new Vector3(-3.8f, CityPortPlan.DeckHeight + .05f, -10.8f),
                new Vector3(1f, CityPortPlan.DeckHeight + .05f, -9.6f)
            };
            for (int index = 0; index < fixtures.Length; index++)
            {
                Transform fixture = CityPortAssetProvider.FindPart(port.Dock.gameObject, fixtures[index]);
                Transform root = new GameObject(index == 0 ? "Port Quay Work Light" : "Port Store Work Light").transform;
                root.SetParent(port.transform, false);
                root.position = fixture.position;
                // Each authored housing owns a definite patch: foreman/landing and
                // warehouse threshold. Neither beam searches the open beach.
                Vector3 target = port.Plan.Origin + targets[index];
                root.rotation = Quaternion.LookRotation(target - fixture.position, Vector3.up);
                Light light = root.gameObject.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = warm;
                light.range = index == 0 ? 12f : 10f;
                light.spotAngle = index == 0 ? 120f : 100f;
                light.innerSpotAngle = index == 0 ? 80f : 64f;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = .72f;
                light.shadowBias = .025f;
                light.shadowNormalBias = .12f;
                CityLightHalo halo = CityLightHalo.CreateNightRegistered(root, Vector3.zero,
                    .30f, .95f, new Color(1f, .72f, .47f, .10f), new Color(.8f, .51f, .3f, .035f));
                CityNightSiteLightRegistry.Register(light, index == 0 ? 8f : 3.3f, halo);
            }
            BuildWarehouseLights(port, warm);
            BuildCanopyLight(port, warm);
            BuildVesselSearchlight(port, warm);
        }

        private static void BuildWarehouseLights(CityPortController port, Color warm)
        {
            Vector3[] targets =
            {
                new Vector3(0f, 1.55f, -13.7f),
                new Vector3(1.3f, 1.8f, -17.45f),
                new Vector3(-6.8f, 2.1f, -15.25f)
            };
            for (int index = 0; index < targets.Length; index++)
            {
                char suffix = (char)('A' + index);
                Transform anchor = CityPortAssetProvider.FindPart(port.Dock.gameObject,
                    "ANCHOR_WarehouseLight" + suffix);
                Renderer glass = CityPortAssetProvider.FindPart(port.Dock.gameObject,
                    "WarehouseLampGlass" + suffix).GetComponent<Renderer>();
                glass.sharedMaterial = CityNightResources.EmissiveMaterial;
                CityNightGlowRegistry.Register(glass, warm * 3.5f);

                // The emitter is below its authored lens. A dedicated host lets
                // distance hiding turn it off without disabling solid furniture.
                Transform fixture = new GameObject("Port Warehouse Work Light " + suffix).transform;
                fixture.SetParent(port.transform, false);
                fixture.position = anchor.position;
                fixture.rotation = Quaternion.LookRotation(port.Plan.World(targets[index]) - anchor.position,
                    Vector3.forward);
                Light light = fixture.gameObject.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = warm;
                light.range = 6.5f;
                light.spotAngle = 130f;
                light.innerSpotAngle = 100f;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = .85f;
                light.shadowBias = .015f;
                light.shadowNormalBias = .06f;
                light.renderMode = LightRenderMode.ForcePixel;
                light.lightmapBakeType = LightmapBakeType.Realtime;
                CityLightHalo halo = CityLightHalo.CreateNightRegistered(fixture, Vector3.zero,
                    .16f, .50f, new Color(1f, .72f, .47f, .075f), new Color(.8f, .51f, .3f, .025f));
                CityNightSiteLightRegistry.Register(light, 14f, halo);
            }
        }

        private static void BuildCanopyLight(CityPortController port, Color warm)
        {
            // Reuse the authored industrial lamp parts. The plate meets the
            // underside of the existing roof; the head and emitter sit below.
            CityMiscAssetProvider models = CityMiscAssetProvider.LoadOrThrow();
            Transform mount = new GameObject("Port Canopy Work Lamp").transform;
            mount.SetParent(port.transform, false);
            mount.position = port.Plan.World(new Vector3(8.2f, 4.155f, -6.8f));
            mount.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            AddCanopyLampMesh(mount, models, CityMiscKind.YardSpotlightWallMount);
            Transform head = new GameObject("Canopy Lamp Head").transform;
            head.SetParent(mount, false);
            head.position = port.Plan.World(new Vector3(8.2f, 3.92f, -6.8f));
            head.rotation = Quaternion.LookRotation(port.Plan.World(new Vector3(8.8f, 1.8f, -6.8f)) - head.position);
            AddCanopyLampMesh(head, models, CityMiscKind.YardSpotlightHeadShell);
            Transform emitter = new GameObject("Port Canopy Work Light").transform;
            emitter.SetParent(head, false);
            emitter.localPosition = Vector3.forward * .065f;
            Light light = emitter.gameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = warm;
            light.range = 7f;
            light.spotAngle = 160f;
            light.innerSpotAngle = 135f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = .72f;
            light.shadowBias = .025f;
            light.shadowNormalBias = .12f;
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            CityLightHalo halo = CityLightHalo.CreateNightRegistered(emitter, Vector3.zero,
                .23f, .65f, new Color(1f, .72f, .47f, .14f), new Color(.8f, .51f, .3f, .04f));
            CityNightSiteLightRegistry.Register(light, 16f, halo);
        }

        private static void AddCanopyLampMesh(Transform host, CityMiscAssetProvider models, CityMiscKind kind)
        {
            var part = new GameObject("Imported " + kind);
            part.transform.SetParent(host, false);
            part.AddComponent<MeshFilter>().sharedMesh = models.GetPartOrThrow(kind, 0, 0).Mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = CityPortAssetProvider.GetSurfaceMaterial("SteelDark");
        }

        private static void BuildVesselSearchlight(CityPortController port, Color warm)
        {
            Transform fixture = CityPortAssetProvider.FindPart(port.Vessel.gameObject, "ANCHOR_Searchlight");
            Transform target = CityPortAssetProvider.FindPart(port.Vessel.gameObject, "ANCHOR_SearchlightTarget");
            Renderer glass = CityPortAssetProvider.FindPart(port.Vessel.gameObject, "SearchlightGlass").GetComponent<Renderer>();
            Renderer shaft = CityPortAssetProvider.FindPart(port.Vessel.gameObject, "SearchlightBeam").GetComponent<Renderer>();

            Transform root = new GameObject("Port Vessel Searchlight").transform;
            // The dedicated host is captured by the vessel's distance gate.
            // Day/night can re-enable components without reopening that host.
            root.SetParent(port.Vessel, false);
            root.position = fixture.position;
            root.rotation = Quaternion.LookRotation(target.position - fixture.position, port.Vessel.up);
            Light light = root.gameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = warm;
            light.range = 26f;
            light.spotAngle = 36f;
            light.innerSpotAngle = 22f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = .72f;
            light.shadowBias = .025f;
            light.shadowNormalBias = .12f;
            CityLightHalo halo = CityLightHalo.CreateNightRegistered(root, Vector3.zero,
                .40f, 1.3f, new Color(1f, .72f, .47f, .18f), new Color(.8f, .51f, .3f, .055f));
            CityNightSiteLightRegistry.Register(light, 22f, halo);
            root.gameObject.AddComponent<CityPortSearchlightShaft>().Initialize(glass, shaft);
        }
    }

    /// <summary>The passive Blender shaft shares the fleet's additive material and fixture floor.</summary>
    internal sealed class CityPortSearchlightShaft : MonoBehaviour
    {
        private MaterialPropertyBlock properties;
        private Renderer glass, shaft;
        private float appliedFactor = -1f;

        internal void Initialize(Renderer lens, Renderer beam)
        {
            glass = lens;
            shaft = beam;
            properties = new MaterialPropertyBlock();
            Prepare(glass);
            Prepare(shaft);
            Apply();
        }

        private static void Prepare(Renderer renderer)
        {
            renderer.sharedMaterial = CityOffshoreBoatResources.Glow;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
        }

        private void LateUpdate() => Apply();
        private void OnEnable() { appliedFactor = -1f; Apply(); }

        private void Apply()
        {
            if (shaft == null || glass == null) return;
            float factor = GameTimeDayNightRules.FixtureFactor(CityNightSiteLightRegistry.NightFactor);
            if (Mathf.Approximately(factor, appliedFactor)) return;
            appliedFactor = factor;
            // The finite shaft fades along its authored UV.x. The lens shares
            // the same material with uniform glow; neither changes global fog.
            Set(glass, 1f, .72f * factor);
            Set(shaft, 0f, .085f * factor);
        }

        private void Set(Renderer renderer, float uniform, float intensity)
        {
            properties.Clear();
            properties.SetColor("_BeamColor", new Color(2.3f, 1.55f, .68f, 1f));
            properties.SetFloat("_Uniform", uniform);
            properties.SetFloat("_Intensity", intensity);
            properties.SetFloat("_FadeStartDistance", 36f);
            properties.SetFloat("_FadeEndDistance", 46f);
            renderer.SetPropertyBlock(properties);
        }
    }
}
