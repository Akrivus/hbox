"""Restore the exact HBOx FootballSimulator tree from its original licensed package."""
import argparse
import json
import os
from pathlib import Path
import shutil
import sys
import uuid
import zipfile

from bundle_common import VENDOR, destination, digest, file_digest, package_files, reconstruct


def load_manifest(bundle_dir):
    manifest = json.loads((bundle_dir / "manifest.json").read_text(encoding="utf-8"))
    if manifest["schema_version"] != 1 or manifest["vendor_root"] != VENDOR:
        raise ValueError("Unsupported bundle format/root")
    if file_digest(bundle_dir / "changes.zip") != manifest["bundle_sha256"]:
        raise ValueError("Patch bundle checksum mismatch")
    seen = set()
    for entry in manifest["files"]:
        if entry["path"].casefold() in seen:
            raise ValueError("Duplicate manifest path")
        seen.add(entry["path"].casefold())
        if entry["operation"] not in ("copy", "splice", "add", "remove"):
            raise ValueError("Unknown patch operation")
    return manifest


def verify(project, manifest):
    errors = []
    for relative in manifest["directories"]:
        if not destination(project, relative).is_dir():
            errors.append(f"Missing directory: {relative}")
    for entry in manifest["files"]:
        path = destination(project, entry["path"])
        actual = file_digest(path) if path.is_file() else None
        if path.exists() and not path.is_file():
            errors.append(f"Expected a file: {entry['path']}")
        elif actual != entry["after_sha256"]:
            errors.append(f"Missing or changed: {entry['path']}")
    if errors:
        raise ValueError("Verification failed:\n" + "\n".join(errors[:30]))
    print(f"Verified {len(manifest['files'])} vendor file states and {len(manifest['directories'])} directories.")


def preflight(project, manifest):
    pending, errors = {}, []
    for relative in manifest["directories"]:
        target = destination(project, relative)
        if target.exists() and not target.is_dir():
            errors.append(f"Expected a directory: {relative}")
    for entry in manifest["files"]:
        target = destination(project, entry["path"])
        if target.exists() and not target.is_file():
            errors.append(f"Expected a file: {entry['path']}")
            continue
        current = file_digest(target) if target.is_file() else None
        if current == entry["after_sha256"]:
            continue
        accepted = [None, entry["before_sha256"], *entry.get("accepted_intermediate_sha256", [])]
        if current not in accepted:
            errors.append(f"Unrecognized local edit: {entry['path']}")
        else:
            pending[entry["path"]] = (entry, current)
    if errors:
        raise ValueError("Preflight rejected the installation; no destination files changed:\n" + "\n".join(errors[:30]))
    return pending


def install(project, package, bundle_dir, manifest, dry_run=False):
    if package.stat().st_size != manifest["package"]["size"] or file_digest(package) != manifest["package"]["sha256"]:
        raise ValueError("Original package checksum mismatch. Use the pinned original distribution; do not bypass this check.")
    if (project / "Temp/UnityLockfile").exists():
        raise ValueError("Close Unity before installing (Temp/UnityLockfile exists).")
    pending = preflight(project, manifest)
    if not pending:
        missing_dirs = [p for p in manifest["directories"] if not destination(project, p).is_dir()]
        if not dry_run:
            for relative in missing_dirs:
                destination(project, relative).mkdir(parents=True, exist_ok=True)
            verify(project, manifest)
        print(f"No file changes needed; {len(missing_dirs)} directories to create.")
        return
    # Staging lives on the project volume so individual os.replace calls are atomic.
    work = (project / "Temp/FootballSimulatorSetup" / uuid.uuid4().hex).resolve()
    if not work.is_relative_to(project) or work.exists():
        raise ValueError("Unsafe staging location")
    work.mkdir(parents=True)
    staged, prior, committed = {}, {}, []
    preserve_work = False
    try:
        with zipfile.ZipFile(bundle_dir / "changes.zip") as archive:
            def stage(entry, before):
                if entry["before_sha256"] is not None and digest(before) != entry["before_sha256"]:
                    raise ValueError(f"Original asset checksum mismatch: {entry['path']}")
                payload = archive.read(entry["payload"]) if "payload" in entry else b""
                after = reconstruct(entry, before, payload)
                if after is not None:
                    path = work / (str(len(staged)) + ".new")
                    path.write_bytes(after)
                    staged[entry["path"]] = path
                else:
                    staged[entry["path"]] = None

            for relative, before in package_files(package):
                if relative in pending:
                    stage(pending[relative][0], before)
            for relative, (entry, _) in pending.items():
                if entry["operation"] == "add":
                    stage(entry, b"")
            if set(staged) != set(pending):
                raise ValueError("Package inventory does not match bundle manifest")
        print(f"Preflight and byte reconstruction passed: {len(pending)} file changes.")
        if dry_run:
            print("Dry run complete; destination files unchanged.")
            return
        # Recheck all destinations before committing, then retain exact backups for rollback.
        for relative, (_, expected) in pending.items():
            target = destination(project, relative)
            current = file_digest(target) if target.is_file() else None
            if current != expected:
                raise ValueError(f"Destination changed during staging: {relative}")
            backup = work / (str(len(prior)) + ".old") if target.is_file() else None
            if backup is not None:
                shutil.copyfile(target, backup)
            prior[relative] = backup
        for relative in manifest["directories"]:
            destination(project, relative).mkdir(parents=True, exist_ok=True)
        try:
            for relative, staged_path in staged.items():
                target = destination(project, relative)
                # Check again immediately before each individual replacement.
                expected = pending[relative][1]
                if (file_digest(target) if target.is_file() else None) != expected:
                    raise ValueError(f"Destination changed during commit: {relative}")
                target.parent.mkdir(parents=True, exist_ok=True)
                if staged_path is None:
                    target.unlink(missing_ok=True)
                else:
                    os.replace(staged_path, target)
                committed.append(relative)
            verify(project, manifest)
        except BaseException:
            rollback_errors = []
            for relative in reversed(committed):
                try:
                    target = destination(project, relative)
                    backup = prior[relative]
                    if backup is None:
                        target.unlink(missing_ok=True)
                    else:
                        os.replace(backup, target)
                except (OSError, ValueError) as error:
                    rollback_errors.append(f"{relative}: {error}")
            if rollback_errors:
                preserve_work = True
                try:
                    (work / "recovery.json").write_text(json.dumps({relative: str(path) if path else None for relative, path in prior.items()}, indent=2))
                except OSError:
                    pass  # Retain backups even if a full/failed disk prevents writing the recovery index.
                print(f"Rollback incomplete. Recovery backups retained at {work}: {rollback_errors}", file=sys.stderr)
            raise
        print("FootballSimulator restored. Open HBOx in the pinned Unity version to import assets.")
    finally:
        # Never perform recursive cleanup outside this script's unique workspace staging directory.
        if not preserve_work and work.is_relative_to((project / "Temp/FootballSimulatorSetup").resolve()):
            shutil.rmtree(work)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path, help="Original Football Soccer Simulator.unitypackage")
    parser.add_argument("--project-root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--bundle-dir", type=Path, default=Path(__file__).with_name("bundle"))
    parser.add_argument("--verify", action="store_true", help="Read-only installed-file verification; package not needed")
    parser.add_argument("--dry-run", action="store_true", help="Reconstruct and validate in staging without changing vendor files")
    args = parser.parse_args()
    project = args.project_root.resolve()
    manifest = load_manifest(args.bundle_dir.resolve())
    if args.verify:
        verify(project, manifest)
    elif args.package is None:
        parser.error("--package is required for installation")
    else:
        install(project, args.package.resolve(), args.bundle_dir.resolve(), manifest, args.dry_run)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, KeyError, zipfile.BadZipFile) as error:
        print(f"Setup failed: {error}", file=sys.stderr)
        sys.exit(1)
