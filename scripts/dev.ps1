Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ConfigHelper = Join-Path $RepoRoot "scripts\dev-config.mjs"
$BackendProject = Join-Path $RepoRoot "src\backend\FortressSouls.Api\FortressSouls.Api.csproj"
$FrontendDir = Join-Path $RepoRoot "src\frontend"

$backendProcess = $null

function Ensure-FrontendDependencies {
    $NodeModulesDir = Join-Path $FrontendDir "node_modules"
    if (Test-Path $NodeModulesDir) {
        return
    }

    Write-Host "==> frontend install"
    Push-Location $FrontendDir
    try {
        & npm install
        if ($LASTEXITCODE -ne 0) {
            throw "frontend install failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Set-EnvironmentFromConfig {
    $output = & node $ConfigHelper env $RepoRoot
    if ($LASTEXITCODE -ne 0) {
        throw "local config resolution failed with exit code $LASTEXITCODE."
    }

    foreach ($line in $output) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $separatorIndex = $line.IndexOf("=")
        if ($separatorIndex -lt 1) {
            continue
        }

        $name = $line.Substring(0, $separatorIndex)
        $value = $line.Substring($separatorIndex + 1)
        Set-Item -Path ("Env:" + $name) -Value $value
    }
}

function Write-ConfigSummary {
    Write-Host "==> config"
    & node $ConfigHelper summary $RepoRoot
    if ($LASTEXITCODE -ne 0) {
        throw "local config summary failed with exit code $LASTEXITCODE."
    }
}

try {
    Ensure-FrontendDependencies
    Set-EnvironmentFromConfig
    Write-ConfigSummary

    Write-Host "==> backend"
    $backendProcess = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList @("run", "--no-launch-profile", "--project", $BackendProject, "--urls", $env:FORTRESS_SOULS_BACKEND_BASE_URL) `
        -WorkingDirectory $RepoRoot `
        -PassThru `
        -NoNewWindow

    if ($backendProcess.HasExited) {
        throw "backend exited before the frontend started with exit code $($backendProcess.ExitCode)."
    }

    Write-Host "==> frontend"
    Push-Location $FrontendDir
    try {
        & npm run dev -- --host 127.0.0.1 --port $env:FORTRESS_SOULS_FRONTEND_PORT --strictPort
        if ($LASTEXITCODE -ne 0) {
            throw "frontend exited with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    if ($null -ne $backendProcess -and -not $backendProcess.HasExited) {
        Stop-Process -Id $backendProcess.Id -Force
    }
}
