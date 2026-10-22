//! skills/merchant/woodenwarrior.cpp: class SkillWoodenWarrior : public SkillImpl (BO_WOODENWARRIOR).

use super::ids::MOBID_BIONIC_WOODENWARRIOR;
use crate::db::skills::*;
use crate::mob::spawn::mob_once_spawn_sub;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillWoodenWarrior::SkillWoodenWarrior() : SkillImpl(BO_WOODENWARRIOR).
pub struct SkillWoodenWarrior;

impl SkillImpl for SkillWoodenWarrior {
    fn skill_id(&self) -> u16 {
        BO_WOODENWARRIOR
    }

    /// SkillWoodenWarrior::castendNoDamageId: the skill packet, the skill's status on the target, then the bionic summon on the
    /// caster's cell ("--ja--", AI_BIONIC), attached to the caster until skill_get_time ends it.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        sc::sc_start(c, Some(src), &t, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let md = mob_once_spawn_sub(c, Some(&s), s.map, s.x, s.y, Some("--ja--"), MOBID_BIONIC_WOODENWARRIOR, Some(""), crate::status::defs::SZ_SMALL as u8, crate::mob::AI_BIONIC);
        if let Some(md) = md {
            super::summon_attach_and_spawn(c, md, Some(src.id), crate::mob::AI_BIONIC as i32, c.tick() + skill_get_time(id, skill_lv) as Tick);
        }
    }
}
