using System;

namespace BarPromenade
{
    /// <summary>
    /// The optical soundtrack of the Begotten print, as numbers.
    ///
    /// The picture replaces the world with a rephotographed 16 mm print; this
    /// is what that print does to the sound. It is one apparatus, so it is one
    /// mixer effect: the existing sound is folded into a single optical track,
    /// ridden to the track's own modulation standard, torn against the mask,
    /// read through the slit and given back by the little cone in the lid,
    /// while the print's own surface and the projector's transport are added
    /// on top.
    ///
    /// A track is a GEOMETRY, not a channel with a waveshaper on it. The
    /// programme is carried as the width of a clear wedge and recovered as the
    /// area of light through a slit, and the wedge cannot open past the mask
    /// nor close past base fog. Everything harsh about a worn print falls out
    /// of that, and none of it is referenced to dBFS - which is exactly how
    /// the first tuning went wrong. It asked a level-relative <c>tanh</c> to
    /// do the tearing, and at the levels a game bus actually carries that
    /// curve is a straight line: the print measured 1.4-4 % distortion, sat
    /// 7 dB below the world it replaced and was band-limited to a telephone.
    ///
    /// Every number here is read from the one weight the picture already uses,
    /// <c>BegottenModeRamp.Weight</c>, so the sound cannot arrive at a
    /// different moment from the image. The stages answer that weight with
    /// different curves, exactly as the picture's own stages do: the gate
    /// closes and the track folds first, the mask closes EARLY so the track is
    /// tearing well before the fifteen seconds are up, and the surface, the
    /// amplifier's own voice and the apparatus arrive last - so the ramp reads
    /// as "the world is receding" and only then as "it is being projected".
    ///
    /// These mirror the published contract at the top of
    /// <c>tools/audio-vhs/OpticalProcessor.h</c>, which is where the DSP that
    /// obeys them lives. <c>BegottenAudioRulesTests</c> reads that header and
    /// fails the build if the two ever disagree.
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

        // The band, as TPT one-poles. The topology is part of the contract:
        // an exponential pole cannot attenuate past c/(2-c), which at 22050 Hz
        // is -2.9 dB per pole, so the old cascade could never close the slit
        // at that rate at all. The high end is the SLIT itself - 16 mm at 24
        // pictures runs 182.9 mm/s past the head, so a worn 29 um slit puts
        // its first aperture null at 6307 Hz, and four poles there are -3 dB
        // at 2740 Hz. The old three poles at 3800 were -3 dB at 1937 Hz: a
        // telephone, and they threw the harsh band away before the emulsion
        // ever saw it.
        public const int HighpassPoles = 2;
        public const int LowpassPoles = 4;
        public const double HighpassRestHz = 10.0;
        public const double HighpassPrintHz = 300.0;
        public const double LowpassRestHz = 22000.0;
        public const double LowpassPrintHz = 6300.0;

        // Record-side pre-emphasis, never undone. Prints were cut bright to
        // survive the Academy curve and nobody installed a de-emphasis filter:
        // the slit and the horn WERE the de-emphasis. It also decides which
        // band drives the emulsion, so the odd harmonics are made from
        // 1-3 kHz material and land where the slit still passes them.
        public const double PreEmphasisZeroHz = 1200.0;
        public const double PreEmphasisPoleHz = 4800.0;
        public const double PreEmphasisShelf = 4.0;

        // The meter and the dubbing mixer's hand. A film has no opinion about
        // dBFS; it has a slit that is 100 % modulated at a reference level and
        // a mixer riding a PEAK meter to it. Peak, not RMS: an RMS reference
        // is crest-blind, so a 3 dB-crest sine would never reach the knee
        // while a 12 dB-crest bed tore, and the amount of grind would be set
        // by the source's crest factor instead of by the medium.
        public const double ExposureMeterAttackSeconds = 0.010;
        public const double ExposureMeterDecaySeconds = 1.50;
        public const double TargetOvermodulationDb = 9.0;
        public const double ExposureMaxGainDb = 45.0;
        public const double ExposureMinGainDb = -9.0;
        public const double ExposureHoldPeak = 0.002;
        public const double GainRiseDbPerPicture = 0.16;
        public const double GainFallDbPerPicture = 2.00;
        public const double GainArrivalSeconds = 1.50;

        /// <summary>
        /// How the print arrives at the mask: the dub starts this far under
        /// the track's own reference and closes on it as the weight rises. A
        /// FIXED number of decibels rather than a fraction of the mixer's
        /// ride, because the ride depends on how loud the room happens to be
        /// and the shape of an arrival must not: scaled by the ride, a quiet
        /// scene reached the tearing far later in the ramp than a loud one.
        /// Spread linearly in decibels, as the first tuning did, the whole
        /// travel lands in the last quarter of the ramp.
        /// </summary>
        public const double ArrivalHeadroomDb = 20.0;
        public const double DriveArrivalExponent = 0.20;

        /// <summary>
        /// The bands sweep four and a half octaves at the bottom and under two
        /// at the top, so a linear sweep spends most of the ramp where nothing
        /// is audible: at half weight the slit stood at 11.8 kHz and the
        /// highpass at 55 Hz, neither of which can be heard leaving.
        /// </summary>
        public const double BandArrivalExponent = 0.50;

        // The mask, and the printing smear that decides where the trace's
        // edge lands. At rest the mask stands wide open and the emulsion is a
        // straight line; it closes to 100 % modulation as the print arrives,
        // and it closes EARLY. The spread is the real optical defect: printing
        // bleeds the exposure across the edge and widens the clear side more
        // than the dark one, which is the only reason a second harmonic exists
        // at all - an odd-symmetric curve cannot make one at any drive.
        public const double ModulationRest = 1.60;
        public const double ModulationArrivalExponent = 0.55;
        public const double ImageSpread = 0.20;
        public const double PrintToe = 0.12;

        // The emulsion runs at twice the rate with antiderivative
        // antialiasing. Measured on the worst case, naive is -32 dBc in band,
        // either measure alone about -40, and the two together -89, because
        // the residual falls where the half-band's stopband is deepest.
        public const double AdaaEpsilon = 0.000001;
        public const int OversampleFactor = 2;
        public const int HalfBandTaps = 31;
        public const double HalfBandKaiserBeta = 7.0;
        public const int OversamplerLatencySamples = 15;
        public const int LatchFadeSamples = 64;

        // Cross-modulation: THE classic named distortion of an optical track,
        // and the one mechanism that makes this a print rather than a pedal.
        // The printed edge sits where the smeared exposure crosses the stock's
        // threshold, so a high-frequency wiggle's smeared peaks never reach
        // full exposure and the mean edge shifts with the wiggle's amplitude.
        public const double CrossModSplitHz = 2600.0;
        public const double CrossModTopHz = 6000.0;
        public const double CrossModEnvelopeHz = 600.0;
        public const double CrossModBlockHz = 8.0;
        public const double CrossModDepth = 0.45;

        // The exciter lamp on a tired smoothing capacitor modulates the LIGHT,
        // and light follows power, so the ripple is at twice mains. It
        // multiplies rather than adds, which is why a sick projector buzzes
        // rather than hums.
        public const double MainsHz = 50.0;
        public const double LampRippleDepth = 0.07;

        // The recording characteristic and the slit-loss compensation are the
        // AMPLIFIER's voice. They arrive WITH the weight, and the exponent is
        // one because it was measured: at two the mid-ramp loudness moved by a
        // tenth of a LU while the whole bite of the print moved into the last
        // fifth of the arrival, which is what the user heard and objected to.
        // The apparatus still arrives last through the surface and transport
        // exponents, which is where that story belongs.
        public const double AmplifierArrivalExponent = 1.0;
        public const double PresenceHz = 4200.0;
        public const double PresenceQ = 1.60;
        public const double PresenceGainDb = 10.0;

        /// <summary>
        /// The projector's fader, which is FIXED - set once for the room. It
        /// is deliberately not the reciprocal of the mixer's ride: a
        /// reciprocal make-up would pin the output to the bus level, turning
        /// the rail into a permanent second clipper on a hot bus, and it would
        /// be a content-dependent compensation for a band loss. Fixed, the
        /// printed peak is the mask's own height times this fader for ANY
        /// input, so the medium's density sets the level rather than the room.
        /// The price, stated plainly: a bus quieter than the reference prints
        /// LOUDER, bounded by <see cref="LoudestPrintDb"/>. The number is
        /// referenced to the level the GAME carries - the themes sit near
        /// -17.6 LUFS - and not to a synthetic bed six decibels under them,
        /// which is how the first setting put the print 7.3 LU below the
        /// music it was replacing.
        /// </summary>
        public const double ProjectorGainDb = -19.0;

        // On the print but not recorded through it: grain is a property of
        // the developed emulsion and dirt is in the gate, so neither is torn
        // by the mask and neither is scaled by the mixer's hand. Do not tune
        // these by their dB names; tune them by the validator's measured
        // quiet_floor_rms, which is the settled floor as heard.
        public const double SurfaceAmplitude = 0.0141;
        public const double SurfaceExponent = 1.6;
        public const double DustAmplitude = 0.0282;
        public const double DustExponent = 1.6;
        public const double DustPerSecond = 11.0;
        public const double DustDecayPerSecond = 900.0;

        // Not on the print at all: the projector stands in the room with the
        // audience, so it joins after the track has been read.
        public const double TransportAmplitude = 0.0447;
        public const double TransportExponent = 2.2;

        /// <summary>
        /// The frame line crossing the sound slit: a dip once per picture. A
        /// worn print puts a distinct twenty-four per second put-put into its
        /// own track, and this is the strongest tie the ear has to a screen
        /// held at twenty-four pictures. It is a dip, never a gate - the sound
        /// head reads a continuous track, and a stutter would be a lie about
        /// the medium. Deeper and broader than the first tuning, whose 24 Hz
        /// comb line was a quarter of a decibel - well under the roughness
        /// threshold, which is why it read as nothing.
        /// </summary>
        public const double FrameLineDepth = 0.34;
        public const double FrameLineSharpness = 9.0;

        // Wow and flutter as the peak speed error of a tired portable
        // machine: a serviceable one holds 0.3 % and a tired one reaches
        // 1.2 %, and the print is a worn dupe run through a tired machine.
        // The shares sum to one, so the depth IS the peak error.
        //
        // The third term is the point of the model: the picture is held at
        // twenty-four pictures a second, and the intermittent that holds it
        // leaks into the sound drum at exactly that rate, so one term in a
        // sum locks the ear to the eye. Both rates are literals on both sides
        // because the mirror cannot read an expression, and while they were
        // written as products the lock was unmirrored.
        public const double SwimDepth = 0.0125;
        public const double SpoolHz = 0.90;
        public const double SpoolShare = 0.55;
        public const double FlywheelHz = 2.40;
        public const double FlywheelShare = 0.30;
        public const double IntermittentHz = 24.0;
        public const double IntermittentShare = 0.10;
        public const double ShutterHz = 48.0;
        public const double ShutterShare = 0.05;

        /// <summary>
        /// The amplifier's rail: absolute and input-independent, because a
        /// saturating medium's output is bounded by its own geometry rather
        /// than by what it was handed. It has about twelve decibels of
        /// headroom over the print and provably never engages on programme.
        /// </summary>
        public const double OutputCeiling = 0.5012;
        public const double BypassCeiling = 1.0;

        /// <summary>Below this the weight has settled and the DSP takes an
        /// exact, zero-latency bypass.</summary>
        public const double SettledWeight = 0.00001;

        /// <summary>Seconds one picture is held.</summary>
        public static double PictureSeconds => 1.0 / PicturesPerSecond;

        /// <summary>The loudest the print can be against the world it
        /// replaces: the top of the mixer's travel through a fixed fader.</summary>
        public static double LoudestPrintDb => ExposureMaxGainDb + ProjectorGainDb;

        /// <summary>The band closes in log frequency, so the whole sweep is
        /// audible rather than spent in the last second.</summary>
        public static double HighpassHz(double weight) =>
            LogLerp(HighpassRestHz, HighpassPrintHz, BandArrivalAt(weight));

        public static double LowpassHz(double weight) =>
            LogLerp(LowpassRestHz, LowpassPrintHz, BandArrivalAt(weight));

        /// <summary>How far the band has closed at this weight.</summary>
        public static double BandArrivalAt(double weight) =>
            Math.Pow(Clamp01(weight), BandArrivalExponent);

        /// <summary>How far the mixer has ridden at this weight.</summary>
        public static double DriveArrivalAt(double weight) =>
            Math.Pow(Clamp01(weight), DriveArrivalExponent);

        /// <summary>One optical track: the channels fold together.</summary>
        public static double MonoAmount(double weight) => Clamp01(weight);

        /// <summary>
        /// How far the trace may open before the mask stops it. At rest it is
        /// wide and the emulsion is a straight line, so at rest the print does
        /// not distort at all; it closes to full modulation as the print
        /// arrives, and it closes EARLY, which is what puts the tearing well
        /// inside the fifteen seconds without the level having moved.
        /// </summary>
        public static double ModulationLimitAt(double weight) =>
            LogLerp(ModulationRest, 1.0,
                Math.Pow(Clamp01(weight), ModulationArrivalExponent));

        /// <summary>The clear side of the trace, spread wider by printing
        /// than the dark side - the whole source of even harmonics.</summary>
        public static double ClearLimitAt(double weight) =>
            ModulationLimitAt(weight) * (1.0 + ImageSpread);

        public static double DarkLimitAt(double weight) =>
            ModulationLimitAt(weight) * (1.0 - ImageSpread);

        /// <summary>The mixer's hand, as the DSP applies it: his ride in
        /// decibels, arriving with the weight.</summary>
        public static double ExposureGainAt(double weight, double rideDecibels) =>
            Math.Pow(10.0,
                (Clamp(rideDecibels, ExposureMinGainDb, ExposureMaxGainDb) -
                 ArrivalHeadroomDb * (1.0 - DriveArrivalAt(weight))) / 20.0);

        /// <summary>
        /// How far over the track's own reference the dub sits at this weight,
        /// in decibels. This is the number the ear actually follows: below
        /// zero the trace stays inside the mask and the emulsion is a straight
        /// line, and above it the track tears harder the further it goes.
        /// </summary>
        public static double OvermodulationDbAt(double weight) =>
            TargetOvermodulationDb -
            ArrivalHeadroomDb * (1.0 - DriveArrivalAt(weight)) -
            20.0 * Math.Log10(ModulationLimitAt(weight));

        /// <summary>
        /// The projector's fader takes back exactly what the early drive
        /// added, so the PUBLISHED gain across the ramp is untouched by the
        /// drive arriving first: only the ratio the emulsion sees moves.
        /// </summary>
        public static double PublishedGainAt(double weight, double rideDecibels)
        {
            double ride = Clamp(rideDecibels, ExposureMinGainDb, ExposureMaxGainDb);
            return Math.Pow(10.0,
                Clamp01(weight) * (ride + ProjectorGainDb) / 20.0);
        }

        /// <summary>The projector's fixed fader, arriving with the weight.</summary>
        public static double ProjectorGainAt(double weight) =>
            Math.Pow(10.0, Clamp01(weight) * ProjectorGainDb / 20.0);

        /// <summary>The amplifier's own voice arrives last, like the
        /// apparatus, and for the same reason.</summary>
        public static double AmplifierArrivalAt(double weight) =>
            Math.Pow(Clamp01(weight), AmplifierArrivalExponent);

        public static double PreEmphasisShelfAt(double weight) =>
            Math.Pow(PreEmphasisShelf, AmplifierArrivalAt(weight));

        public static double PresenceGainDbAt(double weight) =>
            PresenceGainDb * AmplifierArrivalAt(weight);

        public static double SurfaceLevel(double weight) =>
            SurfaceAmplitude * Math.Pow(Clamp01(weight), SurfaceExponent);

        public static double DustLevel(double weight) =>
            DustAmplitude * Math.Pow(Clamp01(weight), DustExponent);

        public static double TransportLevel(double weight) =>
            TransportAmplitude * Math.Pow(Clamp01(weight), TransportExponent);

        /// <summary>
        /// Peak the print can put over a quiet room. The surface and its dust
        /// are injected before the slit, so the amplifier's own bell lifts
        /// them and the lamp's ripple multiplies them; the apparatus joins
        /// after and is lifted by neither.
        /// </summary>
        public static double QuietCeiling(double weight)
        {
            double bell = Math.Pow(10.0, PresenceGainDbAt(weight) / 20.0);
            return (SurfaceLevel(weight) + DustLevel(weight)) *
                   bell * (1.0 + LampRippleDepth) +
                   TransportLevel(weight);
        }

        /// <summary>
        /// The rail, at this weight. It is absolute - a saturating medium is
        /// bounded by its own geometry, not by what it was handed - and it
        /// never licenses more than full scale at any weight.
        /// </summary>
        public static double OutputCeilingAt(double weight) =>
            LogLerp(BypassCeiling, OutputCeiling, weight);

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

        private static double Clamp01(double value) => Clamp(value, 0.0, 1.0);

        private static double Clamp(double value, double lowest, double highest)
        {
            if (double.IsNaN(value))
            {
                return lowest;
            }

            return value < lowest ? lowest : value > highest ? highest : value;
        }

        private static double LogLerp(double from, double to, double amount)
        {
            double clamped = Clamp01(amount);
            return Math.Exp(
                Math.Log(from) + (Math.Log(to) - Math.Log(from)) * clamped);
        }
    }
}
