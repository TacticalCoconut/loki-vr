//! skills/mage/dispell.cpp: class SkillDispell : public SkillImpl (SA_DISPELL).

use crate::db::jobs::{MAPID_SECONDMASK, MAPID_SOUL_LINKER};
use crate::db::misc::status::status_db;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_MOB, BL_PC};
use crate::skill::area::{foreach_in_range, skill_area_sub, Wall};
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::defs::BSF_REM_ON_DISPELL;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// skill.conf `dispel_song: no` (battle_config.dispel_song): the songs can be dispelled outside their area.
pub(crate) fn dispel_song() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DISPEL_SONG) != 0
}

pub struct SkillDispell;

impl SkillImpl for SkillDispell {
    fn skill_id(&self) -> u16 {
        SA_DISPELL
    }

    /// SkillDispell::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let is_sd = src.kind == BL_PC;
        let is_dstsd = target.kind == BL_PC;
        let is_dstmd = target.kind == BL_MOB;
        // status_get_sc(target): nullptr for the units without a status_change
        let has_tsc = crate::battle::mobif::has_sc(target.kind);
        // int32 i: assigned only when flag&1 is 0 (the splash branch reads it)
        let i: i32;

        if *flag & 1 != 0 || {
            i = skill_get_splash(id, skill_lv);
            i < 1
        } {
            if is_sd && is_dstsd && !crate::npc::mapflag_vs(c, src.map) {
                let sd_duel = crate::duel::duel_group(c, src.id);
                let sd_party = crate::party::party_id_of(c, src.id);
                if (sd_duel == 0 || sd_duel != crate::duel::duel_group(c, target.id)) && (sd_party == 0 || sd_party != crate::party::party_id_of(c, target.id)) {
                    return; // Outside PvP it should only affect party members and no skill fail message
                }
            }
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            // (dstsd->class_ & MAPID_SECONDMASK) == MAPID_SOUL_LINKER
            let soul_linker = is_dstsd && crate::status::data::sd_get(c, target.id).map(|d| d.class_ & MAPID_SECONDMASK == MAPID_SOUL_LINKER).unwrap_or(false);
            if soul_linker
                || (has_tsc && sc::get_sce(c, target.id, SC_SPIRIT).map(|e| e.val2 == SL_ROGUE as i32).unwrap_or(false)) //Rogue's spirit defends against dispel.
                || (c.rnd() % 100) as i32 >= 50 + 10 * skill_lv as i32
            {
                if is_sd {
                    skill_fail(c, src, id);
                }
                return;
            }
            if sc::status_isimmune(c, target) != 0 {
                return;
            }

            //Remove bonus_script by Dispell
            if is_dstsd {
                crate::status::bonus::pc_bonus_script_clear(c, target.id, BSF_REM_ON_DISPELL);
            } else if is_dstmd {
                // Monsters will unlock their target instead
                crate::mob::ai::mob_unlocktarget(c, target.id, tick);
            }

            if !has_tsc || sc::load(c, target.id).empty() {
                return;
            }

            //Statuses that can't be Dispelled
            for it in status_db().iter() {
                let status = it.type_;
                let Some(mut sce) = sc::get_sce(c, target.id, status) else { continue };
                if it.has_flag(SCF_NODISPELL) {
                    continue;
                }
                match status {
                    // bugreport:4888 these songs may only be dispelled if you're not in their song area anymore
                    SC_WHISTLE | SC_ASSNCROS | SC_POEMBRAGI | SC_APPLEIDUN | SC_HUMMING | SC_DONTFORGETME | SC_FORTUNE | SC_SERVICE4U => {
                        if !dispel_song() || sce.val4 == 0 {
                            continue; //If in song area don't end it, even if config enatargeted
                        }
                    }
                    SC_ASSUMPTIO => {
                        if target.kind == BL_MOB {
                            continue;
                        }
                    }
                    _ => {}
                }
                if status == SC_BERSERK || status == SC_SATURDAYNIGHTFEVER {
                    //Mark a dispelled berserk to avoid setting hp to 100 by setting hp penalty to 0.
                    sce.val2 = 0;
                    sc::put_sce(c, &sce);
                }
                sc::status_change_end(c, target.id, status, 0);
            }
            return;
        }

        //Affect all targets on splash area.
        // map_foreachinallrange(skill_area_sub, target, i, BL_CHAR, src, SA_DISPELL, skill_lv, tick, flag|1,
        // skill_castend_damage_id): rAthena passes skill_castend_damage_id, which calls this class' castendDamageId
        // (SkillImpl's no-op): kept as is.
        let f = *flag | 1;
        foreach_in_range(c, target, i as i16, BL_CHAR, Wall::None, |c, bl| skill_area_sub(c, bl, src, id, skill_lv, tick, f, crate::skill::castend::skill_castend_damage_id));
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn dispel_song_matches_the_reference_config() {
        assert_eq!(super::dispel_song() as i32, crate::script::battle_flags::battle_get_value("dispel_song"));
    }
}
