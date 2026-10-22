//! skills/acolyte/kitranslation.cpp: class SkillKiTranslation : public SkillImpl (MO_KITRANSLATION).

use crate::db::jobs::ids::{MAPID_FIRSTMASK, MAPID_GUNSLINGER, MAPID_REBELLION, MAPID_SECONDMASK};
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::skill_fail;
use crate::skill::state::{self, pc_addspiritball, pc_delspiritball};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillKiTranslation;

impl SkillImpl for SkillKiTranslation {
    fn skill_id(&self) -> u16 {
        MO_KITRANSLATION
    }

    /// SkillKiTranslation::castendNoDamageId: the spheres the requirement names move from the caster to a player
    /// (not a Gunslinger / Rebellion) with fewer than 5; else the cast fails and consumes nothing.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let dst_class = if target.kind == BL_PC { crate::status::data::sd_get(c, target.id).map(|s| s.class_) } else { None };
        let ok = dst_class.is_some_and(|cl| (cl & MAPID_FIRSTMASK) != MAPID_GUNSLINGER && (cl & MAPID_SECONDMASK) != MAPID_REBELLION) && state::get(c, target.id).spiritball < 5;
        if ok {
            // Require will define how many spiritballs will be transferred (skill_get_requirement(nullptr, …) is
            // empty and pc_delspiritball(nullptr, …) does nothing: a non-player caster moves none)
            if src.kind != BL_PC {
                return;
            }
            let require = crate::skill::condition::skill_get_requirement(c, src, id, skill_lv);
            pc_delspiritball(c, src.id, require.spiritball, 0);
            for _ in 0..require.spiritball {
                pc_addspiritball(c, target.id, skill_get_time(id, skill_lv), 5);
            }
        } else {
            if src.kind == BL_PC {
                skill_fail(c, src, id);
            }
            *flag |= SKILL_NOCONSUME_REQ;
        }
    }
}
