//! map/atcommand.cpp: the generic @commands that wave C left (atcmd agent, wave D). This file is the hub: `run` tries
//! each group file in turn, then its own commands; the commands of systems owned by another agent or a later phase
//! end in `atcmd_stubs.rs` (`not_ported`, called last in atcommand.rs `run`). Routed from atcommand.rs `run` by the
//! ACMD_FUNC name of the command table (atcommand_basecommands).
//!
//! Group files (declared here, one per area):
//! - `atcommand_info.rs`: information commands (@help, @commands, @stats, @exp, @rates, @mapinfo, @iteminfo …)
//! - `atcommand_looks.rs`: looks and presence (@dye, @hairstyle, @disguise, @size, @fakename, @mount …)
//! - `atcommand_inv.rs`: inventory, storage, cart and equipment (@delitem, @dropall, @stockall, @setcard …), family
//! - `atcommand_world.rs`: maps, weather, NPCs, jail (@snow, @skilloff, @cleanmap, @hidenpc, @jail …)
//! - `atcommand_accounts.rs`: the login / char server's account and character operations (@ban, @block, @email …)
//! - `atcommand_admin.rs`: groups, permissions, variables, battle flags, reloads (@adjgroup, @set, @reload… …)

use crate::clif;
use crate::unit::Unit;
use crate::world::Ctx;

#[path = "atcommand_info.rs"]
pub mod info;
#[path = "atcommand_looks.rs"]
pub mod looks;
#[path = "atcommand_inv.rs"]
pub mod inv;
#[path = "atcommand_world.rs"]
pub mod world;
#[path = "atcommand_accounts.rs"]
pub mod accounts;
#[path = "atcommand_admin.rs"]
pub mod admin;
#[path = "atcmd_stubs.rs"]
pub mod stubs;

/// Where a command's replies go (the invoker's fd in rAthena; None for a command run by a script without a player).
pub(crate) struct Out<'a> {
    pub c: &'a Ctx<'a>,
    pub fd: Option<&'a Unit>,
}

impl Out<'_> {
    /// clif_displaymessage(fd, text).
    pub fn say(&self, text: &str) {
        if let Some(u) = self.fd {
            clif::displaymessage(self.c, u, text);
        }
    }
}

/// Runs `func` when it is one of the atcmd agent's commands; None otherwise.
pub fn run(c: &Ctx, fd: Option<&Unit>, sd: u32, func: &str, command: &str, message: &str) -> Option<i32> {
    info::run(c, fd, sd, func, command, message)
        .or_else(|| looks::run(c, fd, sd, func, command, message))
        .or_else(|| inv::run(c, fd, sd, func, command, message))
        .or_else(|| world::run(c, fd, sd, func, command, message))
        .or_else(|| accounts::run(c, fd, sd, func, command, message))
        .or_else(|| admin::run(c, fd, sd, func, command, message))
        .or_else(|| crate::atcmd_disguise::run(c, fd, sd, func, command, message))
        .or_else(|| own(c, fd, sd, func, command, message))
}

/// The hub's own commands.
fn own(c: &Ctx, fd: Option<&Unit>, sd: u32, func: &str, command: &str, message: &str) -> Option<i32> {
    let out = Out { c, fd };
    let _ = command;
    Some(match func {
        "autoloot" => autoloot(c, &out, sd, message),
        "autolootitem" => autolootitem(c, &out, sd, message),
        "autoloottype" => autoloottype(c, &out, sd, message),
        "showexp" => showexp(c, &out, sd),
        "showzeny" => showzeny(c, &out, sd),
        "showdelay" => showdelay(c, &out, sd),
        "showrate" => showrate(&out),
        "monsterignore" => monsterignore(c, &out, sd),
        "ksprotection" => ksprotection(c, &out, sd, message),
        "feelreset" => feelreset(c, &out, sd),
        "hatereset" => hatereset(c, &out, sd),
        "kickall" => kickall(c, &out, sd),
        "auction" => auction(c, sd),
        _ => return None,
    })
}

// ---------------------------------------------------------------------------------------------- C library helpers

/// C atof / strtod (the "C" locale): leading white space, a sign, then a decimal number with an optional exponent,
/// a hexadecimal one ("0x…p…"), "inf" / "infinity" or "nan"; 0.0 when nothing converts.
pub(crate) fn c_atof(s: &str) -> f64 {
    let b = crate::net::wire::latin1(s);
    let mut p = 0;
    while p < b.len() && crate::cstr::is_space(b[p]) {
        p += 1;
    }
    let mut neg = false;
    if p < b.len() && (b[p] == b'+' || b[p] == b'-') {
        neg = b[p] == b'-';
        p += 1;
    }
    let rest = &b[p..];
    let lower: Vec<u8> = rest.iter().take(8).map(|c| c.to_ascii_lowercase()).collect();
    let v = if lower.starts_with(b"inf") {
        f64::INFINITY
    } else if lower.starts_with(b"nan") {
        f64::NAN
    } else if lower.len() >= 2 && lower[0] == b'0' && lower[1] == b'x' && rest.get(2).map(|c| c.is_ascii_hexdigit() || (*c == b'.' && rest.get(3).map(|d| d.is_ascii_hexdigit()).unwrap_or(false))).unwrap_or(false) {
        // hexadecimal floating point: mantissa digits, optional '.', optional binary exponent
        let mut q = 2;
        let mut m = 0f64;
        let mut scale = 0i32;
        let mut seen_dot = false;
        while q < rest.len() {
            let ch = rest[q];
            if ch == b'.' && !seen_dot {
                seen_dot = true;
            } else if let Some(d) = (ch as char).to_digit(16) {
                m = m * 16.0 + d as f64;
                if seen_dot {
                    scale -= 4;
                }
            } else {
                break;
            }
            q += 1;
        }
        if q < rest.len() && (rest[q] == b'p' || rest[q] == b'P') {
            let mut r = q + 1;
            let mut eneg = false;
            if r < rest.len() && (rest[r] == b'+' || rest[r] == b'-') {
                eneg = rest[r] == b'-';
                r += 1;
            }
            if r < rest.len() && rest[r].is_ascii_digit() {
                let mut e = 0i32;
                while r < rest.len() && rest[r].is_ascii_digit() {
                    e = e.saturating_mul(10).saturating_add((rest[r] - b'0') as i32);
                    r += 1;
                }
                scale = scale.saturating_add(if eneg { -e } else { e });
            }
        }
        m * 2f64.powi(scale)
    } else {
        // decimal: the longest prefix Rust's parser accepts in the C syntax [digits][.digits][e[sign]digits]
        let mut q = 0;
        let mut digits = 0;
        while q < rest.len() && rest[q].is_ascii_digit() {
            q += 1;
            digits += 1;
        }
        if q < rest.len() && rest[q] == b'.' {
            q += 1;
            while q < rest.len() && rest[q].is_ascii_digit() {
                q += 1;
                digits += 1;
            }
        }
        if digits == 0 {
            return 0.0;
        }
        let mut end = q;
        if q < rest.len() && (rest[q] == b'e' || rest[q] == b'E') {
            let mut r = q + 1;
            if r < rest.len() && (rest[r] == b'+' || rest[r] == b'-') {
                r += 1;
            }
            if r < rest.len() && rest[r].is_ascii_digit() {
                while r < rest.len() && rest[r].is_ascii_digit() {
                    r += 1;
                }
                end = r;
            }
        }
        let text: String = rest[..end].iter().map(|&c| c as char).collect();
        let text = if text.starts_with('.') { format!("0{text}") } else { text };
        let text = if text.ends_with('.') { format!("{text}0") } else { text };
        text.replace(".e", ".0e").replace(".E", ".0E").parse::<f64>().unwrap_or(0.0)
    };
    if neg {
        -v
    } else {
        v
    }
}

/// A C cast of a double to int32 on x86-64 (cvttsd2si): truncation toward zero; NaN and values out of range give
/// INT_MIN (the "integer indefinite" value) instead of Rust's saturation.
pub(crate) fn c_double_to_i32(v: f64) -> i32 {
    if v.is_nan() || v >= 2147483648.0 || v <= -2147483649.0 {
        i32::MIN
    } else {
        v.trunc() as i32
    }
}

/// C strtoul(s, nullptr, 10) (unsigned long is 64-bit on the oracle's Linux): leading white space, a sign (a minus
/// negates in unsigned arithmetic), decimal digits; ULONG_MAX on overflow; 0 when nothing converts.
pub(crate) fn c_strtoul(s: &str) -> u64 {
    let b = crate::net::wire::latin1(s);
    let mut p = 0;
    while p < b.len() && crate::cstr::is_space(b[p]) {
        p += 1;
    }
    let mut neg = false;
    if p < b.len() && (b[p] == b'+' || b[p] == b'-') {
        neg = b[p] == b'-';
        p += 1;
    }
    let mut v: u64 = 0;
    let mut overflow = false;
    let mut any = false;
    while p < b.len() && b[p].is_ascii_digit() {
        any = true;
        match v.checked_mul(10).and_then(|x| x.checked_add((b[p] - b'0') as u64)) {
            Some(x) => v = x,
            None => overflow = true,
        }
        p += 1;
    }
    if !any {
        return 0;
    }
    if overflow {
        return u64::MAX;
    }
    if neg {
        v.wrapping_neg()
    } else {
        v
    }
}

// ---------------------------------------------------------------------------------------------- autoloot

/// ACMD_FUNC(autoloot): @autoloot {<rate>}: sd->state.autoloot = the drop rate (percent × 100, 0.01% steps) up to
/// which drops go straight to the player (no rate: toggles 0 / 100%).
fn autoloot(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let mut m = crate::pc_misc::pcm(c, sd);
    let mut rate = if message.is_empty() {
        // autoloot command without value
        if m.autoloot != 0 {
            0
        } else {
            10000
        }
    } else {
        let drate = c_atof(message);
        c_double_to_i32(drate * 100.0)
    };
    rate = rate.clamp(0, 10000);
    m.autoloot = rate;
    crate::pc_misc::put_pcm(c, &m);
    if rate != 0 {
        // Autolooting items with drop rates of %0.02f%% and below.
        let pct = crate::mob::cmd::c_fixed2(rate as f64 / 100.0);
        out.say(&crate::data::format_msg(crate::data::msg_txt(1187), &[&pct]));
    } else {
        out.say(crate::data::msg_txt(1188)); // Autoloot is now off.
    }
    0
}

/// ACMD_FUNC(autolootitem): @alootid {+|-}<item name or ID> / reset / (nothing: help and the list).
fn autolootitem(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    use crate::data::{format_msg, msg_txt};
    use crate::db::items;
    let mut action = 3; // 1=add, 2=remove, 3=help+list (default), 4=reset
    let mut message = message;
    if !message.is_empty() {
        if let Some(rest) = message.strip_prefix('+') {
            message = rest;
            action = 1;
        } else if let Some(rest) = message.strip_prefix('-') {
            message = rest;
            action = 2;
        } else if message == "reset" {
            action = 4;
        }
    }
    let mut item = None;
    if action < 3 {
        // add or remove: item_db.find(strtoul(message)) (t_itemid: 32 bits), then item_db.searchname(message)
        item = items::itemdb_exists(c_strtoul(message) as u32).or_else(|| items::itemdb_searchname(message));
        if item.is_none() {
            // No items founds in the DB with Id or Name
            out.say(msg_txt(1189)); // Item not found.
            return -1;
        }
    }
    let mut r = get_state(c, sd);
    match action {
        1 => {
            let it = item.unwrap_or_else(|| items::itemdb_search(0));
            if r.autolootid.contains(&it.nameid) {
                out.say(msg_txt(1190)); // You're already autolooting this item.
                return -1;
            }
            let Some(i) = r.autolootid.iter().position(|&id| id == 0) else {
                out.say(msg_txt(1191)); // Your autolootitem list is full. Remove some items first with @autolootid -<item name or ID>.
                return -1;
            };
            r.autolootid[i] = it.nameid; // Autoloot Activated
            out.say(&format_msg(msg_txt(1192), &[&it.name, &crate::mob::cmd::create_item_link(it), &it.nameid])); // Autolooting item: '%s'/'%s' {%u}
            r.autolooting = true;
        }
        2 => {
            let it = item.unwrap_or_else(|| items::itemdb_search(0));
            let Some(i) = r.autolootid.iter().position(|&id| id == it.nameid) else {
                out.say(msg_txt(1193)); // You're currently not autolooting this item.
                return -1;
            };
            r.autolootid[i] = 0;
            out.say(&format_msg(msg_txt(1194), &[&it.name, &crate::mob::cmd::create_item_link(it), &it.nameid])); // Removed item: '%s'/'%s' {%u} from your autolootitem list.
            if r.autolootid.iter().all(|&id| id == 0) {
                r.autolooting = false;
            }
        }
        3 => {
            out.say(&format_msg(msg_txt(1195), &[&crate::atcmd_pc::AUTOLOOTITEM_SIZE])); // You can have %d items on your autolootitem list.
            out.say(msg_txt(1196)); // To add an item to the list, use "@alootid +<item name or ID>". To remove an item, use "@alootid -<item name or ID>".
            out.say(msg_txt(1197)); // "@alootid reset" will clear your autolootitem list.
            if r.autolootid.iter().all(|&id| id == 0) {
                out.say(msg_txt(1198)); // Your autolootitem list is empty.
            } else {
                out.say(msg_txt(1199)); // Items on your autolootitem list:
                for &id in r.autolootid.iter().filter(|&&id| id != 0) {
                    let Some(it) = items::itemdb_exists(id) else {
                        log::debug!("Non-existant item {id} on autolootitem list (account_id: {sd})");
                        continue;
                    };
                    out.say(&format!("'{}'/'{}' {{{}}}", it.name, crate::mob::cmd::create_item_link(it), it.nameid));
                }
            }
        }
        _ => {
            r.autolootid = vec![0; crate::atcmd_pc::AUTOLOOTITEM_SIZE];
            out.say(msg_txt(1200)); // Your autolootitem list has been reset.
            r.autolooting = false;
        }
    }
    crate::atcmd_pc::put(c, &r);
    0
}

/// The player's @command state row with a full-size @alootid list.
fn get_state(c: &Ctx, sd: u32) -> crate::atcmd_pc::PcAtcmd {
    let mut r = crate::atcmd_pc::get(c, sd);
    r.autolootid.resize(crate::atcmd_pc::AUTOLOOTITEM_SIZE, 0);
    r
}

/// ACMD_FUNC(autoloottype): @aloottype {+|-}<type name or ID> / reset / (nothing: help and the list). rAthena quirk
/// kept: each name test is `strncmp(name, 3 or 4 letters) == 0 || atoi(message) == N`, so any name that is not a
/// number (atoi 0) picks IT_HEALING.
fn autoloottype(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    use crate::data::{format_msg, msg_txt};
    use crate::db::items::*;
    const ITEM_MAX: u16 = 1533;
    let mut action = 3; // 1=add, 2=remove, 3=help+list (default), 4=reset
    let mut message = message;
    if !message.is_empty() {
        if let Some(rest) = message.strip_prefix('+') {
            message = rest;
            action = 1;
        } else if let Some(rest) = message.strip_prefix('-') {
            message = rest;
            action = 2;
        } else if message == "reset" {
            action = 4;
        }
    }
    let mut kind = IT_UNKNOWN;
    if action < 3 {
        let pre = |p: &str, n: usize| {
            let a = message.as_bytes();
            let b = p.as_bytes();
            (0..n).all(|i| a.get(i).copied().unwrap_or(0) == b.get(i).copied().unwrap_or(0)) // strncmp == 0
        };
        let n = crate::cstr::atoi(message);
        kind = if pre("healing", 3) || n == 0 {
            IT_HEALING
        } else if pre("usable", 3) || n == 2 {
            IT_USABLE
        } else if pre("etc", 3) || n == 3 {
            IT_ETC
        } else if pre("armor", 3) || n == 4 {
            IT_ARMOR
        } else if pre("weapon", 3) || n == 5 {
            IT_WEAPON
        } else if pre("card", 3) || n == 6 {
            IT_CARD
        } else if pre("petegg", 4) || n == 7 {
            IT_PETEGG
        } else if pre("petarmor", 4) || n == 8 {
            IT_PETARMOR
        } else if pre("ammo", 3) || n == 10 {
            IT_AMMO
        } else {
            out.say(msg_txt(1480)); // Item type not found.
            return -1;
        };
    }
    let mut r = get_state(c, sd);
    let bit = 1u16.checked_shl(kind as u32).unwrap_or(0);
    match action {
        1 => {
            if r.autoloottype & bit != 0 {
                out.say(msg_txt(1481)); // You're already autolooting this item type.
                return -1;
            }
            if r.autoloottype == ITEM_MAX {
                out.say(msg_txt(1482)); // Your autoloottype list has all item types. You can remove some items with @autoloottype -<type name or ID>.
                return -1;
            }
            r.autoloottype |= bit; // Stores the type
            out.say(&format_msg(msg_txt(1483), &[&itemdb_typename(kind), &kind])); // Autolooting item type: '%s' {%u}
        }
        2 => {
            if r.autoloottype & bit == 0 {
                out.say(msg_txt(1484)); // You're currently not autolooting this item type.
                return -1;
            }
            r.autoloottype &= !bit;
            out.say(&format_msg(msg_txt(1485), &[&itemdb_typename(kind), &kind])); // Removed item type: '%s' {%u} from your autoloottype list.
        }
        3 => {
            out.say(msg_txt(1486)); // To add an item type to the list, use "@aloottype +<type name or ID>". …
            out.say(msg_txt(1487)); // Type List: healing = 0, usable = 2, etc = 3, armor = 4, weapon = 5, card = 6, …
            out.say(msg_txt(1488)); // "@aloottype reset" will clear your autoloottype list.
            if r.autoloottype == 0 {
                out.say(msg_txt(1489)); // Your autoloottype list is empty.
            } else {
                out.say(msg_txt(1490)); // Item types on your autoloottype list:
                for i in 0..IT_MAX {
                    if (r.autoloottype as u32) & (1u32 << i) != 0 {
                        out.say(&format!("  '{}' {{{}}}", itemdb_typename(i), i));
                    }
                }
            }
        }
        _ => {
            r.autoloottype = 0;
            out.say(msg_txt(1491)); // Your autoloottype list has been reset.
        }
    }
    crate::atcmd_pc::put(c, &r);
    0
}

// ---------------------------------------------------------------------------------------------- message toggles

/// ACMD_FUNC(showexp): sd->state.showexp (pc_gainexp shows the gained experience).
fn showexp(c: &Ctx, out: &Out, sd: u32) -> i32 {
    let mut r = get_state(c, sd);
    r.showexp = !r.showexp;
    crate::atcmd_pc::put(c, &r);
    out.say(crate::data::msg_txt(if r.showexp { 1317 } else { 1316 })); // Gained exp is now shown. / will not be shown.
    0
}

/// ACMD_FUNC(showzeny): sd->state.showzeny (pc_getzeny shows the gained zeny).
fn showzeny(c: &Ctx, out: &Out, sd: u32) -> i32 {
    let mut r = get_state(c, sd);
    r.showzeny = !r.showzeny;
    crate::atcmd_pc::put(c, &r);
    out.say(crate::data::msg_txt(if r.showzeny { 1319 } else { 1318 })); // Gained zeny is now shown. / will not be shown.
    0
}

/// ACMD_FUNC(showdelay): sd->state.showdelay (clif_skill_fail shows USESKILL_FAIL_SKILLINTERVAL).
fn showdelay(c: &Ctx, out: &Out, sd: u32) -> i32 {
    let mut r = get_state(c, sd);
    r.showdelay = !r.showdelay;
    crate::atcmd_pc::put(c, &r);
    out.say(crate::data::msg_txt(if r.showdelay { 1321 } else { 1320 })); // Skill delay failures are now shown. / will not be shown.
    0
}

/// ACMD_FUNC(showrate): VIP_ENABLE is not defined (config/core.hpp), so only "disabled".
fn showrate(out: &Out) -> i32 {
    out.say(crate::data::msg_txt(774)); // This command is disabled via configuration.
    -1
}

/// ACMD_FUNC(monsterignore): PCBLOCK_IMMUNE in sd->state.block_action.
fn monsterignore(c: &Ctx, out: &Out, sd: u32) -> i32 {
    let Some(mut p) = crate::pc::get(c, sd) else { return -1 };
    if p.block_action & crate::pc::PCBLOCK_IMMUNE != 0 {
        p.block_action &= !crate::pc::PCBLOCK_IMMUNE;
        crate::pc::put(c, &p);
        out.say(crate::data::msg_txt(1306)); // Returned to normal state.
    } else {
        p.block_action |= crate::pc::PCBLOCK_IMMUNE;
        crate::pc::put(c, &p);
        out.say(crate::data::msg_txt(1305)); // You are now immune to attacks.
    }
    0
}

/// ACMD_FUNC(ksprotection): @noks {self|party|guild}: sd->state.noks (mob_ksprotected reads it; ksprotection is 0 in
/// conf/battle/monster.conf, so it protects nothing on this configuration, as on the oracle).
fn ksprotection(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    use crate::data::msg_txt;
    let mut r = get_state(c, sd);
    if r.noks != 0 {
        r.noks = 0;
        out.say(msg_txt(1325)); // [ K.S Protection Inactive ]
    } else if message.is_empty() || message.eq_ignore_ascii_case("party") {
        // Default is Party
        r.noks = 2;
        out.say(msg_txt(1326)); // [ K.S Protection Active - Option: Party ]
    } else if message.eq_ignore_ascii_case("self") {
        r.noks = 1;
        out.say(msg_txt(1327)); // [ K.S Protection Active - Option: Self ]
    } else if message.eq_ignore_ascii_case("guild") {
        r.noks = 3;
        out.say(msg_txt(1328)); // [ K.S Protection Active - Option: Guild ]
    } else {
        out.say(msg_txt(1329)); // Usage: @noks <self|party|guild>
    }
    crate::atcmd_pc::put(c, &r);
    0
}

/// (sd->class_ & MAPID_SECONDMASK) == MAPID_STAR_GLADIATOR.
fn is_star_gladiator(c: &Ctx, sd: u32) -> bool {
    use crate::db::jobs::{MAPID_SECONDMASK, MAPID_STAR_GLADIATOR};
    crate::status::data::sd_get(c, sd).map(|s| s.class_ & MAPID_SECONDMASK == MAPID_STAR_GLADIATOR).unwrap_or(false)
}

/// ACMD_FUNC(feelreset): pc_resetfeel for a Star Gladiator.
fn feelreset(c: &Ctx, out: &Out, sd: u32) -> i32 {
    if !is_star_gladiator(c, sd) {
        out.say(crate::data::msg_txt(35)); // You can't use this command with this class.
        return -1;
    }
    crate::skilltree::pc_resetfeel(c, sd);
    out.say(crate::data::msg_txt(1324)); // Reset 'Feeling' maps.
    0
}

/// ACMD_FUNC(hatereset): pc_resethate for a Star Gladiator.
fn hatereset(c: &Ctx, out: &Out, sd: u32) -> i32 {
    if !is_star_gladiator(c, sd) {
        out.say(crate::data::msg_txt(35)); // You can't use this command with this class.
        return -1;
    }
    crate::skilltree::pc_resethate(c, sd);
    out.say(crate::data::msg_txt(1515)); // Reset 'Hatred' monsters.
    0
}

/// ACMD_FUNC(kickall): every other player of a group level not above ours is kicked (clif_GM_kick without a GM, so
/// no kick ack), in mapit_getallusers order.
fn kickall(c: &Ctx, out: &Out, sd: u32) -> i32 {
    use crate::pc::pc as _;
    use spacetimedb::Table as _;
    let mut ids: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
    ids.sort_by_key(|&id| crate::party::idb_order(id));
    let my_level = crate::pc::group_level(c, sd);
    for pid in ids {
        if my_level >= crate::pc::group_level(c, pid) {
            // you can kick only lower or same gm level
            if pid != sd {
                crate::session::clif_gm_kick(c, None, pid);
            }
        }
    }
    out.say(crate::data::msg_txt(195)); // All players have been kicked!
    0
}

/// ACMD_FUNC(auction) (atcommand.cpp:9584-9596): with feature.auction off (conf/battle/feature.conf; auction.rs
/// `feature_auction` is the value in force) the red "disabled" message (clif_messagecolor(sd, color_table[COLOR_RED],
/// msg_txt(517), false, SELF); color_table holds BGR: red = 0x0000FF), else the auction window.
fn auction(c: &Ctx, sd: u32) -> i32 {
    if crate::auction::feature_auction(c) == 0 {
        if let Some(u) = crate::unit::get(c, sd) {
            let msg = crate::net::wire::latin1(crate::data::msg_txt(517));
            clif::messagecolor_target(c, &u, 0x0000FF, &msg, false, crate::clif::Target::SelfOnly, None);
        }
        return 0;
    }
    crate::auction::clif_auction_openwindow(c, sd);
    0
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn atof_follows_c() {
        assert_eq!(c_atof("25.5"), 25.5);
        assert_eq!(c_atof("  -3"), -3.0);
        assert_eq!(c_atof("1e10"), 1e10);
        assert_eq!(c_atof(".5x"), 0.5);
        assert_eq!(c_atof("5."), 5.0);
        assert_eq!(c_atof("abc"), 0.0);
        assert_eq!(c_atof("1e"), 1.0);
        assert_eq!(c_atof("0x10"), 16.0);
        assert!(c_atof("inf").is_infinite());
        assert!(c_atof("nan").is_nan());
    }

    #[test]
    fn double_to_int_like_x86() {
        assert_eq!(c_double_to_i32(2550.0), 2550);
        assert_eq!(c_double_to_i32(-0.4), 0);
        assert_eq!(c_double_to_i32(1e12), i32::MIN);
        assert_eq!(c_double_to_i32(f64::NAN), i32::MIN);
    }

    #[test]
    fn strtoul_follows_c() {
        assert_eq!(c_strtoul("909"), 909);
        assert_eq!(c_strtoul(" +12abc"), 12);
        assert_eq!(c_strtoul("-1"), u64::MAX);
        assert_eq!(c_strtoul("Jellopy"), 0);
        assert_eq!(c_strtoul("99999999999999999999999"), u64::MAX);
    }
}
