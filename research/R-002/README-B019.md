# Fortress Souls B-019 repo artifacts

This bundle contains the checked-in artifacts for the validated DFHack B-019 script prototype.

## Contents

```text
../../docs/research/dfhack-field-map.md
../../docs/decisions/adr-0003-dfhack-adapter.md
../../docs/runbooks/dfhack-b019-manual-validation.md
../../dfhack/samples/dwarves-list.sample.json
../../dfhack/samples/dwarf-snapshot.sample.json
../../dfhack/samples/b019-dwarf-snapshots.bundle.json
../../dfhack/samples/snapshots/dwarf-snapshot-*.json
../../dfhack/samples/b019-snapshot-summary.csv
scripts/import-b019-dfhack-scripts.ps1
scripts/validate-b019-samples.ps1
```

The Lua scripts were validated on the user's DFHack installation and now live in `../../dfhack/scripts/fortress-souls/`. Use `scripts/import-b019-dfhack-scripts.ps1` if you need to refresh `diagnose.lua`, `list-dwarves.lua`, and `get-dwarf-snapshot.lua` from a local DFHack install.

## Validation result

```text
ListCount            : 7
ValidSnapshotCount   : 7
ErrorSnapshotCount   : 0
InvalidSnapshotCount : 0
```
