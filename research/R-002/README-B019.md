# Fortress Souls B-019 repo artifacts

This bundle contains the checked-in artifacts for the validated DFHack B-019 script prototype.

## Contents

```text
docs/research/dfhack-field-map.md
docs/decisions/adr-0003-dfhack-adapter.md
docs/runbooks/dfhack-b019-manual-validation.md
dfhack/samples/dwarves-list.sample.json
dfhack/samples/dwarf-snapshot.sample.json
dfhack/samples/b019-dwarf-snapshots.bundle.json
dfhack/samples/snapshots/dwarf-snapshot-*.json
dfhack/samples/b019-snapshot-summary.csv
scripts/import-b019-dfhack-scripts.ps1
scripts/validate-b019-samples.ps1
```

The Lua scripts themselves were validated on the user's DFHack installation. Use `scripts/import-b019-dfhack-scripts.ps1` to copy the local validated `list-dwarves.lua` and `get-dwarf-snapshot.lua` from the DFHack install into the repository.

## Validation result

```text
ListCount            : 7
ValidSnapshotCount   : 7
ErrorSnapshotCount   : 0
InvalidSnapshotCount : 0
```
