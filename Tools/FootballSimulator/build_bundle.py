"""Capture every local vendor-tree difference against a licensed original unitypackage."""
import argparse
from collections import Counter
import difflib
import json
from pathlib import Path
import zipfile

from bundle_common import VENDOR, destination, digest, file_digest, package_files, reconstruct, splice


def build(package, project, output):
    project, package, output = project.resolve(), package.resolve(), output.resolve()
    entries, payloads, review = [], {}, []
    seen = set()
    seed_path = Path(__file__).with_name("seed-patch.json")
    seed_baselines = {e["path"]: e["before"] for e in json.loads(seed_path.read_text())} if seed_path.exists() else {}

    def capture(relative, before):
        target = destination(project, relative)
        after = target.read_bytes() if target.is_file() else None
        entry = {"path": relative, "before_sha256": digest(before) if before is not None else None,
                 "before_size": len(before) if before is not None else None,
                 "after_sha256": digest(after) if after is not None else None,
                 "after_size": len(after) if after is not None else None}
        if before is None:
            entry["operation"] = "add"
        elif after is None:
            entry["operation"] = "remove"
        elif before == after:
            entry["operation"] = "copy"
        else:
            entry["operation"] = "splice"
        if entry["operation"] in ("add", "splice"):
            prefix, suffix, payload = splice(before or b"", after)
            entry.update(prefix=prefix, suffix=suffix, payload_sha256=digest(payload))
            entry["payload"] = "payload/" + digest(payload)
            payloads[entry["payload"]] = payload
        if relative in seed_baselines:
            entry["accepted_intermediate_sha256"] = [seed_baselines[relative]]
        if reconstruct(entry, before or b"", payloads.get(entry.get("payload"), b"")) != after:
            raise ValueError(f"Round-trip failed: {relative}")
        if relative.endswith(".cs") and before != after:
            old = (before or b"").decode("utf-8-sig").splitlines(keepends=True)
            new = (after or b"").decode("utf-8-sig").splitlines(keepends=True)
            review.extend(difflib.unified_diff(old, new, fromfile="original/" + relative, tofile="hbox/" + relative))
        entries.append(entry)
        seen.add(relative)

    for relative, data in package_files(package):
        capture(relative, data)
    vendor = destination(project, VENDOR)
    for path in sorted(vendor.rglob("*")):
        relative = path.relative_to(project).as_posix()
        if path.is_symlink():
            raise ValueError(f"Linked vendor asset: {path}")
        if path.is_file() and relative not in seen:
            capture(relative, None)
    root_meta = destination(project, VENDOR + ".meta")
    if root_meta.is_file() and VENDOR + ".meta" not in seen:
        capture(VENDOR + ".meta", None)
    entries.sort(key=lambda entry: entry["path"])
    directories = [VENDOR] + sorted(p.relative_to(project).as_posix() for p in vendor.rglob("*") if p.is_dir())
    for relative in directories:
        destination(project, relative)
    # Detect edits made while the inventory was being captured.
    for entry in entries:
        target = destination(project, entry["path"])
        if (file_digest(target) if target.is_file() else None) != entry["after_sha256"]:
            raise ValueError(f"Vendor tree changed during capture: {target}")
    output.mkdir(parents=True, exist_ok=True)
    bundle = output / "changes.zip"
    with zipfile.ZipFile(bundle, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in sorted(payloads.items()):
            info = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, data, compresslevel=9)
    version_file = project / "ProjectSettings/ProjectVersion.txt"
    manifest = {"schema_version": 1, "vendor_root": VENDOR,
                "package": {"filename": package.name, "sha256": file_digest(package), "size": package.stat().st_size},
                "unity_version": version_file.read_text().splitlines()[0].split(": ", 1)[1] if version_file.exists() else None,
                "bundle_sha256": file_digest(bundle), "directories": directories,
                "counts": dict(Counter(e["operation"] for e in entries)), "files": entries}
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    (output / "source-changes.patch").write_text("".join(review), encoding="utf-8", newline="\n")
    print(f"Captured {len(entries)} files: {manifest['counts']}; bundle {bundle.stat().st_size:,} bytes")
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--project-root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--output-dir", type=Path, default=Path(__file__).with_name("bundle"))
    args = parser.parse_args()
    build(args.package, args.project_root, args.output_dir)
