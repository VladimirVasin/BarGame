using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    // A step is sampled from the same two half-cycles that drive the street shoes.
    // Only feet within a few metres of a nearby physical shelter need a history.
    [DefaultExecutionOrder(350)]
    internal sealed class CityShelterFootprints : MonoBehaviour
    {
        private const int Capacity = 24;
        private const float Lifetime = 32f;
        private struct ShoeTrail
        {
            internal CityPedestrianPresentation Body;
            internal Vector3 LastPosition;
            internal int HalfCycle, Encounter, StepsLeft;
            internal float Water;
            internal bool Seated, Moving;
        }
        private struct Mark
        {
            internal MeshRenderer Renderer;
            internal float Water, Remaining;
        }
        private readonly List<CityBusStopDescriptor> stops = new();
        private readonly List<CityBusStopDescriptor> nearby = new();
        private readonly Mark[] marks = new Mark[Capacity];
        private MaterialPropertyBlock ink;
        private static readonly int Opacity = Shader.PropertyToID("_Opacity");
        private static readonly int Mirror = Shader.PropertyToID("_Mirror");
        private Material tread;
        private Mesh projector;
        private CityGameRoot city;
        private Func<Vector3, bool> districtReady;
        private ShoeTrail[] trails;
        private int nextMark;
        private float pending;
        private bool tracking;

        internal static void Create(CityGameRoot city, Func<Vector3, bool> districtReady)
        {
            var host = new GameObject("Shelter wet footprints");
            host.transform.SetParent(city.transform, false);
            var field = host.AddComponent<CityShelterFootprints>();
            field.ink = new MaterialPropertyBlock();
            field.city = city;
            field.districtReady = districtReady;
            field.trails = new ShoeTrail[city.Pedestrians.Count + CityAmbientPedestrians.MaximumActive];
            field.stops.AddRange(CityBusNetworkFurniture.Stops(city.BusPlans));
        }

        private void LateUpdate() => Advance(Time.deltaTime);

        internal void Advance(float elapsed)
        {
            if (!city || !city.IsInitialized || city.IsDormant || elapsed <= 0f) return;
            pending += elapsed;
            if (pending < .12f) return;
            float dt = pending;
            pending = 0f;
            AdvanceMarks(dt);
            var hero = city.Player.GameObject.transform.position;
            nearby.Clear();
            foreach (var stop in stops)
                if ((stop.ShelterPosition - hero).sqrMagnitude < 27f * 27f && districtReady(stop.ShelterPosition))
                    nearby.Add(stop);
            if (nearby.Count == 0)
            {
                if (tracking) { Array.Clear(trails, 0, trails.Length); tracking = false; }
                return;
            }
            tracking = true;
            var residents = city.Pedestrians.Actors;
            float wetness = CityWetSurfaceRegistry.CurrentWetness;
            for (int i = 0; i < residents.Count; ++i)
                Sample(ref trails[i], residents[i].Presentation, 0, wetness,
                    residents[i].MotionState == CityPedestrianMotionState.SittingOnBench);
            for (int i = 0; i < CityAmbientPedestrians.MaximumActive; ++i)
            {
                var ambient = city.AmbientPedestrians;
                ambient.TryGetWalker(i, out _, out _, out int encounter);
                Sample(ref trails[residents.Count + i], ambient.GetWalkerBody(i), encounter, wetness);
            }
        }

        private void Sample(ref ShoeTrail trail, CityPedestrianPresentation body, int encounter, float wetness, bool seatedAtBench = false)
        {
            if (!body || !body.IsInitialized || body.IsInBus && !seatedAtBench || !body.isActiveAndEnabled)
            { trail = default; return; }
            var feet = body.transform.position;
            bool close = false;
            foreach (var stop in nearby)
                if ((feet - stop.ShelterPosition).sqrMagnitude < 8f * 8f) { close = true; break; }
            if (!close) { trail = default; return; }
            int half = Mathf.FloorToInt(body.WalkCycle * 2f);
            if (trail.Body != body || trail.Encounter != encounter || (feet - trail.LastPosition).sqrMagnitude > 1f)
            {
                trail = new ShoeTrail { Body = body, Encounter = encounter, LastPosition = feet, HalfCycle = half };
                return;
            }
            trail.LastPosition = feet;
            if (seatedAtBench && !trail.Seated)
            {
                // As the walker settles, both soles plant just ahead of the timber.
                trail.Seated = true;
                trail.Moving = false;
                Plant(ref trail, body, true, wetness);
                Plant(ref trail, body, false, wetness);
                return;
            }
            trail.Seated = seatedAtBench;
            bool cameToRest = trail.Moving && !body.IsMoving;
            trail.Moving = body.IsMoving;
            if (cameToRest)
            {
                Plant(ref trail, body, true, wetness);
                Plant(ref trail, body, false, wetness);
                return;
            }
            if (!body.IsMoving || trail.HalfCycle == half) return;
            trail.HalfCycle = half;
            Plant(ref trail, body, (half & 1) == 0, wetness);
        }

        private void Plant(ref ShoeTrail trail, CityPedestrianPresentation body, bool left, float wetness)
        {
            var ankle = left ? body.Registry.LeftFoot : body.Registry.RightFoot;
            if (ankle == null) return;
            Vector3 sole = ankle.position + body.transform.forward * .05f;
            bool covered = false;
            foreach (var stop in nearby)
                if (UnderRoof(stop, sole)) { covered = true; break; }
            if (!covered && wetness < .05f && trail.Water < .05f) return;
            if (!Physics.Raycast(sole + Vector3.up * .14f, Vector3.down, out var hit, .42f,
                    FootProbeSurface.ProbeMask & CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore) ||
                hit.normal.y < .85f) return;
            float intensity = StepWater(ref trail, covered, wetness);
            if (intensity > 0f) Stamp(hit.point, hit.normal, body.transform.forward, left, intensity);
        }

        internal static bool UnderRoof(CityBusStopDescriptor stop, Vector3 point)
        {
            Vector3 delta = point - stop.ShelterPosition;
            Vector3 road = Vector3.ProjectOnPlane(stop.RoadsideForward, Vector3.up).normalized;
            Vector3 across = Vector3.Cross(Vector3.up, road);
            float x = Vector3.Dot(delta, across) - CityBusStopWorldBuilder.ShelterOffsetAlongLane;
            return Mathf.Abs(delta.y) < .6f && Mathf.Abs(x) < CityBusStopWorldBuilder.RoofWidth * .5f &&
                Mathf.Abs(Vector3.Dot(delta, road)) < CityBusStopWorldBuilder.RoofDepth * .5f;
        }

        private static float StepWater(ref ShoeTrail trail, bool covered, float pavementWetness)
        {
            if (!covered)
            {
                trail.Water = pavementWetness;
                trail.StepsLeft = 3;
                return 0f;
            }
            if (trail.Water < .05f || trail.StepsLeft <= 0) return 0f;
            float print = trail.Water;
            trail.StepsLeft--;
            trail.Water *= .60f;
            return print;
        }

        private void Stamp(Vector3 point, Vector3 normal, Vector3 facing, bool left, float water)
        {
            ref Mark mark = ref marks[nextMark];
            nextMark = (nextMark + 1) % Capacity;
            if (!mark.Renderer)
            {
                if (!tread) tread = new Material(Resources.Load<Shader>("Shaders/CityWetFootprint"));
                if (!projector) projector = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                var host = new GameObject("Wet shoe print");
                host.transform.SetParent(transform, false);
                host.AddComponent<MeshFilter>().sharedMesh = projector;
                mark.Renderer = host.AddComponent<MeshRenderer>();
                mark.Renderer.sharedMaterial = tread;
                mark.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                mark.Renderer.receiveShadows = false;
            }
            var surface = mark.Renderer.transform;
            Vector3 forward = Vector3.ProjectOnPlane(facing, normal).normalized;
            // The toe and heel can straddle two different pavement heights. Bound
            // projection to those actual floor contacts so it cannot reach the plank.
            float toe = HeightAt(point + forward * .10f);
            float heel = HeightAt(point - forward * .10f);
            float top = Mathf.Max(0f, Mathf.Max(toe, heel)) + .016f;
            float bottom = Mathf.Min(0f, Mathf.Min(toe, heel)) - .016f;
            surface.SetPositionAndRotation(point + normal * ((top + bottom) * .5f),
                Quaternion.LookRotation(-normal, forward));
            surface.localScale = new Vector3(.107f, .255f, top - bottom);
            mark.Remaining = Lifetime;
            mark.Water = water;
            mark.Renderer.enabled = true;
            ink.SetFloat(Mirror, left ? -1f : 1f);
            ink.SetFloat(Opacity, .64f * water);
            mark.Renderer.SetPropertyBlock(ink);

            float HeightAt(Vector3 sample)
            {
                return Physics.Raycast(sample + Vector3.up * .20f, Vector3.down, out var hit, .42f,
                    FootProbeSurface.ProbeMask & CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore)
                    ? Mathf.Clamp(Vector3.Dot(hit.point - point, normal), -.16f, .16f) : 0f;
            }
        }

        private void AdvanceMarks(float dt)
        {
            for (int i = 0; i < marks.Length; i++)
            {
                ref Mark mark = ref marks[i];
                if (mark.Remaining <= 0f) continue;
                mark.Remaining -= dt;
                if (mark.Remaining <= 0f || districtReady != null && !districtReady(mark.Renderer.transform.position))
                {
                    mark.Remaining = 0f;
                    mark.Renderer.enabled = false;
                    continue;
                }
                mark.Renderer.GetPropertyBlock(ink);
                ink.SetFloat(Opacity, .64f * mark.Water * Mathf.SmoothStep(0f, 1f, mark.Remaining / Lifetime));
                mark.Renderer.SetPropertyBlock(ink);
            }
        }

        private void OnDisable()
        {
            AdvanceMarks(float.MaxValue);
            if (trails != null) Array.Clear(trails, 0, trails.Length);
            tracking = false;
            pending = 0f;
        }

        private void OnDestroy() { if (tread) Destroy(tread); }
    }
}
