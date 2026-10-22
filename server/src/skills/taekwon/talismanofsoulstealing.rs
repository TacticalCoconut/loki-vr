//! skills/taekwon/talismanofsoulstealing.cpp: class SkillTalismanOfSoulStealing : public SkillImpl (SOA_TALISMAN_OF_SOUL_STEALING).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_SKILL;
use crate::skill::attack::skill_attack;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::SkillImpl;
use crate::status::hp::status_heal;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillTalismanOfSoulStealing;

impl SkillImpl for SkillTalismanOfSoulStealing {
    fn skill_id(&self) -> u16 {
        SOA_TALISMAN_OF_SOUL_STEALING
    }

    /// SkillTalismanOfSoulStealing::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 500 + 1250 * lv;
        *skillratio += pc_checkskill(c, src, SOA_TALISMAN_MASTERY) * 7 * lv;
        *skillratio += pc_checkskill(c, src, SOA_SOUL_MASTERY) * 7 * lv;
        *skillratio += 3 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillTalismanOfSoulStealing::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        if target.kind != BL_SKILL {
            let sp = (100 + crate::status::status_get_lv(c, src) / 50) * skill_lv as i32;
            status_heal(c, src.id, 0, sp as i64, 0, 0);
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_nodamage(c, Some(&s), &s, id, sp, true);
        }
    }
}
