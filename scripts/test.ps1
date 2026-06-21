Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$BackendSolution = Join-Path $RepoRoot "src\backend\FortressSouls.slnx"
$FrontendDir = Join-Path $RepoRoot "src\frontend"

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter()]
        [string[]]$Arguments = @(),

        [Parameter()]
        [string]$WorkingDirectory = $RepoRoot
    )

    Write-Host "==> $Label"
    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$Label failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

Invoke-NativeCommand -Label "backend build" -FilePath "dotnet" -Arguments @("build", $BackendSolution)
Invoke-NativeCommand -Label "backend test" -FilePath "dotnet" -Arguments @("test", $BackendSolution, "--no-build")
Invoke-NativeCommand -Label "local dev config test" -FilePath "node" -Arguments @("--test", ".\scripts\dev-config.test.mjs")
Invoke-NativeCommand -Label "frontend typecheck" -FilePath "npm" -Arguments @("run", "typecheck") -WorkingDirectory $FrontendDir
Invoke-NativeCommand -Label "frontend test" -FilePath "npm" -Arguments @("test", "--", "--run") -WorkingDirectory $FrontendDir
Invoke-NativeCommand -Label "frontend build" -FilePath "npm" -Arguments @("run", "build") -WorkingDirectory $FrontendDir
Invoke-NativeCommand -Label "frontend e2e install" -FilePath "npm" -Arguments @("run", "test:e2e:install") -WorkingDirectory $FrontendDir
Invoke-NativeCommand -Label "frontend e2e smoke" -FilePath "npm" -Arguments @("run", "test:e2e") -WorkingDirectory $FrontendDir
