using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Passive, authored mainland panorama. The imported metre geometry keeps
    /// its true angles while the shader brings depth inside City's far clip.
    /// Neither the road nor the distant city is a gameplay area.
    /// </summary>
    public static class CityEastDistanceWorldBuilder
    {
        public const string ResourcePath = "City/EastExit/CityEastDistance3D";
        public const string ObjectName = "Distant Mainland Road and City";
        public const float DepthBandMeters = .20f;

        private static readonly string[] Roles =
        {
            "DistanceLand", "DistanceShoulder", "DistanceRoad",
            "DistanceCity", "DistanceWindows", "DistanceGlow",
            "DistanceRock", "DistanceVegetation", "DistanceTrafficBody",
            "DistanceTrafficGlass", "DistanceTrafficHead", "DistanceTrafficTail",
            "DistanceLampBody", "DistanceLampLens", "DistanceLampHalo", "DistanceLampPool"
        };
        private static readonly Color[] Colours =
        {
            new Color(.20f, .235f, .20f), new Color(.29f, .28f, .24f),
            new Color(.075f, .085f, .085f), new Color(.095f, .12f, .12f),
            new Color(1.02f, .65f, .32f), new Color(.92f, .62f, .32f),
            new Color(.25f, .27f, .24f), new Color(.16f, .21f, .16f),
            new Color(.10f, .125f, .115f), new Color(.19f, .24f, .235f),
            new Color(1.4f, 1.18f, .77f), new Color(.85f, .16f, .075f),
            new Color(.085f, .095f, .12f), new Color(3.2f, 1.65f, .45f),
            new Color(2.2f, 1.25f, .42f), new Color(.75f, .51f, .25f)
        };
        // Opaque panorama surfaces share compressed depth. Only the soft glow
        // is blended afterwards; the road can no longer paint through a ridge.
        private static readonly int[] Queues = { 2820, 2821, 2822, 2820, 2823, 2825,
            2820, 2820, 2823, 2824, 2824, 2824, 2823, 2824, 2825, 2824 };
        private static Material[] sharedMaterials;

        public static GameObject Build(Transform parent, CityEastExitPlan plan)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.IsEnabled) return null;
            GameObject source = Resources.Load<GameObject>(ResourcePath);
            if (source == null)
                throw new InvalidOperationException("Missing authored mainland panorama: " + ResourcePath);
            GameObject instance = UnityEngine.Object.Instantiate(source, parent, false);
            instance.name = ObjectName;
            instance.transform.SetPositionAndRotation(plan.RoadEnd, source.transform.rotation);
            // Preserve the imported FBX root scale. Changing it to one would
            // reduce both road and city to one hundredth of their dimensions.
            int[] roleCounts = new int[Roles.Length];
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                int role = RoleOf(renderer.name);
                if (role < 0)
                    throw new InvalidOperationException("Unknown mainland mesh role: " + renderer.name);
                roleCounts[role]++;
                renderer.sharedMaterial = MaterialFor(role);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.allowOcclusionWhenDynamic = false;
                // Shader-displaced distant geometry must not be culled by its
                // unprojected source bounds. This is a renderer override, not
                // a mutation or clone of the shared imported mesh asset.
                float units = Mathf.Max(.0001f, renderer.transform.lossyScale.x);
                renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * (50000f / units));
            }
            for (int i = 0; i < roleCounts.Length; i++)
                if (roleCounts[i] == 0)
                    throw new InvalidOperationException("Missing mainland mesh role: " + Roles[i]);
            if (instance.GetComponentsInChildren<Collider>(true).Length != 0 ||
                instance.GetComponentsInChildren<Light>(true).Length != 0 ||
                instance.GetComponentsInChildren<AudioSource>(true).Length != 0)
                throw new InvalidOperationException("The mainland panorama must remain passive.");
            instance.AddComponent<CityEastDistanceTraffic>().Initialize(plan.RoadEnd, plan.Layout.Seed);
            return instance;
        }

        private static int RoleOf(string meshName)
        {
            for (int i = 0; i < Roles.Length; i++)
                if (meshName.StartsWith(Roles[i], StringComparison.Ordinal)) return i;
            return -1;
        }

        private static Material MaterialFor(int role)
        {
            if (sharedMaterials == null) sharedMaterials = new Material[Roles.Length];
            if (sharedMaterials[role] != null) return sharedMaterials[role];
            Shader shader = Resources.Load<Shader>("Shaders/CityEastDistance");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("Missing or unsupported mainland distance shader.");
            var material = new Material(shader)
            {
                name = "East Distance " + Roles[role] + " (Shared)",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
                renderQueue = Queues[role]
            };
            material.SetColor("_HazeColor", RuntimeSceneSetup.CityFogColor);
            material.SetColor("_Tint", Colours[role]);
            material.SetFloat("_Role", role);
            material.SetFloat("_DepthBandMeters", DepthBandMeters);
            material.SetFloat("_DepthWrite", role == 5 || role >= 14 ? 0f : 1f);
            material.SetTexture("_RockMap", Resources.Load<Texture2D>(
                CityMountainSurfaceAppearance.RockTextureResourcePath));
            material.SetTexture("_RoadMap", CityExteriorAppearance.RoadTexture);
            sharedMaterials[role] = material;
            return material;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (sharedMaterials == null) return;
            foreach (Material material in sharedMaterials)
                if (material != null) UnityEngine.Object.Destroy(material);
            sharedMaterials = null;
        }
    }
}
