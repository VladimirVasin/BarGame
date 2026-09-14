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
        public const float ProjectionRadius = 44f;

        private static readonly string[] Roles =
        {
            "DistanceLand", "DistanceShoulder", "DistanceRoad",
            "DistanceCity", "DistanceWindows", "DistanceGlow"
        };
        private static readonly Color[] Colours =
        {
            new Color(.20f, .235f, .20f), new Color(.29f, .28f, .24f),
            new Color(.075f, .085f, .085f), new Color(.095f, .12f, .12f),
            new Color(1.15f, .66f, .28f), new Color(.60f, .39f, .20f)
        };
        // Explicit painter ordering, since the imported landscape has very
        // large bounds and transparent object-centre sorting is meaningless.
        private static readonly int[] Queues = { 2822, 2823, 2824, 2820, 2821, 2819 };
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
                instance.GetComponentsInChildren<Light>(true).Length != 0)
                throw new InvalidOperationException("The mainland panorama must remain passive.");
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
            material.SetFloat("_ProjectionRadius", ProjectionRadius);
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
