using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarSoundscapeScheduleTests
    {
        [Test]
        public void GetCue_IsRepeatableForSeedAndSequence()
        {
            for (int sequence = 0; sequence < 32; sequence++)
            {
                BarSoundscapeCue first =
                    BarSoundscapeSchedule.GetCue(9127, sequence);
                BarSoundscapeCue second =
                    BarSoundscapeSchedule.GetCue(9127, sequence);

                Assert.That(second.Kind, Is.EqualTo(first.Kind));
                Assert.That(
                    second.DelaySeconds,
                    Is.EqualTo(first.DelaySeconds));
                Assert.That(second.Pitch, Is.EqualTo(first.Pitch));
                Assert.That(
                    second.VolumeScale,
                    Is.EqualTo(first.VolumeScale));
            }
        }

        [Test]
        public void GetCue_StaysRareBoundedAndUsesBothCueKinds()
        {
            bool foundGlass = false;
            bool foundChair = false;

            for (int sequence = 0; sequence < 128; sequence++)
            {
                BarSoundscapeCue cue =
                    BarSoundscapeSchedule.GetCue(
                        -412,
                        sequence);
                Assert.That(
                    cue.DelaySeconds,
                    Is.InRange(
                        BarSoundscapeSchedule.MinimumDelaySeconds,
                        BarSoundscapeSchedule.MaximumDelaySeconds));
                Assert.That(
                    cue.Pitch,
                    Is.InRange(
                        BarSoundscapeSchedule.MinimumPitch,
                        BarSoundscapeSchedule.MaximumPitch));
                Assert.That(
                    cue.VolumeScale,
                    Is.InRange(
                        BarSoundscapeSchedule.MinimumVolumeScale,
                        BarSoundscapeSchedule.MaximumVolumeScale));
                foundGlass |=
                    cue.Kind ==
                    BarSoundscapeCueKind.GlassClink;
                foundChair |=
                    cue.Kind ==
                    BarSoundscapeCueKind.ChairScrape;
            }

            Assert.That(foundGlass, Is.True);
            Assert.That(foundChair, Is.True);
        }

        [Test]
        public void GetCue_NegativeSequenceUsesFirstCue()
        {
            BarSoundscapeCue negative =
                BarSoundscapeSchedule.GetCue(51, -20);
            BarSoundscapeCue first =
                BarSoundscapeSchedule.GetCue(51, 0);

            Assert.That(negative.Kind, Is.EqualTo(first.Kind));
            Assert.That(
                negative.DelaySeconds,
                Is.EqualTo(first.DelaySeconds));
            Assert.That(negative.Pitch, Is.EqualTo(first.Pitch));
            Assert.That(
                negative.VolumeScale,
                Is.EqualTo(first.VolumeScale));
        }
    }
}
