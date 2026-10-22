//! skills/mage/firepillar.cpp: class SkillFirePillar : public SkillImpl (WZ_FIREPILLAR). The pillar's hit is
//! skill_unit_onplace_timer's UNT_FIREPILLAR_ACTIVE (skillunit/ontimer.rs).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFirePillar;

impl SkillImpl for SkillFirePillar {
    fn skill_id(&self) -> u16 {
        WZ_FIREPILLAR
    }

    /// SkillFirePillar::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        //Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        *flag |= 1;
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillFirePillar::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -60 + 20 * skill_lv as i32; //20% MATK each hit
    }

    /// SkillFirePillar::applyAdditionalEffects: `unit_set_walkdelay(target, tick, skill_get_time2(id, lv), 1)`.
    fn apply_additional_effects(&self, c: &Ctx, _src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        crate::battle::unit_attack::unit_set_walkdelay(c, target.id, tick, skill_get_time2(self.skill_id(), skill_lv) as Tick, 1, 0);
    }

    /// SkillFirePillar::modifyDamageData.
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        // const map_session_data* sd = BL_CAST(BL_PC, &src)
        if src.kind == BL_PC && dmg.div_ > 0 {
            dmg.div_ *= -1; // For players, damage is divided by number of hits
        }
    }
}
