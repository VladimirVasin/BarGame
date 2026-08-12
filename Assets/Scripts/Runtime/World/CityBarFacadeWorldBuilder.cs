using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Builds the passive, collider-free identity of a bar facade. City and
    /// the bounded Home exterior share this recipe; gameplay entrances remain
    /// owned by CityWorldBuilder.
    /// </summary>
    public static class CityBarFacadeWorldBuilder
    {
        private static readonly Color BarTrim =
            new Color(0.84f, 0.55f, 0.18f);
        private static readonly Color BarAwning =
            new Color(0.24f, 0.018f, 0.045f);
        private static readonly Color DoorColor =
            new Color(0.055f, 0.025f, 0.022f);

        // The sign keeps the palette of the pixel panel it replaces, so the
        // bars stay recognisable from the same distance they always were.
        private static readonly Color SignOutline =
            new Color(0.137f, 0.071f, 0.114f);
        private static readonly Color SignField =
            new Color(0.357f, 0.086f, 0.169f);
        private static readonly Color SignPale =
            new Color(0.976f, 0.914f, 0.722f);
        private static readonly Color SignDrink =
            new Color(0.871f, 0.545f, 0.165f);

        public static void BuildCity(
            Transform parent,
            BuildingLot lot)
        {
            Validate(parent, lot);
            Build(
                parent,
                lot.DoorPosition,
                ResolveDirection(lot),
                lot.BarId,
                null,
                1f,
                false);
        }

        public static void BuildHomeExterior(
            Transform parent,
            HomeExteriorContextPlan context,
            BuildingLot lot)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            Validate(parent, lot);
            Build(
                parent,
                PlayerHomeBalconyGeometry.ToHomeLocal(
                    context.PlayerHome,
                    lot.DoorPosition),
                PlayerHomeBalconyGeometry.ToHomeLocalDirection(
                    context.PlayerHome,
                    ResolveDirection(lot)),
                lot.BarId,
                null,
                1f,
                true);
        }

        private static void Build(
            Transform parent,
            Vector3 doorPosition,
            Vector3 direction,
            string barId,
            Material material,
            float colorScale,
            bool clipToHomeExterior)
        {
            direction.y = 0f;
            direction.Normalize();
            Vector3 tangent = new Vector3(
                -direction.z,
                0f,
                direction.x);
            bool frontageIsX =
                Mathf.Abs(direction.x) > 0.5f;
            Vector3 doorSize = frontageIsX
                ? new Vector3(0.12f, 2.1f, 1.45f)
                : new Vector3(1.45f, 2.1f, 0.12f);
            Vector3 verticalSize = frontageIsX
                ? new Vector3(0.18f, 2.35f, 0.16f)
                : new Vector3(0.16f, 2.35f, 0.18f);
            Vector3 headerSize = frontageIsX
                ? new Vector3(0.18f, 0.22f, 2.05f)
                : new Vector3(2.05f, 0.22f, 0.18f);
            Vector3 canopySize = frontageIsX
                ? new Vector3(
                    0.82f,
                    0.18f,
                    BarEntranceGeometry.CanopyWidth)
                : new Vector3(
                    BarEntranceGeometry.CanopyWidth,
                    0.18f,
                    0.82f);

            CreateBox(
                "Bar Door",
                parent,
                doorPosition +
                direction * 0.045f +
                Vector3.up * 1.05f,
                doorSize,
                ScaleColor(DoorColor, colorScale),
                material,
                clipToHomeExterior);
            for (int side = -1; side <= 1; side += 2)
            {
                CreateBox(
                    "Bar Door Frame",
                    parent,
                    doorPosition +
                    direction * 0.10f +
                    tangent * side * 0.86f +
                    Vector3.up * 1.14f,
                    verticalSize,
                    ScaleColor(BarTrim, colorScale),
                    material,
                    clipToHomeExterior);
            }

            CreateBox(
                "Bar Door Header",
                parent,
                doorPosition +
                direction * 0.10f +
                Vector3.up * 2.30f,
                headerSize,
                ScaleColor(BarTrim, colorScale),
                material,
                clipToHomeExterior);
            CreateBox(
                "Bar Entrance Canopy",
                parent,
                doorPosition +
                direction * 0.38f +
                Vector3.up * 2.52f,
                canopySize,
                ScaleColor(BarTrim, colorScale),
                material,
                clipToHomeExterior);
            CreateBox(
                "Bar Entrance Canopy Inset",
                parent,
                doorPosition +
                direction * 0.40f +
                Vector3.up * 2.46f,
                Vector3.Scale(
                    canopySize,
                    new Vector3(0.88f, 0.55f, 0.88f)),
                ScaleColor(BarAwning, colorScale),
                material,
                clipToHomeExterior);

            Vector3 bracketSize = frontageIsX
                ? new Vector3(1.25f, 0.10f, 0.10f)
                : new Vector3(0.10f, 0.10f, 1.25f);
            CreateBox(
                "Bar Sign Bracket",
                parent,
                doorPosition +
                direction * 0.34f +
                Vector3.up * 4.10f,
                bracketSize,
                ScaleColor(BarTrim, colorScale),
                material,
                clipToHomeExterior);

            Vector3 markerPosition =
                doorPosition +
                direction * 0.74f +
                Vector3.up * 3.42f;
            if (clipToHomeExterior &&
                markerPosition.x <=
                HomeExteriorViewBuilder.ExteriorMinimumX)
            {
                return;
            }

            GameObject markerObject = new GameObject(
                "Bar Landmark Marker");
            markerObject.transform.SetParent(parent, false);
            markerObject.transform.localPosition = markerPosition;
            BarBuildingMarker marker =
                markerObject.AddComponent<BarBuildingMarker>();
            marker.Initialize(barId);
            BuildHangingSign(
                marker,
                markerObject.transform,
                frontageIsX,
                material,
                colorScale,
                clipToHomeExterior,
                markerPosition);
        }

        /// <summary>
        /// A projecting blade sign hung under the bracket arm: layered plates
        /// whose faces look along the street, plus the tankard that used to be
        /// drawn in pixels.
        /// <para>
        /// Each layer is smaller across the panel than the one behind it but
        /// slightly thicker across the blade, so the layer behind survives as
        /// a border without needing four boxes per frame edge. Eight boxes
        /// carry the whole sign.
        /// </para>
        /// </summary>
        private static void BuildHangingSign(
            BarBuildingMarker marker,
            Transform parent,
            bool frontageIsX,
            Material material,
            float colorScale,
            bool clipToHomeExterior,
            Vector3 markerPosition)
        {
            // `direction` runs out from the wall along the bracket arm, so the
            // panel spans it and the blade thickness runs across the frontage.
            Vector3 outward = frontageIsX
                ? new Vector3(1f, 0f, 0f)
                : new Vector3(0f, 0f, 1f);
            Vector3 across = frontageIsX
                ? new Vector3(0f, 0f, 1f)
                : new Vector3(1f, 0f, 0f);
            AddSignBox(
                marker,
                parent,
                "Bar Sign Hanger Inner",
                markerPosition,
                (outward * -0.30f) + (Vector3.up * 0.46f),
                Size(outward, across, 0.06f, 0.30f, 0.05f),
                SignOutline,
                material,
                colorScale,
                clipToHomeExterior);
            AddSignBox(
                marker,
                parent,
                "Bar Sign Hanger Outer",
                markerPosition,
                (outward * 0.30f) + (Vector3.up * 0.46f),
                Size(outward, across, 0.06f, 0.30f, 0.05f),
                SignOutline,
                material,
                colorScale,
                clipToHomeExterior);

            AddSignBox(
                marker,
                parent,
                "Bar Sign Panel",
                markerPosition,
                Vector3.zero,
                Size(outward, across, 0.90f, 0.70f, 0.10f),
                SignOutline,
                material,
                colorScale,
                clipToHomeExterior);
            AddSignBox(
                marker,
                parent,
                "Bar Sign Panel Frame",
                markerPosition,
                Vector3.zero,
                Size(outward, across, 0.84f, 0.64f, 0.12f),
                BarTrim,
                material,
                colorScale,
                clipToHomeExterior);
            AddSignBox(
                marker,
                parent,
                "Bar Sign Panel Field",
                markerPosition,
                Vector3.zero,
                Size(outward, across, 0.72f, 0.52f, 0.13f),
                SignField,
                material,
                colorScale,
                clipToHomeExterior);

            // The tankard, kept chunky enough to read at the distance the
            // pixel panel used to.
            AddSignBox(
                marker,
                parent,
                "Bar Sign Tankard",
                markerPosition,
                outward * -0.05f,
                Size(outward, across, 0.26f, 0.34f, 0.15f),
                SignPale,
                material,
                colorScale,
                clipToHomeExterior);
            AddSignBox(
                marker,
                parent,
                "Bar Sign Tankard Fill",
                markerPosition,
                (outward * -0.05f) + (Vector3.up * -0.04f),
                Size(outward, across, 0.18f, 0.20f, 0.16f),
                SignDrink,
                material,
                colorScale,
                clipToHomeExterior);
            AddSignBox(
                marker,
                parent,
                "Bar Sign Tankard Handle",
                markerPosition,
                (outward * 0.13f) + (Vector3.up * -0.02f),
                Size(outward, across, 0.09f, 0.18f, 0.14f),
                SignPale,
                material,
                colorScale,
                clipToHomeExterior);
        }

        private static Vector3 Size(
            Vector3 outward,
            Vector3 across,
            float along,
            float height,
            float thickness)
        {
            Vector3 size = (outward * along) + (across * thickness);
            size.x = Mathf.Abs(size.x);
            size.z = Mathf.Abs(size.z);
            size.y = height;
            return size;
        }

        private static void AddSignBox(
            BarBuildingMarker marker,
            Transform parent,
            string name,
            Vector3 markerPosition,
            Vector3 offset,
            Vector3 size,
            Color color,
            Material material,
            float colorScale,
            bool clipToHomeExterior)
        {
            Bounds bounds = new Bounds(markerPosition + offset, size);
            if (clipToHomeExterior &&
                !HomeExteriorViewBuilder.TryClipToExteriorHalfSpace(
                    bounds,
                    out bounds))
            {
                return;
            }

            Color scaled = ScaleColor(color, colorScale);
            GameObject part = material == null
                ? RuntimePrimitiveFactory.CreateBox(
                    name,
                    parent,
                    bounds.center - markerPosition,
                    bounds.size,
                    scaled,
                    false)
                : RuntimePrimitiveFactory.CreateBox(
                    name,
                    parent,
                    bounds.center - markerPosition,
                    bounds.size,
                    scaled,
                    material,
                    false);
            marker.RegisterSignPart(part);
        }

        private static void CreateBox(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 size,
            Color color,
            Material material,
            bool clipToHomeExterior)
        {
            Bounds bounds = new Bounds(position, size);
            if (clipToHomeExterior &&
                !HomeExteriorViewBuilder.TryClipToExteriorHalfSpace(
                    bounds,
                    out bounds))
            {
                return;
            }

            if (material == null)
            {
                RuntimePrimitiveFactory.CreateBox(
                    name,
                    parent,
                    bounds.center,
                    bounds.size,
                    color,
                    false);
                return;
            }

            RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                bounds.center,
                bounds.size,
                color,
                material,
                false);
        }

        private static Vector3 ResolveDirection(BuildingLot lot)
        {
            return new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y);
        }

        private static Color ScaleColor(
            Color color,
            float scale)
        {
            return new Color(
                color.r * scale,
                color.g * scale,
                color.b * scale,
                color.a);
        }

        private static void Validate(
            Transform parent,
            BuildingLot lot)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (lot == null)
            {
                throw new ArgumentNullException(nameof(lot));
            }

            if (!lot.IsBar ||
                !lot.HasRoadFrontage)
            {
                throw new ArgumentException(
                    "A passive bar facade requires a bar lot with street frontage.",
                    nameof(lot));
            }
        }
    }
}
