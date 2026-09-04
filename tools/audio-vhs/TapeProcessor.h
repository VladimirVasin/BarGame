#pragma once

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <vector>

namespace bar_audio
{
// One shared tape transport for every channel preserves stereo positioning.
// Memory is allocated at instance creation, never from the audio callback.
class TapeProcessor
{
public:
    static constexpr int MaxChannels = 8;
    explicit TapeProcessor(int sampleRate)
        : rate_(std::max(8000, sampleRate)), frames_(rate_ * 2),
          history_(static_cast<size_t>(frames_) * MaxChannels, 0.0f) {}

    void Reset()
    {
        written_ = 0;
        clock_ = 0;
        smoothed_ = 0;
        eventAge_ = -1;
        eventDelay_ = 0;
        nextEvent_ = rate_ * 2.0;
        random_ = 0x62AF341Du;
        low_.fill(0);
        previousChannels_ = 0;
    }

    void Process(const float* input, float* output, unsigned int frames,
                 int inputChannels, int outputChannels, float intensity, bool paused)
    {
        intensity = std::isfinite(intensity) ? std::clamp(intensity, 0.0f, 1.0f) : 0;
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
        bool silent = true;
        for (size_t i = 0; i < static_cast<size_t>(frames) * inputChannels; ++i)
            if (input[i] != 0.0f) { silent = false; break; }
        if (silent || (intensity == 0 && smoothed_ < 0.00001f))
        {
            Reset();
            Copy(input, output, frames, inputChannels, outputChannels);
            return;
        }
        if (previousChannels_ != 0 && previousChannels_ != inputChannels)
            Reset();
        previousChannels_ = inputChannels;
        const double inverseRate = 1.0 / rate_;
        const float smoothing = static_cast<float>(1.0 - std::exp(-inverseRate / 0.030));
        for (unsigned int frame = 0; frame < frames; ++frame)
        {
            std::array<float, MaxChannels> dry{};
            for (int channel = 0; channel < inputChannels; ++channel)
            {
                const float value = input[frame * inputChannels + channel];
                dry[channel] = std::isfinite(value) ? value : 0;
                history_[static_cast<size_t>(written_ % frames_) * MaxChannels + channel] = dry[channel];
            }
            smoothed_ += (intensity - smoothed_) * smoothing;
            const double a = smoothed_;
            const double seconds = clock_ * inverseRate;
            const double wow = 0.012 * std::sin(Tau * 0.71 * seconds) +
                               0.009 * std::sin(Tau * 0.37 * seconds + 1.1) +
                               0.00065 * std::sin(Tau * 8.3 * seconds + 0.7) +
                               0.00025 * std::sin(Tau * 13.1 * seconds);
            const double ordinaryDelay = rate_ * (0.027 + a * wow);
            double envelope = 0;
            if (a >= 0.09 && eventAge_ < 0 && clock_ >= nextEvent_ && written_ > rate_ / 3)
            {
                eventAge_ = 0;
                eventDuration_ = rate_ * (0.4 + 0.6 * Random());
                eventDelay_ = 0;
                repeatLength_ = rate_ * (0.07 + 0.11 * Random());
                repeatStart_ = static_cast<double>(written_) - ordinaryDelay - repeatLength_;
                repeat_ = Random() < 0.72;
                // At maximum, onset spacing is 2-4 seconds, including the event.
                nextEvent_ = clock_ + rate_ * (2.0 + 2.0 * Random() + 14.0 * (1.0 - a));
            }
            if (eventAge_ >= 0)
            {
                const double progress = eventAge_ / eventDuration_;
                envelope = Smooth(std::min(1.0, eventAge_ / (rate_ * 0.035))) *
                           Smooth(std::min(1.0, (eventDuration_ - eventAge_) / (rate_ * 0.16)));
                // The head slows to one half speed at the deepest point.
                eventDelay_ += 0.5 * a * std::sin(Pi * progress);
            }
            const double delay = std::clamp(ordinaryDelay + eventDelay_, 0.0, rate_ * 0.75);
            const double normalPosition = static_cast<double>(written_) - ordinaryDelay;
            const double damagedPosition = static_cast<double>(written_) - delay;
            const double repeatPhase = eventAge_ >= 0 ?
                std::fmod(eventAge_ * (1.0 - 0.45 * a), repeatLength_) : 0;
            const double repeatCrossfade = std::min(rate_ * 0.012, repeatLength_ * 0.2);
            // Losing tape contact reduces high frequencies and level together.
            const double cutoff = 19000.0 * std::pow(0.22, a) * (1.0 - 0.61 * a * envelope);
            const float lowpass = static_cast<float>(1.0 - std::exp(-Tau * cutoff * inverseRate));
            const float contact = static_cast<float>(1.0 - a * (0.06 + 0.43 * envelope));
            const float wet = static_cast<float>(0.94 * a);
            const float drive = static_cast<float>(1.0 + 1.5 * a + a * envelope);
            for (int channel = 0; channel < outputChannels; ++channel)
            {
                if (channel >= inputChannels) { output[frame * outputChannels + channel] = 0; continue; }
                const float ordinary = Read(normalPosition, channel);
                float damaged = Read(damagedPosition, channel);
                if (repeat_ && eventAge_ >= 0)
                {
                    float repeated = Read(repeatStart_ + repeatPhase, channel);
                    // Two heads overlap the loop seam; a wrap cannot click.
                    if (repeatPhase < repeatCrossfade)
                    {
                        const float previous = Read(repeatStart_ + repeatLength_ + repeatPhase, channel);
                        repeated = Mix(previous, repeated, Smooth(repeatPhase / repeatCrossfade));
                    }
                    damaged = Mix(damaged, repeated, 0.88 * a);
                }
                const float tape = Mix(ordinary, damaged, envelope);
                low_[channel] += lowpass * (tape - low_[channel]);
                // Unity master headroom remains authoritative. This shape is
                // amplitude-bounded and has unity gain around zero.
                const float shaped = std::tanh(low_[channel] * drive) / drive;
                output[frame * outputChannels + channel] = Mix(dry[channel], shaped * contact, wet);
            }
            if (eventAge_ >= 0 && ++eventAge_ >= eventDuration_)
            {
                eventAge_ = -1;
                eventDelay_ = 0; // rejoin the live head, never accumulate delay
            }
            ++written_;
            ++clock_;
        }
    }

private:
    static constexpr double Pi = 3.14159265358979323846;
    static constexpr double Tau = Pi * 2;
    static double Smooth(double x) { x = std::clamp(x, 0.0, 1.0); return x * x * (3 - 2 * x); }
    static float Mix(float a, float b, double amount) { return static_cast<float>(a + (b - a) * amount); }
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
    std::array<float, MaxChannels> low_{};
    int64_t written_ = 0, clock_ = 0;
    float smoothed_ = 0;
    double nextEvent_ = rate_ * 2.0;
    double eventAge_ = -1, eventDuration_ = 1, eventDelay_ = 0;
    double repeatLength_ = 1, repeatStart_ = 0;
    bool repeat_ = false;
    uint32_t random_ = 0x62AF341Du;
};
}
