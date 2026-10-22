//! map/log.cpp + log.hpp (rAthena e985006): the map server's logs of item and zeny movements, cash points, MVP
//! prizes, Dead Branch use, @commands, `logmes`, chat and pet / homunculus feeding.
//!
//! Every log function is ported with its config switches (conf/log_athena.conf, [`LOG_CONFIG`]) and filters
//! (should_log_item, the `log_chat` bits, the `log_zeny` threshold); the calls sit where rAthena makes them (the
//! table of call sites is in server/ARCHITECTURE.md).
//!
//! Deliberately different:
//! - rAthena writes a log either as a row of a MySQL table of the log database (`sql_logs: yes`, the default) or,
//!   with `sql_logs: no`, as a text line appended to a file (fopen / strftime with `log_timestamp_format`). The module
//!   has no filesystem: only the SQL branch is ported. Every table of sql-files/logs.sql that log.cpp writes is a
//!   private (non-public) SpacetimeDB table here, with the name log_athena.conf gives it (`log_*_db`) and the same
//!   columns (types mapped; a column named `type` is `type_`, a Rust keyword). The auto-increment id is a u64.
//! - `NOW()` is the reducer's own time (`c.rc.timestamp`, the host clock), never gettick(). The date columns are
//!   `Timestamp`s, except `npclog.npc_date` (i64 unix seconds): that table was published by wave D before this port,
//!   and changing a column's type is an incompatible schema change (a `publish -c`).
//! - `LOG_QUERY` ("INSERT" for InnoDB, "INSERT DELAYED" for MyISAM) is one insert. The SQL server's own column limits
//!   are not reproduced: MySQL truncates or (strict sql_mode) rejects a value wider than its column
//!   (`chatlog.message` is varchar(150) while log_chat binds up to CHAT_SIZE_MAX) or a `type` letter missing from an
//!   ENUM list (`zenylog` / `cashlog` list fewer letters than log_picktype2char returns); here the row keeps what
//!   rAthena's code binds (its own strnlen / safestrnlen caps are ported).
//! - The module is `crate::map_log` (lib.rs: `#[path = "log.rs"]`): a module named `log` would shadow the `log`
//!   crate that `log::info!` etc. name everywhere.

use spacetimedb::{table, Table, Timestamp};

use crate::inventory::Item;
use crate::npc::npc as _;
use crate::world::Ctx;

// ---------------------------------------------------------------------------------------------- log.hpp enums

/// log.hpp enum e_log_chat_type (uint8).
pub type LogChatType = u8;
pub const LOG_CHAT_GLOBAL: LogChatType = 0x01;
pub const LOG_CHAT_WHISPER: LogChatType = 0x02;
pub const LOG_CHAT_PARTY: LogChatType = 0x04;
pub const LOG_CHAT_GUILD: LogChatType = 0x08;
pub const LOG_CHAT_MAINCHAT: LogChatType = 0x10;
pub const LOG_CHAT_CLAN: LogChatType = 0x20;
pub const LOG_CHAT_ALL: LogChatType = 0xFF;

/// log.hpp enum e_log_pick_type (uint32): the item / zeny / cash log's transaction type.
pub type LogPickType = u32;
pub const LOG_TYPE_NONE: LogPickType = 0x0000_0000;
pub const LOG_TYPE_TRADE: LogPickType = 0x0000_0001;
pub const LOG_TYPE_VENDING: LogPickType = 0x0000_0002;
pub const LOG_TYPE_PICKDROP_PLAYER: LogPickType = 0x0000_0004;
pub const LOG_TYPE_PICKDROP_MONSTER: LogPickType = 0x0000_0008;
pub const LOG_TYPE_NPC: LogPickType = 0x0000_0010;
pub const LOG_TYPE_SCRIPT: LogPickType = 0x0000_0020;
pub const LOG_TYPE_STEAL: LogPickType = 0x0000_0040;
pub const LOG_TYPE_CONSUME: LogPickType = 0x0000_0080;
pub const LOG_TYPE_PRODUCE: LogPickType = 0x0000_0100;
pub const LOG_TYPE_MVP: LogPickType = 0x0000_0200;
pub const LOG_TYPE_COMMAND: LogPickType = 0x0000_0400;
pub const LOG_TYPE_STORAGE: LogPickType = 0x0000_0800;
pub const LOG_TYPE_GSTORAGE: LogPickType = 0x0000_1000;
pub const LOG_TYPE_MAIL: LogPickType = 0x0000_2000;
pub const LOG_TYPE_AUCTION: LogPickType = 0x0000_4000;
pub const LOG_TYPE_BUYING_STORE: LogPickType = 0x0000_8000;
pub const LOG_TYPE_OTHER: LogPickType = 0x0001_0000;
pub const LOG_TYPE_CASH: LogPickType = 0x0002_0000;
pub const LOG_TYPE_BANK: LogPickType = 0x0004_0000;
pub const LOG_TYPE_BOUND_REMOVAL: LogPickType = 0x0008_0000;
pub const LOG_TYPE_ROULETTE: LogPickType = 0x0010_0000;
pub const LOG_TYPE_MERGE_ITEM: LogPickType = 0x0020_0000;
pub const LOG_TYPE_QUEST: LogPickType = 0x0040_0000;
pub const LOG_TYPE_PRIVATE_AIRSHIP: LogPickType = 0x0080_0000;
pub const LOG_TYPE_BARTER: LogPickType = 0x0100_0000;
pub const LOG_TYPE_LAPHINE: LogPickType = 0x0200_0000;
pub const LOG_TYPE_ENCHANTGRADE: LogPickType = 0x0400_0000;
pub const LOG_TYPE_REFORM: LogPickType = 0x0800_0000;
pub const LOG_TYPE_ENCHANT: LogPickType = 0x1000_0000;
pub const LOG_TYPE_PACKAGE: LogPickType = 0x2000_0000;
/// combinations
pub const LOG_TYPE_LOOT: LogPickType = LOG_TYPE_PICKDROP_MONSTER | LOG_TYPE_CONSUME;
/// all
pub const LOG_TYPE_ALL: LogPickType = 0xFFFF_FFFF;

/// log.hpp enum e_log_cash_type (uint8).
pub type LogCashType = u8;
pub const LOG_CASH_TYPE_CASH: LogCashType = 0x1;
pub const LOG_CASH_TYPE_KAFRA: LogCashType = 0x2;

/// log.hpp enum e_log_feeding_type (uint8).
pub type LogFeedingType = u8;
pub const LOG_FEED_HOMUNCULUS: LogFeedingType = 0x1;
pub const LOG_FEED_PET: LogFeedingType = 0x2;

/// log.cpp enum e_log_filter: the bits of `log_filter` (should_log_item).
pub const LOG_FILTER_NONE: i32 = 0x000;
pub const LOG_FILTER_ALL: i32 = 0x001;
/// Healing items (0)
pub const LOG_FILTER_HEALING: i32 = 0x002;
/// Etc Items(3) + Arrows (10)
pub const LOG_FILTER_ETC_AMMO: i32 = 0x004;
/// Usable Items(2) + Scrolls, Lures(11) + Usable Cash Items(18) (the test is IT_USABLE or IT_CASH only)
pub const LOG_FILTER_USABLE: i32 = 0x008;
/// Weapons(4)
pub const LOG_FILTER_WEAPON: i32 = 0x010;
/// Shields, Armors, Headgears, Accessories, Garments and Shoes(5)
pub const LOG_FILTER_ARMOR: i32 = 0x020;
/// Cards(6)
pub const LOG_FILTER_CARD: i32 = 0x040;
/// Pet Accessories(8) + Eggs(7)
pub const LOG_FILTER_PETITEM: i32 = 0x080;
/// Log expensive items ( >= price_log )
pub const LOG_FILTER_PRICE: i32 = 0x100;
/// Log large amount of items ( >= amount_log )
pub const LOG_FILTER_AMOUNT: i32 = 0x200;
/// Log refined items ( refine >= refine_log )
pub const LOG_FILTER_REFINE: i32 = 0x400;
/// Log rare items and Emperium ( drop chance <= rare_log )
pub const LOG_FILTER_CHANCE: i32 = 0x800;

/// mmo.hpp NAME_LENGTH (23 + 1): strnlen(name, NAME_LENGTH) of the name columns.
const NAME_LENGTH: usize = 24;
/// map.hpp CHAT_SIZE_MAX (255 + 1): safestrnlen(message, CHAT_SIZE_MAX) of chatlog.message.
const CHAT_SIZE_MAX: usize = 256;
/// itemdb.hpp ITEMID_EMPERIUM.
const ITEMID_EMPERIUM: u32 = 714;

// ---------------------------------------------------------------------------------------------- log_config

/// log.hpp struct Log_Config, without its table / file names (`log_branch[64]`, `log_pick[64]`, …: they are the
/// table names below) and log.cpp's `log_timestamp_format` (the file logs' strftime format, not ported).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct LogConfig {
    pub enable_logs: LogPickType,
    pub filter: i32,
    pub sql_logs: bool,
    pub log_chat_woe_disable: bool,
    pub cash: bool,
    pub rare_items_log: i32,
    pub refine_items_log: i32,
    pub price_items_log: i32,
    pub amount_items_log: i32,
    pub branch: i32,
    pub mvpdrop: i32,
    pub zeny: i32,
    pub commands: i32,
    pub npc: i32,
    pub chat: i32,
    /// `unsigned feeding : 2`.
    pub feeding: u8,
}

/// log.cpp log_set_defaults: `memset(&log_config, 0, …)`, then the filter defaults (refine >= +5, drop chance <= 1%,
/// 1000z, 100 items). log_config_read calls it once, before the first file is read.
pub const fn log_set_defaults() -> LogConfig {
    LogConfig {
        enable_logs: LOG_TYPE_NONE,
        filter: 0,
        sql_logs: false,
        log_chat_woe_disable: false,
        cash: false,
        rare_items_log: 100,
        refine_items_log: 5,
        price_items_log: 1000,
        amount_items_log: 100,
        branch: 0,
        mvpdrop: 0,
        zeny: 0,
        commands: 0,
        npc: 0,
        chat: 0,
        feeding: 0,
    }
}

/// log.cpp log_config_read("conf/log_athena.conf"): rAthena's values after the file and its
/// `import: conf/import/log_conf.txt` (Loki has no log_conf.txt override in data/overlay/conf/import/, so the file's
/// values stand; every key of Log_Config is in the file, so no log_set_defaults value survives). The numeric keys go
/// through config_switch (yes / no, or strtol base 0) or atoi as log_config_read reads them. Reloading
/// (@reloadlogconf) is not possible: the values are compiled in.
pub const LOG_CONFIG: LogConfig = LogConfig {
    // `enable_logs: 0xFFFFFFFF` (config_switch: strtol, then (e_log_pick_type)(int32)): every type. (A 32-bit long,
    // Windows, saturates strtol to 0x7FFFFFFF: the same types, bit 31 is none.)
    enable_logs: 0xFFFF_FFFF,
    // `log_filter: 1` = LOG_FILTER_ALL: any item.
    filter: 1,
    // `sql_logs: yes`
    sql_logs: true,
    // `log_chat_woe_disable: no`
    log_chat_woe_disable: false,
    // `log_cash: yes`
    cash: true,
    // `rare_items_log: 100` (1%)
    rare_items_log: 100,
    // `refine_items_log: 5`
    refine_items_log: 5,
    // `price_items_log: 1000`
    price_items_log: 1000,
    // `amount_items_log: 100`
    amount_items_log: 100,
    // `log_branch: no`
    branch: 0,
    // `log_mvpdrop: no`
    mvpdrop: 0,
    // `log_zeny: 1` (Loki Host: conf/import/log_conf.txt; 0 don't log, 1 every change, 2..: the smallest absolute amount logged)
    zeny: 1,
    // `log_commands: yes` (only groups with LogCommands: true, pc_should_log_commands)
    commands: 1,
    // `log_npc: yes`
    npc: 1,
    // `log_chat: 0` (LOG_CHAT_* bits)
    chat: 0,
    // `log_feeding: 3` (both LOG_FEED_* bits; the file's comment names 1 Pet / 2 Homunculus, the enum is the reverse)
    feeding: 3,
};

/// conf/import/log_conf.txt, the file log_athena.conf imports last (`import: conf/import/log_conf.txt`): rAthena's
/// `log_config` is a global that log_config_read fills from the files and @reloadlogconf fills again. The module has
/// no files, so the import is one row (id 0) of this private table, named after the file and with the conf's keys as
/// columns; an operator writes it with SQL (`INSERT INTO log_conf (id, enable_logs, …) VALUES (0, …)`), and it is
/// read at every log call, so a change needs no reload. No row (the default): [`LOG_CONFIG`], rAthena's own values.
/// A row holds every key (log_config_read's keys that give a table or file name, and log_timestamp_format, have no
/// column: the tables are fixed, the file logs are not ported). Added by wave G (glog): the parity scenarios switch
/// on the logs rAthena leaves off (log_zeny, log_chat, log_branch, log_mvpdrop) on both servers.
#[table(accessor = log_conf)]
#[derive(Clone, Debug, PartialEq)]
pub struct LogConf {
    #[primary_key]
    pub id: u8,
    pub enable_logs: u32,
    pub log_filter: i32,
    pub sql_logs: bool,
    pub log_chat_woe_disable: bool,
    pub log_cash: bool,
    pub rare_items_log: i32,
    pub refine_items_log: i32,
    pub price_items_log: i32,
    pub amount_items_log: i32,
    pub log_branch: i32,
    pub log_mvpdrop: i32,
    pub log_zeny: i32,
    pub log_commands: i32,
    pub log_npc: i32,
    pub log_chat: i32,
    pub log_feeding: u8,
}

impl LogConf {
    /// The row as log.hpp's Log_Config (`unsigned feeding : 2`: the two low bits).
    pub fn config(&self) -> LogConfig {
        LogConfig {
            enable_logs: self.enable_logs,
            filter: self.log_filter,
            sql_logs: self.sql_logs,
            log_chat_woe_disable: self.log_chat_woe_disable,
            cash: self.log_cash,
            rare_items_log: self.rare_items_log,
            refine_items_log: self.refine_items_log,
            price_items_log: self.price_items_log,
            amount_items_log: self.amount_items_log,
            branch: self.log_branch,
            mvpdrop: self.log_mvpdrop,
            zeny: self.log_zeny,
            commands: self.log_commands,
            npc: self.log_npc,
            chat: self.log_chat,
            feeding: self.log_feeding & 3,
        }
    }
}

/// rAthena's global `log_config` as it is now: the `log_conf` row when there is one, else [`LOG_CONFIG`].
pub fn log_config(c: &Ctx) -> LogConfig {
    c.db().log_conf().id().find(0).map(|r| r.config()).unwrap_or(LOG_CONFIG)
}

// ---------------------------------------------------------------------------------------------- tables

/// sql-files/logs.sql `atcommandlog` (log_athena.conf `log_gm_db: atcommandlog`): log_atcommand.
#[table(accessor = atcommandlog)]
#[derive(Clone, Debug, PartialEq)]
pub struct AtcommandLog {
    #[primary_key]
    #[auto_inc]
    pub atcommand_id: u64,
    pub atcommand_date: Timestamp,
    pub account_id: u32,
    pub char_id: u32,
    pub char_name: String,
    pub map: String,
    pub command: String,
}

/// sql-files/logs.sql `branchlog` (log_athena.conf `log_branch_db: branchlog`): log_branch.
#[table(accessor = branchlog)]
#[derive(Clone, Debug, PartialEq)]
pub struct BranchLog {
    #[primary_key]
    #[auto_inc]
    pub branch_id: u64,
    pub branch_date: Timestamp,
    pub account_id: i32,
    pub char_id: i32,
    pub char_name: String,
    pub map: String,
}

/// sql-files/logs.sql `cashlog` (log_athena.conf `log_cash_db: cashlog`): log_cash.
#[table(accessor = cashlog)]
#[derive(Clone, Debug, PartialEq)]
pub struct CashLog {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub time: Timestamp,
    pub char_id: i32,
    /// `type`: log_picktype2char.
    pub type_: String,
    /// log_cashtype2char.
    pub cash_type: String,
    pub amount: i32,
    pub map: String,
}

/// sql-files/logs.sql `chatlog` (log_athena.conf `log_chat_db: chatlog`): log_chat.
#[table(accessor = chatlog)]
#[derive(Clone, Debug, PartialEq)]
pub struct ChatLog {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub time: Timestamp,
    /// `type`: log_chattype2char.
    pub type_: String,
    pub type_id: i32,
    pub src_charid: i32,
    pub src_accountid: i32,
    pub src_map: String,
    pub src_map_x: i16,
    pub src_map_y: i16,
    pub dst_charname: String,
    pub message: String,
}

/// sql-files/logs.sql `feedinglog` (log_athena.conf `log_feeding_db: feedinglog`): log_feeding.
#[table(accessor = feedinglog)]
#[derive(Clone, Debug, PartialEq)]
pub struct FeedingLog {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub time: Timestamp,
    pub char_id: i32,
    pub target_id: i32,
    pub target_class: i16,
    /// `type`: log_feedingtype2char (P: Pet, H: Homunculus, O: Other).
    pub type_: String,
    pub intimacy: u32,
    pub item_id: u32,
    pub map: String,
    pub x: u16,
    pub y: u16,
}

/// sql-files/logs.sql `mvplog` (log_athena.conf `log_mvpdrop_db: mvplog`): log_mvpdrop.
#[table(accessor = mvplog)]
#[derive(Clone, Debug, PartialEq)]
pub struct MvpLog {
    #[primary_key]
    #[auto_inc]
    pub mvp_id: u64,
    pub mvp_date: Timestamp,
    pub kill_char_id: i32,
    pub monster_id: i16,
    pub prize: u32,
    pub mvpexp: u64,
    pub map: String,
}

/// sql-files/logs.sql `npclog` (log_athena.conf `log_npc_db: npclog`): log_npc, what the script command `logmes`
/// writes. (Wave D's table, moved here unchanged: `npc_date` stays unix seconds, see the module doc.)
#[table(accessor = npclog)]
#[derive(Clone, Debug, PartialEq)]
pub struct NpcLog {
    #[primary_key]
    #[auto_inc]
    pub npc_id: u64,
    /// NOW(), in seconds since the epoch.
    pub npc_date: i64,
    pub account_id: u32,
    pub char_id: u32,
    pub char_name: String,
    pub map: String,
    pub mes: String,
}

/// sql-files/logs.sql `picklog` (log_athena.conf `log_pick_db: picklog`): log_pick (log_pick_pc / log_pick_mob).
/// `char_id` is the character's id, or the monster's class for a monster (log_pick_mob: md->mob_id).
#[table(accessor = picklog)]
#[derive(Clone, Debug, PartialEq)]
pub struct PickLog {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub time: Timestamp,
    pub char_id: i32,
    /// `type`: log_picktype2char.
    pub type_: String,
    pub nameid: u32,
    pub amount: i32,
    pub refine: u8,
    pub card0: u32,
    pub card1: u32,
    pub card2: u32,
    pub card3: u32,
    pub option_id0: i16,
    pub option_val0: i16,
    pub option_parm0: i8,
    pub option_id1: i16,
    pub option_val1: i16,
    pub option_parm1: i8,
    pub option_id2: i16,
    pub option_val2: i16,
    pub option_parm2: i8,
    pub option_id3: i16,
    pub option_val3: i16,
    pub option_parm3: i8,
    pub option_id4: i16,
    pub option_val4: i16,
    pub option_parm4: i8,
    pub unique_id: u64,
    pub map: String,
    pub bound: u8,
    pub enchantgrade: u8,
}

/// sql-files/logs.sql `zenylog` (log_athena.conf `log_zeny_db: zenylog`): log_zeny.
#[table(accessor = zenylog)]
#[derive(Clone, Debug, PartialEq)]
pub struct ZenyLog {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub time: Timestamp,
    pub char_id: i32,
    pub src_id: i32,
    /// `type`: log_picktype2char.
    pub type_: String,
    pub amount: i32,
    pub map: String,
}

// ---------------------------------------------------------------------------------------------- type2char

/// log.cpp log_picktype2char: the item / zeny / cash log's type letter ('X', with a debug message, for a type that
/// is none of the listed ones).
pub fn log_picktype2char(type_: LogPickType) -> char {
    match type_ {
        LOG_TYPE_TRADE => 'T',             // (T)rade
        LOG_TYPE_VENDING => 'V',           // (V)ending
        LOG_TYPE_PICKDROP_PLAYER => 'P',   // (P)layer
        LOG_TYPE_PICKDROP_MONSTER => 'M',  // (M)onster
        LOG_TYPE_NPC => 'S',               // NPC (S)hop
        LOG_TYPE_SCRIPT => 'N',            // (N)PC Script
        LOG_TYPE_STEAL => 'D',             // Steal/Snatcher
        LOG_TYPE_CONSUME => 'C',           // (C)onsumed
        LOG_TYPE_PRODUCE => 'O',           // Pr(O)duced/Ingredients
        LOG_TYPE_MVP => 'U',               // MVP Rewards
        LOG_TYPE_COMMAND => 'A',           // (A)dmin command
        LOG_TYPE_STORAGE => 'R',           // Sto(R)age
        LOG_TYPE_GSTORAGE => 'G',          // (G)uild storage
        LOG_TYPE_MAIL => 'E',              // (E)mail attachment
        LOG_TYPE_AUCTION => 'I',           // Auct(I)on
        LOG_TYPE_BUYING_STORE => 'B',      // (B)uying Store
        LOG_TYPE_LOOT => 'L',              // (L)oot (consumed monster pick/drop)
        LOG_TYPE_BANK => 'K',              // Ban(K) Transactions
        LOG_TYPE_OTHER => 'X',             // Other
        LOG_TYPE_CASH => '$',              // Cash
        LOG_TYPE_BOUND_REMOVAL => 'F',     // Removed bound items when guild/party is broken
        LOG_TYPE_ROULETTE => 'Y',          // Roulette Lotter(Y)
        LOG_TYPE_MERGE_ITEM => 'Z',        // Merged Item
        LOG_TYPE_QUEST => 'Q',             // (Q)uest Item
        LOG_TYPE_PRIVATE_AIRSHIP => 'H',   // Private Airs(H)ip
        LOG_TYPE_BARTER => 'J',            // Barter Shop
        LOG_TYPE_LAPHINE => 'W',           // Laphine UI
        LOG_TYPE_ENCHANTGRADE => '0',      // Enchantgrade UI
        LOG_TYPE_REFORM => '1',            // Reform UI
        LOG_TYPE_ENCHANT => '2',           // Echant UI
        LOG_TYPE_PACKAGE => '3',           // Item Package Selection
        _ => {
            // should not get here, fallback
            log::debug!("log_picktype2char: Unknown pick type {}.", type_ as i32);
            'X'
        }
    }
}

/// log.cpp log_chattype2char ('O', with a debug message, for an unknown type).
pub fn log_chattype2char(type_: LogChatType) -> char {
    match type_ {
        LOG_CHAT_GLOBAL => 'O',   // Gl(O)bal
        LOG_CHAT_WHISPER => 'W',  // (W)hisper
        LOG_CHAT_PARTY => 'P',    // (P)arty
        LOG_CHAT_GUILD => 'G',    // (G)uild
        LOG_CHAT_MAINCHAT => 'M', // (M)ain chat
        LOG_CHAT_CLAN => 'C',     // (C)lan
        _ => {
            // should not get here, fallback
            log::debug!("log_chattype2char: Unknown chat type {type_}.");
            'O'
        }
    }
}

/// log.cpp log_cashtype2char ('O' for an unknown type; rAthena's debug message names log_chattype2char).
pub fn log_cashtype2char(type_: LogCashType) -> char {
    match type_ {
        LOG_CASH_TYPE_CASH => 'C',
        LOG_CASH_TYPE_KAFRA => 'K',
        _ => {
            log::debug!("log_chattype2char: Unknown chat type {type_}.");
            'O'
        }
    }
}

/// log.cpp log_feedingtype2char ('O', with a debug message, for an unknown type).
pub fn log_feedingtype2char(type_: LogFeedingType) -> char {
    match type_ {
        LOG_FEED_HOMUNCULUS => 'H',
        LOG_FEED_PET => 'P',
        _ => {
            log::debug!("log_feedingtype2char: Unknown feeding type {type_}.");
            'O'
        }
    }
}

// ---------------------------------------------------------------------------------------------- filters

/// The item_data fields should_log_item reads (item_db.find(nameid)).
#[derive(Clone, Copy, Debug)]
pub struct ItemFacts {
    pub nameid: u32,
    /// item_data::type (IT_*).
    pub type_: u8,
    pub value_buy: u32,
}

/// log.cpp should_log_item for an item that is in item_db, with `cfg` as log_config: true when any condition of the
/// filter holds. `maxchance` is item_data::maxchance, only read for LOG_FILTER_CHANCE (as C's `&&` / `||` do).
pub fn should_log_item_sub(cfg: &LogConfig, id: ItemFacts, amount: i32, refine: i32, maxchance: impl FnOnce() -> i32) -> bool {
    use crate::db::items::{IT_AMMO, IT_ARMOR, IT_CARD, IT_CASH, IT_ETC, IT_HEALING, IT_PETARMOR, IT_PETEGG, IT_USABLE, IT_WEAPON};
    let filter = cfg.filter;
    (filter & LOG_FILTER_ALL != 0)
        || (filter & LOG_FILTER_HEALING != 0 && id.type_ == IT_HEALING)
        || (filter & LOG_FILTER_ETC_AMMO != 0 && (id.type_ == IT_ETC || id.type_ == IT_AMMO))
        || (filter & LOG_FILTER_USABLE != 0 && (id.type_ == IT_USABLE || id.type_ == IT_CASH))
        || (filter & LOG_FILTER_WEAPON != 0 && id.type_ == IT_WEAPON)
        || (filter & LOG_FILTER_ARMOR != 0 && id.type_ == IT_ARMOR)
        || (filter & LOG_FILTER_CARD != 0 && id.type_ == IT_CARD)
        || (filter & LOG_FILTER_PETITEM != 0 && (id.type_ == IT_PETEGG || id.type_ == IT_PETARMOR))
        // uint32 value_buy >= int32 price_items_log: the int32 converts to uint32
        || (filter & LOG_FILTER_PRICE != 0 && id.value_buy >= cfg.price_items_log as u32)
        || (filter & LOG_FILTER_AMOUNT != 0 && amount.wrapping_abs() >= cfg.amount_items_log)
        || (filter & LOG_FILTER_REFINE != 0 && refine >= cfg.refine_items_log)
        || (filter & LOG_FILTER_CHANCE != 0 && ({
            let mc = maxchance();
            mc != -1 && mc <= cfg.rare_items_log
        } || id.nameid == ITEMID_EMPERIUM))
}

/// log.cpp should_log_item: false for an item that is not in item_db.
fn should_log_item(c: &Ctx, nameid: u32, amount: i32, refine: i32) -> bool {
    let Some(id) = crate::db::items::itemdb_exists(nameid) else { return false };
    let facts = ItemFacts { nameid: id.nameid, type_: id.r#type, value_buy: id.value_buy };
    should_log_item_sub(&log_config(c), facts, amount, refine, || crate::npc::item_maxchance(c, nameid))
}

/// log_pick's first test: `(log_config.enable_logs & type) == 0` means disabled.
pub fn pick_enabled(cfg: &LogConfig, type_: LogPickType) -> bool {
    cfg.enable_logs & type_ != 0
}

/// log_zeny's first test: `!log_config.zeny || (log_config.zeny != 1 && abs(amount) < log_config.zeny)` returns.
pub fn zeny_enabled(cfg: &LogConfig, amount: i32) -> bool {
    !(cfg.zeny == 0 || (cfg.zeny != 1 && amount.wrapping_abs() < cfg.zeny))
}

/// log_chat's tests: the type's bit in `log_chat`, and no logging during WoE with log_chat_woe_disable.
pub fn chat_enabled(cfg: &LogConfig, type_: LogChatType, agit_start: impl FnOnce() -> bool) -> bool {
    if cfg.chat & type_ as i32 == 0 {
        return false; // disabled
    }
    if cfg.log_chat_woe_disable && agit_start() {
        return false; // no chat logging during woe
    }
    true
}

/// log_feeding's test: `!(log_config.feeding & type)` returns.
pub fn feeding_enabled(cfg: &LogConfig, type_: LogFeedingType) -> bool {
    cfg.feeding & type_ != 0
}

// ---------------------------------------------------------------------------------------------- helpers

/// strnlen / safestrnlen(s, n) of a latin-1 string (one char per byte; a NUL ends it as in C).
fn cut(s: &str, n: usize) -> String {
    s.chars().take_while(|&ch| ch != '\0').take(n).collect()
}

/// mapindex_id2name(sd->mapindex): the name of the map the character is on ("" before its first pc_setpos:
/// mapindex 0, indexes[0].name).
pub fn sd_mapname(c: &Ctx, aid: u32) -> String {
    crate::pc::get(c, aid).and_then(|p| p.mapindex).and_then(|m| crate::data::maps().get(m)).map(|d| d.name.clone()).unwrap_or_default()
}

/// sd->status.char_id of the account's character (0 without one): what callers pass as pc_payzeny / pc_getzeny's
/// `log_charid` (the other party of a trade, a vending, a buying store).
pub fn char_id_of(c: &Ctx, aid: u32) -> u32 {
    crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0)
}

/// map_getmapdata(m)->name ("" for no map).
fn map_name(m: u16) -> String {
    crate::data::maps().get(m).map(|d| d.name.clone()).unwrap_or_default()
}

/// The session fields the logs read: sd->status.account_id / char_id / name, sd->m, sd->x / sd->y.
struct Sd {
    account_id: u32,
    char_id: u32,
    name: String,
    m: u16,
    x: i16,
    y: i16,
}

/// nullpo_retv(sd): None when the account has no character in the world.
fn sd_of(c: &Ctx, aid: u32) -> Option<Sd> {
    let p = crate::pc::get(c, aid)?;
    let u = crate::unit::get_or_freed(c, aid)?;
    Some(Sd { account_id: aid, char_id: p.char_id, name: u.name, m: u.map, x: u.x, y: u.y })
}

// ---------------------------------------------------------------------------------------------- rows

/// The picklog row log_pick inserts (SQL branch).
pub fn pick_row(time: Timestamp, id: u32, type_: LogPickType, amount: i32, map: String, itm: &Item) -> PickLog {
    let o = itm.options();
    PickLog {
        id: 0,
        time,
        // '%u' of the int32 id into an int(11) column
        char_id: id as i32,
        type_: log_picktype2char(type_).to_string(),
        nameid: itm.nameid,
        amount,
        refine: itm.refine,
        card0: itm.card0,
        card1: itm.card1,
        card2: itm.card2,
        card3: itm.card3,
        option_id0: o[0].id,
        option_val0: o[0].value,
        option_parm0: o[0].param as i8,
        option_id1: o[1].id,
        option_val1: o[1].value,
        option_parm1: o[1].param as i8,
        option_id2: o[2].id,
        option_val2: o[2].value,
        option_parm2: o[2].param as i8,
        option_id3: o[3].id,
        option_val3: o[3].value,
        option_parm3: o[3].param as i8,
        option_id4: o[4].id,
        option_val4: o[4].value,
        option_parm4: o[4].param as i8,
        unique_id: itm.unique_id,
        map,
        bound: itm.bound,
        enchantgrade: itm.enchantgrade,
    }
}

/// The chatlog row log_chat inserts (SQL branch): dst_charname capped at NAME_LENGTH, the message at CHAT_SIZE_MAX.
#[allow(clippy::too_many_arguments)]
pub fn chat_row(time: Timestamp, type_: LogChatType, type_id: i32, src_charid: i32, src_accid: i32, mapname: &str, x: i32, y: i32, dst_charname: Option<&str>, message: &str) -> ChatLog {
    // if( dst_charname == nullptr ) dst_charname = "";
    let dst_charname = dst_charname.unwrap_or("");
    ChatLog {
        id: 0,
        time,
        type_: log_chattype2char(type_).to_string(),
        type_id,
        src_charid,
        src_accountid: src_accid,
        src_map: mapname.to_string(),
        src_map_x: x as i16,
        src_map_y: y as i16,
        dst_charname: cut(dst_charname, NAME_LENGTH),
        message: cut(message, CHAT_SIZE_MAX),
    }
}

// ---------------------------------------------------------------------------------------------- log.cpp

/// log.cpp log_branch: logs items that summon monsters (Dead Branch and the like; pc_isUseitem).
pub fn log_branch(c: &Ctx, aid: u32) {
    let Some(sd) = sd_of(c, aid) else { return };
    if log_config(c).branch == 0 {
        return;
    }
    if !log_config(c).sql_logs {
        // the file branch (fopen(log_config.log_branch, "a"), a strftime'd line): not ported, no filesystem
        return;
    }
    c.db().branchlog().insert(BranchLog {
        branch_id: 0,
        branch_date: c.rc.timestamp,
        account_id: sd.account_id as i32,
        char_id: sd.char_id as i32,
        char_name: cut(&sd.name, NAME_LENGTH),
        map: sd_mapname(c, aid),
    });
}

/// log.cpp log_pick: logs item transactions (generic): `id` is a char id or a monster class, `m` the map.
fn log_pick(c: &Ctx, id: u32, m: u16, type_: LogPickType, amount: i32, itm: &Item) {
    if !pick_enabled(&log_config(c), type_) {
        return; // disabled
    }
    if !should_log_item(c, itm.nameid, amount, itm.refine as i32) {
        return; //we skip logging this item set - it doesn't meet our logging conditions [Lupus]
    }
    if !log_config(c).sql_logs {
        // the file branch (fopen(log_config.log_pick, "a"), a strftime'd line): not ported, no filesystem
        return;
    }
    c.db().picklog().insert(pick_row(c.rc.timestamp, id, type_, amount, map_name(m), itm));
}

/// log.cpp log_pick_pc: logs item transactions (players): log_pick(sd->status.char_id, sd->m, …).
pub fn log_pick_pc(c: &Ctx, aid: u32, type_: LogPickType, amount: i32, itm: &Item) {
    let Some(sd) = sd_of(c, aid) else { return };
    log_pick(c, sd.char_id, sd.m, type_, amount, itm);
}

/// log.cpp log_pick_mob: logs item transactions (monsters): log_pick(md->mob_id, md->m, …). The caller passes the
/// two fields of its mob_data.
pub fn log_pick_mob(c: &Ctx, mob_id: i32, m: u16, type_: LogPickType, amount: i32, itm: &Item) {
    log_pick(c, mob_id as u32, m, type_, amount, itm);
}

/// log.cpp log_zeny: logs zeny transactions of `target_sd`; `src_id` is the other party's char id (0: none).
pub fn log_zeny(c: &Ctx, target_aid: u32, type_: LogPickType, src_id: u32, amount: i32) {
    if !zeny_enabled(&log_config(c), amount) {
        return;
    }
    let Some(sd) = crate::pc::get(c, target_aid) else { return };
    if !log_config(c).sql_logs {
        // the file branch (fopen(log_config.log_zeny, "a"), a strftime'd line): not ported, no filesystem
        return;
    }
    c.db().zenylog().insert(ZenyLog {
        id: 0,
        time: c.rc.timestamp,
        char_id: sd.char_id as i32,
        src_id: src_id as i32,
        type_: log_picktype2char(type_).to_string(),
        amount,
        map: sd_mapname(c, target_aid),
    });
}

/// log.cpp log_mvpdrop: logs MVP monster rewards (the prize item, 0 for none, and the MVP exp).
pub fn log_mvpdrop(c: &Ctx, aid: u32, monster_id: i32, nameid: u32, exp: u64) {
    let Some(sd) = sd_of(c, aid) else { return };
    if log_config(c).mvpdrop == 0 {
        return;
    }
    if !log_config(c).sql_logs {
        // the file branch (fopen(log_config.log_mvpdrop, "a"), a strftime'd line): not ported, no filesystem
        return;
    }
    c.db().mvplog().insert(MvpLog {
        mvp_id: 0,
        mvp_date: c.rc.timestamp,
        kill_char_id: sd.char_id as i32,
        monster_id: monster_id as i16,
        prize: nameid,
        mvpexp: exp,
        map: sd_mapname(c, aid),
    });
}

/// log.cpp log_atcommand: logs used atcommands, for groups whose LogCommands is set (pc_should_log_commands).
pub fn log_atcommand(c: &Ctx, aid: u32, message: &str) {
    let Some(sd) = sd_of(c, aid) else { return };
    let Some(p) = crate::pc::get(c, aid) else { return };
    // pc_should_log_commands: sd->group->log_commands
    if log_config(c).commands == 0 || !crate::rules::group(p.group_id).map(|g| g.log_commands).unwrap_or(false) {
        return;
    }
    if !log_config(c).sql_logs {
        // the file branch (fopen(log_config.log_gm, "a"), a strftime'd line): not ported, no filesystem
        return;
    }
    c.db().atcommandlog().insert(AtcommandLog {
        atcommand_id: 0,
        atcommand_date: c.rc.timestamp,
        account_id: sd.account_id,
        char_id: sd.char_id,
        char_name: cut(&sd.name, NAME_LENGTH),
        // sd->mapindex == 0 ? "" : mapindex_id2name(sd->mapindex)
        map: sd_mapname(c, aid),
        command: cut(message, 255),
    });
}

/// log.cpp log_npc(npc_data*, …): logs a `logmes` message of an NPC (no player attached).
pub fn log_npc_nd(c: &Ctx, npc_id: u32, message: &str) {
    let Some(nd) = c.db().npc().id().find(npc_id) else { return };
    if log_config(c).npc == 0 {
        return;
    }
    if !log_config(c).sql_logs {
        // the file branch (fopen(log_config.log_npc, "a"), a strftime'd line): not ported, no filesystem
        return;
    }
    c.db().npclog().insert(NpcLog {
        npc_id: 0,
        npc_date: c.rc.timestamp.to_micros_since_unix_epoch().div_euclid(1_000_000),
        // not in the INSERT: the columns' default '0'
        account_id: 0,
        char_id: 0,
        char_name: cut(&nd.name, NAME_LENGTH),
        // map_mapid2mapname(nd->m) ("Floating" for m -1, an instance map's source map name)
        map: crate::instance_map::map_mapid2mapname(c, nd.map),
        mes: cut(message, 255),
    });
}

/// log.cpp log_npc(map_session_data*, …): logs a `logmes` message of the attached player.
pub fn log_npc_sd(c: &Ctx, aid: u32, message: &str) {
    let Some(sd) = sd_of(c, aid) else { return };
    if log_config(c).npc == 0 {
        return;
    }
    if !log_config(c).sql_logs {
        // the file branch (fopen(log_config.log_npc, "a"), a strftime'd line): not ported, no filesystem
        return;
    }
    c.db().npclog().insert(NpcLog {
        npc_id: 0,
        npc_date: c.rc.timestamp.to_micros_since_unix_epoch().div_euclid(1_000_000),
        account_id: sd.account_id,
        char_id: sd.char_id,
        char_name: cut(&sd.name, NAME_LENGTH),
        map: sd_mapname(c, aid),
        mes: cut(message, 255),
    });
}

/// log.cpp log_chat: logs chat. `mapname` is the speaker's mapindex_id2name(sd->mapindex) ([`sd_mapname`]),
/// `dst_charname` the whisper's target (None for the other types).
#[allow(clippy::too_many_arguments)]
pub fn log_chat(c: &Ctx, type_: LogChatType, type_id: i32, src_charid: i32, src_accid: i32, mapname: &str, x: i32, y: i32, dst_charname: Option<&str>, message: &str) {
    if !chat_enabled(&log_config(c), type_, || crate::guild::is_agit_start(c)) {
        return;
    }
    if !log_config(c).sql_logs {
        // the file branch (fopen(log_config.log_chat, "a"), a strftime'd line): not ported, no filesystem
        return;
    }
    c.db().chatlog().insert(chat_row(c.rc.timestamp, type_, type_id, src_charid, src_accid, mapname, x, y, dst_charname, message));
}

/// log_chat with the speaker's fields as every rAthena caller passes them: `sd->status.char_id,
/// sd->status.account_id, mapindex_id2name(sd->mapindex), sd->x, sd->y` (clif_parse_GlobalMessage,
/// clif_parse_WisMessage, party_send_message, guild_send_message, clan_send_message, intif_main_message).
pub fn log_chat_sd(c: &Ctx, type_: LogChatType, type_id: i32, aid: u32, dst_charname: Option<&str>, message: &str) {
    if !chat_enabled(&log_config(c), type_, || crate::guild::is_agit_start(c)) {
        return; // (log_chat's own test, made before the speaker's fields are read)
    }
    let Some(sd) = sd_of(c, aid) else { return };
    log_chat(c, type_, type_id, sd.char_id as i32, sd.account_id as i32, &sd_mapname(c, aid), sd.x as i32, sd.y as i32, dst_charname, message);
}

/// log.cpp log_cash: logs cash / Kafra point transactions.
pub fn log_cash(c: &Ctx, aid: u32, type_: LogPickType, cash_type: LogCashType, amount: i32) {
    let Some(sd) = sd_of(c, aid) else { return };
    if !log_config(c).cash {
        return;
    }
    if !log_config(c).sql_logs {
        // the file branch (fopen(log_config.log_cash, "a"), a strftime'd line): not ported, no filesystem
        return;
    }
    c.db().cashlog().insert(CashLog {
        id: 0,
        time: c.rc.timestamp,
        char_id: sd.char_id as i32,
        type_: log_picktype2char(type_).to_string(),
        cash_type: log_cashtype2char(cash_type).to_string(),
        amount,
        map: sd_mapname(c, aid),
    });
}

/// log.cpp log_feeding: logs feeding activity. The target is the player's current homunculus (sd->hd) or pet
/// (sd->pd), their id, class and intimacy as they are now (0 when there is none).
pub fn log_feeding(c: &Ctx, aid: u32, type_: LogFeedingType, nameid: u32) {
    let (mut target_id, mut intimacy): (u32, u32) = (0, 0);
    let mut target_class: u16 = 0;
    let Some(sd) = sd_of(c, aid) else { return };
    if !feeding_enabled(&log_config(c), type_) {
        return;
    }
    match type_ {
        LOG_FEED_HOMUNCULUS => {
            if let Some(hd) = crate::homunculus::data::of_master(c, aid) {
                target_id = hd.homunculus.hom_id;
                target_class = hd.homunculus.class_ as u16;
                intimacy = hd.homunculus.intimacy;
            }
        }
        LOG_FEED_PET => {
            if let Some(pd) = crate::pet::data::of_master(c, aid) {
                target_id = pd.pet.pet_id;
                target_class = pd.pet.class_ as u16;
                intimacy = pd.pet.intimate as u32;
            }
        }
        _ => {}
    }
    if !log_config(c).sql_logs {
        // the file branch (fopen(log_config.log_feeding, "a"), a strftime'd line): not ported, no filesystem
        return;
    }
    c.db().feedinglog().insert(FeedingLog {
        id: 0,
        time: c.rc.timestamp,
        char_id: sd.char_id as i32,
        target_id: target_id as i32,
        target_class: target_class as i16,
        type_: log_feedingtype2char(type_).to_string(),
        intimacy,
        item_id: nameid,
        map: sd_mapname(c, aid),
        x: sd.x as u16,
        y: sd.y as u16,
    });
}

// ---------------------------------------------------------------------------------------------- tests

#[cfg(test)]
mod tests {
    use super::*;
    use crate::db::items::{IT_ARMOR, IT_CARD, IT_ETC, IT_HEALING, IT_USABLE, IT_WEAPON};

    #[test]
    fn picktype2char_matches_log_cpp() {
        let table: [(LogPickType, char); 31] = [
            (LOG_TYPE_TRADE, 'T'),
            (LOG_TYPE_VENDING, 'V'),
            (LOG_TYPE_PICKDROP_PLAYER, 'P'),
            (LOG_TYPE_PICKDROP_MONSTER, 'M'),
            (LOG_TYPE_NPC, 'S'),
            (LOG_TYPE_SCRIPT, 'N'),
            (LOG_TYPE_STEAL, 'D'),
            (LOG_TYPE_CONSUME, 'C'),
            (LOG_TYPE_PRODUCE, 'O'),
            (LOG_TYPE_MVP, 'U'),
            (LOG_TYPE_COMMAND, 'A'),
            (LOG_TYPE_STORAGE, 'R'),
            (LOG_TYPE_GSTORAGE, 'G'),
            (LOG_TYPE_MAIL, 'E'),
            (LOG_TYPE_AUCTION, 'I'),
            (LOG_TYPE_BUYING_STORE, 'B'),
            (LOG_TYPE_LOOT, 'L'),
            (LOG_TYPE_BANK, 'K'),
            (LOG_TYPE_OTHER, 'X'),
            (LOG_TYPE_CASH, '$'),
            (LOG_TYPE_BOUND_REMOVAL, 'F'),
            (LOG_TYPE_ROULETTE, 'Y'),
            (LOG_TYPE_MERGE_ITEM, 'Z'),
            (LOG_TYPE_QUEST, 'Q'),
            (LOG_TYPE_PRIVATE_AIRSHIP, 'H'),
            (LOG_TYPE_BARTER, 'J'),
            (LOG_TYPE_LAPHINE, 'W'),
            (LOG_TYPE_ENCHANTGRADE, '0'),
            (LOG_TYPE_REFORM, '1'),
            (LOG_TYPE_ENCHANT, '2'),
            (LOG_TYPE_PACKAGE, '3'),
        ];
        for (t, ch) in table {
            assert_eq!(log_picktype2char(t), ch, "type {t:#x}");
        }
        // LOG_TYPE_NONE, LOG_TYPE_ALL and other combinations fall back to 'X'
        assert_eq!(log_picktype2char(LOG_TYPE_NONE), 'X');
        assert_eq!(log_picktype2char(LOG_TYPE_ALL), 'X');
        assert_eq!(log_picktype2char(LOG_TYPE_TRADE | LOG_TYPE_VENDING), 'X');
        assert_eq!(LOG_TYPE_LOOT, 0x88);
    }

    #[test]
    fn chat_cash_feeding_type2char() {
        assert_eq!(log_chattype2char(LOG_CHAT_GLOBAL), 'O');
        assert_eq!(log_chattype2char(LOG_CHAT_WHISPER), 'W');
        assert_eq!(log_chattype2char(LOG_CHAT_PARTY), 'P');
        assert_eq!(log_chattype2char(LOG_CHAT_GUILD), 'G');
        assert_eq!(log_chattype2char(LOG_CHAT_MAINCHAT), 'M');
        assert_eq!(log_chattype2char(LOG_CHAT_CLAN), 'C');
        assert_eq!(log_chattype2char(LOG_CHAT_ALL), 'O');
        assert_eq!(log_cashtype2char(LOG_CASH_TYPE_CASH), 'C');
        assert_eq!(log_cashtype2char(LOG_CASH_TYPE_KAFRA), 'K');
        assert_eq!(log_cashtype2char(3), 'O');
        assert_eq!(log_feedingtype2char(LOG_FEED_HOMUNCULUS), 'H');
        assert_eq!(log_feedingtype2char(LOG_FEED_PET), 'P');
        assert_eq!(log_feedingtype2char(0), 'O');
    }

    #[test]
    fn config_is_log_athena_conf() {
        let d = log_set_defaults();
        assert_eq!((d.refine_items_log, d.rare_items_log, d.price_items_log, d.amount_items_log), (5, 100, 1000, 100));
        assert_eq!((d.enable_logs, d.filter, d.sql_logs), (0, 0, false));
        let c = LOG_CONFIG;
        assert_eq!(c.enable_logs, LOG_TYPE_ALL);
        assert_eq!(c.filter, LOG_FILTER_ALL);
        assert!(c.sql_logs && c.cash && !c.log_chat_woe_disable);
        assert_eq!((c.branch, c.zeny, c.mvpdrop, c.commands, c.npc, c.chat, c.feeding), (0, 1, 0, 1, 1, 0, 3));
    }

    fn facts(type_: u8) -> ItemFacts {
        ItemFacts { nameid: 501, type_, value_buy: 50 }
    }

    fn cfg(filter: i32) -> LogConfig {
        LogConfig { filter, ..LOG_CONFIG }
    }

    #[test]
    fn should_log_item_filter_bits() {
        let never = || -> i32 { panic!("maxchance is only read for LOG_FILTER_CHANCE") };
        // log_filter: 1 logs any item
        assert!(should_log_item_sub(&cfg(LOG_FILTER_ALL), facts(IT_ETC), 1, 0, never));
        // 0: nothing
        assert!(!should_log_item_sub(&cfg(LOG_FILTER_NONE), facts(IT_ETC), 1000, 20, never));
        // type bits
        assert!(should_log_item_sub(&cfg(LOG_FILTER_HEALING), facts(IT_HEALING), 1, 0, never));
        assert!(!should_log_item_sub(&cfg(LOG_FILTER_HEALING), facts(IT_USABLE), 1, 0, never));
        assert!(should_log_item_sub(&cfg(LOG_FILTER_WEAPON), facts(IT_WEAPON), 1, 0, never));
        assert!(!should_log_item_sub(&cfg(LOG_FILTER_WEAPON), facts(IT_ARMOR), 1, 0, never));
        assert!(should_log_item_sub(&cfg(LOG_FILTER_ARMOR), facts(IT_ARMOR), 1, 0, never));
        assert!(should_log_item_sub(&cfg(LOG_FILTER_CARD), facts(IT_CARD), 1, 0, never));
        // IT_DELAYCONSUME (11) is not tested by LOG_FILTER_USABLE, whatever the comment says
        assert!(!should_log_item_sub(&cfg(LOG_FILTER_USABLE), facts(crate::db::items::IT_DELAYCONSUME), 1, 0, never));
        assert!(should_log_item_sub(&cfg(LOG_FILTER_USABLE), facts(crate::db::items::IT_CASH), 1, 0, never));
        // amount: abs(amount) >= amount_items_log (100)
        assert!(should_log_item_sub(&cfg(LOG_FILTER_AMOUNT), facts(IT_ETC), -100, 0, never));
        assert!(!should_log_item_sub(&cfg(LOG_FILTER_AMOUNT), facts(IT_ETC), 99, 0, never));
        // refine >= refine_items_log (5)
        assert!(should_log_item_sub(&cfg(LOG_FILTER_REFINE), facts(IT_WEAPON), 1, 5, never));
        assert!(!should_log_item_sub(&cfg(LOG_FILTER_REFINE), facts(IT_WEAPON), 1, 4, never));
        // price: value_buy >= price_items_log (1000)
        let mut f = facts(IT_ETC);
        f.value_buy = 1000;
        assert!(should_log_item_sub(&cfg(LOG_FILTER_PRICE), f, 1, 0, never));
        f.value_buy = 999;
        assert!(!should_log_item_sub(&cfg(LOG_FILTER_PRICE), f, 1, 0, never));
        // chance: maxchance != -1 && maxchance <= rare_items_log (100), or the Emperium
        assert!(should_log_item_sub(&cfg(LOG_FILTER_CHANCE), facts(IT_ETC), 1, 0, || 100));
        assert!(!should_log_item_sub(&cfg(LOG_FILTER_CHANCE), facts(IT_ETC), 1, 0, || 101));
        assert!(!should_log_item_sub(&cfg(LOG_FILTER_CHANCE), facts(IT_ETC), 1, 0, || -1));
        let mut emp = facts(IT_ETC);
        emp.nameid = ITEMID_EMPERIUM;
        assert!(should_log_item_sub(&cfg(LOG_FILTER_CHANCE), emp, 1, 0, || -1));
        // log_athena.conf's example 322 = healing + cards + price
        assert!(should_log_item_sub(&cfg(322), facts(IT_CARD), 1, 0, never));
        assert!(!should_log_item_sub(&cfg(322), facts(IT_ETC), 1, 0, never));
    }

    #[test]
    fn enable_zeny_chat_feeding_switches() {
        let c = LOG_CONFIG;
        // enable_logs 0xFFFFFFFF: every type; LOG_TYPE_NONE (inventory <-> cart moves) never
        assert!(pick_enabled(&c, LOG_TYPE_ROULETTE) && pick_enabled(&c, LOG_TYPE_LOOT));
        assert!(!pick_enabled(&c, LOG_TYPE_NONE));
        assert!(!pick_enabled(&LogConfig { enable_logs: LOG_TYPE_TRADE | LOG_TYPE_VENDING, ..c }, LOG_TYPE_NPC));
        // log_zeny: 0 off, 1 every change, n: |amount| >= n
        assert!(zeny_enabled(&c, 0) && zeny_enabled(&c, -5));
        let z0 = LogConfig { zeny: 0, ..c };
        assert!(!zeny_enabled(&z0, 1_000_000));
        let z100 = LogConfig { zeny: 100, ..c };
        assert!(zeny_enabled(&z100, -100) && !zeny_enabled(&z100, 99));
        // log_chat: 0 logs nothing (the agit test is not reached)
        assert!(!chat_enabled(&c, LOG_CHAT_GLOBAL, || panic!("not reached")));
        let ch = LogConfig { chat: 5, ..c };
        assert!(chat_enabled(&ch, LOG_CHAT_GLOBAL, || true));
        assert!(chat_enabled(&ch, LOG_CHAT_PARTY, || true));
        assert!(!chat_enabled(&ch, LOG_CHAT_WHISPER, || false));
        let woe = LogConfig { chat: 63, log_chat_woe_disable: true, ..c };
        assert!(!chat_enabled(&woe, LOG_CHAT_GUILD, || true));
        assert!(chat_enabled(&woe, LOG_CHAT_GUILD, || false));
        // log_feeding: 3 both
        assert!(feeding_enabled(&c, LOG_FEED_PET) && feeding_enabled(&c, LOG_FEED_HOMUNCULUS));
        assert!(!feeding_enabled(&LogConfig { feeding: 1, ..c }, LOG_FEED_PET));
    }

    #[test]
    fn pick_row_columns() {
        let t = Timestamp::from_micros_since_unix_epoch(1_700_000_000_000_000);
        let mut it = Item::new(1201);
        it.refine = 7;
        it.card0 = 4001;
        it.card3 = 4003;
        it.unique_id = 99;
        it.bound = 2;
        it.enchantgrade = 3;
        it.option[1] = crate::inventory::ItemOption { id: 5, value: -10, param: 200 };
        let r = pick_row(t, 150001, LOG_TYPE_LOOT, -3, "prontera".into(), &it);
        assert_eq!(r.time, t);
        assert_eq!((r.char_id, r.type_.as_str(), r.nameid, r.amount, r.refine), (150001, "L", 1201, -3, 7));
        assert_eq!((r.card0, r.card1, r.card2, r.card3), (4001, 0, 0, 4003));
        assert_eq!((r.option_id1, r.option_val1, r.option_parm1), (5, -10, -56));
        assert_eq!((r.option_id0, r.option_id4), (0, 0));
        assert_eq!((r.unique_id, r.map.as_str(), r.bound, r.enchantgrade), (99, "prontera", 2, 3));
    }

    #[test]
    fn chat_row_caps() {
        let t = Timestamp::from_micros_since_unix_epoch(0);
        let long: String = std::iter::repeat('a').take(300).collect();
        let r = chat_row(t, LOG_CHAT_WHISPER, 0, 150000, 2000000, "prontera", 150, 180, Some("Target\0junk"), &long);
        assert_eq!(r.type_, "W");
        assert_eq!(r.dst_charname, "Target");
        assert_eq!(r.message.chars().count(), CHAT_SIZE_MAX);
        assert_eq!((r.src_map_x, r.src_map_y), (150, 180));
        let r = chat_row(t, LOG_CHAT_PARTY, 7, 1, 2, "", 0, 0, None, "hi");
        assert_eq!((r.type_.as_str(), r.type_id, r.dst_charname.as_str(), r.message.as_str()), ("P", 7, "", "hi"));
        let name: String = std::iter::repeat('n').take(30).collect();
        assert_eq!(cut(&name, NAME_LENGTH).len(), NAME_LENGTH);
    }
}
