<#
.SYNOPSIS
  A card in "Cards in your game" can be remixed as a new project. Reports PASS or FAIL per check.

.DESCRIPTION
  Boots a test slot into the main menu with this checkout's build, Side Hustle, Personnel and NPC Cards, opens the
  editor, downloads one card from NPC Vault into Personnel's import folder and adds it to the game. Then it clicks
  the remix button of that card's row through Sideload's raycast and checks the new project:
    - The installed card's row has a remix button, and the click opens the editor.
    - The project holds one NPC with the card's name and an id of its own (not "card_<code>").
    - The NPC is marked as a remix of the card code, and its contact picture is a project file.
    - The log names the remix.
  The project and the card are removed at the end and the game is stopped. Run from anywhere:
    pwsh -File <Personify checkout>/tests/remix-installed.ps1 [-Slot mptest3] [-NoBoot] [-Code <card code>]
#>
[CmdletBinding()]
param(
    [ValidateSet('mptest', 'mptest2', 'mptest3', 'mptest4', 'mptest5', 'mptest6')]
    [string]$Slot = 'mptest3',
    [switch]$NoBoot,
    [string]$Code
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..").Path
$root = (Resolve-Path "$repo\..").Path
$checkout = Split-Path -Leaf $repo
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$app = 'personify-editor'
$vault = 'https://personify.doomods.com'

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

$folder = $null
$installed = $null
$import = $null
try {
    if (-not $NoBoot) {
        $argv = @('-NoProfile', '-File', $session, '-Slot', $Slot, '-Save', '0', '-NoApp', '-Build', $checkout, '-Mods', 'SideHustle,Personnel,NpcCards')
        Write-Host "[test] session.ps1 $($argv[3..($argv.Count - 1)] -join ' ')"
        & pwsh @argv
        if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed with exit code $LASTEXITCODE" }
    }

    $null = @(& python $s1 --slot $Slot run shstart doodesch.personify 2>&1)
    $screen = Eval 'ui.screen'
    Check 'editor open' ($screen -eq 'start' -or $screen -eq 'editor') "ui.screen = $screen"

    # One card from NPC Vault into the import folder, then into the game.
    if (-not $Code) { $Code = (Invoke-RestMethod "$vault/api/v1/cards?limit=1").cards[0].code }
    $import = [string](EvalJson "JSON.stringify(call('cards.folders'))").import
    New-Item -ItemType Directory -Force $import | Out-Null
    $file = Join-Path $import "remix-check-$Code.png"
    Invoke-WebRequest "$vault/c/$Code.png" -OutFile $file
    $esc = $file.Replace('\', '\\')
    $installed = EvalJson "JSON.stringify(call('cards.import', '$esc'))"
    Check 'card added to the game' ([bool]$installed.ok) "code $($installed.code), $($installed.name)"
    $name = [string]$installed.name

    # The card's row on the start screen, then its remix button.
    $null = Eval "goStart(); setStartTab('cards'); 'ok'"
    $mark = Eval "(() => { const r = Array.from(document.querySelectorAll('.krow')).find(r => (r.textContent||'').toUpperCase().includes('$($installed.code)'.toUpperCase())); if (!r) return 'no row'; const b = r.querySelectorAll('.ib'); if (b.length < 2) return b.length + ' button(s)'; b[0].id = 'remixcheck'; for (const s of Array.from(r.parentNode.children)) if (s !== r) s.style.display = 'none'; return 'marked'; })()"
    Check 'installed card row has a remix button' ($mark -eq 'marked') $mark
    $before = @((EvalJson "JSON.stringify(call('projects.list').map(p => p.folder))"))
    $clicked = $mark -eq 'marked' -and (HoverClick '#remixcheck')
    $screen = Eval 'ui.screen'
    Check 'remix button opens the editor' ($clicked -and $screen -eq 'editor') "clicked $clicked, ui.screen = $screen"
    $after = @((EvalJson "JSON.stringify(call('projects.list').map(p => p.folder))"))
    $folder = @($after | Where-Object { $before -notcontains $_ }) | Select-Object -First 1

    $npc = EvalJson "JSON.stringify((() => { const n = npcs(); return { count: n.length, id: n[0] && n[0].id, name: n[0] && n[0].name, portrait: n[0] && n[0].portrait, remixOf: n[0] && n[0].personify && n[0].personify.remixOf }; })())"
    Check 'one NPC with the card name' ($npc.count -eq 1 -and $npc.name -eq $name) "count $($npc.count), name '$($npc.name)'"
    Check 'NPC id of its own' ($npc.id -and $npc.id -ne "card_$($installed.code)") "id '$($npc.id)'"
    Check 'marked as remix of the card' ($npc.remixOf.code -eq $installed.code) "remixOf $($npc.remixOf.code)"
    Check 'contact picture is a project file' ($npc.portrait -like 'portraits/*') "portrait '$($npc.portrait)'"

    $log = (& python $s1 --slot $Slot log --grep "remix of installed card" 2>&1 | Out-String)
    Check 'log names the remix' ($log -match "remix of installed card $($installed.code)") ($log.Trim() -split "`n" | Select-Object -Last 1)
} finally {
    try {
        if ($folder) { $null = Eval "goStart(); JSON.stringify(call('projects.delete', '$folder'))" }
        # Personnel may already have taken the file from the import folder on its own, so the card goes either way.
        if ($installed -and $installed.code) { $null = Eval "JSON.stringify(call('cards.remove', '$($installed.code)'))" }
        if ($import) { Get-ChildItem $import -Recurse -Filter "remix-check-*.png" -ErrorAction SilentlyContinue | Remove-Item -Force }
        $null = Eval "goStart(); 'ok'"
    } catch { Write-Warning "[test] cleanup failed: $_" }
    if (-not $NoBoot) { StopSlotGame }
}

$failed = @($results | Where-Object Result -eq 'FAIL').Count
Write-Host ("[test] {0} of {1} checks passed" -f ($results.Count - $failed), $results.Count)
exit $(if ($failed -eq 0) { 0 } else { 1 })
