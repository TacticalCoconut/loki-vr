//! skills/npc/earthquake.cpp: class SkillEarthquake : public SkillImpl (NPC_EARTHQUAKE), rAthena e985006. Its hits
//! come from its skill unit (skill_unitsetting: skillunit agent).

use crate::battle::calc::Damage;
use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::skill::clif::{clif_skill_damage, DMGVAL_IGNORE};
use crate::skill::iface;
use crate::skills::novice::status_get_amotion;
use crate::skills::SkillImpl;
use crate::status::defs::ELE_NEUTRAL;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillEarthquake;

impl SkillImpl for SkillEarthquake {
    fn skill_id(&self) -> u16 {
        NPC_EARTHQUAKE
    }

    /// SkillEarthquake::castendPos2: a dummy damage packet on the caster (level -1), then the unit group.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_damage(c, src, src, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, -1, DMG_SINGLE);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        iface::skill_unitsetting(c, &s, id, skill_lv, x, y, 0);
    }

    /// SkillEarthquake::modifyElement: always neutral.
    fn modify_element(&self, _c: &Ctx, _dmg: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        *element = ELE_NEUTRAL;
    }
}
