"""Reapply HBOx's gameplay RNG patch to the exact vendor source version. No vendor assets are distributed."""
from pathlib import Path
import hashlib
import json
import re
import sys

root = Path(__file__).resolve().parents[2]
manifest = json.loads(Path(__file__).with_name("seed-patch.json").read_text())
pending = []
for entry in manifest:
    path = root / entry["path"]
    original = path.read_bytes()
    digest = hashlib.sha256(original).hexdigest()
    if digest == entry["after"]:
        continue
    if digest != entry["before"]:
        sys.exit(f"Vendor source differs: {entry['path']}. Review this version before patching; no files changed.")
    text = original.decode("utf-8-sig")
    updated = re.sub(r"\b(?:UnityEngine\.)?(?:URandom|Random)\.Range",
                     "global::FStudio.MatchEngine.SoccerSimulationRandom.Range", text)
    updated = re.sub(r"(?:System\.)?Guid\.NewGuid\s*\(\s*\)",
                     "global::FStudio.MatchEngine.SoccerSimulationRandom.Range(0, int.MaxValue)", updated)
    if path.name == "MatchManager.cs":
        updated = re.sub(r"(public class MatchManager[^\n]*\{)",
                         r"\1\n        public const int SoccerSeedPatchVersion = 1;", updated, count=1)
    result = updated.encode("utf-8")
    if hashlib.sha256(result).hexdigest() != entry["after"]:
        sys.exit(f"Patch verification failed: {path}; no files changed.")
    pending.append((path, result))
for path, result in pending:
    path.write_bytes(result)
print(f"Seed patch verified ({len(manifest)} files); applied to {len(pending)} files.")
