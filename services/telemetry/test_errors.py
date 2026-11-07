import copy
import json
from pathlib import Path
import tempfile
import threading
import time
import unittest
from unittest.mock import patch
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from app import Server, Store
from error_reports import ErrorLimiter, InvalidError, clean


def fixture(**changes):
    return dict(schema_version=1, report_id="a" * 32, session_id="b" * 32,
                build="0.107.1", mod_version="v0.1.6", platform="pc", os="Windows", game_commit="test-build",
                mods=[{"id": "BaseLib", "name": "BaseLib", "version": "v3.2.1"}], mods_truncated=False,
                source="game", message="Failed card 123", stack="at Karen.Play() line 20",
                first_seen=int(time.time()) - 5, last_seen=int(time.time()), count=3) | changes


class ErrorTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.store = Store(Path(self.tmp.name) / "history.sqlite3")
        self.errors = self.store.errors

    def tearDown(self):
        self.tmp.cleanup()

    def test_retry_out_of_order_and_restart_are_idempotent(self):
        data = fixture()
        status, result = self.errors.upload(data)
        self.assertEqual(status, 201)
        self.assertEqual(self.errors.upload(data)[0], 200)
        self.assertEqual(self.errors.upload(data | {"count": 7})[0], 200)
        self.assertEqual(self.errors.upload(data | {"count": 5})[0], 200)
        restarted = Store(self.store.path).errors
        restarted.upload(data | {"count": 7})
        self.assertEqual(restarted.detail(result["id"])["occurrences"], 7)
        self.assertEqual(self.store.runs({})["total"], 0)

    def test_fingerprint_merges_variable_values_separates_versions(self):
        self.errors.upload(fixture())
        self.errors.upload(fixture(report_id="c" * 32, session_id="d" * 32,
                                   message="Failed card 456", stack="at Karen.Play() line 99"))
        listing = self.errors.listing({})
        self.assertEqual(listing["total"], 1)
        self.assertEqual(listing["items"][0]["occurrences"], 6)
        self.assertEqual(listing["items"][0]["sessions"], 2)
        self.errors.upload(fixture(report_id="e" * 32, build="0.111.0"))
        self.errors.upload(fixture(report_id="f" * 32, mod_version="v0.2"))
        self.assertEqual(self.errors.listing({})["total"], 3)
        self.assertEqual(self.errors.listing({"build": ["0.111.0"]})["total"], 1)
        self.assertEqual(self.errors.listing({"search": ["Karen.Play"]})["total"], 3)

    def test_identity_conflicts_and_concurrent_retries(self):
        data = fixture()
        _, result = self.errors.upload(data)
        self.assertEqual(self.errors.upload(data | {"message": "different cause"})[0], 409)
        self.assertEqual(self.errors.upload(data | {"session_id": "d" * 32})[0], 409)
        from concurrent.futures import ThreadPoolExecutor
        with ThreadPoolExecutor(max_workers=8) as pool:
            results = list(pool.map(lambda count: self.errors.upload(data | {"count": count}), [8, 6, 8, 4, 10, 5, 10, 9]))
        self.assertTrue(all(status == 200 for status, _ in results))
        self.assertEqual(self.errors.detail(result["id"])["occurrences"], 10)

    def test_environment_preserves_mod_versions_and_canonicalizes_order(self):
        mods = fixture()["mods"] + [{"id": "AnotherMod", "name": "另一 Mod", "version": "v1.2.3"}]
        _, result = self.errors.upload(fixture(mods=mods))
        detail = self.errors.detail(result["id"])
        self.assertEqual(detail["mods"][0], mods[1])
        self.assertEqual(detail["game_commit"], "test-build")
        self.errors.upload(fixture(report_id="c" * 32, mods=list(reversed(mods))))
        self.assertEqual(self.errors.listing({})["total"], 1)
        changed = copy.deepcopy(mods); changed[0]["version"] = "v3.3.0"
        self.errors.upload(fixture(report_id="d" * 32, mods=changed))
        self.errors.upload(fixture(report_id="e" * 32, platform="android", os="Android"))
        self.assertEqual(self.errors.listing({})["total"], 3)
        self.assertEqual(self.errors.listing({"platform": ["android"]})["total"], 1)
        for changes in ({"mods": mods * 70}, {"mods": mods + [mods[0]]}, {"mods_truncated": 1},
                        {"mods": [{"id": "x", "name": "x", "version": "x" * 41}]}, {"platform": "invalid"}):
            with self.subTest(changes=list(changes)), self.assertRaises(InvalidError):
                self.errors.upload(fixture(**changes))
        backup = Path(self.tmp.name) / "backup.sqlite3"
        self.store.backup(backup)
        self.assertEqual(Store(backup).errors.detail(result["id"])["mods"], detail["mods"])

    def test_rejects_shape_bounds_and_cleans_sensitive_text(self):
        for changes in ({"count": True}, {"count": 1000001}, {"source": "unknown"}, {"message": "x" * 2049},
                        {"stack": "x" * 8193}, {"session_id": "invalid"}, {"last_seen": 1}, {"schema_version": True},
                        {"extra": "value"}, {"build": {"bad": "value"}}):
            with self.subTest(changes=list(changes)), self.assertRaises(InvalidError):
                self.errors.upload(fixture(**changes))
        windows_test_path = "\\".join(("C:", "Users", "PrivateName", "file.cs"))
        unix_test_path = "/".join(("", "home", "private", "file"))
        text = f'Authorization: Bearer PRIVATE_TOKEN\npassword=PRIVATE_PASSWORD\nhttps://host.invalid/secret?q=x\n{windows_test_path}\n{unix_test_path}\ntest@example.invalid 76561199000000001 192.168.3.9'
        text += '\nAuthorization: Basic PRIVATE_BASIC\n"password": "PRIVATE SPACE PASSWORD"'
        _, result = self.errors.upload(fixture(message=text))
        message = self.errors.detail(result["id"])["message"]
        for secret in ("PRIVATE_TOKEN", "PRIVATE_PASSWORD", "PRIVATE_BASIC", "SPACE", "PrivateName", unix_test_path, "host.invalid", "example.invalid", "76561199000000001", "192.168.3.9"):
            self.assertNotIn(secret, message)

    def test_resource_uris_survive_upload_and_remain_distinct(self):
        uris = ("res://images/example.tres", "res://audio/example.ogg", "event:/sfx/example")
        for i, uri in enumerate(uris):
            with self.subTest(uri=uri):
                message, stack = f"Failed loading resource: {uri}", f"at Load('{uri}')"
                _, result = self.errors.upload(fixture(report_id=format(i, "032x"), message=message, stack=stack))
                detail = self.errors.detail(result["id"])
                self.assertEqual(detail["message"], message)
                self.assertEqual(detail["stack"], stack)
        self.assertEqual(self.errors.listing({})["total"], len(uris))

    def test_disk_paths_still_redacted_beside_resource_uris(self):
        paths = ("\\".join(("C:", "Users", "PrivateName", "file.cs")),
                 "/".join(("d:", "Users", "PrivateName", "file.cs")),
                 "\\\\" + "\\".join(("server", "private", "file.cs")),
                 "/".join(("", "home", "private", "file.cs")))
        for path in paths:
            for prefix in ("", "path=", "路径：", "路径"):
                with self.subTest(path=path, prefix=prefix):
                    text = f"res://images/example.tres\n{prefix}{path}\nevent:/sfx/example"
                    expected = f"res://images/example.tres\n{prefix}<path>\nevent:/sfx/example"
                    self.assertEqual(clean(text, 2048), expected)
                    self.assertEqual(clean(expected, 2048), expected)

    def test_persistent_capacity_and_daily_limits_preserve_received_errors(self):
        data = fixture()
        with patch("error_reports.MAX_GROUPS", 1):
            self.errors.upload(data)
            self.assertEqual(self.errors.upload(fixture(report_id="c" * 32, message="another failure"))[0], 507)
            self.assertEqual(self.errors.upload(data | {"count": 4})[0], 200)
        with patch("error_reports.MAX_REPORTS", 1):
            self.assertEqual(self.errors.upload(fixture(report_id="c" * 32))[0], 507)
        with patch("error_reports.DAILY_REPORTS", 1):
            self.assertEqual(self.errors.upload(fixture(report_id="c" * 32))[0], 429)
        self.assertEqual(Store(self.store.path).errors.listing({})["total"], 1)

    def test_accepts_new_errors_beyond_legacy_group_capacity(self):
        data = fixture()
        _, original = self.errors.upload(data)
        with self.store.connect() as db:
            row = db.execute("SELECT * FROM error_groups").fetchone()
            db.executemany("INSERT INTO error_groups (id,build,mod_version,platform,source,message,stack,first_seen,last_seen,occurrences,environment) VALUES (?,?,?,?,?,?,?,?,?,?,?)",
                           [(format(i, "064x"), *tuple(row)[1:11]) for i in range(2999)])
        status, result = self.errors.upload(fixture(report_id="c" * 32, message="new capacity regression"))
        self.assertEqual(status, 201)
        self.assertEqual(self.errors.upload(data | {"count": 8})[0], 200)
        restarted = Store(self.store.path).errors
        self.assertEqual(restarted.detail(original["id"])["occurrences"], 8)
        self.assertIsNotNone(restarted.detail(result["id"]))
        self.assertEqual(restarted.listing({})["total"], 3001)

    def test_compact_list_preserves_detail_and_filters(self):
        _, result = self.errors.upload(fixture())
        compact = self.errors.listing({"compact": ["1"]})
        rich = self.errors.listing({})
        self.assertNotIn("environment", compact["items"][0])
        self.assertIn("environment", rich["items"][0])
        self.assertEqual(compact["items"][0]["id"], result["id"])
        self.assertEqual(compact["source_mods"], ["BaseLib"])
        self.assertEqual(compact["items"][0]["sessions"], 1)
        self.assertEqual(self.errors.detail(result["id"])["stack"], fixture()["stack"])

    def test_environments_are_shared_compressed_and_legacy_rows_migrate(self):
        import gzip
        import sqlite3
        data = fixture()
        _, first = self.errors.upload(data)
        self.errors.upload(fixture(report_id="c" * 32, message="different error"))
        with self.store.connect() as db:
            environments = db.execute("SELECT payload FROM error_environments").fetchall()
            self.assertEqual(len(environments), 1)
            self.assertEqual(json.loads(gzip.decompress(environments[0][0]))["mods"], data["mods"])
        # Build the pre-compression schema with one accepted cumulative report.
        legacy_path = Path(self.tmp.name) / "legacy.sqlite3"
        with sqlite3.connect(legacy_path) as db:
            db.execute("CREATE TABLE error_groups (id TEXT PRIMARY KEY, build TEXT NOT NULL, mod_version TEXT NOT NULL, "
                       "platform TEXT NOT NULL, source TEXT NOT NULL, message TEXT NOT NULL, stack TEXT NOT NULL, "
                       "first_seen INTEGER NOT NULL, last_seen INTEGER NOT NULL, occurrences INTEGER NOT NULL, environment TEXT NOT NULL)")
            environment = {key: data[key] for key in ("os", "game_commit", "mods", "mods_truncated")}
            db.execute("INSERT INTO error_groups VALUES (?,?,?,?,?,?,?,?,?,?,?)",
                       (first["id"], *[data[key] for key in ("build", "mod_version", "platform", "source", "message", "stack", "first_seen", "last_seen", "count")], json.dumps(environment)))
            db.execute("CREATE TABLE error_reports (id TEXT PRIMARY KEY, session TEXT NOT NULL, group_id TEXT NOT NULL, "
                       "count INTEGER NOT NULL, first_seen INTEGER NOT NULL, last_seen INTEGER NOT NULL, received INTEGER NOT NULL)")
            db.execute("INSERT INTO error_reports VALUES (?,?,?,?,?,?,?)",
                       (data["report_id"], data["session_id"], first["id"], data["count"], data["first_seen"], data["last_seen"], int(time.time())))
        db.close()
        migrated = Store(legacy_path).errors
        self.assertTrue(list((legacy_path.parent / "backups").glob("before-error-compression-*.sqlite3")))
        self.assertEqual(migrated.detail(first["id"])["mods"], data["mods"])
        self.assertEqual(migrated.listing({"source_mod": ["BaseLib"]})["total"], 1)
        self.assertEqual(json.loads(migrated.listing({})["items"][0]["environment"]), environment)
        self.assertEqual(migrated.upload(data)[0], 200)
        self.assertEqual(migrated.upload(data | {"count": 8})[0], 200)
        self.assertEqual(Store(legacy_path).errors.detail(first["id"])["occurrences"], 8)

    def test_limiter_blocks_single_source_and_rotating_sources(self):
        now = [0.]
        limiter = ErrorLimiter(lambda: now[0])
        self.assertTrue(all(limiter.accept("one") for _ in range(6)))
        self.assertFalse(limiter.accept("one"))
        self.assertEqual(sum(limiter.accept(str(i)) for i in range(10000)), 53)
        self.assertLessEqual(len(limiter.clients), 60)
        now[0] = 60
        self.assertTrue(limiter.accept("one"))

    def test_http_read_auth_body_limits_and_retry_after(self):
        with Server(("127.0.0.1", 0), self.store, "private-test-key") as server:
            worker = threading.Thread(target=server.serve_forever, daemon=True)
            worker.start()
            base = "http://127.0.0.1:" + str(server.server_port)
            def request(path, data=None, headers=None):
                try:
                    response = urlopen(Request(base + path, data=data, headers=headers or {}), timeout=3)
                except HTTPError as error:
                    response = error
                with response:
                    return response.status, response.headers, response.read()
            try:
                self.assertEqual(request("/api/errors")[0], 404)
                status, _, body = request("/api/errors", json.dumps(fixture()).encode(), {"Content-Type": "application/json"})
                self.assertEqual(status, 201)
                fingerprint = json.loads(body)["id"]
                self.assertEqual(request("/api/errors/" + fingerprint)[0], 404)
                auth = {"X-Observatory-Access": "private-test-key"}
                self.assertEqual(request("/api/errors/" + fingerprint, headers=auth)[0], 200)
                self.assertEqual(request("/errors.html", headers=auth)[0], 200)
                self.assertEqual(request("/api/errors", b"x" * 65537, {"Content-Type": "application/json"})[0], 413)
                self.assertEqual(request("/api/errors", b"{}", {"Content-Type": "text/plain"})[0], 415)
                for _ in range(5):
                    result = request("/api/errors", b"{}", {"Content-Type": "application/json"})
                self.assertEqual(result[0], 429)
                self.assertEqual(result[1]["Retry-After"], "60")
            finally:
                server.shutdown()
                worker.join()

    def test_pagination_escaping_search_and_literal_filters(self):
        for i in range(23):
            self.errors.upload(fixture(report_id=format(i, "032x"), message="failure " + chr(65 + i)))
        listing = self.errors.listing({"page": ["2"], "sort": ["count"]})
        self.assertEqual(len(listing["items"]), 3)
        self.assertEqual(listing["pages"], 2)
        self.assertEqual(self.errors.listing({"search": ["%"]})["total"], 0)
        self.assertEqual(self.errors.listing({"build": ["' OR 1=1 --"]})["total"], 0)
        with self.assertRaises(InvalidError):
            self.errors.listing({"sort": ["id;DROP TABLE runs"]})


if __name__ == "__main__":
    unittest.main()
