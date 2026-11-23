using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Converts metre-authored Blender parts into plan-space placements using their exported axes.</summary>
    internal sealed class CityImportedMeshKit
    {
        private readonly Dictionary<string, MeshFilter> meshes = new(StringComparer.Ordinal);
        private readonly Quaternion inverseBasis;
        private readonly float unit;

        internal CityImportedMeshKit(string resource, string referencePart)
        {
            var asset = Resources.Load<GameObject>(resource);
            if (!asset) throw new InvalidOperationException("Missing model kit " + resource);
            foreach (var part in asset.GetComponentsInChildren<MeshFilter>(true))
                meshes.Add(part.name, part);
            Transform owner = meshes[referencePart].transform.parent;
            Vector3 forward = owner.Find("AxisForward").position - owner.position;
            Vector3 up = owner.Find("AxisUp").position - owner.position;
            inverseBasis = Quaternion.Inverse(Quaternion.LookRotation(forward, up));
            unit = up.magnitude;
        }

        internal RuntimeMeshPlacement Place(string name, Vector3 at, Quaternion rotation, Vector3 size)
        {
            MeshFilter part = meshes[name];
            Quaternion axes = inverseBasis * part.transform.rotation;
            Vector3 local = Quaternion.Inverse(axes) * size;
            Vector3 scale = Vector3.Scale(part.transform.lossyScale / unit,
                new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z)));
            return new RuntimeMeshPlacement(part.sharedMesh, at, rotation * axes, scale);
        }
    }
}
