"""Resolve independently persisted page and upload origins before deployment."""
import re
from urllib.parse import urlsplit


def _origin(value, label):
    parsed = urlsplit(value or "")
    try:
        port = parsed.port
    except ValueError as error:
        raise ValueError("Invalid " + label + " origin") from error
    if (parsed.scheme != "https" or not parsed.hostname or parsed.username or parsed.password
            or parsed.path not in ("", "/") or parsed.query or parsed.fragment
            or not re.fullmatch(r"[A-Za-z0-9.-]+", parsed.hostname)):
        raise ValueError("Provide " + label + " as an HTTPS origin")
    return parsed.scheme + "://" + parsed.netloc.lower()


def resolve_origins(access, page_origin=None, upload_origin=None):
    def existing(key):
        parsed = urlsplit(access.get(key, ""))
        return parsed.scheme + "://" + parsed.netloc if parsed.netloc else ""
    page = _origin(page_origin or existing("page_url") or existing("upload_url"), "--public-origin")
    upload = _origin(upload_origin or existing("upload_url"), "--upload-origin")
    if urlsplit(page).hostname == urlsplit(upload).hostname:
        raise ValueError("Page and upload must use different hostnames; provide --upload-origin")
    return page, upload
