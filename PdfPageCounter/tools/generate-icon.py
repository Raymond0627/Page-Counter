"""Generate the application icon (AppIcon.ico) from the master logo.

Source : Pcount_Logo.png  (square RGBA master, may have transparent padding)
Output : PdfPageCounter.App/Assets/AppIcon.ico

The .ico is a binary build artifact, so this script exists to keep it
reproducible. Re-run after changing the master logo:

    python tools/generate-icon.py

Note the output is consumed in three places:
  * <ApplicationIcon> in PdfPageCounter.App.csproj  -> embedded in the exe
  * AppWindow.SetIcon(...) in MainWindow.xaml.cs   -> runtime window icon
  * SetupIconFile in installer/setup.iss            -> installer + shortcuts
"""

from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent

SOURCE = ROOT / "Pcount_Logo.png"
OUTPUT = ROOT / "PdfPageCounter.App" / "Assets" / "AppIcon.ico"

# Include the non-power-of-two sizes Windows asks for at 125/150/175% DPI;
# omitting them makes the shell upscale from a smaller frame and look blurry.
SIZES = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40),
         (48, 48), (64, 64), (96, 96), (128, 128), (256, 256)]

# Breathing room around the mark, as a fraction of the artwork's long edge.
# Small icons need this most: without it a logo that fills the canvas turns to
# mush at 16px in a taskbar or shortcut.
MARGIN = 0.06

RESAMPLE = Image.Resampling.LANCZOS


def main() -> None:
    if not SOURCE.exists():
        raise SystemExit(f"Source logo not found: {SOURCE}")

    original = Image.open(SOURCE)
    img = original.convert("RGBA")
    print(f"source      : {SOURCE.name}  {original.size}  {original.mode}")

    # 1. Drop any fully transparent border around the artwork.
    bbox = img.getchannel("A").getbbox()
    if bbox is None:
        raise SystemExit("Logo is completely transparent; nothing to convert.")
    if bbox != (0, 0) + img.size:
        print(f"trimmed     : {bbox}  ->  {(bbox[2] - bbox[0], bbox[3] - bbox[1])}")
        img = img.crop(bbox)

    # 2. Re-centre on a square canvas. The artwork is rarely square, so this must
    #    letterbox rather than stretch it, and the .ico format requires square.
    side = max(img.size)
    margin = round(side * MARGIN)
    canvas_px = side + margin * 2
    canvas = Image.new("RGBA", (canvas_px, canvas_px), (0, 0, 0, 0))
    canvas.paste(
        img,
        ((canvas_px - img.width) // 2, (canvas_px - img.height) // 2),
        img,
    )
    print(f"canvas      : {canvas.size}  (margin {margin}px, centred)")

    # 3. Downscale to each target size and pack into a .ico.
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    # bitmap_format='bmp' emits real DIB frames with correct AND masks.
    # The Pillow default (PNG-compressed frames) is handled inconsistently by
    # some shell paths below 256px.
    canvas.save(OUTPUT, format="ICO", sizes=SIZES, bitmap_format="bmp")

    print(f"written     : {OUTPUT.relative_to(ROOT)}  "
          f"({OUTPUT.stat().st_size / 1024:.1f} KB)")
    print(f"sizes       : {', '.join(str(s) for s, _ in SIZES)}")


if __name__ == "__main__":
    main()