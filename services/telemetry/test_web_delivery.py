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

if __name__ == '__main__':
    unittest.main()
