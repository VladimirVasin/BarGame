using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The one man at the park chess set and the wire that lights him.
    ///
    /// Both are derived from geometry somebody else owns — the chess
    /// recipe draws the plank he sits on, and the park's tree field
    /// carries the wire — so what is pinned here is the seams: that he
    /// is on the timber that was actually drawn rather than near it,
    /// that he looks across his own board, that the wire spans the set
    /// and clears the table, and that exactly one of its two lamps
    /// burns.
    /// </summary>
    public sealed class ParkChessPlayerTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        // The chess recipe's own contract, restated: two tables that far
        // apart on the set's tangent, a plank top that high over the
        // anchor plane, and a slab that thick.
        private const float TableOffset = 1.85f;
        private const float SeatTopHeight = 0.54f;
        private const float TableTopHeight = 0.90f;
        private const float ProtectionRadius = 3f;

        [Test]
        public void Plan_AbsentWithoutALayoutOrADecorationPlan()
        {
            CityLayout layout = CreateLayout();

            Assert.That(
                ParkChessPlayerPlan.Create(null, null).IsPresent,
                Is.False);
            Assert.That(
                ParkChessPlayerPlan.Create(layout, null).IsPresent,
                Is.False);
            Assert.That(
                CityParkChessLampPlan.Create(layout, null).IsPresent,
                Is.False);
        }

        /// <summary>
        /// He sits on the plank the recipe drew, not on a plank the
        /// runtime worked out for itself. The seat comes from the same
        /// <see cref="CityDecorationWorldBuilder.AppendBenchSeats"/>
        /// call the hero's sit interaction reads, so the two can never
        /// disagree about where the timber is.
        /// </summary>
        [Test]
        public void Stance_SitsOnTheDrawnPlank()
        {
            CityLayout layout = CreateLayout();
            CityDecorationPlan decorations = CreatePlan(layout);
            ParkChessPlayerPlan plan =
                ParkChessPlayerPlan.Create(layout, decorations);

            Assert.That(plan.IsPresent, Is.True,
                "The default city plants the chess set, so it seats him.");

            CityBenchSeat seat = FindSeat(
                layout,
                decorations,
                plan.Stance.SeatId);
            Assert.That(seat.IsPresent, Is.True,
                "His seat id must name a seat the recipe actually draws.");
            Assert.That(
                plan.Stance.SeatTopCenter,
                Is.EqualTo(seat.SeatTopCenter),
                "He is placed on the drawn seat, not near it.");

            DescribeBasis(
                layout,
                decorations,
                out Vector3 origin,
                out _,
                out _);
            Assert.That(
                plan.Stance.SeatTopCenter.y - origin.y,
                Is.EqualTo(SeatTopHeight).Within(0.0001f),
                "The plank top stands 0.54 m over the set's datum, " +
                "which is the seat height his loop was authored for.");
        }

        /// <summary>
        /// And he looks across his own board rather than out at the
        /// grass. The seat he takes is the one facing the park centre:
        /// the player arrives along the park axis, and this design's
        /// whole content is a face with its head in its hands.
        /// </summary>
        [Test]
        public void Stance_LooksAcrossHisOwnBoardTowardTheParkCentre()
        {
            CityLayout layout = CreateLayout();
            CityDecorationPlan decorations = CreatePlan(layout);
            ParkChessPlayerPlan plan =
                ParkChessPlayerPlan.Create(layout, decorations);
            DescribeBasis(
                layout,
                decorations,
                out Vector3 origin,
                out Vector3 forward,
                out Vector3 tangent);

            Assert.That(
                Vector3.Dot(plan.Stance.Facing, forward),
                Is.GreaterThan(0.99f),
                "The seat on the near side faces back across the table.");

            // And the table he faces is the one at negative tangent,
            // which is also the one the burning lamp hangs over.
            Vector3 offset = plan.Stance.SeatTopCenter - origin;
            Assert.That(
                Vector3.Dot(offset, tangent),
                Is.EqualTo(-TableOffset).Within(0.01f));
            Assert.That(
                Vector3.Dot(offset, forward),
                Is.LessThan(0f),
                "He sits on the park-centre side of his own table.");
        }

        /// <summary>
        /// The wire is strung between two things standing clear of the
        /// set on opposite sides. Whether they are trees is up to the
        /// seed - the park's tree field is deterministic but not
        /// obliging - so what is pinned is the span, not the timber.
        /// </summary>
        [Test]
        public void Lamp_SpansTheSetFromBothSides()
        {
            CityLayout layout = CreateLayout();
            CityDecorationPlan decorations = CreatePlan(layout);
            CityParkChessLampPlan lamp =
                CityParkChessLampPlan.Create(layout, decorations);

            Assert.That(lamp.IsPresent, Is.True);

            float near = Vector3.Dot(
                lamp.NearAnchor.TiePoint - lamp.Origin,
                lamp.Forward);
            float far = Vector3.Dot(
                lamp.FarAnchor.TiePoint - lamp.Origin,
                lamp.Forward);
            Assert.That(near, Is.LessThan(-ProtectionRadius),
                "The near knot stands outside the ground the set " +
                "reserves against paths, trees and neighbours.");
            Assert.That(far, Is.GreaterThan(ProtectionRadius),
                "And so does the far one, on the other side.");

            // On the default city both ends are real trees. The fallback
            // hook pole exists because the tree field is deterministic
            // rather than obliging, but it is a fallback: if a change to
            // the park grid, to the set's placement or to the search
            // band quietly turns the authored city's wire into two posts
            // in a lawn, this is what says so.
            Assert.That(lamp.NearAnchor.IsTree, Is.True);
            Assert.That(lamp.FarAnchor.IsTree, Is.True);

            // Both knots ride their own ground rather than the set's
            // datum: the park lawn slopes under all of this.
            foreach (CityParkChessLampAnchor anchor in
                     new[] { lamp.NearAnchor, lamp.FarAnchor })
            {
                Assert.That(
                    anchor.TiePoint.y - anchor.GroundY,
                    Is.GreaterThan(1.9f),
                    "A knot below head height would be a trip hazard, " +
                    "not a wire over a table.");
            }
        }

        /// <summary>
        /// One wire, two shades, one lamp. The working one hangs over
        /// the middle of the set so its circle reaches both boards; the
        /// other is a bare socket further down the wire, and it has to
        /// stay bare, because a second bulb would quietly undo the one
        /// thing this fixture is saying.
        /// </summary>
        [Test]
        public void Lamp_BurnsOverTheMiddleOfTheSetAndNowhereElse()
        {
            CityLayout layout = CreateLayout();
            CityDecorationPlan decorations = CreatePlan(layout);
            CityParkChessLampPlan lamp =
                CityParkChessLampPlan.Create(layout, decorations);
            ParkChessPlayerPlan player =
                ParkChessPlayerPlan.Create(layout, decorations);

            Assert.That(lamp.LitPendant.IsLit, Is.True);
            Assert.That(lamp.DeadPendant.IsLit, Is.False);

            float lit = Vector3.Dot(
                lamp.LitPendant.ShadeCenter - lamp.Origin,
                lamp.Forward);
            float dead = Vector3.Dot(
                lamp.DeadPendant.ShadeCenter - lamp.Origin,
                lamp.Forward);
            Assert.That(lit, Is.EqualTo(0f).Within(0.01f),
                "The working lamp hangs over the set's own centre.");
            Assert.That(
                dead,
                Is.EqualTo(CityParkChessLampPlan.DeadPendantReachMeters)
                    .Within(0.01f));

            // And it reaches him. He sits one bench row and one table
            // offset away from that centre, so a circle that stops short
            // of him lights furniture and nobody.
            Vector3 head = player.Stance.SeatTopCenter + (Vector3.up * 0.7f);
            Assert.That(
                Vector3.Distance(head, lamp.LitPendant.ShadeCenter),
                Is.LessThan(CityParkChessLampWorldBuilder.LightRange * 0.5f),
                "The one burning lamp has to model the one man under it.");

            // Both tables sit inside the circle too, which is the whole
            // reason the wire crosses the set instead of running along it.
            for (int table = -1; table <= 1; table += 2)
            {
                Vector3 board = lamp.Origin +
                    (lamp.Tangent * (table * TableOffset)) +
                    (Vector3.up * TableTopHeight);
                Assert.That(
                    Vector3.Distance(board, lamp.LitPendant.ShadeCenter),
                    Is.LessThan(CityParkChessLampWorldBuilder.LightRange),
                    "Both boards are inside the lamp's own range.");
            }
        }

        /// <summary>
        /// The sag has to clear the board by enough to sit under and
        /// stay below its own knots, or the wire is either a hazard or
        /// a straight line.
        /// </summary>
        [Test]
        public void Lamp_SagsClearOfTheTableAndBelowItsKnots()
        {
            CityLayout layout = CreateLayout();
            CityDecorationPlan decorations = CreatePlan(layout);
            CityParkChessLampPlan lamp =
                CityParkChessLampPlan.Create(layout, decorations);
            DescribeBasis(layout, decorations, out Vector3 origin, out _, out _);

            float tableTop = origin.y + TableTopHeight;
            foreach (CityParkChessLampPendant pendant in
                     new[] { lamp.LitPendant, lamp.DeadPendant })
            {
                float rim = pendant.ShadeCenter.y -
                    (CityParkChessLampPlan.ShadeHeightMeters * 0.5f);
                Assert.That(
                    rim - tableTop,
                    Is.GreaterThan(1f),
                    "A shade closer than a metre over the board would " +
                    "be in the way of the people using it.");

                // Against the chord rather than against the lower knot.
                // The two ends stand on their own ground and the lawn
                // slopes, so a taut wire between unequal knots is
                // legitimately above the lower one for most of its run;
                // what proves it sags is that it hangs below the
                // straight line joining them.
                Assert.That(
                    pendant.WirePoint.y,
                    Is.LessThan(ChordHeight(lamp, pendant.WirePoint)),
                    "A wire at or above its own chord is not strung, " +
                    "it is a rod.");
            }

            Vector3 middle = (lamp.NearAnchor.TiePoint +
                lamp.FarAnchor.TiePoint) * 0.5f;
            Vector3 sampled = CityParkChessLampPlan.SampleWire(
                lamp.NearAnchor,
                lamp.FarAnchor,
                lamp.Origin,
                lamp.Forward,
                Vector3.Dot(middle - lamp.Origin, lamp.Forward));
            Assert.That(
                middle.y - sampled.y,
                Is.EqualTo(CityParkChessLampPlan.SagMeters).Within(0.001f),
                "The deepest point of the sag is the declared one, and " +
                "it lands at the middle of the span.");
        }

        /// <summary>
        /// Height of the straight line between the two knots, directly
        /// above or below the given point on the wire.
        /// </summary>
        private static float ChordHeight(
            CityParkChessLampPlan lamp,
            Vector3 point)
        {
            float near = Vector3.Dot(
                lamp.NearAnchor.TiePoint - lamp.Origin,
                lamp.Forward);
            float far = Vector3.Dot(
                lamp.FarAnchor.TiePoint - lamp.Origin,
                lamp.Forward);
            float at = Vector3.Dot(point - lamp.Origin, lamp.Forward);
            float amount = Mathf.Approximately(far, near)
                ? 0.5f
                : Mathf.Clamp01((at - near) / (far - near));
            return Mathf.Lerp(
                lamp.NearAnchor.TiePoint.y,
                lamp.FarAnchor.TiePoint.y,
                amount);
        }

        private static CityBenchSeat FindSeat(
            CityLayout layout,
            CityDecorationPlan decorations,
            string seatId)
        {
            var seats = new List<CityBenchSeat>(4);
            for (int index = 0;
                 index < decorations.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    decorations.Descriptors[index];
                if (descriptor.Kind != CityDecorationKind.ParkChessTables)
                {
                    continue;
                }

                CityDecorationWorldBuilder.AppendBenchSeats(
                    layout,
                    descriptor,
                    seats);
            }

            for (int index = 0; index < seats.Count; index++)
            {
                if (string.Equals(seats[index].Id, seatId))
                {
                    return seats[index];
                }
            }

            return default;
        }

        private static void DescribeBasis(
            CityLayout layout,
            CityDecorationPlan decorations,
            out Vector3 origin,
            out Vector3 forward,
            out Vector3 tangent)
        {
            for (int index = 0;
                 index < decorations.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    decorations.Descriptors[index];
                if (descriptor.Kind != CityDecorationKind.ParkChessTables)
                {
                    continue;
                }

                Assert.That(
                    CityDecorationWorldBuilder.TryDescribeRecipeBasis(
                        layout,
                        descriptor,
                        out origin,
                        out forward),
                    Is.True);
                tangent = new Vector3(-forward.z, 0f, forward.x);
                return;
            }

            Assert.Fail("The default city plants no chess set.");
            origin = default;
            forward = default;
            tangent = default;
        }

        private static CityLayout CreateLayout()
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
        }

        private static CityDecorationPlan CreatePlan(CityLayout layout)
        {
            return CityDecorationPlanner.CreatePlan(
                layout,
                RoadFencePlanner.CreatePlan(layout),
                CityNightFixturePlanner.CreatePlan(layout));
        }
    }
}
