//! skills/acolyte/blazingflameblast.cpp: class SkillBlazingFlameBlast : public WeaponSkillImpl (IQ_BLAZING_FLAME_BLAST).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_MASSIVE_F_BLASTER;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillBlazingFlameBlast;

impl SkillImpl for SkillBlazingFlameBlast {
    fn skill_id(&self) -> u16 {
        IQ_BLAZING_FLAME_BLAST
    }

    /// SkillBlazingFlameBlast::castendDamageId: the animation, then WeaponSkillImpl::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        weapon_castend_damage_id(c, id, src, &t, skill_lv, tick, *flag);
    }

    /// SkillBlazingFlameBlast::calculateSkillRatio: 2000% + 3800% per level + 10 x POW (!TODO (rAthena): unknown
    /// ratio); more under the caster's Massive Flame Blaster (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 2000 + 3800 * skill_lv as i32;
        *skillratio += 10 * sstatus.pow as i32; // !TODO: unknown ratio
        if sce(c, src, SC_MASSIVE_F_BLASTER).is_some() {
            *skillratio += 1500 + 400 * skill_lv as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
