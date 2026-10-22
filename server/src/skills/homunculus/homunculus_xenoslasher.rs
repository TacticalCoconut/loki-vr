//! skills/homunculus/homunculus_xenoslasher.cpp: class SkillXenoSlasher : public SkillImplRecursiveDamageSplash
//! (MH_XENO_SLASHER).

use super::unit_castend_pos2;
use crate::battle::calc::Damage;
use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::skill::clif::{clif_skill_damage, DMGVAL_IGNORE};
use crate::skills::novice::status_get_amotion;
use crate::skills::{splash_castend_damage_id, splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_BLEEDING;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillXenoSlasher;

impl SkillImpl for SkillXenoSlasher {
    fn skill_id(&self) -> u16 {
        MH_XENO_SLASHER
    }

    /// SkillImplRecursiveDamageSplash::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        splash_castend_damage_id(self, c, src, target, skill_lv, tick, flag);
    }

    /// SkillXenoSlasher::castendPos2: the ground unit (skill_unitsetting) instead of the splash's area.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        unit_castend_pos2(c, self.skill_id(), src, x, y, skill_lv, flag);
    }

    /// SkillXenoSlasher::calculateSkillRatio: -100 + 450 * lv * base level / 100 + INT (!TODO upstream: confirm the
    /// base level and INT bonus).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 450 * skill_lv as i32 * crate::status::status_get_lv(c, src) / 100 + sstatus.int_ as i32;
    }

    /// SkillXenoSlasher::applyAdditionalEffects: SC_BLEEDING at skill_lv % (val2 = the caster), for skill_get_time2.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start4(c, Some(src), target, SC_BLEEDING, skill_lv as i32, skill_lv as i32, src.id as i32, 0, 0, skill_get_time2(id, skill_lv) as i64);
    }
}

impl RecursiveDamageSplash for SkillXenoSlasher {
    /// SkillXenoSlasher::splashSearch: the dummy damage packet on the first target, then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_damage(c, src, target, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, self.skill_id(), skill_lv as i32, DMG_SINGLE);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
