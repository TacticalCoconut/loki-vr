//! skills/acolyte/absorbspiritsphere.cpp: class SkillAbsorbSpiritSphere : public SkillImpl (MO_ABSORBSPIRITS).

use crate::battle::target::battle_check_target;
use crate::battle::{BCT_ENEMY, BCT_SELF};
use crate::db::jobs::ids::{MAPID_FIRSTMASK, MAPID_GUNSLINGER};
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::state::{self, pc_delspiritball, pc_delspiritcharm, CHARM_TYPE_NONE};
use crate::skills::SkillImpl;
use crate::status::defs::MD_STATUSIMMUNE;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillAbsorbSpiritSphere;

impl SkillImpl for SkillAbsorbSpiritSphere {
    fn skill_id(&self) -> u16 {
        MO_ABSORBSPIRITS
    }

    /// SkillAbsorbSpiritSphere::castendNoDamageId: 7 SP per sphere / charm of a player (self or enemy; not a
    /// Gunslinger's coins), or a 20% chance of 2 SP per level of a monster (which then targets the caster).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let tstatus = crate::status::status_get_status_data(c, target);
        let mut i: i32 = 0;
        let dstsd_class = if target.kind == BL_PC { crate::status::data::sd_get(c, target.id).map(|s| s.class_) } else { None };
        // Only works on self and enemies; split the if for readability, and included gunslingers in the check so that
        // their coins cannot be removed [Reddozen]
        let pc_ok = dstsd_class.is_some_and(|cl| {
            (battle_check_target(c, src, target, BCT_SELF) > 0 || battle_check_target(c, src, target, BCT_ENEMY) > 0) && (cl & MAPID_FIRSTMASK) != MAPID_GUNSLINGER
        });
        if pc_ok {
            let st = state::get(c, target.id);
            if st.spiritball > 0 {
                i = st.spiritball as i32 * 7;
                pc_delspiritball(c, target.id, st.spiritball as i32, 0);
            }
            let st = state::get(c, target.id);
            if st.spiritcharm_type != CHARM_TYPE_NONE && st.spiritcharm > 0 {
                i += st.spiritcharm as i32 * 7;
                pc_delspiritcharm(c, target.id, st.spiritcharm as i32, st.spiritcharm_type);
            }
        } else if target.kind == BL_MOB && !crate::battle::mobif::has_mode(tstatus.mode, MD_STATUSIMMUNE) && c.rnd() % 100 < 20 {
            // check if target is a monster and not status immune, for the 20% chance to absorb 2 SP per monster's
            // level [Reddozen]
            i = 2 * crate::mob::get(c, target.id).map(|md| md.level).unwrap_or(0);
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::mob::ai::mob_target(c, target.id, &s, 0);
        } else {
            if src.kind == BL_PC {
                skill_fail(c, src, id);
            }
            return;
        }
        if i != 0 {
            crate::status::hp::status_heal(c, src.id, 0, i as i64, 0, 3);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, i != 0);
    }
}
