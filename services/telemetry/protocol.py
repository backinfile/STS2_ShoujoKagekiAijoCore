"""Versioned Core context. Legacy client identity and payload fields retain their meaning."""
import json


def core_context(value):
    if not isinstance(value, dict) or set(value) != {"schema_version", "core_version", "api_version", "game_target", "game_version", "source_mods", "model_sources"} or type(value["schema_version"]) is not int or value["schema_version"] != 2:
        raise ValueError("invalid_core_schema")
    result = dict(value)
    for key in ("core_version", "api_version", "game_target", "game_version"):
        if not isinstance(value[key], str) or not value[key].strip() or len(value[key]) > 120 or any(ord(c) < 32 for c in value[key]):
            raise ValueError("invalid_core_" + key)
    for key, fields, limit in (("source_mods", {"id", "name", "version"}, 128), ("model_sources", {"model_id", "source_mod", "source_version"}, 8192)):
        if not isinstance(value[key], list) or len(value[key]) > limit:
            raise ValueError("invalid_core_" + key)
        seen = set()
        for item in value[key]:
            if not isinstance(item, dict) or set(item) != fields or any(not isinstance(v, str) or not v.strip() or len(v) > 180 or any(ord(c) < 32 for c in v) for v in item.values()):
                raise ValueError("invalid_core_" + key)
            identity = item.get("id", item.get("model_id"))
            if identity in seen:
                raise ValueError("duplicate_core_" + key)
            seen.add(identity)
    if len(json.dumps(value, ensure_ascii=False).encode()) > 1024 * 1024:
        raise ValueError("core_context_too_large")
    result["source_mods"] = sorted(value["source_mods"], key=lambda item: item["id"])
    result["model_sources"] = sorted(value["model_sources"], key=lambda item: item["model_id"])
    return result
