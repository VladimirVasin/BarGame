using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class VillageErrandPropLibrary
    {
        public const string ResourcePath = "VillageLife/VillageErrandProps";
        [Serializable] public sealed class Manifest { public string version, scale_mode, build_signature; public VillageLifePropPart[] parts; public VillageLifePropAnchor[] anchors; }
        public static Manifest ReadManifest()
        {
            TextAsset source = Resources.Load<TextAsset>(ResourcePath);
            if (source == null) throw new InvalidOperationException("The authored village water pail is missing.");
            var result = JsonUtility.FromJson<Manifest>(source.text);
            if (result.version != "1.0.0" || result.scale_mode != "fixed_metres" || result.parts.Length != 3)
                throw new InvalidOperationException("The village water pail violates its passive metre contract.");
            return result;
        }
        public static Transform Create(Transform parent)
        {
            var definition = ReadManifest();
            var model = Resources.Load<GameObject>(ResourcePath);
            if (model == null) throw new InvalidOperationException("The authored village water pail FBX is missing.");
            var meshes = new Dictionary<string, MeshFilter>();
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>()) meshes.Add(filter.sharedMesh.name, filter);
            var root = new GameObject("Water bucket").transform; root.SetParent(parent, false);
            foreach (var part in definition.parts)
            {
                var template = meshes[part.mesh];
                var host = new GameObject(part.mesh).transform; host.SetParent(root, false);
                host.localPosition = template.transform.position; host.localRotation = template.transform.rotation;
                host.localScale = template.transform.lossyScale;
                host.gameObject.AddComponent<MeshFilter>().sharedMesh = template.sharedMesh;
                var renderer = host.gameObject.AddComponent<MeshRenderer>();
                if (part.surface == "Water") renderer.sharedMaterial = AlpineSpringWaterResources.PoolMaterial;
                else MountainRoadSurfaceAppearance.ApplyCombined(renderer, MountainRoadSurfaceKind.RustedIron,
                    new Color(part.tint[0], part.tint[1], part.tint[2], part.tint[3]));
            }
            foreach (var anchor in definition.anchors)
            {
                var host = new GameObject("ANCHOR_" + anchor.name).transform; host.SetParent(root, false);
                host.localPosition = VillageLifePropLibrary.Vector(anchor.position);
            }
            return root;
        }
    }
}
