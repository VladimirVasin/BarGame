"""Deterministic bodily voices with quiet thick-bottle resonance for both forms.

No wood, crate, fish, words or baked room response. Attacks share the visible
0.95/1.25-second anticipation; actual contacts remain responsible for impacts.
Glass and contained liquid colour only the existing rise, attack and hurt cues.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import math
from pathlib import Path
import struct
import wave

from combat_zombie_audio import SAMPLE_RATE, _pulse, _throat, _wet, _window

SPECS = (
    ("ZombieDecayRise", "rise", 2.4, 0),
    ("ZombieDecayBreath", "breath", 1.35, 0),
    ("ZombieDecayBreath2", "breath", 1.6, 1),
    ("ZombieDecayBreath3", "breath", 1.2, 2),
    ("ZombieDecayAttack", "attack", 1.32, 0),
    ("ZombieDecayAttack2", "attack", 1.32, 1),
    ("ZombieDecayHurt", "hurt", .4, 0),
    ("ZombieDecayDeath", "death", 1.45, 0),
    ("ZombieCarrierRise", "carrier-rise", 2.4, 0),
    ("ZombieCarrierBreath", "carrier-breath", 2.2, 0),
    ("ZombieCarrierBreath2", "carrier-breath", 2.6, 1),
    ("ZombieCarrierBreath3", "carrier-breath", 2.0, 2),
    ("ZombieCarrierAttack", "carrier-attack", 1.65, 0),
    ("ZombieCarrierAttack2", "carrier-attack", 1.65, 1),
    ("ZombieCarrierHurt", "carrier-hurt", .55, 0),
    ("ZombieCarrierDeath", "carrier-death", 1.85, 0),
)


def _bottle_ring(time: float, onset: float, variant: int, heavy: bool) -> float:
    elapsed = time - onset
    if not 0. < elapsed < .65:
        return 0.
    # Low, unequal modes suggest a thick bottle held against flesh. No brittle
    # high-frequency burst, shard tail or musical/chime interval is added.
    base = (340. if heavy else 545.) * (1. + variant * .027)
    modes = sum(math.sin(math.tau * base * ratio * elapsed) * math.exp(-decay * elapsed) * level
                for ratio, decay, level in ((1., 11., .50), (2.73, 18., .25), (4.12, 29., .12)))
    return modes * min(1., elapsed / .008) * .055


def _bottle_colour(time: float, kind: str, variant: int, heavy: bool, low: float, damp: float) -> float:
    if kind == "rise":
        movement, release = .32, 1.38
        span = .62
    elif kind == "attack":
        movement, release = .18, 1.25 if heavy else .95
        span = .48 if heavy else .36
    elif kind == "hurt":
        movement, release = .008, None
        span = .30 if heavy else .24
    else:
        return 0.
    ring = _bottle_ring(time, movement, variant, heavy)
    if release is not None:
        ring += _bottle_ring(time, release, variant + 1, heavy) * .65
    elapsed = time - movement
    # A small enclosed slosh follows the body's change of weight; the throat
    # remains the foreground sound, with no pour into an unseen receptacle.
    liquid = _window(time, movement + .025, span) * (
        low * 1.2 + damp * .25 + math.sin(math.tau * (171. * elapsed - 48. * elapsed * elapsed)) * .08) * .045
    return ring + liquid


def _samples(kind: str, duration: float, variant: int) -> list[float]:
    seed = (0x31A792C5 + variant * 7919 + sum(map(ord, kind)) * 131) & 0xFFFFFFFF
    heavy = kind.startswith("carrier-")
    kind = kind.removeprefix("carrier-")
    low = damp = previous = highpass = 0.
    output = []
    for index in range(round(duration * SAMPLE_RATE)):
        time = index / SAMPLE_RATE
        seed ^= (seed << 13) & 0xFFFFFFFF
        seed ^= seed >> 17
        seed ^= (seed << 5) & 0xFFFFFFFF
        seed &= 0xFFFFFFFF
        noise = (seed >> 8) * (2. / 16777215.) - 1.
        low += .026 * (noise - low)
        damp += .17 * (noise - damp)
        if heavy:
            if kind == "rise":
                value = _throat(time, .12, .95, -3, low, damp, True) * .5
                value += _throat(time, 1.18, 1.12, -2, low, damp) * .6
                value += damp * (_window(time, .18, .76) + _window(time, 1.13, .9)) * .22
            elif kind == "breath":
                value = _throat(time, .025, duration * .48, variant - 4, low, damp) * .56
                value += damp * _window(time, duration * .32, duration * .65) * .65
                value += _throat(time, duration * .63, duration * .33, variant - 3, low, damp, True) * .24
            elif kind == "attack":
                value = _throat(time, .04, 1.12, variant - 3, low, damp, True) * .45
                value += damp * _window(time, .12, 1.08) * .18
                value += _throat(time, 1.25, .38, variant - 2, low, damp, True) * .82
            elif kind == "hurt":
                value = _wet(time, .008, variant, low, damp) * .26
                value += _throat(time, .025, .5, -2, low, damp, True) * .68
            else:
                value = _throat(time, .015, 1.1, -4, low, damp) * .6
                value += damp * _window(time, .48, 1.32) * .58
        elif kind == "rise":
            value = _throat(time, .18, .7, 0, low, damp) * .25
            value += damp * _window(time, .10, .58) * .6
            value += _throat(time, 1.05, .46, 1, low, damp, True) * .35
            value += _throat(time, 1.68, .65, 0, low, damp) * .55
            value += damp * (_window(time, .54, .28) + _window(time, 1.4, .36)) * .18
            value += _wet(time, .83, 0, low, damp) * .16
        elif kind == "breath":
            value = damp * _window(time, .015, duration * .42) * .75
            value += _throat(time, duration * .38, duration * .58, variant, low, damp) * .42
            value += low * _window(time, duration * .54, duration * .31) * .5
            # The closed-mouth swallow stays bodily; material accents are
            # reserved for the visible rise, arm release and received impact.
            value += _wet(time, duration * .46, variant, low, damp) * .24
            value += _wet(time, duration * .57, variant + 1, low, damp) * .10
        elif kind == "attack":
            value = damp * _window(time, .04, .65) * .42
            value += _throat(time, .22, .58, variant, low, damp, True) * .24
            value += _throat(time, .95, .35, variant + 1, low, damp, True) * .85
            value += damp * _pulse(time, .95, .008, .09) * .2
        elif kind == "hurt":
            value = _wet(time, .006, variant, low, damp) * .5
            value += _throat(time, .04, .32, 1, low, damp, True) * .55
        else:
            value = _throat(time, .015, .72, 1, low, damp) * .5
            value += damp * _window(time, .42, .98) * .58
            value += low * _window(time, .15, 1.2) * .35
        value = math.tanh((value + _bottle_colour(time, kind, variant, heavy, low, damp)) * 1.45)
        highpass = value - previous + .995 * highpass
        previous = value
        output.append(highpass * min(1., time / .006, (duration - time) / .02))
    gain = .82 / max(max(map(abs, output)), .001)
    return [value * gain for value in output]


def audio_files() -> dict[str, bytes]:
    result = {}
    for name, kind, duration, variant in SPECS:
        samples = _samples(kind, duration, variant)
        target = io.BytesIO()
        with wave.open(target, "wb") as writer:
            writer.setnchannels(1)
            writer.setsampwidth(2)
            writer.setframerate(SAMPLE_RATE)
            writer.writeframes(struct.pack("<" + "h" * len(samples), *(round(value * 32767.) for value in samples)))
        result[name + ".wav"] = target.getvalue()
    return result


def validate(files: dict[str, bytes]) -> None:
    if len(files) != len(SPECS) or len(set(files.values())) != len(SPECS):
        raise ValueError("Ordinary bodily audio requires a complete distinct bank.")
    for name, _, duration, _ in SPECS:
        with wave.open(io.BytesIO(files[name + ".wav"]), "rb") as reader:
            if reader.getparams()[:3] != (1, 2, SAMPLE_RATE) or reader.getnframes() != round(duration * SAMPLE_RATE):
                raise ValueError("Invalid finite mono body cue: " + name)
            data = reader.readframes(reader.getnframes())
        samples = struct.unpack("<" + "h" * (len(data) // 2), data)
        peak = max(map(abs, samples)) / 32767.
        rms = math.sqrt(sum(value * value for value in samples) / len(samples)) / 32767.
        if not .81 < peak < .83 or not .025 < rms < .4 or abs(samples[0]) > 1 or abs(samples[-1]) > 90:
            raise ValueError(f"Invalid body-cue envelope {name}: {peak=:.4f}, {rms=:.4f}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, default=Path(__file__).resolve().parents[1] / "Assets/Resources/Combat")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    files = audio_files()
    validate(files)
    if files != audio_files():
        raise ValueError("Ordinary bodily audio is not deterministic.")
    for name, data in files.items():
        destination = args.output_dir / name
        if args.validate_only:
            if destination.read_bytes() != data:
                raise ValueError("Published body cue differs: " + name)
        else:
            args.output_dir.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(data)
            meta = destination.with_suffix(".wav.meta")
            if not meta.exists():
                template = (Path(__file__).resolve().parents[1] / "Assets/Resources/Combat/ZombieGroan.wav.meta").read_text()
                lines = template.splitlines()
                lines[1] = "guid: " + hashlib.sha256(("BarPromenade/Combat/" + name).encode()).hexdigest()[:32]
                meta.write_text("\n".join(lines) + "\n")
    print("ORDINARY BODY AUDIO / FINITE DRY MONO / ATTACK TIMELINE / DETERMINISTIC OK " +
          hashlib.sha256(b"".join(files.values())).hexdigest())


if __name__ == "__main__":
    main()
