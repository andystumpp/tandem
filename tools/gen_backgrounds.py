"""Draws the image themes' backgrounds (Pillow only) into src/TaskList.App/Assets/Themes.

Run: python tools/gen_backgrounds.py
Every scene is generated from a fixed seed, so the output is reproducible.
"""
import math
import os
import random

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

W, H = 900, 1500
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "TaskList.App", "Assets", "Themes")


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def gradient(stops, w=W, h=H):
    """Vertical gradient from [(offset, '#rrggbb'), ...]."""
    col = Image.new("RGB", (1, h))
    px = col.load()
    stops = [(o, hex_rgb(c)) for o, c in stops]
    for y in range(h):
        t = y / (h - 1)
        for i in range(len(stops) - 1):
            o0, c0 = stops[i]
            o1, c1 = stops[i + 1]
            if o0 <= t <= o1:
                px[0, y] = lerp(c0, c1, (t - o0) / max(1e-6, o1 - o0))
                break
        else:
            px[0, y] = stops[-1][1] if t > stops[-1][0] else stops[0][1]
    return col.resize((w, h))


def layer(img, draw_fn, blur=0, opacity=255):
    """Draws onto a transparent layer and composites it over img."""
    over = Image.new("RGBA", img.size, (0, 0, 0, 0))
    draw_fn(ImageDraw.Draw(over), over)
    if blur:
        # Blur premultiplied so soft edges fade to transparent instead of to black.
        over = over.convert("RGBa").filter(ImageFilter.GaussianBlur(blur)).convert("RGBA")
    if opacity < 255:
        alpha = over.getchannel("A").point(lambda a: a * opacity // 255)
        over.putalpha(alpha)
    base = img.convert("RGBA")
    base.alpha_composite(over)
    return base.convert("RGB")


def glow(img, cx, cy, r, color, strength=200, blur=None):
    def fn(d, _):
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=hex_rgb(color) + (strength,))
    return layer(img, fn, blur=blur or r * 0.6)


def ridge(rng, y_base, amplitude, roughness, step=12):
    """A jagged mountain ridge as a list of points across the width."""
    pts, y = [], y_base
    for x in range(-step, W + step * 2, step):
        y += rng.uniform(-roughness, roughness)
        y = max(y_base - amplitude, min(y_base + amplitude * 0.4, y))
        pts.append((x, y))
    return pts


def mix3(stops, t):
    """Color along a multi-stop ramp [(offset, '#hex'), ...] at t in 0..1."""
    stops = [(o, hex_rgb(c)) for o, c in stops]
    t = min(1.0, max(0.0, t))
    for (o0, c0), (o1, c1) in zip(stops, stops[1:]):
        if o0 <= t <= o1:
            return lerp(c0, c1, (t - o0) / max(1e-6, o1 - o0))
    return stops[-1][1]


def prism():
    """Geometric: a low-poly field of facets in lavender, sky and mint, lit from the top-left."""
    rng = random.Random(21)
    ramp = [(0, "#C9BEFF"), (0.45, "#9FD4FF"), (0.8, "#A6F0DA"), (1, "#E9FBF5")]
    img = Image.new("RGB", (W, H), hex_rgb("#F4F6FF"))
    cols, rows = 9, 17
    cw, rh = W / (cols - 1), H / (rows - 1)
    pts = [[(c * cw + (rng.uniform(-0.35, 0.35) * cw if 0 < c < cols - 1 else 0),
             r * rh + (rng.uniform(-0.35, 0.35) * rh if 0 < r < rows - 1 else 0)) for c in range(cols)] for r in range(rows)]

    def facets(d, _):
        for r in range(rows - 1):
            for c in range(cols - 1):
                a, b, cc, dd = pts[r][c], pts[r][c + 1], pts[r + 1][c + 1], pts[r + 1][c]
                tris = [(a, b, cc), (a, cc, dd)] if (r + c) % 2 == 0 else [(a, b, dd), (b, cc, dd)]
                for tri in tris:
                    cx = sum(p[0] for p in tri) / 3
                    cy = sum(p[1] for p in tri) / 3
                    t = (cx / W) * 0.45 + (cy / H) * 0.55
                    base = mix3(ramp, t + rng.uniform(-0.06, 0.06))
                    light = 1 + rng.uniform(-0.07, 0.09) - (cy / H) * 0.05
                    color = tuple(min(255, int(v * light)) for v in base)
                    d.polygon(tri, fill=color + (255,), outline=(255, 255, 255, 70))
    img = layer(img, facets)
    img = glow(img, int(W * 0.2), int(H * 0.15), 260, "#FFFFFF", 120)
    return img


def flow():
    """Abstract: smooth ribbons of indigo, teal and coral sweeping across a light field."""
    rng = random.Random(31)
    img = gradient([(0, "#EEF1FF"), (0.5, "#F6F2FF"), (1, "#FFF3F0")])
    ribbons = [("#6C7CFF", 0.30, 120, 0.0, 200), ("#45CFC4", 0.40, 100, 1.3, 180), ("#FF8E9E", 0.50, 90, 2.4, 170),
               ("#9B7BFF", 0.58, 80, 3.3, 150), ("#5FB8FF", 0.66, 70, 4.1, 140)]
    for color, base, amp, phase, alpha in ribbons:
        thickness = rng.uniform(70, 120)
        top = [(x, H * base + amp * math.sin(x / 210 + phase) + 30 * math.sin(x / 95 + phase * 1.7)) for x in range(-20, W + 30, 6)]
        bottom = [(x, y + thickness + 25 * math.sin(x / 140 + phase)) for x, y in reversed(top)]
        img = layer(img, lambda d, _: d.polygon(top + bottom, fill=hex_rgb(color) + (alpha,)), blur=3)
        img = layer(img, lambda d, _: d.line(top, fill=(255, 255, 255, 140), width=3), blur=1)
    img = glow(img, int(W * 0.8), int(H * 0.12), 240, "#FFFFFF", 150)
    return img


def momentum():
    """Motivating: rising bars climbing to the right under warm light, with upward chevrons."""
    rng = random.Random(41)
    img = gradient([(0, "#FFF4E6"), (0.35, "#FFE0C2"), (0.7, "#FFC4A8"), (1, "#FFB0B8")])
    img = glow(img, int(W * 0.85), int(H * 0.12), 300, "#FFF8D6", 220)

    def rays(d, _):
        for i in range(6):
            x0 = W * (0.55 + i * 0.12)
            d.polygon([(x0, 0), (x0 + 60, 0), (x0 - 420, H), (x0 - 520, H)], fill=(255, 255, 255, 40))
    img = layer(img, rays, blur=20)

    bars = 9
    bw = W / bars
    for i in range(bars):
        t = i / (bars - 1)
        height = H * (0.12 + 0.42 * t ** 1.2) + rng.uniform(-12, 12)
        x0, x1 = i * bw + bw * 0.16, (i + 1) * bw - bw * 0.16
        top = H * 0.78 - height
        color = mix3([(0, "#FFB547"), (0.5, "#FF7A59"), (1, "#E2457E")], t)
        img = layer(img, lambda d, _: d.rounded_rectangle([x0, top, x1, H * 0.78], radius=18, fill=color + (235,)), blur=0.6)
        img = layer(img, lambda d, _: d.rounded_rectangle([x0, top, x1, top + 10], radius=5, fill=(255, 255, 255, 120)), blur=1)

    def chevrons(d, _):
        for k in range(3):
            cx, cy, s = W * 0.82, H * (0.2 + k * 0.06), 34
            d.line([(cx - s, cy + s * 0.6), (cx, cy - s * 0.2), (cx + s, cy + s * 0.6)], fill=(255, 255, 255, 200 - k * 50), width=9, joint="curve")
    img = layer(img, chevrons, blur=0.8)
    img = layer(img, lambda d, _: d.rectangle([0, H * 0.78, W, H], fill=hex_rgb("#FFE9DC") + (255,)))
    return img


def sakura():
    rng = random.Random(5)
    img = gradient([(0, "#FFF0F5"), (0.45, "#FFD3E2"), (1, "#F5A3C2")])
    img = glow(img, int(W * 0.8), int(H * 0.15), 260, "#FFFFFF", 200)
    def bokeh(d, _):
        for _ in range(70):
            x, y = rng.uniform(-50, W + 50), rng.uniform(0, H)
            r = rng.uniform(20, 90)
            c = rng.choice(["#FFFFFF", "#FFC1D8", "#FF9EC2", "#FFE6EF"])
            d.ellipse([x - r, y - r, x + r, y + r], fill=hex_rgb(c) + (rng.randint(50, 120),))
    img = layer(img, bokeh, blur=10)
    # A branch reaching in from the right, with blossoms along it.
    branch = [(W + 40, H * 0.18), (W * 0.78, H * 0.22), (W * 0.58, H * 0.2), (W * 0.42, H * 0.26), (W * 0.3, H * 0.24)]
    img = layer(img, lambda d, _: d.line(branch, fill=hex_rgb("#6B3A4B") + (230,), width=9, joint="curve"), blur=1)
    def blossoms(d, _):
        for i in range(len(branch) - 1):
            (x0, y0), (x1, y1) = branch[i], branch[i + 1]
            for _ in range(6):
                t = rng.random()
                cx = x0 + (x1 - x0) * t + rng.uniform(-30, 30)
                cy = y0 + (y1 - y0) * t + rng.uniform(-30, 30)
                size = rng.uniform(12, 20)
                for k in range(5):
                    a = k * math.tau / 5 + rng.uniform(0, 0.3)
                    px, py = cx + math.cos(a) * size * 0.8, cy + math.sin(a) * size * 0.8
                    d.ellipse([px - size * 0.6, py - size * 0.6, px + size * 0.6, py + size * 0.6], fill=hex_rgb(rng.choice(["#FFFFFF", "#FFD7E6", "#FFC0D6"])) + (235,))
                d.ellipse([cx - 4, cy - 4, cx + 4, cy + 4], fill=hex_rgb("#E05A8A") + (255,))
    img = layer(img, blossoms, blur=0.6)
    def petals(d, _):
        for _ in range(40):
            x, y = rng.uniform(0, W), rng.uniform(H * 0.3, H)
            s = rng.uniform(5, 10)
            d.ellipse([x - s, y - s * 0.6, x + s, y + s * 0.6], fill=hex_rgb("#FFC4D9") + (200,))
    return layer(img, petals, blur=0.8)


def dunes():
    rng = random.Random(9)
    img = gradient([(0, "#7FC4F5"), (0.3, "#BFE3F7"), (0.5, "#FFE3B8"), (0.62, "#FFD08A"), (1, "#FFD9A8")])
    img = glow(img, int(W * 0.35), int(H * 0.55), 110, "#FFF3D6", 240)
    img = glow(img, int(W * 0.35), int(H * 0.55), 340, "#FF9C6B", 120)
    specs = [(0.58, "#E9875E", "#C9653F"), (0.66, "#D9714A", "#A84E2E"), (0.75, "#C25B36", "#8C3B22"), (0.86, "#A2452A", "#6E2C18")]
    for frac, lit, shade in specs:
        wl, ph = rng.uniform(180, 300), rng.uniform(0, 6)
        crest = [(x, H * frac + 55 * math.sin(x / wl + ph) + 14 * math.sin(x / 110 + ph * 2)) for x in range(-20, W + 30, 8)]
        img = layer(img, lambda d, _: d.polygon(crest + [(W + 30, H), (-20, H)], fill=hex_rgb(shade) + (255,)), blur=1)
        # The sunlit face: the same dune shifted a little and lighter.
        lit_pts = [(x, y + 18) for x, y in crest]
        img = layer(img, lambda d, _: d.polygon(lit_pts + [(W + 30, H), (-20, H)], fill=hex_rgb(lit) + (170,)), blur=6)
    return img


# The app shows each scene as a banner across the top of the window, fading into the theme's base
# color, so only the most characteristic band of each scene is kept (as fractions of the height).
BANNERS = {
    "prism": (0.0, 0.44),
    "flow": (0.18, 0.62),
    "momentum": (0.14, 0.58),
    "sakura": (0.02, 0.46),
    "dunes": (0.30, 0.74),
}


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, fn in [("prism", prism), ("flow", flow), ("momentum", momentum), ("sakura", sakura), ("dunes", dunes)]:
        top, bottom = BANNERS[name]
        banner = fn().crop((0, int(H * top), W, int(H * bottom)))
        # Bright and vivid: a lift in brightness and a touch more color.
        banner = ImageEnhance.Brightness(ImageEnhance.Color(banner).enhance(1.15)).enhance(1.04)
        # The picture dissolves into the window below it: alpha eases from opaque to clear over the
        # lower half (smoothstep), so there is no edge where the image ends.
        bw, bh = banner.size
        alpha = Image.new("L", (1, bh))
        for yy in range(bh):
            t = min(1.0, max(0.0, (yy / bh - 0.4) / 0.6))
            alpha.putpixel((0, yy), int(255 * (1 - t * t * (3 - 2 * t))))
        banner = banner.convert("RGBA")
        banner.putalpha(alpha.resize((bw, bh)))
        path = os.path.join(OUT, f"{name}_banner.png")
        banner.save(path, optimize=True)
        print(path, banner.size, os.path.getsize(path) // 1024, "KB")


if __name__ == "__main__":
    main()
