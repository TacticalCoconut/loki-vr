//! skills/other/netsupport.cpp: class SkillNetSupport : public SkillImpl (ABR_NET_SUPPORT).

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

pub struct SkillNetSupport;

impl SkillImpl for SkillNetSupport {
    fn skill_id(&self) -> u16 {
        ABR_NET_SUPPORT
    }

    /// SkillNetSupport::castendNoDamageId: the area call restores 3 % of the target's max SP (the packet as
    /// MG_SRECOVERY, sent before the heal); the first call sends the packet and searches the allies in the splash
    /// range.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let tstatus = crate::status::status_get_status_data(c, target);
        if *flag & 1 != 0 {
            let heal_amount = (tstatus.max_sp.wrapping_mul(3) / 100) as i32;
            clif_skill_nodamage(c, None, target, MG_SRECOVERY, heal_amount, true);
            status_heal(c, target.id, 0, heal_amount as i64, 0, 0);
        } else {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let f = *flag | BCT_ALLY | SD_SPLASH | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
        }
    }
}
