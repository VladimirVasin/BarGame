using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    /// <summary>One small batch of near rain strokes and their surface responses.</summary>
    [DefaultExecutionOrder(410), DisallowMultipleComponent]
    internal sealed class CityRainContactField : MonoBehaviour
    {
        internal const int MaximumDrops = 128;
        internal const int MaximumWaterRings = 20;
        private const int MaximumGrassContacts = 12;
        internal const float DetailRadius = 12f;
        private const int MaximumSpawnsPerFrame = 8;
        private const int MaximumQuads = 3 * MaximumDrops;
        private const float MaximumViewDistance = 26f;
        private static readonly int WaterHitsId = Shader.PropertyToID("_CityRainWaterHits");
        private static readonly int WaterCountId = Shader.PropertyToID("_CityRainWaterHitCount");
        private static readonly int GrassHitsId = Shader.PropertyToID("_CityRainGrassHits");
        private static readonly int GrassCountId = Shader.PropertyToID("_CityRainGrassHitCount");
        private static readonly int ViewId = Shader.PropertyToID("_CityRainView");
        private static readonly List<CityRainContactField> Fields = new List<CityRainContactField>(2);

        private static readonly Color32 FallingTint = new Color32(183, 198, 207, 104);
        private static readonly Color32 StoneTint = new Color32(161, 178, 183, 130);
        private static readonly Color32 MetalTint = new Color32(190, 199, 199, 155);
        private static readonly Color32 TimberTint = new Color32(147, 156, 152, 106);
        private static readonly Color32 LeafTint = new Color32(152, 175, 153, 108);
        private static readonly Color32 DarkTint = new Color32(18, 22, 21, 87);

        private struct Drop
        {
            internal CityRainSurfaceIndex.Contact Hit;
            internal Vector3 OriginalPoint, Direction;
            internal float Elapsed, TravelTime, Speed, Variation, Opacity;
            internal bool HasLanded;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Vertex
        {
            internal Vector3 Position;
            internal Color32 Color;
            internal Vector2 UV;
        }

        private readonly Drop[] drops = new Drop[MaximumDrops];
        private readonly Vertex[] vertices = new Vertex[MaximumQuads * 4];
        private readonly Vector4[] waterHits = new Vector4[MaximumWaterRings];
        private readonly Vector4[] grassHits = new Vector4[MaximumGrassContacts];
        private CityRainSurfaceIndex surfaces;
        private CityRainField rain;
        private Camera view;
        private Transform world, additionalGeometry, hero, truck, car;
        private Collider homeShelter;
        private CityBusDirector[] buses;
        private CityRainDripEdge[] edges;
        private Material material;
        private Mesh mesh;
        private Bounds batchBounds;
        private uint random;
        private float spawnCredit;
        private float nextDrip;
        private int active, quads, waterCount, grassCount;
        private bool dirty = true;
        private bool attemptedBatch;
        private bool hasFocus;
        private Vector3 lastFocus;

        internal int ActiveDropCount => active;
        internal int VisibleQuadCount => quads;
        internal int WaterRingCount => waterCount;

        private void Awake() => surfaces = new CityRainSurfaceIndex();

        internal void Initialize(CityRainField field, Camera camera, Transform geometry,
            int seed, Transform player, CityBusDirector[] transit, Transform movingTruck,
            Transform movingCar, Transform balcony, Collider shelteredInterior)
        {
            rain = field;
            view = camera;
            world = geometry;
            hero = player;
            buses = transit;
            truck = movingTruck;
            car = movingCar;
            additionalGeometry = balcony;
            homeShelter = shelteredInterior;
            random = unchecked((uint)seed) ^ 0x91CB983Bu;
            if (random == 0) random = 1;
            edges = world.GetComponentsInChildren<CityRainDripEdge>(true);
        }

        internal static void InvalidateGeometry(Scene scene)
        {
            foreach (CityRainContactField field in Fields)
                if (field.gameObject.scene == scene) field.dirty = true;
        }

        private void OnEnable()
        {
            Fields.Add(this);
            RenderPipelineManager.beginCameraRendering += BindSurfaceContacts;
            RenderPipelineManager.endCameraRendering += UnbindSurfaceContacts;
        }

        private void OnDisable()
        {
            Fields.Remove(this);
            RenderPipelineManager.beginCameraRendering -= BindSurfaceContacts;
            RenderPipelineManager.endCameraRendering -= UnbindSurfaceContacts;
            Shader.SetGlobalInt(WaterCountId, 0);
            Shader.SetGlobalInt(GrassCountId, 0);
            Clear();
            if (rain != null) rain.SetContactFade(Vector4.zero);
        }

        private void OnDestroy()
        {
            surfaces?.Dispose();
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }

        private void Clear()
        {
            Array.Clear(drops, 0, active);
            active = quads = waterCount = grassCount = 0;
            spawnCredit = 0f;
            nextDrip = 0f;
            hasFocus = false;
        }

        private void LateUpdate()
        {
            if (rain == null || !rain.isActiveAndEnabled || rain.RainRenderer == null ||
                !rain.RainRenderer.enabled || rain.FollowTarget == null || view == null ||
                !view.isActiveAndEnabled || rain.Intensity <= .005f)
            {
                if (active != 0 || hasFocus) Clear();
                if (rain != null) rain.SetContactFade(Vector4.zero);
                return;
            }

            Vector3 focus = rain.FollowTarget.position;
            if (hasFocus && (focus - lastFocus).sqrMagnitude > 144f) Clear();
            lastFocus = focus;
            hasFocus = true;
            if (!attemptedBatch)
            {
                attemptedBatch = true;
                CreateBatch();
            }
            if (mesh == null) return;
            rain.SetContactFade(new Vector4(focus.x, focus.y, focus.z, DetailRadius));
            if (!GameTimeScaleRuntime.IsPaused)
                Advance(focus, Mathf.Min(Time.deltaTime, .075f), rain.Intensity);
            Draw(focus);
        }

        private void Advance(Vector3 focus, float deltaTime, float intensity)
        {
            if (dirty)
            {
                surfaces.Rebuild(world, additionalGeometry);
                dirty = false;
            }

            for (int i = active - 1; i >= 0; i--)
            {
                ref Drop drop = ref drops[i];
                if (!drop.Hit.IsVisible)
                {
                    Remove(i);
                    continue;
                }
                drop.Elapsed += deltaTime;
                if (drop.Elapsed < drop.TravelTime) continue;
                if (!drop.HasLanded)
                {
                    drop.HasLanded = true;
                    if (drop.Hit.Kind == CityRainSurfaceKind.Glass && drop.Hit.Bus != null &&
                        drop.Hit.Bus.RecordRainHit(drop.Hit.Renderer, drop.Hit.Point))
                    {
                        Remove(i);
                        continue;
                    }
                }
                if (drop.Elapsed - drop.TravelTime >= Lifetime(drop.Hit.Kind)) Remove(i);
            }

            spawnCredit = Mathf.Min(MaximumSpawnsPerFrame, spawnCredit + Mathf.Lerp(12f, 178f, intensity) * deltaTime);
            nextDrip -= deltaTime;
            if (spawnCredit < 1f && nextDrip > 0f) return;
            PrepareMovingSurfaces(focus);
            while (spawnCredit >= 1f)
            {
                spawnCredit -= 1f;
                SpawnRain(focus, intensity);
            }
            if (nextDrip <= 0f)
            {
                nextDrip = Mathf.Lerp(1.9f, .26f, intensity) * Mathf.Lerp(.7f, 1.3f, Unit());
                SpawnDrip(intensity);
            }
        }

        private void PrepareMovingSurfaces(Vector3 focus)
        {
            surfaces.BeginFrame();
            surfaces.AddMoving(hero, focus, 4f);
            if (buses != null)
                foreach (CityBusDirector director in buses)
                {
                    CityBusPresentation bus = director != null ? director.Actor?.Presentation : null;
                    if (bus != null) surfaces.AddMoving(bus.transform, focus, 12f, bus);
                }
            surfaces.AddMoving(truck, focus, 15f);
            surfaces.AddMoving(car, focus, 7f);
            IReadOnlyList<Transform> bodies = NpcFootstepSources.Prune();
            for (int i = 0; i < bodies.Count; i++)
            {
                Transform body = bodies[i];
                if (body.gameObject.scene == gameObject.scene) surfaces.AddMoving(body, focus, 4f);
            }
        }

        private void SpawnRain(Vector3 focus, float intensity)
        {
            Vector2 wind = rain.AppliedWindDrift;
            float speed = Mathf.Lerp(12.5f, 16.5f, Unit());
            float drift = Mathf.Lerp(.55f, 1.05f, Unit());
            Vector3 velocity = new Vector3(wind.x * drift, -speed, wind.y * drift);
            Vector3 direction = velocity.normalized;
            float angle = Unit() * Mathf.PI * 2f;
            bool foreground = homeShelter == null && Unit() < .52f;
            float radius = Mathf.Sqrt(Unit()) * (foreground ? 5f : DetailRadius);
            Vector3 point = focus + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            if (foreground)
            {
                Vector3 forward = view.transform.forward;
                forward.y = 0f;
                point += forward.normalized * 3.5f;
            }
            var ray = new Ray(point - direction * (55f / -direction.y), direction);
            if (!TryHit(ray, 79f / -direction.y, out var hit)) return;
            Vector3 landing = hit.Point;
            if (!NearView(landing)) return;

            float length = Mathf.Lerp(1.25f, 3.1f, Unit());
            // A ray can graze the edge of a face. The short rendered tail
            // lies entirely on the incoming side of its first contact.
            AddDrop(hit, direction, velocity.magnitude, length, Mathf.Lerp(.8f, 1.1f, intensity));
        }

        private void SpawnDrip(float intensity)
        {
            CityRainDripEdge nearest = null;
            float best = 9f * 9f;
            foreach (CityRainDripEdge edge in edges)
            {
                if (edge == null || !edge.isActiveAndEnabled) continue;
                Vector3 midpoint = edge.Sample(.5f);
                float distance = (midpoint - view.transform.position).sqrMagnitude;
                if (distance >= best || !NearView(midpoint)) continue;
                nearest = edge;
                best = distance;
            }
            if (nearest == null) return;
            Vector3 origin = nearest.Sample(Unit());
            Vector2 wind = rain.AppliedWindDrift * .12f;
            Vector3 direction = new Vector3(wind.x, -6f, wind.y).normalized;
            if (!TryHit(new Ray(origin, direction), 5f, out var hit)) return;
            float length = Vector3.Distance(origin, hit.Point);
            if (length > .23f) AddDrop(hit, direction, 6f, length, Mathf.Lerp(.83f, 1.1f, intensity));
        }

        private bool NearView(Vector3 point)
        {
            Vector3 offset = point - view.transform.position;
            return offset.sqrMagnitude < MaximumViewDistance * MaximumViewDistance &&
                Vector3.Dot(view.transform.forward, offset) > .15f;
        }

        private bool TryHit(Ray ray, float limit, out CityRainSurfaceIndex.Contact hit)
        {
            // The apartment's own mass is absent from its passive street
            // reconstruction. Its existing shelter still stops outside rain.
            if (homeShelter != null && homeShelter.Raycast(ray, out RaycastHit roof, limit))
                limit = roof.distance;
            return surfaces.Trace(ray, limit, out hit) && hit.Kind != CityRainSurfaceKind.Cover;
        }

        internal bool ProbeSurface(Ray ray, float distance, out CityRainSurfaceIndex.Contact hit)
        {
            if (dirty)
            {
                surfaces.Rebuild(world, additionalGeometry);
                dirty = false;
            }
            PrepareMovingSurfaces(rain.FollowTarget.position);
            return TryHit(ray, distance, out hit);
        }

        private void AddDrop(CityRainSurfaceIndex.Contact hit, Vector3 direction, float speed, float length, float opacity)
        {
            if (active == MaximumDrops) return;
            drops[active++] = new Drop
            {
                Hit = hit, OriginalPoint = hit.Point, Direction = direction, TravelTime = length / speed,
                Speed = speed, Variation = Unit(), Opacity = opacity
            };
        }

        private void Remove(int index)
        {
            drops[index] = drops[--active];
            drops[active] = default;
        }

        private static float Lifetime(CityRainSurfaceKind kind) => kind switch
        {
            CityRainSurfaceKind.Water or CityRainSurfaceKind.Puddle => .64f,
            CityRainSurfaceKind.Soil => .42f,
            CityRainSurfaceKind.Glass => .76f,
            CityRainSurfaceKind.Timber => .29f,
            CityRainSurfaceKind.Fabric or CityRainSurfaceKind.Leaves => .19f,
            _ => .18f
        };

        private float Unit()
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return (random >> 8) * (1f / 16777216f);
        }

        private void Draw(Vector3 focus)
        {
            quads = waterCount = grassCount = 0;
            Vector3 eye = view.transform.position;
            for (int i = 0; i < active; i++)
            {
                ref Drop drop = ref drops[i];
                if (!drop.Hit.IsVisible) continue;
                Vector3 point = drop.Hit.Point;
                float distance = Vector3.Distance(point, eye);
                float fade = (1f - Mathf.InverseLerp(14f, MaximumViewDistance, distance)) * drop.Opacity;
                if (fade <= 0f) continue;
                float age = drop.Elapsed - drop.TravelTime;
                if (age < 0f)
                {
                    float progress = drop.Elapsed / drop.TravelTime;
                    Vector3 tip = Vector3.Lerp(drop.OriginalPoint, point, progress) + drop.Direction * (age * drop.Speed);
                    float tail = Mathf.Min(Mathf.Lerp(.13f, .25f, drop.Variation), drop.Elapsed * drop.Speed);
                    Streak(tip, drop.Direction, tail, Mathf.Lerp(.006f, .014f, drop.Variation), eye, FallingTint, fade);
                    continue;
                }
                DrawHit(drop, point, age / Lifetime(drop.Hit.Kind), eye, fade);
            }

            if (quads == 0) return;
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            mesh.SetVertexBufferData(vertices, 0, 0, quads * 4, 0, flags);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, quads * 6) { vertexCount = quads * 4 }, flags);
            batchBounds = new Bounds(focus, new Vector3(80f, 64f, 80f));
            mesh.bounds = batchBounds;
        }

        private void DrawHit(Drop drop, Vector3 point, float age, Vector3 eye, float fade)
        {
            CityRainSurfaceKind kind = drop.Hit.Kind;
            if (kind == CityRainSurfaceKind.Water || kind == CityRainSurfaceKind.Puddle)
            {
                if (waterCount < MaximumWaterRings)
                    waterHits[waterCount++] = new Vector4(point.x, point.z, point.y,
                        kind == CityRainSurfaceKind.Puddle ? age : 1f + age);
                return;
            }
            if ((kind == CityRainSurfaceKind.Leaves || kind == CityRainSurfaceKind.Soil) &&
                grassCount < MaximumGrassContacts && (point - eye).sqrMagnitude < 121f)
                grassHits[grassCount++] = new Vector4(point.x, point.y, point.z, age);
            Vector3 normal = drop.Hit.Normal;
            Vector3 down = Vector3.ProjectOnPlane(Vector3.down, normal);
            Vector3 right = Vector3.Cross(normal, down).normalized;
            if (right.sqrMagnitude < .1f) right = Vector3.Cross(normal, Vector3.forward).normalized;
            Vector3 tangent = Vector3.Cross(normal, right).normalized;
            point += normal * .003f;
            fade *= 1f - Mathf.Clamp01(age);

            if (kind == CityRainSurfaceKind.Soil || kind == CityRainSurfaceKind.Fabric)
            {
                float size = Mathf.Lerp(.016f, .032f, drop.Variation) * Mathf.Lerp(.75f, 1.25f, age);
                Quad(point, right * size, tangent * (size * .72f), DarkTint, fade);
                return;
            }
            if (kind == CityRainSurfaceKind.Glass)
            {
                float run = Mathf.InverseLerp(.45f, .9f, rain.Intensity);
                float length = .012f + run * age * .033f;
                point += down * (run * age * age * .085f);
                if ((point - drop.Hit.Renderer.bounds.ClosestPoint(point)).sqrMagnitude < .00022f)
                    Quad(point, right * .011f, tangent * length, FallingTint, fade);
                return;
            }
            if (kind == CityRainSurfaceKind.Timber && down.sqrMagnitude > .06f && age > .52f)
            {
                float runoff = Mathf.InverseLerp(.52f, 1f, age);
                point += down * (.08f * runoff * runoff);
                if ((point - drop.Hit.Renderer.bounds.ClosestPoint(point)).sqrMagnitude < .0001f)
                    Quad(point, right * .008f, tangent * .016f, TimberTint, fade);
                return;
            }

            Color32 tint = kind == CityRainSurfaceKind.Metal ? MetalTint : kind == CityRainSurfaceKind.Timber
                ? TimberTint : kind == CityRainSurfaceKind.Leaves ? LeafTint : StoneTint;
            float reach = kind == CityRainSurfaceKind.Metal ? .073f : kind == CityRainSurfaceKind.Leaves ? .038f : .054f;
            int count = kind == CityRainSurfaceKind.Leaves || kind == CityRainSurfaceKind.Timber ? 2 : 3;
            for (int i = 0; i < count; i++)
            {
                float angle = (drop.Variation + i / 3f) * (Mathf.PI * 2f);
                Vector3 across = right * Mathf.Cos(angle) + tangent * Mathf.Sin(angle);
                float lift = 4f * age * (1f - age) * reach * .56f;
                Vector3 chip = point + across * (age * reach) + normal * lift;
                Vector3 motion = (across + normal * (1f - age * 2f)).normalized;
                Streak(chip, motion, .018f + drop.Variation * .016f, .009f, eye, tint, fade);
            }
        }

        private void Streak(Vector3 tip, Vector3 direction, float length, float width, Vector3 eye, Color32 color, float alpha)
        {
            Vector3 side = Vector3.Cross(direction, eye - tip).normalized;
            if (side.sqrMagnitude < .1f) side = view.transform.right;
            Vector3 halfLength = direction * (length * .5f);
            Quad(tip - halfLength, side * width, halfLength, color, alpha);
        }

        private void Quad(Vector3 centre, Vector3 right, Vector3 up, Color32 color, float alpha)
        {
            if (quads == MaximumQuads) return;
            color.a = (byte)Mathf.Clamp(Mathf.RoundToInt(color.a * alpha), 0, 255);
            int index = quads++ * 4;
            vertices[index] = new Vertex { Position = centre - right - up, Color = color, UV = new Vector2(-1f, -1f) };
            vertices[index + 1] = new Vertex { Position = centre + right - up, Color = color, UV = new Vector2(1f, -1f) };
            vertices[index + 2] = new Vertex { Position = centre + right + up, Color = color, UV = new Vector2(1f, 1f) };
            vertices[index + 3] = new Vertex { Position = centre - right + up, Color = color, UV = new Vector2(-1f, 1f) };
        }

        private void CreateBatch()
        {
            Shader shader = Resources.Load<Shader>("Shaders/CityRainContact");
            if (shader == null || !shader.isSupported) return;
            material = new Material(shader) { name = "City rain contacts", hideFlags = HideFlags.HideAndDontSave };
            mesh = new Mesh { name = "City rain contact batch", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(vertices.Length,
                new VertexAttributeDescriptor(VertexAttribute.Position),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
            var indices = new ushort[MaximumQuads * 6];
            for (int i = 0; i < MaximumQuads; i++)
            {
                int start = i * 6;
                int vertex = i * 4;
                indices[start] = (ushort)vertex;
                indices[start + 1] = (ushort)(vertex + 1);
                indices[start + 2] = (ushort)(vertex + 2);
                indices[start + 3] = (ushort)vertex;
                indices[start + 4] = (ushort)(vertex + 2);
                indices[start + 5] = (ushort)(vertex + 3);
            }
            mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt16);
            mesh.SetIndexBufferData(indices, 0, 0, indices.Length);
            mesh.subMeshCount = 1;
        }

        private void BindSurfaceContacts(ScriptableRenderContext context, Camera camera)
        {
            if (camera != view) return;
            // The camera owns submission just as it does for instanced grass.
            // A manual render of this camera gets the same prepared geometry.
            if (quads != 0 && mesh != null)
            {
                var parameters = new RenderParams(material)
                {
                    camera = camera, worldBounds = batchBounds, shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false, lightProbeUsage = LightProbeUsage.Off,
                    motionVectorMode = MotionVectorGenerationMode.ForceNoMotion
                };
                Graphics.RenderMesh(parameters, mesh, 0, Matrix4x4.identity);
            }
            Shader.SetGlobalInt(WaterCountId, waterCount);
            Shader.SetGlobalInt(GrassCountId, grassCount);
            if (waterCount + grassCount == 0) return;
            Shader.SetGlobalVector(ViewId, camera.transform.position);
            if (waterCount != 0) Shader.SetGlobalVectorArray(WaterHitsId, waterHits);
            if (grassCount != 0) Shader.SetGlobalVectorArray(GrassHitsId, grassHits);
        }

        private void UnbindSurfaceContacts(ScriptableRenderContext context, Camera camera)
        {
            if (camera != view) return;
            Shader.SetGlobalInt(WaterCountId, 0);
            Shader.SetGlobalInt(GrassCountId, 0);
        }
    }
}
