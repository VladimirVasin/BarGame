using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The checkpoint's authored city seen from the broken village road.
    /// Only its passive city and valley meshes are shared; the working road,
    /// traffic and street lamps belong to the checkpoint approach alone.
    /// </summary>
    public static class AlpineVillageDistanceWorldBuilder
    {
        public const string ObjectName = "Distant City Below Broken Road";
        public const float BasinDropMeters = 1740f;
        public const float StormVisibilityFloor = .065f;

        public static GameObject Build(Transform parent, AlpineVillagePlan plan)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            GameObject source = Resources.Load<GameObject>(CityEastDistanceWorldBuilder.ResourcePath);
            if (source == null)
                throw new InvalidOperationException("Missing the shared authored mainland panorama.");

            var instance = new GameObject(ObjectName);
            instance.transform.SetParent(parent, false);
            Vector3 downhill = -plan.Uphill;
            Quaternion bearing = Quaternion.FromToRotation(Vector3.right, downhill);
            instance.transform.SetPositionAndRotation(
                plan.Expansion.CliffEdge - Vector3.up * BasinDropMeters,
                bearing * source.transform.rotation);
            instance.transform.localScale = source.transform.lossyScale;
            Vector3 sourceScale = source.transform.lossyScale;
            Vector3 inverseScale = new Vector3(1f / sourceScale.x, 1f / sourceScale.y, 1f / sourceScale.z);
            int count = 0;
            foreach (MeshRenderer template in source.GetComponentsInChildren<MeshRenderer>(true))
            {
                int role = CityEastDistanceWorldBuilder.RoleOf(template.name);
                if (role != 0 && role != 3 && role != 4 && role != 5 && role != 6 && role != 7)
                    continue;
                GameObject meshObject = UnityEngine.Object.Instantiate(template.gameObject, instance.transform, false);
                meshObject.name = template.name;
                // Preserve the imported metre transform even if the FBX has
                // an extra authoring root between this mesh and its asset.
                meshObject.transform.localPosition = source.transform.InverseTransformPoint(template.transform.position);
                meshObject.transform.localRotation = Quaternion.Inverse(source.transform.rotation) * template.transform.rotation;
                meshObject.transform.localScale = Vector3.Scale(template.transform.lossyScale, inverseScale);
                Material material = CityEastDistanceWorldBuilder.MaterialFor(role, village: true);
                material.SetVector("_ViewDirection", downhill);
                CityEastDistanceWorldBuilder.ConfigureRenderer(meshObject.GetComponent<MeshRenderer>(), material);
                count++;
            }
            if (count != 6)
                throw new InvalidOperationException("The shared city requires its three city and three relief meshes.");
            if (instance.GetComponentsInChildren<Collider>(true).Length != 0 ||
                instance.GetComponentsInChildren<Light>(true).Length != 0 ||
                instance.GetComponentsInChildren<AudioSource>(true).Length != 0)
                throw new InvalidOperationException("The distant village city must remain passive.");
            SetVisibility(RuntimeSceneSetup.AlpineVillageFogColor, 0f);
            return instance;
        }

        /// <summary>
        /// The village root passes the already-applied haze and actual smoothed
        /// storm wave. No extra weather clock, global fog write or material
        /// instance is created, and the checkpoint profile stays untouched.
        /// </summary>
        public static void SetVisibility(Color hazeColor, float stormWave01)
        {
            float visibility = Mathf.Lerp(1f, StormVisibilityFloor, Mathf.Clamp01(stormWave01));
            CityEastDistanceWorldBuilder.SetVillageVisibility(hazeColor, visibility);
        }

        public static Vector3 CityAimPoint(AlpineVillagePlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            return plan.Expansion.CliffEdge - plan.Uphill * 8500f -
                Vector3.up * (BasinDropMeters + 105f);
        }
    }
}
