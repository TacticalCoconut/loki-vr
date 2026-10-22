//! Script commands of the extras systems (script.cpp): the achievements (achievementadd, achievementremove,
//! achievementinfo, achievementcomplete, achievementexists, achievementupdate), the clans (clan_join, clan_leave),
//! the reputation (reputationui, set_reputation_points, get_reputation_points, add_reputation_points) and fame
//! (addfame, getfame, getfamerank).

use super::*;
use crate::achievement::{self as ach, ACHIEVEINFO_COMPLETE, ACHIEVEINFO_COMPLETEDATE, ACHIEVEINFO_COUNT1, ACHIEVEINFO_COUNT10, ACHIEVEINFO_GOTREWARD};
use crate::db::misc::achievement::achievement_db;
use crate::reputation::{cap_value, clif_reputation_open, clif_reputation_type, pc_readreg2, pc_setreg2};
use crate::script::vm::*;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "achievementadd" => achievementadd,
        "achievementremove" => achievementremove,
        "achievementinfo" => achievementinfo,
        "achievementcomplete" => achievementcomplete,
        "achievementexists" => achievementexists,
        "achievementupdate" => achievementupdate,
        "clan_join" => clan_join,
        "clan_leave" => clan_leave,
        "set_reputation_points" => set_reputation_points,
        "reputationui" => reputationui,
        "get_reputation_points" => get_reputation_points,
        "add_reputation_points" => add_reputation_points,
        "addfame" => addfame,
        "getfame" => getfame,
        "getfamerank" => getfamerank,
        _ => return None,
    })
}

/// sd->state.pc_loaded.
fn pc_loaded(rt: &Rt, aid: u32) -> bool {
    rt.c.and_then(|c| crate::pc::get(c, aid)).map(|p| p.pc_loaded).unwrap_or(false)
}

fn exists(id: i32) -> bool {
    achievement_db().exists(id as u32)
}

/// achievementadd(<achievement ID>{,<char ID>}): adds the achievement to the log.
fn achievementadd(rt: &Rt, st: &mut State) -> i32 {
    let achievement_id = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 3, "buildin_achievementadd") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    if !exists(achievement_id) {
        log::warn!("buildin_achievementadd: Achievement '{achievement_id}' doesn't exist.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    if !pc_loaded(rt, aid) {
        // Simply ignore it on the first call, because the status will be recalculated after loading anyway
        return SCRIPT_CMD_SUCCESS;
    }
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let ok = ach::achievement_add(c, aid, achievement_id).is_some();
    st.pushint(ok as i64);
    SCRIPT_CMD_SUCCESS
}

/// achievementremove(<achievement ID>{,<char ID>}).
fn achievementremove(rt: &Rt, st: &mut State) -> i32 {
    let achievement_id = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 3, "buildin_achievementremove") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    if !exists(achievement_id) {
        log::warn!("buildin_achievementremove: Achievement '{achievement_id}' doesn't exist.");
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    }
    if !pc_loaded(rt, aid) {
        return SCRIPT_CMD_SUCCESS;
    }
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let ok = ach::achievement_remove(c, aid, achievement_id);
    st.pushint(ok as i64);
    SCRIPT_CMD_SUCCESS
}

/// achievementinfo(<achievement ID>,<type>{,<char ID>}): achievement_check_progress.
fn achievementinfo(rt: &Rt, st: &mut State) -> i32 {
    let achievement_id = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 4, "buildin_achievementinfo") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    if !exists(achievement_id) {
        log::warn!("buildin_achievementinfo: Achievement '{achievement_id}' doesn't exist.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    if !pc_loaded(rt, aid) {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    }
    let type_ = getnum(rt, st, 3);
    let v = rt.c.map(|c| ach::achievement_check_progress(c, aid, achievement_id, type_)).unwrap_or(-2);
    st.pushint(v as i64);
    SCRIPT_CMD_SUCCESS
}

/// achievementcomplete(<achievement ID>{,<char ID>}): completes it, ignoring its requirements.
fn achievementcomplete(rt: &Rt, st: &mut State) -> i32 {
    let achievement_id = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 3, "buildin_achievementcomplete") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    if !exists(achievement_id) {
        log::warn!("buildin_achievementcomplete: Achievement '{achievement_id}' doesn't exist.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    if !pc_loaded(rt, aid) {
        return SCRIPT_CMD_SUCCESS;
    }
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    if ach::achievement_check_progress(c, aid, achievement_id, ACHIEVEINFO_COMPLETE) == -1 {
        ach::achievement_add(c, aid, achievement_id);
    }
    ach::achievement_update_achievement(c, aid, achievement_id, true);
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// achievementexists(<achievement ID>{,<char ID>}): the player has it, completed.
fn achievementexists(rt: &Rt, st: &mut State) -> i32 {
    let achievement_id = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 3, "buildin_achievementexists") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    if !exists(achievement_id) {
        log::warn!("buildin_achievementexists: Achievement '{achievement_id}' doesn't exist.");
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    }
    if !pc_loaded(rt, aid) {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    }
    let done = rt.c.map(|c| ach::achievement_check_progress(c, aid, achievement_id, ACHIEVEINFO_COMPLETE) == 1).unwrap_or(false);
    st.pushint(done as i64);
    SCRIPT_CMD_SUCCESS
}

/// achievementupdate(<achievement ID>,<type>,<value>{,<char ID>}): sets a counter, the completion date or the
/// reward date directly, then achievement_update_achievement(…, false).
fn achievementupdate(rt: &Rt, st: &mut State) -> i32 {
    let achievement_id = getnum(rt, st, 2);
    let type_ = getnum(rt, st, 3);
    let value = getnum(rt, st, 4);
    let Some(aid) = charid2sd(rt, st, 5, "buildin_achievementupdate") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    if !exists(achievement_id) {
        log::warn!("buildin_achievementupdate: Achievement '{achievement_id}' doesn't exist.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    if !pc_loaded(rt, aid) {
        return SCRIPT_CMD_SUCCESS;
    }
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    if ach::achievement_check_progress(c, aid, achievement_id, ACHIEVEINFO_COMPLETE) == -1 {
        ach::achievement_add(c, aid, achievement_id);
    }
    let set = |f: &dyn Fn(&mut ach::Achievement)| -> bool { ach::with_entry(c, aid, achievement_id, f) };
    let found = if (ACHIEVEINFO_COUNT1..=ACHIEVEINFO_COUNT10).contains(&type_) {
        let k = (type_ - 1) as usize;
        Some(set(&|a| {
            if let Some(v) = a.count.get_mut(k) {
                *v = value;
            }
        }))
    } else if type_ == ACHIEVEINFO_COMPLETE || type_ == ACHIEVEINFO_COMPLETEDATE {
        Some(set(&|a| a.completed = value as i64))
    } else if type_ == ACHIEVEINFO_GOTREWARD {
        Some(set(&|a| a.rewarded = value as i64))
    } else {
        None
    };
    match found {
        Some(false) => {
            st.pushint(0);
            SCRIPT_CMD_SUCCESS
        }
        None => {
            // (the entry exists) an unknown type
            if ach::achievement_check_progress(c, aid, achievement_id, ACHIEVEINFO_COMPLETE) == -1 {
                st.pushint(0);
                return SCRIPT_CMD_SUCCESS;
            }
            log::warn!("buildin_achievementupdate: Unknown type '{type_}'.");
            st.pushint(0);
            SCRIPT_CMD_FAILURE
        }
        Some(true) => {
            ach::achievement_update_achievement(c, aid, achievement_id, false);
            st.pushint(1);
            SCRIPT_CMD_SUCCESS
        }
    }
}

// ------------------------------------------------------------------------------------------------ clans

/// clan_join(<clan id>{,<char id>}).
fn clan_join(rt: &Rt, st: &mut State) -> i32 {
    let clan_id = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 3, "buildin_clan_join") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(p) = crate::pc::get(c, aid) else { return SCRIPT_CMD_SUCCESS };
    let ok = crate::clan::clan_member_join(c, aid, clan_id, p.account_id, p.char_id);
    st.pushint(ok as i64);
    SCRIPT_CMD_SUCCESS
}

/// clan_leave({<char id>}).
fn clan_leave(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 2, "buildin_clan_leave") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(ch) = crate::pc::character_of(c, aid) else { return SCRIPT_CMD_SUCCESS };
    let ok = crate::clan::clan_member_leave(c, aid, ch.clan_id as i32, ch.account_id, ch.id);
    st.pushint(ok as i64);
    SCRIPT_CMD_SUCCESS
}

// ------------------------------------------------------------------------------------------------ reputation

/// set_reputation_points(<type>,<points>{,<char id>}).
fn set_reputation_points(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 4, "buildin_set_reputation_points") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let type_ = getnum64(rt, st, 2);
    let Some(reputation) = crate::db::jobs::reputation_find(type_) else {
        log::error!("buildin_set_reputation_points: Unknown reputation type {type_}.");
        return SCRIPT_CMD_FAILURE;
    };
    let points = cap_value(getnum64(rt, st, 3), reputation.minimum, reputation.maximum);
    if !pc_setreg2(c, aid, &reputation.variable, points) {
        return SCRIPT_CMD_FAILURE;
    }
    clif_reputation_type(c, aid, type_, points);
    SCRIPT_CMD_SUCCESS
}

/// reputationui({<group id>{,<reputation id>{,<char id>}}}): ZC_REPUTE_OPEN.
fn reputationui(rt: &Rt, st: &mut State) -> i32 {
    let aid = if st.hasdata(4) {
        match charid2sd(rt, st, 4, "buildin_reputationui") {
            Some(a) => a,
            None => {
                st.state = END;
                return SCRIPT_CMD_FAILURE;
            }
        }
    } else {
        match rid2sd(rt, st, "buildin_reputationui") {
            Some(a) => a,
            None => {
                // Player not attached!
                st.state = END;
                return SCRIPT_CMD_FAILURE;
            }
        }
    };
    let mut group_id: i64 = 0;
    let mut reputation_id: i64 = 0;
    if st.hasdata(2) {
        group_id = getnum64(rt, st, 2);
        if group_id < 0 {
            log::error!("buildin_reputationui: Unknown GroupID {group_id}.");
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
    }
    if st.hasdata(3) {
        reputation_id = getnum64(rt, st, 3);
        if reputation_id != 0 && crate::db::jobs::reputation_find(reputation_id).is_none() {
            log::error!("buildin_reputationui: Unknown reputation type {reputation_id}.");
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
    }
    if let Some(c) = rt.c {
        clif_reputation_open(c, aid, group_id as u64, reputation_id as u64);
    }
    SCRIPT_CMD_SUCCESS
}

/// get_reputation_points(<type>{,<char id>}).
fn get_reputation_points(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_get_reputation_points") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let type_ = getnum64(rt, st, 2);
    let Some(reputation) = crate::db::jobs::reputation_find(type_) else {
        log::error!("buildin_set_reputation_points: Unknown reputation type {type_}.");
        return SCRIPT_CMD_FAILURE;
    };
    let points = cap_value(pc_readreg2(c, aid, &reputation.variable), reputation.minimum, reputation.maximum);
    st.pushint(points);
    SCRIPT_CMD_SUCCESS
}

/// add_reputation_points(<type>,<points>{,<char id>}).
fn add_reputation_points(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 4, "buildin_add_reputation_points") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let type_ = getnum64(rt, st, 2);
    let Some(reputation) = crate::db::jobs::reputation_find(type_) else {
        log::error!("buildin_set_reputation_points: Unknown reputation type {type_}.");
        return SCRIPT_CMD_FAILURE;
    };
    let points = pc_readreg2(c, aid, &reputation.variable).wrapping_add(getnum64(rt, st, 3));
    let points = cap_value(points, reputation.minimum, reputation.maximum);
    if !pc_setreg2(c, aid, &reputation.variable, points) {
        return SCRIPT_CMD_FAILURE;
    }
    clif_reputation_type(c, aid, type_, points);
    SCRIPT_CMD_SUCCESS
}

// ------------------------------------------------------------------------------------------------ fame

/// addfame(<points>{,<char id>}).
fn addfame(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_addfame") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let count = getnum(rt, st, 2);
    if !crate::fame::pc_addfame(c, aid, count) {
        return SCRIPT_CMD_FAILURE;
    }
    SCRIPT_CMD_SUCCESS
}

/// getfame({<char id>}).
fn getfame(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 2, "buildin_getfame") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    st.pushint(crate::fame::fame_of(c, aid) as i64);
    SCRIPT_CMD_SUCCESS
}

/// getfamerank({<char id>}).
fn getfamerank(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 2, "buildin_getfamerank") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    st.pushint(crate::fame::famerank_of(c, aid) as i64);
    SCRIPT_CMD_SUCCESS
}
