//! skills/archer/rhythmicalwave.cpp: class SkillRhythmicalWave : public SkillImpl (TR_RHYTHMICAL_WAVE).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::skill::area::{foreach_in_range, skill_area_sub, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_SPLASH;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::SC_MYSTIC_SYMPHONY;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRhythmicalWave;

impl SkillImpl for SkillRhythmicalWave {
    fn skill_id(&self) -> u16 {
        TR_RHYTHMICAL_WAVE
    }

    /// SkillRhythmicalWave::castendDamageId: the recursive call hits (skill_attack of the skill's type); the first
    /// sends the no-damage packet, calls every character and skill unit around the target (SD_SPLASH) and consumes
    /// the ammunition ("Consume here since Magic/Misc attacks reset arrow_atk").
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            crate::skill::attack::skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        } else {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
            let t = super::fresh(c, target);
            foreach_in_range(c, &t, skill_get_splash(id, skill_lv) as i16, crate::map::BL_CHAR | crate::map::BL_SKILL, Wall::None, |c, bl| skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
            if src.kind == crate::map::BL_PC {
                crate::battle::weapon_attack::battle_consume_ammo(c, src.id, id, skill_lv);
            }
        }
    }

    /// SkillRhythmicalWave::calculateSkillRatio: -100 + 250 + 3650 * lv + 25 * Stage Manner + 5 * SPL (!TODO: check
    /// the Stage Manner / SPL ratios), + 200 + 1000 * lv under Mystic Symphony, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 250 + 3650 * skill_lv as i32;
        *skillratio += pc_checkskill(c, src, TR_STAGE_MANNER) * 25;
        *skillratio += 5 * sstatus.spl as i32;
        if sce(c, src, SC_MYSTIC_SYMPHONY).is_some() {
            *skillratio += 200 + 1000 * skill_lv as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillRhythmicalWave::modifyElement: a player's arrow element.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        super::arrow_element(c, src, element);
    }
}
