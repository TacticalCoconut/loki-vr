//! skills/taekwon/soulofheavenandearth.cpp: class SkillSoulOfHeavenAndEarth : public SkillImpl (SOA_SOUL_OF_HEAVEN_AND_EARTH).

use crate::battle::BCT_PARTY;
use crate::conf::area_size;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::mob::unit::check_distance_bl;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{party_foreachsamemap_area_sub, sce};
use crate::skills::SkillImpl;
use crate::status::consts::SC_TOTEM_OF_TUTELARY;
use crate::status::hp::{status_heal, status_percent_heal};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillSoulOfHeavenAndEarth;

impl SkillImpl for SkillSoulOfHeavenAndEarth {
    fn skill_id(&self) -> u16 {
        SOA_SOUL_OF_HEAVEN_AND_EARTH
    }

    /// SkillSoulOfHeavenAndEarth::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        // map_session_data* sd = BL_CAST(BL_PC, src); sd->status.party_id
        let sd = if src.kind == BL_PC { crate::pc::character_of(c, src.id) } else { None };
        let party_id = sd.as_ref().map(|ch| ch.party_id).unwrap_or(0);
        if sd.is_none() || party_id == 0 || *flag & 1 != 0 {
            // Animations don't play when outside visitargete range
            if check_distance_bl(src, target, area_size() as i32) {
                clif_skill_nodamage(c, Some(target), target, id, skill_lv as i32, true);
            }
            status_percent_heal(c, target.id, 0, 100, 0);
            // status_change* sc = status_get_sc(src) (read now through the pointer)
            if src.id != target.id && sce(c, src, SC_TOTEM_OF_TUTELARY).is_some() {
                status_heal(c, target.id, 0, 0, 3 * skill_lv as i64, 0);
            }
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            sc::sc_start(c, Some(src), &t, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        } else if sd.is_some() {
            party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, *flag | BCT_PARTY | 1, skill_castend_nodamage_id);
        }
    }
}
