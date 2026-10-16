"""Byte-preserving helpers shared by the vendor bundle builder and installer."""
import hashlib
from pathlib import Path, PurePosixPath
import tarfile

VENDOR = "Assets/3rdParty/FootballSimulator"
SOURCE = "Assets/FootballSimulator"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def file_digest(path):
    value = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def validate_path(value):
    path = PurePosixPath(value)
    if (not isinstance(value, str) or "\\" in value or ":" in value or "\x00" in value or
            path.is_absolute() or ".." in path.parts or str(path) != value or
            not (value == VENDOR or value == VENDOR + ".meta" or value.startswith(VENDOR + "/"))):
        raise ValueError(f"Unsafe vendor destination: {value!r}")
    return path


def destination(project, value):
    path = project.joinpath(*validate_path(value).parts)
    # Reject junctions/symlinks, including links that lead elsewhere inside the project.
    if path.resolve() != path.absolute():
        raise ValueError(f"Destination passes through a link: {path}")
    if not path.resolve().is_relative_to(project):
        raise ValueError(f"Destination escapes project: {path}")
    return path


def package_files(package):
    """Read only vendor files; never extract paths, links, or package ProjectSettings.

    Two sequential passes avoid repeated gzip seeks on large art files. Unity packages
    may append extra lines to ProjectSettings pathname records; only line one is a path.
    """
    with tarfile.open(package, "r:gz") as archive:
        members = archive.getmembers()
        mapping = {}
        for member in members:
            if not member.name.endswith("/pathname"):
                continue
            if not member.isfile() or member.size > 8192:
                raise ValueError("Invalid package pathname record")
            raw = archive.extractfile(member).read().decode("utf-8-sig")
            name = raw.splitlines()[0].rstrip("\x00")
            if name != SOURCE and not name.startswith(SOURCE + "/"):
                continue
            relative = VENDOR + name[len(SOURCE):]
            validate_path(relative)
            group = member.name.rsplit("/", 1)[0]
            if group in mapping:
                raise ValueError("Duplicate package asset group")
            mapping[group] = relative
        seen = set()
        for member in members:
            group, _, leaf = member.name.rpartition("/")
            if group not in mapping or leaf not in ("asset", "asset.meta"):
                continue
            if not member.isfile():
                raise ValueError("Vendor assets must be regular files")
            relative = mapping[group] + (".meta" if leaf == "asset.meta" else "")
            validate_path(relative)
            if relative.casefold() in seen:
                raise ValueError(f"Duplicate vendor package path: {relative}")
            seen.add(relative.casefold())
            yield relative, archive.extractfile(member).read()
        if not seen:
            raise ValueError(f"Package does not contain {SOURCE}")


def splice(before, after):
    """Store only a replacement middle; reuse exact prefix/suffix bytes from vendor."""
    prefix = 0
    limit = min(len(before), len(after))
    while prefix < limit and before[prefix] == after[prefix]:
        prefix += 1
    suffix = 0
    while suffix < limit - prefix and before[-1 - suffix] == after[-1 - suffix]:
        suffix += 1
    return prefix, suffix, after[prefix:len(after) - suffix if suffix else len(after)]


def reconstruct(entry, before, payload):
    if entry["operation"] == "copy":
        result = before
    elif entry["operation"] == "remove":
        return None
    else:
        prefix, suffix = entry["prefix"], entry["suffix"]
        if prefix < 0 or suffix < 0 or prefix + suffix > len(before):
            raise ValueError(f"Invalid splice bounds: {entry['path']}")
        if digest(payload) != entry["payload_sha256"]:
            raise ValueError(f"Payload checksum mismatch: {entry['path']}")
        result = before[:prefix] + payload + (before[-suffix:] if suffix else b"")
    if len(result) != entry["after_size"] or digest(result) != entry["after_sha256"]:
        raise ValueError(f"Reconstructed checksum mismatch: {entry['path']}")
    return result
