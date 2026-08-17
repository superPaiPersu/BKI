from __future__ import annotations

import csv
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[2]
ASSET_ROOT = ROOT / "Assets" / "Resources" / "UI" / "StardewValley-Assets-main"
OUTPUT_ROOT = ASSET_ROOT / "extracted" / "spring_town_smart_objects"
MANIFEST_PATH = ASSET_ROOT / "extracted" / "spring_town_smart_objects_manifest.csv"
PREVIEW_PATH = ASSET_ROOT / "extracted" / "spring_town_smart_objects_preview.png"
PADDING = 2


@dataclass(frozen=True)
class SliceSpec:
    name: str
    box: tuple[int, int, int, int]
    note: str = ""


# This sheet is a packed town/building sheet, not a regular terrain autotile sheet.
# Many sprites touch each other, so these coarse semantic regions are intentionally
# used as split hints before alpha trimming.
SLICES: tuple[SliceSpec, ...] = (
    SliceSpec("top_left_house", (0, 0, 128, 170)),
    SliceSpec("top_blue_house", (96, 0, 259, 178)),
    SliceSpec("top_teal_house", (258, 0, 415, 193)),
    SliceSpec("top_dark_house", (401, 0, 512, 169)),
    SliceSpec("top_small_props_left", (122, 0, 257, 75)),
    SliceSpec("top_small_props_right", (324, 0, 401, 72)),
    SliceSpec("sewer_cover", (253, 0, 302, 55)),
    SliceSpec("dog_and_mail", (207, 38, 274, 88)),
    SliceSpec("bench_bins_mailboxes", (0, 128, 88, 171)),
    SliceSpec("clinic", (0, 146, 101, 330)),
    SliceSpec("pierre_store", (81, 150, 238, 331)),
    SliceSpec("saloon", (225, 142, 403, 352)),
    SliceSpec("white_house", (365, 137, 512, 386)),
    SliceSpec("garden_patch", (0, 314, 66, 354)),
    SliceSpec("community_center", (0, 336, 208, 498)),
    SliceSpec("rv", (188, 320, 369, 417)),
    SliceSpec("large_tree", (194, 377, 284, 516)),
    SliceSpec("bush_cluster", (270, 377, 346, 438)),
    SliceSpec("flower_planters", (287, 421, 390, 479)),
    SliceSpec("round_plaza", (391, 392, 512, 504)),
    SliceSpec("stone_path_and_fence", (0, 352, 88, 500)),
    SliceSpec("yellow_cabin", (0, 518, 102, 642)),
    SliceSpec("bus_stop_sign", (0, 495, 91, 544)),
    SliceSpec("sandbox", (97, 510, 164, 588)),
    SliceSpec("playground_small_props", (129, 497, 215, 553)),
    SliceSpec("playground_slide", (175, 505, 286, 631)),
    SliceSpec("fountain", (222, 454, 349, 635)),
    SliceSpec("water_pump", (345, 461, 382, 528)),
    SliceSpec("bus_front", (379, 508, 455, 624)),
    SliceSpec("bench", (438, 489, 512, 535)),
    SliceSpec("crate_stack", (437, 568, 512, 625)),
    SliceSpec("bus_stop_shelter", (102, 535, 186, 642)),
    SliceSpec("vine_house_left", (0, 672, 98, 803)),
    SliceSpec("pelican_town_left", (79, 672, 194, 803)),
    SliceSpec("clock_building_center", (193, 656, 320, 803)),
    SliceSpec("pelican_town_right", (318, 672, 433, 803)),
    SliceSpec("jojamart_left", (0, 836, 193, 992)),
    SliceSpec("joja_car_and_sign", (192, 848, 320, 992)),
    SliceSpec("jojamart_right", (319, 836, 512, 992)),
)


def find_source() -> Path:
    candidates = sorted(
        ASSET_ROOT.rglob("spring_town..png"),
        key=lambda path: ("Maps" not in path.as_posix(), len(path.as_posix())),
    )

    if not candidates:
        raise FileNotFoundError(f"Could not find spring_town..png under {ASSET_ROOT}")

    return candidates[0]


def alpha_bbox(image: Image.Image) -> tuple[int, int, int, int] | None:
    alpha = image.getchannel("A")
    return alpha.getbbox()


def padded_crop(source: Image.Image, box: tuple[int, int, int, int]) -> tuple[Image.Image, tuple[int, int, int, int]]:
    crop = source.crop(box)
    trim = alpha_bbox(crop)
    if trim is None:
        return crop, box

    left, top, right, bottom = trim
    src_left = max(box[0] + left - PADDING, box[0])
    src_top = max(box[1] + top - PADDING, box[1])
    src_right = min(box[0] + right + PADDING, box[2])
    src_bottom = min(box[1] + bottom + PADDING, box[3])
    trimmed_box = (src_left, src_top, src_right, src_bottom)
    return source.crop(trimmed_box), trimmed_box


def draw_preview(source: Image.Image, records: list[dict[str, object]]) -> None:
    preview = Image.new("RGBA", source.size, (18, 18, 18, 255))
    preview.alpha_composite(source)
    draw = ImageDraw.Draw(preview)

    colors = (
        (255, 64, 64, 230),
        (64, 220, 255, 230),
        (255, 220, 64, 230),
        (120, 255, 120, 230),
        (255, 120, 255, 230),
    )

    for index, record in enumerate(records, start=1):
        x, y, w, h = (
            int(record["source_x"]),
            int(record["source_y"]),
            int(record["width"]),
            int(record["height"]),
        )
        color = colors[(index - 1) % len(colors)]
        draw.rectangle((x, y, x + w - 1, y + h - 1), outline=color, width=2)
        label = str(index)
        text_box = draw.textbbox((0, 0), label)
        label_w = text_box[2] - text_box[0] + 4
        label_h = text_box[3] - text_box[1] + 4
        draw.rectangle((x, y, x + label_w, y + label_h), fill=(0, 0, 0, 190))
        draw.text((x + 2, y + 1), label, fill=(255, 255, 255, 255))

    PREVIEW_PATH.parent.mkdir(parents=True, exist_ok=True)
    preview.save(PREVIEW_PATH)


def main() -> None:
    source_path = find_source()
    source = Image.open(source_path).convert("RGBA")
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)

    records: list[dict[str, object]] = []
    for index, spec in enumerate(SLICES, start=1):
        image, final_box = padded_crop(source, spec.box)
        x0, y0, x1, y1 = final_box
        filename = f"{index:03d}_{spec.name}.png"
        image.save(OUTPUT_ROOT / filename)
        records.append(
            {
                "index": index,
                "name": spec.name,
                "file": filename,
                "source_x": x0,
                "source_y": y0,
                "width": x1 - x0,
                "height": y1 - y0,
                "hint_x": spec.box[0],
                "hint_y": spec.box[1],
                "hint_width": spec.box[2] - spec.box[0],
                "hint_height": spec.box[3] - spec.box[1],
                "note": spec.note,
            }
        )

    with MANIFEST_PATH.open("w", newline="", encoding="utf-8-sig") as file:
        writer = csv.DictWriter(
            file,
            fieldnames=(
                "index",
                "name",
                "file",
                "source_x",
                "source_y",
                "width",
                "height",
                "hint_x",
                "hint_y",
                "hint_width",
                "hint_height",
                "note",
            ),
        )
        writer.writeheader()
        writer.writerows(records)

    draw_preview(source, records)

    print(f"Source: {source_path}")
    print(f"Sprites: {OUTPUT_ROOT}")
    print(f"Manifest: {MANIFEST_PATH}")
    print(f"Preview: {PREVIEW_PATH}")
    print(f"Count: {len(records)}")


if __name__ == "__main__":
    main()
