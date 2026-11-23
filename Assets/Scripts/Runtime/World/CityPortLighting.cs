using UnityEngine;

namespace BarPromenade
{
    /// <summary>Port fixtures share the ordinary day/night law.</summary>
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
            var fixtures = new (string Anchor, string Name, Vector3 Target, float Range, float Outer, float Inner, float Intensity)[]
            {
                ("A", "Port Quay Work Light", new Vector3(-5f, 1.66f, -10.45f), 12f, 112f, 68f, 8f),
                ("B", "Port Yard Work Light", new Vector3(19.2f, 1.55f, -23.2f), 18.5f, 126f, 102f, 52f),
                ("C", "Port Yard West Light", new Vector3(10.5f, 1.55f, -29.5f), 17f, 119f, 100f, 52f),
                ("D", "Port Yard Loading Light", new Vector3(11.35f, 1.55f, -17.6f), 19f, 117f, 90f, 44f)
            };
            foreach (var specification in fixtures)
            {
                Transform fixture = CityPortAssetProvider.FindPart(port.Dock.gameObject, "ANCHOR_WorkLight" + specification.Anchor);
                Transform root = new GameObject(specification.Name).transform;
                root.SetParent(port.transform, false);
                root.position = fixture.position;
                Vector3 target = port.Plan.World(specification.Target);
                root.rotation = Quaternion.LookRotation(target - fixture.position, Vector3.up);
                Light light = root.gameObject.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = warm;
                light.range = specification.Range;
                light.spotAngle = specification.Outer;
                light.innerSpotAngle = specification.Inner;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = .72f;
                light.shadowBias = .025f;
                light.shadowNormalBias = .12f;
                if (specification.Anchor != "A")
                {
                    Texture2D cutoff = CreateYardCutoff(port.Plan, light);
                    port.PavingLightCookies.Add(cutoff);
                    light.cookie = cutoff;
                }
                CityLightHalo halo = CityLightHalo.CreateNightRegistered(root, Vector3.zero,
                    .30f, .95f, new Color(1f, .72f, .47f, .10f), new Color(.8f, .51f, .3f, .035f));
                CityNightSiteLightRegistry.Register(light, specification.Intensity, halo);
            }
            BuildWarehouseLights(port, warm);
            BuildCanopyLight(port, warm);
            BuildCraneAndQuayLights(port);
            BuildVesselSearchlight(port, warm);
        }

        private static void BuildCraneAndQuayLights(CityPortController port)
        {
            Color warm = new Color(1f, .78f, .56f);
            Light west = BuildFloodlight(port.Dock, "QuayFloodWest", warm, 165f, 19f, 132f, 96f, true);
            Light east = BuildFloodlight(port.Dock, "QuayFloodEast", warm, 157f, 15f, 126f, 84f, true);
            // The two floods extend across the approaches but cut off at the
            // berth. Their low-angle optics also suppress the near hot spot.
            Rect paving = port.Plan.RectAt(-16.5f, -13f, 16.5f, .42f);
            foreach (Light light in new[] { west, east })
            {
                Texture2D cutoff = CreatePavingCutoff(light, port.Plan.QuayTopY, 128, 4.5f, paving);
                light.cookie = cutoff;
                port.PavingLightCookies.Add(cutoff);
            }
            for (int index = 0; index < port.CraneBases.Length; index++)
            {
                BuildFloodlight(port.Booms[index], "CraneLoadLamp", warm, 88f, 15f, 62f, 45f, true);
                BuildFloodlight(port.CraneBases[index], "CraneMechanismLamp", warm, 10f, 3.8f, 104f, 72f, false);
                BuildFloodlight(port.CraneBases[index], "CraneControlLamp", warm, 11f, 4.2f, 104f, 76f, false);
            }
        }

        private static Light BuildFloodlight(Transform model, string name, Color tint,
            float intensity, float range, float outerAngle, float innerAngle, bool castsShadows)
        {
            Transform origin = CityPortAssetProvider.FindPart(model.gameObject, "ANCHOR_" + name);
            Transform aim = CityPortAssetProvider.FindPart(model.gameObject, "ANCHOR_" + name + "Aim");
            Renderer glass = CityPortAssetProvider.FindPart(model.gameObject, name + "Glass").GetComponent<Renderer>();
            glass.sharedMaterial = CityNightResources.EmissiveMaterial;
            glass.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            CityNightGlowRegistry.Register(glass, tint * (castsShadows ? 4.6f : 2.6f));

            // Keep metre scale for the halo while following the imported fixture's pivot.
            Transform emitter = new GameObject(name + " Light").transform;
            emitter.SetParent(origin, true);
            emitter.SetPositionAndRotation(origin.position, Quaternion.LookRotation(aim.position - origin.position));
            Light light = emitter.gameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = tint;
            light.range = range;
            light.spotAngle = outerAngle;
            light.innerSpotAngle = innerAngle;
            light.shadows = castsShadows ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = .74f;
            light.renderMode = LightRenderMode.ForcePixel;
#if UNITY_EDITOR
            light.lightmapBakeType = LightmapBakeType.Realtime;
#endif
            CityLightHalo halo = null;
            if (castsShadows)
                halo = CityLightHalo.CreateNightRegistered(emitter, Vector3.zero, .19f, .72f,
                    new Color(1f, .78f, .56f, .08f), new Color(.8f, .57f, .34f, .026f));
            CityNightSiteLightRegistry.Register(light, intensity, halo);
            return light;
        }

        private static Texture2D CreateYardCutoff(CityPortPlan plan, Light light)
        {
            // The long overlapping cones need a soft cutoff at the actual
            // paving edges. Their below-deck continuation is occluded by
            // the solid apron. Bridge the two patches at their shared seam.
            Rect lower = plan.Access?.LowerYard ?? plan.RectAt(4f, -35f, 28f, -20f);
            Rect upper = plan.Access?.UpperYard ?? plan.RectAt(4f, -20f, 23f, -10f);
            Rect join = Rect.MinMaxRect(Mathf.Max(lower.xMin, upper.xMin), lower.yMax - 2f,
                Mathf.Min(lower.xMax, upper.xMax), upper.yMin + 2f);
            return CreatePavingCutoff(light, plan.QuayTopY, 64, 0f, lower, upper, join);
        }

        private static Texture2D CreatePavingCutoff(Light light, float groundY, int size,
            float nearSpread, params Rect[] paving)
        {
            var pixels = new Color32[size * size];
            Transform fixture = light.transform;
            Vector3 source = fixture.position;
            float depth = source.y - groundY;
            float tangent = Mathf.Tan(light.spotAngle * .5f * Mathf.Deg2Rad);
            for (int y = 1; y < size - 1; y++)
            for (int x = 1; x < size - 1; x++)
            {
                Vector3 ray = fixture.TransformDirection(new Vector3(
                    (2f * (x + .5f) / size - 1f) * tangent,
                    (2f * (y + .5f) / size - 1f) * tangent, 1f));
                if (ray.y >= -.001f) continue;
                Vector3 landing = source + ray * (-depth / ray.y);
                float inside = float.NegativeInfinity;
                foreach (Rect area in paving) inside = Mathf.Max(inside, EdgeDistance(area, landing));
                float cutoff = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.18f, 1.35f, inside));
                if (nearSpread > 0f)
                {
                    float offset = new Vector2(landing.x - source.x, landing.z - source.z).magnitude;
                    cutoff *= Mathf.SmoothStep(.14f, 1f, offset / nearSpread);
                }
                byte value = (byte)Mathf.RoundToInt(255f * cutoff);
                pixels[y * size + x] = new Color32(value, value, value, value);
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = light.name + " paved cutoff",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static float EdgeDistance(Rect area, Vector3 point) =>
            Mathf.Min(Mathf.Min(point.x - area.xMin, area.xMax - point.x),
                Mathf.Min(point.z - area.yMin, area.yMax - point.z));

        private static void BuildWarehouseLights(CityPortController port, Color warm)
        {
            Vector3[] targets =
            {
                new Vector3(1.4f, 1.55f, -13.7f),
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
#if UNITY_EDITOR
                light.lightmapBakeType = LightmapBakeType.Realtime;
#endif
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
            head.rotation = Quaternion.LookRotation(port.Plan.World(new Vector3(8.35f, 1.8f, -7.17f)) - head.position);
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
#if UNITY_EDITOR
            light.lightmapBakeType = LightmapBakeType.Realtime;
#endif
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
            properties.SetFloat("_SoftShaft", 1f - uniform);
            properties.SetFloat("_Intensity", intensity);
            properties.SetFloat("_FadeStartDistance", 36f);
            properties.SetFloat("_FadeEndDistance", 46f);
            renderer.SetPropertyBlock(properties);
        }
    }
}
