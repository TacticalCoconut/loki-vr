//! Script commands of the skill tree and job progression (script.cpp): jobchange, eaclass, roclass, changebase,
//! resetlvl, resetstatus, resetskill, skillpointcount, getskilllist, needed_status_point, needed_trait_point,
//! recalculatestat, jobcanentermap, and the Plagiarism / Reproduce slot commands plagiarizeskill and
//! plagiarizeskillreset.

use super::*;
use crate::db::jobs::*;
use crate::script::reg::{store_set_num, Store};
use crate::status::consts::{SP_CRT, SP_POW};
use crate::status::defs::SCO_FORCE;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "jobchange" => jobchange,
        "eaclass" => eaclass,
        "roclass" => roclass,
        "changebase" => changebase,
        "resetlvl" => resetlvl,
        "resetstatus" => resetstatus,
        "resetskill" => resetskill,
        "resetfeel" => resetfeel,
        "resethate" => resethate,
        "skillpointcount" => skillpointcount,
        "getskilllist" => getskilllist,
        "needed_status_point" => needed_status_point,
        "needed_trait_point" => needed_trait_point,
        "recalculatestat" => recalculatestat,
        "jobcanentermap" => jobcanentermap,
        "plagiarizeskill" => plagiarizeskill,
        "plagiarizeskillreset" => plagiarizeskillreset,
        _ => return None,
    })
}

fn ctx<'a>(rt: &'a Rt<'a>) -> &'a crate::world::Ctx<'a> {
    rt.c.expect("buildins run with a world context")
}

/// conf/battle/client.conf `wedding_modifydisplay: no`.
fn wedding_modifydisplay() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::WEDDING_MODIFYDISPLAY) != 0
}

/// BUILDIN_FUNC(jobchange): jobchange <job>{,<upper flag>{,<char_id>}};
fn jobchange(rt: &Rt, st: &mut State) -> i32 {
    let job = getnum(rt, st, 2);
    let mut upper = -1;
    if st.hasdata(3) {
        upper = getnum(rt, st, 3);
    }
    if pcdb_checkid(job) {
        let Some(aid) = charid2sd(rt, st, 4, "buildin_jobchange") else { return SCRIPT_CMD_SUCCESS };
        crate::skilltree::pc_jobchange(ctx(rt), aid, job, upper);
    }
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(eaclass): eaclass({<job>{,<char_id>}}) — the MAPID of a job (the player's own by default).
fn eaclass(rt: &Rt, st: &mut State) -> i32 {
    let class_ = if st.hasdata(2) {
        getnum(rt, st, 2)
    } else {
        let Some(aid) = charid2sd(rt, st, 3, "buildin_eaclass") else {
            st.pushint(-1);
            return SCRIPT_CMD_SUCCESS;
        };
        crate::pc::character_of(ctx(rt), aid).map(|ch| ch.class as i32).unwrap_or(0)
    };
    st.pushint(pc_jobid2mapid(class_ as u16) as i64);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(roclass): roclass(<job MAPID>{,<gender>}) — the job id of a MAPID.
fn roclass(rt: &Rt, st: &mut State) -> i32 {
    let class_ = getnum(rt, st, 2);
    let sex = if st.hasdata(3) {
        getnum(rt, st, 3)
    } else {
        let sd = if st.rid != 0 { rid2sd(rt, st, "buildin_roclass") } else { None };
        match sd.and_then(|aid| crate::pc::character_of(ctx(rt), aid)) {
            Some(ch) => ch.sex as i32,
            None => 1, // SEX_MALE: Just use male when not found.
        }
    };
    st.pushint(pc_mapid2jobid(class_ as i64 as u64, sex) as i64);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(changebase): changebase <job ID>{,<account ID>}; — the character's look only.
fn changebase(rt: &Rt, st: &mut State) -> i32 {
    use crate::pc_equip::{clif_changelook, LOOK_BASE, LOOK_BODY2, LOOK_CLOTHES_COLOR, LOOK_WEAPON};
    let Some(aid) = accid2sd(rt, st, 3, "buildin_changebase") else { return SCRIPT_CMD_SUCCESS };
    let c = ctx(rt);
    let vclass = getnum(rt, st, 2);
    if vclass == JOB_WEDDING {
        let baby = crate::status::data::sd_get(c, aid).map(|s| s.class_ & JOBL_BABY != 0).unwrap_or(false);
        // Do not show the wedding sprites; Baby classes screw up when showing wedding sprites. [Skotlex]
        if !wedding_modifydisplay() || baby {
            return SCRIPT_CMD_SUCCESS;
        }
    }
    let base = crate::unit::get(c, aid).map(|u| u.vd.class).unwrap_or(0);
    if crate::atcmd_disguise::disguise_of(c, aid) == 0 && vclass != base {
        crate::skilltree::status_set_viewdata_pc(c, aid, vclass);
        // Updated client view. Base, Weapon and Cloth Colors.
        let Some(u) = crate::unit::get(c, aid) else { return SCRIPT_CMD_SUCCESS };
        clif_changelook(c, aid, LOOK_BASE, u.vd.class);
        let weapon = crate::pc::character_of(c, aid).map(|ch| ch.weapon as i32).unwrap_or(0);
        clif_changelook(c, aid, LOOK_WEAPON, weapon);
        let Some(u) = crate::unit::get(c, aid) else { return SCRIPT_CMD_SUCCESS };
        if u.vd.clothes_color != 0 {
            clif_changelook(c, aid, LOOK_CLOTHES_COLOR, u.vd.clothes_color);
        }
        if u.vd.body2 != 0 {
            clif_changelook(c, aid, LOOK_BODY2, u.vd.body2);
        }
        crate::clif_skill::clif_skillinfoblock(c, aid);
    }
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(resetlvl): resetlvl <action type>{,<char_id>};
fn resetlvl(rt: &Rt, st: &mut State) -> i32 {
    let type_ = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 3, "buildin_resetlvl") else { return SCRIPT_CMD_FAILURE };
    crate::skilltree::pc_resetlvl(ctx(rt), aid, type_);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(resetstatus): resetstatus({<char_id>});
fn resetstatus(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 2, "buildin_resetstatus") else { return SCRIPT_CMD_FAILURE };
    crate::status::progress::pc_resetstate(ctx(rt), aid);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(resetskill): resetskill({<char_id>});
fn resetskill(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 2, "buildin_resetskill") else { return SCRIPT_CMD_FAILURE };
    crate::skilltree::pc_resetskill(ctx(rt), aid, 1);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(resetfeel): resetfeel({<char_id>}) — a Star Gladiator's Feeling maps are forgotten (pc_resetfeel).
fn resetfeel(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 2, "buildin_resetfeel") else { return SCRIPT_CMD_FAILURE };
    let c = ctx(rt);
    if crate::status::data::sd_get(c, aid).map(|s| s.class_ & MAPID_SECONDMASK) != Some(MAPID_STAR_GLADIATOR) {
        return SCRIPT_CMD_FAILURE;
    }
    crate::skilltree::pc_resetfeel(c, aid);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(resethate): resethate({<char_id>}) — a Star Gladiator's Hatred targets are forgotten (pc_resethate).
fn resethate(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 2, "buildin_resethate") else { return SCRIPT_CMD_FAILURE };
    let c = ctx(rt);
    if crate::status::data::sd_get(c, aid).map(|s| s.class_ & MAPID_SECONDMASK) != Some(MAPID_STAR_GLADIATOR) {
        return SCRIPT_CMD_FAILURE;
    }
    crate::skilltree::pc_resethate(c, aid);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(skillpointcount): skillpointcount({<char_id>}) — the unspent points plus the spent ones.
fn skillpointcount(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 2, "buildin_skillpointcount") else { return SCRIPT_CMD_FAILURE };
    let c = ctx(rt);
    let skill_point = crate::pc::character_of(c, aid).map(|ch| ch.skill_point as i64).unwrap_or(0);
    let spent = crate::skilltree::pc_resetskill(c, aid, 2) as i64;
    st.pushint(skill_point + spent);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(getskilllist): getskilllist({<char_id>}) — @skilllist_id[] / _lv[] / _flag[] and @skilllist_count.
fn getskilllist(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 2, "buildin_getskilllist") else { return SCRIPT_CMD_FAILURE };
    let c = ctx(rt);
    let Some(s) = crate::skilltree::Skills::load(c, aid) else { return SCRIPT_CMD_FAILURE };
    let store = Store::CharTmp(aid);
    let mut j: u32 = 0;
    for (_, sl) in s.snapshot() {
        if sl.id > 0 && sl.lv > 0 {
            store_set_num(rt, st, &store, "@skilllist_id", j, sl.id as i64);
            store_set_num(rt, st, &store, "@skilllist_lv", j, sl.lv as i64);
            store_set_num(rt, st, &store, "@skilllist_flag", j, sl.flag as i64);
            j += 1;
        }
    }
    store_set_num(rt, st, &store, "@skilllist_count", 0, j as i64);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(needed_status_point): needed_status_point(<type>,<val>{,<char id>}).
fn needed_status_point(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 4, "buildin_needed_status_point") else { return SCRIPT_CMD_FAILURE };
    let type_ = getnum(rt, st, 2);
    let val = getnum(rt, st, 3);
    st.pushint(crate::status::progress::pc_need_status_point(ctx(rt), aid, type_, val) as i64);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(needed_trait_point): needed_trait_point(<type>,<val>{,<char id>}).
fn needed_trait_point(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 4, "buildin_needed_trait_point") else { return SCRIPT_CMD_FAILURE };
    let type_ = getnum(rt, st, 2);
    if !(SP_POW..=SP_CRT).contains(&type_) {
        log::error!("buildin_needed_trait_point: Unknown trait type {type_}");
        return SCRIPT_CMD_FAILURE;
    }
    let val = getnum(rt, st, 3);
    st.pushint(crate::status::progress::pc_need_trait_point(ctx(rt), aid, type_, val) as i64);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(recalculatestat): status_calc_pc(sd, SCO_FORCE).
fn recalculatestat(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_recalculatestat") else { return SCRIPT_CMD_FAILURE };
    crate::status::calc_pc(ctx(rt), aid, SCO_FORCE);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(jobcanentermap): jobcanentermap("<mapname>"{,<JobID>}).
fn jobcanentermap(rt: &Rt, st: &mut State) -> i32 {
    let mapname = lat(&getstr(rt, st, 2));
    let c = ctx(rt);
    // mapindex_name2id / map_mapindex2mapid: every map of the index is on this server
    let Some(m) = crate::data::maps().id_of(&mapname) else {
        log::debug!("mapindex_name2id: Map \"{mapname}\" not found in index list!");
        st.pushint(0); // Invalid map
        return SCRIPT_CMD_FAILURE;
    };
    let (jobid, group_lv) = if st.hasdata(3) {
        (getnum(rt, st, 3), 0)
    } else {
        let Some(aid) = rid2sd(rt, st, "buildin_jobcanentermap") else {
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        };
        let class_ = crate::pc::character_of(c, aid).map(|ch| ch.class as i32).unwrap_or(0);
        (class_, crate::pc::group_level(c, aid))
    };
    st.pushint(crate::pc::job_can_entermap(c, jobid, m, group_lv) as i64);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(plagiarizeskill) (script.cpp:10342-10350): plagiarizeskill <skill id>,<level>; — the attached player's
/// Plagiarism or Reproduce slot takes the skill (pc_skill_plagiarism: whichever skill_isCopyable allows); pushes
/// whether it did. Both arguments are the uint16 the int converts to. Without a player nothing is pushed and the
/// script ends (script_rid2sd).
fn plagiarizeskill(rt: &Rt, st: &mut State) -> i32 {
    if let Some(aid) = rid2sd(rt, st, "buildin_plagiarizeskill") {
        let skill_id = getnum(rt, st, 2) as u16;
        let skill_lv = getnum(rt, st, 3) as u16;
        let ok = crate::skilltree::pc_skill_plagiarism(ctx(rt), aid, skill_id, skill_lv);
        st.pushint(ok as i64);
    }
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(plagiarizeskillreset) (script.cpp:10352-10360): plagiarizeskillreset <type>; — clears the attached
/// player's plagiarised (1) or reproduced (2) skill; pc_skill_plagiarism_reset takes the uint8 the int converts to
/// (another type: "Unknown type" and 0). Pushes the result.
fn plagiarizeskillreset(rt: &Rt, st: &mut State) -> i32 {
    if let Some(aid) = rid2sd(rt, st, "buildin_plagiarizeskillreset") {
        let type_ = getnum(rt, st, 2) as u8;
        let ok = crate::skilltree::pc_skill_plagiarism_reset(ctx(rt), aid, type_);
        st.pushint(ok as i64);
    }
    SCRIPT_CMD_SUCCESS
}

#[cfg(test)]
mod tests {
    use crate::script::compiler;
    use crate::script::vm::*;
    use crate::script::{code, vm};

    /// Runs a script without a world (the commands used need no player) and returns its `.@` numbers.
    fn run(src: &str) -> State {
        let names: std::collections::HashSet<Vec<u8>> = std::collections::HashSet::new();
        let is_fn = compiler::userfunc_set(&names);
        let code_ = code::with_table(|t| compiler::parse_script(t, src.as_bytes(), "skilltree_test", 1, 0, &is_fn)).expect("compiles").expect("not empty");
        let key = CodeKey::Npc(2_000_001);
        code::insert(key.clone(), code_);
        let buf = code::get(None, &key).unwrap();
        let mut st = State::new(key, buf, 0, 0, 0);
        vm::execute(&Rt::none(), &mut st);
        st
    }

    fn num(st: &State, name: &str) -> i64 {
        match st.scopes.get(&1).and_then(|s| s.vars.get(&(name.to_ascii_lowercase().into_bytes(), 0))) {
            Some(Val::Num(n)) => *n,
            None => 0,
            other => panic!("{name} is {other:?}"),
        }
    }

    #[test]
    fn eaclass_and_roclass() {
        let st = run(
            r#"{
            .@a = eaclass(Job_Knight);
            .@b = roclass(EAJ_KNIGHT, 1);
            .@c = roclass(EAJ_BARDDANCER, 0);
            .@d = roclass(EAJ_BARDDANCER, 1);
            .@e = eaclass(Job_Lord_Knight) == (EAJ_KNIGHT|EAJL_UPPER);
            .@f = roclass(eaclass(Job_Baby_Crusader), 1);
            end;
        }"#,
        );
        assert_eq!(num(&st, ".@a") as u64, crate::db::jobs::MAPID_KNIGHT);
        assert_eq!(num(&st, ".@b"), crate::db::jobs::JOB_KNIGHT as i64);
        assert_eq!(num(&st, ".@c"), crate::db::jobs::JOB_DANCER as i64);
        assert_eq!(num(&st, ".@d"), crate::db::jobs::JOB_BARD as i64);
        assert_eq!(num(&st, ".@e"), 1);
        assert_eq!(num(&st, ".@f"), crate::db::jobs::JOB_BABY_CRUSADER as i64);
    }
}
