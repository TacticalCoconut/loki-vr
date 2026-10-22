//! skills/mage/arrullo.cpp: class SkillArrullo : public SkillImpl (SO_ARRULLO).

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC};
use crate::skill::area::{foreach_in_area, skill_area_sub, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::{sc, status_get_status_data};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillArrullo;

impl SkillImpl for SkillArrullo {
    fn skill_id(&self) -> u16 {
        SO_ARRULLO
    }

    /// SkillArrullo::castendNoDamageId: the packet, then the skill's status (skill_get_sc) at
    /// `(15 + 5 * lv) + INT / 5 + job level / 5 - target INT / 6 - target LUK / 10` percent.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let lv = skill_lv as i32;
        // map_session_data* sd = BL_CAST(BL_PC, src); … (sd ? sd->status.job_level / 5 : 0)
        let job_level = if src.kind == BL_PC { crate::pc::character_of(c, src.id).map_or(0, |ch| ch.job_level as i32) } else { 0 };
        let sint = status_get_status_data(c, src).int_ as i32; // status_get_int(src)
        let tstatus = status_get_status_data(c, target);
        let rate = (15 + 5 * lv) + sint / 5 + job_level / 5 - tstatus.int_ as i32 / 6 - tstatus.luk as i32 / 10;

        clif_skill_nodamage(c, Some(src), target, id, lv, true);
        sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, rate, lv, skill_get_time(id, skill_lv) as i64);
    }

    /// SkillArrullo::castendPos2: skill_castend_nodamage_id (flag | BCT_ENEMY | 1) on every character in the splash
    /// square around the cell (map_foreachinallarea: no wall check).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let i = skill_get_splash(id, skill_lv) as i16;
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_CHAR, Wall::None, |c, bl| {
            skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id)
        });
    }
}
