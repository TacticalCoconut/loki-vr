//! Script commands of the status system (script.cpp): bonus / bonus2 … bonus5, autobonus / autobonus2 / autobonus3,
//! skill / addtoskill (item-granted skills), bonus_script / bonus_script_clear, heal / healap / itemheal /
//! percentheal, statusup / statusup2 / traitstatusup / traitstatusup2, getexp, sc_start / sc_start2 / sc_start4,
//! sc_end, sc_end_class, getstatus, getrandomoptinfo and montransform (transform / active_transform).

use super::*;
use crate::script::vm::*;
use crate::status::consts::*;
use crate::status::defs::*;
use crate::status::{bonus, calc, data, hp, progress, sc};

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "bonus" => bonus_,
        "autobonus" => autobonus,
        "autobonus2" => autobonus2,
        "autobonus3" => autobonus3,
        "skill" => skill,
        "bonus_script" => bonus_script,
        "bonus_script_clear" => bonus_script_clear,
        "heal" => heal,
        "healap" => healap,
        "itemheal" => itemheal,
        "percentheal" => percentheal,
        "statusup" => statusup,
        "statusup2" => statusup2,
        "traitstatusup" => traitstatusup,
        "traitstatusup2" => traitstatusup2,
        "getexp" => getexp,
        "sc_start" => sc_start,
        "sc_end" => sc_end,
        "sc_end_class" => sc_end_class,
        "montransform" => montransform,
        "getstatus" => getstatus,
        "getrandomoptinfo" => getrandomoptinfo,
        _ => return None,
    })
}

fn ctx<'a>(rt: &'a Rt<'a>) -> &'a crate::world::Ctx<'a> {
    rt.c.expect("buildins run with a world context")
}

/// Whether a bonus type takes a skill (name or id) as its first value (BUILDIN_FUNC(bonus)).
fn takes_skill(type_: i32) -> bool {
    matches!(
        type_,
        SP_AUTOSPELL
            | SP_AUTOSPELL_WHENHIT
            | SP_AUTOSPELL_ONSKILL
            | SP_SKILL_ATK
            | SP_SKILL_HEAL
            | SP_SKILL_HEAL2
            | SP_ADD_SKILL_BLOW
            | SP_CASTRATE
            | SP_ADDEFF_ONSKILL
            | SP_SKILL_USE_SP_RATE
            | SP_SKILL_COOLDOWN
            | SP_SKILL_FIXEDCAST
            | SP_SKILL_VARIABLECAST
            | SP_VARCASTRATE
            | SP_FIXCASTRATE
            | SP_SKILL_DELAY
            | SP_SKILL_USE_SP
            | SP_SUB_SKILL
    )
}

/// BUILDIN_FUNC(bonus): bonus / bonus2 / bonus3 / bonus4 / bonus5 <bonus type>,<val1>{,…};
fn bonus_(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_bonus") else { return SCRIPT_CMD_SUCCESS }; // no player attached
    let c = ctx(rt);
    let type_ = getnum(rt, st, 2);
    let mut val1 = 0;
    if takes_skill(type_) {
        // these bonuses support skill names
        if isstring(rt, st, 3) {
            let name = lat(&getstr(rt, st, 3));
            val1 = crate::db::skills::skill_name2id(&name) as i32;
            if val1 == 0 {
                log::error!("buildin_bonus: Invalid skill name {name} passed to item bonus. Skipping.");
                return SCRIPT_CMD_FAILURE;
            }
        } else {
            val1 = getnum(rt, st, 3);
            // Only check skill ID for bonus2, bonus3, bonus4, or bonus5
            if !st.funcname.eq_ignore_ascii_case("bonus") && crate::db::skills::skill_get_index(val1 as u16) == 0 {
                log::error!("buildin_bonus: Invalid skill ID {val1} passed to item bonus. Skipping.");
                return SCRIPT_CMD_FAILURE;
            }
        }
    } else if st.hasdata(3) {
        val1 = getnum(rt, st, 3);
    }
    let Some(mut sd) = data::sd_get(c, aid) else { return SCRIPT_CMD_SUCCESS };
    match st.lastdata() - 2 {
        0 | 1 => bonus::pc_bonus(c, &mut sd, type_, val1),
        2 => {
            let val2 = getnum(rt, st, 4);
            bonus::pc_bonus2(c, &mut sd, type_, val1, val2);
        }
        3 => {
            let val2 = getnum(rt, st, 4);
            let val3 = getnum(rt, st, 5);
            bonus::pc_bonus3(c, &mut sd, type_, val1, val2, val3);
        }
        4 => {
            let val2 = if type_ == SP_AUTOSPELL_ONSKILL && isstring(rt, st, 4) {
                // 2nd value can be skill name
                crate::db::skills::skill_name2id(&lat(&getstr(rt, st, 4))) as i32
            } else {
                getnum(rt, st, 4)
            };
            let val3 = getnum(rt, st, 5);
            let val4 = getnum(rt, st, 6);
            bonus::pc_bonus4(c, &mut sd, type_, val1, val2, val3, val4);
        }
        5 => {
            let val2 = if type_ == SP_AUTOSPELL_ONSKILL && isstring(rt, st, 4) {
                crate::db::skills::skill_name2id(&lat(&getstr(rt, st, 4))) as i32
            } else {
                getnum(rt, st, 4)
            };
            let val3 = getnum(rt, st, 5);
            let val4 = getnum(rt, st, 6);
            let val5 = getnum(rt, st, 7);
            bonus::pc_bonus5(c, &mut sd, type_, val1, val2, val3, val4, val5);
        }
        n => {
            log::debug!("buildin_bonus: unexpected number of arguments ({})", n + 1);
            return SCRIPT_CMD_SUCCESS;
        }
    }
    data::sd_put(c, &sd);
    SCRIPT_CMD_SUCCESS
}

/// The equip position an autobonus belongs to: the combo's, or the equipped item's (current_equip_item_index).
fn autobonus_pos(c: &crate::world::Ctx, aid: u32) -> u32 {
    let combo = calc::current_equip_combo_pos();
    if combo != 0 {
        return combo;
    }
    // sd->inventory.u.items_inventory[current_equip_item_index].equip (-1: rAthena reads before the array; 0 here)
    let idx = calc::current_equip_item_index();
    if idx < 0 {
        return 0;
    }
    crate::inventory::inv_get(c, aid, idx as usize).map(|r| r.item.equip).unwrap_or(0)
}

/// BUILDIN_FUNC(autobonus) / autobonus2: <bonus script>,<rate>,<duration>{,<flag>,{<other script>}}.
fn autobonus_common(rt: &Rt, st: &mut State, which: u8) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_autobonus") else { return SCRIPT_CMD_SUCCESS }; // no player attached
    let c = ctx(rt);
    let pos = autobonus_pos(c, aid);
    let rate = getnum(rt, st, 3) as i16;
    let dur = getnum(rt, st, 4) as u32;
    let bonus_script = lat(&getstr(rt, st, 2));
    if rate == 0 || dur == 0 || pos == 0 || bonus_script.is_empty() {
        return SCRIPT_CMD_SUCCESS;
    }
    let atk_type = if st.hasdata(5) { getnum(rt, st, 5) as u16 } else { 0 };
    let other_script = if st.hasdata(6) { Some(lat(&getstr(rt, st, 6))) } else { None };
    let Some(mut sd) = data::sd_get(c, aid) else { return SCRIPT_CMD_SUCCESS };
    // (script_add_autobonus: the texts compile when first run, through the "autobonus" script source)
    bonus::pc_addautobonus(&mut sd, which, &bonus_script, rate, dur, atk_type, other_script.as_deref(), pos, false);
    data::sd_put(c, &sd);
    SCRIPT_CMD_SUCCESS
}

fn autobonus(rt: &Rt, st: &mut State) -> i32 {
    autobonus_common(rt, st, 1)
}

fn autobonus2(rt: &Rt, st: &mut State) -> i32 {
    autobonus_common(rt, st, 2)
}

/// BUILDIN_FUNC(autobonus3): <bonus script>,<rate>,<duration>,<skill id / name>{,<other script>}.
fn autobonus3(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_autobonus3") else { return SCRIPT_CMD_SUCCESS }; // no player attached
    let c = ctx(rt);
    let pos = autobonus_pos(c, aid);
    let rate = getnum(rt, st, 3) as i16;
    let dur = getnum(rt, st, 4) as u32;
    let skill_id: u16 = if isstring(rt, st, 5) {
        let name = lat(&getstr(rt, st, 5));
        let id = crate::db::skills::skill_name2id(&name);
        if id == 0 {
            log::error!("buildin_autobonus3: Invalid skill name {name} passed to item bonus. Skipping.");
            return SCRIPT_CMD_FAILURE;
        }
        id
    } else {
        let id = getnum(rt, st, 5) as u16;
        if crate::db::skills::skill_get_index(id) == 0 {
            log::error!("buildin_autobonus3: Invalid skill ID {id} passed to item bonus. Skipping.");
            return SCRIPT_CMD_FAILURE;
        }
        id
    };
    let bonus_script = lat(&getstr(rt, st, 2));
    if rate == 0 || dur == 0 || pos == 0 || bonus_script.is_empty() {
        return SCRIPT_CMD_SUCCESS;
    }
    let other_script = if st.hasdata(6) { Some(lat(&getstr(rt, st, 6))) } else { None };
    let Some(mut sd) = data::sd_get(c, aid) else { return SCRIPT_CMD_SUCCESS };
    bonus::pc_addautobonus(&mut sd, 3, &bonus_script, rate, dur, skill_id, other_script.as_deref(), pos, true);
    data::sd_put(c, &sd);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(skill): skill / addtoskill <skill id / name>,<level>{,<flag>}.
fn skill(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_skill") else { return SCRIPT_CMD_SUCCESS }; // no player attached, report source
    let c = ctx(rt);
    let mut flag = ADDSKILL_TEMP;
    if st.funcname.eq_ignore_ascii_case("addtoskill") {
        flag = ADDSKILL_TEMP_ADDLEVEL;
    }
    let id = if isstring(rt, st, 2) { crate::db::skills::skill_name2id(&lat(&getstr(rt, st, 2))) as i32 } else { getnum(rt, st, 2) };
    let level = getnum(rt, st, 3);
    if st.hasdata(4) {
        flag = getnum(rt, st, 4);
    }
    progress::pc_skill(c, aid, id as u16, level, flag);
    SCRIPT_CMD_SUCCESS
}

/// mmo.hpp MAX_BONUS_SCRIPT_LENGTH.
const MAX_BONUS_SCRIPT_LENGTH: usize = 512;

/// BUILDIN_FUNC(bonus_script): "<script code>",<duration>{,<flag>{,<type>{,<status_icon>{,<char_id>}}}}.
fn bonus_script(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 7, "buildin_bonus_script") else { return SCRIPT_CMD_FAILURE };
    let c = ctx(rt);
    let script_b = getstr(rt, st, 2);
    let dur = 1000i64 * (getnum(rt, st, 3) as i64).abs();
    let mut flag: u16 = 0;
    let mut kind: u8 = 0;
    let mut icon: i16 = EFST_BLANK as i16;
    if st.hasdata(4) {
        flag = getnum(rt, st, 4) as u16;
    }
    if st.hasdata(5) {
        kind = getnum(rt, st, 5) as u8;
    }
    if st.hasdata(6) {
        icon = getnum(rt, st, 6) as i16;
    }
    let script_str = lat(&script_b);
    // No Script string, No Duration!
    if script_b.is_empty() || dur as u32 == 0 {
        log::error!("buildin_bonus_script: Invalid! Script: \"{script_str}\". Duration: {}", dur as u32);
        return SCRIPT_CMD_FAILURE;
    }
    if script_b.len() >= MAX_BONUS_SCRIPT_LENGTH {
        log::error!("buildin_bonus_script: Script string to long: \"{script_str}\".");
        return SCRIPT_CMD_FAILURE;
    }
    if icon as i32 <= EFST_BLANK || icon as i32 >= EFST_MAX {
        icon = EFST_BLANK as i16;
    }
    if bonus::pc_bonus_script_add(c, aid, &script_str, dur as u32 as i64, icon as i32, flag, kind) {
        crate::status::calc_pc(c, aid, SCO_NONE);
    }
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(bonus_script_clear): {<flag>{,<char_id>}}.
fn bonus_script_clear(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_bonus_script_clear") else { return SCRIPT_CMD_FAILURE };
    let c = ctx(rt);
    let flag = st.hasdata(2) && getnum(rt, st, 2) != 0;
    bonus::pc_bonus_script_clear(c, aid, if flag { BSF_PERMANENT } else { BSF_REM_ALL });
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(heal): heal <hp>,<sp>{,<char_id>} (forced heal).
fn heal(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 4, "buildin_heal") else { return SCRIPT_CMD_SUCCESS };
    let c = ctx(rt);
    let hp_ = getnum(rt, st, 2);
    let sp_ = getnum(rt, st, 3);
    hp::status_heal(c, aid, hp_ as i64, sp_ as i64, 0, 1);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(healap): healap <ap>{,<char_id>}.
fn healap(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_healap") else { return SCRIPT_CMD_FAILURE };
    let c = ctx(rt);
    let ap = getnum(rt, st, 2);
    hp::status_heal(c, aid, 0, 0, ap as i64, 1);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(itemheal): itemheal <hp>,<sp>{,<char_id>} (with the item heal bonuses of the used item).
fn itemheal(rt: &Rt, st: &mut State) -> i32 {
    let hp_ = getnum(rt, st, 2);
    let sp_ = getnum(rt, st, 3);
    if hp::potion_flag() == 1 {
        hp::set_potion_hpsp(hp_, sp_);
        return SCRIPT_CMD_SUCCESS;
    }
    let Some(aid) = charid2sd(rt, st, 4, "buildin_itemheal") else { return SCRIPT_CMD_SUCCESS };
    let c = ctx(rt);
    let itemid = crate::status::itemid(c, aid); // sd->itemid
    hp::pc_itemheal(c, aid, itemid, hp_, sp_);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(percentheal): percentheal <hp %>,<sp %>{,<char_id>}.
fn percentheal(rt: &Rt, st: &mut State) -> i32 {
    let mut hp_ = getnum(rt, st, 2);
    let mut sp_ = getnum(rt, st, 3);
    if hp::potion_flag() == 1 {
        hp::set_potion_per(hp_, sp_);
        return SCRIPT_CMD_SUCCESS;
    }
    let Some(aid) = charid2sd(rt, st, 4, "buildin_percentheal") else { return SCRIPT_CMD_SUCCESS };
    let c = ctx(rt);
    let scl = sc::load(c, aid);
    // RENEWAL
    if scl.has(SC_EXTREMITYFIST) {
        sp_ = 0;
    }
    if scl.has(SC_NORECOVER_STATE) {
        hp_ = 0;
        sp_ = 0;
    }
    if scl.has(SC_BITESCAR) {
        hp_ = 0;
    }
    hp::pc_percentheal(c, aid, hp_, sp_);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(statusup): statusup <stat>{,<char_id>}.
fn statusup(rt: &Rt, st: &mut State) -> i32 {
    let type_ = getnum(rt, st, 2);
    let Some(aid) = charid2sd(rt, st, 3, "buildin_statusup") else { return SCRIPT_CMD_FAILURE };
    progress::pc_statusup(ctx(rt), aid, type_, 1);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(statusup2): statusup2 <stat>,<amount>{,<char_id>}.
fn statusup2(rt: &Rt, st: &mut State) -> i32 {
    let type_ = getnum(rt, st, 2);
    let val = getnum(rt, st, 3);
    let Some(aid) = charid2sd(rt, st, 4, "buildin_statusup2") else { return SCRIPT_CMD_FAILURE };
    progress::pc_statusup2(ctx(rt), aid, type_, val);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(traitstatusup): traitstatusup <stat>{,<char_id>}.
fn traitstatusup(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_traitstatusup") else { return SCRIPT_CMD_FAILURE };
    let type_ = getnum(rt, st, 2);
    if !(SP_POW..=SP_CRT).contains(&type_) {
        log::error!("buildin_traitstatusup: Unknown trait type {type_}");
        return SCRIPT_CMD_FAILURE;
    }
    progress::pc_traitstatusup(ctx(rt), aid, type_, 1);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(traitstatusup2): traitstatusup2 <stat>,<amount>{,<char_id>}.
fn traitstatusup2(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 4, "buildin_traitstatusup2") else { return SCRIPT_CMD_FAILURE };
    let type_ = getnum(rt, st, 2);
    if !(SP_POW..=SP_CRT).contains(&type_) {
        log::error!("buildin_traitstatusup2: Unknown trait type {type_}");
        return SCRIPT_CMD_FAILURE;
    }
    let val = getnum(rt, st, 3);
    progress::pc_traitstatusup2(ctx(rt), aid, type_, val);
    SCRIPT_CMD_SUCCESS
}

/// exp.conf `quest_exp_rate: 100`.
/// battle_config.quest_exp_rate (conf/battle/exp.conf `quest_exp_rate: 100`), read at run time.
fn quest_exp_rate() -> f64 {
    crate::battle_config::get(crate::battle_config::Bc::QUEST_EXP_RATE) as f64
}

/// BUILDIN_FUNC(getexp): getexp <base xp>,<job xp>{,<char_id>} (× quest_exp_rate / 100, quest EXP).
fn getexp(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 4, "buildin_getexp") else { return SCRIPT_CMD_FAILURE };
    let c = ctx(rt);
    let mut base = getnum64(rt, st, 2);
    if base < 0 {
        log::error!("buildin_getexp: Called with negative base exp.");
        return SCRIPT_CMD_FAILURE;
    }
    let mut job = getnum64(rt, st, 3);
    if job < 0 {
        log::error!("buildin_getexp: Called with negative job exp.");
        return SCRIPT_CMD_FAILURE;
    }
    if base == 0 && job == 0 {
        log::error!("buildin_getexp: Called with base and job exp 0.");
        return SCRIPT_CMD_FAILURE;
    }
    // bonus for npc-given exp
    let bonus = quest_exp_rate() / 100.;
    if base != 0 {
        base = (base as f64 * bonus).clamp(0.0, MAX_EXP as f64) as i64;
    }
    if job != 0 {
        job = (job as f64 * bonus).clamp(0.0, MAX_EXP as f64) as i64;
    }
    progress::pc_gainexp(c, aid, None, base as u64, job as u64, 1);
    // RENEWAL (script.cpp:11177-11180): the homunculus gets homunculus_exp_gain % of the base EXP
    crate::homunculus::buildins::getexp_share(c, aid, base);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(sc_start): sc_start / sc_start2 / sc_start4 <effect>,<duration>,<val1>{,…{,<rate>,<flag>{,<unit id>}}}.
fn sc_start(rt: &Rt, st: &mut State) -> i32 {
    let c = ctx(rt);
    let start_type: i32 = if st.funcname.contains('4') {
        4
    } else if st.funcname.contains('2') {
        2
    } else {
        1
    };
    let type_ = getnum(rt, st, 2);
    let mut tick = getnum(rt, st, 3);
    let val1 = getnum(rt, st, 4);
    // If from NPC we make default flag 1 to be unavoidable
    let from_fake = st.oid != 0 && st.oid == crate::script::fake_npc_id(c);
    let flag = if st.hasdata(5 + start_type) {
        getnum(rt, st, 5 + start_type)
    } else if from_fake {
        SCSTART_NOTICKDEF as i32
    } else {
        SCSTART_NOAVOID as i32
    };
    let rate = if st.hasdata(4 + start_type) { getnum(rt, st, 4 + start_type).min(10000) } else { 10000 };
    let bl_id = if st.hasdata(6 + start_type) { getnum(rt, st, 6 + start_type) as u32 } else { st.rid };
    if tick == 0 && val1 > 0 && type_ > SC_NONE && type_ < SC_MAX {
        let skill_id = crate::db::misc::status::status_db().get_skill(type_);
        if skill_id > 0 {
            // When there isn't a duration specified, try to get it from the skill_db
            tick = crate::db::skills::skill_get_time(skill_id, val1 as u16);
        }
    }
    let mut val4 = 0;
    let mut bl_id = bl_id;
    if hp::potion_flag() == 1 {
        if let Some(target) = hp::potion_target() {
            // skill.cpp set the flags before running the script, this is a potion-pitched effect.
            bl_id = target;
            tick /= 2; // Thrown potions only last half.
            val4 = 1; // Mark that this was a thrown sc_effect
        }
    }
    let Some(bl) = crate::unit::get(c, bl_id) else { return SCRIPT_CMD_SUCCESS };
    match start_type {
        1 => {
            sc::status_change_start(c, Some(&bl), &bl, type_, rate, val1, 0, 0, val4, tick as i64, flag as u8, 0);
        }
        2 => {
            let val2 = getnum(rt, st, 5);
            sc::status_change_start(c, Some(&bl), &bl, type_, rate, val1, val2, 0, val4, tick as i64, flag as u8, 0);
        }
        _ => {
            let val2 = getnum(rt, st, 5);
            let val3 = getnum(rt, st, 6);
            let val4 = getnum(rt, st, 7);
            sc::status_change_start(c, Some(&bl), &bl, type_, rate, val1, val2, val3, val4, tick as i64, flag as u8, 0);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(sc_end): sc_end <effect>{,<unit id>} (SC_ALL / -1: every effect, status_change_clear(bl, 3)).
fn sc_end(rt: &Rt, st: &mut State) -> i32 {
    let c = ctx(rt);
    let type_ = getnum(rt, st, 2);
    let mut bl_id = if st.hasdata(3) { getnum(rt, st, 3) as u32 } else { st.rid };
    if hp::potion_flag() == 1 {
        if let Some(target) = hp::potion_target() {
            bl_id = target;
        }
    }
    if crate::unit::get(c, bl_id).is_none() {
        return SCRIPT_CMD_SUCCESS;
    }
    if type_ > SC_NONE && type_ < SC_MAX {
        let Some(mut sce) = sc::get_sce(c, bl_id, type_) else { return SCRIPT_CMD_SUCCESS };
        let Some(scdb) = crate::db::misc::status::status_db().find(type_) else {
            log::error!("buildin_sc_end: Unknown status change {type_}.");
            return SCRIPT_CMD_FAILURE;
        };
        if scdb.has_flag(SCF_NOCLEARBUFF) && scdb.has_flag(SCF_NOFORCEDEND) {
            log::error!("buildin_sc_end: Status {type_} cannot be cleared.");
            return SCRIPT_CMD_FAILURE;
        }
        // This should help status_change_end force disabling the SC in case it has no limit.
        sce.val1 = 0;
        sce.val2 = 0;
        sce.val3 = 0;
        sce.val4 = 0;
        sc::put_sce(c, &sce);
        sc::status_change_end(c, bl_id, type_, 0);
    } else {
        sc::status_change_clear(c, bl_id, 3); // remove all effects
    }
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(sc_end_class): sc_end_class {<char_id>{,<job_id>}}: the status changes of the job's skills end
/// (StatusDatabase::changeSkillTree, the skill tree agent's port).
fn sc_end_class(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 2, "buildin_sc_end_class") else { return SCRIPT_CMD_FAILURE };
    let c = ctx(rt);
    let own = crate::pc::character_of(c, aid).map(|ch| ch.class as i32).unwrap_or(0);
    let class_ = if st.hasdata(3) { getnum(rt, st, 3) } else { own };
    if !crate::clif::pcdb_checkid(class_) {
        let given = getnum(rt, st, 3);
        log::error!("buildin_sc_end_class: Invalid job ID '{given}' given.");
        return SCRIPT_CMD_FAILURE;
    }
    // status_db.changeSkillTree(sd, class_): skill_tree_db.find(class_ > 0 ? class_ : sd->status.class_)
    crate::skilltree::change_skill_tree(c, aid, if class_ > 0 { class_ as u16 } else { own as u16 });
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(montransform): transform / active_transform <monster id|"name">,<duration>{,<sc type>{,<val1>,
/// <val2>,<val3>,<val4>}}: SC_MONSTER_TRANSFORM (or SC_ACTIVE_MONSTER_TRANSFORM) with the monster in val1, and the
/// optional status change.
fn montransform(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_montransform") else { return SCRIPT_CMD_FAILURE };
    let c = ctx(rt);
    let by_name = isstring(rt, st, 2);
    let mob_id: i32 = if by_name {
        let name = String::from_utf8_lossy(&getstr(rt, st, 2)).into_owned();
        crate::db::mobs::mobdb_searchname(&name, &crate::mob::spawn::mob_has_spawn_fn(c)) as i32
    } else {
        crate::db::mobs::mobdb_checkid(getnum(rt, st, 2))
    };
    let tick = getnum(rt, st, 3);
    let type_ = if st.hasdata(4) { getnum(rt, st, 4) } else { SC_NONE };
    if mob_id == 0 {
        if by_name {
            let name = String::from_utf8_lossy(&getstr(rt, st, 2)).into_owned();
            log::warn!("buildin_montransform: Attempted to use non-existing monster '{name}'.");
        } else {
            let id = getnum(rt, st, 2);
            log::warn!("buildin_montransform: Attempted to use non-existing monster of ID '{id}'.");
        }
        return SCRIPT_CMD_FAILURE;
    }
    if mob_id == crate::db::mobs::MOBID_EMPERIUM as i32 {
        log::warn!("buildin_montransform: Monster 'Emperium' cannot be used.");
        return SCRIPT_CMD_FAILURE;
    }
    if !(SC_NONE..SC_MAX).contains(&type_) {
        log::warn!("buildin_montransform: Unsupported status change id {type_}");
        return SCRIPT_CMD_FAILURE;
    }
    let v = |st: &mut State, i: i32| if st.hasdata(i) { getnum(rt, st, i) } else { 0 };
    let (val1, val2, val3, val4) = (v(st, 5), v(st, 6), v(st, 7), v(st, 8));
    if tick != 0 {
        let Some(u) = crate::unit::get(c, aid) else { return SCRIPT_CMD_FAILURE };
        if crate::battle_config::get(crate::battle_config::Bc::MON_TRANS_DISABLE_IN_GVG) != 0 && crate::npc::mapflag_gvg2(c, u.map) {
            crate::clif::displaymessage(c, &u, crate::data::msg_txt(731)); // Transforming into monster is not allowed in Guild Wars.
            return SCRIPT_CMD_FAILURE;
        }
        if crate::atcmd_disguise::disguise_of(c, aid) != 0 {
            crate::clif::displaymessage(c, &u, crate::data::msg_txt(729)); // Cannot transform into monster while in disguise.
            return SCRIPT_CMD_FAILURE;
        }
        if st.funcname == "active_transform" {
            sc::status_change_end(c, aid, SC_ACTIVE_MONSTER_TRANSFORM, 0); // Clear previous
            sc::sc_start2(c, None, &u, SC_ACTIVE_MONSTER_TRANSFORM, 100, mob_id, type_, tick as i64);
        } else {
            sc::status_change_end(c, aid, SC_MONSTER_TRANSFORM, 0); // Clear previous
            sc::sc_start2(c, None, &u, SC_MONSTER_TRANSFORM, 100, mob_id, type_, tick as i64);
        }
        if type_ != SC_NONE {
            sc::sc_start4(c, None, &u, type_, 100, val1, val2, val3, val4, tick as i64);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(getstatus): getstatus(<effect>{,<type>{,<char_id>}}).
fn getstatus(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 4, "buildin_getstatus") else { return SCRIPT_CMD_FAILURE };
    let c = ctx(rt);
    let id = getnum(rt, st, 2);
    let type_ = if st.hasdata(3) { getnum(rt, st, 3) } else { 0 };
    if id <= SC_NONE || id >= SC_MAX {
        // invalid status type given
        log::warn!("script.cpp:getstatus: Invalid status type given ({id}).");
        return SCRIPT_CMD_SUCCESS;
    }
    let Some(sce) = sc::get_sce(c, aid, id) else {
        // Check if the status is active
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    match type_ {
        1 => st.pushint(sce.val1 as i64),
        2 => st.pushint(sce.val2 as i64),
        3 => st.pushint(sce.val3 as i64),
        4 => st.pushint(sce.val4 as i64),
        5 => {
            // return the amount of time remaining
            match crate::timer::due(c, sce.timer).filter(|_| sce.timer != 0) {
                Some(due) => st.pushint(due - c.tick()),
                None => st.pushint(-1),
            }
        }
        _ => st.pushint(1),
    }
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(getrandomoptinfo): getrandomoptinfo(<type>): the random option whose script status_calc_pc runs
/// (current_equip_item_index / current_equip_opt_index), 0 outside of one.
fn getrandomoptinfo(rt: &Rt, st: &mut State) -> i32 {
    const ROA_ID: i32 = 0;
    const ROA_VALUE: i32 = 1;
    const ROA_PARAM: i32 = 2;
    let idx = calc::current_equip_item_index();
    let opt = calc::current_equip_opt_index();
    let found = match (rid2sd(rt, st, "buildin_getrandomoptinfo"), idx != -1 && opt != -1) {
        (Some(aid), true) => crate::inventory::inv_get(ctx(rt), aid, idx as usize).map(|r| r.item.opt(opt as usize)).filter(|o| o.id != 0),
        _ => None,
    };
    match found {
        Some(o) => {
            let param = getnum(rt, st, 2);
            let val = match param {
                ROA_ID => o.id as i64,
                ROA_VALUE => o.value as i64,
                ROA_PARAM => o.param as i64,
                _ => {
                    log::warn!("buildin_getrandomoptinfo: Invalid attribute type {param} (Max {}).", crate::inventory::MAX_ITEM_RDM_OPT);
                    0
                }
            };
            st.pushint(val);
        }
        None => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}
