"""Build Sati's multi-resolution Windows icon from its master PNG artwork."""

from pathlib import Path

from PIL import Image


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
SOURCE_PATH = REPOSITORY_ROOT / "images" / "sati-app-icon.png"
OUTPUT_PATH = REPOSITORY_ROOT / "images" / "sati.ico"
WINDOWS_ICON_SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def main() -> None:
    with Image.open(SOURCE_PATH) as source:
        if source.width != source.height:
            raise ValueError("The application icon master artwork must be square.")
        if source.width < max(WINDOWS_ICON_SIZES):
            raise ValueError("The application icon master artwork must be at least 256 pixels square.")

        source.convert("RGBA").save(
            OUTPUT_PATH,
            format="ICO",
            sizes=[(size, size) for size in WINDOWS_ICON_SIZES],
        )

    print(f"Built {OUTPUT_PATH} from {SOURCE_PATH}.")


if __name__ == "__main__":
    main()
