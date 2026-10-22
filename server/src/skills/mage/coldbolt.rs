//! skills/mage/coldbolt.cpp: class SkillColdBolt : public SkillImpl (MG_COLDBOLT).

use crate::battle::calc::Damage;
use crate::battle::{BF_MAGIC, BF_SHORT, BF_WEAPON, DMG_NORMAL};
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_COLD_FORCE_OPTION, SC_SPELLFIST};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillColdBolt;

impl SkillImpl for SkillColdBolt {
    fn skill_id(&self) -> u16 {
        MG_COLDBOLT
    }

    /// SkillColdBolt::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, mflag: i32) {
        // const status_change *sc = status_get_sc(src); if (sc) { ... }
        if sce(c, src, SC_COLD_FORCE_OPTION).is_some() {
            *base_skillratio *= 5;
        }
        if let Some(sf) = sce(c, src, SC_SPELLFIST).filter(|_| mflag & BF_SHORT != 0) {
            *base_skillratio += (sf.val3 * 100) + (sf.val1 * 50 - 50) - 100;
            // val3 = used bolt level, val1 = used spellfist level. [Rytech]
        }
    }

    /// SkillColdBolt::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }

    /// SkillColdBolt::modifyDamageData.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if sce(c, src, SC_SPELLFIST).is_some() && (dmg.miscflag & BF_SHORT) != 0 {
            dmg.div_ = 1; // ad mods, to make it work similar to regular hits [Xazax]
            dmg.flag = BF_WEAPON | BF_SHORT;
            dmg.type_ = DMG_NORMAL;
        }
    }
}
