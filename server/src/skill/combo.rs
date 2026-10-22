//! skill.cpp skill_is_combo, skill_combo_toggle_inf, skill_combo (the SC_COMBO chain a skill hit starts or ends) and
//! unit.cpp unit_cancel_combo.

use crate::db::skills::*;
use crate::map::{BL_HOM, BL_PC};
use crate::status::consts::*;
use crate::status::sc;
use crate::timer::{self, Func};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// skill.cpp skill_is_combo: 0 not a combo, 1 a normal combo, 2 a combo that prioritizes auto-target even if val2
/// is set.
pub fn skill_is_combo(skill_id: u16) -> i32 {
    match skill_id {
        MO_CHAINCOMBO | MO_COMBOFINISH | CH_TIGERFIST | CH_CHAINCRUSH | MO_EXTREMITYFIST | TK_TURNKICK | TK_STORMKICK | TK_DOWNKICK | TK_COUNTER | TK_JUMPKICK | HT_POWER
        | SR_DRAGONCOMBO => 1,
        SR_FALLENEMPIRE | SR_TIGERCANNON | SR_GATEOFHELL => 2,
        _ => 0,
    }
}

/// skill.cpp skill_combo_toggle_inf: the combo's follow-up skill shows as usable (inf) or not in the skill window.
pub fn skill_combo_toggle_inf(c: &Ctx, bl: &Unit, skill_id: u16, inf: i32) {
    let is_pc = bl.kind == BL_PC;
    match skill_id {
        MH_MIDNIGHT_FRENZY | MH_EQC => {
            let skill_id2 = if skill_id == MH_EQC { MH_TINDER_BREAKER } else { MH_SONIC_CRAW };
            let idx = crate::homunculus::hom_skill_get_index(skill_id2);
            let flag = if inf != 0 { crate::status::defs::SKILL_FLAG_TMP_COMBO } else { crate::homunculus::SKILL_FLAG_PERMANENT };
            if idx != -1 {
                // hd = BL_CAST(BL_HOM, bl), dereferenced without a check in rAthena (only homunculi have these skills)
                let found = bl.kind == BL_HOM
                    && crate::homunculus::data::with(c, bl.id, |hd| {
                        if let Some(s) = hd.homunculus.hskill.get_mut(idx as usize) {
                            s.flag = flag;
                        }
                    })
                    .is_some();
                if found {
                    crate::homunculus::clif::clif_homskillinfoblock(c, bl.id); // refresh info //@FIXME we only want to refresh one skill
                }
            }
        }
        MO_COMBOFINISH | CH_CHAINCRUSH => {
            if is_pc {
                crate::clif_skill::clif_skillinfo(c, bl.id, MO_EXTREMITYFIST, inf);
            }
        }
        TK_JUMPKICK => {
            if is_pc {
                crate::clif_skill::clif_skillinfo(c, bl.id, TK_JUMPKICK, inf);
            }
        }
        MO_TRIPLEATTACK => {
            if is_pc && crate::skill::checkskill(c, bl.id, SR_DRAGONCOMBO) > 0 {
                crate::clif_skill::clif_skillinfo(c, bl.id, SR_DRAGONCOMBO, inf);
            }
        }
        SR_FALLENEMPIRE => {
            if is_pc {
                crate::clif_skill::clif_skillinfo(c, bl.id, SR_GATEOFHELL, inf);
                crate::clif_skill::clif_skillinfo(c, bl.id, SR_TIGERCANNON, inf);
            }
        }
        _ => {}
    }
}

/// skill.cpp skill_combo: ends the running combo state after a skill and starts the next one.
pub fn skill_combo(c: &Ctx, src: &Unit, dsrc: &Unit, bl: &Unit, skill_id: u16, skill_lv: u16, tick: Tick) {
    let mut duration: Tick = 0; // Set to duration the user can use a combo skill or 1 for aftercast delay of pre-skill
    let mut nodelay = 0; // Set to 1 for no walk/attack delay, set to 2 for no walk delay
    let mut target_id = bl.id; // Set to 0 if combo skill should not autotarget
    if !crate::battle::mobif::has_sc(src.kind) {
        return; // status_get_sc(src) == nullptr
    }
    // End previous combo state after skill is invoked
    if let Some(mut sce) = sc::get_sce(c, src.id, SC_COMBO) {
        match skill_id {
            TK_TURNKICK | TK_STORMKICK | TK_DOWNKICK | TK_COUNTER => {
                let ranker = src.kind == BL_PC
                    && crate::pc::character_of(c, src.id).map(|ch| crate::fame::pc_famerank(c, ch.id, crate::db::jobs::MAPID_TAEKWON) != 0).unwrap_or(false);
                if ranker {
                    // Extend combo time.
                    sce.val1 = skill_id as i32; // Update combo-skill
                    sce.val3 = skill_id as i32;
                    if sce.timer != 0 {
                        timer::delete(c, sce.timer);
                    }
                    sce.timer = timer::add(c, tick + sce.val4 as i64, Func::StatusChangeTimer, src.id, SC_COMBO as i64);
                    sc::put_sce(c, &sce);
                } else {
                    super::unit::unit_cancel_combo(c, src.id); // Cancel combo wait
                }
            }
            _ => {
                if src.id == dsrc.id {
                    // Ground skills are exceptions. [Inkfish]
                    sc::status_change_end(c, src.id, SC_COMBO, 0);
                }
            }
        }
    }
    // start new combo
    if src.kind == BL_PC {
        // player only
        let chk = |id| crate::skill::checkskill(c, src.id, id) as i32;
        let spiritball = super::state::get(c, src.id).spiritball as i32;
        match skill_id {
            MO_TRIPLEATTACK => {
                if chk(MO_CHAINCOMBO) > 0 || chk(SR_DRAGONCOMBO) > 0 {
                    duration = 1;
                    target_id = 0; // Will target current auto-target instead
                }
            }
            MO_CHAINCOMBO => {
                if chk(MO_COMBOFINISH) > 0 && spiritball >= 1 {
                    duration = 1;
                    target_id = 0;
                }
            }
            MO_COMBOFINISH => {
                let party_id = crate::pc::character_of(c, src.id).map(|ch| ch.party_id).unwrap_or(0);
                if party_id > 0 {
                    // bonus from SG_FRIEND [Komurka]
                    super::misc::party_skill_check(c, src.id, party_id, skill_id, skill_lv);
                }
                let explosion = sc::has_sce(c, src.id, SC_EXPLOSIONSPIRITS);
                if chk(CH_TIGERFIST) > 0 && spiritball >= 1 {
                    duration = 1;
                    target_id = 0;
                } else if chk(CH_CHAINCRUSH) > 0 && spiritball >= 2 {
                    duration = 1;
                    target_id = 0;
                } else if chk(MO_EXTREMITYFIST) > 0 && spiritball >= 4 && explosion {
                    duration = 1;
                    target_id = 0;
                }
            }
            CH_TIGERFIST => {
                if chk(CH_CHAINCRUSH) > 0 && spiritball >= 2 {
                    duration = 1;
                    target_id = 0;
                }
            }
            CH_CHAINCRUSH => {
                if chk(MO_EXTREMITYFIST) > 0 && spiritball >= 1 && sc::has_sce(c, src.id, SC_EXPLOSIONSPIRITS) {
                    duration = 1;
                    target_id = 0;
                }
            }
            AC_DOUBLE => {
                if chk(HT_POWER) != 0 {
                    duration = 2000;
                    nodelay = 1; // Neither gives walk nor attack delay
                    target_id = 0; // Does not need to be used on previous target
                }
            }
            SR_DRAGONCOMBO => {
                if chk(SR_FALLENEMPIRE) > 0 {
                    duration = 1;
                }
            }
            SR_FALLENEMPIRE => {
                if chk(SR_TIGERCANNON) > 0 || chk(SR_GATEOFHELL) > 0 {
                    duration = 1;
                }
            }
            SJ_PROMINENCEKICK => {
                if chk(SJ_SOLARBURST) > 0 {
                    duration = 1;
                }
            }
            _ => {}
        }
    } else {
        // other (hd = BL_CAST(BL_HOM, src): rAthena dereferences it for these skills, which only homunculi have; any
        // other caster's spheres count as 0 here). `nodelay = 1` is outside the if in rAthena (its indentation lies).
        let spiritball = if src.kind == BL_HOM { crate::homunculus::data::get(c, src.id).map(|hd| hd.homunculus.spiritball as i32).unwrap_or(0) } else { 0 };
        match skill_id {
            MH_TINDER_BREAKER | MH_CBC | MH_SONIC_CRAW | MH_SILVERVEIN_RUSH => {
                if spiritball > 0 {
                    duration = 2000;
                }
                nodelay = 1;
            }
            MH_EQC | MH_MIDNIGHT_FRENZY => {
                if spiritball >= 2 {
                    duration = 2000;
                }
                nodelay = 1;
            }
            _ => {}
        }
    }
    if duration != 0 {
        // Possible to chain
        let src_now = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        if src.kind == BL_PC && duration == 1 {
            duration = src_now.ud.canact_tick - tick; // Auto calc duration
        }
        let amotion = crate::status::status_get_status_data(c, &src_now).amotion as Tick;
        duration = amotion.max(duration); // Never less than aMotion
        sc::sc_start4(c, Some(&src_now), &src_now, SC_COMBO, 100, skill_id as i32, target_id as i32, nodelay, 0, duration);
        let src_now = unit::get(c, src.id).unwrap_or(src_now);
        super::clif::clif_combo_delay(c, &src_now, duration);
    }
}
