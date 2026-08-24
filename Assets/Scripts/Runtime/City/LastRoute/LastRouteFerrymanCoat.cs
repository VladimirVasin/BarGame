using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The Ferryman's coat skirt, as real PhysX cloth.
    ///
    /// The long skirt is deliberately not drawn in Blender. A rigid slab
    /// below the waist is the one part of a Charon silhouette that always
    /// looks wrong: it either clips through whatever he is sitting on or it
    /// stands off it like a bell. So the art stops at a short hem stub and
    /// this replaces it at runtime with a `ClothPanelFactory` panel, cut to
    /// the stub's own measured width so a coat redrawn in Blender still
    /// gets a skirt that fits it.
    ///
    /// Two decisions are load-bearing and both are about avoiding cloth
    /// that intersects things:
    ///
    /// The panel is a child of the runtime ROOT at scale one and is
    /// teleported onto the hem anchor every frame, never parented to it.
    /// The imported bone hierarchy carries Unity's 100x FBX scale, and a
    /// Cloth inside a 100x transform simulates as a hundred-metre sheet.
    /// This is the coin's rule for the same reason and with the same
    /// benefit: no inverse scale to get wrong.
    ///
    /// And it is TWO narrow flaps beside his hips rather than one skirt in
    /// front of him. The single front panel was tried first and it was
    /// wrong in a way only a render showed: a seated man's thighs run
    /// forward, so a sheet hung from his waist falls past them and reads as
    /// a signboard propped against his shins, with daylight between it and
    /// the man. Two side flaps have nowhere to go wrong, because the outer
    /// side of each thigh is open air - there is nothing there for cloth
    /// without colliders to sink into, whether he is sitting on a bonnet
    /// with the bodywork behind him or standing up. They also happen to be
    /// what a long coat actually does when its owner sits down: it parts
    /// over the knees and hangs at the sides.
    ///
    /// Each flap is turned so its width runs FORE AND AFT rather than
    /// across him, which is what makes it a coat panel rather than a
    /// curtain, and the free travel is kept short so neither finds the
    /// bodywork on a swing.
    /// </summary>
    [DefaultExecutionOrder(310)]
    [DisallowMultipleComponent]
    public sealed class LastRouteFerrymanCoat : MonoBehaviour
    {
        /// <summary>How far out from the hem anchor each flap hangs, as a
        /// fraction of the measured hem width - just outside the hip, so
        /// the flap brushes the leg rather than passing through it.
        /// </summary>
        public const float LateralOffsetFraction = 0.46f;

        /// <summary>And a little forward, so the flaps hang beside the
        /// thighs rather than beside the backside.</summary>
        public const float ForwardStandoffMeters = 0.06f;

        /// <summary>Below the knee, which is what makes it a coat rather
        /// than a jacket.</summary>
        public const float SkirtDropMeters = 0.48f;

        /// <summary>
        /// Each flap's fore-and-aft width, as a fraction of the measured
        /// hem width. Under one on purpose: two panels the full width of
        /// his waist would meet in front of him and be the slab this
        /// design exists to avoid.
        /// </summary>
        public const float FlapWidthFraction = 0.62f;

        /// <summary>Fraction of the drop a free particle may wander. Well
        /// under the factory default for the reason in the class
        /// docstring.</summary>
        public const float FreeTravelFraction = 0.16f;

        private static readonly Color FallbackCoatColor =
            new Color(0.13f, 0.13f, 0.15f, 1f);

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");

        private Transform hemAnchor;
        private Transform facingReference;
        private Transform leftFlap;
        private Transform rightFlap;
        private float lateralOffsetMeters;

        public bool IsInitialized { get; private set; }

        /// <summary>The left-hand flap, and the one a test reaches for
        /// when it wants to prove the coat is real cloth.</summary>
        public Transform Panel => leftFlap;

        public Transform LeftFlap => leftFlap;
        public Transform RightFlap => rightFlap;

        public void Initialize(
            LastRouteFerrymanRigAnchors anchors,
            Transform ferrymanFacing)
        {
            if (anchors == null)
            {
                throw new ArgumentNullException(nameof(anchors));
            }

            if (ferrymanFacing == null)
            {
                throw new ArgumentNullException(nameof(ferrymanFacing));
            }

            if (anchors.CoatHemAnchor == null)
            {
                throw new InvalidOperationException(
                    "The Ferryman prefab needs a bound coat hem anchor " +
                    "before his coat can be hung from it.");
            }

            hemAnchor = anchors.CoatHemAnchor;
            facingReference = ferrymanFacing;

            float hemWidth = Mathf.Max(0.18f, anchors.CoatHemSize.x);
            lateralOffsetMeters = hemWidth * LateralOffsetFraction;
            Color color = ResolveCoatColor(anchors.CoatHemRenderer);
            leftFlap = CreateFlap(
                "Ferryman Coat Flap.L",
                hemWidth * FlapWidthFraction,
                color);
            rightFlap = CreateFlap(
                "Ferryman Coat Flap.R",
                hemWidth * FlapWidthFraction,
                color);

            // The stub and the skirt would otherwise be drawn one inside
            // the other. The stub's job was to say where the coat ends and
            // how wide it is; it has now said it.
            if (anchors.CoatHemRenderer != null)
            {
                anchors.CoatHemRenderer.enabled = false;
            }

            IsInitialized = true;
            WritePose();
        }

        private Transform CreateFlap(string name, float width, Color color)
        {
            GameObject cloth = ClothPanelFactory.CreateHangingRag(
                name,
                transform,
                Vector3.zero,
                0f,
                width,
                SkirtDropMeters,
                color,
                tornVariant: 0,
                columns: 4,
                rows: 6);
            TightenTravel(cloth.GetComponent<Cloth>());
            return cloth.transform;
        }

        /// <summary>
        /// The colour actually on screen, read back off the hem stub rather
        /// than re-derived. The palette variant has already been applied by
        /// the time this runs, so this cannot disagree with the coat above
        /// it the way a second copy of the palette logic could.
        /// </summary>
        private static Color ResolveCoatColor(Renderer hemRenderer)
        {
            if (hemRenderer == null)
            {
                return FallbackCoatColor;
            }

            var properties = new MaterialPropertyBlock();
            hemRenderer.GetPropertyBlock(properties);
            Color color = properties.GetColor(BaseColorId);

            // An untouched block reads back as clear black, which is not a
            // colour anybody chose.
            return color.a > 0.01f ? color : FallbackCoatColor;
        }

        private static void TightenTravel(Cloth cloth)
        {
            if (cloth == null)
            {
                return;
            }

            ClothSkinningCoefficient[] coefficients = cloth.coefficients;
            float travel = SkirtDropMeters * FreeTravelFraction;
            for (int index = 0; index < coefficients.Length; index++)
            {
                if (coefficients[index].maxDistance > 0f)
                {
                    coefficients[index].maxDistance = travel;
                }
            }

            cloth.coefficients = coefficients;

            // Oilskin, not silk: stiff, heavily damped, and it does not
            // take the wind. He is not standing in one.
            cloth.stretchingStiffness = 0.95f;
            cloth.bendingStiffness = 0.6f;
            cloth.damping = 0.65f;
            cloth.friction = 0.4f;

            // It does react to him getting into the car, which is the one
            // time he moves, but modestly - a coat that snaps when its
            // owner sits down reads as a physics bug rather than as cloth.
            cloth.worldVelocityScale = 0.25f;
            cloth.worldAccelerationScale = 0.15f;
        }

        private void LateUpdate()
        {
            if (!IsInitialized)
            {
                return;
            }

            WritePose();
        }

        private void WritePose()
        {
            Vector3 forward = facingReference.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.000001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 hip = hemAnchor.position +
                          forward * ForwardStandoffMeters;

            // Each flap's face is turned to look ACROSS him, which puts its
            // width fore and aft: a panel down the outside of a thigh
            // rather than a curtain hung across his front.
            leftFlap.SetPositionAndRotation(
                hip - right * lateralOffsetMeters,
                Quaternion.LookRotation(-right, Vector3.up));
            rightFlap.SetPositionAndRotation(
                hip + right * lateralOffsetMeters,
                Quaternion.LookRotation(right, Vector3.up));
        }
    }
}
