//! skills/swordman/windcutter.cpp: class SkillWindCutter : public SkillImplRecursiveDamageSplash (RK_WINDCUTTER).

use crate::battle::calc::Damage;
use crate::battle::{BF_LONG, DMG_SINGLE};
use crate::db::skills::*;
use crate::skill::area;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skills::novice::{re_lvl_dmod, status_get_amotion};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::defs::{W_1HSPEAR, W_2HSPEAR, W_2HSWORD};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillWindCutter;

impl SkillImpl for SkillWindCutter {
    fn skill_id(&self) -> u16 {
        RK_WINDCUTTER
    }

    crate::splash_skill_defaults!();

    /// SkillWindCutter::modifyDamageData: long range with a spear (sd->status.weapon), 2 hits with a two-handed
    /// sword (sd->weapontype1).
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if let Some(sd) = crate::battle::bl::sd(c, src) {
            let w = sd.ch.weapon as i32;
            if w == W_1HSPEAR || w == W_2HSPEAR {
                dmg.flag |= BF_LONG;
            }
            if sd.it.weapontype1 as i32 == W_2HSWORD {
                dmg.div_ = 2;
            }
        }
    }

    /// SkillWindCutter::calculateSkillRatio (by sd->weapontype1).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let lv = skill_lv as i32;
        match crate::battle::bl::sd(c, src) {
            Some(sd) => {
                let w1 = sd.it.weapontype1 as i32;
                if w1 == W_2HSWORD {
                    *skillratio += -100 + 250 * lv;
                } else if w1 == W_1HSPEAR || w1 == W_2HSPEAR {
                    *skillratio += -100 + 400 * lv;
                } else {
                    *skillratio += -100 + 300 * lv;
                }
            }
            None => *skillratio += -100 + 300 * lv,
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillWindCutter::castendNoDamageId: the splash through skill_castend_damage_id; the dummy damage packet on the
    /// caster when nothing was hit.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_castend_damage_id(c, src, target, id, skill_lv, tick, *flag);
        if area::temp(2) == 0 {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_damage(c, &s, &s, tick, status_get_amotion(c, &s), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        }
    }
}

impl RecursiveDamageSplash for SkillWindCutter {}
