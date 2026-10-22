//! skills/archer/springtrap.cpp: class SkillSpringTrap : public SkillImpl (HT_SPRINGTRAP).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::skillunit::consts::*;
use crate::skillunit::data;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSpringTrap;

impl SkillImpl for SkillSpringTrap {
    fn skill_id(&self) -> u16 {
        HT_SPRINGTRAP
    }

    /// SkillSpringTrap::castendNoDamageId: the no-damage packet; a Hunter trap (an Ankle Snare only while it holds
    /// nobody: "remove trap should be used instead") becomes a used trap that ends 1.5 s later.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        if target.kind != crate::map::BL_SKILL {
            return;
        }
        let Some(su) = data::get_unit(c, target.id) else { return };
        let Some(sg) = data::skill_id2group(c, su.group_id) else { return };
        let spring = match sg.unit_id {
            // if it is already trapping something don't spring it, remove trap should be used instead
            UNT_ANKLESNARE => sg.val2 == 0,
            UNT_BLASTMINE | UNT_SKIDTRAP | UNT_LANDMINE | UNT_SHOCKWAVE | UNT_SANDMAN | UNT_FLASHER | UNT_FREEZINGTRAP | UNT_CLAYMORETRAP | UNT_TALKIEBOX => true,
            _ => false,
        };
        if spring {
            data::upd_group(c, sg.group_id, |g| g.unit_id = UNT_USED_TRAPS);
            crate::skillunit::clif::clif_changetraplook(c, &super::fresh(c, target), UNT_USED_TRAPS);
            // su->group->limit = DIFF_TICK(tick+1500, su->group->tick); su->limit = DIFF_TICK(tick+1500, su->group->tick);
            data::upd_group(c, sg.group_id, |g| g.limit = tick + 1500 - g.tick);
            data::upd_unit(c, su.id(), |u| u.limit = tick + 1500 - sg.tick);
        }
    }
}
