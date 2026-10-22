//! Updates without wiping: the host's export and import of everything players own. Loki's own: rAthena has no such
//! code, because its player data lives in MySQL, outside the servers, and a new build simply opens the same tables.
//! Here the tables live inside the module's database, and a module update that changes a table incompatibly can only
//! be published with `--delete-data`. This file is what lets a host do that without losing the players:
//!
//! 1. **close** (`cutover_freeze true`): what rAthena's shutdown does. Every character leaves the world saved
//!    (map.cpp:5316-5325 MapServer::handle_shutdown: clif_GM_kick for every user; map.cpp:5013-5017
//!    MapServer::finalize: map_quit for whoever is left), the char server writes the guilds it had not saved yet
//!    (char/inter.cpp:1000-1018 inter_final → int_guild.cpp:809-820 inter_guild_sql_final), and from then on the
//!    database stands still: logins and the character screen are refused ([`refuse_if_frozen`], release.rs
//!    check_client) and no timer runs (timer.rs on_wakeup), so the tables the owner reads next are one consistent state.
//! 2. **export**: the owner reads the tables of [`TABLES`] whose class is exported, with SQL (SpacetimeDB lets the
//!    identity that published a database read its private tables; everyone else gets "no such table"), and writes them
//!    to one file (tools/host/loki_host.py). Nothing in the module is needed for the reading itself; the module only
//!    says WHICH tables ([`cutover_manifest`], written by the freeze).
//! 3. **publish** the new module with `--delete-data`.
//! 4. **import** (`cutover_begin`, `cutover_chunk` …, `cutover_commit`): the file is staged in a private table and
//!    committed in ONE transaction: every row is decoded by this module's own row types (a column that is missing,
//!    unknown, of another type or out of range refuses the whole file, naming the table and the column), every id a
//!    player or another row can refer to is kept (account, character, guild, party, pet, homunculus, mercenary,
//!    elemental, mail, auction, the items' SQL ids), the auto-increment handles nothing refers to are handed out
//!    again in their old order, and the id counters go on after the highest id (ids.rs: the next account, character,
//!    guild … never collides). Then the servers "start": the char server's caches are dropped and the world is built
//!    again (npc.rs build_world), which is what rAthena does after its SQL tables are restored: castles, guild flags,
//!    the permanent `$` variables' OnInit readers, the fame lists and the auction timers all come from the imported
//!    rows.
//!
//! **What is exported** is rAthena's own rule for what a restart keeps: the tables that are its SQL tables (login,
//! char, inventory … guild, party, mail, pet, the registries, mapreg), plus Loki's account tables. What rAthena holds
//! in memory (map_session_data, the world, the caches, timers) is not exported; [`TABLES`] has a line for EVERY table
//! of the module with the reason, and a unit test fails when a table is added without one.
//!
//! **The file's version rule** ([`VERSION`]): the file names its format and version. This module reads versions
//! [`MIN_VERSION`] ..= [`VERSION`]; an older one first goes through [`upgrade`] (one step per version, written with
//! the change that needed it); a newer one is refused ("made by a newer Loki"). Rows are JSON objects keyed by column
//! name at every depth (the names the database's schema gives: SpacetimeDB writes `card0` as `card_0`, so names are
//! compared without their underscores and case, [`to_module_names`]), so the strict decode is the safety net: a
//! table whose columns changed without an upgrade step refuses the file instead of loading it wrong. Adding a table
//! or a class of tables needs no new version (a file without it leaves it empty).

use std::collections::{HashMap, HashSet};

use spacetimedb::sats::serde::SerdeWrapper;
use spacetimedb::sats::typespace::TypespaceBuilder;
use spacetimedb::sats::AlgebraicType;
use spacetimedb::{reducer, table, ReducerContext, SpacetimeType, Table, Timestamp};

use crate::world::Ctx;

/// The file's `format`.
pub const FORMAT: &str = "loki-cutover";
/// The file version this module writes the manifest for and reads without an upgrade.
pub const VERSION: u32 = 1;
/// The oldest file version [`upgrade`] can still bring up to [`VERSION`].
pub const MIN_VERSION: u32 = 1;

/// The text a client reads while the server is closed for an export or an import.
pub const FROZEN_TEXT: &str = "The server is closed for an update. Try again in a few minutes.";

// ---------------------------------------------------------------------------------------------- the table list

/// What the export does with a table.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Class {
    /// What a player owns: always exported.
    Player,
    /// Server-wide state rAthena keeps in SQL and the players' data depends on: always exported.
    Server,
    /// rAthena's log tables (sql-files/logs.sql): exported only when the host asks for them.
    Log,
    /// Not exported; `why` says why.
    No,
}

impl Class {
    pub fn name(self) -> &'static str {
        match self {
            Class::Player => "player",
            Class::Server => "server",
            Class::Log => "log",
            Class::No => "no",
        }
    }
}

/// One table of the module.
pub struct Spec {
    pub table: &'static str,
    pub class: Class,
    /// The auto-increment handle column the import hands out again in the old order ("" = the table has none, or its
    /// key is kept).
    pub auto: &'static str,
    /// Columns the import resets, as `column=JSON` pairs separated by `;` (the export tool writes the same values, so
    /// the file never holds them).
    pub reset: &'static str,
    pub why: &'static str,
}

const fn p(table: &'static str, auto: &'static str, why: &'static str) -> Spec {
    Spec { table, class: Class::Player, auto, reset: "", why }
}
const fn pr(table: &'static str, auto: &'static str, reset: &'static str, why: &'static str) -> Spec {
    Spec { table, class: Class::Player, auto, reset, why }
}
const fn s(table: &'static str, auto: &'static str, why: &'static str) -> Spec {
    Spec { table, class: Class::Server, auto, reset: "", why }
}
const fn l(table: &'static str, auto: &'static str, why: &'static str) -> Spec {
    Spec { table, class: Class::Log, auto, reset: "", why }
}
const fn n(table: &'static str, why: &'static str) -> Spec {
    Spec { table, class: Class::No, auto: "", reset: "", why }
}

// the reasons that repeat
const SD: &str = "map_session_data of a character in the world (the freeze takes everyone out saved; the row is made again at login)";
const WORLD: &str = "the world, built from the embedded data when the database is published and at every world rebuild";
const MEM: &str = "server memory that rAthena loses at a restart too";
const CACHE: &str = "a server's memory copy of exported rows, loaded from them again on first use";
const LOGIN: &str = "login-server memory, minutes long";
const TIMER: &str = "a timer or its bookkeeping: armed again when the servers start";
const HOST: &str = "the host's own setting, not a player's";

/// EVERY table of the module, in the order of the source files (a unit test compares the list with the `#[table]`
/// attributes under src/). The file:line of each definition is in server/ARCHITECTURE.md.
pub const TABLES: &[Spec] = &[
    // account.rs
    p("account", "", "rAthena's `login` table: name, Argon2id password hash, sex, group"),
    pr("session", "", "selected_char={\"none\":[]};online=false;connection={\"none\":[]}", "the identities that stay logged in to an account (\"continue as before\"); the connection state is reset"),
    n("login_source", LOGIN),
    p("account_device", "id", "the identities an account trusts (its lock does not stop them)"),
    n("reg_flood", LOGIN),
    n("login_notice", LOGIN),
    // achievement.rs
    n("pc_achievement", SD),
    p("achievement", "id", "the char server's `achievement` table"),
    s("achievement_slots", "", "the `achievement` table's record slots: they decide the order new rows are read in"),
    // atcmd_pc.rs, atcommand_accounts.rs, atcommand_admin.rs
    n("pc_atcmd", SD),
    n("atcmd_map", MEM),
    p("login_ext", "", "the other `login` columns: e-mail, state (a block), unban time, login count"),
    n("db_reloaded", MEM),
    // auction.rs, autosave.rs, autotrade.rs
    n("battle_config_auction", "battle_set_value's changes: rAthena reads its conf again at a start"),
    n("pc_auction", SD),
    n("autosave_state", TIMER),
    n("pc_autotrade", SD),
    n("autotrader_vending", MEM),
    n("autotrader_buyingstore", MEM),
    n("buyingstores", "an open buying store: the freeze closes every shop (the zeny and items are the character's)"),
    n("buyingstore_items", "an open buying store: the freeze closes every shop"),
    // barter.rs, battle/, battle_config/, battleground.rs, buyingstore.rs
    s("barter_stock", "", "rAthena's `barter` table: what players bought from the stock-limited barter NPCs"),
    n("pc_battle", SD),
    n("battle_delay_damage", TIMER),
    n("battle_config", "the conf/battle settings that differ from the built-in ones: made again by the conf read of every world build (battle_set_value's changes go, as at an rAthena start)"),
    n("battle_config_state", "the stamp of those rows and the rates mob_db was loaded with: written with them"),
    n("battle_conf_import", "the host's conf/import/battle_conf.txt: given again with `set_battle_conf` after a fresh publish (tools/host/README.md)"),
    n("pc_bg", SD),
    n("bg_team", MEM),
    n("bg_queue", MEM),
    n("bg_map_reserve", MEM),
    n("bg_score", MEM),
    n("bg_state", MEM),
    n("pc_buyingstore", SD),
    // cart.rs, cashshop.rs, channel.rs, character.rs, chat.rs, clan.rs, clif_client.rs, duel.rs
    p("cart_inventory", "id", "rAthena's `cart_inventory` table"),
    n("pc_points", "the session's copy of #CASHPOINTS / #KAFRAPOINTS / #BANKVAULT: the values are acc_reg_num rows, which are exported"),
    n("cash_sale", "the cash shop's sales (`sales` table): the host's setting; listed under what is left"),
    n("channel", "chat channels: rAthena keeps them in memory, a player's own channel ends with the server"),
    n("channel_init", WORLD),
    p("character", "", "rAthena's `char` table"),
    n("chat_room", MEM),
    n("clan_state", "the map server's part of a clan (who is online): the membership is character.clan_id"),
    n("pc_clan", SD),
    n("clan_cleanup_timer", TIMER),
    n("pc_client", SD),
    n("nick_db", CACHE),
    n("duel", MEM),
    n("pc_duel", SD),
    // elemental/, fame.rs, flooritem.rs, guild.rs, guild_storage.rs
    n("elemental_data", "the summoned elemental in the world: its saved row is `elemental`"),
    n("elemental_ai_state", TIMER),
    n("fame_list", "the rankings: built again from character.fame the first time they are needed"),
    n("flooritem", "items lying on the ground: gone at an rAthena restart too"),
    n("agit_state", MEM),
    n("guild_db", CACHE),
    n("pc_guild", SD),
    n("guild_expcache", "guild EXP not paid out yet: rAthena drops it at shutdown (guild.cpp:2758)"),
    n("guild_infoevent", MEM),
    n("guild_state", TIMER),
    n("guild_storage", CACHE),
    n("guild_storage_item", "the open guild storage: written to guild_storage_sql when it closes (the freeze closes it)"),
    p("guild_storage_sql", "", "rAthena's `guild_storage` table"),
    p("guild_storage_log", "", "rAthena's `guild_storage_log` table (the log players read in the guild storage window)"),
    // homunculus/, horde/, hotkey.rs, ids.rs, instance*.rs
    n("hom_data", "the homunculus in the world: its saved row is `homunculus`"),
    n("horde_peer", "the Horde channel's connected clients"),
    n("horde_out", "an event table: never stored"),
    n("horde_run", "the Horde run being gathered or played: its simulation is the module instance's memory, and Loki Host loses a run at a restart too"),
    n("horde_member", "the players of that run"),
    n("horde_op", "what clients asked of that run, until its scheduled call applies it"),
    n("horde_tick", TIMER),
    n("horde_config", "the host's Horde switch and settings: set again with `horde_settings` after a fresh publish"),
    n("horde_state", "the Horde run counter, the last run's line and the service's measurements since the database was published (a reward row's run number is only its label)"),
    p("horde_reward", "id", "a Horde reward decided for a player: one that still waits (the player was offline, dead or off a map at the run's end) is owed at the next map entry"),
    p("hotkey", "id", "rAthena's `hotkey` table"),
    s("id_counter", "", "the AUTO_INCREMENT counters; only the kinds of exported ids are read (PERSISTENT_COUNTERS), each raised past the highest imported id"),
    n("instance_data", MEM),
    n("instance_state", MEM),
    n("pc_instance", SD),
    n("instance_map", MEM),
    n("instance_mapindex", MEM),
    n("instance_map_gen", MEM),
    n("npc_instance", MEM),
    // int_auction.rs, int_elemental.rs, int_guild.rs, int_homun.rs, int_mail.rs, int_mercenary.rs, int_party.rs, int_pet.rs
    pr("auction", "", "auction_end_timer=0", "rAthena's `auction` table; the end timers are armed again from each auction's end time"),
    n("auction_mem", CACHE),
    p("elemental", "", "rAthena's `elemental` table"),
    p("guild_sql", "", "rAthena's `guild` table"),
    p("guild_member_sql", "id", "rAthena's `guild_member` table"),
    p("guild_position_sql", "id", "rAthena's `guild_position` table"),
    p("guild_alliance_sql", "id", "rAthena's `guild_alliance` table"),
    p("guild_expulsion_sql", "id", "rAthena's `guild_expulsion` table"),
    p("guild_skill_sql", "id", "rAthena's `guild_skill` table"),
    n("inter_guild", "the char server's guild cache: the freeze writes its unsaved changes to the guild tables first"),
    n("inter_guild_state", TIMER),
    p("homunculus", "", "rAthena's `homunculus` table"),
    p("skill_homunculus", "row_id", "rAthena's `skill_homunculus` table"),
    p("skillcooldown_homunculus", "row_id", "rAthena's `skillcooldown_homunculus` table"),
    p("mail", "", "rAthena's `mail` and `mail_attachments` tables"),
    n("mail_char_state", TIMER),
    p("mercenary", "", "rAthena's `mercenary` table"),
    p("mercenary_owner", "", "rAthena's `mercenary_owner` table"),
    p("skillcooldown_mercenary", "id", "rAthena's `skillcooldown_mercenary` table"),
    p("party", "", "rAthena's `party` table (the members are character.party_id)"),
    n("inter_party", CACHE),
    n("inter_party_config", MEM),
    p("pet", "", "rAthena's `pet` table"),
    // inventory.rs, itemgroup.rs, itemui/
    p("inventory", "id", "rAthena's `inventory` table"),
    n("pc_items", SD),
    n("item_cd", "item cooldowns kept between logins while the server runs (pc.cpp itemcd_db): memory"),
    n("itemdb_runtime", MEM),
    n("item_group_pool", MEM),
    n("itemdb_edit", MEM),
    n("itemdb_edit_state", MEM),
    n("pc_itemui", SD),
    // log.rs
    n("log_conf", HOST),
    l("atcommandlog", "atcommand_id", "logs.sql `atcommandlog`"),
    l("branchlog", "branch_id", "logs.sql `branchlog`"),
    l("cashlog", "id", "logs.sql `cashlog`"),
    l("chatlog", "id", "logs.sql `chatlog`"),
    l("feedinglog", "id", "logs.sql `feedinglog`"),
    l("mvplog", "mvp_id", "logs.sql `mvplog`"),
    l("npclog", "npc_id", "logs.sql `npclog`"),
    l("picklog", "id", "logs.sql `picklog`"),
    l("zenylog", "id", "logs.sql `zenylog`"),
    // macro_detector.rs, mail.rs, map*.rs, mercenary/, mob*.rs, net/
    n("captcha", MEM),
    n("captcha_order", MEM),
    n("pc_macro", SD),
    n("pc_mail", SD),
    n("block_seq", WORLD),
    n("object_id_state", WORLD),
    n("map_cell_mod", WORLD),
    n("map_cell_gen", WORLD),
    n("iwall", WORLD),
    n("map_damage_adjust", WORLD),
    n("map_skill_damage", WORLD),
    n("map_skill_damage_order", WORLD),
    n("mercenary_data", "the mercenary in the world: its saved row is `mercenary`"),
    n("pc_mercenary", SD),
    n("mob_data", WORLD),
    n("mob_map", WORLD),
    n("mob_ai_state", TIMER),
    n("mob_drop_list", MEM),
    n("pc_kill", SD),
    n("mvp_tomb", MEM),
    n("mob_db_rt", MEM),
    n("item_mob_rt", MEM),
    n("rfifo", "a connection's unread bytes"),
    // npc.rs, npc_load.rs
    n("npc_shop_family", WORLD),
    n("npc", WORLD),
    n("npc_function", WORLD),
    n("mapflag", WORLD),
    n("map_save_point", WORLD),
    n("spawn_data", WORLD),
    n("world_state", WORLD),
    n("npc_shop_sold", WORLD),
    n("npc_src_file", WORLD),
    n("npc_src_vfile", WORLD),
    n("barter_npc", WORLD),
    n("npc_dynamic", MEM),
    n("pc_npc_dynamic", SD),
    // party.rs, pc.rs, pc_misc.rs, pc_taekwon.rs, pet/, presence.rs
    n("party_data", CACHE),
    n("party_state", TIMER),
    n("party_booking", "the party booking board: rAthena keeps it in memory"),
    n("party_bookings", "rAthena's web server writes it; nothing in the module does"),
    n("pc", SD),
    n("pc_misc", SD),
    n("daynight", MEM),
    n("npc_misc", WORLD),
    n("unit_hat", MEM),
    n("atcmd_binding", WORLD),
    p("char_memo", "", "rAthena's `memo` table (Warp Portal points)"),
    n("npc_chat_set", WORLD),
    n("pc_taekwon", "the session's copy of the feel / hate / mission variables: they are char_reg_num rows, which are exported"),
    n("pet_data", "the pet in the world: its saved row is `pet`"),
    n("pet_catch_process", MEM),
    n("pet_delayed_drop", MEM),
    n("pet_ai_state", TIMER),
    p("account_settings", "", "Loki's per-account settings and Codex journal (LokiGame.W18Account.cs)"),
    n("presence_here", "the headset channel's connected clients"),
    n("presence_out", "an event table: never stored"),
    n("presence_config", "the host's switches for headsets and voice: set again with SQL after a fresh publish (tools/host/README.md)"),
    n("presence_stats", "the voice counters since the database was published"),
    // quest.rs, release.rs, roulette.rs
    n("pc_quest", SD),
    p("quest", "id", "the char server's `quest` table"),
    n("npc_questinfo", WORLD),
    n("map_qi_npc", WORLD),
    s("server_config", "", "the release switch (test / release, the administrator's account): a fresh publish starts as test, where every player has every command"),
    n("server_owner", "who published THIS database: the new one records its own publisher"),
    n("client_hello", "a connection's handshake"),
    n("client_release", "published by publish_release.py after each release"),
    n("pc_roulette", "the session's copy of RouletteBronze / Silver / Gold: they are char_reg_num rows, which are exported"),
    // script/
    n("userfunc_def", WORLD),
    n("pc_script", SD),
    n("npc_scr", WORLD),
    n("npc_event", WORLD),
    n("ev_db_removed", WORLD),
    n("ev_db_op", WORLD),
    n("npc_timer_ted", TIMER),
    n("script_world", WORLD),
    p("char_reg_num", "id", "rAthena's `char_reg_num` table (permanent character variables)"),
    p("char_reg_str", "id", "rAthena's `char_reg_str` table"),
    n("char_tmp_reg_num", "`@` variables: cleared at logout"),
    n("char_tmp_reg_str", "`@` variables: cleared at logout"),
    p("acc_reg_num", "id", "rAthena's `acc_reg_num` table (`#` account variables: cash points, the bank vault)"),
    p("acc_reg_str", "id", "rAthena's `acc_reg_str` table"),
    p("global_acc_reg_num", "id", "rAthena's `global_acc_reg_num` table (`##` variables)"),
    p("global_acc_reg_str", "id", "rAthena's `global_acc_reg_str` table"),
    s("mapreg_num", "id", "rAthena's `mapreg` table (permanent `$` variables; the `$@` rows in the same table are never saved and are dropped)"),
    s("mapreg_str", "id", "rAthena's `mapreg` table, the strings"),
    n("npc_reg_num", "`.` NPC variables: memory"),
    n("npc_reg_str", "`.` NPC variables: memory"),
    n("instance_reg_num", "`'` instance variables: memory"),
    n("instance_reg_str", "`'` instance variables: memory"),
    n("script_state", "running scripts"),
    // searchstore.rs, session.rs, shop.rs, skill*.rs, skillunit/, social.rs
    n("pc_searchstore", SD),
    n("session_eof", "connections being closed"),
    s("market", "id", "rAthena's `market` table: the stock of the market NPCs (the world rebuild reads it before the NPC files load, shop.rs npc_market_fromsql)"),
    p("char_skill", "id", "rAthena's `skill` table"),
    n("skill_scd", SD),
    p("skillcooldown", "id", "rAthena's `skillcooldown` table (the cooldowns left at logout)"),
    n("pc_skill", SD),
    n("skill_timerskill", TIMER),
    n("unit_shadow_scar", MEM),
    n("pc_skilltree", SD),
    n("skill_partner_state", MEM),
    n("skill_unit_group", MEM),
    n("skill_unit", MEM),
    n("skill_unit_tickset", MEM),
    n("skill_usave", "skillusave_db: memory"),
    n("map_skill_duration", WORLD),
    n("map_skill_duration_order", WORLD),
    n("skill_unit_state", TIMER),
    n("skillunit_db_node", MEM),
    n("skillunit_db_root", MEM),
    n("skillunit_db_free", MEM),
    n("pc_social", SD),
    p("char_friends", "", "rAthena's `friends` table"),
    // status/, storage.rs, timer*.rs, trade.rs, unit.rs, vending.rs
    n("pc_status", SD),
    n("pc_regen_tick", SD),
    n("sc_entry", "the status changes of units in the world: a character's are saved to sc_saved when it leaves"),
    n("sc_unit", MEM),
    p("sc_saved", "id", "the char server's `sc_data` table (status changes that go on at the next login)"),
    s("sc_data_slots", "", "the `sc_data` table's record slots"),
    pr("bonus_script", "id", "tid=0;online=false", "rAthena's `bonus_script` table (the remaining time of each)"),
    n("status_world", TIMER),
    n("sc_delay", TIMER),
    p("storage", "id", "rAthena's `storage` table (the account's Kafra storage)"),
    n("pc_premium_storage", SD),
    n("timer", TIMER),
    n("wakeup", TIMER),
    n("timer_state", TIMER),
    n("timer_heap", TIMER),
    n("timer_heap_built", TIMER),
    n("pc_trade", SD),
    n("unit", WORLD),
    n("pc_vending", SD),
    n("pc_cart_ids", SD),
    n("vendings", "an open shop: the freeze closes every shop (the items are in the cart, which is exported)"),
    n("vending_items", "an open shop: the freeze closes every shop"),
    // woe*.rs, world.rs
    n("guild_castle_rt", CACHE),
    n("guild_flags", WORLD),
    n("npc_guild", WORLD),
    n("guardian_data", WORLD),
    p("guild_castle_sql", "", "rAthena's `guild_castle` table (who owns each castle, its economy and defence)"),
    n("inter_castle", CACHE),
    n("packet_out", "an event table: never stored"),
    n("debug_rng", "the test rig's fixed roll"),
    // cutover.rs
    n("cutover_state", "this tool's own state"),
    n("cutover_stage", "this tool's own staging rows"),
    n("cutover_manifest", "this list, as the export tool reads it"),
];

/// The id_counter kinds (ids.rs) whose ids are exported: the import takes the file's counter and raises it past the
/// highest imported id of its table. account.rs:338, character.rs:376, inventory.rs:726 + character.rs:388,
/// int_guild.rs:450, guild_storage.rs:682 / :208, int_party.rs:250, int_pet.rs:112, int_homun.rs:303,
/// int_mercenary.rs:141, int_elemental.rs:88, int_mail.rs:359, int_auction.rs:325.
pub const PERSISTENT_COUNTERS: &[&str] = &["account", "char", "item_sql_id", "guild", "guild_storage_sql", "guild_storage_log", "party", "pet", "homunculus", "mercenary", "elemental", "mail_id", "auction_id"];
/// The other kinds: ids of things that do not outlive a server start (shop and duel ids, the world's own sequences).
/// "npc_id" (mob/spawn.rs:31-62, npc_get_new_npc_id's cursor), "npc_src_vfile" (npc_load.rs VFILE_COUNTER) and
/// "userfunc_def_gen" (script/code.rs DEFS_GEN) are rows of the same table written without ids::next.
pub const RUNTIME_COUNTERS: &[&str] = &["buyer_id", "vender_id", "cash_sale_order", "duel_id", "block_seq", "ev_db_rt_seq", "npc_id"];

pub fn spec(table: &str) -> Option<&'static Spec> {
    TABLES.iter().find(|s| s.table == table)
}

/// The list as JSON (tools/host/tables.json: what the export tool falls back on for a database whose module is older
/// than this file; a unit test keeps the committed copy equal to this).
pub fn manifest_json() -> String {
    let mut out = String::from("{\n  \"format\": \"loki-cutover\",\n  \"version\": ");
    out.push_str(&VERSION.to_string());
    out.push_str(",\n  \"tables\": [\n");
    for (i, s) in TABLES.iter().enumerate() {
        let row = serde_json::json!({ "table": s.table, "class": s.class.name(), "auto": s.auto, "reset": s.reset, "why": s.why });
        out.push_str("    ");
        out.push_str(&row.to_string());
        out.push_str(if i + 1 < TABLES.len() { ",\n" } else { "\n" });
    }
    out.push_str("  ]\n}\n");
    out
}

// ---------------------------------------------------------------------------------------------- state

/// This tool's state (key 0). Private.
#[table(accessor = cutover_state)]
#[derive(Clone, Debug)]
pub struct CutoverState {
    #[primary_key]
    pub key: u8,
    /// The server is closed: no login, no character screen, no timer.
    pub frozen: bool,
    /// An import has begun and is not committed yet.
    pub importing: bool,
    /// The header of the file being imported.
    pub header: String,
    pub since: Timestamp,
    /// What the last freeze or commit did, as JSON (the host's tool prints it).
    pub report: String,
}

/// A staged piece of the file being imported.
#[table(accessor = cutover_stage)]
#[derive(Clone, Debug)]
pub struct CutoverStage {
    #[primary_key]
    pub seq: u32,
    pub table: String,
    pub rows: u32,
    /// A JSON array of rows, each an object keyed by column name.
    pub json: String,
}

/// [`TABLES`] as rows, for the export tool (written by the freeze and by `cutover_begin`).
#[table(accessor = cutover_manifest)]
#[derive(Clone, Debug)]
pub struct CutoverManifest {
    #[primary_key]
    pub table: String,
    pub class: String,
    pub auto: String,
    pub reset: String,
    pub why: String,
}

fn state(db: &spacetimedb::Local) -> Option<CutoverState> {
    db.cutover_state().key().find(0)
}

fn put_state(db: &spacetimedb::Local, row: CutoverState) {
    if db.cutover_state().key().find(0).is_some() {
        db.cutover_state().key().update(row);
    } else {
        db.cutover_state().insert(row);
    }
}

/// Is the server closed for an export or an import (timer.rs on_wakeup: no timer runs meanwhile).
pub fn frozen(db: &spacetimedb::Local) -> bool {
    state(db).map(|s| s.frozen).unwrap_or(false)
}

/// release.rs check_client (`login`, `register`, every character-screen reducer): nobody comes in while the server is
/// closed.
pub fn refuse_if_frozen(ctx: &ReducerContext) -> Result<(), String> {
    if frozen(&ctx.db) {
        return Err(FROZEN_TEXT.into());
    }
    Ok(())
}

fn write_manifest(ctx: &ReducerContext) {
    for t in ctx.db.cutover_manifest().iter().map(|r| r.table).collect::<Vec<_>>() {
        ctx.db.cutover_manifest().table().delete(t);
    }
    for s in TABLES {
        ctx.db.cutover_manifest().insert(CutoverManifest { table: s.table.into(), class: s.class.name().into(), auto: s.auto.into(), reset: s.reset.into(), why: s.why.into() });
    }
}

// ---------------------------------------------------------------------------------------------- closing and opening

/// rAthena's shutdown for the players and the char server's unsaved guilds; returns (characters taken out, guilds
/// written).
fn shutdown(c: &Ctx) -> (u32, u32) {
    use crate::int_guild::{inter_guild, inter_guild_tosql, put_cache, GS_MASK};
    use crate::pc::pc;
    // MapServer::handle_shutdown (map.cpp:5316-5325): clif_GM_kick(nullptr, sd) for every user, in pc_db's order.
    // rAthena's kick by the server also sets keepshop, so that an autotrader's shop is still in SQL for the next
    // start; an export carries no open shop, so the shops close here as at any other quit (vending.rs / autotrade.rs)
    let mut users: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
    users.sort_by_key(|&aid| crate::party::idb_order(aid));
    for &aid in &users {
        if crate::pc::is_active_session(c, aid) {
            crate::session::clif_authfail_fd(c, aid, 15); // clif_GM_kick: clif_authfail_fd(fd, 15)
        }
    }
    // do_sockets: clif_parse closes the sessions that were marked (clif_quitsave)
    crate::session::do_eof(c);
    // MapServer::finalize (map.cpp:5013-5017): map_quit for everyone who is still there (prevent_logout's 10 s,
    // characters without a client)
    for &aid in &users {
        if crate::pc::get(c, aid).is_some() {
            crate::pc::quit(c, aid);
        }
    }
    // inter_final → inter_guild_sql_final (int_guild.cpp:809-820): the guilds with unsaved changes go to SQL
    let mut saved = 0;
    let mut ids: Vec<u32> = c.db().inter_guild().iter().map(|g| g.guild_id).collect();
    ids.sort_unstable();
    for id in ids {
        let Some(mut g) = c.db().inter_guild().guild_id().find(id) else { continue };
        if g.save_flag & GS_MASK != 0 {
            let flag = g.save_flag & GS_MASK;
            inter_guild_tosql(c, &mut g.guild, flag);
            g.save_flag &= !GS_MASK;
            put_cache(c, &g);
            saved += 1;
        }
    }
    (users.len() as u32, saved)
}

/// Nobody is at the character screen with a character picked (pc.rs clif_parse_WantToConnection lets a client in on
/// `selected_char`).
fn clear_selections(ctx: &ReducerContext) {
    use crate::account::session;
    for mut s in ctx.db.session().iter().filter(|s| s.selected_char.is_some()).collect::<Vec<_>>() {
        s.selected_char = None;
        ctx.db.session().identity().update(s);
    }
}

/// The owner closes (`on` true) or opens the server.
/// Closing: every character leaves the world saved, unsaved guilds are written, logins and the character screen are
/// refused and the timers stop, so that the tables can be read as one state; the table list is written for the export
/// tool. Opening: the timers run again.
#[reducer]
pub fn cutover_freeze(ctx: &ReducerContext, on: bool) -> Result<(), String> {
    crate::release::require_owner(ctx, "close or open the server")?;
    let old = state(&ctx.db);
    if old.as_ref().map(|s| s.importing).unwrap_or(false) {
        return Err("An import has begun: commit it (cutover_commit) or give it up (cutover_abort).".into());
    }
    if on {
        let (out, guilds) = crate::world::run(ctx, shutdown);
        use crate::pc::pc;
        let left = ctx.db.pc().count();
        if left != 0 {
            return Err(format!("{left} character(s) are still in the world: the server was not closed."));
        }
        clear_selections(ctx);
        write_manifest(ctx);
        // `file_version`: the version an export of this database is (the tool writes it into the file's header)
        let report = serde_json::json!({ "closed": true, "file_version": VERSION, "characters_out": out, "guilds_written": guilds }).to_string();
        log::info!("cutover: the server is closed ({out} character(s) taken out, {guilds} guild(s) written)");
        put_state(&ctx.db, CutoverState { key: 0, frozen: true, importing: false, header: String::new(), since: ctx.timestamp, report });
    } else {
        let report = old.map(|s| s.report).unwrap_or_default();
        put_state(&ctx.db, CutoverState { key: 0, frozen: false, importing: false, header: String::new(), since: ctx.timestamp, report });
        // the timers that came due meanwhile run now (timer.rs: more than a second late, each with the pass's tick)
        crate::world::run(ctx, crate::timer::rearm);
        log::info!("cutover: the server is open");
    }
    Ok(())
}

// ---------------------------------------------------------------------------------------------- import: staging

/// Brings a staged piece written by file version `version` up to [`VERSION`]. One arm per older version, added with
/// the change that made it necessary (rename a column, give a new one its value …); the rows are JSON objects keyed
/// by column name, so a step is a small edit of `json`.
pub fn upgrade(version: u32, _table: &str, json: String) -> Result<String, String> {
    match version {
        VERSION => Ok(json),
        v => Err(format!("file version {v}: this module has no upgrade from it.")),
    }
}

/// The pure rule for a file's header: its format and version, and that this module imports every table it holds.
/// Returns (version, table → row count).
pub fn check_header(header: &str) -> Result<(u32, Vec<(String, u64)>), String> {
    let h: serde_json::Value = serde_json::from_str(header).map_err(|e| format!("The file's header is not JSON: {e}"))?;
    if h.get("format").and_then(|v| v.as_str()) != Some(FORMAT) {
        return Err(format!("Not a Loki export (its format is not \"{FORMAT}\")."));
    }
    let version = h.get("version").and_then(|v| v.as_u64()).ok_or("The file has no version.")? as u32;
    if version > VERSION {
        return Err(format!("The file is version {version}; this module reads up to {VERSION}: it was made by a newer Loki. Publish that module, or export again."));
    }
    if version < MIN_VERSION {
        return Err(format!("The file is version {version}; this module reads {MIN_VERSION} to {VERSION}."));
    }
    let counts = h.get("counts").and_then(|v| v.as_object()).ok_or("The file's header has no table counts.")?;
    let mut out = Vec::new();
    for (table, rows) in counts {
        match spec(table) {
            Some(s) if s.class != Class::No => {}
            Some(_) => return Err(format!("The file holds the table '{table}', which this module does not import.")),
            None => return Err(format!("The file holds the table '{table}', which this module does not have.")),
        }
        out.push((table.clone(), rows.as_u64().ok_or_else(|| format!("The header's count of '{table}' is not a number."))?));
    }
    Ok((version, out))
}

fn fresh(ctx: &ReducerContext) -> Result<(), String> {
    use crate::account::account;
    use crate::character::character;
    use crate::pc::pc;
    let (a, ch, online) = (ctx.db.account().count(), ctx.db.character().count(), ctx.db.pc().count());
    if a != 0 || ch != 0 || online != 0 {
        return Err(format!("This database already has {a} account(s) and {ch} character(s): an import goes into a freshly published database only."));
    }
    Ok(())
}

/// The owner begins an import: `header` is the file's header (format, version, the row count of every table).
/// Accepted only on a database without accounts and characters; closes the server until the commit.
#[reducer]
pub fn cutover_begin(ctx: &ReducerContext, header: String) -> Result<(), String> {
    crate::release::require_owner(ctx, "import player data")?;
    check_header(&header)?;
    fresh(ctx)?;
    for seq in ctx.db.cutover_stage().iter().map(|r| r.seq).collect::<Vec<_>>() {
        ctx.db.cutover_stage().seq().delete(seq);
    }
    write_manifest(ctx);
    put_state(&ctx.db, CutoverState { key: 0, frozen: true, importing: true, header, since: ctx.timestamp, report: String::new() });
    Ok(())
}

/// One piece of the file: `rows` rows of `table` as a JSON array. Pieces are numbered from 0 without gaps.
#[reducer]
pub fn cutover_chunk(ctx: &ReducerContext, seq: u32, table: String, rows: u32, json: String) -> Result<(), String> {
    crate::release::require_owner(ctx, "import player data")?;
    let st = state(&ctx.db).filter(|s| s.importing).ok_or("No import has begun (cutover_begin).")?;
    let (_, counts) = check_header(&st.header)?;
    if !counts.iter().any(|(t, _)| *t == table) {
        return Err(format!("The table '{table}' is not in the file's header."));
    }
    let next = ctx.db.cutover_stage().count() as u32;
    if seq != next {
        return Err(format!("Piece {seq} arrived where piece {next} was expected."));
    }
    ctx.db.cutover_stage().insert(CutoverStage { seq, table, rows, json });
    Ok(())
}

/// The owner gives up an import that has begun: the staged pieces go, the server opens.
#[reducer]
pub fn cutover_abort(ctx: &ReducerContext) -> Result<(), String> {
    crate::release::require_owner(ctx, "import player data")?;
    for seq in ctx.db.cutover_stage().iter().map(|r| r.seq).collect::<Vec<_>>() {
        ctx.db.cutover_stage().seq().delete(seq);
    }
    put_state(&ctx.db, CutoverState { key: 0, frozen: false, importing: false, header: String::new(), since: ctx.timestamp, report: String::new() });
    crate::world::run(ctx, crate::timer::rearm);
    Ok(())
}

// ---------------------------------------------------------------------------------------------- import: the commit

/// The staged file: table → its pieces' JSON, in order.
pub struct Staged {
    pub version: u32,
    /// The tables the file's header names (a table with no row has no piece).
    pub tables: HashSet<String>,
    pub pieces: HashMap<String, Vec<String>>,
}

/// A row type's SATS type with every named type written out in place (the module's types refer to each other by
/// number inside a typespace; none of the tables' types contains itself).
struct Inline;

impl TypespaceBuilder for Inline {
    fn add(&mut self, _typeid: std::any::TypeId, _name: Option<&'static str>, make_ty: impl FnOnce(&mut Self) -> AlgebraicType) -> AlgebraicType {
        make_ty(self)
    }
}

/// A column or variant name as both sides are compared: SpacetimeDB's schema spells the module's `card0` as
/// `card_0` and `class_` as `class`, so underscores and case do not count.
fn plain(name: &str) -> String {
    name.chars().filter(|c| *c != '_').map(|c| c.to_ascii_lowercase()).collect()
}

/// Checks a value of the file against the module's type `ty` and returns it keyed by the module's own field names,
/// which is what the typed decode reads. Strict at every depth: a struct is an object with exactly the type's
/// columns (none missing, none unknown), an enum or Option is `{variant: value}` with a variant the type has.
pub fn to_module_names(ty: &AlgebraicType, v: serde_json::Value, path: &str) -> Result<serde_json::Value, String> {
    use serde_json::Value;
    match ty {
        AlgebraicType::Product(p) => {
            if p.elements.is_empty() {
                return match v {
                    Value::Array(a) if a.is_empty() => Ok(Value::Array(a)),
                    Value::Object(o) if o.is_empty() => Ok(Value::Array(Vec::new())),
                    _ => Err(format!("{path}: expected an empty value")),
                };
            }
            let Value::Object(mut map) = v else { return Err(format!("{path}: expected an object keyed by column name")) };
            let mut out = serde_json::Map::new();
            for (i, e) in p.elements.iter().enumerate() {
                let name = e.name.as_ref().map(|n| n.to_string()).unwrap_or_else(|| i.to_string());
                let want = plain(&name);
                let key = map.keys().find(|k| plain(k) == want).cloned().ok_or_else(|| format!("{path}: the column `{name}` is missing"))?;
                let val = map.remove(&key).unwrap_or(Value::Null);
                out.insert(name.clone(), to_module_names(&e.algebraic_type, val, &format!("{path}.{name}"))?);
            }
            if let Some(k) = map.keys().next() {
                return Err(format!("{path}: the column `{k}` is not one this module's table has"));
            }
            Ok(Value::Object(out))
        }
        AlgebraicType::Sum(sum) => {
            let Value::Object(map) = v else { return Err(format!("{path}: expected {{variant: value}}")) };
            if map.len() != 1 {
                return Err(format!("{path}: expected exactly one variant"));
            }
            let Some((k, val)) = map.into_iter().next() else { return Err(format!("{path}: expected exactly one variant")) };
            let want = plain(&k);
            let variant = sum.variants.iter().find(|x| x.name.as_ref().map(|n| plain(n) == want).unwrap_or(false)).ok_or_else(|| format!("{path}: `{k}` is not a variant this module has"))?;
            let name = variant.name.as_ref().map(|n| n.to_string()).unwrap_or_default();
            let mut out = serde_json::Map::new();
            out.insert(name.clone(), to_module_names(&variant.algebraic_type, val, &format!("{path}.{name}"))?);
            Ok(Value::Object(out))
        }
        AlgebraicType::Array(a) => match v {
            Value::Array(items) => {
                let mut out = Vec::with_capacity(items.len());
                for (i, x) in items.into_iter().enumerate() {
                    out.push(to_module_names(&a.elem_ty, x, &format!("{path}[{i}]"))?);
                }
                Ok(Value::Array(out))
            }
            other => Ok(other), // bytes travel as a hex string
        },
        AlgebraicType::Ref(_) => Err(format!("{path}: a type this tool cannot resolve")),
        _ => Ok(v),
    }
}

impl Staged {
    /// Does the file hold `table` (possibly with no row).
    pub fn has(&self, table: &str) -> bool {
        self.tables.contains(table)
    }

    /// Every row of `table`, decoded by the module's row type. Strict: an unknown, missing or mistyped column is an
    /// error that names the table and the column.
    pub fn rows<T>(&self, table: &str) -> Result<Vec<T>, String>
    where
        T: SpacetimeType + for<'de> spacetimedb::Deserialize<'de>,
    {
        let ty = T::make_type(&mut Inline);
        let mut out = Vec::new();
        for json in self.pieces.get(table).map(|v| v.as_slice()).unwrap_or(&[]) {
            let v: serde_json::Value = serde_json::from_str(json).map_err(|e| format!("table {table}: {e}"))?;
            let serde_json::Value::Array(items) = v else { return Err(format!("table {table}: a piece is not a list of rows")) };
            for item in items {
                let n = out.len();
                let item = to_module_names(&ty, item, "row").map_err(|e| format!("table {table}, row {n}: {e}"))?;
                let row: SerdeWrapper<T> = serde_json::from_value(item).map_err(|e| format!("table {table}, row {n}: {e}"))?;
                out.push(row.0);
            }
        }
        Ok(out)
    }
}

/// What a commit did.
#[derive(Default)]
pub struct Stats {
    /// (table, rows inserted)
    pub done: Vec<(&'static str, u64)>,
    /// (table, rows whose owner — account, character or guild — is not in the file); they are imported as they are
    pub orphans: Vec<(&'static str, u64)>,
    /// (table, rows left out by a rule: `$@` variables)
    pub dropped: Vec<(&'static str, u64)>,
    /// counter kind → the highest imported id
    pub maxes: HashMap<&'static str, u64>,
    /// (kind, next id)
    pub counters: Vec<(&'static str, u32)>,
    pub accounts: HashSet<u32>,
    pub chars: HashSet<u32>,
    pub guilds: HashSet<u32>,
}

impl Stats {
    fn max(&mut self, kind: &'static str, m: Option<u64>) {
        if let Some(m) = m {
            let e = self.maxes.entry(kind).or_insert(0);
            *e = (*e).max(m);
        }
    }
}

/// Inserts the rows of one table; the table must be empty (a freshly published database).
macro_rules! put {
    ($c:ident, $st:ident, $name:literal, $acc:ident, $rows:expr) => {{
        let there = $c.db().$acc().count();
        if there != 0 {
            return Err(format!("table {}: {there} row(s) are already there: an import goes into a freshly published database only.", $name));
        }
        let mut n = 0u64;
        for r in $rows {
            $c.db().$acc().try_insert(r).map_err(|e| format!("table {}: {e}", $name))?;
            n += 1;
        }
        $st.done.push(($name, n));
    }};
}

/// Decodes and inserts one table: `auto` names the handle column handed out again in the old order, `owner` the set
/// and column its rows belong to (rows without their owner are counted, not dropped), `fix` what is reset, `max` the
/// id counter the table's ids continue.
macro_rules! load {
    ($c:ident, $f:ident, $st:ident, $name:literal, $acc:ident : $ty:ty
        $(, auto $auto:ident)? $(, owner $set:ident . $col:ident)? $(, fix |$r:ident| $fix:block)? $(, max $kind:literal = |$m:ident| $max:expr)?) => {{
        #[allow(unused_mut)]
        let mut v: Vec<$ty> = $f.rows($name)?;
        $( v.sort_by_key(|r| r.$auto); for r in v.iter_mut() { r.$auto = 0; } )?
        $( for $r in v.iter_mut() $fix )?
        $( let orphans = v.iter().filter(|r| !$st.$set.contains(&(r.$col as u32))).count() as u64; if orphans > 0 { $st.orphans.push(($name, orphans)); } )?
        $( $st.max($kind, v.iter().map(|$m| $max as u64).max()); )?
        put!($c, $st, $name, $acc, v);
    }};
}

/// Writes every staged table. The order only matters for the owner sets (accounts and characters first).
fn load_all(c: &Ctx, f: &Staged, st: &mut Stats) -> Result<(), String> {
    use crate::account::{account, account_device, session, Account, AccountDevice, Session};
    use crate::achievement::{achievement, achievement_slots, AchievementRow, AchievementSlots};
    use crate::atcommand_atcmd::accounts::{login_ext, LoginExt};
    use crate::cart::{cart_inventory, CartItem};
    use crate::character::{character, Character};
    use crate::guild_storage::{guild_storage_log, guild_storage_sql, GuildStorageLog, GuildStorageSql};
    use crate::horde::service::{horde_reward, HordeReward};
    use crate::hotkey::{hotkey, Hotkey};
    use crate::int_auction::{auction, AuctionData};
    use crate::int_elemental::{elemental, ElementalRow};
    use crate::int_guild::{guild_alliance_sql, guild_expulsion_sql, guild_member_sql, guild_position_sql, guild_skill_sql, guild_sql};
    use crate::int_guild::{GuildAllianceSql, GuildExpulsionSql, GuildMemberSql, GuildPositionSql, GuildSkillSql, GuildSql};
    use crate::int_homun::{homunculus, skill_homunculus, skillcooldown_homunculus, HomunculusRow, SkillHomunculusRow, SkillcooldownHomunculusRow};
    use crate::int_mail::{mail, MailRow};
    use crate::int_mercenary::{mercenary, mercenary_owner, skillcooldown_mercenary, MercenaryOwner, MercenaryRow, SkillCooldownMercenary};
    use crate::int_party::{party, PartySql};
    use crate::int_pet::{pet, Pet};
    use crate::inventory::{inventory, InventoryItem};
    use crate::map_log::{atcommandlog, branchlog, cashlog, chatlog, feedinglog, mvplog, npclog, picklog, zenylog};
    use crate::map_log::{AtcommandLog, BranchLog, CashLog, ChatLog, FeedingLog, MvpLog, NpcLog, PickLog, ZenyLog};
    use crate::pc_misc::{char_memo, CharMemo};
    use crate::presence::{account_settings, AccountSettings};
    use crate::quest::{quest, QuestRow};
    use crate::script::reg::{acc_reg_num, acc_reg_str, char_reg_num, char_reg_str, global_acc_reg_num, global_acc_reg_str};
    use crate::script::reg::{AccRegNum, AccRegStr, CharRegNum, CharRegStr, GlobalAccRegNum, GlobalAccRegStr};
    use crate::skill::cooldown::{skillcooldown, SkillCooldownRow};
    use crate::skill::{char_skill, CharSkill};
    use crate::social::{char_friends, CharFriends};
    use crate::status::data::{bonus_script, sc_saved, BonusScript, ScSaved};
    use crate::storage::{storage, StorageItem};
    use crate::woe::int_castle::{guild_castle_sql, GuildCastleSql};

    // ---- accounts (rAthena's login server)
    let v: Vec<Account> = f.rows("account")?;
    st.accounts = v.iter().map(|r| r.id).collect();
    st.max("account", v.iter().map(|r| r.id as u64).max());
    put!(c, st, "account", account, v);
    load!(c, f, st, "login_ext", login_ext: LoginExt, owner accounts.account_id);
    load!(c, f, st, "session", session: Session, owner accounts.account_id, fix |r| {
        r.selected_char = None;
        r.online = false;
        r.connection = None;
    });
    load!(c, f, st, "account_device", account_device: AccountDevice, auto id, owner accounts.account_id);
    load!(c, f, st, "account_settings", account_settings: AccountSettings, owner accounts.account_id);
    load!(c, f, st, "acc_reg_num", acc_reg_num: AccRegNum, auto id, owner accounts.owner);
    load!(c, f, st, "acc_reg_str", acc_reg_str: AccRegStr, auto id, owner accounts.owner);
    load!(c, f, st, "global_acc_reg_num", global_acc_reg_num: GlobalAccRegNum, auto id, owner accounts.owner);
    load!(c, f, st, "global_acc_reg_str", global_acc_reg_str: GlobalAccRegStr, auto id, owner accounts.owner);
    load!(c, f, st, "storage", storage: StorageItem, auto id, owner accounts.account_id, max "item_sql_id" = |r| r.save_id);

    // ---- characters (the char server)
    let v: Vec<Character> = f.rows("character")?;
    st.chars = v.iter().map(|r| r.id).collect();
    st.max("char", v.iter().map(|r| r.id as u64).max());
    let orphans = v.iter().filter(|r| !st.accounts.contains(&r.account_id)).count() as u64;
    if orphans > 0 {
        st.orphans.push(("character", orphans));
    }
    put!(c, st, "character", character, v);
    load!(c, f, st, "inventory", inventory: InventoryItem, auto id, owner chars.char_id, max "item_sql_id" = |r| r.save_id);
    load!(c, f, st, "cart_inventory", cart_inventory: CartItem, auto id, owner chars.char_id, max "item_sql_id" = |r| r.save_id);
    load!(c, f, st, "char_skill", char_skill: CharSkill, auto id, owner chars.char_id);
    load!(c, f, st, "skillcooldown", skillcooldown: SkillCooldownRow, auto id, owner chars.char_id);
    load!(c, f, st, "hotkey", hotkey: Hotkey, auto id, owner chars.char_id);
    load!(c, f, st, "char_friends", char_friends: CharFriends, owner chars.char_id);
    load!(c, f, st, "char_memo", char_memo: CharMemo, owner chars.char_id);
    load!(c, f, st, "quest", quest: QuestRow, auto id, owner chars.char_id);
    load!(c, f, st, "achievement", achievement: AchievementRow, auto id, owner chars.char_id);
    load!(c, f, st, "sc_saved", sc_saved: ScSaved, auto id, owner chars.char_id);
    load!(c, f, st, "bonus_script", bonus_script: BonusScript, auto id, owner chars.char_id, fix |r| {
        // a character that left the world: no timer, the remaining time in `tick` (status/bonus.rs:2430-2431)
        r.tid = 0;
        r.online = false;
    });
    load!(c, f, st, "char_reg_num", char_reg_num: CharRegNum, auto id, owner chars.owner);
    load!(c, f, st, "char_reg_str", char_reg_str: CharRegStr, auto id, owner chars.owner);
    load!(c, f, st, "horde_reward", horde_reward: HordeReward, auto id, owner chars.char_id);
    load!(c, f, st, "pet", pet: Pet, max "pet" = |r| r.pet_id);
    load!(c, f, st, "homunculus", homunculus: HomunculusRow, owner chars.char_id, max "homunculus" = |r| r.homun_id);
    load!(c, f, st, "skill_homunculus", skill_homunculus: SkillHomunculusRow, auto row_id);
    load!(c, f, st, "skillcooldown_homunculus", skillcooldown_homunculus: SkillcooldownHomunculusRow, auto row_id);
    load!(c, f, st, "mercenary", mercenary: MercenaryRow, owner chars.char_id, max "mercenary" = |r| r.mer_id);
    load!(c, f, st, "mercenary_owner", mercenary_owner: MercenaryOwner, owner chars.char_id);
    load!(c, f, st, "skillcooldown_mercenary", skillcooldown_mercenary: SkillCooldownMercenary, auto id);
    load!(c, f, st, "elemental", elemental: ElementalRow, owner chars.char_id, max "elemental" = |r| r.ele_id);
    load!(c, f, st, "mail", mail: MailRow, max "mail_id" = |r| r.id);
    load!(c, f, st, "auction", auction: AuctionData, fix |r| {
        // the timer belonged to the old database: inter_auctions_fromsql arms a new one (int_auction.rs:371-398)
        r.auction_end_timer = 0;
    }, max "auction_id" = |r| r.auction_id);
    load!(c, f, st, "party", party: PartySql, max "party" = |r| r.party_id);

    // ---- guilds
    let v: Vec<GuildSql> = f.rows("guild_sql")?;
    st.guilds = v.iter().map(|r| r.guild_id).collect();
    st.max("guild", v.iter().map(|r| r.guild_id as u64).max());
    put!(c, st, "guild_sql", guild_sql, v);
    load!(c, f, st, "guild_member_sql", guild_member_sql: GuildMemberSql, auto id, owner guilds.guild_id);
    load!(c, f, st, "guild_position_sql", guild_position_sql: GuildPositionSql, auto id, owner guilds.guild_id);
    load!(c, f, st, "guild_alliance_sql", guild_alliance_sql: GuildAllianceSql, auto id, owner guilds.guild_id);
    load!(c, f, st, "guild_expulsion_sql", guild_expulsion_sql: GuildExpulsionSql, auto id, owner guilds.guild_id);
    load!(c, f, st, "guild_skill_sql", guild_skill_sql: GuildSkillSql, auto id, owner guilds.guild_id);
    load!(c, f, st, "guild_storage_sql", guild_storage_sql: GuildStorageSql, owner guilds.guild_id, max "guild_storage_sql" = |r| r.sql_id);
    load!(c, f, st, "guild_storage_log", guild_storage_log: GuildStorageLog, owner guilds.guild_id, max "guild_storage_log" = |r| r.id);
    load!(c, f, st, "guild_castle_sql", guild_castle_sql: GuildCastleSql);

    // ---- the logs (only in a file made with them)
    load!(c, f, st, "atcommandlog", atcommandlog: AtcommandLog, auto atcommand_id);
    load!(c, f, st, "branchlog", branchlog: BranchLog, auto branch_id);
    load!(c, f, st, "cashlog", cashlog: CashLog, auto id);
    load!(c, f, st, "chatlog", chatlog: ChatLog, auto id);
    load!(c, f, st, "feedinglog", feedinglog: FeedingLog, auto id);
    load!(c, f, st, "mvplog", mvplog: MvpLog, auto mvp_id);
    load!(c, f, st, "npclog", npclog: NpcLog, auto npc_id);
    load!(c, f, st, "picklog", picklog: PickLog, auto id);
    load!(c, f, st, "zenylog", zenylog: ZenyLog, auto id);

    // ---- server state the players' data depends on: these tables are not empty in a fresh database, the file's rows
    // take the place of what is there
    {
        // the `achievement` and `sc_data` record slots
        use crate::status::data::{sc_data_slots, ScDataSlots};
        let v: Vec<AchievementSlots> = f.rows("achievement_slots")?;
        if f.has("achievement_slots") {
            for id in c.db().achievement_slots().iter().map(|r| r.id).collect::<Vec<_>>() {
                c.db().achievement_slots().id().delete(id);
            }
        }
        st.done.push(("achievement_slots", v.len() as u64));
        for r in v {
            c.db().achievement_slots().try_insert(r).map_err(|e| format!("table achievement_slots: {e}"))?;
        }
        let v: Vec<ScDataSlots> = f.rows("sc_data_slots")?;
        if f.has("sc_data_slots") {
            for id in c.db().sc_data_slots().iter().map(|r| r.id).collect::<Vec<_>>() {
                c.db().sc_data_slots().id().delete(id);
            }
        }
        st.done.push(("sc_data_slots", v.len() as u64));
        for r in v {
            c.db().sc_data_slots().try_insert(r).map_err(|e| format!("table sc_data_slots: {e}"))?;
        }
    }
    {
        // mapreg: the permanent `$` variables as the old server had them; the `$@` ones are never saved
        // (script/reg.rs:9-10). The world rebuild that follows runs every OnInit over them, as an rAthena start does.
        use crate::script::reg::{mapreg_num, mapreg_str, MapregNum, MapregStr};
        if f.has("mapreg_num") {
            for id in c.db().mapreg_num().iter().map(|r| r.id).collect::<Vec<_>>() {
                c.db().mapreg_num().id().delete(id);
            }
            let mut v: Vec<MapregNum> = f.rows("mapreg_num")?;
            v.sort_by_key(|r| r.id);
            let all = v.len();
            v.retain(|r| !r.key.starts_with("$@"));
            if all != v.len() {
                st.dropped.push(("mapreg_num", (all - v.len()) as u64));
            }
            st.done.push(("mapreg_num", v.len() as u64));
            for mut r in v {
                r.id = 0;
                c.db().mapreg_num().try_insert(r).map_err(|e| format!("table mapreg_num: {e}"))?;
            }
        }
        if f.has("mapreg_str") {
            for id in c.db().mapreg_str().iter().map(|r| r.id).collect::<Vec<_>>() {
                c.db().mapreg_str().id().delete(id);
            }
            let mut v: Vec<MapregStr> = f.rows("mapreg_str")?;
            v.sort_by_key(|r| r.id);
            let all = v.len();
            v.retain(|r| !r.key.starts_with("$@"));
            if all != v.len() {
                st.dropped.push(("mapreg_str", (all - v.len()) as u64));
            }
            st.done.push(("mapreg_str", v.len() as u64));
            for mut r in v {
                r.id = 0;
                c.db().mapreg_str().try_insert(r).map_err(|e| format!("table mapreg_str: {e}"))?;
            }
        }
    }
    {
        // the shops' stock: `market` is read by the world rebuild before the NPC files load (shop.rs:503-506
        // npc_market_fromsql, npc.rs build_world), `barter` by every purchase (barter.rs:68); the fresh database's
        // rows are the NPC files' full stock
        use crate::barter::{barter_stock, BarterStock};
        use crate::shop::{market, Market};
        if f.has("market") {
            for id in c.db().market().iter().map(|r| r.id).collect::<Vec<_>>() {
                c.db().market().id().delete(id);
            }
            let mut v: Vec<Market> = f.rows("market")?;
            v.sort_by_key(|r| r.id);
            st.done.push(("market", v.len() as u64));
            for mut r in v {
                r.id = 0;
                c.db().market().try_insert(r).map_err(|e| format!("table market: {e}"))?;
            }
        }
        if f.has("barter_stock") {
            for key in c.db().barter_stock().iter().map(|r| r.key).collect::<Vec<_>>() {
                c.db().barter_stock().key().delete(key);
            }
            let v: Vec<BarterStock> = f.rows("barter_stock")?;
            st.done.push(("barter_stock", v.len() as u64));
            for r in v {
                c.db().barter_stock().try_insert(r).map_err(|e| format!("table barter_stock: {e}"))?;
            }
        }
    }
    {
        // the release switch
        use crate::release::{server_config, ServerConfig};
        let v: Vec<ServerConfig> = f.rows("server_config")?;
        st.done.push(("server_config", v.len() as u64));
        if let Some(row) = v.into_iter().find(|r| r.key == 0) {
            if c.db().server_config().key().find(0).is_some() {
                c.db().server_config().key().update(row);
            } else {
                c.db().server_config().insert(row);
            }
        }
    }
    {
        // the id counters: the file's value, never below the highest imported id + 1
        use crate::ids::{id_counter, IdCounter};
        let file: Vec<IdCounter> = f.rows("id_counter")?;
        st.done.push(("id_counter", file.len() as u64));
        for &kind in PERSISTENT_COUNTERS {
            let from_file = file.iter().find(|r| r.kind == kind).map(|r| r.next as u64).unwrap_or(0);
            let floor = st.maxes.get(kind).map(|m| m + 1).unwrap_or(0);
            let next = from_file.max(floor);
            if next == 0 {
                continue; // never used: ids.rs starts it
            }
            let next = u32::try_from(next).map_err(|_| format!("id counter '{kind}': {next} does not fit"))?;
            let row = IdCounter { kind: kind.to_string(), next };
            if c.db().id_counter().kind().find(&row.kind).is_some() {
                c.db().id_counter().kind().update(row);
            } else {
                c.db().id_counter().insert(row);
            }
            st.counters.push((kind, next));
        }
    }
    Ok(())
}

/// The servers "start" over the imported rows: the char server's caches go (they are loaded from the tables on first
/// use: woe/int_castle.rs:53-66, int_auction.rs:403-407, fame.rs), the world is built again (npc.rs build_world: a
/// map-server start, which asks for the castles, runs every OnInit over the imported `$` variables and builds the
/// NPC set of the imported mode).
fn restart(c: &Ctx) {
    use crate::fame::fame_list;
    use crate::int_auction::auction_mem;
    use crate::npc::world_state;
    use crate::woe::int_castle::inter_castle;
    for id in c.db().inter_castle().iter().map(|r| r.castle_id).collect::<Vec<_>>() {
        c.db().inter_castle().castle_id().delete(id);
    }
    for key in c.db().fame_list().iter().map(|r| r.key).collect::<Vec<_>>() {
        c.db().fame_list().key().delete(key);
    }
    for key in c.db().auction_mem().iter().map(|r| r.key).collect::<Vec<_>>() {
        c.db().auction_mem().key().delete(key);
    }
    // the imported mode decides the NPC set and the groups (release.rs load, world_key)
    crate::release::load(c.db());
    if let Some(ws) = c.db().world_state().key().find(0) {
        c.db().world_state().key().update(crate::npc::WorldState { data_version: !ws.data_version, ..ws });
    }
    crate::npc::ensure_world(c);
    // inter_auction_sql_init: the auctions' order and end timers
    crate::int_auction::ensure_loaded(c);
    crate::timer::rearm(c);
}

/// The owner commits the staged file: `pieces` and `rows` are what the tool sent (a file cut short is refused).
/// Everything happens in this one call or not at all: an error leaves the database as it was before `cutover_begin`'s
/// pieces were read (the pieces stay, for `cutover_abort` or another try).
#[reducer]
pub fn cutover_commit(ctx: &ReducerContext, pieces: u32, rows: u64) -> Result<(), String> {
    crate::release::require_owner(ctx, "import player data")?;
    let st = state(&ctx.db).filter(|s| s.importing).ok_or("No import has begun (cutover_begin).")?;
    let (version, counts) = check_header(&st.header)?;
    fresh(ctx)?;
    let mut staged: Vec<CutoverStage> = ctx.db.cutover_stage().iter().collect();
    staged.sort_by_key(|r| r.seq);
    let total: u64 = staged.iter().map(|r| r.rows as u64).sum();
    if staged.len() as u32 != pieces || total != rows {
        return Err(format!("The import is incomplete: {} piece(s) with {total} row(s) arrived, the tool sent {pieces} with {rows}.", staged.len()));
    }
    let mut per_table: HashMap<String, u64> = HashMap::new();
    let mut f = Staged { version, tables: counts.iter().map(|(t, _)| t.clone()).collect(), pieces: HashMap::new() };
    for r in staged {
        *per_table.entry(r.table.clone()).or_insert(0) += r.rows as u64;
        let json = upgrade(version, &r.table, r.json)?;
        f.pieces.entry(r.table).or_default().push(json);
    }
    for (table, want) in &counts {
        let got = per_table.get(table).copied().unwrap_or(0);
        if got != *want {
            return Err(format!("table {table}: the file's header says {want} row(s), {got} arrived."));
        }
    }
    // open before the world is built again: the build arms its timers
    put_state(&ctx.db, CutoverState { key: 0, frozen: false, importing: false, header: String::new(), since: ctx.timestamp, report: String::new() });
    let mut stats = Stats::default();
    crate::world::run(ctx, |c| {
        load_all(c, &f, &mut stats)?;
        // every table of the header was read, and with the row count the header gave (a piece whose `rows` lied, or
        // a table the list exports and load_all forgot, is caught here)
        for (table, want) in &counts {
            let dropped = stats.dropped.iter().find(|(t, _)| t == table).map(|(_, n)| *n).unwrap_or(0);
            match stats.done.iter().find(|(t, _)| t == table) {
                Some((_, n)) if *n + dropped == *want => {}
                Some((_, n)) => return Err(format!("table {table}: the file's header says {want} row(s), {} were read.", *n + dropped)),
                None => return Err(format!("table {table}: this module has no importer for it.")),
            }
        }
        restart(c);
        Ok::<(), String>(())
    })?;
    for seq in ctx.db.cutover_stage().iter().map(|r| r.seq).collect::<Vec<_>>() {
        ctx.db.cutover_stage().seq().delete(seq);
    }
    let pairs = |v: &[(&'static str, u64)]| serde_json::Value::Object(v.iter().map(|(t, n)| (t.to_string(), serde_json::json!(n))).collect());
    let report = serde_json::json!({
        "imported": true,
        "file_version": version,
        "tables": pairs(&stats.done),
        "rows_without_owner": pairs(&stats.orphans),
        "dropped": pairs(&stats.dropped),
        "counters": serde_json::Value::Object(stats.counters.iter().map(|(k, n)| (k.to_string(), serde_json::json!(n))).collect()),
    })
    .to_string();
    log::info!("cutover: imported {} table(s), {} row(s)", stats.done.len(), stats.done.iter().map(|(_, n)| n).sum::<u64>());
    put_state(&ctx.db, CutoverState { key: 0, frozen: false, importing: false, header: String::new(), since: ctx.timestamp, report });
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    fn src_dir() -> std::path::PathBuf {
        std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("src")
    }

    fn rs_files(dir: &std::path::Path, out: &mut Vec<std::path::PathBuf>) {
        for e in std::fs::read_dir(dir).unwrap() {
            let p = e.unwrap().path();
            if p.is_dir() {
                rs_files(&p, out);
            } else if p.extension().map(|x| x == "rs").unwrap_or(false) {
                out.push(p);
            }
        }
    }

    /// Finds `needle` followed by an identifier in every source file: (identifier, file, line).
    fn scan(needle: &str) -> Vec<(String, String, usize)> {
        let mut files = Vec::new();
        rs_files(&src_dir(), &mut files);
        files.sort();
        let mut out = Vec::new();
        for p in files {
            let text = std::fs::read_to_string(&p).unwrap();
            let rel = p.strip_prefix(src_dir()).unwrap().to_string_lossy().replace('\\', "/");
            let mut from = 0;
            while let Some(i) = text[from..].find(needle) {
                let at = from + i + needle.len();
                let id: String = text[at..].chars().take_while(|c| c.is_ascii_alphanumeric() || *c == '_').collect();
                let line = text[..at].matches('\n').count() + 1;
                if !id.is_empty() {
                    out.push((id, rel.clone(), line));
                }
                from = at;
            }
        }
        out
    }

    /// The module's tables as the source declares them: (accessor, file, line of the attribute).
    fn declared_tables() -> Vec<(String, String, usize)> {
        let mut files = Vec::new();
        rs_files(&src_dir(), &mut files);
        files.sort();
        let mut out = Vec::new();
        for p in files {
            let text = std::fs::read_to_string(&p).unwrap();
            let rel = p.strip_prefix(src_dir()).unwrap().to_string_lossy().replace('\\', "/");
            for (n, line) in text.lines().enumerate() {
                let t = line.trim_start();
                // the attribute, not a mention in a comment or a string
                let Some(rest) = t.strip_prefix("#[table(").or_else(|| t.strip_prefix("#[spacetimedb::table(")) else { continue };
                let rest = rest.trim_start();
                let Some(rest) = rest.strip_prefix("accessor") else { panic!("{rel}:{}: a table attribute that does not start with its accessor", n + 1) };
                let name: String = rest.trim_start().trim_start_matches('=').trim_start().chars().take_while(|c| c.is_ascii_alphanumeric() || *c == '_').collect();
                out.push((name, rel.clone(), n + 1));
            }
        }
        out
    }

    /// EVERY table of the module has a line in [`TABLES`], and the list names nothing else. A new table fails this
    /// test until its author says whether a player owns it.
    #[test]
    fn every_table_is_classified() {
        let declared = declared_tables();
        assert!(declared.len() > 200, "the scan found only {} tables", declared.len());
        let mut missing = Vec::new();
        for (name, file, line) in &declared {
            if spec(name).is_none() {
                missing.push(format!("{name} ({file}:{line})"));
            }
        }
        assert!(missing.is_empty(), "tables without a line in cutover::TABLES (exported or not, and why): {missing:?}");
        for s in TABLES {
            assert!(declared.iter().any(|(n, _, _)| n == s.table), "cutover::TABLES names '{}', which is no table", s.table);
            assert_eq!(TABLES.iter().filter(|o| o.table == s.table).count(), 1, "'{}' is listed twice", s.table);
            assert!(!s.why.is_empty(), "'{}' has no reason", s.table);
        }
    }

    /// Every id counter kind is either continued by the import or named as one that does not outlive a start.
    #[test]
    fn every_counter_is_classified() {
        let mut seen = 0;
        for needle in ["ids::next(ctx, \"", "ids::next(c.rc, \""] {
            for (kind, file, line) in scan(needle) {
                seen += 1;
                assert!(PERSISTENT_COUNTERS.contains(&kind.as_str()) || RUNTIME_COUNTERS.contains(&kind.as_str()), "id counter '{kind}' ({file}:{line}) is in neither list of cutover.rs");
            }
        }
        assert!(seen >= 20, "the scan found only {seen} id counters");
        // the two kinds named by a constant (npc_load.rs VFILE_COUNTER, script/code.rs DEFS_GEN): the world's own
        let named: Vec<String> = scan("ids::next(c.rc, ").into_iter().map(|(k, _, _)| k).filter(|k| k.chars().next().map(|c| c.is_ascii_uppercase()).unwrap_or(false)).collect();
        assert_eq!(named, vec!["VFILE_COUNTER".to_string(), "DEFS_GEN".to_string()]);
        // the files that read or write id_counter rows themselves: a new one must be looked at (its kind classified)
        let mut direct: Vec<String> = scan(".id_counter().").into_iter().map(|(_, file, _)| file).filter(|f| f != "ids.rs" && f != "cutover.rs").collect();
        direct.dedup();
        assert_eq!(direct, vec!["mob/spawn.rs".to_string(), "script/code.rs".to_string(), "script/npc_run.rs".to_string()]);
        assert!(RUNTIME_COUNTERS.contains(&"npc_id") && RUNTIME_COUNTERS.contains(&"ev_db_rt_seq"));
    }

    /// tools/host/tables.json is this list. `LOKI_WRITE_MANIFEST=1 lcargo test --lib cutover` writes it.
    #[test]
    fn manifest_file_is_current() {
        let path = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../tools/host/tables.json");
        let want = manifest_json();
        if std::env::var("LOKI_WRITE_MANIFEST").is_ok() {
            std::fs::write(&path, &want).unwrap();
        }
        let have = std::fs::read_to_string(&path).unwrap_or_default().replace("\r\n", "\n");
        assert!(have == want, "tools/host/tables.json is not cutover::TABLES: run `LOKI_WRITE_MANIFEST=1 lcargo test --lib cutover`");
    }

    /// The list in numbers, and the progress file's table (`lcargo test --lib cutover::tests::list -- --nocapture`).
    #[test]
    fn list() {
        let count = |c: Class| TABLES.iter().filter(|s| s.class == c).count();
        let declared = declared_tables();
        let mut out = std::io::stdout();
        if std::env::var("LOKI_PRINT_TABLES").is_ok() {
            for s in TABLES {
                let (_, file, line) = declared.iter().find(|(n, _, _)| n == s.table).unwrap();
                let what = match s.class {
                    Class::No => "not exported",
                    Class::Log => "exported with --logs",
                    _ => "exported",
                };
                writeln!(out, "| `{}` | {file}:{line} | {what} | {} |", s.table, s.why).unwrap();
            }
        }
        writeln!(out, "tables {}: player {}, server {}, log {}, not exported {}", TABLES.len(), count(Class::Player), count(Class::Server), count(Class::Log), count(Class::No)).unwrap();
        assert_eq!(TABLES.len(), count(Class::Player) + count(Class::Server) + count(Class::Log) + count(Class::No));
    }

    fn staged(table: &str, json: &str) -> Staged {
        Staged { version: VERSION, tables: [table.to_string()].into_iter().collect(), pieces: [(table.to_string(), vec![json.to_string()])].into_iter().collect() }
    }

    /// Every table the list exports has its importer in load_all (the commit refuses a file's table it did not read).
    #[test]
    fn every_exported_table_has_an_importer() {
        let src = std::fs::read_to_string(src_dir().join("cutover.rs")).unwrap();
        let body = &src[src.find("fn load_all(").unwrap()..src.find("fn restart(").unwrap()];
        for s in TABLES.iter().filter(|s| s.class != Class::No) {
            assert!(body.contains(&format!("\"{}\"", s.table)), "load_all does not read '{}'", s.table);
        }
        for s in TABLES.iter().filter(|s| s.class == Class::No) {
            assert!(!body.contains(&format!("rows(\"{}\")", s.table)) && !body.contains(&format!("st, \"{}\",", s.table)), "load_all reads '{}', which is not exported", s.table);
        }
    }

    /// Rows as the export tool writes them (objects keyed by column name at every depth) decode into the module's row
    /// types; the values are the ones SpacetimeDB's SQL endpoint returned for a test account on 2026-10-05.
    #[test]
    fn rows_decode_by_name() {
        use crate::account::{Account, Session};
        use crate::inventory::InventoryItem;
        let f = staged("account", r#"[{"id":2000000,"username":"player1","username_key":"player1","pass_hash":"$argon2id$v=19$m=19456,t=2,p=1$AAAAAAAAAAAAAAAAAAAAAA$BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB","sex":1,"group_id":0,"created":{"__timestamp_micros_since_unix_epoch__":1791203949872109},"last_login":{"some":{"__timestamp_micros_since_unix_epoch__":1791203949973935}},"fail_count":0,"fail_since":{"none":[]},"locked_until":{"none":[]}}]"#);
        let v: Vec<Account> = f.rows("account").unwrap();
        assert_eq!(v.len(), 1);
        assert_eq!((v[0].id, v[0].username.as_str(), v[0].sex), (2_000_000, "player1", 1));
        assert_eq!(v[0].last_login.unwrap().to_micros_since_unix_epoch(), 1791203949973935);
        assert!(v[0].fail_since.is_none());

        let f = staged("session", r#"[{"identity":{"__identity__":"0x0101010101010101010101010101010101010101010101010101010101010101"},"account_id":2000000,"since":{"__timestamp_micros_since_unix_epoch__":1791203949872109},"selected_char":{"none":[]},"online":false,"connection":{"none":[]}}]"#);
        let v: Vec<Session> = f.rows("session").unwrap();
        assert_eq!(v[0].identity.to_hex().to_string(), "0101010101010101010101010101010101010101010101010101010101010101");
        assert_eq!(v[0].account_id, 2_000_000);

        let item = r#"{"nameid":1201,"amount":1,"equip":2,"identify":1,"refine":0,"attribute":0,"card_0":0,"card_1":0,"card_2":0,"card_3":0,"option":[{"id":0,"value":0,"param":0},{"id":0,"value":0,"param":0},{"id":0,"value":0,"param":0},{"id":0,"value":0,"param":0},{"id":0,"value":0,"param":0}],"expire_time":0,"favorite":0,"bound":0,"unique_id":644245094400001,"equip_switch":0,"enchantgrade":0}"#;
        let f = staged("inventory", &format!(r#"[{{"id":7,"char_id":150000,"idx":0,"save_id":1,"item":{item}}}]"#));
        let v: Vec<InventoryItem> = f.rows("inventory").unwrap();
        assert_eq!((v[0].id, v[0].char_id, v[0].item.nameid, v[0].item.unique_id), (7, 150_000, 1201, 644245094400001));
        assert_eq!(v[0].item.option.len(), 5);
    }

    /// A row that does not fit the module's table refuses the file: a missing column, an unknown one, another type,
    /// a value out of range, also inside a nested struct.
    #[test]
    fn rows_that_do_not_fit_are_refused() {
        use crate::ids::IdCounter;
        use crate::int_pet::Pet;
        let ok = r#"[{"kind":"char","next":150001}]"#;
        assert_eq!(staged("id_counter", ok).rows::<IdCounter>("id_counter").unwrap()[0].next, 150_001);
        for bad in [
            r#"[{"kind":"char"}]"#,                                // a column is missing
            r#"[{"kind":"char","next":150001,"more":1}]"#,         // a column the module does not have
            r#"[{"kind":"char","nxt":150001}]"#,                   // renamed
            r#"[{"kind":7,"next":150001}]"#,                       // another type
            r#"[{"kind":"char","next":4294967296}]"#,              // out of the column's range
            r#"[{"kind":"char","next":-1}]"#,
            r#"{"kind":"char","next":150001}"#,                    // not a list of rows
            r#"[{"kind":"char","next":150001}"#,                   // cut short
        ] {
            let e = staged("id_counter", bad).rows::<IdCounter>("id_counter").err().unwrap_or_else(|| panic!("accepted: {bad}"));
            assert!(e.starts_with("table id_counter"), "{e}");
        }
        // a pet row from a module whose `pet` had no `autofeed` column
        let old_pet = r#"[{"pet_id":1,"class_":1002,"name":"Poring","account_id":2000000,"char_id":150000,"level":1,"egg_id":9001,"equip":0,"intimate":250,"hungry":100,"rename_flag":0,"incubate":1}]"#;
        let e = staged("pet", old_pet).rows::<Pet>("pet").err().unwrap();
        assert_eq!(e, "table pet, row 0: row: the column `autofeed` is missing");
        // the database spells the module's `card0` as `card_0` and `class_` as `class`: both spellings are the column
        let pet = |class: &str| format!(r#"[{{"pet_id":1,"{class}":1002,"name":"Poring","account_id":2000000,"char_id":150000,"level":1,"egg_id":9001,"equip":0,"intimate":250,"hungry":100,"rename_flag":0,"incubate":1,"autofeed":false}}]"#);
        assert_eq!(staged("pet", &pet("class")).rows::<Pet>("pet").unwrap()[0].class_, 1002);
        assert_eq!(staged("pet", &pet("class_")).rows::<Pet>("pet").unwrap()[0].class_, 1002);
        assert!(staged("pet", &pet("klass")).rows::<Pet>("pet").is_err());
        // inside a nested struct and an Option too
        use crate::inventory::InventoryItem;
        let item = r#"{"nameid":1201,"amount":1,"equip":2,"identify":1,"refine":0,"attribute":0,"card_0":0,"card_1":0,"card_2":0,"card_3":0,"option":[{"id":0,"value":0}],"expire_time":0,"favorite":0,"bound":0,"unique_id":1,"equip_switch":0,"enchantgrade":0}"#;
        let e = staged("inventory", &format!(r#"[{{"id":7,"char_id":150000,"idx":0,"save_id":1,"item":{item}}}]"#)).rows::<InventoryItem>("inventory").err().unwrap();
        assert_eq!(e, "table inventory, row 0: row.item.option[0]: the column `param` is missing");
        use crate::account::Session;
        let sess = r#"[{"identity":{"__identity__":"0x0101010101010101010101010101010101010101010101010101010101010101"},"account_id":2000000,"since":{"__timestamp_micros_since_unix_epoch__":1},"selected_char":{"maybe":1},"online":false,"connection":{"none":[]}}]"#;
        assert!(staged("session", sess).rows::<Session>("session").err().unwrap().contains("`maybe` is not a variant"));
    }

    #[test]
    fn header_rule() {
        let h = |version: u32, counts: &str| format!(r#"{{"format":"loki-cutover","version":{version},"counts":{counts}}}"#);
        let (v, counts) = check_header(&h(VERSION, r#"{"account":2,"character":3}"#)).unwrap();
        assert_eq!(v, VERSION);
        assert_eq!(counts.len(), 2);
        // a newer file, an older one than the module can upgrade, another format, no counts
        assert!(check_header(&h(VERSION + 1, "{}")).unwrap_err().contains("newer Loki"));
        assert!(check_header(&h(0, "{}")).is_err());
        assert!(check_header(r#"{"format":"something","version":1,"counts":{}}"#).is_err());
        assert!(check_header(r#"{"format":"loki-cutover","version":1}"#).is_err());
        assert!(check_header("not json").is_err());
        // a table this module does not import, and one it does not have
        assert!(check_header(&h(VERSION, r#"{"pc":1}"#)).unwrap_err().contains("does not import"));
        assert!(check_header(&h(VERSION, r#"{"wings":1}"#)).unwrap_err().contains("does not have"));
        assert!(upgrade(VERSION, "account", "[]".into()).is_ok());
        assert!(upgrade(VERSION + 1, "account", "[]".into()).is_err());
    }
}
