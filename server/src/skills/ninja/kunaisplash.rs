//! skills/ninja/kunaisplash.cpp: class SkillKunaiSplash : public SkillImplRecursiveDamageSplash (KO_HAPPOKUNAI),
//! rAthena e985006.

use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::skill::area;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skills::novice::status_get_amotion;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillKunaiSplash;

impl SkillImpl for SkillKunaiSplash {
    fn skill_id(&self) -> u16 {
        KO_HAPPOKUNAI
    }

    crate::splash_skill_defaults!();

    /// SkillKunaiSplash::castendNoDamageId: the animation, skill_castend_damage_id (the splash), and the caster's
    /// damage-less animation when it hit nobody (skill_area_temp[2] == 0).
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

impl RecursiveDamageSplash for SkillKunaiSplash {}
