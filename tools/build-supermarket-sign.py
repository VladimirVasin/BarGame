"""Paint the supermarket's enamel sign at its physical aspect ratio."""
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "Assets/Resources/Supermarket/ExteriorTextures/SupermarketSign.png"


def build():
    # Letter spacing and outlines are drawn in square pixels, then packed to
    # a mip-friendly strip. The board restores the 6.05:0.90 aspect in game.
    width, height = 2048, 305
    board = Image.new("RGB", (width, height), (217, 211, 184))
    pen = ImageDraw.Draw(board)
    ink = (43, 59, 50)
    pen.rounded_rectangle((13, 13, width-14, height-14), radius=8, outline=ink, width=5)
    font = ImageFont.truetype(str(ROOT / "Assets/Resources/Fonts/Roboto-Regular.ttf"), 257)
    word = "ПРОДУКТЫ"
    # A painted grocery fascia has generously tracked capitals and side margins.
    advance = [font.getlength(ch) for ch in word]
    spacing = 32
    x = (width - sum(advance) - spacing * (len(word)-1)) * .5
    _, top, _, bottom = pen.textbbox((0, 0), word, font=font, stroke_width=2)
    y = (height - top - bottom) * .5
    for ch, step in zip(word, advance):
        pen.text((round(x), round(y)), ch, font=font, fill=ink, stroke_width=2, stroke_fill=ink)
        x += step + spacing
    board = board.resize((1024, 128), Image.Resampling.LANCZOS)
    board.save(OUTPUT, optimize=True)

    # Diffuse reflection of the two shielded lenses, in board-local metres.
    # The bake follows the mounted swivel and the 205 mm offset to its diffuser.
    u = ((np.arange(board.width) + .5) / board.width - .5) * 6.05
    v = (.5 - (np.arange(board.height)[:, None] + .5) / board.height) * .90
    pivot_z = (.18 + .08) * 2.6 - .075
    lens_scale = 1 - .205 / np.hypot(.61, pivot_z)
    lens_y, lens_z = .61 * lens_scale, pivot_z * lens_scale
    irradiance = np.zeros((board.height, board.width))
    # Measured across the board, the fan opens from the upper rim toward the lower one.
    beam_width = .54 + (.45 - v) * 2.8
    for side in (-1, 1):
        dx = u - side * 6.05 * .225
        r2 = (dx / 3.5)**2 + (v - lens_y)**2 + lens_z**2
        diffuser = .10 + .90 * np.exp(-.5 * (dx / beam_width)**2)
        irradiance += diffuser * lens_z / r2**1.5
    # Frosted enamel and a broad diffuser soften the two near-field peaks.
    wash = .78 * (1 - np.exp(-.85 * irradiance))
    text_band = wash[board.height // 2, abs(u) < 2.45]
    if text_band.min() < .75 * text_band.max():
        raise ValueError("The two lamp footprints must cover the full word, including the central join.")
    if wash[2, board.width // 2] > .65 * wash[2].max():
        raise ValueError("The two light sources should still be visible at the upper rim.")
    rgb = np.asarray(board, dtype=float) / 255
    pigment = np.where(rgb <= .04045, rgb / 12.92, ((rgb + .055) / 1.055)**2.4)
    reflected = pigment * wash[..., None]
    srgb = np.where(reflected <= .0031308, 12.92 * reflected, 1.055 * reflected**(1/2.4) - .055)
    srgb[-1, 0] = 0  # Plain rear and bevel UV.
    Image.fromarray(np.uint8(np.clip(srgb, 0, 1) * 255 + .5)).save(OUTPUT.with_name("SupermarketSignWash.png"), optimize=True)


if __name__ == "__main__":
    build()
