using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one place in his mother's front room the hero can sit down.
    ///
    /// Nothing about the sitting itself is new. <see cref="CityBenchSitPlan"/>
    /// derives the dock, the seated pelvis and the walk;
    /// <see cref="CityBenchSitInteraction"/> owns the offer and the three bus
    /// clips; the shared <c>PlayerAnimatedInteractionController</c> owns the
    /// timeline. This file is only the authored seat, in the same shape
    /// <see cref="MountainRoadSeatPlanner"/> already proves works in an area
    /// with no <c>CityLayout</c> at all.
    ///
    /// What IS special here is the room. The sofa's back stands 0.425 m from
    /// the stair ramp - narrower than the hero's own 0.64 m capsule - so the
    /// shared router's detour corner has nowhere to land. The seat is
    /// authored `frontApproachOnly` to make that corner UNREACHABLE rather
    /// than to survive it: no clearance value can move the corner out of a
    /// gap that is narrower than the body.
    /// </summary>
    public static class MothersHouseSofaSeatPlanner
    {
        public const string SeatId = "mothers-house-sofa-seat";

        /// <summary>
        /// A sofa cannot say what the plank kind's own prompt says, which is
        /// "sit on the bench" - in Russian, on a bench specifically.
        /// </summary>
        public const string SitPromptKey = "interaction.sit_sofa";

        /// <summary>
        /// The centre of the usable pocket on the SOUTH cushion - not the
        /// cushion's own centre, and not the north cushion.
        ///
        /// SOUTH, because `DRESS_Sofa.PatchedThrow`
        /// (tools/build-mothers-house-interior-3d-model.py:1283) stands a
        /// 0.035 m cloth plate from y 0.31 to 0.87 across z -0.10..0.62 - a
        /// 0.30 m wall of folded throw over the NORTH seat that a seated
        /// body would pass straight through. The south cushion clears it by
        /// 0.045 m.
        ///
        /// `y = 0.57` is that cushion's top: line 1263 builds it at centre
        /// (-2.35, 0.45, -0.60) with size (0.62, 0.24, 0.91), against
        /// `SOFA_CENTER = (-2.48, 0, -0.08)`.
        ///
        /// `x = -2.26` is the middle of what is left once the BACKREST is
        /// subtracted. The south back cushion (line 1268) is rotated -5
        /// degrees about Z, which carries its front face to x = -2.4742 at
        /// seat height; the cushion's own front lip is -2.04. Pocket
        /// -2.474..-2.04, centre -2.257.
        /// </summary>
        public static readonly Vector3 SeatTopCenter =
            new Vector3(-2.26f, 0.57f, -0.60f);

        /// <summary>The cushion's own z span.</summary>
        public const float SeatWidth = 0.91f;

        /// <summary>
        /// The usable pocket's depth, NOT the cushion's own 0.62. Anyone
        /// reading this as "how deep the cushion is" will be 0.18 m out: it
        /// is the backrest-clear depth, and 0.62 would put the seated pelvis
        /// inside the back cushion.
        /// </summary>
        public const float SeatDepth = 0.44f;

        /// <summary>
        /// The floor the hero stands on - the top of the generated Floor
        /// collider. NEVER the sofa fixture's `Height` (1.33, the top of the
        /// BACKREST), the cushion (0.57) or the rug (0.032): a dock more
        /// than two centimetres off the hero's own root height shows a
        /// prompt, accepts E, walks him over and then never settles, in
        /// silence.
        /// </summary>
        public const float FloorY = 0f;

        /// <summary>
        /// The hero's capsule radius. `PlayerFactory` does not expose it,
        /// and `MountainRoadSeatTests` hard-codes the same number.
        /// </summary>
        private const float CapsuleRadius = 0.32f;

        public static List<CityBenchSitPlan> CreateAll(
            MothersHouseInteriorLayoutPlan layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var seat = new CityBenchSeat(
                SeatId,
                SeatTopCenter,
                SeatWidth,
                SeatDepth,
                FloorY,
                Vector3.right,
                sitPromptKey: SitPromptKey,
                frontApproachOnly: true);
            var plan = new CityBenchSitPlan(seat);
            ValidateOrThrow(plan, layout);
            return new List<CityBenchSitPlan>(1) { plan };
        }

        /// <summary>
        /// Fails LOUDLY at scene boot rather than degrading into a prompt
        /// that never seats anybody.
        ///
        /// It exists because both halves of the shared path swallow bad
        /// input in silence: <c>CityBenchSeat</c>'s constructor does
        /// <c>this = default; return;</c> on a zero facing or a non-positive
        /// span, and <c>CityBenchSitWorldBuilder</c> simply continues past a
        /// seat that is not present. No exception, no log, no sofa.
        /// </summary>
        public static void ValidateOrThrow(
            CityBenchSitPlan plan,
            MothersHouseInteriorLayoutPlan layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (!plan.IsPresent)
            {
                throw new InvalidOperationException(
                    "The sofa seat was rejected by CityBenchSeat: check the " +
                    "id, the facing and the spans.");
            }

            if (!layout.TryGetFixture(
                    MothersHouseInteriorFixtureKind.Sofa,
                    out MothersHouseInteriorFixturePlan sofa))
            {
                throw new InvalidOperationException(
                    "The mother's front room has no sofa to sit on.");
            }

            // READ THE SEAT BACK OUT OF THE PLAN, not out of this file's
            // own constants. They agree for `CreateAll`, which is exactly
            // why checking the constants looked like it worked - and why a
            // plan handed in from anywhere else was validated against
            // numbers it did not contain.
            Vector3 seatTop = plan.ActionHipPosition -
                Vector3.up * CityBenchSitPlan.SeatClearance;
            var seatXZ = new Vector2(seatTop.x, seatTop.z);
            if (!sofa.Bounds.Contains(seatXZ))
            {
                throw new InvalidOperationException(
                    $"The authored cushion {seatXZ} is outside the sofa " +
                    $"fixture {sofa.Bounds}: the furniture moved and the " +
                    "seat did not.");
            }

            if (seatTop.y <= sofa.BaseHeight ||
                seatTop.y >= sofa.BaseHeight + sofa.Height)
            {
                throw new InvalidOperationException(
                    $"The cushion top {seatTop.y} is not inside the " +
                    $"sofa's own height band {sofa.BaseHeight} to " +
                    $"{sofa.BaseHeight + sofa.Height}.");
            }

            float expectedDockY = FloorY + PlayerFactory.GroundedRootOffset;
            if (Mathf.Abs(plan.EntryRootPosition.y - expectedDockY) >
                PlayerMotor.InteractionVerticalTolerance)
            {
                throw new InvalidOperationException(
                    $"The sofa dock stands at y {plan.EntryRootPosition.y} " +
                    $"against the hero's own {expectedDockY}. Past " +
                    $"{PlayerMotor.InteractionVerticalTolerance} m the walk " +
                    "never settles and the prompt does nothing.");
            }

            Rect walkable = layout.WalkableBounds;
            var dock = new Vector2(
                plan.EntryRootPosition.x,
                plan.EntryRootPosition.z);
            if (dock.x < walkable.xMin + CapsuleRadius ||
                dock.x > walkable.xMax - CapsuleRadius ||
                dock.y < walkable.yMin + CapsuleRadius ||
                dock.y > walkable.yMax - CapsuleRadius)
            {
                throw new InvalidOperationException(
                    $"The sofa dock {dock} does not fit inside the room's " +
                    $"walkable bounds {walkable} with the hero's radius.");
            }

            // The room's walkable area is a bare Rect clamp with no
            // furniture holes, so it would happily clamp a dock into the
            // sofa. Only the colliders stop the hero, and those are built
            // from these same fixtures.
            for (int index = 0; index < layout.Fixtures.Count; index++)
            {
                MothersHouseInteriorFixturePlan fixture =
                    layout.Fixtures[index];
                if (!fixture.BlocksMovement)
                {
                    continue;
                }

                if (DistanceToRect(fixture.Bounds, dock) < CapsuleRadius)
                {
                    throw new InvalidOperationException(
                        $"The sofa dock {dock} stands inside " +
                        $"'{fixture.Id}' {fixture.Bounds}.");
                }
            }

            if (!plan.IsWithinApproachLane(plan.EntryRootPosition))
            {
                throw new InvalidOperationException(
                    "The sofa's own dock fails its front-approach veto, so " +
                    "the hero could sit down and never be offered the " +
                    "stand.");
            }
        }

        private static float DistanceToRect(Rect rect, Vector2 point)
        {
            float outsideX = Mathf.Max(
                rect.xMin - point.x,
                point.x - rect.xMax);
            float outsideY = Mathf.Max(
                rect.yMin - point.y,
                point.y - rect.yMax);
            return new Vector2(
                Mathf.Max(0f, outsideX),
                Mathf.Max(0f, outsideY)).magnitude;
        }
    }
}
