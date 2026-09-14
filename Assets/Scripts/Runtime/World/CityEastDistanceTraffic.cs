using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Two visual cars sample the road exported with the valley. No navigation,
    /// physics, lights, audio or accumulated spawn queue belongs to this view.
    /// </summary>
    public sealed class CityEastDistanceTraffic : MonoBehaviour
    {
        [Serializable] private sealed class RoadPoint
        {
            public float x, y, z, distance;
            public Vector3 Position => new Vector3(x, y, z);
        }
        [Serializable] private sealed class TrafficSettings
        {
            public float body_length, body_width, body_height, lane_offset, lane_height_offset;
            public float start_distance, end_distance;
            public float visible_witness_distance;
            public int max_actors;
        }
        [Serializable] private sealed class Manifest
        {
            public int panorama_version;
            public RoadPoint[] road_route_points;
            public TrafficSettings traffic;
        }

        private Transform[] vehicles = Array.Empty<Transform>();
        private RoadPoint[] route;
        private Vector3 origin;
        private float laneOffset;
        private float laneHeightOffset;
        private float phaseOffset;
        private double elapsed;
        public IReadOnlyList<Transform> Vehicles => vehicles;
        public bool AutoAdvance { get; set; } = true;
        public double ElapsedSeconds => elapsed;
        public float RouteStartDistance { get; private set; }
        public float RouteEndDistance { get; private set; }
        public double VisibleWitnessSeconds { get; private set; }

        internal void Initialize(Vector3 roadOrigin, int seed)
        {
            TextAsset asset = Resources.Load<TextAsset>(CityEastDistanceWorldBuilder.ResourcePath);
            Manifest manifest = asset == null ? null : JsonUtility.FromJson<Manifest>(asset.text);
            if (manifest == null || manifest.panorama_version != 2 ||
                manifest.road_route_points == null || manifest.road_route_points.Length < 2 ||
                manifest.traffic == null || manifest.traffic.max_actors != 2)
                throw new InvalidOperationException("Missing measured eastern valley traffic route.");
            route = manifest.road_route_points;
            float previous = -1f;
            foreach (RoadPoint point in route)
            {
                if (point == null || !Finite(point.x) || !Finite(point.y) || !Finite(point.z) ||
                    !Finite(point.distance) || point.distance <= previous)
                    throw new InvalidOperationException("Eastern traffic route must contain finite ordered metre samples.");
                previous = point.distance;
            }
            TrafficSettings settings = manifest.traffic;
            if (!Finite(settings.start_distance) || !Finite(settings.end_distance) ||
                settings.start_distance < route[0].distance || settings.end_distance > previous ||
                settings.end_distance - settings.start_distance < 100f ||
                !Finite(settings.lane_offset) || settings.lane_offset <= 0f ||
                !Finite(settings.body_width) || settings.body_width <= 0f ||
                !Finite(settings.lane_height_offset) ||
                !Finite(settings.visible_witness_distance) ||
                settings.visible_witness_distance <= settings.start_distance ||
                settings.visible_witness_distance >= settings.end_distance ||
                settings.lane_offset + settings.body_width * .5f > 3f)
                throw new InvalidOperationException("Eastern traffic must stay on the authored two-lane road.");
            origin = roadOrigin;
            laneOffset = settings.lane_offset;
            laneHeightOffset = settings.lane_height_offset;
            RouteStartDistance = settings.start_distance;
            RouteEndDistance = settings.end_distance;
            phaseOffset = (uint)seed % 37;
            double witnessPeriod = (RouteEndDistance - RouteStartDistance) / 13.6d + 47d;
            VisibleWitnessSeconds = ((settings.visible_witness_distance - RouteStartDistance) / 13.6d -
                phaseOffset - 12d + witnessPeriod) % witnessPeriod;

            var templates = new List<Transform>();
            foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name.StartsWith("DistanceTraffic", StringComparison.Ordinal))
                    templates.Add(renderer.transform);
            if (templates.Count != 4)
                throw new InvalidOperationException("The valley needs its four authored car surfaces.");
            vehicles = new Transform[2];
            for (int i = 0; i < vehicles.Length; i++)
            {
                Transform actor = new GameObject("Distant Road Car " + i).transform;
                // The imported panorama root carries FBX's factor 100. Keep
                // the actor basis at real world metres, and preserve each
                // template's measured mesh scale instead of reducing it 100x.
                actor.SetParent(transform, true);
                actor.SetPositionAndRotation(origin, Quaternion.identity);
                vehicles[i] = actor;
                foreach (Transform template in templates)
                {
                    Transform part = Instantiate(template.gameObject).transform;
                    part.name = template.name;
                    part.SetPositionAndRotation(origin, template.rotation);
                    part.localScale = template.lossyScale;
                    part.SetParent(actor, true);
                    part.gameObject.SetActive(true);
                }
            }
            foreach (Transform template in templates) template.gameObject.SetActive(false);
            // Re-entry uses the session's absolute phase, never a new arrival
            // convoy. Motion itself follows the pause-aware presentation clock.
            SampleAt((GameSessionState.GameDayIndex * 1440d +
                GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void Update()
        {
            if (AutoAdvance) Advance(Time.deltaTime);
        }

        public void Advance(float seconds)
        {
            if (!isActiveAndEnabled || !GameSessionState.IsGameTimeRunning ||
                GameTimeScaleRuntime.IsPaused || !Finite(seconds) || seconds <= 0f) return;
            SampleAt(elapsed + seconds);
        }

        /// <summary>Explicit deterministic pose sampler used by the focused capture.</summary>
        public void SampleAt(double seconds)
        {
            if (route == null || double.IsNaN(seconds) || double.IsInfinity(seconds)) return;
            elapsed = seconds;
            float length = RouteEndDistance - RouteStartDistance;
            for (int i = 0; i < vehicles.Length; i++)
            {
                float speed = i == 0 ? 13.6f : 11.3f;
                double duration = length / speed;
                double period = duration + (i == 0 ? 47d : 79d);
                double time = (seconds + phaseOffset + (i == 0 ? 12d : 71d)) % period;
                if (time < 0d) time += period;
                bool visible = time < duration;
                // Place even hidden cars, so resuming a pass cannot expose a
                // frame at the template origin. Endpoints lie in the distance.
                float along = Mathf.Min((float)time * speed, length);
                float distance = i == 0 ? RouteStartDistance + along : RouteEndDistance - along;
                SampleRoad(distance, out Vector3 position, out Vector3 tangent);
                Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
                Vector3 direction = i == 0 ? tangent : -tangent;
                position += right * (i == 0 ? laneOffset : -laneOffset);
                vehicles[i].SetPositionAndRotation(origin + position + Vector3.up * (laneHeightOffset + .003f),
                    Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(0f, -90f, 0f));
                vehicles[i].gameObject.SetActive(visible);
            }
        }

        private void SampleRoad(float distance, out Vector3 position, out Vector3 tangent)
        {
            int low = 0, high = route.Length - 1;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (route[middle].distance <= distance) low = middle;
                else high = middle;
            }
            RoadPoint a = route[low], b = route[high];
            float t = Mathf.InverseLerp(a.distance, b.distance, distance);
            position = Vector3.Lerp(a.Position, b.Position, t);
            tangent = (b.Position - a.Position).normalized;
        }
    }
}
