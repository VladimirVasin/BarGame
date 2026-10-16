using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Authored hollow lockers retain FBX units, shared warehouse surfaces and moving collision.</summary>
    public static class WarehouseLockerFactory
    {
        public const string ResourceFolder = "CombatWarehouse/Lockers/";
        public static readonly Vector3 CabinetSize = new Vector3(2.2f, 1.9f, .65f);
        private static GameObject template;
        private static bool measured;

        [Serializable] private sealed class Manifest { public Model[] models; }
        [Serializable] private sealed class Model { public float[] bounds_min, bounds_max; }

        public static WarehouseLocker Create(Transform parent, CombatTestRoot owner, Vector3 position, float yaw)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (template == null)
            {
                template = Resources.Load<GameObject>(ResourceFolder + "WarehouseLocker");
                if (template == null) throw new InvalidOperationException("Missing authored warehouse locker model.");
            }
            var wrapper = new GameObject("Warehouse Locker");
            wrapper.transform.SetParent(parent, false);
            try
            {
                // The imported root keeps its centimetre factor; this wrapper is in world metres.
                UnityEngine.Object.Instantiate(template, wrapper.transform, false);
                foreach (MeshRenderer renderer in wrapper.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.name.StartsWith("Collision_", StringComparison.Ordinal))
                    {
                        renderer.enabled = false;
                        var collision = renderer.gameObject.AddComponent<MeshCollider>();
                        collision.sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                        FootstepGround.Stamp(collision, FootstepGroundKind.Metal);
                        continue;
                    }
                    int separator = renderer.name.LastIndexOf("__", StringComparison.Ordinal);
                    if (separator < 0) throw new InvalidOperationException("Locker mesh lacks a shared surface role.");
                    string role = renderer.name.Substring(separator + 2);
                    renderer.sharedMaterial = WarehouseArenaAssetProvider.GetPropSurface(role);
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
                if (!measured) { ValidateBounds(wrapper); measured = true; }
                wrapper.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
                var locker = wrapper.AddComponent<WarehouseLocker>();
                locker.Initialize(owner);
                return locker;
            }
            catch
            {
                wrapper.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(wrapper);
                else UnityEngine.Object.DestroyImmediate(wrapper);
                throw;
            }
        }

        private static void ValidateBounds(GameObject wrapper)
        {
            TextAsset text = Resources.Load<TextAsset>(ResourceFolder + "WarehouseLocker3D");
            Manifest manifest = text == null ? null : JsonUtility.FromJson<Manifest>(text.text);
            if (manifest?.models == null || manifest.models.Length != 1)
                throw new InvalidOperationException("Locker requires its measured model manifest.");
            Bounds actual = default;
            bool found = false;
            foreach (MeshRenderer renderer in wrapper.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled) continue;
                Bounds bounds = renderer.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 world = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    Vector3 point = wrapper.transform.InverseTransformPoint(world);
                    if (!found) { actual = new Bounds(point, Vector3.zero); found = true; }
                    else actual.Encapsulate(point);
                }
            }
            Model expected = manifest.models[0];
            var low = new Vector3(expected.bounds_min[0], expected.bounds_min[1], expected.bounds_min[2]);
            var high = new Vector3(expected.bounds_max[0], expected.bounds_max[1], expected.bounds_max[2]);
            if (!found || Vector3.Distance(actual.min, low) > .03f || Vector3.Distance(actual.max, high) > .03f)
                throw new InvalidOperationException("Imported locker geometry disagrees with its measured metre bounds.");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() { template = null; measured = false; }
    }
}
