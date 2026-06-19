# R-002B Prep: DFHack Live-State Probes

**Status:** Archived exploratory spike  
**Date:** 2026-06-19  
**Project:** Fortress Souls v0.1  
**Source probes:** `probe-dwarf-location.lua`, `probe-dwarf-live.lua`  
**Canonical production boundary:** `dfhack/scripts/fortress-souls/get-dwarf-snapshot.lua`

---

## 1. Purpose

R-002 included exploratory probes beyond soul/personality mapping. Those probes tested whether DFHack/Lua could expose richer live unit state for future slices without committing that data to the v0.1 snapshot contract.

The explored areas were:

- location and pathing
- current job details
- health and wounds
- inventory
- noble and military roles
- relationship identifiers
- assorted refs, counters, and syndromes

This document preserves the useful conclusions from those probes so the raw spike scripts can be removed from the repo.

---

## 2. What was explored

### 2.1 Location and job context

The location probe showed that DFHack can expose:

- unit position (`pos`)
- path destination
- idle area and burrow identifiers
- current job metadata
- building-at-position lookups

This makes a future `DwarfLocationSummary` feasible, but the fields are operationally noisy and need curation before they are appropriate for a model-facing DTO.

### 2.2 Health and wounds

The broader live-state probe showed that DFHack can expose:

- body state and blood/infection counters
- wound collections
- syndrome state
- pain, nausea, exhaustion, and similar counters

This suggests `DwarfHealth` and `DwarfWounds` are technically accessible, but the shape is too verbose and low-level for v0.1. It needs a curated design rather than raw passthrough.

### 2.3 Inventory, roles, and relationship seams

The same probe also demonstrated access to:

- inventory items and equip modes
- noble or military position hints
- general and specific refs
- relationship-linked identifiers

These look promising for future gameplay grounding, but they were not mature enough to freeze into the initial snapshot contract.

---

## 3. Why these probes were not promoted into v0.1

The probes were useful for discovery, but not suitable as canonical production assets:

- they exposed much broader raw state than v0.1 needs
- several sections were debug-oriented rather than stable DTO design
- some structures are sparse, version-sensitive, or likely to invite overinterpretation
- the v0.1 product loop only needs a reliable curated dwarf identity and soul/personality snapshot

The project rule still applies:

> Prefer a small reliable snapshot over a huge unreliable one.

---

## 4. Current decision

The v0.1 snapshot remains intentionally narrow.

Deferred from the production snapshot contract:

1. `DwarfHealth`
2. `DwarfWounds`
3. `DwarfInventory`
4. `DwarfLocationSummary`
5. `DwarfRoles`
6. `DwarfRelationships`

The production seam for v0.1 remains:

- Lua source in `dfhack/scripts/fortress-souls/`
- retained canonical samples in `dfhack/samples/`
- soul/personality field decisions in `docs/research/dfhack-field-map.md`

---

## 5. Follow-up guidance

If this work resumes later, start from a new curated document and fresh production-oriented samples rather than resurrecting the raw spike probes directly.

Use the deleted probes only as historical evidence that DFHack can read these categories at all; do not treat their output schema as a contract.
