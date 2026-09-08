using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The print in the ear: the same weight that prints the picture, answered
    /// by a chain whose two ends are exact and whose stages arrive in the order
    /// the fifteen seconds are meant to read in.
    /// </summary>
    public sealed class BegottenAudioRulesTests
    {
        private static readonly double[] Between =
        {
            0.05, 0.15, 0.25, 0.4, 0.5, 0.6, 0.75, 0.85, 0.95
        };

        [Test]
        public void AtRest_TheSoundIsTheOrdinarySound()
        {
            Assert.That(
                BegottenAudioRules.HighpassHz(0.0),
                Is.EqualTo(BegottenAudioRules.HighpassRestHz).Within(1e-9));
            Assert.That(
                BegottenAudioRules.LowpassHz(0.0),
                Is.EqualTo(BegottenAudioRules.LowpassRestHz).Within(1e-9));
            Assert.That(BegottenAudioRules.MonoAmount(0.0), Is.Zero);
            Assert.That(BegottenAudioRules.SurfaceLevel(0.0), Is.Zero);
            Assert.That(BegottenAudioRules.DustLevel(0.0), Is.Zero);
            Assert.That(BegottenAudioRules.TransportLevel(0.0), Is.Zero);
            Assert.That(BegottenAudioRules.NoiseCeiling(0.0), Is.Zero);
            Assert.That(
                BegottenAudioRules.CompressorRatioAt(0.0),
                Is.EqualTo(1.0).Within(1e-9),
                "At rest the track must not be compressed at all.");
        }

        [Test]
        public void AtFullStrength_TheSoundIsThePrint()
        {
            Assert.That(
                BegottenAudioRules.HighpassHz(1.0),
                Is.EqualTo(BegottenAudioRules.HighpassPrintHz).Within(1e-6));
            Assert.That(
                BegottenAudioRules.LowpassHz(1.0),
                Is.EqualTo(BegottenAudioRules.LowpassPrintHz).Within(1e-6));
            Assert.That(
                BegottenAudioRules.MonoAmount(1.0),
                Is.EqualTo(1.0),
                "A 16 mm release print carries one optical track.");
            Assert.That(
                BegottenAudioRules.SurfaceLevel(1.0),
                Is.EqualTo(BegottenAudioRules.SurfaceAmplitude).Within(1e-9));
            Assert.That(
                BegottenAudioRules.TransportLevel(1.0),
                Is.EqualTo(BegottenAudioRules.TransportAmplitude).Within(1e-9));
            Assert.That(
                BegottenAudioRules.CompressorRatioAt(1.0),
                Is.EqualTo(BegottenAudioRules.CompressorRatio).Within(1e-9));
        }

        [Test]
        public void TheGateCloses_MonotonicallyAndFromBothEnds()
        {
            double previousLowpass = BegottenAudioRules.LowpassHz(0.0);
            double previousHighpass = BegottenAudioRules.HighpassHz(0.0);
            for (int step = 1; step <= 100; step++)
            {
                double weight = step / 100.0;
                double lowpass = BegottenAudioRules.LowpassHz(weight);
                double highpass = BegottenAudioRules.HighpassHz(weight);
                Assert.That(
                    lowpass,
                    Is.LessThan(previousLowpass),
                    $"The slit widens again at {weight}.");
                Assert.That(
                    highpass,
                    Is.GreaterThan(previousHighpass),
                    $"The low end comes back at {weight}.");
                previousLowpass = lowpass;
                previousHighpass = highpass;
            }
        }

        [Test]
        public void TheBandCloses_InLogFrequency()
        {
            // Interpolated linearly, half the ramp would sit above hearing and
            // the arrival would be inaudible for most of the fifteen seconds.
            // The geometric mean is the whole point of the log sweep.
            Assert.That(
                BegottenAudioRules.LowpassHz(0.5),
                Is.EqualTo(Math.Sqrt(
                    BegottenAudioRules.LowpassRestHz *
                    BegottenAudioRules.LowpassPrintHz)).Within(1e-6));
            Assert.That(
                BegottenAudioRules.LowpassHz(0.5),
                Is.LessThan(
                    (BegottenAudioRules.LowpassRestHz +
                     BegottenAudioRules.LowpassPrintHz) * 0.5),
                "A log sweep must sit below the linear midpoint.");
        }

        [Test]
        public void TheStagesArrive_InTheOrderTheFifteenSecondsRead()
        {
            // The world recedes first, then its surface shows, then the
            // apparatus does - the audio counterpart of the gate narrowing
            // before the grain boils.
            foreach (double weight in Between)
            {
                double gate = weight;
                double surface =
                    BegottenAudioRules.SurfaceLevel(weight) /
                    BegottenAudioRules.SurfaceAmplitude;
                double apparatus =
                    BegottenAudioRules.TransportLevel(weight) /
                    BegottenAudioRules.TransportAmplitude;

                Assert.That(
                    surface,
                    Is.LessThan(gate),
                    $"The surface arrives no later than the gate at {weight}.");
                Assert.That(
                    apparatus,
                    Is.LessThan(surface),
                    $"The apparatus arrives no later than the surface at {weight}.");
            }
        }

        [Test]
        public void TheSwimSharesSumToOne_SoTheDepthIsThePeakError()
        {
            double shares =
                BegottenAudioRules.SpoolShare +
                BegottenAudioRules.FlywheelShare +
                BegottenAudioRules.IntermittentShare +
                BegottenAudioRules.ShutterShare;
            Assert.That(
                shares,
                Is.EqualTo(1.0).Within(1e-9),
                "The published depth is only the peak speed error if the " +
                "component shares sum to one.");
            Assert.That(
                BegottenAudioRules.SwimDepth,
                Is.InRange(0.003, 0.020),
                "A serviceable projector holds 0.3 % and a tired one 1.2 %; " +
                "the print is a worn dupe through a tired machine.");
        }

        [Test]
        public void TheSwimIsLockedToThePictureRate()
        {
            // This is the one number the ear can match to the eye: the screen
            // is held at twenty-four pictures a second and the sound swims at
            // exactly that rate, because the same intermittent does both.
            Assert.That(
                BegottenAudioRules.IntermittentHz,
                Is.EqualTo(BegottenAudioRules.PicturesPerSecond));
            Assert.That(
                BegottenAudioRules.ShutterHz,
                Is.EqualTo(BegottenAudioRules.PicturesPerSecond * 2.0));
            Assert.That(
                BegottenAudioRules.PictureSeconds,
                Is.EqualTo(1.0 / 24.0).Within(1e-12));
        }

        [Test]
        public void TheTapeLeaves_ExactlyAsFarAsThePrintArrives()
        {
            // The two apparatus are mutually exclusive, so they are never
            // stacked on one another.
            Assert.That(BegottenAudioRules.TapeShare(0.0), Is.EqualTo(1f));
            Assert.That(BegottenAudioRules.TapeShare(1.0), Is.EqualTo(0f));
            Assert.That(
                BegottenAudioRules.TapeShare(0.25),
                Is.EqualTo(0.75f).Within(1e-6f));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(-1.0)]
        [TestCase(2.0)]
        public void ABrokenWeight_StaysInsideTheContract(double weight)
        {
            Assert.That(BegottenAudioRules.MonoAmount(weight), Is.InRange(0.0, 1.0));
            Assert.That(BegottenAudioRules.SurfaceLevel(weight), Is.InRange(
                0.0, BegottenAudioRules.SurfaceAmplitude));
            Assert.That(BegottenAudioRules.TransportLevel(weight), Is.InRange(
                0.0, BegottenAudioRules.TransportAmplitude));
            Assert.That(BegottenAudioRules.LowpassHz(weight), Is.InRange(
                BegottenAudioRules.LowpassPrintHz,
                BegottenAudioRules.LowpassRestHz));
            Assert.That(BegottenAudioRules.HighpassHz(weight), Is.InRange(
                BegottenAudioRules.HighpassRestHz,
                BegottenAudioRules.HighpassPrintHz));
            Assert.That(BegottenAudioRules.TapeShare(weight), Is.InRange(0f, 1f));
        }

        [Test]
        public void TheRules_MirrorTheNativeHeader()
        {
            // These rules exist to stand where the DSP stands. The DSP is C++
            // and cannot be called from here, so the two share these numbers
            // by being read against each other - a retune of either side fails
            // the build rather than a listening session.
            string headerPath = Path.GetFullPath(Path.Combine(
                Application.dataPath, "../tools/audio-vhs/OpticalProcessor.h"));
            Assert.That(
                File.Exists(headerPath),
                Is.True,
                "The native optical processor must live at " + headerPath);

            Dictionary<string, double> native = ReadPublishedConstants(headerPath);
            Assert.That(
                native, Is.Not.Empty,
                "No published constants were found in the native header.");

            foreach (KeyValuePair<string, double> pair in Expected())
            {
                Assert.That(
                    native.ContainsKey(pair.Key),
                    Is.True,
                    $"The native header no longer publishes '{pair.Key}'.");
                Assert.That(
                    native[pair.Key],
                    Is.EqualTo(pair.Value).Within(1e-9),
                    $"'{pair.Key}' disagrees between the header and the rules.");
            }
        }

        private static Dictionary<string, double> Expected()
        {
            return new Dictionary<string, double>
            {
                { "DezipperSeconds", BegottenAudioRules.DezipperSeconds },
                { "BaseDelaySeconds", BegottenAudioRules.BaseDelaySeconds },
                { "PicturesPerSecond", BegottenAudioRules.PicturesPerSecond },
                { "HighpassPoles", BegottenAudioRules.HighpassPoles },
                { "LowpassPoles", BegottenAudioRules.LowpassPoles },
                { "HighpassRestHz", BegottenAudioRules.HighpassRestHz },
                { "HighpassPrintHz", BegottenAudioRules.HighpassPrintHz },
                { "LowpassRestHz", BegottenAudioRules.LowpassRestHz },
                { "LowpassPrintHz", BegottenAudioRules.LowpassPrintHz },
                { "SurfaceAmplitude", BegottenAudioRules.SurfaceAmplitude },
                { "SurfaceExponent", BegottenAudioRules.SurfaceExponent },
                { "DustAmplitude", BegottenAudioRules.DustAmplitude },
                { "DustExponent", BegottenAudioRules.DustExponent },
                { "DustPerSecond", BegottenAudioRules.DustPerSecond },
                { "DustDecayPerSecond", BegottenAudioRules.DustDecayPerSecond },
                { "TransportAmplitude", BegottenAudioRules.TransportAmplitude },
                { "TransportExponent", BegottenAudioRules.TransportExponent },
                { "FrameLineDepth", BegottenAudioRules.FrameLineDepth },
                { "FrameLineSharpness", BegottenAudioRules.FrameLineSharpness },
                { "SwimDepth", BegottenAudioRules.SwimDepth },
                { "SpoolHz", BegottenAudioRules.SpoolHz },
                { "SpoolShare", BegottenAudioRules.SpoolShare },
                { "FlywheelHz", BegottenAudioRules.FlywheelHz },
                { "FlywheelShare", BegottenAudioRules.FlywheelShare },
                { "IntermittentShare", BegottenAudioRules.IntermittentShare },
                { "ShutterShare", BegottenAudioRules.ShutterShare },
                { "SaturationDrive", BegottenAudioRules.SaturationDrive },
                { "CompressorThreshold", BegottenAudioRules.CompressorThreshold },
                { "CompressorRatio", BegottenAudioRules.CompressorRatio },
                { "CompressorAttackSeconds", BegottenAudioRules.CompressorAttackSeconds },
                { "CompressorReleaseSeconds", BegottenAudioRules.CompressorReleaseSeconds },
                { "SettledWeight", BegottenAudioRules.SettledWeight }
            };
        }

        private static Dictionary<string, double> ReadPublishedConstants(string path)
        {
            var found = new Dictionary<string, double>(StringComparer.Ordinal);
            var pattern = new Regex(
                @"static\s+constexpr\s+(?:double|int)\s+(\w+)\s*=\s*" +
                @"(-?\d+(?:\.\d+)?)\s*;");
            foreach (Match match in pattern.Matches(File.ReadAllText(path)))
            {
                found[match.Groups[1].Value] = double.Parse(
                    match.Groups[2].Value, CultureInfo.InvariantCulture);
            }

            return found;
        }
    }
}
