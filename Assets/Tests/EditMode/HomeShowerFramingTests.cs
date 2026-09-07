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
                         HomeShowerFraming.ExitCameraDock,
                         HomeShowerFraming.Stand,
                         HomeShowerCurtainPose.OutsideDock,
                         HomeShowerCurtainPose.InsideDock
                     })
            {
                Assert.That(
                    BathroomHold.Contains(new Vector2(point.x, point.z)),
                    Is.True,
                    $"{point} would flip the fixed camera off the bathroom shot.");
            }

            Assert.That(ShowerFootprint.Contains(new Vector2(HomeShowerFraming.Dock.x, HomeShowerFraming.Dock.z)), Is.True);
            Assert.That(HomeShowerFraming.Dock.z, Is.LessThanOrEqualTo(3.33f), "The walkable inset caps the root at z 3.33.");
            Assert.That(HomeShowerFraming.Dock.z, Is.EqualTo(3.08f).Within(0.001f), "The feet stand farther from the tap wall beneath the stronger torso incline.");
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
                    Is.GreaterThan(curtainRightEdge + 0.025f),
                    "The capsule must pass the gathered curtain with room to spare.");
                // Toilet footprint collider reaches z 1.829.
                Assert.That(point.z - HomeShowerFraming.CapsuleRadius, Is.GreaterThan(1.829f + 0.025f));
            }

            // The opening-to-dock leg crosses the curtain plane clear of the folds too.
            float t = (2.384f - HomeShowerFraming.Waypoint.z) / (HomeShowerFraming.Dock.z - HomeShowerFraming.Waypoint.z);
            float crossingX = Mathf.Lerp(HomeShowerFraming.Waypoint.x, HomeShowerFraming.Dock.x, t);
            Assert.That(crossingX - HomeShowerFraming.CapsuleRadius, Is.GreaterThan(curtainRightEdge + 0.03f));
        }

        [Test]
        public void TheWayOutIsTheWayIn()
        {
            Assert.That(HomeShowerFraming.Exit, Is.EqualTo(HomeShowerCurtainPose.OutsideDock), "He finishes outside at the same grounded curtain gesture dock used on entry.");
            Assert.That(HomeShowerFraming.IsInsideStall(HomeShowerFraming.Exit), Is.False, "The exit is outside the stall's footprint.");
            Assert.That(HomeShowerFraming.IsInsideStall(HomeShowerCurtainPose.InsideDock), Is.True);
            Assert.That(HomeShowerFraming.ExitCameraDock, Is.EqualTo(HomeShowerCurtainPose.OutsideDock),
                "The departure stays at the walkable outside dock beside the toilet.");
            Assert.That(Vector3.Dot(HomeShowerFraming.ExitCameraFacing * Vector3.forward, Vector3.back),
                Is.GreaterThan(0.999f), "The toes point into the room, away from the curtain; actual foot clearance is measured in PlayMode.");
            Assert.That(Quaternion.Angle(HomeShowerFraming.ExitCameraFacing, HomeShowerCurtainPose.OutsideFacing),
                Is.EqualTo(180f).Within(0.001f), "Only after the camera returns does he turn toward the authored closing gesture.");
            Assert.That(HomeShowerFraming.IsInsideStall(HomeShowerFraming.ExitCameraDock), Is.False);
        }

        [Test]
        public void BothPalmsBraceTheTileAtShoulderHeight()
        {
            Assert.That(HomeShowerFraming.LeftPalm.z, Is.EqualTo(HomeShowerFraming.WallZ - 0.01f).Within(0.001f));
            Assert.That(HomeShowerFraming.RightPalm.z, Is.EqualTo(HomeShowerFraming.WallZ - 0.01f).Within(0.001f));
            Assert.That(HomeShowerFraming.LeftPalm.y, Is.InRange(0.03f, 1.73f), "Inside the tile band.");
            Assert.That(HomeShowerFraming.RightPalm.y, Is.InRange(1.3f, 1.73f), "Both hands start against the tile at shoulder height.");
            Assert.That(HomeShowerFraming.RightPalm.x - HomeShowerFraming.LeftPalm.x, Is.InRange(0.36f, 0.65f));
            // Actual reach and both rendered hand contacts are measured on
            // the posed production rig in the focused shower scenario.
        }

        [Test]
        public void TheCameraCrossesTheSameOpenGapOnEntryAndItsReverseReturn()
        {
            Vector3 before = HomeShowerCameraPath.BeforeCurtain;
            Vector3 after = HomeShowerCameraPath.AfterCurtain;
            const float curtainPlane = 2.384f;
            Assert.That(before.z, Is.LessThan(curtainPlane));
            Assert.That(after.z, Is.GreaterThan(curtainPlane));
            Vector3 start = before + new Vector3(-1.8f, 0.5f, -0.8f);
            Vector3 eye = HomeShowerFraming.Dock + new Vector3(-0.024f, 1.60f, 0.33f);
            Vector3 crossing = default, previous = before;
            int crossings = 0;
            const float step = 1f / 120f;
            float previousTime = 0f;
            for (float time = step; time <= HomeShowerSceneTimeline.CameraInSeconds + step; time += step)
            {
                float progress = Mathf.Lerp(HomeShowerCameraPath.CurtainApproachEnd, 1f,
                    Mathf.Clamp01(time / HomeShowerSceneTimeline.CameraInSeconds));
                Vector3 current = HomeShowerCameraPath.Evaluate(start, before, after, eye, progress);
                if (previous.z < curtainPlane && current.z >= curtainPlane)
                {
                    crossings++;
                    crossing = Vector3.Lerp(previous, current, (curtainPlane - previous.z) / (current.z - previous.z));
                }
                if (previousTime == 0f || time >= HomeShowerSceneTimeline.CameraInSeconds)
                    Assert.That(Vector3.Distance(previous, current) / step, Is.LessThan(0.01f),
                        "The camera starts and lands without a sudden velocity change.");
                previous = current;
                previousTime = time;
            }
            Assert.That(crossings, Is.EqualTo(1));
            Assert.That(Vector3.Distance(previous, eye), Is.LessThan(0.0001f));
            float gatheredEdge = 3.40f + 1.115f * HomeShowerInteraction.GatheredCurtainScale;
            Assert.That(crossing.x, Is.GreaterThan(gatheredEdge + 0.025f).And.LessThan(4.50f - 0.025f));
            Assert.That(crossing.y, Is.InRange(0.49f, 2.11f), "The camera travels through the open cloth-height interval, not above it.");

            Assert.That(HomeShowerSceneTimeline.CameraOutSeconds, Is.EqualTo(
                HomeShowerSceneTimeline.CameraInSeconds + HomeShowerSceneTimeline.CameraApproachSeconds));
            previous = eye;
            int returnCrossings = 0;
            int frames = Mathf.CeilToInt(HomeShowerSceneTimeline.CameraOutSeconds / step);
            for (int frame = 1; frame <= frames; frame++)
            {
                float time = Mathf.Min(frame * step, HomeShowerSceneTimeline.CameraOutSeconds);
                float progress = time <= HomeShowerSceneTimeline.CameraInSeconds
                    ? Mathf.Lerp(1f, HomeShowerCameraPath.CurtainApproachEnd, time / HomeShowerSceneTimeline.CameraInSeconds)
                    : Mathf.Lerp(HomeShowerCameraPath.CurtainApproachEnd, 0f,
                        (time - HomeShowerSceneTimeline.CameraInSeconds) / HomeShowerSceneTimeline.CameraApproachSeconds);
                Vector3 current = HomeShowerCameraPath.Evaluate(start, before, after, eye, progress);
                if (previous.z >= curtainPlane && current.z < curtainPlane)
                {
                    returnCrossings++;
                    Vector3 returnCrossing = Vector3.Lerp(previous, current,
                        (curtainPlane - previous.z) / (current.z - previous.z));
                    Assert.That(Vector3.Distance(returnCrossing, crossing), Is.LessThan(0.001f),
                        "The reverse lens uses the same opening instead of an arc above the rail.");
                }
                if (frame == 1 || frame == frames)
                    Assert.That(Vector3.Distance(previous, current) / step, Is.LessThan(0.01f));
                previous = current;
            }
            Assert.That(returnCrossings, Is.EqualTo(1));
            Assert.That(Vector3.Distance(previous, start), Is.LessThan(0.0001f));
        }

        [Test]
        public void TheTiltedNozzleStaysBeforeTheWallAndItsDripsLandInsideTheBasin()
        {
            Assert.That(HomeShowerFraming.DripOrigin.x, Is.EqualTo(HomeShowerFraming.BasinLanding.x).Within(0.001f));
            Assert.That(HomeShowerFraming.DripOrigin.z, Is.GreaterThan(HomeShowerFraming.BasinLanding.z),
                "The repositioned head directs its residual drops back toward the existing landing.");
            Assert.That(Vector3.Distance(HomeShowerFraming.DripOrigin,
                HomeShowerFraming.HeadFaceCenter + HomeShowerFraming.StreamDirection * 0.014f), Is.LessThan(0.0001f));
            Assert.That(HomeShowerFraming.DripOrigin.x, Is.InRange(ShowerFootprint.xMin, ShowerFootprint.xMax));
            Assert.That(HomeShowerFraming.DripOrigin.z, Is.GreaterThan(ShowerFootprint.yMin).And.LessThan(HomeShowerFraming.WallZ),
                "The overhead nozzle can occupy the gap behind the tray but must stay in front of the tap wall.");
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
            Assert.That(HomeShowerFraming.IsInsideStall(new Vector3(3.30f, 0f, 2.80f)), Is.False, "The left curtain cannot be crossed as if the hero were already inside.");
            Assert.That(HomeShowerFraming.IsInsideStall(HomeShowerFraming.Waypoint), Is.False, "A hero standing in the opening still takes it.");
        }
    }
}
