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
        /// Where up the stone the board goes: the upper part of the
        /// face, which is where a mason puts a plate and where an eye
        /// looks for one. The search starts high and only comes down
        /// as far as the middle — a board at the foot of a monument
        /// reads as something that fell off it.
        /// </summary>
        public const float TopSeatFraction = 0.74f;
        public const float LowestSeatFraction = 0.44f;
        public const int SeatSteps = 9;

        /// <summary>
        /// Narrower than this is not a face, it is an edge. It is
        /// deliberately small: the board is cut down to whatever the
        /// stone offers rather than the stone being searched for
        /// somewhere a full-width board would fit, which is what
        /// drove it to the plinth.
        /// </summary>
        public const float MinimumFaceMeters = 0.10f;

        /// <summary>Below this a plate is a badge, not a plaque.
        /// </summary>
        public const float MinimumWidthMeters = 0.13f;

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

            Vector3 outward = plan.Heading * Vector3.back;
            if (!TryFindSolidFace(
                    stone,
                    bounds,
                    outward,
                    out Vector3 face,
                    out float span,
                    out float seat))
            {
                return null;
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(stone, true);

            float width = Mathf.Clamp(
                span * MaximumFaceShare,
                MinimumWidthMeters,
                WidthMeters);
            float height = width * (HeightMeters / WidthMeters);

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
            var surface = root.AddComponent<CemeteryPlaqueSurface>();
            // The board reads its own grave's line and no other one's:
            // there are several finished stones in the yard now, each
            // with a different stranger under it.
            surface.Initialize(plan.Plot.StableId);
            BuildPlate(root.transform, width, height);
            surface.Refresh();
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
                CemeteryPlaqueSurface.NameMinimumSize);
            TMP_Text years = CreateLine(
                parent,
                "Plaque Years",
                font,
                width,
                height * 0.22f,
                height * 0.04f,
                CemeteryPlaqueSurface.YearsSize,
                CemeteryPlaqueSurface.YearsSize * 0.55f);
            TMP_Text epitaph = CreateLine(
                parent,
                "Plaque Epitaph",
                font,
                width,
                height * 0.40f,
                -height * 0.24f,
                CemeteryPlaqueSurface.EpitaphSize,
                CemeteryPlaqueSurface.EpitaphMinimumSize);
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
            float minimumSize)
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
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Truncate;
            // Every line shrinks to fit rather than being dropped:
            // `Truncate` on a line that does not fit renders nothing
            // at all, which is a blank plate and looks like a bug.
            text.enableAutoSizing = true;
            text.fontSizeMin = minimumSize;
            text.fontSizeMax = size;

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

        /// <summary>
        /// Finds real stone to screw the board to.
        ///
        /// The monument is one of four silhouettes and only two of
        /// them are slabs. An Orthodox cross is mostly the air between
        /// its arms, and an obelisk narrows as it climbs, so the front
        /// face of the whole bounding box is not a surface — a board
        /// fixed to it hangs in a gap.
        ///
        /// It works on triangles rather than on vertices, and that is
        /// the whole trick. These monuments are combined boxes: a
        /// stele is forty-eight vertices, all of them at corners, so
        /// sampling points inside a horizontal band finds nothing at
        /// all at any height between them. A triangle, on the other
        /// hand, knows it spans the band it crosses.
        /// </summary>
        private static bool TryFindSolidFace(
            Transform stone,
            Bounds bounds,
            Vector3 outward,
            out Vector3 face,
            out float span,
            out float seat)
        {
            face = bounds.center;
            span = 0f;
            seat = bounds.center.y;

            var triangles =
                new System.Collections.Generic.List<Vector3[]>(256);
            MeshFilter[] filters =
                stone.GetComponentsInChildren<MeshFilter>(true);
            for (int index = 0; index < filters.Length; index++)
            {
                if (filters[index]
                        .GetComponentInParent<CemeteryPlaqueSurface>()
                    != null)
                {
                    continue;
                }

                Mesh mesh = filters[index].sharedMesh;
                if (mesh == null || !mesh.isReadable)
                {
                    continue;
                }

                Transform owner = filters[index].transform;
                Vector3[] vertices = mesh.vertices;
                int[] indices = mesh.triangles;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    triangles.Add(new[]
                    {
                        owner.TransformPoint(vertices[indices[i]]),
                        owner.TransformPoint(vertices[indices[i + 1]]),
                        owner.TransformPoint(vertices[indices[i + 2]])
                    });
                }
            }

            if (triangles.Count == 0)
            {
                return false;
            }

            // Across the face, horizontally: the axis the board's own
            // width runs along.
            Vector3 across = Vector3.Cross(Vector3.up, outward);
            float bottom = bounds.min.y;
            float total = Mathf.Max(bounds.size.y, 0.0001f);

            float bestSpan = 0f;
            Vector3 bestFace = bounds.center;
            float bestSeat = bounds.center.y;

            for (int step = 0; step < SeatSteps; step++)
            {
                float fraction = Mathf.Lerp(
                    TopSeatFraction,
                    LowestSeatFraction,
                    step / (float)(SeatSteps - 1));
                float y = bottom + (total * fraction);
                float low = float.MaxValue;
                float high = float.MinValue;
                float front = float.MinValue;
                for (int index = 0; index < triangles.Count; index++)
                {
                    Vector3[] corners = triangles[index];
                    float lowest = Mathf.Min(
                        corners[0].y,
                        Mathf.Min(corners[1].y, corners[2].y));
                    float highest = Mathf.Max(
                        corners[0].y,
                        Mathf.Max(corners[1].y, corners[2].y));
                    if (y < lowest || y > highest)
                    {
                        continue;
                    }

                    for (int c = 0; c < 3; c++)
                    {
                        Vector3 corner = corners[c];
                        float sideways =
                            (corner.x * across.x) +
                            (corner.z * across.z);
                        low = Mathf.Min(low, sideways);
                        high = Mathf.Max(high, sideways);
                        front = Mathf.Max(
                            front,
                            (corner.x * outward.x) +
                            (corner.z * outward.z));
                    }
                }

                if (low > high)
                {
                    continue;
                }

                float measured = high - low;
                float middle = (low + high) * 0.5f;
                Vector3 seated = (across * middle) +
                                 (outward * (front + ProudMeters));
                if (measured > bestSpan)
                {
                    bestSpan = measured;
                    bestFace = seated;
                    bestSeat = y;
                }

                // Highest band that can carry a board wins, and the
                // board is cut to it rather than the other way round.
                if (measured >= MinimumFaceMeters)
                {
                    span = measured;
                    seat = y;
                    face = seated;
                    return true;
                }
            }

            span = bestSpan;
            seat = bestSeat;
            face = bestFace;
            return bestSpan > MinimumWidthMeters * 0.5f;
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
