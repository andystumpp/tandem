"""Draws the app icon: two frosted-glass task cards floating on a vivid gradient, the front one
carrying the app's progress ring and a check.

Run: python tools/gen_icon.py
Writes src/TaskList.App/Assets/AppIcon.ico (16-256 px; small sizes are drawn separately as a bold
ring and check so they stay crisp on the taskbar) and AppIcon.png (1024 px preview).
"""
import math
import os

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ASSETS = os.path.join(os.path.dirname(__file__), "..", "src", "TaskList.App", "Assets")
SS = 4  # supersampling factor

# A restrained indigo-to-violet field with a cool cyan glow: the app's "Pearl"/"Nebula" pairing.
LILAC, VIOLET, INDIGO, NIGHT, CYAN = "#9A8CF2", "#6655DD", "#3F3AAE", "#262A6E", "#5CC6DE"
PINK, CORAL = VIOLET, CYAN  # used for the ring's gradient on the front card


def rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def mix(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def mesh(size):
    """A smooth field: soft lilac top-left deepening to night indigo, with a faint cyan glow bottom-left."""
    n = 64
    img = Image.new("RGB", (n, n))
    px = img.load()
    for y in range(n):
        for x in range(n):
            u, v = x / (n - 1), y / (n - 1)
            d = (u + v) / 2  # diagonal position
            base = mix(rgb(LILAC), rgb(VIOLET), min(1, d / 0.45)) if d < 0.45 else mix(rgb(VIOLET), rgb(INDIGO), min(1, (d - 0.45) / 0.35))
            if d > 0.8:
                base = mix(base, rgb(NIGHT), (d - 0.8) / 0.2 * 0.8)
            g = max(0, 1 - math.hypot(u - 0.05, v - 0.95) / 0.75)
            base = mix(base, rgb(CYAN), g ** 2 * 0.45)
            px[x, y] = base
    return img.resize((size, size), Image.BICUBIC)


def rounded_mask(size, box, radius):
    m = Image.new("L", (size, size), 0)
    ImageDraw.Draw(m).rounded_rectangle(box, radius=radius, fill=255)
    return m


def rotated(mask, degrees, center):
    return mask.rotate(degrees, resample=Image.BICUBIC, center=center)


def glass_card(tile, mask, body, tint_alpha, rim=True):
    """Frosts whatever is behind `mask` on `tile`: blur, white tint, lit rim and a soft shadow."""
    frosted = tile.filter(ImageFilter.GaussianBlur(body * 0.05))
    white = Image.new("RGBA", tile.size, (255, 255, 255, tint_alpha))
    frosted.alpha_composite(white)

    # Shadow falls outside the card only, so the glass itself stays bright.
    shadow = Image.new("RGBA", tile.size, (0, 0, 0, 0))
    shadow.paste((50, 0, 80, 110), (0, int(body * 0.03)), mask)
    shadow = shadow.filter(ImageFilter.GaussianBlur(body * 0.035))
    outside = ImageChops.invert(mask)
    shadow.putalpha(ImageChops.multiply(shadow.getchannel("A"), outside))
    tile.alpha_composite(shadow)
    tile.paste(frosted, (0, 0), mask)

    if rim:
        # A bright edge that fades from top-left to bottom-right.
        edge = ImageChops.subtract(mask, mask.filter(ImageFilter.MinFilter(max(3, int(body * 0.012)) | 1)))
        fade = Image.linear_gradient("L").rotate(45, resample=Image.BICUBIC).resize(tile.size).point(lambda v: 255 - int(v * 0.75))
        edge = ImageChops.multiply(edge, fade)
        tile.paste((255, 255, 255, 255), (0, 0), edge)


def ring_and_check(draw, c, ring_r, stroke, check_scale=1.0, track=True):
    box = [c[0] - ring_r, c[1] - ring_r, c[0] + ring_r, c[1] + ring_r]
    start, end = -90, 200
    if track:
        draw.arc(box, end + 16, start + 360 - 16, fill=(255, 255, 255, 90), width=max(1, int(stroke * 0.5)))
    draw.arc(box, start, end, fill=(255, 255, 255, 255), width=int(stroke))
    for angle in (start, end):
        a = math.radians(angle)
        x, y = c[0] + (ring_r - stroke / 2) * math.cos(a), c[1] + (ring_r - stroke / 2) * math.sin(a)
        draw.ellipse([x - stroke / 2, y - stroke / 2, x + stroke / 2, y + stroke / 2], fill=(255, 255, 255, 255))
    w = stroke * 0.95 * check_scale
    r = ring_r * check_scale
    p1 = (c[0] - r * 0.40, c[1] + r * 0.02)
    p2 = (c[0] - r * 0.10, c[1] + r * 0.32)
    p3 = (c[0] + r * 0.44, c[1] - r * 0.30)
    draw.line([p1, p2, p3], fill=(255, 255, 255, 255), width=int(w), joint="curve")
    for p in (p1, p3):
        draw.ellipse([p[0] - w / 2, p[1] - w / 2, p[0] + w / 2, p[1] + w / 2], fill=(255, 255, 255, 255))


def draw_icon(px):
    S = px * SS
    detailed = px >= 64
    # Taskbar sizes get the same breathing room Windows' own icons have, so the tile doesn't shout.
    inset = int(S * (0.04 if detailed else 0.07))
    body = S - inset * 2

    tile = mesh(body).convert("RGBA")
    # A soft bloom of light in the top-left.
    bloom = Image.new("RGBA", (body, body), (0, 0, 0, 0))
    ImageDraw.Draw(bloom).ellipse([-body * 0.3, -body * 0.4, body * 0.6, body * 0.45], fill=(235, 230, 255, 90))
    tile.alpha_composite(bloom.filter(ImageFilter.GaussianBlur(body * 0.14)))

    if detailed:
        # Back card: tilted, peeking out above and to the left.
        back = rounded_mask(body, [body * 0.17, body * 0.13, body * 0.73, body * 0.69], int(body * 0.1))
        glass_card(tile, rotated(back, 9, (body * 0.45, body * 0.41)), body, 70)
        # Front card: the hero, slightly turned the other way.
        front = rounded_mask(body, [body * 0.27, body * 0.27, body * 0.86, body * 0.86], int(body * 0.11))
        front = rotated(front, -4, (body * 0.565, body * 0.565))
        glass_card(tile, front, body, 150)

        # On the bright glass the ring and check take the brand gradient, so they pop.
        shape = Image.new("RGBA", (body, body), (0, 0, 0, 0))
        ring_and_check(ImageDraw.Draw(shape), (body * 0.565, body * 0.565), body * 0.165, body * 0.052)
        ink = Image.new("RGB", (2, 2))
        ink.putdata([rgb(VIOLET), rgb(CYAN), rgb(INDIGO), rgb(VIOLET)])
        ink = ink.resize((body, body), Image.BILINEAR).convert("RGBA")
        ink.putalpha(shape.getchannel("A"))
        glow = Image.new("RGBA", (body, body), (0, 0, 0, 0))
        glow.paste((110, 90, 230, 110), (0, int(body * 0.012)), shape.getchannel("A"))
        tile.alpha_composite(glow.filter(ImageFilter.GaussianBlur(body * 0.02)))
        tile.alpha_composite(ink)
    else:
        # Taskbar sizes: just the bold ring and check, as large as the tile allows.
        glyph = Image.new("RGBA", (body, body), (0, 0, 0, 0))
        ring_and_check(ImageDraw.Draw(glyph), (body / 2, body / 2), body * 0.28, body * (0.105 if px <= 20 else 0.09), track=False)
        tile.alpha_composite(glyph)

    mask = rounded_mask(body, [0, 0, body - 1, body - 1], int(body * 0.23))
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(tile, (inset, inset), mask)

    if detailed:
        # Lit rim around the whole tile.
        rim = ImageChops.subtract(mask, mask.filter(ImageFilter.MinFilter(max(3, int(body * 0.01)) | 1)))
        fade = Image.linear_gradient("L").resize((body, body)).point(lambda v: int((255 - v) * 0.6))
        rim = ImageChops.multiply(rim, fade)
        layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        layer.paste((255, 255, 255, 255), (inset, inset), rim)
        out.alpha_composite(layer)

    return out.resize((px, px), Image.LANCZOS)


def main():
    sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
    images = [draw_icon(s) for s in sizes]
    ico = os.path.join(ASSETS, "AppIcon.ico")
    images[-1].save(ico, format="ICO", sizes=[(s, s) for s in sizes], append_images=images[:-1])
    draw_icon(1024).save(os.path.join(ASSETS, "AppIcon.png"))
    print(ico, os.path.getsize(ico) // 1024, "KB")


if __name__ == "__main__":
    main()
