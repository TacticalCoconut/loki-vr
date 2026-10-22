//! The status @commands of atcommand.cpp: @heal, @healap, @baselevelup (@lvup / @blevel / @baselvlup),
//! @joblevelup (@joblvup / @jlevel / @joblvlup), @statuspoint, @traitpoint, @skillpoint, @str … @crt (param),
//! @allstats (stat_all) and @alltraits (trait_all). Routed from atcommand.rs `run` by the ACMD function name.

use crate::cstr::{atoi, Scan};
use crate::data::msg_txt;
use crate::status::consts::*;
use crate::status::defs::*;
use crate::status::{self, clif, data, hp, progress};
use crate::world::Ctx;

/// The ACMD functions this file ports.
pub fn handles(func: &str) -> bool {
    matches!(func, "heal" | "healap" | "baselevelup" | "joblevelup" | "statuspoint" | "traitpoint" | "skillpoint" | "param" | "stat_all" | "trait_all")
}

/// Runs the command (0 ok, -1 failed: the caller answers "%s failed.").
pub fn run(c: &Ctx, say: &dyn Fn(&str), aid: u32, func: &str, command: &str, message: &str) -> i32 {
    if data::sd_get(c, aid).is_none() {
        return -1;
    }
    match func {
        "heal" => heal(c, say, aid, message),
        "healap" => healap(c, say, aid, message),
        "baselevelup" => baselevelup(c, say, aid, message),
        "joblevelup" => joblevelup(c, say, aid, message),
        "statuspoint" => statuspoint(c, say, aid, message),
        "traitpoint" => traitpoint(c, say, aid, message),
        "skillpoint" => skillpoint(c, say, aid, message),
        "param" => param(c, say, aid, command, message),
        "stat_all" => stat_all(c, say, aid, message),
        "trait_all" => trait_all(c, say, aid, message),
        _ => -1,
    }
}

/// sscanf(message, "%11d", &v) (the value stays when nothing is read).
fn scan_int(message: &str, v: &mut i32) -> bool {
    let b = crate::net::wire::latin1(message);
    let mut s = Scan::new(&b);
    match s.int(11) {
        Some(x) => {
            *v = x as i32;
            true
        }
        None => false,
    }
}

/// sscanf(message, "%11d %11d", &a, &b): how many were read.
fn scan_two(message: &str, a: &mut i32, b: &mut i32) -> usize {
    let bytes = crate::net::wire::latin1(message);
    let mut s = Scan::new(&bytes);
    let Some(x) = s.int(11) else { return 0 };
    *a = x as i32;
    let Some(y) = s.int(11) else { return 1 };
    *b = y as i32;
    2
}

/// ACMD_FUNC(heal): @heal [<hp> [<sp>]].
fn heal(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    let (mut hp_, mut sp_) = (0i32, 0i32); // [Valaris] thanks to fov
    scan_two(message, &mut hp_, &mut sp_);
    // some overflow checks
    if hp_ == i32::MIN {
        hp_ += 1;
    }
    if sp_ == i32::MIN {
        sp_ += 1;
    }
    if hp_ == 0 && sp_ == 0 {
        if hp::status_percent_heal(c, aid, 100, 100, 0) == 0 {
            say(msg_txt(157)); // HP and SP have already been recovered.
        } else {
            say(msg_txt(17)); // HP, SP recovered.
        }
        return 0;
    }
    if hp_ > 0 && sp_ >= 0 {
        if hp::status_heal(c, aid, hp_ as i64, sp_ as i64, 0, 0) == 0 {
            say(msg_txt(157)); // HP and SP are already with the good value.
        } else {
            say(msg_txt(17)); // HP, SP recovered.
        }
        return 0;
    }
    if hp_ < 0 && sp_ <= 0 {
        hp::status_damage(c, None, aid, -(hp_ as i64), -(sp_ as i64), 0, 0, 0, 0);
        heal_clif_damage(c, aid, hp_);
        say(msg_txt(156)); // HP or/and SP modified.
        return 0;
    }
    // Opposing signs.
    if hp_ != 0 {
        if hp_ > 0 {
            hp::status_heal(c, aid, hp_ as i64, 0, 0, 0);
        } else {
            hp::status_damage(c, None, aid, -(hp_ as i64), 0, 0, 0, 0, 0);
            heal_clif_damage(c, aid, hp_);
        }
    }
    if sp_ != 0 {
        if sp_ > 0 {
            hp::status_heal(c, aid, 0, sp_ as i64, 0, 0);
        } else {
            hp::status_damage(c, None, aid, 0, -(sp_ as i64), 0, 0, 0, 0);
        }
    }
    say(msg_txt(156)); // HP or/and SP modified.
    0
}

/// @heal's clif_damage(*sd, *sd, gettick(), 0, 0, -hp, 0, DMG_ENDURE, 0, false) after status_damage: the player
/// "hits" itself for the HP taken (ZC_NOTIFY_ACT3 type 4 to the area).
fn heal_clif_damage(c: &Ctx, aid: u32, hp_: i32) {
    if let Some(u) = crate::unit::get(c, aid) {
        crate::battle::clif::clif_damage(c, &u, &u, c.tick(), 0, 0, -(hp_ as i64), 0, crate::battle::DMG_ENDURE, 0, false);
    }
}

/// ACMD_FUNC(healap).
fn healap(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    let mut ap = 0i32;
    scan_int(message, &mut ap);
    // Overflow check.
    if ap == i32::MIN {
        ap += 1;
    }
    if ap == 0 {
        if hp::status_percent_heal(c, aid, 0, 0, 100) == 0 {
            say(msg_txt(823)); // AP have already been recovered.
        } else {
            say(msg_txt(821)); // AP recovered.
        }
    } else if ap > 0 {
        if hp::status_heal(c, aid, 0, 0, ap as i64, 0) == 0 {
            say(msg_txt(823));
        } else {
            say(msg_txt(821));
        }
    } else {
        hp::status_damage(c, None, aid, 0, 0, -(ap as i64), 0, 0, 0);
        say(msg_txt(822)); // AP modified.
    }
    0
}

/// ACMD_FUNC(baselevelup): @lvup / @blevel / @baselvlup <levels>.
fn baselevelup(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    let mut level = atoi(message);
    if message.is_empty() || level == 0 {
        say(msg_txt(986)); // Please enter a level adjustment (usage: @lvup/@blevel/@baselvlup <number of levels>).
        return -1;
    }
    let Some(mut ch) = crate::pc::character_of(c, aid) else { return -1 };
    let maxlv = crate::db::jobs::pc_maxbaselv(ch.class);
    let sp_db = crate::db::jobs::statpoint_db();
    let (mut status_point, mut trait_point) = (0u32, 0u32);
    if level > 0 {
        if ch.base_level as u32 >= maxlv {
            // check for max level by Valaris
            say(msg_txt(47)); // Base level can't go any higher.
            return -1;
        }
        if level as u32 > maxlv || level as u32 > maxlv - ch.base_level as u32 {
            // fix positive overflow
            level = (maxlv - ch.base_level as u32) as i32;
        }
        for i in 0..level {
            status_point += sp_db.pc_gets_status_point((ch.base_level as i32 + i) as u16);
            trait_point += sp_db.pc_gets_trait_point((ch.base_level as i32 + i) as u16);
        }
        ch.status_point = ch.status_point.wrapping_add(status_point);
        ch.trait_point += trait_point;
        ch.base_level += level as u16;
        status::put_char(c, &ch);
        status::calc_pc(c, aid, SCO_FORCE);
        hp::status_percent_heal(c, aid, 100, 100, 0);
        if let Some(u) = crate::unit::get(c, aid) {
            clif::misceffect(c, &u, NOTIFYEFFECT_BASE_LEVEL_UP);
        }
        let (blv, class) = crate::pc::character_of(c, aid).map(|ch| (ch.base_level as u32, ch.class as i32)).unwrap_or((0, 0));
        for j in blv.wrapping_sub(level as u32)..=blv {
            crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_GOAL_LEVEL, &[j as i32]);
            crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_GOAL_STATUS, &[j as i32, class]);
        }
        say(msg_txt(21)); // Base level raised.
    } else {
        if ch.base_level == 1 {
            say(msg_txt(158)); // Base level can't go any lower.
            return -1;
        }
        level = -level;
        if level as u32 >= ch.base_level as u32 {
            level = ch.base_level as i32 - 1;
        }
        let mut i = 0;
        while i > -level {
            status_point += sp_db.pc_gets_status_point((ch.base_level as i32 + i - 1) as u16);
            trait_point += sp_db.pc_gets_trait_point((ch.base_level as i32 + i - 1) as u16);
            i -= 1;
        }
        if ch.status_point < status_point || ch.trait_point < trait_point {
            progress::pc_resetstate(c, aid);
        }
        let Some(mut ch2) = crate::pc::character_of(c, aid) else { return -1 };
        ch2.status_point = if ch2.status_point < status_point { 0 } else { ch2.status_point - status_point };
        ch2.trait_point = if ch2.trait_point < trait_point { 0 } else { ch2.trait_point - trait_point };
        ch2.base_level -= level as u16;
        status::put_char(c, &ch2);
        say(msg_txt(22)); // Base level lowered.
        status::calc_pc(c, aid, SCO_FORCE);
        level = -level;
    }
    if let Some(mut ch) = crate::pc::character_of(c, aid) {
        ch.base_exp = 0;
        status::put_char(c, &ch);
    }
    for t in [SP_STATUSPOINT, SP_TRAITPOINT, SP_BASELEVEL, SP_BASEEXP, SP_NEXTBASEEXP] {
        clif::updatestatus(c, aid, t);
    }
    progress::pc_baselevelchanged(c, aid);
    if crate::pc::character_of(c, aid).map(|ch| ch.party_id != 0).unwrap_or(false) {
        crate::party::party_send_levelup(c, aid);
    }
    if level > 0 && crate::conf::atcommand_levelup_events() {
        crate::script::npc_run::npc_script_event(c, aid, crate::script::npc_run::NPCE_BASELVUP);
    }
    0
}

/// ACMD_FUNC(joblevelup): @joblvup / @jlevel / @joblvlup <levels>.
fn joblevelup(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    let mut level = atoi(message);
    if message.is_empty() || level == 0 {
        say(msg_txt(987)); // Please enter a level adjustment (usage: @joblvup/@jlevel/@joblvlup <number of levels>).
        return -1;
    }
    let Some(mut ch) = crate::pc::character_of(c, aid) else { return -1 };
    let maxlv = crate::db::jobs::pc_maxjoblv(ch.class);
    if level > 0 {
        if ch.job_level as u32 >= maxlv {
            say(msg_txt(23)); // Job level can't go any higher.
            return -1;
        }
        if level as u32 > maxlv || level as u32 > maxlv - ch.job_level as u32 {
            // fix positive overflow
            level = (maxlv - ch.job_level as u32) as i32;
        }
        ch.job_level += level as u16;
        ch.skill_point = ch.skill_point.wrapping_add(level as u32);
        status::put_char(c, &ch);
        if let Some(u) = crate::unit::get(c, aid) {
            clif::misceffect(c, &u, NOTIFYEFFECT_JOB_LEVEL_UP);
        }
        let jlv = crate::pc::character_of(c, aid).map(|ch| ch.job_level as u32).unwrap_or(0);
        for i in jlv.wrapping_sub(level as u32)..=jlv {
            crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_GOAL_LEVEL, &[i as i32]);
        }
        say(msg_txt(24)); // Job level raised.
    } else {
        if ch.job_level == 1 {
            say(msg_txt(159)); // Job level can't go any lower.
            return -1;
        }
        level = -level;
        if level as u32 >= ch.job_level as u32 {
            // fix negative overflow
            level = ch.job_level as i32 - 1;
        }
        ch.job_level -= level as u16;
        if ch.skill_point < level as u32 {
            status::put_char(c, &ch);
            crate::skilltree::pc_resetskill(c, aid, 0); // Reset skills since we need to subtract more points.
            let Some(fresh) = crate::pc::character_of(c, aid) else { return -1 };
            ch = fresh;
        }
        if ch.skill_point < level as u32 {
            ch.skill_point = 0;
        } else {
            ch.skill_point -= level as u32;
        }
        status::put_char(c, &ch);
        say(msg_txt(25)); // Job level lowered.
        level = -level;
    }
    if let Some(mut ch) = crate::pc::character_of(c, aid) {
        ch.job_exp = 0;
        status::put_char(c, &ch);
    }
    for t in [SP_JOBLEVEL, SP_JOBEXP, SP_NEXTJOBEXP, SP_SKILLPOINT] {
        clif::updatestatus(c, aid, t);
    }
    status::calc_pc(c, aid, SCO_FORCE);
    if level > 0 && crate::conf::atcommand_levelup_events() {
        crate::script::npc_run::npc_script_event(c, aid, crate::script::npc_run::NPCE_JOBLVUP);
    }
    0
}

/// The @statuspoint / @traitpoint / @skillpoint arithmetic (uint32 with UINT_MAX / 0 caps).
fn point_change(cur: u32, point: i32) -> u32 {
    if point < 0 {
        if cur < point.unsigned_abs() {
            0
        } else {
            (cur as i64 + point as i64) as u32
        }
    } else if u32::MAX - cur < point as u32 {
        u32::MAX
    } else {
        cur + point as u32
    }
}

/// ACMD_FUNC(statuspoint): @stpoint <points>.
fn statuspoint(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    let point = atoi(message);
    if message.is_empty() || point == 0 {
        say(msg_txt(1010)); // Please enter a number (usage: @stpoint <number of points>).
        return -1;
    }
    let Some(mut ch) = crate::pc::character_of(c, aid) else { return -1 };
    let new_status_point = point_change(ch.status_point, point);
    if new_status_point != ch.status_point {
        ch.status_point = new_status_point;
        status::put_char(c, &ch);
        clif::updatestatus(c, aid, SP_STATUSPOINT);
        say(msg_txt(174)); // Number of status points changed.
    } else {
        if point < 0 {
            say(msg_txt(41)); // Unable to decrease the number/value.
        } else {
            say(msg_txt(149)); // Unable to increase the number/value.
        }
        return -1;
    }
    0
}

/// ACMD_FUNC(traitpoint): @trpoint <points>.
fn traitpoint(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    let point = atoi(message);
    if message.is_empty() || point == 0 {
        say(msg_txt(820)); // Please enter a number (usage: @trpoint <number of points>).
        return -1;
    }
    let Some(mut ch) = crate::pc::character_of(c, aid) else { return -1 };
    let new_trait_point = point_change(ch.trait_point, point);
    if new_trait_point != ch.trait_point {
        ch.trait_point = new_trait_point;
        status::put_char(c, &ch);
        clif::updatestatus(c, aid, SP_TRAITPOINT);
        say(msg_txt(174)); // Number of status points changed.
    } else {
        if point < 0 {
            say(msg_txt(41));
        } else {
            say(msg_txt(149));
        }
        return -1;
    }
    0
}

/// ACMD_FUNC(skillpoint): @skpoint <points>.
fn skillpoint(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    let point = atoi(message);
    if message.is_empty() || point == 0 {
        say(msg_txt(1011)); // Please enter a number (usage: @skpoint <number of points>).
        return -1;
    }
    let Some(mut ch) = crate::pc::character_of(c, aid) else { return -1 };
    let new_skill_point = point_change(ch.skill_point, point);
    if new_skill_point != ch.skill_point {
        ch.skill_point = new_skill_point;
        status::put_char(c, &ch);
        clif::updatestatus(c, aid, SP_SKILLPOINT);
        say(msg_txt(175)); // Number of skill points changed.
    } else {
        if point < 0 {
            say(msg_txt(41));
        } else {
            say(msg_txt(149));
        }
        return -1;
    }
    0
}

/// pc.hpp parameter_names.
const PARAMETER_NAMES: [&str; PARAM_MAX] = ["Str", "Agi", "Vit", "Int", "Dex", "Luk", "Pow", "Sta", "Wis", "Spl", "Con", "Crt"];

/// ACMD_FUNC(param): @str / @agi / @vit / @int / @dex / @luk / @pow … @crt <+/-adjustment>.
fn param(c: &Ctx, say: &dyn Fn(&str), aid: u32, command: &str, message: &str) -> i32 {
    let mut value = 0i32;
    if message.is_empty() || !scan_int(message, &mut value) || value == 0 {
        say(msg_txt(1013)); // Please enter a valid value (usage: @str/@agi/@vit/@int/@dex/@luk <+/-adjustment>).
        return -1;
    }
    let name = command.get(1..).unwrap_or("");
    let Some(stat) = PARAMETER_NAMES.iter().position(|n| crate::cstr::eq_ignore_case(n, name)) else {
        // normally impossible...
        say(msg_txt(1013));
        return -1;
    };
    let Some(mut ch) = crate::pc::character_of(c, aid) else { return -1 };
    let class_ = data::sd_get(c, aid).map(|s| s.class_).unwrap_or(0);
    let status_v: u16 = if stat < PARAM_POW {
        progress::pc_getstat(&ch, SP_STR + stat as i32 - PARAM_STR as i32) as u16
    } else {
        if !crate::db::jobs::pc_is_trait_job(class_) {
            say(msg_txt(797)); // This command is unavailable to non - 4th class.
            return -1;
        }
        progress::pc_getstat(&ch, SP_POW + (stat - PARAM_POW) as i32) as u16
    };
    let max_status: u16 = if crate::pc::has_permission(c, aid, crate::rules::PC_PERM_BYPASS_MAX_STAT) {
        i16::MAX as u16
    } else {
        progress::pc_maxparameter(c, aid, &ch, stat) as u16
    };
    let new_value: u16 = if value > 0 && status_v as i32 + value >= max_status as i32 {
        max_status
    } else if value < 0 && value.unsigned_abs() >= status_v as u32 {
        if stat < PARAM_POW {
            1
        } else {
            0
        }
    } else {
        (status_v as i32 + value) as u16
    };
    if new_value != status_v {
        if stat < PARAM_POW {
            progress::pc_setstat(&mut ch, SP_STR + stat as i32 - PARAM_STR as i32, new_value as i32);
            status::put_char(c, &ch);
            clif::updatestatus(c, aid, SP_STR + stat as i32);
            clif::updatestatus(c, aid, SP_USTR + stat as i32);
        } else {
            progress::pc_setstat(&mut ch, SP_POW + (stat - PARAM_POW) as i32, new_value as i32);
            status::put_char(c, &ch);
            clif::updatestatus(c, aid, SP_POW + (stat - PARAM_POW) as i32);
            clif::updatestatus(c, aid, SP_UPOW + (stat - PARAM_POW) as i32);
        }
        status::calc_pc(c, aid, SCO_FORCE);
        say(msg_txt(42)); // Stat changed.
        crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_GOAL_STATUS, &[]);
    } else {
        if value < 0 {
            say(msg_txt(41)); // Unable to decrease the number/value.
        } else {
            say(msg_txt(149)); // Unable to increase the number/value.
        }
        return -1;
    }
    0
}

/// ACMD_FUNC(stat_all): @allstats / @statall [<+/-adjustment>].
fn stat_all(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    let Some(mut ch) = crate::pc::character_of(c, aid) else { return -1 };
    let mut value = 0i32;
    let mut status_v = [0u16; PARAM_MAX];
    let mut max_status = [0u16; PARAM_MAX];
    for i in PARAM_STR..PARAM_POW {
        status_v[i] = progress::pc_getstat(&ch, SP_STR + i as i32) as u16;
    }
    if message.is_empty() || !scan_int(message, &mut value) || value == 0 {
        for i in PARAM_STR..PARAM_POW {
            max_status[i] = progress::pc_maxparameter(c, aid, &ch, i) as u16;
        }
        value = i16::MAX as i32;
    } else if crate::pc::has_permission(c, aid, crate::rules::PC_PERM_BYPASS_MAX_STAT) {
        for m in max_status.iter_mut().take(PARAM_POW) {
            *m = i16::MAX as u16;
        }
    } else {
        for i in PARAM_STR..PARAM_POW {
            max_status[i] = progress::pc_maxparameter(c, aid, &ch, i) as u16;
        }
    }
    let mut count = 0;
    for i in PARAM_STR..PARAM_POW {
        let new_value: i16 = if value > 0 && status_v[i] as i32 + value >= max_status[i] as i32 {
            max_status[i] as i16
        } else if value < 0 && value.unsigned_abs() >= status_v[i] as u32 {
            1
        } else {
            (status_v[i] as i32 + value) as i16
        };
        if new_value as i32 != status_v[i] as i32 {
            progress::pc_setstat(&mut ch, SP_STR + i as i32, new_value as i32);
            status::put_char(c, &ch);
            clif::updatestatus(c, aid, SP_STR + i as i32);
            clif::updatestatus(c, aid, SP_USTR + i as i32);
            count += 1;
        }
    }
    if count > 0 {
        // if at least 1 stat modified
        status::calc_pc(c, aid, SCO_FORCE);
        say(msg_txt(84)); // All stats changed!
        crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_GOAL_STATUS, &[]);
    } else {
        if value < 0 {
            say(msg_txt(177)); // You cannot decrease that stat anymore.
        } else {
            say(msg_txt(178)); // You cannot increase that stat anymore.
        }
        return -1;
    }
    0
}

/// ACMD_FUNC(trait_all): @alltraits [<+/-adjustment>] (4th classes).
fn trait_all(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    let class_ = data::sd_get(c, aid).map(|s| s.class_).unwrap_or(0);
    if !crate::db::jobs::pc_is_trait_job(class_) {
        say(msg_txt(797)); // This command is unavailable to non - 4th class.
        return -1;
    }
    let Some(mut ch) = crate::pc::character_of(c, aid) else { return -1 };
    let mut value = 0i32;
    let mut status_v = [0u16; PARAM_MAX];
    let mut max_status = [0u16; PARAM_MAX];
    for i in PARAM_POW..PARAM_MAX {
        status_v[i] = progress::pc_getstat(&ch, SP_POW + (i - PARAM_POW) as i32) as u16;
    }
    if message.is_empty() || !scan_int(message, &mut value) || value == 0 {
        for i in PARAM_POW..PARAM_MAX {
            max_status[i] = progress::pc_maxparameter(c, aid, &ch, i) as u16;
        }
        value = i16::MAX as i32;
    } else if crate::pc::has_permission(c, aid, crate::rules::PC_PERM_BYPASS_MAX_STAT) {
        for m in max_status.iter_mut().skip(PARAM_POW) {
            *m = i16::MAX as u16;
        }
    } else {
        for i in PARAM_POW..PARAM_MAX {
            max_status[i] = progress::pc_maxparameter(c, aid, &ch, i) as u16;
        }
    }
    let mut count = 0;
    for i in PARAM_POW..PARAM_MAX {
        let new_value: i16 = if value > 0 && status_v[i] as i32 + value >= max_status[i] as i32 {
            max_status[i] as i16
        } else if value < 0 && value.unsigned_abs() >= status_v[i] as u32 {
            0
        } else {
            (status_v[i] as i32 + value) as i16
        };
        if new_value as i32 != status_v[i] as i32 {
            progress::pc_setstat(&mut ch, SP_POW + (i - PARAM_POW) as i32, new_value as i32);
            status::put_char(c, &ch);
            clif::updatestatus(c, aid, SP_POW + (i - PARAM_POW) as i32);
            clif::updatestatus(c, aid, SP_UPOW + (i - PARAM_POW) as i32);
            count += 1;
        }
    }
    if count > 0 {
        status::calc_pc(c, aid, SCO_FORCE);
        say(msg_txt(84)); // All stats changed!
        crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_GOAL_STATUS, &[]);
    } else {
        if value < 0 {
            say(msg_txt(177));
        } else {
            say(msg_txt(178));
        }
        return -1;
    }
    0
}
