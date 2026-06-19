# R-002A: DFHack Soul and Personality Field Map

**Status:** Completed research spike  
**Date:** 2026-06-19  
**Project:** Fortress Souls v0.1  
**Input artifact:** `r002-soul-snapshots.zip`  
**Scope:** Dwarf identity, skills, soul/personality, values, needs, mannerisms, prompt candidates  
**Out of scope for this document:** health/wounds, location, relationships, squads, historical identity, recent events

---

## 1. Research question

Which DFHack/Lua soul and personality fields are reliable enough for the Fortress Souls v0.1 dwarf snapshot?

The project principle is:

> Prefer a small reliable snapshot over a huge unreliable one.

The raw Dwarf Fortress structures are useful, but they must be mapped into curated, versioned DTOs. Do not expose raw DF memory directly to the model.

---

## 2. Test dataset

Bulk extraction was run against a live fortress with 7 citizens.

Extraction result:

```text
CitizenCount  : 7
SnapshotCount : 7
FailureCount  : 0
```

The uploaded zip included 8 snapshot entries because unit `6603` appeared twice in the bundled artifact, once as `dwarf-6603.json` and once as `dwarf-6603 copy.json`. The analysis in this document deduplicates by `identity.id`.

---

## 3. Citizen coverage summary

| Unit id | Name | Profession | Current job | Stress raw | Stress cat | Skills | Traits | Values | Needs | Mannerisms | Preferences | Emotions |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 6597 | Melbil Keskalmeden "Shottribe", Miner | Miner | PickupEquipment | 0 | 3 | 7 | 50 | 2 | 21 | 0 | 0 | 0 |
| 6598 | Rakust Tekkudthedak "Pickclans", Planter | Planter |  | 0 | 3 | 6 | 50 | 3 | 22 | 0 | 0 | 0 |
| 6599 | îton Oltarkurik "Gildthorn", Woodworker | Woodworker | PickupEquipment | 0 | 3 | 8 | 50 | 3 | 22 | 0 | 0 | 0 |
| 6600 | Edzul Nimarlogem "Pathpaints", Stonecrafter | Stonecrafter |  | 0 | 3 | 6 | 50 | 2 | 22 | 1 | 0 | 0 |
| 6601 | Kadôl Thocitoddom "Spikescloisters", Fisherdwarf | Fisherdwarf | Fish | 0 | 3 | 6 | 50 | 3 | 19 | 1 | 0 | 0 |
| 6602 | Olon Regokil "Glovetest", Mason | Mason |  | 0 | 3 | 8 | 50 | 2 | 23 | 0 | 0 | 0 |
| 6603 | Olon Lisidlogem "Clashpaints", expedition leader | expedition leader |  | 0 | 3 | 10 | 50 | 2 | 21 | 2 | 0 | 0 |

Observations:

- All 7 citizens serialized successfully.
- Every citizen had 50 personality traits.
- Every citizen had 2 or 3 values.
- Every citizen had 19 to 23 needs.
- Skills varied from 6 to 10 per dwarf.
- Mannerisms appeared for 3 of 7 dwarves.
- Preferences, emotions, memories, and habits were empty in this young fortress sample.

---

## 4. Confirmed field decisions

| Section | v0.1 decision | Reason |
|---|---:|---|
| `identity` | Keep | Stable and useful for display and prompt grounding. |
| `flags` | Keep in debug/source | Useful diagnostics: citizen, sane, alive, dwarf, active. |
| `work.currentJobType` | Keep nullable | Present for some dwarves, absent for others. Missing current job must not be interpreted as idle without proof. |
| `stress.raw` | Keep | Direct raw stress value. |
| `stress.category` | Keep with scale note | DFHack category scale is 0 most stressed to 6 least stressed. |
| `skills.items` | Keep | Tokens, ratings, effective levels, and XP serialize cleanly. |
| `personality.traits.items` | Keep raw; prompt only extremes | 50 named facets are stable but too noisy to feed wholesale to the model. |
| `personality.values.items` | Keep `type`, `token`, `strength` only | `personality_valuest` has `type` and `strength`; there is no `value` field. |
| `personality.needs.items` | Keep | `focusLevel`, `needLevel`, `deityId` are useful. `needLevel` is severity/decay pressure, not current satisfaction. |
| `personality.mannerisms.items` | Keep | Sparse but high flavour-to-token value. |
| `personality.preferences.items` | Keep schema, but low confidence until non-empty sample | Empty in this sample. Needs another fortress or older citizens. |
| `personality.emotions.items` | Defer from prompt, keep in debug if present | Empty in this sample. Needs richer fortress history. |
| `personality.memories` | Defer | Memory handler absent in this sample. Likely relevant later. |
| `promptCandidates` | Keep | Good seam between full snapshot and LLM input. |

---

## 5. Important corrections from the spike

### 5.1 Trait indexing

`unit_personality.traits` is a static array indexed by `personality_facet_type`.

Therefore use:

```lua
token = enum_name(df.personality_facet_type, index)
```

Do not subtract one.

### 5.2 Values

`personality_valuest` contains:

```text
type
strength
```

The field originally probed as `belief.value` does not exist and caused errors.

Correct shape:

```json
{
  "type": 2,
  "token": "FAMILY",
  "strength": -25
}
```

### 5.3 Needs

`personality_needst` contains:

```text
id
deity_id
focus_level
need_level
```

Interpretation for v0.1:

- `focusLevel`: current satisfaction/focus. It can rise when satisfied and drop when unmet.
- `needLevel`: raw severity or decay pressure. It is not the same as current unmet urgency.
- `isUnmet`: derive from `focusLevel < 0`.
- `isDeeplyUnmet`: derive from `focusLevel < -999`.

### 5.4 Empty-object issue

Lua tables with all nil fields may serialize as `[]`, not `{}`.

Avoid final DTOs that rely on empty object shape. For nullable sections, include at least one stable boolean or count field, for example:

```json
{
  "hasCurrentJob": false,
  "currentJobType": null
}
```

The production script should avoid returning sections that collapse into arrays due to nil-only Lua tables.

---

## 6. Prompt candidate rules

The snapshot may preserve rich raw data, but the prompt should use compact candidates.

Recommended v0.1 prompt fields:

```json
{
  "topSkills": [],
  "extremeTraits": [],
  "strongValues": [],
  "strongNeeds": [],
  "mannerisms": []
}
```

Rules:

| Candidate | Rule |
|---|---|
| `topSkills` | Sort by effective/rating, include non-zero skills. |
| `extremeTraits` | Include traits with absolute deviation from neutral 50 of at least 10. |
| `strongValues` | Sort by absolute value strength. Do not overinterpret sign yet. |
| `strongNeeds` | Include needs with `needLevel >= 2` or `focusLevel < 0`. |
| `mannerisms` | Include all present mannerisms. They are sparse and flavourful. |

---

## 7. Observed prompt candidate examples

### 7.1 Strong skills

The sample showed strong professional identity:

- miner: `MINING=5`, `APPRAISAL=4`
- planter: `PLANT=5`, `RECORD_KEEPING=5`
- woodworker: `CARPENTRY=5`, `WOODCUTTING=3`
- stonecrafter: `STONECRAFT=5`, `METALCRAFT=5`
- fisherdwarf: `FISH=5`, `MELEE_COMBAT=5`
- expedition leader: `SPEAKING=3`, `CONVERSATION=2`, `NEGOTIATION=2`, `ORGANIZATION=2`

### 7.2 Strong needs

Every dwarf in this sample had `DrinkAlcohol` with `needLevel=10`.

This is useful flavour but should not dominate every prompt. Treat it as part of dwarf baseline unless focus is unmet or the player asks about mood/needs.

Other high-signal needs:

- `CraftObject=5`
- `PrayOrMeditate=5`
- `HelpSomebody=5`
- `AdmireArt=2`
- `PracticeSkill=2`
- `StayOccupied=2`

### 7.3 Mannerisms

Observed mannerisms:

| Unit | Mannerism | Situation |
|---|---|---|
| 6600 | `POSTURE_RIGID` | `WHEN_ANGRY` |
| 6601 | `CHEWS_CHEEK` | `WHEN_TRYING_TO_REMEMBER` |
| 6603 | `TONGUE_STICKS_OUT` | `WHEN_THINKING` |
| 6603 | `EYES_LOWERS` | `WHEN_ANGRY` |

These are excellent prompt material because they are concrete and characterful.

---

## 8. Recommended v0.1 `DwarfSoulSnapshot` shape

```json
{
  "schemaVersion": "dwarf-soul-snapshot.v0.1",
  "identity": {
    "id": "6597",
    "readableName": "Melbil Keskalmeden \"Shottribe\", Miner",
    "professionName": "Miner",
    "professionToken": "MINER"
  },
  "flags": {
    "isCitizen": true,
    "isResident": false,
    "isSane": true,
    "isAlive": true,
    "isDwarf": true,
    "isActive": true
  },
  "work": {
    "hasCurrentJob": true,
    "currentJobType": "PickupEquipment"
  },
  "stress": {
    "raw": 0,
    "longterm": 0,
    "category": 3,
    "categoryScale": "0-most-stressed-6-least-stressed"
  },
  "skills": {
    "count": 7,
    "items": []
  },
  "personality": {
    "state": {},
    "traits": {},
    "values": {},
    "needs": {},
    "mannerisms": {},
    "preferences": {}
  },
  "promptCandidates": {
    "topSkills": [],
    "extremeTraits": [],
    "strongValues": [],
    "strongNeeds": [],
    "mannerisms": []
  }
}
```

---

## 9. DTO inclusion recommendations

### Include in v0.1 production snapshot

1. `DwarfIdentity`
2. `DwarfFlags`
3. `DwarfCurrentWork`
4. `DwarfStress`
5. `DwarfSkill`
6. `DwarfPersonalityTrait`
7. `DwarfValue`
8. `DwarfNeed`
9. `DwarfMannerism`
10. `DwarfPromptCandidates`

### Include but not prompt by default

1. `DwarfPersonalityState`
2. `DwarfPreference`
3. `DwarfEmotion`
4. `DwarfMemory`

### Defer to R-002B

1. `DwarfHealth`
2. `DwarfWounds`
3. `DwarfInventory`
4. `DwarfLocationSummary`
5. `DwarfRoles`
6. `DwarfRelationships`

---

## 10. Risks and caveats

| Risk | Mitigation |
|---|---|
| Young fortress has no emotions/memories/preferences | Test on older/richer fortress later. Keep schema tolerant. |
| Trait/value semantics are easy to overinterpret | Store raw values and a small interpretation guide. Do not generate false certainty. |
| Empty Lua tables serialize as arrays | Include stable boolean/count fields in every DTO section. |
| All dwarves have `DrinkAlcohol=10` | Treat as baseline unless focus is unmet or need differs from common baseline. |
| Value sign semantics need care | Keep raw sign. Use labels cautiously until validated against in-game prose. |
| Raw structure fields may vary by DF/DFHack version | Include schema version, DFHack version in future diagnostic output, and tolerant parsing. |

---

## 11. Decision

R-002A concludes that soul/personality extraction through DFHack Lua is reliable enough for v0.1, provided that output is curated and primitive-only.

Proceed to:

1. update `probe-dwarf-soul.lua` into a production-oriented `get-dwarf-snapshot.lua`,
2. create sample JSON under `dfhack/samples/`,
3. start R-002B for live unit state: health, wounds, location, inventory, roles, and relationships.
