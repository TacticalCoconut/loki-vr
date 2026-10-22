#!/bin/bash
# publish.sh: a host publishes a new Loki Online module WITHOUT losing the players (tools/host/README.md).
#
#   bash tools/host/publish.sh DATABASE [--build release|test] [--features LIST] [--wasm FILE] [--fresh]
#                                       [--server NICKNAME] [--url URL] [--backups DIR] --yes
#
# What it does, in this order (without --yes it only prints this plan for your arguments and exits):
#   1. builds the module: --build release (the default: cargo's release profile WITH the cargo feature install_maps,
#      the maps of the Ragnarok Online install in server/data/install/ and their data stamp: what a host runs, because
#      players draw and walk on their install's maps; server/Cargo.toml, server/ARCHITECTURE.md 1.4) or --build test
#      (the fast dev-wasm profile on rAthena's stock maps, what the test rigs use); --features LIST passes other cargo
#      features instead (--features none: a release build on the stock maps); --wasm FILE takes a module that is
#      already built.
#   2. a database that does not exist yet is published and that is all (it starts in TEST mode: every player has
#      every command; `spacetime call -s <server> <db> set_server_mode release <admin account>` makes it a real one).
#   3. an existing database is BACKED UP first, always: the server closes (everyone is logged out saved, logins are
#      refused), everything players own is exported to <backups>/<database>-<date>-<time>.json.gz.
#   4. the module is published IN PLACE when SpacetimeDB accepts it (nothing is deleted; it does when tables were only
#      added), the server opens, the database is compared with the backup.
#   5. when SpacetimeDB refuses the update (a table changed), or with --fresh: the database is published fresh
#      (--delete-data: EVERY table is emptied), the backup is imported, compared table by table, and the server opens.
#      If the import is refused nothing is loaded and the server stays closed: the backup file is intact, nothing
#      is lost; fix the cause (the message names the table and the column) and run
#      `python tools/host/loki_host.py import <database> <backup file>`.
#
# The backup holds every account's password hash: keep the folder private. DRY=1 is the same as leaving out --yes.
set -u
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
DB=; BUILD=release; FEATURES=default; WASM=; FRESH=; SERVER=local; URL=http://127.0.0.1:3000; BACKUPS=$ROOT/backups; YES=
while [ $# -gt 0 ]; do
  case "$1" in
    --build) BUILD=$2; shift 2;;
    --features) FEATURES=$2; shift 2;;
    --wasm) WASM=$2; shift 2;;
    --fresh) FRESH=1; shift;;
    --server) SERVER=$2; shift 2;;
    --url) URL=$2; shift 2;;
    --backups) BACKUPS=$2; shift 2;;
    --yes) YES=1; shift;;
    -*) echo "publish.sh: unknown option $1"; exit 2;;
    *) [ -z "$DB" ] || { echo "publish.sh: one database"; exit 2; }; DB=$1; shift;;
  esac
done
[ -n "$DB" ] || { sed -n '2,26p' "$0" | sed 's/^# \{0,1\}//'; exit 2; }
case "$BUILD" in release) PROFILE="--release"; DIR=release;; test) PROFILE="--profile dev-wasm"; DIR=dev-wasm;; *) echo "publish.sh: --build release or test"; exit 2;; esac
# the features of the build: a host's release build is the install's maps (imaps: `--features install_maps`), a test
# build the stock maps the parity suites compare on; --features says otherwise ("none" or "" = no feature)
case "$FEATURES" in default) if [ "$BUILD" = release ]; then FEATURES=install_maps; else FEATURES=; fi;; none) FEATURES=;; esac
PY=${PY:-python}
CARGO=$(command -v cargo)
command -v spacetime > /dev/null || { echo "publish.sh: the SpacetimeDB CLI (spacetime) is not in PATH"; exit 2; }
$PY -c "import sys; sys.exit(0 if sys.version_info >= (3, 8) else 1)" 2> /dev/null || { echo "publish.sh: Python 3.8 or newer is needed (set PY=...)"; exit 2; }
if [ -n "$WASM" ]; then
  [ -f "$WASM" ] || { echo "publish.sh: $WASM does not exist"; exit 2; }
  BUILT="the module $WASM (already built)"
else
  [ -n "$CARGO" ] || { echo "publish.sh: cargo is not in PATH (or give --wasm FILE)"; exit 2; }
  WASM=$ROOT/server/target/wasm32-unknown-unknown/$DIR/loki_server.wasm
  BUILT="a $BUILD build of $ROOT/server${FEATURES:+ with the features $FEATURES}"
fi
HOST="$PY $HERE/loki_host.py --server $URL"
STAMP=$(date +%Y%m%d-%H%M%S)
BACKUP=$BACKUPS/$DB-$STAMP.json.gz
exists() { spacetime sql -s "$SERVER" "$DB" "SELECT COUNT(*) AS n FROM account" > /dev/null 2>&1 < /dev/null; }

echo "publish.sh: database '$DB' on the server '$SERVER' ($URL)"
echo "  1. $BUILT"
if exists; then
  echo "  2. the database exists: close it and back it up to $BACKUP"
  if [ -n "$FRESH" ]; then echo "  3. publish FRESH (every table emptied), import the backup, compare, open"
  else echo "  3. publish in place if SpacetimeDB accepts the module; else publish FRESH (every table emptied), import the backup, compare, open"; fi
  IS_NEW=
else
  echo "  2. the database does not exist yet: publish it (it starts in test mode)"
  IS_NEW=1
fi
if [ -z "$YES" ] || [ -n "${DRY:-}" ]; then echo "publish.sh: nothing was done. Add --yes to do it."; exit 0; fi

# ---- 1. build
if [ "$BUILT" != "the module $WASM (already built)" ]; then
  (cd "$ROOT/server" && $CARGO build $PROFILE --target wasm32-unknown-unknown ${FEATURES:+--features "$FEATURES"}) || { echo "publish.sh: the build failed; nothing was published"; exit 1; }
  [ -f "$WASM" ] || { echo "publish.sh: the build left no $WASM"; exit 1; }
fi

# ---- 2. a new database
if [ -n "$IS_NEW" ]; then
  spacetime publish -s "$SERVER" -b "$WASM" "$DB" -y || { echo "publish.sh: the publish failed"; exit 1; }
  echo "publish.sh: '$DB' is published and open, in TEST mode. For a real server: spacetime call -s $SERVER $DB set_server_mode release <admin account>"
  exit 0
fi

# ---- 3. the backup (the server closes and stays closed until step 4 / 5 opens it)
mkdir -p "$BACKUPS"
$HOST export "$DB" --out "$BACKUP" || { echo "publish.sh: the backup failed; nothing was published. (If the server is closed: $HOST open $DB)"; exit 1; }
[ -s "$BACKUP" ] || { echo "publish.sh: no backup file; nothing was published"; exit 1; }

# ---- 4. in place, when SpacetimeDB takes the module as an update of the tables that are there
if [ -z "$FRESH" ]; then
  if spacetime publish -s "$SERVER" -b "$WASM" "$DB" -y > "$BACKUP.publish.log" 2>&1 < /dev/null; then
    echo "publish.sh: published in place (no table was emptied)"
    $HOST open "$DB" || { echo "publish.sh: the server did not open: $HOST status $DB"; exit 1; }
    $HOST verify "$DB" "$BACKUP" | tail -4
    echo "publish.sh: done. The backup: $BACKUP"
    exit 0
  fi
  echo "publish.sh: SpacetimeDB did not take the module in place:"
  tail -4 "$BACKUP.publish.log" | sed 's/^/    /'
  echo "publish.sh: publishing fresh and importing the backup"
fi

# ---- 5. fresh, then the import
spacetime publish -s "$SERVER" -b "$WASM" "$DB" --delete-data=always -y > "$BACKUP.publish.log" 2>&1 < /dev/null || { tail -4 "$BACKUP.publish.log"; echo "publish.sh: the fresh publish FAILED. The old database may be gone; the players are in $BACKUP: publish again, then $HOST import $DB $BACKUP"; exit 1; }
if $HOST import "$DB" "$BACKUP"; then
  echo "publish.sh: done: published fresh, the backup imported and compared. The backup: $BACKUP"
  exit 0
fi
echo "publish.sh: THE IMPORT DID NOT GO THROUGH. The database is empty and closed; every player is in $BACKUP."
echo "  Fix what the message names (or publish the previous module with --fresh), then: $HOST import $DB $BACKUP"
exit 1
