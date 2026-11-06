<#
.SYNOPSIS
  The editor's icon-button actions answer a click. Reports PASS or FAIL per check.

.DESCRIPTION
  Boots a test slot into the main menu with this checkout's build, Side Hustle, Personnel and NPC Cards, opens the
  editor from the hub and works on a throwaway project with one customer and one supplier. Every click goes through
  Sideload's raycast (s1.py click), after a mouseenter so the hover tooltip is up as it is under a real mouse:
    - Duplicate adds a copy of the selected NPC.
    - Remove opens its dialog, and the dialog's Remove takes the NPC out.
    - The project menu (top left) opens.
    - On the start screen the project card's More and an installed pack's More open their menus.
    - Personnel answers the card calls: exported and installed card lists are arrays.
  The pack menu is only opened, never confirmed: removing an installed pack would change the slot. The throwaway
  project is deleted at the end and the game is stopped. Run from anywhere:
    pwsh -File <Personify checkout>/tests/editor-actions.ps1 [-Slot mptest] [-Sideload _wt-Sideload-x] [-NoBoot]
  -NoBoot uses the game already running in the slot (Debug Side Hustle, main menu).
#>
[CmdletBinding()]
param(
    [ValidateSet('mptest', 'mptest2', 'mptest3', 'mptest4', 'mptest5', 'mptest6')]
    [string]$Slot = 'mptest',
    # A Sideload checkout or worktree to build into the slot instead of the stack's Sideload.
    [string]$Sideload = '',
    [switch]$NoBoot
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..").Path
$root = (Resolve-Path "$repo\..").Path
$checkout = Split-Path -Leaf $repo
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$app = 'personify-editor'
$project = 'Actions Check'

$results = [System.Collections.Generic.List[object]]::new()
function Check([string]$name, [bool]$ok, [string]$detail) {
    $results.Add([pscustomobject]@{ Check = $name; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Detail = $detail })
    Write-Host ("{0}  {1}  {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail)
}

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

# Hover first, as a real mouse does, then click through the raycast.
function HoverClick([string]$selector) {
    $tip = Eval "(()=>{const b=document.querySelector('$selector'); if(!b) return 'missing'; b.dispatchEvent(new Event('mouseenter')); return 'hovered'; })()"
    if ($tip -ne 'hovered') { Write-Warning "[test] $selector not found for the hover: $tip"; return $false }
    $out = (& python $s1 --slot $Slot click $app $selector 2>&1 | Out-String).Trim()
    $ok = $out -match "'dispatched': True"
    if (-not $ok) { Write-Warning "[test] click on $selector was not dispatched: $out" }
    return $ok
}

function StopSlotGame {
    $slotRoot = [string]((& python (Join-Path $root 'Workspace\tools\slots\slots.py') table | ConvertFrom-Json).$Slot.root)
    $procs = @(Get-CimInstance Win32_Process -Filter "Name = 'Schedule I.exe'" |
               Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($slotRoot, 'OrdinalIgnoreCase') })
    foreach ($p in $procs) { Write-Host "[test] stopping pid $($p.ProcessId) in $Slot"; Stop-Process -Id $p.ProcessId -Force }
}

try {
    if (-not $NoBoot) {
        $builds = if ($Sideload) { "$checkout,$Sideload" } else { $checkout }
        $argv = @('-NoProfile', '-File', $session, '-Slot', $Slot, '-Save', '0', '-NoApp', '-Build', $builds, '-Mods', 'SideHustle,Personnel,NpcCards')
        Write-Host "[test] session.ps1 $($argv[3..($argv.Count - 1)] -join ' ')"
        & pwsh @argv
        if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed with exit code $LASTEXITCODE" }
    }

    $out = @(& python $s1 --slot $Slot run shstart doodesch.personify 2>&1 | ForEach-Object { "$_" })
    $screen = Eval 'ui.screen'
    Check 'editor open' ($screen -eq 'start' -or $screen -eq 'editor') "ui.screen = $screen"

    # Throwaway project with one customer and one supplier.
    $null = Eval "JSON.stringify(call('projects.delete', '$project'))"
    $made = EvalJson "JSON.stringify(call('projects.create', {name: '$project', author: 'Check'}))"
    Check 'project created' ([bool]$made.ok) "folder $($made.folder)"
    $null = Eval "openProject('$project'); 'ok'"
    $added = EvalJson "JSON.stringify([call('npc.add', {name: 'Cass Tomer', role: 'customer'}).ok, call('npc.add', {name: 'Sol Plier', role: 'supplier'}).ok])"
    Check 'customer and supplier added' (@($added | Where-Object { $_ -eq $true }).Count -eq 2) ($added -join ',')
    $null = Eval "openProject('$project'); setActive(npcs()[0].id); 'ok'"
    $before = [int](Eval 'npcs().length')

    # Duplicate
    $clicked = HoverClick '.sacts .ib:nth-child(2)'
    $after = [int](Eval 'npcs().length')
    Check 'duplicate adds an NPC' ($clicked -and $after -eq $before + 1) "$before -> $after"

    # Remove: dialog, then its Remove button
    $null = Eval "hideTip(); 'ok'"
    $clicked = HoverClick '.sacts .ib:nth-child(3)'
    $dlg = Eval 'String(!!dialog)'
    Check 'remove opens its dialog' ($clicked -and $dlg -eq 'true') "dialog = $dlg"
    $null = HoverClick '.scrim .dacts .btn.bad'
    $left = [int](Eval 'npcs().length')
    Check 'dialog remove takes the NPC out' ($left -eq $after - 1) "$after -> $left"

    # Project menu top left
    $null = Eval "hideTip(); 'ok'"
    $clicked = HoverClick '.pmenu'
    $menu = EvalJson "JSON.stringify({open: !!menuNode, items: menuNode ? menuNode.querySelectorAll('.mi').length : 0})"
    Check 'project menu opens' ($clicked -and $menu.open -and $menu.items -gt 0) "$($menu.items) item(s)"
    $null = Eval "closeMenu(); hideTip(); goStart(); ui.screen"

    # Start screen: project card More, then pack row More
    $idx = [int](Eval "Array.from(document.querySelectorAll('.pcard .pnm')).map(x=>x.textContent).indexOf('$project') + 1")
    Check 'project card on the start screen' ($idx -gt 0) "position $idx"
    if ($idx -gt 0) {
        $clicked = HoverClick ".grid .pcard:nth-child($idx) .pcf .ib"
        $menu = EvalJson "JSON.stringify({open: !!menuNode, items: menuNode ? menuNode.querySelectorAll('.mi').length : 0})"
        Check 'project card More opens its menu' ($clicked -and $menu.open -and $menu.items -ge 2) "$($menu.items) item(s)"
        $null = Eval "closeMenu(); hideTip(); 'ok'"
    }
    $packs = [int](Eval "document.querySelectorAll('.krow').length")
    if ($packs -gt 0) {
        $clicked = HoverClick '.krow .ib'
        $menu = EvalJson "JSON.stringify({open: !!menuNode, items: menuNode ? menuNode.querySelectorAll('.mi').length : 0})"
        Check 'pack More opens its menu' ($clicked -and $menu.open -and $menu.items -ge 1) "$($menu.items) item(s), $packs pack(s)"
        $null = Eval "closeMenu(); hideTip(); 'ok'"
    } else {
        Check 'pack More opens its menu' $false 'no installed pack row on the start screen'
    }

    # Cards through Personnel
    $cards = EvalJson "JSON.stringify({exports: Array.isArray(call('cards.exports')), installed: Array.isArray(call('cards.installed')), n: (call('cards.installed')||[]).length})"
    Check 'card lists answer' ($cards.exports -and $cards.installed) "installed cards: $($cards.n)"
} finally {
    try { $null = Eval "JSON.stringify(call('projects.delete', '$project'))"; $null = Eval "goStart(); 'ok'" }
    catch { Write-Warning "[test] deleting the throwaway project failed: $_" }
    if (-not $NoBoot) { StopSlotGame }
}

$failed = @($results | Where-Object Result -eq 'FAIL').Count
Write-Host ("[test] {0} of {1} checks passed" -f ($results.Count - $failed), $results.Count)
exit $(if ($failed -eq 0) { 0 } else { 1 })
