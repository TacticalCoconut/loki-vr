//! skills/archer/reverberation.cpp: class SkillReverberation : public SkillImpl (WM_REVERBERATION).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::skill::area::{foreach_in_range, skill_area_sub, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_SPLASH;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::SC_SOUNDBLEND;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillReverberation;

impl SkillImpl for SkillReverberation {
    fn skill_id(&self) -> u16 {
        WM_REVERBERATION
    }

    /// SkillReverberation::applyAdditionalEffects: the target's Sound Blend ends.
    fn apply_additional_effects(&self, c: &Ctx, _src: &Unit, target: &Unit, _skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::status_change_end(c, target.id, SC_SOUNDBLEND, 0);
    }

    /// SkillReverberation::calculateSkillRatio: MATK [{(Skill Level x 300) + 400} x Casters Base Level / 100] %:
    /// -100 + 700 + 300 * lv, RE_LVL_DMOD(100), + 50% on a Sound Blend target.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 700 + 300 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, target, SC_SOUNDBLEND).is_some() {
            *skillratio += *skillratio * 50 / 100;
        }
    }

    /// SkillReverberation::castendDamageId: the recursive call hits (skill_attack of the skill's type); the first
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

    /// SkillReverberation::modifyElement: a player's arrow element.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        super::arrow_element(c, src, element);
    }
}
