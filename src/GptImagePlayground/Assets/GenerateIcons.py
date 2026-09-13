"""Regenerate Windows branding assets from AppIcon.svg.

Prerequisites: python -m pip install resvg-py==0.5.0 Pillow==11.3.0
Run: python .\\src\\GptImagePlayground\\Assets\\GenerateIcons.py
Optional: --preview <path> writes a light/dark contact sheet outside the app.
"""

import argparse
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageDraw
import resvg_py


ASSETS = Path(__file__).resolve().parent
ICON_SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)
LOGOS = {
    "LockScreenLogo.scale-200.png": (48, 48, 40),
    "SplashScreen.scale-200.png": (1240, 600, 256),
    "Square150x150Logo.scale-200.png": (300, 300, 216),
    "Square44x44Logo.scale-200.png": (88, 88, 88),
    "StoreLogo.png": (50, 50, 50),
    "Wide310x150Logo.scale-200.png": (620, 300, 216),
}


def render(size: int) -> Image.Image:
    data = resvg_py.svg_to_bytes(
        svg_path=str(ASSETS / "AppIcon.svg"),
        width=size * 4,
        height=size * 4,
        skip_system_fonts=True,
    )
    with Image.open(BytesIO(data)) as image:
        return image.convert("RGBA").resize((size, size), Image.Resampling.LANCZOS)


def save_logo(name: str, width: int, height: int, size: int) -> None:
    canvas = Image.new("RGBA", (width, height))
    canvas.alpha_composite(render(size), ((width - size) // 2, (height - size) // 2))
    canvas.save(ASSETS / name)
    with Image.open(ASSETS / name) as saved:
        if saved.size != (width, height) or saved.mode != "RGBA":
            raise RuntimeError(f"Incorrect PNG dimensions or mode: {name}")
        bounds = saved.getchannel("A").point(lambda alpha: 255 if alpha >= 16 else 0).getbbox()
        if not bounds or not (0 < bounds[0] < bounds[2] < width and 0 < bounds[1] < bounds[3] < height):
            raise RuntimeError(f"Empty or clipped icon: {name}")


def save_preview(path: Path) -> None:
    sizes = (16, 24, 32, 48, 128)
    sheet = Image.new("RGB", (600, 320))
    draw = ImageDraw.Draw(sheet)
    for row, (background, foreground) in enumerate(
        (("#F6F8FC", "#233452"), ("#192332", "#D8E6F8"))
    ):
        draw.rectangle((0, row * 160, 600, (row + 1) * 160), fill=background)
        for column, size in enumerate(sizes):
            icon = render(size)
            x = column * 116 + (116 - size) // 2 + 10
            y = row * 160 + (132 - size) // 2
            sheet.paste(icon, (x, y), icon)
            draw.text((column * 116 + 48, row * 160 + 138), str(size), fill=foreground)
    sheet.save(path)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--preview", type=Path)
    args = parser.parse_args()

    for name, dimensions in LOGOS.items():
        save_logo(name, *dimensions)
    for size in (16, 24, 32, 48, 256):
        for suffix in ("", "_altform-unplated", "_altform-lightunplated"):
            save_logo(f"Square44x44Logo.targetsize-{size}{suffix}.png", size, size, size)

    icon = render(256)
    icon.save(
        ASSETS / "AppIcon.ico",
        sizes=[(size, size) for size in ICON_SIZES],
        append_images=[render(size) for size in ICON_SIZES if size != 256],
    )
    with Image.open(ASSETS / "AppIcon.ico") as saved:
        if saved.ico.sizes() != {(size, size) for size in ICON_SIZES}:
            raise RuntimeError("ICO does not contain every required resolution")
    if args.preview:
        save_preview(args.preview)
    print(f"Generated and verified {len(LOGOS) + 15} PNG logos and {len(ICON_SIZES)} ICO sizes.")


if __name__ == "__main__":
    main()
