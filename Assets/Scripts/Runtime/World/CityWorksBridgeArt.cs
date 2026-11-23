using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Places the metric Works kit on the river plan's span, bed and tread datums.</summary>
    internal sealed class CityWorksBridgeArt
    {
        internal const string FinishPath = "City/Surfaces/WorksBridge";
        private readonly Dictionary<string, MeshFilter> parts = new(StringComparer.Ordinal);
        private readonly Material material;

        internal CityWorksBridgeArt()
        {
            GameObject source = Resources.Load<GameObject>("City/WorksBridge");
            material = Resources.Load<Material>(FinishPath);
            if (!source || !material) throw new InvalidOperationException("Missing Works bridge kit.");
            foreach (MeshFilter part in source.GetComponentsInChildren<MeshFilter>(true))
                parts.Add(part.name, part);
        }

        // The FBX root owns the unit conversion and may rotate its mesh axes at import.
        private RuntimeMeshPlacement Fit(string part, Vector3 centre, Vector3 size, Quaternion rotation)
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

        private GameObject Batch(string name, Transform root, List<RuntimeMeshPlacement> partsToPlace, bool stone)
        {
            GameObject host = RuntimePrimitiveFactory.CreateCombinedMeshes(name, root, partsToPlace, Color.white);
            Renderer renderer = host.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            CityWetSurfaceRegistry.Register(renderer, stone ? CityWetSurfaceKind.Sidewalk : CityWetSurfaceKind.PaintedSteel, Color.white);
            return host;
        }

        internal void Span(Transform root, Rect span, float deckY, float riverBedY)
        {
            const float soffit = -.08f, bearing = -1.2425f;
            Vector3 size = new Vector3(span.width, soffit - bearing, span.height * (7.94f / 8f));
            var centre = new Vector3(span.center.x, deckY + (soffit + bearing) * .5f, span.center.y);
            GameObject bed = Batch("Bridge Underside", root, new() { Fit("PlateGirders", centre, size, Quaternion.identity) }, false);
            Box(bed, new Vector3(centre.x, deckY - .182f, centre.z), new Vector3(size.x, .204f, size.z));
            float height = Mathf.Max(.1f, deckY + bearing - riverBedY);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                var pier = new Vector3(centre.x + sign * span.width * .30f,
                    deckY + bearing - height * .5f, centre.z);
                var extent = new Vector3(.96f, height, span.height * (7.96f / 8f));
                GameObject host = Batch(sign < 0 ? "Bridge Pier West" : "Bridge Pier East", root,
                    new() { Fit("Pier", pier, extent, Quaternion.identity) }, true);
                Box(host, pier, extent);
            }
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

        internal void Rail(string name, Transform root, Vector3 start, Vector3 end, IReadOnlyList<Bounds> treads = null)
        {
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length < .10f) return;
            Vector3 dir = delta / length;
            Vector3 mid = (start + end) * .5f;
            Quaternion orientation = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.ProjectOnPlane(Vector3.up, dir));
            var members = new List<RuntimeMeshPlacement>();
            int bays = Mathf.Max(1, Mathf.CeilToInt(length / 2.1f));
            float inset = Mathf.Min(.09f, length * .24f);
            for (int index = 0; index <= bays; index++)
            {
                Vector3 at = start + dir * Mathf.Lerp(inset, length - inset, index / (float)bays);
                float footY = at.y;
                if (treads != null)
                {
                    // A shoe needs one flat tread beneath its entire footprint.
                    int tread = Mathf.Clamp(Mathf.FloorToInt(Vector3.Dot(at - start, dir) / length * treads.Count), 0, treads.Count - 1);
                    at.z = treads[tread].center.z;
                    at.y = Mathf.Lerp(start.y, end.y, (at.z - start.z) / delta.z);
                    footY = treads[tread].max.y;
                }
                float postHeight = at.y + 1.016f - footY;
                members.Add(Fit("Stanchion", new Vector3(at.x, footY + postHeight * .5f, at.z),
                    new Vector3(.17f, postHeight, .17f), Quaternion.identity));
            }
            for (int bay = 0; bay < bays; bay++)
            {
                Vector3 at = Vector3.Lerp(start, end, (bay + .5f) / bays);
                float run = length / bays - .004f;
                members.Add(Fit("Rail", at + Vector3.up * 1.05f, new Vector3(run, .072f, .115f), orientation));
                members.Add(Fit("Infill", at + Vector3.up * .52f, new Vector3(run, .054f, .047f), orientation));
            }
            GameObject rail = Batch(name, root, members, false);
            // Level spans keep the collider on the named guard. Sloping guards need its local axis.
            if (Mathf.Abs(delta.y) < .001f)
                Box(rail, mid + Vector3.up * .54f, new Vector3(Mathf.Abs(delta.x) + .115f * Mathf.Abs(dir.z),
                    1.08f, Mathf.Abs(delta.z) + .115f * Mathf.Abs(dir.x)));
            else
            {
                Transform guard = new GameObject("Sloping guard").transform;
                guard.SetParent(rail.transform, false);
                guard.localPosition = mid + Vector3.up * .54f;
                guard.localRotation = orientation;
                Box(guard.gameObject, Vector3.zero, new Vector3(length, 1.08f, .115f));
            }
        }

        private static void Box(GameObject host, Vector3 centre, Vector3 size)
        {
            BoxCollider box = host.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
        }
    }
}
