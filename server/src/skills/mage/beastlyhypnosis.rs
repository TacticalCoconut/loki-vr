//! skills/mage/beastlyhypnosis.cpp: class SkillBeastlyHypnosis : public SkillImpl (SA_TAMINGMONSTER).

use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillBeastlyHypnosis;

impl SkillImpl for SkillBeastlyHypnosis {
    fn skill_id(&self) -> u16 {
        SA_TAMINGMONSTER
    }

    /// SkillBeastlyHypnosis::castendNoDamageId: a player casting on a monster gets the taming cursor for any
    /// monster (pet_catch_process_start(*sd, 0, PET_CATCH_UNIVERSAL_ALL)).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        if src.kind == BL_PC && target.kind == BL_MOB {
            crate::pet::pet_catch_process_start(c, src.id, 0, crate::pet::data::PET_CATCH_UNIVERSAL_ALL);
        }
    }
}
