"""Core history collector. Python 3.12+, SQLite; private HTTP behind nginx."""
from __future__ import annotations

import argparse
from contextlib import contextmanager, closing
import gzip
import hashlib
import hmac
import json
import logging
import math
import os
from pathlib import Path
import re
import socket
import sqlite3
import threading
import time
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit

from protocol import core_context
from raw_archive import RawArchive, utc_day
from error_reports import ErrorStore, ErrorLimiter, InvalidError, MAX_BODY as MAX_ERROR_BODY

ROOT = Path(__file__).resolve().parent
WEB = Path(os.environ.get("TELEMETRY_WEB_ROOT", str(ROOT / "web" if (ROOT / "web").is_dir() else ROOT.parents[1] / "web/telemetry")))
MAX_BODY = 8 * 1024 * 1024
SHINE_VALUES = {"karen_shine_current", "karen_shine_max", "core_shine_current", "core_shine_max"}
STARTERS = {"KAREN_STRIKE", "KAREN_DEFEND", "KAREN_SHINE_STRIKE", "KAREN_FALL"}
MODEL = re.compile(r"[A-Za-z0-9_.:\-]{1,180}\Z")
CATALOG = json.loads((WEB / "catalog.json").read_text(encoding="utf-8"))
LOG = logging.getLogger("karen-telemetry")
FACTS_VERSION = 3


class Invalid(ValueError):
    pass


class CapacityError(OSError):
    pass


def dumps(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), allow_nan=False)


def model(value):
    if isinstance(value, dict):
        value = value.get("Entry", value.get("entry"))
    if not isinstance(value, str) or not MODEL.fullmatch(value):
        raise Invalid("invalid_model_id")
    return value.rsplit(".", 1)[-1].upper()


def full_model(value):
    if not isinstance(value, str) or not MODEL.fullmatch(value):
        raise Invalid("invalid_full_model_id")
    return value


def card_id(card):
    if not isinstance(card, dict):
        raise Invalid("invalid_card")
    if "current_upgrade_level" in card:
        integer(card["current_upgrade_level"], -100, 100000, "current_upgrade_level")
    if "model_full_id" in card:
        identity = full_model(card["model_full_id"])
        # Native Category.Entry identities keep the established statistics key.
        # Full identities with additional namespace segments stay distinct.
        return model(identity) if identity.startswith("CARD.") and identity.count(".") == 1 else identity
    return model(card.get("id"))


def collection(obj, key, limit=3000):
    value = obj.get(key, [])
    if not isinstance(value, list) or len(value) > limit:
        raise Invalid("invalid_" + key)
    return value


def validate_display_fields(data):
    """Validate structures the reader uses, while retaining unknown extension fields."""
    for player in collection(data, "players", 16):
        if not isinstance(player, dict):
            raise Invalid("invalid_player")
        for relic in collection(player, "relics"):
            model(relic.get("id", relic) if isinstance(relic, dict) else relic)
    for act in collection(data, "map_point_history", 20):
        if not isinstance(act, list) or len(act) > 300:
            raise Invalid("invalid_act_history")
        for entry in act:
            if not isinstance(entry, dict):
                raise Invalid("invalid_map_entry")
            for room in collection(entry, "rooms", 32):
                if not isinstance(room, dict):
                    raise Invalid("invalid_room")
                if room.get("model_id") is not None:
                    model(room["model_id"])
                if "turns_taken" in room:
                    integer(room["turns_taken"], 0, 100000, "turns_taken")
            for stat in collection(entry, "player_stats", 16):
                if not isinstance(stat, dict):
                    raise Invalid("invalid_player_stats")
                for choice in collection(stat, "rest_site_choices", 100):
                    if not isinstance(choice, str) or len(choice) > 1000:
                        raise Invalid("invalid_rest_site_choice")
                for key in ("current_hp", "max_hp", "current_gold", "damage_taken"):
                    if key in stat:
                        integer(stat[key], -2**31, 2**31 - 1, key)


def integer(value, low, high, field):
    if type(value) is not int or not low <= value <= high:
        raise Invalid("invalid_" + field)
    return value


def player_id(value):
    if type(value) is int and 0 <= value <= 2**64 - 1:
        return str(value)
    if isinstance(value, str) and re.fullmatch(r"[0-9]{1,20}", value) and int(value) <= 2**64 - 1:
        return str(int(value))
    raise Invalid("invalid_player_id")


def enum_name(value, names):
    if type(value) is int and 0 <= value < len(names):
        return names[value]
    if isinstance(value, str) and len(value) <= 80:
        return value.lower().replace("_", "")
    raise Invalid("invalid_enum")


def sanitize(value, depth=0):
    """Discard only the expressly unwanted Shine counters, including SavedProperty entries."""
    if depth > 80:
        raise Invalid("json_too_deep")
    if isinstance(value, dict):
        return {k: sanitize(v, depth + 1) for k, v in value.items() if k not in SHINE_VALUES}
    if isinstance(value, list):
        return [sanitize(v, depth + 1) for v in value
                if not (isinstance(v, dict) and v.get("name") in SHINE_VALUES)]
    if isinstance(value, float) and not math.isfinite(value):
        raise Invalid("invalid_number")
    return value


def strict_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise Invalid("duplicate_json_key")
        result[key] = value
    return result


def parse_history(body):
    try:
        data = json.loads(body.decode("utf-8-sig"), object_pairs_hook=strict_object,
                          parse_constant=lambda _: (_ for _ in ()).throw(Invalid("invalid_number")))
    except (UnicodeError, ValueError, RecursionError) as exc:
        raise Invalid("invalid_json") from exc
    if not isinstance(data, dict):
        raise Invalid("expected_run_history")
    return sanitize(data)


def legacy_session(manifest):
    """r6 expects a token-shaped response; it grants no permission or identity."""
    if not isinstance(manifest, dict) or not re.fullmatch(r"[0-9a-f]{64}", str(manifest.get("sha256", ""))):
        raise Invalid("invalid_payload_sha256")
    integer(manifest.get("bytes"), 1, MAX_BODY, "payload_size")
    return 201, {"upload_token": "legacy-client-compatibility", "deprecated": True,
                 "scope": "compatibility_only"}


def derive(data, strict_display=True):
    context = None
    if "core_telemetry" in data:
        try:
            context = core_context(data["core_telemetry"])
        except ValueError as exc:
            raise Invalid(str(exc)) from exc
    if strict_display:
        validate_display_fields(data)
    start = integer(data.get("start_time"), 1, int(time.time()) + 86400, "start_time")
    seed = data.get("seed")
    if not isinstance(seed, str) or not 1 <= len(seed) <= 256:
        raise Invalid("invalid_seed")
    for field in ("win", "was_abandoned"):
        if type(data.get(field)) is not bool:
            raise Invalid("invalid_" + field)
    if data["win"] and data["was_abandoned"]:
        raise Invalid("conflicting_outcome")
    asc = integer(data.get("ascension"), 0, 100, "ascension")
    duration = data.get("run_time", 0)
    if type(duration) not in (int, float) or not math.isfinite(duration) or not 0 <= duration <= 31536000:
        raise Invalid("invalid_run_time")
    build = data.get("build_id")
    if not isinstance(build, str) or not 1 <= len(build) <= 120:
        raise Invalid("invalid_build_id")
    mode = enum_name(data.get("game_mode", "standard"), ["none", "standard", "daily", "custom"])
    modifiers = collection(data, "modifiers", 100)
    default_start = mode == "standard" and not modifiers
    platform = str(data.get("platform_type", "unknown"))[:80]
    players = data.get("players")
    if not isinstance(players, list) or not 1 <= len(players) <= 16:
        raise Invalid("invalid_players")
    seats = {}
    for player in players:
        if not isinstance(player, dict):
            raise Invalid("invalid_player")
        pid = player_id(player.get("id"))
        if pid in seats:
            raise Invalid("duplicate_player_id")
        character = model(player.get("character"))
        held = set(STARTERS) if character == "KAREN" and default_start else set()
        for field in ("deck", "karen_shine_exhaust_cards", "core_shine_exhaust_cards"):
            cards = player.get(field, [])
            if not isinstance(cards, list) or len(cards) > 3000:
                raise Invalid("invalid_" + field)
            held.update(card_id(c) for c in cards)
        seats[pid] = {"id": pid, "character": character, "held": held,
                      "offers": [], "observed": [], "missing_picked": 0}
    acts = data.get("map_point_history")
    if not isinstance(acts, list) or len(acts) > 20:
        raise Invalid("invalid_map_history")
    floor = 0
    for act_index, entries in enumerate(acts):
        if not isinstance(entries, list) or len(entries) > 300:
            raise Invalid("invalid_act_history")
        for entry in entries:
            floor += 1
            if floor > 1000 or not isinstance(entry, dict):
                raise Invalid("invalid_map_entry")
            stats = entry.get("player_stats", [])
            if not isinstance(stats, list) or len(stats) > len(seats):
                raise Invalid("invalid_player_stats")
            seen = set()
            for stat in stats:
                if not isinstance(stat, dict):
                    raise Invalid("invalid_player_stats")
                pid = player_id(stat.get("player_id"))
                if pid not in seats or pid in seen:
                    raise Invalid("unmatched_player_stats")
                seen.add(pid)
                seat = seats[pid]
                for field in ("cards_gained", "cards_removed"):
                    cards = stat.get(field, [])
                    if not isinstance(cards, list) or len(cards) > 3000:
                        raise Invalid("invalid_" + field)
                    seat["held"].update(card_id(c) for c in cards)
                transformations = stat.get("cards_transformed", [])
                if not isinstance(transformations, list) or len(transformations) > 3000:
                    raise Invalid("invalid_transformations")
                for change in transformations:
                    if not isinstance(change, dict):
                        raise Invalid("invalid_transformation")
                    for field in ("original_card", "final_card"):
                        seat["held"].add(card_id(change.get(field)))
                choices = stat.get("card_choices", [])
                if not isinstance(choices, list) or len(choices) > 3000:
                    raise Invalid("invalid_card_choices")
                for choice in choices:
                    if not isinstance(choice, dict):
                        raise Invalid("invalid_card_choice")
                    cid = card_id(choice.get("card"))
                    picked = choice.get("was_picked")
                    if picked is not None and type(picked) is not bool:
                        raise Invalid("invalid_was_picked")
                    if picked is None:
                        seat["missing_picked"] += 1
                    if picked is True:
                        seat["held"].add(cid)
                    seat["observed"].append({"card": cid, "picked": picked, "act": act_index + 1})
                    # This measures known native selection records across all sources.
                    if type(picked) is bool:
                        seat["offers"].append({"card": cid, "picked": picked, "act": act_index + 1})
    for seat in seats.values():
        seat["held"] = sorted(seat["held"])
    identity = dumps([platform, seed, start, sorted(seats)])
    rid = hashlib.sha256(identity.encode()).hexdigest()[:32]
    return {"facts_version": FACTS_VERSION, "id": rid, "start_time": start, "seed": seed, "floor": floor, "ascension": asc,
            "build": build, "mode": mode, "player_count": len(seats), "duration": duration,
            "outcome": "win" if data["win"] else "abandon" if data["was_abandoned"] else "death",
            "seats": list(seats.values()), "verification": "unverified_client",
            "missing_picked": sum(s["missing_picked"] for s in seats.values()),
            "core_telemetry": context}


class Store:
    def __init__(self, path, raw_root=None, raw_limit=None):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.raw = RawArchive(raw_root or os.environ.get("RAW_SUBMISSIONS_PATH") or self.path.parent / "raw-submissions")
        self.raw_limit = int(raw_limit if raw_limit is not None else os.environ.get("RAW_SUBMISSIONS_LIMIT_BYTES", 4 * 1024**3))
        with self.connect() as db:
            db.execute("PRAGMA journal_mode=WAL")
            db.execute("""CREATE TABLE IF NOT EXISTS runs (
                id TEXT PRIMARY KEY, digest TEXT NOT NULL, payload BLOB NOT NULL,
                facts TEXT NOT NULL, received INTEGER NOT NULL, start_time INTEGER NOT NULL,
                build TEXT NOT NULL, ascension INTEGER NOT NULL, mode TEXT NOT NULL,
                player_count INTEGER NOT NULL, outcome TEXT NOT NULL, floor INTEGER NOT NULL,
                duplicates INTEGER NOT NULL DEFAULT 0)""")
            db.execute("CREATE INDEX IF NOT EXISTS runs_received ON runs(received DESC, id)")
            db.execute("""CREATE TABLE IF NOT EXISTS submissions (
                sha256 TEXT PRIMARY KEY, run_id TEXT NOT NULL, status INTEGER NOT NULL,
                received INTEGER NOT NULL, bytes INTEGER NOT NULL, archive TEXT,
                source TEXT NOT NULL DEFAULT 'request')""")
            if "source" not in {row[1] for row in db.execute("PRAGMA table_info(submissions)")}:
                db.execute("ALTER TABLE submissions ADD COLUMN source TEXT NOT NULL DEFAULT 'request'")
            db.execute("CREATE INDEX IF NOT EXISTS submissions_run ON submissions(run_id,status,received)")
        self.errors = ErrorStore(self)

    @contextmanager
    def connect(self):
        db = sqlite3.connect(self.path, timeout=15)
        db.row_factory = sqlite3.Row
        db.execute("PRAGMA busy_timeout=15000")
        try:
            with db:
                yield db
        finally:
            db.close()

    def preserve_submission(self, db, body, rid, status, received, source="request"):
        digest = hashlib.sha256(body).hexdigest()
        existing = db.execute("SELECT * FROM submissions WHERE sha256=?", (digest,)).fetchone()
        if existing:
            self.raw.read(existing)
            return
        used = db.execute("SELECT COALESCE(SUM(bytes),0) FROM submissions").fetchone()[0]
        if used + len(body) > self.raw_limit:
            raise CapacityError("raw_submission_capacity_reached")
        self.raw.write(digest, received, body)
        db.execute("INSERT INTO submissions(sha256,run_id,status,received,bytes,source) VALUES (?,?,?,?,?,?)",
                   (digest, rid, status, received, len(body), source))

    def upload(self, data, original_body=None):
        facts = derive(data)
        raw = dumps(data).encode()
        source = "request" if original_body is not None else "generated_json"
        original_body = original_body if original_body is not None else raw
        digest = hashlib.sha256(json.dumps(data, sort_keys=True, ensure_ascii=False,
                                         separators=(",", ":"), allow_nan=False).encode()).hexdigest()
        rid = facts["id"]
        received = int(time.time())
        with self.connect() as db:
            db.execute("BEGIN IMMEDIATE")
            old = db.execute("SELECT digest FROM runs WHERE id=?", (rid,)).fetchone()
            if old:
                if not hmac.compare_digest(old["digest"], digest):
                    self.preserve_submission(db, original_body, rid, 409, received, source)
                    return 409, {"error": "run_identity_conflict", "run_id": rid}
                self.preserve_submission(db, original_body, rid, 200, received, source)
                db.execute("UPDATE runs SET duplicates=duplicates+1 WHERE id=?", (rid,))
                return 200, {"status": "duplicate", "run_id": rid}
            if db.execute("SELECT COUNT(*) FROM runs WHERE received>=?", (int(time.time()) - 86400,)).fetchone()[0] >= 2000:
                return 429, {"error": "daily_collection_budget_reached"}
            if sum(p.stat().st_size for p in (self.path, Path(str(self.path) + "-wal")) if p.exists()) > 1024**3:
                return 507, {"error": "storage_capacity_reached"}
            self.preserve_submission(db, original_body, rid, 201, received, source)
            db.execute("INSERT INTO runs VALUES (?,?,?,?,?,?,?,?,?,?,?,?,0)",
                       (rid, digest, gzip.compress(raw), dumps(facts), received,
                        facts["start_time"], facts["build"], facts["ascension"], facts["mode"],
                        facts["player_count"], facts["outcome"], facts["floor"]))
        return 201, {"status": "ok", "run_id": rid,
                     "quality": {"missing_was_picked": facts["missing_picked"]}}

    def where(self, query, include_outcome=False):
        clauses, values = [], []
        try:
            days = int(query.get("days", ["7"])[0])
        except ValueError as exc:
            raise Invalid("invalid_days") from exc
        if days not in (0, 1, 7, 14, 30, 90):
            raise Invalid("invalid_days")
        if days:
            clauses.append("received>=?")
            values.append(int(time.time()) - days * 86400)
        for key, column in [("build", "build"), ("ascension", "ascension"),
                            ("mode", "mode"), ("players", "player_count")]:
            value = query.get(key, ["all"])[0]
            if value != "all":
                if len(value) > 120:
                    raise Invalid("invalid_filter")
                clauses.append(column + "=?")
                values.append(value)
        if include_outcome and query.get("outcome", ["all"])[0] != "all":
            clauses.append("outcome=?")
            values.append(query["outcome"][0])
        if include_outcome and query.get("character", ["all"])[0] != "all":
            character = model(query["character"][0])
            clauses.append("EXISTS (SELECT 1 FROM json_each(runs.facts, '$.seats') AS seat "
                           "WHERE json_extract(seat.value, '$.character')=?)")
            values.append(character)
        for key in ("core_version", "game_target"):
            value = query.get(key, ["all"])[0]
            if len(value) > 120:
                raise Invalid("invalid_filter")
            if value != "all":
                clauses.append("json_extract(facts, '$.core_telemetry." + key + "')=?")
                values.append(value)
        source = query.get("source_mod", ["all"])[0]
        if len(source) > 180:
            raise Invalid("invalid_filter")
        if source != "all":
            clauses.append("EXISTS (SELECT 1 FROM json_each(runs.facts, '$.core_telemetry.source_mods') AS mod WHERE json_extract(mod.value, '$.id')=?)")
            values.append(source)
        return (" WHERE " + " AND ".join(clauses) if clauses else ""), values

    @staticmethod
    def summary(row):
        facts = json.loads(row["facts"])
        return {k: v for k, v in facts.items() if k != "seats"} | {
            "received_at": row["received"], "duplicates": row["duplicates"],
            "players": [{"id": s["id"], "character": s["character"]} for s in facts["seats"]]}

    def runs(self, query):
        where, values = self.where(query, include_outcome=True)
        try:
            page = max(1, min(1000000, int(query.get("page", ["1"])[0])))
        except ValueError as exc:
            raise Invalid("invalid_page") from exc
        with self.connect() as db:
            count = db.execute("SELECT COUNT(*) FROM runs" + where, values).fetchone()[0]
            page = min(page, max(1, (count + 19) // 20))
            rows = db.execute("SELECT facts,received,duplicates FROM runs" + where +
                              " ORDER BY received DESC,id LIMIT 20 OFFSET ?", values + [(page - 1) * 20]).fetchall()
        return {"items": [self.summary(r) for r in rows], "total": count, "page": page, "page_size": 20}

    def stats(self, query):
        where, values = self.where(query)
        character = query.get("character", ["all"])[0]
        if character != "all":
            character = model(character)
        with self.connect() as db:
            builds = [r[0] for r in db.execute("SELECT DISTINCT build FROM runs ORDER BY build DESC")]
        def rows():
            with self.connect() as db:
                yield from db.execute("SELECT facts,received FROM runs" + where, values)
        cards, total, wins, excluded, missing = {}, 0, 0, 0, 0
        count, latest = 0, None
        characters = set()
        core_versions, source_mods, game_targets = set(), set(), set()
        raw_seats = 0
        limited = False
        for row in rows():
            run = json.loads(row["facts"])
            context = run.get("core_telemetry") or {}
            core_versions.update([context["core_version"]] if context else [])
            game_targets.update([context["game_target"]] if context else [])
            source_mods.update(mod["id"] for mod in context.get("source_mods", []))
            characters.update(s["character"] for s in run["seats"])
            seats = [s for s in run["seats"] if character == "all" or s["character"] == character]
            if not seats:
                continue
            count += 1
            latest = max(latest or 0, row["received"])
            raw_seats += len(seats)
            if query.get("sample", ["codex"])[0] != "all" and run["outcome"] == "abandon" and run["floor"] <= 5:
                excluded += len(seats)
                continue
            for seat in seats:
                total += 1
                won = run["outcome"] == "win"
                wins += won
                missing += seat["missing_picked"]
                ids = set(seat["held"]) | {x["card"] for x in seat["observed"]}
                for cid in ids:
                    if cid not in cards and len(cards) >= 4096:
                        limited = True
                        continue
                    cards.setdefault(cid, {"id": cid, "held": 0, "wins": 0, "offered": 0, "picked": 0,
                                           "recorded_offers": 0, "recorded_picked": 0, "unknown_picked": 0})
                for cid in seat["held"]:
                    if cid not in cards:
                        continue
                    cards[cid]["held"] += 1
                    cards[cid]["wins"] += won
                for choice in seat["observed"]:
                    if choice["card"] not in cards:
                        continue
                    item = cards[choice["card"]]
                    item["recorded_offers"] += 1
                    item["recorded_picked"] += choice["picked"] is True
                    item["unknown_picked"] += choice["picked"] is None
                for choice in seat["observed"]:
                    if type(choice["picked"]) is not bool:
                        continue
                    if choice["card"] not in cards:
                        continue
                    cards[choice["card"]]["offered"] += 1
                    cards[choice["card"]]["picked"] += choice["picked"]
        result = []
        for cid, item in cards.items():
            item.update(CATALOG.get(cid, {"name": cid, "type": "未知", "rarity": "未知", "cost": None}))
            item.update({"pick_rate": item["picked"] / item["offered"] if item["offered"] else None,
                         "win_rate": item["wins"] / item["held"] if item["held"] else None,
                         "hold_rate": item["held"] / total if total else None})
            result.append(item)
        return {"runs": count, "raw_seats": raw_seats, "seats": total, "wins": wins,
                "excluded": excluded, "missing_picked": missing,
                "latest_received": latest, "builds": builds, "card_limit_reached": limited,
                "cards": result, "character": character, "characters": sorted(characters),
                "pick_policy": "native_known_choices_all_sources",
                "core_versions": sorted(core_versions), "source_mods": sorted(source_mods), "game_targets": sorted(game_targets)}

    def original(self, rid):
        with self.connect() as db:
            row = db.execute("SELECT * FROM submissions WHERE run_id=? AND status=201 ORDER BY received,sha256 LIMIT 1", (rid,)).fetchone()
        if row:
            try:
                return self.raw.read(row)
            except FileNotFoundError:
                # A reader may have fetched the loose location just before the archive
                # transaction published its bundle and removed that loose copy.
                with self.connect() as db:
                    current = db.execute("SELECT * FROM submissions WHERE sha256=?", (row["sha256"],)).fetchone()
                if not row["archive"] and current and current["archive"]:
                    return self.raw.read(current)
                raise
        row = self.detail(rid)
        return gzip.decompress(row["payload"]) if row else None

    def archive_submissions(self, before=None):
        before = before if before is not None else int(datetime.now(timezone.utc).replace(hour=0, minute=0, second=0, microsecond=0).timestamp())
        packed = 0
        with self.raw.maintenance_lock():
            while True:
                with self.connect() as db:
                    rows = db.execute("SELECT * FROM submissions WHERE archive IS NULL AND received<? ORDER BY received,sha256 LIMIT 256", (before,)).fetchall()
                if not rows:
                    break
                batches, batch, size, day = [], [], 0, None
                for row in rows:
                    row_day = utc_day(row["received"])
                    if batch and (row_day != day or size + row["bytes"] > 64 * 1024**2):
                        batches.append((day, batch))
                        batch, size = [], 0
                    day = row_day
                    batch.append(row)
                    size += row["bytes"]
                if batch:
                    batches.append((day, batch))
                for day, entries in batches:
                    archive = self.raw.pack(day, entries)
                    with self.connect() as db:
                        db.executemany("UPDATE submissions SET archive=? WHERE sha256=? AND archive IS NULL",
                                       [(archive, row["sha256"]) for row in entries])
                    for row in entries:
                        self.raw.loose_path(row).unlink(missing_ok=True)
                    packed += len(entries)
            # Also recover the harmless loose files left by a crash after publishing an index.
            with self.connect() as db:
                for loose in self.raw.root.glob("pending/*/*.json"):
                    row = db.execute("SELECT * FROM submissions WHERE sha256=? AND archive IS NOT NULL", (loose.stem,)).fetchone()
                    if row:
                        self.raw.read(row)
                        loose.unlink()
        return packed

    def needs_rebuild(self):
        with self.connect() as db:
            return any(json.loads(row[0]).get("facts_version") != FACTS_VERSION for row in db.execute("SELECT facts FROM runs"))

    def rebuild(self, backup):
        if Path(backup).exists():
            raise ValueError("rebuild_backup_already_exists")
        self.backup(backup)
        count = 0
        with self.connect() as db:
            db.execute("BEGIN IMMEDIATE")
            for row in db.execute("SELECT id,payload,received FROM runs"):
                body = gzip.decompress(row["payload"])
                data = parse_history(body)
                try:
                    facts = derive(data)
                except Invalid:
                    # Older accepted display fields remain available through the defensive reader.
                    # Core history validation still runs, so an invalid statistical record aborts atomically.
                    facts = derive(data, strict_display=False)
                    facts["legacy_display_warning"] = True
                if facts["id"] != row["id"]:
                    raise Invalid("rebuild_identity_mismatch")
                db.execute("UPDATE runs SET facts=? WHERE id=?", (dumps(facts), row["id"]))
                if not db.execute("SELECT 1 FROM submissions WHERE run_id=? AND status=201", (row["id"],)).fetchone():
                    self.preserve_submission(db, body, row["id"], 201, row["received"], "legacy_saved_history")
                count += 1
        return count

    def detail(self, rid):
        with self.connect() as db:
            row = db.execute("SELECT * FROM runs WHERE id=?", (rid,)).fetchone()
        if not row:
            return None
        return row

    def backup(self, destination):
        target = Path(destination)
        if target.resolve() == self.path.resolve():
            raise ValueError("backup_must_not_replace_database")
        target.parent.mkdir(parents=True, exist_ok=True)
        with self.connect() as db, closing(sqlite3.connect(target)) as out:
            db.backup(out)


def safe_display(value):
    # JavaScript must not round 64-bit Steam/player identifiers.
    if type(value) is int and abs(value) > 2**53 - 1:
        return str(value)
    if isinstance(value, dict):
        return {k: safe_display(v) for k, v in value.items()}
    if isinstance(value, list):
        return [safe_display(v) for v in value]
    return value


class Server(ThreadingHTTPServer):
    daemon_threads = True
    request_queue_size = 32

    def __init__(self, address, store, read_key):
        self.slots = threading.BoundedSemaphore(8)
        self.store, self.read_key = store, read_key
        self.error_limiter = ErrorLimiter()
        super().__init__(address, Handler)

    def process_request(self, request, address):
        if not self.slots.acquire(blocking=False):
            request.close()
            return
        try:
            super().process_request(request, address)
        except Exception:
            self.slots.release()
            raise

    def process_request_thread(self, request, address):
        try:
            super().process_request_thread(request, address)
        finally:
            self.slots.release()


class Handler(BaseHTTPRequestHandler):
    server_version = "KarenHistory"
    sys_version = ""

    def setup(self):
        self.request.settimeout(20)
        super().setup()

    def log_message(self, fmt, *args):
        # Request URLs and headers can contain access secrets; never log them.
        pass

    def respond(self, status, value, content_type="application/json; charset=utf-8", download=False):
        raw = value if isinstance(value, bytes) else dumps(value).encode()
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(raw)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Connection", "close")
        if status == 429:
            self.send_header("Retry-After", "60")
        if download:
            self.send_header("Content-Disposition", 'attachment; filename="run-history.json"')
        self.end_headers()
        self.close_connection = True
        if self.command != "HEAD":
            self.wfile.write(raw)

    def do_HEAD(self):
        self.do_GET()

    def do_GET(self):
        try:
            self.get()
        except (Invalid, InvalidError) as exc:
            self.respond(400, {"error": str(exc)})
        except (BrokenPipeError, ConnectionResetError, socket.timeout):
            pass
        except Exception:
            LOG.exception("read_failed")
            self.respond(500, {"error": "internal_error"})

    def get(self):
        parts = urlsplit(self.path)
        path, query = parts.path, parse_qs(parts.query)
        if path == "/healthz":
            with self.server.store.connect() as db:
                db.execute("SELECT 1").fetchone()
            return self.respond(200, {"status": "ok"})
        if not hmac.compare_digest(self.headers.get("X-Observatory-Access", ""), self.server.read_key):
            return self.respond(404, {"error": "not_found"})
        if path == "/api/runs":
            return self.respond(200, self.server.store.runs(query))
        if path == "/api/stats":
            return self.respond(200, self.server.store.stats(query))
        if path == "/api/errors":
            return self.respond(200, self.server.store.errors.listing(query))
        error_match = re.fullmatch(r"/api/errors/([0-9a-f]{64})", path)
        if error_match:
            detail = self.server.store.errors.detail(error_match[1])
            return self.respond(200 if detail else 404, detail or {"error": "not_found"})
        match = re.fullmatch(r"/api/runs/([0-9a-f]{32})(/raw)?", path)
        if match:
            row = self.server.store.detail(match[1])
            if not row:
                return self.respond(404, {"error": "not_found"})
            if match[2]:
                return self.respond(200, self.server.store.original(match[1]), download=True)
            raw = gzip.decompress(row["payload"])
            return self.respond(200, {"summary": self.server.store.summary(row),
                                      "history": safe_display(json.loads(raw))})
        if path in ("/", "/index.html", "/app.js", "/style.css", "/catalog.json", "/relics.json", "/characters.json", "/errors.html", "/errors.js", "/errors.css") or re.fullmatch(r"/assets/[a-z0-9_]+\.(?:webp|png)", path):
            file = WEB / ("index.html" if path == "/" else path.lstrip("/"))
            if file.is_file():
                mime = {".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8",
                        ".css": "text/css; charset=utf-8", ".json": "application/json; charset=utf-8",
                        ".webp": "image/webp", ".png": "image/png"}[file.suffix]
                return self.respond(200, file.read_bytes(), mime)
        self.respond(404, {"error": "not_found"})

    def do_POST(self):
        try:
            path = urlsplit(self.path).path
            if path not in ("/api/upload", "/api/upload-session", "/api/errors"):
                return self.respond(404, {"error": "not_found"})
            session = path == "/api/upload-session"
            errors = path == "/api/errors"
            if errors:
                source = self.headers.get("X-Karen-Client-IP", self.client_address[0])[:80]
                if not self.server.error_limiter.accept(source):
                    return self.respond(429, {"error": "error_rate_limit"})
            if self.headers.get("Content-Type", "").split(";", 1)[0].strip().lower() != "application/json":
                return self.respond(415, {"error": "application_json_required"})
            if self.headers.get("Transfer-Encoding"):
                return self.respond(400, {"error": "content_length_required"})
            try:
                length = int(self.headers.get("Content-Length", "0"))
            except ValueError:
                return self.respond(400, {"error": "invalid_content_length"})
            if not 0 < length <= (MAX_ERROR_BODY if errors else 1024 if session else MAX_BODY):
                return self.respond(413, {"error": "invalid_body_size"})
            body = self.rfile.read(length)
            if len(body) != length:
                return self.respond(400, {"error": "incomplete_body"})
            if errors:
                status, result = self.server.store.errors.upload(parse_history(body))
                return self.respond(status, result)
            if session:
                status, result = legacy_session(parse_history(body))
                return self.respond(status, result)
            status, result = self.server.store.upload(parse_history(body), original_body=body)
            self.respond(status, result)
        except (Invalid, InvalidError) as exc:
            self.respond(400, {"error": str(exc)})
        except CapacityError as exc:
            self.respond(507, {"error": str(exc)})
        except (BrokenPipeError, ConnectionResetError, socket.timeout):
            pass
        except Exception:
            LOG.exception("upload_failed")
            self.respond(500, {"error": "internal_error"})


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--backup")
    parser.add_argument("--archive-raw", action="store_true")
    parser.add_argument("--rebuild", action="store_true")
    args = parser.parse_args()
    store = Store(os.environ.get("DATABASE_PATH", str(ROOT / "data/history.sqlite3")))
    if args.rebuild or (not args.backup and not args.archive_raw and store.needs_rebuild()):
        backup = args.backup or str(store.path.parent / "backups" / ("before-facts-v3-" + str(time.time_ns()) + ".sqlite3"))
        LOG.warning("derived_history_rebuild count=%s", store.rebuild(backup))
        if args.rebuild:
            return
    if args.archive_raw:
        LOG.warning("raw_submissions_archived count=%s", store.archive_submissions())
        return
    if args.backup:
        store.backup(args.backup)
        return
    read_key = os.environ.get("READ_KEY", "")
    if len(read_key) < 24:
        raise SystemExit("READ_KEY must contain at least 24 characters")
    logging.basicConfig(level=logging.INFO)
    port = int(os.environ.get("PORT", "42767"))
    with Server(("127.0.0.1", port), store, read_key) as server:
        LOG.info("collector_started port=%s", port)
        server.serve_forever()


if __name__ == "__main__":
    main()
