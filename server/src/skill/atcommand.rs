//! The skill-use @commands of atcommand.cpp (rAthena e985006): @resetcooltime, @useskill, @displayskill,
//! @displayskillcast, @nuke, @spiritball and @soulball. Routed from atcommand.rs `run` by the ACMD function name.
//! (Homunculus parts — sd->hd — are homunculus/cmd.rs'; mercenary parts — sd->md — mercenary/skill.rs'.)

use crate::cstr::Scan;
use crate::data::{format_msg, msg_txt};
use crate::db::skills::{skill_db, skill_get_ele, skill_get_inf, NPC_SELFDESTRUCTION, INF_GROUND_SKILL};
use crate::world::Ctx;

/// Runs the command when this file ports it (0 ok, -1 failed: the caller answers "%s failed.").
pub fn run(c: &Ctx, say: &dyn Fn(&str), aid: u32, func: &str, _command: &str, message: &str) -> Option<i32> {
    Some(match func {
        "resetcooltime" => resetcooltime(c, say, aid),
        "useskill" => useskill(c, say, aid, message),
        "displayskill" => displayskill(c, say, aid, message),
        "displayskillcast" => displayskillcast(c, say, aid, message),
        "nuke" => nuke(c, say, aid, message),
        "spiritball" => spiritball(c, say, aid, message),
        "soulball" => soulball(c, say, aid, message),
        _ => return None,
    })
}

/// `%6hu`: a number stored into an unsigned short (the C conversion's truncation).
fn hu(s: &mut Scan) -> Option<u16> {
    s.int(6).map(|v| v as u16)
}

/// ACMD_FUNC(resetcooltime): every skill cooldown of the player ends, one "Found skill '%s', unblocking..." per
/// entry in sd->scd's order, then the active homunculus' (homunculus/cmd.rs) and the mercenary's (sd->md->scd,
/// mercenary/skill.rs).
fn resetcooltime(c: &Ctx, say: &dyn Fn(&str), aid: u32) -> i32 {
    let entries = super::cooldown::entries(c, aid);
    if !entries.is_empty() {
        for e in &entries {
            let name = skill_db().find(e.skill_id).map(|s| s.name.as_str()).unwrap_or("");
            say(&format_msg(msg_txt(1537), &[&name])); // Found skill '%s', unblocking...
        }
        super::cooldown::skill_blockpc_clear(c, aid);
    }
    // the active homunculus' cooldowns (atcommand.cpp:1179-1186, homunculus/cmd.rs)
    crate::homunculus::cmd::resetcooltime(c, say, aid);
    // if( sd->md != nullptr ): skill_blockmerc_clear (atcommand.cpp:1188-1195)
    if let Some(md) = crate::mercenary::data::of_master(c, aid) {
        for e in md.scd_entries() {
            let name = skill_db().find(e.skill_id).map(|s| s.name.as_str()).unwrap_or("");
            say(&format_msg(msg_txt(1537), &[&name])); // Found skill '%s', unblocking...
        }
        crate::mercenary::skill::skill_blockmerc_clear(c, md.id);
    }
    0
}

/// ACMD_FUNC(useskill): @useskill <skill ID> <skill level> <char name> — the GM uses the skill on the player ("self":
/// on itself), a ground skill at the player's cell.
fn useskill(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    // sscanf(message, "%6hu %6hu %23[^\n]", &skill_id, &skill_lv, atcmd_player_name) != 3
    let b = crate::net::wire::latin1(message);
    let mut s = Scan::new(&b);
    let parsed = hu(&mut s).and_then(|id| {
        hu(&mut s).and_then(|lv| {
            s.ws();
            s.set(23, b"\n").map(|name| (id, lv, name))
        })
    });
    let Some((skill_id, mut skill_lv, name)) = parsed.filter(|_| !message.is_empty()) else {
        say(msg_txt(1165)); // Usage: @useskill <skill ID> <skill level> <char name>
        return -1;
    };
    if skill_id == 0 || skill_db().find(skill_id).is_none() {
        say(msg_txt(198)); // This skill number doesn't exist.
        return -1;
    }
    if skill_lv == 0 {
        skill_lv = 1;
    }
    let pl = if name == "self" {
        crate::unit::get(c, aid) // quick keyword
    } else {
        crate::pc::nick2sd(c, &name, true)
    };
    let Some(pl) = pl else {
        say(msg_txt(3)); // Character not found.
        return -1;
    };
    if crate::pc::group_level(c, aid) < crate::pc::group_level(c, pl.id) {
        say(msg_txt(81)); // Your GM level don't authorise you to do this action on this player.
        return -1;
    }
    // SKILL_CHK_HOMUN(skill_id) && hom_is_active(sd->hd): the homunculus casts (atcommand.cpp:6546-6549,
    // homunculus/cmd.rs); else bl = sd
    let bl = crate::homunculus::cmd::useskill_src(c, aid, skill_id);
    if skill_get_inf(skill_id) & INF_GROUND_SKILL as i32 != 0 {
        super::unit::unit_skilluse_pos(c, bl, pl.x, pl.y, skill_id, skill_lv);
    } else {
        super::unit::unit_skilluse_id(c, bl, pl.id, skill_id, skill_lv);
    }
    0
}

/// ACMD_FUNC(displayskill): @displayskill <skill ID> {<skill level> <type>} — the skill's animations on the GM
/// (type 0 all, 1 damage, 2 splash damage, 3 no damage, 4 ground).
fn displayskill(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    use crate::battle::{DMG_SINGLE, DMG_SPLASH};
    use super::clif::{clif_skill_damage, clif_skill_nodamage, clif_skill_poseffect};
    // sscanf(message, "%6hu %6hu %6hu", &skill_id, &skill_lv, &type) < 1 (skill_lv = 1, type = 0 by default)
    let b = crate::net::wire::latin1(message);
    let mut s = Scan::new(&b);
    let (mut skill_lv, mut type_) = (1u16, 0u16);
    let Some(skill_id) = hu(&mut s).filter(|_| !message.is_empty()) else {
        say(msg_txt(1166)); // Usage: @displayskill <skill ID> {<skill level> <type>}
        say(msg_txt(825)); // Effect Types: 0: All, 1: Damage, 2: Splash Dmg, 3: No Damage, 4: Ground
        return -1;
    };
    if let Some(lv) = hu(&mut s) {
        skill_lv = lv;
        if let Some(t) = hu(&mut s) {
            type_ = t;
        }
    }
    let Some(sd) = crate::unit::get(c, aid) else { return -1 };
    let status = crate::battle::bl::status(c, &sd);
    let tick = c.tick();
    if type_ == 0 || type_ == 1 {
        clif_skill_damage(c, &sd, &sd, tick, status.amotion as i32, status.dmotion as i32, 1, 1, skill_id, skill_lv as i32, DMG_SINGLE);
    }
    if type_ == 0 || type_ == 2 {
        clif_skill_damage(c, &sd, &sd, tick, status.amotion as i32, status.dmotion as i32, 1, 1, skill_id, skill_lv as i32, DMG_SPLASH);
    }
    if type_ == 0 || type_ == 3 {
        clif_skill_nodamage(c, Some(&sd), &sd, skill_id, skill_lv as i32, true);
    }
    if type_ == 0 || type_ == 4 {
        clif_skill_poseffect(c, &sd, skill_id, skill_lv, sd.x, sd.y, tick);
    }
    0
}

/// ACMD_FUNC(displayskillcast): @displayskillcast <skill ID> {<skill level> <ground target flag> <cast time>} — the
/// cast bar of the skill on the GM (or at its cell with the ground flag 1), 5 s by default.
fn displayskillcast(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    // sscanf(message, "%6hu %6hu %6hu %6hu", &skill_id, &skill_lv, &target_type, &cast_time) < 1
    let b = crate::net::wire::latin1(message);
    let mut s = Scan::new(&b);
    let (mut skill_lv, mut target_type, mut cast_time) = (1u16, 0u16, 5000u16);
    let Some(skill_id) = hu(&mut s).filter(|_| !message.is_empty()) else {
        say(msg_txt(824)); // Usage: @displayskillcast <skill ID> {<skill level> <ground target flag> <cast time>}
        return -1;
    };
    if let Some(lv) = hu(&mut s) {
        skill_lv = lv;
        if let Some(t) = hu(&mut s) {
            target_type = t;
            if let Some(ct) = hu(&mut s) {
                cast_time = ct;
            }
        }
    }
    let Some(sd) = crate::unit::get(c, aid) else { return -1 };
    let ele = skill_get_ele(skill_id, skill_lv);
    if target_type == 1 {
        super::clif::clif_skillcasting(c, &sd, None, sd.x as u16, sd.y as u16, skill_id, skill_lv, ele, cast_time as i32);
    } else {
        super::clif::clif_skillcasting(c, &sd, Some(&sd), 0, 0, skill_id, skill_lv, ele, cast_time as i32);
    }
    0
}

/// ACMD_FUNC(nuke): @nuke <char name> — the player casts NPC_SELFDESTRUCTION level 99 on itself
/// (skill_castend_nodamage_id; its class SkillSuicideBombing is the npc family's, next wave: until then the pipeline
/// takes rAthena's no-impl path for it).
fn nuke(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    // sscanf(message, "%23[^\n]", atcmd_player_name) < 1
    let b = crate::net::wire::latin1(message);
    let mut s = Scan::new(&b);
    let Some(name) = s.set(23, b"\n").filter(|_| !message.is_empty()) else {
        say(msg_txt(1128)); // Please enter a player name (usage: @nuke <char name>).
        return -1;
    };
    let Some(pl) = crate::pc::nick2sd(c, &name, false) else {
        say(msg_txt(3)); // Character not found.
        return -1;
    };
    if crate::pc::group_level(c, aid) >= crate::pc::group_level(c, pl.id) {
        // you can kill only lower or same GM level
        super::castend::skill_castend_nodamage_id(c, &pl, &pl, NPC_SELFDESTRUCTION, 99, c.tick(), 0);
        say(msg_txt(109)); // Player has been nuked!
    } else {
        say(msg_txt(81)); // Your GM level don't authorise you to do this action on this player.
        return -1;
    }
    0
}

/// ACMD_FUNC(spiritball): @spiritball <number: 0-MAX_SPIRITBALL> — the player's spirit spheres are removed
/// (pc_delspiritball type 1: no client effect) and set to the number (without sphere timers, as rAthena does), then
/// shown (clif_spiritball to the area).
fn spiritball(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    use super::state::MAX_SPIRITBALL;
    let number = crate::cstr::atoi(message);
    if message.is_empty() || number < 0 || number > MAX_SPIRITBALL {
        say(&format_msg(msg_txt(1028), &[&MAX_SPIRITBALL])); // Please enter an amount (usage: @spiritball <number: 0-%d>).
        return -1;
    }
    if super::state::get(c, aid).spiritball > 0 {
        let n = super::state::get(c, aid).spiritball as i32;
        super::state::pc_delspiritball(c, aid, n, 1);
    }
    super::state::with(c, aid, |s| s.spiritball = number as i8);
    if let Some(u) = crate::unit::get(c, aid) {
        super::clif::clif_spiritball(c, &u, None, crate::clif::Target::Area);
    }
    // no message, player can look the difference
    0
}

/// ACMD_FUNC(soulball): @soulball <number: 0-MAX_SOUL_BALL> — the soul energy is removed (no client effect), then
/// pc_addsoulball(number) (MAX_SOUL_BALL cap, SC_SOULENERGY) and clif_soulball.
fn soulball(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    use super::state::MAX_SOUL_BALL;
    let number = crate::cstr::atoi(message);
    if message.is_empty() || number < 0 || number > MAX_SOUL_BALL {
        say(&format!("Usage: @soulball <number: 0-{MAX_SOUL_BALL}>"));
        return -1;
    }
    let cur = super::state::get(c, aid).soulball as i32;
    if cur > 0 {
        super::state::pc_delsoulball(c, aid, cur, true);
    }
    // pc_addsoulball(*sd, number): max defaults to MAX_SOUL_BALL (pc.hpp)
    super::state::pc_addsoulball(c, aid, number, MAX_SOUL_BALL);
    if let Some(u) = crate::unit::get(c, aid) {
        super::state::clif_soulball(c, &u, None, crate::clif::Target::Area);
    }
    0
}
