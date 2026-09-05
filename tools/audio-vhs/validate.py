"""Exercise the shipping DLL through Unity's ABI and render listening references."""
import argparse
import ctypes as C
import json
import math
from pathlib import Path
import time
import wave

import numpy as np


class State(C.Structure):
    _fields_ = [("structsize", C.c_uint32), ("samplerate", C.c_uint32),
                ("currdsptick", C.c_uint64), ("prevdsptick", C.c_uint64),
                ("sidechainbuffer", C.c_void_p), ("effectdata", C.c_void_p),
                ("flags", C.c_uint32), ("internal", C.c_void_p),
                ("spatializerdata", C.c_void_p), ("dspbuffersize", C.c_uint32),
                ("hostapiversion", C.c_uint32), ("ambisonicdata", C.c_void_p)]


class Parameter(C.Structure):
    _fields_ = [("name", C.c_char * 16), ("unit", C.c_char * 16),
                ("description", C.c_char_p), ("minimum", C.c_float),
                ("maximum", C.c_float), ("default", C.c_float),
                ("scale", C.c_float), ("exponent", C.c_float)]


CALL = C.WINFUNCTYPE
StateCall = CALL(C.c_int, C.POINTER(State))
ProcessCall = CALL(C.c_int, C.POINTER(State), C.POINTER(C.c_float),
                   C.POINTER(C.c_float), C.c_uint, C.c_int, C.c_int)
SetCall = CALL(C.c_int, C.POINTER(State), C.c_int, C.c_float)
GetCall = CALL(C.c_int, C.POINTER(State), C.c_int, C.POINTER(C.c_float), C.c_void_p)


class Definition(C.Structure):
    _fields_ = [("structsize", C.c_uint32), ("paramstructsize", C.c_uint32),
                ("apiversion", C.c_uint32), ("pluginversion", C.c_uint32),
                ("channels", C.c_uint32), ("numparameters", C.c_uint32),
                ("flags", C.c_uint64), ("name", C.c_char * 32),
                ("create", StateCall), ("release", StateCall), ("reset", StateCall),
                ("process", ProcessCall), ("setposition", C.c_void_p),
                ("paramdefs", C.POINTER(Parameter)), ("set", SetCall),
                ("get", GetCall), ("getfloatbuffer", C.c_void_p)]


class Effect:
    def __init__(self, definition, rate=48000):
        self.definition = definition
        self.state = State()
        self.state.structsize = C.sizeof(State)
        self.state.samplerate = rate
        self.state.flags = 1
        self.state.dspbuffersize = 512
        self.state.hostapiversion = 0x010402
        assert definition.create(C.byref(self.state)) == 0

    def close(self):
        assert self.definition.release(C.byref(self.state)) == 0

    def set(self, index, value):
        assert self.definition.set(C.byref(self.state), index, value) == 0

    def reset(self):
        assert self.definition.reset(C.byref(self.state)) == 0

    def process(self, samples, block=512):
        samples = np.ascontiguousarray(samples, dtype=np.float32)
        result = np.zeros_like(samples)
        channels = samples.shape[1]
        for start in range(0, len(samples), block):
            end = min(start + block, len(samples))
            source = samples[start:end]
            target = result[start:end]
            assert self.definition.process(C.byref(self.state),
                source.ctypes.data_as(C.POINTER(C.c_float)),
                target.ctypes.data_as(C.POINTER(C.c_float)), end - start,
                channels, channels) == 0
            self.state.prevdsptick = self.state.currdsptick
            self.state.currdsptick += end - start
        return result


def profile(level):
    return math.expm1(4.5 * level / 100) / math.expm1(4.5)


def reference(rate, seconds=16):
    """A clean chord/bass bed with panned causal glass/step transients."""
    t = np.arange(rate * seconds, dtype=np.float64) / rate
    result = np.zeros((len(t), 2), dtype=np.float64)
    for frequency, amplitude in [(110, .06), (220, .035), (277.18, .03), (329.63, .03)]:
        signal = amplitude * np.sin(2 * np.pi * frequency * t)
        result[:, 0] += signal
        result[:, 1] += signal * .88
    for event in np.arange(.75, seconds - .3, 1.35):
        dt = t - event
        envelope = np.exp(-np.maximum(0, dt) * 18) * (dt >= 0) * (dt < .4)
        glass = envelope * (.08 * np.sin(2 * np.pi * 2417 * dt) +
                            .04 * np.sin(2 * np.pi * 3471 * dt))
        result[:, int(event * 10) % 2] += glass
    # Finite attack/release keep the reference's boundaries inaudible.
    fade = np.minimum(1, t / .1) * np.minimum(1, (seconds - t) / .15)
    return (result * fade[:, None]).astype(np.float32)


def save_wav(path, samples, rate):
    pcm = (np.clip(samples, -1, 1) * 32767).astype('<i2')
    with wave.open(str(path), 'wb') as stream:
        stream.setnchannels(samples.shape[1])
        stream.setsampwidth(2)
        stream.setframerate(rate)
        stream.writeframes(pcm.tobytes())


def validate(definition, output):
    checks = []
    rates = [22050, 44100, 48000, 96000]
    rng = np.random.default_rng(3167)
    for rate in rates:
        effect = Effect(definition, rate)
        dry = rng.uniform(-.35, .35, (rate // 5, 2)).astype(np.float32)
        assert np.array_equal(effect.process(dry, 127), dry), 'Sober bypass changed samples'
        fresh = Effect(definition, rate)
        fresh.set(0, 1)
        fresh_result = fresh.process(dry)
        fresh.close()
        effect.set(0, 1)
        effect.process(dry)
        effect.set(1, 1)
        assert not np.any(effect.process(dry)), 'Paused transport leaked audio'
        effect.set(1, 0)
        assert np.array_equal(effect.process(dry), fresh_result), 'Resume retained pre-pause transport'
        silence = np.zeros_like(dry)
        assert not np.any(effect.process(silence)), 'Resume replayed stale history'
        effect.process(dry)
        effect.set(2, 5)
        assert np.array_equal(effect.process(dry), fresh_result), 'Epoch did not clear active history'
        assert not np.any(effect.process(silence)), 'Epoch reset replayed history'
        effect.process(dry)
        effect.state.flags = 1 | 4
        assert not np.any(effect.process(dry)), 'Editor mute leaked audio'
        effect.state.flags = 1
        assert np.array_equal(effect.process(dry), fresh_result), 'Unmute retained old transport'
        assert not np.any(effect.process(silence)), 'Unmute replayed history'
        effect.process(dry)
        effect.state.currdsptick += 2048
        assert np.array_equal(effect.process(dry), fresh_result), 'DSP scheduling gap retained stale history'
        effect.reset()
        effect.set(0, 0)
        assert np.array_equal(effect.process(dry), dry), 'Reset did not return to exact bypass'
        effect.close()
        checks.append(f'{rate} Hz exact bypass, pause/resume, epoch reset, mute, schedule gap, silence')

    # Block-boundary invariance protects against scheduler-dependent tape jumps.
    signal = reference(48000, 5)
    a, b = Effect(definition), Effect(definition)
    a.set(0, 1); b.set(0, 1)
    result_a, result_b = a.process(signal, 127), b.process(signal, 1024)
    assert np.array_equal(result_a, result_b), 'Transport depends on DSP block size'
    a.close(); b.close()
    checks.append('Bit-exact block-size invariance, 127 vs 1024 frames')

    # Identical spatial channels remain identical even through the heaviest burst.
    for channels in [1, 2, 6, 8]:
        effect = Effect(definition)
        effect.set(0, 1)
        identical = np.repeat(signal[:, :1], channels, axis=1)
        processed = effect.process(identical)
        assert np.all(np.isfinite(processed)) and np.max(np.abs(processed)) <= np.max(np.abs(identical)) + 1e-6
        assert np.array_equal(processed, np.repeat(processed[:, :1], channels, axis=1))
        effect.close()
    checks.append('Mono/stereo/5.1/7.1 channel coherence, finite and amplitude-bounded output')

    # Recovery changes transport origin, so old audio must never persist.
    effect = Effect(definition)
    effect.set(0, 1)
    effect.process(signal)
    effect.set(0, 0)
    recovery_input = np.tile(signal[:4800], (40, 1))
    recovered = effect.process(recovery_input)
    assert np.array_equal(recovered[-1024:], recovery_input[-1024:])
    effect.close()
    checks.append('Sobering returns to exact zero-latency bypass')

    # A constant source exposes changes introduced by the effect itself.
    # Sudden control changes must neither duck it sharply on arrival nor
    # remove its coloration abruptly on recovery. Exercise the shipping
    # DSP at every supported reference rate, not a copy of its envelope math.
    transition_metrics = []
    for rate in rates:
        dc = np.full((rate * 12, 2), .1, dtype=np.float32)
        effect = Effect(definition, rate)
        effect.process(dc[:rate])
        effect.set(0, 1)
        arrival = effect.process(dc[:round(rate * 1.75)])
        effect.set(0, 0)
        recovery = effect.process(dc[:rate * 4])
        effect.close()
        first_50ms = round(rate * .05)
        arrival_delta = float(np.max(np.abs(arrival[:first_50ms] - .1)))
        recovery_delta = float(np.max(np.abs(recovery[:first_50ms] - arrival[-1])))
        assert arrival_delta < .003, f'{rate} Hz effect arrives abruptly: {arrival_delta}'
        assert recovery_delta < .003, f'{rate} Hz effect releases abruptly: {recovery_delta}'
        assert np.array_equal(recovery[-1024:], dc[-1024:]), 'Smooth recovery never reached exact dry'

        effect = Effect(definition, rate)
        effect.set(0, 1)
        episodes = effect.process(dc)[rate * 2:]
        effect.close()
        hop = round(rate * .005)
        episode_delta = float(np.max(np.abs(episodes[hop:] - episodes[:-hop])))
        assert episode_delta < .0035, f'{rate} Hz tape episode changes level sharply: {episode_delta}'
        transition_metrics.append({'rate': rate, 'arrival_50ms_delta': arrival_delta,
                                   'recovery_50ms_delta': recovery_delta,
                                   'episode_5ms_delta': episode_delta})
    checks.append('Gentle onset/recovery and episode envelopes at all four sample rates')

    # Change the target while a repeat head is active, then reverse it and
    # recover. A low, smooth tone reveals any discontinuous transport jump.
    rate = 48000
    t = np.arange(rate * 8) / rate
    tone = np.repeat((.15 * np.sin(2 * np.pi * 110 * t))[:, None], 2, axis=1).astype(np.float32)
    effect = Effect(definition, rate)
    transport = []
    for start, end, level in [(0, 2.2, 100), (2.2, 2.28, 40), (2.28, 3, 100), (3, 8, 0)]:
        effect.set(0, profile(level))
        transport.append(effect.process(tone[round(start * rate):round(end * rate)]))
    effect.close()
    transport = np.concatenate(transport)
    assert float(np.max(np.abs(np.diff(transport, axis=0)))) < .004, 'Changing strength jumps the tape head'
    assert np.array_equal(transport[-1024:], tone[-1024:])
    checks.append('Rapid strength reversals during repeat transport stay continuous and recover dry')

    metrics = []
    for level in [0, 40, 60, 80, 90, 100]:
        effect = Effect(definition)
        effect.set(0, profile(level))
        signal = reference(48000)
        started = time.perf_counter()
        processed = effect.process(signal)
        elapsed = time.perf_counter() - started
        assert np.all(np.isfinite(processed))
        assert np.max(np.abs(processed)) <= np.max(np.abs(signal)) + 1e-6
        if level == 0:
            assert np.array_equal(processed, signal)
        difference = float(np.sqrt(np.mean((processed - signal) ** 2)))
        metrics.append({'level': level, 'intensity': profile(level),
                        'difference_rms': difference,
                        'peak': float(np.max(np.abs(processed))),
                        'processing_seconds': round(elapsed, 4)})
        save_wav(output / f'intoxication-{level:03d}.wav', processed, 48000)
        effect.close()
    assert all(metrics[i]['difference_rms'] < metrics[i + 1]['difference_rms'] for i in range(5))
    checks.append('Six comparative renders increase monotonically in signal deformation')
    # One additional listening clip includes abrupt debug-like strength
    # changes over continuous source audio, including a short reversal.
    signal = reference(48000)
    effect = Effect(definition)
    transition_render = []
    for start, end, level in [(0, 2, 0), (2, 7, 100), (7, 7.4, 40),
                              (7.4, 10, 100), (10, 16, 0)]:
        effect.set(0, profile(level))
        transition_render.append(effect.process(signal[round(start * 48000):round(end * 48000)]))
    effect.close()
    save_wav(output / 'intoxication-smooth-transitions.wav', np.concatenate(transition_render), 48000)
    return {'result': 'passed', 'checks': checks, 'transitions': transition_metrics, 'renders': metrics}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--plugin', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    dll = C.WinDLL(str(args.plugin.resolve()))
    definitions = C.POINTER(C.POINTER(Definition))()
    dll.UnityGetAudioEffectDefinitions.argtypes = [C.POINTER(C.POINTER(C.POINTER(Definition)))]
    dll.UnityGetAudioEffectDefinitions.restype = C.c_int
    assert dll.UnityGetAudioEffectDefinitions(C.byref(definitions)) == 1
    definition = definitions[0].contents
    assert definition.structsize == C.sizeof(Definition)
    assert definition.paramstructsize == C.sizeof(Parameter)
    assert definition.apiversion == 0x010402
    assert definition.name == b'Intoxication VHS'
    assert [definition.paramdefs[i].name for i in range(3)] == [b'Intensity', b'Paused', b'Reset']
    report = validate(definition, args.output)
    (args.output / 'validation.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
