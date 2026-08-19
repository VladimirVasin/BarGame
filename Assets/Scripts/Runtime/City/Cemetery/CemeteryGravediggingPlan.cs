using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one job the cemetery watchman has to give: which vacant
    /// plot he sends the hero to, and the exact hole that comes out of
    /// it. Pure data derived from the cemetery plan, so a seed always
    /// offers the same grave whether or not anybody ever accepts it.
    ///
    /// The hole is axis-aligned rather than turned with the plot's own
    /// few degrees of jitter: the ground it is cut out of is a terrain
    /// skin subtracted by axis-aligned rectangles, and a grave that
    /// agrees with its own hole matters more than four degrees nobody
    /// can see.
    /// </summary>
    public sealed class CemeteryGravediggingPlan
    {
        /// <summary>Dug to the shoulder: deep enough to read as a
        /// grave, shallow enough to climb back out of.</summary>
        public const float PitDepthMeters = 1.60f;

        /// <summary>Mouth of the hole, along the plot's own heading
        /// and across it. A coffin's envelope with working room.
        /// </summary>
        public const float PitLengthMeters = 2.30f;
        public const float PitWidthMeters = 1.05f;

        /// <summary>How much earth the collar walls lining the hole
        /// keep between the pit face and the cut terrain edge.
        /// </summary>
        public const float PitWallThicknessMeters = 0.30f;

        /// <summary>The spoil heap: everything that came out of the
        /// hole, piled along one side within the plot's own envelope.
        /// </summary>
        public const float SpoilLengthMeters = 1.90f;
        public const float SpoilWidthMeters = 0.85f;

        /// <summary>
        /// Where the work lamp is set down: on the collar at the head
        /// end of the hole, on its right-hand corner. It stands on
        /// refilled earth rather than over the void, and the offsets
        /// put it on the middle of the collar ring, so the whole
        /// fixture — a hand's span across — clears the mouth even
        /// turned off the axis.
        /// </summary>
        public const float LampLongOffsetMeters =
            PitLengthMeters * 0.5f + PitWallThicknessMeters * 0.5f;
        public const float LampLateralOffsetMeters =
            PitWidthMeters * 0.5f + PitWallThicknessMeters * 0.5f;

        /// <summary>
        /// Where the coffin waits and where the spade stands while
        /// they are not in the hero's hands: past the foot end of the
        /// grave, laid across it.
        ///
        /// It is the only place on the plot they fit. The heap owns
        /// one flank, the digger and his camera own the other, and the
        /// head end carries the lamp; what is left is the ground below
        /// the feet, and there is a metre and a half of it before the
        /// next row of graves begins.
        /// </summary>
        public const float KitLongOffsetMeters =
            PitLengthMeters * 0.5f +
            PitWallThicknessMeters +
            0.42f;

        /// <summary>
        /// Half the length of the coffin as it lies there, across the
        /// grave. The spade has to stand past the end of it: anything
        /// nearer the middle puts the handle through the lid, which is
        /// exactly what a first pass at these numbers did.
        /// </summary>
        public const float CoffinHalfSpanMeters = 0.98f;
        public const float SpadeLongOffsetMeters =
            KitLongOffsetMeters - 0.16f;
        public const float SpadeLateralOffsetMeters =
            CoffinHalfSpanMeters + 0.30f;

        /// <summary>
        /// Where the monument lies on its back until it is stood up:
        /// past the head of the grave, on the flank away from the work
        /// lamp. It waits within sight of the spot it will occupy,
        /// because a stone dragged from somewhere else at the last
        /// moment would be a stone the player never saw arrive.
        /// </summary>
        public const float StoneRestLongOffsetMeters =
            PitLengthMeters * 0.5f +
            PitWallThicknessMeters +
            0.46f;
        public const float StoneRestLateralOffsetMeters = 0.62f;

        private static readonly CemeteryGravediggingPlan AbsentPlan =
            new CemeteryGravediggingPlan();

        private CemeteryGravediggingPlan()
        {
            IsPresent = false;
        }

        private CemeteryGravediggingPlan(
            CityCemeteryPlotDescriptor plot,
            float headingYawDegrees,
            float groundTopY)
        {
            IsPresent = true;
            Plot = plot;
            Heading = Quaternion.Euler(0f, headingYawDegrees, 0f);
            // The mouth runs along world X exactly when the snapped
            // heading points that way, so the hole and everything the
            // digging later stands over it can never disagree.
            bool runsAlongX =
                Mathf.Abs(
                    Mathf.Abs(Mathf.DeltaAngle(headingYawDegrees, 0f)) -
                    90f) < 45f;
            RunsAlongX = runsAlongX;
            GroundTopY = groundTopY;
            Ground = new Vector3(
                plot.Ground.x,
                groundTopY,
                plot.Ground.z);
            PitMouth = CreateCenteredRect(
                Ground,
                runsAlongX ? PitLengthMeters : PitWidthMeters,
                runsAlongX ? PitWidthMeters : PitLengthMeters);
            // The heap sits on the side of the hole away from the
            // headstone end, clear of the collar and still inside the
            // plot the watchman signed over.
            float spoilOffset =
                PitWidthMeters * 0.5f + SpoilWidthMeters * 0.5f + 0.12f;
            Vector3 spoilCenter = runsAlongX
                ? new Vector3(Ground.x, groundTopY, Ground.z + spoilOffset)
                : new Vector3(Ground.x + spoilOffset, groundTopY, Ground.z);
            SpoilCenter = spoilCenter;
            SpoilFootprint = CreateCenteredRect(
                spoilCenter,
                runsAlongX ? SpoilLengthMeters : SpoilWidthMeters,
                runsAlongX ? SpoilWidthMeters : SpoilLengthMeters);
            Vector3 lampOffset = Heading * new Vector3(
                LampLateralOffsetMeters,
                0f,
                LampLongOffsetMeters);
            LampGround = new Vector3(
                Ground.x + lampOffset.x,
                groundTopY,
                Ground.z + lampOffset.z);

            // Which stone ends up over him is fixed by the plot rather
            // than by when the hero gets round to filling the hole in:
            // the same seed always raises the same monument here. Only
            // the four single-grave silhouettes are eligible — a family
            // vault is not what one man digs in an afternoon, and an
            // overgrown slab cannot be an hour old.
            uint stoneHash = StableHash(plot.StableId);
            // Both are set down by hand rather than installed, so both
            // sit a few degrees off true — the same few degrees every
            // time, because it is the plot that decides and not the
            // moment the hero happens to arrive.
            float coffinSkew =
                ((stoneHash >> 3) % 13u) - 6f;
            float spadeSkew =
                ((stoneHash >> 11) % 19u) - 9f;
            Vector3 coffinOffset = Heading * new Vector3(
                0f,
                0f,
                -KitLongOffsetMeters);
            CoffinRestGround = new Vector3(
                Ground.x + coffinOffset.x,
                groundTopY,
                Ground.z + coffinOffset.z);
            CoffinRestYawDegrees =
                headingYawDegrees + 90f + coffinSkew;
            Vector3 spadeOffset = Heading * new Vector3(
                SpadeLateralOffsetMeters,
                0f,
                -SpadeLongOffsetMeters);
            SpadeRestGround = new Vector3(
                Ground.x + spadeOffset.x,
                groundTopY,
                Ground.z + spadeOffset.z);
            SpadeRestYawDegrees = headingYawDegrees + spadeSkew;
            // The lamp sits at the head end on the positive lateral
            // side, so the stone lies out on the other one.
            Vector3 stoneOffset = Heading * new Vector3(
                -StoneRestLateralOffsetMeters,
                0f,
                StoneRestLongOffsetMeters);
            StoneRestGround = new Vector3(
                Ground.x + stoneOffset.x,
                groundTopY,
                Ground.z + stoneOffset.z);
            StoneRestYawDegrees =
                headingYawDegrees + 90f + (coffinSkew * 0.5f);

            Monument = (CityCemeteryGraveVariant)(stoneHash % 4u);
            MonumentStyle = ((stoneHash >> 8) & 1u) == 0u
                ? CityCemeteryStyle.GraniteDark
                : CityCemeteryStyle.MarbleLight;
        }

        /// <summary>False when the city has no cemetery, no lodge, or
        /// no vacant plot left to bury anybody in.</summary>
        public bool IsPresent { get; }

        /// <summary>The vacant plot the job was written against.
        /// </summary>
        public CityCemeteryPlotDescriptor Plot { get; }

        /// <summary>Ground point at the middle of the hole.</summary>
        public Vector3 Ground { get; }

        /// <summary>True when the hole's long axis runs along world X.
        /// </summary>
        public bool RunsAlongX { get; }

        /// <summary>
        /// The plot's own heading snapped to the nearest compass
        /// quarter, local +Z at the head of the grave. Everything the
        /// digging puts on this plot — the lamp, the coffin, the mound
        /// and the stone — is placed in this frame, so all of them
        /// agree with the axis-aligned hole.
        /// </summary>
        public Quaternion Heading { get; }

        /// <summary>Where the work lamp stands from the moment the
        /// job is taken until the earth goes back.</summary>
        public Vector3 LampGround { get; }

        /// <summary>Where the coffin lies waiting, on its two blocks
        /// past the foot of the grave.</summary>
        public Vector3 CoffinRestGround { get; }
        public float CoffinRestYawDegrees { get; }

        /// <summary>Where the spade stands driven into the ground
        /// between acts.</summary>
        public Vector3 SpadeRestGround { get; }
        public float SpadeRestYawDegrees { get; }

        /// <summary>Where the monument lies on its back until the
        /// hero heaves it up.</summary>
        public Vector3 StoneRestGround { get; }
        public float StoneRestYawDegrees { get; }

        /// <summary>The stone that goes up when the grave is closed.
        /// </summary>
        public CityCemeteryGraveVariant Monument { get; }

        /// <summary>The stone it is cut from. Fresh work, so never the
        /// weathered concrete of the old rows.</summary>
        public CityCemeteryStyle MonumentStyle { get; }

        public float GroundTopY { get; }

        /// <summary>The rectangle cut out of the cemetery's terrain
        /// skin — the hole's mouth, exactly.</summary>
        public Rect PitMouth { get; }

        public Vector3 SpoilCenter { get; }
        public Rect SpoilFootprint { get; }

        /// <summary>Floor level of the finished hole.</summary>
        public float PitFloorY => GroundTopY - PitDepthMeters;

        /// <summary>
        /// The whole worksite — hole, collar and spoil heap. It never
        /// leaves the plot, so digging can never disturb a neighbour.
        /// </summary>
        public Rect WorkFootprint
        {
            get
            {
                Rect collar = Expand(PitMouth, PitWallThicknessMeters);
                return Rect.MinMaxRect(
                    Mathf.Min(collar.xMin, SpoilFootprint.xMin),
                    Mathf.Min(collar.yMin, SpoilFootprint.yMin),
                    Mathf.Max(collar.xMax, SpoilFootprint.xMax),
                    Mathf.Max(collar.yMax, SpoilFootprint.yMax));
            }
        }

        /// <summary>
        /// The job nearest the watchman's own post: he is not going to
        /// walk a new man to the far fence. Ties break on the plot's
        /// stable id, so the choice never depends on list order.
        /// </summary>
        public static CemeteryGravediggingPlan Create(
            CityCemeteryPlan cemeteryPlan,
            CemeteryWatchmanPlan watchmanPlan)
        {
            if (cemeteryPlan == null ||
                watchmanPlan == null ||
                !watchmanPlan.IsPresent)
            {
                return AbsentPlan;
            }

            Vector3 post = watchmanPlan.Stance.Position;
            bool found = false;
            CityCemeteryPlotDescriptor best = default;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0;
                 index < cemeteryPlan.Plots.Count;
                 index++)
            {
                CityCemeteryPlotDescriptor plot =
                    cemeteryPlan.Plots[index];
                if (!plot.IsVacant)
                {
                    continue;
                }

                float distance = new Vector2(
                    plot.Ground.x - post.x,
                    plot.Ground.z - post.z).sqrMagnitude;
                const float tieEpsilon = 0.0001f;
                bool nearer = !found ||
                              distance < bestDistance - tieEpsilon;
                bool tiedAndEarlier =
                    found &&
                    !nearer &&
                    distance <= bestDistance + tieEpsilon &&
                    string.CompareOrdinal(
                        plot.StableId,
                        best.StableId) < 0;
                if (nearer || tiedAndEarlier)
                {
                    found = true;
                    best = plot;
                    bestDistance = distance;
                }
            }

            if (!found)
            {
                return AbsentPlan;
            }

            // The plot's heading turned into the world axis it is
            // closest to: the cemetery frame only ever faces the four
            // compass directions, and the jitter is four degrees.
            Vector3 forward = best.Yaw * Vector3.forward;
            float headingYawDegrees = Mathf.Round(
                Mathf.Atan2(forward.x, forward.z) *
                Mathf.Rad2Deg / 90f) * 90f;
            return new CemeteryGravediggingPlan(
                best,
                headingYawDegrees,
                cemeteryPlan.GroundTopY);
        }

        /// <summary>
        /// The contract the digging rests on: a finite hole that fits
        /// inside its own plot with its collar and heap, so cutting it
        /// out of the terrain can never reach a neighbouring grave.
        /// </summary>
        public static void ValidateOrThrow(CemeteryGravediggingPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!plan.IsPresent)
            {
                return;
            }

            if (plan.Plot.State != CityCemeteryPlotState.Vacant)
            {
                throw new InvalidOperationException(
                    "A gravedigging job must be written against a " +
                    "vacant plot.");
            }

            if (plan.PitMouth.width <= 0f ||
                plan.PitMouth.height <= 0f ||
                float.IsNaN(plan.GroundTopY) ||
                float.IsInfinity(plan.GroundTopY))
            {
                throw new InvalidOperationException(
                    "A gravedigging job requires a finite positive " +
                    "hole.");
            }

            if (!plan.WorkFootprint.Contains(new Vector2(
                    plan.LampGround.x,
                    plan.LampGround.z)))
            {
                throw new InvalidOperationException(
                    "The gravedigger's lamp must stand on the " +
                    "worksite.");
            }

            // Neither the coffin nor the spade may be standing on the
            // hole, on its collar or in the heap: the hero has to be
            // able to work all three of those without the props being
            // in the way, and that is the whole point of setting them
            // down past the feet.
            Rect work = plan.WorkFootprint;
            var spade = new Vector2(
                plan.SpadeRestGround.x,
                plan.SpadeRestGround.z);
            if (work.Contains(new Vector2(
                    plan.CoffinRestGround.x,
                    plan.CoffinRestGround.z)) ||
                work.Contains(spade))
            {
                throw new InvalidOperationException(
                    "The gravedigger's coffin and spade must wait " +
                    "clear of the worksite.");
            }

            // And clear of each other. A spade standing inside the
            // coffin's own outline comes up through the lid.
            Vector3 apart = plan.SpadeRestGround -
                            plan.CoffinRestGround;
            // The box is laid across the grave, so its own length runs
            // along the world direction its resting yaw faces.
            Vector3 coffinLength = Quaternion.Euler(
                0f,
                plan.CoffinRestYawDegrees,
                0f) * Vector3.forward;
            float alongCoffin = Mathf.Abs(
                (apart.x * coffinLength.x) +
                (apart.z * coffinLength.z));
            if (alongCoffin < CoffinHalfSpanMeters)
            {
                throw new InvalidOperationException(
                    "The gravedigger's spade must stand past the " +
                    "end of the waiting coffin, not through it.");
            }

            Rect footprint = plan.WorkFootprint;
            Rect plot = plan.Plot.Footprint;
            if (footprint.xMin < plot.xMin ||
                footprint.xMax > plot.xMax ||
                footprint.yMin < plot.yMin ||
                footprint.yMax > plot.yMax)
            {
                throw new InvalidOperationException(
                    $"The gravedigging worksite leaves plot " +
                    $"'{plan.Plot.StableId}'.");
            }
        }

        /// <summary>
        /// FNV-1a over the plot's stable id. The plan carries no seed
        /// of its own, and that id already encodes the lattice cell
        /// the city seed put the plot on.
        /// </summary>
        private static uint StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string text = value ?? string.Empty;
                for (int index = 0; index < text.Length; index++)
                {
                    hash ^= text[index];
                    hash *= 16777619u;
                }

                return hash;
            }
        }

        private static Rect CreateCenteredRect(
            Vector3 center,
            float sizeX,
            float sizeZ)
        {
            return Rect.MinMaxRect(
                center.x - sizeX * 0.5f,
                center.z - sizeZ * 0.5f,
                center.x + sizeX * 0.5f,
                center.z + sizeZ * 0.5f);
        }

        private static Rect Expand(Rect source, float amount)
        {
            return new Rect(
                source.x - amount,
                source.y - amount,
                source.width + amount * 2f,
                source.height + amount * 2f);
        }
    }
}
