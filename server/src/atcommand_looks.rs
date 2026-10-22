//! map/atcommand.cpp: looks and presence (atcmd agent, wave D): @model, @dye, @hairstyle, @haircolor, @bodystyle,
//! @changelook, @size / @sizeall / @sizeguild, @fakename, @font, @misceffect, @refresh / @refreshall, @camerainfo,
//! @costume, @changedress, @mount / @mount2, @displaystatus. (@disguise and friends: atcmd_disguise.rs.) Routed from
//! atcommand_atcmd.rs `run` by the ACMD_FUNC name.

use crate::atcommand_atcmd::Out;
use crate::clif::{self, Target};
use crate::conf::{max_cloth_color, max_hair_color, max_hair_style, min_cloth_color, min_hair_color, min_hair_style};
use crate::cstr::Scan;
use crate::data::{self, format_msg, msg_txt};
use crate::itemui::look::{self, pc_changelook};
use crate::pc;
use crate::status::consts::*;
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// Runs `func` when it is one of this file's commands; None otherwise.
pub fn run(c: &Ctx, fd: Option<&Unit>, sd: u32, func: &str, command: &str, message: &str) -> Option<i32> {
    let out = Out { c, fd };
    Some(match func {
        "model" => model(c, &out, sd, message),
        "bodystyle" => bodystyle(c, &out, sd, command, message),
        "dye" => dye(c, &out, sd, message),
        "hair_style" => hair_style(c, &out, sd, message),
        "hair_color" => hair_color(c, &out, sd, message),
        "changelook" => changelook(c, &out, sd, message),
        "size" => size(c, &out, sd, message),
        "sizeall" => sizeall(c, &out, message),
        "sizeguild" => sizeguild(c, &out, message),
        "fakename" => fakename(c, &out, sd, message),
        "font" => font(c, &out, sd, message),
        "misceffect" => misceffect(c, sd, message),
        "refresh" => {
            clif_refresh(c, sd);
            0
        }
        "refreshall" => refreshall(c),
        "camerainfo" => camerainfo(c, &out, sd, message),
        "costume" => costume(c, &out, sd, message),
        "changedress" => changedress(c, sd),
        "mount_peco" => mount_peco(c, &out, sd, message),
        "mount2" => mount2(c, &out, sd),
        "displaystatus" => displaystatus(c, &out, sd, message),
        _ => return None,
    })
}

fn bytes(s: &str) -> Vec<u8> {
    crate::net::wire::latin1(s)
}

/// `%11d`: an int (a longer number is cut at 11 characters; the long result truncated to int as the C store).
fn d11(s: &mut Scan) -> Option<i32> {
    s.int(11).map(|v| v as i32)
}

// ---------------------------------------------------------------------------------------------- looks

/// ACMD_FUNC(model): @model <hair style> {<hair color> {<clothes color>}}.
fn model(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let b = bytes(message);
    let mut s = Scan::new(&b);
    let (mut hair_style, mut hair_color, mut cloth_color) = (0, 0, 0);
    let mut n = 0;
    if let Some(v) = d11(&mut s) {
        hair_style = v;
        n += 1;
        if let Some(v) = d11(&mut s) {
            hair_color = v;
            n += 1;
            if let Some(v) = d11(&mut s) {
                cloth_color = v;
                n += 1;
            }
        }
    }
    if message.is_empty() || n < 1 {
        // Please enter at least one value (usage: @model <hair ID: %d-%d> <hair color: %d-%d> <clothes color: %d-%d>).
        out.say(&format_msg(msg_txt(991), &[&min_hair_style(), &max_hair_style(), &min_hair_color(), &max_hair_color(), &min_cloth_color(), &max_cloth_color()]));
        return -1;
    }
    if (min_hair_style()..=max_hair_style()).contains(&hair_style) && (min_hair_color()..=max_hair_color()).contains(&hair_color) && (min_cloth_color()..=max_cloth_color()).contains(&cloth_color) {
        pc_changelook(c, sd, look::LOOK_HAIR, hair_style);
        pc_changelook(c, sd, look::LOOK_HAIR_COLOR, hair_color);
        pc_changelook(c, sd, look::LOOK_CLOTHES_COLOR, cloth_color);
        out.say(msg_txt(36)); // Appearance changed.
        0
    } else {
        out.say(msg_txt(37)); // An invalid number was specified.
        -1
    }
}

/// ACMD_FUNC(bodystyle): @bodystyle <style> / off: the job's alternate outfit (LOOK_BODY2).
fn bodystyle(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    let Some(ch) = pc::character_of(c, sd) else { return -1 };
    let job = crate::db::jobs::job_db().find(ch.class);
    let Some(job) = job.filter(|j| !j.alternate_outfits.is_empty()) else {
        out.say(msg_txt(740)); // This job has no alternate body styles.
        return -1;
    };
    let help = || {
        if let Some(h) = data::atcommands().help_string(command) {
            out.say(h);
        }
    };
    if message.is_empty() {
        help();
        return -1;
    }
    let body2 = unit::get(c, sd).map(|u| u.vd.body2).unwrap_or(0);
    // Handle the 'off' alias to revert to the default bodystyle
    if crate::cstr::eq_ignore_case(message, "off") {
        if body2 != ch.class as i32 {
            pc_changelook(c, sd, look::LOOK_BODY2, ch.class as i32);
            out.say(msg_txt(1539)); // Appearance changed to default.
        } else {
            out.say(msg_txt(1540)); // Appearance is already set to default.
        }
        return 0;
    }
    let b = bytes(message);
    // sscanf("%hu"): the number stored into an unsigned short
    let Some(v) = Scan::new(&b).int(usize::MAX) else {
        help();
        return -1;
    };
    let body_style = v as u16;
    if body_style != ch.class && !job.alternate_outfits.contains(&body_style) {
        out.say(msg_txt(37)); // An invalid number was specified.
        return -1;
    }
    if body_style as i32 != body2 {
        pc_changelook(c, sd, look::LOOK_BODY2, body_style as i32);
        out.say(msg_txt(36)); // Appearance changed.
    }
    0
}

/// ACMD_FUNC(dye): @dye / @ccolor <clothes color>.
fn dye(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    one_look(c, out, sd, message, 992, min_cloth_color(), max_cloth_color(), look::LOOK_CLOTHES_COLOR)
}

/// ACMD_FUNC(hair_style): @hairstyle / @hstyle <hair style>.
fn hair_style(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    one_look(c, out, sd, message, 993, min_hair_style(), max_hair_style(), look::LOOK_HAIR)
}

/// ACMD_FUNC(hair_color): @haircolor / @hcolor <hair color>.
fn hair_color(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    one_look(c, out, sd, message, 994, min_hair_color(), max_hair_color(), look::LOOK_HAIR_COLOR)
}

/// The shape of @dye / @hairstyle / @haircolor: sscanf "%11d", the usage message with the range, the range check,
/// pc_changelook and "Appearance changed.".
#[allow(clippy::too_many_arguments)]
fn one_look(c: &Ctx, out: &Out, sd: u32, message: &str, usage: u16, min: i32, max: i32, kind: i32) -> i32 {
    let b = bytes(message);
    let v = d11(&mut Scan::new(&b));
    let Some(v) = v.filter(|_| !message.is_empty()) else {
        out.say(&format_msg(msg_txt(usage), &[&min, &max]));
        return -1;
    };
    if (min..=max).contains(&v) {
        pc_changelook(c, sd, kind, v);
        out.say(msg_txt(36)); // Appearance changed.
        0
    } else {
        out.say(msg_txt(37)); // An invalid number was specified.
        -1
    }
}

/// ACMD_FUNC(changelook): @changelook {<position>} <view id>: clif_changelook only (the character keeps its look).
fn changelook(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    const POS: [i32; 8] = [look::LOOK_HEAD_TOP, look::LOOK_HEAD_MID, look::LOOK_HEAD_BOTTOM, look::LOOK_WEAPON, look::LOOK_SHIELD, look::LOOK_SHOES, look::LOOK_ROBE, look::LOOK_BODY2];
    let b = bytes(message);
    let mut s = Scan::new(&b);
    let mut j = 0;
    let mut k = 0;
    let mut i = 0;
    if let Some(v) = d11(&mut s) {
        j = v;
        i += 1;
        if let Some(v) = d11(&mut s) {
            k = v;
            i += 1;
        }
    }
    if i < 1 {
        out.say(msg_txt(1177)); // Usage: @changelook {<position>} <view id>
        out.say(msg_txt(1178)); // Position: 1-Top 2-Middle 3-Bottom 4-Weapon 5-Shield 6-Shoes 7-Robe 8-Body
        return -1;
    } else if i == 2 {
        if !(1..=8).contains(&j) {
            j = 1;
        }
        j = POS[(j - 1) as usize];
    } else {
        // position not defined, use HEAD_TOP as default
        k = j;
        j = look::LOOK_HEAD_TOP;
    }
    crate::pc_equip::clif_changelook(c, sd, j as u8, k);
    0
}

// ---------------------------------------------------------------------------------------------- size

/// status.hpp e_size.
const SZ_SMALL: i32 = 0;
const SZ_MEDIUM: i32 = 1;
const SZ_BIG: i32 = 2;

/// The size change of @size / @sizeall / @sizeguild for one player: an old size is undone by pc_setpos to the same
/// cell (the client redraws the player), then the new size's effect (EF_BABYBODY / EF_GIANTBODY) to the area.
fn apply_size(c: &Ctx, aid: u32, size: i32) {
    let mut r = crate::atcmd_pc::get(c, aid);
    if r.size != 0 {
        r.size = SZ_SMALL as u8;
        crate::atcmd_pc::put(c, &r);
        if let (Some(u), Some(p)) = (unit::get(c, aid), pc::get(c, aid)) {
            pc::setpos(c, aid, p.mapindex.unwrap_or(u.map), u.x, u.y, unit::CLR_TELEPORT);
        }
    }
    let mut r = crate::atcmd_pc::get(c, aid);
    r.size = size as u8;
    crate::atcmd_pc::put(c, &r);
    if let Some(u) = unit::get(c, aid) {
        if size == SZ_MEDIUM {
            clif::specialeffect(c, &u, data::constant("EF_BABYBODY").unwrap_or(0) as i32, Target::Area);
        } else if size == SZ_BIG {
            clif::specialeffect(c, &u, data::constant("EF_GIANTBODY").unwrap_or(0) as i32, Target::Area);
        }
    }
}

/// ACMD_FUNC(size): @size <0-2>: sd->state.size (tiny / big players [Valaris]); every time, even to the same size.
fn size(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let size = crate::cstr::atoi(message).clamp(SZ_SMALL, SZ_BIG);
    apply_size(c, sd, size);
    out.say(msg_txt(1303)); // Size change applied.
    0
}

/// ACMD_FUNC(sizeall): every player whose size differs, in mapit_getallusers order.
fn sizeall(c: &Ctx, out: &Out, message: &str) -> i32 {
    use crate::pc::pc as _;
    use spacetimedb::Table as _;
    let size = crate::cstr::atoi(message).clamp(SZ_SMALL, SZ_BIG);
    let mut ids: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
    ids.sort_by_key(|&id| crate::party::idb_order(id));
    for pid in ids {
        if crate::atcmd_pc::get(c, pid).size as i32 != size {
            apply_size(c, pid, size);
        }
    }
    out.say(msg_txt(1303)); // Size change applied.
    0
}

/// ACMD_FUNC(sizeguild): @sizeguild <size> <guild name/ID>: the guild's online members, in member slot order.
fn sizeguild(c: &Ctx, out: &Out, message: &str) -> i32 {
    let b = bytes(message);
    let mut s = Scan::new(&b);
    let size = d11(&mut s);
    let guild = size.and_then(|_| {
        s.ws();
        s.set(23, b"\n")
    });
    let (Some(size), Some(guild)) = (size, guild) else {
        out.say(msg_txt(1304)); // Please enter guild name/ID (usage: @sizeguild <size> <guild name/ID>).
        return -1;
    };
    let Some(g) = crate::guild::guild_searchnameid(c, &guild) else {
        out.say(msg_txt(94)); // Incorrect name/ID, or no one from the guild is online.
        return -1;
    };
    let size = size.clamp(SZ_SMALL, SZ_BIG);
    for pid in g.guild.member.iter().take(g.guild.max()).map(|m| m.sd).filter(|&sd| sd != 0).collect::<Vec<u32>>() {
        if crate::atcmd_pc::get(c, pid).size as i32 != size {
            apply_size(c, pid, size);
        }
    }
    out.say(msg_txt(1303)); // Size change applied.
    0
}

// ---------------------------------------------------------------------------------------------- name, font, effects

/// ACMD_FUNC(fakename): @fakename {<name>}: sd->fakename (clif_name and chat show it; nothing returns the real name).
fn fakename(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let mut r = crate::atcmd_pc::get(c, sd);
    let disguised = crate::atcmd_disguise::disguise_of(c, sd) != 0;
    if message.is_empty() {
        if !r.fakename.is_empty() {
            r.fakename.clear();
            crate::atcmd_pc::put(c, &r);
            if let Some(u) = unit::get(c, sd) {
                clif::name_area(c, &u);
                if disguised {
                    clif::name(c, &u, &u, Target::SelfOnly); // clif_name_self
                }
            }
            out.say(msg_txt(1307)); // Returned to real name.
            return 0;
        }
        out.say(msg_txt(1308)); // You must enter a name.
        return -1;
    }
    let b = bytes(message);
    if b.len() < 2 {
        out.say(msg_txt(1309)); // Fake name must be at least two characters.
        return -1;
    }
    // safestrncpy(sd->fakename, message, sizeof(sd->fakename)): NAME_LENGTH - 1 characters
    r.fakename = crate::net::wire::from_latin1(&b[..b.len().min(23)]);
    crate::atcmd_pc::put(c, &r);
    if let Some(u) = unit::get(c, sd) {
        clif::name_area(c, &u);
        if disguised {
            // Another packet should be sent so the client updates the name for sd
            clif::name(c, &u, &u, Target::SelfOnly);
        }
    }
    out.say(msg_txt(1310)); // Fake name enabled.
    0
}

/// ACMD_FUNC(font): @font <0-9>: sd->status.font, clif_font to the area.
fn font(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let font_id = crate::cstr::atoi(message);
    let Some(mut ch) = pc::character_of(c, sd) else { return -1 };
    let set = |ch: &mut crate::character::Character, f: u8| {
        ch.font = f;
        crate::status::put_char(c, ch);
        if let Some(mut u) = unit::get(c, sd) {
            u.font = f as i16;
            unit::save(c, &u);
        }
    };
    if font_id == 0 {
        if ch.font != 0 {
            set(&mut ch, 0);
            out.say(msg_txt(1356)); // Returning to normal font.
            if let Some(u) = unit::get(c, sd) {
                crate::script::buildins::script2::looks::clif_font(c, &u);
            }
        } else {
            out.say(msg_txt(1357)); // Use @font <1-9> to change your message font.
            out.say(msg_txt(1358)); // Use 0 or no parameter to return to normal font.
        }
    } else if !(0..=9).contains(&font_id) {
        out.say(msg_txt(1359)); // Invalid font. Use a value from 0 to 9.
    } else if font_id != ch.font as i32 {
        set(&mut ch, font_id as u8);
        if let Some(u) = unit::get(c, sd) {
            crate::script::buildins::script2::looks::clif_font(c, &u);
        }
        out.say(msg_txt(1360)); // Font changed.
    } else {
        out.say(msg_txt(1361)); // Already using this font.
    }
    0
}

/// ACMD_FUNC(misceffect): @misceffect <NOTIFYEFFECT_*>: clif_misceffect on the player (0..9, no message otherwise).
fn misceffect(c: &Ctx, sd: u32, message: &str) -> i32 {
    const NOTIFYEFFECT_BASE_LEVEL_UP: i32 = 0;
    const NOTIFYEFFECT_TAEKWON_BASE_LEVEL_UP: i32 = 9;
    if message.is_empty() {
        return -1;
    }
    let b = bytes(message);
    let Some(effect) = d11(&mut Scan::new(&b)) else { return -1 };
    if !(NOTIFYEFFECT_BASE_LEVEL_UP..=NOTIFYEFFECT_TAEKWON_BASE_LEVEL_UP).contains(&effect) {
        return -1;
    }
    crate::itemui::misceffect(c, sd, effect);
    0
}

/// ACMD_FUNC(refreshall): clif_refresh of every player (mapit_getallusers order).
fn refreshall(c: &Ctx) -> i32 {
    use crate::pc::pc as _;
    use spacetimedb::Table as _;
    let mut ids: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
    ids.sort_by_key(|&id| crate::party::idb_order(id));
    for pid in ids {
        clif_refresh(c, pid);
    }
    0
}

/// clif.cpp clif_refresh: the client is sent its map, inventory, status and surroundings again, getting rid of any
/// effects; open chats, trades and buying stores end.
pub fn clif_refresh(c: &Ctx, aid: u32) {
    use crate::inventory::{self as inv, SP_CARTINFO, SP_MAXWEIGHT, SP_WEIGHT};
    let Some(u) = unit::get(c, aid) else { return };
    clif::changemap(c, &u, u.map, u.x, u.y);
    crate::clif_items::inventorylist(c, aid);
    crate::clif_items::equipswitch_list(c, aid);
    if inv::pc_iscarton(c, aid) {
        crate::clif_items::cartlist(c, aid);
        inv::updatestatus(c, aid, SP_CARTINFO);
    }
    inv::updatestatus(c, aid, SP_WEIGHT);
    inv::updatestatus(c, aid, SP_MAXWEIGHT);
    for sp in [SP_STR, SP_AGI, SP_VIT, SP_INT, SP_DEX, SP_LUK, SP_POW, SP_STA, SP_WIS, SP_SPL, SP_CON, SP_CRT] {
        crate::status::clif::updatestatus(c, aid, sp); // (RENEWAL: the trait stats too)
    }
    // spirit spheres, Millennium Shield, charms, servant / abyss balls (clif_spiritball … _single to self): skill
    // resources of their families
    // clif.cpp:9871-9872: `if (sd->soulball) clif_soulball(sd, sd, SELF)` (the Soul Reaper's soul energy)
    if crate::skill::state::get(c, aid).soulball != 0 {
        if let Some(u) = unit::get(c, aid) {
            crate::skill::state::clif_soulball(c, &u, Some(&u), crate::clif::Target::SelfOnly);
        }
    }
    // (clif_refresh_clothcolor sends nothing for PACKETVER >= 20091103)
    // if (hom_is_active(sd->hd)) clif_send_homdata(*sd->hd, SP_ACK) (clif.cpp:9876-9877, homunculus/cmd.rs)
    crate::homunculus::cmd::clif_refresh(c, aid);
    // if (sd->md) { clif_mercenary_info; clif_mercenary_skillblock } if (sd->ed) clif_elemental_info
    // (clif.cpp:9878-9883, companion.rs)
    crate::companion::clif_refresh(c, aid);
    let r = crate::conf::area_size();
    if let Some(u) = unit::get(c, aid) {
        for other in crate::map::units_in_area(c, u.map, u.x - r, u.y - r, u.x + r, u.y + r, crate::map::BL_ALL) {
            clif::getareachar(c, &u, &other); // map_foreachinallrange(clif_getareachar, sd, AREA_SIZE, BL_ALL, sd)
        }
        crate::npc::weather_check(c, &u);
    }
    if pc::get(c, aid).map(|p| p.chat_id != 0).unwrap_or(false) {
        crate::chat::chat_leavechat(c, aid, false);
    }
    // if (sd->state.vending) clif_openvending(sd) (vending.rs)
    crate::vending::clif_refresh(c, aid);
    let Some(u) = unit::get(c, aid) else { return };
    if u.vd.dead_sit == 2 {
        clif::sitting(c, &u); // FIXME: just send to self, not area
    }
    if pc::isdead(c, aid) {
        // When you refresh, resend the death packet.
        clif::clearunit_single(c, aid, unit::CLR_DEAD, &u);
    } else {
        clif::changed_dir(c, &u, Target::SelfOnly);
    }
    crate::status::clif::efst_status_change_sub(c, &u, &u, Target::SelfOnly);
    // Issue #2143: cancel the trading state and the buying / selling state
    if pc::get(c, aid).map(|p| p.trading).unwrap_or(false) {
        crate::trade::trade_tradecancel(c, aid);
    }
    if pc::get(c, aid).map(|p| p.buyingstore).unwrap_or(false) {
        crate::buyingstore::buyingstore_close(c, aid);
    }
    crate::mail::mail_clear(c, aid);
    clif_load_confirm(c, aid);
    // if disguised: pc_disguise(sd, 0) then pc_disguise(sd, disguise) (refresh-da)
    let disguise = crate::atcmd_disguise::disguise_of(c, aid);
    if disguise != 0 {
        crate::atcmd_disguise::pc_disguise(c, aid, 0);
        crate::atcmd_disguise::pc_disguise(c, aid, disguise);
    }
    crate::storage::clif_refresh_storagewindow(c, aid);
}

/// clif.cpp clif_loadConfirm (PACKETVER_RE_NUM >= 20190320): ZC_NOTIFY_ACTORINIT to self, then clif_instance_info
/// (clif_instance.rs).
fn clif_load_confirm(c: &Ctx, aid: u32) {
    if let Some(u) = unit::get(c, aid) {
        crate::clif_instance::clif_load_confirm(c, &u);
    }
}

/// ACMD_FUNC(camerainfo): @camerainfo {<range> <rotation> <latitude>}: clif_camerainfo (0x0a78).
fn camerainfo(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    if message.is_empty() {
        clif_camerainfo(c, sd, true, 0.0, 0.0, 0.0);
        return 0;
    }
    // sscanf("%f %f %f")
    let mut rest = message;
    let mut vals = [0f32; 3];
    let mut n = 0;
    for v in vals.iter_mut() {
        match scan_float(rest) {
            Some((f, used)) => {
                *v = f;
                rest = &rest[used..];
                n += 1;
            }
            None => break,
        }
    }
    if n < 3 {
        out.say(msg_txt(793)); // Usage @camerainfo range rotation latitude
        return -1;
    }
    clif_camerainfo(c, sd, false, vals[0], vals[1], vals[2]);
    0
}

/// sscanf's `%f` on a str: leading white space, then the longest prefix strtof converts; (value, bytes used).
fn scan_float(s: &str) -> Option<(f32, usize)> {
    let b = s.as_bytes();
    let mut p = 0;
    while p < b.len() && crate::cstr::is_space(b[p]) {
        p += 1;
    }
    let st = p;
    if p < b.len() && (b[p] == b'+' || b[p] == b'-') {
        p += 1;
    }
    let mut digits = 0;
    while p < b.len() && b[p].is_ascii_digit() {
        p += 1;
        digits += 1;
    }
    if p < b.len() && b[p] == b'.' {
        p += 1;
        while p < b.len() && b[p].is_ascii_digit() {
            p += 1;
            digits += 1;
        }
    }
    if digits == 0 {
        // (inf / nan forms: strtof accepts them too)
        let t = &s[st..];
        let lower = t.to_ascii_lowercase();
        let unsigned = lower.trim_start_matches(['+', '-']);
        let sign_len = lower.len() - unsigned.len();
        if sign_len > 1 {
            return None;
        }
        return if unsigned.starts_with("inf") || unsigned.starts_with("nan") {
            let skip = if unsigned.starts_with("infinity") { 8 } else { 3 };
            Some((crate::atcommand_atcmd::c_atof(t) as f32, st + sign_len + skip))
        } else {
            None
        };
    }
    if p < b.len() && (b[p] == b'e' || b[p] == b'E') {
        let mut q = p + 1;
        if q < b.len() && (b[q] == b'+' || b[q] == b'-') {
            q += 1;
        }
        if q < b.len() && b[q].is_ascii_digit() {
            while q < b.len() && b[q].is_ascii_digit() {
                q += 1;
            }
            p = q;
        }
    }
    let v = crate::atcommand_atcmd::c_atof(&s[st..p]) as f32;
    Some((v, p))
}

/// clif.cpp clif_camerainfo (PACKETVER >= 20160525): ZC_CAMERA_INFO show.B range.F rotation.F latitude.F to self.
fn clif_camerainfo(c: &Ctx, aid: u32, show: bool, range: f32, rotation: f32, latitude: f32) {
    let mut w = Vec::with_capacity(15);
    w.extend_from_slice(&0x0a78u16.to_le_bytes());
    w.push(show as u8);
    w.extend_from_slice(&range.to_le_bytes());
    w.extend_from_slice(&rotation.to_le_bytes());
    w.extend_from_slice(&latitude.to_le_bytes());
    if let Some(u) = unit::get(c, aid) {
        clif::send(c, &w, &u, Target::SelfOnly);
    }
}

// ---------------------------------------------------------------------------------------------- costumes, mounts

/// The costume statuses of @costume, in rAthena's order (names[] / name2id[]).
const COSTUME_NAMES: [&str; 6] = ["Wedding", "Xmas", "Summer", "Summer2", "Hanbok", "Oktoberfest"];
const COSTUME_SC: [i32; 6] = [SC_WEDDING, SC_XMAS, SC_SUMMER, SC_DRESSUP, SC_HANBOK, SC_OKTOBERFEST];

/// ACMD_FUNC(changedress): the first costume status worn ends (SC_WEDDING, SC_XMAS, SC_SUMMER, SC_DRESSUP, SC_HANBOK,
/// SC_OKTOBERFEST).
fn changedress(c: &Ctx, sd: u32) -> i32 {
    for t in COSTUME_SC {
        if crate::status::sc::has_sce(c, sd, t) {
            crate::status::sc::status_change_end(c, sd, t, 0);
            // You should only be able to have one - so we cancel here
            break;
        }
    }
    0
}

/// ACMD_FUNC(costume): @costume {<name>}: no name takes the worn costume off (or lists them), a name puts it on
/// (sc_start, rate 100, val1 1 for SC_DRESSUP, INFINITE_TICK).
fn costume(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    use crate::status::sc::{has_sce, sc_start, status_change_end};
    if message.is_empty() {
        for k in 0..COSTUME_NAMES.len() {
            if has_sce(c, sd, COSTUME_SC[k]) {
                out.say(&format_msg(msg_txt(727), &[&COSTUME_NAMES[k]])); // '%s' Costume removed.
                status_change_end(c, sd, COSTUME_SC[k], 0);
                return 0;
            }
        }
        out.say(msg_txt(726)); // Available Costumes
        for name in COSTUME_NAMES {
            out.say(&format_msg(msg_txt(725), &[&name])); // -- %s
        }
        return -1;
    }
    for k in 0..COSTUME_NAMES.len() {
        if has_sce(c, sd, COSTUME_SC[k]) {
            out.say(&format_msg(msg_txt(724), &[&COSTUME_NAMES[k]])); // You're already wearing a(n) '%s' costume, type '@costume' to remove it.
            return -1;
        }
    }
    let Some(k) = COSTUME_NAMES.iter().position(|n| crate::cstr::eq_ignore_case(message, n)) else {
        out.say(&format_msg(msg_txt(723), &[&message])); // '%s' is an unknown costume
        return -1;
    };
    if let Some(u) = unit::get(c, sd) {
        sc_start(c, Some(&u), &u, COSTUME_SC[k], 100, if COSTUME_SC[k] == SC_DRESSUP { 1 } else { 0 }, crate::status::sc::INFINITE_TICK);
    }
    0
}

/// ACMD_FUNC(mount2): SC_ALL_RIDING on / off (the new mounts).
fn mount2(c: &Ctx, out: &Out, sd: u32) -> i32 {
    use crate::status::sc::{has_sce, sc_start, status_change_end};
    out.say(msg_txt(1362)); // NOTICE: If you crash with mount your LUA is outdated.
    if !has_sce(c, sd, SC_ALL_RIDING) {
        out.say(msg_txt(1363)); // You have mounted.
        if let Some(u) = unit::get(c, sd) {
            sc_start(c, None, &u, SC_ALL_RIDING, 10000, 1, crate::status::sc::INFINITE_TICK);
        }
    } else {
        out.say(msg_txt(1364)); // You have released your mount.
        status_change_end(c, sd, SC_ALL_RIDING, 0);
    }
    0
}

/// ACMD_FUNC(mount_peco): @mount: the job's mount (Rune Knight dragon by colour, Ranger warg, Mechanic Mado Gear by
/// type, else the Peco Peco).
fn mount_peco(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    use crate::db::jobs::{MAPID_MECHANIC, MAPID_RANGER, MAPID_RUNE_KNIGHT, MAPID_THIRDMASK};
    use crate::db::skills::ids::{KN_RIDING, RA_WUGRIDER, RK_DRAGONTRAINING, WH_HAWK_M};
    use crate::skilltree::pc_setoption;
    use crate::status::defs::*;
    if crate::atcmd_disguise::disguise_of(c, sd) != 0 {
        out.say(msg_txt(212)); // Cannot mount while in disguise.
        return -1;
    }
    let class = crate::status::data::sd_get(c, sd).map(|s| s.class_).unwrap_or(0);
    let option = unit::get(c, sd).map(|u| u.sc.option).unwrap_or(0);
    let skill = |id: u16| crate::skill::checkskill(c, sd, id);
    let warg_can_falcon: bool = crate::battle_config::get(crate::battle_config::Bc::WARG_CAN_FALCON) != 0; // conf/battle/skill.conf warg_can_falcon: no
    if class & MAPID_THIRDMASK == MAPID_RUNE_KNIGHT && skill(RK_DRAGONTRAINING) > 0 {
        if option & OPTION_DRAGON == 0 {
            let mut opt = OPTION_DRAGON1;
            if !message.is_empty() {
                opt = match crate::cstr::atoi(message) {
                    2 => OPTION_DRAGON2,
                    3 => OPTION_DRAGON3,
                    4 => OPTION_DRAGON4,
                    5 => OPTION_DRAGON5,
                    _ => OPTION_DRAGON1,
                };
            }
            out.say(msg_txt(1119)); // You have mounted your Dragon.
            pc_setoption(c, sd, option | opt, 0);
        } else {
            out.say(msg_txt(1120)); // You have released your Dragon.
            pc_setoption(c, sd, option & !OPTION_DRAGON, 0);
        }
        return 0;
    }
    if class & MAPID_THIRDMASK == MAPID_RANGER && skill(RA_WUGRIDER) > 0 && (option & OPTION_FALCON == 0 || (skill(WH_HAWK_M) > 0 || warg_can_falcon)) {
        if option & OPTION_WUGRIDER == 0 {
            out.say(msg_txt(1121)); // You have mounted your Warg.
            pc_setoption(c, sd, option | OPTION_WUGRIDER, 0);
        } else {
            out.say(msg_txt(1122)); // You have released your Warg.
            pc_setoption(c, sd, option & !OPTION_WUGRIDER, 0);
        }
        return 0;
    }
    if class & MAPID_THIRDMASK == MAPID_MECHANIC {
        if option & OPTION_MADOGEAR == 0 {
            const MADO_ROBOT: i32 = 0;
            const MADO_SUIT: i32 = 1;
            let mut kind = MADO_ROBOT;
            if !message.is_empty() {
                // strtol(message, nullptr, 10) into an int
                let tmp = crate::atcommand_atcmd::c_strtoul(message) as i64 as i32;
                kind = if tmp == MADO_ROBOT || tmp == MADO_SUIT { tmp } else { MADO_ROBOT };
            }
            out.say(msg_txt(1123)); // You have mounted your Mado Gear.
            crate::script::buildins::script2::looks::pc_setmadogear(c, sd, true, kind);
        } else {
            out.say(msg_txt(1124)); // You have released your Mado Gear.
            crate::script::buildins::script2::looks::pc_setmadogear(c, sd, false, 0);
        }
        return 0;
    }
    if option & OPTION_RIDING == 0 {
        // if actually no peco
        if skill(KN_RIDING) == 0 {
            out.say(msg_txt(213)); // You can not mount a Peco Peco with your current job.
            return -1;
        }
        pc_setoption(c, sd, option | OPTION_RIDING, 0);
        out.say(msg_txt(102)); // You have mounted a Peco Peco.
    } else {
        // Dismount
        pc_setoption(c, sd, option & !OPTION_RIDING, 0);
        out.say(msg_txt(214)); // You have released your Peco Peco.
    }
    0
}

/// ACMD_FUNC(displaystatus): @displaystatus <status type> {<flag> {<tick> {<val1> {<val2> {<val3>}}}}}:
/// clif_status_change with the values as given (flag 1, tick 0 when left out).
fn displaystatus(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let b = bytes(message);
    let mut s = Scan::new(&b);
    let mut v = [0i32; 6];
    let mut i = 0;
    for slot in v.iter_mut() {
        match d11(&mut s) {
            Some(x) => {
                *slot = x;
                i += 1;
            }
            None => break,
        }
    }
    if message.is_empty() || i < 1 {
        out.say(msg_txt(1009)); // Please enter a status type/flag (usage: @displaystatus <status type> <flag> <tick> {<val1> {<val2> {<val3>}}}).
        return -1;
    }
    let (kind, mut flag, mut tick) = (v[0], v[1], v[2]);
    if i < 2 {
        flag = 1;
    }
    if i < 3 {
        tick = 0;
    }
    if let Some(u) = unit::get(c, sd) {
        crate::status::clif::status_change(c, &u, kind, flag, tick as i64, v[3], v[4], v[5]);
    }
    0
}
