# Loki Host

One window to host rAthena for friends over the internet: set up a separate copy of your rAthena build with its own
database, start and stop it cleanly, test the forwarded ports, manage accounts, and copy what friends need.

## Use
1. **Setup tab.** The folder of your built rAthena (with `login-server.exe`, `char-server.exe`, `map-server.exe`), an
   empty folder for the host's copy (default `C:\LokiHost\rathena`), your MariaDB / MySQL admin login (used once,
   never saved), and **your Ragnarok Online folder** (the one with `data.grf`: only read; see "Game data updates").
   Press **Set up**.
2. **Ports tab.** Forward TCP **7900–7903** on your router to the PC address shown (7903: the players' headsets). Press **Add Windows Firewall rule**
   (asks for admin), then **Test my ports from the internet**. The definitive test: a friend runs Loki Host too and
   uses **Test a server** with your address.
3. **Accounts tab.** Make your friends' accounts (or allow self-registration from the login screen; off by default,
   which is safer on the internet: a self-made account's password crosses the internet unencrypted once). Tick **GM** for
   your own account if you want GM commands. Names: letters A-Z, digits and `_ - .` (what Loki's login screen types), not
   ending in `_M` / `_F` (rAthena reads that as a registration while self-registration is on); passwords: letters, digits
   and English-keyboard symbols, no spaces or accents (only ASCII reaches the server as the same bytes Loki hashes).
4. **Server tab.** **Start server.** Send friends the text under "What to send your friends" (English or Português,
   chosen beside it): the steps in Loki (Servers… → Add a server → Name, Address, Port → Test connection → Save), the
   line for `servers` in `Loki_Data\StreamingAssets\loki.json` as the other way, and how they get their account. The
   friends' guide (`client/release/`, in the release zip) tells the rest: the RO client, Loki's first start, controllers.
   **Stop server** (or closing the window) shuts the servers down cleanly, so they save.

Windows SmartScreen may warn about an unsigned program: "More info" → "Run anyway".

## Headsets (the pose relay)
Friends playing in VR see each other twice: as their RO characters on the table, and as a floating headset with two
controllers above it, where the other player's head and hands are over their own table. Loki Host runs a small relay for
this beside the servers (**port 7903**, TCP, in the same router range and firewall rule: 7900-7903; switch and port on the
Setup tab, editable after the setup; the **Headsets** light on the Server tab). It starts after the map server and stops
with the servers; if its port is taken it stays off with a warning and everything else runs. The share line carries it as
`"presencePort": 7903`; a client whose entry has none tries the login port + 3.
- What travels: 15 times a second, each VR client's head and hand poses **in the map's own frame** (GAT cells, relative to
  the map, so each player's table scale, pan and turn cancel out), its map name, and once its RO account id and character
  name. The relay forwards a pose to the other players on the same map only, adding the account id and name the sender gave
  when it connected, and tells them "gone" when a player changes map, stops sharing or disconnects (a killed client's
  connection reset counts). Desktop clients (no headset) send nothing. Wire format: `client/Assets/Loki/Runtime/Net/PresenceWire.cs`
  (compiled into Loki Host too).
- Standalone, for tests or a server without Loki Host: `dotnet run -c Release --project tools/lokihost/Relay -- --port 7273`
  (`--bind`, `--log file`, `--no-voice`, `--stats seconds`: the counters in the log while they change).

## Voice (on the same relay)
Friends hear each other through Loki: an always-open microphone behind a noise gate, no push-to-talk. Voice travels on the
headset relay's own connections and port (**7903**: nothing new to forward, the same firewall rule; "What to send your
friends" is unchanged). The switch is on the Setup tab under the headsets' ("Relay the players' voices too", on by default,
editable after the setup, used at the next Start; the light on the Server tab then reads **Headsets & voice**). Voice needs the
relay: with the headsets' relay off there is no voice either.
- What crosses the relay: while a player's own noise gate is open (they are speaking), 50 frames a second of 20 ms of
  compressed speech each (IMA ADPCM, 16 kHz mono, 163 bytes: the client's `Voice/ImaAdpcm.cs`), each with the speaker's map
  name; nothing at all while they are silent. The relay adds the account id and name the speaker gave when it connected and
  forwards the frame to **every other connected client that knows voice, whatever its map** (friends keep talking while one
  walks to town; each listener's client decides what it plays: "Hear friends on other maps", per-friend mute and volume).
  When a speaker switches voice off, leaves or loses its connection, the others are told and free its stream. The relay never
  decodes, keeps or logs the sound; its log has one line when a player starts to share voice and one when they stop.
- Bandwidth: a talking player sends about 10 KB/s (50 frames of about 190 bytes; about 12 KB/s with the TCP/IP headers), and
  every listener receives that per simultaneous speaker; the host's upload is that times (speakers × listeners). Four
  friends all talking at once: about 36 KB/s down per player, about 144 KB/s up from the host. Silence costs nothing.
- Old and new: a client from before voice is served exactly as before (it is never sent a voice frame); a new client on an
  older Loki Host shows the headsets as before and says "this server's Loki Host is from before voice". Both directions are
  tested (`Tests/VoiceTests.cs`, and the client's smokes with the 2026-09-30 relay).
- Limits (each bounded, as for the headsets): 75 voice frames per second per client (a speaker sends 50; more are dropped), frames of at most
  274 bytes on a voice connection (200 on an old one), 128 queued frames per slow listener (the oldest are dropped first:
  late speech is worth nothing), the connection limits below.

## Casting menus (on the same relay; client wave 18)
Friends whose headsets are shown see the casting menus each other opens: the pad, the flick, the wand's wheel, the forearm bar
and the wand in a hand, drawn at the sender's hands and head, see-through and only to look at (the client's
`UI/GestureShare.cs` sends, `UI/FriendGestures.cs` draws; the frames: `Net/GestureWire.cs`). Nothing to switch on here: the
relay always forwards them (the standalone relay: `--no-gestures` to stop it).
- Who sees them is the SENDER's choice (Settings → Headsets: "Casting menus shown"; "Seen by": Everyone, or any of Friends,
  Family, Guild), and the RELAY applies it: a player whom the choice leaves out is never sent the frames. Who is who comes from
  rAthena's own tables in the host's database (`friends`; `char`.partner_id / father / mother / child; `char`.guild_id: read
  only, `Core/Relations.cs`), read when a player connects and again after 30 s at the next menu that needs them. rAthena
  writes a new friend or a wedding to the database when it saves that character (a new guild member within about a minute; one who
  leaves at once) (within its autosave time, 5
  minutes by default; at once on logout): a brand-new friend may see the menus a few minutes later. Without the database
  (the standalone relay without `--relations-db`) a menu shown only to some goes to nobody; "Everyone" still goes to everyone.
- What crosses the relay: only while a menu is open or changes (at most 15 frames a second per menu, one a second while it
  stays still), each at most 274 bytes: which menu, where it stands, the slots' skill / item ids and amounts, the lit choice.
  Old and new: a client from before wave 18 is never sent a menu; a new client on an older Loki Host sends none and sees
  none. Tested in `Tests/GestureTests.cs`.
- Menus shown only to friends, family or guild travel only between PROVED accounts (wave 18 merge): each client proves its
  account with rAthena's web token of its login (the proof of "Settings per account" below: the copy
  gives web tokens, `use_web_auth_token: yes`), and the relay sends a restricted menu only from a proved account to a proved
  account. A client that merely claims an account in its HELLO (a stranger claiming a friend's account) is sent "Everyone"
  menus only, and its own restricted menus go to nobody. Headsets and "Everyone" menus keep the headsets' threat model (below):
  a claimed account takes that account's connection over (the friend's headset and menus stop; the log shows the new address).
  Without the settings store (the standalone relay without `--accounts-db`) nobody is proved: restricted menus go to nobody.

## Air pen drawings (on the same relay; client wave 20)
A player draws 3D strokes in the air with the pen (the Social page's "Take pen", or pulled out of the air beside the overhead menu);
other players on the map see them where they were drawn (the client's `LokiGame.W20Pen.cs` and `UI/PenDrawings.cs`; the frames:
`Net/DrawWire.cs`). Nothing to switch on here: the relay always forwards them (the standalone relay: `--no-drawings` to stop it).
- Who sees them is decided twice. The SENDER's "Drawings seen by" (Settings → Headsets: Everyone, or any of Friends, Family, Guild;
  friends only by default) rides in every frame and the RELAY applies it exactly as for the casting menus (the same relations, the
  same proved accounts, the same "nobody without the database"). Each copy also carries what the sender is to that viewer
  (friend / family / guild, from the same tables; nothing when not known or not proved), and the VIEWER's client draws it only when
  its own "Drawings I see" (friends only by default) allows.
- The relay keeps no drawing: the sender's client sends a stroke as it is drawn (a part every 0.1 s), erasures at once, and all its
  drawings again (the list of its strokes, then every part, 25 frames a second) when a link starts, when a player comes onto its map
  and every 20 s. A viewer who was sent a player's strokes and is left out later (a friendship ended) is told once by the relay to drop
  them. A player's drawings go for the others when the relay says that player left the map or the relay (GONE: a map change,
  a logout, the headset no longer shared). Limits: 60 drawing frames per second per client (more are dropped, never the connection),
  each at most 274 bytes (39 points); a player keeps at most 100 strokes and 4,000 points. Old and new: a client from before wave 20
  is never sent a drawing; a new client on an older Loki Host sends none and sees only its own. Tested in `Tests/DrawTests.cs`.

## Exact spots (on the same relay; free movement)
rAthena keeps every character on a cell; a Loki client lets its player stop anywhere and draws the character there, up to half a cell
from that cell (`Sprites/Unit.Smooth.cs`). Each client sends where it draws its own character (the frames: `Net/SpotWire.cs`; 15 a
second while it moves, once a second at rest, none while it is hidden), and the relay forwards that to the other players on the same
map, so they draw it at the same spot. Nothing to switch on here (the standalone relay: `--no-spots` to stop it). A spot goes only to
players whose own spot is within 32 cells (the server shows a player the units within 14): nobody learns where a far player stands.
Limits: 40 spots a second per client (more are dropped, never the connection). Old and new: a client from before this is never sent a
spot and draws everyone on their cells; a new client on an older Loki Host sends none and sees the others on their cells. Tested in
`Tests/SpotTests.cs`.

## Horde (the co-op side mode, in the relay)
Friends can start a **Horde** run from Loki's wrist menu: everybody connected is asked to join (30 s), and those who do play a
co-op bullet-heaven run on a field of an RO map, from Novice through RO's jobs, inside the run only. The run is simulated **here**,
in the relay (40 ticks a second since client wave 9, 20 snapshots a second to each player; the players' games only send their
stick and their choices and draw what they are sent), on the relay's own port (**7903**: nothing new to forward). The world
pauses while anyone is choosing a card or a job (at most 30 s a choice at first; each time that limit is met the run's limit
drops by 10 s, down to 5 s), and the party shares one EXP bar (design note, 2026-10-05). Setup tab: "Horde: friends can start a co-op run…" (on by default; it needs
the headsets' relay), "A run lasts" (15 minutes by default) and "Real EXP at the end" (Off / Low / Normal / High; Normal). Server
tab: the "Horde" box shows the run being played (its players, their level and job) and the last run; the log has one line per run.
- **The real character is never changed by a run**, except for the EXP reward: at the end Loki Host turns each player's result into
  a share of what that character needs for its next base / job level (capped by the size you chose), writes it into the table
  `loki_horde_reward` of its database, and a script it installs into **its own copy** (`npc/custom/loki_horde.txt`, with a line in
  the copy's `conf/import/map_conf.txt`; written at every Start, removed when Horde or its reward is off) gives it to the character
  once with rAthena's `getexp`, as soon as the character is online and able to gain EXP (a player who logged out before the end
  gets it at their next login). The amount is decided here, never by a player's game. Details and the rAthena lines it relies on:
  `design notes` §2.5.
- Bandwidth: about 17 KB/s down per player in a run with 8 players and 300 monsters (8 players: ~136 KB/s up from the host), on
  top of voice (client wave 8 was 10 KB/s at 10 snapshots a second).
- Old and new: a Loki from before Horde is served exactly as before; a new Loki on an older Loki Host says Horde is not available
  there. Horde's wire has a protocol version (2 since client wave 9): a Loki whose Horde is another version is told so plainly
  ("the host runs another version of Horde") and keeps headsets, voice and the data check; it cannot join a run until one of the
  two updates. **No password**, as for headsets and voice (threat model below): someone who knows the address can play a run as any
  account id and name, which earns that account's character the run's capped EXP; nothing else.
- Standalone relay: `--horde` (`--horde-minutes`, `--horde-invite`, `--horde-towns`, `--horde-bots N`): no rAthena database, so no
  real EXP (the results say so).

## Settings per account (in the relay)
Client wave 18 (the design's point 1): every player's Loki settings and Reposition places are kept per ACCOUNT in this host's
database (table `loki_account_settings`, made at every Start), so a player who starts Loki on another PC gets them all after
entering the world ("Settings loaded from your account."). Audio and Graphics stay per PC. The newest save wins, the whole
file. It rides on the relay (no new port), needs the relay on, and is switched by `AccountSettings` in the settings file (on).
- The proof: rAthena's own web token. With it on, Loki Host writes `use_web_auth_token: yes` into the copy's
  `conf\import\login_conf.txt` at every start: each login gets a new random token, sent to that client only, and switched off
  10 s after the account goes offline. The relay accepts a client's settings only with the token of its account's login in force,
  checked against the `login` table as rAthena's web server checks it (`AccountSettingsDb.cs`). So a friend's settings stay theirs.
- Standalone relay: `--accounts-db "<MySQL connection string to the rAthena database>"` (its login server must give tokens).

## Game data updates (your Ragnarok Online install, and your friends')
Loki ships none of Gravity's files: every player's Loki reads the game from that player's own Ragnarok Online install, and
the server walks every unit on its **map data** (rAthena's map cache). When the official patcher changes a map, the three
must move together, or a walk can be refused, or drawn through something the server walks around.

- **Your friends** need their own Ragnarok Online install, kept up to date with its official patcher. Their Loki follows it
  by itself (it prepares its game data again after a patch). "What to send your friends" says so.
- **You** set **Your Ragnarok Online folder** on the Setup tab (the one with `data.grf`; editable after the setup; only ever
  read) and keep that install patched. At every **Start**, before any server runs, Loki Host compares the folder with what
  the server's map data was made from (each GRF's size, time and version, the loose map files, the patcher's own number,
  `patch.inf`). Unchanged: nothing is done ("Map data: current … no rebuild"). Changed: it waits until the files have been
  quiet for a minute (a patch still running is never read), builds the map data again from your maps, runs the safety
  checks, and only then puts the new file in place; the Server tab and the log say what happened ("REBUILT … 1 taken from
  the install (prontera) … checks clean"). With no folder set, nothing is followed and the server walks on the map data it
  has.
- **Which files of the folder are read**: the ones each player's Loki reads, by the same code (`Core/HostInstall.cs` opens
  the folder through the client's `Data/RoInstall.cs` and `IO/RoLookup.cs`): `data.grf`, or the archives a `DATA.INI` lists,
  in its order, when the install has one; loose files in `data\` before or after the archives as the install's clientinfo
  says. Unlike a player's Loki, Loki Host does not go on with part of an install: an archive that is damaged, cut short or
  held open by the patcher keeps the map data in place as it is ("NOT rebuilt … cannot be read"), and the next Start tries
  again.
- **What is built**: `db\import\map_cache.dat` in the host's copy, holding exactly the maps whose walkable cells differ
  between rAthena's own cache and your install, with your install's cells (rAthena reads the import cache first and takes
  each map from the first cache that has it: `map/map.cpp:3919-3925, 3967-3971`). It is what `tools/mapcache/` builds,
  ported to C# step by step (`Core/MapCacheBuilder.cs`, `Core/MapCache.cs`; no Python is needed to host).
- **The safety checks** (`Core/PointsCheck.cs` = `tools/mapcache/check_points.py`): every warp's touch area and landing
  point, every OnTouch area, every `"map",x,y` in the scripts, fixed spawns, start points, `@go` points (read from your
  rAthena build folder when it holds the source): none may become unreachable or be cut off from its map's main area. If
  one would, the new map data is **not** installed and the lines naming it are in the log.
- **Nothing is lost**: the file that was in place is kept beside the new one as `map_cache.dat.before-<date>-<time>` (the
  very first time that is rAthena's empty template). To go back: Stop, move `map_cache.dat` away, rename the kept file to
  `map_cache.dat`, move `map_cache.loki-stamp.txt` away too (Loki Host then checks again at the next Start), Start.
- **If it fails** (a GRF that cannot be read, a patch that never settles, a check that fails): the map data in place stays
  exactly as it was, the Server tab says why ("NOT rebuilt, the map cache in place is kept: …"), the servers start as usual,
  and the next Start tries again.
- **Friends whose game data differs from yours are told, once per session**, in their chat ("Your Ragnarok Online data
  differs from the host's: walking may not match on some maps", and which side should patch when the two patch numbers
  differ), and the Server tab lists every connected player with "the host's game data" / "OTHER game data" / "no data
  stamp" (an older Loki). What is compared is a short stamp of the maps' cells (`client/Assets/Loki/Runtime/IO/MapDataStamp.cs`,
  the same code in the client and here), not file dates or sizes: two installs at the same patch always have the same
  stamp, and a patch that touches no map changes nothing. It travels on the headset relay's connection (port 7903, nothing
  new to forward; it needs the relay: with the headsets' relay off nothing is compared); a Loki from before this, or an
  older Loki Host, simply does not take part: no check, everything else as before. The stamp proves nothing and protects
  nothing: a client that sends a false one only silences its own warning.
- **Which zlib wrote the file does not matter to the server**, but it shows when files are compared: the map data's
  compressed bytes differ between this program (.NET 8), Python on Windows, Python on Linux and other .NET versions, for the
  same cells. `tools/mapcache/cache_digest.py` and `LokiMapTool digest` print a digest of what the server reads (names,
  sizes, cells): equal digests mean the same map data. Run on the .NET 9 runtime, `LokiMapTool build` writes the Python
  tool's file byte for byte (`design notes`).

### Threat model (why there is no password)
- Who can connect: anyone who can reach the port, like the login server. The relay has no login of its own: it never
  sees RO passwords or session keys, and asking for one would mean either sending the RO password to a second service or
  changing rAthena (the rule: nothing outside the RO protocol goes into rAthena-specific code).
- What a stranger or a cheating friend can do: claim any account id and name, and send poses for any map. Effects: a fake
  or misplaced floating headset (labelled with whatever name they claim) on the tables of the players on that map, or,
  by claiming a real player's id, replacing that player's headset until the real client reconnects (one connection per
  account id: the newer wins). **Nothing else**: no RO packet goes through the relay, the game never acts on a pose (no
  targeting, no movement, no chat), and the client ignores its own id coming back.
- What a connected client learns: the head / hand positions, map and character name of every sharing player on the map it
  claims, including maps it is not on and players out of RO's view range. On a friends' server this is the point; players
  who don't want it switch **Share my headset** off (Comfort → Sound & display), and **Friends' headsets** hides the others.
- Denial of service is bounded: 64 connections, 16 per address, 200-byte frames (anything larger or malformed closes the
  connection: an HTTP request or a port scanner is closed at once), 40 poses per second per client (more are dropped), 64
  queued frames per slow receiver (the oldest are dropped: newer poses supersede them), 5 s to say hello, 15 s of silence.
  A flood can at worst stop the headsets; the RO servers are separate processes and do not depend on the relay.
- **Voice, said plainly:** with no password, anyone who can reach the port can **speak as any account id and name**, and
  **hears everyone who shares their voice**, on every map, without logging in to the game: what is said above about a faked
  headset holds for a faked voice, and a listener who should not be there is not visible in the game (the relay's log shows
  every connection with its address, and "shares voice" for each speaker). On a friends' server, where the address is only
  given to friends, that is fine; it is not a private line against someone who knows the address and port. A player who
  minds switches **Share my voice** off (Settings → Voice: the microphone is then closed, nothing is sent), mutes a voice in
  the player menu or on the Voice page, or the host switches voice off here. Voice is not encrypted on the wire (as the game
  itself is not). A voice flood is bounded like the poses (rate, frame size, queue): the RO servers do not depend on the
  relay, but voice is forwarded to everyone, so a flooding connection costs the host's upload up to 75 frames a second
  (about 20 KB/s) per connected listener, which the game shares; switching voice off here (or closing the port) ends it.
- **Settings per account** (client wave 18) are the exception: they need the account's login token (rAthena's web token, see
  above), so nobody can read or write another account's settings. The token travels unencrypted, as the game itself does.
  Casting menus shown only to friends, family or guild need the same proof on both sides (wave 18 merge).
- Not covered: a later version could sign HELLO with a token the login server issues (needs a change on the server side),
  or run as a SpacetimeDB table where the module knows the player (the client's `IPresenceChannel` and `IVoiceChannel` are
  built for that swap).

## Database backups and the zeny log
design note, 2026-10-07: "Turn on both, the zeny log and the startup backup". Two switches on the Setup tab, both on by default (a
settings file from before them gets them on), editable after the setup, used at the next Start.
- **Zeny log**: at every Start Loki Host writes `log_zeny: 1` into the copy's `conf/import/log_conf.txt` (`0` when the switch is
  off; rAthena's own `conf/log_athena.conf` is never edited: its import is read last). rAthena then records every zeny change
  (trades, shops, vending, NPCs, drops, mail, GM commands …) in the database's `zenylog` table; the map-server says "Logging Zeny
  transactions to table 'zenylog'" at its start. Nothing about EXP, levels, stats or skills is logged by rAthena.
- **Backup at every Start**: once the database answers and **before** the login / char / map servers start, the host's database
  (`DbName` in `lokihost.json`, `loki_host`) is dumped with MariaDB's own `mariadb-dump.exe` (from the MariaDB programs' folder;
  `mysqldump.exe` if that is missing) into **`%APPDATA%\LokiHost\backups\<db>-<yyyy-MM-dd_HH-mm-ss>.sql.gz`** (gzip). The
  Server tab shows the last backup's time, size and folder, or, in red, why it failed; the log says the same
  (`log\lokihost-backup.log` in the host folder). **The servers start even when the backup fails.**
  - Consistent: `--single-transaction`, and no rAthena server runs yet. The password reaches the tool in its environment
    (`MYSQL_PWD`), never on a command line or in a log; `--no-defaults` keeps option files on the PC out of it.
  - No half files: the dump is written as `….sql.gz.incomplete` and renamed only when the tool ended well and wrote its
    "-- Dump completed" line. A failed one stays `.incomplete` (look at it, then remove it yourself).
  - **Nothing is ever deleted or overwritten**: two backups in one second get `_2`, `_3` … **Old backups must be removed by
    hand.** Size: an empty rAthena database (main.sql + logs.sql) is 7.2 KB; a sample host database on 2026-10-07 holds about
    30 KB of rows, so a backup is some tens of KB today; the logs tables (items, zeny, chat …) make it grow as people play.
    One per Start: a few hundred Starts are a few MB at today's size.
- **Restoring a backup by hand** (there is no button; this replaces every table of the database with the backup's):
  1. **Stop server**. Start **only the database**: open the **Accounts** tab and press **Refresh** (with the servers stopped
     this starts Loki Host's own database alone; closing the window stops it again).
  2. Unpack the `.sql.gz` (7-Zip; or PowerShell:
     `$i=[IO.File]::OpenRead('C:\…\loki_host-….sql.gz'); $g=New-Object IO.Compression.GZipStream($i,[IO.Compression.CompressionMode]::Decompress); $o=[IO.File]::Create('C:\LokiVR\restore.sql'); $g.CopyTo($o); $o.Close(); $g.Close()`).
  3. In **cmd.exe** (PowerShell has no `<`): `"C:\Program Files\MariaDB 13.0\bin\mariadb.exe" --no-defaults -h 127.0.0.1 -P 3307 -u lokihost -p loki_host < C:\LokiVR\restore.sql`
     (port, user and database from `lokihost.json`: `DbPort`, `DbUser`, `DbName`; the password it asks for is `DbPass` there).
  4. Close the window (it stops the database), or press **Start server** (it backs up the restored data first, then starts).
  To look at a backup without replacing anything, restore it into a new, empty database instead: `-u root` (its password is
  `PrivateDbRootPass` in `lokihost.json`; the `lokihost` user may not make databases), `CREATE DATABASE x` first, then the same
  command with `x` (tested so: `BackupTests`).

## What it changes on your PC
- The host folder it creates (a copy: your rAthena build is only read).
- In MariaDB: a new database (default `loki_host`) and a user `lokihost` that may use only that database. An existing
  database with tables is never reused or dropped.
- In its database: the table `loki_horde_reward` (Horde's reward decisions; made at Start while Horde's reward is on).
- `%APPDATA%\LokiHost\backups\`: one database backup per Start while that switch is on (never removed by Loki Host: see
  "Database backups and the zeny log").
- `%APPDATA%\LokiHost\lokihost.json`: its settings, including the `lokihost` database password (rAthena keeps it in
  plain text in `conf\import\inter_conf.txt` too). Never the admin password.
- In the host folder, when a Ragnarok Online folder is set: `db\import\map_cache.dat` (rebuilt when your maps changed, the
  file before kept as `map_cache.dat.before-…`), `db\import\map_cache.loki-stamp.txt` (what it was made from) and
  `log\lokihost-mapdata.log`. **Your Ragnarok Online folder is never written to.**
- Only when you press the button: one Windows Firewall inbound rule, "Loki Host (TCP 7900-7903)" (the other button
  removes it; a rule made by a Loki Host from before the headset relay is called "Loki Host (TCP 7900-7902)": remove that
  one with the older version or in wf.msc).
- Nothing starts with Windows, no services, no scheduled tasks.

## What the copy gets
- Your build's programs and DLLs, `conf`, `db`, `npc`, `sql-files`; rAthena's SQL (`main`, `logs`, `web`, the roulette
  prizes) imported into the new database.
- Test settings from Loki's parity rig are taken out: `conf\import\groups.yml` made every account a GM (reset to
  rAthena's default, old file kept as `groups.yml.loki-test`), the test NPCs in `npc\scripts_custom.conf` are commented
  out. Game settings (exp rates, sit regen, pincode off) stay as your build has them.
- rAthena's inter-server account (`s1` / `p1` in a fresh rAthena) gets a random name and password.
- With settings per account on (the default): `use_web_auth_token: yes` in `conf\import\login_conf.txt` (rAthena then gives
  each login a web token, the relay's proof for that account's settings).
- At every start, `conf\import\*.txt` and `conf\subnet_athena.conf` are rewritten for the current public address
  (asked from api.ipify.org, checkip.amazonaws.com or icanhazip.com): players on the internet get the public
  address, players on this PC and on your home network get the local one (no router "NAT loopback" needed).
- At every start too, the controls tutorial's own map (client wave 21): one entry "Loki Tutorial" in `db\import\instance_db.yml`
  (in a block Loki Host owns), `npc\custom\loki_tutorial.txt` and its line in `npc\scripts_custom.conf`. A Loki client asks for it
  when its tutorial begins: each character gets a private copy of the map (an rAthena instance: no NPC, no warp, no monster,
  nobody else) and is back where it came from when the tutorial ends (a new character: its start point). The map and the cell
  are one line of `lokihost.json`: `"TutorialMap": "valkyrie,49,36"` (the Valkyrie's hall; `""` = no private map: the tutorial
  then runs where the character stands, as on any rAthena server).
- Server logs are also written to `<host folder>\log\lokihost-*.log`.

## Tested / not tested
- Linux, in the cloud build sandbox (`Tests/`): the configuration logic with Loki's real overlay files; the database
  setup against a real MariaDB with rAthena e985006's own SQL files; end to end against a real rAthena e985006 build
  (setup, start, "Map Server is now online" through the random server account, a login with an account made by the
  tool accepted and a wrong password refused, clean shutdown).
- **Windows, on a test PC (2026-09-30),** with a scratch host (`--settings`, a separate folder) against a scratch
  MariaDB 13.0.2 on port 3307 and a standard rAthena build (C:\rAthena):
  - the window; setup (70 tables, a `lokihost` user limited to its database, the random server account,
    `groups.yml` reset, the test NPC off); start; the status lights; the Accounts tab (a GM and a player account,
    the length check);
  - **Stop by Ctrl+C: failed on the first run** (all three servers ended by force after 30 s: Loki Host started with
    Windows' "ignore Ctrl+C" flag set and every server inherited it; a ping.exe control behaved the same). Fixed
    (c8344a7: the flag is cleared before each server starts); since then every stop logs "Shutting down /
    Terminating / Finished" for map, char and login within ~4 s, also when the window is closed while running;
  - servers left running by a killed Loki Host are taken over when it opens again, and stopped cleanly;
  - a VPN (WireGuard VPN) was on: the LAN address is the Wi-Fi's, not the VPN's, and start / the share text / the port test
    warn that the public address is the VPN's (fixed f2b4e9c; the first version took the VPN address);
  - the internet port test reads portchecker.io's answer (a positive control: portchecker.io:443 open, 7900 closed);
    with the VPN on, 7900-7902 show CLOSED, as they must;
  - **LAN test with two clients** (`smoke/run-lan.cmd`, the Loki client build w3 on the fresh LATAM data): one client
    through 127.0.0.1, one through the Wi-Fi address; login, char and map servers were each reached on the address
    of the client's own network (subnet_athena.conf); party (create, invite, members, HP, minimap, lead hand-over,
    leave), trade (offer, lock, result, cancel) and cart all as expected. The vending / kill steps of that old smoke
    need job skills and HP the fresh Novice characters do not have (fixtures, not the host).
- **Client wave 5 (w5friends, 2026-09-30), tests only (the running LokiHost.exe was not replaced):** stopping a private
  database this window did not start now waits for its program (found by its `--defaults-file`) to end, not only for it to
  stop answering: `PrivateDbTests.A_window_that_did_not_start_the_database_waits_for_its_program_to_end`, a real private
  MariaDB with shutdown work (256,000 rows deleted, `innodb_fast_shutdown 0`): the original code returned after 2.3 s while
  mariadbd ran 3.9 s longer; now StopAsync returns when it has ended (7.0 s). The share text and the account rules:
  `FriendsTests` (the client's own button names, English and its pt-BR table).
- **w14host (2026-10-07), tests only (the running LokiHost.exe was not replaced):** the zeny log line and the backup
  (`BackupTests`: a scratch private MariaDB 13.0.2 on its own port; the dump is gzip, complete, restorable with `mariadb.exe`;
  a refused login fails plainly with no file that looks whole; old backups byte for byte unchanged), and the whole private path
  (`Private_database_setup_start_login_stop` against a standard rAthena build on ports 179xx): the backup (7.7 KB) is made before
  "Starting login-server", the map-server says "Logging Zeny transactions", logins accepted / refused, clean stop. Not
  tried: a real zeny change written to `zenylog` (needs a player in game), the window itself (built, not opened).
- **wfollow (2026-10-14):** Loki Host's own database keeps its temporary tables in a folder of its own, `tmp` beside `data`
  (`PrivateDb.TmpDir`, given as `--tmpdir` at each start; made when missing; my.ini and the data folder unchanged). Before, it
  used `%TEMP%`, which every MariaDB of the Windows user shares, and a MariaDB that starts deletes the `#sql*` files there:
  one test database starting took another's temporary table away (the parallel test run's failure, client wave 26).
  `PrivateDbTests.Two_private_databases_started_at_once_keep_their_own_temporary_files`.
- **Not tested yet:** setup against an existing MariaDB (same code, their admin login); the firewall button (design note:
  it changes a security setting and asks for admin); a second physical device on the LAN (a VPN's LAN blocking would
  show there); a real internet session (needs the VPN off, the router's forwarding and the firewall rule).

## Build
```
cd tools/lokihost/App
dotnet publish -c Release -r win-x64     # → bin/Release/net8.0/win-x64/publish/LokiHost.exe (self-contained)
cd ../Tests && dotnet test               # the database / end-to-end tests run when LOKIHOST_TEST_ADMIN,
                                         # LOKIHOST_TEST_RATHENA and LOKIHOST_TEST_RATHENA_BUILD are set; the map data
                                         # against the real install when LOKIHOST_TEST_RO, LOKIHOST_TEST_RATHENA_DB and
                                         # LOKIHOST_TEST_MAPCACHE_REF are (MapDataTests.cs)
```
Projects: `Core` (everything but the window), `App` (the window), `Relay` (the relay alone: `--ro-folder <Ragnarok Online
folder>` makes it compare players' game data as Loki Host does, `--print-data-stamp <folder>` prints a folder's stamp),
`RoData` (the Loki client's own GRF reader and data stamp, linked from `client/Assets/Loki/Runtime/IO`: moving those files
breaks this build, and they must stay free of Unity types), `MapTool` (the map data code from the command line: `build`,
`check`, `digest`, `cells`, `stamp`, `watch`; for comparing with `tools/mapcache/*.py`), `smoke/` (scratch-host helpers:
`w7host-finalpass.ps1` runs the map data smokes from nothing).
