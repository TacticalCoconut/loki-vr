//! skills/merchant/magneticfield.cpp: class SkillMagneticField : public SkillImpl (NC_MAGNETICFIELD).

use crate::battle::{BCT_ENEMY, DMG_SINGLE};
use crate::db::skills::*;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::{clif_skill_damage, DMGVAL_IGNORE};
use crate::skill::SD_SPLASH;
use crate::skills::novice::status_get_amotion;
use crate::skills::SkillImpl;
use crate::status::consts::SC_MAGNETICFIELD;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillMagneticField::SkillMagneticField() : SkillImpl(NC_MAGNETICFIELD).
pub struct SkillMagneticField;

impl SkillImpl for SkillMagneticField {
    fn skill_id(&self) -> u16 {
        NC_MAGNETICFIELD
    }

    /// SkillMagneticField::castendNoDamageId: each enemy of the splash (flag & 1) gets SC_MAGNETICFIELD; the first call
    /// gives the caster the skill's status only on PvP / GvG maps ("Doesn't affect the caster in non-PVP maps"),
    /// walks the splash (map_foreachinallrange, flag | BCT_ENEMY | SD_SPLASH | 1) and sends the damage packet without
    /// a value.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            sc::sc_start2(c, Some(src), target, SC_MAGNETICFIELD, 100, skill_lv as i32, src.id as i32, skill_get_time(id, skill_lv) as i64);
        } else {
            if crate::npc::mapflag_vs(c, src.map) {
                // Doesn't affect the caster in non-PVP maps [exneval]
                sc::sc_start2(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, src.id as i32, skill_get_time(id, skill_lv) as i64);
            }
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
            foreach_in_range(c, &t, skill_get_splash(id, skill_lv) as i16, area::splash_target(src), Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or(t);
            clif_skill_damage(c, &s, &t, tick, status_get_amotion(c, &s), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        }
    }
}
