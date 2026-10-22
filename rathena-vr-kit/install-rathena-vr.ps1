<#
.SYNOPSIS
    Non-destructively installs Loki VR extensions onto an existing rAthena server directory.
    Never overwrites existing configuration values or database tables—only appends missing lines
    after creating `.bak` backups of `conf/import/*`.
#>
param(
    [Parameter(Mandatory = $true)][string]$RathenaDir
)
$ErrorActionPreference = 'Stop'
$RathenaDir = (Resolve-Path $RathenaDir).Path
$kit = $PSScriptRoot

Write-Host "==> Applying Loki VR extensions non-destructively to $RathenaDir..."
New-Item -ItemType Directory -Force (Join-Path $RathenaDir 'npc\custom') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $RathenaDir 'conf\import') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $RathenaDir 'db\import') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $RathenaDir 'sql-files') | Out-Null

foreach ($npc in @('loki_horde.txt', 'loki_codex.txt', 'loki_tutorial.txt')) {
    $src = Join-Path $kit "npc\custom\$npc"
    $dst = Join-Path $RathenaDir "npc\custom\$npc"
    if (Test-Path $src) {
        Copy-Item $src $dst -Force
        Write-Host "  Installed npc/custom/$npc"
    }
}

Copy-Item (Join-Path $kit 'sql-files\loki_vr_tables.sql') (Join-Path $RathenaDir 'sql-files\loki_vr_tables.sql') -Force
Write-Host "  Installed sql-files/loki_vr_tables.sql (run this SQL file once on your rAthena MySQL/MariaDB database)"

function Append-IfMissing([string]$file, [string]$matchRx, [string]$lineToAdd) {
    $content = if (Test-Path $file) { Get-Content -Raw -Encoding UTF8 $file } else { "" }
    if ($content -notmatch $matchRx) {
        if (Test-Path $file) { Copy-Item $file "$file.loki.bak" -Force }
        $nl = if ($content.Length -gt 0 -and -not $content.EndsWith("`n")) { "`r`n" } else { "" }
        Add-Content -Path $file -Value ($nl + $lineToAdd) -Encoding UTF8
        Write-Host "  Updated $file (+ $lineToAdd)"
    }
}

Append-IfMissing (Join-Path $RathenaDir 'conf\import\login_conf.txt') 'use_web_auth_token\s*:' 'use_web_auth_token: yes'
Append-IfMissing (Join-Path $RathenaDir 'conf\import\battle_conf.txt') 'area_size\s*:' 'area_size: 60'
Append-IfMissing (Join-Path $RathenaDir 'conf\import\map_conf.txt') 'loki_horde\.txt' 'npc: npc/custom/loki_horde.txt'
Append-IfMissing (Join-Path $RathenaDir 'conf\import\map_conf.txt') 'loki_codex\.txt' 'npc: npc/custom/loki_codex.txt'
Append-IfMissing (Join-Path $RathenaDir 'conf\import\map_conf.txt') 'loki_tutorial\.txt' 'npc: npc/custom/loki_tutorial.txt'

Write-Host "==> Done! Your existing rAthena files and database were preserved."
Write-Host "    Next step: Run LokiPoseRelay (port 7903) alongside your rAthena server, or manage your server via LokiHost."
