"""Installer tests use synthetic assets; no licensed package is needed."""
import importlib.util
import contextlib
import io
import json
from pathlib import Path
import shutil
import sys
import tarfile
from unittest import mock
import uuid

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tools/FootballSimulator"))
from build_bundle import build
from bundle_common import VENDOR, digest, package_files

spec = importlib.util.spec_from_file_location("football_setup", ROOT / "Tools/FootballSimulator/setup.py")
setup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(setup)
fixture = (ROOT / "Temp/VendorBundleTests" / uuid.uuid4().hex).resolve()
fixture.mkdir(parents=True)


def check_reject(action, message):
    try:
        action()
    except (ValueError, OSError):
        return
    raise AssertionError(message)


def package_at(path, assets):
    with tarfile.open(path, "w:gz") as archive:
        for index, (name, data) in enumerate(assets):
            group = f"asset-{index}"
            records = {"pathname": name.encode(), "asset.meta": f"guid: {index:032x}\n".encode()}
            if data is not None:
                records["asset"] = data
            for leaf, body in records.items():
                member = tarfile.TarInfo(group + "/" + leaf)
                member.size = len(body)
                archive.addfile(member, io.BytesIO(body))


def snapshot(project):
    return {p.relative_to(project).as_posix(): p.read_bytes() for p in project.rglob("*")
            if p.is_file() and "Temp" not in p.relative_to(project).parts}


package = fixture / "original.unitypackage"
package_at(package, [
    ("Assets/FootballSimulator", None),
    ("Assets/FootballSimulator/Code/Example.cs", b"\xef\xbb\xbfclass Example {}\r\n"),
    ("Assets/FootballSimulator/Art.bin", bytes(range(256)) * 8),
    ("Assets/FootballSimulator/Keep.bin", b"keep exactly"),
    ("Assets/FootballSimulator/Removed.bin", b"removed"),
    ("ProjectSettings/ProjectSettings.asset\n00", b"vendor settings must never replace host settings"),
])
working = fixture / "working"
for relative, data in package_files(package):
    target = working / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
(working / VENDOR / "Code/Example.cs").write_bytes(b"\xef\xbb\xbfclass Example { int patched; }\r\n")
(working / VENDOR / "Art.bin").write_bytes(bytes(range(256)) + b"changed binary" + bytes(range(256)) * 7)
(working / VENDOR / "Removed.bin").unlink()
(working / VENDOR / "Removed.bin.meta").unlink()
(working / VENDOR / "Added.asset").write_bytes(b"\x00added asset\xff")
(working / VENDOR / "Added.asset.meta").write_bytes(b"guid: saved-guid\n")
(working / VENDOR / "EmptyFolder").mkdir()
bundle = fixture / "bundle"
build(package, working, bundle)
manifest = setup.load_manifest(bundle)
fresh = fixture / "fresh"
fresh.mkdir()
settings = fresh / "ProjectSettings/ProjectSettings.asset"
settings.parent.mkdir()
settings.write_bytes(b"host settings")
setup.install(fresh, package, bundle, manifest, dry_run=True)
assert not (fresh / VENDOR).exists(), "Dry run changed the vendor tree"
setup.install(fresh, package, bundle, manifest)
expected = snapshot(working)
actual = snapshot(fresh)
assert all(actual[path] == data for path, data in expected.items()), "Byte-for-byte restore failed"
assert settings.read_bytes() == b"host settings", "Package settings escaped vendor scope"
assert not (fresh / VENDOR / "Removed.bin").exists()
assert (fresh / VENDOR / "EmptyFolder").is_dir()
with mock.patch.object(setup.os, "replace", side_effect=AssertionError("Idempotent setup wrote a file")):
    setup.install(fresh, package, bundle, manifest)

# Reject unsupported local edits before touching a different, recognized old file.
changed = fresh / VENDOR / "Code/Example.cs"
changed.write_bytes(b"unrecognized local changes")
recognized = fresh / VENDOR / "Art.bin"
old_art = dict(package_files(package))[VENDOR + "/Art.bin"]
recognized.write_bytes(old_art)
before = snapshot(fresh)
check_reject(lambda: setup.install(fresh, package, bundle, manifest), "Local edits were overwritten")
assert snapshot(fresh) == before, "Preflight rejection modified files"

# A supported intermediate state is rebuilt from the original, not patched in place.
code_entry = next(e for e in manifest["files"] if e["path"].endswith("Example.cs"))
code_entry["accepted_intermediate_sha256"] = [digest(b"intermediate")]
changed.write_bytes(b"intermediate")
setup.install(fresh, package, bundle, manifest)
assert changed.read_bytes() == expected[VENDOR + "/Code/Example.cs"]

# Force a failure during a multi-file commit, then prove exact old bytes were restored.
rollback = fixture / "rollback"
for relative, data in package_files(package):
    target = rollback / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
before = snapshot(rollback)
real_replace = setup.os.replace
calls = 0


def fail_once(source, target):
    global calls
    calls += 1
    if calls == 3:
        raise OSError("synthetic disk failure")
    return real_replace(source, target)


with mock.patch.object(setup.os, "replace", side_effect=fail_once):
    check_reject(lambda: setup.install(rollback, package, bundle, manifest), "Commit failure was ignored")
assert snapshot(rollback) == before, "Rollback lost existing asset bytes"

# If restoration itself fails, retain the backup rather than deleting the only old copy.
recovery = fixture / "recovery"
for relative, data in package_files(package):
    target = recovery / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
calls = 0


def fail_commit_and_rollback(source, target):
    global calls
    calls += 1
    if calls in (3, 4):
        raise OSError("synthetic commit/rollback failure")
    return real_replace(source, target)


diagnostic = io.StringIO()
with contextlib.redirect_stderr(diagnostic), mock.patch.object(setup.os, "replace", side_effect=fail_commit_and_rollback):
    check_reject(lambda: setup.install(recovery, package, bundle, manifest), "Rollback failure was ignored")
assert "Recovery backups retained" in diagnostic.getvalue()
indexes = list((recovery / "Temp/FootballSimulatorSetup").rglob("recovery.json"))
assert len(indexes) == 1, "Rollback failure discarded recovery information"
backups = json.loads(indexes[0].read_text())
assert any(path and Path(path).is_file() for path in backups.values()), "Rollback failure deleted old bytes"

bad_package = fixture / "wrong.unitypackage"
bad_package.write_bytes(package.read_bytes() + b"different release")
check_reject(lambda: setup.install(fresh, bad_package, bundle, manifest), "Wrong package accepted")
bad_bundle = fixture / "bad-bundle"
shutil.copytree(bundle, bad_bundle)
with (bad_bundle / "changes.zip").open("ab") as stream:
    stream.write(b"corrupt")
check_reject(lambda: setup.load_manifest(bad_bundle), "Corrupt bundle accepted")
bad_manifest = json.loads(json.dumps(manifest))
bad_manifest["files"][0]["path"] = VENDOR + "/../../escape"
check_reject(lambda: setup.preflight(fresh, bad_manifest), "Traversal accepted")
unsafe = fixture / "unsafe.unitypackage"
package_at(unsafe, [("Assets/FootballSimulator/../../escape", b"bad")])
check_reject(lambda: list(package_files(unsafe)), "Package traversal accepted")
print("Complete vendor bundle: restore, binary/GUID preservation, dry-run, idempotence, preflight, rollback, corruption, and path tests passed.")
