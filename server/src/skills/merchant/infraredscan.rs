//! skills/merchant/infraredscan.cpp: class SkillInfraredScan : public SkillImpl (NC_INFRAREDSCAN).

use crate::battle::{BCT_ENEMY, DMG_SINGLE};
use crate::db::skills::*;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_damage, DMGVAL_IGNORE};
use crate::skill::SD_SPLASH;
use crate::skills::novice::{sce, status_get_amotion};
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillInfraredScan::SkillInfraredScan() : SkillImpl(NC_INFRAREDSCAN).
pub struct SkillInfraredScan;

impl SkillImpl for SkillInfraredScan {
    fn skill_id(&self) -> u16 {
        NC_INFRAREDSCAN
    }

    /// SkillInfraredScan::castendDamageId: each target of the splash (flag & 1) loses Hiding, Cloaking, Cloaking
    /// Exceed, Camouflage and New Moon (Shadow Form with a [100 - level x 10]% chance) and gets SC_INFRAREDSCAN; the
    /// first call sends the damage packet without a value and walks the splash (map_foreachinallrange).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            for t in [SC_HIDING, SC_CLOAKING, SC_CLOAKINGEXCEED, SC_CAMOUFLAGE, SC_NEWMOON] {
                sc::status_change_end(c, target.id, t, 0);
            }
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            if let Some(e) = sce(c, &t, SC__SHADOWFORM) {
                // [100 - (Skill Level x 10)] %
                if ((c.rnd() % 100) as i32) < 100 - e.val1 * 10 {
                    sc::status_change_end(c, target.id, SC__SHADOWFORM, 0);
                }
            }
            let t = unit::get(c, target.id).unwrap_or(t);
            sc::sc_start(c, Some(src), &t, SC_INFRAREDSCAN, 10000, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        } else {
            clif_skill_damage(c, src, target, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
            let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, area::splash_target(src), Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
        }
    }

    /// SkillInfraredScan::castendNoDamageId: skill_castend_damage_id on the caster itself.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_castend_damage_id(c, src, src, self.skill_id(), skill_lv, tick, *flag);
    }
}
