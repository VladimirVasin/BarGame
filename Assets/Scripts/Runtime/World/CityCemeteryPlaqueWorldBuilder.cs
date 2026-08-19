using System;
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

        internal static readonly Color Bezel =
            new Color(0.29f, 0.31f, 0.24f);

        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");

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

            root.transform.SetPositionAndRotation(
                new Vector3(face.x, seat, face.z),
                Quaternion.LookRotation(-outward, Vector3.up));

            RuntimePrimitiveFactory.CreateBox(
                BezelName,
                root.transform,
                new Vector3(0f, 0f, BezelThicknessMeters * 0.5f),
                new Vector3(
                    width + BezelMeters,
                    height + BezelMeters,
                    BezelThicknessMeters),
                Bezel,
                false);
            BuildPlate(root.transform, width, height);
            root.AddComponent<CemeteryPlaqueSurface>().Refresh();
            return root;
        }

        /// <summary>
        /// The plate: one quad facing the reader, with its own UVs so
        /// the stamping runs the right way up and the right way round.
        /// </summary>
        private static void BuildPlate(
            Transform parent,
            float width,
            float height)
        {
            var plate = new GameObject(PlateName);
            plate.transform.SetParent(parent, false);
            plate.transform.localPosition =
                new Vector3(0f, 0f, -0.001f);

            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;
            var mesh = new Mesh { name = PlateName };
            mesh.SetVertices(new[]
            {
                new Vector3(-halfWidth, -halfHeight, 0f),
                new Vector3(-halfWidth, halfHeight, 0f),
                new Vector3(halfWidth, halfHeight, 0f),
                new Vector3(halfWidth, -halfHeight, 0f)
            });
            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            });
            mesh.SetNormals(new[]
            {
                Vector3.back,
                Vector3.back,
                Vector3.back,
                Vector3.back
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();

            plate.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = plate.AddComponent<MeshRenderer>();
            renderer.sharedMaterial =
                RuntimePrimitiveFactory.DefaultMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering
                .ShadowCastingMode.Off;
        }

        /// <summary>
        /// Hands one plate its stamped texture. Kept here so the
        /// property names live beside the material that wants them.
        /// </summary>
        internal static void ApplyPlate(
            Renderer renderer,
            Texture2D stamp)
        {
            if (renderer == null || stamp == null)
            {
                return;
            }

            renderer.sharedMaterial =
                RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, stamp);
            properties.SetColor(BaseColorId, Color.white);
            properties.SetColor(ColorId, Color.white);
            renderer.SetPropertyBlock(properties);
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
