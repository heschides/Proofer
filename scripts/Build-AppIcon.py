"""Build Sati's transparent multi-resolution Windows icon from its leaf artwork."""

from pathlib import Path

from PIL import Image, ImageDraw


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
SOURCE_PATH = REPOSITORY_ROOT / "images" / "sati-watercolor-leaf.png"
MASTER_PATH = REPOSITORY_ROOT / "images" / "sati-app-icon.png"
OUTPUT_PATH = REPOSITORY_ROOT / "images" / "sati.ico"
WINDOWS_ICON_SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)
MASTER_SIZE = 1024
LEAF_HEIGHT = 992
LEAF_BODY_BOTTOM = 934
LEAF_STEM_CUTOUT = ((409, 785), (380, LEAF_BODY_BOTTOM), (440, LEAF_BODY_BOTTOM))


def main() -> None:
    with Image.open(SOURCE_PATH) as source:
        # The source includes a long decorative stem. The application mark uses the
        # recognizable leaf body alone so its colored silhouette remains legible at
        # 16-32 px and occupies the same visual area as neighboring taskbar icons.
        leaf = source.convert("RGBA").crop((0, 0, source.width, LEAF_BODY_BOTTOM))
        ImageDraw.Draw(leaf).polygon(LEAF_STEM_CUTOUT, fill=(0, 0, 0, 0))
        leaf.thumbnail((MASTER_SIZE, LEAF_HEIGHT), Image.Resampling.LANCZOS)

        master = Image.new("RGBA", (MASTER_SIZE, MASTER_SIZE), (0, 0, 0, 0))
        position = ((MASTER_SIZE - leaf.width) // 2, (MASTER_SIZE - leaf.height) // 2)
        master.alpha_composite(leaf, position)
        master.save(MASTER_PATH)
        master.save(
            OUTPUT_PATH,
            format="ICO",
            sizes=[(size, size) for size in WINDOWS_ICON_SIZES],
        )

    print(f"Built {MASTER_PATH} and {OUTPUT_PATH} from {SOURCE_PATH}.")


if __name__ == "__main__":
    main()
