"""Exercise the shipping DLL through Unity's ABI and render listening references."""
import argparse
import ctypes as C
import json
import math
from pathlib import Path
import re
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


def published(header=None):
    """The header's own numbers, read with the SAME regex the C# mirror uses.

    The validator used to keep its own copy of three of them, which made it a
    fourth uncontrolled mirror of a contract that already had two.
    """
    path = header or (Path(__file__).resolve().parent / 'OpticalProcessor.h')
    pattern = re.compile(
        r'static\s+constexpr\s+(?:double|int)\s+(\w+)\s*=\s*(-?\d+(?:\.\d+)?)\s*;')
    found = {name: float(value)
             for name, value in pattern.findall(Path(path).read_text(encoding='utf-8'))}
    assert found, 'No published constants were found in the native header.'
    return found


def log_lerp(start, end, amount):
    return math.exp(math.log(start) + (math.log(end) - math.log(start)) * amount)


def rail(K, weight):
    """The amplifier's rail at this weight: absolute, not input-referenced."""
    return log_lerp(K['BypassCeiling'], K['OutputCeiling'], weight)


def quiet_ceiling(K, weight):
    """What the print alone may put over a quiet room.

    The surface and its dust are injected before the slit, so the amplifier's
    bell lifts them and the lamp's ripple multiplies them; the apparatus joins
    afterwards and is lifted by neither.
    """
    bell = 10 ** (K['PresenceGainDb'] * weight ** K['AmplifierArrivalExponent'] / 20)
    surface = K['SurfaceAmplitude'] * weight ** K['SurfaceExponent']
    dust = K['DustAmplitude'] * weight ** K['DustExponent']
    transport = K['TransportAmplitude'] * weight ** K['TransportExponent']
    return (surface + dust) * bell * (1 + K['LampRippleDepth']) + transport


def settling_seconds(K):
    """The slowest published behaviour is the mixer's hand, and it is a SLEW.

    A probe shorter than his whole travel measures an unconverged ride and
    biases every distortion reading downward - on the one metric this design
    is judged by.
    """
    return K['ExposureMaxGainDb'] / (K['GainRiseDbPerPicture'] * K['PicturesPerSecond']) \
        + 3 * K['DezipperSeconds']


def signal_path(definition, rate, samples, weight):
    """The print's answer to a signal, with the print's own voice removed.

    Two identically seeded fresh instances, differenced. It is exact because
    the noise is generated on a counter that advances once per sample whatever
    the input, and because the noise joins AFTER the emulsion - but only while
    the rail is idle, which every caller asserts.
    """
    wet = Effect(definition, rate)
    wet.set(0, weight)
    printed = wet.process(samples)
    wet.close()
    hush = Effect(definition, rate)
    hush.set(0, weight)
    alone = hush.process(np.zeros_like(samples))
    hush.close()
    return printed.astype(np.float64) - alone.astype(np.float64), printed


def line(samples, rate, hertz):
    """The magnitude of one line, integrated over the band the swim gives it.

    The swim is a VARIABLE delay, so every partial is frequency modulated: at
    4.2 kHz a 1.25 % speed error spreads the line over +-52 Hz. A three-bin
    reading of that measures the wow rather than the response.
    """
    window = np.hanning(len(samples))
    power = np.abs(np.fft.rfft(samples * window)) ** 2
    frequencies = np.fft.rfftfreq(len(samples), 1.0 / rate)
    half = 0.03 * hertz + 60.0
    band = (frequencies > hertz - half) & (frequencies < hertz + half)
    return float(np.sqrt(power[band].sum()))


def distortion(definition, rate, weight, peak, hertz=440.0):
    """Total harmonic distortion of the print, on the signal path alone.

    440 Hz because it is a clean fifth above the print's own corner and its
    harmonics 2..14 all sit inside the slit's passband, so one probe serves
    both the arrival and this.
    """
    K = published()
    seconds = settling_seconds(K) + 3.0
    t = np.arange(int(rate * seconds)) / rate
    tone = np.repeat((peak * np.sin(2 * np.pi * hertz * t))[:, None], 2, axis=1).astype(np.float32)
    path, printed = signal_path(definition, rate, tone, weight)
    # The peak is read on the SETTLED tail, which is also where the harmonics
    # are read. The first sixty milliseconds are the de-zipper's own, and
    # there the print is still nearly the dry signal, so a loud probe would
    # trip a settled-weight rail that was not in force yet.
    settled_peak = float(np.max(np.abs(printed[-int(rate * 2.5):])))
    assert settled_peak < 0.9 * rail(K, weight), \
        'The rail engaged, so the signal-path difference is not exact'
    tail = path[-int(rate * 2.5):, 0]
    fundamental = line(tail, rate, hertz)
    harmonics = sum(line(tail, rate, hertz * order) ** 2
                    for order in range(2, 21) if hertz * order < rate / 2 - 200)
    return (100 * math.sqrt(harmonics) / max(fundamental, 1e-30),
            float(np.sqrt(np.mean(tail ** 2))),
            settled_peak)


def validate_optical(definition, output):
    """The print SPEAKS and the print TEARS, so most of the tape's assertions
    are inverted here.

    A tape only transforms what it is given; a projector runs over a quiet room
    too, and a film print is a saturating medium whose output level is set by
    its own density rather than by what it was handed. So silence in does not
    mean silence out; the output is bounded ABSOLUTELY rather than by the input
    peak, because a quiet bus is printed LOUDER and a loud one quieter; and the
    tape's DC arrival probe cannot be used at all, since an optical track has
    no DC and the print's own highpass would read as an abrupt arrival.
    """
    K = published()
    checks = []
    rates = [22050, 44100, 48000, 96000]
    rng = np.random.default_rng(9041)

    for rate in rates:
        effect = Effect(definition, rate)
        dry = rng.uniform(-.35, .35, (rate // 5, 2)).astype(np.float32)
        assert np.array_equal(effect.process(dry, 127), dry), 'Rest weight changed samples'
        fresh = Effect(definition, rate)
        fresh.set(0, 1)
        fresh_result = fresh.process(dry)
        fresh.close()
        effect.set(0, 1)
        effect.process(dry)
        effect.set(1, 1)
        assert not np.any(effect.process(dry)), 'Paused projector leaked audio'
        effect.set(1, 0)
        assert np.array_equal(effect.process(dry), fresh_result), 'Resume retained pre-pause state'
        effect.process(dry)
        effect.set(2, 5)
        assert np.array_equal(effect.process(dry), fresh_result), 'Epoch did not thread a fresh reel'
        effect.process(dry)
        effect.state.flags = 1 | 4
        assert not np.any(effect.process(dry)), 'Editor mute leaked audio'
        effect.state.flags = 1
        assert np.array_equal(effect.process(dry), fresh_result), 'Unmute retained old state'
        effect.process(dry)
        effect.state.currdsptick += 2048
        assert np.array_equal(effect.process(dry), fresh_result), 'DSP scheduling gap retained stale state'
        effect.reset()
        effect.set(0, 0)
        assert np.array_equal(effect.process(dry), dry), 'Reset did not return to exact bypass'
        effect.close()
        checks.append(f'{rate} Hz exact bypass, pause/resume, epoch reset, mute, schedule gap')

    a, b = Effect(definition), Effect(definition)
    a.set(0, 1); b.set(0, 1)
    signal = reference(48000, 5)
    assert np.array_equal(a.process(signal, 127), b.process(signal, 1024)), \
        'The print depends on DSP block size'
    a.close(); b.close()
    checks.append('Bit-exact block-size invariance, 127 vs 1024 frames')

    # One optical track: identical channels stay identical, and the output is
    # bounded by the amplifier's rail rather than by what it was handed.
    for channels in [1, 2, 6, 8]:
        effect = Effect(definition)
        effect.set(0, 1)
        identical = np.repeat(signal[:, :1], channels, axis=1)
        processed = effect.process(identical)
        assert np.all(np.isfinite(processed))
        assert np.max(np.abs(processed)) <= rail(K, 1.0) + 1e-6
        assert np.array_equal(processed, np.repeat(processed[:, :1], channels, axis=1))
        effect.close()
    checks.append('Mono/stereo/5.1/7.1 coherence, finite and inside the amplifier rail')

    # The rail is a RAIL and not an operating point: nothing a bus can carry
    # may reach it, and nothing may pass it. A full-scale square is the worst
    # case a mixer can hand a medium whose own density sets its level.
    abuse = {
        'full-scale square': np.repeat(np.sign(np.sin(
            2 * np.pi * 70 * np.arange(48000 * 16) / 48000))[:, None], 2, axis=1),
        'full-scale noise': rng.uniform(-1, 1, (48000 * 16, 2)),
        'a quiet room': rng.uniform(-.01, .01, (48000 * 16, 2)),
        'direct current': np.full((48000 * 16, 2), 0.9),
        'below the hold peak': rng.uniform(-.0005, .0005, (48000 * 16, 2)),
        'silence': np.zeros((48000 * 16, 2)),
    }
    abuse_metrics = []
    for name, samples in abuse.items():
        effect = Effect(definition)
        effect.set(0, 1)
        # The hand needs its whole travel before the level it settles at can
        # be read: a six-second probe measured a quiet room while the mixer
        # was still pushing the fader up, and read the noise floor.
        processed = effect.process(samples.astype(np.float32))[-48000 * 2:]
        effect.close()
        peak = float(np.max(np.abs(processed)))
        assert np.all(np.isfinite(processed)), f'{name} produced a non-finite sample'
        assert peak <= rail(K, 1.0) + 1e-6, f'{name} passed the rail: {peak}'
        abuse_metrics.append({'input': name, 'peak': round(peak, 5)})
    printed_peak = {entry['input']: entry['peak'] for entry in abuse_metrics}
    # Anything the meter can SEE prints at one density, because the medium's
    # own geometry sets the level and the projector's fader is fixed. That is
    # the property the fader buys, and it is what makes the rail genuine
    # headroom rather than a second clipper.
    carried = [printed_peak[name] for name in
               ('full-scale square', 'full-scale noise', 'a quiet room')]
    assert max(carried) / max(min(carried), 1e-9) < 3.0, \
        f'The print level follows the bus rather than the medium: {abuse_metrics}'
    assert max(printed_peak.values()) < 0.9 * rail(K, 1.0), \
        f'The rail is an operating point rather than headroom: {abuse_metrics}'
    # An optical track has no DC at all, so direct current is silence to it -
    # which is also what keeps the deliberately asymmetric emulsion from
    # leaving an offset behind.
    assert printed_peak['direct current'] <= 1.1 * printed_peak['silence'], \
        f'Direct current reached the track: {abuse_metrics}'
    # And below the meter's hold peak the mixer's hand stays off the fader
    # instead of chasing a quiet room to the top of its travel.
    assert printed_peak['below the hold peak'] <= 1.2 * printed_peak['silence'], \
        f'The hand chased a room quieter than its own meter: {abuse_metrics}'
    checks.append('A square, full-scale noise and a quiet room all print at '
                  'one density inside the rail; DC and anything under the '
                  'meter hold peak print as silence does')

    # The tape asserts silence in, silence out. Here that must be FALSE.
    rate = 48000
    effect = Effect(definition, rate)
    effect.set(0, 1)
    over_silence = effect.process(np.zeros((rate * 4, 2), dtype=np.float32))
    effect.close()
    assert np.all(np.isfinite(over_silence))
    quiet_peak = float(np.max(np.abs(over_silence)))
    assert quiet_peak > .0005, 'The print is inaudible over a quiet room'
    assert quiet_peak <= quiet_ceiling(K, 1.0) + 1e-6, \
        f'Generated surface exceeds its declared floor: {quiet_peak}'
    checks.append('Surface and transport speak over silence, inside the declared floor')

    # The apparatus runs at exactly twenty-four pictures a second, which is
    # the one number the ear can match to the screen. 22050 is not divisible
    # by 24, so this also proves the picture counter is a fractional one - and
    # the picture must be the LARGEST periodicity the print has, or the lamp's
    # own ripple has quietly taken the machine over.
    grid_metrics = []
    for rate in [22050, 48000]:
        effect = Effect(definition, rate)
        effect.set(0, 1)
        quiet = effect.process(np.zeros((rate * 4, 2), dtype=np.float32))
        effect.close()
        envelope = np.abs(quiet[rate:, 0].astype(np.float64))
        envelope -= envelope.mean()
        spectrum = np.abs(np.fft.rfft(envelope * np.hanning(len(envelope))))
        frequencies = np.fft.rfftfreq(len(envelope), 1. / rate)
        picture = int(np.argmin(np.abs(frequencies - 24.)))
        strength = float(spectrum[picture - 1:picture + 2].max())
        floor = float(np.median(spectrum[(frequencies >= 5) & (frequencies <= 200)]))
        assert strength > 8 * floor, f'{rate} Hz transport is not locked to the picture grid'
        # Everything loud in the apparatus must sit ON the picture grid: the
        # claw pulls once a picture and the shutter's second blade falls
        # halfway through it, so 48 Hz is the picture's own second harmonic
        # and not a rival. The rival to watch for is the exciter lamp, whose
        # ripple is at twice mains and lands on no multiple of twenty-four:
        # at the first tuning of its depth it would have become the loudest
        # thing the machine did while this assertion still passed.
        band = (frequencies >= 5) & (frequencies <= 200)
        grid = np.zeros_like(frequencies, dtype=bool)
        for harmonic in range(1, 9):
            grid |= np.abs(frequencies - 24. * harmonic) <= 3.
        stranger = float(spectrum[band & ~grid].max())
        assert strength > stranger, \
            f'{rate} Hz: something off the picture grid is louder than the ' \
            f'picture itself: {stranger} against {strength}'
        grid_metrics.append({'rate': rate, 'picture_strength_over_floor': round(strength / floor, 2)})
    checks.append('Transport locked to 24 pictures a second at 22050 and 48000 Hz, '
                  'and the picture is the largest periodicity the print has')

    # THE PRINT TEARS. This is the contract the first tuning had none of: it
    # shipped 1.4-4 % distortion, which is a warm tint, and the tint was
    # measured only by ear. Referenced to the medium's own modulation rather
    # than to dBFS, the tearing must be the SAME for a whisper and for a
    # shout, because a dubbing mixer rides both to the same meter.
    tear_metrics = []
    for peak, name in [(.02, '-34 dBFS'), (.1, '-20 dBFS'), (.4, '-8 dBFS')]:
        total, level, printed_peak = distortion(definition, 48000, 1.0, peak)
        tear_metrics.append({'input': name, 'thd_percent': round(total, 2),
                             'signal_rms': round(level, 5),
                             'printed_peak': round(printed_peak, 4)})
    assert all(entry['thd_percent'] > 20.0 for entry in tear_metrics), \
        f'The print does not tear: {tear_metrics}'
    spread = max(e['thd_percent'] for e in tear_metrics) / \
        max(min(e['thd_percent'] for e in tear_metrics), 1e-9)
    assert spread < 1.4, \
        f'The tearing follows the bus level rather than the medium: {tear_metrics}'
    checks.append('Above 20 % distortion at every bus level, within 40 % of '
                  'itself from a whisper to a shout')

    # And it must ARRIVE, monotonically, so the fifteen seconds read as a
    # print being struck rather than as a switch being thrown.
    weights = [0., .1, .2, .35, .5, .65, .8, 1.]
    arrival_tear = [round(distortion(definition, 48000, weight, .1)[0], 2)
                    for weight in weights]
    assert arrival_tear[0] < 0.5, \
        f'The emulsion is not a straight line at rest: {arrival_tear[0]} %'
    assert all(arrival_tear[i] < arrival_tear[i + 1] for i in range(len(weights) - 1)), \
        f'The tearing does not arrive monotonically: {arrival_tear}'
    # And it must arrive THROUGHOUT, not at the end. This is the user's own
    # verdict turned into a number: the first version of this chain measured
    # 0.79 % at a third of the way in and 1.4 % at two thirds, so nothing
    # could be heard changing until the last few seconds and the mode read as
    # a switch thrown rather than as a print being struck. The shape belongs
    # to the print - it is a fixed twenty decibels of headroom closing on the
    # track's own reference - so it cannot depend on how loud the room is.
    assert arrival_tear[weights.index(.2)] > 4.0, \
        f'Nothing is tearing yet a fifth of the way in: {arrival_tear}'
    assert arrival_tear[weights.index(.35)] > 8.0, \
        f'The print is still clean a third of the way in: {arrival_tear}'
    assert arrival_tear[weights.index(.5)] > 15.0, \
        f'Half the ramp has passed with less than half the tearing: {arrival_tear}'
    checks.append(f'Tearing arrives monotonically and THROUGHOUT the ramp, '
                  f'not at its end: {arrival_tear}')

    # THE GATE, measured LINEARLY. The old measure ran loud broadband noise
    # through a clipper and called the result a filter response; it also ran
    # at one rate, which is exactly why an exponential pole's stopband floor
    # at 22050 was invisible for a whole release. A multitone BELOW the
    # exposure meter's hold peak leaves the mixer's hand off the fader
    # altogether, so the print is linear there and the difference is exact.
    voices = [120, 300, 1000, 2000, 3150, 4200, 6300, 8000, 10000, 16000]
    gate_metrics = []
    for rate in rates:
        usable = [f for f in voices if f < 0.4 * rate]
        t = np.arange(rate * 3) / rate
        probe = sum(np.sin(2 * np.pi * f * t + index) for index, f in enumerate(usable))
        probe = probe * (0.8 * K['ExposureHoldPeak'] / max(np.max(np.abs(probe)), 1e-9))
        stereo = np.repeat(probe[:, None], 2, axis=1).astype(np.float32)
        opened, _ = signal_path(definition, rate, stereo, 0.0)
        closed, printed = signal_path(definition, rate, stereo, 1.0)
        assert float(np.max(np.abs(printed))) < 0.9 * rail(K, 1.0)
        tail = slice(-rate * 2, None)
        response = {f: line(closed[tail, 0], rate, f) / max(line(opened[tail, 0], rate, f), 1e-30)
                    for f in usable}
        shape = {f: round(20 * math.log10(max(value / response[1000], 1e-12)), 2)
                 for f, value in response.items()}
        gate_metrics.append({'rate': rate, 'relative_to_1k_db': shape})
        assert shape[120] <= -6.0, f'{rate} Hz: the print keeps its bass: {shape[120]}'
        assert shape[4200] >= 6.0, \
            f'{rate} Hz: the print is not forward in the presence band: {shape[4200]}'
        assert shape[6300] < shape[4200] - 6.0, \
            f'{rate} Hz: the bell has no cliff above it: {shape}'
        # The top voice each rate can carry. An exponential pole cannot
        # attenuate past its own stopband floor of c/(2-c), about twelve
        # decibels for the whole cascade, so a topology that cannot close
        # the slit shows up here and nowhere else - and only at the low
        # rates, which is why this runs at all four.
        top = max(shape)
        assert shape[top] <= -15.0 and shape[top] <= shape[4200] - 25.0, \
            f'{rate} Hz: the slit never closes at {top} Hz: {shape}'
    checks.append('The slit closes and the bell speaks alike at all four rates, '
                  'measured linearly below the meter hold peak')

    # Arrival and recovery, measured on a tone above the print's own highpass
    # rather than on DC. The measure is the windowed level, because the dust
    # clicks are deliberate transients and a sample-difference measure would
    # be reading them rather than the ramp. Every threshold is RELATIVE: an
    # absolute one silently loosens the moment the print's level moves.
    transition_metrics = []
    for rate in rates:
        seconds = settling_seconds(K) + 4.0
        t = np.arange(int(rate * (seconds + 9))) / rate
        tone = np.repeat((.2 * np.sin(2 * np.pi * 440 * t))[:, None], 2, axis=1).astype(np.float32)
        effect = Effect(definition, rate)
        effect.process(tone[:rate])
        effect.set(0, 1)
        arrival = effect.process(tone[rate:rate + int(rate * seconds)])
        effect.set(0, 0)
        recovery = effect.process(tone[rate + int(rate * seconds):])
        effect.close()
        moving = np.concatenate([arrival, recovery])[:, 0].astype(np.float64)
        # One window per picture, so the dip the print takes once per picture
        # cancels exactly instead of being read as roughness.
        block = round(rate / 24)
        blocks = len(moving) // block
        level = np.sqrt((moving[:blocks * block].reshape(-1, block) ** 2).mean(axis=1))
        curvature = np.abs(level[1:-1] - .5 * (level[:-2] + level[2:]))

        settling = round(rate * settling_seconds(K)) // block + 1
        steady = np.ones(len(curvature), dtype=bool)
        for start in [0, len(arrival) // block]:
            steady[max(0, start - 1):start + settling] = False
        assert steady.any(), 'The probe is too short to hold a settled stretch'
        # The MEDIAN, not the maximum: eleven dust clicks a second are
        # deliberate content and land in about half the windows, so a maximum
        # would be reading the dust. Relative, so a level retune cannot
        # silently loosen it.
        settled = float(np.median(
            curvature[steady] / np.maximum(level[1:-1][steady], 1e-9)))
        assert settled < .02, f'{rate} Hz settled print is not steady: {settled}'
        edge = len(arrival) // block
        converged = level[edge - 48:edge]
        assert float(np.max(converged) / max(np.min(converged), 1e-9)) < 1.05, \
            f'{rate} Hz: the hand had not settled where the probe measured it'

        during = level[:edge]
        rises = float(np.max(np.diff(during)) / max(np.max(during), 1e-9))
        assert rises < .030, f'{rate} Hz print jumps upward while arriving: {rises}'
        # The recovery's own de-zipper is sixty milliseconds and a picture is
        # forty-two, so the print's whole level difference from dry is spent
        # in about one window: that is the ramp, not a slam, and measuring it
        # would only be measuring DezipperSeconds. What must be watched is
        # what happens AFTER it, where the weight has settled and the exact
        # bypass latches - and where the wet path's own oversampler latency
        # would otherwise put a time jump of a full-amplitude signal.
        latch = edge + round(rate * 6 * K['DezipperSeconds']) // block + 1
        leaving = level[latch:]
        steps = float(np.max(np.abs(np.diff(leaving))) / max(np.max(leaving), 1e-9))
        assert steps < .08, f'{rate} Hz print slams at the bypass latch: {steps}'

        assert np.array_equal(recovery[-1024:], tone[rate + int(rate * seconds):][-1024:]), \
            'Smooth recovery never reached exact dry'
        transition_metrics.append({'rate': rate,
                                   'settled_relative_curvature': round(settled, 5),
                                   'arrival_upward_step': round(rises, 5),
                                   'recovery_step': round(steps, 5)})
    checks.append('Gentle arrival, a latch that does not slam, and an exact '
                  'dry recovery at all four sample rates')

    # A weight re-raised in a single block onto a hand that is already high is
    # the one case the arrival theorem's second term can reach.
    rate = 48000
    t = np.arange(rate * 24) / rate
    tone = np.repeat((.15 * np.sin(2 * np.pi * 440 * t))[:, None], 2, axis=1).astype(np.float32)
    effect = Effect(definition, rate)
    reraise = []
    for start, end, weight in [(0, 14, 1.), (14, 15, .2), (15, 24, 1.)]:
        effect.set(0, weight)
        reraise.append(effect.process(tone[round(start * rate):round(end * rate)]))
    effect.close()
    reraise = np.concatenate(reraise)[:, 0].astype(np.float64)
    block = round(rate / 24)
    blocks = len(reraise) // block
    level = np.sqrt((reraise[:blocks * block].reshape(-1, block) ** 2).mean(axis=1))
    # The de-zipper's own sixty milliseconds are excluded at each change: the
    # bands open and close with the weight and that transition is what
    # DezipperSeconds is for. What is being asserted is the MIXER'S HAND -
    # that a weight re-raised onto a hand already at the top of its travel
    # cannot step the published gain, which is the one case the arrival
    # theorem's second term can reach.
    settle = round(rate * 6 * K['DezipperSeconds']) // block + 1
    steady = np.ones(len(level) - 1, dtype=bool)
    for second in [0, 14, 15]:
        edge = round(second * rate) // block
        steady[max(0, edge - 1):edge + settle] = False
    jump = float(np.max(np.abs(np.diff(level))[steady]) / max(np.max(level), 1e-9))
    assert jump < .08, f'A re-raised weight steps the published gain: {jump}'
    checks.append(f'A weight re-raised in one block steps the gain by {jump:.3f}')

    # Six comparative renders for listening, plus the ramp the mode actually
    # uses. A plain difference measure is useless here: the swim is a VARIABLE
    # delay, so on tonal material the difference is a phase reading that rises
    # and falls. Measure what the print actually does, all of it phase-blind:
    # the two channels fold into one track, the surface arrives, and the print
    # comes forward in the band where sharpness lives.
    metrics = []
    signal = reference(48000, 16)
    probe = rng.uniform(-.3, .3, (48000 * 4, 2)).astype(np.float32)
    hush = np.zeros((48000 * 4, 2), dtype=np.float32)
    for weight in [0., .2, .4, .6, .8, 1.]:
        effect = Effect(definition)
        effect.set(0, weight)
        started = time.perf_counter()
        processed = effect.process(signal)
        elapsed = time.perf_counter() - started
        assert np.all(np.isfinite(processed))
        assert np.max(np.abs(processed)) <= rail(K, weight) + 1e-6
        if weight == 0:
            assert np.array_equal(processed, signal)
        save_wav(output / f'begotten-{round(weight * 100):03d}.wav', processed, 48000)
        effect.close()

        effect = Effect(definition)
        effect.set(0, weight)
        broadband = effect.process(probe)[-48000 * 2:].astype(np.float64)
        effect.close()
        power = np.abs(np.fft.rfft(broadband[:, 0])) ** 2
        frequencies = np.fft.rfftfreq(len(broadband), 1. / 48000)
        above = float(power[frequencies > 5000].sum() / max(power.sum(), 1e-12))
        # Sharpness is measured on the MUSICAL bed and not on the noise
        # probe: flat noise already carries a fifth of its energy in
        # 1-6 kHz, so the one band the whole redesign is about is the one
        # band a noise probe cannot see a change in. The bed is what the
        # game actually carries, and it is where the print has to bite.
        heard = np.abs(np.fft.rfft(processed[-48000 * 3:, 0].astype(np.float64) *
                                   np.hanning(48000 * 3))) ** 2
        voice = np.fft.rfftfreq(48000 * 3, 1. / 48000)
        forward = float(heard[(voice > 1000) & (voice < 6000)].sum() /
                        max(heard.sum(), 1e-12))
        left, right = broadband[:, 0], broadband[:, 1]
        together = float(np.dot(left, right) /
                         max(np.linalg.norm(left) * np.linalg.norm(right), 1e-12))

        effect = Effect(definition)
        effect.set(0, weight)
        floor = float(np.sqrt(np.mean(effect.process(hush)[-48000 * 2:] ** 2)))
        effect.close()

        metrics.append({'weight': weight, 'presence_share_1k_6k': round(forward, 6),
                        'energy_above_5k': round(above, 6),
                        'channel_correlation': round(together, 6),
                        'quiet_floor_rms': round(floor, 8),
                        'peak': float(np.max(np.abs(processed))),
                        'processing_seconds': round(elapsed, 4)})
    # The amplifier's own voice arrives LAST, so the print is duller than the
    # world for the first part of the ramp and only then comes forward: that
    # is the stage order the fifteen seconds are built on, and a
    # strictly-monotone assertion here would forbid it. What must hold is
    # that the world recedes without vanishing, and that the bite arrives
    # over the last third.
    forward = [entry['presence_share_1k_6k'] for entry in metrics]
    assert all(share > 0.6 * forward[0] for share in forward), \
        f'The print loses the presence band on its way in: {forward}'
    assert all(forward[i] < forward[i + 1] for i in range(3, 5)), \
        f'The bite does not arrive over the last third of the ramp: {forward}'
    assert forward[-1] > 3 * forward[0], \
        f'The print is no sharper than the world it replaces: {forward}'
    assert all(metrics[i]['quiet_floor_rms'] < metrics[i + 1]['quiet_floor_rms'] for i in range(5)), \
        'The print surface does not arrive monotonically'
    assert .004 <= metrics[-1]['quiet_floor_rms'] <= .020, \
        'The settled floor is outside its window: ' + str(metrics[-1]['quiet_floor_rms'])
    assert all(metrics[i]['channel_correlation'] < metrics[i + 1]['channel_correlation'] for i in range(5)), \
        'The two channels do not fold into one track monotonically'
    assert metrics[-1]['channel_correlation'] > .999, 'The print never reaches a single optical track'
    checks.append('Presence, surface and the fold to one track all arrive '
                  'monotonically across six weights')

    effect = Effect(definition)
    arriving = []
    for index in range(0, 20 * 48000, 4800):
        second = index / 48000
        linear = min(1., second / 15.) if second < 18 else max(0., 1. - (second - 18.) / 3.)
        effect.set(0, linear * linear * (3 - 2 * linear))
        arriving.append(effect.process(np.tile(signal, (2, 1))[index:index + 4800]))
    effect.close()
    save_wav(output / 'begotten-fifteen-second-arrival.wav', np.concatenate(arriving), 48000)
    return {'result': 'passed', 'checks': checks, 'grid': grid_metrics,
            'abuse': abuse_metrics, 'tearing': tear_metrics,
            'tearing_arrival': arrival_tear, 'gate': gate_metrics,
            'transitions': transition_metrics, 'renders': metrics}


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
    assert dll.UnityGetAudioEffectDefinitions(C.byref(definitions)) == 2
    tape, optical = definitions[0].contents, definitions[1].contents
    for definition in (tape, optical):
        assert definition.structsize == C.sizeof(Definition)
        assert definition.paramstructsize == C.sizeof(Parameter)
        assert definition.apiversion == 0x010402
    assert tape.name == b'Intoxication VHS'
    assert [tape.paramdefs[i].name for i in range(3)] == [b'Intensity', b'Paused', b'Reset']
    assert optical.name == b'Begotten Optical'
    assert [optical.paramdefs[i].name for i in range(3)] == [b'Weight', b'Paused', b'Reset']
    report = {'intoxication_vhs': validate(tape, args.output),
              'begotten_optical': validate_optical(optical, args.output)}
    (args.output / 'validation.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
