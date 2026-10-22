//! skills/mage/psychicwave.cpp: class SkillPsychicWave : public SkillImpl (SO_PSYCHIC_WAVE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::iface;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::defs::{W_2HSTAFF, W_BOOK, W_STAFF};
use crate::status::status_get_status_data;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPsychicWave;

/// The element-giving statuses modifyElement looks for, in its order (`static const std::vector<sc_type> types`).
const ELEMENT_OPTIONS: [i32; 9] = [
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

impl SkillImpl for SkillPsychicWave {
    fn skill_id(&self) -> u16 {
        SO_PSYCHIC_WAVE
    }

    /// SkillPsychicWave::modifyDamageData: 2 hits when the player holds a staff, two-handed staff or book in the
    /// right hand (sd->weapontype1).
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if src.kind != BL_PC {
            return;
        }
        let Some(it) = crate::inventory::st(c, src.id) else { return };
        let w = it.weapontype1 as i32;
        if w == W_STAFF || w == W_2HSTAFF || w == W_BOOK {
            dmg.div_ = 2;
        }
    }

    /// SkillPsychicWave::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = status_get_status_data(c, src);

        *skillratio += -100 + 70 * skill_lv as i32 + 3 * sstatus.int_ as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_HEATER_OPTION).is_some()
            || sce(c, src, SC_COOLER_OPTION).is_some()
            || sce(c, src, SC_BLAST_OPTION).is_some()
            || sce(c, src, SC_CURSED_SOIL_OPTION).is_some()
        {
            *skillratio += 20;
        }
    }

    /// SkillPsychicWave::castendPos2: the skill's unit group.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillPsychicWave::modifyElement: the element (val3) of the caster's first elemental option status, in the
    /// order of ELEMENT_OPTIONS.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        // sc != nullptr && !sc->empty()
        let Some(sc) = crate::battle::bl::sc_nonempty(c, src) else { return };
        for t in ELEMENT_OPTIONS {
            if let Some(e) = sc.get(t) {
                *element = e.val3;
                break;
            }
        }
    }
}
