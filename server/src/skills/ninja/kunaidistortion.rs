//! skills/ninja/kunaidistortion.cpp: class SkillKunaiDistortion : public SkillImpl (SS_KUNAIWAIKYOKU), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_WOS;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skill::{SKILL_ALTDMG_FLAG, UNIT_NOCONSUME_AMMO};
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillKunaiDistortion;

impl SkillImpl for SkillKunaiDistortion {
    fn skill_id(&self) -> u16 {
        SS_KUNAIWAIKYOKU
    }

    /// SkillKunaiDistortion::applyAdditionalEffects: the skill's status (skill_get_time2).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
    }

    /// SkillKunaiDistortion::calculateSkillRatio: 300 + 600% per level, + 10% x level per Kunai Refraction level, + 5
    /// x POW, RE_LVL_DMOD(100); a mirage's hit (SKILL_ALTDMG_FLAG) deals 30%.
    fn calculate_skill_ratio(&self, c: &Ctx, wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 300 + 600 * skill_lv as i32;
        *skillratio += pc_checkskill(c, src, SS_KUNAIKUSSETSU) * 10 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if wd.miscflag & SKILL_ALTDMG_FLAG != 0 {
            *skillratio = *skillratio * 3 / 10;
        }
    }

    /// SkillKunaiDistortion::castendDamageId: the splash hit (flag & 1).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        super::splash_hit(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillKunaiDistortion::castendPos2: the mirages cast it at (x, y) too, the square around (x, y) is hit
    /// (map_foreachinallarea, flag | 1), then the unit (no ammunition consumed).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        super::skill_mirage_cast(c, src, None, id, skill_lv, x, y, tick, *flag | BCT_WOS);
        super::pos_splash(c, id, src, x, y, skill_lv, tick, *flag);
        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        skill_unitsetting(c, &s, id, skill_lv, x, y, UNIT_NOCONSUME_AMMO);
    }
}
