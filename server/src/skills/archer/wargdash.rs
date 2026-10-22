//! skills/archer/wargdash.cpp: class SkillWargDash : public SkillImplRecursiveDamageSplash (RA_WUGDASH). The run
//! itself is SC_WUGDASH's (status2's unit_run / unit_run_hit).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::sce;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_NONE;
use crate::status::defs::OPTION_WUGRIDER;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWargDash;

impl SkillImpl for SkillWargDash {
    fn skill_id(&self) -> u16 {
        RA_WUGDASH
    }

    crate::splash_skill_defaults!();

    /// SkillWargDash::calculateSkillRatio: ATK 300% (`base_skillratio += 200`).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 200;
    }

    /// SkillWargDash::castendNoDamageId: a running dash ends (no requirement consumed); a player riding its warg
    /// starts SC_WUGDASH in its facing direction and gets clif_walkok.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let tsce = if type_ != SC_NONE { sce(c, target, type_) } else { None };
        if tsce.is_some() {
            let ended = sc::status_change_end(c, target.id, type_, 0) != 0;
            clif_skill_nodamage(c, Some(src), &super::fresh(c, target), id, skill_lv as i32, ended);
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        // sd && pc_isridingwug(sd)
        if src.kind == crate::map::BL_PC && src.sc.option & OPTION_WUGRIDER != 0 {
            let dir = crate::unit::getdir(target) as i32;
            let ok = sc::sc_start4(c, Some(src), target, type_, 100, skill_lv as i32, dir, 0, 0, 0);
            clif_skill_nodamage(c, Some(src), &super::fresh(c, target), id, skill_lv as i32, ok);
            crate::clif::walkok(c, &super::fresh(c, src));
        }
    }
}

impl RecursiveDamageSplash for SkillWargDash {}
