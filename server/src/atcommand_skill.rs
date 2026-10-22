//! The skill-tree and job @commands of atcommand.cpp: @job / @jobchange (jobchange), @allskill (allskill),
//! @questskill, @lostskill, @reset, @resetstat, @resetskill and @skilltree. Routed from atcommand.rs `run` by the ACMD
//! function name.

use crate::cstr::{atoi, Scan};
use crate::data::{self, format_msg, msg_txt};
use crate::db::jobs::*;
use crate::db::skills::{self as sk};
use crate::skilltree;
use crate::world::Ctx;

/// Runs the command when this file ports it (0 ok, -1 failed: the caller answers "%s failed.").
pub fn run(c: &Ctx, say: &dyn Fn(&str), aid: u32, func: &str, command: &str, message: &str) -> Option<i32> {
    Some(match func {
        "jobchange" => jobchange(c, say, aid, command, message),
        "allskill" => allskill(c, say, aid),
        "questskill" => questskill(c, say, aid, command, message),
        "lostskill" => lostskill(c, say, aid, command, message),
        "reset" => reset(c, say, aid),
        "resetstat" => resetstat(c, say, aid),
        "resetskill" => resetskill(c, say, aid),
        "skilltree" => skilltree_cmd(c, say, aid, message),
        _ => return None,
    })
}

fn help(say: &dyn Fn(&str), command: &str) {
    if let Some(text) = data::atcommands().help_string(command) {
        say(text);
    }
}

/// strncmpi(a, b, n) == 0 (latin-1 bytes, case-insensitive, stopping at the end of either string).
fn strncmpi_eq(a: &str, b: &str, n: usize) -> bool {
    let a = crate::net::wire::latin1(a);
    let b = crate::net::wire::latin1(b);
    for i in 0..n {
        let x = a.get(i).copied().unwrap_or(0).to_ascii_lowercase();
        let y = b.get(i).copied().unwrap_or(0).to_ascii_lowercase();
        if x != y {
            return false;
        }
        if x == 0 {
            return true;
        }
    }
    true
}

/// ACMD_FUNC(jobchange): @job / @jobchange <job ID|job name> [<upper>].
fn jobchange(c: &Ctx, say: &dyn Fn(&str), aid: u32, command: &str, message: &str) -> i32 {
    let (mut job, mut upper) = (0i32, 0i32);
    // sscanf(message, "%11d %11d", &job, &upper) < 1
    let b = crate::net::wire::latin1(message);
    let mut s = Scan::new(&b);
    let n = match s.int(11) {
        Some(x) => {
            job = x as i32;
            match s.int(11) {
                Some(y) => {
                    upper = y as i32;
                    2
                }
                None => 1,
            }
        }
        None => 0,
    };
    if message.is_empty() || n < 1 {
        let mut found = false;
        upper = 0;
        // Normal Jobs
        let mut i = JOB_NOVICE;
        while i < JOB_MAX_BASIC && !found {
            if strncmpi_eq(message, job_name(i), 16) {
                job = i;
                found = true;
            }
            i += 1;
        }
        // High Jobs, Babys, Third, and Fourth
        let mut i = JOB_NOVICE_HIGH;
        while i < JOB_MAX && !found {
            if strncmpi_eq(message, job_name(i), 16) {
                job = i;
                found = true;
            }
            i += 1;
        }
        if !found {
            help(say, command);
            return -1;
        }
    }
    // Deny direct transformation into dummy jobs
    if job == JOB_KNIGHT2
        || job == JOB_CRUSADER2
        || job == JOB_WEDDING
        || job == JOB_XMAS
        || job == JOB_SUMMER
        || job == JOB_HANBOK
        || job == JOB_OKTOBERFEST
        || job == JOB_LORD_KNIGHT2
        || job == JOB_PALADIN2
        || job == JOB_BABY_KNIGHT2
        || job == JOB_BABY_CRUSADER2
        || job == JOB_STAR_GLADIATOR2
        || (JOB_RUNE_KNIGHT2..=JOB_MECHANIC_T2).contains(&job)
        || (JOB_BABY_RUNE_KNIGHT2..=JOB_BABY_MECHANIC2).contains(&job)
        || job == JOB_BABY_STAR_GLADIATOR2
        || job == JOB_STAR_EMPEROR2
        || job == JOB_BABY_STAR_EMPEROR2
        || job == JOB_SUMMER2
        || (JOB_WINDHAWK2..=JOB_IMPERIAL_GUARD2).contains(&job)
        || job == JOB_SKY_EMPEROR2
    {
        say(msg_txt(923)); // You can not change to this job by command.
        return 0;
    }
    if pcdb_checkid(job) {
        if skilltree::pc_jobchange(c, aid, job, upper) {
            say(msg_txt(12)); // Your job has been changed.
        } else {
            say(msg_txt(155)); // You are unable to change your job.
            return -1;
        }
    } else {
        help(say, command);
        return -1;
    }
    0
}

/// ACMD_FUNC(allskill): @allskill / @allskills / @skillall / @skillsall.
fn allskill(c: &Ctx, say: &dyn Fn(&str), aid: u32) -> i32 {
    skilltree::pc_allskillup(c, aid); // all skills
    if let Some(mut ch) = crate::pc::character_of(c, aid) {
        ch.skill_point = 0; // 0 skill points
        crate::status::put_char(c, &ch);
    }
    crate::status::clif::updatestatus(c, aid, crate::status::consts::SP_SKILLPOINT); // update
    say(msg_txt(76)); // All skills have been added to your skill tree.
    0
}

/// ACMD_FUNC(questskill): @questskill <#quest skill>.
fn questskill(c: &Ctx, say: &dyn Fn(&str), aid: u32, command: &str, message: &str) -> i32 {
    // `(skill_id = atoi(message)) <= 0` on a uint16: zero
    let skill_id = atoi(message) as u16;
    if message.is_empty() || skill_id == 0 {
        // also send a list of skills applicable to this command
        say(msg_txt(1027)); // Please enter a quest skill number.
        help(say, command);
        return -1;
    }
    if !sk::skill_db().exists(skill_id) {
        say(msg_txt(198)); // This skill number doesn't exist.
        return -1;
    }
    if !sk::skill_get_inf2(skill_id, &[sk::INF2_ISQUEST]) {
        say(msg_txt(197)); // This skill number doesn't exist or isn't a quest skill.
        return -1;
    }
    if crate::skill::checkskill(c, aid, skill_id) > 0 {
        say(msg_txt(196)); // You already have this quest skill.
        return -1;
    }
    skilltree::pc_skill(c, aid, skill_id, 1, crate::status::defs::ADDSKILL_PERMANENT);
    say(msg_txt(70)); // You have learned the skill.
    0
}

/// ACMD_FUNC(lostskill): @lostskill <#quest skill>.
fn lostskill(c: &Ctx, say: &dyn Fn(&str), aid: u32, command: &str, message: &str) -> i32 {
    let skill_id = atoi(message) as u16;
    if message.is_empty() || skill_id == 0 {
        // also send a list of skills applicable to this command
        say(msg_txt(1027)); // Please enter a quest skill number.
        help(say, command);
        return -1;
    }
    let sk_idx = sk::skill_get_index(skill_id);
    if sk_idx == 0 {
        say(msg_txt(198)); // This skill number doesn't exist.
        return -1;
    }
    if !sk::skill_get_inf2(skill_id, &[sk::INF2_ISQUEST]) {
        say(msg_txt(197)); // This skill number doesn't exist or isn't a quest skill.
        return -1;
    }
    if crate::skill::checkskill(c, aid, skill_id) == 0 {
        say(msg_txt(201)); // You don't have this quest skill.
        return -1;
    }
    let Some(mut s) = skilltree::Skills::load(c, aid) else { return -1 };
    let mut sl = s.at(sk_idx);
    sl.lv = 0;
    sl.flag = crate::status::defs::SKILL_FLAG_PERMANENT;
    s.put(sk_idx, sl);
    s.save(c);
    crate::clif_skill::clif_deleteskill(c, aid, skill_id, false);
    say(msg_txt(71)); // You have forgotten the skill.
    0
}

/// ACMD_FUNC(reset).
fn reset(c: &Ctx, say: &dyn Fn(&str), aid: u32) -> i32 {
    crate::status::progress::pc_resetstate(c, aid);
    skilltree::pc_resetskill(c, aid, 1);
    let name = crate::pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
    say(&format_msg(msg_txt(208), &[&name])); // '%s' skill and stats points reseted!
    0
}

/// ACMD_FUNC(resetstat).
fn resetstat(c: &Ctx, say: &dyn Fn(&str), aid: u32) -> i32 {
    crate::status::progress::pc_resetstate(c, aid);
    let name = crate::pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
    say(&format_msg(msg_txt(207), &[&name])); // '%s' stats points reset.
    0
}

/// ACMD_FUNC(resetskill).
fn resetskill(c: &Ctx, say: &dyn Fn(&str), aid: u32) -> i32 {
    skilltree::pc_resetskill(c, aid, 1);
    let name = crate::pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
    say(&format_msg(msg_txt(206), &[&name])); // '%s' skill points reset.
    0
}

/// ACMD_FUNC(skilltree): @skilltree <skill ID> <char name> — what the player still needs for a skill.
fn skilltree_cmd(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    // sscanf(message, "%6hu %23[^\n]", &skill_id, atcmd_player_name) != 2
    let b = crate::net::wire::latin1(message);
    let mut s = Scan::new(&b);
    let parsed = s.int(6).map(|v| v as u16).and_then(|id| {
        s.ws();
        s.set(23, b"\n").map(|name| (id, name))
    });
    let Some((skill_id, name)) = parsed.filter(|_| !message.is_empty()) else {
        say(msg_txt(1167)); // Usage: @skilltree <skill ID> <char name>
        return -1;
    };
    let Some(pl) = crate::pc::nick2sd(c, &name, true) else {
        say(msg_txt(3)); // Character not found.
        return -1;
    };
    let Some(ch) = crate::pc::character_of(c, pl.id) else { return -1 };
    let Some(job) = skilltree::normalize_job_of(c, pl.id) else { return -1 };
    let cc = pc_mapid2jobid(job, ch.sex as i32);
    let basic = crate::skill::checkskill(c, pl.id, sk::NV_BASIC);
    say(&format_msg(msg_txt(1168), &[&job_name(cc), &basic])); // Player is using %s skill tree (%d basic points).
    let Some(entry) = skill_tree_db().get_skill_data(cc, skill_id) else {
        say(msg_txt(1169)); // The player cannot use that skill.
        return 0;
    };
    let mut meets = true;
    for &(need_id, need_lv) in &entry.need {
        // (rAthena checks the invoking player's own levels here: pc_checkskill(sd, …))
        if (crate::skill::checkskill(c, aid, need_id) as u16) < need_lv {
            say(&format_msg(msg_txt(1170), &[&need_lv, &sk::skill_get_desc(need_id)])); // Player requires level %d of skill %s.
            meets = false;
        }
    }
    if meets {
        say(msg_txt(1171)); // The player meets all the requirements for that skill.
    }
    0
}
