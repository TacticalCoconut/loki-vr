# Loki VR — Open-Source Ragnarok Online VR Experience (Client, rAthena VR Host & SpacetimeDB Server)

Loki VR brings **Ragnarok Online (Renewal)** into Virtual Reality (and desktop) with tabletop miniature views, first/third-person follow views, physical VR skill casting (Cast Wheel, Cast Pad, Skill Sling, Gesture Cast, Voice & Vision Aim), 3D job skill trees, Bestiary/Codex scanning & bounties, co-op **Horde Mode** (bullet-heaven survival), and shared VR presence (floating headsets, articulated hands, spatial voice chat, shared casting menus, and 3D air-pen drawings).

This repository is **100% server-agnostic and owner-agnostic**. You can connect the Loki VR client to your own server using either of the two included server backends:

1. **Option A — Modified rAthena Server (`tools/lokihost/` & `rathena-vr-kit/`)**:
   - Host a standard **rAthena** server augmented with the **Loki Host / PoseRelay** sidecar and non-destructive custom scripts (`loki_horde.txt`, `loki_codex.txt`, `loki_tutorial.txt`, and `loki_account_settings`).
   - Works both as a turnkey one-window manager (**Loki Host**) OR as a drop-in kit (`rathena-vr-kit/`) for an **existing rAthena server** without altering your existing accounts, characters, or custom NPCs.
2. **Option B — SpacetimeDB Server (`server/`)**:
   - Run the complete Ragnarok Online Renewal ruleset + VR Presence + Co-Op Horde Mode inside a single Rust **SpacetimeDB** module (self-hosted via Docker/CLI or on your own SpacetimeDB instance).

---

## Repository Structure

| Path | Description |
| :--- | :--- |
| [`LokiLauncher.exe`](LokiLauncher.exe) | **Pre-Compiled Standalone Launcher & Updater** — Ready to run out of the box (no Unity or compiling required). Detects your Ragnarok Online folder (`data.grf`), downloads/launches the VR client, and includes optional auto-update + one-click rollback. |
| [`client/`](client/) | **Loki VR Unity Project** (Unity 2022.3.22f1, OpenXR). Full client source code. Reads maps, sprites, and audio directly from the player's own Ragnarok Online installation (`data.grf` — no proprietary Gravity assets are included in this repo). |
| [`client/tools/launcher/`](client/tools/launcher/) | **`LokiLauncher.cs`** — Source code for `LokiLauncher.exe` (also pre-compiled at [`client/release/LokiLauncher.exe`](client/release/LokiLauncher.exe)). |
| [`tools/lokihost/`](tools/lokihost/) | **Loki Host & `LokiPoseRelay`** (.NET 8) — Turnkey GUI/CLI manager and TCP sidecar relay (port `7903`) that adds VR headsets, spatial voice, shared casting menus, 3D air-pen drawings, Horde Mode, and per-account settings/Bestiary sync to any rAthena server. |
| [`rathena-vr-kit/`](rathena-vr-kit/) | **Drop-In rAthena VR Kit** — SQL tables (`loki_vr_tables.sql`), custom NPC scripts, `conf/import` rules, and a non-destructive installer (`install-rathena-vr.ps1`) for existing rAthena servers. |
| [`server/`](server/) | **SpacetimeDB Rust Module** — Full port of rAthena Renewal + VR Presence + Horde Mode as a SpacetimeDB WASM module, including non-destructive player data cutover/backup (`server/src/cutover.rs`). |
| [`tools/update-loki-vr.ps1`](tools/update-loki-vr.ps1) | **Optional Non-Destructive Updater & Rollback Script** — Checks your configured GitHub repository, updates Loki VR while preserving all local configs and databases, and supports one-command rollback (`-Rollback`). |

---

## Quick Start

### 1. Download & Play (No Unity or Compiling Required)
You do **not** need to install Unity or compile `LokiLauncher.exe` to play:
1. Download **`Loki-VR.zip`** from the repository's **Releases** page and extract it to any folder (e.g., `C:\Games\LokiVR`), **OR** simply run the pre-compiled **`LokiLauncher.exe`** included directly in this repository.
2. Start your headset's PC VR runtime (Meta Quest Link / Air Link / Virtual Desktop / SteamVR) and open **`LokiLauncher.exe`**.
3. Select your local Ragnarok Online folder (containing `data.grf`) and click **Play Loki Online**.
4. On the VR Login Screen, click **Servers…** to pick or add your **rAthena** server (`host:port` + `presencePort`) or **SpacetimeDB** server (`ws://host:port/database`).

#### Building from Source (Optional — Only for Modders & Contributors)
If you modify the C# client source code in `client/` and want to build a custom `Loki.exe` or recompile `LokiLauncher.exe` yourself:
```powershell
Unity.exe -batchmode -projectPath client -executeMethod Loki.EditorTools.LokiSetup.ConfigureAll -quit
Unity.exe -batchmode -projectPath client -executeMethod Loki.EditorTools.LokiSetup.BuildWindows -lokiBuildOut C:\LokiVR\Build -lokiRelease -quit
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /optimize+ /r:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,System.IO.Compression.dll,System.IO.Compression.FileSystem.dll /out:C:\LokiVR\Build\LokiLauncher.exe client\tools\launcher\LokiLauncher.cs
```

---

### 2. Hosting Option A: Modified rAthena Server

#### Method 1: Using the Turnkey `Loki Host` GUI (`tools/lokihost/`)
*(Pre-compiled sidecar binaries are also available in `LokiHost-VR-Sidecar.zip` on the Releases page.)*
```powershell
dotnet run -c Release --project tools/lokihost/App
```
- Open the **Setup** tab, point it to your compiled rAthena folder (`login-server.exe`, `char-server.exe`, `map-server.exe`) and your Ragnarok Online folder (`data.grf`), choose either **Private MariaDB** (zero-config) or your existing MariaDB/MySQL server, and click **Set up**.
- Open the **Server** tab and click **Start server**. Forward TCP ports **7900–7903** (`7900` login, `7901` char, `7902` map, `7903` VR headsets/voice/Horde relay).

#### Method 2: Adding Loki VR to an Existing rAthena Server (`rathena-vr-kit/`)
If you already run an active rAthena server and want to keep your existing database, accounts, and scripts untouched:
1. Run the non-destructive kit installer:
   ```powershell
   powershell -ExecutionPolicy Bypass -File rathena-vr-kit\install-rathena-vr.ps1 -RathenaDir C:\path\to\your\rathena
   ```
2. Import `rathena-vr-kit/sql-files/loki_vr_tables.sql` into your rAthena MySQL/MariaDB database (adds `loki_account_settings` and `loki_horde_reward` using `CREATE TABLE IF NOT EXISTS`).
3. Run the standalone VR Pose/Voice/Horde relay alongside your rAthena server:
   ```powershell
   dotnet run -c Release --project tools/lokihost/Relay -- --port 7903 --horde --accounts-db "Server=127.0.0.1;Port=3306;Database=ragnarok;User ID=ragnarok;Password=ragnarok" --relations-db "Server=127.0.0.1;Port=3306;Database=ragnarok;User ID=ragnarok;Password=ragnarok"
   ```

---

### 3. Hosting Option B: SpacetimeDB Server (`server/`)

You can self-host SpacetimeDB 24/7 on any machine or flat-rate VPS via Docker:
```bash
docker run -d --restart unless-stopped -p 3000:3000 -v /var/lib/loki-stdb:/stdb clockworklabs/spacetime start --data-dir /stdb
```
Then build and publish the `server/` module to your server:
```bash
cd server
spacetime publish --server http://127.0.0.1:3000 loki
```
*(Note: Never pass `--delete-data` when updating an existing server so all player accounts, characters, and Bestiary/Codex settings are preserved.)*

---

### 4. Optional GitHub Auto-Updates & One-Click Rollback

Auto-updating is **disabled by default** (`"autoUpdate": false` in `version.json`) and includes built-in **Rollback** protection so you can always revert an update if needed:
- **Client (`LokiLauncher.exe`)**:
  - Toggle **Auto-Update: ON / OFF** directly in the bottom bar of `LokiLauncher.exe`.
  - Before applying any update, `LokiLauncher.exe` automatically creates a full rollback snapshot of your previous version in `Loki\_rollback_backup` while preserving your `loki.json` (server list, RO path) and `loki-user.json` (account settings, VR comfort preferences, saved servers).
  - **One-Click Rollback**: After an update, a **`Rollback to vX`** / **`Rollback (vX)`** button appears in `LokiLauncher.exe` (or run `LokiLauncher.exe --rollback`). Clicking it restores your exact previous game files, keeps all of your settings intact, and automatically switches `Auto-Update` to **OFF** so the rolled-back version stays pinned until you re-enable updates.
- **Server (`tools/update-loki-vr.ps1` & `LokiHost`)**:
  - Check for updates without applying:
    ```powershell
    powershell -ExecutionPolicy Bypass -File tools\update-loki-vr.ps1 -CheckOnly
    ```
  - Apply updates safely (automatically creates a timestamped rollback snapshot in `backups\pre-update-*` and preserves all rAthena/SpacetimeDB databases and local configuration files):
    ```powershell
    powershell -ExecutionPolicy Bypass -File tools\update-loki-vr.ps1
    ```
  - **Roll back the latest server/repo update** (restores the pre-update git commit, configuration files, and optional SpacetimeDB module):
    ```powershell
    powershell -ExecutionPolicy Bypass -File tools\update-loki-vr.ps1 -Rollback
    ```

---

## License

GNU General Public License v3.0 or later (`LICENSE`). Third-party libraries retain their respective licenses (`THIRD_PARTY.md`). No proprietary Gravity Corporation game data (`.grf`, `.spr`, `.act`, `.gat`, `.gnd`, `.rsw`, `.rsm`, audio) is included in this repository.
