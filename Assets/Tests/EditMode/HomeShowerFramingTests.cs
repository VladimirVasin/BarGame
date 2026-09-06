using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The shower's authored geometry: every point stays inside the
    /// bathroom's camera hold rectangle, the opening clears both the
    /// gathered curtain and the toilet, the palms press the tile at
    /// shoulder height within reach, the drips fall into the basin, and
    /// the stall test sends a hero outside through the opening and a
    /// hero inside straight to the dock.
    /// </summary>
    public sealed class HomeShowerFramingTests
    {
        /// <summary>The bathroom shot's hold rectangle, from HomeInteriorRoot's bathroom bounds Rect(1.55, 0.65, 3.10, 3.00) inset 0.06/0.08.</summary>
        private static readonly Rect BathroomHold = new Rect(1.61f, 0.73f, 2.98f, 2.86f);
        private static readonly Rect ShowerFootprint = new Rect(3.35f, 2.35f, 1.15f, 1.15f);
        private const float TrayTop = 0.18f;

        [Test]
        public void EveryAuthoredPointStaysInsideTheBathroomHold()
        {
            foreach (Vector3 point in new[]
                     {
                         HomeShowerFraming.Waypoint,
                         HomeShowerFraming.Dock,
                         HomeShowerFraming.Exit,
                         HomeShowerFraming.Stand
                     })
            {
                Assert.That(
                    BathroomHold.Contains(new Vector2(point.x, point.z)),
                    Is.True,
                    $"{point} would flip the fixed camera off the bathroom shot.");
            }

            Assert.That(ShowerFootprint.Contains(new Vector2(HomeShowerFraming.Dock.x, HomeShowerFraming.Dock.z)), Is.True);
            Assert.That(HomeShowerFraming.Dock.z, Is.LessThanOrEqualTo(3.33f), "The walkable inset caps the root at z 3.33.");
            Assert.That(HomeShowerFraming.Waypoint.x, Is.LessThanOrEqualTo(4.33f), "The walkable inset caps the root at x 4.33.");
        }

        [Test]
        public void TheOpeningClearsTheCurtainAndTheToilet()
        {
            // Curtain group pivot x 3.40; fold 4 centre 0.98, half width 0.135, scaled.
            float curtainRightEdge = 3.40f + (0.98f + 0.135f) * HomeShowerInteraction.GatheredCurtainScale;
            foreach (Vector3 point in new[] { HomeShowerFraming.Waypoint, HomeShowerFraming.Exit })
            {
                Assert.That(
                    point.x - HomeShowerFraming.CapsuleRadius,
                    Is.GreaterThan(curtainRightEdge + 0.05f),
                    "The capsule must pass the gathered curtain with room to spare.");
                // Toilet footprint collider reaches z 1.829.
                Assert.That(point.z - HomeShowerFraming.CapsuleRadius, Is.GreaterThan(1.829f + 0.05f));
            }

            // The opening-to-dock leg crosses the curtain plane clear of the folds too.
            float t = (2.384f - HomeShowerFraming.Waypoint.z) / (HomeShowerFraming.Dock.z - HomeShowerFraming.Waypoint.z);
            float crossingX = Mathf.Lerp(HomeShowerFraming.Waypoint.x, HomeShowerFraming.Dock.x, t);
            Assert.That(crossingX - HomeShowerFraming.CapsuleRadius, Is.GreaterThan(curtainRightEdge + 0.03f));
        }

        [Test]
        public void TheWayOutIsTheWayIn()
        {
            Assert.That(HomeShowerFraming.Exit, Is.EqualTo(HomeShowerFraming.Waypoint), "He leaves through the opening he came in by and turns to the room there.");
            Assert.That(HomeShowerFraming.IsInsideStall(HomeShowerFraming.Exit), Is.False, "The exit is outside the stall's footprint.");
        }

        [Test]
        public void ThePalmsTouchTheTileAtShoulderHeight()
        {
            Assert.That(HomeShowerFraming.LeftPalm.z, Is.EqualTo(HomeShowerFraming.WallZ - 0.01f).Within(0.001f));
            Assert.That(HomeShowerFraming.RightPalm.z, Is.EqualTo(HomeShowerFraming.WallZ - 0.01f).Within(0.001f));
            Assert.That(HomeShowerFraming.LeftPalm.y, Is.InRange(0.03f, 1.73f), "Inside the tile band.");
            Assert.That(HomeShowerFraming.RightPalm.x - HomeShowerFraming.LeftPalm.x, Is.InRange(0.36f, 0.52f));
            // Reachable with bent elbows: shoulder ≈ dock + 1.60 up, ±0.20 across, chain ≈ 0.586 * 0.98.
            Vector3 shoulder = HomeShowerFraming.Dock + new Vector3(0.20f, TrayTop + 1.42f, 0.08f);
            Assert.That(Vector3.Distance(shoulder, HomeShowerFraming.RightPalm), Is.LessThan(0.55f));
        }

        [Test]
        public void TheDripsFallStraightIntoTheBasin()
        {
            Assert.That(HomeShowerFraming.DripOrigin.x, Is.EqualTo(HomeShowerFraming.BasinLanding.x).Within(0.001f));
            Assert.That(HomeShowerFraming.DripOrigin.z, Is.EqualTo(HomeShowerFraming.BasinLanding.z).Within(0.001f));
            Assert.That(HomeShowerFraming.DripOrigin.y - HomeShowerFraming.BasinLanding.y, Is.GreaterThan(1.5f));
            Assert.That(HomeShowerFraming.BasinLanding.y, Is.GreaterThan(TrayTop), "The drop lands on the tray, not under it.");
            Assert.That(ShowerFootprint.Contains(new Vector2(HomeShowerFraming.BasinLanding.x, HomeShowerFraming.BasinLanding.z)), Is.True);
        }

        [Test]
        public void AHeroOutsideTheStallEntersThroughTheOpening()
        {
            Assert.That(HomeShowerFraming.IsInsideStall(new Vector3(3.9f, 0f, 2.9f)), Is.True);
            Assert.That(HomeShowerFraming.IsInsideStall(HomeShowerFraming.Stand), Is.True, "The prompt's stand is just inside the opening.");
            Assert.That(HomeShowerFraming.IsInsideStall(HomeShowerFraming.Dock), Is.True);
            Assert.That(HomeShowerFraming.IsInsideStall(new Vector3(2.5f, 0f, 2.0f)), Is.False);
            Assert.That(HomeShowerFraming.IsInsideStall(new Vector3(2.0f, 0f, 3.0f)), Is.False, "Beside the stall, behind its curtain.");
            Assert.That(HomeShowerFraming.IsInsideStall(HomeShowerFraming.Waypoint), Is.False, "A hero standing in the opening still takes it.");
        }
    }
}
