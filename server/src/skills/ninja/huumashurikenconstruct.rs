//! skills/ninja/huumashurikenconstruct.cpp: class SkillHuumaShurikenConstruct : public WeaponSkillImpl
//! (SS_FUUMAKOUCHIKU), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_SKILL};
use crate::skill::area::{self, map_foreachindir, map_foreachinpath, skill_attack_area};
use crate::skill::SKILL_ALTDMG_FLAG;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHuumaShurikenConstruct;

impl SkillImpl for SkillHuumaShurikenConstruct {
    fn skill_id(&self) -> u16 {
        SS_FUUMAKOUCHIKU
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillHuumaShurikenConstruct::calculateSkillRatio: 900 + 1750% per level (+ 200% for an exploding Huuma
    /// Shuriken Grasp, SKILL_ALTDMG_FLAG), + 100% x level per Huuma Shuriken Grasp level, + 5 x POW, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 900 + 1750 * skill_lv as i32;
        if wd.miscflag & SKILL_ALTDMG_FLAG != 0 {
            *skillratio += 200;
        }
        *skillratio += pc_checkskill(c, src, SS_FUUMASHOUAKU) * 100 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillHuumaShurikenConstruct::castendPos2: skill_attack_area along the path from the caster to (x, y), players
    /// and skill units alike (a Huuma Shuriken Grasp unit on the path explodes: skill_attack_area's SS_FUUMAKOUCHIKU
    /// rule).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        area::set_temp(1, 0);
        let atk_type = skill_get_type(id);
        let splash = skill_get_splash(id, skill_lv) as i16;
        let maxcount = skill_get_maxcount(id, skill_lv);
        let f = *flag;
        if crate::skill::conf::skill_eightpath_algorithm() {
            // Use official AoE algorithm
            map_foreachindir(c, src.map, src.x, src.y, x, y, splash, maxcount, 0, BL_CHAR | BL_SKILL, |c, bl| {
                skill_attack_area(c, bl, atk_type, src, src, id, skill_lv, tick, f, BCT_ENEMY)
            });
        } else {
            map_foreachinpath(c, src.map, src.x, src.y, x, y, splash, maxcount, BL_CHAR | BL_SKILL, |c, bl| {
                skill_attack_area(c, bl, atk_type, src, src, id, skill_lv, tick, f, BCT_ENEMY)
            });
        }
    }

    /// SkillHuumaShurikenConstruct::modifyElement: a player's arrow (ammunition) element.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        if let Some(e) = super::arrow_ele(c, src) {
            *element = e;
        }
    }
}
