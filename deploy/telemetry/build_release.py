"""Assemble a deployable service tree without credentials or player data."""
import argparse
import hashlib
from pathlib import Path
import shutil


def build(output):
    root = Path(__file__).resolve().parents[2]
    if output.exists():
        raise ValueError("Release output already exists")
    output.mkdir(parents=True)
    for name in ("app.py", "error_reports.py", "protocol.py", "raw_archive.py"):
        shutil.copy2(root / "services/telemetry" / name, output / name)
    shutil.copytree(root / "web/telemetry", output / "web")
    # Every release gives the worker a new cache when any web asset changes.
    digest = hashlib.sha256()
    for path in sorted((output / "web").rglob("*")):
        if path.is_file() and path.name != "sw.js":
            digest.update(path.relative_to(output / "web").as_posix().encode())
            digest.update(path.read_bytes())
    worker = output / "web/sw.js"
    worker.write_text(worker.read_text(encoding="utf-8").replace("__ASSET_VERSION__", digest.hexdigest()[:16]), encoding="utf-8")
    shutil.copy2(root / "deploy/telemetry/install.py", output / "install.py")
    print("Release assembled")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    build(args.output)
