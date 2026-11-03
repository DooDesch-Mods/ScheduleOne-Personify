<#
.SYNOPSIS
  Personify boots with and without Side Hustle. Reports PASS or FAIL per check.

.DESCRIPTION
  Two boots into the main menu of a test slot, both with this checkout's build:
    with    - Side Hustle in the slot: the gamemode registers with the hub.
    without - SideHustle.dll removed from the slot: Personify still loads, registers its phone app and logs that
              the hub entry is missing, and no FileNotFoundException for SideHustle is thrown.
  The game is stopped at the end. Run from anywhere:
    pwsh -File <Personify checkout>/tests/optional-sidehustle.ps1 [-Slot mptest]
#>
[CmdletBinding()]
param(
    [ValidateSet('mptest', 'mptest2', 'mptest3', 'mptest4', 'mptest5', 'mptest6')]
    [string]$Slot = 'mptest'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..").Path
$root = (Resolve-Path "$repo\..").Path
$checkout = Split-Path -Leaf $repo
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$slotRoot = [string]((& python (Join-Path $root 'Workspace\tools\slots\slots.py') table | ConvertFrom-Json).$Slot.root)
if (-not $slotRoot) { throw "slots.py table names no root for $Slot" }
$log = Join-Path $slotRoot 'MelonLoader\Latest.log'

$results = [System.Collections.Generic.List[object]]::new()
function Check([string]$name, [bool]$ok, [string]$detail) {
    $results.Add([pscustomobject]@{ Check = $name; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Detail = $detail })
    Write-Host ("{0}  {1}  {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail)
}

function StopSlotGame {
    $procs = @(Get-CimInstance Win32_Process -Filter "Name = 'Schedule I.exe'" |
               Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($slotRoot, 'OrdinalIgnoreCase') })
    foreach ($p in $procs) {
        Write-Host "[test] stopping pid $($p.ProcessId) in $Slot"
        Stop-Process -Id $p.ProcessId -Force
    }
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        $left = @(Get-CimInstance Win32_Process -Filter "Name = 'Schedule I.exe'" |
                  Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($slotRoot, 'OrdinalIgnoreCase') })
        if ($left.Count -eq 0) { return }
        Start-Sleep -Milliseconds 250
    }
    throw "the game in $Slot is still running after 20 s"
}

function Boot([string[]]$extra) {
    $argv = @('-NoProfile', '-File', $session, '-Slot', $Slot, '-Save', '0', '-NoApp') + $extra
    Write-Host "[test] session.ps1 $($argv[3..($argv.Count - 1)] -join ' ')"
    & pwsh @argv
    if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed with exit code $LASTEXITCODE" }
    if (-not (Test-Path $log)) { throw "no MelonLoader log at $log" }
    return @(Get-Content $log)
}

function Has($lines, [string]$pattern) { return @($lines | Where-Object { $_ -match $pattern }).Count -gt 0 }

$missingAsm = "Could not load file or assembly 'SideHustle"
$phoneLine = '\[Personify\] \[phone\] (app registered with Sideload|Sideload is not loaded yet - the app registration is queued)'

try {
    # ---- with Side Hustle ------------------------------------------------------------------------------------
    $lines = Boot @('-Build', $checkout, '-Mods', 'SideHustle')
    Check 'with: SideHustle.dll in Mods' (Test-Path (Join-Path $slotRoot 'Mods\SideHustle.dll')) $slotRoot
    Check 'with: gamemode registered' (Has $lines '\[Personify\] Personify .* registered with Side Hustle\.') 'log line "registered with Side Hustle."'
    Check 'with: phone app registered' (Has $lines $phoneLine) 'log line "[phone] app registered"'
    Check 'with: no missing-assembly error' (-not (Has $lines ([regex]::Escape($missingAsm)))) $missingAsm

    # ---- without Side Hustle ---------------------------------------------------------------------------------
    StopSlotGame
    $lines = Boot @('-Build', $checkout)
    $shDlls = @(Get-ChildItem (Join-Path $slotRoot 'Mods'), (Join-Path $slotRoot 'Plugins') -Filter 'SideHustle*.dll' -File -ErrorAction SilentlyContinue)
    if ($shDlls.Count -gt 0) {
        # The stack pulled Side Hustle in as a dependency; take it out of this slot and boot the slot as it is.
        StopSlotGame
        foreach ($f in $shDlls) { Write-Host "[test] removing $($f.FullName)"; Remove-Item $f.FullName -Force }
        $lines = Boot @('-KeepSlot')
    }
    Check 'without: no SideHustle*.dll in Mods or Plugins' (@(Get-ChildItem (Join-Path $slotRoot 'Mods'), (Join-Path $slotRoot 'Plugins') -Filter 'SideHustle*.dll' -File -ErrorAction SilentlyContinue).Count -eq 0) $slotRoot
    Check 'without: Personify loaded' (Has $lines 'Personify v\d') 'MelonLoader mod list'
    Check 'without: phone app registered' (Has $lines $phoneLine) 'log line "[phone] app registered"'
    Check 'without: missing Side Hustle warned' (Has $lines '\[Personify\] Side Hustle is not installed') 'log line "Side Hustle is not installed"'
    Check 'without: no missing-assembly error' (-not (Has $lines ([regex]::Escape($missingAsm)))) $missingAsm
    Check 'without: no Personify init failure' (-not (Has $lines 'Failed to initialize.*Personify|Personify.*Failed to initialize')) 'MelonLoader init failure line'
} finally {
    StopSlotGame
}

$failed = @($results | Where-Object Result -eq 'FAIL').Count
Write-Host ("[test] {0} of {1} checks passed" -f ($results.Count - $failed), $results.Count)
exit $(if ($failed -eq 0) { 0 } else { 1 })
