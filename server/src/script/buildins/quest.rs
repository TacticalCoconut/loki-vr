//! Script commands of the quest system (script.cpp "Questlog script commands"): questinfo, questinfo_refresh,
//! setquest, erasequest, completequest, changequest, checkquest, isbegin_quest, showevent, open_quest_ui — and
//! achievement_condition, the command questinfo conditions are wrapped in (achievement.cpp evaluates them).

use super::*;
use crate::quest::{self, HAVEQUEST, Q_COMPLETE, QMARK_MAX, QMARK_NONE, QTYPE_NONE};
use crate::npc::npc;
use crate::script::vm::*;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "questinfo" => questinfo,
        "questinfo_refresh" => questinfo_refresh,
        "setquest" => setquest,
        "erasequest" => erasequest,
        "completequest" => completequest,
        "changequest" => changequest,
        "checkquest" => checkquest,
        "isbegin_quest" => isbegin_quest,
        "showevent" => showevent,
        "open_quest_ui" => open_quest_ui,
        "achievement_condition" => achievement_condition,
        _ => return None,
    })
}

/// script_reportfunc: the command and its arguments (debug output).
fn reportfunc(st: &State) {
    if !st.hasdata(0) {
        return;
    }
    let params = st.lastdata() - 1;
    if params > 0 {
        log::debug!("Function: {} ({} parameter{}):", st.funcname, params, if params == 1 { "" } else { "s" });
        for i in 2..=st.lastdata() {
            reportdata(st.data(i));
        }
    } else {
        log::debug!("Function: {} (no parameters)", st.funcname);
    }
}

/// questinfo <Icon>{,<Map Mark Color>{,<condition>}}; — in an NPC's OnInit: the NPC shows a quest marker to the
/// players for whom the condition holds.
fn questinfo(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let nd = c.db().npc().id().find(st.oid);
    let Some(nd) = nd.filter(|nd| nd.map != -1) else {
        log::error!("buildin_questinfo: No NPC attached.");
        return SCRIPT_CMD_FAILURE;
    };
    let mut icon = getnum(rt, st, 2);
    // PACKETVER >= 20120410 (>= 20170315: the Warg icons were replaced)
    match icon {
        quest::QTYPE_QUEST
        | quest::QTYPE_QUEST2
        | quest::QTYPE_JOB
        | quest::QTYPE_JOB2
        | quest::QTYPE_EVENT
        | quest::QTYPE_EVENT2
        | quest::QTYPE_CLICKME
        | quest::QTYPE_DAILYQUEST
        | quest::QTYPE_EVENT3
        | quest::QTYPE_JOBQUEST
        | quest::QTYPE_JUMPING_PORING => {} // Leave everything as it is
        _ => icon = QTYPE_NONE,             // Default to nothing if icon id is invalid.
    }
    let mut color = QMARK_NONE;
    if st.hasdata(3) {
        color = getnum(rt, st, 3);
        if !(QMARK_NONE..QMARK_MAX).contains(&color) {
            log::warn!("buildin_questinfo: invalid color '{color}', defaulting to QMARK_NONE.");
            reportfunc(st);
            color = QMARK_NONE;
        }
    }
    let mut condition = None;
    if st.hasdata(4) {
        let s = lat(&getstr(rt, st, 4));
        let s = if !s.contains("achievement_condition") { format!("achievement_condition( {s} );") } else { s };
        // parse_script(condition, "questinfoparsing", 0, SCRIPT_IGNORE_EXTERNAL_BRACKETS)
        if crate::script::code::get(Some(c), &quest::condition_key(&s)).is_none() {
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
        condition = Some(s);
    }
    quest::add_questinfo(c, nd.id, nd.map as u16, icon, color, condition);
    SCRIPT_CMD_SUCCESS
}

/// questinfo_refresh {<char_id>};
fn questinfo_refresh(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 2, "buildin_questinfo_refresh") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    quest::pc_show_questinfo(c, aid);
    SCRIPT_CMD_SUCCESS
}

/// setquest <ID>{,<char_id>};
fn setquest(rt: &Rt, st: &mut State) -> i32 {
    let quest_id = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 3, "buildin_setquest") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    if quest::quest_add(c, aid, quest_id) == -1 {
        reportsrc(rt, st);
        reportfunc(st);
    }
    // 20120410 or 20090218 ? no reason that shouldn't work for 2009
    quest::pc_show_questinfo(c, aid);
    SCRIPT_CMD_SUCCESS
}

/// erasequest <ID>{,<char_id>};
fn erasequest(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_erasequest") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let quest_id = getnum(rt, st, 2);
    if quest::quest_delete(c, aid, quest_id) == -1 {
        reportsrc(rt, st);
        reportfunc(st);
    }
    quest::pc_show_questinfo(c, aid);
    SCRIPT_CMD_SUCCESS
}

/// completequest <ID>{,<char_id>};
fn completequest(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_completequest") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let quest_id = getnum(rt, st, 2);
    quest::quest_update_status(c, aid, quest_id, Q_COMPLETE);
    // 20120410 or 20090218
    quest::pc_show_questinfo(c, aid);
    SCRIPT_CMD_SUCCESS
}

/// changequest <ID>,<ID2>{,<char_id>};
fn changequest(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 4, "buildin_changequest") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let qid1 = getnum(rt, st, 2);
    let qid2 = getnum(rt, st, 3);
    if quest::quest_change(c, aid, qid1, qid2) == -1 {
        reportsrc(rt, st);
        reportfunc(st);
    }
    // 20120410 or 20090218
    quest::pc_show_questinfo(c, aid);
    SCRIPT_CMD_SUCCESS
}

/// checkquest(<ID>{,PLAYTIME|HUNTING{,<char_id>}})
fn checkquest(rt: &Rt, st: &mut State) -> i32 {
    let mut kind = HAVEQUEST;
    if st.hasdata(3) {
        kind = getnum(rt, st, 3);
    }
    let Some(aid) = charid2sd(rt, st, 4, "buildin_checkquest") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let quest_id = getnum(rt, st, 2);
    // (e_quest_check_type)script_getnum: the enum is uint8
    st.pushint(quest::quest_check(c, aid, quest_id, kind as u8 as i32) as i64);
    SCRIPT_CMD_SUCCESS
}

/// isbegin_quest(<ID>{,<char_id>}): 0 (not taken), 1 (active / inactive) or 2 (complete).
fn isbegin_quest(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_isbegin_quest") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let quest_id = getnum(rt, st, 2);
    let i = quest::quest_check(c, aid, quest_id, HAVEQUEST);
    st.pushint((i + (i < 1) as i32) as i64);
    SCRIPT_CMD_SUCCESS
}

/// showevent <icon>{,<mark color>{,<char_id>}}: shows a marker on the running NPC to one player.
fn showevent(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 4, "buildin_showevent") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(nd) = c.db().npc().id().find(st.oid) else { return SCRIPT_CMD_SUCCESS };
    let mut color = QMARK_NONE;
    let mut icon = getnum(rt, st, 2);
    if st.hasdata(3) {
        color = getnum(rt, st, 3);
        if !(QMARK_NONE..QMARK_MAX).contains(&color) {
            log::warn!("buildin_showevent: Invalid color '{color}', defaulting to QMARK_NONE.");
            reportfunc(st);
            color = QMARK_NONE;
        }
    }
    // PACKETVER >= 20120410
    if icon < 0 || (icon > 8 && icon != QTYPE_NONE) || icon == 7 {
        icon = QTYPE_NONE; // Default to nothing if icon id is invalid.
    }
    quest::clif_quest_show_event(c, aid, nd.id, icon, color);
    SCRIPT_CMD_SUCCESS
}

/// open_quest_ui {<quest ID>,{<char ID>}}: opens the client's quest window (at a quest).
fn open_quest_ui(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_open_quest_ui") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let quest_id = if st.hasdata(2) { getnum(rt, st, 2) } else { 0 };
    if quest_id != 0 {
        let (log, avail) = quest::quest_log(c, aid);
        let avail = (avail as usize).min(log.len());
        if !log[..avail].iter().any(|e| e.quest_id == quest_id) {
            log::warn!("buildin_open_quest_ui: Character {} doesn't have quest {quest_id}.", crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0));
        }
    }
    quest::clif_ui_open_quest(c, aid, quest_id);
    SCRIPT_CMD_SUCCESS
}

/// achievement_condition(<expr>): what a questinfo / achievement condition script calls; it keeps the state alive
/// (RERUNLINE) so achievement_check_condition can read the argument back.
fn achievement_condition(_rt: &Rt, st: &mut State) -> i32 {
    // Push what we get from the script
    st.pushint(2);
    // Otherwise the script is freed afterwards
    st.state = RERUNLINE;
    SCRIPT_CMD_SUCCESS
}
