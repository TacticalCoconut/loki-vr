//! skills/thief/fatalmenace.cpp: class SkillFatalMenace : public WeaponSkillImpl (SC_FATALMENACE).

use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, DMG_SINGLE};
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::area::{foreach_in_range, skill_area_sub, splash_target, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_damage, DMGVAL_IGNORE};
use crate::skills::novice::{re_lvl_dmod, sce, status_get_amotion};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_ABYSS_DAGGER;
use crate::status::defs::W_DAGGER;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillFatalMenace;

impl SkillImpl for SkillFatalMenace {
    fn skill_id(&self) -> u16 {
        SC_FATALMENACE
    }

    /// SkillFatalMenace::modifyDamageData: one more hit with a dagger in the right hand (sd->weapontype1).
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if src.kind == BL_PC && crate::inventory::st(c, src.id).map(|s| s.weapontype1 as i32 == W_DAGGER).unwrap_or(false) {
            dmg.div_ += 1;
        }
    }

    /// SkillFatalMenace::calculateSkillRatio: `skillratio += 120 * skill_lv + sstatus->agi; // !TODO: What's the AGI
    /// bonus?`
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += 120 * lv + sstatus.agi as i32;
        if sce(c, src, SC_ABYSS_DAGGER).is_some() {
            *skillratio += 30 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillFatalMenace::castendDamageId: flag&1 hits a splash target; else the splash around the target, then the
    /// caster's damage packet without damage.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            weapon_castend_damage_id(c, id, src, target, skill_lv, tick, *flag);
        } else {
            let f = *flag | BCT_ENEMY | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, splash_target(src), Wall::Config, |c, bl| skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_damage(c, &s, &s, tick, status_get_amotion(c, &s), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        }
    }

    /// SkillFatalMenace::modifyHitRate.
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, skill_lv: u16) {
        let lv = skill_lv as i16;
        if skill_lv < 6 {
            *hit_rate = hit_rate.wrapping_sub(35 - 5 * lv);
        } else if skill_lv > 6 {
            *hit_rate = hit_rate.wrapping_add(5 * lv - 30);
        }
    }
}
