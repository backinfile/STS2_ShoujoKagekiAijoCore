import copy
import hashlib
import json
import gzip
from pathlib import Path
import tempfile
import threading
import time
import unittest
from unittest.mock import patch
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from app import CapacityError, FACTS_VERSION, Invalid, Server, Store, derive, parse_history


def fixture():
    card = lambda cid: {"id": "CARD." + cid}
    pid = 76561199000000001
    return {"schema_version": 1, "platform_type": "steam", "game_mode": "standard",
            "win": True, "was_abandoned": False, "seed": "COLLECTOR-SELFTEST",
            "start_time": int(time.time()) - 1000, "run_time": 900, "ascension": 10,
            "build_id": "collector-test", "players": [{"id": pid, "character": "CHARACTER.KAREN",
            "deck": [card("KAREN_STRIKE"), card("KAREN_STRIKE"), card("KAREN_NEW_DAY")],
            "karen_shine_exhaust_cards": [card("KAREN_DROP_FUEL")]}],
            "map_point_history": [[{"map_point_type": "monster", "rooms": [{"room_type": "monster"}],
            "player_stats": [{"player_id": pid, "current_hp": 50, "max_hp": 72,
            "cards_gained": [card("KAREN_NEW_DAY")], "card_choices": [
                {"card": card("KAREN_NEW_DAY"), "was_picked": True},
                {"card": card("KAREN_BASEBALL"), "was_picked": False},
                {"card": card("KAREN_WAKE_UP")}]}]}]]}


class CollectorTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.store = Store(Path(self.tmp.name) / "history.sqlite3")
        self.data = fixture()

    def tearDown(self):
        self.tmp.cleanup()

    def test_ingest_duplicates_conflicts_and_large_ids(self):
        status, result = self.store.upload(self.data)
        self.assertEqual(status, 201)
        self.assertEqual(result["quality"]["missing_was_picked"], 1)
        self.assertEqual(self.store.upload(self.data)[0], 200)
        changed = copy.deepcopy(self.data)
        changed["run_time"] += 1
        self.assertEqual(self.store.upload(changed)[0], 409)
        detail = self.store.detail(result["run_id"])
        self.assertEqual(json.loads(detail["facts"])["seats"][0]["id"], "76561199000000001")
        self.assertEqual(self.store.runs({})["total"], 1)

    def test_native_selection_rate_excludes_only_unknown_results(self):
        self.store.upload(self.data)
        stats = self.store.stats({})
        cards = {c["id"]: c for c in stats["cards"]}
        self.assertEqual(cards["KAREN_STRIKE"]["held"], 1)
        self.assertEqual(cards["KAREN_DROP_FUEL"]["held"], 1)
        self.assertEqual(cards["KAREN_NEW_DAY"]["recorded_picked"], 1)
        self.assertEqual(cards["KAREN_BASEBALL"]["recorded_picked"], 0)
        self.assertEqual(cards["KAREN_WAKE_UP"]["unknown_picked"], 1)
        self.assertEqual(cards["KAREN_NEW_DAY"]["pick_rate"], 1)
        self.assertEqual(cards["KAREN_BASEBALL"]["pick_rate"], 0)
        self.assertIsNone(cards["KAREN_WAKE_UP"]["pick_rate"])
        self.assertEqual(stats["pick_policy"], "native_known_choices_all_sources")
        self.assertEqual(cards["KAREN_DROP_FUEL"]["win_rate"], 1)

    def test_repeated_native_choices_count_each_known_record(self):
        choices = self.data["map_point_history"][0][0]["player_stats"][0]["card_choices"]
        choices.extend([{"card": {"id": "CARD.KAREN_NEW_DAY"}, "was_picked": False},
                        {"card": {"id": "CARD.KAREN_NEW_DAY"}}])
        self.store.upload(self.data)
        cards = {c["id"]: c for c in self.store.stats({})["cards"]}
        self.assertEqual(cards["KAREN_NEW_DAY"]["pick_rate"], .5)
        self.assertEqual(cards["KAREN_NEW_DAY"]["offered"], 2)
        self.assertEqual(cards["KAREN_NEW_DAY"]["recorded_offers"], 3)
        self.assertEqual(cards["KAREN_BASEBALL"]["pick_rate"], 0)
        self.assertIsNone(cards["KAREN_WAKE_UP"]["pick_rate"])

    def test_early_death_preserved_and_abandon_only_excluded_in_stats(self):
        self.data["win"] = False
        self.store.upload(self.data)
        self.assertEqual(self.store.stats({})["seats"], 1)
        self.data["seed"] += "-abandon"
        self.data["was_abandoned"] = True
        self.store.upload(self.data)
        self.assertEqual(self.store.runs({})["total"], 2)
        self.assertEqual(self.store.stats({})["seats"], 1)
        self.assertEqual(self.store.stats({"sample": ["all"]})["seats"], 2)

    def test_coop_uses_karen_seats_not_team_size(self):
        second = copy.deepcopy(self.data["players"][0])
        second["id"] += 1
        self.data["players"].append(second)
        self.store.upload(self.data)
        self.assertEqual(self.store.stats({})["seats"], 2)
        self.assertEqual(self.store.runs({})["total"], 1)

    def test_character_filter_matches_runs_once_and_stats_by_seat(self):
        self.store.upload(self.data)
        other = copy.deepcopy(self.data)
        other["seed"] += "-ironclad"
        other["players"][0]["character"] = "CHARACTER.IRONCLAD"
        self.store.upload(other)
        coop = copy.deepcopy(self.data)
        coop["seed"] += "-coop"
        for offset in (1, 2):
            seat = copy.deepcopy(coop["players"][0])
            seat["id"] += offset
            seat["character"] = "CHARACTER.SILENT"
            coop["players"].append(seat)
        _, result = self.store.upload(coop)
        for character in ("SILENT", "silent", "CHARACTER.SILENT"):
            runs = self.store.runs({"character": [character]})
            self.assertEqual(runs["total"], 1)
            self.assertEqual([r["id"] for r in runs["items"]], [result["run_id"]])
        self.assertEqual(self.store.runs({"character": ["KAREN"]})["total"], 2)
        self.assertEqual(self.store.runs({"character": ["all"]})["total"], 3)
        self.assertEqual(self.store.runs({"character": ["SILENT"], "players": ["1"]})["total"], 0)
        self.assertEqual(self.store.runs({"character": ["SILENT"], "outcome": ["death"]})["total"], 0)
        for character in ("SILENT", "silent", "CHARACTER.SILENT"):
            stats = self.store.stats({"character": [character]})
            self.assertEqual((stats["runs"], stats["seats"], stats["character"]), (1, 2, "SILENT"))
            self.assertEqual(stats["characters"], ["IRONCLAD", "KAREN", "SILENT"])
        self.assertEqual(self.store.stats({"character": ["KAREN"]})["seats"], 2)
        self.assertEqual(self.store.stats({})["seats"], 5)
        empty = self.store.stats({"character": ["WATCHER"]})
        self.assertEqual((empty["runs"], empty["seats"], empty["cards"], empty["latest_received"]), (0, 0, [], None))

    def test_character_stats_isolate_cards_choices_and_denominators(self):
        self.data["players"].append({"id": 42, "character": "CHARACTER.SILENT",
                                     "deck": [{"id": "CARD.NEUTRALIZE"}, {"id": "CARD.COORDINATE"}]})
        self.data["map_point_history"][0][0]["player_stats"].append({"player_id": 42, "card_choices": [
            {"card": {"id": "CARD.COORDINATE"}, "was_picked": True},
            {"card": {"id": "CARD.COORDINATE"}, "was_picked": False}]})
        self.store.upload(self.data)
        lost = copy.deepcopy(self.data)
        lost["seed"] += "-lost"
        lost["win"] = False
        lost["players"] = [{"id": 42, "character": "CHARACTER.SILENT", "deck": [{"id": "CARD.NEUTRALIZE"}]}]
        lost["map_point_history"] = []
        self.store.upload(lost)
        stats = self.store.stats({"character": ["SILENT"], "outcome": ["win"]})
        self.assertEqual((stats["runs"], stats["raw_seats"], stats["seats"], stats["wins"]), (2, 2, 2, 1))
        cards = {c["id"]: c for c in stats["cards"]}
        self.assertEqual(set(cards), {"NEUTRALIZE", "COORDINATE"})
        self.assertEqual(cards["NEUTRALIZE"]["win_rate"], .5)
        self.assertEqual(cards["COORDINATE"]["pick_rate"], .5)
        self.assertEqual(cards["COORDINATE"]["hold_rate"], .5)
        self.assertEqual(stats["missing_picked"], 0)
        self.assertNotIn("COORDINATE", {c["id"] for c in self.store.stats({"character": ["KAREN"]})["cards"]})
        self.assertEqual(self.store.stats({"players": ["2"], "character": ["SILENT"]})["seats"], 1)
        lost["seed"] += "-abandon"
        lost["was_abandoned"] = True
        self.store.upload(lost)
        stats = self.store.stats({"character": ["SILENT"]})
        self.assertEqual((stats["runs"], stats["raw_seats"], stats["seats"], stats["excluded"]), (3, 3, 2, 1))
        self.assertEqual(self.store.stats({"character": ["SILENT"], "sample": ["all"]})["seats"], 3)

    def test_run_character_filter_applies_before_pagination(self):
        self.store.upload(self.data)
        for index in range(22):
            data = copy.deepcopy(self.data)
            data["seed"] += f"-silent-{index}"
            data["players"][0]["character"] = "CHARACTER.SILENT"
            self.assertEqual(self.store.upload(data)[0], 201)
        first = self.store.runs({"character": ["SILENT"]})
        second = self.store.runs({"character": ["SILENT"], "page": ["2"]})
        self.assertEqual(first["total"], 22)
        self.assertEqual(len(first["items"]), 20)
        self.assertEqual(len(second["items"]), 2)
        self.assertEqual(second["page"], 2)
        self.assertFalse({r["id"] for r in first["items"]} & {r["id"] for r in second["items"]})
        self.assertTrue(all(r["players"][0]["character"] == "SILENT" for r in first["items"] + second["items"]))
        self.assertEqual(self.store.runs({"character": ["SILENT"], "page": ["99"]}), second)
        self.assertEqual(self.store.runs({})["total"], 23)

    def test_run_character_filter_supports_models_and_validates_input(self):
        self.data["players"][0]["character"] = "CHARACTER.CUSTOM_CLASS"
        self.store.upload(self.data)
        self.assertEqual(self.store.runs({"character": ["CUSTOM_CLASS"]})["total"], 1)
        self.assertEqual(self.store.stats({})["characters"], ["CUSTOM_CLASS"])
        self.assertEqual(self.store.runs({"character": ["WATCHER"]})["total"], 0)
        for value in ("SILENT' OR 1=1 --", "x" * 181, "", "[]"):
            with self.assertRaises(Invalid):
                self.store.runs({"character": [value]})
            with self.assertRaises(Invalid):
                self.store.stats({"character": [value]})

    def test_parser_strips_shine_only_and_rejects_duplicate_keys(self):
        counters = [prefix + field for prefix in ("", "CardModel_")
                    for field in ("karen_shine_current", "karen_shine_max", "core_shine_current", "core_shine_max")]
        card = self.data["players"][0]["deck"][0]
        card["props"] = {"ints": [{"name": name, "value": 3} for name in counters] + [{"name": "other", "value": 9}]}
        card.update({name: 3 for name in counters})
        card["core_shine_exhaust_count"] = 2
        clean = parse_history(json.dumps(self.data).encode())
        self.assertEqual(clean["players"][0]["deck"][0]["props"]["ints"], [{"name": "other", "value": 9}])
        self.assertFalse(set(counters) & clean["players"][0]["deck"][0].keys())
        self.assertEqual(clean["players"][0]["deck"][0]["core_shine_exhaust_count"], 2)
        status, result = self.store.upload(clean)
        saved = json.loads(gzip.decompress(self.store.detail(result["run_id"])["payload"]))
        self.assertEqual(status, 201)
        self.assertEqual(saved, clean)
        for body in (b'{"a":1,"a":2}', b'{"x":NaN}', b'not json'):
            with self.assertRaises(Invalid):
                parse_history(body)

    def test_backup_restores_records(self):
        self.store.upload(self.data)
        backup = Path(self.tmp.name) / "backup.sqlite3"
        self.store.backup(backup)
        self.assertEqual(Store(backup).runs({})["total"], 1)

    def test_modified_and_nonstandard_decks_do_not_invent_starters(self):
        for mode, modifiers in [("custom", []), ("daily", []), ("standard", ["MODIFIER.DRAFT"])]:
            data = copy.deepcopy(self.data)
            data["seed"] += mode + str(modifiers)
            data["game_mode"], data["modifiers"] = mode, modifiers
            data["players"][0]["deck"] = [{"id": "CARD.KAREN_NEW_DAY"}]
            facts = derive(data)
            self.assertNotIn("KAREN_STRIKE", facts["seats"][0]["held"])
            self.assertNotIn("KAREN_DEFEND", facts["seats"][0]["held"])
        self.store.upload(self.data)
        self.assertIn("KAREN_DEFEND", {c["id"] for c in self.store.stats({})["cards"]})

    def test_invalid_display_structures_are_rejected_before_storing(self):
        mutations = [lambda d: d["players"][0].update(relics=[None]),
                     lambda d: d["map_point_history"][0][0].update(rooms=[None]),
                     lambda d: d["map_point_history"][0][0]["player_stats"][0].update(rest_site_choices="rest")]
        for mutate in mutations:
            data = copy.deepcopy(self.data)
            mutate(data)
            with self.assertRaises(Invalid):
                self.store.upload(data)
        self.assertEqual(self.store.runs({})["total"], 0)

    def test_exact_raw_duplicate_conflict_and_verified_archive(self):
        self.data["players"][0]["deck"][0]["karen_shine_current"] = 7
        original = ("\ufeff" + json.dumps(self.data, indent=2) + "\n").encode("utf-8")
        status, result = self.store.upload(parse_history(original), original_body=original)
        self.assertEqual(status, 201)
        rid = result["run_id"]
        self.assertEqual(self.store.original(rid), original)
        saved = json.loads(gzip.decompress(self.store.detail(rid)["payload"]))
        self.assertNotIn("karen_shine_current", saved["players"][0]["deck"][0])
        self.assertEqual(self.store.upload(parse_history(original), original_body=original)[0], 200)
        changed = copy.deepcopy(self.data)
        changed["run_time"] += 1
        conflict = json.dumps(changed).encode()
        self.assertEqual(self.store.upload(parse_history(conflict), original_body=conflict)[0], 409)
        self.assertEqual(self.store.archive_submissions(before=int(time.time()) + 86400), 2)
        self.assertEqual(self.store.original(rid), original)
        self.assertEqual(self.store.archive_submissions(before=int(time.time()) + 86400), 0)
        self.assertEqual(len(list(self.store.raw.root.glob("bundles/*.tar.gz"))), 1)
        self.assertEqual(list(self.store.raw.root.glob("pending/*/*.json")), [])
        with self.store.connect() as db:
            row = db.execute("SELECT * FROM submissions WHERE status=409").fetchone()
        self.assertEqual(self.store.raw.read(row), conflict)

    def test_archive_keeps_today_loose_and_failure_preserves_originals(self):
        status, result = self.store.upload(self.data)
        self.assertEqual(self.store.archive_submissions(), 0)
        with self.store.connect() as db:
            row = db.execute("SELECT * FROM submissions").fetchone()
        path = self.store.raw.loose_path(row)
        body = path.read_bytes()
        path.write_bytes(b'corrupt')
        with self.assertRaises(OSError):
            self.store.archive_submissions(before=int(time.time()) + 86400)
        self.assertTrue(path.exists())
        self.assertEqual(list(self.store.raw.root.glob("bundles/*.tar.gz")), [])
        path.write_bytes(body)
        self.assertEqual(self.store.original(result["run_id"]), body)

    def test_raw_budget_never_acknowledges_an_unpreserved_submission(self):
        self.store.raw_limit = 1
        with self.assertRaises(CapacityError):
            self.store.upload(self.data)
        self.assertEqual(self.store.runs({})["total"], 0)

    def test_archive_recovers_index_published_before_loose_cleanup(self):
        self.store.upload(self.data)
        with self.store.connect() as db:
            row = db.execute("SELECT * FROM submissions").fetchone()
        archive = self.store.raw.pack("fixture", [row])
        with self.store.connect() as db:
            db.execute("UPDATE submissions SET archive=?", (archive,))
        self.assertTrue(self.store.raw.loose_path(row).exists())
        self.assertEqual(self.store.archive_submissions(), 0)
        self.assertFalse(self.store.raw.loose_path(row).exists())

    def test_raw_download_survives_archive_publication_after_location_lookup(self):
        body = json.dumps(self.data).encode()
        _, result = self.store.upload(self.data, original_body=body)
        read = self.store.raw.read
        def read_after_archive(row):
            if not row["archive"]:
                with patch.object(self.store.raw, "read", side_effect=read):
                    self.store.archive_submissions(before=int(time.time()) + 86400)
            return read(row)
        with patch.object(self.store.raw, "read", side_effect=read_after_archive):
            self.assertEqual(self.store.original(result["run_id"]), body)

    def test_archive_drains_multiple_bounded_batches(self):
        body = json.dumps(self.data).encode()
        for suffix in range(257):
            self.store.upload(self.data, original_body=body + b" " * suffix)
        self.assertEqual(self.store.archive_submissions(before=int(time.time()) + 86400), 257)
        self.assertEqual(len(list(self.store.raw.root.glob("bundles/*.tar.gz"))), 2)
        self.assertEqual(list(self.store.raw.root.glob("pending/*/*.json")), [])

    def test_legacy_rebuild_retains_available_json_and_degrades_old_display_fields(self):
        status, result = self.store.upload(self.data)
        legacy = copy.deepcopy(self.data)
        legacy["players"][0]["relics"] = [None]
        body = json.dumps(legacy).encode()
        with self.store.connect() as db:
            db.execute("UPDATE runs SET payload=?", (gzip.compress(body),))
            db.execute("DELETE FROM submissions")
        backup = Path(self.tmp.name) / "legacy-before.sqlite3"
        self.assertEqual(self.store.rebuild(backup), 1)
        with self.store.connect() as db:
            row = db.execute("SELECT * FROM submissions").fetchone()
        self.assertEqual(row["source"], "legacy_saved_history")
        self.assertEqual(self.store.original(result["run_id"]), body)
        self.assertTrue(json.loads(self.store.detail(result["run_id"])["facts"])["legacy_display_warning"])
        with self.assertRaises(ValueError):
            self.store.rebuild(backup)

    def test_native_catalog_covers_professions_and_colorless_images(self):
        from app import CATALOG, WEB
        for cid, owner in [("COORDINATE", "colorless"), ("TAG_TEAM", "colorless"), ("STRIKE_IRONCLAD", "ironclad"),
                           ("STRIKE_SILENT", "silent"), ("STRIKE_DEFECT", "defect"),
                           ("STRIKE_REGENT", "regent"), ("STRIKE_NECROBINDER", "necrobinder")]:
            self.assertEqual(CATALOG[cid]["owner"], owner)
            self.assertTrue((WEB / CATALOG[cid]["image"]).is_file())

    def test_rebuild_backs_up_and_changes_only_derived_facts(self):
        status, result = self.store.upload(self.data)
        rid = result["run_id"]
        before = dict(self.store.detail(rid))
        legacy = json.loads(before["facts"])
        legacy.pop("facts_version")
        with self.store.connect() as db:
            db.execute("UPDATE runs SET facts=? WHERE id=?", (json.dumps(legacy), rid))
        self.assertTrue(self.store.needs_rebuild())
        backup = Path(self.tmp.name) / "before-rebuild.sqlite3"
        self.assertEqual(self.store.rebuild(backup), 1)
        self.assertTrue(backup.exists())
        after = self.store.detail(rid)
        self.assertEqual(json.loads(after["facts"])["facts_version"], FACTS_VERSION)
        for key in ("id", "digest", "payload", "received", "duplicates"):
            self.assertEqual(after[key], before[key])
        self.assertFalse(self.store.needs_rebuild())

    def test_http_direct_and_r6_upload_read_guard_validation_and_raw(self):
        with Server(("127.0.0.1", 0), self.store, "r" * 32) as server:
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            base = f"http://127.0.0.1:{server.server_port}"
            def request(path, data=None, headers=None):
                try:
                    with urlopen(Request(base + path, data=data, headers=headers or {}), timeout=5) as response:
                        return response.status, response.read()
                except HTTPError as error:
                    return error.code, error.read()
            try:
                self.assertEqual(request("/api/runs")[0], 404)
                self.assertEqual(request("/api/upload", b"{}")[0], 415)
                def upload(body):
                    return request("/api/upload", body, {"Content-Type": "application/json"})
                self.assertEqual(upload(b"{}")[0], 400)
                body = json.dumps(self.data).encode()
                status, raw = upload(body)
                self.assertEqual(status, 201)
                rid = json.loads(raw)["run_id"]
                manifest = json.dumps({"sha256": hashlib.sha256(body).hexdigest(), "bytes": len(body)}).encode()
                status, response = request("/api/upload-session", manifest, {"Content-Type": "application/json"})
                self.assertEqual(status, 201)
                session = json.loads(response)
                self.assertTrue(session["deprecated"])
                status, _ = request("/api/upload", body, {"Content-Type": "application/json", "Authorization": "Bearer " + session["upload_token"]})
                self.assertEqual(status, 200)
                self.assertEqual(request("/api/upload", b"{}", {"Content-Type": "application/json", "Content-Length": str(8 * 1024 * 1024 + 1)})[0], 413)
                self.assertEqual(request("/api/upload-session", b"{}", {"Content-Type": "application/json"})[0], 400)
                read = {"X-Observatory-Access": "r" * 32}
                status, filtered = request("/api/runs?character=KAREN", headers=read)
                self.assertEqual(status, 200)
                self.assertEqual(json.loads(filtered)["total"], 1)
                self.assertEqual(json.loads(request("/api/runs?character=SILENT", headers=read)[1])["total"], 0)
                self.assertEqual(request("/api/runs?character=%27", headers=read)[0], 400)
                stats = json.loads(request("/api/stats?character=KAREN", headers=read)[1])
                self.assertEqual((stats["character"], stats["seats"]), ("KAREN", 1))
                self.assertEqual(json.loads(request("/api/stats?character=SILENT", headers=read)[1])["seats"], 0)
                self.assertEqual(request("/api/stats?character=%27", headers=read)[0], 400)
                status, raw = request("/api/runs/" + rid, headers=read)
                self.assertEqual(status, 200)
                self.assertEqual(json.loads(raw)["history"]["players"][0]["id"], "76561199000000001")
                status, raw = request("/api/runs/" + rid + "/raw", headers=read)
                self.assertEqual(raw, body)
                self.assertEqual(json.loads(raw)["players"][0]["id"], 76561199000000001)
                self.assertEqual(request("/../app.py", headers=read)[0], 404)
            finally:
                server.shutdown()
                thread.join(timeout=5)


if __name__ == "__main__":
    unittest.main()
