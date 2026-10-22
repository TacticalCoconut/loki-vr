//! skills/acolyte/laudaagnus.cpp: class SkillLaudaAgnus : public SkillImpl (AB_LAUDAAGNUS).

use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::party_foreachsamemap_area_sub;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_BLIND, SC_BURNING, SC_CRYSTALIZE, SC_FREEZE, SC_FREEZING, SC_STONE};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillLaudaAgnus;

/// The statuses Lauda Agnus cures, in rAthena's order.
const CURES: [i32; 6] = [SC_FREEZE, SC_STONE, SC_BLIND, SC_BURNING, SC_FREEZING, SC_CRYSTALIZE];

impl SkillImpl for SkillLaudaAgnus {
    fn skill_id(&self) -> u16 {
        AB_LAUDAAGNUS
    }

    /// SkillLaudaAgnus::castendNoDamageId: an afflicted target is cured ((60 + 10 x level)% chance: the success
    /// rate only applies to the curing effect); a healthy one gets the bonus status. On the target alone or every
    /// party member in range.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let sd = if src.kind == BL_PC { crate::pc::character_of(c, src.id) } else { None };
        match sd {
            Some(ch) if *flag & 1 == 0 && ch.party_id != 0 => {
                let f = *flag | BCT_PARTY | 1;
                party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, f, skill_castend_nodamage_id);
            }
            _ => {
                let has_tsc = crate::battle::mobif::has_sc(target.kind);
                if has_tsc && CURES.iter().any(|&t| sc::has_sce(c, target.id, t)) {
                    // Success Chance: (60 + 10 * Skill Level) %
                    if (c.rnd() % 100) as i32 > 60 + 10 * skill_lv as i32 {
                        return;
                    }
                    for t in CURES {
                        sc::status_change_end(c, target.id, t, 0);
                    }
                } else {
                    // Success rate only applies to the curing effect and not stat bonus. Bonus status only applies to
                    // non infected targets
                    let ok = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
                    let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                    clif_skill_nodamage(c, Some(&t), &t, id, skill_lv as i32, ok);
                }
            }
        }
    }
}
