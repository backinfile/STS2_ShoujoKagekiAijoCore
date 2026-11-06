"""Build Web catalogues from author-generated Core exports; requires Pillow for thumbnails."""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image


def build(exports, web):
    web.mkdir(parents=True, exist_ok=True)
    assets = web / "assets"
    assets.mkdir(exist_ok=True)
    def read(name):
        path = web / name
        return json.loads(path.read_text(encoding="utf-8-sig")) if path.exists() else {}
    catalog, relics, characters = read("catalog.json"), read("relics.json"), read("characters.json")
    catalog.update(read("native_catalog.json"))
    card_alias_sources, relic_alias_sources = {}, {}
    for folder in exports:
        game = json.loads((folder / "game_info.json").read_text(encoding="utf-8-sig"))
        for character in game.get("characters", []):
            characters[character["id"].split(".")[-1].upper()] = character["name"]
        for relic in game.get("relics", []):
            relics[relic["id"]] = {"name": relic["name"], "source_mod": relic.get("sourceMod", "unknown"), "source_version": relic.get("sourceVersion", "unknown")}
            alias = relic["id"].split(".")[-1].upper()
            if relic_alias_sources.setdefault(alias, relic["id"]) == relic["id"]:
                relics[alias] = relics[relic["id"]]
        manifest = json.loads((folder / "cards.json").read_text(encoding="utf-8-sig"))
        for card in manifest["cards"]:
            cid = card["id"]
            owner = (card.get("playerId") or "unknown").split(".")[-1].lower()
            item = {"name": card["name"], "cost": card.get("cost"), "rarity": card.get("rarity", "未知"),
                    "type": card.get("type", "未知"), "owner": owner, "owner_name": characters.get(owner.upper(), owner),
                    "full_id": cid, "source_mod": card.get("sourceMod", "unknown"), "source_version": card.get("sourceVersion", "unknown")}
            image = (folder / card["images"]["base"]).resolve()
            if not image.is_relative_to(folder.resolve()):
                raise ValueError("Image path escapes author export directory")
            if image.is_file():
                destination = assets / ("model_" + hashlib.sha256(cid.encode()).hexdigest()[:24] + ".webp")
                with Image.open(image) as bitmap:
                    bitmap.thumbnail((420, 600))
                    bitmap.save(destination, "WEBP", quality=82)
                item["image"] = "assets/" + destination.name
            catalog[cid] = item
            alias = cid.split(".")[-1].upper()
            if card_alias_sources.setdefault(alias, cid) == cid:
                catalog[alias] = item
    for name, value in (("catalog.json", catalog), ("relics.json", relics), ("characters.json", characters)):
        (web / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return len(catalog)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--export", dest="exports", type=Path, action="append", required=True)
    parser.add_argument("--web", type=Path, default=Path(__file__).resolve().parents[2] / "web/telemetry")
    args = parser.parse_args()
    print("Built", build(args.exports, args.web), "catalogue entries")
