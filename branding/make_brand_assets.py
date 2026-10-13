"""
Builds every logo, icon and splash image of the apps from the two source images in branding/source:
  mountain.png  (brown mountain, transparent)
  wordmark.png  (HELLO Chérie SWITZERLAND, black, transparent)

    pip install pillow
    python branding/make_brand_assets.py

Outputs (overwritten):
  branding/hellocherie-logo.png              combined logo like the website (mountain, rule, boxed wordmark), transparent
  branding/hellocherie-logo-on-cream.png     the same on the website's cream, for previews
  backend/.../Branding/company-logo.png      default company logo on PDF documents (seeded when none is set)
  frontend/projects/{web,mobile}/public/brand/mountain.png, favicon.ico, icon-192.png
  frontend/projects/mobile/android/.../mipmap-*/ic_launcher*.png, splash*.png, values/ic_launcher_background.xml
  frontend/projects/mobile/ios/.../AppIcon-512@2x.png, Splash.imageset/*.png
"""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "branding" / "source"
BROWN = (154, 107, 44, 255)       # #9A6B2C, sampled from the mountain
CREAM = (241, 234, 224, 255)      # #F1EAE0, the website background
INK = (0, 0, 0, 255)


def trimmed(path: Path) -> Image.Image:
    im = Image.open(path).convert("RGBA")
    # Drop near-invisible noise so the crop is tight.
    alpha = im.getchannel("A").point(lambda a: 0 if a < 16 else a)
    im.putalpha(alpha)
    return im.crop(im.getbbox())


def fit(im: Image.Image, width: int) -> Image.Image:
    return im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)


def combined_logo() -> Image.Image:
    """Mountain centred over a thin rule and the wordmark in a rounded box, as in the website header."""
    mountain, word = trimmed(SRC / "mountain.png"), trimmed(SRC / "wordmark.png")
    word = fit(word, 1600)
    pad_x, pad_y, border, radius = 46, 30, 12, 34
    box_w, box_h = word.width + 2 * pad_x, word.height + 2 * pad_y
    mountain = fit(mountain, round(box_w * 0.86))
    gap, rule_h, rule_gap = 18, 9, 34
    width = box_w + 2 * border
    height = mountain.height + gap + rule_h + rule_gap + box_h
    out = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    out.alpha_composite(mountain, ((width - mountain.width) // 2, 0))
    y = mountain.height + gap
    ImageDraw.Draw(out).rectangle([border, y, width - border, y + rule_h], fill=(62, 48, 32, 255))
    y += rule_h + rule_gap
    d = ImageDraw.Draw(out)
    d.rounded_rectangle([border // 2, y, width - border // 2 - 1, y + box_h - 1], radius=radius, fill=(255, 255, 255, 255), outline=INK, width=border)
    out.alpha_composite(word, ((width - word.width) // 2, y + pad_y))
    return out


def on_background(im: Image.Image, size: tuple[int, int], scale: float, color=CREAM, y_shift: float = 0.0) -> Image.Image:
    """`im` scaled to `scale` of the canvas width (or height, whichever is tighter), centred on a coloured canvas."""
    w, h = size
    s = min(w * scale / im.width, h * scale / im.height)
    small = im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS)
    out = Image.new("RGBA", size, color)
    out.alpha_composite(small, ((w - small.width) // 2, round((h - small.height) / 2 + h * y_shift)))
    return out


def rounded_mask(size: int, radius_ratio: float) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=round(size * radius_ratio), fill=255)
    return mask


def save(im: Image.Image, path: Path, rgb: bool = False) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    (im.convert("RGB") if rgb else im).save(path, optimize=True)
    print("wrote", path.relative_to(ROOT), im.size)


def main() -> None:
    logo = combined_logo()
    mountain = trimmed(SRC / "mountain.png")

    save(fit(logo, 1200), ROOT / "branding" / "hellocherie-logo.png")
    save(on_background(logo, (1400, round(1400 * logo.height / logo.width) + 160), 0.86), ROOT / "branding" / "hellocherie-logo-on-cream.png", rgb=True)
    save(fit(logo, 640), ROOT / "backend/src/Inventory.Infrastructure/Branding/company-logo.png")

    # Web and phone: the mountain alone.
    for app in ("web", "mobile"):
        pub = ROOT / "frontend/projects" / app / "public"
        save(fit(mountain, 512), pub / "brand" / "mountain.png")
        icon = on_background(mountain, (512, 512), 0.82, CREAM)
        icon.putalpha(rounded_mask(512, 0.22))
        save(icon.resize((192, 192), Image.LANCZOS), pub / "brand" / "icon-192.png")
        fav = on_background(mountain, (256, 256), 0.94, (0, 0, 0, 0))
        fav.save(pub / "favicon.ico", sizes=[(16, 16), (32, 32), (48, 48), (64, 64)])
        print("wrote", (pub / "favicon.ico").relative_to(ROOT))

    # Android launcher icons: legacy square/round, and the adaptive foreground (safe zone = central 66%).
    res = ROOT / "frontend/projects/mobile/android/app/src/main/res"
    for density, px in {"mdpi": 48, "hdpi": 72, "xhdpi": 96, "xxhdpi": 144, "xxxhdpi": 192}.items():
        legacy = on_background(mountain, (px * 4, px * 4), 0.84, CREAM)
        sq = legacy.copy()
        sq.putalpha(rounded_mask(px * 4, 0.18))
        save(sq.resize((px, px), Image.LANCZOS), res / f"mipmap-{density}" / "ic_launcher.png")
        rnd = on_background(mountain, (px * 4, px * 4), 0.76, CREAM)
        circle = Image.new("L", rnd.size, 0)
        ImageDraw.Draw(circle).ellipse([0, 0, rnd.width - 1, rnd.height - 1], fill=255)
        rnd.putalpha(circle)
        save(rnd.resize((px, px), Image.LANCZOS), res / f"mipmap-{density}" / "ic_launcher_round.png")
        fg_px = round(px * 108 / 48)
        save(on_background(mountain, (fg_px * 4, fg_px * 4), 0.62, (0, 0, 0, 0)).resize((fg_px, fg_px), Image.LANCZOS),
             res / f"mipmap-{density}" / "ic_launcher_foreground.png")
    (res / "values" / "ic_launcher_background.xml").write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n<resources>\n    <color name="ic_launcher_background">#F1EAE0</color>\n</resources>\n', encoding="utf-8")

    # Android splash screens: the combined logo on cream.
    for f in sorted(res.glob("drawable*/splash.png")):
        w, h = Image.open(f).size
        save(on_background(logo, (w, h), 0.55 if w < h else 0.38, CREAM), f, rgb=True)

    # iOS: one 1024 icon without transparency, and the splash images.
    ios = ROOT / "frontend/projects/mobile/ios/App/App/Assets.xcassets"
    save(on_background(mountain, (1024, 1024), 0.84, CREAM), ios / "AppIcon.appiconset" / "AppIcon-512@2x.png", rgb=True)
    for f in sorted((ios / "Splash.imageset").glob("*.png")):
        w, h = Image.open(f).size
        save(on_background(logo, (w, h), 0.36, CREAM), f, rgb=True)


if __name__ == "__main__":
    main()
