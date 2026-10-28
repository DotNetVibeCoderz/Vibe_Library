# APL.Net - Another Parallel Library for .NET
# Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
#
# Renders the package icon: four parallel lanes cut into partitions, all finishing on the same line
# (static partitioning: equal shares, one finish), on the Gallery's ultramarine. Drawn at 1024 px
# and downsampled, so the edges stay clean at the 128 px nuget.org asks for.
#
#   python tools/icon/make_icon.py
#
# Writes src/APL.Net/icon.png (128 px, packed into the nupkg) and docs/images/icon-512.png.

import math
import os
from PIL import Image, ImageDraw, ImageFilter

S = 1024
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))

TOP = (58, 76, 236)       # lighter ultramarine
BOTTOM = (16, 20, 92)     # deep navy
LANE = (255, 255, 255)
ACCENT = (94, 230, 255)   # cyan: the vectorised edge
FINISH = (255, 196, 92)   # warm finish line


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def rounded_mask(size, radius):
    mask = Image.new('L', (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    return mask


def background():
    img = Image.new('RGB', (S, S))
    px = img.load()
    for y in range(S):
        for x in range(S):
            # Diagonal gradient, top-left light to bottom-right deep.
            t = (x * 0.35 + y * 0.65) / S
            px[x, y] = lerp(TOP, BOTTOM, min(1.0, max(0.0, t)))
    # A soft glow behind the finish line.
    glow = Image.new('L', (S, S), 0)
    ImageDraw.Draw(glow).ellipse([560, 180, 1100, 860], fill=120)
    glow = glow.filter(ImageFilter.GaussianBlur(140))
    img = Image.composite(Image.new('RGB', (S, S), (110, 140, 255)), img, glow)
    return img


def draw_lanes(img):
    over = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(over)

    finish_x = 790
    lane_h = 104
    gap = 52
    top = (S - (4 * lane_h + 3 * gap)) // 2
    starts = [214, 290, 178, 252]          # ragged starts: work arrives unevenly
    for k in range(4):
        y0 = top + k * (lane_h + gap)
        y1 = y0 + lane_h
        x0 = starts[k]
        # Partitions: equal blocks with small gaps, the last one cyan (the SIMD tail).
        blocks = 3
        seg_gap = 18
        width = finish_x - x0
        seg = (width - (blocks - 1) * seg_gap) / blocks
        for b in range(blocks):
            bx0 = x0 + b * (seg + seg_gap)
            bx1 = bx0 + seg
            alpha = [150, 205, 255][b]
            colour = LANE + (alpha,) if b < blocks - 1 else ACCENT + (255,)
            d.rounded_rectangle([bx0, y0, bx1, y1], radius=26, fill=colour)
        # Speed streaks trailing each lane.
        for s in range(3):
            sx1 = x0 - 34 - s * 46
            sx0 = sx1 - (70 - s * 18)
            sy = y0 + lane_h // 2
            d.rounded_rectangle([sx0, sy - 9, sx1, sy + 9], radius=9, fill=LANE + (110 - s * 35,))

    # Finish line: every lane ends here at once.
    d.rounded_rectangle([finish_x + 34, top - 40, finish_x + 66, top + 4 * lane_h + 3 * gap + 40], radius=16, fill=FINISH + (255,))

    # Lean everything forward: x' = x + shear * (y - centre). PIL maps output to input, so the
    # inverse shear goes in the matrix.
    shear = 0.24
    over = over.transform((S, S), Image.AFFINE, (1, shear, -shear * S / 2 + 10, 0, 1, 0), resample=Image.BICUBIC)
    img.paste(over, (0, 0), over)


def main():
    img = background()
    draw_lanes(img)
    radius = int(S * 0.22)
    out = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    out.paste(img, (0, 0), rounded_mask(S, radius))

    out.resize((128, 128), Image.LANCZOS).save(os.path.join(ROOT, 'src', 'APL.Net', 'icon.png'), optimize=True)
    out.resize((512, 512), Image.LANCZOS).save(os.path.join(ROOT, 'docs', 'images', 'icon-512.png'), optimize=True)
    print('wrote icon.png (128) and icon-512.png')


if __name__ == '__main__':
    main()
