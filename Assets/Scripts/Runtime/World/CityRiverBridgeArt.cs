using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Fits metre-authored bridge parts to the river plan, preserving per-face atlas UVs.</summary>
    internal abstract class CityRiverBridgeArt
    {
        private readonly Dictionary<string, MeshFilter> parts = new(StringComparer.Ordinal);
        private readonly Material material;

        private readonly CityWetSurfaceKind stoneResponse;

        protected CityRiverBridgeArt(string modelName, string finishPath, CityWetSurfaceKind stoneResponse = CityWetSurfaceKind.Sidewalk)
        {
            this.stoneResponse = stoneResponse;
            GameObject source = Resources.Load<GameObject>("City/" + modelName);
            material = Resources.Load<Material>(finishPath);
            if (!source || !material) throw new InvalidOperationException($"Missing {modelName} bridge kit.");
            foreach (MeshFilter part in source.GetComponentsInChildren<MeshFilter>(true))
                parts.Add(part.name, part);
        }

        // The FBX root owns the unit conversion and may rotate its mesh axes at import.
        protected RuntimeMeshPlacement Fit(string part, Vector3 centre, Vector3 size, Quaternion rotation)
        {
            MeshFilter filter = parts[part];
            Bounds bounds = filter.sharedMesh.bounds;
            Quaternion axes = filter.transform.rotation;
            Vector3 local = Quaternion.Inverse(axes) * size;
            Vector3 scale = new Vector3(Mathf.Abs(local.x) / bounds.size.x,
                Mathf.Abs(local.y) / bounds.size.y, Mathf.Abs(local.z) / bounds.size.z);
            Quaternion pose = rotation * axes;
            return new RuntimeMeshPlacement(filter.sharedMesh, centre - pose * Vector3.Scale(bounds.center, scale), pose, scale);
        }

        protected GameObject Batch(string name, Transform root, List<RuntimeMeshPlacement> partsToPlace, bool stone = true)
        {
            GameObject host = RuntimePrimitiveFactory.CreateCombinedMeshes(name, root, partsToPlace, Color.white);
            Renderer renderer = host.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            CityWetSurfaceRegistry.Register(renderer, stone ? stoneResponse : CityWetSurfaceKind.PaintedSteel, Color.white);
            return host;
        }

        internal void Stairs(Transform root, CityRiverLandingDescriptor landing, IReadOnlyList<Bounds> steps)
        {
            var stones = new List<RuntimeMeshPlacement>(steps.Count);
            float rise = Mathf.Abs(landing.UpperY - landing.LowerY) / steps.Count;
            for (int i = 0; i < steps.Count; ++i)
            {
                Bounds step = steps[i];
                float blockHeight = Mathf.Max(.16f, rise + .035f);
                var middle = new Vector3(step.center.x, step.max.y - blockHeight * .5f, step.center.z);
                stones.Add(Fit((i & 1) == 0 ? "TreadA" : "TreadB", middle,
                    new Vector3(step.size.x, blockHeight, step.size.z - .016f), Quaternion.identity));
            }
            GameObject flight = Batch("Granite Stair Flight", root, stones, true);
            foreach (Bounds step in steps) Box(flight, step.center, step.size);
            Rect floor = landing.PlatformBounds;
            Vector3 plinth = new Vector3(floor.center.x, landing.LowerY - .12f, floor.center.y);
            Vector3 size = new Vector3(floor.width, .24f, floor.height);
            Quaternion facing = Quaternion.Euler(0f, landing.WestBank ? 0f : 180f, 0f);
            GameObject platform = Batch("Lower Waterside Platform", root, new() { Fit("Landing", plinth, size, facing) }, true);
            Box(platform, plinth, size);
        }

        protected static void Box(GameObject host, Vector3 centre, Vector3 size)
        {
            BoxCollider box = host.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
        }
    }
}
