# ADR-0003: DFHack Adapter Invocation Strategy

**Status:** Accepted  
**Date:** 2026-06-18  
**Decision owner:** Fortress Souls project  
**Related research:** `docs/research/dfhack-command-invocation.md`, `docs/research/dfhack-field-map.md`
**Related backlog items:** `B-018`, `B-019`, `B-020`, `B-021`

---

## Context

Fortress Souls v0.1 needs a safe, local, read-only way to obtain dwarf data from Dwarf Fortress through DFHack.

The v0.1 product slice is intentionally narrow:

1. show a list of dwarves,
2. allow the player to select one dwarf,
3. extract a curated dwarf-state snapshot,
4. send that snapshot to an LLM as prompt context,
5. display a chat response.

v0.1 must not mutate the game. It must not expose arbitrary DFHack command execution. It must not let the LLM call DFHack or any game-writing tool.

The main integration options were:

1. use `dfhack-run` with read-only Lua scripts that emit JSON,
2. use DFHack's remote API directly,
3. delay live DFHack integration and use fake / JSON-file adapters first.

Manual R-001 verification showed that `dfhack-run` can invoke installed Lua scripts by command name, that scripts can emit clean JSON to stdout, that command arguments are passed correctly, and that failures can be classified by exit code and captured output.

Manual verification also showed one important edge case: when DF/DFHack is closed, `dfhack-run` may crash with a negative Windows exit code and empty stdout/stderr. A TCP preflight against DFHack's RPC port cleanly distinguishes DFHack-off from DFHack-running states.

---

## Decision

For v0.1, Fortress Souls will use this adapter sequence:

1. `FakeDwarfFortressAdapter`
2. `JsonFileDwarfFortressAdapter`
3. `DfHackDwarfFortressAdapter` using `dfhack-run` plus allowlisted read-only Lua scripts

The live DFHack adapter will not use the direct DFHack remote API in v0.1.

The backend will invoke only exact allowlisted DFHack command names:

```text
fortress-souls/diagnose
fortress-souls/list-dwarves
fortress-souls/get-dwarf-snapshot
```

The backend must not expose a generic endpoint or method equivalent to:

```http
POST /api/dfhack/execute
```

The backend must not pass arbitrary filesystem script paths to `dfhack-run`.

The backend must not allow the LLM to call DFHack.

Scripts in the v0.1 allowlist must be read-only and must emit JSON-safe primitives only. They must not emit raw DFHack userdata or other values that depend on Lua object identity or memory layout.

---

## Script deployment decision

The monorepo is the source of truth for DFHack Lua scripts:

```text
dfhack/scripts/fortress-souls/
  diagnose.lua
  list-dwarves.lua
  get-dwarf-snapshot.lua
```

For local development, scripts are installed into DFHack's script path by an explicit install/sync script:

```text
scripts/install-dfhack-scripts.ps1
scripts/install-dfhack-scripts.sh
```

The Windows install target is expected to be:

```text
<DFHack root>/hack/scripts/fortress-souls/
```

Example local target from R-001 verification:

```text
C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack\scripts\fortress-souls\
```

The backend must not automatically copy scripts into the DFHack installation on startup. Script installation is an explicit developer action.

---

## Runtime invocation decision

The process runner will invoke `dfhack-run` by configured full path.

Recommended configuration:

```json
{
  "DfHack": {
    "Enabled": false,
    "RunPath": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\DFHack\\hack\\dfhack-run.exe",
    "WorkingDirectory": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\DFHack\\hack",
    "Host": "127.0.0.1",
    "Port": 5000,
    "TimeoutMs": 3000
  }
}
```

Even though R-001 verification showed that invoking `dfhack-run.exe` by full path works from outside the DFHack folder, the backend should still set `WorkingDirectory` explicitly to the DFHack `hack` directory.

---

## TCP preflight decision

Before invoking `dfhack-run`, the backend will perform a short TCP preflight against the configured DFHack RPC host/port.

Default:

```text
127.0.0.1:5000
```

If TCP preflight fails, the adapter returns `DfHackUnavailable` without launching `dfhack-run`.

This avoids a verified local failure mode where `dfhack-run` crashes when DF/DFHack is closed:

```text
EXIT=-1073741819
STDOUT:
STDERR:
```

---

## Process result classification

The adapter will classify process results as follows:

```text
if command is not allowlisted:
    reject before process start

if TCP preflight fails:
    return DfHackUnavailable

if process cannot start:
    return DfHackExecutableUnavailable

if process times out:
    kill process and return DfHackInvocationTimedOut

if exitCode < 0:
    return DfHackProcessCrashed

if exitCode != 0:
    return DfHackInvocationFailed

if exitCode == 0:
    parse stdout as JSON

if stdout JSON parse fails:
    return DfHackInvalidJson

else:
    return typed result
```

The adapter must capture:

```text
commandName
arguments
exitCode
stdout
stderr
durationMs
timeout
```

Logs and diagnostics must avoid exposing excessive raw game state. For v0.1, stdout may be retained in developer diagnostics for DFHack adapter debugging, but normal telemetry must not record large snapshots by default.

---

## Observed manual verification facts

R-001 verified the following:

```text
[OK] Absolute Lua file paths are not accepted as direct script invocation.
[OK] Installed scripts under hack/scripts/fortress-souls can be invoked by command name.
[OK] Lua scripts can emit clean stdout JSON.
[OK] PowerShell ConvertFrom-Json can parse script stdout.
[OK] Lua scripts receive command arguments.
[OK] Quoted arguments with spaces survive as one argument.
[OK] Missing scripts return exit code 1.
[OK] Missing-script errors are written to stdout, not stderr.
[OK] Valid scripts can report no fortress loaded as structured JSON.
[OK] tickCount is not a reliable loaded-fortress signal.
[OK] TCP 127.0.0.1:5000 is false when DFHack is off.
[OK] TCP 127.0.0.1:5000 is true when DFHack is started.
[OK] dfhack-run can be invoked by full path from outside the DFHack folder.
```

---

## B-019 validation update

B-019 validated the first `list-dwarves` and `get-dwarf-snapshot` script pair against a live fortress. The validation run produced:

```text
ListCount            : 7
ValidSnapshotCount   : 7
ErrorSnapshotCount   : 0
InvalidSnapshotCount : 0
```

This is sufficient evidence for v0.1 to proceed with the JSON-file adapter and live process adapter implementation behind the strict command allowlist described above.

The B-019 field-map research also tightened the script/data safety boundary:

- the script layer remains read-only,
- output is a curated DTO rather than raw DF memory,
- raw full snapshots are for debugging and future mapping,
- `promptCandidates` is the preferred model-facing seam,
- stdout remains untrusted until JSON parsing succeeds.

---

## Consequences

### Positive consequences

- The v0.1 integration surface remains small.
- The backend can treat DFHack as a process adapter that returns JSON.
- The app can be developed and tested without live DFHack by using fake and JSON-file adapters first.
- Lua scripts remain reviewable source files in the monorepo.
- Exact command allowlisting supports read-only-by-construction design.
- TCP preflight avoids a verified crash path when DFHack is unavailable.

### Negative consequences

- Script installation/sync is an extra developer step.
- `dfhack-run` is less elegant than a typed RPC client.
- stdout/stderr behaviour is CLI-shaped rather than API-shaped.
- Error classification must account for DFHack quirks, including errors on stdout and negative crash exit codes.
- Live adapter tests will be partly manual in v0.1.

### Neutral / deferred consequences

- Direct DFHack remote API may be reconsidered after v0.1 if live integration becomes performance-sensitive or if richer read models are needed.
- A symlink/junction-based script deployment workflow may be added later for faster script iteration, but copy-based install remains the default.

---

## Rejected alternatives

### Direct DFHack remote API in v0.1

Rejected for v0.1.

Reason:

The direct remote API is more powerful but requires protobuf/RPC client complexity that is not justified for the first product slice. `dfhack-run` already uses the remote interface internally and provides a simpler operational boundary for command execution.

### Passing absolute Lua file paths to `dfhack-run`

Rejected.

Reason:

Manual verification showed that `dfhack-run` interprets an absolute Lua path as a DFHack command name and returns “not a recognized command.” Scripts must be installed in a DFHack script path and invoked by command name.

### Backend-managed script installation on startup

Rejected for v0.1.

Reason:

Startup should not mutate the DFHack installation. Script installation should be an explicit developer action through install/sync scripts.

### Generic DFHack execute endpoint

Rejected permanently for v0.1.

Reason:

A generic execute endpoint violates the read-only safety boundary and creates an obvious route to game mutation. v0.1 must use exact allowlisted command names only.

---

## Implementation notes for B-021

`DfHackDwarfFortressAdapter` should depend on a small `DfHackProcessRunner` abstraction.

Suggested interface shape:

```csharp
public interface IDfHackProcessRunner
{
    Task<DfHackCommandResult> RunJsonCommandAsync(
        DfHackCommand command,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}
```

`DfHackCommand` should not be a raw string supplied by API callers. It should be an enum or value object with a closed set of allowed commands.

Example allowed commands:

```csharp
public enum DfHackCommand
{
    Diagnose,
    ListDwarves,
    GetDwarfSnapshot
}
```

The command-name mapping should be internal:

```text
Diagnose          -> fortress-souls/diagnose
ListDwarves       -> fortress-souls/list-dwarves
GetDwarfSnapshot  -> fortress-souls/get-dwarf-snapshot
```

This prevents accidental stringly-typed command execution from leaking into the application layer.

---

## Review checklist

Before accepting the live DFHack adapter implementation, verify:

- [ ] No generic DFHack command execution endpoint exists.
- [ ] The process runner uses an allowlist.
- [ ] TCP preflight is implemented and configurable.
- [ ] Timeout is implemented.
- [ ] stdout and stderr are both captured.
- [ ] negative exit codes are classified as process crash.
- [ ] non-zero exit codes are handled before JSON parsing.
- [ ] JSON parse failure is handled explicitly.
- [ ] missing scripts produce a clear user-facing diagnostic.
- [ ] no game mutation scripts exist in the v0.1 script folder.
- [ ] no LLM provider code can call DFHack.

---

## References

- DFHack Core documentation, script paths and `dfhack-run`: https://docs.dfhack.org/en/53.14-r2/docs/Core.html
- DFHack remote interface documentation: https://docs.dfhack.org/en/stable/docs/dev/Remote.html
- DFHack Lua API documentation: https://docs.dfhack.org/en/stable/docs/dev/Lua%20API.html
