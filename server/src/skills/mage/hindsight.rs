//! skills/mage/hindsight.cpp: class SkillHindsight : public SkillImpl (SA_AUTOSPELL).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::SC_AUTOSPELL;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHindsight;

impl SkillImpl for SkillHindsight {
    fn skill_id(&self) -> u16 {
        SA_AUTOSPELL
    }

    /// SkillHindsight::castendNoDamageId: a player gets the spell list (clif_autospell; the choice comes back to
    /// skill_autospell: skill/autospell.rs); another caster gets SC_AUTOSPELL with a spell picked by the level.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // status_change *tsc = status_get_sc(target); (only read by the commented-out Soul Link lines below)
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        if src.kind == BL_PC {
            crate::itemui::set_workinprogress(c, src.id, crate::itemui::WIP_DISABLE_ALL);
            crate::skill::autospell::clif_autospell(c, src.id, skill_lv);
        } else {
            let mut maxlv = 1;
            let mut spellid: i32 = 0;
            const SPELLARRAY: [u16; 3] = [MG_COLDBOLT, MG_FIREBOLT, MG_LIGHTNINGBOLT];
            let skill_lv = skill_lv as i32;

            if skill_lv >= 10 {
                spellid = MG_FROSTDIVER as i32;
                // if (tsc && tsc->getSCE(SC_SPIRIT) && tsc->getSCE(SC_SPIRIT)->val2 == SA_SAGE)
                //     maxlv = 10;
                // else
                maxlv = skill_lv - 9;
            } else if skill_lv >= 8 {
                spellid = MG_FIREBALL as i32;
                maxlv = skill_lv - 7;
            } else if skill_lv >= 5 {
                spellid = MG_SOULSTRIKE as i32;
                maxlv = skill_lv - 4;
            } else if skill_lv >= 2 {
                let i_rnd = (c.rnd() % 3) as usize;
                spellid = SPELLARRAY[i_rnd] as i32;
                maxlv = skill_lv - 1;
            } else if skill_lv > 0 {
                spellid = MG_NAPALMBEAT as i32;
                maxlv = 3;
            }

            if spellid > 0 {
                sc::sc_start4(c, Some(src), src, SC_AUTOSPELL, 100, skill_lv, spellid, maxlv, 0, skill_get_time(SA_AUTOSPELL, skill_lv as u16) as i64);
            }
        }
    }
}
