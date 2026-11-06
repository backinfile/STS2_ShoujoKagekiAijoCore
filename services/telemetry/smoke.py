"""One end-to-end deployment probe; leaves its isolated test ID for explicit scoped cleanup."""
import base64
import copy
import hashlib
import json
from pathlib import Path
import ssl
import sys
import time
from urllib.parse import urlsplit
from urllib.error import HTTPError
from urllib.request import build_opener, HTTPSHandler, ProxyHandler, Request
import uuid

from test_app import fixture

access_file = Path(sys.argv[1])
access = json.loads(access_file.read_text())
opener = build_opener(ProxyHandler({}), HTTPSHandler(context=ssl.create_default_context()))
opener.addheaders = []  # Match the game HttpClient, which sends no browser User-Agent.
auth = "Basic " + base64.b64encode((access["username"] + ":" + access["password"]).encode()).decode()
def request(url, payload=None, headers=None):
    data = payload if isinstance(payload, bytes) else json.dumps(payload, ensure_ascii=False).encode() if payload is not None else None
    try:
        with opener.open(Request(url, data=data, headers=headers or {}), timeout=20) as response:
            return response.status, response.read(), dict(response.headers)
    except HTTPError as error:
        return error.code, error.read(), dict(error.headers)

read = {"Authorization": auth} if access.get("page_access", "password") == "password" else {}
upload = {"Content-Type": "application/json"}
def direct_upload(payload):
    body = json.dumps(payload, ensure_ascii=False).encode()
    return request(access["upload_url"], body, upload)
checks = {}
checks["root_hidden"] = request(urlsplit(access["page_url"])._replace(path="/", query="", fragment="").geturl())[0] == 404
gate_status = 401 if access.get("page_access", "password") == "password" else 200
checks["portal_access_matches_policy"] = request(access["page_url"])[0] == gate_status
checks["read_api_access_matches_policy"] = request(access["page_url"] + "api/runs")[0] == gate_status
checks["portal_loaded"] = request(access["page_url"], headers=read)[0] == 200
checks["invalid_payload_rejected_without_token"] = direct_upload({})[0] == 400
data = fixture()
data["seed"] = "COLLECTOR-SELFTEST-" + uuid.uuid4().hex
template = data["map_point_history"][0][0]
acts = []
for act in range(3):
    entries = []
    for floor in range(6):
        entry = copy.deepcopy(template)
        if floor:
            entry["player_stats"][0]["card_choices"] = []
            entry["player_stats"][0]["cards_gained"] = []
        room = ["monster", "event", "shop", "treasure", "rest_site", "boss"][floor]
        entry["rooms"][0]["room_type"] = room
        entry["rooms"][0]["model_id"] = "ENCOUNTER.TEST_ROOM"
        entry["player_stats"][0]["current_gold"] = 99 + floor * 10
        entry["player_stats"][0]["current_hp"] = 72 - floor * 5
        entries.append(entry)
    acts.append(entries)
data["map_point_history"] = acts
status, raw, _ = direct_upload(data)
checks["new_run_201"] = status == 201
if status != 201:
    raise SystemExit(f"Upload failed: {status} {raw[:200]}")
rid = json.loads(raw)["run_id"]
checks["duplicate_200"] = direct_upload(data)[0] == 200
conflict = copy.deepcopy(data)
conflict["run_time"] += 1
checks["conflict_409"] = direct_upload(conflict)[0] == 409
body = json.dumps(data, ensure_ascii=False).encode()
status, raw, _ = request(access["upload_url"] + "/session",
                         {"sha256": hashlib.sha256(body).hexdigest(), "bytes": len(body)}, upload)
checks["r6_session_compatible"] = status == 201 and "upload_token" in json.loads(raw)
if checks["r6_session_compatible"]:
    headers = upload | {"Authorization": "Bearer " + json.loads(raw)["upload_token"]}
    checks["r6_upload_compatible"] = request(access["upload_url"], body, headers)[0] == 200
status, raw, _ = request(access["page_url"] + "api/runs/" + rid, headers=read)
checks["detail_64_bit_id"] = status == 200 and json.loads(raw)["history"]["players"][0]["id"] == "76561199000000001"
status, raw, _ = request(access["page_url"] + "api/runs/" + rid + "/raw", headers=read)
checks["raw_64_bit_id"] = status == 200 and json.loads(raw)["players"][0]["id"] == 76561199000000001
status, raw, _ = request(access["page_url"] + "api/stats", headers=read)
checks["stats_reads_uploaded_data"] = status == 200 and json.loads(raw)["seats"] >= 1
proof = {"run_id": rid, "seed": data["seed"], "checks": checks, "tested_at": int(time.time())}
(access_file.parent / "smoke-result.json").write_text(json.dumps(proof, indent=2), encoding="utf-8")
print(json.dumps(checks, indent=2))
if not all(checks.values()):
    raise SystemExit(1)
