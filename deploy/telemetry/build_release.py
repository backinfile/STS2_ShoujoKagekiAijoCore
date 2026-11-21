"""Assemble a deployable service tree without credentials or player data."""
import argparse
import hashlib
from pathlib import Path
import shutil


def stamp_web_assets(web):
    # Version metadata requests as well as the worker so an older active worker
    # can load the current dashboard and catalogues immediately on navigation.
    digest = hashlib.sha256()
    for path in sorted(web.rglob("*")):
        if path.is_file() and path.name != "sw.js":
            digest.update(path.relative_to(web).as_posix().encode())
            digest.update(path.read_bytes())
    version = digest.hexdigest()[:16]
    for name in ("index.html", "sw.js"):
        path = web / name
        path.write_text(path.read_text(encoding="utf-8").replace("__ASSET_VERSION__", version), encoding="utf-8")
    return version


def build(output):
    root = Path(__file__).resolve().parents[2]
    if output.exists():
        raise ValueError("Release output already exists")
    output.mkdir(parents=True)
    for name in ("app.py", "error_reports.py", "error_retention.py", "protocol.py", "raw_archive.py"):
        shutil.copy2(root / "services/telemetry" / name, output / name)
    shutil.copytree(root / "web/telemetry", output / "web")
    stamp_web_assets(output / "web")
    for name in ("install.py", "domain_config.py"):
        shutil.copy2(root / "deploy/telemetry" / name, output / name)
    print("Release assembled")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    build(args.output)
