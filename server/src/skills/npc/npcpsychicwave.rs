//! skills/npc/npcpsychicwave.cpp: class SkillNpcPsychicWave : public SkillImpl (NPC_PSYCHIC_WAVE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillNpcPsychicWave;

/// SkillNpcPsychicWave::modifyElement's `types`: the elemental option statuses, in their order (the first one the
/// caster has gives the element).
const OPTION_TYPES: [i32; 9] = [
    SC_HEATER_OPTION,
    SC_COOLER_OPTION,
    SC_BLAST_OPTION,
    SC_CURSED_SOIL_OPTION,
    SC_FLAMETECHNIC_OPTION,
    SC_COLD_FORCE_OPTION,
    SC_GRACE_BREEZE_OPTION,
    SC_EARTH_CARE_OPTION,
    SC_DEEP_POISONING_OPTION,
];

impl SkillImpl for SkillNpcPsychicWave {
    fn skill_id(&self) -> u16 {
        NPC_PSYCHIC_WAVE
    }

    /// SkillNpcPsychicWave::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 500 * skill_lv as i32;
    }

    /// SkillNpcPsychicWave::castendPos2: the skill's ground unit (skill_unitsetting: skillunit agent, skill/iface.rs).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillNpcPsychicWave::modifyElement: the element of the caster's elemental option status (its val3).
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        // const status_change* sc = status_get_sc(&src); if (sc != nullptr && !sc->empty())
        if let Some(sc) = crate::battle::bl::sc_nonempty(c, src) {
            for t in OPTION_TYPES {
                if let Some(e) = sc.get(t) {
                    *element = e.val3;
                    break;
                }
            }
        }
    }
}
