//! map/channel.cpp: chat channels — the conf/channels.conf ones (#global, #support, #trade), players' private
//! channels, map channels (#map) and alliance channels (#ally, with guilds), @join / @channel, whispers to
//! "#channel" and a chat bar bound to a channel (@channel bindto). A channel is a `channel` row; channel_db's key is
//! the lower-cased name (the db is case-insensitive), map / alliance channels (not in channel_db) have their own keys.
//! A player's sd->channels[] / channel_tick[] / gcbind are in `pc_social` (social.rs).

use spacetimedb::{table, Table};

use crate::clif::{self, Target};
use crate::data::{format_msg, msg_txt};
use crate::net::wire::latin1;
use crate::pc;
use crate::social::{self, COLOR_LIGHT_GREEN, COLOR_RED};
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// map.hpp MF_NOMAPCHANNELAUTOJOIN (script_constants: 53).
const MF_NOMAPCHANNELAUTOJOIN: i16 = 53;

/// channel.hpp CHAN_NAME_LENGTH / CHAN_MSG_LENGTH.
pub const CHAN_NAME_LENGTH: usize = 20;

/// channel.hpp Channel_Opt.
pub const CHAN_OPT_NONE: u8 = 0;
pub const CHAN_OPT_ANNOUNCE_SELF: u8 = 0x01;
pub const CHAN_OPT_ANNOUNCE_JOIN: u8 = 0x02;
pub const CHAN_OPT_ANNOUNCE_LEAVE: u8 = 0x04;
pub const CHAN_OPT_MSG_DELAY: u8 = 0x08;
pub const CHAN_OPT_COLOR_OVERRIDE: u8 = 0x10;
pub const CHAN_OPT_CAN_CHAT: u8 = 0x20;
pub const CHAN_OPT_CAN_LEAVE: u8 = 0x40;
pub const CHAN_OPT_AUTOJOIN: u8 = 0x80;
pub const CHAN_OPT_BASE: u8 = CHAN_OPT_ANNOUNCE_SELF | CHAN_OPT_MSG_DELAY | CHAN_OPT_CAN_CHAT | CHAN_OPT_CAN_LEAVE;

/// channel.hpp Channel_Type.
pub const CHAN_TYPE_PUBLIC: u8 = 0;
pub const CHAN_TYPE_PRIVATE: u8 = 1;
pub const CHAN_TYPE_MAP: u8 = 2;
pub const CHAN_TYPE_ALLY: u8 = 3;

/// channel.hpp struct chan_banentry.
#[derive(spacetimedb::SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct ChanBanEntry {
    pub char_id: u32,
    pub char_name: String,
}

/// channel.hpp struct Channel.
#[table(accessor = channel)]
#[derive(Clone, Debug)]
pub struct Channel {
    #[primary_key]
    pub key: String,
    /// Channel name (without '#').
    pub name: String,
    pub pass: String,
    /// Channel display name.
    pub alias: String,
    pub kind: u8,
    /// BGR.
    pub color: u32,
    pub opt: u8,
    pub msg_delay: u16,
    /// CHAN_TYPE_PRIVATE: the creator's char id.
    pub char_id: u32,
    /// CHAN_TYPE_MAP: the map.
    pub m: u16,
    /// CHAN_TYPE_ALLY: the guild.
    pub gid: i32,
    /// users: char ids (an int DBMap: iterated with party::idb_order).
    pub users: Vec<u32>,
    /// banned: chan_banentry (char id, name).
    pub banned: Vec<ChanBanEntry>,
    /// Only these groups can join (empty = any).
    pub groups: Vec<u16>,
}

/// Whether conf/channels.conf has been read (do_init_channel).
#[table(accessor = channel_init)]
pub struct ChannelInit {
    #[primary_key]
    pub key: u8,
    pub done: bool,
}

// ---------------------------------------------------------------------------------------------- conf/channels.conf

/// channel_config.colors (name, RGB as written; stored BGR).
const COLORS: &[(&str, u32)] = &[
    ("Default", 0xffffff),
    ("Red", 0xff0000),
    ("Blue", 0x83cfe9),
    ("Orange", 0xe57c00),
    ("Cyan", 0x00b89d),
    ("Yellow", 0xffff90),
    ("Green", 0x28bf00),
    ("White", 0xFFFFFF),
    ("Purple", 0xD67FFF),
    ("LightGreen", 0xB6FF00),
    ("Normal", 0x00ff00),
];

fn bgr(rgb: u32) -> u32 {
    (rgb & 0x0000FF) << 16 | (rgb & 0x00FF00) | (rgb & 0xFF0000) >> 16
}

/// channel_config.colors[k] (BGR).
fn color_of(k: usize) -> u32 {
    COLORS.get(k).map(|c| bgr(c.1)).unwrap_or(0)
}

/// channel.cpp channel_getColor: a colour by name (case-insensitive), else the first one.
pub fn channel_get_color(name: &str) -> u32 {
    COLORS.iter().find(|c| c.0.eq_ignore_ascii_case(name)).map(|c| bgr(c.1)).unwrap_or_else(|| color_of(0))
}

/// channel_config.private_channel (conf/channels.conf private_channel: allow true, color Default, delay 1000,
/// max_member 1000, self_notif true, join_notif false, leave_notif false, ban true, kick true, color_override false,
/// change_delay false).
pub struct PrivateChannelConfig {
    pub allow: bool,
    pub delay: u16,
    pub max_member: usize,
    pub ban: bool,
    pub kick: bool,
    pub color_override: bool,
    pub change_delay: bool,
    pub opt: u8,
}

pub const PRIVATE_CHANNEL: PrivateChannelConfig = PrivateChannelConfig {
    allow: true,
    delay: 1000,
    max_member: 1000,
    ban: true,
    kick: true,
    color_override: false,
    change_delay: false,
    // CAN_CHAT | CAN_LEAVE | (color_override) | (self_notif ? ANNOUNCE_SELF) | (join_notif) | (leave_notif)
    opt: CHAN_OPT_CAN_CHAT | CHAN_OPT_CAN_LEAVE | CHAN_OPT_ANNOUNCE_SELF,
};

/// One channel entry of conf/channels.conf as channel_read_sub reads it.
struct ChanConf {
    name: &'static str,
    alias: &'static str,
    color: &'static str,
    kind: u8,
    delay: u16,
    autojoin: bool,
    leave: bool,
}

impl ChanConf {
    /// channel_read_sub's opt: (autojoin) | (leave) | (chat, default 1) | (color_override 0) | (self_notif, default 1)
    /// | (join_notif 0) | (leave_notif 0).
    fn opt(&self) -> u8 {
        (if self.autojoin { CHAN_OPT_AUTOJOIN } else { 0 }) | (if self.leave { CHAN_OPT_CAN_LEAVE } else { 0 }) | CHAN_OPT_CAN_CHAT | CHAN_OPT_ANNOUNCE_SELF
    }
    fn template(&self) -> Channel {
        Channel {
            key: String::new(),
            name: self.name[1..].to_string(),
            pass: String::new(),
            alias: self.alias.to_string(),
            kind: self.kind,
            color: channel_get_color(self.color),
            opt: self.opt(),
            msg_delay: self.delay,
            char_id: 0,
            m: 0,
            gid: 0,
            users: Vec::new(),
            banned: Vec::new(),
            groups: Vec::new(),
        }
    }
}

/// conf/channels.conf `channels`.
const CHANNELS: &[ChanConf] = &[
    ChanConf { name: "#global", alias: "[Global]", color: "White", kind: CHAN_TYPE_PUBLIC, delay: 1000, autojoin: false, leave: false },
    ChanConf { name: "#support", alias: "[Support]", color: "Blue", kind: CHAN_TYPE_PUBLIC, delay: 1000, autojoin: false, leave: true },
    ChanConf { name: "#trade", alias: "[Trade]", color: "LightGreen", kind: CHAN_TYPE_PUBLIC, delay: 1000, autojoin: false, leave: true },
];
/// conf/channels.conf `ally` and `map`.
const ALLY_TMPL: ChanConf = ChanConf { name: "#ally", alias: "[Ally]", color: "Green", kind: CHAN_TYPE_ALLY, delay: 1000, autojoin: false, leave: true };
const MAP_TMPL: ChanConf = ChanConf { name: "#map", alias: "[Map]", color: "Yellow", kind: CHAN_TYPE_MAP, delay: 1000, autojoin: false, leave: true };

pub(crate) fn map_tmpl_name() -> &'static str {
    &MAP_TMPL.name[1..]
}
fn ally_tmpl_name() -> &'static str {
    &ALLY_TMPL.name[1..]
}

/// do_init_channel / channel_read_config: the conf channels exist from the start.
pub fn ensure_init(c: &Ctx) {
    if c.db().channel_init().key().find(0).map(|r| r.done).unwrap_or(false) {
        return;
    }
    for cc in CHANNELS {
        if get(c, &db_key(&cc.name[1..])).is_none() {
            channel_create(c, cc.template());
        }
    }
    if c.db().channel_init().key().find(0).is_some() {
        c.db().channel_init().key().update(ChannelInit { key: 0, done: true });
    } else {
        c.db().channel_init().insert(ChannelInit { key: 0, done: true });
    }
}

/// A world rebuild is the module's restart with new files: rAthena's channel_db lives in memory and is built again
/// from conf/channels.conf at every start (do_final_channel, do_init_channel: channel.cpp:1493-1526), and the map /
/// ally channels live in the map and guild data, so no private channel, ban or user list survives a restart. Every
/// channel row goes (its users taken out, channel_delete with force), then the conf channels are created again.
pub fn reset_for_world_build(c: &Ctx) {
    let keys: Vec<String> = c.db().channel().iter().map(|ch| ch.key).collect();
    for key in keys {
        channel_delete(c, &key, true);
    }
    if c.db().channel_init().key().find(0).is_some() {
        c.db().channel_init().key().update(ChannelInit { key: 0, done: false });
    }
    ensure_init(c);
}

// ---------------------------------------------------------------------------------------------- storage

/// channel_db key (stridb: case-insensitive, CHAN_NAME_LENGTH).
fn db_key(name: &str) -> String {
    name.chars().take(CHAN_NAME_LENGTH).collect::<String>().to_ascii_lowercase()
}

pub(crate) fn map_key(m: u16) -> String {
    format!("\u{1}map:{m}")
}

/// The key of guild `gid`'s #ally channel (MapGuild::channel).
pub fn ally_key(gid: i32) -> String {
    format!("\u{1}ally:{gid}")
}

pub fn get(c: &Ctx, key: &str) -> Option<Channel> {
    c.db().channel().key().find(key.to_string())
}

fn put(c: &Ctx, ch: &Channel) {
    c.db().channel().key().update(ch.clone());
}

/// strdb_get(channel_db, name).
fn db_get(c: &Ctx, name: &str) -> Option<Channel> {
    get(c, &db_key(name)).filter(|ch| ch.kind == CHAN_TYPE_PUBLIC || ch.kind == CHAN_TYPE_PRIVATE)
}

/// The channel_db entries (public and private) in DBIterator order: db_istring_hash bucket, then key.
fn db_iter(c: &Ctx) -> Vec<Channel> {
    let mut v: Vec<Channel> = c.db().channel().iter().filter(|ch| ch.kind == CHAN_TYPE_PUBLIC || ch.kind == CHAN_TYPE_PRIVATE).collect();
    v.sort_by(|a, b| (istring_bucket(&a.name), a.name.to_ascii_lowercase()).cmp(&(istring_bucket(&b.name), b.name.to_ascii_lowercase())));
    v
}

/// common/db.cpp db_istring_hash % HASH_SIZE (maxlen CHAN_NAME_LENGTH).
fn istring_bucket(name: &str) -> usize {
    let mut hash: u32 = 0;
    for (i, b) in latin1(name).into_iter().enumerate() {
        if b == 0 {
            break;
        }
        hash = (hash.wrapping_mul(33).wrapping_add(b.to_ascii_lowercase() as u32)) ^ (hash >> 24);
        if i == CHAN_NAME_LENGTH {
            break;
        }
    }
    hash as usize % 283
}

/// The users of a channel in its DBMap's iteration order.
fn users_in_order(ch: &Channel) -> Vec<u32> {
    let mut v = ch.users.clone();
    v.sort_by_key(|&cid| crate::party::idb_order(cid));
    v
}

fn char_id_of(c: &Ctx, aid: u32) -> u32 {
    pc::get(c, aid).map(|p| p.char_id).unwrap_or(0)
}

fn say(c: &Ctx, sd: &Unit, text: &str) {
    clif::displaymessage(c, sd, text);
}

/// clif_messagecolor(sd, color, msg, false, SELF).
fn say_color(c: &Ctx, sd: &Unit, color: u32, text: &str) {
    clif::messagecolor_target(c, sd, color, &latin1(text), false, Target::SelfOnly, None);
}

/// sprintf into a CHAT_SIZE_MAX buffer.
fn fmt(f: &str, args: &[&dyn std::fmt::Display]) -> String {
    let s = format_msg(f, args);
    let b: Vec<u8> = latin1(&s).into_iter().take(crate::net::CHAT_SIZE_MAX - 1).collect();
    crate::net::wire::from_latin1(&b)
}

// ---------------------------------------------------------------------------------------------- channel.cpp

/// channel.cpp channel_create: the channel (public / private ones go into channel_db). Returns its key.
pub(crate) fn channel_create(c: &Ctx, tmp: Channel) -> Option<String> {
    if tmp.name.is_empty() {
        return None;
    }
    let mut ch = tmp;
    ch.name = ch.name.chars().take(CHAN_NAME_LENGTH - 1).collect(); // Store channel cname without '#'
    if ch.alias.is_empty() {
        ch.alias = ch.name.clone();
    }
    ch.alias = ch.alias.chars().take(CHAN_NAME_LENGTH - 1).collect();
    ch.pass = ch.pass.chars().take(CHAN_NAME_LENGTH - 1).collect();
    ch.users.clear();
    ch.banned.clear();
    ch.key = match ch.kind {
        CHAN_TYPE_MAP => map_key(ch.m),
        CHAN_TYPE_ALLY => ally_key(ch.gid),
        _ => db_key(&ch.name),
    };
    if c.db().channel().key().find(ch.key.clone()).is_some() {
        c.db().channel().key().update(ch.clone());
    } else {
        c.db().channel().insert(ch.clone());
    }
    Some(ch.key)
}

/// channel.cpp channel_create_simple.
fn channel_create_simple(c: &Ctx, name: Option<&str>, pass: Option<&str>, chantype: u8, owner: u32) -> Option<String> {
    let tmp = match chantype {
        CHAN_TYPE_ALLY => {
            let mut t = ALLY_TMPL.template();
            t.gid = owner as i32;
            t
        }
        CHAN_TYPE_MAP => {
            let mut t = MAP_TMPL.template();
            t.m = owner as u16;
            t
        }
        CHAN_TYPE_PUBLIC | CHAN_TYPE_PRIVATE => {
            let name = name.unwrap_or("#");
            let mut t = Channel {
                key: String::new(),
                name: name.get(1..).unwrap_or("").chars().take(CHAN_NAME_LENGTH - 1).collect(),
                pass: pass.unwrap_or("").to_string(),
                alias: name.chars().take(CHAN_NAME_LENGTH - 1).collect(),
                kind: chantype,
                color: channel_get_color("Default"),
                opt: CHAN_OPT_BASE,
                msg_delay: 1000,
                char_id: 0,
                m: 0,
                gid: 0,
                users: Vec::new(),
                banned: Vec::new(),
                groups: Vec::new(),
            };
            if chantype == CHAN_TYPE_PRIVATE {
                t.opt = PRIVATE_CHANNEL.opt;
                t.msg_delay = PRIVATE_CHANNEL.delay;
                t.color = channel_get_color("Default"); // private_channel.color: "Default"
                t.char_id = owner;
            }
            t
        }
        _ => return None,
    };
    channel_create(c, tmp)
}

/// channel.cpp channel_delete: 0 deleted, -1 invalid, -2 a conf channel while the server runs.
pub fn channel_delete(c: &Ctx, key: &str, force: bool) -> i32 {
    let Some(ch) = get(c, key) else { return -1 };
    // only delete those serv stop
    if !force && ch.kind == CHAN_TYPE_PUBLIC {
        return -2;
    }
    for cid in users_in_order(&ch) {
        if let Some(sd) = pc::charid2sd(c, cid) {
            channel_clean(c, key, sd.id, 1); // make all quit
        }
    }
    c.db().channel().key().delete(key.to_string());
    0
}

/// channel.cpp channel_join: 0 joined, -1 invalid, -2 already in / group not allowed, -3 banned, -4 full.
pub fn channel_join(c: &Ctx, key: &str, aid: u32) -> i32 {
    let Some(mut ch) = get(c, key) else { return -1 };
    let Some(sd) = unit::get(c, aid) else { return -1 };
    if crate::autotrade::autotrade(c, aid) != 0 {
        return 0; // fake success
    }
    let cid = char_id_of(c, aid);
    if ch.users.contains(&cid) {
        return -2;
    }
    let group = pc::get(c, aid).map(|p| p.group_id).unwrap_or(0);
    if !pc::has_permission(c, aid, crate::rules::PC_PERM_CHANNEL_ADMIN) && !channel_pccheckgroup(&ch, group) {
        return -2;
    }
    if ch.banned.iter().any(|b| b.char_id == cid) {
        say(c, &sd, &fmt(msg_txt(1438), &[&ch.name])); // You're currently banned from the '%s' channel.
        return -3;
    }
    if ch.kind == CHAN_TYPE_PRIVATE && ch.users.len() >= PRIVATE_CHANNEL.max_member {
        say(c, &sd, &fmt(msg_txt(760), &[&ch.name, &PRIVATE_CHANNEL.max_member])); // You cannot join channel '%s'. Limit of %d has been met.
        return -4;
    }
    let mut x = social::sx(c, aid);
    x.channels.push(key.to_string());
    x.channel_tick.push(0);
    ch.users.push(cid);
    put(c, &ch);
    let count = x.channels.len();
    if x.stealth {
        x.stealth = false;
        social::put_sx(c, &x);
    } else {
        social::put_sx(c, &x);
        if ch.opt & CHAN_OPT_ANNOUNCE_JOIN != 0 {
            let name = pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
            let out = fmt(msg_txt(761), &[&ch.alias, &name]); // %s %s has joined.
            clif_channel_msg(c, &ch, &out, ch.color);
        }
    }
    // someone is cheating, we kindly disconnect the bastard
    if count > 200 {
        crate::session::set_eof(c, aid);
    }
    0
}

/// channel.cpp channel_mjoin: joins the map channel (creating it).
pub fn channel_mjoin(c: &Ctx, aid: u32) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    let key = map_key(sd.map);
    if get(c, &key).is_none() {
        channel_create_simple(c, None, None, CHAN_TYPE_MAP, sd.map as u32);
    }
    let Some(ch) = get(c, &key) else { return -1 };
    if ch.opt & CHAN_OPT_ANNOUNCE_SELF != 0 {
        let mapname = crate::data::maps().get(sd.map).map(|d| d.name.clone()).unwrap_or_default();
        let mout = format_msg(msg_txt(1435), &[&ch.name, &mapname]); // You're now in the '#%s' channel for '%s'.
        say_color(c, &sd, COLOR_LIGHT_GREEN, &mout);
    }
    channel_join(c, &key, aid)
}

/// channel.cpp channel_ajoin: the members of the guild's allies who are in their own guild channel join this guild's
/// channel. -1 when the guild has no channel.
pub fn channel_ajoin(c: &Ctx, gid: i32) -> i32 {
    let Some(g) = crate::guild::guild_search(c, gid) else { return -1 };
    let key = ally_key(gid);
    if get(c, &key).is_none() {
        return -1;
    }
    for ga in g.guild.alliance.iter() {
        // allied guild
        if ga.guild_id == 0 || ga.opposition != 0 {
            continue;
        }
        let Some(ag) = crate::guild::guild_search(c, ga.guild_id) else { continue };
        let akey = ally_key(ag.guild.guild_id);
        for j in 0..ag.guild.max() {
            // load all guildmember
            let pl = ag.guild.member[j].sd;
            if pl != 0 && channel_haspc(c, &akey, pl) == 1 {
                // only if they are in their own guildchan
                channel_join(c, &key, pl);
            }
        }
    }
    0
}

/// channel.cpp channel_gjoin: joins the guild channel (flag 1) and the allies' channels (2), creating the guild's
/// channel. -1 invalid player, -2 no guild attached (sd->guild).
pub fn channel_gjoin(c: &Ctx, aid: u32, flag: i32) -> i32 {
    if pc::get(c, aid).is_none() || crate::autotrade::autotrade(c, aid) != 0 {
        return -1;
    }
    let Some(g) = crate::guild::sd_guild(c, aid) else { return -2 };
    let gid = g.guild.guild_id;
    let key = ally_key(gid);
    if get(c, &key).is_none() {
        channel_create_simple(c, None, None, CHAN_TYPE_ALLY, gid as u32);
        channel_ajoin(c, gid);
    }
    if flag & 1 != 0 {
        channel_join(c, &key, aid); // join our guild chat
    }
    if flag & 2 != 0 {
        for ga in g.guild.alliance.iter() {
            // only join allies
            if ga.guild_id != 0 && ga.opposition == 0 && crate::guild::guild_search(c, ga.guild_id).is_some() {
                channel_join(c, &ally_key(ga.guild_id), aid);
            }
        }
    }
    0
}

/// channel.cpp channel_clean: the player leaves the channel; an emptied channel is deleted (unless flag & 1).
pub fn channel_clean(c: &Ctx, key: &str, aid: u32, flag: i32) -> i32 {
    let Some(mut ch) = get(c, key) else { return -1 };
    let mut x = social::sx(c, aid);
    if x.gcbind == key {
        x.gcbind = String::new();
    }
    if let Some(i) = x.channels.iter().position(|k| k == key) {
        // slice move list down
        x.channels.remove(i);
        x.channel_tick.remove(i);
    }
    social::put_sx(c, &x);
    let cid = char_id_of(c, aid);
    ch.users.retain(|&u| u != cid); // remove user for channel user list
    put(c, &ch);
    // auto delete when no more user in
    if ch.users.is_empty() && flag & 1 == 0 {
        channel_delete(c, key, false);
    }
    0
}

/// channel.cpp channel_pcquit: type 1 guild, 2 ally, 4 map, 8 all.
pub fn channel_pcquit(c: &Ctx, aid: u32, kind: i32) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    // Leave all chat channels.
    if kind & (1 | 2) != 0 && !ally_tmpl_name().is_empty() {
        if let Some(g) = crate::guild::sd_guild(c, aid) {
            // quit guild and ally chan
            let key = ally_key(g.guild.guild_id);
            if kind & 1 != 0 && channel_haspc(c, &key, aid) == 1 {
                channel_clean(c, &key, aid, 0); // leave guild chan
            }
            if kind & 2 != 0 {
                for a in g.guild.alliance.iter() {
                    // leave all alliance chan (the first allied guild found only: rAthena breaks out of the loop)
                    if a.guild_id != 0 && crate::guild::guild_search(c, a.guild_id).is_some() {
                        let akey = ally_key(a.guild_id);
                        if channel_haspc(c, &akey, aid) == 1 {
                            channel_clean(c, &akey, aid, 0);
                        }
                        break;
                    }
                }
            }
        }
    }
    if kind & 4 != 0 && !map_tmpl_name().is_empty() {
        let key = map_key(sd.map);
        if channel_haspc(c, &key, aid) == 1 {
            channel_clean(c, &key, aid, 0); // quit map chan
        }
    }
    if kind & 8 != 0 {
        // quit all chan, going backward to avoid shifting
        let chans = social::sx(c, aid).channels;
        for key in chans.iter().rev() {
            channel_clean(c, key, aid, 0);
        }
    }
    0
}

/// channel.cpp channel_send: 0 sent, -1 invalid / not in the channel, -2 too fast.
pub fn channel_send(c: &Ctx, key: &str, aid: u32, msg: &str) -> i32 {
    let Some(ch) = get(c, key) else { return -1 };
    let Some(sd) = unit::get(c, aid) else { return -1 };
    let idx = channel_pc_haschan(c, aid, &ch);
    if idx < 0 {
        return -1;
    }
    let idx = idx as usize;
    let mut x = social::sx(c, aid);
    let tick0 = x.channel_tick.get(idx).copied().unwrap_or(0);
    if !pc::has_permission(c, aid, crate::rules::PC_PERM_CHANNEL_ADMIN) && ch.msg_delay != 0 && tick0 + ch.msg_delay as i64 - c.tick() > 0 {
        say_color(c, &sd, COLOR_RED, msg_txt(1455)); // You're talking too fast!
        return -2;
    }
    let mut color = ch.color;
    if ch.opt & CHAN_OPT_COLOR_OVERRIDE != 0 && x.fontcolor != 0 && (x.fontcolor as usize) < COLORS.len() && color_of(x.fontcolor as usize) != 0 {
        color = color_of(x.fontcolor as usize);
    }
    let name = pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
    let output: Vec<u8> = latin1(&format!("{} {} : {}", ch.alias, name, msg)).into_iter().take(crate::net::CHAT_SIZE_MAX - 1).collect();
    clif_channel_msg_bytes(c, &ch, &output, color);
    if let Some(t) = x.channel_tick.get_mut(idx) {
        *t = c.tick();
    }
    social::put_sx(c, &x);
    0
}

/// channel.cpp channel_chk: type 1 name ('#' + length), 2 name not taken, 4 password length. 0 ok, -1 no '#',
/// -2 bad length, -3 password too long, -4 taken.
pub fn channel_chk(c: &Ctx, chname: &str, chpass: &str, kind: i32) -> i32 {
    let nb = latin1(chname);
    if kind & 1 != 0 {
        // check name
        if nb.first() != Some(&b'#') {
            return -1;
        }
        if nb.len() < 3 || nb.len() > CHAN_NAME_LENGTH {
            return -2;
        }
        let rest = &chname[1..];
        if kind & 2 != 0 && (rest.eq_ignore_ascii_case(map_tmpl_name()) || rest.eq_ignore_ascii_case(ally_tmpl_name()) || db_get(c, rest).is_some()) {
            return -4;
        }
    }
    if kind & 4 != 0 && !chpass.is_empty() && latin1(chpass).len() > CHAN_NAME_LENGTH {
        return -3;
    }
    0
}

/// channel.cpp channel_name2channel: flag 1 creates a map / ally channel, 2 joins it. Returns the channel's key.
pub fn channel_name2channel(c: &Ctx, chname: &str, sd: Option<u32>, flag: i32) -> Option<String> {
    if channel_chk(c, chname, "", 1) != 0 {
        return None;
    }
    let rest = &chname[1..];
    if let Some(aid) = sd {
        if rest.eq_ignore_ascii_case(map_tmpl_name()) {
            let su = unit::get(c, aid)?;
            let key = map_key(su.map);
            if flag & 1 != 0 && get(c, &key).is_none() {
                channel_create_simple(c, None, None, CHAN_TYPE_MAP, su.map as u32);
            }
            if flag & 2 != 0 {
                let has = match get(c, &key) {
                    Some(ch) => channel_pc_haschan(c, aid, &ch),
                    None => -1,
                };
                if has < 1 {
                    channel_mjoin(c, aid);
                }
            }
            return get(c, &key).map(|_| key);
        }
        if rest.eq_ignore_ascii_case(ally_tmpl_name()) {
            if let Some(g) = crate::guild::sd_guild(c, aid) {
                let gid = g.guild.guild_id;
                let key = ally_key(gid);
                if flag & 1 != 0 && get(c, &key).is_none() {
                    channel_create_simple(c, None, None, CHAN_TYPE_ALLY, gid as u32);
                }
                if flag & 2 != 0 {
                    // channel_pc_haschan(sd, mapdata->channel) < 1: rAthena tests the MAP channel here
                    let su = unit::get(c, aid)?;
                    let has = match get(c, &map_key(su.map)) {
                        Some(ch) => channel_pc_haschan(c, aid, &ch),
                        None => -1,
                    };
                    if has < 1 {
                        channel_gjoin(c, aid, 3);
                    }
                }
                return get(c, &key).map(|_| key);
            }
        }
    }
    db_get(c, rest).map(|ch| ch.key)
}

/// channel.cpp channel_haspc: 1 in the channel, 0 not, -1 invalid.
pub fn channel_haspc(c: &Ctx, key: &str, aid: u32) -> i32 {
    let Some(ch) = get(c, key) else { return -1 };
    if pc::get(c, aid).is_none() {
        return -1;
    }
    ch.users.contains(&char_id_of(c, aid)) as i32
}

/// channel.cpp channel_pc_haschan: the index in sd->channels of a channel with this name, -2 none.
pub fn channel_pc_haschan(c: &Ctx, aid: u32, ch: &Channel) -> i32 {
    let x = social::sx(c, aid);
    for (k, key) in x.channels.iter().enumerate() {
        if let Some(o) = get(c, key) {
            if o.name.eq_ignore_ascii_case(&ch.name) {
                return k as i32;
            }
        }
    }
    -2
}

/// channel.cpp channel_pccheckgroup.
pub fn channel_pccheckgroup(ch: &Channel, group_id: u8) -> bool {
    ch.groups.is_empty() || ch.groups.contains(&(group_id as u16))
}

/// channel.cpp channel_display_list: "colors", "mine", or the public channels.
pub fn channel_display_list(c: &Ctx, aid: u32, options: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    if !options.is_empty() && options.eq_ignore_ascii_case("colors") {
        // display availaible colors
        say(c, &sd, msg_txt(1444)); // ---- Available Colors ----
        for (k, (name, _)) in COLORS.iter().enumerate() {
            if color_of(k) != 0 {
                let m = format_msg(msg_txt(1445), &[name]); // - '%s'
                say_color(c, &sd, color_of(k), &m);
            }
        }
    } else if !options.is_empty() && options.eq_ignore_ascii_case("mine") {
        // display chan I'm into
        say(c, &sd, msg_txt(1475)); // ---- My Channels ----
        let x = social::sx(c, aid);
        if x.channels.is_empty() {
            say(c, &sd, msg_txt(1476)); // You have not joined any channels.
        } else {
            for key in x.channels.iter() {
                if let Some(ch) = get(c, key) {
                    say(c, &sd, &fmt(msg_txt(1409), &[&ch.name, &ch.users.len()])); // - #%s (%d users)
                }
            }
        }
    } else {
        // display public chanels
        let has_perm = pc::has_permission(c, aid, crate::rules::PC_PERM_CHANNEL_ADMIN);
        say(c, &sd, msg_txt(1410)); // ---- Public Channels ----
        if !map_tmpl_name().is_empty() {
            if let Some(ch) = get(c, &map_key(sd.map)) {
                say(c, &sd, &fmt(msg_txt(1409), &[&ch.name, &ch.users.len()]));
            }
        }
        if !ally_tmpl_name().is_empty() && crate::guild::guild_id_of(c, aid) != 0 {
            if let Some(g) = crate::guild::sd_guild(c, aid) {
                if let Some(ch) = get(c, &ally_key(g.guild.guild_id)) {
                    say(c, &sd, &fmt(msg_txt(1409), &[&ch.name, &ch.users.len()])); // - #%s (%d users)
                }
            }
        }
        let group = pc::get(c, aid).map(|p| p.group_id).unwrap_or(0);
        for ch in db_iter(c) {
            if !has_perm && !channel_pccheckgroup(&ch, group) {
                continue;
            }
            if has_perm || ch.kind == CHAN_TYPE_PUBLIC {
                say(c, &sd, &fmt(msg_txt(1409), &[&ch.name, &ch.users.len()]));
            }
        }
    }
    0
}

/// channel.cpp channel_pccreate (@channel create).
pub fn channel_pccreate(c: &Ctx, aid: u32, chname: &str, chpass: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    let res = channel_chk(c, chname, chpass, 7);
    if res == 0 {
        // success
        let Some(key) = channel_create_simple(c, Some(chname), Some(chpass), CHAN_TYPE_PRIVATE, char_id_of(c, aid)) else { return -1 };
        channel_join(c, &key, aid);
        if get(c, &key).map(|ch| ch.opt & CHAN_OPT_ANNOUNCE_SELF != 0).unwrap_or(false) {
            say(c, &sd, &fmt(msg_txt(1403), &[&chname])); // You're now in the '%s' channel.
        }
    } else {
        // failure display cause
        let out = match res {
            -1 => fmt(msg_txt(1405), &[&CHAN_NAME_LENGTH]), // Channel name must start with '#'.
            -2 => fmt(msg_txt(1406), &[&CHAN_NAME_LENGTH]), // Channel length must be between 3 and %d.
            -3 => fmt(msg_txt(1436), &[&CHAN_NAME_LENGTH]), // Channel password can't be over %d characters.
            _ => fmt(msg_txt(1407), &[&chname]),            // Channel '%s' is not available.
        };
        say(c, &sd, &out);
        return -1;
    }
    0
}

/// channel.cpp channel_pcdelete (@channel delete).
pub fn channel_pcdelete(c: &Ctx, aid: u32, chname: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    if channel_chk(c, chname, "", 1) != 0 {
        say(c, &sd, msg_txt(1405)); // Channel name must start with '#'.
        return -1;
    }
    let key = channel_name2channel(c, chname, Some(aid), 0);
    let has = key.as_ref().and_then(|k| get(c, k)).map(|ch| channel_pc_haschan(c, aid, &ch)).unwrap_or(-1);
    if has < 0 {
        say(c, &sd, &fmt(msg_txt(1425), &[&chname])); // You're not part of the '%s' channel.
        return -2; // channel doesn't exist or player don't have it
    }
    if let Some(k) = key {
        channel_delete(c, &k, false);
    }
    say(c, &sd, &fmt(msg_txt(1448), &[&chname])); // Channel '%s' deleted.
    0
}

/// channel.cpp channel_pcleave (@channel leave).
pub fn channel_pcleave(c: &Ctx, aid: u32, chname: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    if channel_chk(c, chname, "", 1) != 0 {
        say(c, &sd, msg_txt(1405)); // Channel name must start with '#'.
        return -1;
    }
    let key = channel_name2channel(c, chname, Some(aid), 0);
    let ch = key.as_ref().and_then(|k| get(c, k));
    let Some(ch) = ch.filter(|ch| channel_pc_haschan(c, aid, ch) >= 0) else {
        say(c, &sd, &fmt(msg_txt(1425), &[&chname])); // You're not part of the '%s' channel.
        return -2;
    };
    if ch.opt & CHAN_OPT_CAN_LEAVE == 0 {
        say(c, &sd, &fmt(msg_txt(762), &[&chname])); // You cannot leave channel '%s'.
        return -1;
    }
    if ch.opt & CHAN_OPT_ANNOUNCE_LEAVE != 0 {
        let name = pc::character_of(c, aid).map(|c| c.name).unwrap_or_default();
        let out = fmt(msg_txt(763), &[&ch.alias, &name]); // %s %s left.
        clif_channel_msg(c, &ch, &out, ch.color);
    }
    match ch.kind {
        CHAN_TYPE_ALLY => {
            channel_pcquit(c, aid, 3);
        }
        CHAN_TYPE_MAP => {
            channel_pcquit(c, aid, 4);
        }
        _ => {
            channel_clean(c, &ch.key, aid, 0);
        }
    }
    say(c, &sd, &fmt(msg_txt(1426), &[&chname])); // You've left the '%s' channel.
    0
}

/// channel.cpp channel_pcjoin (@join, @channel join, autojoin).
pub fn channel_pcjoin(c: &Ctx, aid: u32, chname: &str, pass: Option<&str>) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    if channel_chk(c, chname, "", 1) != 0 {
        say(c, &sd, msg_txt(1405)); // Channel name must start with '#'.
        return -1;
    }
    let Some(key) = channel_name2channel(c, chname, Some(aid), 1) else {
        say(c, &sd, &fmt(msg_txt(1400), &[&chname, &"@join"])); // Unknown channel '%s' (usage: %s <#channel_name>).
        return -1;
    };
    let Some(ch) = get(c, &key) else { return -1 };
    let group = pc::get(c, aid).map(|p| p.group_id).unwrap_or(0);
    let admin = pc::has_permission(c, aid, crate::rules::PC_PERM_CHANNEL_ADMIN);
    if !admin && !channel_pccheckgroup(&ch, group) {
        say(c, &sd, &fmt(msg_txt(1407), &[&chname])); // Channel '%s' is not available.
        return -1;
    }
    if channel_haspc(c, &key, aid) == 1 {
        say(c, &sd, &fmt(msg_txt(1434), &[&chname])); // You're already in the '%s' channel.
        return -1;
    } else if !ch.pass.is_empty() {
        // chan has a pass
        if pass.unwrap_or("") != ch.pass {
            // wrong pass entry
            if admin {
                let mut x = social::sx(c, aid);
                x.stealth = true;
                social::put_sx(c, &x);
            } else {
                say(c, &sd, &fmt(msg_txt(1401), &[&chname, &"@join"])); // Channel '%s' is password-protected (usage: %s <#channel_name> <password>).
                return -1;
            }
        }
    }
    match ch.kind {
        CHAN_TYPE_ALLY => {
            channel_gjoin(c, aid, 3);
        }
        CHAN_TYPE_MAP => {
            channel_mjoin(c, aid);
        }
        _ => {
            // private and public atm
            if channel_join(c, &key, aid) != 0 {
                return -1;
            }
        }
    }
    if ch.opt & CHAN_OPT_ANNOUNCE_SELF != 0 {
        say(c, &sd, &fmt(msg_txt(1403), &[&chname])); // You're now in the '%s' channel.
    }
    0
}

fn not_owner(c: &Ctx, aid: u32, ch: &Channel) -> bool {
    ch.char_id != char_id_of(c, aid)
}

/// channel.cpp channel_pccolor (@channel setcolor).
pub fn channel_pccolor(c: &Ctx, aid: u32, chname: &str, color: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    if channel_chk(c, chname, "", 1) != 0 {
        say(c, &sd, msg_txt(1405));
        return -1;
    }
    let Some(mut ch) = channel_name2channel(c, chname, Some(aid), 0).and_then(|k| get(c, &k)) else {
        say(c, &sd, &fmt(msg_txt(1407), &[&chname])); // Channel '%s' is not available.
        return -1;
    };
    if !pc::has_permission(c, aid, crate::rules::PC_PERM_CHANNEL_ADMIN) {
        if not_owner(c, aid, &ch) {
            say(c, &sd, &fmt(msg_txt(1412), &[&chname])); // You're not the owner of channel '%s'.
            return -1;
        } else if ch.opt & CHAN_OPT_COLOR_OVERRIDE == 0 {
            say(c, &sd, &fmt(msg_txt(764), &[&chname])); // You cannot change the color for channel '%s'.
            return -1;
        }
    }
    let Some(k) = COLORS.iter().position(|c| c.0.eq_ignore_ascii_case(color)) else {
        say(c, &sd, &fmt(msg_txt(1411), &[&color])); // Unknown color '%s'.
        return -1;
    };
    ch.color = color_of(k);
    put(c, &ch);
    say(c, &sd, &fmt(msg_txt(1413), &[&chname, &COLORS[k].0])); // '%s' channel color updated to '%s'.
    0
}

/// channel.cpp channel_pcbind (@channel bindto).
pub fn channel_pcbind(c: &Ctx, aid: u32, chname: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    if channel_chk(c, chname, "", 1) != 0 {
        say(c, &sd, msg_txt(1405));
        return -1;
    }
    let key = channel_name2channel(c, chname, Some(aid), 0);
    let ok = key.as_ref().and_then(|k| get(c, k)).map(|ch| channel_pc_haschan(c, aid, &ch) >= 0).unwrap_or(false);
    if !ok {
        say(c, &sd, &fmt(msg_txt(1425), &[&chname])); // You're not part of the '%s' channel.
        return -2;
    }
    let mut x = social::sx(c, aid);
    x.gcbind = key.unwrap_or_default();
    social::put_sx(c, &x);
    say(c, &sd, &fmt(msg_txt(1431), &[&chname])); // Your global chat is now binded to the '%s' channel.
    0
}

/// channel.cpp channel_pcunbind (@channel unbind).
pub fn channel_pcunbind(c: &Ctx, aid: u32) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    let mut x = social::sx(c, aid);
    let Some(ch) = get(c, &x.gcbind).filter(|_| !x.gcbind.is_empty()) else {
        say(c, &sd, msg_txt(1432)); // Your global chat is not binded to any channel.
        return -1;
    };
    say(c, &sd, &fmt(msg_txt(1433), &[&ch.name])); // Your global chat is now unbinded from the '#%s' channel.
    x.gcbind = String::new();
    social::put_sx(c, &x);
    0
}

/// channel.cpp channel_pcban: flag 0 ban, 1 unban, 2 unban all, 3 ban list.
pub fn channel_pcban(c: &Ctx, aid: u32, chname: &str, pname: &str, flag: i32) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    let tsd = pc::nick2sd(c, pname, false);
    if channel_chk(c, chname, "", 1) != 0 {
        say(c, &sd, msg_txt(1405));
        return -1;
    }
    let Some(mut ch) = channel_name2channel(c, chname, Some(aid), 0).and_then(|k| get(c, &k)) else {
        say(c, &sd, &fmt(msg_txt(1407), &[&chname]));
        return -1;
    };
    if !pc::has_permission(c, aid, crate::rules::PC_PERM_CHANNEL_ADMIN) {
        if not_owner(c, aid, &ch) {
            say(c, &sd, &fmt(msg_txt(1412), &[&chname]));
            return -1;
        } else if !PRIVATE_CHANNEL.ban {
            say(c, &sd, &fmt(msg_txt(765), &[&chname])); // You're not allowed to ban a player.
            return -1;
        }
    }
    if flag != 2 && flag != 3 {
        let Some(t) = tsd.as_ref().filter(|t| !pc::has_permission(c, t.id, crate::rules::PC_PERM_CHANNEL_ADMIN)) else {
            say(c, &sd, &fmt(msg_txt(1464), &[&pname])); // Ban failed for player '%s'.
            return -1;
        };
        let banned = ch.banned.iter().any(|b| b.char_id == t.char_id);
        if flag == 0 && banned {
            say(c, &sd, &fmt(msg_txt(1465), &[&t.name])); // Player '%s' is already banned from this channel.
            return -1;
        } else if flag == 1 && !banned {
            say(c, &sd, &fmt(msg_txt(1440), &[&t.name])); // Player '%s' is not banned from this channel.
            return -1;
        }
    } else if ch.banned.is_empty() {
        say(c, &sd, &fmt(msg_txt(1439), &[&chname])); // Channel '%s' contains no banned players.
        return 0;
    }
    // let properly alter the list now
    let out = match flag {
        0 => {
            let Some(t) = tsd else { return -1 };
            ch.banned.push(ChanBanEntry { char_id: t.char_id, char_name: t.name.clone() });
            put(c, &ch);
            channel_clean(c, &ch.key, t.id, 0);
            fmt(msg_txt(1437), &[&t.name, &chname]) // Player '%s' is banned from the '%s' channel.
        }
        1 => {
            let Some(t) = tsd else { return -1 };
            ch.banned.retain(|b| b.char_id != t.char_id);
            put(c, &ch);
            fmt(msg_txt(1441), &[&t.name, &chname]) // Player '%s' is unbanned from the '%s' channel.
        }
        2 => {
            ch.banned.clear();
            put(c, &ch);
            fmt(msg_txt(1442), &[&chname]) // Cleared all bans from the '%s' channel.
        }
        _ => {
            say(c, &sd, &fmt(msg_txt(1443), &[&ch.name])); // ---- '#%s' Ban List:
            let mut list = ch.banned.clone();
            list.sort_by_key(|b| crate::party::idb_order(b.char_id));
            for b in list {
                let (cid, name) = (b.char_id, b.char_name);
                let line = if !name.is_empty() { format!("{cid}: {name}") } else { format!("{cid}: ****") };
                say(c, &sd, &line);
            }
            return 0;
        }
    };
    say(c, &sd, &out);
    0
}

/// channel.cpp channel_pckick (@channel kick): 1 kicked, -1 refused.
pub fn channel_pckick(c: &Ctx, aid: u32, chname: &str, pname: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    let tsd = pc::nick2sd(c, pname, false);
    if channel_chk(c, chname, "", 1) != 0 {
        say(c, &sd, msg_txt(1405));
        return -1;
    }
    let Some(ch) = channel_name2channel(c, chname, Some(aid), 0).and_then(|k| get(c, &k)) else {
        say(c, &sd, &fmt(msg_txt(1407), &[&chname]));
        return -1;
    };
    let Some(t) = tsd else {
        say(c, &sd, msg_txt(3));
        return -1;
    };
    if !pc::has_permission(c, aid, crate::rules::PC_PERM_CHANNEL_ADMIN) {
        if not_owner(c, aid, &ch) {
            say(c, &sd, &fmt(msg_txt(1412), &[&chname]));
        } else if !PRIVATE_CHANNEL.kick {
            say(c, &sd, &fmt(msg_txt(766), &[&chname])); // You cannot kick a player from channel '%s'.
        }
        return -1;
    }
    if channel_pc_haschan(c, aid, &ch) < 0 {
        say(c, &sd, &fmt(msg_txt(1425), &[&chname]));
        return -1;
    }
    if ch.char_id == char_id_of(c, aid) {
        say(c, &sd, msg_txt(767)); // You're not allowed to kick a player.
        return -1;
    }
    if ch.opt & CHAN_OPT_ANNOUNCE_LEAVE != 0 {
        let out = fmt(msg_txt(768), &[&ch.alias, &t.name]); // %s %s has been kicked.
        clif_channel_msg(c, &ch, &out, ch.color);
    }
    match ch.kind {
        CHAN_TYPE_ALLY => {
            channel_pcquit(c, t.id, 3);
        }
        CHAN_TYPE_MAP => {
            channel_pcquit(c, t.id, 4);
        }
        _ => {
            channel_clean(c, &ch.key, t.id, 0);
        }
    }
    1
}

/// channel.cpp channel_pcsetopt (@channel setopt).
pub fn channel_pcsetopt(c: &Ctx, aid: u32, chname: &str, option: &str, val: &str) -> i32 {
    const OPT_STR: [&str; 9] = ["None", "SelfAnnounce", "JoinAnnounce", "LeaveAnnounce", "MessageDelay", "ColorOverride", "CanChat", "CanLeave", "Autojoin"];
    let Some(sd) = unit::get(c, aid) else { return -1 };
    if channel_chk(c, chname, "", 1) != 0 {
        say(c, &sd, msg_txt(1405));
        return -1;
    }
    let Some(mut ch) = channel_name2channel(c, chname, Some(aid), 0).and_then(|k| get(c, &k)) else {
        say(c, &sd, &fmt(msg_txt(1407), &[&chname]));
        return -1;
    };
    let admin = pc::has_permission(c, aid, crate::rules::PC_PERM_CHANNEL_ADMIN);
    if not_owner(c, aid, &ch) && !admin {
        say(c, &sd, &fmt(msg_txt(1412), &[&chname]));
        return -1;
    }
    // we only cmp 3 letter atm
    let ob = latin1(option);
    let k = (1..OPT_STR.len()).find(|&k| {
        let a: Vec<u8> = ob.iter().copied().take(3).collect();
        let b: Vec<u8> = OPT_STR[k].bytes().take(3).collect();
        a.eq_ignore_ascii_case(&b)
    });
    let Some(k) = k.filter(|_| !option.is_empty()) else {
        say(c, &sd, &fmt(msg_txt(1447), &[&option])); // Unknown channel option '%s'.
        say(c, &sd, msg_txt(1414)); // ---- Available options:
        for s in OPT_STR.iter().skip(1) {
            say(c, &sd, &fmt(msg_txt(1445), &[s])); // - '%s'
        }
        return -1;
    };
    let opt: u8 = 1 << (k - 1);
    if ch.kind == CHAN_TYPE_PRIVATE && !admin {
        match opt {
            CHAN_OPT_MSG_DELAY if !PRIVATE_CHANNEL.change_delay => return -1,
            CHAN_OPT_COLOR_OVERRIDE if !PRIVATE_CHANNEL.color_override => return -1,
            _ => {}
        }
    }
    if val.is_empty() {
        if opt == CHAN_OPT_MSG_DELAY {
            say(c, &sd, &fmt(msg_txt(1466), &[&OPT_STR[k]])); // Input the number of seconds (0-10) for the '%s' option.
            return -1;
        } else if ch.opt & opt != 0 {
            say(c, &sd, &fmt(msg_txt(1449), &[&OPT_STR[k], &OPT_STR[k]])); // Option '%s' is already enabled (use '@channel setopt %s 0' to disable).
            return -1;
        } else {
            ch.opt |= opt;
            put(c, &ch);
            say(c, &sd, &fmt(msg_txt(1450), &[&OPT_STR[k], &ch.name])); // Option '%s' is enabled for channel '#%s'.
        }
    } else {
        let v = crate::cstr::atoi(val);
        if opt == CHAN_OPT_MSG_DELAY {
            if !(0..=10).contains(&v) {
                say(c, &sd, &fmt(msg_txt(1451), &[&v, &OPT_STR[k]])); // Value '%d' for option '%s' is out of range (limit 0-10).
                return -1;
            }
            if v == 0 {
                ch.opt &= !opt;
                ch.msg_delay = 0;
                put(c, &ch);
                say(c, &sd, &fmt(msg_txt(1453), &[&OPT_STR[k], &ch.name, &v])); // Option '%s' is disabled for channel '#%s'.
            } else {
                ch.opt |= opt;
                ch.msg_delay = (v * 1000) as u16;
                put(c, &ch);
                say(c, &sd, &fmt(msg_txt(1452), &[&OPT_STR[k], &ch.name, &v])); // Option '%s' is enabled for channel '#%s' at %d seconds.
            }
        } else if v != 0 {
            if ch.opt & opt != 0 {
                say(c, &sd, &fmt(msg_txt(1449), &[&OPT_STR[k], &OPT_STR[k]]));
                return -1;
            } else {
                ch.opt |= opt;
                put(c, &ch);
                say(c, &sd, &fmt(msg_txt(1450), &[&OPT_STR[k], &ch.name]));
            }
        } else if ch.opt & opt == 0 {
            say(c, &sd, &fmt(msg_txt(1450), &[&OPT_STR[k], &ch.name])); // (rAthena prints the "enabled" message here)
            return -1;
        } else {
            ch.opt &= !opt;
            put(c, &ch);
            say(c, &sd, &fmt(msg_txt(1453), &[&OPT_STR[k], &ch.name]));
        }
    }
    0
}

/// channel.cpp channel_autojoin (pc_reg_received): the conf channels with autojoin, without a password.
pub fn channel_autojoin(c: &Ctx, aid: u32) {
    ensure_init(c);
    // if (sd->state.autotrade || !sd->fd) return;
    if crate::autotrade::autotrade(c, aid) != 0 || !pc::is_active_session(c, aid) {
        return;
    }
    let group = pc::get(c, aid).map(|p| p.group_id).unwrap_or(0);
    let admin = pc::has_permission(c, aid, crate::rules::PC_PERM_CHANNEL_ADMIN);
    for ch in db_iter(c) {
        if !ch.pass.is_empty() || ch.opt & CHAN_OPT_AUTOJOIN == 0 {
            continue;
        }
        if !admin && !channel_pccheckgroup(&ch, group) {
            continue;
        }
        let channame = format!("#{}", ch.name);
        channel_pcjoin(c, aid, &channame, None);
    }
}

/// clif_parse_LoadEndAck on a map change: joins the new map's channel when the map template autojoins
/// (conf/channels.conf's #map does not; instances and nomapchannelautojoin maps never do).
pub fn on_map_change(c: &Ctx, aid: u32, m: u16) {
    // clif.cpp:11106: `... && !mapdata->instance_id && ...`
    if !map_tmpl_name().is_empty() && MAP_TMPL.autojoin && crate::instance_map::map_instance_id(c, m) == 0 && !crate::npc::mapflag(c, m, MF_NOMAPCHANNELAUTOJOIN) {
        channel_mjoin(c, aid);
    }
}

// ---------------------------------------------------------------------------------------------- clif.cpp

/// clif.cpp clif_channel_msg: 0x02C1 (id 0, colour) to every user of the channel.
pub fn clif_channel_msg(c: &Ctx, ch: &Channel, msg: &str, color: u32) {
    clif_channel_msg_bytes(c, ch, &latin1(msg), color);
}

fn clif_channel_msg_bytes(c: &Ctx, ch: &Channel, msg: &[u8], color: u32) {
    let mut msg_len = msg.len() + 1;
    if msg_len > crate::net::CHAT_SIZE_MAX - 12 {
        log::warn!("clif_channel_msg: Truncating too long message (len={msg_len}).");
        msg_len = crate::net::CHAT_SIZE_MAX - 12;
    }
    let mut w = Vec::with_capacity(12 + msg_len);
    w.extend_from_slice(&0x02C1u16.to_le_bytes());
    w.extend_from_slice(&((msg_len + 12) as u16).to_le_bytes());
    w.extend_from_slice(&0u32.to_le_bytes());
    w.extend_from_slice(&color.to_le_bytes());
    // safestrncpy(buf+12, msg, msg_len)
    let mut body: Vec<u8> = msg.iter().copied().take(msg_len - 1).collect();
    body.resize(msg_len, 0);
    w.extend_from_slice(&body);
    for cid in users_in_order(ch) {
        if let Some(u) = pc::charid2sd(c, cid) {
            clif::send(c, &w, &u, Target::SelfOnly);
        }
    }
}

// ---------------------------------------------------------------------------------------------- chat routing

/// clif_parse_WisMessage's '#channel' branch: true when the whisper went to a channel (or was refused there).
pub fn whisper(c: &Ctx, aid: u32, target: &str, message: &str) -> bool {
    ensure_init(c);
    let Some(sd) = unit::get(c, aid) else { return false };
    let Some(key) = channel_name2channel(c, target, Some(aid), 3) else { return false };
    let Some(ch) = get(c, &key) else { return false };
    let group = pc::get(c, aid).map(|p| p.group_id).unwrap_or(0);
    if !(pc::has_permission(c, aid, crate::rules::PC_PERM_CHANNEL_ADMIN) || (ch.opt & CHAN_OPT_CAN_CHAT != 0 && channel_pccheckgroup(&ch, group))) {
        return false;
    }
    if channel_pc_haschan(c, aid, &ch) >= 0 {
        // we are in the chan
        channel_send(c, &key, aid, message);
    } else if ch.pass.is_empty() {
        // no pass needed
        if channel_join(c, &key, aid) == 0 {
            channel_send(c, &key, aid, message); // join success
        }
    } else {
        say(c, &sd, msg_txt(1402)); // You're not in that channel, type '@join <#channel_name>'
    }
    true
}

/// clif_parse_GlobalMessage: a chat bar bound to a channel (@channel bindto) sends there. True when it did.
pub fn global_message(c: &Ctx, aid: u32, message: &str) -> bool {
    let x = social::sx(c, aid);
    if x.gcbind.is_empty() {
        return false;
    }
    let Some(ch) = get(c, &x.gcbind) else { return false };
    if ch.opt & CHAN_OPT_CAN_CHAT != 0 || pc::has_permission(c, aid, crate::rules::PC_PERM_CHANNEL_ADMIN) {
        channel_send(c, &ch.key, aid, message);
        return true;
    }
    false
}

/// atcommand.cpp @fontcolor's colour list lookup (channel_config.colors_name).
pub fn color_index(name: &str) -> Option<usize> {
    COLORS.iter().position(|c| c.0.eq_ignore_ascii_case(name))
}

#[allow(dead_code)]
const _: u8 = CHAN_OPT_NONE;
