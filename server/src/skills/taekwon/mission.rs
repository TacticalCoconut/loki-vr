//! skills/taekwon/mission.cpp: class SkillMission : public SkillImpl (TK_MISSION).

use crate::db::mobs::{MOBG_TAEKWON_MISSION, RMF_NONE};
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::pc_taekwon::{clif_mission_info, mission_count, mission_mobid, set_mission};
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMission;

impl SkillImpl for SkillMission {
    fn skill_id(&self) -> u16 {
        TK_MISSION
    }

    /// SkillMission::castendNoDamageId: a new Taekwon mission target (a player caster only).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        let mobid = mission_mobid(c, src.id);
        if mobid != 0 && (mission_count(c, src.id) != 0 || c.rnd() % 100 != 0) {
            // Cannot change target when already have one
            clif_mission_info(c, src, mobid as i32, mission_count(c, src.id));
            skill_fail(c, src, id);
            return;
        }
        let mob_id = crate::mob::spawn::get_random_id(c, MOBG_TAEKWON_MISSION, RMF_NONE, 0);
        if mob_id == 0 {
            skill_fail(c, src, id);
            return;
        }
        // sd->mission_mobid = id; sd->mission_count = 0; pc_setglobalreg(TKMISSIONID_VAR, id)
        set_mission(c, src.id, mob_id);
        clif_mission_info(c, src, mob_id, 0);
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
    }
}
