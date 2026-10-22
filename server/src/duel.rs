//! map/duel.cpp (duels: create, invite, accept, reject, leave, the info list, the time limit) with the duel parts of
//! clif.cpp (clif_send's DUEL / DUEL_WOS targets, clif_disp_message) and atcommand.cpp (@duel, @invite, @accept,
//! @reject, @leave).
//!
//! duel_list (duel id → members, invites, player limit) is the [`Duel`] table; sd->duel_group / sd->duel_invite are
//! the [`PcDuel`] row of an online character. Deliberately different: duel.cpp's `static size_t lastID` starts again
//! at every map-server start, and do_init_duel empties duel_list; the module's players (and their duels) outlive what
//! the module calls a restart (a world build), so the list is kept and the counter goes on.

use spacetimedb::{table, Table};

use crate::clif::{self, Target};
use crate::cstr::Scan;
use crate::data::{format_msg, msg_txt};
use crate::pc::{self, pc as _};
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// conf/battle/misc.conf `duel_allow_pvp: no`, `duel_allow_gvg: no`, `duel_allow_teleport: no`,
/// `duel_autoleave_when_die: yes`, `duel_time_interval: 60`, `duel_only_on_same_map: no`.
pub fn duel_allow_pvp() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DUEL_ALLOW_PVP) != 0
}
pub fn duel_allow_gvg() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DUEL_ALLOW_GVG) != 0
}
pub fn duel_allow_teleport() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DUEL_ALLOW_TELEPORT) != 0
}
pub fn duel_autoleave_when_die() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DUEL_AUTOLEAVE_WHEN_DIE) != 0
}
pub fn duel_only_on_same_map() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DUEL_ONLY_ON_SAME_MAP) != 0
}
/// conf/battle/misc.conf `duel_time_interval: 60`: battle_config.duel_time_interval (battle.cpp:8597, 0 to INT_MAX),
/// read at run time (battle_config/mod.rs).
pub fn duel_time_interval() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::DUEL_TIME_INTERVAL)
}
/// clif.hpp e_broadcast_flags BC_BLUE.
const BC_BLUE: i32 = 0x10;

/// duel.hpp struct duel.
#[table(accessor = duel)]
#[derive(Clone, Debug)]
pub struct Duel {
    #[primary_key]
    pub did: u32,
    pub members_count: i32,
    pub invites_count: i32,
    pub max_players_limit: i32,
}

/// sd->duel_group / sd->duel_invite.
#[table(accessor = pc_duel)]
#[derive(Clone, Debug)]
pub struct PcDuel {
    #[primary_key]
    pub account_id: u32,
    pub duel_group: u32,
    pub duel_invite: u32,
}

fn pd(c: &Ctx, aid: u32) -> PcDuel {
    c.db().pc_duel().account_id().find(aid).unwrap_or(PcDuel { account_id: aid, duel_group: 0, duel_invite: 0 })
}

fn put_pd(c: &Ctx, r: PcDuel) {
    if c.db().pc_duel().account_id().find(r.account_id).is_some() {
        c.db().pc_duel().account_id().update(r);
    } else {
        c.db().pc_duel().insert(r);
    }
}

/// sd->duel_group (0: not in a duel).
pub fn duel_group(c: &Ctx, aid: u32) -> u32 {
    c.db().pc_duel().account_id().find(aid).map(|r| r.duel_group).unwrap_or(0)
}

/// sd->duel_invite (0: no invitation).
pub fn duel_invite_of(c: &Ctx, aid: u32) -> u32 {
    c.db().pc_duel().account_id().find(aid).map(|r| r.duel_invite).unwrap_or(0)
}

fn set_invite(c: &Ctx, aid: u32, did: u32) {
    let mut r = pd(c, aid);
    r.duel_invite = did;
    put_pd(c, r);
}

/// duel.cpp duel_exist.
pub fn duel_exist(c: &Ctx, did: u32) -> bool {
    c.db().duel().did().find(did).is_some()
}

fn get(c: &Ctx, did: u32) -> Option<Duel> {
    c.db().duel().did().find(did)
}

/// `duel_list[did]`: std::unordered_map's operator[] makes a zeroed entry when there is none.
fn get_or_make(c: &Ctx, did: u32) -> Duel {
    get(c, did).unwrap_or(Duel { did, members_count: 0, invites_count: 0, max_players_limit: 0 })
}

fn put(c: &Ctx, d: Duel) {
    if c.db().duel().did().find(d.did).is_some() {
        c.db().duel().did().update(d);
    } else {
        c.db().duel().insert(d);
    }
}

/// duel.cpp duel_counttotal.
pub fn duel_counttotal(c: &Ctx) -> usize {
    c.db().duel().count() as usize
}

/// duel.cpp duel_countactives: duels with more than one member.
pub fn duel_countactives(c: &Ctx) -> usize {
    c.db().duel().iter().filter(|d| d.members_count > 1).count()
}

/// The players in the world in pc_db's iteration order (map_foreachpc / mapit_getallusers).
fn all_pcs(c: &Ctx) -> Vec<u32> {
    let mut v: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
    v.sort_by_key(|&aid| crate::party::idb_order(aid));
    v
}

/// PC_LAST_DUEL_TIME's clock: localtime's `tm_mday*24*60 + tm_hour*60 + tm_min` (the oracle's time zone).
fn clock_minutes(c: &Ctx) -> i64 {
    let t = crate::script::npc_run::localtime(c.tick().div_euclid(1000));
    t.mday as i64 * 24 * 60 + t.hour as i64 * 60 + t.min as i64
}

/// duel.cpp duel_savetime: the current time in PC_LAST_DUEL_TIME.
pub fn duel_savetime(c: &Ctx, aid: u32) {
    crate::status::write_char_reg_num(c, aid, "PC_LAST_DUEL_TIME", clock_minutes(c));
}

/// duel.cpp duel_checktime: whether duel_time_interval minutes have gone by since the last duel.
pub fn duel_checktime(c: &Ctx, aid: u32) -> bool {
    let diff = clock_minutes(c) - crate::status::read_char_reg_num(c, aid, "PC_LAST_DUEL_TIME");
    !(diff >= 0 && diff < duel_time_interval() as i64)
}

/// duel.cpp duel_check_player_limit.
pub fn duel_check_player_limit(d: &Duel) -> bool {
    !(d.max_players_limit > 0 && d.members_count >= d.max_players_limit)
}

// ---------------------------------------------------------------------------------------------- clif

/// clif.cpp clif_send with DUEL / DUEL_WOS: every player of `sd`'s duel group (`wos`: but `sd`) whose client is
/// there; nothing when `sd` is in no duel ("Invalid usage").
fn send_duel(c: &Ctx, buf: &[u8], sd: &Unit, wos: bool) {
    let group = duel_group(c, sd.id);
    if group == 0 {
        return;
    }
    for aid in all_pcs(c) {
        if wos && aid == sd.id {
            continue;
        }
        if duel_group(c, aid) == group {
            if let Some(id) = pc::identity_of(c, aid) {
                c.send(id, buf);
            }
        }
    }
}

/// clif.cpp clif_disp_message: ZC_GUILD_CHAT (0x017f) "using the guild-chat colors", `len` characters (at most 251).
fn clif_disp_message(c: &Ctx, src: &Unit, mes: &str, wos: bool) {
    let b = crate::net::wire::latin1(mes);
    let mut len = b.len();
    if len == 0 {
        return;
    }
    if len > 256 - 5 {
        log::warn!("clif_disp_message: Truncated message '{mes}' (len={len}, max={}, aid={}).", 256 - 5, src.id);
        len = 256 - 5;
    }
    let mut w = Vec::with_capacity(len + 5);
    w.extend_from_slice(&0x017fu16.to_le_bytes());
    w.extend_from_slice(&((len + 5) as u16).to_le_bytes());
    w.extend_from_slice(&b[..len]);
    w.push(0);
    send_duel(c, &w, src, wos);
}

/// clif_messagecolor(sd, color_table[COLOR_LIGHT_GREEN], output, false, SELF).
fn green(c: &Ctx, sd: &Unit, mes: &str) {
    clif::messagecolor_target(c, sd, crate::social::COLOR_LIGHT_GREEN, &crate::net::wire::latin1(mes), false, Target::SelfOnly, None);
}

// ---------------------------------------------------------------------------------------------- duel.cpp

/// duel.cpp duel_showinfo_sub for every player: its line when it is in `ssd`'s duel.
fn duel_showinfo(c: &Ctx, did: u32, sd: &Unit) {
    let Some(d) = get(c, did) else { return };
    let total = duel_counttotal(c);
    let output = if d.max_players_limit > 0 {
        //" -- Duels: %d/%d, Members: %d/%d, Max players: %d --"
        format_msg(msg_txt(370), &[&did, &total, &d.members_count, &(d.members_count + d.invites_count), &d.max_players_limit])
    } else {
        //" -- Duels: %d/%d, Members: %d/%d --"
        format_msg(msg_txt(371), &[&did, &total, &d.members_count, &(d.members_count + d.invites_count)])
    };
    green(c, sd, &output);
    let group = duel_group(c, sd.id);
    let mut p = 0;
    for aid in all_pcs(c) {
        if duel_group(c, aid) != group {
            continue;
        }
        p += 1;
        let name = unit::get(c, aid).map(|u| u.name).unwrap_or_default();
        green(c, sd, &format!("      {p}. {name}"));
    }
}

/// duel.cpp duel_set: moves the player to the duel `did` (0: out of it). As rAthena, the warp flags end cleared, also
/// when this runs in the middle of a warp (unit_remove_map's duel_leave).
fn duel_set(c: &Ctx, did: u32, aid: u32) {
    if let Some(mut p) = pc::get(c, aid) {
        p.changemap = true;
        p.warping = true;
        pc::put(c, &p);
    }
    // As you move to a different plane, ground effects need to be cleared
    crate::skillunit::skill_clear_unitgroup(c, aid);
    if let Some(sd) = unit::get(c, aid) {
        crate::skillunit::skill_unit_move(c, &sd, c.tick(), 2);
    }
    if let Some(sd) = unit::get(c, aid) {
        crate::skill::timerskill::skill_cleartimerskill(c, &sd);
    }
    let mut r = pd(c, aid);
    r.duel_group = did;
    put_pd(c, r);
    if let Some(sd) = unit::get(c, aid) {
        crate::skillunit::skill_unit_move(c, &sd, c.tick(), 3);
    }
    if let Some(mut p) = pc::get(c, aid) {
        p.changemap = false;
        p.warping = false;
        pc::put(c, &p);
    }
}

/// duel.cpp duel_create: a new duel for the player (`maxpl` 0 = no limit); returns its id.
pub fn duel_create(c: &Ctx, aid: u32, maxpl: u32) -> u32 {
    let last = crate::ids::next(c.rc, "duel_id", 1);
    let mut d = get_or_make(c, last);
    d.members_count += 1;
    d.invites_count = 0;
    d.max_players_limit = maxpl as i32;
    put(c, d);
    duel_set(c, last, aid);
    if let Some(sd) = unit::get(c, aid) {
        green(c, &sd, msg_txt(372)); // " -- Duel has been created (@invite/@leave) --"
        clif::map_property(c, &sd, clif::MAPPROPERTY_FREEPVPZONE, Target::SelfOnly);
    }
    //clif_misceffect2(&sd->bl, 159);
    last
}

/// duel.cpp duel_invite: `aid` invites `target` into the duel `did`.
pub fn duel_invite(c: &Ctx, did: u32, aid: u32, target: u32) -> bool {
    if !duel_exist(c, did) {
        return false;
    }
    set_invite(c, target, did);
    let mut d = get_or_make(c, did);
    d.invites_count += 1;
    put(c, d);
    let (Some(sd), Some(tsd)) = (unit::get(c, aid), unit::get(c, target)) else { return true };
    // " -- Player %s invites %s to duel --"
    let output = format_msg(msg_txt(373), &[&sd.name, &tsd.name]);
    clif_disp_message(c, &sd, &output, true);
    // "Blue -- Player %s invites you to PVP duel (@accept/@reject) --"
    let output = format_msg(msg_txt(374), &[&sd.name]);
    let b = crate::net::wire::latin1(&output);
    clif::broadcast(c, Some(&tsd), &b, b.len() + 1, BC_BLUE, Target::SelfOnly);
    true
}

/// duel.cpp duel_leave: the player leaves the duel `did` (the last one out ends it and drops its invitations).
pub fn duel_leave(c: &Ctx, did: u32, aid: u32) -> bool {
    let Some(mut d) = get(c, did) else { return false };
    d.members_count -= 1;
    if d.members_count == 0 {
        // map_foreachpc(duel_leave_sub, did): the invitations to it go
        for pl in all_pcs(c) {
            if duel_invite_of(c, pl) == did {
                set_invite(c, pl, 0);
            }
        }
        c.db().duel().did().delete(did);
    } else {
        put(c, d);
    }
    duel_set(c, 0, aid);
    duel_savetime(c, aid);
    if let Some(sd) = unit::get(c, aid) {
        // " <- Player %s has left duel --" (to the duel the player is no longer in: nobody)
        let output = format_msg(msg_txt(375), &[&sd.name]);
        clif_disp_message(c, &sd, &output, true);
        clif::map_property(c, &sd, clif::MAPPROPERTY_NOTHING, Target::SelfOnly);
    }
    true
}

/// duel.cpp duel_accept: the player joins the duel it is invited to.
pub fn duel_accept(c: &Ctx, did: u32, aid: u32) -> bool {
    let Some(mut d) = get(c, did) else { return false };
    d.members_count += 1;
    d.invites_count -= 1;
    put(c, d);
    let invite = duel_invite_of(c, aid);
    duel_set(c, invite, aid);
    set_invite(c, aid, 0);
    if let Some(sd) = unit::get(c, aid) {
        // " -> Player %s has accepted duel --"
        let output = format_msg(msg_txt(376), &[&sd.name]);
        clif_disp_message(c, &sd, &output, true);
        clif::map_property(c, &sd, clif::MAPPROPERTY_FREEPVPZONE, Target::SelfOnly);
    }
    //clif_misceffect2(&sd->bl, 159);
    true
}

/// duel.cpp duel_reject: the player declines the invitation to `did`.
pub fn duel_reject(c: &Ctx, did: u32, aid: u32) -> bool {
    let Some(mut d) = get(c, did) else { return false };
    d.invites_count -= 1;
    put(c, d);
    set_invite(c, aid, 0);
    if let Some(sd) = unit::get(c, aid) {
        // " -- Player %s has rejected duel --" (to the rejecting player's own duel group: none, so nobody)
        let output = format_msg(msg_txt(377), &[&sd.name]);
        clif_disp_message(c, &sd, &output, true);
    }
    true
}

// ---------------------------------------------------------------------------------------------- hooks of other systems

/// unit.cpp unit_remove_map (BL_PC): `if (sd->duel_group > 0) duel_leave(sd->duel_group, sd)` — every warp.
pub fn unit_remove_map(c: &Ctx, aid: u32) {
    let g = duel_group(c, aid);
    if g > 0 {
        duel_leave(c, g, aid);
    }
}

/// unit.cpp unit_free (BL_PC): `if (sd->duel_invite > 0) duel_reject(sd->duel_invite, sd)`; then the state goes
/// with the session.
pub fn unit_free(c: &Ctx, aid: u32) {
    let inv = duel_invite_of(c, aid);
    if inv > 0 {
        duel_reject(c, inv, aid);
    }
    c.db().pc_duel().account_id().delete(aid);
}

/// The session's duel state goes without the unit_free steps (a character that never finished logging in).
pub fn drop_state(c: &Ctx, aid: u32) {
    c.db().pc_duel().account_id().delete(aid);
}

/// pc.cpp pc_dead: "Leave duel if you die" (duel_autoleave_when_die).
pub fn pc_dead(c: &Ctx, aid: u32) {
    if duel_autoleave_when_die() {
        let g = duel_group(c, aid);
        if g > 0 {
            duel_leave(c, g, aid);
        }
        let inv = duel_invite_of(c, aid);
        if inv > 0 {
            duel_reject(c, inv, aid);
        }
    }
}

/// battle.cpp battle_check_target's duel rule for a player source that is not the target and not a @killer: in a
/// duel (outside PvP / GvG maps unless duel_allow_pvp / _gvg), only the duel's players may be targeted, and only as
/// enemies. Some(result) when the rule decides (`(BCT_ENEMY&flag)?1:-1`, or 0), None when it does not apply.
pub fn battle_check_target(c: &Ctx, s_aid: u32, t_bl: &Unit, m: u16, flag_enemy: bool) -> Option<i32> {
    let g = duel_group(c, s_aid);
    if g == 0 {
        return None;
    }
    let pvp = crate::npc::mapflag(c, m, crate::npc::MF_PVP);
    let gvg = crate::npc::mapflag_gvg(c, m);
    if (!duel_allow_pvp() && pvp) || (!duel_allow_gvg() && gvg) {
        return None;
    }
    if t_bl.kind == crate::map::BL_PC && duel_group(c, t_bl.id) == g {
        Some(if flag_enemy { 1 } else { -1 }) // Duel targets can ONLY be your enemy, nothing else.
    } else {
        Some(0) // You can't target anything out of your duel
    }
}

/// pc.cpp pc_isUseitem's duel rule for the teleport / return item groups: `if (sd->duel_group &&
/// !battle_config.duel_allow_teleport)` msg 663 and the item cannot be used.
pub fn item_refused(c: &Ctx, sd: &Unit) -> bool {
    if duel_group(c, sd.id) != 0 && !duel_allow_teleport() {
        clif::displaymessage(c, sd, msg_txt(663)); // Duel: Can't use this item in duel.
        return true;
    }
    false
}

// ---------------------------------------------------------------------------------------------- @commands

/// `%23[^\n]` of a message.
fn player_name_arg(message: &str) -> Option<String> {
    let b = crate::net::wire::latin1(message);
    Scan::new(&b).set(23, b"\n")
}

/// atcommand.cpp ACMD_FUNC(invite).
pub fn atcommand_invite(c: &Ctx, say: &dyn Fn(&str), aid: u32, command: &str, message: &str) -> i32 {
    let did = duel_group(c, aid);
    let Some(name) = player_name_arg(message).filter(|_| !message.is_empty()) else {
        say(&format_msg(msg_txt(435), &[&command])); // Please enter a player name (usage: %s <char name>).
        return -1;
    };
    if did == 0 || !duel_exist(c, did) {
        say(msg_txt(350)); // "Duel: @invite without @duel."
        return 0;
    }
    if !get(c, did).map(|d| duel_check_player_limit(&d)).unwrap_or(true) {
        say(msg_txt(351)); // "Duel: Limit of players is reached."
        return 0;
    }
    let Some(target) = pc::nick2sd(c, &name, true) else {
        say(msg_txt(352)); // "Duel: Player not found."
        return 0;
    };
    if duel_group(c, target.id) > 0 || duel_invite_of(c, target.id) > 0 {
        say(msg_txt(353)); // "Duel: Player already in duel."
        return 0;
    }
    if duel_only_on_same_map() && unit::get(c, aid).map(|u| u.map != target.map).unwrap_or(true) {
        say(&format_msg(msg_txt(364), &[&name])); // Duel: You can't invite %s because he/she isn't on the same map.
        return 0;
    }
    duel_invite(c, did, aid, target.id);
    say(msg_txt(354)); // "Duel: Invitation has been sent."
    0
}

/// atcommand.cpp ACMD_FUNC(duel): @duel [limit|nick].
pub fn atcommand_duel(c: &Ctx, say: &dyn Fn(&str), aid: u32, command: &str, message: &str) -> i32 {
    let g = duel_group(c, aid);
    if g > 0 {
        if let Some(sd) = unit::get(c, aid) {
            duel_showinfo(c, g, &sd);
        }
        return 0;
    }
    if duel_invite_of(c, aid) > 0 {
        say(msg_txt(355)); // "Duel: @duel without @reject."
        return 0;
    }
    if !duel_checktime(c, aid) {
        // "Duel: You can take part in duel only one time per %d minutes."
        say(&format_msg(msg_txt(356), &[&duel_time_interval()]));
        return 0;
    }
    if !message.is_empty() {
        let b = crate::net::wire::latin1(message);
        if let Some(v) = Scan::new(&b).int(11) {
            // sscanf(message, "%11u", &maxpl) >= 1
            let maxpl = v as u32;
            if !(2..=65535).contains(&maxpl) {
                say(msg_txt(357)); // "Duel: Invalid value."
                return 0;
            }
            duel_create(c, aid, maxpl);
        } else {
            let Some(name) = player_name_arg(message) else {
                say(&format_msg(msg_txt(435), &[&command])); // Please enter a player name (usage: %s <char name>).
                return -1;
            };
            match pc::nick2sd(c, &name, true) {
                Some(target) => {
                    let newduel = duel_create(c, aid, 2);
                    if duel_group(c, target.id) > 0 || duel_invite_of(c, target.id) > 0 {
                        say(msg_txt(353)); // "Duel: Player already in duel."
                        return 0;
                    }
                    duel_invite(c, newduel, aid, target.id);
                    say(msg_txt(354)); // "Duel: Invitation has been sent."
                }
                None => {
                    say(msg_txt(352)); // "Duel: Player not found."
                    return 0;
                }
            }
        }
    } else {
        duel_create(c, aid, 0);
    }
    0
}

/// atcommand.cpp ACMD_FUNC(leave).
pub fn atcommand_leave(c: &Ctx, say: &dyn Fn(&str), aid: u32) -> i32 {
    let g = duel_group(c, aid);
    if g == 0 {
        say(msg_txt(358)); // "Duel: @leave without @duel."
        return 0;
    }
    duel_leave(c, g, aid);
    say(msg_txt(359)); // "Duel: You left the duel."
    0
}

/// atcommand.cpp ACMD_FUNC(accept).
pub fn atcommand_accept(c: &Ctx, say: &dyn Fn(&str), aid: u32) -> i32 {
    if !duel_checktime(c, aid) {
        // "Duel: You can take part in duel only one time per %d minutes."
        say(&format_msg(msg_txt(356), &[&duel_time_interval()]));
        return 0;
    }
    let inv = duel_invite_of(c, aid);
    if inv == 0 || !duel_exist(c, inv) {
        say(msg_txt(360)); // "Duel: @accept without invititation."
        return 0;
    }
    if !get(c, inv).map(|d| duel_check_player_limit(&d)).unwrap_or(true) {
        say(msg_txt(351)); // "Duel: Limit of players is reached."
        return 0;
    }
    duel_accept(c, inv, aid);
    say(msg_txt(361)); // "Duel: Invitation has been accepted."
    0
}

/// atcommand.cpp ACMD_FUNC(reject).
pub fn atcommand_reject(c: &Ctx, say: &dyn Fn(&str), aid: u32) -> i32 {
    let inv = duel_invite_of(c, aid);
    if inv == 0 {
        say(msg_txt(362)); // "Duel: @reject without invititation."
        return 0;
    }
    duel_reject(c, inv, aid);
    say(msg_txt(363)); // "Duel: Invitation has been rejected."
    0
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn player_limit() {
        let d = Duel { did: 1, members_count: 2, invites_count: 0, max_players_limit: 2 };
        assert!(!duel_check_player_limit(&d));
        let d = Duel { did: 1, members_count: 1, invites_count: 1, max_players_limit: 2 };
        assert!(duel_check_player_limit(&d));
        let d = Duel { did: 1, members_count: 9, invites_count: 0, max_players_limit: 0 };
        assert!(duel_check_player_limit(&d));
    }
}
