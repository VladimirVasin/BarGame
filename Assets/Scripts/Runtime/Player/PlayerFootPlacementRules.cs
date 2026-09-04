using UnityEngine;

namespace BarPromenade
{
    /// <summary>What the two probes under one boot said about the ground.</summary>
    public enum FootSurfaceKind
    {
        /// <summary>No probe hit; the foot keeps the clip's own height.</summary>
        None = 0,

        /// <summary>Heel and toe within a couple of millimetres: a floor.</summary>
        Flat,

        /// <summary>
        /// A gentle continuous slope — a hidden stair ramp, a village path —
        /// the sole tilts to it.
        /// </summary>
        Ramp,

        /// <summary>
        /// A step too sharp to be a slope — a kerb, a tread nosing — the
        /// sole stays flat on the higher of the two hits.
        /// </summary>
        Edge
    }

    /// <summary>
    /// The pure arithmetic of the foot layer: which boot the clip has on
    /// the ground right now, where each sole should be given what the
    /// probes found under it, how far the pelvis follows the lower foot,
    /// and how much of the solved pose may show through the clip.
    ///
    /// No Unity objects, so every rule is provable in EditMode; the
    /// presentation only feeds it world heights and reads back targets.
    /// </summary>
    public static class PlayerFootPlacementRules
    {
        /// <summary>
        /// The pelvis may drop this far below the clip's pelvis so the
        /// lower leg reaches a tread below the capsule.
        /// </summary>
        public const float DefaultPelvisMinimumDrop = -0.35f;

        /// <summary>
        /// The pelvis may rise this far when both feet find higher ground
        /// than the clip gave them.
        /// </summary>
        public const float DefaultPelvisMaximumLift = 0.12f;

        /// <summary>Fraction of the leg length a foot target may reach.</summary>
        public const float DefaultReachFraction =
            LimbTwoBoneIk.DefaultReachFraction;

        /// <summary>Heel-to-toe slopes at or under this angle are ramps.</summary>
        public const float DefaultRampLimitDegrees = 20f;

        /// <summary>Heel-to-toe height differences under this are floors.</summary>
        public const float FlatToleranceMetres = 0.002f;

        /// <summary>
        /// A planted foot's target height may move this fast; a swinging
        /// foot's target is not limited at all, otherwise a <c>0.10 m</c>
        /// tread drop lags ten frames behind the boot.
        ///
        /// The rate has to clear one riser inside one tread. The walk clip
        /// runs in place, so a stance boot slides across the stairwell's
        /// <c>0.24 m</c> treads with the capsule — about eleven of them a
        /// second at walking pace — and each nosing it crosses moves the
        /// surface under it by the full <c>0.10 m</c> rise. At <c>0.6</c>
        /// that took <c>0.167 s</c> against a tread every <c>0.092 s</c>,
        /// so the sole was never out of the step: measured <c>9 cm</c> deep
        /// in a tread climbing the lower flight. This clears a riser in
        /// <c>0.083 s</c> and still filters the millimetre flicker at a
        /// nosing that the smoothing is there for.
        /// </summary>
        public const float DefaultPlantedTargetRateMetresPerSecond = 1.2f;

        /// <summary>
        /// Splits one locomotion cycle into a plant weight per foot.
        ///
        /// Walk contacts the LEFT heel at cycle <c>0</c> and the right at
        /// <c>0.5</c> (<c>tools/player_3d_model_common.py</c> authors the
        /// clip as <c>walk_left_contact, ..., walk_right_contact, ...</c>);
        /// Run keeps the same order with a short flight near <c>0.375</c>
        /// and <c>0.875</c>. <c>max(left, right)</c> reproduces the scalar
        /// <c>SampleFootPlant</c> curve the contact shadow already reads.
        /// </summary>
        public static void FootPlantAmounts(
            float cycle,
            bool runLandmarks,
            float minimumPlant,
            out float left,
            out float right)
        {
            float wrapped = Mathf.Repeat(cycle, 1f);
            float minimum = Mathf.Clamp01(minimumPlant);
            float leftPlanted;
            float rightPlanted;
            if (runLandmarks)
            {
                leftPlanted = RunHalfCyclePlant(wrapped);
                rightPlanted = RunHalfCyclePlant(
                    Mathf.Repeat(wrapped + 0.5f, 1f));
            }
            else
            {
                float cosine = Mathf.Cos(wrapped * Mathf.PI * 2f);
                leftPlanted = Mathf.Max(0f, cosine);
                rightPlanted = Mathf.Max(0f, -cosine);
            }

            left = Mathf.Lerp(minimum, 1f, leftPlanted);
            right = Mathf.Lerp(minimum, 1f, rightPlanted);
        }

        /// <summary>The scalar plant the contact shadow and gait consumers read.</summary>
        public static float CombinedPlant(float left, float right)
        {
            return Mathf.Max(left, right);
        }

        /// <summary>
        /// How high the authored clip holds this sole above the clip's own
        /// ground plane: zero while planted, a few centimetres through a
        /// walk swing, more through the run flight. Preserved on top of
        /// whatever surface the probe found so a lifted boot stays lifted.
        /// </summary>
        public static float ClipLift(float clipSoleY, float clipGroundY)
        {
            return Mathf.Max(0f, clipSoleY - clipGroundY);
        }

        /// <summary>Where the sole should sit over a probed surface.</summary>
        public static float TargetSoleHeight(
            float surfaceY,
            float soleClearance,
            float clipLift)
        {
            return surfaceY + soleClearance + clipLift;
        }

        /// <summary>
        /// The surface height one foot stands on given its two probe hits.
        /// Over an edge a planted boot rests on the higher hit (the tread,
        /// not the riser it would otherwise clip into); a swinging boot
        /// follows the heel so it can arc over the edge.
        /// </summary>
        public static float SupportHeight(
            FootSurfaceKind kind,
            float heelY,
            float toeY,
            float plant)
        {
            if (kind == FootSurfaceKind.Edge && plant > 0.5f)
            {
                return Mathf.Max(heelY, toeY);
            }

            return heelY;
        }

        /// <summary>
        /// Floor, ramp or edge from the heel and toe hits
        /// <paramref name="toeDistance"/> apart.
        /// </summary>
        public static FootSurfaceKind Classify(
            float heelY,
            float toeY,
            float toeDistance,
            float rampLimitDegrees)
        {
            float rise = Mathf.Abs(heelY - toeY);
            if (rise <= FlatToleranceMetres)
            {
                return FootSurfaceKind.Flat;
            }

            float limit = Mathf.Tan(
                             Mathf.Clamp(rampLimitDegrees, 0f, 89f) *
                             Mathf.Deg2Rad) *
                         Mathf.Max(0.0001f, toeDistance);
            return rise <= limit
                ? FootSurfaceKind.Ramp
                : FootSurfaceKind.Edge;
        }

        /// <summary>
        /// How far the pelvis follows the feet: the lower foot wins, a
        /// downward correction is released with the Run blend so the
        /// authored flight survives, and both directions are bounded.
        /// </summary>
        public static float PelvisDrop(
            float leftDelta,
            float rightDelta,
            float runBlend,
            bool hasRunClip,
            float minimumDrop = DefaultPelvisMinimumDrop,
            float maximumLift = DefaultPelvisMaximumLift)
        {
            float delta = Mathf.Min(leftDelta, rightDelta);
            if (delta < 0f && hasRunClip)
            {
                delta *= 1f - Mathf.Clamp01(runBlend);
            }

            return Mathf.Clamp(delta, minimumDrop, maximumLift);
        }

        /// <summary>
        /// How far the pelvis follows the walkable ground the ACTOR stands
        /// on — the surface under his capsule — measured against the clip's
        /// own ground plane.
        ///
        /// On a floor this is the same number <see cref="PelvisDrop"/>
        /// takes from the boots: both of them probe the very surface the
        /// capsule rests on, so every delta is the one below. A stair
        /// flight is where they part. The controller walks one continuous
        /// hidden ramp while the flat-authored stride straddles two or
        /// three risers, so the leading boot's tread sits a quarter of a
        /// metre under the trailing one; following the lower boot drops the
        /// hips by that whole difference every step and the trailing knee
        /// folds double to keep up. The capsule's own ground already
        /// carries the descent — the pelvis only has to keep its height
        /// above it.
        /// </summary>
        public static float PelvisPlaneDelta(
            float groundY,
            float soleClearance,
            float referenceSole)
        {
            return groundY + soleClearance - referenceSole;
        }

        /// <summary>
        /// How far a hip must come down for a foot to reach a target
        /// <paramref name="planarDistance"/> away and
        /// <paramref name="verticalDrop"/> below it on a leg of
        /// <paramref name="reach"/>: nothing while the target is inside the
        /// leg's cone, and never negative — a leg with slack does not pull
        /// the body up after it.
        /// </summary>
        public static float ReachShortfall(
            float planarDistance,
            float verticalDrop,
            float reach)
        {
            if (!(reach > 0f) || planarDistance >= reach)
            {
                return 0f;
            }

            float allowed = Mathf.Sqrt(
                Mathf.Max(
                    0f,
                    (reach * reach) - (planarDistance * planarDistance)));
            return Mathf.Max(0f, verticalDrop - allowed);
        }

        /// <summary>
        /// How much of a boot's own reach the hips answer for: the leg
        /// carrying the weight in full, the leg still swinging not at all,
        /// and both while he stands with his weight shared between them.
        ///
        /// The plants a walk hands out never fall to zero (the clip's own
        /// floor holds them well above it), so the stance foot is the one
        /// planted HARDER, not the one over some absolute threshold.
        /// Without that, a boot still swinging down a flight drags the
        /// whole body a riser ahead of its own footfall.
        ///
        /// Equal plants mean the caller is not telling the two boots apart
        /// at all — the backpedal and turn-in-place clips hand both feet
        /// one scalar because they do not share Walk's contact order, and
        /// so does every city pedestrian. Full and equal is a real stand
        /// on both feet and answers for both; anything less is a gait
        /// whose swing foot cannot be identified, and a body must not come
        /// down for a boot that may be in the air.
        /// </summary>
        public static float StanceWeight(
            float plant,
            float lowestPlant,
            float highestPlant)
        {
            float span = highestPlant - lowestPlant;
            if (span <= 0.0001f)
            {
                return lowestPlant >= 0.999f ? 1f : 0f;
            }

            return Mathf.Clamp01((plant - lowestPlant) / span);
        }

        /// <summary>
        /// Keeps a foot target inside the leg's reach measured from the
        /// hip, so the solver never straightens the knee into its lock.
        /// </summary>
        public static Vector3 ClampReach(
            Vector3 hip,
            Vector3 target,
            float legLength,
            float reachFraction = DefaultReachFraction)
        {
            return LimbTwoBoneIk.ClampReach(
                hip,
                legLength,
                target,
                reachFraction);
        }

        /// <summary>
        /// How much of the solved leg shows: the layer's own blend-in,
        /// released through the Run flight for a foot that is not planted.
        /// </summary>
        public static float IkWeight(
            float ikBlend,
            float runBlend,
            float plant)
        {
            return Mathf.Clamp01(ikBlend) *
                   (1f - Mathf.Clamp01(runBlend) *
                    (1f - Mathf.Clamp01(plant)));
        }

        /// <summary>
        /// The most a foot's target height may move this frame: bounded
        /// only while the foot is planted, unbounded while it swings.
        /// </summary>
        public static float MaximumTargetStep(
            float plant,
            float deltaTime,
            float plantedRate = DefaultPlantedTargetRateMetresPerSecond)
        {
            return plant >= 0.5f
                ? Mathf.Max(0f, plantedRate) * Mathf.Max(0f, deltaTime)
                : float.PositiveInfinity;
        }

        private static float RunHalfCyclePlant(float cycle)
        {
            // Run contacts at 0/.5 and reaches its short flight near
            // .375/.875. This foot owns the first half of the cycle: it is
            // down at 0, leaves the ground three quarters of the way
            // through its half, and lands again at the end of the OTHER
            // half, which is where the scalar curve rises back to one.
            float halfCycle = Mathf.Repeat(cycle, 0.5f) * 2f;
            bool ownHalf = cycle < 0.5f;
            if (ownHalf)
            {
                return halfCycle <= 0.75f
                    ? 1f - (halfCycle / 0.75f)
                    : 0f;
            }

            return halfCycle <= 0.75f
                ? 0f
                : (halfCycle - 0.75f) / 0.25f;
        }
    }
}
