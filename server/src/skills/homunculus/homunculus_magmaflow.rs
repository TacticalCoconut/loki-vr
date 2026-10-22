//! skills/homunculus/homunculus_magmaflow.cpp: class SkillMagmaFlow : public SkillImplRecursiveDamageSplash
//! (MH_MAGMA_FLOW).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{splash_castend_damage_id, splash_castend_pos2, RecursiveDamageSplash, SkillImpl};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMagmaFlow;

impl SkillImpl for SkillMagmaFlow {
    fn skill_id(&self) -> u16 {
        MH_MAGMA_FLOW
    }

    /// SkillMagmaFlow::castendNoDamageId: the status on the target (its hits come from battle_calc_damage's
    /// SC_MAGMA_FLOW, which calls skill_castend_damage_id). No packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }

    /// SkillMagmaFlow::castendDamageId: each splash target (flag&1) has a 3% per level chance to be hit
    /// (`(rnd() % 100) > (3 * skill_lv)` skips it), then SkillImplRecursiveDamageSplash's.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        if *flag & 1 != 0 && (c.rnd() % 100) as i32 > 3 * skill_lv as i32 {
            return; // chance to not trigger atk
        }
        splash_castend_damage_id(self, c, src, target, skill_lv, tick, flag);
    }

    /// SkillImplRecursiveDamageSplash::castendPos2 (not overridden).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        splash_castend_pos2(self, c, src, x, y, skill_lv, tick, flag);
    }

    /// SkillMagmaFlow::calculateSkillRatio: -100 + (100 * lv + 3 * base level) * base level / 120.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let lv = crate::status::status_get_lv(c, src);
        *base_skillratio += -100 + (100 * skill_lv as i32 + 3 * lv) * lv / 120;
    }
}

impl RecursiveDamageSplash for SkillMagmaFlow {}
