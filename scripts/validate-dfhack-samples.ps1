param(
    [string]$SampleRoot = ".\dfhack\samples"
)

$ErrorActionPreference = "Stop"

$listPath = Join-Path $SampleRoot "dwarves-list.sample.json"
$snapshotPath = Join-Path $SampleRoot "dwarf-snapshot.sample.json"
$bundlePath = Join-Path $SampleRoot "b019-dwarf-snapshots.bundle.json"

$dwarfList = Get-Content $listPath -Raw | ConvertFrom-Json
$snapshot = Get-Content $snapshotPath -Raw | ConvertFrom-Json
$bundle = Get-Content $bundlePath -Raw | ConvertFrom-Json

[PSCustomObject]@{
    DwarfListSchema = $dwarfList.schemaVersion
    DwarfListCount = $dwarfList.count
    SnapshotSchema = $snapshot.schemaVersion
    SnapshotUnitId = $snapshot.identity.id
    BundleSchema = $bundle.schemaVersion
    BundleSnapshotCount = $bundle.snapshots.Count
} | Format-List

if ($dwarfList.count -lt 1) { throw "Dwarf list sample is empty." }
if ($bundle.snapshots.Count -lt 1) { throw "Bundle sample is empty." }
if (-not $snapshot.promptCandidates) { throw "Snapshot sample has no promptCandidates." }

Write-Host "Samples parsed successfully."
