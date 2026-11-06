"""Bounded error collection, separate from run statistics and raw history archives."""
from protocol import core_context

import hashlib
import json
import re
import threading
import time
from pathlib import Path

MAX_BODY = 64 * 1024
MAX_GROUPS = 3000
MAX_REPORTS = 50000
DAILY_REPORTS = 2000
SOURCES = {"game", "engine", "shader", "stderr", "managed"}


class InvalidError(ValueError):
    pass


def clean(text, limit):
    text = re.sub(r"(?:https?|wss?)://[^\s<>\"']+", "<url>", text, flags=re.I)
    # A drive letter must not be the tail of a resource URI scheme (res://, event:/).
    text = re.sub(r"(?:(^|[^a-z])[a-z]:[\\/]|\\\\)[^\r\n\"<>|]+", r"\1<path>", text, flags=re.I)
    text = re.sub(r"/(?:home|Users|data|storage|sdcard|tmp|var)/[^\r\n\"<>|]+", "<path>", text, flags=re.I)
    text = re.sub(r"[\w.+-]{1,128}@[\w.-]{1,128}\.[a-z]{2,24}", "<email>", text, flags=re.I)
    text = re.sub(r"\b(?:\d{1,3}\.){3}\d{1,3}\b|\b\d{15,20}\b", "<id>", text)
    sensitive = r"\b(?:authorization|bearer|token|password|passwd|secret|api[_-]?key|steam[_-]?id|player[_-]?id|user[_-]?name|player[_-]?name)\b"
    text = re.sub(sensitive + r"[\"']?\s*[:=]\s*(?:\"[^\"\r\n]*\"|'[^'\r\n]*')", "<redacted>", text, flags=re.I)
    text = re.sub(sensitive + r"[\"'\s:=]+(?:(?:bearer|basic)\s+)?[^\s,\"';]+", "<redacted>", text, flags=re.I)
    return re.sub(r"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]", "", text)[:limit]


def normalize(text):
    text = re.sub(r"\b[0-9a-f]{8}-[0-9a-f-]{27,}\b|\b0x[0-9a-f]+\b|\b[0-9a-f]{24,}\b|\b\d+\b", "#", text, flags=re.I)
    return re.sub(r"\s+", " ", text).strip()


def validate(data):
    fields = {"schema_version", "report_id", "session_id", "build", "mod_version", "platform",
              "source", "message", "stack", "first_seen", "last_seen", "count",
              "os", "game_commit", "mods", "mods_truncated"}
    version = data.get("schema_version") if isinstance(data, dict) else None
    expected = fields | ({"core_telemetry"} if version == 2 else set())
    if not isinstance(data, dict) or set(data) != expected or type(version) is not int or version not in (1, 2):
        raise InvalidError("invalid_error_schema")
    result = dict(data)
    if version == 2:
        try:
            result["core_telemetry"] = core_context(data["core_telemetry"])
        except ValueError as exc:
            raise InvalidError(str(exc)) from exc
    for name in ("report_id", "session_id"):
        if not isinstance(data[name], str) or not re.fullmatch(r"[0-9a-f]{32}", data[name]):
            raise InvalidError("invalid_" + name)
    for name, limit in (("build", 80), ("mod_version", 80), ("platform", 40), ("source", 20), ("message", 2048), ("stack", 8192), ("os", 40), ("game_commit", 80)):
        value = data[name]
        if not isinstance(value, str) or len(value) > limit or (name != "stack" and not value.strip()):
            raise InvalidError("invalid_" + name)
        result[name] = clean(value, limit)
    if result["source"] not in SOURCES:
        raise InvalidError("invalid_source")
    if result["platform"] not in ("pc", "android"):
        raise InvalidError("invalid_platform")
    if not isinstance(data["mods"], list) or len(data["mods"]) > 128 or type(data["mods_truncated"]) is not bool:
        raise InvalidError("invalid_mods")
    mods, ids = [], set()
    for mod in data["mods"]:
        if not isinstance(mod, dict) or set(mod) != {"id", "name", "version"}:
            raise InvalidError("invalid_mod")
        bounded = {}
        for key, limit in (("id", 96), ("name", 80), ("version", 40)):
            if not isinstance(mod[key], str) or not mod[key].strip() or len(mod[key]) > limit:
                raise InvalidError("invalid_mod_" + key)
            bounded[key] = clean(mod[key], limit)
        if bounded["id"] in ids:
            raise InvalidError("duplicate_mod_id")
        ids.add(bounded["id"])
        mods.append(bounded)
    mods.sort(key=lambda mod: mod["id"])
    if len(json.dumps(mods, ensure_ascii=False, separators=(",", ":")).encode()) > 16384:
        raise InvalidError("mods_too_large")
    result["mods"] = mods
    for name in ("first_seen", "last_seen", "count"):
        if type(data[name]) is not int:
            raise InvalidError("invalid_" + name)
    if not 1 <= data["count"] <= 1000000:
        raise InvalidError("invalid_count")
    if not 0 < data["first_seen"] <= data["last_seen"] <= int(time.time()) + 300:
        raise InvalidError("invalid_error_time")
    # The server computes the fingerprint; client-provided hashes are not trusted.
    identity = [result[name] for name in ("build", "mod_version", "platform", "source")]
    identity += [result["os"], result["game_commit"], data["mods_truncated"], [(mod["id"], mod["version"]) for mod in mods]]
    identity += [normalize(result["message"]), normalize(result["stack"])]
    if version == 2:
        identity.append(result["core_telemetry"])
    result["fingerprint"] = hashlib.sha256(json.dumps(identity, ensure_ascii=False).encode()).hexdigest()
    return result


class ErrorLimiter:
    """Token buckets before body parsing; bounded memory, no persisted IP addresses."""
    def __init__(self, clock=time.monotonic):
        self.clock = clock
        self.lock = threading.Lock()
        self.global_bucket = (60., clock())
        self.clients = {}

    def accept(self, source):
        with self.lock:
            now = self.clock()
            tokens, then = self.global_bucket
            tokens = min(60., tokens + max(0, now - then))
            self.global_bucket = (tokens, now)
            if tokens < 1:
                return False
            # Even rejected per-IP traffic consumes the global parsing budget.
            self.global_bucket = (tokens - 1, now)
            if len(self.clients) >= 2048:
                self.clients = {key: value for key, value in self.clients.items() if now - value[1] < 120}
                if source not in self.clients and len(self.clients) >= 2048:
                    return False
            available, previous = self.clients.get(source, (6., now))
            available = min(6., available + max(0, now - previous) / 10)
            allowed = available >= 1
            self.clients[source] = (available - 1 if allowed else available, now)
            return allowed


class ErrorStore:
    def __init__(self, store):
        self.store = store
        with store.connect() as db:
            db.execute("""CREATE TABLE IF NOT EXISTS error_groups (
                id TEXT PRIMARY KEY, build TEXT NOT NULL, mod_version TEXT NOT NULL,
                platform TEXT NOT NULL, source TEXT NOT NULL, message TEXT NOT NULL, stack TEXT NOT NULL,
                first_seen INTEGER NOT NULL, last_seen INTEGER NOT NULL, occurrences INTEGER NOT NULL,
                environment TEXT NOT NULL DEFAULT '{}')""")
            if "environment" not in {row[1] for row in db.execute("PRAGMA table_info(error_groups)")}:
                db.execute("ALTER TABLE error_groups ADD COLUMN environment TEXT NOT NULL DEFAULT '{}'")
            db.execute("CREATE INDEX IF NOT EXISTS errors_last ON error_groups(last_seen DESC,id)")
            db.execute("""CREATE TABLE IF NOT EXISTS error_reports (
                id TEXT PRIMARY KEY, session TEXT NOT NULL, group_id TEXT NOT NULL,
                count INTEGER NOT NULL, first_seen INTEGER NOT NULL, last_seen INTEGER NOT NULL,
                received INTEGER NOT NULL)""")
            db.execute("CREATE INDEX IF NOT EXISTS error_reports_group ON error_reports(group_id,session)")
            db.execute("CREATE INDEX IF NOT EXISTS error_reports_received ON error_reports(received)")

    def upload(self, data):
        item = validate(data)
        now = int(time.time())
        with self.store.connect() as db:
            db.execute("BEGIN IMMEDIATE")
            old = db.execute("SELECT * FROM error_reports WHERE id=?", (item["report_id"],)).fetchone()
            if old and (old["session"] != item["session_id"] or old["group_id"] != item["fingerprint"] or old["first_seen"] != item["first_seen"]):
                return 409, {"error": "error_report_identity_conflict"}
            if old and item["count"] <= old["count"]:
                return 200, {"status": "duplicate", "id": item["fingerprint"]}
            if sum(p.stat().st_size for p in (self.store.path, Path(str(self.store.path) + "-wal")) if p.exists()) >= 1024**3:
                return 507, {"error": "storage_capacity_reached"}
            if not old:
                if db.execute("SELECT COUNT(*) FROM error_reports WHERE received>=?", (now - 86400,)).fetchone()[0] >= DAILY_REPORTS:
                    return 429, {"error": "daily_error_budget_reached"}
                if db.execute("SELECT COUNT(*) FROM error_reports").fetchone()[0] >= MAX_REPORTS:
                    return 507, {"error": "error_report_capacity_reached"}
            group = db.execute("SELECT id FROM error_groups WHERE id=?", (item["fingerprint"],)).fetchone()
            if not group:
                if db.execute("SELECT COUNT(*) FROM error_groups").fetchone()[0] >= MAX_GROUPS:
                    return 507, {"error": "error_group_capacity_reached"}
                environment = {key: item[key] for key in ("os", "game_commit", "mods", "mods_truncated")}
                if "core_telemetry" in item:
                    environment["core_telemetry"] = item["core_telemetry"]
                db.execute("INSERT INTO error_groups VALUES (?,?,?,?,?,?,?,?,?,0,?)",
                           tuple(item[key] for key in ("fingerprint", "build", "mod_version", "platform", "source", "message", "stack", "first_seen", "last_seen")) + (json.dumps(environment, ensure_ascii=False),))
            delta = item["count"] - (old["count"] if old else 0)
            db.execute("""UPDATE error_groups SET occurrences=occurrences+?,
                first_seen=MIN(first_seen,?),last_seen=MAX(last_seen,?) WHERE id=?""",
                       (delta, item["first_seen"], item["last_seen"], item["fingerprint"]))
            if old:
                db.execute("UPDATE error_reports SET count=?,last_seen=MAX(last_seen,?) WHERE id=?",
                           (item["count"], item["last_seen"], item["report_id"]))
            else:
                db.execute("INSERT INTO error_reports VALUES (?,?,?,?,?,?,?)",
                           (item["report_id"], item["session_id"], item["fingerprint"], item["count"], item["first_seen"], item["last_seen"], now))
        return 200 if old else 201, {"status": "ok", "id": item["fingerprint"]}

    def listing(self, query):
        clauses, values = [], []
        for name in ("build", "mod_version", "source", "platform"):
            value = query.get(name, ["all"])[0]
            if len(value) > 80:
                raise InvalidError("invalid_filter")
            if value != "all":
                clauses.append(name + "=?")
                values.append(value)
        for name in ("core_version", "game_target"):
            value = query.get(name, ["all"])[0]
            if len(value) > 120:
                raise InvalidError("invalid_filter")
            if value != "all":
                clauses.append("json_extract(environment, '$.core_telemetry." + name + "')=?")
                values.append(value)
        source_mod = query.get("source_mod", ["all"])[0]
        if len(source_mod) > 180:
            raise InvalidError("invalid_filter")
        if source_mod != "all":
            clauses.append("(EXISTS (SELECT 1 FROM json_each(error_groups.environment, '$.mods') AS mod WHERE json_extract(mod.value, '$.id')=?) OR EXISTS (SELECT 1 FROM json_each(error_groups.environment, '$.core_telemetry.source_mods') AS mod WHERE json_extract(mod.value, '$.id')=?))")
            values.extend((source_mod, source_mod))
        search = query.get("search", [""])[0].strip()
        if len(search) > 120:
            raise InvalidError("invalid_search")
        if search:
            clauses.append("instr(lower(message || ' ' || stack),lower(?))>0")
            values.append(search)
        where = " WHERE " + " AND ".join(clauses) if clauses else ""
        order = {"recent": "last_seen DESC,id", "count": "occurrences DESC,id", "sessions": "sessions DESC,id"}.get(query.get("sort", ["recent"])[0])
        if order is None:
            raise InvalidError("invalid_sort")
        try:
            page = max(1, min(100000, int(query.get("page", ["1"])[0])))
        except ValueError as exc:
            raise InvalidError("invalid_page") from exc
        with self.store.connect() as db:
            total = db.execute("SELECT COUNT(*) FROM error_groups" + where, values).fetchone()[0]
            page = min(page, max(1, (total + 19) // 20))
            rows = db.execute("SELECT id,build,mod_version,platform,source,message,first_seen,last_seen,occurrences,environment,"
                              "(SELECT COUNT(DISTINCT session) FROM error_reports WHERE group_id=error_groups.id) AS sessions "
                              "FROM error_groups" + where + " ORDER BY " + order + " LIMIT 20 OFFSET ?", values + [(page - 1) * 20]).fetchall()
            builds = [row[0] for row in db.execute("SELECT DISTINCT build FROM error_groups ORDER BY build")]
            versions = [row[0] for row in db.execute("SELECT DISTINCT mod_version FROM error_groups ORDER BY mod_version")]
            contexts = [json.loads(row[0]) for row in db.execute("SELECT DISTINCT environment FROM error_groups")]
            core_versions = sorted({value["core_telemetry"]["core_version"] for value in contexts if value.get("core_telemetry")})
            source_mods = sorted({mod["id"] for value in contexts for mod in value.get("mods", []) + value.get("core_telemetry", {}).get("source_mods", [])})
            stored = db.execute("SELECT COUNT(*) FROM error_reports").fetchone()[0]
        return {"items": [dict(row) | {"core_telemetry": json.loads(row["environment"]).get("core_telemetry")} for row in rows], "total": total, "page": page,
                "pages": max(1, (total + 19) // 20), "builds": builds, "mod_versions": versions,
                "stored_reports": stored, "report_limit": MAX_REPORTS, "group_limit": MAX_GROUPS,
                "verification": "unverified_client", "core_versions": core_versions, "source_mods": source_mods}

    def detail(self, fingerprint):
        with self.store.connect() as db:
            row = db.execute("SELECT * FROM error_groups WHERE id=?", (fingerprint,)).fetchone()
        if not row:
            return None
        result = dict(row)
        environment = json.loads(result.pop("environment"))
        return result | environment | {"verification": "unverified_client"}
