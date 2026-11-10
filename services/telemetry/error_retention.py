"""Transactional error lifecycle and conservative, fingerprint-based review policy."""
import hashlib
import json
import os
import time

DAY = 86400


def session_key(session):
    return hashlib.sha256(session.encode()).digest()


def report_identity(item):
    return hashlib.sha256(json.dumps([item['session_id'], item['fingerprint'], item['first_seen']],
                                     separators=(',', ':')).encode()).digest()


def initialize(db, now):
    columns = {row[1] for row in db.execute('PRAGMA table_info(error_groups)')}
    for name, declaration in (('last_activity', 'INTEGER NOT NULL DEFAULT 0'),
                              ('retired_at', 'INTEGER NOT NULL DEFAULT 0'),
                              ('retention_reason', "TEXT NOT NULL DEFAULT ''")):
        if name not in columns:
            db.execute('ALTER TABLE error_groups ADD COLUMN ' + name + ' ' + declaration)
    # Past count updates have no receipt timestamp. Start a full grace period once.
    if 'last_activity' not in columns:
        db.execute('UPDATE error_groups SET last_activity=?', (now,))
    db.execute('CREATE INDEX IF NOT EXISTS errors_active ON error_groups(retired_at,last_activity)')
    db.execute("""CREATE TABLE IF NOT EXISTS error_receipts (
        id TEXT PRIMARY KEY, group_id TEXT NOT NULL, identity BLOB NOT NULL, count INTEGER NOT NULL)""")
    db.execute('CREATE INDEX IF NOT EXISTS error_receipts_group ON error_receipts(group_id)')
    db.execute("""CREATE TABLE IF NOT EXISTS error_sessions (
        group_id TEXT NOT NULL, session_key BLOB NOT NULL, PRIMARY KEY(group_id,session_key))""")
    db.execute("""CREATE TABLE IF NOT EXISTS error_retention_meta (
        singleton INTEGER PRIMARY KEY CHECK(singleton=1), revision INTEGER NOT NULL,
        generated_at INTEGER NOT NULL, valid_until INTEGER NOT NULL)""")
    fresh = db.execute('INSERT OR IGNORE INTO error_retention_meta VALUES (1,0,0,0)').rowcount
    if fresh:
        for row in db.execute('SELECT DISTINCT group_id,session FROM error_reports'):
            db.execute('INSERT OR IGNORE INTO error_sessions VALUES (?,?)', (row['group_id'], session_key(row['session'])))
    db.execute("""CREATE TABLE IF NOT EXISTS error_retention_policy (
        id TEXT PRIMARY KEY, status TEXT NOT NULL, closed_at INTEGER NOT NULL)""")
    db.execute("""CREATE TABLE IF NOT EXISTS error_retention_runs (
        id INTEGER PRIMARY KEY, completed INTEGER NOT NULL, result TEXT NOT NULL)""")


def revision(db):
    return db.execute('SELECT revision FROM error_retention_meta WHERE singleton=1').fetchone()[0]


def changed(db):
    db.execute('UPDATE error_retention_meta SET revision=revision+1 WHERE singleton=1')


def sync_policy(store, policy, now=None):
    now = int(time.time()) if now is None else now
    if not isinstance(policy, dict) or set(policy) != {'schema_version', 'generated_at', 'valid_until', 'groups'}:
        raise ValueError('invalid_retention_policy')
    generated, expires, groups = policy['generated_at'], policy['valid_until'], policy['groups']
    if (type(policy['schema_version']) is not int or policy['schema_version'] != 1 or type(generated) is not int or type(expires) is not int
            or not 0 < generated <= now + 300 or not max(now, generated) < expires <= generated + 3 * DAY
            or not isinstance(groups, dict) or not groups):
        raise ValueError('invalid_or_stale_retention_policy')
    import re
    for fingerprint, entry in groups.items():
        if (not re.fullmatch('[0-9a-f]{64}', fingerprint) or not isinstance(entry, dict)
                or set(entry) != {'status', 'closed_at'} or entry['status'] not in {'ordinary', 'ignored', 'protected', 'closed'}
                or type(entry['closed_at']) is not int):
            raise ValueError('invalid_retention_mapping')
        if entry['status'] == 'closed':
            if not 0 < entry['closed_at'] <= generated:
                raise ValueError('invalid_closure_time')
        elif entry['closed_at'] != 0:
            raise ValueError('unexpected_closure_time')
    with store.connect() as db:
        db.execute('BEGIN IMMEDIATE')
        meta = db.execute('SELECT * FROM error_retention_meta').fetchone()
        if generated < meta['generated_at']:
            raise ValueError('policy_would_move_backwards')
        old = {row['id']: dict(row) for row in db.execute('SELECT * FROM error_retention_policy')}
        for fingerprint, entry in old.items():
            incoming = groups.get(fingerprint)
            if entry['status'] == 'protected':
                if incoming is None or incoming['status'] not in {'protected', 'closed'}:
                    raise ValueError('protected_mapping_requires_explicit_closure')
                if incoming['status'] == 'closed' and incoming['closed_at'] < meta['generated_at']:
                    raise ValueError('closure_predates_protection')
            if entry['status'] == 'closed' and (incoming is None or incoming['status'] != 'protected'):
                if incoming is None or incoming['status'] != 'closed' or incoming['closed_at'] != entry['closed_at']:
                    raise ValueError('closure_time_must_remain_stable')
        if generated == meta['generated_at']:
            previous = {key: {name: value[name] for name in ('status', 'closed_at')} for key, value in old.items()}
            if previous != groups or meta['valid_until'] not in (0, expires):
                raise ValueError('same_policy_time_has_different_content')
            db.execute('UPDATE error_retention_meta SET valid_until=? WHERE singleton=1', (expires,))
            return {'status': 'duplicate', 'mappings': len(groups)}
        db.execute('DELETE FROM error_retention_policy')
        db.executemany('INSERT INTO error_retention_policy VALUES (?,?,?)',
                       [(key, entry['status'], entry['closed_at']) for key, entry in groups.items()])
        db.execute('UPDATE error_retention_meta SET generated_at=?,valid_until=? WHERE singleton=1', (generated, expires))
    return {'status': 'ok', 'mappings': len(groups), 'valid_until': expires}


def space(db):
    pages = db.execute('PRAGMA page_count').fetchone()[0]
    free = db.execute('PRAGMA freelist_count').fetchone()[0]
    size = db.execute('PRAGMA page_size').fetchone()[0]
    return {'pages': pages, 'free_pages': free, 'free_bytes': free * size,
            'vacuum_recommended': free * size >= 16 * 1024**2 and free >= pages * 0.2}


def prune(store, dry_run=True, now=None, ordinary_days=None, ignored_days=None):
    started = time.monotonic()
    now = int(time.time()) if now is None else now
    ordinary_days = int(os.environ.get('ERROR_RETENTION_DAYS', '7')) if ordinary_days is None else ordinary_days
    ignored_days = int(os.environ.get('ERROR_IGNORED_RETENTION_DAYS', '3')) if ignored_days is None else ignored_days
    if not 1 <= ordinary_days <= 3650 or not 1 <= ignored_days <= 3650:
        raise ValueError('invalid_error_retention_days')
    result = {'dry_run': dry_run, 'at': now, 'ordinary_days': ordinary_days, 'ignored_days': ignored_days,
              'candidates': {}, 'retained': {}, 'candidate_groups': 0, 'candidate_reports': 0,
              'body_bytes': 0, 'estimated_removed_body_bytes': 0, 'orphan_environments': 0, 'orphan_environment_bytes': 0,
              'deleted_reports': 0, 'retired_groups': 0, 'deleted_environments': 0,
              'failure_scope': 'none'}
    with store.connect() as db:
        # Same writer lock as upload and policy synchronization, including candidate selection.
        db.execute('BEGIN' if dry_run else 'BEGIN IMMEDIATE')
        meta = db.execute('SELECT * FROM error_retention_meta').fetchone()
        result['policy_generated_at'] = meta['generated_at']
        result['policy_valid_until'] = meta['valid_until']
        rows = db.execute("""SELECT g.id,g.last_activity,g.retired_at,p.status,p.closed_at,
            substr(g.message,1,160) AS summary,
            length(CAST(g.message AS BLOB))+length(CAST(g.stack AS BLOB)) AS body_bytes,
            (SELECT COUNT(*) FROM error_reports r WHERE r.group_id=g.id) AS reports
            FROM error_groups g LEFT JOIN error_retention_policy p ON p.id=g.id""").fetchall()
        candidates = []
        for row in rows:
            eligible = False
            if row['retired_at']:
                reason = 'already_retired'
            elif not meta['generated_at'] or now >= meta['valid_until']:
                reason = 'policy_missing_or_stale'
            elif row['status'] is None:
                reason = 'unmapped'
            elif row['status'] == 'protected':
                reason = 'protected'
            elif not row['last_activity']:
                reason = 'activity_unknown'
            else:
                days = ignored_days if row['status'] == 'ignored' else ordinary_days
                anchor = max(row['last_activity'], row['closed_at'] or 0)
                eligible = now >= anchor + days * DAY
                reason = row['status'] if eligible else 'within_retention'
            bucket = result['candidates' if eligible else 'retained']
            bucket[reason] = bucket.get(reason, 0) + 1
            if eligible:
                candidates.append(row['id'])
                result['candidate_reports'] += row['reports']
                body_bytes = row['body_bytes']
                result['body_bytes'] += body_bytes
                summary = row['summary'].splitlines()[0] if row['summary'] else ''
                result['estimated_removed_body_bytes'] += body_bytes - len(summary.encode())
        candidate_set = set(candidates)
        result['candidate_groups'] = len(candidates)
        db.execute('CREATE TEMP TABLE retention_candidates (id TEXT PRIMARY KEY)')
        db.executemany('INSERT INTO retention_candidates VALUES (?)', [(key,) for key in candidates])
        orphans = db.execute("""SELECT key,length(payload)+length(CAST(metadata AS BLOB)) AS bytes
            FROM error_environments e WHERE NOT EXISTS (
                SELECT 1 FROM error_groups g WHERE g.environment_id=e.key
                AND g.id NOT IN (SELECT id FROM retention_candidates))""").fetchall()
        result['orphan_environments'] = len(orphans)
        result['orphan_environment_bytes'] = sum(row['bytes'] for row in orphans)
        result['space_before'] = space(db)
        if not dry_run:
            for row in db.execute('SELECT * FROM error_reports WHERE group_id IN (SELECT id FROM retention_candidates)'):
                identity = report_identity({'session_id': row['session'], 'fingerprint': row['group_id'], 'first_seen': row['first_seen']})
                db.execute('INSERT INTO error_receipts VALUES (?,?,?,?)', (row['id'], row['group_id'], identity, row['count']))
            result['deleted_reports'] = db.execute('DELETE FROM error_reports WHERE group_id IN (SELECT id FROM retention_candidates)').rowcount
            for row in rows:
                if row['id'] in candidate_set:
                    summary = row['summary'].splitlines()[0] if row['summary'] else ''
                    db.execute("""UPDATE error_groups SET message=?,stack='',environment='{}',environment_id=NULL,
                        retired_at=?,retention_reason=? WHERE id=?""", (summary, now, row['status'], row['id']))
            result['retired_groups'] = len(candidates)
            result['deleted_environments'] = db.execute("""DELETE FROM error_environments WHERE NOT EXISTS (
                SELECT 1 FROM error_groups WHERE environment_id=error_environments.key)""").rowcount
            if candidates:
                changed(db)
            result['space_after'] = space(db)
            result['duration_ms'] = round((time.monotonic() - started) * 1000)
            db.execute('INSERT INTO error_retention_runs(completed,result) VALUES (?,?)', (now, json.dumps(result)))
            db.execute('DELETE FROM error_retention_runs WHERE id NOT IN (SELECT id FROM error_retention_runs ORDER BY id DESC LIMIT 90)')
    result['duration_ms'] = round((time.monotonic() - started) * 1000)
    return result


def inventory(store, query):
    cursor = query.get('after', [''])[0]
    import re
    if cursor and not re.fullmatch('[0-9a-f]{64}', cursor):
        raise ValueError('invalid_inventory_cursor')
    with store.connect() as db:
        db.execute('BEGIN')
        current = revision(db)
        expected = query.get('revision', [str(current)])[0]
        if str(current) != expected:
            raise ValueError('inventory_changed')
        rows = db.execute("""SELECT id,build,mod_version,platform,source,message,first_seen,last_seen,occurrences,
            last_activity,retired_at,retention_reason,
            (SELECT COUNT(*) FROM error_sessions s WHERE s.group_id=g.id) AS sessions
            FROM error_groups g WHERE id>? ORDER BY id LIMIT 501""", (cursor,)).fetchall()
        total = db.execute('SELECT COUNT(*) FROM error_groups').fetchone()[0]
        active = db.execute('SELECT COUNT(*) FROM error_groups WHERE retired_at=0').fetchone()[0]
        reports = db.execute('SELECT COUNT(*) FROM error_reports').fetchone()[0]
        receipts = db.execute('SELECT COUNT(*) FROM error_receipts').fetchone()[0]
    items = [dict(row) | {'data_state': 'retired' if row['retired_at'] else 'active'} for row in rows[:500]]
    return {'schema_version': 1, 'revision': current, 'items': items, 'total': total,
            'active_groups': active, 'retired_groups': total - active, 'active_reports': reports,
            'receipt_reports': receipts, 'next': items[-1]['id'] if len(rows) > 500 else None}
