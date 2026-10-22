//! atcommand.cpp's combat commands: @kill, @alive, @pvpoff / @pvpon, @gvgoff / @gvgon, @doom, @doommap, @raise,
//! @raisemap, @killer, @killable. Routed from atcommand.rs `run` (None: not one of these).

use spacetimedb::Table as _;

use super::*;
use crate::clif;
use crate::data::msg_txt;
use crate::map::BL_CHAR;
use crate::npc::{mapflag, MF_GVG, MF_PVP};
use crate::pc::pc as _;
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// script.hpp EF_GRANDCROSS2.
const EF_GRANDCROSS2: i32 = 450;

/// The invoker's message (clif_displaymessage(fd, …)).
fn say(c: &Ctx, fd: Option<&Unit>, text: &str) {
    if let Some(u) = fd {
        clif::displaymessage(c, u, text);
    }
}

pub fn run(c: &Ctx, fd: Option<&Unit>, sd: u32, func: &str, _command: &str, _message: &str) -> Option<i32> {
    Some(match func {
        "kill" => kill(c, fd, sd),
        "alive" => alive(c, fd, sd),
        "pvpoff" => pvpoff(c, fd, sd),
        "pvpon" => pvpon(c, fd, sd),
        "gvgoff" => gvgoff(c, fd, sd),
        "gvgon" => gvgon(c, fd, sd),
        "doom" => doom(c, fd, sd, false),
        "doommap" => doom(c, fd, sd, true),
        "raise" => raise(c, fd, sd, false),
        "raisemap" => raise(c, fd, sd, true),
        "killer" => killer(c, fd, sd),
        "killable" => killable(c, fd, sd),
        _ => return None,
    })
}

/// clif.cpp clif_skill_nodamage: ZC_USE_SKILL2 (skill/clif.rs).
pub fn clif_skill_nodamage(c: &Ctx, src: Option<&Unit>, dst: &Unit, skill_id: u16, heal: i32, success: bool) -> bool {
    crate::skill::clif::clif_skill_nodamage(c, src, dst, skill_id, heal, success)
}

/// ACMD_FUNC(kill).
fn kill(c: &Ctx, fd: Option<&Unit>, aid: u32) -> i32 {
    crate::status::hp::status_kill(c, aid);
    if let Some(u) = unit::get(c, aid) {
        clif::displaymessage(c, &u, msg_txt(13)); // A pity! You've died.
    }
    if fd.map(|f| f.id) != Some(aid) {
        say(c, fd, msg_txt(14)); // Character killed.
    }
    0
}

/// ACMD_FUNC(alive).
fn alive(c: &Ctx, fd: Option<&Unit>, aid: u32) -> i32 {
    if crate::status::hp::status_revive(c, aid, 100, 100, 0) == 0 {
        say(c, fd, msg_txt(667)); // You're not dead.
        return -1;
    }
    if let Some(u) = unit::get(c, aid) {
        clif_skill_nodamage(c, Some(&u), &u, crate::db::skills::ALL_RESURRECTION, 4, true);
    }
    say(c, fd, msg_txt(16)); // You've been revived! It's a miracle!
    0
}

/// ACMD_FUNC(pvpoff).
fn pvpoff(c: &Ctx, fd: Option<&Unit>, aid: u32) -> i32 {
    let Some(u) = unit::get(c, aid) else { return -1 };
    if !mapflag(c, u.map, MF_PVP) {
        say(c, fd, msg_txt(160)); // PvP is already Off.
        return -1;
    }
    pvp::map_setmapflag(c, u.map, MF_PVP, false, 0);
    say(c, fd, msg_txt(31)); // PvP: Off.
    0
}

/// ACMD_FUNC(pvpon).
fn pvpon(c: &Ctx, fd: Option<&Unit>, aid: u32) -> i32 {
    let Some(u) = unit::get(c, aid) else { return -1 };
    if mapflag(c, u.map, MF_PVP) {
        say(c, fd, msg_txt(161)); // PvP is already On.
        return -1;
    }
    pvp::map_setmapflag(c, u.map, MF_PVP, true, 0);
    say(c, fd, msg_txt(32)); // PvP: On.
    0
}

/// ACMD_FUNC(gvgoff).
fn gvgoff(c: &Ctx, fd: Option<&Unit>, aid: u32) -> i32 {
    let Some(u) = unit::get(c, aid) else { return -1 };
    if !mapflag(c, u.map, MF_GVG) {
        say(c, fd, msg_txt(162)); // GvG is already Off.
        return -1;
    }
    pvp::map_setmapflag(c, u.map, MF_GVG, false, 0);
    say(c, fd, msg_txt(33)); // GvG: Off.
    0
}

/// ACMD_FUNC(gvgon).
fn gvgon(c: &Ctx, fd: Option<&Unit>, aid: u32) -> i32 {
    let Some(u) = unit::get(c, aid) else { return -1 };
    if mapflag(c, u.map, MF_GVG) {
        say(c, fd, msg_txt(163)); // GvG is already On.
        return -1;
    }
    pvp::map_setmapflag(c, u.map, MF_GVG, true, 0);
    say(c, fd, msg_txt(34)); // GvG: On.
    0
}

/// Every online player in mapit order (the `pc` rows; mapit_getallusers).
fn all_users(c: &Ctx) -> Vec<u32> {
    let mut v: Vec<u32> = c.db().pc().iter().filter(|p| p.active).map(|p| p.account_id).collect();
    v.sort();
    v
}

/// ACMD_FUNC(doom) / ACMD_FUNC(doommap): every other player of a lower or equal group level dies.
fn doom(c: &Ctx, fd: Option<&Unit>, aid: u32, same_map: bool) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    let my_level = crate::pc::group_level(c, aid);
    let invoker = fd.map(|f| f.id);
    for pl in all_users(c) {
        let Some(pl_sd) = unit::get(c, pl) else { continue };
        if Some(pl) != invoker && (!same_map || sd.map == pl_sd.map) && my_level >= crate::pc::group_level(c, pl) {
            crate::status::hp::status_kill(c, pl);
            if let Some(u) = unit::get(c, pl) {
                clif::specialeffect(c, &u, EF_GRANDCROSS2, clif::Target::Area);
                clif::displaymessage(c, &u, msg_txt(61)); // The holy messenger has given judgement.
            }
        }
    }
    say(c, fd, msg_txt(62)); // Judgement was made.
    0
}

/// atcommand.cpp atcommand_raise_sub.
fn raise_sub(c: &Ctx, aid: u32) {
    crate::status::hp::status_revive(c, aid, 100, 100, 0);
    if let Some(u) = unit::get(c, aid) {
        clif_skill_nodamage(c, Some(&u), &u, crate::db::skills::ALL_RESURRECTION, 4, true);
        clif::displaymessage(c, &u, msg_txt(63)); // Mercy has been shown.
    }
}

/// ACMD_FUNC(raise) / ACMD_FUNC(raisemap): every dead player (of the map) comes back.
fn raise(c: &Ctx, fd: Option<&Unit>, aid: u32, same_map: bool) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    for pl in all_users(c) {
        let Some(pl_sd) = unit::get(c, pl) else { continue };
        if (!same_map || sd.map == pl_sd.map) && crate::pc::isdead(c, pl) {
            raise_sub(c, pl);
        }
    }
    say(c, fd, msg_txt(64)); // Mercy has been granted.
    0
}

/// ACMD_FUNC(killer): may attack anyone, anywhere.
fn killer(c: &Ctx, fd: Option<&Unit>, aid: u32) -> i32 {
    let on = state::with(c, aid, |b| {
        b.killer = !b.killer;
        b.killer
    });
    if on {
        say(c, fd, msg_txt(241)); // You can now attack and kill players freely.
    } else {
        say(c, fd, msg_txt(292)); // Killer state reset.
        if let Some(mut u) = unit::get(c, aid) {
            unit::stop_attack(c, &mut u);
            unit::save(c, &u);
        }
    }
    0
}

/// ACMD_FUNC(killable): anyone may attack this player.
fn killable(c: &Ctx, fd: Option<&Unit>, aid: u32) -> i32 {
    let on = state::with(c, aid, |b| {
        b.killable = !b.killable;
        b.killable
    });
    if on {
        say(c, fd, msg_txt(242)); // You can now be attacked and killed by players.
    } else {
        say(c, fd, msg_txt(288)); // You are no longer killable.
        // map_foreachinallrange(unit_stopattack, sd, AREA_SIZE, BL_CHAR, sd->id)
        if let Some(sd) = unit::get(c, aid) {
            let r = crate::conf::area_size();
            for u in crate::map::units_in_area(c, sd.map, sd.x - r, sd.y - r, sd.x + r, sd.y + r, BL_CHAR) {
                super::unit_attack::unit_stopattack(c, &u, aid);
            }
        }
    }
    0
}
