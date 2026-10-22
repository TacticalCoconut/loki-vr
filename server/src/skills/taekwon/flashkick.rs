//! skills/taekwon/flashkick.cpp: class SkillFlashKick : public SkillImpl (SJ_FLASHKICK).

use crate::battle::BF_WEAPON;
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::attack::skill_attack;
use crate::skill::clif::{clif_skill_fail, fail::USESKILL_FAIL};
use crate::skill::state::MAX_STELLAR_MARKS;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::sce;
use crate::skills::taekwon::arr_find;
use crate::skills::SkillImpl;
use crate::status::consts::SC_FLASHKICK;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFlashKick;

impl SkillImpl for SkillFlashKick {
    fn skill_id(&self) -> u16 {
        SJ_FLASHKICK
    }

    /// SkillFlashKick::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let md = src.kind == BL_MOB;
        let tmd = target.kind == BL_MOB;
        let sd = src.kind == BL_PC;
        let tsd = target.kind == BL_PC;

        // Only players and monsters can be tagged....I think??? [Rytech]
        // Lets only allow players and monsters to use this skill for safety reasons.
        if (!tsd && !tmd) || !sd && !md {
            if sd {
                clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
            }
            return;
        }

        // Check if the target is already tagged by another source.
        // (tsd / tmd: the target is a player or a monster here; both branches read its SC_FLASHKICK)
        if sce(c, target, SC_FLASHKICK).map(|e| e.val1 != src.id as i32).unwrap_or(false) {
            // Same as the above check, but for monsters.
            // Can't tag a player that was already tagged from another source.
            if sd {
                clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
            }
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }

        if sd {
            // Tagging the target.
            let marks = crate::skill::state::get(c, src.id).stellar_mark;
            let mut i = arr_find(&marks, MAX_STELLAR_MARKS, target.id);
            if i == MAX_STELLAR_MARKS {
                i = arr_find(&marks, MAX_STELLAR_MARKS, 0);
                if i == MAX_STELLAR_MARKS {
                    // Max number of targets tagged. Fail the skill.
                    clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
                    *flag |= SKILL_NOCONSUME_REQ;
                    return;
                }
            }

            // Tag the target only if damage was done. If it deals no damage, it counts as a miss and won't tag.
            // Note: Not sure if it works like this in official but you can't mark on something you can't
            // hit, right? For now well just use this logic until we can get a confirm on if it does this or not. [Rytech]
            if skill_attack(c, BF_WEAPON, src, src, target, id, skill_lv, tick, *flag) > 0 {
                // Add the ID of the tagged target to the player's tag list and start the status on the target.
                crate::skill::state::with(c, src.id, |s| s.stellar_mark[i] = target.id);

                // Val4 flags if the status was applied by a player or a monster.
                // This will be important for other skills that work together with this one.
                // 1 = Player, 2 = Monster.
                // Note: Because the attacker's ID and the slot number is handled here, we have to
                // apply the status here. We can't pass this data to skill_additional_effect.
                let s = crate::unit::get_or_freed(c, src.id).unwrap_or_else(|| src.clone());
                let t = crate::unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
                sc::sc_start4(c, Some(&s), &t, SC_FLASHKICK, 100, src.id as i32, i as i32, skill_lv as i32, 1, skill_get_time(id, skill_lv) as i64);
            }
        } else if md {
            // Monsters can't track with this skill. Just give the status.
            if skill_attack(c, BF_WEAPON, src, src, target, id, skill_lv, tick, *flag) > 0 {
                let s = crate::unit::get_or_freed(c, src.id).unwrap_or_else(|| src.clone());
                let t = crate::unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
                sc::sc_start4(c, Some(&s), &t, SC_FLASHKICK, 100, 0, 0, skill_lv as i32, 2, skill_get_time(id, skill_lv) as i64);
            }
        }
    }
}
