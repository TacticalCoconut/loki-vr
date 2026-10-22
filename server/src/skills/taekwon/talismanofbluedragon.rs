//! skills/taekwon/talismanofbluedragon.cpp: class SkillTalismanOfBlueDragon : public SkillImpl (SOA_TALISMAN_OF_BLUE_DRAGON).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::SC_T_FIFTH_GOD;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillTalismanOfBlueDragon;

impl SkillImpl for SkillTalismanOfBlueDragon {
    fn skill_id(&self) -> u16 {
        SOA_TALISMAN_OF_BLUE_DRAGON
    }

    /// SkillTalismanOfBlueDragon::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 1250 + 2650 * lv;
        *skillratio += pc_checkskill(c, src, SOA_TALISMAN_MASTERY) * 15 * lv;
        *skillratio += 5 * sstatus.spl as i32;
        if sce(c, src, SC_T_FIFTH_GOD).is_some() {
            *skillratio += 850 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillTalismanOfBlueDragon::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        sc::sc_start(c, Some(&s), &s, skill_get_sc(id) as i32, 100, 1, skill_get_time(id, skill_lv) as i64);
    }
}
