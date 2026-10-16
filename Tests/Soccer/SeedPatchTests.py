"""Exercise patch application/preflight without redistributing vendor source."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

root = Path(__file__).resolve().parents[2]
fixture = root / "Temp" / "SeedPatchTests" / uuid.uuid4().hex
tool = fixture / "Tools" / "FootballSimulator"
tool.mkdir(parents=True)
script = tool / "apply_seed_patch.py"
shutil.copyfile(root / "Tools/FootballSimulator/apply_seed_patch.py", script)
originals = {
    "Assets/MatchManager.cs": b"public class MatchManager {\nint x = Random.Range(0, 3);\n}\n",
    "Assets/Player.cs": b"var order = System.Guid.NewGuid ();\n",
}
results = {
    "Assets/MatchManager.cs": b"public class MatchManager {\n        public const int SoccerSeedPatchVersion = 1;\nint x = global::FStudio.MatchEngine.SoccerSimulationRandom.Range(0, 3);\n}\n",
    "Assets/Player.cs": b"var order = global::FStudio.MatchEngine.SoccerSimulationRandom.Range(0, int.MaxValue);\n",
}
manifest = []
for path, data in originals.items():
    target = fixture / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
    manifest.append(dict(path=path, before=hashlib.sha256(data).hexdigest(), after=hashlib.sha256(results[path]).hexdigest()))
(tool / "seed-patch.json").write_text(json.dumps(manifest))
for _ in range(2):
    subprocess.run([sys.executable, str(script)], check=True, capture_output=True)
    assert all((fixture / path).read_bytes() == data for path, data in results.items())
for path, data in originals.items():
    (fixture / path).write_bytes(data)
(fixture / "Assets/Player.cs").write_bytes(b"different vendor version")
failure = subprocess.run([sys.executable, str(script)], capture_output=True)
assert failure.returncode != 0
assert (fixture / "Assets/MatchManager.cs").read_bytes() == originals["Assets/MatchManager.cs"], "Preflight edited an earlier file before rejecting a later mismatch"
print("Vendor seed patch application, idempotence, and preflight checks passed.")
