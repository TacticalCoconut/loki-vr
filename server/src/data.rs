//! Static game data compiled into the module (loki-server/data/*.bin, made by tools/datagen.py from the rAthena tree).
//! It is read-only and derived entirely from the embedded bytes, so it lives in lazily-built in-memory caches rather
//! than in tables; publishing a new module version is how it changes.

use std::cell::OnceCell;
use std::collections::HashMap;
use std::sync::OnceLock;

/// The maps the module embeds. Default: rAthena's stock map cache (tools/datagen.py build_maps), the maps every parity
/// suite's rAthena copy runs on. With the cargo feature `install_maps`: the same maps with the cells of a Ragnarok
/// Online install where its walkable cells or sizes differ (data/install/maps.bin, tools/datagen.py --install: Loki
/// Host's import map cache, read first as rAthena reads db/import/map_cache.dat, map.cpp:3919-3925, 3967-3971): the
/// build a host publishes, because its players draw and plan their walks on their install's maps.
#[cfg(not(feature = "install_maps"))]
static MAPS_BIN: &[u8] = include_bytes!("../data/maps.bin");
#[cfg(feature = "install_maps")]
static MAPS_BIN: &[u8] = include_bytes!("../data/install/maps.bin");

/// data/install/mapstamp.txt (written with the install's maps): `key: value` lines; `stamp` is the install's map-data
/// stamp (client IO/MapDataStamp.cs, "g1-" + 16 hex digits), `label` its patch label ("patch 1486").
#[cfg(feature = "install_maps")]
static MAP_STAMP_TXT: &str = include_str!("../data/install/mapstamp.txt");

/// The map-data stamp and patch label of the Ragnarok Online install the embedded maps were made from, or None when
/// the embedded maps are rAthena's stock cache (they are no install's maps: there is nothing to compare a player's
/// stamp with). presence.rs answers the client's data check with it, as Loki Host's relay answers with the stamp of
/// the install its map cache was made from.
pub fn map_stamp() -> Option<(&'static str, &'static str)> {
    #[cfg(feature = "install_maps")]
    {
        return parse_map_stamp(MAP_STAMP_TXT);
    }
    #[cfg(not(feature = "install_maps"))]
    {
        None
    }
}

/// The `stamp` and `label` lines of a mapstamp.txt; None without a stamp.
#[cfg_attr(not(any(test, feature = "install_maps")), allow(dead_code))]
fn parse_map_stamp(text: &'static str) -> Option<(&'static str, &'static str)> {
    let value = |key: &str| text.lines().find_map(|l| l.strip_prefix(key)?.strip_prefix(':')).map(str::trim);
    let stamp = value("stamp").filter(|s| !s.is_empty())?;
    Some((stamp, value("label").unwrap_or("")))
}

/// One map as rAthena's map_data keeps it: name, size and the gat cell types (map.cpp map_readfromcache).
pub struct MapData {
    pub id: u16,
    /// mapdata->name. An instance map's is its generated "<src id>#<instance id>" name (instance_generate_mapname);
    /// what the client is told is map_mapid2mapname's source map name (instance_map.rs).
    pub name: String,
    pub xs: i16,
    pub ys: i16,
    zdata: &'static [u8],
    cells: OnceCell<Vec<u8>>,
    /// An instance map (map.cpp map_addinstancemap): the source map whose cells it copied (instance_map.rs).
    src: Option<&'static MapData>,
}

// SpacetimeDB runs a module on one thread; the caches are never shared across threads.
unsafe impl Sync for MapData {}
unsafe impl Send for MapData {}

impl MapData {
    /// map.cpp map_addinstancemap's copy of `src` (size, cells) under another id and name. The gat cells are shared
    /// with the source (rAthena memcpy's them; the static cells never change, the dynamic ones are map_cell.rs rows).
    pub fn instance_copy(id: u16, name: String, src: &'static MapData) -> MapData {
        MapData { id, name, xs: src.xs, ys: src.ys, zdata: src.zdata, cells: OnceCell::new(), src: Some(src) }
    }

    /// The source map of an instance map copy.
    pub fn instance_src(&self) -> Option<&'static MapData> {
        self.src
    }

    /// Gat type of every cell (0 walkable, 1 wall, 3 water, 5 cliff…), row-major, decoded on first use.
    pub fn cells(&self) -> &[u8] {
        if let Some(src) = self.src {
            return src.cells();
        }
        self.cells.get_or_init(|| {
            miniz_oxide::inflate::decompress_to_vec_zlib(self.zdata).unwrap_or_else(|e| {
                log::error!("map {}: cell data does not inflate ({e:?})", self.name);
                vec![1; self.xs as usize * self.ys as usize]
            })
        })
    }
    pub fn gat(&self, x: i16, y: i16) -> u8 {
        if x < 0 || y < 0 || x >= self.xs || y >= self.ys {
            return 1;
        }
        self.cells().get(x as usize + y as usize * self.xs as usize).copied().unwrap_or(1)
    }
}

pub struct Maps {
    pub list: Vec<MapData>,
    by_name: HashMap<String, u16>,
}

static MAPS: OnceLock<Maps> = OnceLock::new();

/// A maps.bin (tools/datagen.py build_maps): the maps in file order, which is their id.
fn parse_maps(b: &'static [u8]) -> Vec<MapData> {
    assert!(&b[0..4] == b"LKMP", "maps.bin: bad magic");
    let count = u32::from_le_bytes(b[8..12].try_into().unwrap()) as usize;
    let mut p = 12;
    let mut list = Vec::with_capacity(count);
    for i in 0..count {
        let n = b[p] as usize;
        let name = String::from_utf8_lossy(&b[p + 1..p + 1 + n]).into_owned();
        p += 1 + n;
        let xs = u16::from_le_bytes([b[p], b[p + 1]]) as i16;
        let ys = u16::from_le_bytes([b[p + 2], b[p + 3]]) as i16;
        let len = u32::from_le_bytes(b[p + 4..p + 8].try_into().unwrap()) as usize;
        p += 8;
        let zdata: &'static [u8] = &b[p..p + len];
        p += len;
        list.push(MapData { id: i as u16, name, xs, ys, zdata, cells: OnceCell::new(), src: None });
    }
    list
}

pub fn maps() -> &'static Maps {
    MAPS.get_or_init(|| {
        let list = parse_maps(MAPS_BIN);
        let by_name = list.iter().map(|m| (m.name.clone(), m.id)).collect();
        Maps { list, by_name }
    })
}

impl Maps {
    /// map_getmapdata(m) for a map that exists: the static maps, then the instance maps created at run time (ids from
    /// instance_start = the static map count on, instance_map.rs).
    pub fn get(&self, id: u16) -> Option<&MapData> {
        match self.list.get(id as usize) {
            Some(m) => Some(m),
            None => crate::instance_map::registry_get(id),
        }
    }
    /// The number of maps loaded at start: map.cpp map_num at do_init_instance, i.e. `instance_start`.
    pub fn static_count(&self) -> u16 {
        self.list.len() as u16
    }
    /// map_mapname2mapid: accepts "prontera" or "prontera.gat" (mapindex_getmapname strips the extension).
    /// mapindex.cpp mapindex_getmapname + mapindex_name2idx: at most 15 characters are read, a ".gat" extension (any
    /// case) is dropped, 11 characters are kept; the lookup itself is case-sensitive (strdb). Instance map names
    /// ("0123#000001") resolve through the instance maps' mapindex slots (instance_map.rs).
    pub fn id_of(&self, name: &str) -> Option<u16> {
        let n: String = name.chars().take(15).collect();
        let n = if n.len() >= 4 && n.is_char_boundary(n.len() - 4) && n[n.len() - 4..].eq_ignore_ascii_case(".gat") { &n[..n.len() - 4] } else { &n[..] };
        let n: String = n.chars().take(11).collect();
        match self.by_name.get(&n) {
            Some(&m) => Some(m),
            None => crate::instance_map::registry_id_of(&n),
        }
    }
}

// ---------------------------------------------------------------------------------------------- script constants

static CONSTS_BIN: &[u8] = include_bytes!("../data/consts.bin");

pub struct Constant {
    pub value: i64,
    pub parameter: bool,
    pub deprecated: bool,
}

static CONSTS: OnceLock<HashMap<String, Constant>> = OnceLock::new();

/// script.cpp script_get_constant / script_get_parameter: names are case-insensitive and the first definition of a
/// name wins (add_str + script_set_constant_ refuse to overwrite).
pub fn consts() -> &'static HashMap<String, Constant> {
    CONSTS.get_or_init(|| {
        let b = CONSTS_BIN;
        assert!(&b[0..4] == b"LKCN", "consts.bin: bad magic");
        let count = u32::from_le_bytes(b[8..12].try_into().unwrap()) as usize;
        let mut p = 12;
        let mut m = HashMap::with_capacity(count);
        for _ in 0..count {
            let n = b[p] as usize;
            let name = crate::net::wire::from_latin1(&b[p + 1..p + 1 + n]).to_ascii_lowercase();
            p += 1 + n;
            let value = i64::from_le_bytes(b[p..p + 8].try_into().unwrap());
            let parameter = b[p + 8] != 0;
            let deprecated = b[p + 9] != 0;
            p += 10;
            m.entry(name).or_insert(Constant { value, parameter, deprecated });
        }
        m
    })
}

/// A non-parameter constant's value.
pub fn constant(name: &str) -> Option<i64> {
    consts().get(&name.to_ascii_lowercase()).filter(|c| !c.parameter).map(|c| c.value)
}

// ---------------------------------------------------------------------------------------------- monster names

static MOB_NAMES_BIN: &[u8] = include_bytes!("../data/mob_names.bin");

pub struct MobNames {
    pub by_id: HashMap<u32, String>,
    pub by_aegis: HashMap<String, u32>,
}

static MOB_NAMES: OnceLock<MobNames> = OnceLock::new();

pub fn mob_names() -> &'static MobNames {
    MOB_NAMES.get_or_init(|| {
        let b = MOB_NAMES_BIN;
        assert!(&b[0..4] == b"LKMN", "mob_names.bin: bad magic");
        let count = u32::from_le_bytes(b[8..12].try_into().unwrap()) as usize;
        let mut p = 12;
        let mut by_id = HashMap::with_capacity(count);
        let mut by_aegis = HashMap::with_capacity(count);
        for _ in 0..count {
            let id = u32::from_le_bytes(b[p..p + 4].try_into().unwrap());
            let n = b[p + 4] as usize;
            let name = crate::net::wire::from_latin1(&b[p + 5..p + 5 + n]);
            p += 5 + n;
            by_aegis.entry(name.to_ascii_lowercase()).or_insert(id);
            by_id.insert(id, name);
        }
        MobNames { by_id, by_aegis }
    })
}

/// mob.cpp mobdb_checkid (clones are never in the database here).
pub fn mobdb_checkid(id: i32) -> bool {
    id > 0 && mob_names().by_id.contains_key(&(id as u32))
}

/// mob.cpp mobdb_search_aegisname (case-insensitive).
pub fn mobdb_search_aegisname(name: &str) -> Option<u32> {
    mob_names().by_aegis.get(&name.to_ascii_lowercase()).copied()
}

// ---------------------------------------------------------------------------------------------- NPC script files

static NPCS_BIN: &[u8] = include_bytes!("../data/npcs.bin");

/// One NPC source file as rAthena loads it (npc/re/scripts_main.conf order).
pub struct NpcFile {
    pub path: String,
    pub text: Vec<u8>,
}

/// npcs.bin's header: (version, raw length, start-up file count, offset of the zlib stream). Version 1 holds only the
/// start-up files; version 2 (tools/datagen.py build_npcs) holds them first and then every other file rAthena ships
/// under npc/ (sorted by path), which only @loadnpc / @reloadnpcfile load (npc_load.rs).
fn npcs_header() -> (u32, usize, Option<usize>, usize) {
    let b = NPCS_BIN;
    assert!(&b[0..4] == b"LKNP", "npcs.bin: bad magic");
    let version = u32::from_le_bytes(b[4..8].try_into().unwrap());
    let raw_len = u32::from_le_bytes(b[8..12].try_into().unwrap()) as usize;
    if version >= 2 {
        (version, raw_len, Some(u32::from_le_bytes(b[12..16].try_into().unwrap()) as usize), 16)
    } else {
        (version, raw_len, None, 12)
    }
}

/// How many of [`npc_files`] are the start-up set (the files npc/re/scripts_main.conf registers, in load order: the
/// world npc.rs build_world makes, and the list map_reloadnpc makes again for @reloadscript).
pub fn npc_boot_count() -> usize {
    match npcs_header().2 {
        Some(n) => n,
        None => npc_files().len(),
    }
}

/// The NPC files, inflated: the start-up set first ([`npc_boot_count`]), then the others. Called when the world is
/// (re)built; the text is also what the script compiler reads (script/code.rs files()).
pub fn npc_files() -> Vec<NpcFile> {
    let b = NPCS_BIN;
    let (_, raw_len, _, z) = npcs_header();
    let raw = miniz_oxide::inflate::decompress_to_vec_zlib_with_limit(&b[z..], raw_len).expect("npcs.bin does not inflate");
    let count = u32::from_le_bytes(raw[0..4].try_into().unwrap()) as usize;
    let mut p = 4;
    let mut out = Vec::with_capacity(count);
    for _ in 0..count {
        let n = u16::from_le_bytes([raw[p], raw[p + 1]]) as usize;
        let path = String::from_utf8_lossy(&raw[p + 2..p + 2 + n]).into_owned();
        p += 2 + n;
        let len = u32::from_le_bytes(raw[p..p + 4].try_into().unwrap()) as usize;
        p += 4;
        out.push(NpcFile { path, text: raw[p..p + len].to_vec() });
        p += len;
    }
    out
}

/// A fingerprint of everything the world is built from: the world is rebuilt when a published module carries
/// different data (see world build in npc.rs). build.rs hashes every byte of every embedded data file (data/*.bin,
/// data/json/*) at compile time; WORLD_CODE_VERSION covers changes of the world-building code.
pub fn data_version() -> u64 {
    const FINGERPRINT: &str = env!("LOKI_DATA_FINGERPRINT");
    let mut h: u64 = 0;
    for b in FINGERPRINT.bytes() {
        h = h.wrapping_mul(10).wrapping_add((b - b'0') as u64);
    }
    h ^ crate::WORLD_CODE_VERSION
}

// ---------------------------------------------------------------------------------------------- map messages

static MSGS_BIN: &[u8] = include_bytes!("../data/msgs.bin");
static MSGS: OnceLock<HashMap<u16, String>> = OnceLock::new();

/// map.cpp msg_txt (conf/msg_conf/map_msg.conf); an unknown number gives "??" like rAthena's _msg_txt.
pub fn msg_txt(n: u16) -> &'static str {
    let m = MSGS.get_or_init(|| {
        let b = MSGS_BIN;
        assert!(&b[0..4] == b"LKMS", "msgs.bin: bad magic");
        let count = u32::from_le_bytes(b[8..12].try_into().unwrap()) as usize;
        let mut p = 12;
        let mut m = HashMap::with_capacity(count);
        for _ in 0..count {
            let id = u16::from_le_bytes([b[p], b[p + 1]]);
            let len = u16::from_le_bytes([b[p + 2], b[p + 3]]) as usize;
            // msg_conf.cpp _msg_config_read reads a line with sscanf("%7[^:]: %511[^\r\n]"): the blank after the
            // colon skips every leading space / tab of the text (" Drops:" is "Drops:" in game)
            let text = crate::net::wire::from_latin1(&b[p + 4..p + 4 + len]);
            m.insert(id, text.trim_start_matches([' ', '\t', '\x0b', '\x0c']).to_string());
            p += 4 + len;
        }
        m
    });
    m.get(&n).map(|s| s.as_str()).unwrap_or("??")
}

/// C printf for msg_txt formats: %s %d %u %i %% (the conversions rAthena's messages use), with the arguments as text.
pub fn format_msg(fmt: &str, args: &[&dyn std::fmt::Display]) -> String {
    let mut out = String::new();
    let mut it = fmt.chars().peekable();
    let mut ai = 0;
    while let Some(ch) = it.next() {
        if ch != '%' {
            out.push(ch);
            continue;
        }
        // flags / width / length modifiers
        let mut spec = String::new();
        while let Some(&n) = it.peek() {
            if n.is_ascii_digit() || matches!(n, '-' | '+' | ' ' | '.' | 'l' | 'h' | 'z' | '#') {
                spec.push(n);
                it.next();
            } else {
                break;
            }
        }
        match it.next() {
            Some('%') => out.push('%'),
            Some(_) => {
                if let Some(a) = args.get(ai) {
                    out.push_str(&a.to_string());
                }
                ai += 1;
            }
            None => out.push('%'),
        }
    }
    out
}

// ---------------------------------------------------------------------------------------------- JSON datasets

/// zlib-compressed JSON written by tools/datagen.py (write_json). Each dataset is parsed once, on first use.
pub fn json<T: serde::de::DeserializeOwned>(name: &str, z: &[u8]) -> T {
    let raw = miniz_oxide::inflate::decompress_to_vec_zlib(z).unwrap_or_else(|e| panic!("json/{name}: does not inflate ({e:?})"));
    serde_json::from_slice(&raw).unwrap_or_else(|e| panic!("json/{name}: {e}"))
}

/// `dataset!(fn_name, "file", Type)`: `fn_name()` returns data/json/file.json.z parsed as `Type` (once per instance).
/// Usable from any module (`crate::dataset!`); paths are relative to src/data.rs's crate root via CARGO_MANIFEST_DIR.
#[macro_export]
macro_rules! dataset {
    ($fn_name:ident, $file:literal, $ty:ty) => {
        pub fn $fn_name() -> &'static $ty {
            static CELL: std::sync::OnceLock<$ty> = std::sync::OnceLock::new();
            CELL.get_or_init(|| $crate::data::json($file, include_bytes!(concat!(env!("CARGO_MANIFEST_DIR"), "/data/json/", $file, ".json.z"))))
        }
    };
}

/// atcommand.cpp atcommand_basecommands + conf/atcommands.yml aliases.
#[derive(serde::Deserialize)]
pub struct AtCommands {
    /// (name, rAthena function, ATCMD_NO* restriction bits: 1 console, 2 script, 4 autotrade).
    pub commands: Vec<(String, String, u8)>,
    /// alias -> command (case-sensitive, AtcommandAliasDatabase::checkAlias).
    pub aliases: HashMap<String, String>,
    /// command (as conf/atcommands.yml writes it) -> Help text (atcommand_help_string).
    pub help: HashMap<String, String>,
}

impl AtCommands {
    /// AtcommandAliasDatabase::checkAlias.
    pub fn check_alias<'a>(&'a self, alias: &'a str) -> &'a str {
        self.aliases.get(alias).map(|s| s.as_str()).unwrap_or(alias)
    }
    /// get_atcommandinfo_byname (the command database is case-insensitive).
    pub fn info(&self, name: &str) -> Option<&(String, String, u8)> {
        if crate::release::hides_command(name) {
            return None; // Loki's test-rig command (@lokirnd) is no command at all on a release server (release.rs)
        }
        self.commands.iter().find(|c| c.0.eq_ignore_ascii_case(name))
    }
    /// atcommand_help_string.
    pub fn help_string(&self, command: &str) -> Option<&str> {
        let command = command.strip_prefix(['@', '#']).unwrap_or(command);
        let command = self.check_alias(command);
        self.help.get(command).map(|s| s.as_str())
    }
}
dataset!(atcommands, "atcommands", AtCommands);

/// pc_groups.cpp s_player_group after loading (conf/groups.yml + import, inheritance applied).
#[derive(serde::Deserialize)]
pub struct PlayerGroup {
    pub id: u32,
    pub name: String,
    pub level: u32,
    pub log_commands: bool,
    pub commands: Vec<String>,
    pub char_commands: Vec<String>,
    /// e_pc_permission bits.
    pub permissions: u64,
}
dataset!(groups, "groups", Vec<PlayerGroup>);
// rAthena's own conf/groups.yml without Loki's import: the groups of a release server (release.rs, rules::groups).
dataset!(groups_stock, "groups_stock", Vec<PlayerGroup>);

// db/re/job_noenter_map.txt (+ import): (job, zone bits, group level that bypasses it).
dataset!(job_noenter_map, "job_noenter_map", Vec<(i32, i32, i32)>);

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_mapstamp_file_gives_its_stamp_and_label() {
        assert_eq!(parse_map_stamp("stamp: g1-603785af4ecfea8d\nlabel: patch 1486\nstamp_maps: 965\n"), Some(("g1-603785af4ecfea8d", "patch 1486")));
        assert_eq!(parse_map_stamp("stamp_maps: 965\r\nlabel: patch 1486\r\nstamp: g1-603785af4ecfea8d\r\n"), Some(("g1-603785af4ecfea8d", "patch 1486")));
        // an install without a patch.inf has a stamp and no label
        assert_eq!(parse_map_stamp("stamp: g1-603785af4ecfea8d\nlabel:\n"), Some(("g1-603785af4ecfea8d", "")));
        assert_eq!(parse_map_stamp("stamp: g1-603785af4ecfea8d\n"), Some(("g1-603785af4ecfea8d", "")));
        assert_eq!(parse_map_stamp("stamp:\nlabel: patch 1486\n"), None);
        assert_eq!(parse_map_stamp("stamp_maps: 965\n"), None);
    }

    /// The default build embeds rAthena's stock maps and claims no install's stamp (the parity suites' build).
    #[cfg(not(feature = "install_maps"))]
    #[test]
    fn the_default_build_has_the_stock_maps_and_no_stamp() {
        assert!(map_stamp().is_none());
        assert!(MAPS_BIN == include_bytes!("../data/maps.bin").as_slice());
    }

    /// The install build: the same maps under the same ids as the stock build; a map differs from rAthena's only in
    /// its size or in which cells are walkable (the import cache's rule, tools/mapcache/build_import_cache.py: a map
    /// whose water flags alone differ is left as rAthena has it), exactly the maps mapstamp.txt lists.
    #[cfg(feature = "install_maps")]
    #[test]
    fn the_install_maps_differ_from_the_stock_maps_only_by_the_import_rule() {
        // map_gat2cell (map.cpp:3280-3299): types 0, 2, 3, 4, 6 are walkable
        let walkable = |t: &u8| matches!(*t, 0 | 2 | 3 | 4 | 6);
        let stock = parse_maps(include_bytes!("../data/maps.bin"));
        let install = &maps().list;
        assert_eq!(stock.len(), install.len());
        let (mut resized, mut rewalked) = (Vec::new(), Vec::new());
        for (a, b) in stock.iter().zip(install.iter()) {
            assert_eq!((a.id, &a.name), (b.id, &b.name));
            assert_eq!(b.cells().len(), b.xs as usize * b.ys as usize, "{}", b.name);
            if (a.xs, a.ys) != (b.xs, b.ys) {
                resized.push(b.name.clone());
            } else if a.cells() != b.cells() {
                assert!(a.cells().iter().map(walkable).ne(b.cells().iter().map(walkable)), "{}: only non-walkability bytes differ", b.name);
                rewalked.push(b.name.clone());
            }
        }
        let line = |key: &str| MAP_STAMP_TXT.lines().find_map(|l| l.strip_prefix(key)).unwrap_or("").to_string();
        let mut listed_walk: Vec<String> = line("walkable_cells_changed:").split_whitespace().map(|e| e.rsplit_once(':').map(|(n, _)| n).unwrap_or(e).to_string()).collect();
        let mut listed_size: Vec<String> = line("size_changed:").split_whitespace().filter(|e| !e.contains("->")).map(str::to_string).collect();
        rewalked.sort();
        resized.sort();
        listed_walk.sort();
        listed_size.sort();
        assert!(!rewalked.is_empty());
        assert_eq!(rewalked, listed_walk);
        assert_eq!(resized, listed_size);
        let (stamp, label) = map_stamp().expect("the install build embeds its stamp");
        assert!(stamp.starts_with("g1-") && stamp.len() == 19, "{stamp}");
        assert!(label.is_empty() || label.starts_with("patch "), "{label}");
    }
}
