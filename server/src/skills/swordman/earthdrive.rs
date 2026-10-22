//! skills/swordman/earthdrive.cpp: class SkillEarthDrive : public SkillImplRecursiveDamageSplash (LG_EARTHDRIVE).

use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, DMG_SINGLE};
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skill::iface;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce, status_get_amotion};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_SHIELD_POWER;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillEarthDrive;

impl SkillImpl for SkillEarthDrive {
    fn skill_id(&self) -> u16 {
        LG_EARTHDRIVE
    }

    crate::splash_skill_defaults!();

    /// SkillEarthDrive::castendNoDamageId: the dummy damage packet, the skill units around the caster it overlaps
    /// (skill_cell_overlap: skillunit agent), the hits around the target, the caster's animation.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, bl: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_damage(c, src, bl, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        let i = skill_get_splash(id, skill_lv) as i16;
        // map_foreachinallarea(skill_cell_overlap, src->m, src->x-i, src->y-i, src->x+i, src->y+i, BL_SKILL, getSkillId(), &dummy, src)
        iface::skill_cell_overlap_area(c, src, src.map, src.x - i, src.y - i, src.x + i, src.y + i, id, Wall::None);
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_range(c, bl, i, BL_CHAR, Wall::Config, |c, t| area::skill_area_sub(c, t, src, id, skill_lv, tick, f, skill_castend_damage_id));
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, true);
    }

    /// SkillEarthDrive::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 380 * lv + sstatus.str as i32 + sstatus.vit as i32; // !TODO: What's the STR/VIT bonus?
        if sce(c, src, SC_SHIELD_POWER).is_some() {
            *skillratio += lv * 37 * pc_checkskill(c, src, IG_SHIELD_MASTERY);
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillEarthDrive {}
