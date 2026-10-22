//! skills/mage/stasis.cpp: class SkillStasis : public SkillImpl (WL_STASIS), rAthena e985006. The skill's status
//! (skill_get_sc: SC_STASIS) on every character around the caster: enemies and the caster itself, everyone on a
//! PvP / GvG / battleground map.

use crate::battle::{BCT_ALL, BCT_ENEMY, BCT_SELF};
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillStasis;

impl SkillImpl for SkillStasis {
    fn skill_id(&self) -> u16 {
        WL_STASIS
    }

    /// SkillStasis::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if *flag & 1 != 0 {
            sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        } else {
            // struct map_data *mapdata = map_getmapdata(src->m); mapdata_flag_vs(mapdata)
            let bct = if crate::npc::mapflag_vs(c, src.map) { BCT_ALL } else { BCT_ENEMY | BCT_SELF };
            let f = bct | *flag | 1;
            foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::None, |c, bl| {
                area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id)
            });
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
        }
    }
}
