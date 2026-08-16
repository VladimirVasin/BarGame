using System;
using UnityEngine;

namespace BarPromenade
{
    internal enum SurfaceProjection
    {
        BoxXY,
        BoxXZ,
        BoxZY,
        CylinderSide,
        CylinderCapXZ
    }

    /// <summary>
    /// The scene-agnostic half of the packaged surface pipelines: projected
    /// metre-scale UV tiling with a deterministic per-transform offset, and
    /// the albedo-compensated display tint. Scene appearance classes own
    /// their recipes and caches; this owns the math they share.
    /// </summary>
    internal static class SurfaceAppearanceCore
    {
        public static Vector4 CreateBaseMapTransform(
            Renderer renderer,
            float metersPerTile,
            float minimumUvScale,
            SurfaceProjection projection,
            int hashSalt)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            Vector3 dimensions = GetLocalMeshDimensions(renderer);
            ResolveProjectedDimensions(
                dimensions,
                projection,
                out float uMeters,
                out float vMeters);
            return CreateBaseMapTransform(
                renderer.transform,
                uMeters,
                vMeters,
                metersPerTile,
                minimumUvScale,
                hashSalt);
        }

        /// <summary>
        /// The dimension-explicit variant for renderers whose metre size
        /// cannot be read from a MeshFilter — the skinned cloth panels
        /// carry plain 0..1 UVs over their authored width and height.
        /// </summary>
        public static Vector4 CreateBaseMapTransform(
            Transform transform,
            float uMeters,
            float vMeters,
            float metersPerTile,
            float minimumUvScale,
            int hashSalt)
        {
            if (transform == null)
            {
                throw new ArgumentNullException(nameof(transform));
            }

            float uScale = Mathf.Max(
                minimumUvScale,
                uMeters / metersPerTile);
            float vScale = Mathf.Max(
                minimumUvScale,
                vMeters / metersPerTile);

            uint hash = StableHash(transform, hashSalt);
            float uOffset = (hash & 0xFFFFu) / 65536f;
            hash = Mix(hash, 0x9E3779B9u);
            float vOffset = (hash & 0xFFFFu) / 65536f;
            return new Vector4(uScale, vScale, uOffset, vOffset);
        }

        public static Color CreateDisplayTint(
            Color sourceTint,
            float albedoCompensation)
        {
            return new Color(
                Mathf.Clamp01(sourceTint.r * albedoCompensation),
                Mathf.Clamp01(sourceTint.g * albedoCompensation),
                Mathf.Clamp01(sourceTint.b * albedoCompensation),
                sourceTint.a);
        }

        private static Vector3 GetLocalMeshDimensions(Renderer renderer)
        {
            Vector3 meshSize = Vector3.one;
            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                meshSize = meshFilter.sharedMesh.bounds.size;
            }

            Vector3 scale = renderer.transform.localScale;
            return new Vector3(
                Mathf.Abs(meshSize.x * scale.x),
                Mathf.Abs(meshSize.y * scale.y),
                Mathf.Abs(meshSize.z * scale.z));
        }

        private static void ResolveProjectedDimensions(
            Vector3 dimensions,
            SurfaceProjection projection,
            out float uMeters,
            out float vMeters)
        {
            switch (projection)
            {
                case SurfaceProjection.BoxXY:
                    uMeters = dimensions.x;
                    vMeters = dimensions.y;
                    return;
                case SurfaceProjection.BoxXZ:
                case SurfaceProjection.CylinderCapXZ:
                    uMeters = dimensions.x;
                    vMeters = dimensions.z;
                    return;
                case SurfaceProjection.BoxZY:
                    uMeters = dimensions.z;
                    vMeters = dimensions.y;
                    return;
                case SurfaceProjection.CylinderSide:
                    uMeters = Mathf.PI *
                              (dimensions.x + dimensions.z) *
                              0.5f;
                    vMeters = dimensions.y;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(projection),
                        projection,
                        null);
            }
        }

        private static uint StableHash(Transform transform, int salt)
        {
            uint hash = 2166136261u;
            hash = Mix(hash, unchecked((uint)salt));
            Transform current = transform;
            while (current != null)
            {
                string objectName = current.gameObject.name;
                for (int index = 0; index < objectName.Length; index++)
                {
                    hash ^= objectName[index];
                    hash *= 16777619u;
                }

                Vector3 position = current.localPosition;
                hash = Mix(
                    hash,
                    unchecked(
                        (uint)Mathf.RoundToInt(position.x * 1000f)));
                hash = Mix(
                    hash,
                    unchecked(
                        (uint)Mathf.RoundToInt(position.y * 1000f)));
                hash = Mix(
                    hash,
                    unchecked(
                        (uint)Mathf.RoundToInt(position.z * 1000f)));
                current = current.parent;
            }

            return hash;
        }

        private static uint Mix(uint first, uint second)
        {
            uint hash = first ^ second;
            hash *= 16777619u;
            hash ^= hash >> 16;
            return hash;
        }
    }
}
