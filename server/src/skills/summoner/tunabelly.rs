//! skills/summoner/tunabelly.cpp: class SkillTunaBelly : public SkillImpl (SU_TUNABELLY).

use crate::db::skills::*;
use crate::map::BL_MOB;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::defs::CLASS_BATTLEFIELD;
use crate::status::hp::status_heal;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillTunaBelly;

impl SkillImpl for SkillTunaBelly {
    fn skill_id(&self) -> u16 {
        SU_TUNABELLY
    }

    /// SkillTunaBelly::castendNoDamageId: heals (2 * level - 1) * 10 % of the target's max HP (uint32 math) unless it
    /// is at full HP, an Emperium or a battlefield-class monster; the packet comes first.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let is_emperium = target.kind == BL_MOB && crate::mob::get_or_freed(c, target.id).map(|md| md.mob_id as u32 == crate::db::mobs::MOBID_EMPERIUM).unwrap_or(false);
        let tstatus = crate::status::status_get_status_data(c, target);
        let mut heal: u32 = 0;
        if target.kind == BL_MOB && (is_emperium || crate::battle::bl::class_type(c, target) == CLASS_BATTLEFIELD) {
            heal = 0;
        } else if tstatus.hp != tstatus.max_hp {
            heal = (((2 * skill_lv as i32 - 1) * 10) as u32).wrapping_mul(tstatus.max_hp) / 100;
        }
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        status_heal(c, target.id, heal as i64, 0, 0, 0);
    }
}
