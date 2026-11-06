"""Assemble a deployable service tree without credentials or player data."""
import argparse
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
    shutil.copy2(root / "deploy/telemetry/install.py", output / "install.py")
    print("Release assembled")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    build(args.output)
