//! skills/swordman/servantweaponsign.cpp: class SkillServantWeaponSign : public SkillImpl (DK_SERVANT_W_SIGN).

use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage, fail::USESKILL_FAIL};
use crate::skill::state::{self, MAX_SERVANT_SIGN};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillServantWeaponSign;

impl SkillImpl for SkillServantWeaponSign {
    fn skill_id(&self) -> u16 {
        DK_SERVANT_W_SIGN
    }

    /// SkillServantWeaponSign::castendNoDamageId: a player marks the target in its servant_sign list (val1 the
    /// caster, val2 the slot); a monster only gives the status.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let sd = src.kind == BL_PC;
        let md = src.kind == BL_MOB;
        let dstsd = target.kind == BL_PC;
        let dstmd = target.kind == BL_MOB;
        let type_ = skill_get_sc(id) as i32;
        // Max allowed targets to be marked.
        // Only players and monsters can be marked....I think??? [Rytech]
        // Lets only allow players and monsters to use this skill for safety reasons.
        if (!dstsd && !dstmd) || (!sd && !md) {
            if sd {
                clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
            }
            return;
        }
        // Check if the target is already marked by another source.
        if sce(c, target, type_).map(|e| e.val1 != src.id as i32).unwrap_or(false) {
            if sd {
                clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
            }
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        // Mark the target.
        if sd {
            let count = MAX_SERVANT_SIGN;
            let mut st = state::get(c, src.id);
            let slot = |st: &state::PcSkill, i: usize| st.servant_sign.get(i).copied().unwrap_or(0);
            let mut i = (0..count).find(|&i| slot(&st, i) == target.id).unwrap_or(count);
            if i == count {
                i = (0..count).find(|&i| slot(&st, i) == 0).unwrap_or(count);
                if i == count {
                    // Max number of targets marked. Fail the skill.
                    clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
                    *flag |= SKILL_NOCONSUME_REQ;
                    return;
                }
                // Add the ID of the marked target to the player's sign list.
                if st.servant_sign.len() < MAX_SERVANT_SIGN {
                    st.servant_sign.resize(MAX_SERVANT_SIGN, 0);
                }
                st.servant_sign[i] = target.id;
                state::put(c, &st);
            }
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            sc::sc_start4(c, Some(src), &t, type_, 100, src.id as i32, i as i32, skill_lv as i32, 0, skill_get_time(id, skill_lv) as i64);
        } else if md {
            // Monster's cant track with this skill. Just give the status.
            let ok = sc::sc_start4(c, Some(src), target, type_, 100, 0, 0, skill_lv as i32, 0, skill_get_time(id, skill_lv) as i64);
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
        }
    }
}
