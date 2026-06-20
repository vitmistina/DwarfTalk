# Fortress Souls Agent Guide

## Purpose

Fortress Souls v0.1 is a local, read-only companion for Dwarf Fortress. It
lists eligible dwarves, lets the player select one in the web UI, extracts a
validated snapshot for that dwarf, assembles a deterministic prompt, and
supports an in-memory chat with that dwarf through one configured LLM provider.

The repository is a modular monolith in a monorepo. Optimize for a dependable
vertical slice, clear module boundaries, fast local feedback, and code that a
human can review. Do not optimize for hypothetical scale or future features.

## Instruction Hierarchy

Use the most specific applicable instruction. In descending precedence:

1. The user's current request and the target backlog item's acceptance criteria.
2. The nearest `AGENTS.md` governing the files being changed.
3. Accepted ADRs under `docs/decisions/`.
4. Current contracts and specifications under `docs/specs/`.
5. Module instructions under `.agents/instructions/` and applicable skills
   under `.agents/skills/`.
6. Runbooks and research notes.
7. This root guide for everything not overridden above.

Do not use an older plan, sample, research result, or project-memory entry to
override a newer accepted decision. If authoritative sources conflict in a way
that changes behavior, safety, architecture, or acceptance criteria, stop and
surface the conflict instead of choosing silently.

## v0.1 Boundaries

In scope:

- Read-only listing of eligible dwarves and snapshot extraction by validated
  dwarf ID after browser-side selection.
- Versioned, validated dwarf snapshot contracts.
- Deterministic prompt assembly from snapshot, portrayal rules, static guidance,
  and the active in-memory conversation.
- Fake adapters and fixtures for offline development and deterministic tests.
- One real, configured LLM provider behind an application-owned interface.
- A local web UI that owns dwarf selection and clearly identifies the selected
  dwarf throughout the in-memory chat.
- Structured logs, traces, metrics, diagnostics, and operational runbooks.

Out of scope unless a later accepted backlog item explicitly adds it:

- Any mutation of Dwarf Fortress state or saves.
- Model tool calling or direct model access to DFHack, shell, filesystem, or
  write-capable APIs.
- Autonomous multi-step agent runtime behavior.
- Persistent chat, gameplay memory, promises, commitments, or knowledge bases.
- Streaming, provider marketplaces, councils, voting, or background simulation.
- Microservices or independently deployed internal modules.

## Non-Negotiable Safety

- Make game mutation impossible by construction, not merely discouraged.
- Never add a generic DFHack command endpoint, arbitrary process runner, shell
  escape hatch, or write-capable debug route.
- Preserve saves, DFHack research, scripts, samples, spike evidence, and env
  templates unless the task explicitly requires changing them.
- Never commit or print secrets. Keep credentials in environment variables or
  approved local secret storage. Commit only redacted examples.
- Never log full prompts, model responses, conversation text, environment
  values, authorization headers, or dwarf data that is not explicitly approved
  for telemetry. Log identifiers, versions, sizes, timing, and error categories.
- Treat DFHack output, JSON files, browser input, provider responses, paths, and
  configuration as untrusted data. Validate at every trust boundary.
- Invoke processes with a fixed executable and structured argument list. Do not
  concatenate untrusted values into shell commands.
- Use timeouts and cancellation for DFHack and provider calls. Bound input,
  output, history, and payload sizes.
- Do not weaken tests, analyzers, validation, redaction, or security controls to
  make a change pass.
- Do not perform destructive Git or filesystem operations unless the user has
  explicitly requested them.

If a requested implementation would violate these rules, do not improvise a
workaround. Explain the conflict and propose the smallest safe alternative.

## Context Loading

Always load:

- The exact backlog item in `docs/backlog/v0.1-backlog.md`.
- `docs/architecture/0001-architecture-overview.md` for system design.
- `docs/specs/fortress-souls-v0.1.spec.md` for product boundaries.
- Relevant accepted ADRs in `docs/decisions/`.
- The nearest tests and implementation files for the behavior being changed.

Load only when relevant:

| Work area         | Additional context                                                                                                                                |
| ----------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| Backend or API    | `.agents/instructions/backend.instructions.md`                                                                                                    |
| Frontend          | `.agents/instructions/frontend.instructions.md`                                                                                                   |
| Testing           | `.agents/instructions/testing.instructions.md`                                                                                                    |
| DFHack            | `.agents/instructions/dfhack.instructions.md`, `docs/research/`, `docs/runbooks/dfhack-setup.md`, `.agents/skills/dfhack-adapter-safety/SKILL.md` |
| Prompting         | `.agents/instructions/prompting.instructions.md`, prompt contract, `.agents/skills/prompt-contracts/SKILL.md`                                     |
| Observability     | `.agents/instructions/observability.instructions.md`, observability contract, `.agents/skills/observability-first/SKILL.md`                       |
| Module boundaries | Architecture docs and `.agents/skills/modular-monolith-boundaries/SKILL.md`                                                                       |
| Repeated workflow | The matching file in `.agents/prompts/`                                                                                                           |

Do not load every agent primitive by default. Progressive disclosure is part of
the design: broad permanent rules here, specific rules close to their work.

## Work Protocol

### Before Editing

1. Read the target backlog item and restate its observable outcome.
2. Inspect `git status` and preserve pre-existing user changes.
3. Read the relevant implementation, tests, contracts, and local instructions.
4. Identify affected module boundaries and trust boundaries.
5. Find the repository's existing pattern before introducing a new one.
6. Choose the smallest test that can demonstrate the requested behavior.
7. For substantial work, keep a short plan with one active step at a time.

Do not start from a presumed solution when the repository can answer the
question. Search first, preferably with `rg` and `rg --files`.

### While Editing

- Keep the diff within the backlog item and one primary module plus necessary
  integration edges.
- Preserve existing public contracts unless the task explicitly changes them.
- Prefer a thin vertical slice over disconnected layers of scaffolding.
- Reuse repository patterns and standard library capabilities before adding an
  abstraction, framework, package, service, or project.
- Separate behavior changes from broad refactors. Refactor only what is needed
  to make the requested change clear and safe.
- Remove dead code, obsolete imports, and temporary diagnostics introduced by
  the change. Do not conduct unrelated housekeeping.
- Keep comments for intent, invariants, hazards, and non-obvious trade-offs.
  Do not narrate obvious code.
- Do not leave silent fallbacks. Make degraded modes explicit and observable.
- Do not edit generated files by hand; change their source and regenerate them.
- Do not invent command output, test results, live DFHack behavior, provider
  behavior, or successful verification.

### When To Stop

Stop and request a human decision when:

- A change would alter accepted architecture, v0.1 scope, or the read-only
  safety boundary.
- A new production dependency, external service, persistence mechanism, or
  deployment unit is materially necessary but not already approved.
- DFHack behavior cannot be verified and proceeding would risk game mutation.
- Required secrets or external access are unavailable and no fake path can
  validate the behavior.
- The task requires a broad refactor spanning unrelated backlog items.
- Authoritative documentation conflicts and the difference is consequential.

First investigate unrelated test failures and unexpected worktree changes.
Preserve them, work around them when safe, and report them. Ask only when they
make the requested work unsafe or impossible.

## Architecture Contract

### Modular Monolith

- Keep one backend deployment unit with explicit internal modules.
- Each module owns its domain concepts and exposes a small intentional contract.
- Do not reference another module's internals or create cyclic dependencies.
- Keep the domain independent of ASP.NET Core, DFHack, provider SDKs, telemetry
  SDKs, filesystem details, and process execution.
- Keep orchestration in the application layer and adapters at the edges.
- Keep API endpoints thin: transport mapping, validation, application call, and
  response mapping. Business rules do not belong in controllers or endpoints.
- Keep provider-specific DTOs and exceptions inside the LLM adapter.
- Keep DFHack-specific DTOs and process details inside the Dwarf Fortress
  adapter. Map them to versioned application-owned contracts.
- Do not turn Aspire or Docker Compose resources into service boundaries.

### Deterministic and Probabilistic Boundary

Application code owns:

- Snapshot extraction, normalization, validation, and versioning.
- Prompt structure, ordering, escaping, truncation, and token-budget policy.
- Configuration, redaction, history limits, retries, timeouts, and error mapping.
- Session state and all user-visible metadata.

The model owns only prose generation. Do not ask the model to repair malformed
snapshots, infer missing required fields, select tools, execute actions, or make
authorization and safety decisions.

### Dependency Direction

Enforce these rules with architecture tests when the projects exist:

- Domain depends on no infrastructure or delivery module.
- Application may depend on Domain and application-owned ports.
- Adapters may depend inward on application contracts.
- API composes modules but does not become a shared domain layer.
- Frontend depends on the published API contract, not backend implementation
  types.

Any deliberate boundary change requires a new or superseding ADR and matching
architecture-test change in the same task.

### Frontend

- Organize user behavior by feature; keep shared components genuinely generic.
- Model server state through the repository's query layer rather than duplicate
  ad hoc caches.
- Cover loading, empty, success, degraded, and error states.
- Build accessible controls with semantic HTML, labels, keyboard operation, and
  visible focus.
- Do not expose raw provider errors, prompts, secrets, stack traces, or internal
  paths in the browser.

## Test-Driven Change

Use red-green-refactor for observable behavior when practical:

1. **Red:** Add the smallest test that fails for the intended reason. Run it and
   confirm the failure is meaningful.
2. **Green:** Implement the smallest coherent change that makes the test pass.
3. **Refactor:** Improve names and structure while keeping the relevant suite
   green.

Mandatory applications:

- For a bug fix, reproduce the bug with a failing regression test before fixing
  it whenever the failure is automatable.
- For domain rules, parsers, prompt assembly, validation, API contracts, and
  error mapping, write the behavior test first.
- For existing untested behavior that must be preserved during refactoring, add
  characterization tests first.

Reasonable exceptions include pure documentation, generated scaffolding,
format-only changes, exploratory research spikes, and visual styling where a
stable automated assertion would be misleading. In an exception, still define
the evidence of correctness and state why test-first was not useful.

Do not turn TDD into ceremony. Tests should protect behavior and design
boundaries, not mirror private methods or freeze incidental implementation.

## Testing Strategy

Choose the lowest test level that proves the risk:

| Test type          | Use for                                                                                                         | Must avoid                                              |
| ------------------ | --------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------- |
| Unit               | Domain rules, normalization, prompt assembly, redaction, pure mapping                                           | Filesystem, network, clock, random state, provider SDKs |
| Contract/component | Adapter mappings, schemas, provider request/response translation, frontend feature behavior                     | Live third-party services                               |
| Integration        | ASP.NET pipeline, dependency injection, serialization, module wiring, process adapter against a controlled fake | Tests that merely repeat unit coverage                  |
| Architecture       | Project references, forbidden dependencies, module cycles, safety invariants                                    | Subjective style preferences                            |
| End-to-end         | A few critical user journeys in fake mode                                                                       | Exhaustive permutations or live provider assertions     |
| Manual smoke       | Live DFHack integration and explicitly documented provider checks                                               | Undocumented acceptance evidence                        |

Test quality rules:

- Keep tests deterministic, isolated, order-independent, and parallel-safe.
- Control time, randomness, filesystem locations, process output, and network
  responses. Do not use arbitrary sleeps.
- Required CI checks must not call a live LLM provider, the public internet, or a
  developer's Dwarf Fortress instance.
- Assert externally visible behavior and stable contracts. Avoid assertions on
  private implementation details.
- Prefer small real collaborators over excessive mocks. Mock only a true
  boundary or interaction whose call is itself the behavior.
- Give tests behavior-revealing names and keep arrange, act, and assert obvious.
- A test must fail when the protected behavior is deliberately broken. Avoid
  tautological mocks and assertions.
- Treat flaky tests as defects. Find and remove nondeterminism; do not hide it
  behind retries or broad timeouts.
- Keep one fake-mode browser smoke test for the critical chat journey. Use
  user-facing locators and assertions.
- LLM tests verify deterministic prompt content, request mapping, budgets,
  redaction, and fake-provider behavior. Never assert exact prose from a live
  model.
- Golden files are review surfaces, not an auto-approval mechanism. Normalize
  unstable values, inspect diffs, and update a golden only when the contract
  intentionally changes.
- Every defect fixed in parsing, contracts, safety, or boundary handling gets a
  regression test when technically feasible.

Coverage is a diagnostic, not a target to game. Prioritize critical branches,
failure paths, trust boundaries, and architecture invariants over line count.

## Code Quality Gates

For every changed area, run the narrowest fast checks during development and
the broadest relevant checks before completion:

1. Formatter or format check.
2. Compiler and type checker.
3. Linter and static analyzers.
4. Focused tests for the changed behavior.
5. Relevant module or integration suite.
6. Full repository check for shared contracts, cross-module changes, build
   configuration, or release work.
7. Manual or browser verification when automated checks cannot establish the
   user-visible result.

Treat warnings as work, not decoration. Do not introduce new warnings. Do not
add blanket suppressions, disable analyzer rules, lower strictness, skip tests,
or regenerate snapshots solely to obtain a green check. A narrow suppression
requires a code-local reason.

### Canonical Commands

Prefer repository scripts because local and CI verification should use the same
entry points. Use the PowerShell counterpart on Windows.

| Intent                        | Preferred entry point |
| ----------------------------- | --------------------- |
| Start local development       | `./scripts/dev.sh`    |
| Format                        | `./scripts/format.sh` |
| Run tests                     | `./scripts/test.sh`   |
| Run all merge-blocking checks | `./scripts/check.sh`  |

If a listed script does not exist yet, use the commands documented in the
nearest README or runbook. Do not create unrelated tooling merely to satisfy
this table. When the repository establishes a new canonical command, update
this table and CI in the same change.

When no wrapper exists, typical direct checks are:

```bash
dotnet format --verify-no-changes
dotnet build --no-restore
dotnet test --no-build
```

For the frontend, use the package manager selected by the committed lockfile
and the scripts declared in `src/frontend/package.json`, normally `lint`,
`test`, `build`, and the narrowest supported test command.

Do not claim a command passed unless it was run in the current worktree. Record
the exact command and result. If a check cannot run, explain the concrete reason
and what evidence was obtained instead.

## Dependencies and Supply Chain

- Add a dependency only when it removes more risk or complexity than it adds.
- Before adding one, check whether the standard library or an existing package
  already solves the problem.
- Use the repository's selected package manager and committed lockfiles.
- Pin and restore dependencies through normal manifests; never download and
  execute unverified scripts as part of a routine task.
- Do not combine feature work with unrelated dependency upgrades.
- Review transitive impact, license compatibility, maintenance status, and known
  vulnerabilities for a new production dependency.
- Run the repository's dependency and vulnerability checks when manifests or
  lockfiles change. Explain any accepted advisory; do not silently suppress it.

## Observability and Failure Design

- Instrument meaningful boundaries: chat turn, dwarf snapshot, prompt assembly,
  and provider call. Avoid spans for trivial internal methods.
- Propagate correlation and cancellation through the complete request path.
- Use structured event names and stable low-cardinality fields defined by the
  observability contract.
- Keep metrics dimensions bounded. Never use prompt text, response text, error
  messages, paths, or dwarf names as metric labels.
- Map expected failures to stable application error codes and useful,
  non-sensitive user messages.
- Preserve diagnostic causes internally without leaking secrets or provider
  internals across module or API boundaries.
- A fallback, retry, timeout, or partial result must be deliberate, bounded,
  observable, and tested.

## Documentation and Decisions

- Keep code, tests, contracts, examples, and runbooks consistent in one change.
- Update an ADR when architecture, stack, module ownership, integration style,
  or a significant cross-cutting policy changes. Prefer a superseding ADR over
  rewriting decision history.
- Update the relevant spec when externally visible behavior or a versioned
  contract changes.
- Update runbooks when setup, configuration, commands, diagnostics, or recovery
  procedures change.
- Record research as evidence with date, environment, versions, commands, raw
  observations, conclusion, and remaining uncertainty. Do not present an
  unverified hypothesis as an accepted decision.
- Keep sample and fixture provenance clear. Synthetic data must not masquerade
  as live DFHack output.
- Add a TODO only when it names the missing work, why it is deferred, and its
  issue or backlog item. Do not leave anonymous promises in code.

## Change and Git Discipline

- Keep commits and diffs small, coherent, and reviewable.
- Do not modify unrelated files or normalize an entire file for a local change.
- Preserve user changes. Never reset, revert, overwrite, or discard work you did
  not create.
- Do not commit, push, rebase, amend, or force-update history unless explicitly
  asked.
- Do not upgrade dependencies, reformat the repository, rename public concepts,
  or reorganize modules as incidental chores.
- Review the final diff for accidental generated files, secrets, debug output,
  broad formatting churn, and contract drift.

## Definition of Done for a Backlog Item

A change is complete only when:

- Acceptance criteria are demonstrably satisfied.
- The red-green-refactor loop was followed where applicable, or the exception is
  explained.
- Relevant unit, contract, integration, architecture, and UI tests exist at the
  risk-appropriate levels.
- Formatting, compilation, types, linting, analyzers, and relevant tests pass.
- Safety, redaction, failure paths, cancellation, and observability were
  considered for changed boundaries.
- Architecture and dependency rules still hold.
- Documentation, ADRs, schemas, samples, and runbooks affected by the change are
  updated.
- The final diff contains no unrelated cleanup, secrets, temporary diagnostics,
  or unexplained generated changes.

End every implementation task with:

1. Outcome and important design decisions.
2. Changed files.
3. Validation commands and results.
4. Known limitations, unverified assumptions, and manual checks still needed.
5. Marking the backlog item as DONE to make sure handover works correctly

Never substitute "looks correct" for evidence.

## Maintaining This Guide

Keep this file practical and stable. Put detailed, fast-changing, or
module-specific guidance near the relevant subtree. When an agent repeatedly
makes the same consequential mistake, fix the underlying automation or test
first when possible, then add the smallest durable rule here or in the nearest
scoped instruction file.

Review this guide when commands, repository layout, accepted architecture,
safety boundaries, or the definition of done changes. Delete obsolete rules;
an inaccurate instruction is worse than a missing one.
