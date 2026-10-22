//! skills/novice/megasonicblow.cpp: class SkillMegaSonicBlow : public WeaponSkillImpl (HN_MEGA_SONIC_BLOW).

use super::{pc_checkskill, re_lvl_dmod};
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_STUN;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMegaSonicBlow;

impl SkillImpl for SkillMegaSonicBlow {
    fn skill_id(&self) -> u16 {
        HN_MEGA_SONIC_BLOW
    }

    /// SkillMegaSonicBlow::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_STUN, 2 * skill_lv as i32 + 10, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillMegaSonicBlow::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        let lv = skill_lv as i32;
        *skillratio += -100 + 900 + 750 * lv;
        *skillratio += pc_checkskill(c, src, HN_SELFSTUDY_TATICS) * 5 * lv;
        *skillratio += 5 * sstatus.pow as i32;
        // status_get_hp(target) < status_get_max_hp(target) / 2 (uint32)
        if tstatus.hp < tstatus.max_hp / 2 {
            *skillratio *= 2;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillMegaSonicBlow::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}
