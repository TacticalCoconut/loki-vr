//! skills/elemental/firemantle.cpp: class SkillFireMantle : public SkillImpl (EL_FIRE_MANTLE), rAthena e985006.
//! The unit group itself is skillunit's (skill_unitsetting's case for the skill).

use crate::battle::calc::Damage;
use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::skill::clif::{clif_skill_damage, DMGVAL_IGNORE};
use crate::skill::iface;
use crate::skills::novice::status_get_amotion;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillFireMantle;

impl SkillImpl for SkillFireMantle {
    fn skill_id(&self) -> u16 {
        EL_FIRE_MANTLE
    }

    /// SkillFireMantle::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 900;
    }

    /// SkillFireMantle::castendNoDamageId: the dummy damage packet on the target, then the skill's unit group on the
    /// target's cell (skill_unitsetting flag 0).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_damage(c, src, target, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        iface::skill_unitsetting(c, &s, id, skill_lv, target.x, target.y, 0);
    }
}
