using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Small authored supplement; shared warehouse surfaces and imported unit roots stay intact.</summary>
    internal static class WarehouseGateAssets
    {
        private static readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>(3);

        internal static GameObject Create(string kind, Transform parent, Vector3 position, Quaternion rotation)
        {
            if (!Templates.TryGetValue(kind, out GameObject template) || template == null)
            {
                template = Resources.Load<GameObject>("CombatWarehouse/GateChain/" + kind);
                if (template == null) throw new InvalidOperationException("Missing authored warehouse gate asset " + kind);
                Templates[kind] = template;
            }
            var wrapper = new GameObject("Warehouse " + kind);
            wrapper.transform.SetParent(parent, false);
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
                if (separator < 0) throw new InvalidOperationException("Gate asset has no surface role.");
                string role = renderer.name.Substring(separator + 2);
                int duplicate = role.LastIndexOf('.');
                if (duplicate >= 0 && int.TryParse(role.Substring(duplicate + 1), out _)) role = role.Substring(0, duplicate);
                renderer.sharedMaterial = WarehouseArenaAssetProvider.Surface(kind == "ValveChain" ? "GateControl" : "Platform",
                    role);
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            wrapper.transform.SetLocalPositionAndRotation(position, rotation);
            return wrapper;
        }
    }
}
