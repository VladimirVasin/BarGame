#!/usr/bin/env python3
"""Bake yielding abstract shadows and a synchronized wet material phrase.

The two nonfigurative masses yield around one flat, elongated sliding prop.
Its insertion, their deformation and finite wet audio share one collision phase.
Runtime uses only retained cookies
and PCM; no new geometry, render target, material or audio synthesis is needed.
"""

from __future__ import annotations
import argparse
import hashlib
import io
import math
import random
import re
import struct
import wave
from pathlib import Path
from typing import NamedTuple
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageStat

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "Assets/Resources/Combat/RoomAttackShadow.png"
AUDIO_OUTPUT = ROOT / "Assets/Resources/Combat/RoomAttackMemory.wav"
SIZE, SUPERSAMPLE, FRAME_COUNT = 256, 4, 24
FRAME_CYCLE, AUDIO_RATE, AUDIO_DURATION = 0.64, 24000, 3.6
# Authored in 512-pixel space: no joints, faces, anatomy or repeated lobes.
OUTLINES = (
    (
        (99, 112),
        (
            ((143, 75), (192, 82), (224, 103)),
            ((243, 157), (223, 198), (229, 248)),
            ((239, 300), (213, 343), (219, 391)),
            ((193, 422), (155, 441), (112, 428)),
            ((90, 387), (104, 336), (86, 298)),
            ((65, 249), (82, 198), (81, 154)),
            ((81, 136), (89, 121), (99, 112)),
        ),
    ),
    (
        (283, 79),
        (
            ((321, 70), (382, 89), (398, 126)),
            ((422, 181), (390, 231), (410, 275)),
            ((429, 326), (390, 368), (375, 411)),
            ((338, 431), (296, 429), (270, 405)),
            ((242, 362), (265, 322), (255, 277)),
            ((242, 233), (269, 197), (258, 150)),
            ((255, 120), (268, 90), (283, 79)),
        ),
    ),
)


def smoothstep(value: float) -> float:
    value = max(0.0, min(1.0, value))
    return value * value * (3 - 2 * value)


def pulse(phase: float, center: float, radius: float) -> float:
    """Periodic compact pulse with zero slope at both ends."""
    distance = (phase - center + 0.5) % 1.0 - 0.5
    return (
        0.5 + 0.5 * math.cos(math.pi * distance / radius)
        if abs(distance) < radius
        else 0.0
    )


class CollisionPhase(NamedTuple):
    phase: float
    approach: float
    compression: float
    release: float
    recoil: float


def collision_phase(seconds: float) -> CollisionPhase:
    """The single timing source for contours, wet pressure and viscous release."""
    phase = (seconds / FRAME_CYCLE) % 1.0
    approach = pulse(phase, 0.43, 0.36)
    return CollisionPhase(
        phase,
        approach,
        smoothstep((approach - 0.38) / 0.62),
        pulse(phase, 0.66, 0.15),
        pulse(phase, 0.77, 0.18),
    )


def bezier_path(start: tuple, segments: tuple) -> list:
    points, previous = [start], start
    for control_a, control_b, end in segments:
        for step in range(1, 25):
            t = step / 24
            u = 1 - t
            points.append(
                tuple(
                    u**3 * previous[axis]
                    + 3 * u * u * t * control_a[axis]
                    + 3 * u * t * t * control_b[axis]
                    + t**3 * end[axis]
                    for axis in (0, 1)
                )
            )
        previous = end
    return [(x, 260.0 + (y - 260.0) * 0.94) for x, y in points]


def area_center(points: list) -> tuple:
    twice_area = cx = cy = 0.0
    for (x, y), (nx, ny) in zip(points, points[1:] + points[:1]):
        cross = x * ny - nx * y
        twice_area += cross
        cx += (x + nx) * cross
        cy += (y + ny) * cross
    assert abs(twice_area) > 1.0, "Contour must have nonzero area"
    return abs(twice_area) * 0.5, (cx / (3 * twice_area), cy / (3 * twice_area))


BASE_PATHS = [bezier_path(*outline) for outline in OUTLINES]
BASE_MEASUREMENTS = [area_center(points) for points in BASE_PATHS]


def contour(shape_index: int, seconds: float) -> list:
    state = collision_phase(seconds)
    base_area, (cx, cy) = BASE_MEASUREMENTS[shape_index]
    inward = 1 if shape_index == 0 else -1
    width = 1.0 - 0.20 * state.compression + 0.13 * state.release
    deformed = []
    for x, y in BASE_PATHS[shape_index]:
        dx, dy = x - cx, y - cy
        # The delayed edge wave travels through the mass after contact.
        delay = 0.035 * (dy / 180.0 + 1.0) + shape_index * 0.012
        age = (state.phase - 0.46 - delay) % 1.0
        wave = (
            math.sin(math.tau * age * 5.0)
            * smoothstep(age / 0.04)
            * math.exp(-age / 0.15)
            * smoothstep((0.65 - age) / 0.10)
        )
        inner_edge = max(0.0, min(1.0, inward * dx / 82.0))
        neck = 21.0 * state.release * inner_edge**2 * math.exp(-((dy / 100.0) ** 2))
        deformed.append(
            (
                cx
                + dx * width
                + inward * neck
                + 15.0 * wave * math.sin(dy / 95.0 + shape_index),
                cy + dy / width + 9.0 * wave * math.cos(dy / 115.0),
            )
        )
    # Redistribute a stable amount of material; do not confuse centroid drift
    # with deformation. Only the common collision phase drives translation.
    area, (new_cx, new_cy) = area_center(deformed)
    size = math.sqrt(base_area / area)
    # Keep an open gap around the prop so its axial travel remains readable
    # instead of disappearing inside two overlapping dark masses.
    shift = inward * (-25.0 + 24.0 * state.approach - 7.0 * state.recoil)
    return [
        (cx + (x - new_cx) * size + shift, cy + (y - new_cy) * size)
        for x, y in deformed
    ]


def horizontal_span(points: list, y: float) -> tuple:
    crossings = [
        a[0] + (y - a[1]) * (b[0] - a[0]) / (b[1] - a[1])
        for a, b in zip(points, points[1:] + points[:1])
        if (a[1] <= y < b[1]) or (b[1] <= y < a[1])
    ]
    assert len(crossings) >= 2, "The striking prop needs a continuous attachment"
    return min(crossings), max(crossings)


def striker_profile(seconds: float) -> list:
    """A rigid flat bar slides horizontally into the other mass at mid-height."""
    state = collision_phase(seconds)
    attach_y = 260.0
    advance = 64.0 * state.compression - 32.0 * state.release
    attach_x = horizontal_span(contour(0, seconds), attach_y)[1] - 88.0 + advance
    length = 134.0
    # A narrow shaft and wider chamfered, flat end give the prop an ordinary
    # manufactured profile. Its rear slides inside the left mass; its head is
    # concealed by the right mass on insertion and reappears on withdrawal.
    profile = (
        (0, -8),
        (0.73, -8),
        (0.73, -14),
        (0.97, -14),
        (1, -10),
        (1, 10),
        (0.97, 14),
        (0.73, 14),
        (0.73, 8),
        (0, 8),
    )
    result = []
    for fraction, side in profile:
        distance = fraction * length
        result.append((attach_x + distance, attach_y + side))
    return result


def region_mask(points: list) -> Image.Image:
    """Small bake-only mask used to verify attachment and actual contact."""
    mask = Image.new("L", (SIZE, SIZE), 0)
    scale = SIZE / 512
    ImageDraw.Draw(mask).polygon(
        [(round(x * scale), round(y * scale)) for x, y in points], fill=255
    )
    return mask


def render(frame_index: int = 0) -> Image.Image:
    seconds = (frame_index % FRAME_COUNT) * FRAME_CYCLE / FRAME_COUNT
    canvas = Image.new("L", (SIZE * SUPERSAMPLE,) * 2, 255)
    draw = ImageDraw.Draw(canvas)
    scale = SUPERSAMPLE * SIZE / 512
    # The opaque receiving mass conceals the inserted end of the same bar.
    draw.polygon(
        [(round(x * scale), round(y * scale)) for x, y in striker_profile(seconds)],
        fill=0,
    )
    for shape_index in range(2):
        draw.polygon(
            [
                (round(x * scale), round(y * scale))
                for x, y in contour(shape_index, seconds)
            ],
            fill=0,
        )
    canvas = canvas.resize((SIZE, SIZE), Image.Resampling.LANCZOS)
    source = canvas.copy()
    pixels, output = source.load(), canvas.load()
    for y in range(SIZE):
        shift = 0.8 * math.sin(y * 0.142) + 0.4 * math.sin(y * 0.346 + 0.9)
        for x in range(SIZE):
            output[x, y] = pixels[max(0, min(SIZE - 1, round(x + shift))), y]
    canvas = canvas.filter(ImageFilter.GaussianBlur(1.15))
    generator = random.Random(6719)
    noise = Image.frombytes(
        "L", (SIZE, SIZE), bytes(generator.randrange(65) for _ in range(SIZE * SIZE))
    )
    noise = noise.filter(ImageFilter.GaussianBlur(0.8))
    output, grain = canvas.load(), noise.load()
    for y in range(SIZE):
        for x in range(SIZE):
            light = output[x, y]
            if light < 255:
                output[x, y] = min(
                    255, light + round(max(0, grain[x, y] - 26) * (255 - light) / 255)
                )
    return Image.merge(
        "RGBA", (canvas, canvas, canvas, Image.new("L", canvas.size, 255))
    )


def encode(image: Image.Image) -> bytes:
    output = io.BytesIO()
    image.save(output, format="PNG", compress_level=9, optimize=False)
    return output.getvalue()


def row_spans(red: Image.Image, y: int) -> list:
    spans, start = [], None
    for x, occupied in enumerate(
        [red.getpixel((x, y)) < 64 for x in range(SIZE)] + [False]
    ):
        if occupied and start is None:
            start = x
        elif not occupied and start is not None:
            spans.append((start, x))
            start = None
    return spans


def validate_bank(images: list) -> None:
    assert len(images) == FRAME_COUNT == 24
    shares, changes, contacts = [], [], []
    profiles, depths = [], []
    for index, image in enumerate(images):
        assert image.mode == "RGBA" and image.size == (SIZE, SIZE)
        red, green, blue, alpha = image.split()
        assert red.tobytes() == green.tobytes() == blue.tobytes(), (
            "Cookie must be grayscale"
        )
        assert alpha.getextrema() == (255, 255) and red.getextrema() == (0, 255)
        for border in (
            (0, 0, SIZE, 16),
            (0, 0, 16, SIZE),
            (SIZE - 16, 0, SIZE, SIZE),
            (0, SIZE - 16, SIZE, SIZE),
        ):
            assert red.crop(border).getextrema() == (255, 255), (
                "Keep the projected border open"
            )
        state = collision_phase(index * FRAME_CYCLE / FRAME_COUNT)
        # Below the single prop the figures stay separate, including on insertion.
        spans = row_spans(red, SIZE // 2 + 32)
        assert len(spans) == 2 and spans[1][0] - spans[0][1] >= 12, spans
        seconds = index * FRAME_CYCLE / FRAME_COUNT
        profile = striker_profile(seconds)
        profiles.append(profile)
        prop = region_mask(profile)
        left, right = [region_mask(contour(shape, seconds)) for shape in (0, 1)]
        assert ImageChops.multiply(prop, left).getbbox(), (
            "The bar must remain attached to the left figure"
        )
        overlap = ImageChops.multiply(prop, right).histogram()[255]
        contacts.append(overlap)
        tip_x = (profile[4][0] + profile[5][0]) * 0.5
        contact_y = (profile[4][1] + profile[5][1]) * 0.5
        depths.append(tip_x - horizontal_span(contour(1, seconds), contact_y)[0])
        if state.compression > 0.8:
            assert overlap >= 8, "The flat end must enter the right figure"
        if state.compression > 0.95:
            head = region_mask(profile[2:8])
            hidden = ImageChops.multiply(head, right).histogram()[255]
            assert hidden / head.histogram()[255] > 0.95, (
                "The inserted head must disappear inside the right silhouette"
            )
            for shape in (0, 1):
                ys = [point[1] for point in contour(shape, seconds)]
                assert (
                    abs(contact_y - (min(ys) + max(ys)) * 0.5)
                    < (max(ys) - min(ys)) * 0.10
                ), "Insertion must remain at the middle of both figures"
        if state.approach < 0.2:
            assert overlap == 0, "Withdrawal must visibly clear the right figure"
        shares.append(sum(value < 128 for value in red.tobytes()) / (SIZE * SIZE))
        difference = ImageChops.difference(
            red, images[(index + 1) % FRAME_COUNT].getchannel("R")
        )
        changes.append(ImageStat.Stat(difference).mean[0] / 255.0)
    assert min(shares) > 0.28 and max(shares) - min(shares) < 0.09, shares
    assert max(contacts) >= 8 and contacts.count(0) >= FRAME_COUNT // 3, (
        "Keep distinct insertion, concealment and withdrawal"
    )
    # Measured geometry rules out the retired angular swing or elastic growth.
    offsets = [
        [(x - points[0][0], y - points[0][1]) for x, y in points] for points in profiles
    ]
    assert all(
        all(
            abs(x - bx) < 0.00001 and abs(y - by) < 0.00001
            for (x, y), (bx, by) in zip(points, offsets[0])
        )
        for points in offsets
    ), "The bar must translate rigidly without swinging"
    assert (
        max(points[0][1] for points in profiles)
        - min(points[0][1] for points in profiles)
        < 0.00001
    ), "The feed must remain horizontal"
    assert max(depths) > 35.0 and min(depths) < -16.0, (
        "Show actual insertion depth and a clear withdrawn end"
    )
    assert max(changes) < 0.085, "Neighboring phases must not jump across the cookie"
    assert max(abs(a - b) for a, b in zip(shares, shares[1:] + shares[:1])) < 0.02, (
        "Keep total illumination stable"
    )
    assert changes[-1] < max(changes[:-1]) * 0.5, "The cyclic seam must settle smoothly"
    assert render(FRAME_COUNT).tobytes() == images[0].tobytes(), (
        "Cycle must close exactly"
    )
    for shape_index in range(2):
        aspects, shapes = [], []
        for index in range(FRAME_COUNT):
            points = contour(shape_index, index * FRAME_CYCLE / FRAME_COUNT)
            area, (cx, cy) = area_center(points)
            assert abs(area / BASE_MEASUREMENTS[shape_index][0] - 1.0) < 0.00001, (
                "Keep material area stable"
            )
            shapes.append([(x - cx, y - cy) for x, y in points])
            aspects.append(
                (max(x for x, y in points) - min(x for x, y in points))
                / (max(y for x, y in points) - min(y for x, y in points))
            )
        deformation = max(
            math.sqrt(
                sum(
                    (x - bx) ** 2 + (y - by) ** 2
                    for (x, y), (bx, by) in zip(points, shapes[0])
                )
                / len(points)
            )
            for points in shapes
        )
        assert deformation > 12.0 and max(aspects) / min(aspects) > 1.6, (
            "Shapes must yield visibly"
        )
    assert (
        FRAME_COUNT * sum((SIZE >> level) ** 2 for level in range(9)) <= 2 * 1024 * 1024
    )
    provider = (
        ROOT / "Assets/Scripts/Runtime/Combat/CombatRoomAttackAssetProvider.cs"
    ).read_text("utf-8")
    for name, expected in (
        ("ShadowFrameCount", FRAME_COUNT),
        ("ShadowFrameSize", SIZE),
        ("ShadowCycleSeconds", FRAME_CYCLE),
    ):
        match = re.search(r"\b" + name + r"\s*=\s*([\d.]+)f?", provider)
        assert match and float(match[1]) == expected, (
            f"Runtime {name} differs from the bake"
        )


def audio_phrase() -> bytes:
    """Close wet clicks over liquid weight, then a drawn-out suction release."""
    generator = random.Random(191063)
    samples = []
    liquid_low = liquid_base = suction_low = 0.0
    resonators = [[0.0, 0.0], [0.0, 0.0]]
    click_phases = [0.0] * 6
    mass_phase = 0.0
    previous_pressure = previous_release = 0.0
    compression_start = release_start = -10.0
    for index in range(round(AUDIO_RATE * AUDIO_DURATION)):
        seconds = index / AUDIO_RATE
        state = collision_phase(seconds)
        pressure, release = state.compression, state.release
        # Follow state crossings rather than hardcoded phase positions, so
        # moving the authored contact keeps the sound on its actual pressure.
        if previous_pressure < 0.55 <= pressure:
            compression_start = seconds
        if previous_release < 0.35 <= release:
            release_start = seconds
        previous_pressure, previous_release = pressure, release
        noise = generator.uniform(-1.0, 1.0)
        alpha = 1.0 - math.exp(
            -math.tau * (360.0 + 1350.0 * pressure + 800.0 * release) / AUDIO_RATE
        )
        liquid_low += alpha * (noise - liquid_low)
        liquid_base += 0.018 * (liquid_low - liquid_base)
        suction_low += 0.052 * (noise - suction_low)
        mass_phase += math.tau * (78.0 + 63.0 * pressure + 32.0 * release) / AUDIO_RATE
        # Low, irregular liquid mass sits beneath the close wet transients.
        wet = (liquid_low - liquid_base) * (
            0.12 * pressure + 0.18 * release
        ) + suction_low * 0.21 * release
        wet += (
            0.085
            * (pressure + 0.65 * release)
            * (math.sin(mass_phase + 0.31 * math.sin(mass_phase * 0.137)))
        )
        for band, (base, sweep) in enumerate(((410.0, 1120.0), (150.0, 390.0))):
            frequency = base + sweep * (0.12 + 0.88 * release) * (
                0.62 + 0.38 * math.sin((seconds - release_start) * 37.0 + band)
            )
            decay = 0.970 - 0.010 * band
            previous, older = resonators[band]
            value = (
                (1.0 - decay) * noise
                + 2.0 * decay * math.cos(math.tau * frequency / AUDIO_RATE) * previous
                - decay * decay * older
            )
            resonators[band] = [value, previous]
            wet += value * (0.10 * pressure + 0.28 * release)
        # Three closely spaced, rounded liquid snaps make a distinct chomp-like
        # material envelope. Each closes with a descending bubble rather than
        # the old broadband seat hit. The second group stretches the release.
        for group, (onset, amount, offsets, duration) in enumerate(
            (
                (compression_start, pressure, (0.0, 0.021, 0.047), 0.037),
                (release_start, release, (0.0, 0.031, 0.071), 0.057),
            )
        ):
            for click, offset in enumerate(offsets):
                age = seconds - onset - offset
                if not 0.0 <= age < duration:
                    continue
                frequency = 115.0 + (1550.0 - 180.0 * click) * math.exp(
                    -age / (0.0055 + group * 0.005)
                )
                slot = group * 3 + click
                click_phases[slot] += math.tau * frequency / AUDIO_RATE
                envelope = (
                    smoothstep(age / 0.0035)
                    * math.exp(-age / (0.011 + group * 0.010))
                    * smoothstep((duration - age) / 0.009)
                )
                weight = (1.0, 0.73, 0.52)[click]
                wet += (
                    0.42
                    * weight
                    * envelope
                    * math.sqrt(amount)
                    * (
                        0.74 * math.sin(click_phases[slot])
                        + 0.26 * (liquid_low - liquid_base)
                    )
                )
        samples.append(
            wet
            * smoothstep(seconds / 0.08)
            * smoothstep((AUDIO_DURATION - seconds) / 0.14)
        )
    peak = max(abs(value) for value in samples)
    assert peak > 0 and all(math.isfinite(value) for value in samples)
    values = [round(value * 0.82 / peak * 32767) for value in samples]
    output = io.BytesIO()
    with wave.open(output, "wb") as audio:
        audio.setnchannels(1)
        audio.setsampwidth(2)
        audio.setframerate(AUDIO_RATE)
        audio.writeframes(struct.pack("<" + "h" * len(values), *values))
    return output.getvalue()


def validate_audio(data: bytes) -> None:
    with wave.open(io.BytesIO(data), "rb") as audio:
        assert audio.getnchannels() == 1 and audio.getsampwidth() == 2
        assert audio.getframerate() == AUDIO_RATE and audio.getcomptype() == "NONE"
        assert audio.getnframes() == round(AUDIO_RATE * AUDIO_DURATION)
        frames = audio.readframes(audio.getnframes())
    values = struct.unpack("<" + "h" * (len(frames) // 2), frames)
    assert 0.80 <= max(abs(value) for value in values) / 32768 <= 0.84, (
        "Keep wet material below clipping"
    )
    assert max(abs(value) for value in values[:240]) == 0, "Start must be silent"
    assert values[-1] == 0 and max(abs(value) for value in values[-24:]) <= 8, (
        "End must fade to silence"
    )
    rms = math.sqrt(sum((value / 32768) ** 2 for value in values) / len(values))
    assert 0.07 <= rms <= 0.22, "Keep the finite wet phrase controlled"
    assert max(abs(b - a) for a, b in zip(values, values[1:])) < 13000, (
        "Avoid hard transient edges"
    )
    states = [collision_phase(index / AUDIO_RATE) for index in range(len(values))]
    total = sum(value * value for value in values)
    active = sum(
        value * value
        for state, value in zip(states, values)
        if state.compression + state.release > 0.08
    )
    assert active / total > 0.98, "Audio must follow visual compression and detachment"
    # Evaluate the recognizable envelope after the existing room low-pass,
    # rather than accepting bright clicks which disappear at its 2400 Hz cut.
    room_alpha = 1.0 - math.exp(-math.tau * 2400.0 / AUDIO_RATE)
    body_alpha = 1.0 - math.exp(-math.tau * 280.0 / AUDIO_RATE)
    room_level = body_level = body_energy = 0.0
    filtered = []
    for value in values:
        room_level += room_alpha * (value / 32768 - room_level)
        body_level += body_alpha * (room_level - body_level)
        filtered.append(room_level)
        body_energy += body_level * body_level
    assert body_energy / sum(value * value for value in filtered) > 0.14, (
        "Wet clicks need a low liquid body beneath their transient"
    )
    cycle_samples = round(FRAME_CYCLE * AUDIO_RATE)
    for cycle in range(int(AUDIO_DURATION / FRAME_CYCLE)):
        levels = []
        start, end = cycle * cycle_samples, (cycle + 1) * cycle_samples
        for part in ("compression", "release"):
            window = [
                value
                for state, value in zip(states[start:end], filtered[start:end])
                if getattr(state, part) > 0.55
            ]
            assert window, "The common collision must retain both sound phases"
            levels.append(
                math.sqrt(sum(value * value for value in window) / len(window))
            )
        assert min(levels) > 0.04 and 0.25 < levels[1] / levels[0] < 2.5, (
            "Keep squeeze and viscous pull audible"
        )
        block = round(0.008 * AUDIO_RATE)
        envelope = []
        for offset in range(start, end - block, block):
            if states[offset + block // 2].compression > 0.55:
                window = filtered[offset : offset + block]
                envelope.append(
                    math.sqrt(sum(value * value for value in window) / block)
                )
        peaks = sum(
            level > 1.15 * max(before, after)
            for before, level, after in zip(envelope, envelope[1:], envelope[2:])
        )
        assert peaks >= 2, "Compression must retain several distinct close wet surges"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--validate-only",
        action="store_true",
        help="Rebuild in memory and compare exact PNG/WAV bytes",
    )
    parser.add_argument(
        "--contact-sheet",
        action="store_true",
        help="Write a temporary phase preview under Captures/RoomShadowAssets",
    )
    args = parser.parse_args()
    if args.validate_only and args.contact_sheet:
        parser.error("--validate-only cannot publish a contact sheet")
    images = [render(index) for index in range(FRAME_COUNT)]
    validate_bank(images)
    data_bank, audio_data = [encode(image) for image in images], audio_phrase()
    validate_audio(audio_data)
    outputs = [
        OUTPUT.with_name(f"RoomAttackShadow{index if index else ''}.png")
        for index in range(FRAME_COUNT)
    ]
    if args.validate_only:
        for path, data in zip(outputs, data_bank):
            assert path.is_file() and path.read_bytes() == data, (
                f"Asset differs: {path}"
            )
        assert AUDIO_OUTPUT.is_file() and AUDIO_OUTPUT.read_bytes() == audio_data, (
            "Sound differs; rerun generator"
        )
    else:
        assert data_bank == [encode(render(index)) for index in range(FRAME_COUNT)], (
            "Cookies must reproduce identical bytes"
        )
        assert audio_data == audio_phrase(), "Sound must reproduce identical bytes"
        OUTPUT.parent.mkdir(parents=True, exist_ok=True)
        for path, data in zip(outputs, data_bank):
            path.write_bytes(data)
        AUDIO_OUTPUT.write_bytes(audio_data)
        if args.contact_sheet:
            sheet = Image.new("RGB", (SIZE * 6, (SIZE + 24) * 4), (240, 240, 235))
            draw = ImageDraw.Draw(sheet)
            for index, image in enumerate(images):
                x, y = index % 6 * SIZE, index // 6 * (SIZE + 24)
                sheet.paste(image.convert("RGB"), (x, y))
                state = collision_phase(index * FRAME_CYCLE / FRAME_COUNT)
                label = (
                    "compress"
                    if state.compression > 0.5
                    else "pull"
                    if state.release > 0.25
                    else "settle"
                    if state.phase > 0.7
                    else "approach"
                )
                draw.text(
                    (x + 8, y + SIZE + 5),
                    f"{index}: {index * FRAME_CYCLE / FRAME_COUNT:.3f}s {label}",
                    fill=(30, 30, 30),
                )
            preview = ROOT / "Captures/RoomShadowAssets/contact-sheet.png"
            preview.parent.mkdir(parents=True, exist_ok=True)
            sheet.save(preview)
    print(
        f"Room yielding shadow bank: {'validated' if args.validate_only else 'generated'} "
        f"{FRAME_COUNT} x {SIZE}x{SIZE}, R8+mips=2097144 B, sha256={hashlib.sha256(b''.join(data_bank)).hexdigest()}"
    )
    print(
        f"Room wet material sound: {'validated' if args.validate_only else 'generated'} "
        f"mono PCM16 {AUDIO_RATE} Hz {AUDIO_DURATION}s, sha256={hashlib.sha256(audio_data).hexdigest()}"
    )


if __name__ == "__main__":
    main()
