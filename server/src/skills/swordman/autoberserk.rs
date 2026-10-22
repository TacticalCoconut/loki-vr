//! skills/swordman/autoberserk.cpp: class SkillAutoBerserk : public SkillImpl (SM_AUTOBERSERK).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillAutoBerserk;

impl SkillImpl for SkillAutoBerserk {
    fn skill_id(&self) -> u16 {
        SM_AUTOBERSERK
    }

    /// SkillAutoBerserk::castendNoDamageId: a toggle (the running status ends, else it starts for 60 s).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, bl: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let i = if sce(c, bl, type_).is_some() {
            sc::status_change_end(c, bl.id, type_, 0) != 0
        } else {
            sc::sc_start(c, Some(src), bl, type_, 100, skill_lv as i32, 60000)
        };
        let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, i);
    }
}
