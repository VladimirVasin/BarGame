#!/usr/bin/env python3
"""Build the Room attack's abstract collision cookies and dry material sound.

Two irregular dark masses interrupt a narrow, uneven slit of light. There are
no people, anatomy, objects or represented actions. White admits light; black
blocks it. The two flat masses approach, briefly meet and recoil without
depicting any action or anatomy. Alpha is opaque because URP spot cookies
sample RGB. No new geometry or runtime painting is used.

The finite audio phrase contains pronounced dry cloth flaps against a seat, with faint
wood creaks beneath it. It represents offscreen materials, without voices or
represented actions.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import math
import random
import struct
import wave
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "Assets/Resources/Combat/RoomAttackShadow.png"
AUDIO_OUTPUT = ROOT / "Assets/Resources/Combat/RoomAttackMemory.wav"
SIZE = 256
SUPERSAMPLE = 4
AUDIO_RATE = 24000
AUDIO_DURATION = 3.6
FRAME_CYCLE = .64
# Authored in 512-pixel space: positive shifts bring both flat shapes inward.
FRAME_SHIFTS = (-24, -12, 7, 20, 26, -32, -27, -24)
FRAME_NAMES = ("separation", "approach", "near", "contact", "overlap",
               "recoil", "separation", "settle")


def bezier_path(start: tuple[float, float], segments: tuple, shift: float) -> list:
    """Sample fixed cubic outlines; coordinates are authored in cookie pixels."""
    points = [start]
    previous = start
    for control_a, control_b, end in segments:
        for step in range(1, 25):
            t = step / 24
            u = 1 - t
            points.append(tuple(
                u ** 3 * previous[axis] + 3 * u * u * t * control_a[axis]
                + 3 * u * t * t * control_b[axis] + t ** 3 * end[axis]
                for axis in (0, 1)))
        previous = end
    assert all(math.isfinite(value) for point in points for value in point)
    scale = SUPERSAMPLE * SIZE / 512
    return [(round((x + shift) * scale), round(y * scale)) for x, y in points]


def render(frame_index: int = 0) -> Image.Image:
    canvas = Image.new("L", (SIZE * SUPERSAMPLE,) * 2, 255)
    draw = ImageDraw.Draw(canvas)

    shape_index = 0

    def shape(start, segments):
        nonlocal shape_index
        shift = FRAME_SHIFTS[frame_index] * (1 if shape_index == 0 else -1)
        draw.polygon(bezier_path(start, segments, shift), fill=0)
        shape_index += 1

    # Asymmetric broad shapes have no joints, face profiles or repeated lobes.
    # Their inboard edges form the only readable motif: light under pressure.
    shape((99, 112), (
        ((143, 75), (192, 82), (224, 103)),
        ((243, 157), (223, 198), (229, 248)),
        ((239, 300), (213, 343), (219, 391)),
        ((193, 422), (155, 441), (112, 428)),
        ((90, 387), (104, 336), (86, 298)),
        ((65, 249), (82, 198), (81, 154)),
        ((81, 136), (89, 121), (99, 112)),
    ))
    shape((283, 79), (
        ((321, 70), (382, 89), (398, 126)),
        ((422, 181), (390, 231), (410, 275)),
        ((429, 326), (390, 368), (375, 411)),
        ((338, 431), (296, 429), (270, 405)),
        ((242, 362), (265, 322), (255, 277)),
        ((242, 233), (269, 197), (258, 150)),
        ((255, 120), (268, 90), (283, 79)),
    ))

    # Slightly wavering optical edges and low-amplitude grain keep this a light
    # interruption rather than a pictogram. All perturbations have a fixed seed.
    canvas = canvas.resize((SIZE, SIZE), Image.Resampling.LANCZOS)
    source = canvas.copy()
    pixels = source.load()
    canvas_pixels = canvas.load()
    for y in range(SIZE):
        shift = .8 * math.sin(y * .142) + .4 * math.sin(y * .346 + .9)
        for x in range(SIZE):
            canvas_pixels[x, y] = pixels[max(0, min(SIZE - 1, round(x + shift))), y]
    canvas = canvas.filter(ImageFilter.GaussianBlur(1.15))
    generator = random.Random(6719)
    noise = Image.frombytes("L", (SIZE, SIZE),
                            bytes(generator.randrange(65) for _ in range(SIZE * SIZE)))
    noise = noise.filter(ImageFilter.GaussianBlur(.8))
    canvas_pixels = canvas.load()
    noise_pixels = noise.load()
    for y in range(SIZE):
        for x in range(SIZE):
            light = canvas_pixels[x, y]
            if light < 255:
                grain = max(0, noise_pixels[x, y] - 26)
                canvas_pixels[x, y] = min(255, light + round(grain * (255 - light) / 255))
    return Image.merge("RGBA", (canvas, canvas, canvas, Image.new("L", canvas.size, 255)))


def encode(image: Image.Image) -> bytes:
    output = io.BytesIO()
    image.save(output, format="PNG", compress_level=9, optimize=False)
    return output.getvalue()


def row_spans(red: Image.Image, y: int) -> list[tuple[int, int]]:
    row = [red.getpixel((x, y)) < 64 for x in range(SIZE)]
    spans = []
    start = None
    for x, occupied in enumerate(row + [False]):
        if occupied and start is None:
            start = x
        elif not occupied and start is not None:
            spans.append((start, x))
            start = None
    return spans


def validate(image: Image.Image, frame_index: int) -> None:
    assert image.mode == "RGBA" and image.size == (SIZE, SIZE), "Expected 256 RGBA"
    red, green, blue, alpha = image.split()
    assert red.tobytes() == green.tobytes() == blue.tobytes(), "Cookie must be grayscale"
    assert alpha.getextrema() == (255, 255), "RGB cookie requires opaque alpha"
    assert red.getextrema() == (0, 255), "Keep full light/shadow range"
    darkness = red.point(lambda value: 255 if value < 128 else 0)
    bounds = darkness.getbbox()
    assert bounds and 18 <= bounds[0] <= 58 and 33 <= bounds[1] < 50, bounds
    assert 185 <= bounds[2] <= 239 and 208 <= bounds[3] <= 225, bounds
    # Border light makes Clamp safe and avoids hard rectangular projection edges.
    for border in ((0, 0, SIZE, 16), (0, 0, 16, SIZE),
                   (SIZE - 16, 0, SIZE, SIZE), (0, SIZE - 16, SIZE, SIZE)):
        assert red.crop(border).getextrema() == (255, 255), "Cookie border must admit light"
    spans = row_spans(red, SIZE // 2)
    if frame_index in (3, 4):
        assert len(spans) == 1, "Contact must close the central light slit"
    else:
        assert len(spans) == 2, "Separated phases require two abstract regions"
        assert spans[1][0] - spans[0][1] >= (18 if frame_index != 2 else 2), spans
    assert red.crop((70, 90, 85, 150)).getextrema()[1] > 4, "Keep subtle shadow grain"


def validate_bank(images: list[Image.Image]) -> None:
    assert len(images) == len(FRAME_SHIFTS) == 8
    shares = []
    for index, image in enumerate(images):
        validate(image, index)
        red = image.getchannel("R")
        shares.append(sum(value < 128 for value in red.tobytes()) / (SIZE * SIZE))
    assert min(shares) > .33 and max(shares) - min(shares) < .055, shares
    assert all(image.getchannel("R").getpixel((128, 16)) == 255 for image in images)
    # Imported R8 plus complete mip chains remain bounded below one MiB.
    assert len(images) * sum((SIZE >> level) ** 2 for level in range(9)) < 1024 * 1024


def smoothstep(value: float) -> float:
    value = max(0., min(1., value))
    return value * value * (3 - 2 * value)


def audio_phrase() -> bytes:
    """Firm dry cloth-on-seat flaps with rounded attacks; faint wood beneath."""
    frame_count = round(AUDIO_RATE * AUDIO_DURATION)
    samples = [0.] * frame_count
    generator = random.Random(191063)
    # Cloth follows abstract contact by .04 seconds, with small timbre variation.
    events = tuple((.28 + FRAME_CYCLE * index, .35 + (.03 if index % 2 else 0),
                    (.89, .96, .83, 1., .91, .79)[index],
                    (236., 194., 267., 219., 253., 206.)[index])
                   for index in range(6))
    for onset, duration, strength, fundamental in events:
        noise_slow = 0.
        cloth_low = cloth_base = 0.
        seat_low = seat_soft = seat_base = 0.
        resonators = [[0., 0.] for _ in range(3)]
        for index in range(round((duration + .16) * AUDIO_RATE)):
            local_time = index / AUDIO_RATE
            phase = local_time / duration
            friction = generator.uniform(-1., 1.)
            noise_slow += .008 * (friction - noise_slow)
            # Gradual rubbing edges, then a short natural resonance tail.
            envelope = (smoothstep(local_time / .11)
                        * smoothstep((duration - local_time) / .19))
            cloth_envelope = (smoothstep(local_time / .085)
                              * smoothstep((duration + .12 - local_time) / .24))
            pressure = .46 + .30 * math.sin(phase * 9.1 + .3)
            pressure += .24 * math.sin(phase * 23.7 + noise_slow * 12.) ** 2
            excitation = friction * envelope * max(.08, pressure)
            wood = 0.
            for band, (ratio, gain, decay) in enumerate(
                    ((1., .54, .991), (2.13, .31, .987), (3.67, .15, .981))):
                frequency = fundamental * ratio * (1 + .065 * math.sin(phase * 5.3)
                                                   + .024 * noise_slow)
                coefficient = 2 * decay * math.cos(2 * math.pi * frequency / AUDIO_RATE)
                previous, older = resonators[band]
                value = (1 - decay) * excitation + coefficient * previous - decay * decay * older
                resonators[band] = [value, previous]
                wood += value * gain
            # Broad cloth friction, rolled off before the harsh high frequencies.
            cloth_low += .31 * (friction - cloth_low)
            cloth_base += .033 * (friction - cloth_base)
            cloth = (cloth_low - cloth_base) * cloth_envelope * .006
            # A dry fabric flap against padding: broad noise, rounded onset,
            # quick soft decay and two low-pass stages. No wet or bodily sound.
            seat_low += .30 * (friction - seat_low)
            seat_soft += .26 * (seat_low - seat_soft)
            seat_base += .008 * (seat_soft - seat_base)
            seat_envelope = (smoothstep(local_time / .009)
                             * math.exp(-local_time / .095)
                             * smoothstep((.29 - local_time) / .09))
            seat = (seat_soft - seat_base) * seat_envelope * 2.6
            destination = round(onset * AUDIO_RATE) + index
            if destination < frame_count:
                samples[destination] += strength * (seat + wood * .10 + cloth)

    # Preserve the authored material level; metadata disables normalization.
    peak = max(abs(value) for value in samples)
    assert 0 < peak and all(math.isfinite(value) for value in samples)
    gain = .88 / peak
    values = []
    for index, value in enumerate(samples):
        time = index / AUDIO_RATE
        fade = smoothstep(time / .2) * smoothstep((AUDIO_DURATION - time) / .2)
        values.append(round(value * gain * fade * 32767))
    output = io.BytesIO()
    with wave.open(output, "wb") as audio:
        audio.setnchannels(1)
        audio.setsampwidth(2)
        audio.setframerate(AUDIO_RATE)
        audio.writeframes(struct.pack("<" + "h" * len(values), *values))
    return output.getvalue()


def validate_audio(data: bytes) -> None:
    with wave.open(io.BytesIO(data), "rb") as audio:
        assert audio.getnchannels() == 1 and audio.getsampwidth() == 2, "Expected mono PCM16"
        assert audio.getframerate() == AUDIO_RATE and audio.getcomptype() == "NONE"
        assert audio.getnframes() == round(AUDIO_RATE * AUDIO_DURATION), "Phrase duration changed"
        frames = audio.readframes(audio.getnframes())
    values = struct.unpack("<" + "h" * (len(frames) // 2), frames)
    assert all(math.isfinite(value) for value in values)
    peak = max(abs(value) for value in values) / 32768
    assert .84 <= peak <= .90, "Keep the pronounced dry impacts below clipping"
    assert max(abs(value) for value in values[:240]) == 0, "Start must be silent"
    assert values[-1] == 0 and max(abs(value) for value in values[-24:]) <= 8, "End must fade to silence"
    rms = math.sqrt(sum((value / 32768) ** 2 for value in values) / len(values))
    assert .065 <= rms <= .14, "Keep the stronger material phrase controlled"
    assert max(abs(b - a) for a, b in zip(values, values[1:])) < 15000, "Avoid discontinuities in the rounded impacts"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true",
                        help="Rebuild in memory, validate and compare exact PNG/WAV bytes")
    parser.add_argument("--contact-sheet", action="store_true",
                        help="Write a temporary phase preview under Captures/RoomShadowAssets")
    args = parser.parse_args()
    if args.validate_only and args.contact_sheet:
        parser.error("--validate-only cannot publish a contact sheet")
    images = [render(index) for index in range(8)]
    validate_bank(images)
    data_bank = [encode(image) for image in images]
    assert data_bank == [encode(render(index)) for index in range(8)], "Generation must reproduce identical bytes"
    outputs = [OUTPUT.with_name(f"RoomAttackShadow{index if index else ''}.png")
               for index in range(8)]
    audio_data = audio_phrase()
    validate_audio(audio_data)
    assert audio_data == audio_phrase(), "Sound generation must reproduce identical bytes"
    if args.validate_only:
        for index, (path, data) in enumerate(zip(outputs, data_bank)):
            assert path.is_file(), f"Missing generated asset: {path}"
            assert path.read_bytes() == data, "Asset differs; rerun the generator"
            validate(Image.open(path), index)
        assert AUDIO_OUTPUT.is_file(), f"Missing generated sound: {AUDIO_OUTPUT}"
        assert AUDIO_OUTPUT.read_bytes() == audio_data, "Sound differs; rerun the generator"
        validate_audio(AUDIO_OUTPUT.read_bytes())
    else:
        OUTPUT.parent.mkdir(parents=True, exist_ok=True)
        for path, data in zip(outputs, data_bank):
            path.write_bytes(data)
        AUDIO_OUTPUT.write_bytes(audio_data)
        if args.contact_sheet:
            sheet = Image.new("RGB", (SIZE * 4, (SIZE + 24) * 2), (240, 240, 235))
            draw = ImageDraw.Draw(sheet)
            for index, image in enumerate(images):
                x = index % 4 * SIZE
                y = index // 4 * (SIZE + 24)
                sheet.paste(image.convert("RGB"), (x, y))
                draw.text((x + 8, y + SIZE + 5), f"{index}: {FRAME_NAMES[index]}", fill=(30, 30, 30))
            preview = ROOT / "Captures/RoomShadowAssets/contact-sheet.png"
            preview.parent.mkdir(parents=True, exist_ok=True)
            sheet.save(preview)
    print(f"Room abstract shadow bank: {'validated' if args.validate_only else 'generated'} "
          f"8 x {SIZE}x{SIZE} RGBA, sha256={hashlib.sha256(b''.join(data_bank)).hexdigest()}")
    print(f"Room material sound: {'validated' if args.validate_only else 'generated'} "
          f"mono PCM16 {AUDIO_RATE} Hz {AUDIO_DURATION}s, "
          f"sha256={hashlib.sha256(audio_data).hexdigest()}")


if __name__ == "__main__":
    main()
