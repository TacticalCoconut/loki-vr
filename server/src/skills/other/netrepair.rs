//! skills/other/netrepair.cpp: class SkillNetRepair : public SkillImpl (ABR_NET_REPAIR).

use crate::battle::BCT_ALLY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_SPLASH;
use crate::skills::SkillImpl;
use crate::status::hp::status_heal;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillNetRepair;

impl SkillImpl for SkillNetRepair {
    fn skill_id(&self) -> u16 {
        ABR_NET_REPAIR
    }

    /// SkillNetRepair::castendNoDamageId: the area call heals 10 % of the target's max HP (the heal packet as
    /// AL_HEAL, sent before the heal); the first call sends the packet and searches the allies in the splash range.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let tstatus = crate::status::status_get_status_data(c, target);
        if *flag & 1 != 0 {
            let heal_amount = (tstatus.max_hp.wrapping_mul(10) / 100) as i32;
            clif_skill_nodamage(c, None, target, AL_HEAL, heal_amount, true);
            status_heal(c, target.id, heal_amount as i64, 0, 0, 0);
        } else {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let f = *flag | BCT_ALLY | SD_SPLASH | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
        }
    }
}
