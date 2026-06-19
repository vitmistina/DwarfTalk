# Fortress Souls

## Product and Technical Specification

**Status:** Draft
**Spec version:** 0.3
**Working title:** Fortress Souls
**Target platform:** Dwarf Fortress with DFHack
**Primary capability:** Conversational interaction with individual dwarves and groups of dwarves using interchangeable language-model providers

---

## 1. Product vision

Fortress Souls adds a conversational, perceptual, and political layer to Dwarf Fortress.

The player can speak with individual dwarves whose responses are grounded in:

* their current game state,
* personality and values,
* work and social circumstances,
* local perception of the fortress,
* personal and role-based knowledge,
* prior conversations,
* remembered promises and disagreements,
* fortress craft knowledge expressed in dwarf-readable form,
* a persistent narrative identity represented by a `SOUL.md` document.

Dwarves are not generic chatbots wearing procedurally generated beards. Each dwarf should behave as a persistent social actor whose views emerge from the simulated fortress.

The long-term vision includes:

* individual conversations,
* bounded dwarf perception,
* agentic information gathering,
* persistent relationships and commitments,
* dwarf requests and objections,
* councils containing multiple dwarves,
* structured deliberation,
* voting,
* proposals that may affect game state,
* a Dwarf Fortress knowledge base filtered by what each dwarf could reasonably know,
* interchangeable local and remote language-model providers.

---

## 2. Product thesis

The core feature is not merely:

> Talk to a dwarf using an LLM.

The stronger product thesis is:

> Dwarves become conversational political actors whose identities, perceptions, beliefs, memories, requests, and commitments are grounded in the simulated fortress.

The chat interface makes existing simulation state legible. Persistent memory and consequences turn dialogue into governance. Bounded perception and filtered knowledge prevent the system from becoming an omniscient debug console disguised as Urist.

---

## 3. Design principles

### 3.1 The game remains authoritative

Dwarf Fortress is the canonical source for:

* identity,
* profession,
* skills,
* work assignments,
* relationships represented by the game,
* injuries and physical condition,
* needs and stress,
* location,
* current job,
* historical events,
* material game state.

The language model may interpret and discuss this state. It may not redefine it.

### 3.2 The model speaks for the dwarf

The model is responsible for:

* language,
* tone,
* argumentation,
* interpretation,
* opinions,
* negotiation,
* proposed intentions,
* conversational continuity.

The model must not claim that a game action occurred unless the action was confirmed by the game adapter.

### 3.3 Narrative identity is persistent in later versions

Each dwarf should eventually have a persistent `SOUL.md` document.

Dwarf Fortress already contains its own concept of a soul. The mod's `SOUL.md` is not intended to replace or duplicate the game's soul data. Instead, it serves as a persistent narrative interpretation layer that helps language models understand and consistently portray the dwarf.

`SOUL.md` is not required for version 0.1.

Version 0.1 may use an ephemeral character brief derived from the current dwarf snapshot. This brief is not stored permanently and is regenerated when needed.

### 3.4 Knowledge is bounded

A dwarf must not automatically know everything stored in the game.

Information supplied to the model must eventually distinguish between:

* directly experienced facts,
* personally observed facts,
* information told to the dwarf,
* common fortress knowledge,
* privileged knowledge associated with an office,
* professional knowledge,
* military knowledge,
* craft knowledge,
* cultural knowledge,
* uncertain information,
* unknown information.

Version 0.1 uses a simpler rule:

> The dwarf knows only the extracted dwarf state, the provided system prompt, and the current in-memory chat history.

### 3.5 Perception is bounded in later versions

Later versions should allow a dwarf to inspect its surroundings through controlled tools.

A dwarf may eventually be able to look around, recall stock levels, inspect nearby workshops, or ask what is below the current z-level.

Version 0.1 has no dwarf tool use and no perception calls.

### 3.6 Agentic inquiry is allowed later but excluded from version 0.1

The long-term harness should support multi-step interaction such as:

1. receive player question,
2. inspect current state,
3. call a bounded perception tool,
4. retrieve relevant personal or fortress knowledge,
5. reason over the results,
6. answer as the dwarf.

Version 0.1 does not implement this.

The version 0.1 model receives context and produces a response. It does not call tools.

### 3.7 Consequential actions are structured

The model may eventually propose an action through a structured interface.

All proposed actions must be:

1. validated,
2. checked against current game state,
3. authorized according to player settings,
4. executed by deterministic code,
5. returned to the model as a confirmed or rejected result.

Version 0.1 has no write actions and no proposed-action execution.

### 3.8 Providers are interchangeable

The system must not depend on one language-model vendor.

Supported provider categories should include:

* hosted API providers,
* OpenAI-compatible APIs,
* local models,
* experimental subscription-backed providers where technically and legally supported.

### 3.9 Read-only before read-write

The first usable versions must not mutate Dwarf Fortress state.

Dialogue quality and basic grounding must be proven before the system gains memory, perception, knowledge retrieval, or mechanical authority.

### 3.10 Interpretation must be explicit

Raw Dwarf Fortress values are often difficult for language models to interpret correctly.

The system should provide structured interpretation guidance describing:

* valid ranges,
* minimum and maximum values,
* typical values,
* percentile meanings,
* categorical thresholds,
* game-specific semantics,
* known caveats.

Version 0.1 may use a small hand-written interpretation guide embedded in the system prompt.

Later versions should move this into a deterministic interpretation service.

### 3.11 Retrieval must be permissioned

The knowledge system must not simply retrieve the “best answer” from a Dwarf Fortress wiki-like corpus.

It must retrieve the best answer the dwarf could plausibly know, based on:

* profession,
* skills,
* role,
* social position,
* experience,
* literacy,
* military training,
* exposure to relevant events,
* access to records,
* cultural common sense,
* previous conversations.

This is not part of version 0.1.

---

## 4. Release roadmap

## 4.1 Version 0.1: Minimal external dwarf chat

Version 0.1 is the smallest useful product slice.

Its purpose is to validate whether a player can select a dwarf, extract the dwarf’s state, feed that state into an LLM, and have a satisfying chat.

Version 0.1 should avoid persistence, tools, councils, promises, voting, knowledge bases, and write actions.

### Version 0.1 user flow

1. The player starts Dwarf Fortress with DFHack available.
2. The player starts the separate Fortress Souls web app.
3. The web app connects to the local DFHack adapter.
4. The web app displays a list of current dwarves.
5. The player selects one dwarf.
6. The app extracts a curated dwarf-state snapshot.
7. The app assembles a prompt from:

   * base system prompt,
   * dwarf-state snapshot,
   * simple interpretation guidance,
   * current in-memory conversation turns.
8. The player chats with the dwarf.
9. The response is displayed in the web app.
10. The conversation exists only for the current session unless basic debug logging is explicitly enabled.

### Version 0.1 included capabilities

Version 0.1 includes:

* separate local web app,
* list of dwarves,
* select one dwarf,
* extract dwarf state,
* display extracted dwarf state for debugging,
* configure one model provider,
* send prompt to model provider,
* receive model response,
* maintain in-memory chat turns during the active session,
* optionally refresh the dwarf snapshot manually,
* optionally show the exact prompt in a developer panel.

### Version 0.1 excluded capabilities

Version 0.1 excludes:

* persistent `SOUL.md`,
* persistent memory,
* promises,
* commitments,
* grievances,
* knowledge base,
* dwarf perception tools,
* `.look(...)`,
* `rememberStock(...)`,
* tool calling,
* agentic multi-step reasoning,
* councils,
* voting,
* proposed actions,
* game-state mutation,
* in-game overlay,
* role-filtered knowledge,
* learned knowledge,
* long-term relationship tracking.

### Version 0.1 success criterion

Version 0.1 succeeds if:

> A player can select a dwarf from the live fortress, chat with that dwarf in a separate web app, and feel that the dwarf’s answers are meaningfully influenced by the extracted dwarf state.

The bar is not “perfect dwarf consciousness.”

The bar is:

> “This is already a little alive.”

### Version 0.1 architectural shape

```text
┌──────────────────────────────────────────────┐
│              Dwarf Fortress                  │
│                                              │
│ Units, traits, jobs, skills, needs, state    │
└───────────────────┬──────────────────────────┘
                    │
               DFHack adapter
                    │
             dwarf list + snapshot
                    │
┌───────────────────▼──────────────────────────┐
│          Fortress Souls Web App              │
│                                              │
│ Dwarf list                                   │
│ Dwarf snapshot viewer                        │
│ Prompt assembler                             │
│ In-memory chat session                       │
│ Provider client                              │
└───────────────────┬──────────────────────────┘
                    │
              Model provider
```

### Version 0.1 prompt structure

```text
System prompt
  ↓
Dwarf portrayal instructions
  ↓
Current dwarf state snapshot
  ↓
Simple interpretation guide
  ↓
Recent in-memory chat turns
  ↓
Current player message
```

### Version 0.1 model instructions

The version 0.1 system prompt should tell the model:

* You portray this specific dwarf.
* Use only the supplied dwarf state and conversation history.
* Do not claim to know current surroundings unless provided.
* Do not claim to have performed actions.
* Do not invent game events.
* Speak as the dwarf, not as a general assistant.
* If the dwarf would not know something, say so.
* Keep answers grounded in the supplied state.

### Version 0.1 dwarf snapshot

The snapshot should be compact and curated.

Suggested fields:

```text
identity
profession
current job
skills
labors or work details
personality traits
values
preferences
needs
stress or mood indicators
injuries or health
family and important relationships if available
noble or military role if available
location summary if easy to extract
recent notable events if easy to extract
```

Version 0.1 should prefer fewer reliable fields over many confusing fields.

### Version 0.1 persistence policy

By default, version 0.1 does not persist:

* conversations,
* dwarf memories,
* player promises,
* generated summaries,
* generated souls,
* relationship changes.

Optional developer logging may persist:

* prompt text,
* response text,
* dwarf snapshot JSON,
* model provider metadata,
* errors.

Developer logs are diagnostic artifacts, not gameplay memory.

### Version 0.1 acceptance criteria

#### Dwarf selection

* The web app shows a list of dwarves from the current fortress.
* The player can select one dwarf.
* The selected dwarf can be refreshed.

#### State extraction

* The app extracts a curated dwarf-state snapshot.
* The app can show the raw or semi-raw snapshot in a debug panel.
* The extracted state has a schema version.

#### Chat

* The player can send a message.
* The app sends the system prompt, dwarf state, and conversation turns to the model.
* The model response is displayed.
* The chat can continue for multiple turns during the session.

#### Grounding

* The dwarf response reflects at least some supplied dwarf-state details.
* The dwarf does not claim unsupported tool use or game-state changes.
* The dwarf can admit ignorance.

#### Safety

* No write operations are sent to Dwarf Fortress.
* The model cannot call DFHack commands.
* No model-generated text is executed as code.

---

## 4.2 Version 0.2: Grounded memory and promises

Version 0.2 is the first real narrative-memory slice.

It should demonstrate the following scenario:

1. The player selects a dwarf.
2. The adapter extracts the dwarf’s current state.
3. The system generates the dwarf’s first persistent `SOUL.md`.
4. The player asks about an unwanted work assignment.
5. The dwarf explains an objection grounded in personality, skill, current work, or unmet needs.
6. The player makes a promise.
7. The promise is stored as a structured commitment.
8. Time passes in the game.
9. The system checks whether the promise was fulfilled.
10. The dwarf refers to the outcome in a later conversation.
11. `SOUL.md` is amended only if the event meaningfully changes the dwarf’s view of the player.

This slice tests:

* game-state extraction,
* persistent identity,
* `SOUL.md`,
* conversation persistence,
* memory,
* commitments,
* game-time progression,
* basic consequence tracking.

Version 0.2 still does not require:

* perception tools,
* knowledge base,
* councils,
* voting,
* game-state mutation.

---

## 4.3 Version 0.3: Bounded perception

Version 0.3 introduces bounded dwarf perception.

It should demonstrate that a dwarf can inspect limited local information without becoming omniscient.

Example scenario:

1. The player asks a dwarf what is nearby.
2. The dwarf calls a bounded `look(radius, zOffset)` tool.
3. The system returns a compact local grid and parsed observations.
4. The dwarf answers based on the returned perception.
5. Hidden or inaccessible information remains unavailable.

This slice tests:

* local map extraction,
* perception filtering,
* tool calling,
* tool-result authority,
* stale perception handling.

---

## 4.4 Version 0.4: Bounded expertise

Version 0.4 introduces role-filtered Dwarf Fortress knowledge.

Scenario:

1. The player asks a farmer about siege defense.
2. The farmer retrieves only vague or common knowledge.
3. The farmer admits limited expertise.
4. The farmer suggests asking a soldier, mechanic, or militia commander.
5. The player asks the militia commander the same question.
6. The militia commander retrieves relevant military knowledge.
7. The militia commander provides a practical answer.
8. The farmer later remembers only the parts heard in conversation, not full military expertise.

This slice tests:

* knowledge-base retrieval,
* access filtering,
* role-based competence,
* knowledge leakage prevention,
* learned knowledge,
* differentiated dwarf voices.

---

## 4.5 Later versions

Later versions may add:

* councils,
* multiple dwarf deliberation,
* voting,
* in-game overlay,
* proposed work changes,
* player-approved actions,
* limited automatic actions,
* richer relationship simulation,
* dwarf-to-dwarf knowledge sharing,
* fortress political dynamics.

---

## 5. System architecture

```text
┌──────────────────────────────────────────────────────────────┐
│                    Dwarf Fortress                            │
│                                                              │
│ Units, jobs, traits, relationships, events, map, stock, world│
└────────────────────────┬─────────────────────────────────────┘
                         │
                    DFHack adapter
                         │
           snapshots, perception, commands, events
                         │
┌────────────────────────▼─────────────────────────────────────┐
│              Fortress Dialogue Service                       │
│                                                              │
│ Character service         Conversation service               │
│ Soul service              Memory service                     │
│ Interpretation service    Knowledge filter                   │
│ Perception service        Agentic harness                    │
│ Knowledge base service    Prompt assembler                   │
│ Council service           Action validator                   │
│ SQLite persistence        Diagnostics                        │
└──────────────┬────────────────────────┬──────────────────────┘
               │                        │
        External web UI          In-game overlay
                                        later
               │
        Model provider interface
               │
   ┌───────────┼─────────────┬────────────────┐
   │           │             │                │
Hosted API  OpenAI-       Local model     Experimental
provider    compatible                     subscription
            endpoint                       provider
```

For version 0.1, only the following components are required:

```text
DFHack adapter
External web UI
Prompt assembler
In-memory conversation session
Model provider client
Diagnostics panel
```

All other components may be stubbed or absent.

---

## 6. Major components

## 6.1 DFHack adapter

The DFHack adapter isolates the rest of the application from raw game internals.

It must expose curated, versioned data transfer objects rather than raw memory structures.

### Version 0.1 read operations

```text
listDwarves()
getDwarfSnapshot(dwarfId)
```

### Later read operations

```text
getFortressSnapshot()
getDwarfRelationships(dwarfId)
getDwarfCurrentWork(dwarfId)
getDwarfRecentEvents(dwarfId)
getDwarfKnowledgeContext(dwarfId)
getLocalMapView(dwarfId, options)
getApproximateStockMemory(dwarfId, options)
```

### Future write operations

```text
validateProposal(proposal)
applyApprovedWorkAssignment(proposal)
recordGameAnnotation(annotation)
scheduleCouncil(councilRequest)
```

The adapter must never accept arbitrary code or unrestricted model-generated DFHack commands.

---

## 6.2 Character service

The character service eventually owns the mod’s representation of a dwarf.

It combines:

* canonical identifiers from Dwarf Fortress,
* the latest canonical snapshot,
* `SOUL.md`,
* memories,
* commitments,
* conversation metadata,
* mod-owned opinions,
* knowledge permissions,
* model-provider configuration overrides.

Version 0.1 does not require a persistent character service.

It may represent the selected dwarf as an in-memory object created from the current snapshot.

---

## 6.3 Soul service

The soul service creates, maintains, validates, and versions `SOUL.md`.

It must distinguish between:

* canonical facts,
* deterministic interpretations,
* model-generated interpretation,
* persistent social history,
* uncertainty.

The soul service must never silently overwrite canonical data.

Version 0.1 does not implement the soul service.

---

## 6.4 Interpretation service

The interpretation service translates raw Dwarf Fortress values into model-friendly meanings.

Its responsibilities include:

* mapping numerical values to descriptive language,
* defining normal ranges,
* defining extreme ranges,
* explaining game-specific mechanics,
* exposing confidence and uncertainty,
* generating reusable interpretation guides.

Example:

```json
{
  "attribute": "assertiveness",
  "rawValue": 71,
  "minimum": 0,
  "maximum": 100,
  "populationAverage": 50,
  "percentile": 82,
  "interpretation": "More assertive than most dwarves. Comfortable expressing disagreement and defending opinions."
}
```

The interpretation service should be deterministic wherever possible.

Version 0.1 may use static prompt text instead of a full service.

---

## 6.5 Perception service

The perception service allows a dwarf to inspect the world through bounded tools.

It answers questions such as:

* What can this dwarf see nearby?
* What is below or above the dwarf?
* What workshops, stockpiles, creatures, items, hazards, or constructions are nearby?
* What approximate stock information could this dwarf remember?
* What recent local events could this dwarf plausibly have noticed?

The perception service must not expose perfect global knowledge by default.

Version 0.1 does not implement perception.

### Suggested later perception tools

```text
look(radius, zOffset)
lookAround(radius)
lookAbove(radius)
lookBelow(radius)
inspectTile(relativeX, relativeY, zOffset)
inspectNearbyWorkshop()
inspectNearbyStockpile()
rememberStock(category)
recallRecentLocalEvents(timeWindow)
```

---

## 6.6 Memory service

The memory service stores and retrieves:

* rolling conversation summaries,
* durable episodic memories,
* commitments,
* disagreements,
* player promises,
* dwarf requests,
* opinions,
* unresolved topics,
* remembered perceptions,
* knowledge items the dwarf has learned.

Version 0.1 does not implement memory persistence.

---

## 6.7 Knowledge base service

The knowledge base service stores general Dwarf Fortress knowledge in small, linked, retrievable items.

It is conceptually similar to a Personal Knowledge Management system such as an Obsidian vault.

The knowledge base should contain:

* game mechanics,
* farming knowledge,
* food and drink knowledge,
* military knowledge,
* siege defense,
* hospital and medicine,
* moods,
* stress and needs,
* nobles and mandates,
* workshops,
* stockpiles,
* traps,
* burrows,
* aquifers,
* magma,
* caverns,
* trade,
* justice,
* fortress planning.

However, knowledge base items must not be directly exposed to every dwarf.

Version 0.1 does not implement the knowledge base.

---

## 6.8 Knowledge filter

The knowledge filter decides what information may be presented to a particular dwarf.

Its output should classify information using an epistemic category.

```text
DirectExperience
Observed
ToldByPlayer
ToldByDwarf
CommonKnowledge
ProfessionalKnowledge
RolePrivileged
CraftLore
MilitaryTraining
RecordAccess
Rumour
Inferred
Uncertain
Unknown
```

Version 0.1 does not implement a full knowledge filter.

The only filtering rule in version 0.1 is:

> Do not provide information that is not part of the selected dwarf snapshot, system prompt, or current in-memory chat.

---

## 6.9 Agentic harness

The agentic harness orchestrates multi-step dwarf reasoning.

It allows the model to call approved tools such as:

```text
look
rememberStock
retrieveKnowledge
recallMemory
inspectCurrentWork
proposeAction
```

Version 0.1 does not implement the agentic harness.

The model receives a single prompt and produces a single assistant message.

---

## 6.10 Provider abstraction

The provider abstraction must expose a vendor-neutral application interface.

```csharp
public interface ILanguageModelProvider
{
    Task<ModelResponse> GenerateAsync(
        ModelRequest request,
        CancellationToken cancellationToken);
}
```

The internal request should support:

* instructions,
* messages,
* structured output,
* tools,
* token budget,
* generation policy,
* provider-specific extension options.

For version 0.1, this can be simplified to:

```csharp
public interface IChatProvider
{
    Task<string> SendAsync(
        ChatRequest request,
        CancellationToken cancellationToken);
}
```

The version 0.1 design should not make provider-specific request types part of the domain model.

---

## 7. Dwarf identity model

A dwarf has four related but distinct representations in the long-term design.

## 7.1 Canonical dwarf state

Canonical state comes from Dwarf Fortress.

Examples:

* name,
* age,
* sex,
* profession,
* skills,
* values,
* personality facets,
* preferences,
* family relations,
* friendships,
* enemies,
* wounds,
* stress,
* needs,
* current job,
* work details,
* noble position,
* military position.

Canonical state may change independently of the mod.

Version 0.1 uses canonical dwarf state directly in the prompt.

## 7.2 Interpretation guide

The interpretation guide explains how canonical values should be understood.

It provides:

* ranges,
* thresholds,
* averages,
* percentiles,
* semantic descriptions,
* game-specific notes.

Example:

```text
Trait: Anxiety
Range: 0-100
Average: 50

0-20   Rarely worried
21-40  Generally calm
41-60  Typical range
61-80  Frequently worried
81-100 Extremely anxious
```

Version 0.1 may include only a small static guide.

## 7.3 Interpreted identity

Interpreted identity translates numerical or encoded game state into meaningful language.

Example:

```text
Canonical:
orderliness = 82
assertiveness = 71
anxiety = 18

Interpretation:
Urist strongly prefers order, is willing to challenge others directly,
and is rarely troubled by vague or distant risks.
```

Version 0.1 may allow the model to perform some interpretation from supplied guidance.

Later versions should generate interpretations deterministically.

## 7.4 Narrative identity

Narrative identity is represented by `SOUL.md`.

It combines interpreted canonical state with durable personal history.

Example:

```markdown
# Urist McLedger

Urist is a methodical bookkeeper who believes that a fortress survives
through accurate records and predictable responsibilities. He is direct
when he sees disorder, but he does not worry merely for the pleasure of
worrying.

He respects competence more than rank. He currently sees the overseer as
capable but prone to making promises before checking whether the labour
exists to fulfil them.

Urist has repeatedly objected to being assigned hauling work during stock
reviews. The overseer promised that this would not happen again. Urist
considers this promise unresolved.
```

Version 0.1 does not implement persistent narrative identity.

---

## 8. `SOUL.md`

## 8.1 Purpose

`SOUL.md` is a persistent Markdown document describing the dwarf as a coherent person.

It should make raw traits, values, preferences, history, and relationships easier for both humans and models to understand.

It is not intended to replace Dwarf Fortress soul data. Instead, it acts as a narrative interpretation layer built on top of canonical game state.

## 8.2 Version availability

`SOUL.md` begins in version 0.2.

Version 0.1 does not create, store, or update `SOUL.md`.

This is deliberate.

Version 0.1 tests live state-grounded chat before adding persistent identity. Otherwise the first release risks becoming a filing cabinet with aspirations.

## 8.3 Storage

Each dwarf record should eventually contain:

```text
soulMarkdown
soulVersion
soulCreatedAt
soulUpdatedAt
soulSourceSnapshotTick
soulSourceHash
soulGenerationReason
soulSchemaVersion
```

The database should store the Markdown as a text string.

Previous versions should be retained in a soul-history table.

## 8.4 Authority

`SOUL.md` is not canonical game state.

It is a derived narrative artifact.

When `SOUL.md` conflicts with current canonical state:

1. canonical state wins,
2. the discrepancy is recorded,
3. the soul service decides whether regeneration or amendment is needed.

## 8.5 Stability

`SOUL.md` must not be regenerated for every conversation turn.

Constant regeneration would cause personality drift and make the dwarf feel rewritten rather than developed.

Updates should occur after meaningful changes, such as:

* first encounter,
* major profession change,
* appointment to an office,
* significant injury,
* marriage or death of a close relation,
* major mood or stress transition,
* important promise,
* betrayal,
* major dispute,
* council decision with personal consequences,
* explicit player request to refresh the soul,
* detected contradiction with canonical state.

## 8.6 Suggested structure

```markdown
# Identity

A concise description of the dwarf’s social and occupational identity.

## Character

A narrative interpretation of personality facets and values.

## Priorities

What the dwarf currently values or wants most.

## Work

How the dwarf understands their profession, skills, and assignments.

## Knowledge and Competence

What the dwarf understands well, understands vaguely, or does not know.

## Relationships

Important relationships and the dwarf’s interpretation of them.

## View of the Overseer

Current trust, respect, suspicion, resentment, or loyalty toward the player.

## Important Memories

A short selection of identity-shaping events.

## Commitments and Grievances

Promises made, promises received, unresolved disputes, and perceived wrongs.

## Voice

Guidance for conversational style without reducing the dwarf to a caricature.

## Uncertainties

Interpretations that are tentative, conflicting, or based on incomplete information.
```

Not every section must be populated.

---

## 9. Dwarf knowledge model

A dwarf eventually has several kinds of knowledge.

## 9.1 Personal knowledge

Facts from the dwarf’s own state and experience.

Examples:

* current job,
* own injuries,
* own relationships,
* remembered conversations,
* recent local observations,
* personal grievances,
* promises made to or by the dwarf.

## 9.2 Local perceptual knowledge

Facts from the dwarf’s surroundings.

Examples:

* visible creatures,
* nearby workshops,
* nearby stockpiles,
* visible hazards,
* local architecture,
* doors, walls, ramps, stairs,
* what is above or below if plausibly visible or known.

## 9.3 Professional knowledge

Knowledge associated with skills, jobs, and labors.

Examples:

* miners understand mining risks better than children,
* planters understand crops and seeds,
* brewers understand drink production,
* doctors understand wounds and hospitals,
* mechanics understand traps,
* soldiers understand immediate combat better than peasants.

## 9.4 Role-privileged knowledge

Knowledge associated with offices or records.

Examples:

* bookkeepers may know stock records,
* militia commanders may know squad readiness,
* managers may know work orders,
* nobles may know mandates and demands,
* captains of the guard may know justice cases.

## 9.5 Cultural common knowledge

Things most fortress citizens may know vaguely.

Examples:

* goblins are dangerous,
* drink matters,
* miasma is bad,
* unhappy dwarves are trouble,
* nobles make demands,
* caverns are dangerous,
* magma is useful and dangerous.

## 9.6 Learned knowledge

Knowledge acquired through:

* conversations,
* councils,
* direct instruction,
* surviving events,
* working on projects,
* observing consequences,
* reading records or artifacts where applicable.

Learned knowledge should be persisted as memory or knowledge grants.

Version 0.1 does not implement this model beyond the dwarf state supplied in the prompt.

---

## 10. Prompt assembly

An individual conversation prompt should be assembled from separate layers.

## 10.1 Version 0.1 prompt assembly

Version 0.1 uses a minimal prompt:

```text
Base system prompt
  ↓
Dwarf portrayal rules
  ↓
Current dwarf snapshot
  ↓
Small static interpretation guide
  ↓
In-memory chat turns
  ↓
Current player message
```

Version 0.1 does not include:

* `SOUL.md`,
* persistent memory,
* retrieved knowledge,
* perception results,
* available tools,
* commitments,
* council context.

## 10.2 Later prompt assembly

Later versions may use:

```text
Behavioural contract
  ↓
Current canonical state
  ↓
Interpretation guide
  ↓
SOUL.md
  ↓
Knowledge context
  ↓
Retrieved knowledge base items
  ↓
Retrieved memory
  ↓
Conversation summary
  ↓
Recent messages
  ↓
Available tools
  ↓
Current player message
```

## 10.3 Version 0.1 output format

Version 0.1 may use plain text output.

A structured envelope is optional.

Recommended optional envelope:

```json
{
  "speech": "Text shown to the player"
}
```

The application must not interpret plain text as commands.

---

## 11. Database model

Version 0.1 does not require a gameplay database.

It may use only:

* application configuration,
* provider configuration,
* optional developer logs.

Persistent gameplay records begin in version 0.2.

## 11.1 Future Dwarf

```text
Dwarf
  id
  fortressId
  gameUnitId
  historicalFigureId
  displayName
  firstSeenTick
  lastSeenTick
  latestSnapshotId
  currentSoulVersionId
  knowledgeProfileId
  createdAt
  updatedAt
```

## 11.2 Future DwarfSnapshot

```text
DwarfSnapshot
  id
  dwarfId
  gameTick
  schemaVersion
  snapshotJson
  snapshotHash
  createdAt
```

## 11.3 Future SoulVersion

```text
SoulVersion
  id
  dwarfId
  versionNumber
  markdown
  sourceSnapshotId
  sourceHash
  generationReason
  generationProvider
  generationModel
  changeSummaryJson
  createdAt
```

## 11.4 Future KnowledgeItem

```text
KnowledgeItem
  id
  title
  domain
  markdown
  dwarfEnglishMarkdown
  tagsJson
  accessPolicyJson
  sourceRefsJson
  spoilerLevel
  mechanicalPrecision
  createdAt
  updatedAt
```

## 11.5 Future KnowledgeGrant

```text
KnowledgeGrant
  id
  dwarfId
  knowledgeItemId
  accessLevel
  knowledgeType
  sourceType
  sourceConversationId
  sourceMemoryId
  sourceGameEventId
  confidence
  grantedAtTick
  expiresAtTick
  createdAt
```

## 11.6 Future PerceptionRecord

```text
PerceptionRecord
  id
  dwarfId
  toolName
  parametersJson
  resultSummary
  resultJson
  gameTick
  confidence
  createdAt
```

Perception records should usually be short-lived unless promoted to memory.

## 11.7 Future Conversation

```text
Conversation
  id
  fortressId
  type
  title
  startedAtTick
  endedAtTick
  status
  createdAt
  updatedAt
```

Conversation types:

```text
Individual
Council
DwarfToDwarf
SystemEvent
```

## 11.8 Future ConversationParticipant

```text
ConversationParticipant
  conversationId
  participantType
  participantId
  role
  joinedAt
  leftAt
```

Participant types:

```text
Player
Dwarf
System
```

## 11.9 Future Message

```text
Message
  id
  conversationId
  participantId
  role
  body
  structuredPayloadJson
  gameTick
  provider
  model
  promptVersion
  createdAt
```

## 11.10 Future ConversationSummary

```text
ConversationSummary
  id
  conversationId
  dwarfId
  summary
  coveredThroughMessageId
  unresolvedTopicsJson
  createdAt
```

Different dwarves in a council may receive different summaries of the same conversation.

## 11.11 Future Memory

```text
Memory
  id
  dwarfId
  memoryType
  summary
  detailsJson
  importance
  confidence
  status
  sourceConversationId
  sourceMessageId
  sourceGameEventId
  occurredAtTick
  createdAt
  updatedAt
```

Memory types:

```text
Episode
Commitment
Promise
Grievance
Opinion
Request
RelationshipInterpretation
Rumour
CouncilOutcome
LearnedKnowledge
PerceptionMemory
```

## 11.12 Future Commitment

Commitments should have an explicit structure rather than existing only in prose.

```text
Commitment
  id
  fortressId
  debtorParticipantId
  creditorParticipantId
  description
  verificationRuleJson
  createdAtTick
  dueAtTick
  status
  resolvedAtTick
  resolutionEvidenceJson
```

Statuses:

```text
Proposed
Accepted
Fulfilled
Broken
Cancelled
Impossible
Disputed
Unknown
```

---

## 12. Conversation behaviour

A dwarf should eventually be able to:

* answer questions,
* express uncertainty,
* refuse to answer,
* object,
* ask questions,
* inspect nearby surroundings,
* remember approximate relevant information,
* retrieve knowledge it plausibly has,
* make requests,
* form opinions,
* refer to prior conversations,
* recognise fulfilled and broken promises,
* distinguish personal experience from hearsay,
* change its mind when given credible evidence.

Version 0.1 supports only:

* answer questions,
* express uncertainty,
* object in-character,
* ask simple clarifying questions,
* reflect supplied dwarf state,
* maintain continuity inside the active in-memory conversation.

A dwarf should not:

* know hidden game state without justification,
* claim impossible abilities,
* invent relationships,
* execute actions through prose,
* treat every player statement as true,
* agree automatically with the player,
* become a generic assistant detached from its character,
* use expert fortress strategy it has no plausible basis to know,
* turn every answer into a wiki article.

---

## 13. State freshness and concurrency

Each model request should eventually include:

```text
fortressId
saveId
snapshotTick
dwarfId
snapshotHash
```

Version 0.1 should at minimum include:

```text
dwarfId
snapshotTick or extractionTime
snapshotSchemaVersion
```

Version 0.1 may refresh the dwarf state manually.

Automatic state refresh can be added later.

When later versions allow actions or perception, tool results and proposals must carry their source tick and be checked for staleness.

---

## 14. Council system

The council system is out of scope for version 0.1.

Later council conversations may contain:

* an agenda,
* a set of participating dwarves,
* optional player participation,
* shared evidence,
* participant-specific knowledge,
* deliberation rules,
* voting rules.

Later deliberation phases may include:

```text
Briefing
InitialPositions
Questions
InformationGathering
Responses
Amendments
FinalStatements
Vote
Tally
Outcome
```

Each dwarf should be invoked as an independent agent context rather than asking one model invocation to impersonate all councillors simultaneously.

Vote counting must be deterministic application logic.

---

## 15. Model-provider configuration

Configuration should eventually support:

```text
providerType
endpoint
authenticationMode
model
contextWindow
maxOutputTokens
temperaturePolicy
structuredOutputSupport
toolSupport
streamingSupport
```

Version 0.1 requires only:

```text
providerType
endpoint or base URL
authentication setting
model
maxOutputTokens
temperature
```

Secrets must not be stored in dwarf records or conversation data.

Local-model support should work through an OpenAI-compatible endpoint where possible, while preserving an internal provider-neutral interface.

---

## 16. Safety and control

The system must support configurable authority levels.

```text
ReadOnly
ProposalsOnly
PlayerApprovedActions
LimitedAutomaticActions
```

Version 0.1 supports only:

```text
ReadOnly
```

The model must not mutate Dwarf Fortress state.

Model-generated text must never be interpreted as executable code.

Structured actions must be selected from a predefined registry with validated arguments in later versions.

All write operations must be logged once write operations exist.

---

## 17. Observability

Version 0.1 should record or display:

* DFHack connection status,
* dwarf-list loading errors,
* snapshot extraction duration,
* prompt-assembly duration,
* model latency,
* provider and model,
* token counts where available,
* raw prompt in developer mode,
* raw dwarf snapshot in developer mode,
* provider errors.

Later versions should also record:

* perception tool duration,
* knowledge retrieval duration,
* validation failures,
* soul-regeneration events,
* memory-extraction events,
* knowledge-access denials,
* rejected proposals,
* stale-state conflicts.

Prompts, tool calls, tool results, retrieval decisions, and responses should eventually be inspectable through a diagnostics interface, subject to privacy settings.

---

## 18. Versioning

The following artifacts require explicit versions.

For version 0.1:

```text
DwarfSnapshot schema
Prompt template
Provider request format
```

For later versions:

```text
SOUL.md schema
Structured response schema
Action schema
Tool schema
Perception schema
Memory schema
Knowledge item schema
Knowledge access policy schema
Council protocol
```

Stored conversations must record which versions produced them once persistent conversation storage exists.

---

## 19. Version 0.1 implementation checklist

### UI

* Web app starts locally.
* Web app shows DFHack connection status.
* Web app shows a list of dwarves.
* Player can select a dwarf.
* Player can see the current selected dwarf.
* Player can chat with the selected dwarf.
* Player can clear the current chat session.
* Player can manually refresh dwarf state.

### DFHack adapter

* Can list current dwarves.
* Can extract a selected dwarf snapshot.
* Returns schema-versioned JSON.
* Handles missing or invalid dwarf IDs safely.
* Does not expose write commands.

### Prompting

* Has a base system prompt.
* Injects selected dwarf state.
* Includes simple interpretation guidance.
* Includes current in-memory chat turns.
* Does not include persistent memory or tools.

### Provider

* Supports at least one model provider.
* Handles provider errors gracefully.
* Does not leak provider-specific DTOs into the domain model.

### Safety

* No game mutation.
* No arbitrary DFHack command execution.
* No tool calls from the model.
* No persistent gameplay memory.

---

## 20. Version 0.2 implementation checklist

### Persistence

* Store dwarf identity records.
* Store dwarf snapshots selectively.
* Store conversations.
* Store messages.
* Store `SOUL.md` versions.
* Store commitments.

### `SOUL.md`

* Generate first persistent soul.
* Preserve previous soul versions.
* Detect basic contradiction with current state.
* Include soul in later prompts.

### Memory and promises

* Extract promise candidates.
* Confirm before storing if needed.
* Store structured commitments.
* Check whether simple commitments were fulfilled.
* Allow dwarf to reference fulfilled or broken promises later.

---

## 21. Version 0.3 implementation checklist

### Perception

* Implement bounded `look`.
* Return local grid and parsed observations.
* Include z-level offset.
* Enforce radius limits.
* Avoid hidden information leakage.
* Mark perception result with game tick.
* Treat stale perception as possibly outdated.

---

## 22. Version 0.4 implementation checklist

### Knowledge base

* Store atomic Markdown knowledge items.
* Store dwarf-English versions.
* Add structured metadata.
* Implement topic retrieval.
* Implement access filtering.
* Persist learned knowledge where appropriate.
* Prevent expert knowledge leakage through summaries.

---

## 23. Open design questions

### 23.1 Is `SOUL.md` visible to the player?

Options:

* fully visible,
* hidden diagnostic artifact,
* partially visible character sheet,
* unlockable through relationships or offices.

### 23.2 Can the player edit `SOUL.md`?

Direct editing is useful for experimentation but may undermine the idea that identity emerges from the simulation.

Possible policy:

* editable only in developer mode,
* player-authored notes stored separately,
* generated soul remains protected.

### 23.3 Should soul interpretation be deterministic or model-generated?

A hybrid approach is recommended:

* deterministic translation for numerical traits,
* model synthesis for coherent narrative,
* validation against canonical claims.

### 23.4 Can two dwarves remember the same event differently?

They probably should.

Shared events should have:

* one canonical event record,
* separate dwarf-specific memories and interpretations.

### 23.5 Can a dwarf reject its generated soul?

This could eventually become a useful diagnostic or even narrative mechanism. However, for the early versions, the dwarf does not inspect or rewrite `SOUL.md` autonomously.

### 23.6 When does an opinion become mechanically consequential?

An opinion may begin as narrative state.

Later versions may map strong persistent opinions to:

* willingness to cooperate,
* council voting,
* requests,
* refusal,
* trust,
* social conflict.

This mapping should be explicit and deterministic where possible.

### 23.7 How precise should perception be?

There is a tension between usefulness and roleplay.

Precise perception is easier to implement and debug.

Bounded, approximate perception is more believable.

The recommended path is:

1. start with precise data internally,
2. convert it into approximate dwarf-facing summaries,
3. expose raw details only in diagnostics.

### 23.8 How should knowledge items be authored?

Options:

* manually authored,
* generated from imported sources,
* generated and then reviewed,
* automatically imported with quality warnings.

The recommended approach is generated and then reviewed. Unreviewed knowledge should be marked as lower confidence.

### 23.9 Should knowledge be spoiler-safe?

Some players may not want every mechanic exposed, even through dwarves.

Possible policy:

```text
NoSpoilers
LightSpoilers
FullMechanics
DeveloperMode
```

---

## 24. Guiding invariant

The central invariant of Fortress Souls is:

> Canonical state determines what is true. Perception determines what the dwarf can currently notice. Interpretation guides determine what game values mean. Knowledge filtering determines what the dwarf could reasonably know. `SOUL.md` determines how the dwarf understands itself. Memory determines what the dwarf carries forward. The model determines how the dwarf expresses it.

For version 0.1, the simplified invariant is:

> Canonical dwarf state determines what is true. The system prompt determines how the dwarf should be portrayed. The active chat window determines short-term conversational continuity. The model determines how the dwarf expresses it.

Breaking this boundary would turn the system from an extension of Dwarf Fortress into an improvisational chatbot loosely attached to a save file.
