using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The bouquet the mourner leaves on the slab: the SAME funeral-
    /// bouquet hand prop she carried, placed free-standing instead of
    /// under her grip, so what lies on the grave is what was in her hands
    /// and not a second, cruder object.
    ///
    /// The prop's root is the socket head at the stems' end, and its
    /// geometry runs wherever the grip pointed in the bind pose, so laying
    /// it takes two steps this class keeps pure and testable: rotate the
    /// root so the stems-to-blooms axis lies along grave-local +Z (toward
    /// the stone), then re-centre the rotated bounds on the slab point and
    /// lift them so the lowest point rests on the slab rather than the
    /// root (the stems' end) doing so.
    /// </summary>
    internal static class CemeteryLaidBouquet
    {
        public const string RuntimeObjectName = "Cemetery Mourner Bouquet";

        /// <summary>The two parts the laid axis is measured between: the
        /// stems at the socket end, the first bloom at the far end.</summary>
        public const string StemsRendererName = "ACC_BouquetStems";
        public const string BloomRendererName = "ACC_BouquetBloomA";

        /// <summary>
        /// Places the funeral bouquet on the slab. `slabPosition` is the
        /// world point on the slab top the bouquet is centred on, and
        /// `graveYaw` the grave's frame (+Z toward the stone). The prop
        /// is parented under `parent` so it lives exactly as long as the
        /// visit.
        /// </summary>
        public static CityPedestrianHandPropRegistry Place(
            Transform parent,
            Vector3 slabPosition,
            Quaternion graveYaw,
            Material material,
            int paletteVariant)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            // Placed at the slab in the grave frame first, so every
            // measurement below can be taken in the prop's root space
            // with nothing but the free-standing Mount between the
            // parts and the root.
            CityPedestrianHandPropRegistry registry =
                CityPedestrianHandProps.Place(
                    CityPedestrianHandPropId.FuneralBouquet,
                    parent,
                    parent.InverseTransformPoint(slabPosition),
                    Quaternion.Inverse(parent.rotation) * graveYaw,
                    material,
                    paletteVariant);
            Transform root = registry.transform;
            root.name = RuntimeObjectName;

            // Mesh-bounds centres through the part transforms rather than
            // Renderer.bounds: exact for the centre of one mesh, and not
            // dependent on the renderer having been through a frame.
            if (!TryGetRootLocalCentre(
                    registry,
                    StemsRendererName,
                    out Vector3 stemsCentre) ||
                !TryGetRootLocalCentre(
                    registry,
                    BloomRendererName,
                    out Vector3 bloomCentre))
            {
                CityPedestrianHandProps.Detach(ref registry);
                throw new InvalidOperationException(
                    "The funeral bouquet prop lost '" + StemsRendererName +
                    "' or '" + BloomRendererName + "'.");
            }

            if (!TryMeasureRootLocalBounds(
                    registry,
                    out Vector3 boundsMin,
                    out Vector3 boundsMax))
            {
                CityPedestrianHandProps.Detach(ref registry);
                throw new InvalidOperationException(
                    "The funeral bouquet prop carries no mesh to lay.");
            }

            ComputeLaidPose(
                stemsCentre,
                bloomCentre,
                boundsMin,
                boundsMax,
                out Quaternion laidRotation,
                out Vector3 laidOffset);
            root.SetPositionAndRotation(
                slabPosition + graveYaw * laidOffset,
                graveYaw * laidRotation);
            return registry;
        }

        /// <summary>
        /// Pure. Given, in the prop's free-standing root space, the
        /// centres of the stems and of the first bloom and the AABB of the
        /// whole prop, returns the root rotation that lays the bouquet
        /// along +Z (blooms toward the stone) and the grave-local offset
        /// of the root from the slab point that centres the laid bouquet
        /// on it in XZ with its lowest point on the slab.
        /// </summary>
        public static void ComputeLaidPose(
            Vector3 stemsCentre,
            Vector3 bloomCentre,
            Vector3 boundsMin,
            Vector3 boundsMax,
            out Quaternion rotation,
            out Vector3 offset)
        {
            rotation = ComputeLaidRotation(stemsCentre, bloomCentre);
            RotateBounds(
                rotation,
                boundsMin,
                boundsMax,
                out Vector3 laidMin,
                out Vector3 laidMax);
            offset = ComputeRestOffset(laidMin, laidMax);
        }

        /// <summary>The rotation taking the stems-to-bloom axis onto +Z;
        /// identity when the two centres coincide, rather than a NaN.</summary>
        public static Quaternion ComputeLaidRotation(
            Vector3 stemsCentre,
            Vector3 bloomCentre)
        {
            Vector3 axis = bloomCentre - stemsCentre;
            if (axis.sqrMagnitude < 0.000001f)
            {
                return Quaternion.identity;
            }

            return Quaternion.FromToRotation(axis.normalized, Vector3.forward);
        }

        /// <summary>
        /// The root offset, in the laid frame, that puts the laid AABB's
        /// XZ centre on the slab point and its bottom on the slab top.
        /// Every input is relative to the root, so negating is exactly the
        /// shift that brings that point to the origin.
        /// </summary>
        public static Vector3 ComputeRestOffset(
            Vector3 laidBoundsMin,
            Vector3 laidBoundsMax)
        {
            return new Vector3(
                -0.5f * (laidBoundsMin.x + laidBoundsMax.x),
                -laidBoundsMin.y,
                -0.5f * (laidBoundsMin.z + laidBoundsMax.z));
        }

        /// <summary>The AABB of a rotated AABB: its eight corners turned
        /// and re-boxed. Conservative, which only ever lifts the bouquet a
        /// hair, never sinks it.</summary>
        public static void RotateBounds(
            Quaternion rotation,
            Vector3 min,
            Vector3 max,
            out Vector3 rotatedMin,
            out Vector3 rotatedMax)
        {
            rotatedMin = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);
            rotatedMax = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = rotation * new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                rotatedMin = Vector3.Min(rotatedMin, point);
                rotatedMax = Vector3.Max(rotatedMax, point);
            }
        }

        private static bool TryGetRootLocalCentre(
            CityPedestrianHandPropRegistry registry,
            string rendererName,
            out Vector3 centre)
        {
            Renderer renderer = registry.FindRenderer(rendererName);
            var filter = renderer != null
                ? renderer.GetComponent<MeshFilter>()
                : null;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
            {
                centre = Vector3.zero;
                return false;
            }

            Matrix4x4 meshToRoot =
                registry.transform.worldToLocalMatrix *
                renderer.localToWorldMatrix;
            centre = meshToRoot.MultiplyPoint3x4(mesh.bounds.center);
            return true;
        }

        /// <summary>
        /// The prop's AABB in its root space, from each part's mesh bounds
        /// through the part's transform. Mesh bounds rather than vertices:
        /// they are enough to rest a bouquet on a slab and they do not
        /// depend on the mesh being CPU-readable (the prop FBX happens to
        /// import readable for the cafe contact sweeps, but nothing here
        /// should rely on that).
        /// </summary>
        private static bool TryMeasureRootLocalBounds(
            CityPedestrianHandPropRegistry registry,
            out Vector3 min,
            out Vector3 max)
        {
            Matrix4x4 worldToRoot = registry.transform.worldToLocalMatrix;
            min = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);
            max = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity);
            bool any = false;
            for (int index = 0; index < registry.Renderers.Count; index++)
            {
                Renderer renderer = registry.Renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                var filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                Matrix4x4 meshToRoot =
                    worldToRoot * renderer.localToWorldMatrix;
                Bounds local = mesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = meshToRoot.MultiplyPoint3x4(new Vector3(
                        (corner & 1) == 0 ? local.min.x : local.max.x,
                        (corner & 2) == 0 ? local.min.y : local.max.y,
                        (corner & 4) == 0 ? local.min.z : local.max.z));
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                    any = true;
                }
            }

            return any;
        }
    }
}
