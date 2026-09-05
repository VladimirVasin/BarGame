using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Presses only the beach's loose visual skin. The fixed compacted-ground
    /// collider stays unchanged, so small ridges and trails never catch a boot.
    /// Mesh coordinates are world coordinates, like the city's terrain mesh.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CitySandTreading : MonoBehaviour, IPlayerFootstepSurface
    {
        public const float TreadRadius = 0.50f;
        public const float TreadStrength = 0.72f;
        public const float RebuildInterval = 0.1f;
        private const float BucketSize = 1f;
        private const float InlandRecoveryPerSecond = 0.0025f;
        private const float MinimumStampTravelSquared = 0.04f;

        private readonly Dictionary<Vector2Int, List<int>> buckets =
            new Dictionary<Vector2Int, List<int>>();
        private readonly List<int> activeVertices = new List<int>();
        private readonly List<Vector3> normalBuffer = new List<Vector3>();
        private readonly List<CitySurfaceDescriptor> beaches =
            new List<CitySurfaceDescriptor>();
        private readonly RaycastHit[] groundHits = new RaycastHit[32];
        private Mesh mesh;
        private MeshCollider beachCollider;
        private Vector3[] vertices;
        private Vector3[] originalNormals;
        private float[] grounds;
        private float[] depths;
        private float[] pressed;
        private bool[] active;
        private Transform walker;
        private float rebuildCountdown;
        private bool dirty;
        private Vector3 lastStamp = new Vector3(float.NaN, 0f, 0f);

        public void Initialize(
            CityLayout layout,
            Mesh visualMesh,
            float[] compactedHeights,
            float[] looseDepths)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            mesh = visualMesh ?? throw new ArgumentNullException(nameof(visualMesh));
            grounds = compactedHeights ??
                      throw new ArgumentNullException(nameof(compactedHeights));
            depths = looseDepths ?? throw new ArgumentNullException(nameof(looseDepths));
            vertices = mesh.vertices;
            originalNormals = mesh.normals;
            if (vertices.Length != grounds.Length || vertices.Length != depths.Length)
            {
                throw new ArgumentException("Sand vertices and compacted/loose heights disagree.");
            }

            beachCollider = GetComponent<MeshCollider>();
            if (beachCollider == null || beachCollider.sharedMesh == null ||
                beachCollider.sharedMesh == mesh)
            {
                throw new InvalidOperationException(
                    "Treadable sand needs its own separate fixed beach collider mesh.");
            }

            mesh.MarkDynamic();

            beaches.Clear();
            foreach (CitySurfaceDescriptor surface in layout.Surfaces)
            {
                if (surface.Kind == CitySurfaceKind.Beach)
                {
                    beaches.Add(surface);
                }
            }

            buckets.Clear();
            activeVertices.Clear();
            pressed = new float[vertices.Length];
            active = new bool[vertices.Length];
            dirty = false;
            rebuildCountdown = 0f;
            lastStamp = new Vector3(float.NaN, 0f, 0f);
            for (int index = 0; index < vertices.Length; index++)
            {
                if (depths[index] <= 0f) continue;
                Vector2Int key = Bucket(vertices[index]);
                if (!buckets.TryGetValue(key, out List<int> members))
                {
                    members = new List<int>();
                    buckets.Add(key, members);
                }
                members.Add(index);
            }
        }

        public void AttachWalker(Transform walkerToFollow)
        {
            walker = walkerToFollow;
            lastStamp = new Vector3(float.NaN, 0f, 0f);
        }

        /// <summary>
        /// One soft pass at a confirmed beach contact. Runtime callers check
        /// the topmost physical surface first; this method also allows a pure
        /// trail probe without a player rig or a physics tick.
        /// </summary>
        public void Press(Vector3 worldPosition)
        {
            if (vertices == null) return;
            Vector2Int minimum = Bucket(worldPosition - Vector3.one * TreadRadius);
            Vector2Int maximum = Bucket(worldPosition + Vector3.one * TreadRadius);
            float radiusSquared = TreadRadius * TreadRadius;
            for (int z = minimum.y; z <= maximum.y; z++)
            {
                for (int x = minimum.x; x <= maximum.x; x++)
                {
                    if (!buckets.TryGetValue(new Vector2Int(x, z), out List<int> members))
                        continue;
                    foreach (int index in members)
                    {
                        float dx = vertices[index].x - worldPosition.x;
                        float dz = vertices[index].z - worldPosition.z;
                        float squared = dx * dx + dz * dz;
                        if (squared >= radiusSquared) continue;
                        float falloff = 1f - Mathf.Sqrt(squared) / TreadRadius;
                        float target = TreadStrength * Mathf.SmoothStep(0f, 1f, falloff);
                        if (target <= pressed[index]) continue;
                        pressed[index] = target;
                        vertices[index].y = grounds[index] + depths[index] * (1f - target);
                        if (!active[index])
                        {
                            active[index] = true;
                            activeVertices.Add(index);
                        }
                        dirty = true;
                    }
                }
            }
        }

        public bool TryPlayFootstep(Vector3 position, float runBlend)
        {
            if (!TryFindBeachContact(position, out Vector3 contact)) return false;
            float depth = SampleVisibleDepth(contact);
            Press(contact);
            RetroAudio.PlayAt(RetroSfxId.FootstepSoil, contact);
            if (depth > 0.015f)
            {
                AlpineVillageSnowKickup.Spawn(
                    transform, contact + Vector3.up * depth, depth, sand: true);
            }
            return true;
        }

        public float SampleVisibleDepth(Vector3 position)
        {
            if (vertices == null) return 0f;
            Vector2Int center = Bucket(position);
            float bestSquared = BucketSize * BucketSize;
            float result = 0f;
            for (int z = center.y - 1; z <= center.y + 1; z++)
            {
                for (int x = center.x - 1; x <= center.x + 1; x++)
                {
                    if (!buckets.TryGetValue(new Vector2Int(x, z), out List<int> members))
                        continue;
                    foreach (int index in members)
                    {
                        float dx = vertices[index].x - position.x;
                        float dz = vertices[index].z - position.z;
                        float squared = dx * dx + dz * dz;
                        if (squared >= bestSquared) continue;
                        bestSquared = squared;
                        result = depths[index] * (1f - pressed[index]);
                    }
                }
            }
            return result;
        }

        private void Update()
        {
            if (vertices == null) return;
            if (walker != null)
            {
                Vector3 at = walker.position;
                if (float.IsNaN(lastStamp.x) ||
                    (at - lastStamp).sqrMagnitude > MinimumStampTravelSquared)
                {
                    if (TryFindBeachContact(at, out Vector3 contact)) Press(contact);
                    lastStamp = at;
                }
            }

            for (int slot = activeVertices.Count - 1; slot >= 0; slot--)
            {
                int index = activeVertices[slot];
                pressed[index] = Mathf.Max(
                    0f, pressed[index] - InlandRecoveryPerSecond * Time.deltaTime);
                vertices[index].y = grounds[index] + depths[index] * (1f - pressed[index]);
                dirty = true;
                if (pressed[index] <= 0f)
                {
                    active[index] = false;
                    int last = activeVertices.Count - 1;
                    activeVertices[slot] = activeVertices[last];
                    activeVertices.RemoveAt(last);
                }
            }

            rebuildCountdown -= Time.deltaTime;
            if (!dirty || rebuildCountdown > 0f) return;
            mesh.SetVertices(vertices);
            mesh.RecalculateNormals();
            // The compacted surf band is shared with the seabed. A local
            // inland print must not replace its matched analytic normals.
            mesh.GetNormals(normalBuffer);
            for (int index = 0; index < pressed.Length; index++)
                if (pressed[index] <= 0f)
                    normalBuffer[index] = originalNormals[index];
            mesh.SetNormals(normalBuffer);
            mesh.RecalculateBounds();
            dirty = false;
            rebuildCountdown = RebuildInterval;
        }

        private bool TryFindBeachContact(Vector3 position, out Vector3 contact)
        {
            contact = default;
            if (beachCollider == null || !beachCollider.enabled) return false;
            Vector2 point = new Vector2(position.x, position.z);
            bool overBeach = false;
            foreach (CitySurfaceDescriptor beach in beaches)
            {
                if (!beach.WorldBounds.Contains(point)) continue;
                overBeach = true;
                break;
            }
            if (!overBeach) return false;

            int count = Physics.RaycastNonAlloc(
                position + Vector3.up * 0.6f, Vector3.down, groundHits, 1.2f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // A full buffer cannot establish which surface is topmost.
            if (count == groundHits.Length) return false;
            float closest = float.PositiveInfinity;
            Collider topmost = null;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = groundHits[index];
                if (hit.collider == null ||
                    (walker != null && hit.collider.transform.IsChildOf(walker)) ||
                    hit.distance >= closest) continue;
                closest = hit.distance;
                topmost = hit.collider;
                contact = hit.point;
            }
            return topmost == beachCollider &&
                   Mathf.Abs(position.y - contact.y) <= 0.4f;
        }

        private static Vector2Int Bucket(Vector3 point)
        {
            return new Vector2Int(
                Mathf.FloorToInt(point.x / BucketSize),
                Mathf.FloorToInt(point.z / BucketSize));
        }
    }
}
