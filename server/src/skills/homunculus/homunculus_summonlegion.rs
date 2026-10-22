//! skills/homunculus/homunculus_summonlegion.cpp: class SkillSummonLegion : public SkillImpl (MH_SUMMON_LEGION) and
//! its map_foreachinmap callback summon_legion_count_sub.

use crate::db::skills::*;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::SkillImpl;
use crate::status::consts::SC_MODECHANGE;
use crate::status::defs::{MD_AGGRESSIVE, MD_CANATTACK, SZ_SMALL};
use crate::status::sc;
use crate::timer::{self, Func};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// mob.hpp MOBID_S_HORNET / MOBID_S_GIANT_HORNET / MOBID_S_LUCIOLA_VESPA.
pub const MOBID_S_HORNET: i32 = 2158;
pub const MOBID_S_GIANT_HORNET: i32 = 2159;
pub const MOBID_S_LUCIOLA_VESPA: i32 = 2160;

/// The monster summoned per skill level (`summons`) and how many (`qty`).
const SUMMONS: [i32; 5] = [MOBID_S_HORNET, MOBID_S_GIANT_HORNET, MOBID_S_GIANT_HORNET, MOBID_S_LUCIOLA_VESPA, MOBID_S_LUCIOLA_VESPA];
const QTY: [i32; 5] = [3, 3, 4, 4, 5];

pub struct SkillSummonLegion;

impl SkillImpl for SkillSummonLegion {
    fn skill_id(&self) -> u16 {
        MH_SUMMON_LEGION
    }

    /// SkillSummonLegion::castendNoDamageId: nothing (and no requirement consumed: SKILL_NOCONSUME_REQ) while the
    /// level's quantity of its insects is out; otherwise that many insects of the level's class at the caster's
    /// cell (the caster's name, AI_ATTACK then AI_LEGION with the caster as master), each deleted after
    /// skill_get_time and in SC_MODECHANGE (can attack, aggressive) for 60 s. (`summons[skill_lv - 1]` reads past
    /// the arrays above level 5 in rAthena; homunculus skills stop at 5: a higher level does nothing here.)
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let i = (skill_lv as usize).wrapping_sub(1);
        let (Some(&mob_class), Some(&maxcount)) = (SUMMONS.get(i), QTY.get(i)) else { return };
        let mut count = 0;
        for bl in crate::mob::mobs_foreachinmap(c, src.map) {
            summon_legion_count_sub(c, &bl, src.id, mob_class, &mut count);
        }
        if count >= maxcount {
            *flag |= SKILL_NOCONSUME_REQ;
            return; // max qty already spawned
        }
        for _ in 0..maxcount {
            // easy way
            let spawned = crate::mob::spawn::mob_once_spawn_sub(c, Some(src), src.map, src.x, src.y, Some(&src.name), mob_class, Some(""), SZ_SMALL as u8, crate::mob::AI_ATTACK);
            let Some(mut sum_md) = spawned else { continue };
            sum_md.master_id = src.id;
            sum_md.special_ai = crate::mob::AI_LEGION;
            if sum_md.deletetimer != 0 {
                timer::delete_checked(c, sum_md.deletetimer, Func::MobTimerDelete);
            }
            sum_md.deletetimer = timer::add(c, c.tick() + skill_get_time(id, skill_lv) as Tick, Func::MobTimerDelete, sum_md.id, 0);
            crate::mob::put(c, &sum_md);
            crate::mob::spawn::mob_spawn(c, sum_md.id); // Now it is ready for spawning.
            if let Some(mu) = crate::unit::get(c, sum_md.id) {
                sc::sc_start4(c, Some(&mu), &mu, SC_MODECHANGE, 100, 1, 0, MD_CANATTACK | MD_AGGRESSIVE, 0, 60000);
            }
        }
    }
}

/// homunculus_summonlegion.cpp summon_legion_count_sub: 1 for a legion insect of `src_id` (`*count` counts those
/// of class `mob_class`), 0 otherwise.
fn summon_legion_count_sub(c: &Ctx, bl: &Unit, src_id: u32, mob_class: i32, count: &mut i32) -> i32 {
    let Some(md) = crate::mob::get(c, bl.id) else { return 0 };
    if md.master_id != src_id || md.special_ai != crate::mob::AI_LEGION {
        return 0;
    }
    if md.mob_id == mob_class {
        *count += 1;
    }
    1
}
