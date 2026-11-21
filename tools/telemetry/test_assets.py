"""Targeted catalogue rebuild regression using isolated author exports."""
import json
from pathlib import Path
import tempfile

from PIL import Image

from build_assets import build


with tempfile.TemporaryDirectory() as temporary:
    root = Path(temporary)
    export = root / "export"
    export.mkdir()
    web = root / "web"

    def write_export(name, cost):
        (export / "game_info.json").write_text(json.dumps({
            "characters": [{"id": "CHARACTER.EXAMPLE", "name": "Example"}],
            "relics": [{"id": "RELIC.EXAMPLE", "name": name, "sourceMod": "Example", "sourceVersion": cost}],
        }), encoding="utf-8")
        (export / "cards.json").write_text(json.dumps({"cards": [{
            "id": "CARD.EXAMPLE", "name": name, "cost": cost, "rarity": "普通", "type": "技能",
            "playerId": "CHARACTER.EXAMPLE", "sourceMod": "Example", "sourceVersion": cost,
            "images": {"base": "absent.png"},
        }]}), encoding="utf-8")

    Image.new("RGB", (20, 20), "blue").save(export / "absent.png")
    write_export("old", "1")
    build([export], web)
    (export / "absent.png").unlink()
    write_export("updated", "2")
    build([export], web)
    cards = json.loads((web / "catalog.json").read_text(encoding="utf-8"))
    relics = json.loads((web / "relics.json").read_text(encoding="utf-8"))
    assert cards["CARD.EXAMPLE"] == cards["EXAMPLE"]
    assert cards["EXAMPLE"]["name"] == "updated" and cards["EXAMPLE"]["cost"] == 2
    assert (web / cards["EXAMPLE"]["image"]).is_file()
    assert relics["RELIC.EXAMPLE"] == relics["EXAMPLE"]
    assert relics["EXAMPLE"]["source_version"] == "2"

    other = root / "other"
    other.mkdir()
    (other / "game_info.json").write_text((export / "game_info.json").read_text(encoding="utf-8"), encoding="utf-8")
    competing = json.loads((export / "cards.json").read_text(encoding="utf-8"))
    competing["cards"][0].update(id="Other.CARD.EXAMPLE", name="other source")
    (other / "cards.json").write_text(json.dumps(competing), encoding="utf-8")
    build([export, other], web)
    cards = json.loads((web / "catalog.json").read_text(encoding="utf-8"))
    assert cards["EXAMPLE"]["name"] == "updated"
    assert cards["Other.CARD.EXAMPLE"]["name"] == "other source"
    # Synchronizing another Mod must leave earlier sources and their aliases intact.
    competing["cards"][0].update(id="CARD.UNRELATED", sourceMod="Other", cost="X", rarity="远古")
    (other / "cards.json").write_text(json.dumps(competing), encoding="utf-8")
    build([export, other], web, source_mods=["Other"])
    cards = json.loads((web / "catalog.json").read_text(encoding="utf-8"))
    assert cards["EXAMPLE"]["name"] == "updated"
    assert cards["UNRELATED"]["cost"] == -1 and cards["UNRELATED"]["rarity"] == "先古"
    print("PASS targeted catalogue rebuild, retained images and alias precedence")
