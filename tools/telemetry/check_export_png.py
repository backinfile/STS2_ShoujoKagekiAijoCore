"""Check a game-exported PNG for visible, non-uniform content. Requires Pillow."""
import argparse
import json
from PIL import Image


def check(path):
    with Image.open(path) as source, source.convert("RGBA") as image:
        with image.getchannel("A") as alpha:
            visible = image.width * image.height - alpha.histogram()[0]
        colors = image.getcolors(image.width * image.height)
        result = {"width": image.width, "height": image.height, "visible_pixels": visible, "distinct_colors": len(colors)}
    print(json.dumps(result))
    if visible == 0:
        raise ValueError("export_png_has_no_visible_pixels")
    if len(colors) <= 1:
        raise ValueError("export_png_has_no_distinct_content")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("png")
    args = parser.parse_args()
    check(args.png)
