//! skills/npc/suicidebombing.cpp: class SkillSuicideBombing : public SkillImpl (NPC_SELFDESTRUCTION), with map.cpp
//! map_delblock, which it calls around its splash (the module's block list is the unit's `on_map` flag plus map.rs's
//! block order; map_addblock is unit.rs `addblock`).

use crate::battle::{BCT_ALL, BCT_ENEMY, BCT_SLAVE};
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_MOB, BL_SKILL};
use crate::mob::AI_SPHERE;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_HIDING;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillSuicideBombing;

impl SkillImpl for SkillSuicideBombing {
    fn skill_id(&self) -> u16 {
        NPC_SELFDESTRUCTION
    }

    /// SkillSuicideBombing::castendDamageId: a target of the explosion (not hidden, not the caster itself) is hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        // status_change* tsc = status_get_sc(target); if (tsc && tsc->getSCE(SC_HIDING))
        if sce(c, target, SC_HIDING).is_some() {
            return;
        }
        if src.id != target.id {
            let id = self.skill_id();
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        }
    }

    /// SkillSuicideBombing::castendNoDamageId: the caster leaves the block list (so that the explosions it causes
    /// cannot hit it back), hits everything around the target, comes back and dies of it (status_zap: no damage
    /// display, drops and experience are given).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // mob_data* md = BL_CAST(BL_MOB, src); status_data* sstatus = status_get_status_data(*src)
        let md = if src.kind == BL_MOB { crate::mob::get(c, src.id) } else { None };
        //Self Destruction hits everyone in range (allies+enemies)
        //Except for Summoned Marine spheres on non-versus maps, where it's just enemies and your own slaves.
        let i = if md.as_ref().map(|m| m.special_ai == AI_SPHERE).unwrap_or(true) && !crate::npc::mapflag_vs(c, src.map) {
            // Enable Marine Spheres to damage own Homunculus and summons outside PVP
            if crate::battle::conf::alchemist_summon_setting() & 8 != 0 {
                BCT_ENEMY | BCT_SLAVE
            } else {
                BCT_ENEMY
            }
        } else {
            BCT_ALL
        };
        clif_skill_nodamage(c, Some(src), src, id, skill_lv as i32, true);
        map_delblock(c, src.id); //Required to prevent chain-self-destructions hitting back.
        let f = *flag | i;
        foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::Always, |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
        });
        // if (map_addblock(src)): map_addblock answers 1 when it fails
        let added = match unit::get(c, src.id) {
            Some(mut s) => unit::addblock(c, &mut s),
            None => false,
        };
        if !added {
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        // Won't display the damage, but drop items and give exp
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let hp = crate::status::status_get_status_data(c, &s).hp;
        crate::status::hp::status_zap(c, src.id, hp as i64, 0, 0);
    }
}

/// map.cpp map_delblock: takes the unit out of its block's list (`bl->prev = nullptr`: area searches no longer find
/// it; map_addblock puts it back at the head of its block's list). A unit already out of the block list is left alone
/// (rAthena returns 0 then as well). CELL_NOSTACK is not defined (config/core.hpp), so no cell count changes.
pub(crate) fn map_delblock(c: &Ctx, id: u32) -> i32 {
    let Some(mut u) = unit::get(c, id) else { return 0 };
    if !u.on_map {
        return 0;
    }
    u.on_map = false;
    unit::save(c, &u);
    0
}
