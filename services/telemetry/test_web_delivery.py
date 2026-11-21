import copy
import gzip
import json
from pathlib import Path
import tempfile
import threading
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from app import Server, Store
from test_app import fixture
from test_core_protocol import context

class WebDeliveryTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.store = Store(Path(self.tmp.name) / 'history.sqlite3')
        data = fixture()
        data['core_telemetry'] = context()
        self.store.upload(data)
        other = copy.deepcopy(data)
        other['seed'] += '-OTHER'
        other['players'][0]['character'] = 'CHARACTER.SILENT'
        self.store.upload(other)
        self.server = Server(('127.0.0.1', 0), self.store, 'local-test-key-32-characters-long')
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()

    def tearDown(self):
        self.server.shutdown()
        self.thread.join()
        self.server.server_close()
        self.tmp.cleanup()

    def request(self, path, headers=None, authorized=True):
        headers = dict(headers or {})
        if authorized:
            headers['X-Observatory-Access'] = self.server.read_key
        try:
            response = urlopen(Request(f'http://127.0.0.1:{self.server.server_port}' + path, headers=headers), timeout=5)
        except HTTPError as error:
            response = error
        with response:
            return response.status, response.headers, response.read()

    def test_pwa_routes_remain_authenticated_and_revalidate(self):
        for path in ('/manifest.webmanifest', '/sw.js', '/client.js', '/offline.html', '/assets/app_icon_192.png', '/assets/app_icon_512.png'):
            self.assertEqual(self.request(path, authorized=False)[0], 404)
            status, headers, body = self.request(path)
            self.assertEqual(status, 200, path)
            self.assertTrue(body)
            self.assertTrue(headers['Cache-Control'].startswith('private'))
            self.assertEqual(self.request(path, {'If-None-Match': headers['ETag']})[0], 304)
        _, headers, raw = self.request('/manifest.webmanifest')
        self.assertIn('application/manifest+json', headers['Content-Type'])
        manifest = json.loads(raw)
        self.assertEqual(manifest['scope'], './')
        self.assertEqual(manifest['start_url'], './')
        self.assertEqual({icon['sizes'] for icon in manifest['icons']}, {'192x192', '512x512'})

    def test_gzip_reduces_stats_and_api_remains_live(self):
        _, plain_headers, plain = self.request('/api/stats')
        status, headers, raw = self.request('/api/stats', {'Accept-Encoding': 'gzip'})
        self.assertEqual(status, 200)
        self.assertEqual(headers['Content-Encoding'], 'gzip')
        self.assertEqual(gzip.decompress(raw), plain)
        self.assertLess(len(raw), len(plain))
        self.assertEqual(headers['Cache-Control'], 'no-store')
        self.assertEqual(headers['Vary'], 'Accept-Encoding')
        self.assertIsNone(plain_headers.get('Content-Encoding'))
        self.assertIsNone(self.request('/api/stats', {'Accept-Encoding': 'gzip;q=0'})[1].get('Content-Encoding'))
        self.assertEqual(self.request('/api/stats', {'If-None-Match': '* '})[0], 200)

    def test_run_filters_do_not_require_statistics_aggregation(self):
        self.store.stats = lambda query: self.fail('runs must not aggregate card statistics')
        status, _, raw = self.request('/api/runs?character=SILENT')
        self.assertEqual(status, 200)
        data = json.loads(raw)
        self.assertEqual(data['total'], 1)
        self.assertEqual(data['filters']['characters'], ['KAREN', 'SILENT'])
        self.assertEqual(data['filters']['core_versions'], ['1.0.0'])
        self.assertEqual(data['filters']['source_mods'], ['Example'])
        self.assertEqual(data['filters']['builds'], ['collector-test'])

    def test_character_stats_counts_seats_and_distinct_runs(self):
        for seed, roles, outcome, floor, language in (
            ("mixed", ("KAREN", "KAREN", "JUNNA"), "win", 15, "zhs"),
            ("death", ("KAREN",), "death", 2, None),
            ("early", ("JUNNA",), "abandon", 5, "zhs"),
            ("later", ("JUNNA", "OTHER_ROLE"), "abandon", 6, "zhs"),
        ):
            data = fixture() | {
                "seed": seed, "ascension": 7, "win": outcome == "win", "was_abandoned": outcome == "abandon",
                "players": [{"id": i + 1, "character": "CHARACTER." + role} for i, role in enumerate(roles)],
                "map_point_history": [[{} for _ in range(floor)]],
            }
            if language:
                data["core_telemetry"] = context() | {"language": language}
            self.assertEqual(self.store.upload(data)[0], 201)

        # The role endpoint reads derived seats separately from card aggregation.
        self.store.stats = lambda query: self.fail("role stats must not aggregate cards")
        path = "/api/character-stats?days=0&ascension=7"
        self.assertEqual(self.request(path, authorized=False)[0], 404)

        def get(suffix=""):
            status, headers, raw = self.request(path + suffix)
            self.assertEqual(status, 200)
            self.assertEqual(headers["Cache-Control"], "no-store")
            return json.loads(raw)

        data = get()
        self.assertEqual(data["filters"]["languages"], ["unknown", "zhs"])
        self.assertEqual(data["rows"], [
            {"character": "KAREN", "runs": 2, "seats": 3, "wins": 2, "deaths": 1, "abandons": 0,
             "win_rate": 2 / 3, "avg_floor": 32 / 3},
            {"character": "JUNNA", "runs": 2, "seats": 2, "wins": 1, "deaths": 0, "abandons": 1,
             "win_rate": .5, "avg_floor": 10.5},
            {"character": "OTHER_ROLE", "runs": 1, "seats": 1, "wins": 0, "deaths": 0, "abandons": 1,
             "win_rate": 0, "avg_floor": 6},
        ])
        rows = get("&character=CHARACTER.JUNNA&players=3&language=zhs")["rows"]
        self.assertEqual([(r["character"], r["seats"], r["win_rate"]) for r in rows], [("JUNNA", 1, 1)])
        rows = get("&character=JUNNA&sample=all")["rows"]
        self.assertEqual([(r["seats"], r["abandons"], r["win_rate"]) for r in rows], [(3, 2, 1 / 3)])
        self.assertEqual(get("&language=jpn")["rows"], [])


    def test_language_stats_backfills_and_tracks_windows_without_loading_cards(self):
        data = fixture() | {"seed": "mixed-language", "core_telemetry": context() | {"language": "zhs"}}
        data["players"].extend([{"id": 2, "character": "CHARACTER.KAREN"},
                                {"id": 3, "character": "CHARACTER.JUNNA"}])
        status, result = self.store.upload(data)
        self.assertEqual(status, 201)
        early = fixture() | {"seed": "early-language", "win": False, "was_abandoned": True,
                             "core_telemetry": context() | {"language": "eng"}}
        self.assertEqual(self.store.upload(early)[0], 201)
        expected = self.store.stats({"days": ["0"], "character": ["KAREN"]})["language_distribution"]

        # An existing collector database may predate the projection.
        with self.store.connect() as db:
            for table in ("stats_characters", "stats_sources", "stats_runs"):
                db.execute("DELETE FROM " + table)
        self.server.store = self.store = Store(self.store.path)
        self.store.stats = lambda query: self.fail("language stats must not aggregate cards")
        path = "/api/language-stats?days=0&character=KAREN"
        self.assertEqual(self.request(path, authorized=False)[0], 404)

        def get(suffix=""):
            status, headers, raw = self.request(path + suffix)
            self.assertEqual(status, 200)
            self.assertEqual(headers["Cache-Control"], "no-store")
            return json.loads(raw)

        self.assertEqual(get()["language_distribution"], expected)
        self.assertEqual(get("&players=3&source_mod=Example")["language_distribution"],
                         [{"language": "zhs", "runs": 1, "share": 1}])
        self.assertEqual(get("&sample=all")["language_total_runs"], 3)
        self.assertEqual(get("&language=jpn")["language_distribution"], [])

        # Maintenance commands update facts in-place, including when using an older collector.
        with self.store.connect() as db:
            db.execute("UPDATE runs SET facts=json_set(facts, '$.core_telemetry.language', 'jpn') WHERE id=?",
                       (result["run_id"],))
        self.assertEqual(get("&language=jpn")["language_distribution"],
                         [{"language": "jpn", "runs": 1, "share": 1}])
        self.assertEqual(get("&language=zhs")["language_total_runs"], 0)


if __name__ == '__main__':
    unittest.main()
