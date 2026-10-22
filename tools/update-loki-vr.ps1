<#
.SYNOPSIS
    Optional, non-destructive GitHub updater and rollback tool for Loki VR (Client, Modified rAthena / LokiHost, and SpacetimeDB).
    Fetches the latest release or branch from a configured GitHub repository, backs up all existing user/server
    configurations, commit state, and WASM modules, and updates only the Loki VR binaries/code without jeopardizing
    existing server data. Supports one-command rollback via `-Rollback`.

.PARAMETER GitHubRepo
    The GitHub repository ("owner/repo") to check for updates. If omitted, reads `githubRepo` from `version.json`
    or auto-detects from `git remote get-url origin`.

.PARAMETER CheckOnly
    Only check if a new version is available without applying it (optional update workflow).

.PARAMETER Rollback
    Rolls back the most recent update (or the snapshot specified by `-BackupDir`), restoring the previous git commit,
    all backed-up configuration files, and (if `-UpdateSpacetimeDb` is set) the previous SpacetimeDB WASM module.

.PARAMETER BackupDir
    Optional path to a specific `backups\pre-update-*` directory when running `-Rollback`.

.PARAMETER UpdateSpacetimeDb
    If set, also publishes the SpacetimeDB module to your local/configured SpacetimeDB server
    WITHOUT `--delete-data` (preserving 100% of existing tables and player progress).
#>
param(
    [string]$GitHubRepo = "",
    [switch]$CheckOnly,
    [switch]$Rollback,
    [string]$BackupDir = "",
    [switch]$UpdateSpacetimeDb,
    [string]$SpacetimeServer = "http://127.0.0.1:3000",
    [string]$SpacetimeDatabase = "loki"
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# Protected user/server configuration files that must NEVER be overwritten
$protectedFiles = @(
    'client\Assets\StreamingAssets\loki.json',
    'client\release\version.json',
    'tools\lokihost\lokihost.json'
)

if ($Rollback) {
    $backupsRoot = Join-Path $root 'backups'
    if ($BackupDir -eq '') {
        if (-not (Test-Path $backupsRoot)) {
            throw "[Loki VR Rollback] No backups directory found at $backupsRoot."
        }
        $latest = Get-ChildItem -Path $backupsRoot -Directory -Filter 'pre-update-*' |
            Where-Object { Test-Path (Join-Path $_.FullName 'backup-meta.json') } |
            Sort-Object Name -Descending |
            Select-Object -First 1
        if (-not $latest) {
            throw "[Loki VR Rollback] No rollback snapshot containing backup-meta.json found in $backupsRoot."
        }
        $BackupDir = $latest.FullName
    }

    $metaPath = Join-Path $BackupDir 'backup-meta.json'
    if (-not (Test-Path $metaPath)) {
        throw "[Loki VR Rollback] backup-meta.json not found in $BackupDir."
    }

    $meta = Get-Content -Raw -Encoding UTF8 $metaPath | ConvertFrom-Json
    $prevSha = [string]$meta.previousCommit
    if (-not $prevSha) {
        throw "[Loki VR Rollback] previousCommit is missing in $metaPath."
    }

    Write-Host "==> Rolling back Loki VR to pre-update commit $prevSha (from snapshot $BackupDir)..."
    git -C $root reset --hard $prevSha

    foreach ($rel in $protectedFiles) {
        $bak = Join-Path $BackupDir $rel
        $full = Join-Path $root $rel
        if (Test-Path $bak) {
            New-Item -ItemType Directory -Force (Split-Path $full -Parent) | Out-Null
            Copy-Item $bak $full -Force
        }
    }
    Write-Host "==> Restored backed-up configuration files."

    if ($UpdateSpacetimeDb) {
        Write-Host "==> Rolling back SpacetimeDB module ($SpacetimeDatabase on $SpacetimeServer) WITHOUT deleting player data..."
        $bakWasm = Join-Path $BackupDir 'loki_server.wasm'
        if (Test-Path $bakWasm) {
            spacetime publish --server $SpacetimeServer --bin-path $bakWasm $SpacetimeDatabase -y
        } else {
            Push-Location (Join-Path $root 'server')
            try {
                spacetime publish --server $SpacetimeServer $SpacetimeDatabase -y
            } finally {
                Pop-Location
            }
        }
    }

    Remove-Item -Recurse -Force $BackupDir
    Write-Host "==> Rollback to $prevSha complete! All server databases and configurations were preserved."
    exit 0
}

if ($GitHubRepo -eq '') {
    $verFile = Join-Path $root 'client\release\version.json'
    if (Test-Path $verFile) {
        $v = Get-Content -Raw -Encoding UTF8 $verFile | ConvertFrom-Json
        if ($v.githubRepo) { $GitHubRepo = [string]$v.githubRepo }
    }
}

if ($GitHubRepo -eq '') {
    try {
        $originUrl = (git -C $root config --get remote.origin.url 2>$null)
        if ($originUrl -match 'github\.com[:/]([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(\.git)?$') {
            $GitHubRepo = $Matches[1]
        }
    } catch { }
}

if ($GitHubRepo -eq '') {
    Write-Host "[Loki VR Updater] No GitHubRepo configured. Pass -GitHubRepo 'owner/repo' or set 'githubRepo' in version.json."
    exit 0
}

Write-Host "==> Checking GitHub ($GitHubRepo) for Loki VR updates..."
git -C $root fetch "https://github.com/$GitHubRepo.git" main
$localSha = (git -C $root rev-parse HEAD).Trim()
$remoteSha = (git -C $root rev-parse FETCH_HEAD).Trim()

if ($CheckOnly) {
    if ($localSha -eq $remoteSha) {
        Write-Host "==> Already up to date ($localSha)."
    } else {
        Write-Host "==> Update available: $localSha -> $remoteSha. Run without -CheckOnly to apply safely."
    }
    exit 0
}

if ($localSha -eq $remoteSha) {
    Write-Host "==> Already up to date ($localSha)."
    exit 0
}

$snapDir = Join-Path $root ("backups\pre-update-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force $snapDir | Out-Null

foreach ($rel in $protectedFiles) {
    $full = Join-Path $root $rel
    if (Test-Path $full) {
        $dst = Join-Path $snapDir $rel
        New-Item -ItemType Directory -Force (Split-Path $dst -Parent) | Out-Null
        Copy-Item $full $dst -Force
    }
}

$wasmPath = Join-Path $root 'server\target\wasm32-unknown-unknown\release\loki_server.wasm'
if (Test-Path $wasmPath) {
    Copy-Item $wasmPath (Join-Path $snapDir 'loki_server.wasm') -Force
}

@{
    previousCommit = $localSha
    updatedToCommit = $remoteSha
    githubRepo = $GitHubRepo
    timestampUtc = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json | Set-Content -Path (Join-Path $snapDir 'backup-meta.json') -Encoding UTF8

Write-Host "==> Created rollback point at $snapDir (previous commit: $localSha)"

git -C $root merge --ff-only FETCH_HEAD

foreach ($rel in $protectedFiles) {
    $bak = Join-Path $snapDir $rel
    $full = Join-Path $root $rel
    if (Test-Path $bak) {
        Copy-Item $bak $full -Force
    }
}
Write-Host "==> Restored protected local configuration files."

if ($UpdateSpacetimeDb) {
    Write-Host "==> Updating SpacetimeDB module ($SpacetimeDatabase on $SpacetimeServer) WITHOUT deleting player data..."
    Push-Location (Join-Path $root 'server')
    try {
        spacetime publish --server $SpacetimeServer $SpacetimeDatabase -y
    } finally {
        Pop-Location
    }
}

Write-Host "==> Loki VR update complete ($localSha -> $remoteSha)! To roll back this update at any time, run: powershell -ExecutionPolicy Bypass -File tools\update-loki-vr.ps1 -Rollback"
