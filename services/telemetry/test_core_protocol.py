import copy
import json
from pathlib import Path
import tempfile
import unittest
import threading
from urllib.request import Request, urlopen

from app import Invalid, Server, Store, derive, parse_history
from error_reports import InvalidError, validate
from test_app import fixture
from test_errors import fixture as error_fixture


def context():
    return {"schema_version": 2, "core_version": "1.0.0", "api_version": "1", "game_target": "stable", "game_version": "0.107.1",
            "source_mods": [{"id": "Example", "name": "Example", "version": "2.0.0"}],
            "model_sources": [{"model_id": "CARD.EXAMPLE_CARD", "source_mod": "Example", "source_version": "2.0.0"}]}


class CoreProtocolTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.store = Store(Path(self.tmp.name) / "history.sqlite3")

    def tearDown(self):
        self.tmp.cleanup()

    def test_history_metadata_preserves_legacy_identity_and_raw_bytes(self):
        legacy = fixture()
        data = copy.deepcopy(legacy)
        data["core_telemetry"] = context()
        for card in data["players"][0]["deck"]:
            card["model_full_id"] = card["id"]
        data["players"][0]["core_shine_exhaust_cards"] = [{"id": "CARD.EXAMPLE_CARD", "model_full_id": "CARD.EXAMPLE_CARD"}]
        body = json.dumps(data, indent=2).encode()
        status, result = self.store.upload(parse_history(body), original_body=body)
        self.assertEqual(status, 201)
        self.assertEqual(result["run_id"], derive(legacy)["id"])
        self.assertEqual(self.store.original(result["run_id"]), body)
        facts = json.loads(self.store.detail(result["run_id"])["facts"])
        self.assertEqual(facts["core_telemetry"], context())
        self.assertIn("EXAMPLE_CARD", facts["seats"][0]["held"])
        self.assertEqual(facts["seats"][0]["held"].count("KAREN_STRIKE"), 1)
        self.assertEqual(self.store.runs({"source_mod": ["Example"], "core_version": ["1.0.0"]})["total"], 1)
        self.assertEqual(self.store.runs({"source_mod": ["missing"]})["total"], 0)
        self.assertEqual(self.store.stats({})["core_versions"], ["1.0.0"])

    def test_error_v2_idempotency_legacy_version_and_core_separation(self):
        item = error_fixture(schema_version=2, core_telemetry=context())
        status, result = self.store.errors.upload(item)
        self.assertEqual(status, 201)
        self.assertEqual(self.store.errors.upload(item)[0], 200)
        detail = self.store.errors.detail(result["id"])
        self.assertEqual(detail["mod_version"], item["mod_version"])
        self.assertEqual(detail["core_telemetry"]["core_version"], "1.0.0")
        self.assertEqual(self.store.errors.listing({"core_version": ["1.0.0"], "source_mod": ["Example"]})["total"], 1)
        self.assertEqual(self.store.errors.listing({"source_mod": ["missing"]})["total"], 0)
        changed = copy.deepcopy(item)
        changed["core_telemetry"]["core_version"] = "1.1.0"
        self.assertEqual(self.store.errors.upload(changed)[0], 409)
        changed["report_id"] = "c" * 32
        self.assertEqual(self.store.errors.upload(changed)[0], 201)

    def test_metadata_bounds_and_schema_rejection(self):
        for changes in ({"schema_version": 3}, {"core_version": "x" * 121}, {"source_mods": [context()["source_mods"][0]] * 129}):
            data = fixture()
            data["core_telemetry"] = context() | changes
            with self.assertRaises(Invalid):
                self.store.upload(data)
            with self.assertRaises(InvalidError):
                self.store.errors.upload(error_fixture(schema_version=2, core_telemetry=data["core_telemetry"]))

    def test_language_distribution_uses_filtered_runs_not_multiplayer_seats(self):
        for seed, language, extra_seat, abandon in (
                ("zh", "zhs", True, False), ("en", "eng", False, False),
                ("legacy", None, False, False), ("old-core", "", False, False),
                ("abandon", "eng", False, True)):
            data = fixture()
            data["seed"] = seed
            if language is not None:
                data["core_telemetry"] = context()
                if language:
                    data["core_telemetry"]["language"] = language
            if extra_seat:
                data["players"].append({"id": 2, "character": "CHARACTER.IRONCLAD", "deck": []})
            if abandon:
                data.update(win=False, was_abandoned=True)
            self.assertEqual(self.store.upload(data)[0], 201)
            self.assertEqual(self.store.upload(data)[0], 200)
        stats = self.store.stats({})
        self.assertEqual(stats["language_total_runs"], 4)
        self.assertEqual(stats["seats"], 5)
        counts = {row["language"]: row["runs"] for row in stats["language_distribution"]}
        self.assertEqual(counts, {"zhs": 1, "eng": 1, "unknown": 2})
        self.assertEqual(stats["language_distribution"][-1]["language"], "unknown")
        self.assertAlmostEqual(sum(row["share"] for row in stats["language_distribution"]), 1)
        self.assertEqual(self.store.stats({"sample": ["all"]})["language_total_runs"], 5)
        chinese = self.store.stats({"character": ["IRONCLAD"]})
        self.assertEqual(chinese["language_distribution"], [{"language": "zhs", "runs": 1, "share": 1}])
        self.assertEqual(self.store.stats({"language": ["unknown"]})["language_total_runs"], 2)
        self.assertEqual(self.store.runs({"language": ["unknown"]})["total"], 2)
        self.assertEqual(self.store.runs({"language": ["zhs"]})["total"], 1)
        self.assertEqual(self.store.run_filters({})["languages"], ["eng", "unknown", "zhs"])
        empty = self.store.stats({"language": ["jpn"]})
        self.assertEqual((empty["language_total_runs"], empty["language_distribution"]), (0, []))
        self.assertEqual(Store(self.store.path).stats({})["language_distribution"], stats["language_distribution"])

    def test_language_bounds_and_unknown_field_rejection(self):
        for language in (None, "", "a", "ENG", "x" * 33, "eng\x00", "<script>", "en' OR 1=1--", 1, []):
            data = fixture()
            data["core_telemetry"] = context() | {"language": language}
            with self.subTest(language=language):
                with self.assertRaises(Invalid):
                    self.store.upload(data)
                with self.assertRaises(InvalidError):
                    self.store.errors.upload(error_fixture(schema_version=2, core_telemetry=data["core_telemetry"]))
        for language in ("", "EN", "x" * 33, "en' OR 1=1--"):
            with self.assertRaises(Invalid):
                self.store.stats({"language": [language]})
        data = fixture()
        data["core_telemetry"] = context() | {"unexpected": "zhs"}
        with self.assertRaises(Invalid):
            self.store.upload(data)
        for language in ("zhs", "eng", "jpn", "unknown", "en-us"):
            self.assertEqual(derive(fixture() | {"core_telemetry": context() | {"language": language}})
                             ["core_telemetry"]["language"], language)

    def test_language_preserves_run_identity_raw_bytes_and_error_retries(self):
        data = fixture()
        legacy_id = derive(data)["id"]
        data["core_telemetry"] = context() | {"language": "jpn"}
        body = json.dumps(data, indent=2).encode()
        status, result = self.store.upload(parse_history(body), original_body=body)
        self.assertEqual((status, result["run_id"]), (201, legacy_id))
        self.assertEqual(self.store.original(legacy_id), body)
        old = error_fixture(schema_version=2, core_telemetry=context())
        localized = copy.deepcopy(old)
        localized["core_telemetry"]["language"] = "jpn"
        self.assertEqual(validate(old)["fingerprint"], validate(localized)["fingerprint"])
        status, result = self.store.errors.upload(localized)
        self.assertEqual(status, 201)
        self.assertEqual(self.store.errors.detail(result["id"])["core_telemetry"]["language"], "jpn")
        self.assertEqual(self.store.errors.upload(localized)[0], 200)
        localized["core_telemetry"]["language"] = "eng"
        localized["count"] += 1
        self.assertEqual(self.store.errors.upload(localized), (200, {"status": "ok", "id": result["id"]}))

    def test_http_core_uploads_and_generated_character_catalogue(self):
        key = "local-verification-key-000000"
        with Server(("127.0.0.1", 0), self.store, key) as server:
            worker = threading.Thread(target=server.serve_forever, daemon=True)
            worker.start()
            try:
                base = "http://127.0.0.1:" + str(server.server_port)
                def request(path, value=None):
                    headers = {"X-Observatory-Access": key}
                    data = None
                    if value is not None:
                        data = json.dumps(value).encode()
                        headers["Content-Type"] = "application/json"
                    with urlopen(Request(base + path, data=data, headers=headers)) as response:
                        return response.status, response.read()
                history = fixture()
                history["core_telemetry"] = context() | {"language": "jpn"}
                self.assertEqual(request("/api/upload", history)[0], 201)
                self.assertEqual(request("/api/errors", error_fixture(schema_version=2, core_telemetry=context()))[0], 201)
                stats = json.loads(request("/api/stats?language=jpn")[1])
                self.assertEqual(stats["language_distribution"], [{"language": "jpn", "runs": 1, "share": 1}])
                self.assertEqual(json.loads(request("/api/runs?language=eng")[1])["total"], 0)
                for path in ("/healthz", "/", "/app.js", "/characters.json", "/errors.html", "/api/runs?core_version=1.0.0", "/api/errors?core_version=1.0.0"):
                    self.assertEqual(request(path)[0], 200, path)
            finally:
                server.shutdown()
                worker.join()

    def test_old_database_reopens_and_rebuild_preserves_payload_digest(self):
        status, result = self.store.upload(fixture())
        with self.store.connect() as db:
            old = dict(db.execute("SELECT * FROM runs").fetchone())
            facts = json.loads(old["facts"])
            facts["facts_version"] = 2
            facts.pop("core_telemetry", None)
            db.execute("UPDATE runs SET facts=?", (json.dumps(facts),))
        reopened = Store(self.store.path)
        self.assertTrue(reopened.needs_rebuild())
        reopened.rebuild(Path(self.tmp.name) / "backup.sqlite3")
        self.assertFalse(reopened.needs_rebuild())
        with reopened.connect() as db:
            current = dict(db.execute("SELECT * FROM runs").fetchone())
        for key in ("id", "digest", "payload", "received"):
            self.assertEqual(current[key], old[key])


if __name__ == "__main__":
    unittest.main()
