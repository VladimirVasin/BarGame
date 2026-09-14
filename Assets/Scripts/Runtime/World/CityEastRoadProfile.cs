using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The measured Blender road profile shared by the real approach and distant valley.</summary>
    public sealed class CityEastRoadProfile
    {
        [Serializable] private sealed class RoadPoint
        {
            public float x, y, z;
            public Vector3 Position => new Vector3(x, y, z);
        }

        [Serializable] private sealed class Approach
        {
            public float gate_x, flat_end_x, real_end_x;
            public float road_crossfall, road_ground_lift, outer_shoulder_offset;
            public float ground_flat_half_width, ground_blend_half_width;
        }

        [Serializable] public sealed class Lamp
        {
            public float x, y, z, forward_x, forward_z;
            public bool real;
            public Vector3 Position => new Vector3(x, y, z);
            public Vector3 Forward => new Vector3(forward_x, 0f, forward_z).normalized;
        }

        [Serializable] private sealed class Manifest
        {
            public RoadPoint[] road_route_points;
            public Approach approach;
            public Lamp[] road_lamps;
        }

        private static CityEastRoadProfile shared;
        private readonly RoadPoint[] route;
        private readonly Approach approach;
        public float GateX => approach.gate_x;
        public float FlatEndX => approach.flat_end_x;
        public float RealEndX => approach.real_end_x;
        public float Crossfall => approach.road_crossfall;
        public float GroundLift => approach.road_ground_lift;
        public float OuterShoulderOffset => approach.outer_shoulder_offset;
        public float GroundFlatHalfWidth => approach.ground_flat_half_width;
        public float GroundBlendHalfWidth => approach.ground_blend_half_width;
        public IReadOnlyList<Lamp> Lamps { get; }

        private CityEastRoadProfile(Manifest manifest)
        {
            route = manifest.road_route_points;
            approach = manifest.approach;
            if (route == null || route.Length < 2 || approach == null ||
                !Finite(approach.gate_x) || !Finite(approach.flat_end_x) || !Finite(approach.real_end_x) ||
                approach.flat_end_x <= approach.gate_x || approach.real_end_x <= approach.flat_end_x ||
                !Finite(approach.road_crossfall) || approach.road_crossfall < 0f ||
                !Finite(approach.road_ground_lift) || approach.road_ground_lift <= 0f ||
                !Finite(approach.outer_shoulder_offset) || approach.outer_shoulder_offset < approach.road_ground_lift ||
                !Finite(approach.ground_flat_half_width) || approach.ground_flat_half_width < 5f ||
                !Finite(approach.ground_blend_half_width) ||
                approach.ground_blend_half_width <= approach.ground_flat_half_width)
                throw new InvalidOperationException("The eastern valley lacks its measured approach profile.");
            float previous = float.NegativeInfinity;
            foreach (RoadPoint point in route)
            {
                if (point == null || !Finite(point.x) || !Finite(point.y) || !Finite(point.z) || point.x <= previous)
                    throw new InvalidOperationException("The eastern road needs finite strictly ordered X samples.");
                previous = point.x;
            }
            if (route[0].x > approach.gate_x || previous < approach.real_end_x)
                throw new InvalidOperationException("The measured road must cover the whole checkpoint approach.");
            Lamps = Array.AsReadOnly(manifest.road_lamps ?? Array.Empty<Lamp>());
            foreach (Lamp lamp in Lamps)
                if (lamp == null || !Finite(lamp.x) || !Finite(lamp.y) || !Finite(lamp.z) ||
                    !Finite(lamp.forward_x) || !Finite(lamp.forward_z) || lamp.Forward.sqrMagnitude < .9f)
                    throw new InvalidOperationException("Eastern road lamps need measured positions and orientations.");
        }

        public static CityEastRoadProfile Load()
        {
            if (shared != null) return shared;
            TextAsset asset = Resources.Load<TextAsset>(CityEastDistanceWorldBuilder.ResourcePath);
            Manifest manifest = asset == null ? null : JsonUtility.FromJson<Manifest>(asset.text);
            if (manifest == null) throw new InvalidOperationException("Missing eastern valley road manifest.");
            return shared = new CityEastRoadProfile(manifest);
        }

        public Vector3 Sample(float localX)
        {
            int low = 0, high = route.Length - 1;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (route[middle].x <= localX) low = middle;
                else high = middle;
            }
            RoadPoint a = route[low], b = route[high];
            Vector3 sample = Vector3.Lerp(a.Position, b.Position, Mathf.InverseLerp(a.x, b.x, localX));
            sample.x = localX;
            return sample;
        }

        public float CrossfallAt(float localX) => Crossfall * Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(FlatEndX, RealEndX, localX));

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => shared = null;
    }
}
