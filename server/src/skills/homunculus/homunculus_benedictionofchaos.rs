//! skills/homunculus/homunculus_benedictionofchaos.cpp: class SkillBenedictionOfChaos : public SkillImpl
//! (HVAN_CHAOTIC).

use crate::battle::target::{battle_get_master, battle_gettargeted};
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::misc::skill_calc_heal;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// Chance per skill level (`chance_homunculus`).
const CHANCE_HOMUNCULUS: [u8; 5] = [20, 50, 25, 50, 34];
/// `chance_master`: chance_homunculus + 30 / + 10 / + 50 / + 4 / + 33.
const CHANCE_MASTER: [u8; 5] = [20 + 30, 50 + 10, 25 + 50, 50 + 4, 34 + 33];

pub struct SkillBenedictionOfChaos;

impl SkillImpl for SkillBenedictionOfChaos {
    fn skill_id(&self) -> u16 {
        HVAN_CHAOTIC
    }

    /// SkillBenedictionOfChaos::castendNoDamageId: one roll picks the healed unit (the homunculus, its master, or a
    /// random enemy targeting the master; the homunculus when there is none), the heal is skill_calc_heal at a random
    /// level from 1 to skill_lv. The Heal packet with the amount, then this skill's packet with 1 (official servers).
    /// (`chance_*[skill_lv - 1]` reads past the arrays above level 5 in rAthena; homunculus skills stop at 5: a higher
    /// level takes 0 here.)
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let i = (skill_lv as usize).wrapping_sub(1);
        let chance = c.rnd_value(1, 100) as u8;
        let target: Option<Unit> = if chance <= CHANCE_HOMUNCULUS.get(i).copied().unwrap_or(0) {
            // Homunculus
            Some(src.clone())
        } else if chance <= CHANCE_MASTER.get(i).copied().unwrap_or(0) {
            // Master
            Some(battle_get_master(c, src))
        } else {
            // Enemy (A random enemy targeting the master)
            battle_gettargeted(c, &battle_get_master(c, src))
        };
        // If there's no enemy the chance reverts to the homunculus
        let target = target.unwrap_or_else(|| src.clone());
        let heal = skill_calc_heal(c, src, Some(&target), id, c.rnd_value(1, skill_lv as i32) as u16, true);
        // Official servers send the Heal skill packet with the healed amount, and then the skill packet with 1 as
        // healed amount
        clif_skill_nodamage(c, Some(src), &target, AL_HEAL, heal, true);
        clif_skill_nodamage(c, Some(src), &target, id, 1, true);
        crate::status::hp::status_heal(c, target.id, heal as i64, 0, 0, 0);
    }
}
