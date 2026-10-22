//! skills/archer/winkofcharm.cpp: class SkillWinkofCharm : public SkillImpl (DC_WINKCHARM).

use crate::battle::{ATK_DEF, BF_WEAPON};
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_CONFUSION, SC_HALLUCINATION};
use crate::status::defs::{BF_LONG, BF_NORMAL};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWinkofCharm;

impl SkillImpl for SkillWinkofCharm {
    fn skill_id(&self) -> u16 {
        DC_WINKCHARM
    }

    /// SkillWinkofCharm::castendNoDamageId: "In Renewal it causes Confusion and Hallucination to 100% base chance" on
    /// players; on monsters Wink Charm (val2 the caster) with a chance of 40 + the level difference, which "triggers a
    /// 0 damage event and might make the monster switch target to caster"; then the no-damage packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if target.kind == crate::map::BL_PC {
            sc::sc_start(c, Some(src), target, SC_CONFUSION, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            sc::sc_start(c, Some(src), &super::fresh(c, target), SC_HALLUCINATION, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        } else if target.kind == crate::map::BL_MOB {
            // For monsters it causes Wink Charm with a chance depending on the level difference
            let rate = (crate::status::status_get_lv(c, src) - crate::status::status_get_lv(c, target)) + 40;
            if sc::sc_start2(c, Some(src), target, type_, rate, skill_lv as i32, src.id as i32, skill_get_time2(id, skill_lv) as i64) {
                // This triggers a 0 damage event and might make the monster switch target to caster
                let attack_type = (BF_WEAPON | BF_LONG | BF_NORMAL) as u16;
                crate::battle::weapon_attack::battle_damage(c, Some(src), &super::fresh(c, target), 0, 1, skill_lv, 0, ATK_DEF, attack_type, true, tick, false, false);
            }
        }
        clif_skill_nodamage(c, Some(src), &super::fresh(c, target), id, skill_lv as i32, true);
    }
}
