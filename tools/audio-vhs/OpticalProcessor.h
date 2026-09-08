#pragma once

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <vector>

namespace bar_audio
{
// The optical soundtrack of the Begotten print. Unlike the tape, this
// processor also SPEAKS: a print has a surface and a projector has a
// transport, and both are audible over silence. Everything it generates is
// mono, because a 16 mm optical track is one track.
//
// The track is a GEOMETRY, not a channel with a waveshaper on it. A
// variable-area trace carries the programme as the width of a clear wedge and
// gives it back as the area of light through a slit, and it is bounded by the
// mask on one side and by base fog on the other. Everything harsh about a
// worn print falls out of that: the mixer rides the dub to a fixed modulation
// standard, the emulsion clips what he hands it against a mask edge spread
// wider on the clear side than the dark, and the slit and the little cone in
// the lid read the result. The first tuning missed this and asked a
// level-relative tanh to do the work; at bus levels tanh(2.8x)/2.8 is a
// straight line, and the print measured 1.4-4 % distortion, 7 dB quiet and
// dull. Distortion here is a property of the MEDIUM, so it must be referenced
// to the medium's own 100 % modulation and never to dBFS.
//
// Memory is allocated at instance creation, never from the audio callback.
// A settled zero weight is an exact, zero-latency bypass.
class OpticalProcessor
{
public:
    static constexpr int MaxChannels = 8;

    // ---- Published contract. BegottenAudioRules.cs mirrors these, and
    // ---- BegottenAudioRulesTests reads this header to prove it. Every one
    // ---- of them must stay a PLAIN LITERAL: the mirror's regex accepts no
    // ---- expression, and a constant written as one is invisible to it and
    // ---- silently unmirrored.
    // The host weight is already eased and spread over fifteen seconds, so
    // the internal smoothing is only a de-zipper, not a second ramp.
    static constexpr double DezipperSeconds = 0.060;
    // The pitch-swim delay line needs a non-zero centre to swim about.
    static constexpr double BaseDelaySeconds = 0.008;
    // The one number the ear can match to the screen.
    static constexpr double PicturesPerSecond = 24.0;
    // The optical band, interpolated in log frequency, as TPT one-poles. The
    // topology is not cosmetic: the exponential pole y += c(x-y) has a
    // stopband FLOOR of c/(2-c), which at 22050 Hz is -2.9 dB per pole, so
    // four of them could never attenuate more than 12 dB and the print would
    // have been bright and full-band at that rate. TPT puts a zero at Nyquist
    // and prewarps the corner, so the published number means the same thing
    // at 22050, 44100, 48000 and 96000.
    //
    // The high end is the SLIT: 16 mm at 24 pictures runs 182.9 mm/s past the
    // sound head, so a worn, badly focused 29 um slit puts its first aperture
    // null at 182.9/0.029 = 6307 Hz. Four cascaded poles there are -3 dB at
    // 2740 Hz, within 2 % of the true sinc aperture, while replacing the
    // sinc's lobes with an honest monotone roll. The old three poles at 3800
    // were -3 dB at 1937 Hz - a telephone, not a slit, and they threw the
    // harsh band away before the emulsion ever saw it.
    static constexpr int HighpassPoles = 2;   // one before the emulsion, one after
    static constexpr int LowpassPoles = 4;    // the slit
    static constexpr double HighpassRestHz = 10.0;
    static constexpr double HighpassPrintHz = 300.0;
    static constexpr double LowpassRestHz = 22000.0;
    static constexpr double LowpassPrintHz = 6300.0;
    // Record-side pre-emphasis, never undone. Prints were cut bright to
    // survive the Academy curve, and nobody installed a de-emphasis filter -
    // the slit and the horn WERE the de-emphasis, and they are stages of
    // their own below. It also decides which band drives the emulsion: the
    // mids arrive 9.5 dB hot, so the odd harmonics are made from 1-3 kHz
    // material and land where the slit still passes them.
    static constexpr double PreEmphasisZeroHz = 1200.0;
    static constexpr double PreEmphasisPoleHz = 4800.0;
    static constexpr double PreEmphasisShelf = 4.0;   // pole / zero, +12 dB
    // The modulation meter and the dubbing mixer's hand. A film has no
    // opinion about dBFS; it has a slit that is 100 % modulated at a
    // reference level, and a mixer who rides a PEAK meter to it. A peak
    // reference is crest-invariant, which an RMS one is not: at an RMS
    // reference a 3 dB-crest sine would never reach the knee while a
    // 12 dB-crest bed tore, and the amount of grind would be set by the
    // source's crest factor rather than by the medium.
    static constexpr double ExposureMeterAttackSeconds = 0.010;
    static constexpr double ExposureMeterDecaySeconds = 1.50;
    static constexpr double TargetOvermodulationDb = 9.0;
    static constexpr double ExposureMaxGainDb = 45.0;
    static constexpr double ExposureMinGainDb = -9.0;
    // Below this peak the hand HOLDS rather than chasing a quiet room up to
    // the top of its travel.
    static constexpr double ExposureHoldPeak = 0.002;
    // The hand is a slew in dB, not a filter: it makes the arrival's
    // per-picture step a theorem instead of a hope. 0.16 dB per picture is
    // 3.84 dB/s, a plausible hand on a fader; it may come down fast.
    static constexpr double GainRiseDbPerPicture = 0.16;
    static constexpr double GainFallDbPerPicture = 2.00;
    static constexpr double GainArrivalSeconds = 1.50;
    // How the print arrives at the mask: the dub starts this far under the
    // track's own reference and closes on it as the weight rises.
    //
    // A FIXED number of decibels, not a fraction of the mixer's ride, because
    // the ride depends on how loud the room happens to be and the shape of an
    // arrival must not. Scaled by the ride instead, a quiet scene reached the
    // tearing much later in the ramp than a loud one, for no reason the player
    // could see. Twenty decibels is what the arrival travels, and the exponent
    // puts the first tearing at about a seventh of the way in and then grows
    // it steadily - which is what the ear reads as a print being struck rather
    // than as a switch thrown at the end. Spread linearly in decibels, as the
    // first tuning did, the whole travel lands in the last quarter: at half
    // weight the dub was still twelve decibels under the reference and nothing
    // could tear however wide the mask stood, which is exactly what the user
    // heard and objected to.
    static constexpr double ArrivalHeadroomDb = 20.0;
    static constexpr double DriveArrivalExponent = 0.20;
    // The bands sweep in log frequency over four and a half octaves at the
    // bottom and under two at the top, so a linear sweep spends most of the
    // ramp where nothing is audible: at half weight the slit stood at 11.8 kHz
    // and the highpass at 55 Hz, neither of which can be heard leaving.
    static constexpr double BandArrivalExponent = 0.50;
    // The mask. At rest it stands wide open and the emulsion is a straight
    // line; it closes to 100 % modulation as the print arrives, and it closes
    // EARLY, so the track is tearing by the ninth of the fifteen seconds
    // without the level having moved.
    static constexpr double ModulationRest = 1.60;
    static constexpr double ModulationArrivalExponent = 0.55;
    // Printing spreads the exposure across the trace's edge, and it widens
    // the clear side more than the dark one. That asymmetry is the only
    // reason a second harmonic exists at all - tanh is odd-symmetric and
    // cannot make one at any drive - and it is what makes the print blare
    // rather than hum.
    static constexpr double ImageSpread = 0.20;
    // The H&D shoulder's half-width. It costs almost nothing in distortion
    // and buys its alias reduction in the high-order harmonics.
    static constexpr double PrintToe = 0.12;
    // Antiderivative antialiasing, and the guard for the near-zero
    // difference where the quotient is ill-conditioned.
    static constexpr double AdaaEpsilon = 0.000001;
    // Measured on the worst case (5.5 kHz driven 10 dB over): naive 1x is
    // -32 dBc in band, ADAA alone -47, 2x alone -36, and the two together
    // -89, because ADAA's residual falls as (f/Fs)^2 and lands where the
    // half-band's stopband is deepest. -47 dBc is not enough: that residue is
    // inharmonic and moves DOWNWARD as the note goes up, which is the
    // unmistakable signature of digital fizz rather than a print.
    static constexpr int OversampleFactor = 2;
    static constexpr int HalfBandTaps = 31;
    static constexpr double HalfBandKaiserBeta = 7.0;
    static constexpr int OversamplerLatencySamples = 15;
    // The wet path carries that latency and its bands are never quite the dry
    // ones, so the exact bypass is reached through a short crossfade rather
    // than a hard switch: without it every disable was a 0.31 ms time jump.
    static constexpr int LatchFadeSamples = 64;
    // Cross-modulation: THE classic named distortion of an optical track and
    // the one mechanism that makes this a print rather than a pedal. The
    // printed edge sits where the smeared exposure crosses the stock's
    // threshold, so a high-frequency wiggle's smeared peaks never reach full
    // exposure and the mean edge position shifts with the wiggle's AMPLITUDE.
    // That is envelope rectification, and it is what the lab cross-modulation
    // test measures. The detector is BANDED, not merely split: rectifying
    // anything above rate/6 folds dense inharmonic products back into the
    // passband, worst at 22050.
    static constexpr double CrossModSplitHz = 2600.0;
    static constexpr double CrossModTopHz = 6000.0;
    static constexpr double CrossModEnvelopeHz = 600.0;
    static constexpr double CrossModBlockHz = 8.0;
    static constexpr double CrossModDepth = 0.45;
    // The exciter lamp on a dried-out smoothing capacitor modulates the
    // LIGHT, and light follows power, so the ripple is at twice mains. It
    // multiplies transmitted light rather than adding to it, which is why a
    // sick projector buzzes rather than hums: every partial gets sidebands,
    // and the hum is loudest where the track is most open.
    static constexpr double MainsHz = 50.0;
    static constexpr double LampRippleDepth = 0.07;
    // The slit-loss compensation every 16 mm amplifier carried, and the small
    // elliptical cone in the pressed-steel lid breaking up over the same
    // band. This is the cheapest sharpness per operation in the machine: it
    // sits where the aperture loss is steepest but still recoverable, and
    // where the ear's own sharpness weighting has begun to rise.
    // The recording characteristic and the slit-loss compensation are both
    // the AMPLIFIER's voice, not the film's, so they arrive last - as the
    // transport does, and for the same reason. Arriving with the weight they
    // added their +12 and +10 dB while the slit was still open and the mask
    // still wide, and the middle of the arrival measured 1.8 LU LOUDER than
    // the world it was replacing: the print got louder before it got harsh.
    // One, and measured rather than assumed: it was 2.0 to hold down a swell
    // in the middle of the ramp, but the swell was never the amplifier's - at
    // 2.0 the mid-ramp loudness moved by 0.1 LU while the whole bite of the
    // print moved into the last fifth of the arrival. The apparatus still
    // arrives last through the surface and transport exponents, which is where
    // that story belongs.
    static constexpr double AmplifierArrivalExponent = 1.0;
    static constexpr double PresenceHz = 4200.0;
    static constexpr double PresenceQ = 1.60;
    static constexpr double PresenceGainDb = 10.0;
    // The projector's fader is FIXED - it is set once for the room. It is
    // deliberately NOT the reciprocal of the exposure ride: a reciprocal
    // make-up would pin the output peak to the bus level and turn the rail
    // into a permanent second clipper on a hot bus, and it would be a
    // content-dependent compensation for a band loss. Fixed, the printed peak
    // is the mask's own height times this fader for ANY input - a whisper, a
    // full-scale square, noise - so the medium's density sets the level. The
    // price, stated plainly: a bus quieter than the reference prints LOUDER,
    // bounded by ExposureMaxGainDb + ProjectorGainDb.
    //
    // The number is referenced to the level the GAME carries and not to the
    // validator's bed. The themes sit near -17.6 LUFS and the bed is six
    // decibels under them, so a fader set on the bed put the print 7.3 LU
    // below the music it was replacing - which reads as the game turning
    // itself down rather than as a projector standing in the room. Set here,
    // the print lands about three LU under the themes and its peak still sits
    // under the master compressor's own threshold.
    static constexpr double ProjectorGainDb = -19.0;
    // The print's own surface, injected AFTER the fader and in output units:
    // grain is a property of the developed emulsion and dirt is in the gate,
    // so neither is recorded through the characteristic curve and neither is
    // scaled by the mixer's hand. Tune these by the validator's measured
    // quiet_floor_rms, never by their dB names.
    static constexpr double SurfaceAmplitude = 0.0141;
    static constexpr double SurfaceExponent = 1.6;
    static constexpr double DustAmplitude = 0.0282;
    static constexpr double DustExponent = 1.6;
    static constexpr double DustPerSecond = 11.0;
    static constexpr double DustDecayPerSecond = 900.0;
    // The apparatus stands in the room with the audience rather than on the
    // film, so it is neither slit-limited nor clipped nor dimmed by the frame
    // line, and it is half of the 24 Hz envelope the print puts over silence.
    static constexpr double TransportAmplitude = 0.0447;
    static constexpr double TransportExponent = 2.2;
    // The frame line crossing the sound slit. A worn print puts a distinct
    // twenty-four per second put-put into its own track, and this is the
    // strongest tie the ear has to a screen held at twenty-four pictures: it
    // is not a gate and not a stutter, only a dip once per picture. Deeper
    // and broader than the first tuning, whose 24 Hz comb line was 0.25 dB -
    // well under the roughness threshold, which is why it read as nothing.
    static constexpr double FrameLineDepth = 0.34;
    static constexpr double FrameLineSharpness = 9.0;
    // Wow and flutter, as the peak speed error of a tired portable machine.
    // A serviceable projector holds 0.3 %, a tired one reaches 1.2 %; the
    // print is a worn dupe run through a tired machine, so it takes the tired
    // figure. The shares sum to one, so SwimDepth IS the peak error: 1.25 %
    // is 1200*log2(1.0125) = 21 cents.
    //
    // The third term is the point of the whole model. The picture is HELD at
    // twenty-four pictures a second, and the intermittent that holds it leaks
    // into the sound drum at exactly that rate. One term in a sum locks the
    // ear to the eye. Both rates are literals rather than expressions
    // because the mirror's regex cannot see an expression, and while they
    // were written as products the lock was unmirrored and 24 could have been
    // retuned to 25 with a green build.
    static constexpr double SwimDepth = 0.0125;
    static constexpr double SpoolHz = 0.90;          // spool eccentricity
    static constexpr double SpoolShare = 0.55;
    static constexpr double FlywheelHz = 2.40;       // flywheel and sound drum
    static constexpr double FlywheelShare = 0.30;
    static constexpr double IntermittentHz = 24.0;   // = PicturesPerSecond
    static constexpr double IntermittentShare = 0.10;
    static constexpr double ShutterHz = 48.0;        // = PicturesPerSecond * 2
    static constexpr double ShutterShare = 0.05;
    // The amplifier's rail: absolute and input-independent, because a
    // saturating medium's output is bounded by its own geometry rather than
    // by what it was given. With a fixed fader the print peaks near 0.12 plus
    // its noise floor for ANY input, so the rail has about twelve dB of
    // headroom and provably never engages on programme. It stays hard - a
    // rail is hard - and it never licenses more than 0 dBFS at any weight.
    static constexpr double OutputCeiling = 0.5012;  // -6 dBFS
    static constexpr double BypassCeiling = 1.0;
    // Below this the weight has settled and the exact bypass is taken.
    static constexpr double SettledWeight = 0.00001;

    explicit OpticalProcessor(int sampleRate)
        : rate_(std::max(8000, sampleRate)),
          frames_(std::max(1024, rate_ / 4)),
          history_(static_cast<size_t>(frames_) * MaxChannels, 0.0f)
    {
        BuildHalfBand();
        Reset();
    }

    void Reset()
    {
        written_ = 0;
        clock_ = 0;
        smoothed_ = 0;
        picturePhase_ = 0;
        dustAge_ = -1;
        dustGain_ = 0;
        random_ = 0x51A7C39Bu;
        // A fresh instance is already latched dry, so a rest weight takes the
        // exact bypass on its very first sample rather than fading into it.
        latchFade_ = LatchFadeSamples;
        meter_ = 0;
        ride_ = 0;
        rideTarget_ = 0;
        acquired_ = false;
        gainWeight_ = 0;
        crossLow_ = 0;
        crossTop_ = 0;
        crossEnvelope_ = 0;
        crossBase_ = 0;
        highpassOne_.fill(0);
        highpassTwo_.fill(0);
        preEmphasis_.fill(0);
        preEmphasisDetector_ = 0;
        for (auto& pole : slit_) pole.fill(0);
        presenceOne_.fill(0);
        presenceTwo_.fill(0);
        previous_.fill(0);
        for (auto& ring : source_) ring.fill(0);
        for (auto& ring : even_) ring.fill(0);
        for (auto& ring : odd_) ring.fill(0);
        std::fill(history_.begin(), history_.end(), 0.0f);
        previousChannels_ = 0;
    }

    void Process(const float* input, float* output, unsigned int frames,
                 int inputChannels, int outputChannels, float weight, bool paused)
    {
        weight = std::isfinite(weight) ? std::clamp(weight, 0.0f, 1.0f) : 0;
        if (paused)
        {
            Reset();
            std::fill_n(output, static_cast<size_t>(frames) * outputChannels, 0.0f);
            return;
        }
        if (inputChannels < 1 || outputChannels < 1 ||
            inputChannels > MaxChannels || outputChannels > MaxChannels)
        {
            Reset();
            Copy(input, output, frames, inputChannels, outputChannels);
            return;
        }
        // Silence is NOT a reason to bypass here: the projector runs over a
        // quiet room too. Only a settled zero weight is, and only once the
        // latch has finished fading the wet path's own latency away.
        const bool settled = weight == 0 && smoothed_ < SettledWeight;
        if (settled && latchFade_ >= LatchFadeSamples)
        {
            Reset();
            Copy(input, output, frames, inputChannels, outputChannels);
            return;
        }
        if (previousChannels_ != 0 && previousChannels_ != inputChannels)
            Reset();
        previousChannels_ = inputChannels;

        const double inverseRate = 1.0 / rate_;
        const double dezipper = 1.0 - std::exp(-inverseRate / DezipperSeconds);
        const double samplesPerPicture = rate_ / PicturesPerSecond;
        const double meterAttack = 1.0 - std::exp(-inverseRate / ExposureMeterAttackSeconds);
        const double meterDecay = 1.0 - std::exp(-inverseRate / ExposureMeterDecaySeconds);
        const double riseStep = GainRiseDbPerPicture / samplesPerPicture;
        const double fallStep = GainFallDbPerPicture / samplesPerPicture;
        const double arrivalStep = 1.0 / (rate_ * GainArrivalSeconds);
        // A sinusoidal delay of D samples at f Hz is a speed deviation of
        // 2*pi*f*D/rate, so the published percentages fix the excursions.
        const double spoolSamples = SwimDepth * SpoolShare * rate_ / (Tau * SpoolHz);
        const double flywheelSamples = SwimDepth * FlywheelShare * rate_ / (Tau * FlywheelHz);
        const double intermittentSamples = SwimDepth * IntermittentShare * rate_ / (Tau * IntermittentHz);
        const double shutterSamples = SwimDepth * ShutterShare * rate_ / (Tau * ShutterHz);
        // The rectifier's own band is fixed, so its sums top out below
        // Nyquist at every supported rate and it genuinely needs no
        // oversampling of its own.
        const double crossTopCoefficient = Coefficient(std::min(CrossModTopHz, rate_ / 6.0), inverseRate);
        const double crossSplitCoefficient = Coefficient(CrossModSplitHz, inverseRate);
        const double crossEnvelopeCoefficient = Coefficient(CrossModEnvelopeHz, inverseRate);
        const double crossBaseCoefficient = Coefficient(CrossModBlockHz, inverseRate);
        const double preEmphasisCoefficient = Coefficient(PreEmphasisPoleHz, inverseRate);

        for (unsigned int frame = 0; frame < frames; ++frame)
        {
            smoothed_ += (weight - smoothed_) * dezipper;
            const double a = std::clamp(smoothed_, 0.0, 1.0);
            const double seconds = clock_ * inverseRate;
            // Slow up and instant down, so a weight re-raised onto a hand
            // that is already high cannot step the published gain.
            gainWeight_ = std::min(a, gainWeight_ + arrivalStep);

            // One optical track: the channels fold together as the print arrives.
            double sum = 0;
            std::array<float, MaxChannels> dry{};
            for (int channel = 0; channel < inputChannels; ++channel)
            {
                const float value = input[frame * inputChannels + channel];
                dry[channel] = std::isfinite(value) ? value : 0;
                sum += dry[channel];
            }
            const double mono = sum / inputChannels;
            for (int channel = 0; channel < inputChannels; ++channel)
            {
                history_[static_cast<size_t>(written_ % frames_) * MaxChannels + channel] =
                    Mix(dry[channel], static_cast<float>(mono), a);
            }

            // The print swims in the gate. The oversampler's own group delay
            // is taken out of the swim so the wet path's total displacement
            // is still a*(base + swim) wherever the clamp does not bind.
            const double swim =
                spoolSamples * std::sin(Tau * SpoolHz * seconds) +
                flywheelSamples * std::sin(Tau * FlywheelHz * seconds + 1.7) +
                intermittentSamples * std::sin(Tau * IntermittentHz * seconds + 0.4) +
                shutterSamples * std::sin(Tau * ShutterHz * seconds + 2.3);
            const double delay = std::clamp(
                a * (rate_ * BaseDelaySeconds + swim) - OversamplerLatencySamples,
                0.0, frames_ - 2.0);
            const double position = static_cast<double>(written_) - delay;

            // The picture grid. A fractional accumulator, because 22050 is
            // not divisible by 24 and the grid must hold at every rate.
            bool newPicture = false;
            picturePhase_ += 1.0;
            if (picturePhase_ >= samplesPerPicture)
            {
                picturePhase_ -= samplesPerPicture;
                newPicture = true;
            }

            // The bands. Everything below is written so that a == 0 is
            // arithmetically the identity: the shelf is 3^0, both published
            // gains are 10^0, the bell's m1 is zero and the rail is the
            // bypass ceiling. The exact branch above is a shortcut, not the
            // only thing holding transparency.
            const double band = std::pow(a, BandArrivalExponent);
            const double highpassCoefficient = Coefficient(
                LogLerp(HighpassRestHz, HighpassPrintHz, band), inverseRate);
            const double slitCoefficient = Coefficient(
                LogLerp(LowpassRestHz, LowpassPrintHz, band), inverseRate);
            const double amplifier = std::pow(a, AmplifierArrivalExponent);
            const double shelf = std::pow(PreEmphasisShelf, amplifier);

            // The galvanometer, ahead of the emulsion: an optical recorder
            // cannot write much low end, and bass that reaches the mask eats
            // the whole modulation ceiling and drags the mids down with it.
            std::array<double, MaxChannels> carried{};
            double carriedSum = 0;
            for (int channel = 0; channel < inputChannels; ++channel)
            {
                const double read = Read(position, channel);
                carried[channel] = read - Lowpass(highpassOne_[channel], read, highpassCoefficient);
                carriedSum += carried[channel];
            }
            const double modulationMono = carriedSum / inputChannels;

            // The meter and the mixer's hand. The dub is ridden so that PEAKS
            // sit TargetOvermodulationDb over 100 % modulation, which is
            // crest-invariant: a sine, the bed, the street and a music cue
            // all tear by the same amount.
            const double rectified = std::fabs(modulationMono);
            meter_ += (rectified - meter_) * (rectified > meter_ ? meterAttack : meterDecay);
            if (newPicture && meter_ > ExposureHoldPeak)
            {
                rideTarget_ = std::clamp(
                    TargetOvermodulationDb - 20.0 * std::log10(meter_),
                    ExposureMinGainDb, ExposureMaxGainDb);
            }
            // The mixer has his level before the take rather than finding it
            // during it: without this the hand spent six seconds climbing to
            // its operating point every time a reel was threaded, and nothing
            // could tear until it arrived. Afterwards it RIDES, slowly, which
            // is what keeps a loud passage tearing harder than a quiet one.
            if (!acquired_ && meter_ > ExposureHoldPeak)
            {
                ride_ = rideTarget_;
                acquired_ = true;
            }
            else if (ride_ < rideTarget_) ride_ = std::min(rideTarget_, ride_ + riseStep);
            else if (ride_ > rideTarget_) ride_ = std::max(rideTarget_, ride_ - fallStep);
            // The drive arrives early and the fader takes back exactly what it
            // added, so the PUBLISHED gain is still 10^(w*(ride+fader)/20) and
            // the loudness of the ramp is untouched - only the ratio the
            // emulsion sees moves forward.
            const double ridden = ride_ - ArrivalHeadroomDb *
                (1.0 - std::pow(gainWeight_, DriveArrivalExponent));
            const double exposure = std::pow(10.0, ridden / 20.0);
            const double fader = std::pow(
                10.0, (gainWeight_ * (ride_ + ProjectorGainDb) - ridden) / 20.0);

            // The printing smear, before the mask: the two are the same
            // physical event, and a clipper flattens the very envelope the
            // rectifier is supposed to read.
            const double driven = exposure * modulationMono;
            const double drivenLow = Lowpass(preEmphasisDetector_, driven, preEmphasisCoefficient);
            const double smeared = drivenLow + shelf * (driven - drivenLow);
            crossLow_ += (smeared - crossLow_) * crossSplitCoefficient;
            const double banded = Lowpass(crossTop_, smeared - crossLow_, crossTopCoefficient);
            crossEnvelope_ += (std::fabs(banded) - crossEnvelope_) * crossEnvelopeCoefficient;
            crossBase_ += (crossEnvelope_ - crossBase_) * crossBaseCoefficient;
            const double smear = a * CrossModDepth * (crossEnvelope_ - crossBase_);

            // The mask: what the trace can and cannot do. It closes early, so
            // the track is tearing well before the fifteen seconds are up.
            const double limit = LogLerp(ModulationRest, 1.0,
                                         std::pow(a, ModulationArrivalExponent));
            const double clear = limit * (1.0 + ImageSpread);
            const double dark = limit * (1.0 - ImageSpread);
            const double toe = limit * PrintToe;

            const double frameLine = 1.0 - FrameLineDepth * a *
                std::exp(-picturePhase_ * inverseRate * PicturesPerSecond * FrameLineSharpness);
            const double ripple = (std::cos(Tau * 2.0 * MainsHz * seconds) +
                                   0.30 * std::cos(Tau * 4.0 * MainsHz * seconds + 1.9) +
                                   0.12 * std::cos(Tau * 6.0 * MainsHz * seconds + 0.6)) / 1.42;
            const double lamp = 1.0 - LampRippleDepth * a * ripple;

            // One mono white sample serves the surface and its dust.
            const double white = Random() * 2.0 - 1.0;
            const double surface =
                SurfaceAmplitude * std::pow(a, SurfaceExponent) * white;
            if (newPicture && dustAge_ < 0 &&
                Random() < DustPerSecond / PicturesPerSecond)
            {
                dustAge_ = 0;
                dustGain_ = 0.35 + 0.65 * Random();
            }
            double dust = 0;
            if (dustAge_ >= 0)
            {
                const double age = dustAge_ * inverseRate;
                const double decay = std::exp(-age * DustDecayPerSecond);
                dust = DustAmplitude * std::pow(a, DustExponent) *
                       dustGain_ * decay * white;
                if (++dustAge_ * inverseRate > 0.02) dustAge_ = -1;
            }

            // The transport: the claw pulling each picture down, and the
            // shutter's second blade halfway through it.
            const double sincePicture = picturePhase_ * inverseRate;
            const double halfPicture = 0.5 / PicturesPerSecond;
            const double sinceBlade = sincePicture >= halfPicture
                ? sincePicture - halfPicture
                : sincePicture + halfPicture;
            const double transport =
                TransportAmplitude * std::pow(a, TransportExponent) *
                (std::exp(-sincePicture * 190.0) * std::sin(Tau * 132.0 * sincePicture) +
                 0.35 * std::exp(-sinceBlade * 90.0) * std::sin(Tau * 310.0 * sinceBlade));

            const double ceiling = LogLerp(BypassCeiling, OutputCeiling, a);
            const double presence = PresenceCoefficients(amplifier, inverseRate);
            const double fade = settled
                ? static_cast<double>(latchFade_ + 1) / LatchFadeSamples
                : 0.0;
            if (!settled) latchFade_ = 0;
            else if (latchFade_ < LatchFadeSamples) ++latchFade_;

            for (int channel = 0; channel < outputChannels; ++channel)
            {
                if (channel >= inputChannels)
                {
                    output[frame * outputChannels + channel] = 0;
                    continue;
                }
                // Into modulation: the mixer's hand, then the characteristic
                // the recording amplifier applies after his meter, then the
                // smear the print will add at exposure time.
                double value = exposure * carried[channel];
                const double low = Lowpass(preEmphasis_[channel], value, preEmphasisCoefficient);
                value = low + shelf * (value - low);
                value -= smear;

                // The emulsion, in double and at twice the rate. Both
                // antiderivatives are evaluated with THIS sample's mask: the
                // mask moves every sample, so a cached F(m[n-1]) would carry
                // a term divided by a difference that is smallest exactly
                // where the material is quietest.
                Push(source_[channel], value);
                const double even = 2.0 * Convolve(source_[channel]);
                const double odd = source_[channel][static_cast<size_t>(7)];
                Push(even_[channel], Emulsion(previous_[channel], even, clear, dark, toe));
                Push(odd_[channel], Emulsion(previous_[channel], odd, clear, dark, toe));
                value = Convolve(even_[channel]) + 0.5 * odd_[channel][static_cast<size_t>(8)];

                // The output transformer: the emulsion is deliberately
                // asymmetric, so it leaves a programme-dependent offset, and
                // a TPT subtractive pole has exactly zero gain at DC.
                value -= Lowpass(highpassTwo_[channel], value, highpassCoefficient);

                // The projector's fixed fader, and then everything that is
                // not on the film: the grain, the gate's dirt, the frame line
                // crossing the slit and the lamp's own ripple.
                value = value * fader + surface + dust;
                value = value * frameLine * lamp;
                for (auto& pole : slit_)
                    value = Lowpass(pole[channel], value, slitCoefficient);
                value = Bell(presenceOne_[channel], presenceTwo_[channel], value, presence);
                value += transport;
                value = std::clamp(value, -ceiling, ceiling);

                // The latch: the wet path's bands and its latency are not the
                // dry ones, so the last stretch before the exact bypass is a
                // crossfade rather than a step.
                if (fade > 0.0)
                {
                    const float source = channel < inputChannels
                        ? input[frame * inputChannels + channel] : 0.0f;
                    value += (source - value) * fade;
                }
                output[frame * outputChannels + channel] = static_cast<float>(value);
            }

            ++written_;
            ++clock_;
        }
    }

private:
    static constexpr double Pi = 3.14159265358979323846;
    static constexpr double Tau = Pi * 2;
    static constexpr size_t HalfBandBranch = 16;   // the non-zero even taps

    static float Mix(float a, float b, double amount) { return static_cast<float>(a + (b - a) * amount); }
    static double LogLerp(double from, double to, double amount)
    {
        return std::exp(std::log(from) + (std::log(to) - std::log(from)) * amount);
    }
    // A TPT one-pole. Its lowpass has exactly unity gain at DC and a zero at
    // Nyquist, so the subtractive highpass built from it has exactly zero DC
    // gain and the published corner survives prewarping at every rate.
    double Coefficient(double hertz, double inverseRate) const
    {
        const double limit = rate_ * 0.45;
        const double g = std::tan(Pi * std::min(hertz, limit) * inverseRate);
        return g / (1.0 + g);
    }
    static double Lowpass(double& state, double value, double coefficient)
    {
        const double v = (value - state) * coefficient;
        const double low = v + state;
        state = low + v;
        return low;
    }
    // The characteristic curve of the developed trace: linear until the
    // shoulder, flat at the mask, and a quadratic knee between the two, so
    // the shape is C1 everywhere and its antiderivative is exact.
    static double Emulsion(double& previous, double value,
                           double clear, double dark, double toe)
    {
        const double difference = value - previous;
        double result;
        if (std::fabs(difference) > AdaaEpsilon)
        {
            result = (Antiderivative(value, clear, dark, toe) -
                      Antiderivative(previous, clear, dark, toe)) / difference;
        }
        else
        {
            result = Trace(0.5 * (value + previous), clear, dark, toe);
        }
        previous = value;
        return result;
    }
    static double Trace(double value, double clear, double dark, double toe)
    {
        const double sign = value < 0 ? -1.0 : 1.0;
        const double magnitude = std::fabs(value);
        const double limit = value < 0 ? dark : clear;
        if (magnitude <= limit - toe) return value;
        if (magnitude >= limit + toe) return sign * limit;
        const double over = magnitude - limit + toe;
        return sign * (magnitude - over * over / (4.0 * toe));
    }
    static double Antiderivative(double value, double clear, double dark, double toe)
    {
        const double magnitude = std::fabs(value);
        const double limit = value < 0 ? dark : clear;
        if (magnitude <= limit - toe) return 0.5 * magnitude * magnitude;
        if (magnitude >= limit + toe)
        {
            const double edge = limit + toe;
            return 0.5 * edge * edge - 2.0 * toe * toe / 3.0 +
                   limit * (magnitude - edge);
        }
        const double over = magnitude - limit + toe;
        return 0.5 * magnitude * magnitude - over * over * over / (12.0 * toe);
    }
    // A TPT state-variable bell. The gain is modulated by the weight, and an
    // SVF stays click-free under modulation where a direct form does not.
    double PresenceCoefficients(double arrival, double inverseRate)
    {
        const double amplitude = std::pow(10.0, PresenceGainDb * arrival / 40.0);
        const double limit = rate_ * 0.45;
        const double g = std::tan(Pi * std::min(PresenceHz, limit) * inverseRate);
        const double k = 1.0 / (PresenceQ * amplitude);
        bellOne_ = 1.0 / (1.0 + g * (g + k));
        bellTwo_ = g * bellOne_;
        bellThree_ = g * bellTwo_;
        return k * (amplitude * amplitude - 1.0);
    }
    double Bell(double& first, double& second, double value, double middle) const
    {
        const double v3 = value - second;
        const double v1 = bellOne_ * first + bellTwo_ * v3;
        const double v2 = second + bellTwo_ * first + bellThree_ * v3;
        first = 2.0 * v1 - first;
        second = 2.0 * v2 - second;
        return value + middle * v1;
    }
    // A 31-tap Kaiser half-band, as its two polyphase branches. The centre
    // tap is the pass-through and every other odd tap is exactly zero, so one
    // branch is a delay and the other is sixteen multiplies. The round-trip
    // gain is unity at DC by construction, which is worth stating because
    // getting the interpolator's factor of two wrong costs 6 dB in and 6 dB
    // out and silently changes the one thing this design is about.
    void BuildHalfBand()
    {
        const double centre = (HalfBandTaps - 1) / 2.0;
        const double shape = Bessel(HalfBandKaiserBeta);
        double total = 0;
        std::array<double, HalfBandTaps> taps{};
        for (int index = 0; index < HalfBandTaps; ++index)
        {
            const double offset = (index - centre) / 2.0;
            const double window = std::sqrt(std::max(
                0.0, 1.0 - std::pow((index - centre) / centre, 2.0)));
            taps[static_cast<size_t>(index)] =
                Sinc(offset) * Bessel(HalfBandKaiserBeta * window) / shape;
            total += taps[static_cast<size_t>(index)];
        }
        // Normalised so the branch sums to exactly one half, which makes the
        // centre tap exactly one half too and pins the round trip at unity:
        // interpolate to 2*0.5 = 1, decimate to 0.5 + 0.5 = 1. Normalising by
        // the whole kernel's sum instead leaves a fraction of a dB in and the
        // same again out, on the one gain this design is about.
        (void)total;
        double branchTotal = 0;
        for (int index = 0; index < HalfBandTaps; index += 2)
            branchTotal += taps[static_cast<size_t>(index)];
        size_t branch = 0;
        for (int index = 0; index < HalfBandTaps; index += 2)
            halfBand_[branch++] = taps[static_cast<size_t>(index)] / (2.0 * branchTotal);
    }
    static double Sinc(double value)
    {
        if (std::fabs(value) < 1e-12) return 1.0;
        return std::sin(Pi * value) / (Pi * value);
    }
    static double Bessel(double value)
    {
        double sum = 1.0, term = 1.0;
        for (int order = 1; order < 40; ++order)
        {
            const double half = value / (2.0 * order);
            term *= half * half;
            sum += term;
            if (term < 1e-18 * sum) break;
        }
        return sum;
    }
    // The rings run backwards from the cursor, so index 0 is the newest
    // sample and the convolution needs no wrap arithmetic of its own.
    void Push(std::array<double, HalfBandBranch>& ring, double value)
    {
        for (size_t index = HalfBandBranch - 1; index > 0; --index)
            ring[index] = ring[index - 1];
        ring[0] = value;
    }
    double Convolve(const std::array<double, HalfBandBranch>& ring) const
    {
        double sum = 0;
        for (size_t index = 0; index < HalfBandBranch; ++index)
            sum += halfBand_[index] * ring[index];
        return sum;
    }
    static void Copy(const float* input, float* output, unsigned int frames, int inChannels, int outChannels)
    {
        for (unsigned int frame = 0; frame < frames; ++frame)
            for (int channel = 0; channel < outChannels; ++channel)
                output[frame * outChannels + channel] = channel < inChannels ? input[frame * inChannels + channel] : 0;
    }
    double Random()
    {
        random_ ^= random_ << 13; random_ ^= random_ >> 17; random_ ^= random_ << 5;
        return (random_ & 0x00FFFFFFu) / 16777216.0;
    }
    float Sample(int64_t position, int channel) const
    {
        if (position < 0 || position > written_ || written_ - position >= frames_) return 0;
        return history_[static_cast<size_t>(position % frames_) * MaxChannels + channel];
    }
    float Read(double position, int channel) const
    {
        const auto base = static_cast<int64_t>(std::floor(position));
        return Mix(Sample(base, channel), Sample(base + 1, channel), position - base);
    }
    int rate_, frames_, previousChannels_ = 0;
    std::vector<float> history_;
    std::array<double, HalfBandBranch> halfBand_{};
    std::array<double, MaxChannels> highpassOne_{}, highpassTwo_{}, preEmphasis_{};
    double preEmphasisDetector_ = 0;
    std::array<std::array<double, MaxChannels>, LowpassPoles> slit_{};
    std::array<double, MaxChannels> presenceOne_{}, presenceTwo_{}, previous_{};
    std::array<std::array<double, HalfBandBranch>, MaxChannels> source_{}, even_{}, odd_{};
    double bellOne_ = 0, bellTwo_ = 0, bellThree_ = 0;
    int64_t written_ = 0, clock_ = 0;
    int latchFade_ = LatchFadeSamples;
    double smoothed_ = 0, picturePhase_ = 0;
    double meter_ = 0, ride_ = 0, rideTarget_ = 0, gainWeight_ = 0;
    bool acquired_ = false;
    double crossLow_ = 0, crossTop_ = 0, crossEnvelope_ = 0, crossBase_ = 0;
    double dustAge_ = -1, dustGain_ = 0;
    uint32_t random_ = 0x51A7C39Bu;
};
}
