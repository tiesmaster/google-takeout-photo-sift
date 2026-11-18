"""Export what the Python viewer does with each real batch, for the C# golden tests.

For every batch under the batches folder (and batches\\done) this writes
local/golden/<batch>.json with:
  - per folder: the file names in listing order, and match_sidecars() of them
  - the photo groups from rotate_viewer.collect(), as paths relative to
    Takeout\\Google Photos (main file first, then its album copies)

It also writes local/golden/takeout-source.json with only the sidecar matching of
the unsplit Takeout (all years), which covers far more of Takeout's naming quirks.

local/ is git-ignored: these are your own file names. Re-run after a batch changes
(photos deleted or moved), otherwise the golden test reports the difference.

Usage:
  python tools/export_golden.py
  python tools/export_golden.py --scripts C:\\ties\\takeout\\scripts --batches C:\\ties\\takeout\\batches
"""

import argparse
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent


def batches(root):
    for parent in (root, root / "done"):
        if not parent.is_dir():
            continue
        for batch in sorted(parent.iterdir()):
            if (batch / "Takeout" / "Google Photos").is_dir():
                yield batch


def export_folders(photos_root, match_sidecars):
    folders = {}
    for folder in sorted(f for f in photos_root.iterdir() if f.is_dir()):
        names = [e.name for e in folder.iterdir() if e.is_file()]
        folders[folder.name] = {"names": names, "sidecars": match_sidecars(names)}
    return folders


def export(batch, collect, match_sidecars):
    photos_root = batch / "Takeout" / "Google Photos"
    folders = export_folders(photos_root, match_sidecars)
    items, _, _ = collect(batch)
    groups = [[p.relative_to(photos_root).as_posix() for p in group] for group in items]
    return {"batch": str(batch), "folders": folders, "photos": groups}


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--scripts", type=Path, default=Path(r"C:\ties\takeout\scripts"),
                   help="folder with rotate_viewer.py and split_year.py")
    p.add_argument("--batches", type=Path, default=Path(r"C:\ties\takeout\batches"))
    p.add_argument("--takeout", type=Path, default=Path(r"C:\ties\takeout\Takeout\Google Photos"),
                   help="the unsplit Takeout, exported for sidecar matching only")
    p.add_argument("--out", type=Path, default=REPO / "local" / "golden")
    args = p.parse_args()

    sys.path.insert(0, str(args.scripts))
    from rotate_viewer import collect  # noqa: E402
    from split_year import match_sidecars  # noqa: E402

    args.out.mkdir(parents=True, exist_ok=True)
    for batch in batches(args.batches):
        data = export(batch, collect, match_sidecars)
        name = batch.name if batch.parent == args.batches else f"done-{batch.name}"
        out = args.out / f"{name}.json"
        out.write_text(json.dumps(data, ensure_ascii=False, indent=1), encoding="utf-8")
        print(f"{out.name}: {len(data['photos'])} photos, {len(data['folders'])} folders")
    if args.takeout.is_dir():
        data = {"batch": "", "folders": export_folders(args.takeout, match_sidecars), "photos": []}
        out = args.out / "takeout-source.json"
        out.write_text(json.dumps(data, ensure_ascii=False, indent=1), encoding="utf-8")
        files = sum(len(f["names"]) for f in data["folders"].values())
        print(f"{out.name}: {files} files, {len(data['folders'])} folders (sidecars only)")


if __name__ == "__main__":
    main()
