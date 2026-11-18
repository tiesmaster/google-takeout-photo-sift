"""Create the tiny synthetic test images in tests/fixtures (no real photos).

Needs Pillow, pillow-heif and exiftool on PATH. The output is checked in; re-run only to change it.

Usage:
  python tools/make_fixtures.py
"""

import subprocess
from pathlib import Path

import pillow_heif
from PIL import Image

OUT = Path(__file__).resolve().parent.parent / "tests" / "fixtures"


def gradient(width=32, height=16):
    """A landscape image whose top-left corner is red, so a test can tell the rotation apart."""
    im = Image.new("RGB", (width, height), (40, 40, 200))
    for x in range(width // 4):
        for y in range(height // 4):
            im.putpixel((x, y), (220, 30, 30))
    return im


def exiftool(*args):
    subprocess.run(["exiftool", "-q", "-overwrite_original", *args], check=True)


def main():
    OUT.mkdir(parents=True, exist_ok=True)

    plain = OUT / "no-exif.jpg"
    gradient().save(plain, quality=90)

    for orientation in (1, 3, 5, 6, 8):
        path = OUT / f"orientation-{orientation}.jpg"
        gradient().save(path, quality=90)
        exiftool(f"-Orientation#={orientation}", "-Make=Fixture", "-Model=Synthetic", str(path))

    png = OUT / "plain.png"
    gradient().save(png)

    pillow_heif.register_heif_opener()
    heic = OUT / "orientation-6.heic"
    gradient().save(heic, quality=90)
    exiftool("-Orientation#=6", str(heic))

    for path in sorted(OUT.iterdir()):
        print(f"{path.name}: {path.stat().st_size} bytes")


if __name__ == "__main__":
    main()
