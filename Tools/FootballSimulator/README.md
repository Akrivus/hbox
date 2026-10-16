# Rebuilding FootballSimulator

This bundle reconstructs the complete working HBOx simulator directory from the pinned
original Asset Store `.unitypackage`, including all earlier vendor edits and the seed
changes. Python 3.10+ and the HBOx checkout are required. No pip packages are needed.

Close Unity before installing. From the HBOx repository root:

```powershell
.\Tools\FootballSimulator\Setup.ps1 -Package 'D:\Backups\Football Soccer Simulator.unitypackage' -DryRun
.\Tools\FootballSimulator\Setup.ps1 -Package 'D:\Backups\Football Soccer Simulator.unitypackage'
.\Tools\FootballSimulator\Setup.ps1 -Verify
```

You can run the same installer directly on other hosts:

```text
python Tools/FootballSimulator/setup.py --package /backups/original.unitypackage
python Tools/FootballSimulator/setup.py --verify
```

`-ProjectRoot` / `--project-root` selects another checkout. The default is the checkout
containing the script. An empty vendor directory is supported: importing the package in
Unity first is unnecessary. Installing over the exact original package, the captured
working tree, or a recognized pre-seed state is also supported. Unknown local file
edits are rejected before changing any destination file; there is no force-overwrite
option. Unmanaged extra files are left alone. `--verify` checks every managed file and
directory; it does not claim unmanaged extra files are absent.

After setup, open HBOx using **Unity 6000.3.10f1**, let it resolve the repository's
packages/import assets, and perform the soccer startup/replay smoke checks in
[SoccerSavedMatches.md](../../Docs/SoccerSavedMatches.md). The repository's own project
settings and host integration remain prerequisites. This tool does not install Unity,
other ignored dependencies such as Synty, or reproduce the vendor's standalone project.

## Original package to preserve

Keep this exact original distribution in your workstation backup:

- Filename: `Football Soccer Simulator.unitypackage`
- Size: **111,471,045 bytes**
- SHA-256: `16742847c23aa99fc9fc457ca10a167cacf79093577cc5c44a6c8752a20cf946`

The source used to build this bundle was found in Unity's local Asset Store cache under
`%APPDATA%/Unity/Asset Store-5.x/EsnetSoftware/Complete ProjectsPacks/`. A newly downloaded
vendor release may differ. Setup intentionally rejects an unknown package rather than
guessing how to apply changes. The original package is not included in this repository.

## What is captured

The manifest manages **2,270 file states and 212 directories**:

- 1,095 unchanged files restored directly from the original package.
- 1,131 changed files reconstructed using byte splices against the original.
- 44 added files restored from the bundle.

The changes include 35 C# files, 65 materials, two scenes, asset/import settings, and
the added team database and baked-lighting assets. All `.meta` contents, including GUIDs,
are preserved byte for byte. The roughly **26 MiB** bundle includes added lightmaps,
which account for most of its size. All differences are captured, including Unity API
upgrades, generated Input System code, and formatting/importer changes—not just changes
listed in the historical patch notes.

`bundle/source-changes.patch` is a human-readable review of all C# differences;
`bundle/manifest.json` inventories every managed file and its before/after hashes.
The installer uses `bundle/changes.zip`, not the review diff. Unchanged vendor source
and art are read from the separately supplied original package.

Only `Assets/FootballSimulator` from the original package is mapped to
`Assets/3rdParty/FootballSimulator` (plus its root `.meta`). The package's ProjectSettings
are deliberately excluded. The installed tree is about 206 MiB; allow additional space
for staging and rollback backups under `Temp/FootballSimulatorSetup`.

## Installation checks and recovery

Setup checks the package, bundle, original assets, reconstructed outputs, and existing
destination states before committing. Writes are staged on the destination volume;
each file replacement is atomic. If an ordinary commit error occurs, setup restores
the previous bytes. Newly created empty directories may remain after rollback. If
rollback itself fails, backups and `recovery.json` are retained at the printed staging
path. A sudden power loss/process kill is not an atomic multi-file transaction; rerun
setup to complete a partial installation once the destination is available.

The installer refuses paths through junctions/symlinks and never extracts archive paths
directly. Close Unity so asset import cannot race with installation. Verification may
report differences after a newer Unity version reserializes imported assets; review
those changes rather than changing expected hashes to silence the check.

## Capturing future vendor edits

After deliberately changing the ignored working simulator files, regenerate the bundle
against the same original package:

```powershell
python Tools/FootballSimulator/build_bundle.py --package 'D:\Backups\Football Soccer Simulator.unitypackage'
python Tools/FootballSimulator/setup.py --verify
python Tests/Soccer/VendorBundleTests.py
```

Review the manifest counts and source diff, then check in the scripts and all three
files in `bundle/` together. Test a clean restoration into a temporary project before
calling an updated bundle complete. If the original vendor version changes, capture
against that exact new original and review the entire delta; existing bundles remain
pinned to their original package hash.

`apply_seed_patch.py` / `seed-patch.json` remain an incremental compatibility tool for
the older modified baseline. They are unnecessary after complete setup. If future edits
change a seed-patched file, regenerate or retire that incremental tool as appropriate;
the complete bundle manifest is the current restoration authority.

## Validation of this captured bundle

The pinned real package was restored into an empty temporary project and all 2,270
file states and 212 directories passed SHA-256 verification. The restored vendor's
377 C# source files were then compiled with HBOx against the installed Unity/package
assemblies. The installer fixture tests also cover removal, rollback after a simulated
commit failure, and rejection of corrupted bundles, wrong packages, and unsafe paths.
Live Unity import/startup/replay still needs the workstation smoke check above.
