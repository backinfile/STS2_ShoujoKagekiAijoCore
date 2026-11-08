import unittest
from domain_config import resolve_origins


class DomainConfigTests(unittest.TestCase):
    def test_migration_preserves_page_origin(self):
        access = {"page_url": "https://view.example/observe-private/", "upload_url": "https://view.example/ingest-old"}
        self.assertEqual(resolve_origins(access, upload_origin="https://upload.example"), ("https://view.example", "https://upload.example"))

    def test_reinstall_retains_both_origins(self):
        access = {"page_url": "https://view.example/observe-private/", "upload_url": "https://upload.example/ingest-private"}
        self.assertEqual(resolve_origins(access), ("https://view.example", "https://upload.example"))

    def test_page_change_does_not_change_upload_origin(self):
        access = {"page_url": "https://view.example/observe-private/", "upload_url": "https://upload.example/ingest-private"}
        self.assertEqual(resolve_origins(access, page_origin="https://other.example/"), ("https://other.example", "https://upload.example"))

    def test_shared_hostname_rejected(self):
        for upload in ("https://view.example", "https://VIEW.example:8443"):
            with self.subTest(upload=upload), self.assertRaises(ValueError):
                resolve_origins({}, "https://view.example", upload)

    def test_invalid_origins_rejected(self):
        for origin in (None, "http://upload.example", "https://upload.example/path", "https://user@upload.example", "https://upload.example?key=x", "https://upload.example#fragment", "https://bad;host.example", "https://upload.example:invalid"):
            with self.subTest(origin=origin), self.assertRaises(ValueError):
                resolve_origins({}, "https://view.example", origin)


if __name__ == "__main__":
    unittest.main()
