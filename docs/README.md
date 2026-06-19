# Fortress Souls documentation index

This folder contains project-level documentation that should be stable across research spikes and implementation work.

## Decisions

- `decisions/adr-0003-dfhack-adapter.md` records the accepted v0.1 DFHack adapter invocation strategy.

## Research

- `research/dfhack-command-invocation.md` records R-001 manual verification of safe DFHack command invocation.
- `research/dfhack-field-map.md` records R-002/B-019 field mapping decisions for the validated dwarf list and snapshot scripts.

## Runbooks

- `runbooks/dfhack-b019-manual-validation.md` describes the manual validation flow for the B-019 DFHack scripts.

## Repository placement conventions

- Production DFHack scripts live in `dfhack/scripts/fortress-souls/`.
- Adapter/sample JSON artifacts live in `dfhack/samples/`.
- Research-only probes and spike-specific notes remain under `research/`.
