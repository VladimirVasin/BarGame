using System.Collections.Generic;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class InteriorSoundscapeScheduleTests
    {
        [Test]
        public void StairwellSchedule_IsRepeatableBoundedAndCoversKinds()
        {
            var kinds =
                new HashSet<StairwellSoundscapeCueKind>();
            for (int sequence = 0; sequence < 192; sequence++)
            {
                StairwellSoundscapeCue first =
                    StairwellSoundscapeSchedule.GetCue(
                        -7193,
                        sequence);
                StairwellSoundscapeCue second =
                    StairwellSoundscapeSchedule.GetCue(
                        -7193,
                        sequence);

                Assert.That(second.Kind, Is.EqualTo(first.Kind));
                Assert.That(
                    second.DelaySeconds,
                    Is.EqualTo(first.DelaySeconds));
                Assert.That(second.Pitch, Is.EqualTo(first.Pitch));
                Assert.That(
                    second.VolumeScale,
                    Is.EqualTo(first.VolumeScale));
                Assert.That(
                    first.DelaySeconds,
                    Is.InRange(
                        StairwellSoundscapeSchedule
                            .MinimumDelaySeconds,
                        StairwellSoundscapeSchedule
                            .MaximumDelaySeconds));
                Assert.That(
                    first.Pitch,
                    Is.InRange(
                        StairwellSoundscapeSchedule.MinimumPitch,
                        StairwellSoundscapeSchedule.MaximumPitch));
                Assert.That(
                    first.VolumeScale,
                    Is.InRange(
                        StairwellSoundscapeSchedule
                            .MinimumVolumeScale,
                        StairwellSoundscapeSchedule
                            .MaximumVolumeScale));
                kinds.Add(first.Kind);
            }

            CollectionAssert.AreEquivalent(
                new[]
                {
                    StairwellSoundscapeCueKind.PipeKnock,
                    StairwellSoundscapeCueKind.MetalStress,
                    StairwellSoundscapeCueKind.DistantWater,
                    StairwellSoundscapeCueKind.DistantMovement
                },
                kinds);
        }

        [Test]
        public void HomeSchedule_IsRepeatableBoundedAndCoversKinds()
        {
            var kinds = new HashSet<HomeSoundscapeCueKind>();
            for (int sequence = 0; sequence < 192; sequence++)
            {
                HomeSoundscapeCue first =
                    HomeSoundscapeSchedule.GetCue(4812, sequence);
                HomeSoundscapeCue second =
                    HomeSoundscapeSchedule.GetCue(4812, sequence);

                Assert.That(second.Kind, Is.EqualTo(first.Kind));
                Assert.That(
                    second.DelaySeconds,
                    Is.EqualTo(first.DelaySeconds));
                Assert.That(second.Pitch, Is.EqualTo(first.Pitch));
                Assert.That(
                    second.VolumeScale,
                    Is.EqualTo(first.VolumeScale));
                Assert.That(
                    first.DelaySeconds,
                    Is.InRange(
                        HomeSoundscapeSchedule.MinimumDelaySeconds,
                        HomeSoundscapeSchedule.MaximumDelaySeconds));
                Assert.That(
                    first.Pitch,
                    Is.InRange(
                        HomeSoundscapeSchedule.MinimumPitch,
                        HomeSoundscapeSchedule.MaximumPitch));
                Assert.That(
                    first.VolumeScale,
                    Is.InRange(
                        HomeSoundscapeSchedule.MinimumVolumeScale,
                        HomeSoundscapeSchedule.MaximumVolumeScale));
                kinds.Add(first.Kind);
            }

            CollectionAssert.AreEquivalent(
                new[]
                {
                    HomeSoundscapeCueKind.SoftWood,
                    HomeSoundscapeCueKind.RadiatorTick,
                    HomeSoundscapeCueKind.RadioMurmur,
                    HomeSoundscapeCueKind.BathroomDetail
                },
                kinds);
        }

        [Test]
        public void NegativeSequences_ResolveToFirstCue()
        {
            StairwellSoundscapeCue stairwellNegative =
                StairwellSoundscapeSchedule.GetCue(72, -18);
            StairwellSoundscapeCue stairwellFirst =
                StairwellSoundscapeSchedule.GetCue(72, 0);
            Assert.That(
                stairwellNegative.Kind,
                Is.EqualTo(stairwellFirst.Kind));
            Assert.That(
                stairwellNegative.DelaySeconds,
                Is.EqualTo(stairwellFirst.DelaySeconds));
            Assert.That(
                stairwellNegative.Pitch,
                Is.EqualTo(stairwellFirst.Pitch));
            Assert.That(
                stairwellNegative.VolumeScale,
                Is.EqualTo(stairwellFirst.VolumeScale));

            HomeSoundscapeCue homeNegative =
                HomeSoundscapeSchedule.GetCue(72, -18);
            HomeSoundscapeCue homeFirst =
                HomeSoundscapeSchedule.GetCue(72, 0);
            Assert.That(
                homeNegative.Kind,
                Is.EqualTo(homeFirst.Kind));
            Assert.That(
                homeNegative.DelaySeconds,
                Is.EqualTo(homeFirst.DelaySeconds));
            Assert.That(
                homeNegative.Pitch,
                Is.EqualTo(homeFirst.Pitch));
            Assert.That(
                homeNegative.VolumeScale,
                Is.EqualTo(homeFirst.VolumeScale));
        }
    }
}
