import copy
import json
from pathlib import Path
import tempfile
import unittest
import threading
from urllib.request import Request, urlopen

from app import Invalid, Server, Store, derive, parse_history
from error_reports import InvalidError
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
                history["core_telemetry"] = context()
                self.assertEqual(request("/api/upload", history)[0], 201)
                self.assertEqual(request("/api/errors", error_fixture(schema_version=2, core_telemetry=context()))[0], 201)
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
