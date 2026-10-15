"""Deterministic dry material voices for the isolated warehouse's crate porters.

The model generator imports audio_files(); the same finite bank can be authored
or validated without Blender. Room reflections belong to Unity's warehouse mix.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import math
from pathlib import Path
import struct
import wave

SAMPLE_RATE = 24000
REVEAL_SECONDS = 1.4
AUDIO_NAMES = (
    "ZombieReveal", "ZombieGroan", "ZombieGroan2", "ZombieGroan3",
    "ZombieLunge", "ZombieLunge2", "ZombieHurtWood", "ZombieHurtWood2",
    "ZombieHurtFlesh", "ZombieHurtFlesh2", "ZombieDeath", "ZombieDeath2",
)
_SPECS = (
    ("reveal", REVEAL_SECONDS, 0), ("groan", 1.15, 0), ("groan", 1.29, 1), ("groan", 1.08, 2),
    ("attack", .42, 0), ("attack", .46, 1), ("wood", .31, 0), ("wood", .35, 1),
    ("flesh", .32, 0), ("flesh", .37, 1), ("death", 1.18, 0), ("death", 1.31, 1),
)


def _pulse(time: float, onset: float, attack: float, release: float) -> float:
    elapsed = time - onset
    return 0.0 if elapsed <= 0.0 else min(1.0, elapsed / attack) * math.exp(-elapsed / release)


def _window(time: float, onset: float, duration: float) -> float:
    phase = (time - onset) / duration
    return math.sin(math.pi * phase) ** 1.3 if 0.0 < phase < 1.0 else 0.0


def _wood(time: float, onset: float, variant: int, noise: float, damp: float) -> float:
    elapsed = time - onset
    if elapsed <= 0.0:
        return 0.0
    scale = 1.0 + variant * .067
    # Loaded boards make one splintering edge and several short, unequal modes.
    crack = (noise - damp * .7) * math.exp(-elapsed * 126.0) * .8
    modes = 0.0
    for frequency, decay, gain in ((173., 22., .36), (431., 35., .23), (1187., 53., .15), (2293., 77., .07)):
        modes += math.sin(math.tau * frequency * scale * elapsed) * math.exp(-elapsed * decay) * gain
    return (crack + modes) * min(1.0, elapsed / .0018)


def _wet(time: float, onset: float, variant: int, low: float, damp: float) -> float:
    elapsed = time - onset
    if elapsed <= 0.0:
        return 0.0
    pressure = math.sin(math.tau * ((133. + variant * 17.) * elapsed - 82. * elapsed * elapsed))
    membrane = math.sin(math.tau * 341. * elapsed) * math.sin(math.tau * 37. * elapsed)
    return _pulse(time, onset, .004, .092) * (low * 3.3 + damp * .56 + pressure * .35 + membrane * .1)


def _throat(time: float, onset: float, duration: float, variant: int, low: float, damp: float,
            compressed: bool = False) -> float:
    elapsed = time - onset
    if elapsed <= 0.0 or elapsed >= duration:
        return 0.0
    phase = elapsed / duration
    base = (65. + variant * 5.) * (1.0 - .17 * phase)
    jitter = .3 * math.sin(elapsed * 41.) + .17 * math.sin(elapsed * 89.)
    glottal_phase = math.tau * (base * elapsed + jitter * .065)
    voiced = 0.0
    for harmonic in (1, 2, 3, 5, 7, 11):
        voiced += math.sin(glottal_phase * harmonic) / (harmonic ** 1.12)
    rattle = .56 + .44 * math.sin(elapsed * math.tau * (19. + variant)) ** 4
    formant = math.sin(glottal_phase * 6.) * .14 + math.sin(glottal_phase * 14.) * .055
    wet = low * 1.8 * math.sin(elapsed * 113.) ** 2
    pressure = (voiced * .3 + formant) * rattle + damp * .38 + wet
    if compressed:
        pressure = math.tanh(pressure * 2.1) * .72
    return pressure * _window(time, onset, duration)


def _samples(kind: str, duration: float, variant: int) -> list[float]:
    count = round(duration * SAMPLE_RATE)
    seed = (0xA38D61B7 + variant * 7919 + sum(ord(char) for char in kind) * 131) & 0xFFFFFFFF
    low = damp = last_input = last_output = 0.0
    samples = []
    for index in range(count):
        time = index / SAMPLE_RATE
        seed ^= (seed << 13) & 0xFFFFFFFF
        seed ^= seed >> 17
        seed ^= (seed << 5) & 0xFFFFFFFF
        seed &= 0xFFFFFFFF
        noise = (seed >> 8) * (2. / 16777215.) - 1.
        low += .026 * (noise - low)
        damp += .17 * (noise - damp)
        if kind == "reveal":
            # Exact limb contacts in the authored reveal: feet, knee, hands, head.
            tension = _window(time, .015, .30) * (
                math.sin(math.tau * (184. * time + 121. * time * time)) * .14 + damp * .15)
            value = tension
            for onset, timber, gain in ((.24, 0, .55), (.30, 1, .32), (.35, 2, .71),
                                        (.59, 1, .65), (.70, 2, .64), (.91, 0, .82)):
                value += _wood(time, onset, timber, noise, damp) * gain
            value += _wet(time, .36, 0, low, damp) * .63 + _wet(time, .71, 1, low, damp) * .42
            value += _wet(time, .92, 0, low, damp) * .47
            value += _throat(time, .94, .43, 1, low, damp, True) * .93
            value += (noise - damp) * _window(time, .91, .31) * .10
        elif kind == "groan":
            value = _throat(time, .025, duration - .05, variant, low, damp)
            value += _wet(time, .62 + variant * .04, variant, low, damp) * .12
        elif kind == "attack":
            value = _throat(time, .008, duration - .025, variant + 1, low, damp, True) * .93
            value += damp * _pulse(time, .015, .012, .075) * .34
            # Timber strain follows the loaded torso, not a fictitious floor impact.
            value += _wood(time, .07, variant, noise, damp) * .17
        elif kind == "wood":
            value = _wood(time, .005, variant, noise, damp)
            value += _wood(time, .048 + variant * .012, variant + 2, noise, damp) * .29
        elif kind == "flesh":
            value = _wet(time, .006, variant, low, damp)
            value += damp * _pulse(time, .017, .005, .052) * .27
        else:
            # Air escapes and weight loses tension. No baked floor thud: the
            # physical body may die in a fall, and its contacts own those sounds.
            value = _throat(time, .012, duration * .62, variant + 2, low, damp, True) * .8
            value += damp * _window(time, duration * .31, duration * .56) * .34
            value += _wet(time, .29, variant, low, damp) * .48
            value += _wood(time, .14, variant + 1, noise, damp) * .34
            value += _wood(time, .59, variant + 2, noise, damp) * .15
        value = math.tanh(value * 1.45)
        # A dry mono source, no DC, no baked echo and no discontinuous ends.
        filtered = value - last_input + .995 * last_output
        last_input, last_output = value, filtered
        samples.append(filtered * min(1., time / .004, (duration - time) / .012))
    peak = max(abs(value) for value in samples)
    gain = .89 / max(peak, .001)
    return [value * gain for value in samples]


def audio_files() -> dict[str, bytes]:
    result = {}
    for name, (kind, duration, variant) in zip(AUDIO_NAMES, _SPECS, strict=True):
        samples = _samples(kind, duration, variant)
        output = io.BytesIO()
        with wave.open(output, "wb") as writer:
            writer.setnchannels(1)
            writer.setsampwidth(2)
            writer.setframerate(SAMPLE_RATE)
            writer.writeframes(struct.pack("<" + "h" * len(samples), *(round(value * 32767.) for value in samples)))
        result[name + ".wav"] = output.getvalue()
    return result


def validate(files: dict[str, bytes]) -> None:
    if len(files) != len(AUDIO_NAMES) or len(set(files.values())) != len(files):
        raise ValueError("The porter requires a complete, distinct finite audio bank.")
    for name, data in files.items():
        with wave.open(io.BytesIO(data), "rb") as reader:
            if reader.getparams()[:3] != (1, 2, SAMPLE_RATE):
                raise ValueError("The porter audio must remain mono PCM at the authored rate: " + name)
            count = reader.getnframes()
            samples = struct.unpack("<" + "h" * count, reader.readframes(count))
        peak = max(abs(value) for value in samples) / 32767.
        rms = math.sqrt(sum(value * value for value in samples) / len(samples)) / 32767.
        if not .87 < peak < .90 or not .035 < rms < .38 or abs(samples[0]) > 1 or abs(samples[-1]) > 90:
            raise ValueError(f"Invalid porter audio envelope {name}: peak={peak:.4f}, rms={rms:.4f}")
    with wave.open(io.BytesIO(files["ZombieReveal.wav"]), "rb") as reader:
        if reader.getnframes() != round(REVEAL_SECONDS * SAMPLE_RATE):
            raise ValueError("The unfolding sound must retain the reveal timeline.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, default=Path(__file__).resolve().parents[1] / "Assets/Resources/Combat")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    files = audio_files()
    validate(files)
    if files != audio_files():
        raise ValueError("Porter audio generation is not deterministic.")
    if args.validate_only:
        for name, data in files.items():
            if (args.output_dir / name).read_bytes() != data:
                raise ValueError("Published porter audio differs: " + name)
    else:
        args.output_dir.mkdir(parents=True, exist_ok=True)
        for name, data in files.items():
            (args.output_dir / name).write_bytes(data)
    signature = hashlib.sha256(b"".join(files.values())).hexdigest()
    print("PORTER MATERIAL AUDIO / FINITE MONO PCM / REVEAL TIMELINE / DETERMINISTIC OK " + signature)


if __name__ == "__main__":
    main()
