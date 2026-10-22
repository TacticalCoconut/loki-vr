//! skills/acolyte/clearance.cpp: class SkillClearance : public SkillImpl (AB_CLEARANCE).

use crate::battle::target::battle_check_target;
use crate::battle::BCT_PARTY;
use crate::db::misc::status::status_db;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_MOB, BL_PC};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::defs::BSF_REM_ON_CLEARANCE;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

use super::conf::dispel_song;

pub struct SkillClearance;

impl SkillImpl for SkillClearance {
    fn skill_id(&self) -> u16 {
        AB_CLEARANCE
    }

    /// SkillClearance::castendNoDamageId: as of the behavior in official server Clearance is just a super version of
    /// Dispell skill. [Jobbie] Only monsters and party members; a (60 + 8 x level)% chance; ends the target's
    /// bonus_scripts marked for Clearance and every status without SCF_NOCLEARANCE (songs only with dispel_song and
    /// outside their area, Assumptio not on monsters), in status_db's order. With a splash area (none in the skill
    /// database) the cast would go to everyone around through skill_castend_damage_id, as rAthena has it.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let i = skill_get_splash(id, skill_lv);
        if *flag & 1 != 0 || i < 1 {
            if target.kind != BL_MOB && battle_check_target(c, src, target, BCT_PARTY) <= 0 {
                // Only affect mob or party.
                return;
            }
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            if (c.rnd() % 100) as i32 >= 60 + 8 * skill_lv as i32 {
                if src.kind == BL_PC {
                    skill_fail(c, src, id);
                }
                return;
            }
            if sc::status_isimmune(c, target) != 0 {
                return;
            }
            // Remove bonus_script by Clearance
            if target.kind == BL_PC {
                crate::status::bonus::pc_bonus_script_clear(c, target.id, BSF_REM_ON_CLEARANCE);
            }
            // status_get_sc(target) == nullptr || tsc->empty()
            if !crate::battle::mobif::has_sc(target.kind) || sc::load(c, target.id).empty() {
                return;
            }
            // Statuses change that can't be removed by Cleareance
            for s in status_db().iter() {
                let status = s.type_;
                let Some(mut sce) = sc::get_sce(c, target.id, status) else { continue };
                if s.has_flag(SCF_NOCLEARANCE) {
                    continue;
                }
                match status {
                    SC_WHISTLE | SC_ASSNCROS | SC_POEMBRAGI | SC_APPLEIDUN | SC_HUMMING | SC_DONTFORGETME | SC_FORTUNE | SC_SERVICE4U => {
                        if !dispel_song() || sce.val4 == 0 {
                            continue; // If in song area don't end it, even if config enabled
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
                    // Mark a dispelled berserk to avoid setting hp to 100 by setting hp penalty to 0.
                    sce.val2 = 0;
                    sc::put_sce(c, &sce);
                }
                sc::status_change_end(c, target.id, status, 0);
            }
            return;
        }
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        let f = *flag | 1;
        foreach_in_range(c, &t, i as i16, BL_CHAR, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
    }
}
