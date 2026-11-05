<#
.SYNOPSIS
  Vanilla NPCs as templates and replacements, end to end. Reports PASS or FAIL per check.

.DESCRIPTION
  Boots a test slot with this checkout's build and the given Personnel checkout (2.6+), loads a save so Personnel
  writes UserData/Personify/World/templates.json, goes back to the main menu and opens the editor from Side Hustle:
    - templates.list answers the town NPCs with role and block reason; Personnel knows replacements.
    - "From template" opens its dialog through a raycast click and lists the town NPCs.
    - A template copy is a new NPC: new name and id, the vanilla look in catalog paths, its role values, no target.
    - The wizard's template option adds a customer with the source's voice and a connection.
    - "Edit" on a town NPC makes a project whose NPC replaces it; routine and relations tabs are hidden.
    - Its install carries "replaces" and schemaVersion 4.
    - After a save load the town NPC wears the edited voice pitch and dealer cut (Personnel's replace probe).
  The throwaway projects and the installed pack are removed at the end and the game is stopped.
    pwsh -File <Personify checkout>/tests/templates-check.ps1 -Personnel _wt-Personnel-replace [-Slot mptest3] [-Profile main] [-NoBoot]
#>
[CmdletBinding()]
param(
    [ValidateSet('mptest', 'mptest2', 'mptest3', 'mptest4', 'mptest5', 'mptest6')]
    [string]$Slot = 'mptest3',
    [string]$Personnel = 'Personnel',
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    [int]$Save = 5,
    [string]$Target = 'benji_coleman',
    [switch]$NoBoot
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..").Path
$root = (Resolve-Path "$repo\..").Path
$checkout = Split-Path -Leaf $repo
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$call = Join-Path $PSScriptRoot 'bridge_call.py'
$app = 'personify-editor'
$copyName = 'Tpl Check Copy'
$editPack = $null
$script:installedPacks = @()

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

function ReadLogFrom([long]$offset) {
    $fs = [IO.File]::Open($script:log, 'Open', 'Read', 'ReadWrite')
    try { $fs.Seek($offset, 'Begin') | Out-Null; return (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
}

function Probe([string]$id) {
    $before = (Get-Item -LiteralPath $script:log).Length
    & python -I $s1 --slot $Slot run personnel replace probe $id *> $null
    if ($LASTEXITCODE -ne 0) { Write-Warning "[test] probe run exited $LASTEXITCODE" }
    $line = @((ReadLogFrom $before) -split "`r?`n" | Where-Object { $_ -match '\[replace-probe\].* found=' }) | Select-Object -Last 1
    Write-Host "  $($line.Trim())"
    return [string]$line
}
function Field([string]$text, [string]$key) { return [regex]::Match($text, "(?<= $key=)\S*").Value }

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
    # The shstart call can land while the menu scene is still coming up; retry until the editor answers.
    for ($i = 0; $i -lt 12; $i++) {
        $screen = Eval 'typeof ui === "object" ? ui.screen : "none"'
        if ($screen -eq 'start' -or $screen -eq 'editor') { return $screen }
        Write-Host "[test] editor not up yet ($screen), asking again"
        Start-Sleep -Seconds 2
        $answer = StartPersonify
    }
    Write-Host "[test] last shstart answer: $($answer -join ' | ')"
    if ($screen -ne 'start' -and $screen -ne 'editor') {
        Select-String -LiteralPath $script:log -Pattern 'SideHustle|Personify|Sideload' | Select-Object -Last 15 |
            ForEach-Object { Write-Host "[test] log: $($_.Line)" }
    }
    return $screen
}

$table = (& python -I $slotsPy --json table | Out-String | ConvertFrom-Json)
$script:gameRoot = [string]$table.$Slot.root
$script:log = Join-Path $script:gameRoot 'MelonLoader\Latest.log'
$templatesFile = Join-Path $script:gameRoot 'UserData\Personify\World\templates.json'
$packsDir = Join-Path $script:gameRoot 'UserData\Personnel\Packs'

try {
    if (-not $NoBoot) {
        # Profile main already carries Side Hustle like the real install and takes only store mods in -Mods. Its
        # Sideload is a release build without the probe that eval and click go through, so a Debug Sideload
        # replaces it; every other mod stays as installed.
        $builds = if ($Profile -eq 'main') { "$checkout,$Personnel,Sideload" } else { "$checkout,$Personnel" }
        $argv = @('-NoProfile', '-File', $session, '-Slot', $Slot, '-Profile', $Profile, '-Save', "$Save", '-NoApp',
                  '-Build', $builds)
        if ($Profile -eq 'stack') { $argv += @('-Mods', 'SideHustle') }
        Write-Host "[test] session.ps1 $($argv[3..($argv.Count - 1)] -join ' ')"
        & pwsh @argv
        if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed with exit code $LASTEXITCODE" }
    }
    Check 'save loaded' (WaitReady) "slot $Slot save $Save"

    # Personnel writes the town NPCs on save load.
    $tf = if (Test-Path -LiteralPath $templatesFile) { Get-Content -Raw -LiteralPath $templatesFile | ConvertFrom-Json } else { $null }
    $tpl = @($tf.npcs)
    $tTarget = $tpl | Where-Object id -eq $Target | Select-Object -First 1
    Check 'templates.json written on save load' ($tpl.Count -gt 30) "$($tpl.Count) NPC(s) in $templatesFile"
    Check "target $Target is a replaceable dealer" ($tTarget -and $tTarget.role -eq 'dealer' -and -not $tTarget.blocked) "role $($tTarget.role), blocked '$($tTarget.blocked)'"

    Check 'back to the main menu' (Bridge 'return_to_menu' '{}' 60000) 'return_to_menu'
    $screen = OpenEditor
    Check 'editor open' ($screen -eq 'start' -or $screen -eq 'editor') "ui.screen = $screen"
    $null = Eval "goStart(); 'ok'"

    $list = EvalJson "JSON.stringify((r => ({available: r.available, ready: r.personnelReady, n: r.npcs.length, target: r.npcs.find(x => x.id === '$Target'), nelson: r.npcs.find(x => x.id === 'uncle_nelson')}))(call('templates.list')))"
    Check 'templates.list answers the town NPCs' ($list.available -and $list.n -eq $tpl.Count) "$($list.n) NPC(s), available $($list.available)"
    Check 'Personnel knows replacements' ([bool]$list.ready) "personnelReady $($list.ready)"
    Check 'a story NPC is blocked' ([bool]$list.nelson.blocked) "uncle_nelson blocked '$($list.nelson.blocked)'"

    # The start screen button, through the raycast.
    $marked = Eval "(()=>{const b=Array.from(document.querySelectorAll('.wallh .btn')).find(x=>x.textContent.indexOf(t('From template'))>=0); if(!b) return 'missing'; b.classList.add('tplcheck'); return 'ok';})()"
    $clicked = ($marked -eq 'ok') -and (Click '.tplcheck')
    $rows = [int](Eval "document.querySelectorAll('.tlist .prow').length")
    Check 'From template opens the town list' ($clicked -and $rows -eq $tpl.Count) "clicked $clicked, $rows row(s)"
    $null = Eval "closeDialog(); 'ok'"

    # A template copy as a new NPC.
    $null = Eval "JSON.stringify(call('projects.delete', '$copyName'))"
    $made = EvalJson "JSON.stringify(call('templates.newProject', {id: '$Target', name: '$copyName'}))"
    Check 'template copy makes a project' ([bool]$made.ok) "folder $($made.folder)"
    $script:copyFolder = $made.folder
    $null = Eval "openProject('$($made.folder)'); 'ok'"
    $c = EvalJson "JSON.stringify((n => ({id: n.id, name: n.name, replaces: n.replaces || null, hair: (n.appearance||{}).hairPath || '', cut: (n.dealer||{}).cut, voice: (n.voice||{}).from || '', layers: ((n.appearance||{}).bodyLayers||[]).map(l => l.path)}))(npcs()[0]))"
    $vanCut = $tTarget.npc.dealer.cut
    Check 'copy is a new NPC with its own name' ($c.name -eq $copyName -and $c.id -ne $Target -and -not $c.replaces) "id $($c.id), name $($c.name), replaces $($c.replaces)"
    Check 'copy keeps the role values' ($c.cut -eq $vanCut) "cut $($c.cut) (vanilla $vanCut)"
    Check 'copy keeps the voice' ($c.voice -eq $Target -or $c.voice -eq $tTarget.npc.voice.from) "voice.from $($c.voice)"
    $paths = @($c.layers) + @($c.hair) | Where-Object { $_ }
    Check 'look pieces are catalog paths' ($paths.Count -gt 0 -and @($paths | Where-Object { $_ -notmatch '/' }).Count -eq 0) ($paths -join ', ')

    # The wizard's template option: a customer from a town customer.
    $cust = $tpl | Where-Object { $_.role -eq 'customer' -and -not $_.blocked } | Select-Object -First 1
    $w = EvalJson "JSON.stringify((ps => (ps && ps.ok !== false && applyState(ps) !== undefined) ? (n =>({ok: true, name: n.name, voice: (n.voice||{}).from || '', conn: ((n.relationships||{}).connections||[]).length, cust: !!n.customer}))(npcById(ps.added)) : ({ok: false, err: ps && ps.error}))(call('npc.add', {name: 'Kyla Check', role: 'customer', look: 'random', template: '$($cust.id)'})))"
    Check 'wizard template adds a customer' ($w.ok -and $w.name -eq 'Kyla Check' -and $w.cust) "from $($cust.id): $($w | ConvertTo-Json -Compress)"
    Check 'wizard copy has a voice and a connection' ($w.voice -and $w.conn -gt 0) "voice $($w.voice), connections $($w.conn)"
    $null = Eval "goStart(); 'ok'"

    # Edit a town NPC: a project whose NPC replaces it.
    $ed = EvalJson "JSON.stringify(call('templates.editVanilla', '$Target'))"
    Check 'Edit makes a replacing project' ([bool]$ed.ok) "folder $($ed.folder)"
    $editPack = $ed.folder
    $null = Eval "openProject('$editPack'); 'ok'"
    $e = EvalJson "JSON.stringify((n => ({name: n.name, replaces: n.replaces, tabs: Array.from(E.tabs.children).map(x => x.textContent), banner: !!E.fbody.querySelector('.cnote')}))(npcs()[0]))"
    Check 'replacement keeps the vanilla name and target' ($e.replaces -eq $Target -and $e.name -eq $tTarget.name) "name $($e.name), replaces $($e.replaces)"
    Check 'routine and relations tabs are hidden' (@($e.tabs).Count -eq 2) ($e.tabs -join ', ')
    Check 'editor shows the replacement banner' ([bool]$e.banner) "banner $($e.banner)"

    # Edit the voice pitch and the cut, then install.
    $null = Eval "setActive(npcs()[0].id); write([{path: 'voice.pitch', value: 1.37}, {path: 'dealer.cut', value: 0.45}]); 'ok'"
    $packsBefore = @(Get-ChildItem -LiteralPath $packsDir -Directory -ErrorAction SilentlyContinue | ForEach-Object Name)
    $inst = EvalJson "JSON.stringify(call('install'))"
    Check 'install succeeds' ($inst.ok -ne $false) "$($inst | ConvertTo-Json -Compress -Depth 4)"
    # Only the folders the install created count, and only those are removed at the end.
    $script:installedPacks = @(Get-ChildItem -LiteralPath $packsDir -Directory | Where-Object { $packsBefore -notcontains $_.Name })
    $man = $script:installedPacks | ForEach-Object { Join-Path $_.FullName 'manifest.json' } |
           Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { [pscustomobject]@{ Path = $_; Json = (Get-Content -Raw -LiteralPath $_ | ConvertFrom-Json) } } |
           Where-Object { @($_.Json.npcs | Where-Object replaces -eq $Target).Count -gt 0 } | Select-Object -First 1
    $mn = @($man.Json.npcs | Where-Object replaces -eq $Target)[0]
    Check 'installed manifest replaces the target, schema 4' ($man -and $man.Json.schemaVersion -eq 4) "$($man.Path), schema $($man.Json.schemaVersion)"
    Check 'installed manifest carries the edits' ($mn.voice.pitch -eq 1.37 -and $mn.dealer.cut -eq 0.45) "pitch $($mn.voice.pitch), cut $($mn.dealer.cut)"

    # Personnel reads packs at game start: restart on the same slot and save, then it applies the replacement.
    $null = Eval "goStart(); 'ok'"
    StopSlotGame
    & pwsh -NoProfile -File $session -Slot $Slot -Profile $Profile -Save "$Save" -NoApp -KeepSlot -Template none
    Check 'game restarts with the replacement' ($LASTEXITCODE -eq 0 -and (WaitReady)) "save $Save"
    $p = Probe $Target
    Check 'town NPC is replaced in the game' ((Field $p 'applied') -eq 'True') (Field $p 'applied')
    Check 'town NPC keeps its vanilla name' ((Field $p 'name') -like ($tTarget.name -replace ' ', '_')) (Field $p 'name')
    Check 'edited voice pitch applied' ([double](Field $p 'pitch') -eq 1.37) "pitch $(Field $p 'pitch')"
    Check 'edited dealer cut applied' ([double](Field $p 'cut') -eq 0.45) "cut $(Field $p 'cut')"
    $errs = @((ReadLogFrom 0) -split "`r?`n" | Where-Object { $_ -match '\[(Personify|Personnel)\].*(ERROR|\[E\]|Exception)' })
    Check 'no Personify or Personnel errors in the log' ($errs.Count -eq 0) "$($errs.Count) line(s): $(($errs | Select-Object -First 3) -join ' | ')"
} finally {
    # The template copy and the replacing project are test projects and leave with the test.
    foreach ($proj in @($script:copyFolder, $editPack) | Where-Object { $_ }) {
        $dir = Join-Path $script:gameRoot "UserData\Personify\Projects\$proj"
        if (-not (Test-Path -LiteralPath $dir)) { Write-Host "[test] project $proj is already gone"; continue }
        try { Remove-Item -LiteralPath $dir -Recurse -Force; Write-Host "[test] removed project $proj" }
        catch { Write-Warning "[test] removing project $proj failed: $_" }
    }
    if (-not $NoBoot) { try { StopSlotGame } catch { Write-Warning "[test] stopping the game failed: $_" } }
    # The slot is rebuilt from the stack on the next start; the installed test pack and projects go with it.
    if (-not $script:installedPacks) { Write-Host '[test] no installed test pack to remove' }
    foreach ($d in @($script:installedPacks)) {
        try { Remove-Item -LiteralPath $d.FullName -Recurse -Force; Write-Host "[test] removed pack $($d.Name)" }
        catch { Write-Warning "[test] removing pack $($d.Name) failed: $_" }
    }
}

$failed = @($results | Where-Object Result -eq 'FAIL').Count
Write-Host ("[test] {0} of {1} checks passed" -f ($results.Count - $failed), $results.Count)
Write-Host ("SUMMARY {0} PASS {1} FAIL" -f ($results.Count - $failed), $failed)
exit $(if ($failed -eq 0) { 0 } else { 1 })
