//! skills/taekwon/soulunity.cpp: class SkillSoulUnity : public SkillImpl (SP_SOULUNITY).

use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage, fail::USESKILL_FAIL};
use crate::skill::state::MAX_UNITED_SOULS;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::party_foreachsamemap_area_sub;
use crate::skills::taekwon::arr_find;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSoulUnity;

impl SkillImpl for SkillSoulUnity {
    fn skill_id(&self) -> u16 {
        SP_SOULUNITY
    }

    /// SkillSoulUnity::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let sd = src.kind == BL_PC;
        let dstsd = target.kind == BL_PC;
        let mut i: usize = 0;

        let count = std::cmp::min(5 + skill_lv as usize, MAX_UNITED_SOULS);

        if !sd || crate::party::party_id_of(c, src.id) == 0 || (*flag & 1) != 0 {
            if !dstsd || !sd {
                // Only put player's souls in unity.
                if sd {
                    clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
                }
                return;
            }

            if sc::get_sce(c, target.id, type_).map(|e| e.val2 != src.id as i32).unwrap_or(false) {
                // Fail if a player is in unity with another source.
                if sd {
                    clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
                }
                *flag |= SKILL_NOCONSUME_REQ;
                return;
            }

            if sd {
                // Unite player's soul with caster's soul.
                let souls = crate::skill::state::get(c, src.id).united_soul;
                i = arr_find(&souls, count, target.id);
                if i == count {
                    i = arr_find(&souls, count, 0);
                    if i == count {
                        // No more free slots? Fail the skill.
                        clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
                        *flag |= SKILL_NOCONSUME_REQ;
                        return;
                    }
                }

                crate::skill::state::with(c, src.id, |s| s.united_soul[i] = target.id);
            }

            let ok = sc::sc_start4(c, Some(src), target, type_, 100, skill_lv as i32, src.id as i32, i as i32, 0, skill_get_time(id, skill_lv) as i64);
            let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
        } else if sd {
            party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, *flag | BCT_PARTY | 1, skill_castend_nodamage_id);
        }
    }
}
