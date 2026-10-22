//! skills/ninja/fourcolorscharm.cpp: class SkillFourColorsCharm : public StatusSkillImpl (SS_FOUR_CHARM), rAthena
//! e985006.

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::state::{self as pcskill, CHARM_TYPE_FIRE, CHARM_TYPE_LAND, CHARM_TYPE_WATER, CHARM_TYPE_WIND};
use crate::skills::SkillImpl;
use crate::status::consts::{SC_FIRE_CHARM_POWER, SC_GROUND_CHARM_POWER, SC_NONE, SC_WATER_CHARM_POWER, SC_WIND_CHARM_POWER};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFourColorsCharm;

impl SkillImpl for SkillFourColorsCharm {
    fn skill_id(&self) -> u16 {
        SS_FOUR_CHARM
    }

    /// SkillFourColorsCharm::castendNoDamageId: a player's charm type picks the power status (fire, water, earth,
    /// wind); none without charms.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        let type_ = match pcskill::get(c, src.id).spiritcharm_type {
            CHARM_TYPE_FIRE => SC_FIRE_CHARM_POWER,
            CHARM_TYPE_WATER => SC_WATER_CHARM_POWER,
            CHARM_TYPE_LAND => SC_GROUND_CHARM_POWER,
            CHARM_TYPE_WIND => SC_WIND_CHARM_POWER,
            _ => SC_NONE,
        };
        if type_ != SC_NONE {
            let ok = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
        }
    }
}
