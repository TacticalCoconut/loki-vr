//! skills/taekwon/newmoonkick.cpp: class SkillNewMoonKick : public SkillImplRecursiveDamageSplash (SJ_NEWMOONKICK).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_NONE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillNewMoonKick;

impl SkillImpl for SkillNewMoonKick {
    fn skill_id(&self) -> u16 {
        SJ_NEWMOONKICK
    }

    crate::splash_skill_defaults!();

    /// SkillNewMoonKick::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 600 + 100 * skill_lv as i32;
    }

    /// SkillNewMoonKick::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        // tsc != nullptr && type != SC_NONE ? tsc->getSCE(type)
        let tsce = crate::battle::mobif::has_sc(target.kind) && type_ != SC_NONE && sc::has_sce(c, target.id, type_);

        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);

        if tsce {
            sc::status_change_end(c, target.id, type_, 0);
            return;
        } else {
            sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        }

        let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
        skill_castend_damage_id(c, src, &t, id, skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillNewMoonKick {}
