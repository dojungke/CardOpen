from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import math


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets/CardPackPrototype/Resources/Textures/CardMagicBulletFront.png"
OUT = ROOT / "Assets/CardPackPrototype/Resources/CardAssets"
PREVIEW = ROOT / "Assets/CardPackPrototype/CardAssetCatalog.png"
W, H = 1024, 1840

ATTRIBUTES = {
    "Green": (105, 157, 70),
    "Blue": (64, 126, 180),
    "Red": (181, 72, 64),
    "Black": (45, 48, 53),
    "White": (224, 220, 202),
}


def ensure_dirs():
    for folder in ("Attributes", "Rarities", "Costs", "Content"):
        (OUT / folder).mkdir(parents=True, exist_ok=True)


def darken(color, amount):
    return tuple(max(0, int(channel * (1.0 - amount))) for channel in color)


def lighten(color, amount):
    return tuple(min(255, int(channel + (255 - channel) * amount)) for channel in color)


def draw_background(name, color):
    image = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    ink = (20, 24, 20, 255) if name != "Black" else (224, 220, 202, 255)
    draw.rounded_rectangle((18, 18, W - 18, H - 18), radius=78, fill=color + (255,), outline=ink, width=15)
    draw.rounded_rectangle((43, 43, W - 43, H - 43), radius=60, outline=darken(color, 0.32) + (255,), width=7)
    draw.rounded_rectangle((58, 1060, W - 58, H - 72), radius=34,
                           fill=lighten(color, 0.05) + (255,), outline=darken(color, 0.22) + (255,), width=7)
    draw.rounded_rectangle((63, 296, W - 63, 1048), radius=28, fill=(247, 244, 232, 255), outline=ink, width=12)
    image.save(OUT / "Attributes" / f"Attribute{name}.png")
    return image


def draw_spiral(draw, center, start_radius, turns, color, width):
    points = []
    for i in range(150):
        t = i / 149.0 * math.tau * turns
        radius = start_radius * (1.0 - i / 165.0)
        points.append((center[0] + math.cos(t) * radius, center[1] + math.sin(t) * radius))
    draw.line(points, fill=color, width=width, joint="curve")


def draw_pattern(name):
    image = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    if name == "Common":
        ink = (15, 25, 15, 34)
        for x, y in ((110, 150), (880, 170), (130, 1640), (870, 1610), (520, 1160)):
            draw.ellipse((x - 8, y - 8, x + 8, y + 8), fill=ink)
        for offset in range(-180, 1180, 170):
            draw.line((offset, 1080, offset + 430, 1760), fill=(15, 25, 15, 18), width=6)
    elif name == "Rare":
        ink = (220, 245, 255, 58)
        draw_spiral(draw, (210, 1460), 155, 2.2, ink, 9)
        draw.arc((680, 1110, 1050, 1480), 80, 285, fill=ink, width=8)
        draw.ellipse((385, 1200, 720, 1535), outline=ink, width=7)
        draw.ellipse((445, 1260, 660, 1475), outline=ink, width=5)
    elif name == "Epic":
        ink = (230, 205, 255, 64)
        nodes = [(220, 1290), (505, 1130), (815, 1280), (705, 1580), (330, 1620), (510, 1420)]
        edges = ((0, 1), (1, 2), (2, 3), (3, 4), (4, 0), (0, 5), (1, 5), (2, 5), (3, 5), (4, 5))
        for a, b in edges:
            draw.line((nodes[a], nodes[b]), fill=ink, width=7)
        for x, y in nodes:
            draw.ellipse((x - 24, y - 24, x + 24, y + 24), outline=ink, width=7)
        draw.arc((115, 1080, 905, 1740), 195, 350, fill=ink, width=6)
    else:
        ink = (255, 218, 105, 82)
        center = (512, 1400)
        for radius in (150, 250, 355):
            draw.ellipse((center[0] - radius, center[1] - radius, center[0] + radius, center[1] + radius), outline=ink, width=7)
        for i in range(16):
            angle = math.tau * i / 16
            inner = 95 if i % 2 else 55
            outer = 390 if i % 2 else 430
            draw.line((center[0] + math.cos(angle) * inner, center[1] + math.sin(angle) * inner,
                       center[0] + math.cos(angle) * outer, center[1] + math.sin(angle) * outer), fill=ink, width=7)
        for x, y in ((125, 1150), (900, 1180), (145, 1680), (875, 1660)):
            draw.regular_polygon((x, y, 24), n_sides=4, rotation=45, outline=ink, width=6)
    image.save(OUT / "Rarities" / f"Pattern{name}.png")
    return image


def find_font(size):
    candidates = [
        Path("C:/Windows/Fonts/arialbd.ttf"),
        Path("C:/Windows/Fonts/seguisb.ttf"),
        Path("C:/Windows/Fonts/malgunbd.ttf"),
    ]
    for path in candidates:
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def draw_cost(value):
    image = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    box = (45, 38, 274, 267)
    draw.ellipse(box, fill=(250, 248, 239, 255), outline=(12, 14, 12, 255), width=13)
    symbol = "σ" if value == 6 else str(value)
    font = find_font(154 if value != 6 else 144)
    bounds = draw.textbbox((0, 0), symbol, font=font, stroke_width=1)
    tw, th = bounds[2] - bounds[0], bounds[3] - bounds[1]
    cx = (box[0] + box[2]) / 2
    cy = (box[1] + box[3]) / 2
    draw.text((cx - tw / 2 - bounds[0], cy - th / 2 - bounds[1] - 5), symbol,
              font=font, fill=(10, 12, 10, 255), stroke_width=1, stroke_fill=(10, 12, 10, 255))
    filename = "CostSigma.png" if value == 6 else f"Cost{value}.png"
    image.save(OUT / "Costs" / filename)
    return image


def black_only(region):
    rgba = region.convert("RGBA")
    pixels = rgba.load()
    for y in range(rgba.height):
        for x in range(rgba.width):
            r, g, b, _ = pixels[x, y]
            luminance = (r * 3 + g * 5 + b * 2) / 10
            alpha = max(0, min(255, int((145 - luminance) * 3.2)))
            pixels[x, y] = (r, g, b, alpha)
    return rgba


def build_content_layer():
    source = Image.open(SOURCE).convert("RGBA").resize((W, H), Image.Resampling.LANCZOS)
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    title_box = (275, 55, 960, 285)
    art_box = (55, 286, 969, 1056)
    effect_box = (70, 1120, 960, 1510)
    title = black_only(source.crop(title_box))
    effect = black_only(source.crop(effect_box))
    layer.alpha_composite(title, title_box[:2])
    layer.alpha_composite(source.crop(art_box), art_box[:2])
    layer.alpha_composite(effect, effect_box[:2])
    layer.save(OUT / "Content" / "CardMagicBulletContent.png")
    return layer


def make_preview(backgrounds, patterns, costs, content):
    thumb_w, thumb_h = 205, 368
    sheet = Image.new("RGB", (thumb_w * 6, thumb_h * 3), (28, 31, 35))
    for i, image in enumerate(backgrounds):
        sheet.paste(image.convert("RGB").resize((thumb_w, thumb_h)), (i * thumb_w, 0))
    base = backgrounds[0].copy()
    for i, pattern in enumerate(patterns):
        composed = base.copy()
        composed.alpha_composite(pattern)
        composed.alpha_composite(content)
        sheet.paste(composed.convert("RGB").resize((thumb_w, thumb_h)), (i * thumb_w, thumb_h))
    for i, cost in enumerate(costs):
        composed = base.copy()
        composed.alpha_composite(patterns[min(i // 2, 3)])
        composed.alpha_composite(content)
        composed.alpha_composite(cost)
        sheet.paste(composed.convert("RGB").resize((thumb_w, thumb_h)), (i * thumb_w, thumb_h * 2))
    sheet.save(PREVIEW)


def main():
    ensure_dirs()
    backgrounds = [draw_background(name, color) for name, color in ATTRIBUTES.items()]
    patterns = [draw_pattern(name) for name in ("Common", "Rare", "Epic", "Legendary")]
    costs = [draw_cost(value) for value in range(1, 7)]
    content = build_content_layer()
    make_preview(backgrounds, patterns, costs, content)
    print(f"Generated {len(backgrounds) + len(patterns) + len(costs) + 1} reusable layers")
    print(PREVIEW)


if __name__ == "__main__":
    main()
