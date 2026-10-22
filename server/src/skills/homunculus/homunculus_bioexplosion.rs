//! skills/homunculus/homunculus_bioexplosion.cpp: class SkillBioExplosion : public SkillImpl (HVAN_EXPLOSION).

use super::{fresh, hd_of};
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::homunculus::data::{HOMGRADE_HATE_WITH_PASSION, SP_INTIMATE};
use crate::map::{BL_CHAR, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillBioExplosion;

impl SkillImpl for SkillBioExplosion {
    fn skill_id(&self) -> u16 {
        HVAN_EXPLOSION
    }

    /// SkillBioExplosion::castendNoDamageId: the explosion (map_foreachinshootrange of skill_castend_damage_id around
    /// the target), the intimacy falls to "hate with passion", and the homunculus dies after skill_get_time
    /// (skill_timerskill's HVAN_EXPLOSION arm: status_kill).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if hd_of(c, src).is_none() {
            return;
        }
        clif_skill_nodamage(c, Some(src), src, id, skill_lv as i32, true);
        let f = *flag | BCT_ENEMY;
        foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::Always, |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
        });
        // hd->homunculus.intimacy = hom_intimacy_grade2intimacy(HOMGRADE_HATE_WITH_PASSION) (the row as it is now)
        crate::homunculus::data::with(c, src.id, |hd| hd.homunculus.intimacy = crate::homunculus::hom_intimacy_grade2intimacy(HOMGRADE_HATE_WITH_PASSION));
        crate::homunculus::clif::clif_send_homdata(c, src.id, SP_INTIMATE);
        // There's a delay between the explosion and the homunculus death
        let s = fresh(c, src);
        crate::skill::timerskill::skill_addtimerskill(c, &s, tick + skill_get_time(id, skill_lv) as Tick, s.id, 0, 0, id, skill_lv, 0, *flag);
    }

    /// SkillBioExplosion::castendDamageId: every unit of the area but the homunculus itself.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        if src.id != target.id {
            let id = self.skill_id();
            crate::skill::attack::skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        }
    }
}
