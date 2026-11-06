<#
.SYNOPSIS
  The editor's buttons answer a mouse click whose press and release lie frames apart. PASS or FAIL per check.

.DESCRIPTION
  A player's click is a pointer that rests on a button, a press, a few frames held, and a release. The raycast click
  of editor-actions.ps1 sends all three in one call and cannot meet anything the page rebuilds in between. This test
  drives Sideload's DEBUG probe `sideloadgesture` instead: it walks hover, press and release frame by frame through
  the real EventSystem and decides the click with uGUI's own rule (the release clicks only when the pressed object
  is still the click handler under the pointer).

  Boots a test slot into the main menu with this Personify checkout, the given Sideload checkout (Debug, it carries
  the probe), Side Hustle and Personnel, opens the editor and works on a throwaway project:
    - Duplicate, Remove and the Remove dialog's confirm button.
    - The project menu (top left).
    - On the start screen the project card's More and an installed pack's More (SKIP without an installed pack).
  Every check also prints the probe's line: renders and mouseenter events during the gesture, whether uGUI sent the
  up and the click. The throwaway project is deleted at the end and the game is stopped.

    pwsh -File <Personify checkout>/tests/press-gesture.ps1 [-Slot mptest2] [-Sideload _wt-Sideload-x] [-Profile main]
    [-HoverFrames 30] [-HoldFrames 6] [-NoBoot]

  -Profile main boots with the mods of the real installation (read only) plus these builds. -NoBoot uses the game
  already running in the slot (Debug Side Hustle, main menu).
#>
[CmdletBinding()]
param(
    [ValidateSet('mptest', 'mptest2', 'mptest3', 'mptest4', 'mptest5', 'mptest6')]
    [string]$Slot = 'mptest2',
    [string]$Sideload = 'Sideload',
    [string]$SideHustle = '_wt-SideHustle-stack',
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    [int]$HoverFrames = 30,
    [int]$HoldFrames = 6,
    [switch]$NoBoot
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..").Path
$root = (Resolve-Path "$repo\..").Path
$checkout = Split-Path -Leaf $repo
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$app = 'personify-editor'
$project = 'Gesture Check'

$results = [System.Collections.Generic.List[object]]::new()
function Check([string]$name, [string]$result, [string]$detail) {
    $results.Add([pscustomobject]@{ Check = $name; Result = $result; Detail = $detail })
    Write-Host ("{0}  {1}  {2}" -f $result, $name, $detail)
}
function Verdict([bool]$ok) { if ($ok) { 'PASS' } else { 'FAIL' } }

function Eval([string]$js) {
    $out = (& python $s1 --slot $Slot eval $app $js 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { Write-Warning "[test] eval failed ($LASTEXITCODE): $out" }
    return $out
}

function EvalJson([string]$js) {
    $out = Eval $js
    try { return $out | ConvertFrom-Json }
    catch { Write-Warning "[test] eval answer is not JSON: $out"; return $null }
}

# Rest, press, hold, release through the probe. Returns the probe's "released" line, or '' when it never came.
function Gesture([string]$selector) {
    $words = @('sideloadgesture', $app, "$HoverFrames", "$HoldFrames") + ($selector -split ' ')
    $out = @(& python $s1 --slot $Slot run @words --until 'gesture on .* done' --max-wait 20000 2>&1 | ForEach-Object { "$_" })
    $line = @($out | Where-Object { $_ -match 'released:' }) | Select-Object -Last 1
    if (-not $line) {
        Write-Warning "[test] no probe line for $selector`n$($out -join "`n")"
        return ''
    }
    return ($line -replace '^.*released: ', '')
}

function MenuState { EvalJson "JSON.stringify({open: !!menuNode, items: menuNode ? menuNode.querySelectorAll('.mi').length : 0})" }
function Calm { $null = Eval "closeMenu(); closeDialog(); hideTip(); 'ok'" }

function StopSlotGame {
    $slotRoot = [string]((& python (Join-Path $root 'Workspace\tools\slots\slots.py') table | ConvertFrom-Json).$Slot.root)
    $procs = @(Get-CimInstance Win32_Process -Filter "Name = 'Schedule I.exe'" |
               Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($slotRoot, 'OrdinalIgnoreCase') })
    foreach ($p in $procs) { Write-Host "[test] stopping pid $($p.ProcessId) in $Slot"; Stop-Process -Id $p.ProcessId -Force }
}

try {
    if (-not $NoBoot) {
        # Side Hustle as a Debug build of its released tag: the hub's `shstart` opens the editor, and only the Debug
        # build has it. -Profile main takes only store mods in -Mods; Personnel comes in as Personify's dependency.
        $argv = @('-NoProfile', '-File', $session, '-Slot', $Slot, '-Save', '0', '-NoApp', '-Profile', $Profile,
                  '-Build', "$checkout,$Sideload,$SideHustle")
        if ($Profile -eq 'stack') { $argv += @('-Mods', 'Personnel') }
        Write-Host "[test] session.ps1 $($argv[3..($argv.Count - 1)] -join ' ')"
        & pwsh @argv
        if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed with exit code $LASTEXITCODE" }
    }

    $null = @(& python $s1 --slot $Slot run shstart doodesch.personify 2>&1 | ForEach-Object { "$_" })
    $screen = Eval 'ui.screen'
    Check 'editor open' (Verdict ($screen -eq 'start' -or $screen -eq 'editor')) "ui.screen = $screen"

    $null = Eval "JSON.stringify(call('projects.delete', '$project'))"
    $made = EvalJson "JSON.stringify(call('projects.create', {name: '$project', author: 'Check'}))"
    Check 'project created' (Verdict ([bool]$made.ok)) "folder $($made.folder)"
    $null = Eval "openProject('$project'); 'ok'"
    $added = EvalJson "JSON.stringify([call('npc.add', {name: 'Cass Tomer', role: 'customer'}).ok, call('npc.add', {name: 'Sol Plier', role: 'supplier'}).ok])"
    Check 'customer and supplier added' (Verdict (@($added | Where-Object { $_ -eq $true }).Count -eq 2)) ($added -join ',')
    $null = Eval "openProject('$project'); setActive(npcs()[0].id); 'ok'"
    Calm

    # Duplicate
    $before = [int](Eval 'npcs().length')
    $probe = Gesture '.sacts .ib:nth-child(2)'
    $after = [int](Eval 'npcs().length')
    Check 'duplicate answers one click' (Verdict ($after -eq $before + 1)) "$before -> $after; $probe"
    Calm

    # Remove opens its dialog, the dialog's Remove takes the NPC out
    $probe = Gesture '.sacts .ib:nth-child(3)'
    $dlg = Eval 'String(!!dialog)'
    Check 'remove answers one click' (Verdict ($dlg -eq 'true')) "dialog = $dlg; $probe"
    if ($dlg -eq 'true') {
        $probe = Gesture '.scrim .dacts .btn.bad'
        $left = [int](Eval 'npcs().length')
        Check 'dialog remove answers one click' (Verdict ($left -eq $after - 1)) "$after -> $left; $probe"
    } else {
        Check 'dialog remove answers one click' 'FAIL' 'the dialog never opened'
    }
    Calm

    # Project menu top left
    $probe = Gesture '.pmenu'
    $menu = MenuState
    Check 'project menu answers one click' (Verdict ($menu.open -and $menu.items -gt 0)) "$($menu.items) item(s); $probe"
    Calm
    $null = Eval "goStart(); ui.screen"

    # Start screen: project card More, then pack row More
    $idx = [int](Eval "Array.from(document.querySelectorAll('.pcard .pnm')).map(x=>x.textContent).indexOf('$project') + 1")
    if ($idx -gt 0) {
        $probe = Gesture ".grid .pcard:nth-child($idx) .pcf .ib"
        $menu = MenuState
        Check 'project card More answers one click' (Verdict ($menu.open -and $menu.items -ge 2)) "$($menu.items) item(s); $probe"
    } else {
        Check 'project card More answers one click' 'FAIL' 'project card not on the start screen'
    }
    Calm

    $packs = [int](Eval "document.querySelectorAll('.krow').length")
    if ($packs -gt 0) {
        $probe = Gesture '.krow .ib'
        $menu = MenuState
        Check 'pack More answers one click' (Verdict ($menu.open -and $menu.items -ge 1)) "$($menu.items) item(s), $packs pack(s); $probe"
    } else {
        Check 'pack More answers one click' 'SKIP' 'no installed pack row on the start screen'
    }
    Calm
} finally {
    try { $null = Eval "JSON.stringify(call('projects.delete', '$project'))"; $null = Eval "goStart(); 'ok'" }
    catch { Write-Warning "[test] deleting the throwaway project failed: $_" }
    if (-not $NoBoot) { StopSlotGame }
}

$failed = @($results | Where-Object Result -eq 'FAIL').Count
$skipped = @($results | Where-Object Result -eq 'SKIP').Count
Write-Host ("[test] {0} of {1} checks passed, {2} skipped" -f ($results.Count - $failed - $skipped), $results.Count, $skipped)
exit $(if ($failed -eq 0) { 0 } else { 1 })
