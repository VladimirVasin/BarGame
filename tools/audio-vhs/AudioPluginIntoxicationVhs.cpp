#ifdef _MSC_VER
#pragma warning(push)
#pragma warning(disable: 4201) // Unity's ABI intentionally uses an anonymous union.
#endif
#include "vendor/AudioPluginInterface.h"
#ifdef _MSC_VER
#pragma warning(pop)
#endif
#include "TapeProcessor.h"
#include <atomic>
#include <cstdio>
#include <cstring>
#include <new>

namespace
{
enum Parameter { Intensity, Paused, ResetEpoch, Count };
struct Instance
{
    explicit Instance(int rate) : tape(rate) {}
    bar_audio::TapeProcessor tape;
    std::atomic<float> parameters[Count]{};
    float previousReset = 0;
    UInt64 nextTick = 0;
    bool hasTick = false;
};
static_assert(std::atomic<float>::is_always_lock_free, "The DSP control mailbox must be lock-free.");
UNITY_AUDIODSP_RESULT UNITY_AUDIODSP_CALLBACK Create(UnityAudioEffectState* state)
{
    try { state->effectdata = new Instance(static_cast<int>(state->samplerate)); }
    catch (...) { state->effectdata = nullptr; return UNITY_AUDIODSP_ERR_UNSUPPORTED; }
    return UNITY_AUDIODSP_OK;
}
UNITY_AUDIODSP_RESULT UNITY_AUDIODSP_CALLBACK Release(UnityAudioEffectState* state)
{
    delete static_cast<Instance*>(state->effectdata);
    state->effectdata = nullptr;
    return UNITY_AUDIODSP_OK;
}
UNITY_AUDIODSP_RESULT UNITY_AUDIODSP_CALLBACK Reset(UnityAudioEffectState* state)
{
    auto& instance = *static_cast<Instance*>(state->effectdata);
    instance.tape.Reset(); instance.hasTick = false;
    return UNITY_AUDIODSP_OK;
}
UNITY_AUDIODSP_RESULT UNITY_AUDIODSP_CALLBACK Process(UnityAudioEffectState* state,
    float* input, float* output, unsigned int frames, int inputChannels, int outputChannels)
{
    auto& instance = *static_cast<Instance*>(state->effectdata);
    const float reset = instance.parameters[ResetEpoch].load(std::memory_order_relaxed);
    if (reset != instance.previousReset || (instance.hasTick && state->currdsptick != instance.nextTick))
        instance.tape.Reset();
    instance.previousReset = reset;
    instance.nextTick = state->currdsptick + frames;
    instance.hasTick = true;
    const bool paused = instance.parameters[Paused].load(std::memory_order_relaxed) >= 0.5f ||
        (state->flags & (UnityAudioEffectStateFlags_IsPaused | UnityAudioEffectStateFlags_IsMuted)) != 0;
    const float intensity = instance.parameters[Intensity].load(std::memory_order_relaxed);
    instance.tape.Process(input, output, frames, inputChannels, outputChannels, intensity, paused);
    return UNITY_AUDIODSP_OK;
}
UNITY_AUDIODSP_RESULT UNITY_AUDIODSP_CALLBACK Set(UnityAudioEffectState* state, int index, float value)
{
    if (index < 0 || index >= Count || !std::isfinite(value)) return UNITY_AUDIODSP_ERR_UNSUPPORTED;
    value = std::clamp(value, 0.0f, index == ResetEpoch ? 1000000.0f : 1.0f);
    static_cast<Instance*>(state->effectdata)->parameters[index].store(value, std::memory_order_relaxed);
    return UNITY_AUDIODSP_OK;
}
UNITY_AUDIODSP_RESULT UNITY_AUDIODSP_CALLBACK Get(UnityAudioEffectState* state, int index, float* value, char* text)
{
    if (index < 0 || index >= Count) return UNITY_AUDIODSP_ERR_UNSUPPORTED;
    const float result = static_cast<Instance*>(state->effectdata)->parameters[index].load(std::memory_order_relaxed);
    if (value) *value = result;
    if (text) text[0] = 0;
    return UNITY_AUDIODSP_OK;
}
UnityAudioParameterDefinition parameters[Count] = {
    { "Intensity", "%", "Externally shaped intoxication amplitude; zero is exact dry bypass.", 0, 1, 0, 100, 1 },
    { "Paused", "", "Silence and clear tape history while gameplay audio is paused.", 0, 1, 0, 1, 1 },
    { "Reset", "", "Change this epoch to clear history on a new game, scene boundary or audio reset.", 0, 1000000, 0, 1, 1 }
};
UnityAudioEffectDefinition definition{};
UnityAudioEffectDefinition* definitions[] = { &definition };
}

extern "C" UNITY_AUDIODSP_EXPORT_API int AUDIO_CALLING_CONVENTION
UnityGetAudioEffectDefinitions(UnityAudioEffectDefinition*** result)
{
    definition.structsize = sizeof(UnityAudioEffectDefinition);
    definition.paramstructsize = sizeof(UnityAudioParameterDefinition);
    definition.apiversion = UNITY_AUDIO_PLUGIN_API_VERSION;
    definition.pluginversion = 0x00010000;
    definition.channels = 0;
    definition.numparameters = Count;
    std::strcpy(definition.name, "Intoxication VHS");
    definition.create = Create; definition.release = Release;
    definition.reset = Reset; definition.process = Process;
    definition.setfloatparameter = Set; definition.getfloatparameter = Get;
    definition.paramdefs = parameters;
    *result = definitions;
    return 1;
}
