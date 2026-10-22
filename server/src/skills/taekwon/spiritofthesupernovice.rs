//! skills/taekwon/spiritofthesupernovice.cpp: class SkillSpiritoftheSupernovice : public SkillImpl (SL_SUPERNOVICE).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::taekwon::spirit_castend_nodamage_id;
use crate::skills::SkillImpl;
use crate::status::consts::SP_PCDIECOUNTER;
use crate::status::defs::SCO_NONE;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// script.hpp e_effect EF_ANGEL2.
pub(crate) const EF_ANGEL2: i32 = 338;

pub struct SkillSpiritoftheSupernovice;

impl SkillImpl for SkillSpiritoftheSupernovice {
    fn skill_id(&self) -> u16 {
        SL_SUPERNOVICE
    }

    /// SkillSpiritoftheSupernovice::castendNoDamageId: the spirit link; on success, a 1% chance to erase the target's
    /// death count (before the caster's SC_SMA).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        spirit_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, |c| {
            if target.kind != BL_PC {
                return;
            }
            // 1% chance to erase death count on successful cast
            let die_counter = crate::status::data::sd_get(c, target.id).map(|s| s.die_counter).unwrap_or(0);
            if die_counter != 0 && c.rnd_chance(1, 100) {
                crate::status::param::setparam(c, target.id, SP_PCDIECOUNTER as i64, 0);
                if let Some(t) = crate::unit::get(c, target.id) {
                    crate::clif::specialeffect(c, &t, EF_ANGEL2, crate::clif::Target::Area);
                }
                crate::status::calc_pc(c, target.id, SCO_NONE);
            }
        });
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn effect_ids() {
        assert_eq!(crate::data::constant("EF_ANGEL2"), Some(super::EF_ANGEL2 as i64));
    }
}
