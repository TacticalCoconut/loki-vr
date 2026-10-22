//! skills/merchant/mightysmash.cpp: class SkillMightySmash : public SkillImplRecursiveDamageSplash (MT_MIGHTY_SMASH).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::SC_AXE_STOMP;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillMightySmash::SkillMightySmash() : SkillImplRecursiveDamageSplash(MT_MIGHTY_SMASH).
pub struct SkillMightySmash;

impl SkillImpl for SkillMightySmash {
    fn skill_id(&self) -> u16 {
        MT_MIGHTY_SMASH
    }
    crate::splash_skill_defaults!();

    /// SkillMightySmash::modifyDamageData: 7 hits under the caster's SC_AXE_STOMP.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        // const status_change *sc = status_get_sc(&src); sc->hasSCE(SC_AXE_STOMP)
        if sce(c, src, SC_AXE_STOMP).is_some() {
            dmg.div_ = 7;
        }
    }

    /// SkillMightySmash::castendNoDamageId: the skill packet, then skill_castend_damage_id on the same target (the
    /// splash parent's castendDamageId: the area around it).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_castend_damage_id(c, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillMightySmash::calculateSkillRatio: 80 + 240 * lv %, + 5 * POW; under SC_AXE_STOMP + 20 % and another
    /// 5 * POW; RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 80 + 240 * skill_lv as i32;
        *base_skillratio += 5 * sstatus.pow as i32;
        if sce(c, src, SC_AXE_STOMP).is_some() {
            *base_skillratio += 20;
            *base_skillratio += 5 * sstatus.pow as i32;
        }
        re_lvl_dmod(c, src, base_skillratio, 100);
    }
}

impl crate::skills::RecursiveDamageSplash for SkillMightySmash {}
