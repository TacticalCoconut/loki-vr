//! skills/acolyte/aspersio.cpp: class SkillAspersio : public SkillImpl (PR_ASPERSIO).

use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::attack::skill_attack;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillAspersio;

impl SkillImpl for SkillAspersio {
    fn skill_id(&self) -> u16 {
        PR_ASPERSIO
    }

    /// SkillAspersio::castendNoDamageId: a player cannot endow a monster (the packet reports the failure).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind == BL_PC && target.kind == BL_MOB {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, false);
            return;
        }
        let ok = sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }

    /// SkillAspersio::castendDamageId: a player's offensive cast on an undead enemy target (skill_castend_nodamage_id
    /// redirects it).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }
}
