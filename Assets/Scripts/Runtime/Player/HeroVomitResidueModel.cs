using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>What one impact did to the residue.</summary>
    public enum HeroVomitResidueChange
    {
        Ignored,
        Created,
        Grown
    }

    /// <summary>
    /// One puddle: a centre on some surface, the plane it lies in, and the
    /// wet area it has collected. The radius follows the area, so a puddle
    /// fed by hundreds of rods widens like a real one instead of jumping.
    /// </summary>
    public readonly struct HeroVomitPatch
    {
        public HeroVomitPatch(
            Vector3 center,
            Vector3 normal,
            Vector3 tangent,
            float radius,
            float area,
            int seed,
            int ordinal)
        {
            Center = center;
            Normal = normal;
            Tangent = tangent;
            Radius = radius;
            Area = area;
            Seed = seed;
            Ordinal = ordinal;
        }

        public Vector3 Center { get; }
        public Vector3 Normal { get; }
        public Vector3 Tangent { get; }
        public float Radius { get; }
        public float Area { get; }
        public int Seed { get; }
        public int Ordinal { get; }
    }

    /// <summary>A lump that stayed where it landed.</summary>
    public readonly struct HeroVomitChunk
    {
        public HeroVomitChunk(
            Vector3 position,
            Vector3 normal,
            float size,
            float yawDegrees,
            bool pale)
        {
            Position = position;
            Normal = normal;
            Size = size;
            YawDegrees = yawDegrees;
            Pale = pale;
        }

        public Vector3 Position { get; }
        public Vector3 Normal { get; }
        public float Size { get; }
        public float YawDegrees { get; }
        public bool Pale { get; }
    }

    /// <summary>
    /// The geometry of what the hero leaves behind: puddles that merge and
    /// grow as the stream keeps landing in the same place, a handful of
    /// lumps, and the ragged fan each puddle is drawn as.
    ///
    /// Pure. Every irregularity comes from a stable hash of the model's seed,
    /// so the same bout in the same place leaves the same marks in a capture
    /// as in the game.
    /// </summary>
    public sealed class HeroVomitResidueModel
    {
        /// <summary>Impacts closer than this to a puddle's centre feed it.</summary>
        public const float MergeRadius = 0.3f;
        public const float MaxRadius = 0.45f;
        public const float MinRadius = 0.06f;
        public const int MaxPatches = 12;
        public const int MaxChunks = 48;
        public const int RimVertexCount = 10;
        public const float RimRadiusMinimum = 0.62f;
        /// <summary>
        /// A hair above the city's 5 mm puddle lift: the film must never
        /// z-fight the floor it lies on, nor a city puddle it lands in.
        /// </summary>
        public const float LiftMetres = 0.006f;
        public const float TextureMetresPerTile = 0.18f;
        /// <summary>Two surfaces count as one plane above this normal dot.</summary>
        public const float MergeNormalDot = 0.9f;
        /// <summary>
        /// A point this far off a puddle's plane belongs to another surface —
        /// a kerb above the road, a step above the landing — even when the
        /// normals agree.
        /// </summary>
        public const float MergePlaneTolerance = 0.05f;
        public const float ChunkSizeMinimum = 0.02f;
        public const float ChunkSizeMaximum = 0.03f;
        /// <summary>One lump in five is the pale kind.</summary>
        public const int PaleChunkEvery = 5;

        private const uint PatchSalt = 0x50415443u;
        private const uint ChunkSalt = 0x4348554Eu;
        private const uint RimSalt = 0x52494D00u;

        private readonly List<HeroVomitPatch> patches =
            new List<HeroVomitPatch>(MaxPatches + 1);
        private readonly List<HeroVomitChunk> chunks =
            new List<HeroVomitChunk>(MaxChunks);
        private readonly uint seed;
        private int nextPatchOrdinal;
        private int nextChunkOrdinal;

        public HeroVomitResidueModel(int seed)
        {
            this.seed = unchecked((uint)seed);
        }

        public IReadOnlyList<HeroVomitPatch> Patches => patches;
        public IReadOnlyList<HeroVomitChunk> Chunks => chunks;
        public int PatchCount => patches.Count;
        public int ChunkCount => chunks.Count;

        /// <summary>Set by every accepted change; the view clears it.</summary>
        public bool Dirty { get; private set; }

        public void ClearDirty()
        {
            Dirty = false;
        }

        /// <summary>
        /// A rod of liquid landed. Feeds the nearest puddle on the same plane
        /// within <see cref="MergeRadius"/>, or starts a new one; past
        /// <see cref="MaxPatches"/> the oldest puddle goes.
        /// </summary>
        public HeroVomitResidueChange AddImpact(
            Vector3 point,
            Vector3 normal,
            float volume)
        {
            if (!IsFinite(point) ||
                !IsFinite(normal) ||
                float.IsNaN(volume) ||
                float.IsInfinity(volume) ||
                volume <= 0f ||
                normal.sqrMagnitude <= 0.000001f)
            {
                return HeroVomitResidueChange.Ignored;
            }

            normal.Normalize();
            int nearest = FindMergeCandidate(point, normal);
            if (nearest >= 0)
            {
                HeroVomitPatch patch = patches[nearest];
                float area = patch.Area + volume;
                float radius = Mathf.Clamp(
                    Mathf.Sqrt(area / Mathf.PI),
                    MinRadius,
                    MaxRadius);
                // The centre drifts toward where the liquid actually lands,
                // by the share this rod is of the whole puddle, but only
                // within the puddle's own plane: the point may sit a few
                // millimetres off it and the mesh must stay flat on the floor.
                Vector3 delta = point - patch.Center;
                delta -= patch.Normal * Vector3.Dot(delta, patch.Normal);
                Vector3 center = patch.Center +
                                 delta * Mathf.Clamp01(volume / area);
                patches[nearest] = new HeroVomitPatch(
                    center,
                    patch.Normal,
                    patch.Tangent,
                    radius,
                    area,
                    patch.Seed,
                    patch.Ordinal);
                Dirty = true;
                return HeroVomitResidueChange.Grown;
            }

            int ordinal = nextPatchOrdinal++;
            int patchSeed = unchecked((int)CitySoundStableHash.Combine(
                seed,
                unchecked((uint)ordinal) ^ PatchSalt));
            patches.Add(new HeroVomitPatch(
                point,
                normal,
                TangentFor(normal),
                MinRadius,
                MinRadius * MinRadius * Mathf.PI + volume,
                patchSeed,
                ordinal));
            if (patches.Count > MaxPatches)
            {
                patches.RemoveAt(0);
            }

            Dirty = true;
            return HeroVomitResidueChange.Created;
        }

        /// <summary>
        /// A lump landed and stays as a small box. False once the cap is
        /// reached — the ground holds forty-eight and no more.
        /// </summary>
        public bool AddChunk(Vector3 point, Vector3 normal)
        {
            if (chunks.Count >= MaxChunks ||
                !IsFinite(point) ||
                !IsFinite(normal) ||
                normal.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            int ordinal = nextChunkOrdinal++;
            uint hash = CitySoundStableHash.Combine(
                seed,
                unchecked((uint)ordinal) ^ ChunkSalt);
            float size = Mathf.Lerp(
                ChunkSizeMinimum,
                ChunkSizeMaximum,
                Unit(hash));
            uint yawHash = CitySoundStableHash.Combine(hash, 1u);
            float yaw = Unit(yawHash) * 360f;
            bool pale = CitySoundStableHash.Combine(hash, 2u) %
                        PaleChunkEvery == 0;
            chunks.Add(new HeroVomitChunk(
                point,
                normal.normalized,
                size,
                yaw,
                pale));
            Dirty = true;
            return true;
        }

        /// <summary>
        /// Appends one puddle's fan to the lists: the centre and
        /// <see cref="RimVertexCount"/> rim vertices at hashed radii in the
        /// patch's tangent plane, lifted along its normal, facing the normal.
        /// UVs are tangent-plane metres over <see cref="TextureMetresPerTile"/>,
        /// so the slurry texture tiles at the same size on a floor and on a wall.
        /// </summary>
        public static void BuildPatchMesh(
            in HeroVomitPatch patch,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles)
        {
            if (vertices == null)
            {
                throw new ArgumentNullException(nameof(vertices));
            }

            if (normals == null)
            {
                throw new ArgumentNullException(nameof(normals));
            }

            if (uvs == null)
            {
                throw new ArgumentNullException(nameof(uvs));
            }

            if (triangles == null)
            {
                throw new ArgumentNullException(nameof(triangles));
            }

            Vector3 normal = patch.Normal;
            Vector3 tangent = patch.Tangent;
            // Left-handed: cross(normal, tangent) is the bitangent that makes
            // (centre, rim i, rim i+1) wind clockwise seen from the normal
            // side, which is the face Unity draws.
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            Vector3 origin = patch.Center + normal * LiftMetres;
            int baseIndex = vertices.Count;
            uint patchSeed = unchecked((uint)patch.Seed);

            vertices.Add(origin);
            normals.Add(normal);
            uvs.Add(Vector2.zero);
            for (int index = 0; index < RimVertexCount; index++)
            {
                float angle = index * (Mathf.PI * 2f / RimVertexCount);
                uint hash = CitySoundStableHash.Combine(
                    patchSeed,
                    unchecked((uint)index) ^ RimSalt);
                float radius = patch.Radius *
                               Mathf.Lerp(RimRadiusMinimum, 1f, Unit(hash));
                float along = Mathf.Cos(angle) * radius;
                float across = Mathf.Sin(angle) * radius;
                vertices.Add(origin + tangent * along + bitangent * across);
                normals.Add(normal);
                uvs.Add(new Vector2(
                    along / TextureMetresPerTile,
                    across / TextureMetresPerTile));
            }

            for (int index = 0; index < RimVertexCount; index++)
            {
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 1 + index);
                triangles.Add(baseIndex + 1 + (index + 1) % RimVertexCount);
            }
        }

        /// <summary>A unit vector perpendicular to the normal, stable per plane.</summary>
        public static Vector3 TangentFor(Vector3 normal)
        {
            Vector3 helper = Mathf.Abs(normal.y) < 0.9f
                ? Vector3.up
                : Vector3.forward;
            Vector3 tangent = Vector3.Cross(helper, normal);
            if (tangent.sqrMagnitude <= 0.000001f)
            {
                tangent = Vector3.Cross(Vector3.right, normal);
            }

            return tangent.normalized;
        }

        private int FindMergeCandidate(Vector3 point, Vector3 normal)
        {
            int nearest = -1;
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < patches.Count; index++)
            {
                HeroVomitPatch patch = patches[index];
                if (Vector3.Dot(patch.Normal, normal) <= MergeNormalDot)
                {
                    continue;
                }

                Vector3 delta = point - patch.Center;
                if (Mathf.Abs(Vector3.Dot(delta, patch.Normal)) >
                    MergePlaneTolerance)
                {
                    continue;
                }

                float distance = delta.magnitude;
                if (distance > MergeRadius || distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = distance;
                nearest = index;
            }

            return nearest;
        }

        private static float Unit(uint hash)
        {
            return (hash >> 8) / (float)(1u << 24);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
