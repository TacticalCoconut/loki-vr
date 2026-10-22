//! skills/npc/propertyimmune.cpp: class SkillPropertyImmune : public SkillImpl (NPC_IMMUNE_PROPERTY).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillPropertyImmune;

impl SkillImpl for SkillPropertyImmune {
    fn skill_id(&self) -> u16 {
        NPC_IMMUNE_PROPERTY
    }

    /// SkillPropertyImmune::castendNoDamageId: the level picks the element immunity status (other levels: the
    /// skill's own status, skill_get_sc), then the animation with the result.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = match skill_lv {
            1 => SC_IMMUNE_PROPERTY_NOTHING,
            2 => SC_IMMUNE_PROPERTY_WATER,
            3 => SC_IMMUNE_PROPERTY_GROUND,
            4 => SC_IMMUNE_PROPERTY_FIRE,
            5 => SC_IMMUNE_PROPERTY_WIND,
            6 => SC_IMMUNE_PROPERTY_DARKNESS,
            7 => SC_IMMUNE_PROPERTY_SAINT,
            8 => SC_IMMUNE_PROPERTY_POISON,
            9 => SC_IMMUNE_PROPERTY_TELEKINESIS,
            10 => SC_IMMUNE_PROPERTY_UNDEAD,
            _ => skill_get_sc(id) as i32,
        };
        let ok = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}

#[cfg(test)]
mod tests {
    use crate::status::consts::*;

    /// The immunity statuses are script constants (status.hpp enum sc_type).
    #[test]
    fn immune_property_statuses_match_the_script_constants() {
        for (name, v) in [
            ("SC_IMMUNE_PROPERTY_NOTHING", SC_IMMUNE_PROPERTY_NOTHING),
            ("SC_IMMUNE_PROPERTY_WATER", SC_IMMUNE_PROPERTY_WATER),
            ("SC_IMMUNE_PROPERTY_GROUND", SC_IMMUNE_PROPERTY_GROUND),
            ("SC_IMMUNE_PROPERTY_FIRE", SC_IMMUNE_PROPERTY_FIRE),
            ("SC_IMMUNE_PROPERTY_WIND", SC_IMMUNE_PROPERTY_WIND),
            ("SC_IMMUNE_PROPERTY_DARKNESS", SC_IMMUNE_PROPERTY_DARKNESS),
            ("SC_IMMUNE_PROPERTY_SAINT", SC_IMMUNE_PROPERTY_SAINT),
            ("SC_IMMUNE_PROPERTY_POISON", SC_IMMUNE_PROPERTY_POISON),
            ("SC_IMMUNE_PROPERTY_TELEKINESIS", SC_IMMUNE_PROPERTY_TELEKINESIS),
            ("SC_IMMUNE_PROPERTY_UNDEAD", SC_IMMUNE_PROPERTY_UNDEAD),
        ] {
            assert_eq!(crate::data::constant(name), Some(v as i64), "{name}");
        }
    }
}
