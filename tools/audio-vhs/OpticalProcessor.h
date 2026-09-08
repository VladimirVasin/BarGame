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
// Memory is allocated at instance creation, never from the audio callback.
// A settled zero weight is an exact, zero-latency bypass.
class OpticalProcessor
{
public:
    static constexpr int MaxChannels = 8;

    // ---- Published contract. BegottenAudioRules.cs mirrors these, and
    // ---- BegottenAudioContractTests reads this header to prove it.
    // The host weight is already eased and spread over fifteen seconds, so
    // the internal smoothing is only a de-zipper, not a second ramp.
    static constexpr double DezipperSeconds = 0.060;
    // The pitch-swim delay line needs a non-zero centre to swim about.
    static constexpr double BaseDelaySeconds = 0.008;
    // The one number the ear can match to the screen.
    static constexpr double PicturesPerSecond = 24.0;
    // The optical band, interpolated in log frequency. A single pole is far
    // too gentle for a print: at one pole the gate still passed most of the
    // energy above 5 kHz, so each end is a cascade and these are the corners
    // of one section.
    static constexpr int HighpassPoles = 2;   // 12 dB per octave
    static constexpr int LowpassPoles = 3;    // 18 dB per octave
    static constexpr double HighpassRestHz = 10.0;
    static constexpr double HighpassPrintHz = 200.0;
    static constexpr double LowpassRestHz = 22000.0;
    static constexpr double LowpassPrintHz = 3800.0;
    // The print's own surface, injected before the band so the band shapes it.
    //
    // These are PRE-band amplitudes, and that is why the first tuning was
    // inaudible: a three-pole gate at 3.8 kHz throws away roughly nine tenths
    // of white noise's energy, so a -42 dBFS hiss reached the ear at about
    // -51 dBFS - measured, not guessed. The number that matters is the settled
    // quiet floor, and it is measured by the validator's `quiet_floor_rms`.
    static constexpr double SurfaceAmplitude = 0.0316;   // -30 dBFS pre-band
    static constexpr double SurfaceExponent = 1.6;
    static constexpr double DustAmplitude = 0.0631;      // -24 dBFS pre-band
    static constexpr double DustExponent = 1.6;
    static constexpr double DustPerSecond = 11.0;
    static constexpr double DustDecayPerSecond = 900.0;
    // The apparatus arrives last, but it must still ARRIVE: at the cube it
    // was inaudible for two thirds of the fifteen seconds and quiet after.
    static constexpr double TransportAmplitude = 0.0282; // -31 dBFS
    static constexpr double TransportExponent = 2.2;
    // The frame line crossing the sound slit. A worn print puts a distinct
    // twenty-four per second put-put into its own track, and this is the
    // strongest tie the ear has to a screen held at twenty-four pictures: it
    // is not a gate and not a stutter, only a dip once per picture.
    static constexpr double FrameLineDepth = 0.22;
    static constexpr double FrameLineSharpness = 14.0;
    // Wow and flutter, as the peak speed error of a tired portable machine.
    // A serviceable projector holds 0.3 %, a tired one reaches 1.2 %; the
    // print is a worn dupe run through a tired machine, so it takes the tired
    // figure. The shares sum to one, so SwimDepth IS the peak error: 1.25 %
    // is 1200*log2(1.0125) = 21 cents.
    //
    // The third term is the point of the whole model. The picture is HELD at
    // twenty-four pictures a second, and the intermittent that holds it leaks
    // into the sound drum at exactly that rate. One term in a sum locks the
    // ear to the eye.
    static constexpr double SwimDepth = 0.0125;
    static constexpr double SpoolHz = 0.90;          // spool eccentricity
    static constexpr double SpoolShare = 0.55;
    static constexpr double FlywheelHz = 2.40;       // flywheel and sound drum
    static constexpr double FlywheelShare = 0.30;
    static constexpr double IntermittentHz = PicturesPerSecond;
    static constexpr double IntermittentShare = 0.10;
    static constexpr double ShutterHz = PicturesPerSecond * 2.0;
    static constexpr double ShutterShare = 0.05;
    // Saturation and the ~40 dB the track can hold. No make-up gain: the
    // print is quieter and denser, and Unity's master headroom stays
    // authoritative, exactly as it does for the tape.
    static constexpr double SaturationDrive = 1.80;
    static constexpr double CompressorThreshold = 0.1259; // -18 dBFS
    static constexpr double CompressorRatio = 2.5;
    static constexpr double CompressorAttackSeconds = 0.008;
    static constexpr double CompressorReleaseSeconds = 0.220;
    // Below this the weight has settled and the exact bypass is taken.
    static constexpr double SettledWeight = 0.00001;

    explicit OpticalProcessor(int sampleRate)
        : rate_(std::max(8000, sampleRate)),
          frames_(std::max(1024, rate_ / 4)),
          history_(static_cast<size_t>(frames_) * MaxChannels, 0.0f) {}

    void Reset()
    {
        written_ = 0;
        clock_ = 0;
        smoothed_ = 0;
        picturePhase_ = 0;
        dustAge_ = -1;
        dustGain_ = 0;
        level_ = 0;
        random_ = 0x51A7C39Bu;
        for (auto& pole : highpass_) pole.fill(0);
        for (auto& pole : lowpass_) pole.fill(0);
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
        // quiet room too. Only a settled zero weight is.
        if (weight == 0 && smoothed_ < SettledWeight)
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
        const double attack = 1.0 - std::exp(-inverseRate / CompressorAttackSeconds);
        const double release = 1.0 - std::exp(-inverseRate / CompressorReleaseSeconds);
        // A sinusoidal delay of D samples at f Hz is a speed deviation of
        // 2*pi*f*D/rate, so the published percentages fix the excursions.
        const double spoolSamples = SwimDepth * SpoolShare * rate_ / (Tau * SpoolHz);
        const double flywheelSamples = SwimDepth * FlywheelShare * rate_ / (Tau * FlywheelHz);
        const double intermittentSamples = SwimDepth * IntermittentShare * rate_ / (Tau * IntermittentHz);
        const double shutterSamples = SwimDepth * ShutterShare * rate_ / (Tau * ShutterHz);

        for (unsigned int frame = 0; frame < frames; ++frame)
        {
            smoothed_ += (weight - smoothed_) * dezipper;
            const double a = std::clamp(smoothed_, 0.0, 1.0);
            const double seconds = clock_ * inverseRate;

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

            // The print swims in the gate.
            const double swim =
                spoolSamples * std::sin(Tau * SpoolHz * seconds) +
                flywheelSamples * std::sin(Tau * FlywheelHz * seconds + 1.7) +
                intermittentSamples * std::sin(Tau * IntermittentHz * seconds + 0.4) +
                shutterSamples * std::sin(Tau * ShutterHz * seconds + 2.3);
            const double delay = std::clamp(
                a * (rate_ * BaseDelaySeconds + swim), 0.0, frames_ - 2.0);
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

            // The surface and its dust are ON the print, so the band shapes
            // them. The projector is not: it stands in the room with the
            // audience, so it joins after the track has been read, and it is
            // not compressed by the track's own limiting either.
            const double injected = surface + dust;

            // The optical band, in log frequency so the whole sweep is audible.
            const double highpassHz = LogLerp(HighpassRestHz, HighpassPrintHz, a);
            const double lowpassHz = LogLerp(LowpassRestHz, LowpassPrintHz, a);
            const float highpassCoefficient = Pole(highpassHz, inverseRate);
            const float lowpassCoefficient = Pole(lowpassHz, inverseRate);
            const double drive = 1.0 + SaturationDrive * a;
            const double ratio = 1.0 + a * (CompressorRatio - 1.0);
            // The frame line crossing the sound slit: a dip once per picture,
            // which is the strongest tie the ear has to a screen held at
            // twenty-four. It is a dip, never a gate - the sound head reads a
            // continuous track, and a stutter would be a lie about the medium.
            const double frameLine = 1.0 - FrameLineDepth * a *
                std::exp(-sincePicture * PicturesPerSecond * FrameLineSharpness);

            std::array<float, MaxChannels> shaped{};
            double peak = 0;
            for (int channel = 0; channel < outputChannels; ++channel)
            {
                if (channel >= inputChannels) { shaped[channel] = 0; continue; }
                float value = static_cast<float>(Read(position, channel) + injected);
                for (int pole = 0; pole < HighpassPoles; ++pole)
                {
                    highpass_[pole][channel] += highpassCoefficient * (value - highpass_[pole][channel]);
                    value -= highpass_[pole][channel];
                }
                for (int pole = 0; pole < LowpassPoles; ++pole)
                {
                    lowpass_[pole][channel] += lowpassCoefficient * (value - lowpass_[pole][channel]);
                    value = lowpass_[pole][channel];
                }
                value = static_cast<float>(std::tanh(value * drive) / drive * frameLine);
                shaped[channel] = value;
                peak = std::max(peak, static_cast<double>(std::fabs(value)));
            }

            // One follower for every channel keeps the image coherent.
            level_ += (peak - level_) * (peak > level_ ? attack : release);
            double gain = 1;
            if (level_ > CompressorThreshold && ratio > 1.0)
                gain = std::pow(level_ / CompressorThreshold, 1.0 / ratio - 1.0);
            for (int channel = 0; channel < outputChannels; ++channel)
                output[frame * outputChannels + channel] =
                    static_cast<float>(shaped[channel] * gain + transport);

            ++written_;
            ++clock_;
        }
    }

private:
    static constexpr double Pi = 3.14159265358979323846;
    static constexpr double Tau = Pi * 2;
    static float Mix(float a, float b, double amount) { return static_cast<float>(a + (b - a) * amount); }
    static double LogLerp(double from, double to, double amount)
    {
        return std::exp(std::log(from) + (std::log(to) - std::log(from)) * amount);
    }
    float Pole(double hertz, double inverseRate) const
    {
        const double limit = rate_ * 0.45;
        return static_cast<float>(1.0 - std::exp(-Tau * std::min(hertz, limit) * inverseRate));
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
    std::array<std::array<float, MaxChannels>, HighpassPoles> highpass_{};
    std::array<std::array<float, MaxChannels>, LowpassPoles> lowpass_{};
    int64_t written_ = 0, clock_ = 0;
    double smoothed_ = 0, picturePhase_ = 0;
    double dustAge_ = -1, dustGain_ = 0;
    double level_ = 0;
    uint32_t random_ = 0x51A7C39Bu;
};
}
