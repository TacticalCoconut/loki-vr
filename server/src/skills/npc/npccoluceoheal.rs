//! skills/npc/npccoluceoheal.cpp: class SkillNpcColuceoHeal : public SkillImpl (NPC_CHEAL), rAthena e985006.

use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::map::BL_MOB;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::misc::skill_calc_heal;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_AKAITSUKI, SC_BERSERK};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillNpcColuceoHeal;

impl SkillImpl for SkillNpcColuceoHeal {
    fn skill_id(&self) -> u16 {
        NPC_CHEAL
    }

    /// SkillNpcColuceoHeal::castendNoDamageId: a level 10 Heal on every monster of the caster's party around it
    /// (flag & 1: one of them).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            let tstatus = crate::status::status_get_status_data(c, target);
            // status_change *tsc = status_get_sc(target)
            let tsc = crate::battle::bl::sc(c, target);
            if !sc::battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) && tsc.as_ref().map(|s| !s.has(SC_BERSERK)).unwrap_or(false) {
                let mut i = skill_calc_heal(c, src, Some(target), AL_HEAL, 10, true);
                if sc::status_isimmune(c, target) != 0 {
                    i = 0;
                }
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                clif_skill_nodamage(c, Some(src), &t, id, i, true);
                if sc::has_sce(c, target.id, SC_AKAITSUKI) && i != 0 {
                    i = (!i).wrapping_add(1);
                }
                // status_heal(target, i, 0, 0): hap 0, flag 0
                crate::status::hp::status_heal(c, target.id, i as i64, 0, 0, 0);
            }
        } else {
            let f = *flag | BCT_PARTY | 1;
            foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, BL_MOB, Wall::None, |c, bl| {
                area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id)
            });
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
        }
    }
}
