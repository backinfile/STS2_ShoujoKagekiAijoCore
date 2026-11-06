"""Read native metadata/PCKs; generate web thumbnails without starting the game."""
import argparse
import io
import json
from pathlib import Path
import re
import struct
import sys

from PIL import Image

from pck_reader import pck_index, pck_read

WEB = Path(__file__).resolve().parents[2] / "web/telemetry"
OWNERS = {"ironclad": "铁甲战士", "silent": "静默猎手", "defect": "故障机器人",
          "regent": "储君", "necrobinder": "亡灵契约师", "colorless": "无色",
          "curse": "诅咒", "status": "状态", "event": "特殊", "token": "衍生", "quest": "任务"}
TYPES = {"Attack": "攻击", "Skill": "技能", "Power": "能力", "Curse": "诅咒", "Status": "状态", "Quest": "任务"}
RARITIES = {"Basic": "基础", "Common": "普通", "Uncommon": "罕见", "Rare": "稀有", "Ancient": "先古",
            "Token": "衍生", "Curse": "诅咒", "Status": "状态", "Event": "特殊", "Quest": "任务", "None": "特殊"}


def texture_image(data):
    # Native card portraits use the lossless PNG/WebP payload in a GST2 container.
    for signature in (b"RIFF", b"\x89PNG\r\n\x1a\n"):
        start = data.find(signature)
        if start >= 0:
            image_bytes = data[start:]
            if signature == b"RIFF":
                image_bytes = image_bytes[:struct.unpack_from("<I", image_bytes, 4)[0] + 8]
            return Image.open(io.BytesIO(image_bytes))
    raise ValueError("Native portrait does not contain a supported lossless image")


def build(games, metadata):
    cards, relics = {}, {}
    missing = []
    (WEB / "assets").mkdir(exist_ok=True)
    for game, definitions in zip(games, metadata):
        pck = game / "SlayTheSpire2.pck"
        index = pck_index(pck)
        titles = json.loads(pck_read(pck, index["localization/zhs/cards.json"]).decode("utf-8-sig"))
        relic_titles = json.loads(pck_read(pck, index["localization/zhs/relics.json"]).decode("utf-8-sig"))
        for key, title in relic_titles.items():
            if key.endswith(".title"):
                relics.setdefault(key.removesuffix(".title"), {"name": title})
        definitions = json.loads(definitions.read_text(encoding="utf-8-sig"))
        for cid, meta in sorted(definitions.items()):
            if cid in cards:
                continue
            portrait = {"MAD_SCIENCE": "mad_science_attack", "DEPRECATED_CARD": "beta"}.get(cid, cid.lower())
            options = [name for name in index if name.startswith("images/packed/card_portraits/") and
                       name.endswith("/" + portrait + ".png.import")]
            options.sort(key=lambda name: ("/beta/" in name, name))
            owner = next((pool.lower() for pool in meta["pools"] if pool.lower() in OWNERS), "unknown")
            if options:
                folder = options[0].split("/")[3]
                if folder in OWNERS:
                    owner = folder
            item = {"name": titles.get(cid + ".title", meta["name"]), "cost": meta["cost"],
                    "type": TYPES.get(meta["type"], "未知"), "rarity": RARITIES.get(meta["rarity"], "特殊"),
                    "owner": owner, "owner_name": OWNERS.get(owner, "其他")}
            if options:
                remap = pck_read(pck, index[options[0]]).decode("utf-8")
                path = re.search(r'path="res://([^"]+)"', remap)
                if path and path[1] in index:
                    with texture_image(pck_read(pck, index[path[1]])) as image:
                        image.thumbnail((420, 300))
                        destination = WEB / "assets" / ("native_" + cid.lower() + ".webp")
                        image.convert("RGB").save(destination, "WEBP", quality=78)
                    item["image"] = "assets/" + destination.name
            if "image" not in item:
                missing.append(cid)
            cards[cid] = item
    (WEB / "native_catalog.json").write_text(json.dumps(cards, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (WEB / "native_relics.json").write_text(json.dumps(relics, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"cards": len(cards), "relics": len(relics), "missing_portraits": missing}, ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game", type=Path, action="append", required=True)
    parser.add_argument("--metadata", type=Path, action="append", required=True)
    args = parser.parse_args()
    if len(args.game) != len(args.metadata):
        parser.error("Provide one --metadata for each --game, in the same priority order")
    build(args.game, args.metadata)
