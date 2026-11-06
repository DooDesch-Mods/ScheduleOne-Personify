<#
.SYNOPSIS
  The "Unlocked from the start" switch shows Personnel's start state. Reports PASS or FAIL per check.

.DESCRIPTION
  Personnel unlocks a pack NPC whose relationships.unlocked is unset unless it is a customer with connections. The
  switch shows that state while the value stays unset, and only a flip writes it. Boots a test slot with this
  checkout's build and the given Personnel checkout, loads a save so the editor knows the game's NPCs, goes back to
  the main menu and opens the editor from Side Hustle on a throwaway project with one customer and one supplier:
    - A new customer has a connection and the switch shows locked; the value is unset.
    - Without connections (Remove clicked) the switch shows unlocked, the value stays unset, no lock warning.
    - A supplier with the value unset shows unlocked.
    - Flipping the switch (raycast click) writes false and raises customer_no_connection; flipping back writes true
      and clears it.
  The throwaway project is deleted at the end and the game is stopped.
    pwsh -File <Personify checkout>/tests/unlock-toggle-check.ps1 [-Personnel Personnel] [-Slot mptest4] [-Profile main] [-NoBoot]
#>
[CmdletBinding()]
param(
    [ValidateSet('mptest', 'mptest2', 'mptest3', 'mptest4', 'mptest5', 'mptest6')]
    [string]$Slot = 'mptest4',
    [string]$Personnel = 'Personnel',
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    [int]$Save = 5,
    # Personify checkout to build; the default is the one this script lives in.
    [string]$Build = '',
    [switch]$NoBoot
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..").Path
$root = (Resolve-Path "$repo\..").Path
$checkout = if ($Build) { $Build } else { Split-Path -Leaf $repo }
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$call = Join-Path $PSScriptRoot 'bridge_call.py'
$app = 'personify-editor'
$project = 'Unlock Check'

$results = [System.Collections.Generic.List[object]]::new()
function Check([string]$name, [bool]$ok, [string]$detail) {
    $results.Add([pscustomobject]@{ Check = $name; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Detail = $detail })
    Write-Host ("{0}  {1}  {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail)
}

function Eval([string]$js) {
    $out = (& python -I $s1 --slot $Slot eval $app $js 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { Write-Warning "[test] eval failed ($LASTEXITCODE): $out" }
    return $out
}

function EvalJson([string]$js) {
    $out = Eval $js
    try { return $out | ConvertFrom-Json }
    catch { Write-Warning "[test] eval answer is not JSON: $out"; return $null }
}

function Click([string]$selector) {
    $out = (& python -I $s1 --slot $Slot click $app $selector 2>&1 | Out-String).Trim()
    $ok = $out -match "'dispatched': True"
    if (-not $ok) { Write-Warning "[test] click on $selector was not dispatched: $out" }
    return $ok
}

function Bridge([string]$command, [string]$argsJson = '{}', [int]$timeoutMs = 12000) {
    $out = (& python -I $call --slot $Slot $command $argsJson --timeout $timeoutMs 2>&1 | Out-String).Trim()
    Write-Host "  bridge $command -> $out"
    return ($LASTEXITCODE -eq 0)
}

function WaitReady([int]$timeout = 600) {
    & python -I $s1 --slot $Slot wait-ready --timeout $timeout | Out-Host
    return ($LASTEXITCODE -eq 0)
}

function StopSlotGame {
    $procs = @(Get-CimInstance Win32_Process -Filter "Name = 'Schedule I.exe'" |
               Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($script:gameRoot, 'OrdinalIgnoreCase') })
    foreach ($p in $procs) { Write-Host "[test] stopping pid $($p.ProcessId) in $Slot"; Stop-Process -Id $p.ProcessId -Force }
}

# shstart is a debug command of Side Hustle. Profile main runs the player's release build, so there the hub's own
# "Play solo" action starts the gamemode.
function StartPersonify {
    if ($Profile -eq 'main') {
        return @(& python -I $s1 --slot $Slot eval sidehustle-hub "act('hub.play', 'doodesch.personify'); 'ok'" 2>&1 | ForEach-Object { "$_" })
    }
    return @(& python -I $s1 --slot $Slot run shstart doodesch.personify 2>&1 | ForEach-Object { "$_" })
}

function OpenEditor {
    $answer = StartPersonify
    for ($i = 0; $i -lt 12; $i++) {
        $screen = Eval 'typeof ui === "object" ? ui.screen : "none"'
        if ($screen -eq 'start' -or $screen -eq 'editor') { return $screen }
        Write-Host "[test] editor not up yet ($screen), asking again"
        Start-Sleep -Seconds 2
        $answer = StartPersonify
    }
    Write-Host "[test] last start answer: $($answer -join ' | ')"
    return $screen
}

# Switch state, stored value and the lock warning of the active NPC. The row is found by its data-path; the
# switch gets an id so the raycast click can name it.
$stateJs = @'
JSON.stringify((() => {
  renderFields();
  const row = Array.from(document.querySelectorAll('.fr')).find(r => r.getAttribute('data-path') === 'relationships.unlocked');
  const sw = row ? row.querySelector('.sw') : null;
  if (sw) sw.id = 'unlock-switch';
  const n = npcById(st.active);
  const i = npcs().indexOf(n);
  const v = getPath(n, 'relationships.unlocked');
  const conns = getPath(n, 'relationships.connections') || [];
  return { row: !!row, on: sw ? sw.classList.contains('on') : null, value: v === undefined ? 'unset' : v,
           conns: conns.length, warn: (st.problems || []).some(p => p.code === 'customer_no_connection' && p.npc === i) };
})())
'@ -replace "`r?`n", ' '

function State { return EvalJson $stateJs }

$table = (& python -I $slotsPy --json table | Out-String | ConvertFrom-Json)
$script:gameRoot = [string]$table.$Slot.root

try {
    if (-not $NoBoot) {
        # Profile main carries Side Hustle like the real install; its Sideload is a release build without the probe
        # that eval and click go through, so a Debug Sideload replaces it.
        $builds = if ($Profile -eq 'main') { "$checkout,$Personnel,Sideload" } else { "$checkout,$Personnel" }
        $argv = @('-NoProfile', '-File', $session, '-Slot', $Slot, '-Profile', $Profile, '-Save', "$Save", '-NoApp', '-Build', $builds)
        if ($Profile -eq 'stack') { $argv += @('-Mods', 'SideHustle') }
        Write-Host "[test] session.ps1 $($argv[3..($argv.Count - 1)] -join ' ')"
        & pwsh @argv
        if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed with exit code $LASTEXITCODE" }
        Check 'save loaded' (WaitReady) "slot $Slot save $Save"
        Check 'back to the main menu' (Bridge 'return_to_menu' '{}' 60000) 'return_to_menu'
    }
    $screen = OpenEditor
    Check 'editor open' ($screen -eq 'start' -or $screen -eq 'editor') "ui.screen = $screen"

    $null = Eval "JSON.stringify(call('projects.delete', '$project'))"
    $made = EvalJson "JSON.stringify(call('projects.create', {name: '$project', author: 'Check'}))"
    Check 'project created' ([bool]$made.ok) "folder $($made.folder)"
    $null = Eval "openProject('$project'); 'ok'"
    $added = EvalJson "JSON.stringify([call('npc.add', {name: 'Cass Tomer', role: 'customer'}).ok, call('npc.add', {name: 'Sol Plier', role: 'supplier'}).ok])"
    Check 'customer and supplier added' (@($added | Where-Object { $_ -eq $true }).Count -eq 2) ($added -join ',')
    $null = Eval "openProject('$project'); setActive(npcs()[0].id); setArea('relations'); 'ok'"

    $s = State
    Check 'new customer has a connection' ($s.conns -ge 1) "connections $($s.conns)"
    Check 'customer with a connection shows locked' ($s.row -and $s.on -eq $false) "on=$($s.on)"
    Check 'its value is unset' ($s.value -eq 'unset') "value=$($s.value)"

    # Remove every connection through the Remove button, the way a player does.
    for ($i = 0; $i -lt 5 -and $s.conns -gt 0; $i++) {
        $null = Click '.conns .lr .btn'
        $s = State
    }
    Check 'connections removed' ($s.conns -eq 0) "connections $($s.conns)"
    Check 'customer without connections shows unlocked' ($s.on -eq $true) "on=$($s.on)"
    Check 'its value stays unset' ($s.value -eq 'unset') "value=$($s.value)"
    Check 'no lock warning while unset' (-not $s.warn) "warn=$($s.warn)"

    $clicked = Click '#unlock-switch'
    $s = State
    Check 'flip writes false' ($clicked -and $s.value -eq $false -and $s.on -eq $false) "value=$($s.value) on=$($s.on)"
    Check 'locked customer without connections warns' ($s.warn) "warn=$($s.warn)"
    $clicked = Click '#unlock-switch'
    $s = State
    Check 'flip back writes true' ($clicked -and $s.value -eq $true -and $s.on -eq $true) "value=$($s.value) on=$($s.on)"
    Check 'warning cleared' (-not $s.warn) "warn=$($s.warn)"

    $null = Eval "setActive(npcs()[1].id); setArea('relations'); 'ok'"
    $s = State
    Check 'supplier with the value unset shows unlocked' ($s.on -eq $true -and $s.value -eq 'unset') "on=$($s.on) value=$($s.value)"
} finally {
    try { $null = Eval "JSON.stringify(call('projects.delete', '$project'))"; $null = Eval "goStart(); 'ok'" }
    catch { Write-Warning "[test] deleting the throwaway project failed: $_" }
    if (-not $NoBoot) { StopSlotGame }
}

$failed = @($results | Where-Object Result -eq 'FAIL').Count
Write-Host ("[test] {0} of {1} checks passed" -f ($results.Count - $failed), $results.Count)
exit $(if ($failed -eq 0) { 0 } else { 1 })
