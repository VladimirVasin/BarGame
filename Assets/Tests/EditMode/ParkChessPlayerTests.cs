using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The chess player's contacts and the gazebo pendants share the drawn
    /// table/deck datum; one working lamp still covers both boards.
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

        [Test]
        public void Lamp_MountsUnderTheGazeboBeamAboveBothTables()
        {
            CityLayout layout = CreateLayout();
            CityDecorationPlan decorations = CreatePlan(layout);
            CityParkChessLampPlan lamp =
                CityParkChessLampPlan.Create(layout, decorations);
            Assert.That(lamp.IsPresent, Is.True);

            CityDecorationDescriptor gazebo = default;
            bool found = false;
            foreach (CityDecorationDescriptor descriptor in decorations.Descriptors)
            {
                if (descriptor.Kind != CityDecorationKind.ParkBandstand)
                {
                    continue;
                }

                gazebo = descriptor;
                found = true;
                break;
            }

            Assert.That(found, Is.True);
            Assert.That(lamp.Origin.y - gazebo.Position.y,
                Is.EqualTo(CityParkGazeboGeometry.DeckHeightMeters)
                    .Within(0.001f));
            CityParkChessLampPendant[] pendants =
                { lamp.LitPendant, lamp.DeadPendant };
            for (int index = 0; index < pendants.Length; index++)
            {
                CityParkChessLampPendant pendant = pendants[index];
                float table = index == 0 ? -TableOffset : TableOffset;
                Assert.That(Vector3.Dot(
                    pendant.MountPoint - lamp.Origin, lamp.Tangent),
                    Is.EqualTo(table).Within(0.001f),
                    "Each fitting hangs directly over its own table.");
                Assert.That(Vector3.Dot(
                    pendant.MountPoint - lamp.Origin, lamp.Forward),
                    Is.EqualTo(0f).Within(0.001f));
                Assert.That(pendant.MountPoint.y - gazebo.Position.y,
                    Is.EqualTo(CityParkGazeboGeometry.BeamUndersideHeightMeters)
                        .Within(0.001f),
                    "The cable must end on the real cross-beam underside.");
            }
        }

        [Test]
        public void Lamp_BurnsOverOneTableAndStillReachesBothBoards()
        {
            CityLayout layout = CreateLayout();
            CityDecorationPlan decorations = CreatePlan(layout);
            CityParkChessLampPlan lamp =
                CityParkChessLampPlan.Create(layout, decorations);
            ParkChessPlayerPlan player =
                ParkChessPlayerPlan.Create(layout, decorations);

            Assert.That(lamp.LitPendant.IsLit, Is.True);
            Assert.That(lamp.DeadPendant.IsLit, Is.False);
            Vector3 head = player.Stance.SeatTopCenter + Vector3.up * 0.7f;
            Assert.That(Vector3.Distance(head, lamp.LitPendant.BulbCenter),
                Is.LessThan(CityParkChessLampWorldBuilder.LightRange * 0.5f),
                "The working pendant must model its seated chess player.");
            for (int table = -1; table <= 1; table += 2)
            {
                Vector3 board = lamp.Origin + lamp.Tangent * (table * TableOffset) +
                    Vector3.up * TableTopHeight;
                Assert.That(Vector3.Distance(board, lamp.LitPendant.BulbCenter),
                    Is.LessThan(CityParkChessLampWorldBuilder.LightRange * 0.5f),
                    "Both boards remain within the useful half of the one light's range.");
            }
        }

        [Test]
        public void Lamp_ClearsStandingPlayersAndStaysBelowTheRoof()
        {
            CityLayout layout = CreateLayout();
            CityDecorationPlan decorations = CreatePlan(layout);
            CityParkChessLampPlan lamp =
                CityParkChessLampPlan.Create(layout, decorations);
            foreach (CityParkChessLampPendant pendant in
                     new[] { lamp.LitPendant, lamp.DeadPendant })
            {
                Assert.That(pendant.ShadeRimCenter.y - lamp.Origin.y,
                    Is.GreaterThan(2.2f),
                    "The lowest metal clears a standing player on the deck.");
                Assert.That(pendant.MountPoint.y - pendant.ShadeRimCenter.y,
                    Is.EqualTo(CityParkChessLampPlan.MountToRimMeters)
                        .Within(0.001f));
                Assert.That(pendant.BulbCenter.y,
                    Is.LessThan(pendant.MountPoint.y - 0.5f),
                    "The source belongs below the beam, never inside or above the roof.");
                Assert.That(pendant.ShadeCenter.y - lamp.Origin.y - TableTopHeight,
                    Is.GreaterThan(1f));
            }
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
