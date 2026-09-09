using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class WorldItemPickupModelTests
    {
        [Test]
        public void Confirm_CommitsExactlyOnceAndThenSettles()
        {
            var pickup = new WorldItemPickupModel();

            Assert.That(pickup.CanOpen, Is.True);
            Assert.That(pickup.CanConfirm, Is.False);
            Assert.That(pickup.Confirm(), Is.False);
            Assert.That(pickup.Open(), Is.True);
            Assert.That(pickup.Open(), Is.False);
            Assert.That(pickup.IsOpen, Is.True);
            Assert.That(pickup.IsTaken, Is.False);
            Assert.That(pickup.IsSettled, Is.False);

            pickup.Advance(
                WorldItemInspectionTimeline.FlyingInDurationSeconds);
            Assert.That(pickup.IsShowing, Is.True);

            Assert.That(pickup.Confirm(), Is.True);
            Assert.That(pickup.IsTaken, Is.True);
            // The second press is the same press as far as the session is
            // concerned: it must not credit a second scarf.
            Assert.That(pickup.Confirm(), Is.False);
            Assert.That(pickup.IsSettled, Is.False);

            pickup.Advance(
                WorldItemInspectionTimeline.FlyingOutDurationSeconds);
            Assert.That(pickup.IsOpen, Is.False);
            Assert.That(pickup.IsSettled, Is.True);
            Assert.That(pickup.CanOpen, Is.False);
            Assert.That(pickup.Confirm(), Is.False);
        }

        [Test]
        public void Confirm_IsAcceptedBeforeTheItemFinishesArriving()
        {
            var pickup = new WorldItemPickupModel();
            pickup.Open();
            pickup.Advance(
                WorldItemInspectionTimeline.FlyingInDurationSeconds * 0.25f);

            Assert.That(pickup.IsShowing, Is.False);
            Assert.That(pickup.CanConfirm, Is.True);
            Assert.That(pickup.Confirm(), Is.True);
            Assert.That(pickup.IsTaken, Is.True);
        }

        [Test]
        public void Abandon_LeavesTheItemUntakenAndReportsIt()
        {
            var pickup = new WorldItemPickupModel();

            Assert.That(pickup.Abandon(), Is.False);
            pickup.Open();
            pickup.Advance(
                WorldItemInspectionTimeline.FlyingInDurationSeconds);

            Assert.That(pickup.Abandon(), Is.True);
            Assert.That(pickup.IsTaken, Is.False);
            Assert.That(pickup.IsOpen, Is.False);
            Assert.That(pickup.IsSettled, Is.False);
            // Nothing was taken, so the find can be made again.
            Assert.That(pickup.CanOpen, Is.True);
        }

        [Test]
        public void Abandon_AfterATakeDoesNotUndoIt()
        {
            var pickup = new WorldItemPickupModel();
            pickup.Open();
            // Far enough in that the take leaves a return to play out, so
            // Abandon really does meet an open screen with a committed take.
            pickup.Advance(
                WorldItemInspectionTimeline.FlyingInDurationSeconds);
            pickup.Confirm();
            Assert.That(pickup.IsOpen, Is.True);
            Assert.That(pickup.IsTaken, Is.True);

            Assert.That(pickup.Abandon(), Is.False);
            Assert.That(pickup.IsTaken, Is.True);
            Assert.That(pickup.IsSettled, Is.True);
        }

        [Test]
        public void Dismiss_ClosesTheFindWithoutTakingIt()
        {
            var pickup = new WorldItemPickupModel();

            Assert.That(pickup.Dismiss(), Is.False);
            pickup.Open();
            pickup.Advance(
                WorldItemInspectionTimeline.FlyingInDurationSeconds);

            Assert.That(pickup.Dismiss(), Is.True);
            // A second press while it is already on its way down changes
            // nothing and must say so, or the screen replays its cue.
            Assert.That(pickup.Dismiss(), Is.False);
            Assert.That(pickup.IsTaken, Is.False);
            Assert.That(pickup.IsOpen, Is.True);
            pickup.Advance(
                WorldItemInspectionTimeline.FlyingOutDurationSeconds);
            Assert.That(pickup.IsOpen, Is.False);
            Assert.That(pickup.IsSettled, Is.False);
            Assert.That(pickup.CanOpen, Is.True);
        }

        [Test]
        public void Dismiss_IsRefusedOnceTheTakeIsCommitted()
        {
            var pickup = new WorldItemPickupModel();
            pickup.Open();
            pickup.Advance(
                WorldItemInspectionTimeline.FlyingInDurationSeconds);
            pickup.Confirm();

            Assert.That(pickup.Dismiss(), Is.False);
            Assert.That(pickup.IsTaken, Is.True);
        }

        [Test]
        public void ManualRotation_ReachesTheTimelineOnlyWhileOpen()
        {
            var pickup = new WorldItemPickupModel();

            Assert.That(pickup.AddManualRotation(40f), Is.False);
            pickup.Open();
            Assert.That(pickup.AddManualRotation(40f), Is.True);
            Assert.That(
                pickup.Timeline.ManualRotationDegrees,
                Is.EqualTo(40f).Within(0.0001f));
        }
    }
}
