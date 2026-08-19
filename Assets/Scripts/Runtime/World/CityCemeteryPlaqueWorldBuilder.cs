using System;
using TMPro;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The board on the front of a stone the hero set himself, with the
    /// words actually on it.
    ///
    /// Two things make that work. The plate is its own quad with
    /// authored UVs rather than a cube face, because a cube's faces do
    /// not all run the same way up and half of them would mirror the
    /// text. And it is placed against the stone's *measured* front
    /// face: the four monument silhouettes are different widths and
    /// heights, so a fixed offset would leave the board floating off a
    /// cross and buried in an obelisk.
    /// </summary>
    public static class CityCemeteryPlaqueWorldBuilder
    {
        public const string RootName = "Grave Plaque";
        public const string PlateName = "Plaque Plate";
        public const string BezelName = "Plaque Bezel";

        public const float WidthMeters = 0.30f;
        public const float HeightMeters = 0.20f;
        public const float BezelMeters = 0.022f;
        public const float BezelThicknessMeters = 0.016f;

        /// <summary>How far proud of the stone's face the plate sits,
        /// so it never fights the stone for the same pixels.</summary>
        public const float ProudMeters = 0.012f;

        /// <summary>
        /// Where up the stone the board is fixed, as a share of the
        /// stone's own height. Chest height on anything the yard
        /// stands: where a hand would put it and an eye finds it.
        /// </summary>
        public const float SeatHeightFraction = 0.56f;

        /// <summary>
        /// The most of the stone's own width the board may take. A
        /// cross is narrow and a plate wider than its arm would read
        /// as nailed to the air.
        /// </summary>
        public const float MaximumFaceShare = 0.82f;

        /// <summary>Stand-in height for the nominal seat, before any
        /// stone has been measured.</summary>
        public const float NominalSeatHeightMeters = 0.74f;

        public const float PlateThicknessMeters = 0.010f;

        /// <summary>Dull brass, and the near-black a stamped letter
        /// reads as against it.</summary>
        internal static readonly Color Plate =
            new Color(0.55f, 0.49f, 0.28f);
        internal static readonly Color Ink =
            new Color(0.12f, 0.10f, 0.06f);
        internal static readonly Color Bezel =
            new Color(0.29f, 0.31f, 0.24f);

        /// <summary>
        /// Fixes a board to a stone that is already built, measuring
        /// the stone to find its face. The board is parented to it, so
        /// it rides the stone while it is being heaved upright and
        /// driven home.
        /// </summary>
        public static GameObject Attach(
            Transform stone,
            CemeteryGravediggingPlan plan)
        {
            if (stone == null)
            {
                throw new ArgumentNullException(nameof(stone));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!plan.IsPresent ||
                !TryMeasure(stone, out Bounds bounds))
            {
                return null;
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(stone, true);

            // The face a visitor stands at: down the grave, toward the
            // feet, which is where the mound is and where the camera
            // walks round to.
            Vector3 outward = plan.Heading * Vector3.back;
            float reach = Mathf.Abs(
                (bounds.extents.x * outward.x) +
                (bounds.extents.z * outward.z));
            Vector3 face = bounds.center +
                           (outward * (reach + ProudMeters));

            float span = Mathf.Max(
                Mathf.Abs(bounds.size.x * outward.z),
                Mathf.Abs(bounds.size.z * outward.x));
            float width = Mathf.Min(
                WidthMeters,
                span * MaximumFaceShare);
            float height = width * (HeightMeters / WidthMeters);
            float seat = bounds.min.y +
                         (bounds.size.y * SeatHeightFraction);

            // TextMeshPro lays its quads out with normals of
            // `(0, 0, -1)`, so a line of text is readable from its own
            // local -Z. Facing the board's +Z into the stone puts that
            // readable side out at the reader, and nothing may then be
            // turned a further half-circle on top of it — which is
            // exactly what showed the words back to front.
            root.transform.SetPositionAndRotation(
                new Vector3(face.x, seat, face.z),
                Quaternion.LookRotation(-outward, Vector3.up));

            RuntimePrimitiveFactory.CreateBox(
                BezelName,
                root.transform,
                new Vector3(0f, 0f, BezelThicknessMeters * 0.6f),
                new Vector3(
                    width + BezelMeters,
                    height + BezelMeters,
                    BezelThicknessMeters),
                Bezel,
                false);
            root.AddComponent<CemeteryPlaqueSurface>();
            BuildPlate(root.transform, width, height);
            root.GetComponent<CemeteryPlaqueSurface>().Refresh();
            return root;
        }

        /// <summary>
        /// The plate and the three lines standing on it.
        ///
        /// The words are real text rather than a picture of text: the
        /// source face carries the whole Russian alphabet, so anything
        /// a player can type has a letter, which a hand-drawn font
        /// could only promise for the characters somebody remembered
        /// to draw.
        /// </summary>
        private static void BuildPlate(
            Transform parent,
            float width,
            float height)
        {
            // The brass sits behind the words, toward the stone.
            RuntimePrimitiveFactory.CreateBox(
                PlateName,
                parent,
                new Vector3(0f, 0f, PlateThicknessMeters * 0.5f),
                new Vector3(width, height, PlateThicknessMeters),
                Plate,
                false);

            var surface = parent.GetComponent<CemeteryPlaqueSurface>();
            TMP_FontAsset font = CemeteryPlaqueFont.Get();
            if (font == null)
            {
                return;
            }

            // Laid out top to bottom on the plate's own face, each
            // line in its own rect so none of them can push another
            // off the brass.
            TMP_Text name = CreateLine(
                parent,
                "Plaque Name",
                font,
                width,
                height * 0.30f,
                height * 0.29f,
                CemeteryPlaqueSurface.NameSize,
                false);
            TMP_Text years = CreateLine(
                parent,
                "Plaque Years",
                font,
                width,
                height * 0.22f,
                height * 0.04f,
                CemeteryPlaqueSurface.YearsSize,
                false);
            TMP_Text epitaph = CreateLine(
                parent,
                "Plaque Epitaph",
                font,
                width,
                height * 0.40f,
                -height * 0.24f,
                CemeteryPlaqueSurface.EpitaphSize,
                true);
            surface.Bind(name, years, epitaph);
        }

        /// <summary>
        /// One line of the board: a text mesh laid flat against the
        /// plate, a hair proud of it so it never fights the brass for
        /// the same pixels.
        /// </summary>
        private static TMP_Text CreateLine(
            Transform parent,
            string name,
            TMP_FontAsset font,
            float width,
            float height,
            float offsetY,
            float size,
            bool autoSize)
        {
            var line = new GameObject(name);
            line.transform.SetParent(parent, false);
            // A hair in front of the brass, and unturned: the board is
            // already facing the right way.
            line.transform.localPosition = new Vector3(
                0f,
                offsetY,
                -0.002f);
            line.transform.localRotation = Quaternion.identity;

            var text = line.AddComponent<TextMeshPro>();
            text.font = font;
            text.fontSize = size;
            text.color = Ink;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Truncate;
            text.enableAutoSizing = autoSize;
            if (autoSize)
            {
                text.fontSizeMin =
                    CemeteryPlaqueSurface.EpitaphMinimumSize;
                text.fontSizeMax = size;
            }

            text.rectTransform.sizeDelta =
                new Vector2(width * 0.92f, height);
            text.GetComponent<Renderer>().shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            return text;
        }

        /// <summary>
        /// Roughly where the board will be, from the plan alone. The
        /// real one is measured off the stone, but a trigger volume
        /// and a first camera guess are needed before any stone
        /// exists, and at a metre's distance the difference does not
        /// show.
        /// </summary>
        public static Vector3 GetNominalSeat(
            CemeteryGravediggingPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Vector3 face = plan.Heading * new Vector3(
                0f,
                0f,
                -CemeteryPlaqueReadInteraction.FaceOffsetMeters);
            return new Vector3(
                plan.Ground.x + face.x,
                plan.GroundTopY + NominalSeatHeightMeters,
                plan.Ground.z + face.z);
        }

        private static bool TryMeasure(
            Transform stone,
            out Bounds bounds)
        {
            Renderer[] renderers =
                stone.GetComponentsInChildren<Renderer>(true);
            bounds = default;
            bool any = false;
            for (int index = 0; index < renderers.Length; index++)
            {
                // The plate itself must never be measured into the
                // stone it is being fitted to.
                if (renderers[index]
                    .GetComponentInParent<CemeteryPlaqueSurface>() !=
                    null)
                {
                    continue;
                }

                if (!any)
                {
                    bounds = renderers[index].bounds;
                    any = true;
                    continue;
                }

                bounds.Encapsulate(renderers[index].bounds);
            }

            return any;
        }
    }
}
