//! skills/swordman/ultimatesacrifice.cpp: class SkillUltimateSacrifice : public SkillImpl (IG_ULTIMATE_SACRIFICE).

use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::party_foreachsamemap_area_sub;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillUltimateSacrifice;

impl SkillImpl for SkillUltimateSacrifice {
    fn skill_id(&self) -> u16 {
        IG_ULTIMATE_SACRIFICE
    }

    /// SkillUltimateSacrifice::castendNoDamageId: the status on the target (no party, or a party member's recursive
    /// call; the packet's source is the target itself), else the caster drops to 1 HP and every party member within
    /// the splash range gets it. (rAthena: "Is the animation on this skill correct? Check if its on caster only or
    /// all affected. [Rytech]")
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let sd = if src.kind == BL_PC { crate::pc::character_of(c, src.id) } else { None };
        match sd {
            Some(ch) if ch.party_id != 0 && *flag & 1 == 0 => {
                crate::status::hp::status_set_hp(c, src.id, 1, 0);
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                let f = *flag | BCT_PARTY | 1;
                party_foreachsamemap_area_sub(c, &s, skill_get_splash(id, skill_lv) as i16, &s, id, skill_lv, tick, f, skill_castend_nodamage_id);
            }
            _ => {
                let ok = sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                clif_skill_nodamage(c, Some(&t), &t, id, skill_lv as i32, ok);
            }
        }
    }
}
