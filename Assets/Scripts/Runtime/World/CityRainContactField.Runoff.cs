using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal sealed partial class CityRainContactField
    {
        private const int MaximumNearbyRunoffs = 12;
        private const int JetSegments = 10;
        private const int PoolSides = 12;
        private const float PoolUvOffset = 4f;
        private const int RunoffQuadBudget = MaximumNearbyRunoffs * (PoolSides * 2);
        private const float RunoffRange = 18f;
        private const float LaunchSpeed = 1.28f;
        private const float Gravity = 9.81f;
        private const int StaticContactMask = Physics.DefaultRaycastLayers &
            ~(1 << CityPedestrianCollision.LayerIndex | 1 << CityBusCollision.LayerIndex);
        private static readonly Color32 StreamTint = new(150, 175, 180, 198);
        private static readonly Color32 FilmTint = new(51, 61, 61, 138);
        private static readonly Color32 PoolTint = new(65, 80, 82, 105);
        private static readonly Vector2[] PoolDirections = CreatePoolDirections();

        private sealed class Runoff
        {
            internal CityRainRunoffSite Site;
            internal Vector3 Position, Direction;
            internal bool Resolved;
            internal Vector3 Impact, ImpactNormal;
            internal float Flight;
            internal FlowPath GroundFlow;
            internal Vector3[] PoolRings;
            internal Vector3 PoolCentre;
        }

        private readonly struct FlowPath
        {
            internal readonly Vector3 Start, Middle, End;

            internal FlowPath(Vector3 start, Vector3 middle, Vector3 end)
            {
                Start = start;
                Middle = middle;
                End = end;
            }

            internal bool Exists => (Start - End).sqrMagnitude > .001f;
            internal Vector3 Point(float fraction) => fraction < .5f
                ? Vector3.Lerp(Start, Middle, fraction * 2f)
                : Vector3.Lerp(Middle, End, fraction * 2f - 1f);
        }

        private readonly Dictionary<Vector2Int, List<Runoff>> runoffCells = new();
        private readonly Runoff[] nearbyRunoffs = new Runoff[MaximumNearbyRunoffs];
        private readonly float[] runoffDistances = new float[MaximumNearbyRunoffs];
        private int nearbyCount;
        private float runoffTime, deliveredRain = -1f, nextRunoffSearch;

        internal float RunoffStrength => deliveredRain;
        internal int VisibleRunoffQuadCount { get; private set; }
        internal int VisibleInletQuadCount { get; private set; }

        internal static Vector3 RunoffPosition(Vector3 mouth, Vector3 axis, float seconds) =>
            mouth + axis * (LaunchSpeed * seconds) + Vector3.down * (.5f * Gravity * seconds * seconds);

        internal bool TryGetRunoffImpact(CityRainRunoffSite site, out Vector3 point)
        {
            for (int index = 0; index < nearbyCount; index++)
            {
                Runoff runoff = nearbyRunoffs[index];
                if (runoff.Site != site || runoff.ImpactNormal == Vector3.zero) continue;
                point = runoff.Impact;
                return true;
            }
            point = default;
            return false;
        }

        private void RebuildGeometry()
        {
            surfaces.Rebuild(world, additionalGeometry);
            runoffCells.Clear();
            AddRunoffSites(world);
            if (additionalGeometry != null && !additionalGeometry.IsChildOf(world))
                AddRunoffSites(additionalGeometry);
            nextRunoffSearch = 0f;
            nearbyCount = 0;
            Array.Clear(nearbyRunoffs, 0, nearbyRunoffs.Length);
            dirty = false;
        }

        private void AddRunoffSites(Transform root)
        {
            if (root == null) return;
            foreach (var site in root.GetComponentsInChildren<CityRainRunoffSite>(true))
            {
                Vector3 position = site.Position;
                Vector2Int key = RunoffCell(position);
                if (!runoffCells.TryGetValue(key, out var bucket))
                {
                    bucket = new List<Runoff>();
                    runoffCells.Add(key, bucket);
                }
                bucket.Add(new Runoff { Site = site, Position = position, Direction = site.Direction });
            }
        }

        private static Vector2Int RunoffCell(Vector3 at) =>
            new(Mathf.FloorToInt(at.x / RunoffRange), Mathf.FloorToInt(at.z / RunoffRange));

        private void ResetRunoff()
        {
            nearbyCount = VisibleRunoffQuadCount = VisibleInletQuadCount = 0;
            Array.Clear(nearbyRunoffs, 0, nearbyRunoffs.Length);
            nextRunoffSearch = 0f;
        }

        private void AdvanceRunoff(Vector3 focus, float deltaTime, float intensity)
        {
            if (runoffCells.Count == 0) return;
            if (deliveredRain < 0f) deliveredRain = intensity;
            // A roof keeps delivering the rain already in its gutter as the shower passes.
            float response = intensity >= deliveredRain ? 1.25f : .40f;
            deliveredRain = Mathf.Lerp(deliveredRain, intensity, 1f - Mathf.Exp(-response * deltaTime));
            runoffTime = Mathf.Repeat(runoffTime + deltaTime, 600f);
            nextRunoffSearch -= deltaTime;
            if (nextRunoffSearch <= 0f)
            {
                FindNearbyRunoff(focus);
                nextRunoffSearch = .13f;
            }

            // Cache fixed contacts, one site per frame, even when the hero moves into a new street.
            for (int index = 0; index < nearbyCount; index++)
            {
                Runoff runoff = nearbyRunoffs[index];
                if (runoff.Resolved) continue;
                runoff.Resolved = true;
                if (runoff.Site.IsInlet) ResolvePool(runoff);
                else ResolveOutlet(runoff);
                break;
            }
        }

        private void FindNearbyRunoff(Vector3 focus)
        {
            nearbyCount = 0;
            Vector2Int cell = RunoffCell(focus);
            for (int z = -1; z <= 1; z++)
            for (int x = -1; x <= 1; x++)
            {
                if (!runoffCells.TryGetValue(cell + new Vector2Int(x, z), out var bucket)) continue;
                foreach (Runoff runoff in bucket)
                {
                    if (runoff.Site == null || !runoff.Site.isActiveAndEnabled) continue;
                    float distance = (runoff.Position - focus).sqrMagnitude;
                    if (distance >= RunoffRange * RunoffRange) continue;
                    int slot = nearbyCount;
                    while (slot > 0 && distance < runoffDistances[slot - 1]) slot--;
                    if (slot >= nearbyRunoffs.Length) continue;
                    nearbyCount = Mathf.Min(nearbyCount + 1, nearbyRunoffs.Length);
                    for (int i = nearbyCount - 1; i > slot; i--)
                    {
                        nearbyRunoffs[i] = nearbyRunoffs[i - 1];
                        runoffDistances[i] = runoffDistances[i - 1];
                    }
                    nearbyRunoffs[slot] = runoff;
                    runoffDistances[slot] = distance;
                }
            }
        }

        private void ResolveOutlet(Runoff runoff)
        {
            Vector3 start = runoff.Position + runoff.Direction * .006f;
            for (int segment = 1; segment <= 14; segment++)
            {
                const float step = .10f;
                float time = segment * step;
                Vector3 end = RunoffPosition(runoff.Position, runoff.Direction, time);
                Vector3 delta = end - start;
                float length = delta.magnitude;
                if (TraceRunoffGround(new Ray(start, delta / length), length, out Vector3 impact, out Vector3 normal))
                {
                    runoff.Impact = impact;
                    runoff.ImpactNormal = normal;
                    runoff.Flight = time - step + step * Vector3.Distance(start, impact) / length;
                    Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, normal);
                    Vector3 along = downhill.sqrMagnitude > .0004f ? downhill.normalized :
                        Vector3.ProjectOnPlane(runoff.Direction, normal).normalized;
                    Vector3 landing = impact + normal * .006f;
                    if (normal.y > .45f && along.sqrMagnitude > .5f &&
                        SampleFilm(landing + along * .24f, out Vector3 mid) &&
                        SampleFilm(landing + along * .55f, out Vector3 tip) &&
                        mid.y <= landing.y + .015f && tip.y <= mid.y + .015f)
                        runoff.GroundFlow = new FlowPath(landing, mid, tip);
                    return;
                }
                start = end;
            }
            runoff.Flight = 1.4f;
        }

        private void ResolvePool(Runoff runoff)
        {
            var support = runoff.Site.StreetSupport;
            if (!SampleInletGround(support, runoff.Position, out Vector3 centre)) return;
            runoff.PoolCentre = centre;
            runoff.PoolRings = new Vector3[PoolSides * 2];
            Vector3 along = Vector3.Cross(Vector3.up, runoff.Direction);
            float longRadius = runoff.Site.Width + (runoff.Site.Width > .6f ? .24f : .79f);
            float shortRadius = runoff.Site.Depth + .56f;
            for (int i = 0; i < PoolSides; i++)
            {
                Vector2 unit = PoolDirections[i];
                Vector3 offset = along * (longRadius * unit.x) + runoff.Direction * (shortRadius * unit.y);
                // Clip the pool to the wearing course at the raised curb, step or apron edge.
                // Reduce the outer radius on an unsupported ray; the shader softens its rim.
                Vector3 rim = centre;
                float reach = 1f;
                for (int attempt = 0; attempt < 4; attempt++, reach *= .70f)
                {
                    if (!SampleInletGround(support, centre + offset * reach, out Vector3 candidate) ||
                        Mathf.Abs(candidate.y - centre.y) > .105f) continue;
                    rim = candidate;
                    break;
                }
                Vector3 middle = Vector3.Lerp(centre, rim, .53f);
                if (SampleInletGround(support, middle, out Vector3 sampled) &&
                    Mathf.Abs(sampled.y - middle.y) < .048f) middle = sampled;
                runoff.PoolRings[i] = middle;
                runoff.PoolRings[PoolSides + i] = rim;
            }
        }

        private static Vector2[] CreatePoolDirections()
        {
            var directions = new Vector2[PoolSides];
            for (int index = 0; index < directions.Length; index++)
            {
                float angle = index * (Mathf.PI * 2f / PoolSides);
                directions[index] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            return directions;
        }

        private bool SampleInletGround(CityStreetSupportPlan support, Vector3 at, out Vector3 point)
        {
            if (support == null) return SampleFilm(at, out point);
            if (support.TrySampleTop(new Vector2(at.x, at.z), out float y, out var kind) &&
                kind == FootstepGroundKind.Concrete)
            {
                point = new Vector3(at.x, y + .009f, at.z);
                return true;
            }
            point = default;
            return false;
        }

        private bool SampleFilm(Vector3 at, out Vector3 point)
        {
            if (TraceRunoffGround(new Ray(at + Vector3.up * .28f, Vector3.down), .55f,
                out Vector3 contact, out Vector3 normal) && normal.y > .52f)
            {
                point = contact + normal * .006f;
                return true;
            }
            point = default;
            return false;
        }

        private bool TraceRunoffGround(Ray ray, float length, out Vector3 point, out Vector3 normal)
        {
            if (surfaces.TraceStatic(ray, length, out var visible) && visible.Kind != CityRainSurfaceKind.Cover)
            {
                point = visible.Point;
                normal = visible.Normal;
                return true;
            }
            // Merged ground can use a GPU-only render mesh while its support
            // is still available to physics. Do not stop a jet on an AABB.
            if (Physics.Raycast(ray, out var ground, length, StaticContactMask, QueryTriggerInteraction.Ignore) &&
                ground.collider is not CharacterController && ground.rigidbody == null)
            {
                point = ground.point;
                normal = ground.normal;
                return true;
            }
            point = normal = Vector3.zero;
            return false;
        }

        private void DrawRunoff(Vector3 eye)
        {
            int start = quads;
            VisibleInletQuadCount = 0;
            for (int index = 0; index < nearbyCount; index++)
            {
                Runoff runoff = nearbyRunoffs[index];
                if (runoff.Site == null || !runoff.Site.isActiveAndEnabled || !runoff.Resolved) continue;
                float fade = 1f - Mathf.InverseLerp(10f, RunoffRange, Vector3.Distance(eye, runoff.Position));
                if (fade <= 0f) continue;
                if (runoff.Site.IsInlet)
                {
                    int before = quads;
                    DrawPool(runoff, fade);
                    VisibleInletQuadCount += quads - before;
                }
                else DrawOutlet(runoff, eye, fade);
            }
            VisibleRunoffQuadCount = quads - start;
        }

        private void DrawOutlet(Runoff runoff, Vector3 eye, float fade)
        {
            float phase = runoffTime + runoff.Position.x * .73f + runoff.Position.z * .51f;
            float width = runoff.Site.Width * Mathf.Lerp(.15f, .57f, deliveredRain);
            float flight = runoff.Flight;
            if (flight < .012f) return;
            Vector3 start = runoff.Position;
            Vector3 startSide = JetSide(start, 0f) * width;
            float cohesion = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.2f, .75f, deliveredRain));
            float strength = fade * Mathf.Lerp(.40f, .90f, deliveredRain);
            float startAlpha = strength * Pulse(0f);
            for (int i = 0; i < JetSegments; i++)
            {
                float fraction = (i + 1f) / JetSegments;
                float time = fraction * flight;
                Vector3 end = i == JetSegments - 1 && runoff.ImpactNormal != Vector3.zero
                    ? runoff.Impact : RunoffPosition(runoff.Position, runoff.Direction, time);
                Vector3 endSide = JetSide(end, time) * (width * Mathf.Lerp(1f, .56f, fraction));
                float endAlpha = strength * Pulse(time);
                AddQuad(start - startSide, start + startSide, end + endSide, end - endSide,
                    StreamTint, startAlpha, 0f, endAlpha);
                start = end;
                startSide = endSide;
                startAlpha = endAlpha;
            }
            if (runoff.ImpactNormal == Vector3.zero) return;
            DrawGroundFlow(runoff.GroundFlow, width * 3f, fade);
            Vector3 point = runoff.Impact;
            Vector3 normal = runoff.ImpactNormal;
            Vector3 across = Vector3.Cross(normal, runoff.Direction).normalized;
            if (across.sqrMagnitude < .1f) across = view.transform.right;
            for (int i = 0; i < 3; i++)
            {
                float age = Mathf.Repeat(phase * Mathf.Lerp(2.4f, 5.1f, deliveredRain) + i / 3f, 1f);
                Vector3 outward = Vector3.ProjectOnPlane(runoff.Direction, normal) + across * ((i - 1) * .72f);
                Vector3 chip = point + outward * (age * .18f) + normal * (.34f * age * (1f - age));
                Vector3 motion = (outward + normal * (1f - 2f * age)).normalized;
                Streak(chip, motion, .022f + width, .005f + .12f * width, eye,
                    StoneTint, fade * deliveredRain * (1f - age));
            }

            Vector3 JetSide(Vector3 point, float time)
            {
                Vector3 tangent = runoff.Direction * LaunchSpeed + Vector3.down * (Gravity * time);
                Vector3 side = Vector3.Cross(tangent, eye - point).normalized;
                return side.sqrMagnitude < .1f ? view.transform.right : side;
            }

            float Pulse(float time)
            {
                float roll = .5f + .5f * Mathf.Sin((phase - time) * 45f);
                return Mathf.Lerp(roll * roll, .73f + .27f * roll, cohesion);
            }
        }

        private void DrawGroundFlow(FlowPath path, float width, float fade)
        {
            if (!path.Exists) return;
            Vector3 side = Vector3.Cross(Vector3.up, path.End - path.Start).normalized * width;
            float strength = fade * Mathf.Lerp(.36f, .85f, deliveredRain);
            AddQuad(path.Start - side * .4f, path.Start + side * .4f,
                path.Middle + side, path.Middle - side, FilmTint, strength, 0f, strength * .66f);
            AddQuad(path.Middle - side, path.Middle + side,
                path.End + side * .5f, path.End - side * .5f, FilmTint, strength * .66f, 0f, 0f);
            float t = Mathf.Repeat(runoffTime * Mathf.Lerp(.48f, .95f, deliveredRain) + path.End.x * .17f, 1f);
            Vector3 a = path.Point(Mathf.Max(0f, t - .12f));
            Vector3 b = path.Point(t);
            Quad((a + b) * .5f + Vector3.up * .002f, side * .19f, (b - a) * .5f, StreamTint, strength * .3f);
        }

        private void DrawPool(Runoff runoff, float fade)
        {
            Vector3[] rings = runoff.PoolRings;
            if (rings == null) return;
            Vector2 originUv = Vector2.up * PoolUvOffset;
            float alpha = fade * Mathf.Lerp(.38f, .86f, deliveredRain);
            for (int i = 0; i < PoolSides; i++)
            {
                int next = (i + 1) % PoolSides;
                Vector2 from = PoolDirections[i], to = PoolDirections[next];
                Vector2 midFrom = originUv + from * .53f, midTo = originUv + to * .53f;
                AddQuad(runoff.PoolCentre, runoff.PoolCentre, rings[next], rings[i], PoolTint,
                    alpha, alpha, originUv, originUv, midTo, midFrom);
                AddQuad(rings[i], rings[next], rings[PoolSides + next], rings[PoolSides + i], PoolTint,
                    alpha, alpha, midFrom, midTo, originUv + to, originUv + from);
            }
        }

        private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 color, float alpha,
            float endUv, float endAlpha = -1f) =>
            AddQuad(a, b, c, d, color, alpha, endAlpha < 0f ? alpha : endAlpha,
                new Vector2(-1f, -endUv), new Vector2(1f, -endUv), new Vector2(1f, endUv), new Vector2(-1f, endUv));

        private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 color,
            float alpha, float endAlpha, Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
        {
            if (quads == MaximumQuads) return;
            Color32 end = color;
            color.a = (byte)Mathf.Clamp(Mathf.RoundToInt(color.a * alpha), 0, 255);
            end.a = (byte)Mathf.Clamp(Mathf.RoundToInt(end.a * endAlpha), 0, 255);
            if (color.a == 0 && end.a == 0) return;
            int index = quads++ * 4;
            vertices[index] = new Vertex { Position = a, Color = color, UV = uvA };
            vertices[index + 1] = new Vertex { Position = b, Color = color, UV = uvB };
            vertices[index + 2] = new Vertex { Position = c, Color = end, UV = uvC };
            vertices[index + 3] = new Vertex { Position = d, Color = end, UV = uvD };
        }
    }
}
