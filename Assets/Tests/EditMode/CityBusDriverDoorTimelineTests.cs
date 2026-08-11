using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityBusDriverDoorTimelineTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void Defaults_PreserveExistingBusDoorTiming()
        {
            Assert.That(
                CityBusDriverDoorTimeline.DefaultDwellDuration,
                Is.EqualTo(CityBusActor.DwellDuration));
            Assert.That(
                CityBusDriverDoorTimeline
                    .DefaultDoorTransitionDuration,
                Is.EqualTo(CityBusActor.DoorTransitionDuration));
        }

        [Test]
        public void Approach_ReachesButtonOnlyNearStopAtLowSpeed()
        {
            CityBusDriverDoorSample far =
                CityBusDriverDoorTimeline.SampleApproach(
                    CityBusDriverDoorTimeline.ApproachReachDistance,
                    0f);
            AssertClosed(far);

            CityBusDriverDoorSample halfway =
                CityBusDriverDoorTimeline.SampleApproach(0.4f, 1f);
            Assert.That(
                halfway.RightHandButtonBlend,
                Is.EqualTo(0.5f).Within(Tolerance));

            CityBusDriverDoorSample stopped =
                CityBusDriverDoorTimeline.SampleApproach(0f, 0f);
            Assert.That(
                stopped.RightHandButtonBlend,
                Is.EqualTo(1f));
            Assert.That(stopped.ButtonPress01, Is.Zero);
            Assert.That(stopped.DoorLook01, Is.Zero);

            CityBusDriverDoorSample tooFast =
                CityBusDriverDoorTimeline.SampleApproach(
                    0f,
                    CityBusDriverDoorTimeline.ApproachMaximumSpeed);
            AssertClosed(tooFast);
        }

        [Test]
        public void Opening_PressesReturnsHandAndLooksAtDoor()
        {
            CityBusDriverDoorSample contact = SampleDwell(0f);
            Assert.That(
                contact.DoorPhase,
                Is.EqualTo(CityBusDoorPhase.Opening));
            Assert.That(contact.DoorOpenness, Is.Zero);
            Assert.That(contact.Phase01, Is.Zero);
            Assert.That(contact.RightHandButtonBlend, Is.EqualTo(1f));
            Assert.That(contact.ButtonPress01, Is.EqualTo(1f));
            Assert.That(contact.DoorLook01, Is.Zero);

            CityBusDriverDoorSample looking = SampleDwell(
                CityBusDriverDoorTimeline.DoorLookTurnDuration);
            Assert.That(
                looking.DoorPhase,
                Is.EqualTo(CityBusDoorPhase.Opening));
            Assert.That(looking.DoorLook01, Is.EqualTo(1f));
            Assert.That(looking.ButtonPress01, Is.Zero);

            CityBusDriverDoorSample openingEnd = SampleDwell(
                CityBusActor.DoorTransitionDuration - 0.001f);
            Assert.That(
                openingEnd.DoorPhase,
                Is.EqualTo(CityBusDoorPhase.Opening));
            Assert.That(openingEnd.DoorLook01, Is.EqualTo(1f));
            Assert.That(openingEnd.RightHandButtonBlend, Is.Zero);

            CityBusDriverDoorSample open = SampleDwell(
                CityBusActor.DoorTransitionDuration);
            Assert.That(
                open.DoorPhase,
                Is.EqualTo(CityBusDoorPhase.Open));
            Assert.That(open.DoorOpenness, Is.EqualTo(1f));
            Assert.That(open.RightHandButtonBlend, Is.Zero);
            Assert.That(open.DoorLook01, Is.EqualTo(1f));

            CityBusDriverDoorSample heldOpen = SampleDwell(
                CityBusActor.DwellDuration * 0.5f);
            Assert.That(
                heldOpen.DoorPhase,
                Is.EqualTo(CityBusDoorPhase.Open));
            Assert.That(heldOpen.DoorLook01, Is.EqualTo(1f));
        }

        [Test]
        public void Closing_ReachesBeforeTransitionPressesAndResets()
        {
            float closingStart =
                CityBusActor.DwellDuration -
                CityBusActor.DoorTransitionDuration;
            float reachStart =
                closingStart -
                CityBusDriverDoorTimeline
                    .ClosingButtonReachDuration;

            CityBusDriverDoorSample neutral = SampleDwell(reachStart);
            Assert.That(
                neutral.DoorPhase,
                Is.EqualTo(CityBusDoorPhase.Open));
            Assert.That(neutral.RightHandButtonBlend, Is.Zero);

            CityBusDriverDoorSample reaching = SampleDwell(
                reachStart +
                CityBusDriverDoorTimeline
                    .ClosingButtonReachDuration * 0.5f);
            Assert.That(
                reaching.RightHandButtonBlend,
                Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(reaching.ButtonPress01, Is.Zero);

            CityBusDriverDoorSample contact = SampleDwell(closingStart);
            Assert.That(
                contact.DoorPhase,
                Is.EqualTo(CityBusDoorPhase.Closing));
            Assert.That(contact.DoorOpenness, Is.EqualTo(1f));
            Assert.That(contact.Phase01, Is.Zero);
            Assert.That(contact.RightHandButtonBlend, Is.EqualTo(1f));
            Assert.That(contact.ButtonPress01, Is.EqualTo(1f));
            Assert.That(contact.DoorLook01, Is.EqualTo(1f));

            CityBusDriverDoorSample lookingForward = SampleDwell(
                closingStart +
                CityBusDriverDoorTimeline.DoorLookReturnDuration);
            Assert.That(lookingForward.DoorLook01, Is.Zero);

            CityBusDriverDoorSample complete = SampleDwell(
                CityBusActor.DwellDuration);
            AssertClosed(complete);
        }

        [Test]
        public void Sampling_IsBoundedPhaseOwnedAndHistoryIndependent()
        {
            CityBusDriverDoorSample expected = SampleDwell(9.42f);
            for (int index = 0; index <= 1000; index++)
            {
                CityBusDriverDoorSample sample = SampleDwell(
                    CityBusActor.DwellDuration * index / 1000f);
                Assert.That(sample.DoorOpenness, Is.InRange(0f, 1f));
                Assert.That(sample.Phase01, Is.InRange(0f, 1f));
                Assert.That(
                    sample.RightHandButtonBlend,
                    Is.InRange(0f, 1f));
                Assert.That(sample.ButtonPress01, Is.InRange(0f, 1f));
                Assert.That(sample.DoorLook01, Is.InRange(0f, 1f));
                if (sample.DoorLook01 > 0f)
                {
                    Assert.That(
                        sample.DoorPhase,
                        Is.EqualTo(CityBusDoorPhase.Opening)
                            .Or.EqualTo(CityBusDoorPhase.Open));
                }
            }

            CityBusDriverDoorSample actual = SampleDwell(9.42f);
            AssertSame(expected, actual);
        }

        private static CityBusDriverDoorSample SampleDwell(float elapsed)
        {
            return CityBusDriverDoorTimeline.SampleDwell(
                elapsed,
                CityBusActor.DwellDuration,
                CityBusActor.DoorTransitionDuration);
        }

        private static void AssertClosed(
            CityBusDriverDoorSample sample)
        {
            Assert.That(
                sample.DoorPhase,
                Is.EqualTo(CityBusDoorPhase.Closed));
            Assert.That(sample.DoorOpenness, Is.Zero);
            Assert.That(sample.Phase01, Is.Zero);
            Assert.That(sample.RightHandButtonBlend, Is.Zero);
            Assert.That(sample.ButtonPress01, Is.Zero);
            Assert.That(sample.DoorLook01, Is.Zero);
        }

        private static void AssertSame(
            CityBusDriverDoorSample expected,
            CityBusDriverDoorSample actual)
        {
            Assert.That(actual.DoorPhase, Is.EqualTo(expected.DoorPhase));
            Assert.That(actual.DoorOpenness, Is.EqualTo(expected.DoorOpenness));
            Assert.That(actual.Phase01, Is.EqualTo(expected.Phase01));
            Assert.That(
                actual.RightHandButtonBlend,
                Is.EqualTo(expected.RightHandButtonBlend));
            Assert.That(
                actual.ButtonPress01,
                Is.EqualTo(expected.ButtonPress01));
            Assert.That(actual.DoorLook01, Is.EqualTo(expected.DoorLook01));
        }
    }
}
