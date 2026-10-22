//! skills/swordman/selfprovoke.cpp: class SkillProvokeSelf : public SkillImpl (SM_SELFPROVOKE).

use crate::battle::BF_MISC;
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::misc::skill_get_range2;
use crate::skill::unit::unit_skillcastcancel;
use crate::skills::SkillImpl;
use crate::status::consts::SC_COMA;
use crate::status::defs::{MD_STATUSIMMUNE, SCSTART_NONE};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillProvokeSelf;

impl SkillImpl for SkillProvokeSelf {
    fn skill_id(&self) -> u16 {
        SM_SELFPROVOKE
    }

    /// SkillProvokeSelf::castendNoDamageId: Provoke that always lands; the packet shows SM_PROVOKE.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, bl: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let tstatus = crate::status::status_get_status_data(c, bl);
        let is_pc = src.kind == BL_PC;
        let dstmd = bl.kind == BL_MOB;
        if crate::battle::mobif::has_mode(tstatus.mode, MD_STATUSIMMUNE) || sc::battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) {
            return;
        }
        let success = sc::sc_start(c, Some(src), bl, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        if !success {
            if is_pc {
                skill_fail(c, src, id);
            }
            return;
        }
        let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
        clif_skill_nodamage(c, Some(src), &t, SM_PROVOKE, skill_lv as i32, success);
        unit_skillcastcancel(c, bl.id, 2);
        if dstmd {
            if let Some(mut md) = crate::mob::get(c, bl.id) {
                md.provoke_flag = src.id;
                crate::mob::put(c, &md);
            }
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let range = skill_get_range2(c, &s, id, skill_lv, true);
            crate::mob::ai::mob_target(c, bl.id, &s, range);
        }
        // Provoke can cause Coma even though it's a nodamage skill
        if is_pc {
            if let Some(sd) = crate::battle::bl::sd(c, src) {
                let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
                if crate::battle::weapon_attack::battle_check_coma(c, &sd, &t, BF_MISC) {
                    sc::status_change_start(c, Some(src), &t, SC_COMA, 10000, skill_lv as i32, 0, src.id as i32, 0, 0, SCSTART_NONE, 0);
                }
            }
        }
    }
}
