//! map/atcommand.cpp's guild @commands: @guild, @breakguild, @guildlevelup (@guildlvup / @guildlvlup), @gstorage
//! (guildstorage), @cleargstorage, @guildrecall, @guildspy and @changegm. atcommand::run routes the ACMD_FUNC names
//! here. The other guild-related commands belong elsewhere: @agitstart / @agitend (2, 3) and @gvgon / @gvgoff come with
//! the War of Emperium and the GvG map rules; @disguiseguild / @undisguiseguild / @sizeguild need disguises and size
//! changes (the looks system).

use crate::clif;
use crate::cstr::Scan;
use crate::data::{format_msg, msg_txt};
use crate::guild;
use crate::int_guild::{GBI_GUILDLV, MAX_GUILDLEVEL};
use crate::net::wire::latin1;
use crate::pc;
use crate::unit::{self, Unit};
use crate::world::Ctx;

struct Out<'a> {
    c: &'a Ctx<'a>,
    fd: Option<&'a Unit>,
}

impl Out<'_> {
    fn say(&self, text: &str) {
        if let Some(u) = self.fd {
            clif::displaymessage(self.c, u, text);
        }
    }
}

/// Calls the ACMD_FUNC `func` for player `sd` (replies go to `fd`, the invoker). None when `func` is not one of these.
pub fn run(c: &Ctx, fd: Option<&Unit>, sd: u32, func: &str, _command: &str, message: &str) -> Option<i32> {
    let out = Out { c, fd };
    Some(match func {
        "guild" => guild_(c, &out, sd, message),
        "breakguild" => breakguild(c, &out, sd),
        "guildlevelup" => guildlevelup(c, &out, sd, message),
        "guildstorage" => guildstorage(c, &out, sd),
        "cleargstorage" => cleargstorage(c, &out, sd),
        "guildrecall" => guildrecall(c, &out, sd, message),
        "guildspy" => guildspy(c, &out, sd, message),
        "changegm" => changegm(c, &out, sd, message),
        _ => return None,
    })
}

/// sscanf(message, "%23[^\n]", …).
fn line23(message: &str) -> Option<String> {
    let b = latin1(message);
    Scan::new(&b).set(23, b"\n")
}

/// ACMD_FUNC(guild): creates a guild without the Emperium (guild_emperium_check is switched off around guild_create).
fn guild_(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    // sd->clan (see guild::in_clan)
    if guild::in_clan(c, aid) {
        out.say(msg_txt(1498)); // You cannot create a guild because you are in a clan.
        return -1;
    }
    let Some(name) = line23(message).filter(|_| !message.is_empty()) else {
        out.say(msg_txt(1030)); // Please enter a guild name (usage: @guild <guild_name>).
        return -1;
    };
    guild::guild_create(c, aid, &name, false);
    0
}

/// ACMD_FUNC(breakguild).
fn breakguild(c: &Ctx, out: &Out, aid: u32) -> i32 {
    if guild::guild_id_of(c, aid) == 0 {
        // Player does not have a guild
        out.say(msg_txt(252)); // You are not in a guild.
        return -1;
    }
    let Some(g) = guild::sd_guild(c, aid) else {
        // Guild was not found. HOW?
        out.say(msg_txt(252));
        return -1;
    };
    if !guild::gx(c, aid).gmaster_flag {
        out.say(msg_txt(1181)); // You need to be a Guild Master to use this command.
        return -1;
    }
    // Break guild
    if guild::guild_break(c, aid, &g.guild.name) != 0 {
        0
    } else {
        -1 // Something went wrong
    }
}

/// ACMD_FUNC(guildlevelup): the guild gains (or loses) levels, capped to 1 .. MAX_GUILDLEVEL.
fn guildlevelup(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let b = latin1(message);
    let level = if message.is_empty() { None } else { Scan::new(&b).int(11) };
    let level = match level {
        Some(l) if l != 0 => l as i32,
        _ => {
            out.say(msg_txt(1014)); // Please enter a valid level (usage: @guildlvup/@guildlvlup <# of levels>).
            return -1;
        }
    };
    let g = guild::sd_guild(c, aid);
    let Some(g) = g.filter(|_| guild::guild_id_of(c, aid) > 0) else {
        out.say(msg_txt(43)); // You're not in a guild.
        return -1;
    };
    let lv = g.guild.guild_lv;
    let mut added_level = level as i16;
    if level > 0 && (level > MAX_GUILDLEVEL as i32 || added_level > MAX_GUILDLEVEL - lv) {
        added_level = MAX_GUILDLEVEL - lv; // fix positive overflow
    } else if level < 0 && (level < -(MAX_GUILDLEVEL as i32) || added_level < 1 - lv) {
        added_level = 1 - lv; // fix negative overflow
    }
    if added_level != 0 {
        guild::intif_guild_change_basicinfo(g.guild.guild_id, GBI_GUILDLV, added_level as i64);
        out.say(msg_txt(179)); // Guild level changed.
    } else {
        out.say(msg_txt(45)); // Guild level change failed.
        return -1;
    }
    0
}

/// ACMD_FUNC(guildstorage) (@gstorage).
fn guildstorage(c: &Ctx, out: &Out, aid: u32) -> i32 {
    let Some(p) = pc::get(c, aid) else { return -1 };
    if p.npc_id != 0 || p.vending || p.buyingstore || p.trading {
        return -1;
    }
    match crate::guild_storage::storage_guild_storageopen(c, aid) {
        crate::guild_storage::GSTORAGE_OPEN => out.say(msg_txt(920)), // Guild storage opened.
        crate::guild_storage::GSTORAGE_STORAGE_ALREADY_OPEN => {
            out.say(msg_txt(250)); // You have already opened your storage. Close it first.
            return -1;
        }
        crate::guild_storage::GSTORAGE_ALREADY_OPEN => {
            out.say(msg_txt(251)); // You have already opened your guild storage. Close it first.
            return -1;
        }
        crate::guild_storage::GSTORAGE_NO_GUILD => {
            out.say(msg_txt(252)); // You are not in a guild.
            return -1;
        }
        crate::guild_storage::GSTORAGE_NO_STORAGE => {
            out.say(msg_txt(786)); // The guild does not have a guild storage.
            return -1;
        }
        crate::guild_storage::GSTORAGE_NO_PERMISSION => {
            out.say(msg_txt(787)); // You do not have permission to use the guild storage.
            return -1;
        }
        _ => {}
    }
    0
}

/// ACMD_FUNC(cleargstorage): deletes the items of the first `amount` slots (the storage is locked meanwhile).
fn cleargstorage(c: &Ctx, out: &Out, aid: u32) -> i32 {
    if guild::sd_guild(c, aid).is_none() {
        out.say(msg_txt(43)); // You're not in a guild.
        return -1;
    }
    match pc::get(c, aid).map(|p| p.storage_flag).unwrap_or(0) {
        1 | 3 => {
            out.say(msg_txt(250)); // You have already opened your storage. Close it first.
            return -1;
        }
        2 => {
            out.say(msg_txt(251)); // You have already opened your guild storage. Close it first.
            return -1;
        }
        _ => {}
    }
    let gid = guild::guild_id_of(c, aid);
    let Some(gs) = crate::guild_storage::guild2storage2(c, gid) else {
        // Doesn't have opened @gstorage yet, so we skip the deletion since *shouldn't* have any item there.
        return -1;
    };
    let j = gs.amount as usize;
    crate::guild_storage::set_lock(c, gid, true); // Lock @gstorage
    for i in 0..j {
        let amount = crate::guild_storage::slot_item(c, gid, i).map(|it| it.amount as i32).unwrap_or(0);
        if let Some(mut s) = crate::guild_storage::guild2storage2(c, gid) {
            crate::guild_storage::storage_guild_delitem(c, aid, &mut s, i, amount);
        }
    }
    crate::guild_storage::storage_guild_storageclose(c, aid);
    crate::guild_storage::set_lock(c, gid, false); // Cleaning done, release lock
    out.say(msg_txt(1395)); // Your guild storage was cleaned.
    0
}

/// ACMD_FUNC(guildrecall): the guild's online members come to the invoker's cell.
fn guildrecall(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    let Some(guild_name) = line23(message).filter(|_| !message.is_empty()) else {
        out.say(msg_txt(1034)); // Please enter a guild name/ID (usage: @guildrecall <guild_name/ID>).
        return -1;
    };
    let any_warp = pc::has_permission(c, aid, crate::rules::PC_PERM_WARP_ANYWHERE);
    if crate::npc::mapflag(c, sd.map, crate::npc::MF_NOWARPTO) && !any_warp {
        out.say(msg_txt(1032)); // You are not authorized to warp someone to your current map.
        return -1;
    }
    let Some(g) = guild::guild_searchnameid(c, &guild_name) else {
        out.say(msg_txt(94)); // Incorrect name/ID, or no one from the guild is online.
        return -1;
    };
    let mut count = 0;
    let my_level = pc::group_level(c, aid);
    let mapindex = pc::get(c, aid).and_then(|p| p.mapindex).unwrap_or(sd.map);
    for pl in crate::social::all_pc(c) {
        if pl.id == aid || guild::guild_id_of(c, pl.id) != g.guild.guild_id {
            continue;
        }
        if pc::group_level(c, pl.id) > my_level || (pl.map == sd.map && pl.x == sd.x && pl.y == sd.y) {
            continue; // Skip GMs greater than you... or chars already on the cell
        }
        if crate::npc::mapflag(c, pl.map, crate::npc::MF_NOWARP) && !any_warp {
            count += 1;
        } else {
            if pc::setpos(c, pl.id, mapindex, sd.x, sd.y, crate::unit::CLR_RESPAWN) == pc::SetPos::Autotrade {
                count += 1;
            }
        }
    }
    out.say(&format_msg(msg_txt(93), &[&g.guild.name])); // All online characters of the %s guild have been recalled to your position.
    if count != 0 {
        out.say(&format_msg(msg_txt(1033), &[&count])); // Because you are not authorized to warp from some maps, %d player(s) have not been recalled.
    }
    0
}

/// ACMD_FUNC(guildspy): enable_spy is off on this server (map_athena.conf), so the command is refused.
fn guildspy(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    if !guild::ENABLE_SPY {
        out.say(msg_txt(1125)); // The mapserver has spy command support disabled.
        return -1;
    }
    let Some(guild_name) = line23(message).filter(|_| !message.is_empty()) else {
        out.say(msg_txt(1126)); // Please enter a guild name/ID (usage: @guildspy <guild_name/ID>).
        return -1;
    };
    let Some(g) = guild::guild_searchnameid(c, &guild_name) else {
        out.say(msg_txt(94)); // Incorrect name/ID, or no one from the specified guild is online.
        return -1;
    };
    let mut x = guild::gx(c, aid);
    if x.guildspy == g.guild.guild_id {
        x.guildspy = 0;
        out.say(&format_msg(msg_txt(103), &[&g.guild.name])); // No longer spying on the %s guild.
    } else {
        x.guildspy = g.guild.guild_id;
        out.say(&format_msg(msg_txt(104), &[&g.guild.name])); // Spying on the %s guild.
    }
    guild::put_gx(c, &x);
    0
}

/// ACMD_FUNC(changegm): the guild master hands the guild to an online member.
fn changegm(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    let gid = guild::guild_id_of(c, aid);
    let g = guild::sd_guild(c, aid);
    let name = pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
    let Some(g) = g.filter(|g| gid != 0 && g.guild.master == name) else {
        out.say(msg_txt(1181)); // You need to be a Guild Master to use this command.
        return -1;
    };
    if crate::npc::mapflag(c, sd.map, guild::MF_GUILDLOCK) || crate::npc::mapflag(c, sd.map, crate::npc::MF_GVG_CASTLE) {
        out.say(msg_txt(1182)); // You cannot change guild leaders on this map.
        return -1;
    }
    let Some(target) = line23(message).filter(|_| !message.is_empty()) else {
        out.say(msg_txt(1183)); // Usage: @changegm <guild_member_name>
        return -1;
    };
    let Some(pl) = pc::nick2sd(c, &target, false).filter(|pl| guild::guild_id_of(c, pl.id) == gid) else {
        out.say(msg_txt(1184)); // Target character must be online and be a guild member.
        return -1;
    };
    if !guild::guild_leaderchange_woe() && guild::is_agit_start(c) {
        // PACKETVER >= 20151001
        crate::clif_items::msg(c, &sd, 2963); // MSI_IMPOSSIBLE_CHANGE_GUILD_MASTER_IN_SIEGE_TIME
        return -1;
    }
    if guild::guild_leaderchange_delay() != 0 && c.tick() / 1000 - g.guild.last_leader_change < guild::guild_leaderchange_delay() {
        crate::clif_items::msg(c, &sd, 2964); // MSI_IMPOSSIBLE_CHANGE_GUILD_MASTER_NOT_TIME
        return -1;
    }
    guild::guild_gm_change(c, gid, pl.char_id);
    0
}
