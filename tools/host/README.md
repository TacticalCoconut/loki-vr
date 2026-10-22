# tools/host: publishing a Loki Online module without losing the players

A module update that changes a table can only be published with `--delete-data`, which empties every table. These
tools carry everything players own across such an update, and make backups. They act as the database's **owner**: the
SpacetimeDB identity that published it (the `spacetime` CLI's login on the host's PC). Nobody else can read the
tables or call the reducers. Design and the proof: `server/src/cutover.rs`, `server/ARCHITECTURE.md`.

Needs: Git Bash, Python 3.8+ (standard library only), the SpacetimeDB CLI logged in as the owner, cargo for a build.

## Publishing

    bash tools/host/publish.sh <database> --yes                      # a release build of server/ with the install's maps (--features install_maps)
    bash tools/host/publish.sh <database> --build test --yes         # the fast test build (dev-wasm) on rAthena's stock maps
    bash tools/host/publish.sh <database> --features none --yes      # a release build on the stock maps (other cargo features: --features LIST)
    bash tools/host/publish.sh <database>                            # prints what it would do, does nothing

For an existing database it always makes a backup first (the server closes: everyone is logged out saved, logins are
refused), then publishes in place when SpacetimeDB accepts the new module (nothing is emptied), and otherwise
publishes fresh, imports the backup, compares the database with it table by table and opens the server. If an import
is refused, nothing is loaded and the server stays closed with the backup intact; the message names the table and
the column. A database that does not exist yet is simply published; it starts in **test mode** (every player has
every command): `spacetime call -s local <database> set_server_mode release <admin account>` makes it a real server.
The mode and the administrator are carried over by an import.

## Backups and the single steps

    python tools/host/loki_host.py backup <database>            # close, export to backups/<database>-<date>.json.gz, open
    python tools/host/loki_host.py backup <database> --logs     # … with the nine log tables
    python tools/host/loki_host.py export <database>            # close and export; the server STAYS closed
    python tools/host/loki_host.py import <database> <file>     # into a freshly published database; compares, opens
    python tools/host/loki_host.py verify <database> <file>     # compare a database with a file
    python tools/host/loki_host.py status | open | close <database>
    python tools/host/loki_host.py tables [database]            # every table: exported or not, and why

`--server URL` (before the command) names another SpacetimeDB server than `http://127.0.0.1:3000`.
To go back to a backup: publish the module fresh (`publish.sh <database> --fresh --yes` exports the present state
first, then imports THAT; to load an older file instead, publish with `spacetime publish … --delete-data` and run
`loki_host.py import <database> <the older file>`).

**A backup file holds every account's password hash and login identities. Keep it as private as the database.**

## The file

One JSON document (gzip when the name ends in `.gz`): `format` "loki-cutover", `version`, `made_at`, `source`,
`counts` (rows per table) and `tables`: for each table its columns as the database describes them and its rows, each
row an object keyed by column name at every depth. The version is the exporting module's (`cutover.rs` VERSION). A
module reads its own version and the older ones it has an upgrade step for; a newer file is refused. Whatever does not
fit the importing module's tables (a column missing, unknown, of another type, a table the module does not import, a
row count that is not the header's) refuses the whole file: an import is one transaction.

## What is carried over, and what is not

Carried: accounts (name, password hash, sex, group, block state), the identities that stay logged in, characters,
inventory / cart / Kafra storage, skills and cooldowns, hotkeys, quests, achievements, friends, memo points, saved
status changes, bonus scripts, character / account / global variables (cash points, the bank vault and roulette
points are such variables), pets, homunculi, mercenaries, elementals, mail with attachments, the auction, guilds
(members, positions, alliances, expulsions, skills, storage and its log, castles), parties, the permanent `$`
variables, the stock of the market and barter NPCs, the id counters, the release switch. With `--logs`: the nine log tables.

Not carried (`loki_host.py tables` gives the reason for each of the module's tables):
- what rAthena also loses at a restart: characters' session state, the world (NPCs, monsters, items on the ground),
  running scripts, timers, instances, battlegrounds, duels, chat rooms, chat channels players made, the party booking
  board, guild EXP not paid out yet, item cooldowns;
- open shops: the freeze closes every vending and buying store (autotraders included; the items are in the cart);
- the cash shop's sales (`sales`): the host sets them again;
- the host's own switches: the headset / voice switch (`presence_config`), the Horde settings (`horde_config`: `spacetime
  call -s local <database> horde_settings ...`), `@setbattleflag` changes, log settings; and the host's own
  conf/import/battle_conf.txt (`battle_conf_import`): after a FRESH publish the database is back on the built-in
  rates until the file is given again (`spacetime call -s local <database> set_battle_conf '"<the file's text>"'`);
  an update published in place keeps all of these;
- the first login of a guild or party member after an import loads the guild from its tables, as after an rAthena
  restart: its packets arrive a moment later than in a running server.

Between a fresh publish and the import's first call (a second or two in `publish.sh`) the new database is open and
empty, in test mode; an import into a database that already has an account is refused.
