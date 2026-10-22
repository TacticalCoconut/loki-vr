//! skills/acolyte/assimilatepower.cpp: class SkillAssimilatePower : public SkillImpl (SR_ASSIMILATEPOWER).

use crate::battle::{BCT_ENEMY, BCT_SELF, DMG_SINGLE};
use crate::db::jobs::ids::{MAPID_FIRSTMASK, MAPID_GUNSLINGER};
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::area::{self, foreach_in_range, splash_target, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skill::state::{self, pc_delspiritball, pc_delspiritcharm, CHARM_TYPE_NONE};
use crate::skill::SD_SPLASH;
use crate::skills::novice::status_get_amotion;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillAssimilatePower;

impl SkillImpl for SkillAssimilatePower {
    fn skill_id(&self) -> u16 {
        SR_ASSIMILATEPOWER
    }

    /// SkillAssimilatePower::castendNoDamageId: the caster's cast reaches everyone around the target (enemies and
    /// itself); each player hit (the caster itself, or anyone on a PvP / GvG / battleground map; not a Gunslinger's
    /// coins) loses its spheres and charms, and the caster recovers 1% SP per sphere.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            let mut amount = 0;
            let dst_class = if target.kind == BL_PC { crate::status::data::sd_get(c, target.id).map(|s| s.class_) } else { None };
            // dstsd && (sd == dstsd || map_flag_vs(src->m)) && not a Gunslinger
            let same = src.kind == BL_PC && src.id == target.id;
            if dst_class.is_some_and(|cl| (same || crate::npc::mapflag_vs(c, src.map)) && (cl & MAPID_FIRSTMASK) != MAPID_GUNSLINGER) {
                let st = state::get(c, target.id);
                if st.spiritball > 0 {
                    amount = st.spiritball as i32;
                    pc_delspiritball(c, target.id, st.spiritball as i32, 0);
                }
                let st = state::get(c, target.id);
                if st.spiritcharm_type != CHARM_TYPE_NONE && st.spiritcharm > 0 {
                    amount += st.spiritcharm as i32;
                    pc_delspiritcharm(c, target.id, st.spiritcharm as i32, st.spiritcharm_type);
                }
            }
            if amount != 0 {
                crate::status::hp::status_percent_heal(c, src.id, 0, amount, 0);
            }
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, amount != 0);
        } else {
            clif_skill_damage(c, src, target, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
            let f = *flag | BCT_ENEMY | BCT_SELF | SD_SPLASH | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, splash_target(src), Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
        }
    }
}
