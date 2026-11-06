"""Private, byte-preserving submission files and verified daily tar.gz bundles."""
from __future__ import annotations

from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import io
import json
import os
from pathlib import Path
import tarfile
import tempfile
import uuid


def utc_day(timestamp):
    return datetime.fromtimestamp(timestamp, timezone.utc).strftime("%Y-%m-%d")


class RawArchive:
    def __init__(self, root):
        self.root = Path(root)
        self.ensure_directory(self.root)

    @staticmethod
    def sync_directory(path):
        if os.name != "nt":
            descriptor = os.open(path, os.O_RDONLY | os.O_DIRECTORY)
            try:
                os.fsync(descriptor)
            finally:
                os.close(descriptor)

    @classmethod
    def ensure_directory(cls, path):
        missing = []
        current = path
        while not current.exists():
            missing.append(current)
            current = current.parent
        for folder in reversed(missing):
            folder.mkdir(mode=0o700, exist_ok=True)
            cls.sync_directory(folder.parent)

    def loose_path(self, row):
        return self.root / "pending" / utc_day(row["received"]) / (row["sha256"] + ".json")

    def write(self, digest, received, body):
        row = {"sha256": digest, "received": received}
        target = self.loose_path(row)
        self.ensure_directory(target.parent)
        if target.exists():
            if hashlib.sha256(target.read_bytes()).hexdigest() != digest:
                raise OSError("raw_submission_checksum_mismatch")
            return
        temporary = None
        try:
            with tempfile.NamedTemporaryFile(dir=target.parent, delete=False) as stream:
                temporary = Path(stream.name)
                stream.write(body)
                stream.flush()
                os.fsync(stream.fileno())
            temporary.replace(target)
            self.sync_directory(target.parent)
        finally:
            if temporary is not None:
                temporary.unlink(missing_ok=True)

    def read(self, row):
        if row["archive"]:
            with tarfile.open(self.root / row["archive"], "r:gz") as bundle:
                with bundle.extractfile(row["sha256"] + ".json") as stream:
                    body = stream.read()
        else:
            body = self.loose_path(row).read_bytes()
        if len(body) != row["bytes"] or hashlib.sha256(body).hexdigest() != row["sha256"]:
            raise OSError("raw_submission_checksum_mismatch")
        return body

    @contextmanager
    def maintenance_lock(self):
        with (self.root / ".maintenance.lock").open("a+b") as stream:
            if os.name == "nt":
                import msvcrt
                if stream.tell() == 0:
                    stream.write(b"\0")
                    stream.flush()
                stream.seek(0)
                msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
            try:
                yield
            finally:
                if os.name == "nt":
                    stream.seek(0)
                    msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
                else:
                    fcntl.flock(stream.fileno(), fcntl.LOCK_UN)

    def pack(self, day, rows):
        folder = self.root / "bundles"
        self.ensure_directory(folder)
        target = folder / (day + "-" + uuid.uuid4().hex[:12] + ".tar.gz")
        temporary = target.with_suffix(target.suffix + ".partial")
        manifest = [{key: row[key] for key in ("sha256", "bytes", "run_id", "status", "received", "source")}
                    for row in rows]
        try:
            with temporary.open("xb") as output:
                with tarfile.open(fileobj=output, mode="w:gz") as bundle:
                    for row in rows:
                        body = self.read(row)
                        info = tarfile.TarInfo(row["sha256"] + ".json")
                        info.size, info.mode, info.mtime = len(body), 0o600, row["received"]
                        bundle.addfile(info, io.BytesIO(body))
                    body = json.dumps({"version": 1, "submissions": manifest}, ensure_ascii=False).encode()
                    info = tarfile.TarInfo("manifest.json")
                    info.size, info.mode = len(body), 0o600
                    bundle.addfile(info, io.BytesIO(body))
                output.flush()
                os.fsync(output.fileno())
            # Validate every member before publishing the bundle or deleting a loose JSON.
            with tarfile.open(temporary, "r:gz") as bundle:
                if set(bundle.getnames()) != {"manifest.json"} | {r["sha256"] + ".json" for r in rows}:
                    raise OSError("raw_archive_manifest_mismatch")
                with bundle.extractfile("manifest.json") as stream:
                    if json.load(stream) != {"version": 1, "submissions": manifest}:
                        raise OSError("raw_archive_manifest_mismatch")
                for row in rows:
                    with bundle.extractfile(row["sha256"] + ".json") as stream:
                        body = stream.read()
                    if len(body) != row["bytes"] or hashlib.sha256(body).hexdigest() != row["sha256"]:
                        raise OSError("raw_archive_checksum_mismatch")
            temporary.replace(target)
            self.sync_directory(folder)
            return target.relative_to(self.root).as_posix()
        finally:
            temporary.unlink(missing_ok=True)
