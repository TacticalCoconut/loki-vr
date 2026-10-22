//! skills/taekwon/talismanofwhitetiger.cpp: class SkillTalismanOfWhiteTiger : public SkillImplRecursiveDamageSplash (SOA_TALISMAN_OF_WHITE_TIGER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_T_FIFTH_GOD, SC_T_FIRST_GOD};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillTalismanOfWhiteTiger;

impl SkillImpl for SkillTalismanOfWhiteTiger {
    fn skill_id(&self) -> u16 {
        SOA_TALISMAN_OF_WHITE_TIGER
    }

    crate::splash_skill_defaults!();

    /// SkillTalismanOfWhiteTiger::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 600 + 1200 * lv;
        *skillratio += pc_checkskill(c, src, SOA_TALISMAN_MASTERY) * 15 * lv;
        *skillratio += 5 * sstatus.spl as i32;
        if sce(c, src, SC_T_FIFTH_GOD).is_some() {
            *skillratio += 400 + 400 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillTalismanOfWhiteTiger::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if sce(c, src, SC_T_FIRST_GOD).is_some() {
            sc::sc_start(c, Some(src), src, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
        skill_castend_damage_id(c, &s, &t, id, skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillTalismanOfWhiteTiger {}
