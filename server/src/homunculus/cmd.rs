//! The homunculus @commands of atcommand.cpp (rAthena e985006, RENEWAL): @homlevel, @homevolution, @hommutate,
//! @makehomun, @homfriendly, @homhungry, @homtalk, @hominfo, @homstats, @homshuffle; and the homunculus parts of
//! other commands, called from those commands' files: @resetcooltime ([`resetcooltime`]), @useskill
//! ([`useskill_src`]), @refresh's clif_refresh ([`clif_refresh`]). @reloadmobdb's hom_reload is
//! crate::homunculus::hom_reload.

use super::data::*;
use crate::atcommand::Out;
use crate::cstr::atoi;
use crate::data::{format_msg, msg_txt};
use crate::db::misc::homunculus::{HomunculusDb, HM_CLASS_BASE};
use crate::world::Ctx;

/// mmo.hpp MAX_HOMUNCULUS_CLASS.
pub const MAX_HOMUNCULUS_CLASS: i32 = 52;

/// The homunculus commands (atcommand.rs chains here): Some(result) when `func` is one of them.
pub(crate) fn run(c: &Ctx, out: &Out, sd: u32, func: &str, command: &str, message: &str) -> Option<i32> {
    Some(match func {
        "homlevel" => homlevel(c, out, sd, message),
        "homevolution" => homevolution(c, out, sd),
        "hommutate" => hommutate(c, out, sd, message),
        "makehomun" => makehomun(c, out, sd, message),
        "homfriendly" => homfriendly(c, out, sd, message),
        "homhungry" => homhungry(c, out, sd, message),
        "homtalk" => homtalk(c, out, sd, message),
        "hominfo" => hominfo(c, out, sd),
        "homstats" => homstats(c, out, sd, command, message),
        "homshuffle" => homshuffle(c, out, sd, command, message),
        _ => return None,
    })
}

/// `hom_is_active(sd->hd)`: the active homunculus of the player.
fn active(c: &Ctx, sd: u32) -> Option<HomData> {
    of_master(c, sd).filter(|hd| hom_is_active(Some(hd)))
}

/// ACMD_FUNC(homlevel) (atcommand.cpp:8304-8332): @homlevel <number of levels>: the homunculus gains exp_next EXP and
/// a level (hom_levelup) that many times (stopping at the maximum level; the EXP of the failed try stays, as in
/// rAthena), then its status, a full heal and the level-up effect.
fn homlevel(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let level = atoi(message);
    if message.is_empty() || level < 1 {
        out.say(msg_txt(1253)); // Please enter a level adjustment (usage: @homlevel <number of levels>).
        return -1;
    }
    let Some(hd) = active(c, sd) else {
        out.say(msg_txt(1254)); // You do not have a homunculus.
        return -1;
    };
    let hd_id = hd.id;
    let mut i = 1;
    while i <= level {
        let Some(mut h) = get(c, hd_id) else { break };
        if h.exp_next == 0 {
            break;
        }
        h.homunculus.exp = h.homunculus.exp.wrapping_add(h.exp_next);
        put(c, &h);
        if super::hom_levelup(c, hd_id) == 0 {
            break;
        }
        i += 1;
    }
    super::status::status_calc_homunculus(c, hd_id, crate::status::defs::SCO_NONE);
    crate::status::hp::status_percent_heal(c, hd_id, 100, 100, 0);
    if let Some(u) = crate::unit::get(c, hd_id) {
        crate::clif::specialeffect(c, &u, crate::data::constant("EF_HO_UP").unwrap_or(0) as i32, crate::clif::Target::Area);
    }
    0
}

/// ACMD_FUNC(homevolution) (atcommand.cpp:8337-8356): hom_evolution, then the skill list.
fn homevolution(c: &Ctx, out: &Out, sd: u32) -> i32 {
    let Some(hd) = active(c, sd) else {
        out.say(msg_txt(1254)); // You do not have a homunculus.
        return -1;
    };
    if super::hom_evolution(c, hd.id) == 0 {
        out.say(msg_txt(1255)); // Your homunculus doesn't evolve.
        return -1;
    }
    super::clif::clif_homskillinfoblock(c, hd.id);
    0
}

/// ACMD_FUNC(hommutate) (atcommand.cpp:8358-8383): @hommutate {<homunculus id>}: an evolved homunculus of level 99
/// or more becomes that homunculus S (random Eira..Eleanor by default); otherwise it sweats.
fn hommutate(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let Some(hd) = active(c, sd) else {
        out.say(msg_txt(1254)); // You do not have a homunculus.
        return -1;
    };
    let homun_id: i32 = if message.is_empty() {
        c.rnd_value(MER_EIRA, MER_ELEANOR) as u16 as i32 // rnd_value<uint16>(MER_EIRA, MER_ELEANOR)
    } else {
        atoi(message)
    };
    let m_class = super::hom_class2mapid(hd.homunculus.class_ as i32);
    let m_id = super::hom_class2mapid(homun_id);
    if m_class != -1 && m_id != -1 && m_class & HOM_EVO != 0 && m_id & HOM_S != 0 && hd.homunculus.level >= 99 {
        super::hom_mutate(c, hd.id, homun_id);
    } else if let Some(u) = crate::unit::get(c, hd.id) {
        crate::clif::emotion(c, &u, super::buildins::ET_SWEAT);
    }
    0
}

/// ACMD_FUNC(makehomun) (atcommand.cpp:8388-8412): @makehomun <homunculus id>: a new homunculus of that class
/// (hom_create_request; the char server's answer brings it) for a player without one.
fn makehomun(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    if super::hom_id_of(c, sd) != 0 {
        out.say(msg_txt(450)); // You already have a homunculus
        return -1;
    }
    if message.is_empty() {
        out.say(msg_txt(1256)); // Please enter a homunculus ID (usage: @makehomun <homunculus id>).
        return -1;
    }
    let homunid = atoi(message);
    if homunid < HM_CLASS_BASE || homunid > HM_CLASS_BASE + MAX_HOMUNCULUS_CLASS - 1 {
        out.say(msg_txt(1257)); // Invalid Homunculus ID.
        return -1;
    }
    super::hom_create_request(c, sd, homunid);
    0
}

/// ACMD_FUNC(homfriendly) (atcommand.cpp:8417-8439): @homfriendly <0-1000>: intimacy = value × 100 (capped), sent to
/// the master.
fn homfriendly(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let Some(hd) = active(c, sd) else {
        out.say(msg_txt(1254)); // You do not have a homunculus.
        return -1;
    };
    if message.is_empty() {
        out.say(msg_txt(1258)); // Please enter a friendly value (usage: @homfriendly <friendly value [0-1000]>).
        return -1;
    }
    let friendly = atoi(message).clamp(0, 1000);
    with(c, hd.id, |hd| hd.homunculus.intimacy = (friendly * 100) as u32);
    super::clif::clif_send_homdata(c, hd.id, SP_INTIMATE);
    0
}

/// ACMD_FUNC(homhungry) (atcommand.cpp:8444-8466): @homhungry <0-100>: hunger = value (capped), sent to the master.
fn homhungry(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let Some(hd) = active(c, sd) else {
        out.say(msg_txt(1254)); // You do not have a homunculus.
        return -1;
    };
    if message.is_empty() {
        out.say(msg_txt(1259)); // Please enter a hunger value (usage: @homhungry <hunger value [0-100]>).
        return -1;
    }
    let hungry = atoi(message).clamp(0, 100);
    with(c, hd.id, |hd| hd.homunculus.hunger = hungry as i16);
    super::clif::clif_send_homdata(c, hd.id, SP_HUNGRY);
    0
}

/// ACMD_FUNC(homtalk) (atcommand.cpp:8471-8500): @homtalk <message>: the homunculus says "<name> : <message>" overhead
/// (min_chat_delay, no talking while muted).
fn homtalk(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    if crate::conf::min_chat_delay() > 0 {
        let Some(mut p) = crate::pc::get(c, sd) else { return -1 };
        if p.cantalk_tick - c.tick() > 0 {
            return 0;
        }
        p.cantalk_tick = c.tick() + crate::conf::min_chat_delay();
        crate::pc::put(c, &p);
    }
    let block = crate::pc::get(c, sd).map(|p| p.block_action & crate::pc::PCBLOCK_CHAT != 0).unwrap_or(false);
    if crate::status::sc::cant(c, sd, crate::status::consts::SCS_NOCHAT) || block {
        return -1; //no "chatting" while muted.
    }
    let Some(hd) = active(c, sd) else {
        out.say(msg_txt(1254)); // You do not have a homunculus.
        return -1;
    };
    // sscanf(message, "%99[^\n]", mes) < 1
    let mes: String = message.split('\n').next().unwrap_or("").chars().take(99).collect();
    if message.is_empty() || mes.is_empty() {
        out.say(msg_txt(1260)); // Please enter a message (usage: @homtalk <message>).
        return -1;
    }
    // snprintf(temp, sizeof temp, "%s : %s", sd->hd->homunculus.name, mes) (CHAT_SIZE_MAX)
    let mut temp = crate::net::wire::latin1(&format!("{} : {}", hd.homunculus.name, mes));
    temp.truncate(crate::net::CHAT_SIZE_MAX - 1);
    if let Some(u) = crate::unit::get(c, hd.id) {
        crate::clif_misc::disp_overhead(c, &u, &temp, crate::clif::Target::Area);
    }
    0
}

/// atcmd_output is char[CHAT_SIZE_MAX]: snprintf keeps CHAT_SIZE_MAX - 1 bytes.
fn output(s: String) -> String {
    let b = crate::net::wire::latin1(&s);
    if b.len() < crate::net::CHAT_SIZE_MAX {
        return s;
    }
    crate::net::wire::from_latin1(&b[..crate::net::CHAT_SIZE_MAX - 1])
}

/// ACMD_FUNC(hominfo) (atcommand.cpp:8505-8538): the homunculus' battle status (HP / SP, ATK, MATK, stats), hunger and
/// intimacy / 100.
fn hominfo(c: &Ctx, out: &Out, sd: u32) -> i32 {
    let Some(hd) = active(c, sd) else {
        out.say(msg_txt(1254)); // You do not have a homunculus.
        return -1;
    };
    let s = &hd.battle_status; // status_get_status_data(*hd)
    out.say(msg_txt(1261)); // Homunculus stats:
    // HP: %d/%d - SP: %d/%d (uint32 values through %d)
    out.say(&output(format_msg(msg_txt(1262), &[&(s.hp as i32), &(s.max_hp as i32), &(s.sp as i32), &(s.max_sp as i32)])));
    // ATK: %d - MATK: %d~%d
    out.say(&output(format_msg(msg_txt(1263), &[&(s.rhw.atk2 as i32 + s.batk), &s.matk_min, &s.matk_max])));
    // Hungry: %d - Intimacy: %u
    out.say(&output(format_msg(msg_txt(1264), &[&hd.homunculus.hunger, &(hd.homunculus.intimacy / 100)])));
    // Stats: Str %d / Agi %d / Vit %d / Int %d / Dex %d / Luk %d
    out.say(&output(format_msg(msg_txt(1265), &[&s.str, &s.agi, &s.vit, &s.int_, &s.dex, &s.luk])));
    0
}

/// The (current, min, max) triples of ACMD_FUNC(homstats) (atcommand.cpp:8558-8604) for Max HP, Max SP, Str, Agi,
/// Vit, Int, Dex, Luk: base + (level - 1) × growth (the stats' growth in tenths, integer-divided) + the evolution
/// bonus when evolved. HP / SP are uint32 sums printed through %d.
pub fn homstats_values(hd: &HomData, db: &HomunculusDb) -> [(i32, i32, i32); 8] {
    let hom = &hd.homunculus;
    let lv = hom.level as i32 - 1; //Since the first increase is at level 2.
    let evo = hom.class_ as i32 == db.evo_class;
    let pool = |base: u32, g: u32, e: u32| base.wrapping_add((lv as u32).wrapping_mul(g)).wrapping_add(if evo { e } else { 0 }) as i32;
    let stat = |base: u16, g: u16, e: u16| base as i32 + lv * (g as i32 / 10) + if evo { e as i32 } else { 0 };
    let (b, gn, gx, en, ex) = (&db.base, &db.gmin, &db.gmax, &db.emin, &db.emax);
    [
        (hom.max_hp as i32, pool(b.hp, gn.hp, en.hp), pool(b.hp, gx.hp, ex.hp)),
        (hom.max_sp as i32, pool(b.sp, gn.sp, en.sp), pool(b.sp, gx.sp, ex.sp)),
        (hom.str / 10, stat(b.str, gn.str, en.str), stat(b.str, gx.str, ex.str)),
        (hom.agi / 10, stat(b.agi, gn.agi, en.agi), stat(b.agi, gx.agi, ex.agi)),
        (hom.vit / 10, stat(b.vit, gn.vit, en.vit), stat(b.vit, gx.vit, ex.vit)),
        (hom.int_ / 10, stat(b.int_, gn.int_, en.int_), stat(b.int_, gx.int_, ex.int_)),
        (hom.dex / 10, stat(b.dex, gn.dex, en.dex), stat(b.dex, gx.dex, ex.dex)),
        (hom.luk / 10, stat(b.luk, gn.luk, en.luk), stat(b.luk, gx.luk, ex.luk)),
    ]
}

/// ACMD_FUNC(homstats) (atcommand.cpp:8540-8606): the homunculus' growth: each value with the range its level allows.
fn homstats(c: &Ctx, out: &Out, sd: u32, _command: &str, _message: &str) -> i32 {
    let Some(hd) = active(c, sd) else {
        out.say(msg_txt(1254)); // You do not have a homunculus.
        return -1;
    };
    // db = hd->homunculusDB
    let Some(db) = hd.db() else { return -1 };
    // Homunculus growth stats (Lv %d %s):
    out.say(&output(format_msg(msg_txt(1266), &[&(hd.homunculus.level as i32), &db.name])));
    // Max HP: %d (%d~%d), Max SP, Str, Agi, Vit, Int, Dex, Luk
    for (msg, (cur, min, max)) in (1267..=1274).zip(homstats_values(&hd, db)) {
        out.say(&output(format_msg(msg_txt(msg), &[&cur, &min, &max])));
    }
    0
}

/// ACMD_FUNC(homshuffle) (atcommand.cpp:8608-8621): hom_shuffle (-1 without a homunculus or when it is not active),
/// msg 1275, then @homstats' output.
fn homshuffle(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    let Some(hd) = of_master(c, sd) else {
        return -1; // nothing to do
    };
    if super::hom_shuffle(c, hd.id) == 0 {
        return -1;
    }
    // clif_displaymessage(sd->fd, …): to the character (the target of a #homshuffle), not the invoker
    if let Some(u) = crate::unit::get(c, sd) {
        crate::clif::displaymessage(c, &u, msg_txt(1275)); // Homunculus stats altered.
    }
    homstats(c, out, sd, command, message); //Print out the new stats
    0
}

/// ACMD_FUNC(resetcooltime)'s homunculus part (atcommand.cpp:1179-1186): an active homunculus' cooldowns, one "Found
/// skill '%s', unblocking..." per entry of hd->scd in its order, then skill_blockhomun_clear.
pub fn resetcooltime(c: &Ctx, _say: &dyn Fn(&str), sd: u32) {
    let Some(hd) = active(c, sd) else { return };
    let su = crate::unit::get(c, sd);
    for e in hd.scd_entries() {
        let name = crate::db::skills::skill_db().find(e.skill_id).map(|s| s.name.as_str()).unwrap_or("");
        // clif_displaymessage(sd->fd, …): to the character (the target of a #resetcooltime)
        if let Some(u) = &su {
            crate::clif::displaymessage(c, u, &format_msg(msg_txt(1537), &[&name])); // Found skill '%s', unblocking...
        }
    }
    super::cooldown::skill_blockhomun_clear(c, hd.id);
}

/// ACMD_FUNC(useskill) (atcommand.cpp:6546-6549): "If used with @useskill, put the homunc as dest": a homunculus skill
/// is cast by the player's active homunculus; anything else by the player.
pub fn useskill_src(c: &Ctx, sd: u32, skill_id: u16) -> u32 {
    if crate::db::skills::skill_chk_homun(skill_id) {
        if let Some(hd) = active(c, sd) {
            return hd.id;
        }
    }
    sd
}

/// clif.cpp clif_refresh's homunculus part (clif.cpp:9876-9877): `if (hom_is_active(sd->hd)) clif_send_homdata(*sd->hd,
/// SP_ACK)`.
pub fn clif_refresh(c: &Ctx, sd: u32) {
    if let Some(hd) = active(c, sd) {
        super::clif::clif_send_homdata(c, hd.id, SP_ACK);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::int_homun::SHomunculus;

    fn hd_of(class_: i32, level: i16, max_hp: u32, str_: i32) -> HomData {
        let db = crate::db::misc::homunculus::homunculus_db().homun_search(class_).expect("class in the database");
        HomData {
            id: 1,
            master: 2,
            homunculus: SHomunculus { class_: class_ as i16, level, max_hp, str: str_, ..Default::default() },
            db_class: db.base_class,
            base_status: Default::default(),
            battle_status: Default::default(),
            regen: Default::default(),
            regen_tick_hp: 0,
            regen_tick_sp: 0,
            masterteleport_timer: 0,
            hungry_timer: 0,
            exp_next: 0,
            scd: Vec::new(),
            scd_order: Vec::new(),
            scd_buckets: 0,
            scd_next_resize: 0,
        }
    }

    /// @homstats' ranges: a level 1 homunculus is at its base values; each level adds the growth (stats: tenths,
    /// integer-divided); an evolved class adds the evolution bonus.
    #[test]
    fn homstats_ranges() {
        let db = crate::db::misc::homunculus::homunculus_db().homun_search(MER_LIF).unwrap();
        let hd = hd_of(MER_LIF, 1, 150, 170);
        let v = homstats_values(&hd, db);
        assert_eq!(v[0], (150, db.base.hp as i32, db.base.hp as i32));
        assert_eq!(v[2], (17, db.base.str as i32, db.base.str as i32));
        let hd = hd_of(MER_LIF, 11, 0, 0);
        let v = homstats_values(&hd, db);
        assert_eq!(v[0].1, (db.base.hp + 10 * db.gmin.hp) as i32);
        assert_eq!(v[2].2, db.base.str as i32 + 10 * (db.gmax.str as i32 / 10));
        let evo = crate::db::misc::homunculus::homunculus_db().homun_search(db.evo_class).unwrap();
        let hd = hd_of(db.evo_class, 1, 0, 0);
        let v = homstats_values(&hd, evo);
        assert_eq!(v[1].2, (evo.base.sp + evo.emax.sp) as i32);
    }

    #[test]
    fn class_range() {
        assert_eq!(HM_CLASS_BASE + MAX_HOMUNCULUS_CLASS - 1, 6052); // HM_CLASS_MAX
        assert_eq!(super::super::buildins::ET_SWEAT, crate::status::sc_ext::ET_SWEAT);
    }
}
