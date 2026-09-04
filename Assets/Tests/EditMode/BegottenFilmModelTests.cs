using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The projector of the Begotten print, without a GPU: it advances at
    /// about twenty-four pictures a second of unscaled time with the
    /// occasional stick in the gate, every roll stays inside the bounds
    /// the shader is written for, the same seed prints the same film, and
    /// a forced picture prints at once.
    /// </summary>
    public sealed class BegottenFilmModelTests
    {
        private const float GameFrame = 1f / 60f;

        [Test]
        public void Cadence_PrintsAboutTwentyFourPicturesASecondWithStutters()
        {
            BegottenFilmModel film =
                new BegottenFilmModel(BegottenFilmRules.DefaultSeed);
            int printed = 0;
            int hold = 0;
            int longestHold = 0;
            bool previousNew = false;
            for (int frame = 0; frame < 600; frame++)
            {
                BegottenFilmFrame picture = film.Advance(GameFrame);
                if (picture.IsNew)
                {
                    Assert.That(
                        previousNew,
                        Is.False,
                        "At sixty game frames a second the projector " +
                        "never prints twice in a row.");
                    printed++;
                    longestHold = Mathf.Max(longestHold, hold);
                    hold = 0;
                }
                else
                {
                    hold++;
                }

                previousNew = picture.IsNew;
            }

            // Ten seconds: 240 ticks, less the stutters.
            Assert.That(printed, Is.InRange(195, 245));
            Assert.That(film.FramesPresented, Is.EqualTo(printed));
            Assert.That(
                longestHold,
                Is.InRange(5, 13),
                "A stutter holds the picture for two to four extra ticks " +
                "and no longer.");
        }

        [Test]
        public void Rolls_StayInsideTheirBounds()
        {
            BegottenFilmModel film =
                new BegottenFilmModel(BegottenFilmRules.DefaultSeed);
            int slips = 0;
            int scratchesSeen = 0;
            for (int frame = 0; frame < 12000; frame++)
            {
                BegottenFilmFrame picture = film.Advance(GameFrame);
                if (!picture.IsNew)
                {
                    continue;
                }

                Assert.That(picture.Seed, Is.InRange(0f, 997f));
                Assert.That(picture.Threshold, Is.InRange(0.25f, 0.6f));
                Assert.That(picture.Exposure, Is.InRange(0.7f, 2.0f));
                Assert.That(
                    Mathf.Abs(picture.WeaveInternalPixels.x),
                    Is.LessThanOrEqualTo(BegottenFilmRules.WeaveClampPixels));
                Assert.That(
                    Mathf.Abs(picture.WeaveInternalPixels.y),
                    Is.LessThanOrEqualTo(BegottenFilmRules.WeaveClampPixels));
                if (picture.SlipPixels != 0f)
                {
                    slips++;
                    Assert.That(
                        Mathf.Abs(picture.SlipPixels),
                        Is.InRange(
                            BegottenFilmRules.SlipMinimumPixels,
                            BegottenFilmRules.SlipMaximumPixels));
                }

                Assert.That(
                    picture.ActiveScratchCount,
                    Is.LessThanOrEqualTo(BegottenFilmRules.ScratchMaximum));
                for (int index = 0; index < 3; index++)
                {
                    Vector4 scratch = picture.Scratch(index);
                    if (scratch.w < 0.5f)
                    {
                        Assert.That(scratch, Is.EqualTo(Vector4.zero));
                        continue;
                    }

                    scratchesSeen++;
                    Assert.That(scratch.x, Is.InRange(0.02f, 0.98f));
                    Assert.That(Mathf.Abs(scratch.y), Is.EqualTo(1f));
                    Assert.That(scratch.z, Is.InRange(0f, 1f));
                }
            }

            Assert.That(slips, Is.GreaterThan(0), "Frames slip now and then.");
            Assert.That(
                film.FlashesPresented,
                Is.InRange(1, 20),
                "The lamp leaks light now and then, not often.");
            Assert.That(
                scratchesSeen,
                Is.GreaterThan(40),
                "Scratches come and go across two hundred seconds.");
        }

        [Test]
        public void SameSeed_PrintsTheSameFilm()
        {
            BegottenFilmModel first = new BegottenFilmModel(0x1234);
            BegottenFilmModel second = new BegottenFilmModel(0x1234);
            BegottenFilmModel other = new BegottenFilmModel(0x1235);
            bool otherDiffers = false;
            for (int frame = 0; frame < 1000; frame++)
            {
                BegottenFilmFrame a = first.Advance(GameFrame);
                BegottenFilmFrame b = second.Advance(GameFrame);
                BegottenFilmFrame c = other.Advance(GameFrame);
                Assert.That(b.IsNew, Is.EqualTo(a.IsNew));
                Assert.That(b.Seed, Is.EqualTo(a.Seed));
                Assert.That(b.Threshold, Is.EqualTo(a.Threshold));
                Assert.That(b.Exposure, Is.EqualTo(a.Exposure));
                Assert.That(
                    b.WeaveInternalPixels,
                    Is.EqualTo(a.WeaveInternalPixels));
                Assert.That(b.SlipPixels, Is.EqualTo(a.SlipPixels));
                Assert.That(b.Scratch0, Is.EqualTo(a.Scratch0));
                Assert.That(b.Scratch1, Is.EqualTo(a.Scratch1));
                Assert.That(b.Scratch2, Is.EqualTo(a.Scratch2));
                if (a.IsNew && c.IsNew && a.Seed != c.Seed)
                {
                    otherDiffers = true;
                }
            }

            Assert.That(otherDiffers, Is.True);
        }

        [Test]
        public void ForceNewFrame_PrintsOnTheNextAdvance()
        {
            BegottenFilmModel film =
                new BegottenFilmModel(BegottenFilmRules.DefaultSeed);
            Assert.That(
                film.Advance(0f).IsNew,
                Is.True,
                "The first picture prints at once.");
            Assert.That(film.Advance(0f).IsNew, Is.False);

            film.ForceNewFrame();
            BegottenFilmFrame forced = film.Advance(0f);
            Assert.That(forced.IsNew, Is.True);
            Assert.That(film.Advance(0f).IsNew, Is.False);
        }

        [Test]
        public void HeldFrame_RepeatsThePicture()
        {
            BegottenFilmModel film =
                new BegottenFilmModel(BegottenFilmRules.DefaultSeed);
            BegottenFilmFrame printed = film.Advance(GameFrame);
            Assert.That(printed.IsNew, Is.True);
            BegottenFilmFrame held = film.Advance(GameFrame);
            Assert.That(held.IsNew, Is.False);
            Assert.That(held.Seed, Is.EqualTo(printed.Seed));
            Assert.That(held.Threshold, Is.EqualTo(printed.Threshold));
            Assert.That(held.Exposure, Is.EqualTo(printed.Exposure));
            Assert.That(
                held.WeaveInternalPixels,
                Is.EqualTo(printed.WeaveInternalPixels));
            Assert.That(film.Current.IsNew, Is.False);
        }

        [Test]
        public void LongStall_AdvancesOneTickNotABurst()
        {
            BegottenFilmModel film =
                new BegottenFilmModel(BegottenFilmRules.DefaultSeed);
            film.Advance(GameFrame);
            int ticksBefore = film.TicksElapsed;
            BegottenFilmFrame afterStall = film.Advance(5f);
            Assert.That(afterStall.IsNew, Is.True);
            Assert.That(film.TicksElapsed, Is.EqualTo(ticksBefore + 1));
            Assert.That(
                film.Advance(GameFrame).IsNew,
                Is.False,
                "The stall does not queue pictures behind it.");
        }
    }
}
