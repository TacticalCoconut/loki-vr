//! skills/summoner/kisulrampage.cpp: class SkillKisulRampage : public SkillImpl (SH_KI_SUL_RAMPAGE).

use super::communes;
use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::hp::status_heal;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillKisulRampage;

impl SkillImpl for SkillKisulRampage {
    fn skill_id(&self) -> u16 {
        SH_KI_SUL_RAMPAGE
    }

    /// SkillKisulRampage::castendNoDamageId:
    /// - flag & 2 (the area call): 2 AP (6 with flag & 4) to every party member around but the centre;
    /// - flag & 1: the packet (level 0) and the area call around the target, with the target as the source (2 cells
    ///   wider and flag & 4 with the Ki Sul communion);
    /// - else the status on the target ("No party check required").
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 2 != 0 {
            if src.id == target.id {
                return;
            }
            let mut ap: i64 = 2;
            if *flag & 4 != 0 {
                ap += 4;
            }
            status_heal(c, target.id, 0, 0, ap, 0);
        } else if *flag & 1 != 0 {
            let mut range = skill_get_splash(id, skill_lv);
            if communes(c, src, SH_COMMUNE_WITH_KI_SUL) {
                range += 2;
                // Set a flag for AP increase
                *flag |= 4;
            }
            clif_skill_nodamage(c, Some(src), target, id, 0, true);
            let f = *flag | BCT_PARTY | 2;
            foreach_in_range(c, target, range as i16, BL_CHAR, Wall::Config, |c, bl| {
                // map_foreachinrange(skill_area_sub, target, range, BL_CHAR, target, …): the target is the source
                let centre = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                area::skill_area_sub(c, bl, &centre, id, skill_lv, tick, f, skill_castend_nodamage_id)
            });
        } else {
            // No party check required
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        }
    }
}
