//! skills/merchant/fullprotection.cpp: class SkillFullProtection : public SkillImpl (CR_FULLPROTECTION).

use crate::db::skills::*;
use crate::inventory::{EQP_ARMOR, EQP_HEAD_TOP, EQP_SHIELD, EQP_WEAPON};
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::SkillImpl;
use crate::status::consts::SC_CP_WEAPON;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillFullProtection::SkillFullProtection() : SkillImpl(CR_FULLPROTECTION).
pub struct SkillFullProtection;

impl SkillImpl for SkillFullProtection {
    fn skill_id(&self) -> u16 {
        CR_FULLPROTECTION
    }

    /// SkillFullProtection::castendNoDamageId: SC_CP_WEAPON + i (weapon, shield, armor, helm) for every piece a player
    /// target wears; a player caster whose target got none fails (requirements not consumed).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let equip = [EQP_WEAPON, EQP_SHIELD, EQP_ARMOR, EQP_HEAD_TOP];
        let skilltime = skill_get_time(id, skill_lv) as i64;
        let mut s = 0;
        for (i_eqp, eqp) in equip.iter().enumerate() {
            // target->type != BL_PC || ( dstsd && pc_checkequip(dstsd,equip[i_eqp]) < 0 )
            if target.kind != BL_PC || crate::pc_equip::pc_checkequip(c, target.id, *eqp, false) < 0 {
                continue;
            }
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            sc::sc_start(c, Some(src), &t, SC_CP_WEAPON + i_eqp as i32, 100, skill_lv as i32, skilltime);
            s += 1;
        }
        if src.kind == BL_PC && s == 0 {
            skill_fail(c, src, id);
            // Don't consume item requirements
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
    }
}

#[cfg(test)]
mod tests {
    /// `(sc_type)(SC_CP_WEAPON + i_eqp)`: status.hpp keeps SC_CP_WEAPON, SC_CP_SHIELD, SC_CP_ARMOR, SC_CP_HELM in a row.
    #[test]
    fn chemical_protection_statuses_are_consecutive() {
        use crate::status::consts::*;
        assert_eq!([SC_CP_SHIELD, SC_CP_ARMOR, SC_CP_HELM], [SC_CP_WEAPON + 1, SC_CP_WEAPON + 2, SC_CP_WEAPON + 3]);
    }
}
