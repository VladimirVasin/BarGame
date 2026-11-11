using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal enum CityRainSurfaceKind
    {
        Ignore, Cover, Stone, Soil, Timber, Metal, Glass, Leaves, Fabric, Water, Puddle
    }

    /// <summary>Scene-local first hit against visible surfaces, including passive Home scenery.</summary>
    internal sealed class CityRainSurfaceIndex : IDisposable
    {
        internal const string WaterShaderName = "Bar Promenade/City River Water";
        private const int MaximumCachedFaces = 196608;
        private const int MaximumCachedMeshes = 144;
        private const int MaximumMovingGroups = 24;
        private const float MovingRange = 24f;
        private static readonly int GroundKindId = Shader.PropertyToID("_GroundKind");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");

        internal readonly struct Contact
        {
            internal readonly Renderer Renderer;
            internal readonly Transform Anchor;
            internal readonly CityBusPresentation Bus;
            internal readonly CityRainSurfaceKind Kind;
            internal readonly Vector3 LocalPoint, LocalNormal;
            private readonly int busGeneration;

            internal Contact(Receiver receiver, Vector3 point, Vector3 normal, CityRainSurfaceKind kind)
            {
                Renderer = receiver.Renderer;
                Anchor = receiver.Skin != null && receiver.Skin.rootBone != null
                    ? receiver.Skin.rootBone : Renderer.transform;
                Bus = receiver.Bus;
                busGeneration = Bus != null ? Bus.RainContactGeneration : 0;
                Kind = kind;
                LocalPoint = Anchor.InverseTransformPoint(point);
                LocalNormal = Anchor.localToWorldMatrix.transpose.MultiplyVector(normal).normalized;
            }

            internal Vector3 Point => Anchor.TransformPoint(LocalPoint);
            internal Vector3 Normal => Anchor.worldToLocalMatrix.transpose.MultiplyVector(LocalNormal).normalized;
            internal bool IsVisible => Renderer != null && Renderer.enabled && !Renderer.forceRenderingOff &&
                Renderer.gameObject.activeInHierarchy && (Bus == null || Bus.RainContactGeneration == busGeneration);
        }

        internal sealed class Receiver
        {
            internal Renderer Renderer;
            internal MeshFilter Filter;
            internal MeshCollider Ground;
            internal FootstepGround GroundKind;
            internal SkinnedMeshRenderer Skin;
            internal CityBusPresentation Bus;
            internal WorldWaterSurface Water;
            internal CityRainWaterReceiver WaterShape;
            internal CityRainSurfaceKind[] Kinds;
        }

        private sealed class MovingGroup
        {
            internal Transform Root;
            internal CityRainGeometry<Receiver>.Item[] Receivers;
            internal float Extent;
            internal int LastFrame;
        }

        private sealed class CachedMesh
        {
            internal CityRainMesh Shape;
            internal int LastFrame;
        }

        private struct Probe : CityRainGeometry<Receiver>.IVisitor
        {
            internal CityRainSurfaceIndex Index;
            internal Ray Ray;
            internal Contact Hit;
            internal bool Found;

            public void Visit(Receiver receiver, ref float distance)
            {
                if (!Index.TryReceiver(receiver, Ray, distance, out float hitDistance,
                    out Vector3 normal, out CityRainSurfaceKind kind)) return;
                distance = hitDistance;
                Found = true;
                Hit = new Contact(receiver, Ray.GetPoint(distance), normal, kind);
            }
        }

        private readonly Dictionary<Mesh, CachedMesh> cache = new Dictionary<Mesh, CachedMesh>(MaximumCachedMeshes);
        private readonly HashSet<Mesh> unreadable = new HashSet<Mesh>();
        private readonly List<MovingGroup> moving = new List<MovingGroup>(MaximumMovingGroups);
        private readonly List<MovingGroup> visibleMoving = new List<MovingGroup>(MaximumMovingGroups);
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private readonly List<Vector3> skinVertices = new List<Vector3>(2048);
        private readonly List<int> skinTriangles = new List<int>(6144);
        private CityRainGeometry<Receiver> statics;
        private Mesh posedSkin;
        private int cachedFaces;
        private int buildsLeft, skinBakesLeft;

        internal void Rebuild(Transform world, Transform extra)
        {
            var receivers = new List<CityRainGeometry<Receiver>.Item>(1024);
            Collect(world, false, null, receivers);
            if (extra != null && !extra.IsChildOf(world)) Collect(extra, false, null, receivers);
            statics = new CityRainGeometry<Receiver>(receivers.ToArray());
        }

        internal void BeginFrame()
        {
            buildsLeft = 2;
            skinBakesLeft = 2;
            visibleMoving.Clear();
        }

        internal void AddMoving(Transform root, Vector3 focus, float extent, CityBusPresentation bus = null)
        {
            if (root == null || !root.gameObject.activeInHierarchy) return;
            Vector3 offset = root.position - focus;
            if (offset.x * offset.x + offset.z * offset.z > MovingRange * MovingRange ||
                Mathf.Abs(offset.y) > MovingRange) return;

            int oldest = 0;
            for (int i = 0; i < moving.Count; i++)
            {
                MovingGroup candidate = moving[i];
                if (candidate.Root == root)
                {
                    if (visibleMoving.Contains(candidate)) return;
                    candidate.LastFrame = Time.frameCount;
                    visibleMoving.Add(candidate);
                    return;
                }
                if (candidate.Root == null || candidate.LastFrame < moving[oldest].LastFrame) oldest = i;
            }

            if (visibleMoving.Count == MaximumMovingGroups) return;
            var receivers = new List<CityRainGeometry<Receiver>.Item>(32);
            Collect(root, true, bus, receivers);
            var group = new MovingGroup
            {
                Root = root, Receivers = receivers.ToArray(), Extent = extent, LastFrame = Time.frameCount
            };
            if (moving.Count == MaximumMovingGroups) moving[oldest] = group;
            else moving.Add(group);
            visibleMoving.Add(group);
        }

        private void Collect(Transform root, bool isMoving, CityBusPresentation bus,
            List<CityRainGeometry<Receiver>.Item> result)
        {
            if (root == null) return;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is not MeshRenderer && (!isMoving || renderer is not SkinnedMeshRenderer)) continue;
                var receiver = new Receiver { Renderer = renderer, Bus = bus, Skin = renderer as SkinnedMeshRenderer };
                if (receiver.Skin == null && (!renderer.TryGetComponent(out receiver.Filter) ||
                    receiver.Filter.sharedMesh == null)) continue;
                if (!isMoving && !renderer.enabled) continue;
                Material[] materials = renderer.sharedMaterials;
                receiver.Kinds = new CityRainSurfaceKind[materials.Length];
                bool receives = false;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    receiver.Kinds[slot] = bus != null && bus.TryGetRainSurface(renderer, out var kind)
                        ? kind : Classify(renderer, materials[slot], slot, receiver.Skin != null);
                    receives |= receiver.Kinds[slot] != CityRainSurfaceKind.Ignore;
                }
                if (!receives) continue;
                renderer.TryGetComponent(out receiver.Water);
                renderer.TryGetComponent(out receiver.WaterShape);
                if (renderer.TryGetComponent(out MeshCollider ground) &&
                    receiver.Filter != null && ground.sharedMesh == receiver.Filter.sharedMesh)
                {
                    renderer.TryGetComponent(out receiver.GroundKind);
                    if (receiver.GroundKind != null || materials.Length == 1) receiver.Ground = ground;
                }

                Bounds bounds = renderer.bounds;
                // Doors, swings and deforming crowns have a small published
                // motion envelope. Long-range movers use their live bounds.
                if (!isMoving) bounds.Expand(.7f);
                result.Add(new CityRainGeometry<Receiver>.Item(bounds, receiver));
            }
        }

        internal bool Trace(Ray ray, float limit, out Contact contact)
        {
            var probe = new Probe { Index = this, Ray = ray };
            statics?.Trace(ray, ref limit, ref probe);
            foreach (MovingGroup group in visibleMoving)
            {
                Vector3 centre = group.Root.position + Vector3.up;
                var bounds = new Bounds(centre, Vector3.one * group.Extent);
                if (CityRainGeometry<Receiver>.EntryDistance(bounds, ray) >= limit) continue;
                foreach (var item in group.Receivers)
                    probe.Visit(item.Value, ref limit);
            }
            contact = probe.Hit;
            return probe.Found;
        }

        private bool TryReceiver(Receiver receiver, Ray ray, float limit,
            out float distance, out Vector3 normal, out CityRainSurfaceKind kind)
        {
            distance = 0f;
            normal = Vector3.up;
            kind = CityRainSurfaceKind.Ignore;
            Renderer renderer = receiver.Renderer;
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy ||
                CityRainGeometry<Receiver>.EntryDistance(renderer.bounds, ray) >= limit) return false;

            if (receiver.Water != null)
            {
                kind = CityRainSurfaceKind.Water;
                return receiver.Water.TryRainHit(ray, limit, out distance);
            }
            if (receiver.WaterShape != null)
            {
                var patches = receiver.WaterShape.Puddles;
                if (patches == null)
                {
                    kind = CityRainSurfaceKind.Water;
                    return receiver.WaterShape.TryDisc(ray, limit, out distance);
                }
                kind = CityRainSurfaceKind.Puddle;
                bool found = false;
                for (int i = 0; i < patches.Count; i++)
                {
                    RuntimeOrientedBox patch = patches[i];
                    if (!CityRainWaterReceiver.TryPuddle(patch, ray, limit, out float hit, out Vector3 up)) continue;
                    found = true;
                    limit = distance = hit;
                    normal = up;
                }
                return found;
            }
            if (receiver.Ground != null && receiver.Ground.enabled &&
                receiver.Ground.Raycast(ray, out RaycastHit groundHit, limit))
            {
                distance = groundHit.distance;
                normal = groundHit.normal;
                kind = receiver.GroundKind != null
                    ? ForGround(receiver.GroundKind.ResolveKind(groundHit.triangleIndex)) : receiver.Kinds[0];
                return true;
            }

            Matrix4x4 inverse = renderer.transform.worldToLocalMatrix;
            Vector3 direction = inverse.MultiplyVector(ray.direction);
            float scale = direction.magnitude;
            if (scale < 1e-6f) return false;
            var local = new Ray(inverse.MultiplyPoint3x4(ray.origin), direction / scale);
            float localLimit = limit * scale;
            int slot = 0;
            Mesh mesh = receiver.Skin != null ? receiver.Skin.sharedMesh : receiver.Filter.sharedMesh;
            if (mesh == null) return false;
            if (receiver.Skin != null)
            {
                kind = CityRainSurfaceKind.Fabric;
                if (!TraceSkin(receiver.Skin, local, ref localLimit, out normal)) return false;
            }
            else if (GetMesh(mesh) is CityRainMesh shape)
            {
                if (!shape.Trace(local, receiver.Kinds, ref localLimit, out slot, out normal)) return false;
                kind = receiver.Kinds[Mathf.Min(slot, receiver.Kinds.Length - 1)];
            }
            else
            {
                // Some imported assets discard CPU data. Their visible thin
                // panels can still receive; other hulls only occlude until
                // their precise mesh is available in the bounded cache.
                if (!mesh.bounds.IntersectRay(local, out localLimit) || localLimit >= limit * scale) return false;
                Vector3 point = local.GetPoint(localLimit) - mesh.bounds.center;
                Vector3 extents = mesh.bounds.extents;
                Vector3 face = new Vector3(Mathf.Abs(point.x) / Mathf.Max(.00001f, extents.x),
                    Mathf.Abs(point.y) / Mathf.Max(.00001f, extents.y), Mathf.Abs(point.z) / Mathf.Max(.00001f, extents.z));
                int axis = face.x >= face.y && face.x >= face.z ? 0 : face.y >= face.z ? 1 : 2;
                normal = Vector3.zero;
                normal[axis] = Mathf.Sign(point[axis]);
                float thickness = renderer.transform.TransformVector(normal * mesh.bounds.size[axis]).magnitude;
                kind = !mesh.isReadable && thickness < .12f ? receiver.Kinds[0] : CityRainSurfaceKind.Cover;
            }
            distance = localLimit / scale;
            normal = inverse.transpose.MultiplyVector(normal).normalized;
            return kind != CityRainSurfaceKind.Ignore;
        }

        private CityRainMesh GetMesh(Mesh mesh)
        {
            if (cache.TryGetValue(mesh, out CachedMesh ready))
            {
                ready.LastFrame = Time.frameCount;
                return ready.Shape;
            }
            if (buildsLeft == 0 || unreadable.Contains(mesh)) return null;

            int faces = 0;
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
                faces += CityRainMesh.FacesInSlot(mesh, slot);
            if (faces == 0 || faces > MaximumCachedFaces) return null;
            // GPU-only imported detail gets one bounded readback when it
            // first receives rain. The source stays GPU-only everywhere else.
            if (!mesh.isReadable && (mesh.vertexCount > 24576 || faces > 32768)) return null;
            buildsLeft = !mesh.isReadable || faces > 8192 ? 0 : buildsLeft - 1;
            CityRainMesh shape;
            try { shape = new CityRainMesh(mesh, faces); }
            catch (InvalidOperationException) when (!mesh.isReadable)
            {
                if (unreadable.Count < MaximumCachedMeshes) unreadable.Add(mesh);
                return null;
            }
            while (cachedFaces + faces > MaximumCachedFaces || cache.Count >= MaximumCachedMeshes)
            {
                Mesh oldest = null;
                int lastFrame = int.MaxValue;
                foreach (var entry in cache)
                {
                    if (entry.Value.LastFrame >= lastFrame) continue;
                    oldest = entry.Key;
                    lastFrame = entry.Value.LastFrame;
                }
                cachedFaces -= cache[oldest].Shape.FaceCount;
                cache.Remove(oldest);
            }
            cache.Add(mesh, new CachedMesh { Shape = shape, LastFrame = Time.frameCount });
            cachedFaces += faces;
            return shape;
        }

        private bool TraceSkin(SkinnedMeshRenderer skin, Ray ray, ref float limit, out Vector3 normal)
        {
            normal = default;
            Mesh source = skin.sharedMesh;
            if (skinBakesLeft == 0 || source.vertexCount > 8192) return false;
            skinBakesLeft--;
            if (posedSkin == null) posedSkin = new Mesh { name = "Rain pose scratch", hideFlags = HideFlags.HideAndDontSave };
            skin.BakeMesh(posedSkin, false);
            posedSkin.GetVertices(skinVertices);
            bool found = false;
            for (int slot = 0; slot < posedSkin.subMeshCount; slot++)
            {
                posedSkin.GetTriangles(skinTriangles, slot);
                for (int i = 0; i < skinTriangles.Count; i += 3)
                {
                    if (!CityRainMesh.TryTriangle(ray, skinVertices[skinTriangles[i]], skinVertices[skinTriangles[i + 1]],
                        skinVertices[skinTriangles[i + 2]], limit, out float distance, out Vector3 face)) continue;
                    found = true;
                    limit = distance;
                    normal = face;
                }
            }
            return found;
        }

        private CityRainSurfaceKind Classify(Renderer renderer, Material material, int slot, bool skinned)
        {
            if (material == null || material.shader == null) return CityRainSurfaceKind.Ignore;
            string shader = material.shader.name;
            if (shader == WaterShaderName) return CityRainSurfaceKind.Water;
            if (shader == "Bar Promenade/City Bus Glass Rain" || Has(shader, "TextMeshPro", "Decal", "Particle", "Fountain", "Shadow", "Outline", "Light Volume"))
                return CityRainSurfaceKind.Ignore;
            if (Has(shader, "Glass", "City Building Window Slots") ||
                Has(material.name, "Glass", "Glazing", "WindowPane", "City Window")) return CityRainSurfaceKind.Glass;
            if (material.renderQueue >= 2500) return CityRainSurfaceKind.Ignore;
            if (skinned) return CityRainSurfaceKind.Fabric;

            renderer.GetPropertyBlock(properties, slot);
            if (properties.isEmpty) renderer.GetPropertyBlock(properties);
            if (material.HasProperty(GroundKindId))
            {
                int kind = (int)(properties.HasFloat(GroundKindId) ? properties.GetFloat(GroundKindId) : material.GetFloat(GroundKindId));
                return (GroundSurfaceKind)kind switch
                {
                    GroundSurfaceKind.Timber => CityRainSurfaceKind.Timber,
                    GroundSurfaceKind.Lawn => CityRainSurfaceKind.Leaves,
                    GroundSurfaceKind.Soil or GroundSurfaceKind.Gravel or GroundSurfaceKind.Sand or GroundSurfaceKind.Silt => CityRainSurfaceKind.Soil,
                    _ => CityRainSurfaceKind.Stone
                };
            }

            Texture texture = properties.GetTexture(BaseMapId);
            if (texture == null && material.HasProperty(BaseMapId)) texture = material.GetTexture(BaseMapId);
            string name = texture != null ? texture.name : material.name;
            if (Has(name, "WindowAtlas", "WindowPane")) return CityRainSurfaceKind.Glass;
            if (Has(shader, "Foliage") || Has(name, "Foliage", "Leaves", "Grass", "Hedge", "Reed")) return CityRainSurfaceKind.Leaves;
            if (Has(name, "Timber", "Wood", "Bark", "Plank", "Plywood")) return CityRainSurfaceKind.Timber;
            if (Has(name, "Soil", "Sand", "Gravel", "Dirt", "Silt")) return CityRainSurfaceKind.Soil;
            if (Has(name, "Fabric", "Cloth", "Canvas", "Textile", "Jacket")) return CityRainSurfaceKind.Fabric;
            float metallic = properties.HasFloat(MetallicId) ? properties.GetFloat(MetallicId) :
                material.HasProperty(MetallicId) ? material.GetFloat(MetallicId) : 0f;
            return metallic >= .35f || Has(name, "Metal", "Steel", "Iron", "Zinc", "TinRoof", "BusBody")
                ? CityRainSurfaceKind.Metal : CityRainSurfaceKind.Stone;
        }

        private static CityRainSurfaceKind ForGround(FootstepGroundKind kind) => kind switch
        {
            FootstepGroundKind.Grass => CityRainSurfaceKind.Leaves,
            FootstepGroundKind.Soil or FootstepGroundKind.Sand => CityRainSurfaceKind.Soil,
            FootstepGroundKind.Wood => CityRainSurfaceKind.Timber,
            FootstepGroundKind.Metal => CityRainSurfaceKind.Metal,
            FootstepGroundKind.Carpet => CityRainSurfaceKind.Fabric,
            _ => CityRainSurfaceKind.Stone
        };

        private static bool Has(string value, params string[] terms)
        {
            foreach (string term in terms)
                if (value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public void Dispose()
        {
            if (posedSkin != null) UnityEngine.Object.Destroy(posedSkin);
            cache.Clear();
            statics = null;
            moving.Clear();
            visibleMoving.Clear();
        }
    }
}
