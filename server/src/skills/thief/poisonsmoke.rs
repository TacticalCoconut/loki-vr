//! skills/thief/poisonsmoke.cpp: class SkillPoisonSmoke : public SkillImpl (GC_POISONSMOKE).

use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::fail::USESKILL_FAIL_GC_POISONINGWEAPON;
use crate::skill::clif::{clif_skill_damage, clif_skill_fail, DMGVAL_IGNORE};
use crate::skill::iface::skill_unitsetting;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::{sce, status_get_amotion};
use crate::skills::SkillImpl;
use crate::status::consts::SC_POISONINGWEAPON;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPoisonSmoke;

impl SkillImpl for SkillPoisonSmoke {
    fn skill_id(&self) -> u16 {
        GC_POISONSMOKE
    }

    /// SkillPoisonSmoke::castendPos2: needs a poisoned weapon (SC_POISONINGWEAPON); a damage packet without damage on
    /// the caster, then the unit (with the cast's flag).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if sce(c, src, SC_POISONINGWEAPON).is_none() {
            if src.kind == BL_PC {
                clif_skill_fail(c, src, id, USESKILL_FAIL_GC_POISONINGWEAPON, 0, 0);
            }
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        clif_skill_damage(c, src, src, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        skill_unitsetting(c, src, id, skill_lv, x, y, *flag);
    }
}
