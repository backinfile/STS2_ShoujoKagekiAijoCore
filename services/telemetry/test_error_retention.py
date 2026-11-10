from concurrent.futures import ThreadPoolExecutor
import json
from pathlib import Path
import sqlite3
import tempfile
import threading
import time
import unittest
from unittest.mock import patch

from app import Store
from error_reports import InvalidError
import error_retention as retention
from test_errors import fixture

DAY = retention.DAY


class RetentionTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.store = Store(Path(self.tmp.name) / 'test.sqlite3')
        self.errors = self.store.errors
        self.now = int(time.time())

    def tearDown(self):
        self.tmp.cleanup()

    def add(self, number=1, age=8 * DAY, **changes):
        data = fixture(report_id=format(number, '032x'), message='failure ' + chr(64 + number), **changes)
        status, result = self.errors.upload(data)
        self.assertEqual(status, 201)
        with self.store.connect() as db:
            db.execute('UPDATE error_groups SET last_activity=? WHERE id=?', (self.now - age, result['id']))
        return data, result['id']

    def policy(self, statuses, at=None, closed=None):
        at = self.now if at is None else at
        return {'schema_version': 1, 'generated_at': at, 'valid_until': at + 3600,
                'groups': {key: {'status': value, 'closed_at': (closed or {}).get(key, 0)} for key, value in statuses.items()}}

    def sync(self, statuses, at=None, closed=None):
        return retention.sync_policy(self.store, self.policy(statuses, at, closed), now=self.now if at is None else at)

    def prune(self, **kwargs):
        return retention.prune(self.store, now=self.now, **kwargs)

    def test_three_and_seven_day_boundaries_and_dry_run(self):
        _, a = self.add(1, age=3 * DAY - 1)
        _, b = self.add(2, age=3 * DAY)
        _, c = self.add(3, age=7 * DAY - 1)
        _, d = self.add(4, age=7 * DAY)
        self.sync({a: 'ignored', b: 'ignored', c: 'ordinary', d: 'ordinary'})
        preview = self.prune()
        self.assertEqual(preview['candidates'], {'ignored': 1, 'ordinary': 1})
        self.assertEqual(self.errors.listing({})['total'], 4)
        applied = self.prune(dry_run=False)
        self.assertEqual(applied['retired_groups'], 2)
        self.assertEqual(applied['deleted_reports'], 2)
        self.assertEqual(applied['body_bytes'], preview['body_bytes'])
        self.assertEqual(self.errors.listing({})['total'], 2)
        self.assertEqual(self.prune(dry_run=False)['retired_groups'], 0)

    def test_missing_stale_and_unmapped_policy_preserve(self):
        _, key = self.add()
        self.assertEqual(self.prune()['retained'], {'policy_missing_or_stale': 1})
        self.sync({'f' * 64: 'ignored'})
        self.assertEqual(self.prune()['retained'], {'unmapped': 1})
        self.sync({key: 'ignored'}, at=self.now + 1)
        result = retention.prune(self.store, now=self.now + 3601)
        self.assertEqual(result['retained'], {'policy_missing_or_stale': 1})

    def test_protection_priority_and_explicit_closure_clock(self):
        _, key = self.add()
        self.sync({key: 'protected'})
        self.assertEqual(self.prune()['retained'], {'protected': 1})
        with self.assertRaises(ValueError):
            self.sync({key: 'ignored'}, at=self.now + 1)
        with self.assertRaises(ValueError):
            self.sync({'f' * 64: 'ordinary'}, at=self.now + 1)
        closed = self.now + 1
        self.sync({key: 'closed'}, at=closed, closed={key: closed})
        self.assertEqual(retention.prune(self.store, now=closed)['candidate_groups'], 0)
        later = closed + 7 * DAY
        self.sync({key: 'closed'}, at=later, closed={key: closed})
        self.assertEqual(retention.prune(self.store, now=later - 1)['candidate_groups'], 0)
        self.assertEqual(retention.prune(self.store, now=later)['candidate_groups'], 1)

    def test_count_update_and_historical_backfill_use_server_activity(self):
        old = self.now - 30 * DAY
        data, key = self.add(first_seen=old, last_seen=old)
        self.sync({key: 'ignored'})
        self.assertEqual(self.prune()['candidate_groups'], 1)
        with patch('error_reports.time.time', return_value=self.now):
            self.assertEqual(self.errors.upload(data | {'count': 9})[0], 200)
        self.assertEqual(self.prune()['candidate_groups'], 0)
        detail = self.errors.detail(key)
        self.assertEqual(detail['last_seen'], old)
        self.assertEqual(detail['last_activity'], self.now)
        backfill = data | {'report_id': 'f' * 32, 'session_id': 'e' * 32}
        self.assertEqual(self.errors.upload(backfill)[0], 201)
        self.assertEqual(self.errors.detail(key)['occurrences'], 12)

    def test_receipt_retries_conflicts_recurrence_and_lifetime_sessions(self):
        data, key = self.add()
        self.sync({key: 'ignored'})
        self.prune(dry_run=False)
        self.assertEqual(self.errors.detail(key)['data_state'], 'retired')
        self.assertEqual(self.errors.detail(key)['stack'], '')
        self.assertEqual(self.errors.upload(data)[1]['status'], 'duplicate')
        self.assertEqual(self.errors.upload(data | {'count': 1})[1]['status'], 'duplicate')
        self.assertEqual(self.errors.upload(data | {'session_id': 'c' * 32})[0], 409)
        self.assertEqual(self.errors.upload(data | {'first_seen': data['first_seen'] - 1})[0], 409)
        self.assertEqual(self.errors.detail(key)['data_state'], 'retired')
        self.assertEqual(self.errors.upload(data | {'count': 8})[0], 200)
        self.assertEqual(self.errors.detail(key)['occurrences'], 8)
        self.assertEqual(self.errors.detail(key)['stack'], data['stack'])
        self.assertEqual(self.errors.listing({})['items'][0]['sessions'], 1)
        self.assertEqual(self.errors.upload(data | {'report_id': 'd' * 32})[0], 201)
        self.assertEqual(self.errors.listing({})['items'][0]['sessions'], 1)
        self.assertEqual(self.errors.upload(data | {'report_id': 'e' * 32, 'session_id': 'e' * 32})[0], 201)
        self.assertEqual(self.errors.listing({})['items'][0]['sessions'], 2)
        self.assertEqual(self.errors.detail(key)['occurrences'], 14)
        restarted = Store(self.store.path).errors
        self.assertEqual(restarted.upload(data | {'count': 8})[1]['status'], 'duplicate')
        self.assertEqual(restarted.detail(key)['first_seen'], data['first_seen'])

    def test_retirement_releases_active_capacity(self):
        data, key = self.add()
        self.sync({key: 'ordinary'})
        self.prune(dry_run=False)
        with patch('error_reports.MAX_GROUPS', 1), patch('error_reports.MAX_REPORTS', 1):
            self.add(2, age=0)
            self.assertEqual(self.errors.upload(data | {'count': 4})[0], 507)
            self.assertEqual(self.errors.upload(data)[1]['status'], 'duplicate')
        listed = self.errors.listing({})
        self.assertEqual((listed['stored_groups'], listed['retired_groups'], listed['stored_reports'], listed['receipt_reports']), (1, 1, 1, 1))

    def test_shared_environment_survives_until_last_reference_and_restart(self):
        _, a = self.add(1)
        _, b = self.add(2)
        self.sync({a: 'ignored', b: 'protected'})
        self.assertEqual(self.prune(dry_run=False)['deleted_environments'], 0)
        with self.store.connect() as db:
            self.assertEqual(db.execute('SELECT COUNT(*) FROM error_environments').fetchone()[0], 1)
        self.assertEqual(self.errors.detail(b)['os'], 'Windows')
        Store(self.store.path)
        self.assertFalse(list(self.store.path.parent.glob('backups/before-error-compression-*')))
        self.sync({a: 'ignored', b: 'closed'}, at=self.now + 1, closed={b: self.now + 1})
        later = self.now + 7 * DAY + 1
        self.sync({a: 'ignored', b: 'closed'}, at=later, closed={b: self.now + 1})
        self.assertEqual(retention.prune(self.store, now=later, dry_run=False)['deleted_environments'], 1)
        Store(self.store.path)
        with self.store.connect() as db:
            self.assertEqual(db.execute('SELECT COUNT(*) FROM error_environments').fetchone()[0], 0)

    def test_interruption_rolls_back_and_retry_finishes(self):
        _, key = self.add()
        self.sync({key: 'ordinary'})
        with patch('error_retention.changed', side_effect=RuntimeError('simulated interruption')):
            with self.assertRaises(RuntimeError):
                self.prune(dry_run=False)
        with self.store.connect() as db:
            self.assertEqual(db.execute('SELECT COUNT(*) FROM error_reports').fetchone()[0], 1)
            self.assertEqual(db.execute('SELECT COUNT(*) FROM error_receipts').fetchone()[0], 0)
            self.assertEqual(db.execute('PRAGMA integrity_check').fetchone()[0], 'ok')
        self.assertEqual(self.errors.detail(key)['data_state'], 'active')
        self.assertEqual(self.prune(dry_run=False)['retired_groups'], 1)

    def test_concurrent_upload_and_prune_preserve_new_activity(self):
        data, key = self.add()
        self.sync({key: 'ordinary'})
        barrier = threading.Barrier(2)
        def upload():
            barrier.wait()
            return self.errors.upload(data | {'count': 11})
        def prune():
            barrier.wait()
            return self.prune(dry_run=False)
        with ThreadPoolExecutor(max_workers=2) as pool:
            uploaded, pruned = pool.submit(upload), pool.submit(prune)
            self.assertEqual(uploaded.result()[0], 200)
            pruned.result()
        self.assertEqual(self.errors.detail(key)['occurrences'], 11)
        self.assertEqual(self.errors.detail(key)['data_state'], 'active')

    def test_inventory_retired_markers_and_revision_fence(self):
        _, key = self.add()
        first = self.errors.listing({'inventory': ['1']})
        self.sync({key: 'ordinary'})
        self.prune(dry_run=False)
        with self.assertRaisesRegex(InvalidError, 'inventory_changed'):
            self.errors.listing({'inventory': ['1'], 'revision': [str(first['revision'])]})
        after = self.errors.listing({'inventory': ['1']})
        self.assertEqual(after['total'], 1)
        self.assertEqual(after['active_groups'], 0)
        self.assertEqual(after['items'][0]['data_state'], 'retired')
        self.assertGreater(after['items'][0]['retired_at'], 0)
        self.assertEqual(after['items'][0]['occurrences'], 3)

    def test_migration_grants_grace_without_guessing_update_time(self):
        legacy = Path(self.tmp.name) / 'legacy.sqlite3'
        data = fixture()
        with sqlite3.connect(legacy) as db:
            db.execute("CREATE TABLE error_groups (id TEXT PRIMARY KEY,build TEXT,mod_version TEXT,platform TEXT,source TEXT,message TEXT,stack TEXT,first_seen INTEGER,last_seen INTEGER,occurrences INTEGER,environment TEXT)")
            db.execute('INSERT INTO error_groups VALUES (?,?,?,?,?,?,?,?,?,?,?)', ('a' * 64, *[data[k] for k in ('build', 'mod_version', 'platform', 'source', 'message', 'stack', 'first_seen', 'last_seen', 'count')], '{}'))
        db.close()
        migrated = Store(legacy)
        self.assertGreaterEqual(migrated.errors.detail('a' * 64)['last_activity'], self.now)
        self.assertTrue(list((legacy.parent / 'backups').glob('before-error-retention-*')))
        with migrated.connect() as db:
            db.execute('UPDATE error_groups SET last_activity=123')
        self.assertEqual(Store(legacy).errors.detail('a' * 64)['last_activity'], 123)

    def test_old_receipt_updates_do_not_consume_new_report_daily_budget(self):
        data, key = self.add()
        self.sync({key: 'ordinary'})
        self.prune(dry_run=False)
        with patch('error_reports.DAILY_REPORTS', 1):
            self.assertEqual(self.errors.upload(data | {'count': 4})[0], 200)
            self.assertEqual(self.errors.upload(data | {'report_id': 'c' * 32})[0], 201)
            self.assertEqual(self.errors.upload(data | {'report_id': 'd' * 32})[0], 429)

    def test_suspended_policy_can_be_restored_only_by_matching_replay(self):
        _, key = self.add()
        self.sync({key: 'ignored'})
        with self.store.connect() as db:
            db.execute('UPDATE error_retention_meta SET valid_until=0')
        self.assertEqual(self.prune()['candidate_groups'], 0)
        self.assertEqual(self.sync({key: 'ignored'})['status'], 'duplicate')
        self.assertEqual(self.prune()['candidate_groups'], 1)
        with self.assertRaises(ValueError):
            self.sync({key: 'ordinary'})

    def test_policy_replay_invalid_and_failed_sync_keep_previous_protection(self):
        _, key = self.add()
        self.sync({key: 'protected'})
        self.assertEqual(self.sync({key: 'protected'})['status'], 'duplicate')
        with self.assertRaises(ValueError):
            self.sync({key: 'ordinary'})
        with self.assertRaises(ValueError):
            self.sync({key: 'ignored'}, at=self.now - 1)
        self.assertEqual(self.prune()['retained'], {'protected': 1})


if __name__ == '__main__':
    unittest.main()
