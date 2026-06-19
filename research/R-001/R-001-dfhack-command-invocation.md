# R-001: DFHack Command Invocation

**Status:** Completed research spike  
**Date:** 2026-06-18  
**Project:** Fortress Souls v0.1  
**Related ADR:** `docs/decisions/adr-0003-dfhack-adapter.md`  
**Related backlog item:** `B-018 Research DFHack command invocation`

---

## 1. Research question

What is the safest and simplest v0.1 path for the backend to obtain dwarf data from DFHack?

The specific options considered were:

1. `dfhack-run` plus read-only Lua scripts that emit JSON.
2. Direct DFHack remote API / protobuf client integration.
3. Delaying live DFHack integration and using fake / JSON-file adapters first.

The project constraint is that v0.1 must remain read-only by construction. The backend must not expose arbitrary DFHack command execution, and the LLM must never receive a tool surface that can call DFHack or mutate the game.

---

## 2. Recommendation

Use this implementation sequence:

1. Implement `FakeDwarfFortressAdapter` first.
2. Implement `JsonFileDwarfFortressAdapter` second.
3. Implement live `DfHackDwarfFortressAdapter` third, using:
   - `dfhack-run.exe`,
   - allowlisted DFHack command names,
   - read-only Lua scripts,
   - stdout JSON parsing,
   - stderr capture,
   - process timeout handling,
   - TCP preflight against DFHack RPC host/port before invocation.

Do not implement direct DFHack remote API integration in v0.1.

---

## 3. External documentation findings

### 3.1 DFHack script discovery

DFHack searches configured script paths when a command is run. The documented default script search paths include:

1. `dfhack-config/scripts`
2. `save/world/scripts` when a save is loaded
3. `hack/scripts`
4. installed mod script locations

A script stored under a script path can be invoked as a DFHack command by its relative path without the `.lua` extension.

Example:

```text
hack/scripts/fortress-souls/diagnose.lua
```

is invoked as:

```powershell
.\dfhack-run.exe fortress-souls/diagnose
```

### 3.2 `dfhack-run`

`dfhack-run` is the documented external command runner for invoking DFHack commands from the operating system terminal while DFHack is running.

The remote API documentation states that DFHack starts a TCP server for remote connections and that `dfhack-run` uses the RPC interface to invoke DFHack commands or Lua functions externally. This means `dfhack-run` gives v0.1 a simpler process boundary while avoiding direct protobuf/RPC client implementation.

### 3.3 Lua JSON output

DFHack Lua can use a JSON module via:

```lua
local json = require('json')
print(json.encode(result, { pretty = false }))
```

Manual verification confirmed that this output can be consumed directly by PowerShell `ConvertFrom-Json`.

---

## 4. Manual verification environment

The following local environment was used for verification:

```text
DFHack path:
C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack

Repo / research path:
C:\coding\DwarfTalk\research

DFHack command runner:
C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack\dfhack-run.exe

Script install target used during spike:
C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack\scripts\fortress-souls
```

---

## 5. Verification scripts

### 5.1 `diagnose.lua`

Installed as:

```text
hack/scripts/fortress-souls/diagnose.lua
```

Content:

```lua
local json = require('json')

local result = {
    schemaVersion = "fortress-souls-diagnose.v0.1",
    worldLoaded = dfhack.isWorldLoaded(),
    mapLoaded = dfhack.isMapLoaded(),
    siteLoaded = dfhack.isSiteLoaded(),
    tickCount = dfhack.getTickCount()
}

print(json.encode(result, { pretty = false }))
```

### 5.2 `echo-args.lua`

Installed as:

```text
hack/scripts/fortress-souls/echo-args.lua
```

Content:

```lua
local json = require('json')
local args = {...}

print(json.encode({
    schemaVersion = "fortress-souls-echo-args.v0.1",
    argCount = #args,
    args = args
}, { pretty = false }))
```

---

## 6. Manual verification results

### 6.1 Absolute Lua file path does not work

Command:

```powershell
.\dfhack-run.exe C:\coding\DwarfTalk\research\diagnose.lua
```

Observed output:

```text
Replacing backslashes with forward slashes in "C:\coding\DwarfTalk\research\diagnose.lua"
C:/coding/DwarfTalk/research/diagnose.lua is not a recognized command.
```

Conclusion:

`dfhack-run` interprets the argument as a DFHack command name, not as an arbitrary Lua file path. The backend must not attempt to pass absolute script paths to `dfhack-run`.

---

### 6.2 Installed script invocation works

After copying the script to:

```text
hack/scripts/fortress-souls/diagnose.lua
```

Command:

```powershell
.\dfhack-run.exe fortress-souls/diagnose
```

Observed output:

```json
{"mapLoaded":true,"schemaVersion":"fortress-souls-diagnose.v0.1","siteLoaded":true,"tickCount":343265296,"worldLoaded":true}
```

Conclusion:

Nested script command invocation works. stdout can contain clean JSON.

---

### 6.3 PowerShell JSON parsing works

Command:

```powershell
.\dfhack-run.exe fortress-souls/diagnose | ConvertFrom-Json
```

Result:

PowerShell parsed stdout successfully.

Conclusion:

The process adapter can treat stdout as machine-readable JSON when exit code is zero.

---

### 6.4 Argument passing works

Command:

```powershell
.\dfhack-run.exe fortress-souls/echo-args unit-123 "hello world" 42 | ConvertFrom-Json
```

Observed parsed output:

```text
argCount args                        schemaVersion
-------- ----                        -------------
       3 {unit-123, hello world, 42} fortress-souls-echo-args.v0.1
```

Conclusion:

Lua scripts receive arguments. Quoted whitespace survives as one argument. This is sufficient for commands such as:

```powershell
.\dfhack-run.exe fortress-souls/get-dwarf-snapshot 12345
```

---

### 6.5 Missing script failure behaviour

Command:

```powershell
.\dfhack-run.exe fortress-souls/no-such-script
$LASTEXITCODE
```

Observed output:

```text
fortress-souls/no-such-script is not a recognized command.
1
```

Explicit stdout/stderr capture:

```text
EXIT=1
STDOUT:
fortress-souls/no-such-script is not a recognized command.
STDERR:
```

Conclusion:

Missing commands return exit code `1`. Error text is written to stdout, not stderr. The process runner must capture both stdout and stderr and must not assume errors only appear on stderr.

---

### 6.6 No fortress loaded behaviour

With DFHack running but no fortress loaded, command:

```powershell
.\dfhack-run.exe fortress-souls/diagnose | ConvertFrom-Json
```

Observed parsed output:

```text
mapLoaded     : False
schemaVersion : fortress-souls-diagnose.v0.1
siteLoaded    : False
tickCount     : 343762734
worldLoaded   : False
```

Conclusion:

A valid script can report no active fortress as structured JSON. The backend should classify this as `NoFortressLoaded`, not as a DFHack failure.

Important: `tickCount` is not a reliable loaded-fortress signal. Use `worldLoaded`, `mapLoaded`, and `siteLoaded` for state classification.

---

### 6.7 DFHack closed behaviour

With DF/DFHack fully closed, command:

```powershell
.\dfhack-run.exe fortress-souls/diagnose
```

Observed captured output:

```text
EXIT=-1073741819
STDOUT:
STDERR:
```

Conclusion:

In this local setup, invoking `dfhack-run` while DFHack is unavailable can crash with a negative Windows exit code and no stdout/stderr. The backend must not assume unavailable DFHack is reported gracefully.

---

### 6.8 TCP preflight works

With DFHack off:

```powershell
Test-NetConnection 127.0.0.1 -Port 5000
```

Observed:

```text
TcpTestSucceeded : False
```

With DFHack started:

```powershell
Test-NetConnection 127.0.0.1 -Port 5000
```

Observed:

```text
TcpTestSucceeded : True
```

Conclusion:

Before invoking `dfhack-run`, the backend should perform a short TCP preflight against the configured DFHack RPC host/port, defaulting to `127.0.0.1:5000`.

---

### 6.9 Running from repo directory works

`dfhack-run.exe` was also invoked by full path from outside the DFHack folder, from the repository directory.

Conclusion:

The backend can invoke `dfhack-run.exe` by full path. Nevertheless, it should set `WorkingDirectory` explicitly to the DFHack `hack` directory to reduce path-dependent surprises.

---

## 7. Recommended script deployment model

The monorepo remains the source of truth:

```text
fortress-souls/
  dfhack/
    scripts/
      fortress-souls/
        diagnose.lua
        echo-args.lua
        list-dwarves.lua
        get-dwarf-snapshot.lua
```

DFHack runtime script directory is an install target:

```text
<DFHack root>/hack/scripts/fortress-souls/
```

Use explicit install/sync scripts:

```text
scripts/install-dfhack-scripts.ps1
scripts/install-dfhack-scripts.sh
```

Do not have the backend automatically mutate the DFHack installation on startup.

Suggested PowerShell install script shape:

```powershell
param(
    [Parameter(Mandatory = $true)]
    [string] $DfHackRoot
)

$Source = Join-Path $PSScriptRoot "..\dfhack\scripts\fortress-souls"
$Target = Join-Path $DfHackRoot "hack\scripts\fortress-souls"

if (-not (Test-Path $Source)) {
    throw "Source script folder not found: $Source"
}

New-Item -ItemType Directory -Force -Path $Target | Out-Null
Copy-Item -Path (Join-Path $Source "*.lua") -Destination $Target -Force

Write-Host "Installed Fortress Souls DFHack scripts:"
Get-ChildItem $Target -Filter "*.lua" | ForEach-Object {
    Write-Host " - $($_.FullName)"
}
```

---

## 8. Backend process runner requirements

`DfHackProcessRunner` should implement this control flow:

```text
1. Validate requested command against allowlist.
2. Perform TCP preflight to configured DFHack host/port.
3. If TCP preflight fails:
     return DfHackUnavailable.
4. Start dfhack-run with exact command and arguments.
5. Capture stdout and stderr.
6. Apply process timeout.
7. If process cannot start:
     return DfHackExecutableUnavailable.
8. If process times out:
     kill process and return DfHackInvocationTimedOut.
9. If exitCode < 0:
     return DfHackProcessCrashed.
10. If exitCode != 0:
     return DfHackInvocationFailed.
11. Parse stdout as JSON.
12. If JSON parse fails:
     return DfHackInvalidJson.
13. Return typed result.
```

The process runner must not expose arbitrary command execution.

Allowed command names for v0.1 should be limited to:

```text
fortress-souls/diagnose
fortress-souls/list-dwarves
fortress-souls/get-dwarf-snapshot
```

`fortress-souls/echo-args` may exist only as a development verification script and should not be part of the production adapter allowlist.

---

## 9. Configuration shape

Recommended backend configuration:

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

Do not hardcode the Steam path in application code. It should be developer configuration only.

---

## 10. Diagnostic states

The adapter should distinguish these states:

| State | Detection | User-facing meaning |
|---|---|---|
| `DfHackUnavailable` | TCP preflight fails | DFHack is not reachable. Is DF/DFHack running? |
| `DfHackExecutableUnavailable` | process cannot start | Configured `dfhack-run` path is invalid. |
| `DfHackProcessCrashed` | negative exit code | `dfhack-run` crashed. Check DFHack state and configuration. |
| `ScriptUnavailable` | non-zero exit with command-not-recognized output | Fortress Souls scripts are not installed. |
| `NoFortressLoaded` | valid JSON, loaded flags false | DFHack is running, but no fortress is loaded. |
| `FortressLoaded` | valid JSON, loaded flags true | Connected to an active fortress. |
| `InvalidScriptOutput` | exit code zero but stdout is not valid JSON | DFHack script returned invalid output. |

---

## 11. Risks and mitigations

| Risk | Mitigation |
|---|---|
| `dfhack-run` crashes when DFHack is unavailable | TCP preflight before invocation. Treat negative exit codes as crash/unavailable. |
| Error text appears on stdout instead of stderr | Always capture both stdout and stderr. Use exit code first. |
| Scripts are missing or stale | Explicit install/sync script. Health endpoint can detect script availability via `diagnose`. |
| Backend accidentally enables arbitrary DFHack commands | Hard allowlist exact command names. No generic execute endpoint. |
| Script stdout contains non-JSON text | Require scripts to print only JSON. Add invalid-output handling and tests. |
| Steam install path differs across machines | Use explicit configuration for `RunPath` and `WorkingDirectory`. |
| Direct RPC is more powerful but complex | Defer direct remote API until after v0.1 if needed. |

---

## 12. Decision proposal

Use fake and JSON-file adapters first. For live DFHack integration in v0.1, use `dfhack-run` plus read-only Lua scripts installed into DFHack's script path. Add a TCP preflight against `127.0.0.1:5000` before invocation. Parse stdout JSON into typed contracts. Capture stdout, stderr, exit code, duration, and command name. Do not implement direct remote API integration in v0.1.

---

## 13. References

- DFHack Core documentation, script paths and `dfhack-run`: https://docs.dfhack.org/en/53.14-r2/docs/Core.html
- DFHack remote interface documentation: https://docs.dfhack.org/en/stable/docs/dev/Remote.html
- DFHack Lua API documentation: https://docs.dfhack.org/en/stable/docs/dev/Lua%20API.html
