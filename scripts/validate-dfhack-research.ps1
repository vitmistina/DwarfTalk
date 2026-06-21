param(
    [string]$SampleRoot = ".\dfhack\samples\research"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Read-Json([string]$Path) {
    return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
}

function Assert-SpatialSample($spatial, [string]$expectedKind, [string]$label) {
    if ($spatial.schemaVersion -ne "fortress-souls-spatial-vision-research.v0.1") { throw "$label has an unexpected spatial schema." }
    if ($spatial.provenance.kind -ne $expectedKind) { throw "$label has incorrect provenance." }
    if ($spatial.bounds.width -gt $spatial.limits.maxWidth -or $spatial.bounds.height -gt $spatial.limits.maxHeight) { throw "$label exceeds spatial bounds." }
    if ($spatial.cells.Count -ne ($spatial.bounds.width * $spatial.bounds.height)) { throw "$label cell count does not match bounds." }
    if ($spatial.grid.Count -ne $spatial.bounds.height) { throw "$label grid height does not match bounds." }

    $coordinateKeys = @($spatial.cells | ForEach-Object { "$($_.x),$($_.y),$($_.z)" })
    if (($coordinateKeys | Sort-Object -Unique).Count -ne $coordinateKeys.Count) { throw "$label contains duplicate coordinates." }

    for ($row = 0; $row -lt $spatial.grid.Count; $row++) {
        if ($spatial.grid[$row].Length -ne $spatial.bounds.width) { throw "$label grid row $row has the wrong width." }
        for ($column = 0; $column -lt $spatial.bounds.width; $column++) {
            $cell = $spatial.cells[$row * $spatial.bounds.width + $column]
            if ($cell.symbol -ne [string]$spatial.grid[$row][$column]) { throw "$label grid and semantic cell symbols differ." }
        }
    }

    $gridSymbols = @($spatial.grid.ToCharArray() | Sort-Object -Unique)
    $legendSymbols = @($spatial.legend.symbol | Sort-Object -Unique)
    if (Compare-Object $gridSymbols $legendSymbols) { throw "$label legend is not exactly the set of symbols present in the grid." }

    $orderedCells = @($spatial.cells | Sort-Object y, x)
    for ($index = 0; $index -lt $spatial.cells.Count; $index++) {
        if ($spatial.cells[$index].x -ne $orderedCells[$index].x -or $spatial.cells[$index].y -ne $orderedCells[$index].y) {
            throw "$label cells are not in deterministic row-major order."
        }
    }

    Write-Host "$label`: PASS ($($spatial.cells.Count) cells, $($legendSymbols.Count) legend symbols)"
}

function Assert-StockSample($stock, [string]$expectedKind, [string]$label) {
    if ($stock.schemaVersion -ne "fortress-souls-stock-summary-research.v0.1") { throw "$label has an unexpected stock schema." }
    if ($stock.provenance.kind -ne $expectedKind) { throw "$label has incorrect provenance." }
    if ($stock.totals.quantity -ne ($stock.totals.usableQuantity + $stock.totals.excludedQuantity)) { throw "$label total quantity invariant failed." }
    if ($stock.totals.itemObjects -ne ($stock.totals.usableItemObjects + $stock.totals.excludedItemObjects)) { throw "$label object count invariant failed." }
    if ($stock.totals.usableQuantity -ne ($stock.totals.categorizedQuantity + $stock.totals.uncategorizedQuantity)) { throw "$label usable quantity invariant failed." }

    $excludedQuantity = ($stock.excludedByReason.PSObject.Properties.Value | Measure-Object -Property quantity -Sum).Sum
    $excludedObjects = ($stock.excludedByReason.PSObject.Properties.Value | Measure-Object -Property itemObjects -Sum).Sum
    $categoryQuantity = ($stock.categories.PSObject.Properties.Value | Measure-Object -Property exact -Sum).Sum
    if ($excludedQuantity -ne $stock.totals.excludedQuantity -or $excludedObjects -ne $stock.totals.excludedItemObjects) { throw "$label exclusion arithmetic failed." }
    if ($categoryQuantity -ne $stock.totals.categorizedQuantity) { throw "$label category arithmetic failed." }

    Write-Host "$label`: PASS ($categoryQuantity categorized quantity, $excludedQuantity excluded quantity)"
}

Assert-SpatialSample (Read-Json (Join-Path $SampleRoot "spatial-vision.synthetic.json")) "synthetic" "Synthetic spatial sample"
Assert-StockSample (Read-Json (Join-Path $SampleRoot "stock-summary.synthetic.json")) "synthetic" "Synthetic stock sample"
Assert-SpatialSample (Read-Json (Join-Path $SampleRoot "spatial-vision.live-2026-06-21.json")) "live-dfhack" "Live spatial sample"
Assert-StockSample (Read-Json (Join-Path $SampleRoot "stock-summary.live-2026-06-21.json")) "live-dfhack" "Live stock sample"

$scriptRoot = Resolve-Path (Join-Path $PSScriptRoot "..\dfhack\scripts\fortress-souls")
$forbiddenCalls = @("ensureTileBlock(", "spawnFlow(", "teleport(", "designate", "setTileAquifer(", "removeTileAquifer(", "moveTo")
foreach ($script in Get-ChildItem -LiteralPath $scriptRoot -Filter "research-*.lua") {
    $source = Get-Content -Raw -LiteralPath $script.FullName
    foreach ($call in $forbiddenCalls) {
        if ($source.IndexOf($call, [StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "$($script.Name) contains forbidden mutation token '$call'." }
    }
}

Write-Host "Read-only source scan: PASS"
