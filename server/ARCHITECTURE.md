# loki-server — how the rAthena port is put together

Loki Online is rAthena e985006 (PACKETVER 20211103, RENEWAL) running as one SpacetimeDB 2.10.1 Rust module. The
client (Loki) speaks the unchanged RO map protocol: its packets arrive through the `packet` reducer, replies leave as
`packet_out` event rows (RLS: each client receives only its own). **Every rule is ported from rAthena's source; read
the C++ before writing the Rust, never guess.** Cite the rAthena file/function in doc comments.

## Ground rules for porting
- Read the rAthena function(s) end to end first. Port control flow, order of packets, and edge cases faithfully,
  including quirks. When something is deliberately different (no sockets, no char server), say so in a comment.
- Config values (conf/battle): `battle_config.member` is `battle_config::get(Bc::NAME)` (port wave I: the run-time
  table, `src/battle_config/`; a host or @setbattleflag can change it). Read it through a small typed function in
  `conf.rs` or the system's own conf file, with the file/key in its doc comment (`pub fn area_size() -> i16`), never
  a new `pub const`: a constant cannot follow the host's battle_conf.txt. Port BOTH branches of a setting, or leave a
  compile-time assertion that names what is missing. A value rAthena consumes while a database loads (datagen bakes
  it) is listed in `tools/gen_battle_converted.py` BAKED. `server/ARCHITECTURE.md` has the lists and the caveats.
- Constants/enums: take values from the compiler (see `tools/packetgen/script_constants.tsv`,
  the probe outputs) or the headers; never renumber.
- Packets: use the generated structs in `net/packets.rs` (`PACKET_*`, `packet_*`: `.to_vec()`, `::read()`), sizes
  verified against the compiler. Send with `clif::send(c, &bytes, &unit, Target::…)`.
- Systems not ported yet stay as clearly commented stubs (`// … (phase N)`), never fake behaviour.
- Never write `println!`, `print!`, `eprintln!`, `eprint!` or `dbg!` anywhere under `src/` (not even in tests or comments): `spacetime publish` from source rejects the module. Use `log::…` (tests may use `writeln!(std::io::stdout(), …)`).
- No `unwrap()` on data that a client controls; no slicing `String`s at byte offsets (names are latin-1 code points:
  use `net::wire::latin1()` bytes).

## Runtime shape
- `world::Ctx` is every game function's context: `c.db()` (tables), `c.tick()` (gettick, unix ms; inside a timer callback the callback's tick, timer.rs), `c.send(identity,
  bytes)`, `c.rnd()` / `c.rnd_value(a,b)`, `c.sender()`. Reducers wrap their work in `world::run(rc, |c| …)`, which
  flushes packets at the end.
- **State is in tables** (rAthena's in-memory structs → rows). Read a row, modify, save it back; after calling a
  function that may have saved the same row, re-read it (lost-update hazard).
  - `unit` = block_list + unit_data + view_data (+ what clif needs) for every object, id = rAthena id space.
  - `pc` = map_session_data runtime; `character` = mmo_charstatus (saved status); `inventory`, `char_skill`, …
  - map_session_data is split by system, one owner per field: `pc_status` (status.rs: battle status, bonuses, regen,
    sd->add_max_weight), `pc_items` (inventory.rs: sd->weight / max_weight, cart weight, equip_index, weapontype1/2,
    combos, itemid, item cooldowns), `pc_quest`, the party / social rows. Other systems call the owner's functions
    (e.g. status_calc_weight writes pc_items; pc_iscarton is the SC_PUSH_CART status change).
  - `npc`, `npc_function`, `mapflag`, `spawn_data`: the world, built from the embedded data by `npc::ensure_world`.
- **Timers**: `timer::add(c, due_tick, Func::X, target_id, data)` / `add_interval` / `delete` / `delete_for_target`.
  Add a `Func` variant (explicit number, never reuse) and a dispatch arm in `timer.rs`. do_timer semantics.
- **Static data** is embedded: `tools/datagen.py <rathena> data` writes `data/*.bin` and `data/json/*.json.z`
  (zlib JSON). In Rust, declare a serde struct and `dataset!(name, "file", Type)` in `data.rs`; it is parsed once
  per module instance. Resolve rAthena's own load rules (import overrides, `Clear`, inheritance) in datagen.
- **Two map sets, one build switch** (port wave I, `server/ARCHITECTURE.md`). `data/maps.bin` is rAthena's stock map
  cache: the DEFAULT build embeds it, and every parity suite needs it (the rAthena copies they compare with run on
  the stock cache). `data/install/` (`tools/datagen.py <rathena> data --install <Ragnarok Online folder>`: Loki Host's
  import-cache rule and checks, `tools/mapcache/`) is the same maps with a Ragnarok Online install's cells where its
  walkable cells or sizes differ, plus that install's map-data stamp; `--features install_maps` embeds it: **the
  build a host publishes** (`lcargo build --release --target wasm32-unknown-unknown --features install_maps`), and
  the one that answers the client's game-data check by itself (`presence.rs`). Rerun `--install` after an official
  patch that changes maps (it refuses maps that lose a warp / spawn / script point and keeps the files in place).
- Randomness only through `c.rnd()` / `c.rnd_value()` (reducer RNG).

## Layout
- `net/` protocol framing + generated packets; `parse.rs` clif_parse_* handlers; `clif.rs` packet senders;
  `unit.rs`, `map.rs`, `path.rs`, `pc.rs`, `npc.rs`, `atcommand.rs`, `timer.rs`, `conf.rs`, `rules.rs`, `data.rs`.
- New systems get their own file named after the rAthena file (`status.rs`, `itemdb.rs`, `storage.rs`, `skill.rs`,
  `mob.rs`, `battle.rs`, `script/…`, `party.rs`, `guild.rs`, …).
- **Every new table needs a line in `cutover::TABLES`** (cutover.rs: the host's export / import of player data,
  tools/host): is it something a player owns (rAthena keeps it in SQL) or server memory, and why. A unit test fails
  until it has one; then `LOKI_WRITE_MANIFEST=1 lcargo test --lib cutover` rewrites tools/host/tables.json. A change to
  an exported table's columns needs a new `cutover::VERSION` with its `upgrade` step, or old backups stop loading.

## Testing (cloud)
- Local SpacetimeDB: `spacetimedb-standalone` on 127.0.0.1:3000 (keys in local SpacetimeDB config;
  start command in rathena-vr-kit/README.md or the session notes). Publish:
  `cargo build --release --target wasm32-unknown-unknown && spacetimedb-cli publish -s local -b target/wasm32-unknown-unknown/release/loki_server.wasm loki -y`
  (add `-c` only when the schema changed incompatibly). Logs: `spacetimedb-cli logs -s local loki`.
- rAthena oracle (same data + Loki imports): local rAthena reference (`db.sh` then `run.sh`), login 127.0.0.1:6900.
- `rocli` (local test client): scripted client for either server
  (`./target/debug/stdbtest TAG user pass "Char" script.txt [ws://127.0.0.1:3000 | tcp://127.0.0.1:6900]`).
  `pair.sh a.txt b.txt tag` runs a two-client scenario on both servers and diffs what the clients received
  (`difflogs.py`). A port is done when the diff shows no differences for its packets.
- Bindings: C# (Unity) and Rust (rocli) can both be generated in the cloud with the 2.10.1 CLI
  (`spacetimedb-cli generate -l csharp|rust -b <wasm> -o <dir> -y`). After a Rust generate, patch the known codegen bug:
  `sed -i 's/^    pub crate: i16,$/    pub crate_: i16,/' src/bindings/status_data_type.rs` (then `rustfmt`).
- The CLI's default server is local: always pass `-s local`. Nothing goes to local until the whole game is ported.
- The standalone server starts a database lazily: after a server restart, make one request (e.g. a `sql`) per database so
  its timers resume.
