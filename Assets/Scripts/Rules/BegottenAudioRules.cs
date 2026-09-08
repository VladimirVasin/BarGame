using System;

namespace BarPromenade
{
    /// <summary>
    /// The optical soundtrack of the Begotten print, as numbers.
    ///
    /// The picture replaces the world with a rephotographed 16 mm print; this
    /// is what that print does to the sound. It is one apparatus, so it is one
    /// mixer effect: the existing sound is folded into a single optical track,
    /// band-limited, made to swim in the gate and squashed, while the print's
    /// own surface and the projector's transport are added on top.
    ///
    /// Every number here is read from the one weight the picture already uses,
    /// <c>BegottenModeRamp.Weight</c>, so the sound cannot arrive at a
    /// different moment from the image. The stages answer that weight with
    /// different curves, exactly as the picture's own stages do: the gate
    /// closes and the track folds first, the swim follows, and the surface and
    /// the apparatus arrive last, so fifteen seconds read as "the world is
    /// receding" and only then as "it is being projected".
    ///
    /// These mirror the published contract at the top of
    /// <c>tools/audio-vhs/OpticalProcessor.h</c>, which is where the DSP that
    /// obeys them lives. <c>BegottenAudioContractTests</c> reads that header
    /// and fails the build if the two ever disagree.
    /// </summary>
    public static class BegottenAudioRules
    {
        /// <summary>The native mixer effect these rules describe.</summary>
        public const string EffectName = "Begotten Optical";

        /// <summary>Names of the effect's three native parameters.</summary>
        public const string NativeWeightParameter = "Weight";
        public const string NativePausedParameter = "Paused";
        public const string NativeResetParameter = "Reset";

        /// <summary>Names the mixer exposes them under.</summary>
        public const string WeightParameter = "BegottenWeight";
        public const string PausedParameter = "BegottenPaused";
        public const string ResetParameter = "BegottenReset";

        // The host weight is already eased and spread over fifteen seconds,
        // so the DSP only de-zippers it rather than ramping it a second time.
        public const double DezipperSeconds = 0.060;
        public const double BaseDelaySeconds = 0.008;

        /// <summary>
        /// The one number the ear can match to the screen: the film is held
        /// at twenty-four pictures a second and so is the transport.
        /// </summary>
        public const double PicturesPerSecond = 24.0;

        // A single pole is far too gentle for a print, so each end of the
        // band is a cascade and these are the corners of one section.
        public const int HighpassPoles = 2;
        public const int LowpassPoles = 3;
        public const double HighpassRestHz = 10.0;
        public const double HighpassPrintHz = 200.0;
        public const double LowpassRestHz = 22000.0;
        public const double LowpassPrintHz = 3800.0;

        // On the print, and therefore shaped by the print's own band. These
        // are PRE-band amplitudes, which is why the first tuning was
        // inaudible: a three-pole gate throws away roughly nine tenths of
        // white noise's energy, so a -42 dBFS hiss reached the ear at about
        // -51 dBFS. The number that matters is the settled quiet floor, and
        // it is measured rather than declared.
        public const double SurfaceAmplitude = 0.0316;
        public const double SurfaceExponent = 1.6;
        public const double DustAmplitude = 0.0631;
        public const double DustExponent = 1.6;
        public const double DustPerSecond = 11.0;
        public const double DustDecayPerSecond = 900.0;

        // Not on the print: the projector stands in the room with the
        // audience, so it joins after the track has been read.
        public const double TransportAmplitude = 0.0282;
        public const double TransportExponent = 2.2;

        /// <summary>
        /// The frame line crossing the sound slit: a dip once per picture. A
        /// worn print puts a distinct twenty-four per second put-put into its
        /// own track, and this is the strongest tie the ear has to a screen
        /// held at twenty-four pictures. It is a dip, never a gate - the sound
        /// head reads a continuous track, and a stutter would be a lie about
        /// the medium.
        /// </summary>
        public const double FrameLineDepth = 0.22;
        public const double FrameLineSharpness = 14.0;

        // Wow and flutter as the peak speed error of a tired portable
        // machine: a serviceable one holds 0.3 % and a tired one reaches
        // 1.2 %, and the print is a worn dupe run through a tired machine.
        // The shares sum to one, so the depth IS the peak error.
        //
        // The third term is the point of the model: the picture is held at
        // twenty-four pictures a second, and the intermittent that holds it
        // leaks into the sound drum at exactly that rate, so one term in a
        // sum locks the ear to the eye.
        public const double SwimDepth = 0.0125;
        public const double SpoolHz = 0.90;
        public const double SpoolShare = 0.55;
        public const double FlywheelHz = 2.40;
        public const double FlywheelShare = 0.30;
        public const double IntermittentHz = PicturesPerSecond;
        public const double IntermittentShare = 0.10;
        public const double ShutterHz = PicturesPerSecond * 2.0;
        public const double ShutterShare = 0.05;

        // No make-up gain. The print is quieter and denser, and Unity's
        // master headroom stays authoritative, as it does for the tape.
        public const double SaturationDrive = 1.80;
        public const double CompressorThreshold = 0.1259;
        public const double CompressorRatio = 2.5;
        public const double CompressorAttackSeconds = 0.008;
        public const double CompressorReleaseSeconds = 0.220;

        /// <summary>Below this the weight has settled and the DSP takes an
        /// exact, zero-latency bypass.</summary>
        public const double SettledWeight = 0.00001;

        /// <summary>Seconds one picture is held.</summary>
        public static double PictureSeconds => 1.0 / PicturesPerSecond;

        /// <summary>The band closes in log frequency, so the whole sweep is
        /// audible rather than spent in the last second.</summary>
        public static double HighpassHz(double weight) =>
            LogLerp(HighpassRestHz, HighpassPrintHz, weight);

        public static double LowpassHz(double weight) =>
            LogLerp(LowpassRestHz, LowpassPrintHz, weight);

        /// <summary>One optical track: the channels fold together.</summary>
        public static double MonoAmount(double weight) => Clamp01(weight);

        public static double SurfaceLevel(double weight) =>
            SurfaceAmplitude * Math.Pow(Clamp01(weight), SurfaceExponent);

        public static double DustLevel(double weight) =>
            DustAmplitude * Math.Pow(Clamp01(weight), DustExponent);

        public static double TransportLevel(double weight) =>
            TransportAmplitude * Math.Pow(Clamp01(weight), TransportExponent);

        /// <summary>Peak the generated print can add on top of the input, and
        /// therefore the bound the native validator holds it to.</summary>
        public static double NoiseCeiling(double weight) =>
            SurfaceLevel(weight) + DustLevel(weight) + TransportLevel(weight);

        /// <summary>At rest the track is not compressed at all.</summary>
        public static double CompressorRatioAt(double weight) =>
            1.0 + Clamp01(weight) * (CompressorRatio - 1.0);

        /// <summary>
        /// How much of the drunkenness tape survives under the print.
        ///
        /// None of it. The two are mutually exclusive by design - the print
        /// is the mode a hangover will drive, and a hangover is not
        /// drunkenness - so the tape leaves exactly as far as the print has
        /// arrived, and a player who somehow holds both hears one apparatus
        /// rather than two stacked on each other.
        /// </summary>
        public static float TapeShare(double weight) =>
            (float)(1.0 - Clamp01(weight));

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value))
            {
                return 0.0;
            }

            return value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
        }

        private static double LogLerp(double from, double to, double amount)
        {
            double clamped = Clamp01(amount);
            return Math.Exp(
                Math.Log(from) + (Math.Log(to) - Math.Log(from)) * clamped);
        }
    }
}
