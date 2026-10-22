# Client wave 5 (w5friends): the friends' package — a RELEASE build of Loki (LokiSetup.BuildWindows) plus the guides
# (LEIA-ME.txt in Brazilian Portuguese, READ-ME.txt in English), THIRD_PARTY.md, the GPL-3.0 text (LICENSE.txt) and the source
# (Loki-source.zip, made by make_source_package.py from the same commit: the design's decision 2026-10-01, Loki is GPL-3.0), zipped into
# one new file.
# It never uploads or shares anything (publishing is the design's call), never overwrites (a zip of the same name stops it) and deletes
# nothing. Run from PowerShell:
#   python client\release\make_source_package.py <sha> C:\LokiVR\release\Loki-source-<sha>.zip <private-patterns file outside the repo>
#   powershell -NoProfile -File client\release\make-friends-package.ps1 -Build C:\LokiVR\client-build\<agent>-release -Commit <sha>
#     -Source C:\LokiVR\release\Loki-source-<sha>.zip [-Out C:\LokiVR\release] [-Name Loki-VR-2026-09-30] [-Server "Loki=203.0.113.7:7900"]
# Checks before zipping (it stops on any): the build has none of the tables the client makes from the player's GRF
# (Data/ClientDataBuilder.cs GeneratedFiles, read from the source next to this script) nor Gravity's music, no file of an RO data
# type anywhere, the first-run parts present, no smoke code in Assembly-CSharp.dll (the UTF-16 strings "-lokiSmoke" and
# "[Smoke]", as Editor/LokiRelease.cs CheckSmokeCode counts them), and a loki.json without an account, character, startup
# commands or debug walk.
# The package's loki.json is the build's with the Loki Online (SpacetimeDB) entry left out — that server is paused and pointed at
# the friend's own PC — and, with -Server, one entry added (by default none: the host's address changes, and a zip should not
# carry it; friends add the server in game, as the guides say). Next to the zip: <Name>.zip.sha256.txt and <Name>.files.txt.
# Wave 28 (wtextc; design note 2026-10-15: "Include the text pack in the friends' package"): -TextPack <folder> puts the text pack (the
# overhaul's game texts, read at run time: Assets/Loki/Runtime/Data/TextPack.cs) beside the game as Loki\textpack\, after checking
# it: its textpack.json lists every file with its SHA-256 and size, every listed file is there and matches, nothing else is in the
# folder, no file of an RO data type. The package's loki.json then says "textPack": "textpack" (the folder next to Loki.exe).
param(
    [Parameter(Mandatory = $true)][string]$Build,
    [string]$Out = 'C:\LokiVR\release',
    [string]$Name = 'Loki-VR',
    [string]$Commit = '',
    [string]$Server = '',
    [string]$TextPack = '',
    [int]$Version = 0,
    [string]$DownloadUrl = '',
    [string]$ManifestUrl = '',
    [switch]$NoOverwrite,
    [Parameter(Mandatory = $true)][string]$Source
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
if ($Name -eq '') { $Name = 'Loki-VR' }
if ($Commit -eq '') { $Commit = '(commit not given)' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$verFile = Join-Path $PSScriptRoot 'version.json'
if ($Version -le 0) {
    if (Test-Path $verFile) {
        $prevVer = Get-Content -Raw -Encoding UTF8 $verFile | ConvertFrom-Json
        $Version = [int]$prevVer.version
        if ($Version -le 0) { $Version = 1 }
    } else {
        $Version = 1
    }
}
$Build = (Resolve-Path $Build).Path
$zipPath = Join-Path $Out "$Name.zip"
function Stop-With([string]$why) { Write-Output "STOPPED: $why"; exit 1 }
# the source (GPL-3.0): a zip of make_source_package.py, from the commit the build was made from
if (-not (Test-Path $Source)) { Stop-With "no source zip $Source (make_source_package.py)" }
$Source = (Resolve-Path $Source).Path
$srcReadme = ''
$sz = [IO.Compression.ZipFile]::OpenRead($Source)
try { $e = $sz.GetEntry('Loki-source/SOURCE-README.md'); if ($e) { $rd = New-Object IO.StreamReader($e.Open()); $srcReadme = $rd.ReadToEnd(); $rd.Close() } }
finally { $sz.Dispose() }
if ($srcReadme -eq '') { Stop-With "$Source has no Loki-source/SOURCE-README.md: not a make_source_package.py zip" }
if ($Commit -ne '(commit not given)' -and $srcReadme -notmatch ('commit ' + [regex]::Escape($Commit))) { Stop-With "the source zip is not from commit $Commit" }

# ---- 1. the build is a release build
if (-not (Test-Path (Join-Path $Build 'Loki.exe'))) { Stop-With "no Loki.exe in $Build" }
$launcherSrc = Join-Path $repo 'client\tools\launcher\LokiLauncher.cs'
$launcherExe = Join-Path $Build 'LokiLauncher.exe'
if (Test-Path $launcherSrc) {
    $csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path $csc)) { $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' }
    if (-not (Test-Path $csc)) { Stop-With "no .NET Framework csc.exe found to build LokiLauncher.exe" }
    & $csc /nologo /target:winexe /optimize+ /r:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,System.IO.Compression.dll,System.IO.Compression.FileSystem.dll "/out:$launcherExe" $launcherSrc
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $launcherExe)) { Stop-With "failed to compile LokiLauncher.exe" }
}
$sa = Join-Path $Build 'Loki_Data\StreamingAssets'
$builder = Get-Content -Raw (Join-Path $repo 'client\Assets\Loki\Runtime\Data\ClientDataBuilder.cs')
$block = [regex]::Match($builder, 'GeneratedFiles\s*=\s*\{(.*?)\};', 'Singleline').Groups[1].Value
$generated = @([regex]::Matches($block, '"([^"]+)"') | ForEach-Object { 'data/' + $_.Groups[1].Value }) + @('bgm/prontera_classic.ogg')
if ($generated.Count -lt 10) { Stop-With "could not read ClientDataBuilder.GeneratedFiles ($($generated.Count) names)" }
$shipped = @($generated | Where-Object { Test-Path (Join-Path $sa $_) })
if ($shipped.Count -gt 0) { Stop-With "not a release build: it carries $($shipped -join ', ')" }
$roTypes = '.grf', '.gpf', '.thor', '.spr', '.act', '.gat', '.gnd', '.rsw', '.rsm', '.rsm2', '.gr2', '.str', '.pal', '.bmp', '.tga', '.wav', '.mp3', '.ogg', '.lub', '.lua', '.imf', '.ebm'
$buildTextPackDir = Join-Path $Build 'textpack\'
$files = @(Get-ChildItem $Build -Recurse -File | Where-Object { -not $_.FullName.StartsWith($buildTextPackDir, [StringComparison]::OrdinalIgnoreCase) })
$roFiles = @($files | Where-Object { $roTypes -contains $_.Extension.ToLowerInvariant() })
if ($roFiles.Count -gt 0) { Stop-With "files of an RO data type: $(($roFiles | ForEach-Object { $_.FullName.Substring($Build.Length + 1) }) -join ', ')" }
if (-not (Test-Path (Join-Path $sa 'data\firstrun\items_rathena.tsv'))) { Stop-With 'the first-run parts (StreamingAssets\data\firstrun) are missing' }
$dll = [IO.File]::ReadAllBytes((Join-Path $Build 'Loki_Data\Managed\Assembly-CSharp.dll'))
$latin1 = [Text.Encoding]::GetEncoding(28591) # one char per byte: a byte search as a string search
$hay = $latin1.GetString($dll)
function Count-Utf16([string]$s) {
    $needle = $latin1.GetString([Text.Encoding]::Unicode.GetBytes($s)); $n = 0; $i = 0
    while (($i = $hay.IndexOf($needle, $i, [StringComparison]::Ordinal)) -ge 0) { $n++; $i++ }
    return $n
}
$smoke = (Count-Utf16 '-lokiSmoke') + (Count-Utf16 '[Smoke]')
if ($smoke -gt 0) { Stop-With "smoke code in Assembly-CSharp.dll ($smoke strings): a development build" }
$cfgPath = Join-Path $sa 'loki.json'
$cfg = Get-Content -Raw -Encoding UTF8 $cfgPath | ConvertFrom-Json
if ($cfg.username -or $cfg.password -or $cfg.charName -or $cfg.debugAutoWalk -or @($cfg.startupCommands).Count -gt 0) { Stop-With "loki.json carries an account / character / startup commands / debug walk" }

# ---- 1b. the text pack (wave 28), checked before anything is zipped
if ($TextPack -eq '') {
    foreach ($cand in @(
        (Join-Path $Build 'textpack'),
        'C:\LokiVR\client-build\main\textpack',
        'C:\LokiVR\client-build\play\textpack',
        (Join-Path $repo 'client\Assets\StreamingAssets\textpack')
    )) {
        if (Test-Path (Join-Path $cand 'textpack.json')) { $TextPack = $cand; break }
    }
}
if ($TextPack -eq '') { Stop-With "no textpack found: pass -TextPack <folder> or place textpack in $Build\textpack" }
$packFiles = @(); $packSha = [Security.Cryptography.SHA256]::Create()
if ($TextPack -ne '') {
    if (-not (Test-Path (Join-Path $TextPack 'textpack.json'))) { Stop-With "-TextPack $TextPack has no textpack.json" }
    $TextPack = (Resolve-Path $TextPack).Path
    $man = Get-Content -Raw -Encoding UTF8 (Join-Path $TextPack 'textpack.json') | ConvertFrom-Json
    if ($man.format -ne 1) { Stop-With "the text pack's manifest is format $($man.format), not 1" }
    $listed = @{}
    foreach ($f in @($man.files)) {
        $fp = Join-Path $TextPack ($f.path -replace '/', '\')
        if (-not (Test-Path $fp)) { Stop-With "the text pack lists $($f.path), which is missing" }
        $h = ([BitConverter]::ToString($packSha.ComputeHash([IO.File]::ReadAllBytes($fp))) -replace '-', '').ToLowerInvariant()
        if ($h -ne $f.sha256 -or (Get-Item $fp).Length -ne [long]$f.bytes) { Stop-With "the text pack's $($f.path) is not the manifest's (sha256 $h)" }
        $listed[$fp.ToLowerInvariant()] = $f.path
    }
    foreach ($x in @(Get-ChildItem $TextPack -Recurse -File)) {
        if ($roTypes -contains $x.Extension.ToLowerInvariant()) { Stop-With "the text pack holds a file of an RO data type: $($x.Name)" }
        if ($x.Name -eq 'textpack.json' -and $x.DirectoryName -eq $TextPack) { continue }
        if (-not $listed.ContainsKey($x.FullName.ToLowerInvariant())) { Stop-With "the text pack holds $($x.FullName.Substring($TextPack.Length + 1)), which its manifest does not list" }
    }
    $packFiles = @(Get-ChildItem $TextPack -Recurse -File)
}
$textPackCfg = $(if ($TextPack -ne '') { 'textpack' } elseif ($cfg.PSObject.Properties.Name -contains 'textPack') { [string]$cfg.textPack } else { 'textpack' })

# ---- 2. the package's loki.json (written field by field: the same keys, the Loki Online entry out, -Server in)
function J([object]$v) {
    if ($v -is [bool]) { return $(if ($v) { 'true' } else { 'false' }) }
    if ($v -is [int] -or $v -is [long]) { return "$v" }
    $s = [string]$v; $sb = New-Object Text.StringBuilder; [void]$sb.Append('"')
    foreach ($c in $s.ToCharArray()) {
        switch ($c) { '"' { [void]$sb.Append('\"') } '\' { [void]$sb.Append('\\') } default { if ([int]$c -lt 0x20) { [void]$sb.Append(('\u{0:x4}' -f [int]$c)) } else { [void]$sb.Append($c) } } }
    }
    [void]$sb.Append('"'); return $sb.ToString()
}
$entries = @(
    '    { "name": "Loki Host (rAthena + VR Relay)", "host": "127.0.0.1", "port": 7900, "kind": "rathena", "database": "loki", "scheme": "", "presencePort": 7903 }',
    '    { "name": "SpacetimeDB (Local)", "host": "127.0.0.1", "port": 3000, "kind": "spacetimedb", "database": "loki", "scheme": "ws", "presencePort": 0 }'
)
if ($Server -ne '') {
    $m = [regex]::Match($Server, '^(?<n>[^=]+)=(?<h>[^:]+):(?<p>\d+)(:(?<pp>\d+))?$')
    if (-not $m.Success) { Stop-With "-Server must be Name=host:port or Name=host:port:presencePort, not '$Server'" }
    $pp = $(if ($m.Groups['pp'].Success) { [int]$m.Groups['pp'].Value } else { 0 })
    $entries = @("    { ""name"": $(J $m.Groups['n'].Value), ""host"": $(J $m.Groups['h'].Value), ""port"": $([int]$m.Groups['p'].Value), ""kind"": ""spacetimedb"", ""database"": ""loki"", ""scheme"": """", ""presencePort"": $pp }")
}
$grfs = @($cfg.grfs | ForEach-Object { J $_ }) -join ', '
$json = @"
{
  "clientRoot": $(J $cfg.clientRoot),
  "grfs": [ $grfs ],
  "startMap": $(J $cfg.startMap),
  "loginHost": $(J $cfg.loginHost),
  "loginPort": $([int]$cfg.loginPort),
  "username": "",
  "password": "",
  "charName": "",
  "debugAutoWalk": false,
  "startupCommands": [],
  "bgmOverrides": [],
  "textLanguage": $(J $cfg.textLanguage),
  "textPack": $(J $textPackCfg),
  "servers": [
$($entries -join ",`r`n")
  ],
  "hashedLogin": $(J ([bool]$cfg.hashedLogin))
}
"@
$back = $json | ConvertFrom-Json
if (@($back.servers).Count -ne $entries.Count -or $back.username -ne '' -or $back.clientRoot -ne $cfg.clientRoot) { Stop-With 'the package loki.json did not read back as written' }

# ---- 3. the zip: Loki\ (the build), LEIA-ME.txt, READ-ME.txt, THIRD_PARTY.md
New-Item -ItemType Directory -Force $Out | Out-Null
if (Test-Path $zipPath) {
    if ($NoOverwrite) { Stop-With "$zipPath exists already (-NoOverwrite set)" }
    Remove-Item -Force $zipPath
}
$utf8Bom = New-Object Text.UTF8Encoding($true)
$utf8NoBom = New-Object Text.UTF8Encoding($false)
function Guide([string]$file) {
    $t = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $file), [Text.Encoding]::UTF8)
    $t = $t.Replace('{{PACKAGE}}', $Name).Replace('{{COMMIT}}', $Commit).Replace("`r`n", "`n").Replace("`n", "`r`n")
    return $utf8Bom.GetBytes($t)
}
$embeddedVersionJson = @"
{
  "version": $Version,
  "commit": $(J $Commit),
  "sha256": "",
  "sizeBytes": 0,
  "downloadUrl": $(J $DownloadUrl),
  "manifestUrl": $(J $ManifestUrl)
}
"@
$extra = [ordered]@{
    'LEIA-ME.txt' = (Guide 'LEIA-ME.txt')
    'READ-ME.txt' = (Guide 'READ-ME.txt')
    'THIRD_PARTY.md' = [IO.File]::ReadAllBytes((Join-Path $repo 'THIRD_PARTY.md'))
    'LICENSE.txt' = [IO.File]::ReadAllBytes((Join-Path $repo 'LICENSE'))
    'Loki-source.zip' = [IO.File]::ReadAllBytes($Source)
    'Loki/Loki_Data/StreamingAssets/loki.json' = $utf8Bom.GetBytes($json.Replace("`r`n", "`n").Replace("`n", "`r`n"))
    'Loki/Loki_Data/StreamingAssets/version.json' = $utf8NoBom.GetBytes($embeddedVersionJson.Replace("`r`n", "`n").Replace("`n", "`r`n"))
}
if (Test-Path $launcherExe) {
    $extra['LokiLauncher.exe'] = [IO.File]::ReadAllBytes($launcherExe)
}
$list = New-Object Collections.Generic.List[string]
$sha = [Security.Cryptography.SHA256]::Create()
$zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    foreach ($f in $files) {
        $rel = 'Loki/' + $f.FullName.Substring($Build.Length + 1).Replace('\', '/')
        if ($extra.Contains($rel)) { continue }
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, $rel, 'Optimal')
        $list.Add(("{0}`t{1}`t{2}" -f $rel, $f.Length, ([BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($f.FullName))) -replace '-', '').ToLowerInvariant()))
    }
    foreach ($f in $packFiles) {  # wave 28: the text pack beside the game (Loki\textpack\)
        $rel = 'Loki/textpack/' + $f.FullName.Substring($TextPack.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, $rel, 'Optimal')
        $list.Add(("{0}`t{1}`t{2}" -f $rel, $f.Length, ([BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($f.FullName))) -replace '-', '').ToLowerInvariant()))
    }
    foreach ($k in $extra.Keys) {
        $e = $zip.CreateEntry($k, 'Optimal'); $s = $e.Open(); $s.Write($extra[$k], 0, $extra[$k].Length); $s.Close()
        $list.Add(("{0}`t{1}`t{2}" -f $k, $extra[$k].Length, ([BitConverter]::ToString($sha.ComputeHash($extra[$k])) -replace '-', '').ToLowerInvariant()))
    }
}
finally { $zip.Dispose() }
$zipLen = (Get-Item $zipPath).Length
$zipSha = ([BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($zipPath))) -replace '-', '').ToLowerInvariant()
$finalVersionJson = @"
{
  "version": $Version,
  "commit": $(J $Commit),
  "sha256": $(J $zipSha),
  "sizeBytes": $zipLen,
  "downloadUrl": $(J $DownloadUrl),
  "manifestUrl": $(J $ManifestUrl)
}
"@
[IO.File]::WriteAllText($verFile, $finalVersionJson.Replace("`r`n", "`n").Replace("`n", "`r`n"), $utf8NoBom)
[IO.File]::WriteAllText((Join-Path $Out 'version.json'), $finalVersionJson.Replace("`r`n", "`n").Replace("`n", "`r`n"), $utf8NoBom)
[IO.File]::WriteAllText((Join-Path $Out "$Name.zip.sha256.txt"), "$zipSha  $Name.zip`r`n")
[IO.File]::WriteAllLines((Join-Path $Out "$Name.files.txt"), $list)
Write-Output ("package {0} (v{5}): {1} entries, {2:N1} MB zipped ({3:N1} MB of files), sha256 {4}" -f $zipPath, $list.Count, ($zipLen / 1MB), (($files | Measure-Object Length -Sum).Sum / 1MB), $zipSha, $Version)
Write-Output ("checks: GRF-derived tables / Gravity music in the build 0 of {0}; RO-type files 0; first-run parts present; smoke strings 0; loki.json without account" -f $generated.Count)
Write-Output ("text pack: {0}" -f $(if ($TextPack -ne '') { "$($packFiles.Count) files from $TextPack in Loki\textpack\ ('$($man.name)' $($man.version)); loki.json textPack 'textpack'" } else { "none (-TextPack not given); loki.json textPack '$textPackCfg'" }))
Write-Output ("loki.json: servers {0}" -f ($entries -join ', '))
