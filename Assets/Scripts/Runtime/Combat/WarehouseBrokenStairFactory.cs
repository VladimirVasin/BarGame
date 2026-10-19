using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Instantiates the authored broken upper flight with its severed collision and shared surfaces.</summary>
    public static class WarehouseBrokenStairFactory
    {
        public const string ResourceFolder = "CombatWarehouse/";
        private static GameObject template;
        private static bool measured;

        [Serializable] private sealed class Manifest { public Model[] models; }
        [Serializable] private sealed class Model { public float[] bounds_min, bounds_max; }

        public static GameObject Create(Transform parent, Vector3 position)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (template == null)
            {
                template = Resources.Load<GameObject>(ResourceFolder + "BrokenStair");
                if (template == null) throw new InvalidOperationException("Missing authored broken warehouse stair.");
            }
            var wrapper = new GameObject("Warehouse Broken Stair");
            wrapper.transform.SetParent(parent, false);
            try
            {
                // The imported root retains its centimetre scale inside this metre-space wrapper.
                UnityEngine.Object.Instantiate(template, wrapper.transform, false);
                foreach (MeshRenderer renderer in wrapper.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.name.StartsWith("Collision_", StringComparison.Ordinal))
                    {
                        renderer.enabled = false;
                        var collider = renderer.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                        FootstepGround.Stamp(collider, FootstepGroundKind.Metal);
                        continue;
                    }
                    int separator = renderer.name.LastIndexOf("__", StringComparison.Ordinal);
                    if (separator < 0) throw new InvalidOperationException("Broken stair mesh lacks a shared surface role.");
                    renderer.sharedMaterial = WarehouseArenaAssetProvider.GetPropSurface(renderer.name.Substring(separator + 2));
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
                if (!measured) { ValidateBounds(wrapper); measured = true; }
                wrapper.transform.localPosition = position;
                return wrapper;
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
            TextAsset text = Resources.Load<TextAsset>(ResourceFolder + "WarehouseBrokenStair3D");
            Manifest manifest = text == null ? null : JsonUtility.FromJson<Manifest>(text.text);
            if (manifest?.models == null || manifest.models.Length != 1)
                throw new InvalidOperationException("Broken warehouse stair requires its measured model manifest.");
            Model expected = manifest.models[0];
            if (expected.bounds_min?.Length != 3 || expected.bounds_max?.Length != 3)
                throw new InvalidOperationException("Broken warehouse stair manifest lacks metre bounds.");
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
            var low = new Vector3(expected.bounds_min[0], expected.bounds_min[1], expected.bounds_min[2]);
            var high = new Vector3(expected.bounds_max[0], expected.bounds_max[1], expected.bounds_max[2]);
            if (!found || Vector3.Distance(actual.min, low) > .03f || Vector3.Distance(actual.max, high) > .03f)
                throw new InvalidOperationException("Imported broken warehouse stair disagrees with its measured metre bounds.");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() { template = null; measured = false; }
    }
}
